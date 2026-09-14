# -*- coding: utf-8 -*-
"""build_armor.py — 把外部角色模型上的甲，重定向骨架后绑到骑砍 human_skeleton 上。

原理（rest 重定向 / rest retarget）：
    每个顶点按它在 SW2 骨 i 的局部坐标，搬到对应骑砍骨 k(i) 的局部坐标：
        v' = Σᵢ wᵢ · ( M_bl[k(i)] · S_k(i) · M_sw2[i]⁻¹ · v )
    · M_sw2[i] / M_bl[k] = 两根骨各自的**静止矩阵**（armature 空间）
    · S_k = 骨内缩放：径向 r（厘米→米），沿骨轴再乘 λ = len_bl / (r·len_sw2)
    · wᵢ = 顶点原有的蒙皮权重（美术做的，原样保留，只换骨名）

  这式子**顺带解决两件事**：
    · 单位换算（SW2 是厘米，骑砍是米）—— 由 r 承担
    · 姿态差（SW2 是 T-pose，骑砍是 A-pose）—— 由 M_bl 自带的旋转承担，不用手转
    · 比例差（SW2 腿偏长）—— 由 λ 逐骨承担

用法（Blender 无头模式）:
  blender -b --python build_armor.py -- \\
      --src  <SW2 角色.fbx> \\
      --skel <human_skeleton.fbx> \\
      --out  <输出目录> \\
      --name taikou_yukimura_do_a \\
      [--parts body]      # body | body_kimono | head | arms | legs
      [--r 0.01]          # 径向缩放（厘米→米）
      [--cut-z 0.0]       # 只保留高于该高度（米，骑砍空间）的面；0 = 不裁
      [--lod 0.834,0.563,0.249,0.140,0.072]
      [--no-lod]
"""
import bpy
import sys
import os
import math
from mathutils import Vector, Matrix

# ---------------------------------------------------------------- 参数

def args_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def get(args, key, default=None):
    return args[args.index(key) + 1] if key in args else default


A = args_after_ddash()
SRC = get(A, "--src")
SKEL = get(A, "--skel")
OUTDIR = get(A, "--out")
NAME = get(A, "--name", "armor")
PARTS = get(A, "--parts", "body")
R = float(get(A, "--r", "0.01"))
# 手臂链的径向缩放（默认跟 R 一样）。原版身体的胳膊比源件粗，躯干调够之后手臂仍会顶穿，
# 这里单独放一点。实测 0.0120 → 0.0145 才把上臂那圈身体盖住。
R_ARMS = float(get(A, "--r-arms", str(R)))
CUT_Z = float(get(A, "--cut-z", "0.0"))
LOD_RATIOS = [float(x) for x in get(A, "--lod", "0.834,0.563,0.249,0.140,0.072").split(",")]
DO_LOD = "--no-lod" not in A

if not (SRC and SKEL and OUTDIR):
    print("!! 缺少 --src / --skel / --out")
    sys.exit(2)
os.makedirs(OUTDIR, exist_ok=True)

HEAD_BONE_Z = 1.569          # 官方骨架校准验收值（换头工程 §11.3）

# 🔴 SW2 与骑砍的"正面"相反，必须镜像：
#    SW2  : 正面 = -Y（脚趾 y=-13.8、面部骨 y=-10.35、前垂草摺 y=-17.4 全在负侧）
#    骑砍  : 正面 = +Y（脚趾 y=+0.04 在脚踝 y=-0.076 的正侧；身体渲染可见胸肌面朝 +Y）
#    ⇒ 用 **Y 轴镜像** (x,y,z)->(x,-y,z)，不能用 180° 绕 Z 旋转（那会连左右一起翻）。
#    镜像是反射（行列式 -1），做完必须**反转面绕序**否则法线朝里。
MIRROR_Y = Matrix.Diagonal((1.0, -1.0, 1.0, 1.0))
MIRROR_Y3 = MIRROR_Y.to_3x3()

# ---------------------------------------------------------------- 部位 -> 子网格号
# 号来自 SW2 角色 FBX 的 model_0_submesh_N（判定依据见 plans/rules 与工程文档）
PART_SETS = {
    "body":             [2, 7, 8, 9],            # 胴(含袴/佩楯/脛当/靴) + 草摺前/后/环
    "body_kimono":      [1, 2, 7, 8, 9],         # 再加内衬着物
    "arms":             [3, 4],                  # 袖(sode) + 籠手(kote)，両腕
    "body_kimono_arms": [1, 2, 3, 4, 7, 8, 9],   # 主体 + 内衬 + 両腕（配 --no-hands）
    "head":             [10, 0],                 # 兜 + 系带
}

# ---------------------------------------------------------------- SW2 骨 -> 骑砍骨
# 依据：两侧骨骼 head 坐标逐根对照（工程文档「骨架对照」表）。左右按 x 符号对齐。
SW2_MAP = {
    "bone_0":  "bip01_pelvis_0",
    "bone_1":  "bip01_spine_9",       # SW2 z=117.47 -> 骑砍 spine_9 z=1.0064
    "bone_8":  "bip01_spine_9",
    "bone_9":  "bip01_spine1_10",     # SW2 z=132.71 -> 骑砍 spine1_10 z=1.1630
    "bone_10": "bip01_neck_12",
    "bone_11": "bip01_head_13",
    # 左腿
    "bone_2":  "bip01_l_thigh_1",
    "bone_4":  "bip01_l_calf_2",
    "bone_6":  "bip01_l_foot_3",
    "bone_24": "bip01_l_toe0_4",
    # 右腿
    "bone_3":  "bip01_r_thigh_5",
    "bone_5":  "bip01_r_calf_6",
    "bone_7":  "bip01_r_foot_7",
    "bone_25": "bip01_r_toe0_8",
    # 左臂
    "bone_12": "bip01_l_clavicle_14",
    "bone_14": "bip01_l_upperarm_twist_15",
    "bone_16": "bip01_l_foretwist_17",
    "bone_18": "bip01_l_hand_19",
    # 右臂
    "bone_13": "bip01_r_clavicle_21",
    "bone_15": "bip01_r_upperarm_twist_22",
    "bone_17": "bip01_r_foretwist_24",
    "bone_19": "bip01_r_hand_26",
}
# 手指（骑砍每只手只有一根 finger0）
SW2_MAP.update({b: "bip01_l_finger0_20" for b in
                ("bone_32", "bone_33", "bone_34", "bone_35", "bone_36",
                 "bone_37", "bone_42", "bone_43", "bone_44", "bone_45")})
SW2_MAP.update({b: "bip01_r_finger0_27" for b in
                ("bone_26", "bone_27", "bone_28", "bone_29", "bone_30",
                 "bone_31", "bone_38", "bone_39", "bone_40", "bone_41")})
# 脸部小骨 -> 头
SW2_MAP.update({b: "bip01_head_13" for b in
                ("bone_46", "bone_59", "bone_60", "bone_61", "bone_62",
                 "bone_68", "bone_69")})


def parse_submesh(name):
    """model_0_submesh_12_noesis_meshnode_0012 -> 12"""
    i = name.find("submesh_")
    if i < 0:
        return None
    j = i + len("submesh_")
    k = j
    while k < len(name) and name[k].isdigit():
        k += 1
    return int(name[j:k]) if k > j else None


# ---------------------------------------------------------------- 导骨架（两遍定标）
def apply_all(obj):
    """把对象自身的变换烘进数据（骨架没有 shape key，可以烘）。"""
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def import_skeleton(path, scale=1.0):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, global_scale=scale)
    arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
    if arm is None:
        return None
    # 🔴 Blender 会把 global_scale 折成"对象缩放 × 单位换算"，必须烘平，
    #    否则导出的 FBX 骨架节点带非单位缩放（换头工程 §13.1 明令网格节点零变换）。
    apply_all(arm)
    return arm


print("== 1/6 导入官方骨架（第一遍反推缩放）==")
tmp = import_skeleton(SKEL)
head_b = next((b for b in tmp.data.bones if "head_13" in b.name), None)
if head_b is None:
    print("!! 官方骨架里找不到 head_13"); sys.exit(3)
z_raw = (tmp.matrix_world @ head_b.head_local).z
k_skel = HEAD_BONE_Z / z_raw
print("   head_13 z=%.5f -> global_scale=%.5f" % (z_raw, k_skel))

print("== 2/6 正式导入官方骨架 ==")
BL = import_skeleton(SKEL, k_skel)
hz = (BL.matrix_world @ BL.data.bones["bip01_head_13"].head_local).z
if abs(hz - HEAD_BONE_Z) > 0.01 or any(abs(s - 1.0) > 1e-4 for s in BL.scale):
    print("!! 骨架校准失败 head z=%.4f scale=%s" % (hz, tuple(BL.scale))); sys.exit(3)
print("   校准 OK  head_13 z=%.5f  scale=%s" % (hz, tuple(round(s, 6) for s in BL.scale)))
BL_BONES = {b.name for b in BL.data.bones}
bl_local = {b.name: b.matrix_local.copy() for b in BL.data.bones}
bl_len = {b.name: b.length for b in BL.data.bones}


# ---------------------------------------------------------------- 导 SW2（叠加，保留 BL）
def import_sw2(path):
    before = set(o.name for o in bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o.name not in before]


print("== 3/6 导入 SW2 角色 ==")
new_objs = import_sw2(SRC)
SW = next((o for o in new_objs if o.type == 'ARMATURE'), None)
sw_meshes = [o for o in new_objs if o.type == 'MESH']
apply_all(SW)          # 烘平 SW2 骨架（清掉 (0,0,-1e-5) 残留平移）
print("   网格 %d 个，骨架 %s（%d 骨）" % (len(sw_meshes), SW.name, len(SW.data.bones)))
sw_local = {b.name: b.matrix_local.copy() for b in SW.data.bones}
sw_len = {b.name: b.length for b in SW.data.bones}


# ---------------------------------------------------------------- 骨映射（含父链兜底）
def build_map():
    m = {}
    unmapped = []
    for b in SW.data.bones:
        n = b.name
        if n in SW2_MAP and SW2_MAP[n] in BL_BONES:
            m[n] = SW2_MAP[n]
            continue
        # 父链兜底：往上找第一根映射得到的骨
        p = b.parent
        hit = None
        while p is not None:
            if p.name in m:
                hit = m[p.name]; break
            if p.name in SW2_MAP and SW2_MAP[p.name] in BL_BONES:
                hit = SW2_MAP[p.name]; break
            p = p.parent
        if hit:
            m[n] = hit
        else:
            unmapped.append(n)
    return m, unmapped


BMAP, UNMAPPED = build_map()
print("   骨映射 %d 根（父链兜底 %d 根）" % (len(BMAP), len(BMAP) - len(SW2_MAP)))
if UNMAPPED:
    print("   !! 未映射（会被丢弃）: %s" % UNMAPPED[:12])
if "--debug" in A:
    print("   --- 直接映射表（SW2骨 -> 骑砍骨 | 骨长cm -> m | λ | 锚点差）---")
    for k, v in sorted(SW2_MAP.items()):
        if k not in sw_local or v not in bl_local:
            print("   %-22s -> %-30s  (缺骨)" % (k, v)); continue
        sl, bl = sw_len[k], bl_len[v]
        lam = 1.0 if (sl < 1.0 or bl < 0.005) else max(0.5, min(2.0, bl / (R * sl)))
        # 锚点差：SW2 骨 head 的 z（cm）按 R 缩放 与 骑砍骨 head 的 z（m）之差
        d_anchor = (sw_local[k].translation.z * R) - bl_local[v].translation.z
        print("   %-22s -> %-30s  %7.2f -> %6.4f  λ=%.3f  锚点Δz=%+.3f m" % (
            k, v, sl, bl, lam, d_anchor))


# ---------------------------------------------------------------- 肢体方向
# 🔴 教训：**不要试图给每根骨都算朝向**。实测两侧都有"朝向信息是垃圾"的骨：
#    · SW2 骨盆骨 bone_1 的最长子骨是腿根 -> head→child 得到"朝下"，与朝上的 spine_9 差 147.7°
#    · SW2 头部骨 bone_11 的 tail 不在子关节上 -> head→tail 差 171.5°
#    · SW2 有一批长度 0.01 的"空节点"（bone_67/70..79/80..101），朝向完全是噪声
#    · 骑砍 bone.matrix_local 的骨轴不沿肢体（spine_9 头 z=1.0064、尾 y=+0.157，指向正前方）
#    策略：**只给真正需要转的骨加旋转** —— 手臂链（SW2 是 T-pose，骑砍是 A-pose，
#    差约 33°）。躯干/腿两边都是竖直的，不需要转；加了反而产生剪切。
ARM_ROT = {
    "bone_12", "bone_13",            # 锁骨
    "bone_14", "bone_15",            # 上臂
    "bone_16", "bone_17",            # 前臂
    "bone_18", "bone_19",            # 手
}
ARM_ROT |= {"bone_%d" % i for i in range(26, 46)}      # 手指（26..45）


def chain_dir(bone):
    """head -> 最长子骨的 head。仅对**关节链上**的骨可靠（手臂骨实测可靠）。"""
    kids = [c for c in bone.children if 'nuno' not in c.name.lower()]
    if not kids:
        return None
    h = bone.head_local
    c = max(kids, key=lambda b: (b.head_local - h).length)
    d = c.head_local - h
    return d.normalized() if d.length > 1e-6 else None


# ---------------------------------------------------------------- 选件
def is_junk(o):
    """武器件与布料驱动件要排掉。
    🔴 别只按子网格号筛：`submesh_1`（着物）与 `submesh_1.001`（矛柄）解析出同一个号。
    判据用材质与 UV：武器材质是 mat_w_*，布料驱动件没有 UV。"""
    if not o.data.uv_layers:
        return True
    for m in o.data.materials:
        if m and m.name.lower().startswith('mat_w_'):
            return True
    return False


want = PART_SETS.get(PARTS)
if want is None:
    print("!! 未知 --parts %s" % PARTS); sys.exit(2)
picked = [o for o in sw_meshes
          if parse_submesh(o.name) in want and not is_junk(o)]
if not picked:
    print("!! 没选到任何网格（--parts %s）" % PARTS); sys.exit(3)
print("== 4/6 选件 ==")
for o in picked:
    print("   [%s] %s  v=%d f=%d" % (parse_submesh(o.name), o.name,
                                    len(o.data.vertices), len(o.data.polygons)))

# 转成独立网格（断开与原骨架的关联，自己算坐标）
bpy.ops.object.select_all(action='DESELECT')
for o in picked:
    o.select_set(True)
bpy.context.view_layer.objects.active = picked[0]
bpy.ops.object.duplicate()
dups = [o for o in bpy.context.selected_objects if o.type == 'MESH']
for o in dups:
    o.modifiers.clear()
    o.parent = None
    o.matrix_world = Matrix.Identity(4)   # 顶点已在 armature 空间（≈世界），清掉残留变换

# 着物瘦身（可选）：submesh_1 的"着物"把**头和手的皮**也包在里面（头 68 顶点 + 右手手指约 200 顶点），
# 直接带上会出现"头顶飘块 + 袖子变形"。按**主骨**删（不用 z 高度——袖子与躯干在 z 上重叠）。
# 🔴 这一条**只能作用于着物那一件**。踩过（2026-09-14）：早先把它作用在**合并后的整块甲**上，
#    而保留列表里没有锁骨骨 bone_12/bone_13 -> **胴的肩帯（正好由锁骨驱动的那 42 个顶点）被一起删掉**，
#    成品肩部一个大缺口。
import bmesh


def drop_verts(o, idxs):
    if not idxs:
        return
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in idxs], context='VERTS')
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()


def dominant_bones(o):
    """每顶点权重最大的那根骨（名字）"""
    vgn = [g.name for g in o.vertex_groups]
    out = []
    for v in o.data.vertices:
        out.append(vgn[max(v.groups, key=lambda g: g.weight).group] if v.groups else None)
    return out


# 手/手指骨（源件名）。籠手应当止于手腕——手留给骑砍身体的手，
# 否则甲的"手"会和身体的手打架（实机症状：拳头被甲整个包住）。
HAND_SW = {"bone_18", "bone_19"} | {"bone_%d" % i for i in range(26, 46)}
# 着物要留的部分：躯干 + 腿 + **手臂（袖子）**。
# 🔴 手臂段必须留：源件上臂中段本来就是**着物的布袖子**盖的，不是袖/籠手盖的。
#    早先只留躯干+腿 -> 上臂中段露身体（试过用拉伸硬撑，副作用是籠手盖住拳头）。
#    手/手指不在内（由 --no-hands 单独切），头也不在内。
KEEP_SW = {"bone_1", "bone_8", "bone_9", "bone_2", "bone_3", "bone_4",
           "bone_5", "bone_6", "bone_7", "bone_24", "bone_25",
           "bone_12", "bone_13", "bone_14", "bone_15", "bone_16", "bone_17"}

for o in dups:
    sm = parse_submesh(o.name)
    dom = dominant_bones(o)
    n0 = len(o.data.vertices)
    if "--no-hands" in A:
        drop_verts(o, [i for i, b in enumerate(dom) if b in HAND_SW])
        if len(o.data.vertices) != n0:
            print("   去手部：%s 删 %d 顶点" % (o.name, n0 - len(o.data.vertices)))
            dom = dominant_bones(o)
    if "--kimono-torso-only" in A and sm == 1:
        # 🔴 只滤着物。作用到整块甲上会误删胴的肩帯（由锁骨驱动）
        k = len(o.data.vertices)
        drop_verts(o, [i for i, b in enumerate(dom) if b not in KEEP_SW])
        print("   着物瘦身：%s 删 %d 顶点，余 %d" % (o.name, k - len(o.data.vertices), len(o.data.vertices)))

# ---------------------------------------------------------------- 重定向
print("== 5/6 骨架重定向（rest retarget）==")
by_name = {o.name: o for o in dups}
picked_names = [o.name for o in picked]
dup_list = [by_name[n] for n in picked_names if n in by_name]
if len(dup_list) != len(picked):
    # 名字可能被加了后缀，退回按数量取
    dup_list = dups

# 逐骨预计算重定向矩阵： T = Translate(bl头) · Rot(仅手臂) · Scale(R) · Translate(-sw头)
# 🔴 手臂的缩放必须拆成两个方向，不能用一个 Matrix.Scale 各向同性地放。
#    踩过（2026-09-14 实机）：把 --r-arms 从 0.0120 提到 0.0165，径向确实盖住了身体，
#    但**沿骨轴也被拉了 65%** —— 前臂 25.97cm × 0.0165 = 0.429m，而骑砍肘到手腕只有 0.267m，
#    于是籠手末端超出拳头 16cm（用户截图："手腕没有伸出袖子"）。
#    修法：沿骨轴按**解剖段长**对齐（源件肘→腕 与 骑砍肘→腕 的比值），径向才用 R_ARMS。
#    段长比值直接由两边关节坐标算，不写死。


def seg_ratio(sw_a, sw_b, bl_a, bl_b):
    """两对关节之间的距离比（米/厘米）= 沿骨轴的缩放"""
    ds = (SW.data.bones[sw_b].head_local - SW.data.bones[sw_a].head_local).length
    db = (BL.data.bones[bl_b].head_local - BL.data.bones[bl_a].head_local).length
    return db / ds if ds > 1e-9 else 0.01


# SW2 骨 -> 沿骨轴缩放（未列出的骨用 R）
ARM_ALONG = {
    "bone_14": seg_ratio("bone_14", "bone_16", "bip01_l_upperarm_twist_15", "bip01_l_foretwist_17"),
    "bone_15": seg_ratio("bone_15", "bone_17", "bip01_r_upperarm_twist_22", "bip01_r_foretwist_24"),
    "bone_16": seg_ratio("bone_16", "bone_18", "bip01_l_foretwist_17", "bip01_l_hand_19"),
    "bone_17": seg_ratio("bone_17", "bone_19", "bip01_r_foretwist_24", "bip01_r_hand_26"),
}


def frame_from_dir(d):
    """造一个 Y 轴 = d 的正交基（列向量）"""
    y = d.normalized()
    up = Vector((0.0, 0.0, 1.0)) if abs(y.z) < 0.9 else Vector((1.0, 0.0, 0.0))
    x = y.cross(up).normalized()
    z = x.cross(y).normalized()
    return Matrix(((x.x, y.x, z.x, 0.0),
                   (x.y, y.y, z.y, 0.0),
                   (x.z, y.z, z.z, 0.0),
                   (0.0, 0.0, 0.0, 1.0)))


T_BONE = {}
T_ANGLE = {}
for bn_sw, bn_bl in BMAP.items():
    if bn_sw not in sw_local or bn_bl not in bl_local:
        continue
    b_sw = SW.data.bones.get(bn_sw)
    b_bl = BL.data.bones.get(bn_bl)
    if b_sw is None or b_bl is None:
        continue
    rot = Matrix.Identity(4)
    ang = 0.0
    if bn_sw in ARM_ROT:
        d_sw, d_bl = chain_dir(b_sw), chain_dir(b_bl)
        if d_sw and d_bl:
            d_sw = (MIRROR_Y3 @ d_sw).normalized()      # 镜像后的朝向才是可比朝向
            ang = math.degrees(d_sw.angle(d_bl))
            rot = d_sw.rotation_difference(d_bl).to_matrix().to_4x4()
    T_ANGLE[bn_sw] = ang
    h_sw_f = MIRROR_Y @ b_sw.head_local
    if bn_sw in ARM_ALONG:
        # 手臂：径向 R_ARMS、沿骨轴按解剖段长 —— 见上面那段说明
        d_sw = chain_dir(b_sw)
        d_sw = (MIRROR_Y3 @ d_sw).normalized() if d_sw else Vector((0.0, 0.0, -1.0))
        F = frame_from_dir(d_sw)
        S = F @ Matrix.Diagonal((R_ARMS, ARM_ALONG[bn_sw], R_ARMS, 1.0)) @ F.inverted()
    else:
        S = Matrix.Scale(R, 4)
    T_BONE[bn_sw] = (Matrix.Translation(b_bl.head_local) @ rot @ S
                     @ Matrix.Translation(-h_sw_f))

bpy.ops.object.select_all(action='DESELECT')
for o in dup_list:
    o.select_set(True)
bpy.context.view_layer.objects.active = dup_list[0]
if len(dup_list) > 1:
    bpy.ops.object.join()
ARM = dup_list[0]
ARM.name = NAME
ARM.data.name = NAME          # 网格**数据**名也要改：FBX 的 Geometry 节点取数据名，编辑器按它命名资源

# 🔴 材质必须换成一张干净的、名字对得上的空材质。源件带过来的是 `mat_L00_yukimura`
#    外加指向 **不存在文件** 的贴图节点，实测后果（2026-09-14）：
#      ① 编辑器按名字找项目里的材质资产 -> 找不到 -> 每个 LOD 弹一次
#         "RGL CONTENT WARNING: Unable to find material for mesh <名字>"（6 个 LOD 弹 6 次）
#      ② 编辑器去加载那个不存在的贴图 -> **崩溃**，之后进编辑器一直报同样的错
#    修法：名字用**编辑器里那个材质资产的内部名**，并且**一个贴图节点都不挂**
#    （贴图由编辑器里的材质资产去接，FBX 只要交出网格就行）。
#
#    🔴 名称关系（tpaccli list 实测，2026-09-14）：
#       编辑器给**文件名**加后缀 `<名>_geo.tpac` / `<名>_mtl.tpac` / `<名>_d_tex.tpac`，
#       而**资产内部名就是你输入的那个名字**，不加后缀。
#       而且网格与材质**本来就同名**（靠类型 GUID 区分）——范本：蒂法头
#       文件名 `head_tifa_a_v11_geo.tpac` 与 `head_tifa_a_mtl.tpac`，内部名都是 `head_tifa_a`。
#       ⇒ 默认材质名 = 网格名 = NAME。**别自作聪明加 `_mtl`**。
MATNAME = get(A, "--mat-name", NAME)
_mat = bpy.data.materials.new(MATNAME)
_mat.use_nodes = True
# 把节点树清空重建：只留 输出 + Principled，**不加任何 TexImage 节点**
_nt = _mat.node_tree
_nt.nodes.clear()
_out = _nt.nodes.new('ShaderNodeOutputMaterial')
_bsdf = _nt.nodes.new('ShaderNodeBsdfPrincipled')
_nt.links.new(_bsdf.outputs['BSDF'], _out.inputs['Surface'])
ARM.data.materials.clear()
ARM.data.materials.append(_mat)
print("   材质 -> %s（无贴图引用）" % MATNAME)

if "--debug" in A:
    print("   --- 逐骨重定向：旋转角（只有手臂链非零）+ 平移偏移 ---")
    rows = []
    for bn_sw, T in T_BONE.items():
        b_sw = SW.data.bones.get(bn_sw); b_bl = BL.data.bones.get(BMAP[bn_sw])
        off = (b_bl.head_local - R * b_sw.head_local)
        rows.append((T_ANGLE.get(bn_sw, 0.0), bn_sw, BMAP[bn_sw], off))
    for ang, bn_sw, bn_bl, off in sorted(rows, key=lambda r: -r[0]):
        print("   %6.1f°  %-20s -> %-28s 平移Δ=(%+.3f,%+.3f,%+.3f)" % (
            ang, bn_sw, bn_bl, off.x, off.y, off.z))

# 统计
n_moved = 0
n_skipped = 0
vg_names = [g.name for g in ARM.vertex_groups]
new_weights = {}          # 新骨名 -> {顶点下标: 权重}

for v in ARM.data.vertices:
    w = {}
    for g in v.groups:
        if g.weight <= 1e-5:
            continue
        bn = vg_names[g.group]
        w[bn] = w.get(bn, 0.0) + g.weight
    if not w:
        n_skipped += 1
        continue
    v_src = MIRROR_Y @ v.co          # 先镜像到骑砍朝向，再重定向
    acc = Vector((0.0, 0.0, 0.0))
    tot = 0.0
    for bn_sw, wt in w.items():
        bn_bl = BMAP.get(bn_sw)
        T = T_BONE.get(bn_sw)
        if bn_bl is None or T is None:
            continue
        acc += (T @ v_src) * wt
        tot += wt
        new_weights.setdefault(bn_bl, {})
        new_weights[bn_bl][v.index] = new_weights[bn_bl].get(v.index, 0.0) + wt
    if tot > 1e-6:
        v.co = acc / tot
        n_moved += 1
    else:
        n_skipped += 1


# 镜像是反射 -> 面绕序反了，法线朝里，必须翻回来
import bmesh
_bm = bmesh.new()
_bm.from_mesh(ARM.data)
bmesh.ops.reverse_faces(_bm, faces=_bm.faces[:])
_bm.to_mesh(ARM.data)
_bm.free()
ARM.data.update()
print("   已反转面绕序（镜像补偿）")

# 换顶点组：清空，按新骨名重建
for g in list(ARM.vertex_groups):
    ARM.vertex_groups.remove(g)
for bn, d in new_weights.items():
    g = ARM.vertex_groups.new(name=bn)
    for idx, wt in d.items():
        g.add([idx], min(1.0, wt), 'ADD')

# 归一化（同顶点多骨权重和 -> 1）
tot_per_v = {}
for bn, d in new_weights.items():
    for idx, wt in d.items():
        tot_per_v[idx] = tot_per_v.get(idx, 0.0) + wt
for bn, d in new_weights.items():
    g = ARM.vertex_groups[bn]
    for idx, wt in d.items():
        s = tot_per_v.get(idx, 0.0)
        if s > 1e-9 and abs(s - 1.0) > 1e-4:
            g.add([idx], wt / s, 'REPLACE')

print("   顶点 %d：移动 %d，跳过 %d" % (len(ARM.data.vertices), n_moved, n_skipped))
print("   顶点组 %d 个" % len(ARM.vertex_groups))
badv = sum(1 for v in ARM.data.vertices if not v.groups)
print("   未绑定顶点 %d" % badv)

# （主骨过滤已移到合并**之前**、且只作用于着物 —— 见上面 dup 循环里那段）

# 挂到骑砍骨架
ARM.parent = BL
mod = ARM.modifiers.new("Armature", 'ARMATURE')
mod.object = BL

# 按 z 裁剪（可选）
if CUT_Z > 0.0:
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(ARM.data)
    kill = [f for f in bm.faces if (sum(v.co.z for v in f.verts) / len(f.verts)) < CUT_Z]
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    bm.to_mesh(ARM.data)
    bm.free()
    print("   按 z>=%.3f 裁剪后：v=%d f=%d" % (CUT_Z, len(ARM.data.vertices), len(ARM.data.polygons)))

# bbox 报告
mn = Vector((1e9,) * 3); mx = Vector((-1e9,) * 3)
for v in ARM.data.vertices:
    for i in range(3):
        mn[i] = min(mn[i], v.co[i]); mx[i] = max(mx[i], v.co[i])
print("   甲 bbox  x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f]" % (mn.x, mx.x, mn.y, mx.y, mn.z, mx.z))
print("   原版身体  x[-0.599,0.599] z[0.379,1.544]   （对照）")

# ---------------------------------------------------------------- LOD
print("== 6/6 LOD + 导出 ==")
lods = [ARM]
if DO_LOD:
    for i, ratio in enumerate(LOD_RATIOS, start=1):
        bpy.ops.object.select_all(action='DESELECT')
        ARM.select_set(True)
        bpy.context.view_layer.objects.active = ARM
        bpy.ops.object.duplicate()
        d = bpy.context.view_layer.objects.active
        d.name = "%s.lod%d" % (NAME, i)
        d.data.name = "%s.lod%d" % (NAME, i)
        m = d.modifiers.new("dec", 'DECIMATE')
        m.decimate_type = 'COLLAPSE'
        m.ratio = ratio
        bpy.ops.object.modifier_apply(modifier=m.name)
        lods.append(d)
        print("   lod%d  ratio=%.3f -> v=%d f=%d" % (i, ratio, len(d.data.vertices), len(d.data.polygons)))

# 清场：导出前只留 甲 + LOD + 骑砍骨架（否则会把 SW2 原始网格、布料驱动件、武器一起打进 FBX）
keep = set(lods) | {BL}
for o in list(bpy.data.objects):
    if o not in keep:
        bpy.data.objects.remove(o, do_unlink=True)
print("   清场后场景对象：%s" % sorted(o.name for o in bpy.data.objects))

blend_path = os.path.join(OUTDIR, NAME + ".blend")
bpy.ops.wm.save_as_mainfile(filepath=blend_path)
print("   存 .blend ->", blend_path)

# 导出 FBX（规格照换头工程 §13.1）
bpy.context.scene.unit_settings.scale_length = 1.0
fbx_path = os.path.join(OUTDIR, NAME + ".fbx")
kw = dict(
    filepath=fbx_path,
    use_selection=False,
    object_types={'ARMATURE', 'MESH'},
    global_scale=1.0,
    apply_unit_scale=False,
    apply_scale_options='FBX_SCALE_UNITS',
    bake_space_transform=False,
    use_mesh_modifiers=False,
    add_leaf_bones=False,
    primary_bone_axis='Y',
    secondary_bone_axis='X',
    axis_forward='Y',
    axis_up='Z',
    bake_anim=False,
    path_mode='COPY',
    embed_textures=False,
    use_custom_props=False,
)
try:
    bpy.ops.export_scene.fbx(**kw)
except TypeError as e:
    print("   fbx kwarg 问题:", e)
    kw.pop('apply_scale_options', None)
    bpy.ops.export_scene.fbx(**kw)
print("   导出 FBX ->", fbx_path)

# 导出后自检：FBX 里不许留贴图引用，材质名必须是我们指定的那个
import re as _re
_blob = open(fbx_path, 'rb').read()
_tex = sorted(set(m.decode('latin1') for m in
                  _re.findall(rb'[ -~]{4,120}\.(?:png|tga|dds|jpg|jpeg)', _blob)))
_mats = sorted(set(m.decode('latin1') for m in _re.findall(rb'mat_[A-Za-z0-9_]+', _blob)))
print("   自检 贴图引用:", _tex if _tex else "无 ✓")
print("   自检 材质名  :", _mats if _mats else "无（用 %s）✓" % MATNAME)
if _tex:
    print("   !! FBX 里仍有贴图引用，编辑器导入时可能去加载不存在的文件而崩溃")
print("DONE", NAME)
