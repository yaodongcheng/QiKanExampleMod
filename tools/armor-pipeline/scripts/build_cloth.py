# -*- coding: utf-8 -*-
"""build_cloth.py —— 给一件甲挂上「布料模拟」需要的两个产物（Blender 脚本，一条命令出两样）。

引擎的布料是「映射模式」：另做一张低模（**模拟网格**）算物理，渲染网格按顶点色 alpha 挂上去跟。
本脚本产出这两半：

  1. **甲 FBX 刷顶点色 alpha**（就地改 `out/<甲名>.fbx`）
     披风那些顶点 = 1（挂到模拟网格），其余 = 0（照旧跟骨），最顶上一圈 = 0（钉死在肩上）。
     ⚠️ 每一级 LOD 都要刷（LOD 是 decimate 出来的、顶点号对不上 ⇒ 按**几何**判，不按顶点号）。

  2. **模拟网格 FBX**（新文件 `out/clo_<甲名>.fbx`）
     源件里的 `driver_N`（原厂给这块布配的低模替身）按同一把变换 T 搬过来，
     整体绑一根身体骨，刷上 0（贴肩）→ alpha_max（下摆）的梯度。
     ⚠️ 编辑器硬性要求：模拟网格**只能有一个子网格、不能有 LOD**。

判据（脚本自己会打）：
  · 甲：披风顶点数 / 每级 LOD 各刷了多少
  · 替身：顶点数、单网格、与渲染件的距离（**必须贴着渲染件、且不被它穿出去**）
  · 「哪块是披风」按**碎片**（连通域）判：合并后的甲里，一个碎片必然只来自一个源子网格
    （build_armor.py 自己那条注释），所以碎片级归属是准的。

用法
----
    blender -b -P build_cloth.py -- --key L02_nobunaga --cape-idx 0 --bone bip01_spine1_10

    --driver <名>    指定用哪个 driver 件（默认按包围盒重合度自动配，与 audit_cloth 同口径）
    --alpha-max 0.44 模拟网格下摆的活动半径（米），参照原版袍子 0.44
    --pin-band 0.06  甲上「贴肩钉死」的高度带（米，从披风顶往下量）
    --dry-run        只量不写
"""
import argparse
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector
from mathutils.kdtree import KDTree

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
sys.path.insert(0, os.path.join(REPO, "tools", "sw2-pipeline"))
from src_transform import T_of, load as t_load, lookup as t_lookup   # noqa: E402

SRC_DIR = r"D:\BrainMaker\战国无双2资产解包分析\export\fbx"
OUT_DIR = os.path.join(REPO, "tools", "armor-pipeline", "out")
HEAD_BONE_Z = 1.569
COLOR_LAYER = "COLOR_0"          # 原版资产里这个层就叫这个名字（tpac 里是 COLOR_0 顶点流）


# --------------------------------------------------------------------------- 参数
def args_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


A = args_after_ddash()


def get(args, key, default=None):
    if key in args:
        i = args.index(key)
        if i + 1 < len(args) and not args[i + 1].startswith("--"):
            return args[i + 1]
    return default


KEY = get(A, "--key")
if not KEY:
    print("!! 必须给 --key（如 L02_nobunaga）"); sys.exit(2)
CAPE_IDX = int(get(A, "--cape-idx", "0"))
BONE = get(A, "--bone")
NAME = get(A, "--name", "taikou_%s_do_a" % KEY.split("_", 1)[1])
CLO_NAME = "clo_" + NAME
T_S = float(get(A, "--t-s", "0"))
T_ZSOLE = float(get(A, "--t-z-sole", "0"))
ALPHA_MAX = float(get(A, "--alpha-max", "0.44"))
PIN_BAND = float(get(A, "--pin-band", "0.06"))
DRIVER = get(A, "--driver")
DO_WRITE = "--dry-run" not in A
ARMOR_FBX = os.path.join(OUT_DIR, NAME + ".fbx")
CLO_FBX = os.path.join(OUT_DIR, CLO_NAME + ".fbx")
SKEL = None
for c in (os.path.join(os.environ.get("MB2_PATH") or "", "modding_resources", "skeletons", "human_skeleton.fbx"),
          r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\modding_resources\skeletons\human_skeleton.fbx"):
    if c and os.path.isfile(c):
        SKEL = c
        break
if not T_S:
    tab = t_load(os.path.join(REPO, "tools", "sw2-pipeline", "out", "srcT.json"))
    row = t_lookup(tab, KEY)
    T_S = float(row["s"])
    T_ZSOLE = float(row.get("z_sole") or 0.0)
T_FN = T_of(T_S, T_ZSOLE)

print("== 布料构建 %s ==" % KEY)
print("   甲 FBX   %s" % ARMOR_FBX)
print("   替身 FBX %s" % CLO_FBX)
print("   变换 T   s=%.9f  z_sole=%.6f" % (T_S, T_ZSOLE))
print("   目标骨   %s      活动半径 %.3f m     贴肩带宽 %.3f m" % (BONE, ALPHA_MAX, PIN_BAND))


# --------------------------------------------------------------------------- 工具
def parse_submesh(name):
    """严格取【子网格号】：`model_0_submesh_<n>_noesis_meshnode_<m>`（`.001` 那种带后缀的不算）。"""
    import re
    m = re.match(r"^model_0_submesh_(\d+)_noesis_meshnode_\d+$", name)
    return int(m.group(1)) if m else None


def import_fbx(path, scale=1.0):
    bpy.ops.import_scene.fbx(filepath=path, global_scale=scale)
    return [o for o in bpy.context.selected_objects] or list(bpy.data.objects)


def bake_object_transform(o, arm):
    """把对象变换烘进顶点（口径同 build_armor.py）。战无2 的源件对象是单位阵 ⇒ 一行不动。"""
    mw = o.matrix_world.copy()
    o.parent = None
    tgt = (arm.matrix_world.inverted() @ mw) if arm is not None else mw
    ident = Matrix.Identity(4)
    if max(abs(tgt[r][c] - ident[r][c]) for r in range(4) for c in range(4)) > 1e-6:
        o.data.transform(tgt)
        print("   烘对象变换进顶点：%s" % o.name)
    o.matrix_world = Matrix.Identity(4)


def kd_of(points):
    kd = KDTree(len(points))
    for i, p in enumerate(points):
        kd.insert(p, i)
    kd.balance()
    return kd


def fragments(me):
    """连通域（并查集）—— 与 build_armor.py 的 frag_dominant_verts 同一套判据。"""
    n = len(me.vertices)
    par = list(range(n))

    def find(a):
        while par[a] != a:
            par[a] = par[par[a]]
            a = par[a]
        return a

    for e in me.edges:
        ra, rb = find(e.vertices[0]), find(e.vertices[1])
        if ra != rb:
            par[rb] = ra
    comp = {}
    for i in range(n):
        comp.setdefault(find(i), []).append(i)
    return list(comp.values())


def ensure_color(me):
    ca = me.color_attributes.get(COLOR_LAYER)
    if ca is None:
        ca = me.color_attributes.new(name=COLOR_LAYER, type='BYTE_COLOR', domain='CORNER')
    me.color_attributes.active_color = ca        # 导出只带「活动」那一层
    return ca


def set_alpha(me, alpha_by_vert):
    """逐顶点写 alpha（RGB 全白）—— CORNER 域：同一顶点的所有 loop 写同一个值。"""
    ca = ensure_color(me)
    for loop in me.loops:
        a = alpha_by_vert.get(loop.vertex_index, 0.0)
        ca.data[loop.index].color = (1.0, 1.0, 1.0, a)


def bbox(pts):
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    return (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs))


def purge_non_scene():
    """清掉「不在场景里、但还在 bpy.data 里」的对象。

    导出用 `use_selection=False`（打场景），所以正常不会有东西漏出去；本函数的用途是
    **让"场景里有什么 = FBX 里有什么"这条判据成立** —— 排查时不用再猜"这个件是模型的还是脚本的"。
    （踩过的坑不在 FBX 侧：拿 `--python-expr` 直接 import 而不先 read_factory_settings，
    探针自己的启动 Cube/Camera/Light 会混进打印结果，看着像产物里多了东西。）
    """
    scene_names = {o.name for o in bpy.context.scene.objects}
    for o in list(bpy.data.objects):
        if o.name not in scene_names:
            bpy.data.objects.remove(o, do_unlink=True)


def surface_samples(ob, subdiv=8):
    """在网格的三角形上撒样本点（世界坐标）—— 量「点到面」的距离比点到点准得多：
    低模替身只有 30 顶点，点到点会把「面中间离得远」误报成「贴不紧」。"""
    me = ob.data
    me.calc_loop_triangles()
    pts = []
    for tri in me.loop_triangles:
        a, b, c = (ob.matrix_world @ me.vertices[i].co for i in tri.vertices)
        for i in range(subdiv + 1):
            for j in range(subdiv + 1 - i):
                u, v = i / subdiv, j / subdiv
                pts.append(a * (1 - u - v) + b * u + c * v)
    return pts


# --------------------------------------------------------------------------- 1) 源件：披风点云 + 挑替身
bpy.ops.wm.read_factory_settings(use_empty=True)
print("== 1/4 读源件，取披风点云 ==")
objs = import_fbx(os.path.join(SRC_DIR, KEY + ".fbx"))
SW_ARM = next((o for o in objs if o.type == 'ARMATURE'), None)
meshes = [o for o in objs if o.type == 'MESH']
for o in meshes:                 # 本段场景即用即弃：先全搬进骑砍空间，之后一律按面/点直接量
    bake_object_transform(o, SW_ARM)
    for v in o.data.vertices:
        v.co = Vector(T_FN(v.co))
cape_objs = [o for o in meshes if parse_submesh(o.name) == CAPE_IDX]
if not cape_objs:
    print("!! 源件里找不到子网格 %d" % CAPE_IDX); sys.exit(3)
cape_pts = [v.co.copy() for o in cape_objs for v in o.data.vertices]
cx0, cx1, cy0, cy1, cz0, cz1 = bbox(cape_pts)
print("   披风 %d 件 / %d 顶点  x[%+.3f,%+.3f] y[%+.3f,%+.3f] z[%+.3f,%+.3f]"
      % (len(cape_objs), len(cape_pts), cx0, cx1, cy0, cy1, cz0, cz1))

drivers = [o for o in meshes if o.name.startswith("driver_")]
if not drivers:
    print("!! 源件里没有 driver 件（这个角色没现成替身，要自己降面）"); sys.exit(3)


def overlap_ratio(pts, bb):
    """包围盒重合度（交集体积 ÷ 本人体积），与 audit_cloth.py 同口径。"""
    x0, x1, y0, y1, z0, z1 = bbox(pts)
    ix = max(0.0, min(x1, bb[1]) - max(x0, bb[0]))
    iy = max(0.0, min(y1, bb[3]) - max(y0, bb[2]))
    iz = max(0.0, min(z1, bb[5]) - max(z0, bb[4]))
    vol = max(1e-9, (x1 - x0) * (y1 - y0) * (z1 - z0))
    return (ix * iy * iz) / vol


cape_bb = (cx0, cx1, cy0, cy1, cz0, cz1)
if DRIVER:
    drv = next((o for o in drivers if o.name.startswith(DRIVER)), None)
    if drv is None:
        print("!! 指定的 --driver %s 不存在" % DRIVER); sys.exit(3)
else:
    scored = []
    for o in drivers:
        pts = [v.co.copy() for v in o.data.vertices]
        scored.append((overlap_ratio(pts, cape_bb), o, pts))
    scored.sort(key=lambda t: -t[0])
    print("   替身候选：" + " · ".join("%s 重合%.0f%%(v=%d)" % (o.name.split("_noesis")[0], r * 100, len(p))
                                       for r, o, p in scored))
    if scored[0][0] < 0.5:
        print("!! 最好的替身重合度只有 %.0f%%，不像这块布的替身 —— 停下来人工确认" % (scored[0][0] * 100))
        sys.exit(3)
    _, drv, drv_pts = scored[0]

DRV_BASE = drv.name.split("_noesis")[0]
print("   替身 %s：%d 顶点 / %d 面  重合 %.0f%%"
      % (DRV_BASE, len(drv_pts), len(drv.data.polygons),
         overlap_ratio(drv_pts, cape_bb) * 100))

# 替身必须贴着渲染件（编辑器文档：贴不紧会出现"腿穿甲"式碰撞错位）
# 🔴 判据用**点到面**：替身只有 30 顶点，点到点会把"面中间离得远"误报成"贴不紧"。
def surf_samples_of(objs_):
    pts = []
    for o in objs_:
        pts += surface_samples(o)
    return pts


kd_srf = kd_of(surf_samples_of([drv]))
kd_render = kd_of(surf_samples_of(cape_objs))

d_cape2sim = sorted(kd_srf.find(p)[2] for p in cape_pts)
n = len(d_cape2sim)
print("   渲染件→替身面 距离：中位 %.1f mm / p90 %.1f mm / 最大 %.1f mm"
      % (d_cape2sim[n // 2] * 1000, d_cape2sim[int(n * 0.9)] * 1000, d_cape2sim[-1] * 1000))

d_sim2cape = sorted(kd_render.find(p)[2] for p in surf_samples_of([drv]))
m = len(d_sim2cape)
print("   替身→渲染件面 距离：中位 %.1f mm / p90 %.1f mm / 最大 %.1f mm"
      % (d_sim2cape[m // 2] * 1000, d_sim2cape[int(m * 0.9)] * 1000, d_sim2cape[-1] * 1000))
# 只报不拦：原版那对也是低模替身，面中间允许有偏差；超 30mm 提示先看渲染图再决定
if d_cape2sim[-1] > 0.03 or d_sim2cape[-1] > 0.03:
    print("   ⚠️ 替身与渲染件最大间距 > 30mm —— 先渲图看是哪儿疏（可能是肩头那两片小片替身没盖到）")


# --------------------------------------------------------------------------- 2) 造替身 FBX
print("== 2/4 造模拟网格 ==")
bpy.ops.wm.read_factory_settings(use_empty=True)
tmp = import_fbx(SKEL)
_tmp_arm = next(o for o in tmp if o.type == 'ARMATURE')
_hb = next(b for b in _tmp_arm.data.bones if "head_13" in b.name)
_z_raw = (_tmp_arm.matrix_world @ _hb.head_local).z
bpy.ops.wm.read_factory_settings(use_empty=True)
objs = import_fbx(SKEL, HEAD_BONE_Z / _z_raw)
BL = next(o for o in objs if o.type == 'ARMATURE')
for o in list(objs):
    if o.type != 'ARMATURE':
        bpy.data.objects.remove(o, do_unlink=True)
_hz = (BL.matrix_world @ BL.data.bones["bip01_head_13"].head_local).z
if abs(_hz - HEAD_BONE_Z) > 0.01:
    print("!! 骨架校准失败 head z=%.4f" % _hz); sys.exit(3)
print("   骨架校准 OK  head_13 z=%.5f" % _hz)

objs = import_fbx(os.path.join(SRC_DIR, KEY + ".fbx"))
SW_ARM = next((o for o in objs if o.type == 'ARMATURE'), None)
drv = next((o for o in objs if o.type == 'MESH' and o.name.split("_noesis")[0] == DRV_BASE), None)
if drv is None:
    print("!! 第二遍导入找不到替身件 %s" % DRV_BASE); sys.exit(3)
bake_object_transform(drv, SW_ARM)
for v in drv.data.vertices:
    v.co = Vector(T_FN(v.co))
drv.matrix_world = Matrix.Identity(4)
# 只留这一件、清掉源骨架带来的顶点组/材质
drv.vertex_groups.clear()
drv.data.materials.clear()
if BONE not in {b.name for b in BL.data.bones}:
    print("!! 骑砍骨架里没有骨 %s" % BONE); sys.exit(3)
vg = drv.vertex_groups.new(name=BONE)
vg.add(list(range(len(drv.data.vertices))), 1.0, 'REPLACE')
drv.parent = BL
_m = drv.modifiers.new("Armature", 'ARMATURE')
_m.object = BL

# alpha 梯度：贴肩 0 → 下摆 ALPHA_MAX（照原版袍子 clo_aserai_robe_c 0.44 → 0）
zs = [v.co.z for v in drv.data.vertices]
z_hi, z_lo = max(zs), min(zs)
span = max(1e-6, z_hi - z_lo)
alpha_drv = {}
for v in drv.data.vertices:
    t = (z_hi - v.co.z) / span                      # 顶部 0 → 底部 1
    alpha_drv[v.index] = round(ALPHA_MAX * t, 4)
set_alpha(drv.data, alpha_drv)
drv.name = CLO_NAME
drv.data.name = CLO_NAME
_mat = bpy.data.materials.new(NAME)                 # 🔴 复用渲染件的材质名（原版也这么做：
_mat.use_nodes = True                               #    模拟网格引用渲染件的材质，不新建）
_nt = _mat.node_tree
_nt.nodes.clear()
_out = _nt.nodes.new('ShaderNodeOutputMaterial')
_bsdf = _nt.nodes.new('ShaderNodeBsdfPrincipled')
_nt.links.new(_bsdf.outputs['BSDF'], _out.inputs['Surface'])
drv.data.materials.append(_mat)
_bins = {}
for a in alpha_drv.values():
    _bins[round(a, 2)] = _bins.get(round(a, 2), 0) + 1
print("   替身 alpha 分布（值:顶点数）: %s" % dict(sorted(_bins.items())))
print("   材质名 -> %s（复用渲染件的材质资产）" % NAME)

if DO_WRITE:
    keep = {drv, BL}
    for o in list(bpy.data.objects):
        if o not in keep:
            bpy.data.objects.remove(o, do_unlink=True)
    purge_non_scene()
    print("   导出前场景：%s" % sorted(o.name for o in bpy.context.scene.objects))
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.ops.export_scene.fbx(
        filepath=CLO_FBX, use_selection=False, object_types={'ARMATURE', 'MESH'},
        global_scale=1.0, apply_unit_scale=False, apply_scale_options='FBX_SCALE_UNITS',
        bake_space_transform=False, use_mesh_modifiers=False, add_leaf_bones=False,
        primary_bone_axis='Y', secondary_bone_axis='X', axis_forward='Y', axis_up='Z',
        bake_anim=False, path_mode='COPY', embed_textures=False, use_custom_props=False,
        colors_type='SRGB', prioritize_active_color=True)
    print("   导出 -> %s" % CLO_FBX)


# --------------------------------------------------------------------------- 3) 给甲刷 alpha
print("== 3/4 给甲刷顶点色 alpha ==")
bpy.ops.wm.read_factory_settings(use_empty=True)
objs = import_fbx(ARMOR_FBX)
AR = next((o for o in objs if o.type == 'ARMATURE'), None)
arm_meshes = [o for o in objs if o.type == 'MESH']
if not arm_meshes:
    print("!! 甲 FBX 里没有网格"); sys.exit(3)
kd_cape = kd_of(cape_pts)
EPS = 0.004                      # 4mm：碎片判定的归属阈值
pin_z = cz1 - PIN_BAND           # 贴肩带：z ≥ 这条线的一律钉死
report = []
for me_ob in sorted(arm_meshes, key=lambda o: o.name):
    me = me_ob.data
    verts = [me_ob.matrix_world @ v.co for v in me.vertices]
    near = [kd_cape.find(p)[2] < EPS for p in verts]
    alpha = {}
    for frag in fragments(me):
        hit = sum(1 for i in frag if near[i])
        is_cape = hit >= 0.9 * len(frag)          # 碎片级投票：合并后一个碎片只来自一个源子网格
        if is_cape:
            for i in frag:
                p = verts[i]
                alpha[i] = 0.0 if p.z >= pin_z else 1.0
    set_alpha(me, alpha)
    n1 = sum(1 for a in alpha.values() if a > 0)
    n0 = sum(1 for a in alpha.values() if a <= 0)
    report.append((me_ob.name, len(me.vertices), len(alpha), n1, n0))
    print("   %-28s v=%-5d 披风顶点=%-4d（自由 %d / 钉死 %d）" % (me_ob.name, len(me.vertices), len(alpha), n1, n0))
if not any(r[2] for r in report):
    print("!! 一级 LOD 都没认出披风 —— 停下（阈值/子网格号可能不对）"); sys.exit(3)

if DO_WRITE:
    purge_non_scene()
    print("   导出前场景：%s" % sorted(o.name for o in bpy.context.scene.objects))
    bpy.ops.export_scene.fbx(
        filepath=ARMOR_FBX, use_selection=False, object_types={'ARMATURE', 'MESH'},
        global_scale=1.0, apply_unit_scale=False, apply_scale_options='FBX_SCALE_UNITS',
        bake_space_transform=False, use_mesh_modifiers=False, add_leaf_bones=False,
        primary_bone_axis='Y', secondary_bone_axis='X', axis_forward='Y', axis_up='Z',
        bake_anim=False, path_mode='COPY', embed_textures=False, use_custom_props=False,
        colors_type='SRGB', prioritize_active_color=True)
    print("   写回 -> %s" % ARMOR_FBX)


# --------------------------------------------------------------------------- 4) 收工摘要
print("== 4/4 摘要 ==")
state = {"key": KEY, "armor": os.path.basename(ARMOR_FBX), "clo": os.path.basename(CLO_FBX),
         "cape_idx": CAPE_IDX, "bone": BONE, "alpha_max": ALPHA_MAX, "pin_band": PIN_BAND,
         "driver_verts": len(drv_pts), "cape_verts": len(cape_pts), "written": DO_WRITE}
print(json.dumps(state, ensure_ascii=False))
if DO_WRITE:
    with open(os.path.join(OUT_DIR, "_cloth_state.json"), "w", encoding="utf-8") as f:
        json.dump(state, f, ensure_ascii=False, indent=1)
print("""
下一步（人做）：
  1. ModKit 开着时（关着的时候拷 = 没拷）：
     · 甲（**已注册**资产）→ 覆盖镜像 TifaHead2/AssetSources/armor/<角色>/%s
     · 替身（**新资产**，从没导入过）→ 🔴 拷进镜像**没用**：编辑器不会因为镜像多一个文件
       就注册新资产。必须**在 ModKit 里手工导入**，文件放**管线落点目录**
       （SW2 的落点 = AssetSources/sw2/<角色键>/，把 out/ 这份拷进去即可）。
       判据：`TifaHead2/Assets/<类>/<名>/` 里出现 `clo_*_geo.tpac`（没有 = 还没注册）。
  2. 先验 alpha 再往下：tpaccli clothinfo --packdir <TifaHead2/Assets 下该资产的编译产物目录> --filter %s
     看是不是「甲身 0、披风 N 个 1」。
  3. 编辑器 Cloth Editor：Preview mesh 选甲 → Simulation mesh 选 %s
     → 碰撞体选 cape_body → 布料材质 → Save mesh settings → cook → Publish
""" % (os.path.basename(ARMOR_FBX), NAME, CLO_NAME))
