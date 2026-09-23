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
      [--r 0.01]          # 🔴 老模式：径向缩放（厘米→米）——T 模式下退役，不参与计算
      [--t-s 0.01064]     # 🔴 走【T 模式】：逐角色源变换的缩放 s（来自 out/srcT.json）
      [--t-z-sole 0.0]    # T 模式的源脚底 z（实测恒 0，见 src_transform.py 文件头第 3 条）
      [--cut-z 0.0]       # 只保留高于该高度（米，骑砍空间）的面；0 = 不裁
      [--drop-coincident <头 FBX>]         # 删掉与头重合（≤1mm）的甲顶点（见正文那段）
      [--drop-coincident-tol 0.001]       # 重合阈值（米），默认 1mm
      [--drop-coincident-band 1.25,1.70]  # 只拿这个 z 带内的头顶点当靶子（兜侧要另给，见正文）
      [--drop-coincident-max-ratio 0.15]  # 安全闸：命中占比超它就报错退出、一个都不删
      [--lod 0.834,0.563,0.249,0.140,0.072]
      [--no-lod]
      [--feet-mesh <腿脚 skin FBX>]        # 脚部覆盖（见正文那段）：把甲里那只"鞋"换成能罩住它的壳
      [--foot-cut-z 0.17]                 #   删到哪（米）· [--foot-shell-z 0.38] 壳包到哪（米）
      [--foot-scale 1.02]                 #   绕脚踝放大倍数 · [--foot-margin 0.010] 法线外扩（米）
      [--no-foot-shell]                   #   关掉（甲按原样导出）

两种定标模式（2026-09-16 第 2 步）：
  · **T 模式**（给了 `--t-s`）= 整装按**逐角色源变换 T** 重排，只保留手臂链姿态修正。
    头/甲/兜三件共用同一份 T（唯一来源 `tools/sw2-pipeline/src_transform.py` + `out/srcT.json`），
    所以拼得回原角色。sw2 批量（build_armors.py / build_helmets.py）走这条。
    🔴 T 模式下甲片走【**碎片主导骨刚性归属**】（2026-09-17，治跨肘弯折，见下面那段长注释）。
  · **老模式**（没给 `--t-s`）= 逐骨 rest 重定向 + 全局径向系数 R（本文档开头那套原理）。
    🔴 真田幸村甲工程（`build_armor_chain.py`）走这条，**行为一行都没变**，别顺手改它。

🔴 T 模式的刚性归属（2026-09-17）——为什么甲要刚性、布要混合：
  · 症状（用户实机前看图）：真田幸村的籠手**跨肘那一段是弯的**。骑砍 A-pose 的肘带一点弯、
    源模型（战无2）是 T-pose 直臂，于是**上臂骨与前臂骨的修正旋转不相等**；老的逐顶点
    权重混合把两个旋转**平均**出来 → 一块**刚性甲片被弯进去**。
  · 修法：甲/兜按**连通域（碎片）**分组，每片取**主导骨**（全片权重求和最大那根），
    该片所有顶点**只吃这一根骨的旋转矩阵** —— 甲是硬片，整片跟着同一根骨转才不弯。
  · 例外（保持原来的逐顶点混合）：① `--cloth-drop` / `--cloth-hang` 标记的**布料件**；
    ② `--kimono-idx` 指定的**内衬着物**（也是布）。理由：布要软，混合正是它该有的行为。
  · 老模式（无 `--t-s`）不启用这条，一行行为不变。
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
# 🔴 退役（2026-09-16 第 2 步）：R / R_ARMS / R_RADIAL 只服务**老模式**（逐骨重定向 + 全局径向系数）。
#    T 模式下这三者一行都不参与计算，取代者 = 逐角色源变换 T（它的 s 同时承担缩放与落位）。
#    保留定义 = 真田幸村甲工程（build_armor_chain.py）还走老模式（按"退役两步走"：实机验证通过才删）。
R = float(get(A, "--r", "0.01"))
# 手臂链的径向缩放（默认跟 R 一样）。原版身体的胳膊比源件粗，躯干调够之后手臂仍会顶穿，
# 这里单独放一点。实测 0.0120 → 0.0145 才把上臂那圈身体盖住。
R_ARMS = float(get(A, "--r-arms", str(R)))
# ---------------------------------------------------------------- 🔴 逐角色源变换 T（第 2 步）
# 给了 `--t-s` 就走【T 模式】：整装顶点按**逐角色源变换 T** 重排
#     T(v) = ( s·x,  −s·y,  s·(z − z_sole) )        # Y 轴镜像（保 x）+ 等比缩放 + 落位
# 没给 = 老模式，行为一行不变（真田工程还靠它）。
#
# 为什么换（2026-09-16 实测）：改之前头按"源模型眼↔嘴距离"定标、甲按"全局 R=0.0120 + 逐骨钉到骑砍
# 骨架"定标 —— 两个基准**不同源**，拼不回原角色：实测宁宁的脖子下沿比甲领口上沿**高 8.7cm**
# （整圈断在甲上方），甲还比人粗 28%（R 0.0120 vs 身高对齐的 0.0094）。三件共用一份 T 之后，
# 宁宁 12/12 个角度过闸（脖子伸进领口）。
# 🔴 T 的定义只有一处实现 = `tools/sw2-pipeline/src_transform.py`（`T_of` / 逐角色表 `out/srcT.json`），
#    本文件**从那里导入**，不在自己这儿再抄一份公式 —— 抄一份 = 以后改 T 要改两处。
T_S_RAW = get(A, "--t-s")
T_Z_SOLE = float(get(A, "--t-z-sole", "0.0") or 0.0)
T_MODE = T_S_RAW not in (None, "")
T_FN = None
if T_MODE:
    T_S = float(T_S_RAW)
    _SW2_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(
        os.path.abspath(__file__)))), "sw2-pipeline")
    if _SW2_DIR not in sys.path:
        sys.path.insert(0, _SW2_DIR)
    try:
        from src_transform import T_of
    except ImportError as _e:
        print("!! T 模式需要 %s\\src_transform.py：%s" % (_SW2_DIR, _e)); sys.exit(2)
    T_FN = T_of(T_S, T_Z_SOLE)
    for _k in ("--r", "--r-arms", "--r-radial"):
        if _k in A:
            print("   ⚠️ T 模式：%s 不参与计算（缩放由 T 的 s 承担，见 --t-s 那段注释）" % _k)
CUT_Z = float(get(A, "--cut-z", "0.0"))
# ---------------------------------------------------------------- 🔴 图集方案（`--atlas-plan`）
# 一件甲 = 1 个材质（合原版范式）。源件各片各有自己的贴图，所以要：
#   ① 把每片的 UV 缩放进图集的某一格（**本脚本做**）② 把源贴图拼进同一张图（`make_armor_atlas.py` 做）
# 两边共用同一份 plan JSON（键 = **材质名**，不是网格名 —— 一个网格可能带多个材质）。
ATLAS_PLAN = get(A, "--atlas-plan", "")
ATLAS = None
if ATLAS_PLAN:
    import json as _jsonA
    with open(ATLAS_PLAN, encoding="utf-8") as _fh:
        _adoc = _jsonA.load(_fh)
    ATLAS = _adoc.get("pieces", {}).get(NAME)
    if ATLAS is None:
        print("   ⚠️ --atlas-plan 里没有 %r 这一件 → 跳过 UV 重排" % NAME)
    else:
        print("   图集：%dx%d 格 · 每格 %dpx · %d 个材质位 ← %s"
              % (ATLAS["grid"][0], ATLAS["grid"][1], ATLAS["cell"], len(ATLAS["map"]),
                 os.path.basename(ATLAS_PLAN)))
# 🔴 布料件「放下来」（2026-09-16）：列出子网格号，这些件会绕**自己的手臂轴**转到
#    「重心正对轴下方」。用于源件里的**悬垂布**（振袖/袍摆）——源模型是 T-pose，
#    布是斜挂在水平手臂上的，直接重定向到骑砍 A-pose 会变成"向后戳出去的一块板"。
CLOTH_DROP = [int(x) for x in get(A, "--cloth-drop", "").split(",") if x.strip()]
# 🔴 布料件「垂挂」（2026-09-16）：同上，但转轴**不是骨轴**，而是「重心→正下方」这条弧对应的
#    水平轴。用在**挂在腰/背上**的布片（长衣尾/后垂）—— 源件描成"迎风向后甩"的姿势，
#    骨轴是竖直的（脊椎），绕它转等于没转。实测：浅井长政 idx1（driven by nuno1_p_9）从腰部
#    向后伸 66cm，绕骨轴这条路走不通，必须用这个。
CLOTH_HANG = [int(x) for x in get(A, "--cloth-hang", "").split(",") if x.strip()]
# 🔴 LOD 保护（2026-09-21）：列出子网格号 —— 这些件在 LOD 的 decimate 里**保下来**。
#    用在**薄片**上（披风/布片：基本共面 ⇒ collapse 直接把它砍没，实测信长披风 lod3 整块消失）。
LOD_PROTECT = [int(x) for x in get(A, "--lod-protect", "").split(",") if x.strip()]
# 🔴 刚性归属的诊断开关（T 模式）：逐碎片打印「主导骨 / 走刚性还是混合 / 位移」。
#    平常只打一行统计，要查"某一片为什么被弯/被挪"时加它。
RIGID_REPORT = "--rigid-report" in A
# 🔴 关掉刚性归属（T 模式，**诊断用**）：整装回到"逐顶点混合"那条路 —— 用来做 A/B，
#    把"刚性归属带来的位移"与"手臂链旋转本来就有的位移"分开。默认不关（行为一行不变）。
#    用法：blender -b --python build_armor.py -- ... --no-rigid --out <临时目录>
NO_RIGID = "--no-rigid" in A
LOD_RATIOS = [float(x) for x in get(A, "--lod", "0.834,0.563,0.249,0.140,0.072").split(",")]
DO_LOD = "--no-lod" not in A

if not (SRC and SKEL and OUTDIR):
    print("!! 缺少 --src / --skel / --out")
    sys.exit(2)
os.makedirs(OUTDIR, exist_ok=True)

HEAD_BONE_Z = 1.569          # 官方骨架校准验收值（换头工程 §11.3）

# ---------------------------------------------------------------- 🔴 源件→骑砍 的「翻面」（两种情形，别搞混）
# 两边都是「正面 = −Y」，区别在**左手在 x 的哪一侧**：
#    · **镜像件**（战无2：前 −Y / 左 −X）→ 骑砍（前 +Y / 左 −X）差一个**反射**
#      ⇒ **Y 轴镜像** (x,y,z)->(x,-y,z)。镜像是反射（行列式 −1），做完**必须反转面绕序**，
#        否则法线朝里、甲渲染成内翻。
#    · **正常人形**（KCD：前 −Y / 左 **+X**）→ 骑砍（前 +Y / 左 −X）只差**半圈**
#      ⇒ **绕 Z 转 180°**（刚体旋转，行列式 +1），**不用**反绕序。
#        ⚠️ 这里千万别照抄战无2 的镜像 —— 那会把左右手对调。
# 判据（拿源件自己量）：找一根 `*Left*` 的骨或件，看它在 x 的正侧还是负侧。
MAP_JSON = get(A, "--map-json", "")
MAP_DOC = None
if MAP_JSON:
    import json as _json0
    with open(MAP_JSON, encoding="utf-8") as _fh:
        MAP_DOC = _json0.load(_fh)

FLIP_MODE = get(A, "--flip", (MAP_DOC or {}).get("flip", "mirror"))
if FLIP_MODE == "mirror":
    MIRROR_Y = Matrix.Diagonal((1.0, -1.0, 1.0, 1.0))
    NEED_REVERSE = True
elif FLIP_MODE in ("rot180", "z180"):
    MIRROR_Y = Matrix.Rotation(math.pi, 4, 'Z')
    NEED_REVERSE = False
else:
    print("!! --flip 只能是 mirror / rot180，收到 %r" % FLIP_MODE)
    sys.exit(2)
MIRROR_Y3 = MIRROR_Y.to_3x3()
print("   翻面 = %s（%s）" % (FLIP_MODE, "反射→要反绕序" if NEED_REVERSE else "刚体旋转→不反绕序"))

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

# ---------------------------------------------------------------- 换源：外部映射表（`--map-json`）
# 给一份 `{源骨名: 骑砍骨名}` 的表就整体替换 SW2_MAP —— 键的形状完全一样，
# 下面的 build_map() / --debug 打印 / 重定向全都照旧走，不需要第二套代码。
# 表由 `tools/kcd-pipeline/scripts/dump_skeletons.py -- pair` 量出来、`map_<角色>.json` 落盘。
# ⚠️ 父链兜底（build_map 里那段）仍然生效 —— 表里只写"有明确对应"的骨即可。
if MAP_DOC:
    _m = MAP_DOC.get("map", MAP_DOC)
    if not isinstance(_m, dict) or not _m:
        print("!! --map-json 里没有可用的 map"); sys.exit(2)
    SW2_MAP = dict(_m)
    print("   骨映射表 ← %s（%d 条显式映射）" % (os.path.basename(MAP_JSON), len(SW2_MAP)))


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
def patch_importer():
    """导入器内存补丁（不碰 Blender 安装文件）。两种源格式各踩过一个坑：

    ① morph 通道缺 FullWeights → 导入器断言崩溃（战无2 的带 morph 的角色 FBX）。
    ② 网格蒙皮到的骨头**不在骨架子树下**（在武器槽位骨那一支）→ `mesh.armature_setup`
       里没有对应登记 → `link_hierarchy` 抛 `KeyError: None`（KCD 的剑/盾/角色件都会触发）。

    两道补丁都只在"本来就会崩"的场合兜底，正常文件行为一行不变。
    """
    import inspect
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    orig = src
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched: morph without FullWeights")
    bad2 = "                    (mmat, amat) = mesh.armature_setup[self]"
    if bad2 in src:
        src = src.replace(bad2, (
            "                    if self not in mesh.armature_setup:\n"
            "                        mesh.armature_setup[self] = (mesh.bind_matrix, self.bind_matrix)\n"
            + bad2))
    if src != orig:
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)


def import_sw2(path):
    patch_importer()
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
    # 🔴 这两列（λ / 锚点差）是老模式的量。T 模式下把折算系数从 R 换成本角色的 T 缩放 s，
    #    否则打出来的数是"另一把尺子"上的，看的人会被误导（不是崩，是错）。
    _CAL = T_S if T_MODE else R
    print("   --- 直接映射表（SW2骨 -> 骑砍骨 | 骨长cm -> m | λ | 锚点差）折算系数 %s=%.6f ---"
          % ("T.s" if T_MODE else "R", _CAL))
    for k, v in sorted(SW2_MAP.items()):
        if k not in sw_local or v not in bl_local:
            print("   %-22s -> %-30s  (缺骨)" % (k, v)); continue
        sl, bl = sw_len[k], bl_len[v]
        lam = 1.0 if (sl < 1.0 or bl < 0.005) else max(0.5, min(2.0, bl / (_CAL * sl)))
        # 锚点差：SW2 骨 head 的 z（cm）按上面的系数缩放 与 骑砍骨 head 的 z（m）之差
        d_anchor = (sw_local[k].translation.z * _CAL) - bl_local[v].translation.z
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
# 换源：`--arm-rot` 给一串源骨名（逗号分隔）就整体替换上表。
# 只列**真的需要转角**的 —— 躯干/腿两边都是竖直的，加了反而产生剪切。
_arm_rot_arg = get(A, "--arm-rot")
if _arm_rot_arg is not None:
    ARM_ROT = set(x.strip() for x in _arm_rot_arg.split(",") if x.strip())
    print("   手臂链骨 ← --arm-rot（%d 根）" % len(ARM_ROT))


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
    _am = next((x for x in bpy.data.objects if x.type == 'ARMATURE'), None)
    # 🔴🔴 统一口径第一步：把网格对象的变换**烘进顶点**，烘到【骨架空间】（2026-09-19 补）。
    #    下面那句「顶点已在 armature 空间」只对**对象变换是单位阵**的源成立 —— 战无2 就是
    #    （它的网格对象没带变换，所以当年一直没暴露）。**KCD 不是**：它的网格对象继承了
    #    FBX 的 `0.01 缩放 + 绕 X 90°`（顶点是 cm / Y-up），而骨架已经被 apply_all 烘平成
    #    m / Z-up ⇒ 顶点与骨头**不在同一个空间**，重定向整块错位（实测甲 bbox 从 ±0.49 米
    #    炸到 x±66.8 / y−160）。
    #    烘到骨架空间（不是世界空间）是原脚本的既有口径 —— 源骨架自己可能带位移（谦信的
    #    Armature 把整机放在 x≈−145 处），烘成世界空间会整体偏出去。
    #    单位阵时 `_tgt` = 单位阵 ⇒ 一行都不动，战无2 行为不变。
    _tgt = (_am.matrix_world.inverted() @ mw) if _am is not None else mw
    _idm = Matrix.Identity(4)
    if max(abs(_tgt[r][c] - _idm[r][c]) for r in range(4) for c in range(4)) > 1e-6:
        o.data.transform(_tgt)
        print("   烘物体变换进顶点（源网格对象非单位变换）：%s" % o.name)
    # 🔴 无蒙皮的件：**补头骨权重**（2026-09-15 深夜）。
    #    实测上杉谦信的兜（submesh_0..._0000.001，242 顶点）一个顶点组都没有 —— 静态网格。
    #    补 bone_11（SW2_MAP → bip01_head_13）则让它跟头走，
    #    并让 --keep-head-frags 认得出它（否则主导骨为空 → 被判非头部 → 整块删光，实测余 0 顶点）。
    if len(o.vertex_groups) == 0 and len(o.data.vertices):
        _nb = next((b for b in ("bone_11", "Head", "head") if b in SW2_MAP), None)
        if _nb is None:
            print("   !! 无蒙皮件 %s 找不到可用的头骨名（SW2_MAP 里没有 bone_11/Head），跳过补权重" % o.name)
        else:
            gg = o.vertex_groups.new(name=_nb)
            gg.add(list(range(len(o.data.vertices))), 1.0, 'REPLACE')
            print("   补头骨权重（源件无蒙皮）：%s → %s ×%d" % (o.name, _nb, len(o.data.vertices)))
    o.matrix_world = Matrix.Identity(4)   # 顶点已烘到 armature 空间，清掉残留变换
    # 🔴 图集 UV 重排（2026-09-19）：按**面**查它用的材质 → 落到哪一格 → 把该面的 UV 缩进格子。
    #    必须在这里做（**合并成一件之前**）—— 合并后材质被统一成一个，就再也分不出哪片面原来是谁的了。
    if ATLAS:
        _cols, _rows = ATLAS["grid"]
        _mp = ATLAS["map"]
        _slot_cell = {}
        for _i, _ms in enumerate(o.material_slots):
            _mn = _ms.material.name if _ms.material else ""
            _slot_cell[_i] = _mp.get(_mn)
        _uvl = o.data.uv_layers.active
        _nface, _miss = 0, set()
        if _uvl is None:
            print("   !! 图集：%s 没有 UV 层，跳过" % o.name)
        else:
            for _poly in o.data.polygons:
                _cell = _slot_cell.get(_poly.material_index)
                if _cell is None:
                    _mn = (o.material_slots[_poly.material_index].material.name
                           if _poly.material_index < len(o.material_slots)
                           and o.material_slots[_poly.material_index].material else "?")
                    _miss.add(_mn)
                    continue
                _cx, _cy = _cell["cell"]
                for _li in _poly.loop_indices:
                    _u, _v = _uvl.data[_li].uv
                    # 🔴 源件可能用**平铺 UV**（实测 KCD 的武装衣主槽 u∈[1.001,1.996]，贴图横向重复
                    #    两次）。不先折回 [0,1) 就直接加偏移 = 整片采到隔壁格子（错得很隐蔽）。
                    _u -= math.floor(_u)
                    _v -= math.floor(_v)
                    _uvl.data[_li].uv = ((_u + _cx) / _cols, (_v + _cy) / _rows)
                _nface += 1
            print("   图集 UV 重排：%s 的 %d 个面（格 %dx%d）" % (o.name, _nface, _cols, _rows))
            if _miss:
                print("   ⚠️ 这些材质没排格子（其 UV 保持原样，会取到图集别的区域）：%s" % sorted(_miss))

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

# ---------------------------------------------------------------- 🔴 与头「分界一致」：按同一批源顶点删（2026-09-17 晚）
#   为什么：区域剔除（--skin-drop-region）是"两头各判一次空间区域"，甲侧会剔多/剔少
#   （实测小太郎：r13cm 剔出洞、r10cm 头上只剩 5 顶点）。正解 = 头侧抠取时把**取走的源顶点**
#   落盘（`--dump-neck-src`），甲侧按**同一批点**删面 —— 头拿多少，甲就正好少多少。
DROP_HEAD = get(A, "--drop-from-head")
if DROP_HEAD and os.path.isfile(DROP_HEAD):
    import json as _json
    from mathutils import kdtree as _kdt
    _d = _json.load(open(DROP_HEAD, encoding="utf-8"))
    _pts = _d.get("pts") or []
    _tol = float(_d.get("tol", 0.0005))
    if _pts:
        _kt = _kdt.KDTree(len(_pts))
        for _i, _pp in enumerate(_pts):
            _kt.insert(Vector(_pp), _i)
        _kt.balance()
        _nf = 0
        for _o in dups:
            _mw = _o.matrix_world
            _near = set(_vi for _vi, _v in enumerate(_o.data.vertices)
                        if _kt.find(_mw @ _v.co)[2] <= _tol)
            _kill = [pp for pp in _o.data.polygons if all(v in _near for v in pp.vertices)]
            if _kill:
                import bmesh as _bm_mod
                _bm = _bm_mod.new(); _bm.from_mesh(_o.data); _bm.faces.ensure_lookup_table()
                _bm_mod.ops.delete(_bm, geom=[_bm.faces[pp.index] for pp in _kill], context='FACES')
                _orph = [v for v in _bm.verts if not v.link_faces]
                if _orph:
                    _bm_mod.ops.delete(_bm, geom=_orph, context='VERTS')
                _bm.to_mesh(_o.data); _bm.free()
                _nf += len(_kill)
        print("   与头分界一致：按源顶点清单 %d 点（≤%.1fmm）删面 %d" % (len(_pts), _tol * 1000, _nf))

# ---------------------------------------------------------------- 重定向
print("== 5/6 骨架重定向（%s）==" % ("T 模式：整装按源变换 T 重排" if T_MODE else "rest retarget"))
if T_MODE:
    print("   T：s=%.6f z_sole=%.3f（T(v) = s·x, −s·y, s·(z−z_sole)）；"
          "只保留手臂链旋转，R/λ/逐骨锚定退役" % (T_S, T_Z_SOLE))

# 🔴 下面这一大段（T_BONE 的老路径 + 布料轴）是**老模式**的定标：
#    逐骨 rest 重定向 = Translate(骑砍骨head) · Rot(仅手臂) · Scale(R) · Translate(−源骨head)。
#    T 模式下**整段退役**，取代者 = 逐角色源变换 T（见文件头 args 区那段注释）。
#    本段只服务真田幸村甲工程（build_armor_chain.py 不带 --t-s），按"退役两步走"保留。
# 逐骨预计算重定向矩阵： T = Translate(bl头) · Rot(仅手臂) · Scale(R) · Translate(-sw头)
# 🔴 手臂的缩放必须拆成两个方向，不能用一个 Matrix.Scale 各向同性地放。
#    踩过（2026-09-14 实机）：把 --r-arms 从 0.0120 提到 0.0165，径向确实盖住了身体，
#    但**沿骨轴也被拉了 65%** —— 前臂 25.97cm × 0.0165 = 0.429m，而骑砍肘到手腕只有 0.267m，
#    于是籠手末端超出拳头 16cm（用户截图："手腕没有伸出袖子"）。
#    修法：沿骨轴按**解剖段长**对齐（源件肘→腕 与 骑砍肘→腕 的比值），径向才用 R_ARMS。
#    段长比值直接由两边关节坐标算，不写死。
by_name = {o.name: o for o in dups}
picked_names = [o.name for o in picked]
dup_list = [by_name[n] for n in picked_names if n in by_name]
if len(dup_list) != len(picked):
    # 名字可能被加了后缀，退回按数量取
    dup_list = dups


def seg_ratio(sw_a, sw_b, bl_a, bl_b):
    """两对关节之间的距离比（米/厘米）= 沿骨轴的缩放。
    🔴 换源（`--map-json`）时下面那两张表里写死的战无2 骨名**一根都对不上** ——
       那不是错，是"这张表对本源不适用"，返回 None 由调用处滤掉即可（别让它 KeyError）。"""
    if (sw_a not in SW.data.bones or sw_b not in SW.data.bones
            or bl_a not in BL.data.bones or bl_b not in BL.data.bones):
        return None
    ds = (SW.data.bones[sw_a].head_local - SW.data.bones[sw_b].head_local).length
    db = (BL.data.bones[bl_a].head_local - BL.data.bones[bl_b].head_local).length
    return db / ds if ds > 1e-9 else 0.01


# SW2 骨 -> 沿骨轴缩放（未列出的骨用 R）
# 🔴 退役（T 模式）：R_RADIAL / ARM_ALONG / LEG_ALONG 三个都是"补偿 R 的错"的补丁
#    （R 同时管粗细和纵向尺度，于是把它们拆开各自调）。T 是整装等比缩放，本来就没有这个错，
#    三者在 T 模式下**一行都不参与计算**；保留 = 老模式（真田工程）还走。取代者 = T。
R_RADIAL = float(get(A, "--r-radial", "0") or 0) or None   # 径向单独给值（0/缺省 = 与 R 相同，即旧行为）

ARM_ALONG = {k: v for k, v in {
        "bone_14": seg_ratio("bone_14", "bone_16", "bip01_l_upperarm_twist_15", "bip01_l_foretwist_17"),
        "bone_15": seg_ratio("bone_15", "bone_17", "bip01_r_upperarm_twist_22", "bip01_r_foretwist_24"),
        "bone_16": seg_ratio("bone_16", "bone_18", "bip01_l_foretwist_17", "bip01_l_hand_19"),
        "bone_17": seg_ratio("bone_17", "bone_19", "bip01_r_foretwist_24", "bip01_r_hand_26"),
}.items() if v is not None}

# 🔴 腿链也要按解剖段长对齐（2026-09-15 实机：下半身像小矮人）。
#    上面第 14 行早就写着「比例差（SW2 腿偏长）—— 由 λ 逐骨承担」，但代码里
#    **只有手臂真的承担了**；腿走 `Matrix.Scale(R, 4)` 各向同性 ——
#    大腿长度变成 源长 × R = 45.63cm × 0.0120 = 0.548m，而骑砍大腿只有 0.417m，差 31%。
#    径向仍用 R（腿的粗细本来就该用 R），只把**沿骨轴**换成段长比。
LEG_ALONG = {k: v for k, v in {
        "bone_2": seg_ratio("bone_2", "bone_4", "bip01_l_thigh_1", "bip01_l_calf_2"),
        "bone_3": seg_ratio("bone_3", "bone_5", "bip01_r_thigh_5", "bip01_r_calf_6"),
        "bone_4": seg_ratio("bone_4", "bone_6", "bip01_l_calf_2", "bip01_l_foot_3"),
        "bone_5": seg_ratio("bone_5", "bone_7", "bip01_r_calf_6", "bip01_r_foot_7"),
}.items() if v is not None}

# ---------------------------------------------------------------- 换源：沿骨轴段长比
# `--map-json` 里的 `segments` 就是为这件事准备的，格式 = [源起点骨, 源终点骨, 骑砍起点骨, 骑砍终点骨, 说明]。
# 沿骨轴缩放挂在**段的起点骨**上（与上面 ARM_ALONG / LEG_ALONG 同口径），所以一个 dict 就够 ——
# 换源后不再分"手臂表/腿表"，两张都指同一份。
#
# 为什么必须有：老模式的 `--r` 是**各向同性**的，它同时管粗细和纵向尺度。
# 骑砍大腿 0.417m、源件按 R 缩完可能差 30% → 腿变矮人或变竹竿。段长比把"沿骨轴"那一维单独校正。
# 🔴 KCD 的账：段长比中位 0.992（≈1），所以这一项在 KCD 上本来就接近 1 —— 但**不能省**，
#    因为它是逐段的（实测上臂 1.053 / 小腿 0.963，正负 5% 的差别肉眼看得出来）。
if MAP_DOC and MAP_DOC.get("segments"):
    _ALONG = {}
    for _sg in MAP_DOC["segments"]:
        _a, _b, _ba, _bb = _sg[0], _sg[1], _sg[2], _sg[3]
        if (_a in SW.data.bones and _b in SW.data.bones
                and _ba in BL.data.bones and _bb in BL.data.bones):
            _ALONG[_a] = seg_ratio(_a, _b, _ba, _bb)
        else:
            print("   !! segments 缺骨，跳过: %s" % (_sg,))
    ARM_ALONG = dict(_ALONG)
    LEG_ALONG = dict(_ALONG)
    print("   沿骨轴段长比 ← segments（%d 段）" % len(_ALONG))


def frame_from_dir(d):
    """造一个 Y 轴 = d 的正交基（列向量）。🔴 只服务**老模式**的径向/沿骨轴拆分（T 模式不用）"""
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
A_ROT = {}       # 手臂链：各骨的【绝对】姿态修正旋转（T 模式用，见下面第二遍）
A_PIV = {}       # 手臂链：关节（T 空间的源骨头部）

# 🔴🔴 T 模式·第三遍（2026-09-17 实机修）：**四肢按骨锚定**。
#    症状（用户实机·宁宁）：**四肢和甲整体错开**，手臂/腿看着像"战无的站姿"。
#    实测（重心对齐，左侧）：原版身体 / 老模式甲 / T 模式甲 ——
#      · 小腿 x 中心：−0.1375 / **−0.1325（差 5mm）** / **−0.0765（差 61mm）**
#      · 上臂 x 中心：−0.257  / **−0.256（差 1mm）**   / **−0.296（差 39mm）**
#    根因：T 只是**整装等比缩放 + 只锚头骨**，它**不知道骑砍骨架的四肢长在哪** ——
#      源模型是**窄站姿**（脚 x=±7.34 源单位 × s = **±0.078m**，与实测 −0.076 吻合），
#      而骑砍身体是**宽站姿** ⇒ 甲整体偏内 6cm。**老模式没这问题，因为它有这一项。**
#    修法：四肢骨（臂链 + 腿链）把几何**搬到骑砍骨头**上；旋转照旧（手臂姿态修正不丢）。
#      **躯干 / 头颈一律不动** —— T 在那里已经对上（领口 vs 身体颈顶只差 16.6mm），动了反而坏。
#    公式：`M_b = Trans(bl_head_b) ∘ rot_b ∘ Trans(-p_b)`，p_b = T(源骨头部)
#      · 性质① 源骨头部 p_b 恰好落到骑砍骨头部 → 锚定成立；
#      · 性质② 旋转部分不变 → 手臂该斜 33° 还是 33°（姿态修正不被这次改动破坏）；
#      · 腿的 rot_b = 单位 → 退化成**纯平移**。
#    `--no-anchor-limb` = 关掉（诊断 A/B 用，关掉即回到"只有 T"的旧行为）。
ANCHOR_LIMB = "--no-anchor-limb" not in A
LIMB_TAG = ("thigh", "calf", "foot", "toe", "upperarm", "forearm", "foretwist", "hand")


def _limb_scale(bn_sw):
    """四肢「沿骨轴」缩放 —— 把源骨段拉到**骑砍骨段长**。

    🔴 为什么还要这一项（第三遍补）：只把骨**头部**锚过去还不够 —— 源的骨段长度与骑砍不同
      （老模式的 `ARM_ALONG`/`LEG_ALONG` 就是干这个的，T 轮一起取消了）。
      实例：大腿源 45.63 单位 → 骑砍 0.417m；T 的 s 给 0.01064 → 得到 0.486m，**长了 14%**
      ⇒ 末端（膝/腕/踝）仍然落不到骑砍关节上。
    本项把沿骨轴的长度从 `s·ds` 拉到 `db`：λ = db/(s·ds) = seg_ratio / s。
    **只动沿骨轴**：径向不缩放（v_src 已经被 T 的 s 缩放过了，两边粗细本就一致）。
    """
    _r = ARM_ALONG.get(bn_sw) or LEG_ALONG.get(bn_sw)
    if not _r or T_S <= 0:
        return Matrix.Identity(4)
    _lam = _r / T_S
    if abs(_lam - 1.0) < 1e-6:
        return Matrix.Identity(4)
    _b = SW.data.bones.get(bn_sw)
    _d = chain_dir(_b) if _b is not None else None
    if _d is None:
        return Matrix.Identity(4)
    _F = frame_from_dir((MIRROR_Y3 @ _d).normalized())
    return _F @ Matrix.Diagonal((1.0, _lam, 1.0, 1.0)) @ _F.inverted()


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
    # 🔴 T 模式（2026-09-16 第 2 步）：整装按源变换 T 重排，逐骨只保留**手臂链的姿态修正**。
    #    · 为什么要修正：源是 T-pose（手臂水平）、骑砍是 A-pose → 不转的话袖子/籠手会横着伸出去，
    #      这是**姿态差**，不是定标差，T 管不着它（老模式里也一样，见上面 ARM_ROT 那段实测）。
    #    · 为什么只留手臂：其余骨两边都朝竖直方向，T 是等比缩放 + 镜像、不改变任何骨的朝向，
    #      再套 rot 只会制造剪切（老模式里非手臂骨 rot 本来就是单位矩阵 —— 两边一致）。
    #    · 绕哪转：**T 空间里的源骨头部**（pivot = T(源骨 head)）。关节为轴 = 姿势修正，
    #      不会把整条手臂平移走（平移该由 T 承担，逐骨平移在老模式里是"把源骨钉到骑砍骨"，
    #      正是 T 要取代的那个错）。
    if T_MODE:
        # 手臂链先只记「绝对旋转 A」与「关节」（T 空间的源骨头部），矩阵留到下面第二遍算 ——
        # 因为链式累积必须**父先于子**（见 T_BONE 第二遍那段）。
        # 🔴 第三遍补充：**四肢骨走「按骨锚定」**（见上面 ANCHOR_LIMB 那段），
        #    非四肢骨（躯干/头颈）才是单位矩阵 —— 那两处 T 已经对上，不许动。
        if bn_sw in ARM_ROT:
            A_ROT[bn_sw] = rot.copy()
            A_PIV[bn_sw] = Vector(T_FN(b_sw.head_local))
        elif ANCHOR_LIMB and any(_t in bn_bl for _t in LIMB_TAG):
            _p = Vector(T_FN(b_sw.head_local))
            T_BONE[bn_sw] = (Matrix.Translation(b_bl.head_local)
                             @ _limb_scale(bn_sw) @ Matrix.Translation(-_p))
        else:
            T_BONE[bn_sw] = Matrix.Identity(4)
        continue
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
        # 🔴 2026-09-16：**径向与沿骨轴分开**（--r-radial）。
        #    动机：R 是"一个数管两件事" —— 它同时定**粗细**和**纵向尺度**（脊椎骨各向同性缩放），
        #    于是"躯干太粗"和"领口对上脖子"两件事被绑死了：想细一点就得连领口一起压下去
        #    （实测 R 0.0120→0.0094 时甲的顶从 1.762 掉到 1.644，脖子缝反而更大）。
        #    按骨轴拆开后：沿骨轴仍用 R（保持纵向对位），径向用实测拟合值。
        #    判据（怎么定 R_RADIAL 的值）：源模型按"身高对齐比例"缩放后的剪影宽度
        #    = 源 1.903m 高时躯干宽 xx m，与我们产物同高度处的宽度比 —— 见 Debug/offline/_cmp_h.py。
        d_sw = chain_dir(b_sw)
        if R_RADIAL is not None and d_sw is not None:
            d_sw = (MIRROR_Y3 @ d_sw).normalized()
            F = frame_from_dir(d_sw)
            S = F @ Matrix.Diagonal((R_RADIAL, R, R_RADIAL, 1.0)) @ F.inverted()
        else:
            S = Matrix.Scale(R, 4)
    T_BONE[bn_sw] = (Matrix.Translation(b_bl.head_local) @ rot @ S
                     @ Matrix.Translation(-h_sw_f))

# 🔴 T 模式·第二遍：手臂链**按父子累积**（2026-09-17 实测修，治"前臂甲不跟肩走"）。
#    症状：只把"每片甲刚性归到主导骨"改完，笼手的**上臂片与前臂片在肘部脱开**，比改前还难看。
#    根因：老的算法给每根骨一个**绝对**旋转、各自绕**自己的关节**转 —— 前臂骨绕**源肘**转，
#      源肘是它的不动点（原地不动）；而上臂骨绕肩转、把肘搬到了下面 → 两段对不上。
#    正确做法（标准链式重定向）：子骨的旋转是**相对父**的，绕的是**父变换之后的关节**：
#        local_b = A_parent⁻¹ · A_b                    （A = 各骨的绝对姿态修正旋转）
#        M_b     = M_parent ∘ Trans(p_b) ∘ local_b ∘ Trans(-p_b)
#    两个性质同时成立：① 朝向 —— M_b 的线性部分仍是 A_b（绝对角不变，手臂该斜 33° 还是 33°）；
#      ② 关节 —— 肘/腕跟着父骨走，甲片在关节处**接得上**。
#    ⚠️ 父骨要跳过源模型里那批**长度 0 的空节点**（bone_80/82/84… 朝向是噪声），
#       往上找到第一根真正在手臂链上的骨（bone_12→14→16→18→手指，实测已验证）。
if T_MODE:
    _armparent = {}
    for _bn in A_ROT:
        _p = SW.data.bones[_bn].parent
        while _p is not None and _p.name not in A_ROT:
            _p = _p.parent
        _armparent[_bn] = _p.name if _p is not None else None
    _accm = {}

    def _chain_mat(_bn):
        if _bn in _accm:
            return _accm[_bn]
        _pv = A_PIV[_bn]
        _par = _armparent.get(_bn)
        if _par is None:
            _m = Matrix.Translation(_pv) @ A_ROT[_bn] @ Matrix.Translation(-_pv)
        else:
            _loc = A_ROT[_par].inverted() @ A_ROT[_bn]
            _m = (_chain_mat(_par) @ Matrix.Translation(_pv) @ _loc
                  @ Matrix.Translation(-_pv))
        _accm[_bn] = _m
        return _m

    _n_anch = 0
    for _bn in A_ROT:
        _m = _chain_mat(_bn)
        if ANCHOR_LIMB and any(_t in BMAP[_bn] for _t in LIMB_TAG):
            # 第三遍：把这条臂骨的**源关节** p_b 搬到骑砍骨的头部，旋转照旧（姿态修正不丢）。
            #   M_b = Trans(bl_head) ∘ rot(chain) ∘ Trans(−p_b)
            _bl_head = BL.data.bones[BMAP[_bn]].head_local
            T_BONE[_bn] = (Matrix.Translation(_bl_head) @ _m.to_3x3().to_4x4()
                           @ _limb_scale(_bn) @ Matrix.Translation(-A_PIV[_bn]))
            _n_anch += 1
        else:
            T_BONE[_bn] = _m
    print("   手臂链累积：%d 根骨（父 → 子，跳过源模型的 0 长空节点）；其中按骨锚定 %d 根"
          % (len(A_ROT), _n_anch))

# ---------------------------------------------------------------- 布料件「放下来」· 第 1 步：打标记
# 🔴 为什么需要（2026-09-16 实机症状："浓姬的衣服像奇怪形状的硬纸板"）：
#    源模型是 **T-pose**（手臂水平：肩 x=±13.6 / 肘 x=±39.9 / 腕 x=±63.0，全在 z=138.8），
#    两片振袖是**沿水平手臂横着伸出去的大布片**（源件里 x 24.5→63.7，同时向后下各挂 ~50cm）。
#    重定向到骑砍 A-pose 时，手臂骨绕 **y 轴**转 ~33°，**布片的垂坠方向跟着一起转**
#    —— 本来是"垂下"的布变成"朝外斜戳出去"，实机就是两片硬翅膀。
#    物理上不对：重力不跟着手臂转。
#
#    修法：**在重定向之后**、在骑砍空间里，绕**骑砍这条手臂自己的轴**（foretwist→hand，
#    即肘→腕）把那片布转到"重心正对轴的正下方"，让它按重力垂下来。
#    ⚠️ 不能在源空间先转 —— 试过，会被随后的手臂旋转再转歪（2026-09-16 实测）。
#    这一步纯几何、离线可验证、不依赖引擎布料；做完它再上布料才是"在正确姿态上飘"。
#
#    轴的取法不写死人名：第 1 步按子网格号打标记 → 第 2 步（重定向之后）用这些顶点
#    **重定向后的主导骨**当轴，所以换角色不用改代码。
CLOTH_MARK = {}
if CLOTH_DROP or CLOTH_HANG:
    _by_sub = {}
    for _o in dup_list:
        _s = parse_submesh(_o.name)
        if _s is not None:
            _by_sub[_s] = _o
    for _mode, _subs in (("drop", CLOTH_DROP), ("hang", CLOTH_HANG)):
        for _s in _subs:
            _o = _by_sub.get(_s)
            if _o is None:
                print("   !! --cloth-%s %d：选件里没有这个子网格" % (_mode, _s)); continue
            _g = _o.vertex_groups.new(name="__cloth_%s_%d__" % (_mode, _s))
            _g.add(list(range(len(_o.data.vertices))), 1.0, 'REPLACE')
            CLOTH_MARK["__cloth_%s_%d__" % (_mode, _s)] = None
    print("   布料件标记：%s" % sorted(CLOTH_MARK))

# 🔴 LOD 保护（2026-09-21）：**薄片会被 decimate 吃光**。实测信长的披风（两片各 44 顶点、
#    基本共面）在 lod3 就整块消失 —— 离远披风直接不见（原版 09-20 那份就有，不是布料引入的）。
#    做法：合并前给这些子网格打组 → 合并后取出顶点号 → LOD 循环里当 decimate 的"别动"权重。
#    Blender 的语义（实测 `_dec_test.py`）：vertex_group 的**权重越高砍得越狠**，
#    所以保护要用 `invert_vertex_group=True` + 权重 1。
#    ⚠️ 只在传了 --lod-protect 时生效 ⇒ 不传就与改动前**逐字节同路**，其他角色不受影响。
LODP_MARKED = False
if LOD_PROTECT:
    for _o in dup_list:
        if parse_submesh(_o.name) in LOD_PROTECT:
            _g = _o.vertex_groups.new(name="__lodprotect__")
            _g.add(list(range(len(_o.data.vertices))), 1.0, 'REPLACE')
            LODP_MARKED = True
    print("   LOD 保护件标记：%s" % (sorted(LOD_PROTECT) if LODP_MARKED else "（选件里没有）"))

# 🔴 着物（内衬布）也要在**合并前**打标记（2026-09-17）：`bpy.ops.object.join()` 之后
#    子网格号就没了，再想认"哪块顶点是着物"只能靠当前这个标记组。着物是**布**，
#    刚性归属要放过它（跟 --cloth-drop/--cloth-hang 一个道理，见文件头那段）。
#    标记组用完即删（留着会被当成骨名导出）。
KIMONO_MARKED = False
if T_MODE and KIMONO_IDX:
    for _o in dup_list:
        if parse_submesh(_o.name) in KIMONO_IDX:
            _g = _o.vertex_groups.new(name="__kimono__")
            _g.add(list(range(len(_o.data.vertices))), 1.0, 'REPLACE')
            KIMONO_MARKED = True

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

# 布料件：把标记组里的顶点号抓出来，然后**删掉标记组**（否则会被当骨名导出）
CLOTH_IDX = {}
if CLOTH_MARK:
    for _nm in list(CLOTH_MARK):
        _g = ARM.vertex_groups.get(_nm)
        if _g is None:
            continue
        _gi = _g.index
        _ids = [v.index for v in ARM.data.vertices
                if any(x.group == _gi and x.weight > 0.5 for x in v.groups)]
        CLOTH_IDX[_nm] = _ids
        ARM.vertex_groups.remove(_g)
    print("   布料件顶点：%s" % {k: len(v) for k, v in CLOTH_IDX.items()})

# 着物标记 → 顶点号集合，然后删掉标记组（同 CLOTH_IDX 的处理）
KIMONO_V = set()
if KIMONO_MARKED:
    _g = ARM.vertex_groups.get("__kimono__")
    if _g is not None:
        _gi = _g.index
        KIMONO_V = set(v.index for v in ARM.data.vertices
                       if any(x.group == _gi and x.weight > 0.5 for x in v.groups))
        ARM.vertex_groups.remove(_g)
    print("   着物件顶点：%d（布，走混合）" % len(KIMONO_V))

# LOD 保护标记 → 顶点号（LOD 循环里当 decimate 的"别动"权重），然后删掉标记组
LODP_V = []
if LODP_MARKED:
    _g = ARM.vertex_groups.get("__lodprotect__")
    if _g is not None:
        _gi = _g.index
        LODP_V = [v.index for v in ARM.data.vertices
                  if any(x.group == _gi and x.weight > 0.5 for x in v.groups)]
        ARM.vertex_groups.remove(_g)
    print("   LOD 保护顶点：%d" % len(LODP_V))

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
    # 🔴 T 模式下 R 不参与计算：第四列换成「T 空间的源骨头部 − 骑砍骨头部」＝这套尺子下的锚点差。
    #    老模式那一列才是 R 折算的平移偏移。两者都是**只打不改**的诊断量。
    _lbl = "枢轴Δ" if T_MODE else "平移Δ"
    print("   --- 逐骨：旋转角（只有手臂链非零）+ %s（%s）---"
          % (_lbl, "T 空间源骨头部 − 骑砍骨头部" if T_MODE else "R 折算偏移"))
    rows = []
    for bn_sw, T in T_BONE.items():
        b_sw = SW.data.bones.get(bn_sw); b_bl = BL.data.bones.get(BMAP[bn_sw])
        if T_MODE:
            off = Vector(T_FN(b_sw.head_local)) - b_bl.head_local
        else:
            off = (b_bl.head_local - R * b_sw.head_local)
        rows.append((T_ANGLE.get(bn_sw, 0.0), bn_sw, BMAP[bn_sw], off))
    for ang, bn_sw, bn_bl, off in sorted(rows, key=lambda r: -r[0]):
        print("   %6.1f°  %-20s -> %-28s %s=(%+.3f,%+.3f,%+.3f)" % (
            ang, bn_sw, bn_bl, _lbl, off.x, off.y, off.z))

# ---------------------------------------------------------------- 🔴 刚性归属（T 模式，2026-09-17）
# 一整段理由见文件头「T 模式的刚性归属」。这里只说实现：
#   · 碎片 = 网格**连通域**（`frag_dominant_verts`，与 part_census / --drop-head-idx 同一套判据）。
#     合并（join）不会把两块拓扑连起来 → 一个碎片必然只属于**一个源子网格**。
#   · 主导骨 = 全片**权重求和**最大的那根源骨（不是"每顶点各自最大"）。
#   · 该片所有顶点改用**主导骨那一根**的 T_BONE 矩阵（T_BONE 里只有手臂链非单位矩阵，
#     所以只有手臂上的片会真的变；躯干/腿本来是单位矩阵，刚性与混合结果相同）。
#   · 放过（继续逐顶点混合）：布料件（--cloth-drop/--cloth-hang）、着物（--kimono-idx）、
#     主导骨没有矩阵的片（没映射上 / 骨不在表里）。
#   · 权重（顶点组）**原样不动** —— 这一步只定"顶点摆在哪"，不动"游戏里怎么跟骨动画"。
CLOTH_V = set()
for _ids in CLOTH_IDX.values():
    CLOTH_V.update(_ids)
RIGID_OF = {}          # 顶点号 -> 骨名（走刚性）；不在表里 = 走混合
_report_rows = []      # --rigid-report 用：[(顶点数, 主导骨, 顶点号列表)]
if NO_RIGID and T_MODE:
    print("   ⚠️ --no-rigid：刚性归属整体关闭（诊断 A/B 用），整装走逐顶点混合")
if T_MODE and not NO_RIGID:
    _frags = frag_dominant_verts(ARM)
    _n_rig_f = _n_rig_v = _n_cloth_f = _n_nomat_f = _n_nodom_f = 0
    _dis_mx = 0.0
    _report_rows = []
    for _comp, _dom in _frags:
        if _dom is None:
            _n_nodom_f += 1
            continue
        if T_BONE.get(_dom) is None:
            _n_nomat_f += 1
            continue
        if any(i in CLOTH_V for i in _comp):
            _n_cloth_f += 1
            continue
        if any(i in KIMONO_V for i in _comp):
            _n_cloth_f += 1
            continue
        for _i in _comp:
            RIGID_OF[_i] = _dom
        _n_rig_f += 1
        _n_rig_v += len(_comp)
        if RIGID_REPORT:
            _report_rows.append((len(_comp), _dom, _comp))
    print("   刚性归属：碎片 %d/%d（顶点 %d）、走混合 %d 碎片（布 %d / 无矩阵 %d / 无主导骨 %d）"
          % (_n_rig_f, len(_frags), _n_rig_v, _n_cloth_f + _n_nomat_f + _n_nodom_f,
             _n_cloth_f, _n_nomat_f, _n_nodom_f))

# 统计
n_moved = 0
n_skipped = 0
n_rigid = 0
_dis_of = {}           # 顶点号 -> 刚性位置 vs 老混合位置的位移（只诊断用）
_dis_mx = 0.0
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
    if T_MODE:
        # T 模式：先整装按 T 重排（T 自带 Y 轴镜像 + 等比缩放 + 落位），再按权重混合逐骨矩阵
        # （逐骨矩阵只有手臂链非单位 = 姿态修正，见上面 T_BONE 那段）
        v_src = Vector(T_FN(v.co))
    else:
        v_src = MIRROR_Y @ v.co      # 先镜像到骑砍朝向，再重定向
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
    if tot <= 1e-6:
        n_skipped += 1
        continue
    # 🔴 刚性归属（只有 T 模式会填 RIGID_OF）：整片甲只吃**主导骨**那一根矩阵。
    #    权重照旧累计（上面那圈已做完），这里只决定顶点摆在哪 —— 甲是硬片，不参与混合。
    _rig = RIGID_OF.get(v.index)
    if _rig is not None:
        v.co = T_BONE[_rig] @ v_src
        n_rigid += 1
        if RIGID_REPORT:
            _d = (v.co - acc / tot).length
            if _d > _dis_mx:
                _dis_mx = _d
            _dis_of[v.index] = _d
        continue
    v.co = acc / tot
    n_moved += 1


# 只有【镜像】才需要反绕序（反射把面绕序翻反了 → 法线朝里）；
# 【绕 Z 转 180°】是刚体旋转，绕序本来就对，再翻一次反而全部朝里。
if NEED_REVERSE:
    import bmesh
    _bm = bmesh.new()
    _bm.from_mesh(ARM.data)
    bmesh.ops.reverse_faces(_bm, faces=_bm.faces[:])
    _bm.to_mesh(ARM.data)
    _bm.free()
    ARM.data.update()
    print("   已反转面绕序（镜像补偿）")
else:
    print("   跳过反绕序（刚体旋转，无需补偿）")

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
if T_MODE:
    print("   刚性归属：%d 顶点只吃主导骨那一根矩阵；仍在混合 %d 顶点"
          % (n_rigid, len(ARM.data.vertices) - n_rigid))
if RIGID_REPORT and _report_rows:
    # 逐碎片：主导骨 / 顶点数 / **刚性位置相对老混合位置**的最大位移（改前改后的差就是这个数）
    _report_rows.sort(key=lambda r: -max(_dis_of.get(i, 0.0) for i in r[2]))
    print("   [rigid] 逐碎片（按位移排序，只列位移 > 1mm 的；重心 = 该片刚性后位置均值）：")
    for _n, _d, _c in _report_rows:
        _mx = max(_dis_of.get(i, 0.0) for i in _c)
        if _mx <= 0.001:
            continue
        _ctr = Vector((0.0, 0.0, 0.0))
        for _i in _c:
            _ctr += ARM.data.vertices[_i].co
        _ctr /= len(_c)
        print("   [rigid]   v=%-5d 主导=%-26s 最大位移 %6.1f mm  重心 (%+.3f,%+.3f,%+.3f)"
              % (_n, _d, _mx * 1000.0, _ctr.x, _ctr.y, _ctr.z))
    print("   [rigid] 全件最大位移 %.1f mm" % (_dis_mx * 1000.0))
print("   顶点组 %d 个" % len(ARM.vertex_groups))
badv = sum(1 for v in ARM.data.vertices if not v.groups)
print("   未绑定顶点 %d" % badv)

# ---------------------------------------------------------------- 布料件「放下来」· 第 2 步：转
# 到这一步顶点已经在**骑砍空间**、权重已换成骑砍骨名，所以"向下"就是世界 -Z。
# 轴 = 该件重定向后的主导骨（袖子 → bip01_?_foretwist_*）→ 它的 hand 子骨，即肘→腕。
if CLOTH_IDX:
    _vgname = {g.index: g.name for g in ARM.vertex_groups}
    for _nm, _ids in CLOTH_IDX.items():
        if not _ids:
            print("   !! %s：0 个顶点，跳过" % _nm); continue
        _mode = "hang" if "_hang_" in _nm else "drop"
        _ctr = Vector((0.0, 0.0, 0.0))
        for _i in _ids:
            _ctr += ARM.data.vertices[_i].co
        _ctr /= len(_ids)
        _dn = Vector((0.0, 0.0, -1.0))
        if _mode == "hang":
            # 挂件（长衣尾/后垂）：以**该件最高点**为悬点（布挂在最上面），
            # 绕「悬点→重心」扫到正下方所对应的水平轴转过去。
            # 悬挂点 = **顶部那一圈的重心**，不是"最高的单个顶点"。
            # 🔴 踩过（2026-09-16）：用单个最高顶点当悬点时，那片布的最高的顶点总在**某一侧边角**上，
            #    悬点一带 x 偏移，转轴 `cross(v, 下)` 就不再是水平横轴 → 整片布被**甩到身体一侧**，
            #    渲出来是一块悬空斜挂的大布（长政实测）。对称的布件必须用对称的悬点。
            _zs = sorted(ARM.data.vertices[i].co.z for i in _ids)
            _cut = _zs[max(0, int(len(_zs) * 0.9))]
            _top_ids = [i for i in _ids if ARM.data.vertices[i].co.z >= _cut]
            _apt = Vector((0.0, 0.0, 0.0))
            for _i in _top_ids:
                _apt += ARM.data.vertices[_i].co
            _apt /= len(_top_ids)
            _v = _ctr - _apt
            _ax = _v.cross(_dn)
            if _v.length < 1e-6 or _ax.length < 1e-6:
                print("   -- %s：已经是竖直的，无需转" % _nm); continue
            _adir = _ax.normalized()
            _ang = _v.angle(_dn)
            _M = (Matrix.Translation(_apt) @ Matrix.Rotation(_ang, 4, _adir)
                  @ Matrix.Translation(-_apt))
            _label = "悬点 z=%.3f" % _apt.z
        else:
            _cnt = {}
            for _i in _ids:
                _gs = ARM.data.vertices[_i].groups
                if not _gs:
                    continue
                _bn = _vgname.get(max(_gs, key=lambda g: g.weight).group)
                if _bn:
                    _cnt[_bn] = _cnt.get(_bn, 0) + 1
            if not _cnt:
                print("   !! %s：定不出重定向后的主导骨，跳过" % _nm); continue
            _bone = max(_cnt, key=_cnt.get)
            _b = BL.data.bones.get(_bone)
            if _b is None or not _b.children:
                print("   !! %s：%s 没有子骨，定不出手臂轴，跳过" % (_nm, _bone)); continue
            _hand = next((c for c in _b.children if "hand" in c.name), None)
            _c = _hand or max(_b.children, key=lambda x: x.length)
            _apt = _b.head_local.copy()
            _adir = (_c.head_local - _b.head_local).normalized()
            _rel = _ctr - _apt
            _perp = _rel - _adir * _rel.dot(_adir)
            _tgt = _dn - _adir * _dn.dot(_adir)
            if _perp.length < 1e-6 or _tgt.length < 1e-6:
                print("   -- %s：重心已在轴下方附近，无需转" % _nm); continue
            _ang = _perp.normalized().angle(_tgt.normalized())
            _sign = 1.0 if _adir.dot(_perp.cross(_tgt)) >= 0.0 else -1.0
            _ang = _sign * _ang
            _M = (Matrix.Translation(_apt) @ Matrix.Rotation(_ang, 4, _adir)
                  @ Matrix.Translation(-_apt))
            _label = "%s→%s" % (_bone, _c.name)
        for _i in _ids:
            ARM.data.vertices[_i].co = _M @ ARM.data.vertices[_i].co
        ARM.data.update()
        print("   cloth-%s %s：绕 %s 转 %+.1f°，让布垂下"
              % (_mode, _nm, _label, math.degrees(_ang)))

# （主骨过滤已移到合并**之前**、且只作用于着物 —— 见上面 dup 循环里那段）

# ---------------------------------------------------------------- 🔴 去重复：与头重合的甲/兜顶点
# 症状（2026-09-17 实测）：甲剔头骨族用的是**骨骼**判据（HEAD_SW = bone_10/11 + 面部骨 46..62，
#   见 `--drop-head-idx`），而**脖子皮肤绑的是胸骨 bone_9**（见 build_head.carve_neck_part 文档里的
#   实测），**不在**那一族里 → 脖子的那圈几何被甲**留着**；头侧也有同一份顶点（脖子件 / 脸壳下沿）、
#   两边共用同一个源变换 T → 位置**完全重合**（实机 z-fighting：皮肤与甲皮同深度打架）。
#   实测中招：光秀 91 / 归蝶 76 / 小太郎 20 / 胜家 13 / 小次郎……（共 6 人 207 顶点 @≤1.5mm）。
#
# 修法：把**头 FBX 的顶点**读进来，甲侧凡是**距离 ≤ 阈值**（默认 1mm）的顶点删掉。
#   为什么是「≤1mm」而不是「相等」：两边走同一个 T，同一批源顶点的理论偏差为 0，
#   1mm 只是吸收浮点误差（实测是整批命中 91/76/20 这种，不是零星蹭到）。
#
# 🔴 判据一般化（2026-09-17 第二版）—— 靶子从「只认 `_neck` 件」改成「头 FBX 的**所有件**」：
#   旧版只认**材质名以 `_neck` 结尾**的那件，于是**没有独立脖子件的头**（脸壳自带颈部）一个顶点
#   都看不见 —— 实测光秀 91 / 小太郎 20 个顶点正是「**脸壳下沿 vs 甲领口**」重合，旧判据报 0。
#   代价是多认了几件当靶子 ⇒ 用**领口带**把它圈住（见下），远处本来就不该重合的区域不参与。
#
#   `--drop-coincident-band z0,z1`（默认 `1.25,1.70`）：**只拿带内的头顶点当靶子**。
#     为什么必须限带：头资产里还挂着头发/头饰/长马尾一类几何，它们在 z 1.25 以下或 1.70 以上
#     与甲本来就不该重合（隔着衣服/悬在体外），拿来当靶子会变成误删。领口那一圈实测就在
#     1.31~1.60（光秀脸壳下沿 1.312、甲领口顶 1.570），1.25~1.70 把整圈包住还留了余量。
#     ⚠️ 兜侧要另给带（兜顶到 2.25，见 `build_helmets.py`），默认值只服务甲。
#
#   🔴 安全闸 `--drop-coincident-max-ratio`（默认 0.15）：**匹配到的顶点数占本件总顶点比例
#     超过它就报错退出、一个都不删**。为什么要有：判据一旦跑歪（比如传错了头、带给宽了），
#     删的就不是"重复的那一圈"而是整件甲 —— 甲被剪空是不可逆的（`--force` 重跑才会回来，
#     而人不会注意到）。实测正常的命中率：光秀 91/1463 = 6.2%、小太郎 20/1172 = 1.7%；
#     兜侧半藏 62/62 = 100%（头侧把整顶兜吞进了脖子件）就是该被这条挡下来的样子。
#
# 谁传：`build_armors.py`（武将只要有头产物就传）/ `build_helmets.py`（同上，带另给）。
#   头 FBX 里混着**源空间残留**（z 0~210 厘米的源子网格）—— 按「整件最高点 > 5 米」整件排除，
#   否则那些顶点会以"游戏空间里的近距离"混进靶子（实测不筛时 15 顶兜里 7 顶报假重合）。
#
# 放这一步之前做过什么：顶点位置（重定向 + 权重）、布料旋转都已算完 → 这里删的坐标就是最终坐标。
COINC_FBX = get(A, "--drop-coincident")
COINC_TOL = float(get(A, "--drop-coincident-tol", "0.001"))
COINC_BAND = [float(x) for x in (get(A, "--drop-coincident-band", "") or "1.25,1.70").split(",")]
COINC_MAX_RATIO = float(get(A, "--drop-coincident-max-ratio", "0.15"))
COINC_Z_GAME = 5.0          # 整件最高点超它 = 源空间残留（游戏空间最高件是兜 ~2.3 米）
if COINC_FBX:
    if not os.path.isfile(COINC_FBX):
        print("   !! --drop-coincident 的文件不存在：%s（跳过，甲按原样导出）" % COINC_FBX)
    else:
        # 🔴 必须先快照再导入：`bpy.ops.wm.read_factory_settings` 会把正在做的甲一起清掉
        #    （这个函数在本文件别处用得很顺手，但那是"从零开场景"时）。读法 = 导入 → 抓坐标 → 删新增对象。
        _before = set(bpy.data.objects)
        _pts = []
        _skipped = []
        try:
            bpy.ops.import_scene.fbx(filepath=COINC_FBX)
            bpy.context.view_layer.update()
            _new = [o for o in bpy.data.objects if o not in _before]
            for _o in _new:
                if _o.type != 'MESH' or not len(_o.data.vertices):
                    continue
                _mw = _o.matrix_world
                _co = [_mw @ v.co for v in _o.data.vertices]
                if max(p.z for p in _co) > COINC_Z_GAME:      # 源空间残留 → 整件不参与
                    _skipped.append(_o.name)
                    continue
                _pts += [p for p in _co if COINC_BAND[0] <= p.z <= COINC_BAND[1]]
        finally:
            # 读完即删（含导入进来的骨架，否则会被后面的清场/导出带上）
            for _o in [o for o in bpy.data.objects if o not in _before]:
                bpy.data.objects.remove(_o, do_unlink=True)
        if _skipped:
            print("   去重复：头 FBX 里 %d 件源空间残留已排除（%s）"
                  % (len(_skipped), ", ".join(n[:28] for n in _skipped[:3])))
        if not _pts:
            print("   !! --drop-coincident：%s 在 z %.2f~%.2f 带内没有头顶点（没删任何顶点）"
                  % (os.path.basename(COINC_FBX), COINC_BAND[0], COINC_BAND[1]))
        else:
            import mathutils.kdtree as _kd
            _kt = _kd.KDTree(len(_pts))
            for _i, _q in enumerate(_pts):
                _kt.insert(_q, _i)
            _kt.balance()
            _amw = ARM.matrix_world
            _hit = [v.index for v in ARM.data.vertices if _kt.find(_amw @ v.co)[2] <= COINC_TOL]
            _ratio = len(_hit) / float(len(ARM.data.vertices)) if len(ARM.data.vertices) else 0.0
            _z0 = min(p.z for p in _pts)
            _z1 = max(p.z for p in _pts)
            if _ratio > COINC_MAX_RATIO:
                # 🔴 安全闸：不删、报错退出、**不覆盖产物**（本文件的导出在最后一步，这里退出 =
                #    目标 FBX 保持原样）。宁可留着重合，也不能把一件甲剪空。
                print("   !! 去重复安全闸触发：头靶子 %d 顶点（带 %.2f~%.2f z %.3f~%.3f）命中本件 %d/%d = %.1f%%"
                      " > 上限 %.1f%% ⇒ 一个都不删，退出（判据跑歪了：传错头 / 带给宽了 / 这件本来就与头同一批几何）"
                      % (len(_pts), COINC_BAND[0], COINC_BAND[1], _z0, _z1,
                         len(_hit), len(ARM.data.vertices), _ratio * 100, COINC_MAX_RATIO * 100))
                sys.exit(4)
            if _hit:
                drop_verts(ARM, _hit)
            print("   去重复：头靶子 %d 顶点（全件去源空间残留后、限带 z %.2f~%.2f，实际 z %.3f~%.3f）"
                  " → 本件删掉 %d 顶点（%.1f%%，距离 ≤ %.0fmm，余 %d）"
                  % (len(_pts), COINC_BAND[0], COINC_BAND[1], _z0, _z1, len(_hit), _ratio * 100,
                     COINC_TOL * 1000, len(ARM.data.vertices)))

# 🔴 区域剔除（`--skin-drop-region "r,z0,z1"`，2026-09-17 晚加）：
#    把「脖子/胸口那片皮肤所在的区域」**从甲里剔掉** —— 与头侧 `build_head.py --skin-region`
#    **同一份参数**（都写在 parts_table 那一行的 `skin_region`），两头一致才不会出现
#    「甲占着皮肤区（实机=领口里一块灰）」或「两边重叠打架」。
#    只删**整个面都落在区域内**的面（保守：不撕开甲、边界留一圈过渡面）。
SKIN_DROP = get(A, "--skin-drop-region")
if SKIN_DROP:
    import bmesh
    _sr, _sz0, _sz1 = [float(x) for x in SKIN_DROP.split(",")[:3]]
    bm = bmesh.new()
    bm.from_mesh(ARM.data)
    _bn_ok = None
    _sb = get(A, "--skin-drop-region-bones")
    if _sb:
        _want = set("bone_%s" % b for b in _sb.split(","))
        _bn_ok = {}
        for v in ARM.data.vertices:
            if v.groups:
                _bn_ok[v.index] = (ARM.vertex_groups[max(v.groups, key=lambda x: x.weight).group].name in _want)
    _kill = [f for f in bm.faces
             if all((v.co.x ** 2 + v.co.y ** 2) ** 0.5 <= _sr and _sz0 <= v.co.z <= _sz1
                    and (_bn_ok is None or _bn_ok.get(v.index, False))
                    for v in f.verts)]
    _n0 = len(bm.faces)
    bmesh.ops.delete(bm, geom=_kill, context='FACES')
    bm.to_mesh(ARM.data)
    bm.free()
    print("   区域剔除（r%.0fcm z%.2f~%.2f）：删面 %d/%d → v=%d f=%d"
          % (_sr * 100, _sz0, _sz1, len(_kill), _n0, len(ARM.data.vertices), len(ARM.data.polygons)))

# 挂到骑砍骨架ARM.parent = BL
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

# ---------------------------------------------------------------- 🔴 脚部覆盖（--feet-mesh，2026-09-23 立）
#
# 症状（用户实机 2026-09-23）：织田信长穿甲时**双脚露皮肤**（脚趾 / 脚背 / 脚踝）。
# 根因（离线量的，不是猜）：游戏里显示的脚**不是甲的一部分** —— 是 skin 的 `legs_mesh`
#   （男 `feet_male_a` / 女 `feet_female_a`，骑砍骨架、圆润人脚，191 顶点/只），
#   而甲里那只"鞋"来自源件（68 顶点/只的低模楔形）。两者**形状对不上**：实测原版脚面
#   136/386 顶点戳在甲外（35%），露的就是脚趾、脚背、脚踝。
#   🔴 **放大救不了**（实测档 1.00~1.50）：最好的 1.20 也只收到 56/386，1.30 以上反而更糟
#      （鞋一大，鞋底/鞋跟又切进脚的另一侧）—— **楔形拓扑套不住圆脚，这是形状问题不是尺寸问题**。
#
# 修法（**只动脚区**；甲其余部分的位置 / 权重 / UV 一律不动）：
#   ① 删：甲里 **z < `--foot-cut-z` 且绑脚骨**的那只"鞋"（它永远罩不住脚，留着就是穿帮）。
#   ② 补：拿**原版脚自己** z < `--foot-shell-z` 的部分，以该侧脚踝为枢轴放大 `--foot-scale`
#      倍做成"靴壳"，顶端收口封盖。因为壳是原版脚的**等比放大**，"罩得住"是**几何保证**，
#      不是调参调出来的（判据自量，见 ③）。
#      · 权重抄原版脚自己的（`l_foot` → `bip01_l_foot_3` …）⇒ 走路时和脚一模一样地跟着动；
#      · UV 从被删的鞋件按**最近顶点**抄 ⇒ 贴的还是源件那只鞋的深色皮面，不另开贴图；
#      · 壳往上包到 `--foot-shell-z`（默认 0.32）：源件小腿件罩不住的那条前缝也一并补上。
#   ③ 验收（离线可验，**不是"看着对"**）：原版脚 z<`--foot-cut-z` 的顶点**必须 0 个露在甲外**；
#      本步自己量并打前后对比数。
FOOT_SHELL = "--no-foot-shell" not in A
FEET_REF = get(A, "--feet-mesh")
FOOT_CUT_Z = float(get(A, "--foot-cut-z", "0.17"))     # 删到哪（米）：源件那只鞋的上缘
FOOT_SHELL_Z = float(get(A, "--foot-shell-z", "0.38"))  # 壳包到哪（米）：小腿件内侧
FOOT_SCALE = float(get(A, "--foot-scale", "1.02"))      # 壳相对原版脚的放大倍数（默认不放大）
FOOT_MARGIN = float(get(A, "--foot-margin", "0.010"))   # 🔴 沿原版脚**表面法线**外扩多少（米）= 保底间隙
FOOT_SOLE_OFF = float(get(A, "--foot-sole-off", "0.0"))    # 壳整体下沉量（米）—— 🔴 默认 0：实测下沉会把脚侧面往下挪，
                                                          #    反而在脚侧上沿露皮肤（脚底那道折痕不值这个代价）      # 壳相对原版脚的放大倍数
_FOOT_W_BONES = ("bip01_l_foot_3", "bip01_l_toe0_4", "bip01_r_foot_7", "bip01_r_toe0_8")
_ANKLE_BONE = {"l": "bip01_l_foot_3", "r": "bip01_r_foot_7"}
# 原版脚 FBX 里的骨名（短名，human_skeleton）→ 本管线的骑砍骨名（带 bip01_ 编号）
_FEET_WMAP = {"l_thigh": "bip01_l_thigh_1", "l_calf": "bip01_l_calf_2",
              "l_foot": "bip01_l_foot_3", "l_toe0": "bip01_l_toe0_4",
              "r_thigh": "bip01_r_thigh_5", "r_calf": "bip01_r_calf_6",
              "r_foot": "bip01_r_foot_7", "r_toe0": "bip01_r_toe0_8"}


def _foot_bvh(zcap=0.70):
    """甲（腿以下）的 BVH —— 验收判据用。"""
    from mathutils.bvhtree import BVHTree
    co = [tuple(v.co) for v in ARM.data.vertices]
    polys = [tuple(p.vertices) for p in ARM.data.polygons
             if min(co[i][2] for i in p.vertices) < zcap]
    return BVHTree.FromPolygons(co, polys, all_triangles=False)


def _tmp_me_for_subdiv(verts, faces, name):
    """把 verts/faces 装成一个临时 Blender mesh（细分用；用完由调用方丢弃）。"""
    me = bpy.data.meshes.new(name + "_sub")
    me.from_pydata([tuple(v) for v in verts], [], faces)
    me.update()
    return me


def _foot_uncovered(bvh, pts, nors, reach=0.05):
    """修**前**的体检数（只当量级参考，别当验收判据）：从原版脚每个顶点沿它的外法线打 5cm 射线，
    够不到任何甲面就记一笔。偏高（法线与甲面接近平行时会擦面而过），所以只用来回答
    "修之前有多糟"，验收一律看渲染（`_foot_visual.py`）。"""
    n_bad = 0
    for p, n in zip(pts, nors):
        if bvh.ray_cast(p + n * 0.002, n, reach)[0] is None:
            n_bad += 1
    return n_bad, 0.0


if FOOT_SHELL and T_MODE:
    if not FEET_REF or not os.path.isfile(FEET_REF):
        print("   !! 脚部覆盖：--feet-mesh 没给或文件不存在（%s）⇒ 跳过（甲按原样导出）" % FEET_REF)
    else:
        import bmesh
        # --- 读原版脚：顶点 / 面 / 权重（导入→抓→删对象，写法照 --drop-coincident）---
        _b4 = set(bpy.data.objects)
        _fv, _ff, _fw, _fn = [], [], [], []
        try:
            bpy.ops.import_scene.fbx(filepath=FEET_REF)
            bpy.context.view_layer.update()
            for _o in [o for o in bpy.data.objects if o not in _b4]:
                if _o.type != 'MESH' or not len(_o.data.vertices):
                    continue
                _base = len(_fv)
                _mw = _o.matrix_world
                _mw3 = _mw.to_3x3()
                _gn = [g.name for g in _o.vertex_groups]
                _fv += [_mw @ v.co for v in _o.data.vertices]
                _fn += [(_mw3 @ v.normal).normalized() for v in _o.data.vertices]
                _ff += [tuple(_base + i for i in p.vertices) for p in _o.data.polygons]
                for _v in _o.data.vertices:
                    _fw.append({_gn[g.group]: g.weight for g in _v.groups if g.weight > 1e-5})
        finally:
            for _o in [o for o in bpy.data.objects if o not in _b4]:
                bpy.data.objects.remove(_o, do_unlink=True)
        # --- 甲侧：要删的"鞋" = **z 低于 FOOT_CUT_Z 且主导骨是腿骨**的那一圈 ---
        # 🔴 判据不能用"脚骨权重 ≥ 0.5"（第一版踩过）：源件那只鞋**上缘有一圈顶点绑的是小腿骨**
        #    （实测 136 个脚区顶点里只删掉 104，剩 32 个 + 它们连的面 ⇒ 鞋面整片留着，
        #     于是鞋面从壳里穿出来、皮肤照样露 —— 症状与没修一样）。
        #    改判"主导骨 ∈ 八根腿骨"（大腿/小腿/脚/趾）⇒ 鞋整个走干净；同时**不会碰**
        #    挂在腿边的披风/长袍（它们绑的是脊椎/布骨），也不是"一刀切 z<0.17"。
        _vgn = [g.name for g in ARM.vertex_groups]
        _LEG_BONES = set(_FEET_WMAP.values())          # bip01_?_{thigh,calf,foot,toe0}
        _dom = []
        for _v in ARM.data.vertices:
            if not _v.groups:
                _dom.append(None)
            else:
                _dom.append(_vgn[max(_v.groups, key=lambda g: g.weight).group])
        _boot = sorted(i for i in range(len(_dom))
                       if _dom[i] in _LEG_BONES and ARM.data.vertices[i].co.z < FOOT_CUT_Z)
        # 枢轴 = 该侧脚踝（造壳与验收共用同一份，含 z：见下面那段"枢轴 z 不能取地面"）
        _piv = {}
        for _sd, _bn in _ANKLE_BONE.items():
            _b = BL.data.bones.get(_bn)
            if _b is not None:
                _piv[_sd] = (BL.matrix_world @ _b.head_local).copy()
        _bvh0 = _foot_bvh()
        _low = [i for i, p in enumerate(_fv) if p.z < FOOT_CUT_Z]      # 脚区（验收只看这里）
        _n0, _ = _foot_uncovered(_bvh0, [_fv[i] for i in _low], [_fn[i] for i in _low])
        if not _boot:
            print("   !! 脚部覆盖：甲里 z<%.2f 没有绑脚骨的顶点（这件没有鞋件）⇒ 跳过" % FOOT_CUT_Z)
        elif not _low:
            print("   !! 脚部覆盖：%s 在 z<%.2f 没有几何 ⇒ 跳过" % (os.path.basename(FEET_REF), FOOT_CUT_Z))
        else:
            # 删鞋之前先把它的 UV 抄下来（供壳采样，壳贴的就是这只鞋的皮面）
            _uvname = ARM.data.uv_layers.active.name if ARM.data.uv_layers.active else None
            _boot_uv = {}
            if _uvname:
                _uvl = ARM.data.uv_layers[_uvname]
                _acc = {}
                for _lp in ARM.data.loops:
                    if _lp.vertex_index in set(_boot):
                        _acc.setdefault(_lp.vertex_index, []).append(tuple(_uvl.data[_lp.index].uv))
                for _i, _l in _acc.items():
                    _boot_uv[_i] = (sum(u for u, v in _l) / len(_l), sum(v for u, v in _l) / len(_l))
            from mathutils.kdtree import KDTree
            _kd = KDTree(len(_boot))
            for _n, _i in enumerate(_boot):
                _kd.insert(ARM.data.vertices[_i].co, _n)
            _kd.balance()
            _bb = [ARM.data.vertices[i].co.copy() for i in _boot]
            # --- ① 删鞋 ---
            _bm = bmesh.new()
            _bm.from_mesh(ARM.data)
            _bm.verts.ensure_lookup_table()
            bmesh.ops.delete(_bm, geom=[_bm.verts[i] for i in _boot], context='VERTS')
            _bm.to_mesh(ARM.data)
            _bm.free()
            ARM.data.update()
            # --- ② 造壳：原版脚 z<FOOT_SHELL_Z 等比放大（枢轴 = 该侧**脚踝**，含 z）---
            # 🔴 枢轴的 z 必须是**脚踝高度**、不能取地面（第一版取 z=0，实测踩到）：
            #    取地面时脚底那圈只放大 0.1mm ⇒ 壳的底面与原版脚底面**几乎重合**
            #    （实机 z-fighting，射线判据也把它记成"光着"：104/112 个脚底顶点）。
            #    取脚踝后脚底整圈下沉 ~5mm（靴底本来就该比脚厚，肉眼看不出）。
            _keep = [i for i, p in enumerate(_fv) if p.z < FOOT_SHELL_Z]
            _kmap = {old: new for new, old in enumerate(_keep)}
            _drop = Vector((0.0, 0.0, -FOOT_SOLE_OFF))
            _sv, _src_of = [], []
            for _old in _keep:
                _p = _fv[_old]
                _pv = _piv.get("l" if _p.x < 0.0 else "r")
                _sv.append(_p if _pv is None
                           else _pv + (_p - _pv) * FOOT_SCALE + _fn[_old] * FOOT_MARGIN + _drop)
                _src_of.append(_old)
            _sf = [tuple(_kmap[i] for i in f) for f in _ff if all(i in _kmap for i in f)]
            # 🔴 **穿模修正**（第四版，也是最终版）：上面的"沿顶点法线外扩"在**凸脊**上会被折角吃掉
            #    （脚掌前下方那条），所以外扩之后再补一道**按皮肤面反推**的修正：
            #    对每个壳顶点，取它到皮肤的最近点 + 该点法线，若"带符号高度"小于 margin，
            #    就沿法线顶到 margin 处。迭代几轮收敛。
            #    —— 这样"壳在皮肤外侧 ≥ margin"变成**逐点成立**的性质，不靠位移方向的选得对不对。
            #    为什么不是别的写法（四条都试过，别再走回头路）：
            #      · 只按比例缩放：踝部离枢轴才 2~3cm ⇒ 只挤出 1~2mm，几乎贴死；
            #      · 只沿平均法线外扩：凸脊处缩进皮肤（一条红）；
            #      · 逐面外扩：折角处顶点分裂 ⇒ 面之间裂开细缝，皮肤从缝里透出来（满腿红线）；
            #      · 沿"离脚踝的径向"外扩：脚底那片径向几乎与脚底**相切** ⇒ 壳整片往前滑、后缘露皮肤。
            # 🔴 **细分**：壳的面是"低模三角"（源件脚就这密度），在**脚底/脚掌**这种凸面上，
            #    三角形会**弦切**进皮肤里（边越长、凸得越厉害，切得越深）——实测侧视就是脚掌下沿一条红。
            #    先细分一遍（弦长减半 ⇒ 下沉量降到 1/4），再跑穿模修正，壳就贴着皮肤走。
            _bm = bmesh.new()
            _bm.from_mesh(_tmp_me_for_subdiv(_sv, _sf, NAME))
            _bm.faces.ensure_lookup_table()
            # 只细分**朝下的面**（脚底那片才是凸面/弦切的灾区；全身细分会把顶点数翻四倍，没必要）
            _down_faces = [f for f in _bm.faces if max(v.co.z for v in f.verts) < 0.12]
            _down_edges = list({e for f in _down_faces for e in f.edges})
            if _down_edges:
                bmesh.ops.subdivide_edges(_bm, edges=_down_edges, cuts=1, use_grid_fill=True)
            _bm.verts.ensure_lookup_table()
            _sub_v = [v.co.copy() for v in _bm.verts]
            _sub_f = [tuple(v.index for v in f.verts) for f in _bm.faces]
            _bm.free()
            from mathutils.kdtree import KDTree as _KDT
            _old_kd = _KDT(len(_sv))
            for _i, _q in enumerate(_sv):
                _old_kd.insert(_q, _i)
            _old_kd.balance()
            _new_src = [_src_of[_old_kd.find(q)[1]] for q in _sub_v]
            _sv, _sf, _src_of = _sub_v, _sub_f, _new_src
            print("   脚部覆盖：壳细分后 %d 顶点 / %d 面" % (len(_sv), len(_sf)))

            from mathutils.bvhtree import BVHTree as _BVT
            _skin_bvh = _BVT.FromPolygons([tuple(q) for q in _fv], _ff, all_triangles=False)
            _moved = 0
            for _round in range(4):
                _nfix = 0
                for _n2 in range(len(_sv)):
                    _loc, _nor, _fi, _d = _skin_bvh.find_nearest(_sv[_n2])
                    if _loc is None:
                        continue
                    _h = (_sv[_n2] - _loc).dot(_nor)
                    if _h < FOOT_MARGIN:
                        _sv[_n2] = _loc + _nor * FOOT_MARGIN
                        _nfix += 1
                _moved += _nfix
                if _nfix == 0:
                    break
            # 🔴 **鞋底板**：壳在**脚底那片**永远差一点（脚底的法线朝下，而"从脚踝往外"的方向与脚底几乎相切，
            #    任何外扩方向在脚底都会漏一条）。补一块平的鞋底：把**朝下的面**（法线 z<−0.5）整片投到
            #    "壳最低点再低 5mm"的平面上（保留 x/y）⇒ 从正下方看就是一块鞋底。
            #    侧面看不见它 —— 壳的边沿比它外扩 8mm（2% + 6mm），把它整圈挡住。
            _sole_z = min(q.z for q in _sv) - 0.005
            _n_sole = 0
            _plate = {}          # 源顶点号 → 底板顶点号（去重：相邻面共用，别逐面复制）
            for _f in list(_sf):
                _vs = [_sv[i] for i in _f]
                _nf = (_vs[1] - _vs[0]).cross(_vs[2] - _vs[0])
                if _nf.length < 1e-12 or _nf.normalized().z >= -0.5:
                    continue
                _ids = []
                for _i in _f:
                    if _i not in _plate:
                        _plate[_i] = len(_sv)
                        _sv.append(Vector((_sv[_i].x, _sv[_i].y, _sole_z)))
                        _src_of.append(_src_of[_i])
                    _ids.append(_plate[_i])
                _sf.append(tuple(_ids))
                _n_sole += 1
            print("   脚部覆盖：鞋底板 %d 面（z=%.4f）" % (_n_sole, _sole_z))

            _me = bpy.data.meshes.new(NAME + "_footshell")
            _me.from_pydata([tuple(p) for p in _sv], [], _sf)
            _me.update()
            _ob = bpy.data.objects.new(NAME + "_footshell", _me)
            bpy.context.scene.collection.objects.link(_ob)
            if _uvname:
                _nuv = _me.uv_layers.new(name=_uvname)
                for _lp in _me.loops:
                    _hit = _kd.find(_sv[_lp.vertex_index])
                    _src = _boot[_hit[1]] if _hit[1] is not None else None
                    _nuv.data[_lp.index].uv = _boot_uv.get(_src, (0.5, 0.5))
            _vg = {}
            _nvert = 0
            for _new, _old in enumerate(_src_of):
                for _gn, _wv in _fw[_old].items():
                    _bn = _FEET_WMAP.get(_gn)
                    if _bn is None:
                        continue
                    if _bn not in _vg:
                        _vg[_bn] = _ob.vertex_groups.new(name=_bn)
                    _vg[_bn].add([_new], _wv, 'REPLACE')
                    _nvert += 1
            if ARM.data.materials:
                _me.materials.append(ARM.data.materials[0])
            # 并入甲（用 join：材质槽 / UV 层 / 顶点组按名合并）
            bpy.ops.object.select_all(action='DESELECT')
            ARM.select_set(True)
            _ob.select_set(True)
            bpy.context.view_layer.objects.active = ARM
            bpy.ops.object.join()
            ARM.data.update()
            # --- ③ 报告 ---
            # 🔴 这里**不给"罩住率"数字**（试过三条判据都不可信，别再往这加）：
            #    · 最近点法线判号 —— 甲在脚那带有两层几何（外面壳、里面源件小腿件），会误报；
            #    · 沿顶点法线打射线 —— 法线几乎与壳面平行时擦面而过，误报；
            #    · 对壳做奇偶（壳是开口筒、无盖）/ 对原版脚反向映射（原版脚网格自身开口+内壁）—— 都失真。
            #    壳到底罩没罩住，**离线只认渲染**：`Debug/offline/_foot_visual.py`（甲不透明 + 皮肤染红，
            #    红的地方就是会露皮肤的地方）。判据 = 前/后/侧/俯/仰五个角度看**没有红**。
            print("   脚部覆盖：删鞋 %d 顶点 · 补壳 %d 顶点/%d 面（穿模修正 %d 次；原版脚 %s，绕脚踝 ×%.2f + 法线外扩 %.1fmm，包到 z=%.2f）"
                  % (len(_boot), len(_sv), len(_sf), _moved, os.path.basename(FEET_REF),
                     FOOT_SCALE, FOOT_MARGIN * 1000, FOOT_SHELL_Z))
            print("   脚部覆盖：修前「露皮肤」方向 %d（射线体检，偏高）→ 修后见渲染复核 "
                  "_foot_visual.py（五个角度无红 = 通过）" % _n0)

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
        # 🔴 薄片保护（--lod-protect）：权重越高砍得越狠 ⇒ 保护要 invert + 权重 1
        if LODP_V:
            _vg = d.vertex_groups.new(name="__lodp__")
            _vg.add(LODP_V, 1.0, 'REPLACE')
            m.vertex_group = "__lodp__"
            m.invert_vertex_group = True
            m.vertex_group_factor = 1000.0
        bpy.ops.object.modifier_apply(modifier=m.name)
        if LODP_V:
            _vg2 = d.vertex_groups.get("__lodp__")
            if _vg2 is not None:
                d.vertex_groups.remove(_vg2)
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
