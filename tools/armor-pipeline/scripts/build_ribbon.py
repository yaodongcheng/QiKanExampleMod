# -*- coding: utf-8 -*-
"""build_ribbon.py —— 造「混天绫」：一条超长丝带披风（`lwn_huntian_ling`）。

【它是谁】飞行开关那件披风（走 `EquipmentIndex.Cape` 槽）。穿着才能飞。
   方案 = `plans/丝带披风与飞行开关.md`。

【为什么是"从零建"】不是改现成装备 —— 原版没有几米长的件。也不是"绑骨做物理"：
   骑砍骨架全游戏共用一份（`human_skeleton`，31 根身体骨），加骨链 = 换骨架，成本高一个数量级。
   **摆动交给引擎布料模拟**：顶点色 alpha = 0 的顶点被钉死在骨骼上，alpha > 0 的才被模拟。

【坐标口径（2026-10-10 重定，前两版都错的，别再猜）】
   · **Blender 世界坐标 == 游戏坐标**：`+z 上 · +x 右 · 正面 = +y · 背面 = -y`。
     两条独立实测，都指向同一个结论：
       ① **基准甲**（`out/taikou_nobunaga_do_a.fbx`，已实机验证过）左脚：踝往 +y 伸 0.118 m、
          往 -y 伸 0.050 m ⇒ 脚尖在 +y ⇒ 前 = +y；
       ② 原版 `feet_male_a`（tpaccli dump，游戏坐标）：踝 y=-0.0758，脚 y∈[-0.117,+0.176]
          ⇒ 往 +y 伸 0.252、往 -y 伸 0.041 ⇒ 同结论。
     ⇒ 两套导入（FBX / OBJ）**同帧**，不存在"y 被翻"。
   · ❌ **走过的两条错路**（别重蹈）：
       ① 拿「锁骨骨根在胸前」判方向 —— 骑砍的 `bip01_l_clavicle` 骨根在 y=-0.034（**颈根靠脊柱那侧**），
          不是胸骨 ⇒ 推出"前 = -y"，把整条丝带挂到了**身前**（2026-10-10 用户实机截图报的）。
       ② 以为 OBJ 导入与 FBX 导入是两套约定、y 会翻 —— 拿脚网格去对骨架，结论自相矛盾。
   · 丝带要往**背后**飘 ⇒ 几何往 **-y** 长（`BACK = -1.0`）。
   · ⚠️ 网格 `v.co` 活在**骨架局部空间**（+x 右 · +y 上 · +z 前），**不是世界坐标**：
     设计全程用世界坐标（直观），**只在 `from_pydata` 那一步**乘 `matrix_world⁻¹` 搬进去；
     脚本里有「局部↔世界往返自证」，偏差 >1mm 直接报错。

【产物】`lwn_huntian_ling.fbx`（骨架 + 单件网格 + 顶点色 + 双面）
   → 写到 `Modules/LwnAnim/AssetSources/ImortReady/HuntianLing/`，由**用户在 ModKit 里 Import**。
   ⚠️ 导入之后还要在 Cloth Editor 里勾 `Use cloth` + 选碰撞体 `cape_body` + 选材质预设，再 Publish。

跑法：
  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python \
      tools/armor-pipeline/scripts/build_ribbon.py -- --out <目录> [选项]
"""
import argparse
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))

HEAD_BONE_Z = 1.569
COLOR_LAYER = "COLOR_0"          # 与 build_cloth.py 同款：原版资产里这层就叫这个名

DEFAULTS = {
    "name":     "lwn_huntian_ling",
    "length":   3.0,             # 丝带总长（沿弧长，米）
    "seg_len":  60,              # 长度方向分段
    "seg_w":    4,               # 宽度方向分段
    "w_root":   0.18,            # 根部宽（米）
    "w_tip":    0.30,            # 末端宽（米）
    "top_z":    1.45,            # 顶端高度（米）—— 颈根/肩线
    "top_back": 0.085,           # 顶端在身后多远（米，**正数 = 往背后**）
                                 #   0.085 来自原版 `helmet_head_shoulder` 的颈胶囊（游戏 y=+0.0073、r=0.09）
                                 #   ⇒ 颈后表面 ≈ y=-0.083，贴上去正好在颈背
    "a0":       30.0,            # 起始切线角（度，从「竖直向下」往「背后」量）
    "a1":       155.0,           # 末端切线角（度；>90 = 已经朝后上方）——实测定 155：
                                 #   再小末端会低到被重力拽进地里，再大就翘得太夸张
    "wave":     0.10,            # 侧向蛇形振幅（米）—— 让静止姿态就不是一块直板
    "wave_turns": 1.5,
    "twist":    80.0,            # 沿长度扭转角（度）—— 布面朝向有变化才像布
    "bone":     "bip01_spine2_11",
    "uv_v_repeat": 6.0,          # UV 沿长度的平铺次数（贴图密度用，见下）
    "pin_band": 0.12,            # 顶端钉死带（沿弧长，米）
    "alpha_max": 0.75,           # 末端活动半径（米）——实测定 0.75：最坏下沉后仍离地 22 cm
    "red":      (0.62, 0.06, 0.05, 1.0),   # 混天绫 = 红
}

BACK = -1.0                      # 🔴 背面 = -y（见文件头「坐标口径」；改这个等于把丝带翻到身前）


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


NAME = get(A, "--name", DEFAULTS["name"])
LENGTH = float(get(A, "--length", DEFAULTS["length"]))
SEG_L = int(get(A, "--seg-len", DEFAULTS["seg_len"]))
SEG_W = int(get(A, "--seg-w", DEFAULTS["seg_w"]))
W_ROOT = float(get(A, "--w-root", DEFAULTS["w_root"]))
W_TIP = float(get(A, "--w-tip", DEFAULTS["w_tip"]))
TOP_Z = float(get(A, "--top-z", DEFAULTS["top_z"]))
TOP_BACK = float(get(A, "--top-back", DEFAULTS["top_back"]))
A0 = float(get(A, "--a0", DEFAULTS["a0"]))
A1 = float(get(A, "--a1", DEFAULTS["a1"]))
WAVE = float(get(A, "--wave", DEFAULTS["wave"]))
WAVE_TURNS = float(get(A, "--wave-turns", DEFAULTS["wave_turns"]))
TWIST = float(get(A, "--twist", DEFAULTS["twist"]))
BONE = get(A, "--bone", DEFAULTS["bone"])
UV_V_REPEAT = float(get(A, "--uv-v-repeat", DEFAULTS["uv_v_repeat"]))
PIN_BAND = float(get(A, "--pin-band", DEFAULTS["pin_band"]))
ALPHA_MAX = float(get(A, "--alpha-max", DEFAULTS["alpha_max"]))
OUT_DIR = get(A, "--out") or os.path.join(
    os.path.abspath(os.path.join(REPO, "..")), "LwnAnim", "AssetSources", "ImortReady", "HuntianLing")
DO_WRITE = "--dry-run" not in A

SKEL = None
for c in (os.path.join(os.environ.get("MB2_PATH") or "", "modding_resources", "skeletons", "human_skeleton.fbx"),
          r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\modding_resources\skeletons\human_skeleton.fbx",
          r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\modding_resources\skeletons\human_skeleton.fbx"):
    if c and os.path.isfile(c):
        SKEL = c
        break
if not SKEL:
    print("!! 找不到 human_skeleton.fbx（查过 MB2_PATH 与两个备份客户端）")
    sys.exit(2)

print("== 混天绫构建 %s ==" % NAME)
print("   骨架   %s" % SKEL)
print("   产物   %s" % os.path.join(OUT_DIR, NAME + ".fbx"))
print("   长度 %.2f m / 宽 %.2f→%.2f m / 段 %d×%d / 骨 %s"
      % (LENGTH, W_ROOT, W_TIP, SEG_L, SEG_W, BONE))
print("   顶端 (0, %+.2f 后, %.2f)  切线 %.0f°→%.0f°  蛇形 %.2f×%.1f圈  扭转 %.0f°"
      % (TOP_BACK, TOP_Z, A0, A1, WAVE, WAVE_TURNS, TWIST))


# --------------------------------------------------------------------------- 工具
def import_fbx(path, scale=1.0):
    bpy.ops.import_scene.fbx(filepath=path, global_scale=scale)
    return list(bpy.context.selected_objects) or list(bpy.data.objects)


def ensure_color(me):
    ca = me.color_attributes.get(COLOR_LAYER)
    if ca is None:
        ca = me.color_attributes.new(name=COLOR_LAYER, type='BYTE_COLOR', domain='CORNER')
    me.color_attributes.active_color = ca        # 导出只带「活动」那一层
    return ca


def set_alpha(me, alpha_by_vert):
    """逐顶点写 alpha（RGB 全白）—— 与 build_cloth.py 同款：CORNER 域，同一顶点所有 loop 写同一个值。"""
    ca = ensure_color(me)
    for loop in me.loops:
        a = alpha_by_vert.get(loop.vertex_index, 0.0)
        ca.data[loop.index].color = (1.0, 1.0, 1.0, a)


def make_double_sided(ob):
    """【薄片复制一份并翻面】—— 引擎材质层**没有**双面开关（21 个 flag 已反编译核对，无 TwoSided/NoCull），
    单面板从背面看就是"没有"。做法与 `build_armor.py:make_sheets_double_sided` 同源。

    🔴 这里**不做「边界边比 ≥ 0.5」那套判据**：实测规则网格的比例只有
       104/436 ≈ 0.24（长政金前立同样的坑，比例 0.18），判据会漏判 ⇒ 直接全复制。
    """
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
    ret = bmesh.ops.duplicate(bm, geom=geom)
    newf = [g for g in ret["geom"] if isinstance(g, bmesh.types.BMFace)]
    bmesh.ops.reverse_faces(bm, faces=newf)
    bm.to_mesh(me)
    me.update()
    bm.free()
    return len(newf)


# --------------------------------------------------------------------------- 1) 骨架
print("\n== 1/4 读骨架（按 head_13 校准到 z=%.3f）==" % HEAD_BONE_Z)
bpy.ops.wm.read_factory_settings(use_empty=True)
_tmp = import_fbx(SKEL)
_a = next(o for o in _tmp if o.type == 'ARMATURE')
_hb = next(b for b in _a.data.bones if "head_13" in b.name)
z_raw = (_a.matrix_world @ _hb.head_local).z
SCALE = HEAD_BONE_Z / z_raw

bpy.ops.wm.read_factory_settings(use_empty=True)
objs = import_fbx(SKEL, SCALE)
ARM = next(o for o in objs if o.type == 'ARMATURE')
for o in list(objs):
    if o.type != 'ARMATURE':
        bpy.data.objects.remove(o, do_unlink=True)
_hz = (ARM.matrix_world @ ARM.data.bones["bip01_head_13"].head_local).z
if abs(_hz - HEAD_BONE_Z) > 0.01:
    print("!! 骨架校准失败 head z=%.4f" % _hz)
    sys.exit(3)
print("   校准 OK  head_13 z=%.5f  (scale %.6f)" % (_hz, SCALE))

if BONE not in {b.name for b in ARM.data.bones}:
    print("!! 骨架里没有骨 %s；现有：%s" % (BONE, sorted(b.name for b in ARM.data.bones)))
    sys.exit(3)
_b = ARM.data.bones[BONE]
_bh = ARM.matrix_world @ _b.head_local
print("   绑骨 %s @ (%+.3f, %+.3f, %+.3f)" % (BONE, _bh.x, _bh.y, _bh.z))

# 朝向金丝雀：脚踝在身体中线**略靠后**（游戏 y=-0.0758）。整帧若被翻，它会变成正的。
_canary = next((ARM.matrix_world @ b.head_local for b in ARM.data.bones
                if b.name.startswith("bip01_l_foot")), None)
if _canary is None or _canary.y > 0:
    print("!! 朝向金丝雀失败：左脚踝 y=%s（应为负 ≈ -0.076）—— 帧口径变了，停下来人工确认"
          % ("<找不到骨>" if _canary is None else "%+.4f" % _canary.y))
    sys.exit(3)
print("   朝向金丝雀 OK  左脚踝 y=%+.4f（<0 ⇒ 与基准甲同帧：前 = +y、后 = -y）" % _canary.y)


# --------------------------------------------------------------------------- 2) 建丝带
print("\n== 2/4 建丝带几何 ==")


def centerline():
    """按切线角积分出中心线。a 从「竖直向下」往「背后」量；背后 = `BACK * y`（BACK = -1）。"""
    pts = [Vector((0.0, BACK * TOP_BACK, TOP_Z))]
    ds = LENGTH / SEG_L
    a0, a1 = math.radians(A0), math.radians(A1)
    for i in range(SEG_L):
        am = a0 + (a1 - a0) * ((i + 0.5) / SEG_L)       # 中点法 = 二阶精度
        pts.append(pts[-1] + Vector((0.0, BACK * math.sin(am), -math.cos(am))) * ds)
    return pts


CL = centerline()
verts = []                       # 🔴 全部在**世界坐标**里设计：+z 上 · +x 右 · 背面 = +y
for i, p in enumerate(CL):
    t = float(i) / SEG_L
    # 切线（端点用单侧差分）
    if i == 0:
        tan = (CL[1] - CL[0]).normalized()
    elif i == SEG_L:
        tan = (CL[-1] - CL[-2]).normalized()
    else:
        tan = (CL[i + 1] - CL[i - 1]).normalized()
    # 宽度方向：以世界 +x 为基准，绕切线扭转
    ex = Vector((1.0, 0.0, 0.0))
    n = tan.cross(ex).normalized()                       # 与切线垂直、落在 y-z 平面内
    tau = math.radians(TWIST * t)
    wdir = (ex * math.cos(tau) + n * math.sin(tau)).normalized()
    half = (W_ROOT + (W_TIP - W_ROOT) * t) * 0.5
    wave_x = WAVE * math.sin(2.0 * math.pi * WAVE_TURNS * t)
    for j in range(SEG_W + 1):
        u = float(j) / SEG_W - 0.5
        v = p + wdir * (u * (half * 2.0))
        v = Vector((v.x + wave_x, v.y, v.z))
        verts.append(v)

faces = []
for i in range(SEG_L):
    for j in range(SEG_W):
        a = i * (SEG_W + 1) + j
        b = (i + 1) * (SEG_W + 1) + j
        faces.append((a, b, b + 1, a + 1))

me = bpy.data.meshes.new(NAME)
# 🔴🔴 **必须换算进骨架局部坐标**（2026-10-09 实机前逮到的错）：
#     骨架对象自带 `matrix_world`（FBX 导入的 Y-up↔Z-up 转换），网格 `v.co` 活在
#     **骨架局部空间**（+x 右 · **+y 上** · **+z 前**）。把世界坐标直接当 `v.co` 用，
#     丝带会整个立在角色**身前**（实测：世界 (+0.03,+2.67,+1.47) 落到局部就成了"前方 1.4 m、高 2.7 m"）。
#     设计一律在世界坐标里做（直观），**只在建网格这一步**用 M⁻¹ 搬进去。
_M_INV = ARM.matrix_world.inverted()
v_local = [_M_INV @ v for v in verts]
me.from_pydata([tuple(v) for v in v_local], [], faces)
me.update()
# 🔴 UV 必须有真值（2026-10-10 补）：第一版全填 (0,0) ⇒ 整条丝带只采样贴图**一个点**，
#    三件套贴图等于没贴。这里按栅格给：**u = 横向 0~1 · v = 沿长度、按 UV_V_REPEAT 平铺**
#    （平铺是为了让「织纹」有足够的像素密度：不平铺的话 3 m 长摊进 2048 px = 683 px/m，
#      织纹会糊成一片）。
uvl = me.uv_layers.new(name="UVMap")
for poly in me.polygons:
    for k in range(poly.loop_total):
        li = poly.loop_start + k
        i, j = divmod(me.loops[li].vertex_index, SEG_W + 1)
        uvl.data[li].uv = (float(j) / SEG_W, (float(i) / SEG_L) * UV_V_REPEAT)
print("   UV：u 横向 0~1 · v 沿长度平铺 %.1f 次（每格 ≈ %.3f m）"
      % (UV_V_REPEAT, LENGTH / UV_V_REPEAT))

# 回读自证：把局部坐标再变回世界，必须与设计值一致（差 >1mm 就是换算错了）
_rt = [ARM.matrix_world @ v for v in v_local]
_dmax = max((_rt[i] - verts[i]).length for i in range(len(verts)))
print("   局部↔世界 往返自证：最大偏差 %.6f m  %s" % (_dmax, "OK" if _dmax < 1e-3 else "🔴 换算错了"))

xs = [v.x for v in verts]; ys = [v.y for v in verts]; zs = [v.z for v in verts]
print("   顶点 %d / 面 %d   x[%+.3f,%+.3f] y[%+.3f,%+.3f] z[%+.3f,%+.3f]"
      % (len(verts), len(faces), min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
if min(zs) < 0.0:
    print("   ⚠️ 末端落到地面以下 %.3f m —— 静止时会插进地里，调 --a1 / --length" % min(zs))
if max(ys) > 0.02:
    print("   ⚠️ 有顶点跑到**身前** y=%+.3f（前 = +y）—— 丝带应该只在身后" % max(ys))
if max(ys) > 0.05 or min(ys) > -0.2:
    print("   ⚠️ 丝带没往身后伸够（身后最远才 %.3f m）—— 检查 BACK 与 --top-back" % (-min(ys)))

# --------------------------------------------------------------------------- 3) 顶点色 alpha + 双面
print("\n== 3/4 顶点色 alpha ==")
# alpha = 该顶点能离开「骨骼给的位置」多远（米）。顶端钉死带内 = 0。
alpha = {}
ramp_len = max(1e-6, LENGTH - PIN_BAND)
for i, p in enumerate(CL):
    s = LENGTH * i / SEG_L
    if s <= PIN_BAND:
        a = 0.0
    else:
        u = (s - PIN_BAND) / ramp_len
        a = ALPHA_MAX * (u * u * (3.0 - 2.0 * u))       # smoothstep：根部过渡柔和些
    for j in range(SEG_W + 1):
        alpha[i * (SEG_W + 1) + j] = round(a, 4)
set_alpha(me, alpha)
bins = {}
for a in alpha.values():
    bins[round(a, 1)] = bins.get(round(a, 1), 0) + 1
print("   alpha 分布（值:顶点数）: %s   钉死 %d / 自由 %d"
      % (dict(sorted(bins.items())), sum(1 for a in alpha.values() if a <= 0),
         sum(1 for a in alpha.values() if a > 0)))

# 🔴 最坏下沉量判据：alpha = 该顶点能被重力拽离骨骼位置的最大距离（米）。
#    ⇒ 每个顶点「静止时最低能到哪儿」= z − alpha。**小于 0 就是会插进地里**。
#    （只按 z 算 = 保守估计：方向随重力当然是往下，布料不会真往下拽满，但留余量。）
_sag = sorted(((verts[i].z - a, i) for i, a in alpha.items()))
_worst, _wi = _sag[0]
print("   最坏下沉：顶点 #%d  z=%+.3f − alpha=%.3f  =>  最低可到 z=%+.3f  %s"
      % (_wi, verts[_wi].z, alpha[_wi], _worst,
         "OK" if _worst > 0.02 else "🔴 会插进地面 —— 抬 --a1 或降 --alpha-max"))

# 🔴 **材质名 = 网格名 = NAME**，不加任何后缀（`build_armor.py:1261` 的原话：
#    "默认材质名 = 网格名 = NAME。**别自作聪明加 `_mtl`**"）。
#    这里的名字就是编辑器中 `_mtl.tpac` 资产的内部名，**网格靠它找材质** ——
#    对不上就是"编辑器拿默认白材质渲染"（铁律 32）。
mat = bpy.data.materials.new(NAME)
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes.get("Principled BSDF")
if bsdf:
    bsdf.inputs["Base Color"].default_value = DEFAULTS["red"]
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = 0.39      # 与 _s 里算出的粗糙度均值对齐
    # 贴图三件套接进材质（由 `build_ribbon_textures.py` 生成、与 FBX 同目录）。
    # 🔴 FBX 只能表达 _d 与 _n；`_s`（金属/粗糙/AO 打包图）表达不了 —— 那个槽由人在
    #    Material Editor 里挂 tex[4]，这是既定流程。
    _uvn = nt.nodes.new("ShaderNodeUVMap")
    _uvn.uv_map = "UVMap"
    _uvn.location = (-700, 0)
    for _suffix, _slot, _is_nrm in (("_d", "Base Color", False), ("_n", "Normal", True)):
        _p = os.path.join(OUT_DIR, NAME + _suffix + ".png")
        if not os.path.isfile(_p):
            print("   （贴图 %s 还不存在，材质里先不接 —— 先跑 build_ribbon_textures.py）"
                  % os.path.basename(_p))
            continue
        _img = bpy.data.images.load(_p, check_existing=True)
        _img.colorspace_settings.name = "Non-Color" if _is_nrm else "sRGB"
        _tex = nt.nodes.new("ShaderNodeTexImage")
        _tex.image = _img
        _tex.location = (-450, -260 if _is_nrm else 200)
        nt.links.new(_uvn.outputs["UV"], _tex.inputs["Vector"])
        if _is_nrm:
            _nm = nt.nodes.new("ShaderNodeNormalMap")
            _nm.location = (-200, -260)
            nt.links.new(_tex.outputs["Color"], _nm.inputs["Color"])
            nt.links.new(_nm.outputs["Normal"], bsdf.inputs["Normal"])
        else:
            nt.links.new(_tex.outputs["Color"], bsdf.inputs["Base Color"])
        print("   材质接了 %s" % os.path.basename(_p))
me.materials.append(mat)

ob = bpy.data.objects.new(NAME, me)
bpy.context.collection.objects.link(ob)
ob.parent = ARM
_m = ob.modifiers.new("Armature", 'ARMATURE')
_m.object = ARM
vg = ob.vertex_groups.new(name=BONE)
vg.add(list(range(len(me.vertices))), 1.0, 'REPLACE')
print("   绑骨 %s 权重 1.0（全片一根骨；骨骼只负责「钉在哪」，摆动交给布料）" % BONE)

_n = make_double_sided(ob)
print("   薄片补背面：复制 %d 面并翻面（引擎材质无双面开关，只能靠几何）" % _n)
print("   补背面后：顶点 %d / 面 %d" % (len(me.vertices), len(me.polygons)))
if COLOR_LAYER not in [c.name for c in me.color_attributes]:
    print("!! 补背面丢了顶点色层"); sys.exit(3)

# --------------------------------------------------------------------------- 4) 导出
print("\n== 4/4 导出 ==")
if DO_WRITE:
    os.makedirs(OUT_DIR, exist_ok=True)
    keep = {ob, ARM}
    for o in list(bpy.data.objects):
        if o not in keep:
            bpy.data.objects.remove(o, do_unlink=True)
    print("   导出前场景：%s" % sorted(o.name for o in bpy.context.scene.objects))
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

state = {"name": NAME, "length": LENGTH, "seg": [SEG_L, SEG_W], "bone": BONE,
         "verts": len(me.vertices), "faces": len(me.polygons),
         "alpha_max": ALPHA_MAX, "pin_band": PIN_BAND,
         "bbox_y": [round(min(ys), 3), round(max(ys), 3)],
         "bbox_z": [round(min(zs), 3), round(max(zs), 3)], "written": DO_WRITE}
print(json.dumps(state, ensure_ascii=False))

print("""
下一步（**人做**）：
  1. 用户开 ModKit
  2. 在编辑器里 Import 这份 FBX（源文件在 %s）
  3. Cloth Editor：Preview mesh 选 %s
     · Render mesh 面板 **勾 `Use cloth`**（不勾 = 整条不参与，怎么设都没用）
     · Simulation mesh **留空**（= 直接模拟；丝带才 %d 顶点，不需要低模替身）
     · Collision body 选 **`cape_body`**（已含脊柱/锁骨/上臂/前臂/大腿/小腿全套胶囊）
     · Cloth material 先选预设 **`flag`**；想"往上飘"改用 **`feather`**（它的重力是 -10）
     · **Save mesh settings** → cook
  4. Publish
  5. 我这边接物品定义与飞行门控（`custom.flight` 加 ribbon/give 子命令）
""" % (OUT_DIR, NAME, len(me.vertices)))
