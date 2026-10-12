# -*- coding: utf-8 -*-
"""build_huntianling.py —— 造「混天绫」v2：**原版围巾的围脖 + 自造的长飘带**。

【为什么改做法（2026-10-10 用户实机反馈）】
    v1（`build_ribbon.py`）是一整条光板带子 ⇒ 用户原话「根本不像围巾」。
    改成：**拿原版那件围巾的围脖当上半身，接一条 3 米飘带**。

【基础件 = 原版 `battania_scarf_a`（物品 "Heavy Tartan Scarf"）】
    选它而不是别的围巾，因为它是原版**唯一一件布料全线接通的围巾**（实测 `tpaccli clothinfo`）：
        ClothMetamesh = clo_battania_scarf_a      有低模替身
        UnknownString = "cape_body"               碰撞体
        meshFlags     = [uses_cloth_simulation]   布料开着
    其余候选（`sturgia_cape_a` / `female_peasant_cape_b`）都是裸网格。

【坐标口径（**世界坐标 == 游戏坐标**）】+z 上 · +x 右 · 正面 = +y · 背面 = -y。
    原版围巾实测：x[-0.30,+0.30] y[-0.19,+0.18] z[+1.19,+1.58]
      ⇒ z≥1.38 那圈是**绕颈围脖**；z<1.35 那一小条是**右肩垂尾**（本脚本把它换成长飘带）。
    🔴 `wm.obj_import` **必须显式给 `forward_axis='Y', up_axis='Z'`**：
      默认是 `-Z/Y`，会把 Z-up 的 dump 转一道 ⇒ 围巾"飘到身前 1.4 m"的假象（已踩过）。

跑法：
  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python \
      tools/armor-pipeline/scripts/build_huntianling.py -- [--dry-run] [选项]
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))

HEAD_BONE_Z = 1.569
COLOR_LAYER = "COLOR_0"

D = {
    "name": "lwn_huntian_ling",
    "collar-obj": os.path.join(REPO, "Debug", "offline", "_scarf_ref", "battania", "battania_scarf_a.obj"),
    "collar-cut": 1.375,     # 只保留 z ≥ 这条线的部分（= 围脖那圈）
    "collar-keep": 0.50,     # 围脖抽稀比例（原版 LOD0 2319 顶点；0.30 会看出棱角，0.50 够滑）
    "top-x": 0.16,           # 飘带顶端 x（贴着围脖垂尾原来那一侧）
    "top-y": 0.06,
    "top-z": 1.38,
    "length": 3.0,
    "seg-len": 60,
    "seg-w": 4,
    "w-root": 0.13,
    "w-tip": 0.30,
    "a0": 25.0,
    "a1": 162.0,
    "wave": 0.10,
    "wave-turns": 1.5,
    "twist": 80.0,
    "bone": "bip01_spine2_11",
    "uv-v-repeat": 6.0,
    "pin-band": 0.14,
    "alpha-max": 0.75,
    # ---- 替身（映射模式）参数 ----
    "sim-rows": 17,          # 沿长度采几行（渲染件是 61 站，这里稀疏采样）
    "sim-cols": 5,           # 横向几列
    "sim-shrink": 0.82,      # 横向缩到渲染件的几成（**必须 <1**：替身要被渲染件裹住，不能穿出去）
    "sim-alpha-max": 0.55,   # 末端活动半径（米）——原版围脖 0.18 / 原版袍子 0.44，我们飘带长，给 0.55
}


def args_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def sim_centerline(top_x, top_y, top_z, a0, a1, length, seg):
    """飘带中心线（按切线角积分）。a 从「竖直向下」往「背后(-y)」量。"""
    pts = [Vector((top_x, top_y, top_z))]
    ds = length / seg
    r0, r1 = math.radians(a0), math.radians(a1)
    for i in range(seg):
        am = r0 + (r1 - r0) * ((i + 0.5) / seg)
        pts.append(pts[-1] + Vector((0.0, -math.sin(am), -math.cos(am))) * ds)
    return pts


def sim_wdir(tan, t, twist_deg):
    """该站的「宽度方向」——以世界 +x 为基准、绕切线扭转 `twist*t` 度。"""
    ex = Vector((1.0, 0.0, 0.0))
    n = tan.cross(ex).normalized()
    tau = math.radians(twist_deg * t)
    return (ex * math.cos(tau) + n * math.sin(tau)).normalized()


A = args_after_ddash()


def get(k, d=None):
    if k in A:
        i = A.index(k)
        if i + 1 < len(A) and not A[i + 1].startswith("--"):
            return A[i + 1]
    return d


NAME = get("--name", D["name"])
COLLAR = get("--collar-obj", D["collar-obj"])
CUT = float(get("--collar-cut", D["collar-cut"]))
KEEP = float(get("--collar-keep", D["collar-keep"]))
TOP_X = float(get("--top-x", D["top-x"]))
TOP_Y = float(get("--top-y", D["top-y"]))
TOP_Z = float(get("--top-z", D["top-z"]))
LENGTH = float(get("--length", D["length"]))
SEG_L = int(get("--seg-len", D["seg-len"]))
SEG_W = int(get("--seg-w", D["seg-w"]))
W_ROOT = float(get("--w-root", D["w-root"]))
W_TIP = float(get("--w-tip", D["w-tip"]))
A0 = float(get("--a0", D["a0"]))
A1 = float(get("--a1", D["a1"]))
WAVE = float(get("--wave", D["wave"]))
WAVE_TURNS = float(get("--wave-turns", D["wave-turns"]))
TWIST = float(get("--twist", D["twist"]))
BONE = get("--bone", D["bone"])
UV_REP = float(get("--uv-v-repeat", D["uv-v-repeat"]))
PIN_BAND = float(get("--pin-band", D["pin-band"]))
ALPHA_MAX = float(get("--alpha-max", D["alpha-max"]))
# ---- `--sim`：造**替身**（映射模式的低模模拟网格），不造渲染件 ----
SIM = "--sim" in A
SIM_ROWS = int(get("--sim-rows", D["sim-rows"]))
SIM_COLS = int(get("--sim-cols", D["sim-cols"]))
SIM_SHRINK = float(get("--sim-shrink", D["sim-shrink"]))
SIM_ALPHA = float(get("--sim-alpha-max", D["sim-alpha-max"]))
SIM_NAME = "clo_" + NAME
TILE_M = LENGTH / UV_REP          # UV 一格覆盖多长（贴图密度，与 build_ribbon_textures 对齐）
# 🔴 待导入落点**必须解析两态名**（AssetSources / AssetSources_disabled）——
#    直接拼 "AssetSources" 会静默建出一个目录，让 to_editor_mode.bat 罢工（2026-10-12 事故）。
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from imort_dir import imort_subdir, ensure_subdir   # noqa: E402
_MODULE_ROOT = os.path.abspath(os.path.join(REPO, "..", "LwnAnim"))
try:
    OUT_DIR = imort_subdir(_MODULE_ROOT, "HuntianLing", get("--out"))
except Exception as _e:
    print("!! 落点解析失败：" + chr(10) + str(_e))
    sys.exit(2)
DO_WRITE = "--dry-run" not in A

SKEL = None
for c in (os.path.join(os.environ.get("MB2_PATH") or "", "modding_resources", "skeletons", "human_skeleton.fbx"),
          r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\modding_resources\skeletons\human_skeleton.fbx"):
    if c and os.path.isfile(c):
        SKEL = c
        break
if not SKEL or not os.path.isfile(COLLAR):
    print("!! 缺骨架或围脖源件：\n   skel=%s\n   collar=%s" % (SKEL, COLLAR)); sys.exit(2)

print("== 混天绫 v2 构建 %s ==" % NAME)
print("   围脖源件 %s  (保留 z>=%.3f，抽稀 %.0f%%)" % (os.path.basename(COLLAR), CUT, KEEP * 100))
print("   飘带 长 %.2f m / 宽 %.2f→%.2f / 段 %d×%d / 顶端 (%.2f,%.2f,%.2f)"
      % (LENGTH, W_ROOT, W_TIP, SEG_L, SEG_W, TOP_X, TOP_Y, TOP_Z))
print("   绑骨 %s   产物 %s" % (BONE, os.path.join(OUT_DIR, NAME + ".fbx")))

# --------------------------------------------------------------------------- 工具
def import_fbx(path, scale=1.0):
    bpy.ops.import_scene.fbx(filepath=path, global_scale=scale)
    return list(bpy.context.selected_objects) or list(bpy.data.objects)


def ensure_color(me):
    ca = me.color_attributes.get(COLOR_LAYER)
    if ca is None:
        ca = me.color_attributes.new(name=COLOR_LAYER, type='BYTE_COLOR', domain='CORNER')
    me.color_attributes.active_color = ca
    return ca


def set_alpha(me, alpha_by_vert, verts=None):
    ca = ensure_color(me)
    for loop in me.loops:
        a = alpha_by_vert.get(loop.vertex_index, 0.0)
        ca.data[loop.index].color = (1.0, 1.0, 1.0, a)


def make_double_sided(me, only_from=None):
    """【薄片复制一份并翻面】—— 引擎材质层**没有**双面开关（21 个 flag 已反编译核对），
    单面板从背面看就是"没有"。做法与 `build_armor.py:make_sheets_double_sided` 同源。

    🔴 本件**不能用"边界边比 ≥0.5"那套判据**（网格状薄片只有 0.24，会漏判 ——
       与长政金前立同一个坑）。这里改成**显式指定**：`only_from` = 从这个面号起才是薄片
       （= 飘带那一段）。围脖是**壳**，复制了会自己跟自己 z-fighting。
    """
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    lo = 0 if only_from is None else max(0, int(only_from))
    dup = [f for f in bm.faces if f.index >= lo]
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


# --------------------------------------------------------------------------- 1) 骨架
print("\n== 1/5 读骨架 ==")
bpy.ops.wm.read_factory_settings(use_empty=True)
_t = import_fbx(SKEL)
_a = next(o for o in _t if o.type == 'ARMATURE')
_h = next(b for b in _a.data.bones if "head_13" in b.name)
z_raw = (_a.matrix_world @ _h.head_local).z
bpy.ops.wm.read_factory_settings(use_empty=True)
objs = import_fbx(SKEL, HEAD_BONE_Z / z_raw)
ARM = next(o for o in objs if o.type == 'ARMATURE')
for o in list(objs):
    if o.type != 'ARMATURE':
        bpy.data.objects.remove(o, do_unlink=True)
_hz = (ARM.matrix_world @ ARM.data.bones["bip01_head_13"].head_local).z
if abs(_hz - HEAD_BONE_Z) > 0.01:
    print("!! 骨架校准失败 head z=%.4f" % _hz); sys.exit(3)
if BONE not in {b.name for b in ARM.data.bones}:
    print("!! 骨架里没有骨 %s" % BONE); sys.exit(3)
_canary = next((ARM.matrix_world @ b.head_local for b in ARM.data.bones
                if b.name.startswith("bip01_l_foot")), None)
if _canary is None or _canary.y > 0:
    print("!! 朝向金丝雀失败：左脚踝 y=%s（应为负 ≈ -0.076）" % _canary)
    sys.exit(3)
print("   校准 OK  head_13 z=%.5f   朝向金丝雀 OK（左脚踝 y=%+.4f）" % (_hz, _canary.y))

# --------------------------------------------------------------------------- 2') 替身模式（映射模式的低模）
if SIM:
    print("\n== [替身模式] 造 %s ==" % SIM_NAME)
    # 🔴 **必须复用渲染件那条中心线**（同 SEG_L 积分），只在采样密度上稀疏 ——
    #    若替身自己用别的段数积分，弧长不同 ⇒ 两者位置对不上。
    _CL = sim_centerline(TOP_X, TOP_Y, TOP_Z, A0, A1, LENGTH, SEG_L)
    if SIM_ROWS < 2 or SIM_COLS < 2:
        print("!! --sim-rows / --sim-cols 至少 2"); sys.exit(3)
    idx = [int(round(k * SEG_L / float(SIM_ROWS - 1))) for k in range(SIM_ROWS)]

    sverts, sfaces = [], []
    for i in idx:
        t = float(i) / SEG_L
        if i == 0:
            tan = (_CL[1] - _CL[0]).normalized()
        elif i == SEG_L:
            tan = (_CL[-1] - _CL[-2]).normalized()
        else:
            tan = (_CL[i + 1] - _CL[i - 1]).normalized()
        w = sim_wdir(tan, t, TWIST)
        half = (W_ROOT + (W_TIP - W_ROOT) * t) * 0.5 * SIM_SHRINK
        wave_x = WAVE * math.sin(2.0 * math.pi * WAVE_TURNS * t)
        for j in range(SIM_COLS):
            u = float(j) / (SIM_COLS - 1) - 0.5
            q = _CL[i] + w * (u * half * 2.0)
            sverts.append(Vector((q.x + wave_x, q.y, q.z)))
    for r in range(SIM_ROWS - 1):
        for j in range(SIM_COLS - 1):
            a = r * SIM_COLS + j
            b = (r + 1) * SIM_COLS + j
            sfaces.append((a, b, b + 1, a + 1))

    xs = [p.x for p in sverts]; ys = [p.y for p in sverts]; zs = [p.z for p in sverts]
    print("   顶点 %d / 面 %d   单子网格、无 LOD（编辑器硬性要求）" % (len(sverts), len(sfaces)))
    print("   x[%+.3f,%+.3f] y[%+.3f,%+.3f] z[%+.3f,%+.3f]  横向缩到渲染件的 %.0f%%"
          % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs), SIM_SHRINK * 100))

    _me = bpy.data.meshes.new(SIM_NAME)
    _M_INV = ARM.matrix_world.inverted()
    _loc = [_M_INV @ v for v in sverts]
    _me.from_pydata([tuple(v) for v in _loc], [], sfaces)
    _me.update()
    _rt = [ARM.matrix_world @ v for v in _loc]
    _d = max((_rt[k] - sverts[k]).length for k in range(len(sverts)))
    print("   局部↔世界 往返自证：最大偏差 %.6f m  %s" % (_d, "OK" if _d < 1e-3 else "🔴"))

    # 🔴 **必须补 UV 层**（源件 driver 无 UV 导致编辑器选 Simulation mesh **直接崩**，工程踩过）
    _uv = _me.uv_layers.new(name="UVMap")
    for poly in _me.polygons:
        for k in range(poly.loop_total):
            li = poly.loop_start + k
            vi = _me.loops[li].vertex_index
            r, j = divmod(vi, SIM_COLS)
            _uv.data[li].uv = (float(j) / (SIM_COLS - 1), (float(idx[r]) / SEG_L) * UV_REP)

    # 顶点色 alpha = **活动半径**（米）：顶行钉死 0 → 末端 SIM_ALPHA
    _alpha = {}
    for r in range(SIM_ROWS):
        u = float(r) / (SIM_ROWS - 1)
        a = SIM_ALPHA * (u * u * (3.0 - 2.0 * u))
        for j in range(SIM_COLS):
            _alpha[r * SIM_COLS + j] = round(a, 4)
    _ca = _me.color_attributes.new(name=COLOR_LAYER, type='BYTE_COLOR', domain='CORNER')
    _me.color_attributes.active_color = _ca
    for loop in _me.loops:
        _ca.data[loop.index].color = (1.0, 1.0, 1.0, _alpha.get(loop.vertex_index, 0.0))

    # 🔴 材质**复用渲染件那个名字**（原版也这么做：模拟网格引用渲染件的材质资产，不新建）
    _mat = bpy.data.materials.new(NAME)
    _mat.use_nodes = True
    _nt = _mat.node_tree
    _nt.nodes.clear()
    _o = _nt.nodes.new('ShaderNodeOutputMaterial')
    _b = _nt.nodes.new('ShaderNodeBsdfPrincipled')
    _nt.links.new(_b.outputs['BSDF'], _o.inputs['Surface'])
    _me.materials.append(_mat)
    print("   材质名 -> %s（复用渲染件的材质资产，不新建）" % NAME)

    _ob = bpy.data.objects.new(SIM_NAME, _me)
    bpy.context.collection.objects.link(_ob)
    _ob.parent = ARM
    _ob.modifiers.new("Armature", 'ARMATURE').object = ARM
    _vg = _ob.vertex_groups.new(name=BONE)
    _vg.add(list(range(len(_me.vertices))), 1.0, 'REPLACE')
    print("   绑骨 %s 权重 1.0" % BONE)

    # ---- 与渲染件的距离（编辑器要求：贴着、且**永远不被渲染件穿出去**）----
    _render = os.path.join(OUT_DIR, NAME + ".fbx")
    if os.path.isfile(_render):
        _b4 = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=_render)
        _rv = []
        for _o2 in [x for x in bpy.data.objects if x not in _b4 and x.type == 'MESH']:
            _rv += [_o2.matrix_world @ v.co for v in _o2.data.vertices]
        if _rv:
            from mathutils.kdtree import KDTree
            _kd = KDTree(len(_rv))
            for _i, _p in enumerate(_rv):
                _kd.insert(_p, _i)
            _kd.balance()
            _ds = sorted(_kd.find(p)[2] for p in sverts)
            print("   替身->渲染件 最近点距离：中位 %.1f mm / p90 %.1f mm / 最大 %.1f mm"
                  % (_ds[len(_ds) // 2] * 1000, _ds[int(len(_ds) * 0.9)] * 1000, _ds[-1] * 1000))
            print("     （替身在渲染件**内侧**，所以有正距离是正常的；要看的是别处太大）")

    print("\n== 导出 ==")
    if DO_WRITE:
        os.makedirs(OUT_DIR, exist_ok=True)
        for o in list(bpy.data.objects):
            if o not in (_ob, ARM):
                bpy.data.objects.remove(o, do_unlink=True)
        bpy.context.scene.unit_settings.scale_length = 1.0
        _p2 = os.path.join(OUT_DIR, SIM_NAME + ".fbx")
        bpy.ops.export_scene.fbx(
            filepath=_p2, use_selection=False, object_types={'ARMATURE', 'MESH'},
            global_scale=1.0, apply_unit_scale=False, apply_scale_options='FBX_SCALE_UNITS',
            bake_space_transform=False, use_mesh_modifiers=False, add_leaf_bones=False,
            primary_bone_axis='Y', secondary_bone_axis='X', axis_forward='Y', axis_up='Z',
            bake_anim=False, path_mode='RELATIVE', embed_textures=False, use_custom_props=False,
            colors_type='SRGB', prioritize_active_color=True)
        print("   写出 -> %s" % _p2)
    else:
        print("   --dry-run：不写盘")
    print("\n替身要点：单子网格 · 无 LOD · 有 UV · alpha 0→%.2f（米）· 材质名复用 %s" % (SIM_ALPHA, NAME))
    sys.exit(0)


# --------------------------------------------------------------------------- 2) 围脖（原版）
print("\n== 2/5 取原版围脖 ==")
before = set(bpy.data.objects)
bpy.ops.wm.obj_import(filepath=COLLAR, forward_axis='Y', up_axis='Z')   # 🔴 轴必须显式给
col_objs = [o for o in bpy.data.objects if o not in before]
# 🔴 **只要 LOD0**（2026-10-10 踩过）：tpaccli 的 OBJ dump 把 6 级 LOD 放在**同一个文件里、
#    拆成多个对象**（`xxx` / `xxx.lod1` / …）。全吃进来 = 6 份几何叠在一起，
#    顶点数虚高 3 倍、面互相打架。
_all_n = len(col_objs)
col_objs = [o for o in col_objs if ".lod" not in o.name.lower()]
print("   LOD 过滤：%d 个对象 -> 留 %d 个（%s）"
      % (_all_n, len(col_objs), ", ".join(sorted(o.name for o in col_objs))))

# 先各自抽成 (vert, face)，再合并、再抽稀
tmp_v, tmp_f = [], []
for o in col_objs:
    mw = o.matrix_world
    base = len(tmp_v)
    for v in o.data.vertices:
        tmp_v.append(mw @ v.co)
    for poly in o.data.polygons:
        tmp_f.append(tuple(base + i for i in poly.vertices))
for o in col_objs:
    bpy.data.objects.remove(o, do_unlink=True)

# ---- 抽稀（原版 LOD0 也有一两千顶点，挂在脖子上太肥）----
if 0.0 < KEEP < 0.999:
    _m = bpy.data.meshes.new("_collar_tmp")
    _m.from_pydata([tuple(v) for v in tmp_v], [], tmp_f)
    _m.update()
    _o = bpy.data.objects.new("_collar_tmp", _m)
    bpy.context.collection.objects.link(_o)
    _dec = _o.modifiers.new("Dec", 'DECIMATE')
    _dec.decimate_type = 'COLLAPSE'
    _dec.ratio = KEEP
    _dg = bpy.context.evaluated_depsgraph_get()
    _ev = _o.evaluated_get(_dg)
    _m2 = bpy.data.meshes.new_from_object(_ev)
    tmp_v = [v.co.copy() for v in _m2.vertices]
    tmp_f = [tuple(p.vertices) for p in _m2.polygons]
    print("   抽稀 %.0f%%：%d 顶点 / %d 面 -> %d 顶点 / %d 面"
          % (KEEP * 100, len(_m.vertices), len(_m.polygons), len(tmp_v), len(tmp_f)))
    bpy.data.objects.remove(_o, do_unlink=True)
    bpy.data.meshes.remove(_m2)

# ---- 只保留 z >= CUT（= 围脖那圈）----
col_verts, col_faces = [], []
keep = [i for i, p in enumerate(tmp_v) if p.z >= CUT]
remap = {old: k for k, old in enumerate(keep)}
for i in keep:
    col_verts.append(tmp_v[i])
for ids in tmp_f:
    if ids and all(i in remap for i in ids):
        col_faces.append(tuple(remap[i] for i in ids))
print("   原版围脖：%d 顶点 / %d 面（z>=%.3f）" % (len(col_verts), len(col_faces), CUT))
if not col_faces:
    print("!! 围脖切出来是空的 —— 检查 --collar-cut 与源件坐标"); sys.exit(3)
zs = [p.z for p in col_verts]
xs = [p.x for p in col_verts]
ys = [p.y for p in col_verts]
print("   围脖范围 x[%+.3f,%+.3f] y[%+.3f,%+.3f] z[%+.3f,%+.3f]"
      % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))

# --------------------------------------------------------------------------- 3) 飘带
print("\n== 3/5 生成飘带 ==")


def centerline():
    pts = [Vector((TOP_X, TOP_Y, TOP_Z))]
    ds = LENGTH / SEG_L
    a0, a1 = __import__("math").radians(A0), __import__("math").radians(A1)
    for i in range(SEG_L):
        am = a0 + (a1 - a0) * ((i + 0.5) / SEG_L)
        pts.append(pts[-1] + Vector((0.0, -__import__("math").sin(am), -__import__("math").cos(am))) * ds)
    return pts


CL = centerline()
str_verts, str_faces = [], []
for i, p in enumerate(CL):
    t = float(i) / SEG_L
    if i == 0:
        tan = (CL[1] - CL[0]).normalized()
    elif i == SEG_L:
        tan = (CL[-1] - CL[-2]).normalized()
    else:
        tan = (CL[i + 1] - CL[i - 1]).normalized()
    ex = Vector((1.0, 0.0, 0.0))
    n = tan.cross(ex).normalized()
    tau = math.radians(TWIST * t)
    wdir = (ex * math.cos(tau) + n * math.sin(tau)).normalized()
    half = (W_ROOT + (W_TIP - W_ROOT) * t) * 0.5
    wave_x = WAVE * math.sin(2.0 * math.pi * WAVE_TURNS * t)
    for j in range(SEG_W + 1):
        u = float(j) / SEG_W - 0.5
        v = p + wdir * (u * (half * 2.0))
        str_verts.append(Vector((v.x + wave_x, v.y, v.z)))
str_n = len(str_verts)
for i in range(SEG_L):
    for j in range(SEG_W):
        a = i * (SEG_W + 1) + j
        b = (i + 1) * (SEG_W + 1) + j
        str_faces.append((a, b, b + 1, a + 1))
zs = [p.z for p in str_verts]; ys = [p.y for p in str_verts]
print("   飘带 %d 顶点 / %d 面  y[%+.3f,%+.3f] z[%+.3f,%+.3f]"
      % (str_n, len(str_faces), min(ys), max(ys), min(zs), max(zs)))

# --------------------------------------------------------------------------- 4) 合并成一个网格
print("\n== 4/5 合并 + 顶点色 + 双面 ==")
all_verts = [tuple(v) for v in col_verts] + [tuple(v) for v in str_verts]   # 世界坐标（== 游戏坐标）
all_faces = list(col_faces) + [(a + len(col_verts), b + len(col_verts),
                                c + len(col_verts), d + len(col_verts)) for (a, b, c, d) in str_faces]
me = bpy.data.meshes.new(NAME)
# 🔴🔴 **必须换算进骨架局部坐标**（v1 里有、v2 第一版漏了，2026-10-10 当场逮到）：
#     网格 `v.co` 活在**骨架局部空间**（+x 右 · **+y 上** · **+z 前**），不是世界坐标。
#     设计全程用世界坐标（直观），**只在 `from_pydata` 这一步**乘 `matrix_world⁻¹` 搬进去。
_M_INV = ARM.matrix_world.inverted()
_local = [_M_INV @ Vector(v) for v in all_verts]
me.from_pydata([tuple(v) for v in _local], [], all_faces)
me.update()
_rt = [ARM.matrix_world @ v for v in _local]
_dmax = max((_rt[i] - Vector(all_verts[i])).length for i in range(len(all_verts)))
print("   局部↔世界 往返自证：最大偏差 %.6f m  %s" % (_dmax, "OK" if _dmax < 1e-3 else "🔴 换算错了"))

# --- UV：整件重投，与贴图密度对齐（u 绕圈/横向 · v 高度或长度，按 TILE_M 平铺）---
uvl = me.uv_layers.new(name="UVMap")
for poly in me.polygons:
    for k in range(poly.loop_total):
        li = poly.loop_start + k
        vi = me.loops[li].vertex_index
        p = Vector(all_verts[vi])
        if vi >= len(col_verts):
            i, j = divmod(vi - len(col_verts), SEG_W + 1)
            uvl.data[li].uv = (float(j) / SEG_W, (float(i) / SEG_L) * UV_REP)
        else:
            # 围脖：u = 绕颈角度（0~1 一圈）· v = 高度 / TILE_M（与飘带同密度）
            uvl.data[li].uv = ((math.atan2(p.x, -p.y) / (2 * math.pi)) % 1.0, p.z / TILE_M)
print("   UV：围脖=绕颈角度×高度 · 飘带=横向×长度；一格 %.3f m" % TILE_M)

# --- 顶点色 alpha：**围脖全钉死(0)**，飘带从钉死带渐变到 alpha_max ---
alpha = {}
for i, p in enumerate(col_verts):
    alpha[i] = 0.0
ramp = max(1e-6, LENGTH - PIN_BAND)
for i in range(SEG_L + 1):
    s = LENGTH * i / SEG_L
    a = 0.0 if s <= PIN_BAND else ALPHA_MAX * (lambda u: u * u * (3 - 2 * u))((s - PIN_BAND) / ramp)
    for j in range(SEG_W + 1):
        alpha[len(col_verts) + i * (SEG_W + 1) + j] = round(a, 4)
set_alpha(me, alpha)

mat = bpy.data.materials.new(NAME)         # 🔴 材质名 = 网格名（build_armor.py:1261 定式）
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes.get("Principled BSDF")
if bsdf:
    bsdf.inputs["Base Color"].default_value = (0.62, 0.06, 0.05, 1.0)
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = 0.39
    _uvn = nt.nodes.new("ShaderNodeUVMap"); _uvn.uv_map = "UVMap"; _uvn.location = (-700, 0)
    for _sfx, _nrm in (("_d", False), ("_n", True)):
        _p = os.path.join(OUT_DIR, NAME + _sfx + ".png")
        if not os.path.isfile(_p):
            continue
        _img = bpy.data.images.load(_p, check_existing=True)
        _img.colorspace_settings.name = "Non-Color" if _nrm else "sRGB"
        _tx = nt.nodes.new("ShaderNodeTexImage"); _tx.image = _img
        _tx.location = (-450, -260 if _nrm else 200)
        nt.links.new(_uvn.outputs["UV"], _tx.inputs["Vector"])
        if _nrm:
            _nm = nt.nodes.new("ShaderNodeNormalMap"); _nm.location = (-200, -260)
            nt.links.new(_tx.outputs["Color"], _nm.inputs["Color"])
            nt.links.new(_nm.outputs["Normal"], bsdf.inputs["Normal"])
        else:
            nt.links.new(_tx.outputs["Color"], bsdf.inputs["Base Color"])
me.materials.append(mat)

ob = bpy.data.objects.new(NAME, me)
bpy.context.collection.objects.link(ob)
ob.parent = ARM
ob.modifiers.new("Armature", 'ARMATURE').object = ARM
vg = ob.vertex_groups.new(name=BONE)
vg.add(list(range(len(me.vertices))), 1.0, 'REPLACE')

# 双面：**只给飘带那一薄片补**（围脖是壳，补了会自己跟自己打架）
_before = len(me.polygons)
_n = make_double_sided(me, only_from=len(col_faces))
print("   薄片补背面：复制 %d 面（只补飘带，围脖不动）；总 %d -> %d 面"
      % (_n, _before, len(me.polygons)))
print("   合并后：顶点 %d / 面 %d" % (len(me.vertices), len(me.polygons)))

# 最坏下沉（只对飘带那部分有意义；围脖全钉死）
sag = min((Vector(all_verts[i]).z - a) for i, a in alpha.items() if i >= len(col_verts))
print("   飘带最坏下沉后最低 z=%+.3f  %s" % (sag, "OK" if sag > 0.02 else "🔴 会插地"))

# --------------------------------------------------------------------------- 5) 导出
print("\n== 5/5 导出 ==")
if DO_WRITE:
    os.makedirs(OUT_DIR, exist_ok=True)
    keep = {ob, ARM}
    for o in list(bpy.data.objects):
        if o not in keep:
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.scene.unit_settings.scale_length = 1.0
    path = os.path.join(OUT_DIR, NAME + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=False, object_types={'ARMATURE', 'MESH'},
        global_scale=1.0, apply_unit_scale=False, apply_scale_options='FBX_SCALE_UNITS',
        bake_space_transform=False, use_mesh_modifiers=False, add_leaf_bones=False,
        primary_bone_axis='Y', secondary_bone_axis='X', axis_forward='Y', axis_up='Z',
        bake_anim=False, path_mode='RELATIVE', embed_textures=False, use_custom_props=False,
        colors_type='SRGB', prioritize_active_color=True)
    print("   写出 -> %s" % path)
else:
    print("   --dry-run：不写盘")
print("\n要点：顶点色 alpha = **围脖全 0（钉死）**、飘带 0→%.2f 渐变；绑骨 %s。" % (ALPHA_MAX, BONE))
