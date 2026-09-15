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


# 🔴 颏带（2026-09-15）：头盔的颏带被美术画在【脸壳件】里（不是兜件）。做兜时用这两个参数
#    把脸壳件里"种子点所在的那几块碎片"并进来 —— 头那边 build_head.py --strap-seed 摘掉的是同一批。
STRAP_FROM = get(A, "--strap-from", "")
STRAP_SEEDS = [Vector([float(v) for v in t.split(",")])
               for t in (get(A, "--strap-seed", "") or "").split(";") if t.strip()]
# 下巴那一横条并进了脸壳主网格（不是独立碎片）→ 只能按【主导骨】认（见 build_head.drop_verts_by_bone）
STRAP_BONES = set(x.strip() for x in (get(A, "--strap-bone", "") or "").split(",") if x.strip())

idx_arg = get(A, "--parts-idx", "") or ""
# 🔴 `--parts-name`（2026-09-15 深夜加）：按【精确对象名】选件，用 `|` 分隔。
#    为什么需要：`parse_submesh` 只取数字，`submesh_0` 与 `submesh_0.001` **解析成同一个号** ——
#    上杉谦信的兜是 `submesh_0..._0000.001`（242 顶点），按号选会把同号的 25 顶点身体件一起选中，
#    再叠上 --prune-far 就把真兜当碎片剔干净了（实测输出只剩 18 顶点）。
#    凡"某块件的 .001 兄弟不是同一件东西"的角色，一律走这个参数。
name_arg = get(A, "--parts-name", "") or ""
if name_arg.strip():
    want_names = [x.strip() for x in name_arg.split("|") if x.strip()]
    # 🔴 精确点名的件**不过 is_junk**（2026-09-15 深夜）：挑件表的 helmet 列是**人工标注**的，
    #    优先级高于启发式判据。实测上杉谦信：兜件（碗+双角+耳庇+垂带，242 顶点）的材质名带
    #    `mat_w_`（普查因此判它 "weapon"，置信 0.99）→ 被 is_junk 当武器排掉 → 兜整个没做出来。
    #    点名 = 人已经确认过这是什么，不要再让机器否决。
    picked = [o for o in sw_meshes if o.name in want_names]
    miss = [n for n in want_names if not any(o.name == n for o in sw_meshes)]
    if miss:
        print("!! --parts-name 没匹配到的网格：%s" % ", ".join(miss))
    # 颏带来源（脸壳件）也要进来，稍后只留颏带碎片 —— 这里按号加即可
    # （脸壳件的 `.001` 兄弟是碎屑，加进来无害；按名加反而要为每个角色维护第二个名字）
    if STRAP_FROM.isdigit():
        picked = picked + [o for o in sw_meshes
                           if parse_submesh(o.name) == int(STRAP_FROM)
                           and not is_junk(o) and o not in picked]
elif idx_arg.strip():
    want = [int(x) for x in idx_arg.split(",") if x.strip().lstrip("-").isdigit()]
else:
    want = PART_SETS.get(PARTS)
    if want is None:
        print("!! 未知 --parts %s" % PARTS); sys.exit(2)
if not name_arg.strip() and STRAP_FROM.isdigit() and int(STRAP_FROM) not in want:
    want = list(want) + [int(STRAP_FROM)]          # 颏带来源（脸壳件）也要进来，稍后只留颏带碎片
if not name_arg.strip():
    picked = [o for o in sw_meshes
              if parse_submesh(o.name) in want and not is_junk(o)]
if not picked:
    print("!! 没选到任何网格（--parts %s / --parts-idx %s / --parts-name %s）"
          % (PARTS, idx_arg, name_arg)); sys.exit(3)
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
    mw = o.matrix_world.copy()      # 🔴 必须在 parent=None **之前**取 —— 解父会改写 matrix_world
    o.parent = None
    # 🔴 无蒙皮的件：**先把物体变换烘进顶点，再补头骨权重**（2026-09-15 深夜）。
    #    实测上杉谦信的兜（submesh_0..._0000.001，242 顶点）一个顶点组都没有 —— 静态网格。
    #    下面那句「顶点已在 armature 空间」对**有蒙皮**的件成立，对无蒙皮的件**不成立**
    #    （顶点在物体局部空间，靠 matrix_world 摆到位）。不烘就清矩阵 → 兜散架：
    #    实测 bbox 从 0.18 米炸到 x[-0.203,1.685] z[-0.607,1.789]，烘了但取在解父之后同样散
    #    （x[-2.675,0.091]）。补 bone_11（SW2_MAP → bip01_head_13）则让它跟头走，
    #    并让 --keep-head-frags 认得出它（否则主导骨为空 → 被判非头部 → 整块删光，实测余 0 顶点）。
    if len(o.vertex_groups) == 0 and len(o.data.vertices):
        # 🔴 烘到【骨架空间】而不是世界空间 —— 源骨架自己可能带变换（谦信的 Armature 把整机
        #    放在 x≈-145 处）。下面的重定向是按骨架空间算的，烘成世界空间会整体偏出去
        #    （实测 bbox x[-2.675,0.091]，正确值应落在头附近）。
        _am = next((x for x in bpy.data.objects if x.type == 'ARMATURE'), None)
        _tgt = (_am.matrix_world.inverted() @ mw) if _am is not None else mw
        o.data.transform(_tgt)
        gg = o.vertex_groups.new(name="bone_11")
        gg.add(list(range(len(o.data.vertices))), 1.0, 'REPLACE')
        print("   补头骨权重（源件无蒙皮）：%s → bone_11 ×%d" % (o.name, len(o.data.vertices)))
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



def prune_far(o, k=4.0):
    """【清理飞出去的碎片】—— 与 tools/face-pipeline/scripts/build_head.py 的 prune_far 同一套判据。

    为什么头盔必须做（2026-09-15 实测）：幸村那件兜（sub10，380 顶点 / 44 片碎片）里有 **2 片飞在
    x=±44.6cm（肩高）**，把包围盒撑到 **103cm 宽** → 缩到骑砍空间后头盔 0.8 米宽，戴上就是个大盖子。
    兜主体其实只有 ~25cm 宽。判据用**稳健离群**（不写死尺寸）：锚点 = 全体顶点中位数，
    尺度 = 到锚点距离的中位数，丢掉「重心离锚点 > k × 尺度」的连通域。
    """
    me = o.data
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    n0 = len(bm.verts)
    if n0 == 0:
        bm.free(); return 0
    seen = [False] * n0
    comps = []
    for v in bm.verts:
        if seen[v.index]:
            continue
        stack = [v]; seen[v.index] = True; comp = []
        while stack:
            cur = stack.pop(); comp.append(cur.index)
            for e in cur.link_edges:
                o2 = e.other_vert(cur)
                if not seen[o2.index]:
                    seen[o2.index] = True; stack.append(o2)
        comps.append(comp)
    co = [v.co.copy() for v in bm.verts]
    ax = sorted(c.x for c in co)[n0 // 2]
    ay = sorted(c.y for c in co)[n0 // 2]
    az = sorted(c.z for c in co)[n0 // 2]
    anchor = Vector((ax, ay, az))
    dists = sorted((c - anchor).length for c in co)
    scale = dists[n0 // 2]
    if scale <= 1e-9:
        bm.free(); return 0
    keep = set()
    for comp in comps:
        cc = Vector((0, 0, 0))
        for i in comp:
            cc += co[i]
        cc /= len(comp)
        if (cc - anchor).length <= k * scale:
            keep.update(comp)
    if not keep:
        keep = set(max(comps, key=len))
    drop = n0 - len(keep)
    if drop:
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.index not in keep], context='VERTS')
        bm.to_mesh(me); me.update()
    bm.free()
    return drop


def frag_dominant_verts(o):
    """→ [(碎片顶点列表, 该片主骨)]。碎片 = 连通域；主骨 = 全片权重求和后最大。

    与 tools/sw2-pipeline/scripts/part_census.py 同一套判据（那份是"量"，这份是"改"）。
    """
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.verts.ensure_lookup_table()
    seen = [False] * len(bm.verts)
    frags = []
    for v in bm.verts:
        if seen[v.index]:
            continue
        stack, comp = [v], []
        seen[v.index] = True
        while stack:
            cur = stack.pop()
            comp.append(cur.index)
            for e in cur.link_edges:
                o2 = e.other_vert(cur)
                if not seen[o2.index]:
                    seen[o2.index] = True
                    stack.append(o2)
        frags.append(comp)
    bm.free()
    out = []
    for comp in frags:
        tot = {}
        for vi in comp:
            for g in o.data.vertices[vi].groups:
                nm = o.vertex_groups[g.group].name
                tot[nm] = tot.get(nm, 0.0) + g.weight
        out.append((comp, max(tot.items(), key=lambda kv: kv[1])[0] if tot else None))
    return out


# 手/手指骨（源件名）。籠手应当止于手腕——手留给骑砍身体的手，
# 否则甲的"手"会和身体的手打架（实机症状：拳头被甲整个包住）。
HAND_SW = {"bone_18", "bone_19"} | {"bone_%d" % i for i in range(26, 46)}
# 头骨族（脸/眼/头发）：混装件（头发+披风）要按碎片把这组剔掉。
HEAD_SW = {"bone_10", "bone_11"} | {"bone_%d" % i for i in range(46, 63)}
# 着物要留的部分：躯干 + 腿 + **手臂（袖子）**。
# 🔴 手臂段必须留：源件上臂中段本来就是**着物的布袖子**盖的，不是袖/籠手盖的。
#    早先只留躯干+腿 -> 上臂中段露身体（试过用拉伸硬撑，副作用是籠手盖住拳头）。
#    手/手指不在内（由 --no-hands 单独切），头也不在内。
KEEP_SW = {"bone_1", "bone_8", "bone_9", "bone_2", "bone_3", "bone_4",
           "bone_5", "bone_6", "bone_7", "bone_24", "bone_25",
           "bone_12", "bone_13", "bone_14", "bone_15", "bone_16", "bone_17"}

# 🔴 通用化（2026-09-15，为其余 27 人）：`--parts` 的预设号是**幸村的**，别人各不相同。
#    新增两个按号直选的开关，配套数据 = tools/sw2-pipeline 的骨普查（part_census.py）：
#      · `--parts-idx 1,2,3,4,7,8,9`  直接给子网格号（覆盖 --parts）
#      · `--kimono-idx 1`            指定哪一件是内衬着物（默认 1 = 幸村，向后兼容）
#      · `--drop-head-idx 3,7`       这几件按**碎片**剔掉头骨族的碎片（头发+披风那类混装件）
KIMONO_IDX = [int(x) for x in (get(A, "--kimono-idx", "1") or "1").split(",") if x.strip().isdigit()]
DROP_HEAD_IDX = [int(x) for x in (get(A, "--drop-head-idx", "") or "").split(",") if x.strip().isdigit()]
# 🔴 `--helmet-whole <子网格号>`：这些件**整块要** —— 既不过 `--prune-far`，也不过
#    `--keep-head-frags` 的骨判据。用在「纯兜件但绑的不是头骨」上：实测谦信 idx15
#    （两条长布垂带，84 顶点）绑脊椎骨 bone_1，两个判据都会把它整块滤掉 →「谁都不属于」。
#    按归属完备性裁定（每个部件必须恰好属于 头/武器/甲/兜/四肢 之一）它归【兜】。
HELM_WHOLE = [int(x) for x in (get(A, "--helmet-whole", "") or "").split(",") if x.strip().isdigit()]


PRUNE_K = get(A, "--prune-far", "") or ""
for o in dups:
    sm = parse_submesh(o.name)
    dom = dominant_bones(o)
    n0 = len(o.data.vertices)
    if PRUNE_K and sm not in HELM_WHOLE:
        d = prune_far(o, float(PRUNE_K))
        if d:
            print("   剔飞散碎片：%s 删 %d 顶点" % (o.name, d))
            dom = dominant_bones(o)
        n0 = len(o.data.vertices)
    if STRAP_FROM and sm == int(STRAP_FROM) and (STRAP_SEEDS or STRAP_BONES):
        # 脸壳件只留【颏带碎片】：按碎片重心离种子最近选（判据同 build_head.drop_frag_by_seed）
        import bmesh as _bm
        b = _bm.new(); b.from_mesh(o.data); b.verts.ensure_lookup_table()
        seen = [False] * len(b.verts); comps = []
        for v in b.verts:
            if seen[v.index]:
                continue
            st = [v]; seen[v.index] = True; c = []
            while st:
                cur = st.pop(); c.append(cur.index)
                for e in cur.link_edges:
                    o2 = e.other_vert(cur)
                    if not seen[o2.index]:
                        seen[o2.index] = True; st.append(o2)
            comps.append(c)
        keep = set()
        for sd in STRAP_SEEDS:
            best, bd = None, 1e18
            for c in comps:
                ce = Vector((0, 0, 0))
                for i in c:
                    ce += b.verts[i].co
                ce /= len(c)
                d = (ce - sd).length
                if d < bd:
                    bd, best = d, c
            if best is not None and bd <= 12.0:
                keep.update(best)
        for i in range(len(b.verts)):
            best, bw = None, -1.0
            for g in o.data.vertices[i].groups:
                if g.weight > bw:
                    bw, best = g.weight, o.vertex_groups[g.group].name
            if best in STRAP_BONES:
                keep.add(i)
        kill = [i for i in range(len(b.verts)) if i not in keep]
        if not keep:
            print("   !! 颏带种子一个都没命中，整件丢掉")
        _bm.ops.delete(b, geom=[b.verts[i] for i in kill], context='VERTS')
        b.to_mesh(o.data); b.free(); o.data.update()
        print("   颏带并入：%s 只留 %d 顶点" % (o.name, len(o.data.vertices)))
        dom = dominant_bones(o)
        n0 = len(o.data.vertices)
    if "--keep-head-frags" in A and sm not in HELM_WHOLE:
        # 🔴 头盔专用：**只留头部碎片**（2026-09-15）。
        #    多数角色的兜和身体甲是**同一块**（挑件表的 helmet 只标"哪块里有兜"），
        #    整块拿来当头盔 = 把身体甲也带上 → 头盔 1.5 米宽。按主导骨只留头骨族的碎片。
        kill = [vi for comp, b in frag_dominant_verts(o) if b not in HEAD_SW for vi in comp]
        if kill:
            drop_verts(o, kill)
            print("   只留头部碎片：%s 删 %d 顶点（余 %d）" % (o.name, n0 - len(o.data.vertices), len(o.data.vertices)))
            dom = dominant_bones(o)
        n0 = len(o.data.vertices)
    if DROP_HEAD_IDX and sm in DROP_HEAD_IDX:
        kill = [vi for comp, b in frag_dominant_verts(o) if b in HEAD_SW for vi in comp]
        if kill:
            drop_verts(o, kill)
            print("   剔头碎片：%s 删 %d 顶点（%d 片）" % (o.name, n0 - len(o.data.vertices), 0))
            dom = dominant_bones(o)
        n0 = len(o.data.vertices)
    if "--no-hands" in A:
        drop_verts(o, [i for i, b in enumerate(dom) if b in HAND_SW])
        if len(o.data.vertices) != n0:
            print("   去手部：%s 删 %d 顶点" % (o.name, n0 - len(o.data.vertices)))
            dom = dominant_bones(o)
    if "--kimono-torso-only" in A and sm in KIMONO_IDX:
        # 🔴 只滤着物。作用到整块甲上会误删胴的肩帯（由锁骨驱动）
        k = len(o.data.vertices)
        drop_verts(o, [i for i, b in enumerate(dom) if b not in KEEP_SW])
        print("   着物瘦身：%s 删 %d 顶点，余 %d" % (o.name, k - len(o.data.vertices), len(o.data.vertices)))

# 🔴 颏带并进来之后必须【改成和兜同一根骨】（2026-09-15 实机实测 + 定位）。
#
#    症状：政宗的兜，下巴那条带子跑到**后脑**去了（用户实机截图）。
#    实测位移：y 后移 0.15~0.25m、z 下沉 0.06~0.09m。
#
#    根因：重定向是**按每根源骨各自**算修正矩阵的 ——
#        T[bone_46] = rider_rest[head_13] @ sw2_rest[bone_46]⁻¹
#        T[bone_11] = rider_rest[head_13] @ sw2_rest[bone_11]⁻¹     ← 两者不等
#    兜主体（碗/月牙/錣）绑 `bone_11`，颏带是从**脸壳件**剪来的、绑 `bone_46`/`bone_59`
#    —— 最后虽然都归到同一根骑砍骨（`bip01_head_13`），位移却各走各的矩阵，
#    于是整条带子被从兜上"拽"了下来。
#
#    修法：颏带是**刚性的挂件**，权重整批改写成【兜的主体骨】，跟兜刚性走。
#    ⚠️ 2026-09-15 深夜：把它扩成「整顶兜都改绑主体骨」（`--rigid`）后实机**更坏**
#       （忠胜兜被打散成飘着的碎片）—— 已回退，只保留颏带这一处。
if STRAP_FROM and (STRAP_SEEDS or STRAP_BONES):
    _strap_objs = [o for o in dups if parse_submesh(o.name) == int(STRAP_FROM)]
    _strap_objs = [o for o in _strap_objs if len(o.data.vertices)]
    if _strap_objs:
        _tot = {}
        for _o in dups:
            if _o in _strap_objs:
                continue
            for _v in _o.data.vertices:
                for _g in _v.groups:
                    _nm = _o.vertex_groups[_g.group].name
                    _tot[_nm] = _tot.get(_nm, 0.0) + _g.weight
        if _tot:
            _main = max(_tot.items(), key=lambda kv: kv[1])[0]
            for _o in _strap_objs:
                for _g in list(_o.vertex_groups):
                    _o.vertex_groups.remove(_g)
                _g = _o.vertex_groups.new(name=_main)
                _g.add([_v.index for _v in _o.data.vertices], 1.0, 'ADD')
            print("   颏带定骨：%d 顶点改绑 %s（跟随兜主体，防重定向拉走）"
                  % (sum(len(_o.data.vertices) for _o in _strap_objs), _main))

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

# 🔴 腿链也要按解剖段长对齐（2026-09-15 实机：下半身像小矮人）。
#    上面第 14 行早就写着「比例差（SW2 腿偏长）—— 由 λ 逐骨承担」，但代码里
#    **只有手臂真的承担了**；腿走 `Matrix.Scale(R, 4)` 各向同性 ——
#    大腿长度变成 源长 × R = 45.63cm × 0.0120 = 0.548m，而骑砍大腿只有 0.417m，差 31%。
#    径向仍用 R（腿的粗细本来就该用 R），只把**沿骨轴**换成段长比。
LEG_ALONG = {
    "bone_2": seg_ratio("bone_2", "bone_4", "bip01_l_thigh_1", "bip01_l_calf_2"),
    "bone_3": seg_ratio("bone_3", "bone_5", "bip01_r_thigh_5", "bip01_r_calf_6"),
    "bone_4": seg_ratio("bone_4", "bone_6", "bip01_l_calf_2", "bip01_l_foot_3"),
    "bone_5": seg_ratio("bone_5", "bone_7", "bip01_r_calf_6", "bip01_r_foot_7"),
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
    elif bn_sw in LEG_ALONG:
        # 腿：径向 R、沿骨轴按解剖段长（同手臂一套，只是径向用 R 不用 R_ARMS）
        d_sw = chain_dir(b_sw)
        d_sw = (MIRROR_Y3 @ d_sw).normalized() if d_sw else Vector((0.0, 0.0, -1.0))
        F = frame_from_dir(d_sw)
        S = F @ Matrix.Diagonal((R, LEG_ALONG[bn_sw], R, 1.0)) @ F.inverted()
    else:
        S = Matrix.Scale(R, 4)
    T_BONE[bn_sw] = (Matrix.Translation(b_bl.head_local) @ rot @ S
                     @ Matrix.Translation(-h_sw_f))

bpy.ops.object.select_all(action='DESELECT')
if "--dbg-parts" in A:
    # 逐件打印重定向后的世界包围盒（定位"某一件被撑爆"用；正常躯干件宽 ≈0.4~0.6 米）
    print("   [dbg] 逐件包围盒（重定向后）：")
    for o in sorted(dup_list, key=lambda x: x.name):
        ws = [o.matrix_world @ v.co for v in o.data.vertices]
        if not ws:
            print("      %-46s （空网格）" % o.name[:46]); continue
        xs = [w.x for w in ws]; ys = [w.y for w in ws]; zs = [w.z for w in ws]
        print("      sub%-3s v=%-5d x[%7.3f,%7.3f] y[%7.3f,%7.3f] z[%6.3f,%6.3f]  宽%.3f 深%.3f 高%.3f"
              % (parse_submesh(o.name), len(ws), min(xs), max(xs), min(ys), max(ys), min(zs), max(zs),
                 max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)))
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
def make_sheets_double_sided(ob, open_ratio=0.5):
    """【把薄片复制一份并翻面】—— 单面板在引擎里背面被剔除，从另一侧看就是"没有"。

    🔴 为什么必须做（2026-09-15 用户实机发现，兜侧）：甲/兜里大量零件是**单个平面**
       （前立 / 月牙 / 飘带 / 小饰件）。而 Bannerlord **材质层没有双面开关** ——
       `TaleWorlds.Engine.Material.MBMaterialShaderFlags` 全部 21 个标志里
       **没有** TwoSided / NoCull 之类（已反编译核对），所以只能靠几何补：复制一份、翻面绕序。
       症状：浅井长政的金前立只有正面，转到背面就"没有"了。

    判据用「边界边比例」区分**薄片**和**壳**：
      · 平面/布条：绝大多数边只挂 1 个面 → 比例接近 1 → 复制
      · 兜钵/甲壳：只有开口那圈是边界 → 比例很低 → **不复制**
        （复制了面数翻倍、两个面还互相打架）

    与 `tools/face-pipeline/scripts/build_head.py` 的同名函数是**同一套判据**
    （那份给头用，这份给甲/兜用）。返回复制的面数。
    """
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    seen = [False] * len(bm.faces)
    dup = []
    for f0 in bm.faces:
        if seen[f0.index]:
            continue
        stack, comp = [f0], []
        seen[f0.index] = True
        while stack:
            cur = stack.pop()
            comp.append(cur)
            for e in cur.edges:
                for nf in e.link_faces:
                    if nf is not cur and not seen[nf.index]:
                        seen[nf.index] = True
                        stack.append(nf)
        be = sum(1 for c in comp for e in c.edges if len(e.link_faces) == 1)
        te = sum(len(c.edges) for c in comp)
        if te and be / float(te) >= open_ratio:
            dup.extend(comp)
    n = len(dup)
    if n:
        geom = (list({v for f in dup for v in f.verts})
                + list({e for f in dup for e in f.edges}) + dup)
        ret = bmesh.ops.duplicate(bm, geom=geom)
        newf = [g for g in ret["geom"] if isinstance(g, bmesh.types.BMFace)]
        bmesh.ops.reverse_faces(bm, faces=newf)
        bm.to_mesh(me)
        me.update()
    bm.free()
    return n


# ---------- 薄片补背面：单面板复制+翻面 ----------
# 判据/理由见上面 make_sheets_double_sided 的 docstring（与 build_head.py 同一套）。
if "--no-double-sided" not in A:
    # ⚠️ 必须作用在**合并后的 ARM** 上：`dups` 那批对象在重定向阶段已经被并掉/删除了，
    #    再访问会 `ReferenceError: StructRNA of type Object has been removed`（实测踩过）。
    if len(ARM.data.polygons):
        _n0 = len(ARM.data.polygons)
        # `--double-sided-all`：不做判据，**每个碎片都复制+翻面**。
        # 用在「单面判据（边界边比 ≥0.5）漏判」的兜上 —— 实测长政的金前立
        # 边界边比只有 0.18（拓扑看着闭合），开背面剔除后整个月牙从背面消失。
        _n = make_sheets_double_sided(ARM, 0.0 if "--double-sided-all" in A else 0.5)
        if _n:
            print("  薄片补背面：复制 %d/%d 面并翻面（引擎材质无双面开关，只能靠几何）"
                  % (_n, _n0))

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
