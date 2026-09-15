# -*- coding: utf-8 -*-
"""build_weapon.py —— 战无2 源模型 → 骑砍2 武器网格（单件核心）。

为什么要有这一步（不能直接拿源件）
------------------------------------
战无2 的模型是 **T-pose**，武器**横躺在手上**（实测 28 人：长轴几乎全是 ±X）。
而骑砍2 的武器网格约定（实测原版 `torch_g` + 织丰 `sho_bokken_katana` 归纳）：
    · **原点 = 握持点**（原版握把跨原点：torch z[-0.08, 0.28]）
    · **长轴 = +Z**（刀尖朝 +Z）
    · **刃宽沿 X、刃厚沿 Y**（原版 blade：x±2.8cm 宽 / y±0.42cm 厚 / z 长）
所以必须做一次刚体变换，把源件摆正。

变换怎么算（全自动，判据都是实测出来的）
----------------------------------------
  1. **握持点** = 离武器最近的那根**手骨**（战无2 手骨族 bone_18/19/26~45）投影到长轴上的点。
     实测 28 人：手骨到武器表面 0.2~40cm（多数 <3cm）——武器确实是握在手里的。
  2. **长轴** = 武器顶点 PCA 第一主成分；**次轴** = 第二主成分（扁武器的刃宽方向）。
  3. **刀尖朝哪端** = 长轴上离握持点**更远**的那一端。
  4. **旋转** R：长轴(刀尖向)→+Z、次轴→+X、第三轴→+Y（三轴正交，det=+1，天然是旋转）。
  5. **平移**：握持点 → 原点。**缩放**：源件是厘米，×0.01 → 米。

用法（Blender）:
    blender -b --python build_weapon.py -- --src <源.fbx> --out <输出目录> --name taikou_xxx_weapon_a
"""
import bpy
import sys
import os
import re
import math
import numpy as np
from mathutils import Vector, Matrix

def get(a, k, d=None):
    return a[a.index(k) + 1] if k in a else d

def patch_importer():
    """战无2 FBX 的 morph 通道缺 FullWeights，Blender 导入器会断言崩溃 → 内存级补丁。"""
    import inspect
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched: TWT morph without FullWeights")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)

def bone_group(name):
    m = re.fullmatch(r"bone_(\d+)", name or "")
    if not m:
        return "other"
    n = int(m.group(1))
    return "hand" if (n in (18, 19) or 26 <= n <= 45) else "other"

# ---------------- 参数 ----------------
a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC = get(a, "--src")
OUTDIR = os.path.abspath(get(a, "--out"))
NAME = get(a, "--name")
if not (SRC and OUTDIR and NAME):
    sys.exit("用法: --src <源.fbx> --out <目录> --name <资源名>")
os.makedirs(OUTDIR, exist_ok=True)

# ---------------- 导入 + 挑武器件 ----------------
patch_importer()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
bpy.context.view_layer.update()

weapon_obs = []
for ob in bpy.data.objects:
    if ob.type != "MESH":
        continue
    mats = " ".join((m.name if m else "") for m in ob.data.materials).lower()
    if "mat_w_" in mats:
        weapon_obs.append(ob)
if not weapon_obs:
    sys.exit("!! 源模型里没有材质名带 mat_w_ 的武器件")
print("[WEAP] 武器件 %d 个：%s" % (len(weapon_obs), [o.name for o in weapon_obs]))

W = np.array([list(ob.matrix_world @ v.co) for ob in weapon_obs for v in ob.data.vertices])
print("[WEAP] 顶点 %d，包围盒 x[%.1f,%.1f] y[%.1f,%.1f] z[%.1f,%.1f]"
      % (len(W), W[:, 0].min(), W[:, 0].max(), W[:, 1].min(), W[:, 1].max(),
         W[:, 2].min(), W[:, 2].max()))

# ---------------- 手骨（握持点）----------------
hands = []
for arm in bpy.data.objects:
    if arm.type != "ARMATURE":
        continue
    for b in arm.data.bones:
        if bone_group(b.name) == "hand":
            hands.append((b.name, np.array(list(arm.matrix_world @ b.head_local))))
if not hands:
    sys.exit("!! 骨架里没有手骨（bone_18/19/26~45），无法定握持点")
P, dmin, pname = None, 1e9, ""
for nm, p in hands:
    d = float(np.linalg.norm(W - p, axis=1).min())
    if d < dmin:
        P, dmin, pname = p, d, nm
print("[HAND] 握持手 %s，到手表面最近 %.1fcm" % (pname, dmin))
if dmin > 25.0:
    print("   !! 手离武器 %.0fcm —— 握持点可疑，务必渲染核对" % dmin)

# ---------------- PCA 定长轴 / 次轴 ----------------
c = W.mean(axis=0)
X = W - c
ev, evec = np.linalg.eigh(np.cov(X.T))
order = np.argsort(ev)[::-1]
u, v = evec[:, order[0]], evec[:, order[1]]
t = X @ u
th = float((P - c) @ u)
# 刀尖朝离握持点更远的那端
u_tip = u if (t.max() - th) >= (th - t.min()) else -u
print("[AXIS] 长轴 %.3f/%.3f/%.3f（刀尖向）· 次轴 %.3f/%.3f/%.3f · 全长 %.1fcm"
      % (u_tip[0], u_tip[1], u_tip[2], v[0], v[1], v[2], t.max() - t.min()))
print("[AXIS] 握持点两侧杆长：柄侧 %.0fcm / 尖侧 %.0fcm"
      % (min(th - t.min(), t.max() - th), max(th - t.min(), t.max() - th)))

# ---------------- 构造旋转 R ----------------
e1 = u_tip
e2 = v - e1 * float(e1 @ v)              # 正交化次轴
n2 = np.linalg.norm(e2)
if n2 < 1e-6:
    sys.exit("!! 次轴退化，PCA 结果不可用")
e2 /= n2
e3 = np.cross(e1, e2)                    # 右手系
R = np.array([e2, e3, e1])               # 行 = e2/e3/e1 ⇒ R·e2=x̂, R·e3=ŷ, R·e1=ẑ
print("[XFORM] det(R) = %.6f（应为 +1）" % np.linalg.det(R))

# 握持点 = P 在长轴上的投影（让武器轴线穿过原点）
hold = c + e1 * th
M = Matrix([list(R[0]), list(R[1]), list(R[2])]).to_4x4()

# ---------------- 变换顶点 + 合并成一件 ----------------
me_new = bpy.data.meshes.new(NAME)
all_v, all_f, all_uv = [], [], []
for ob in weapon_obs:
    me = ob.data
    mw = ob.matrix_world
    base = len(all_v)
    for vt in me.vertices:
        w = np.array(list(mw @ vt.co))
        q = R @ (w - hold) * 0.01          # 厘米 → 米
        all_v.append(q)
    uvl = me.uv_layers.active
    for poly in me.polygons:
        all_f.append([i + base for i in poly.vertices])
    # 🔴 缺 UV 的件**不能整件放弃 UV**（踩过：小次郎/政宗/归蝶各有一个无 UV 的小件，
    #    于是整把武器都不导 UV）——按环逐个取，没有的补 (0,0)。
    for li in range(len(me.loops)):
        all_uv.append(tuple(uvl.data[li].uv) if uvl else (0.0, 0.0))
    if uvl is None:
        print("   ⚠️ 件 %s 没有 UV 层 → 该件 UV 全部记 (0,0)" % ob.name[-24:])
me_new.from_pydata([Vector(x) for x in all_v], [], all_f)
me_new.update()
if len(all_uv) == len(me_new.loops):
    uvl = me_new.uv_layers.new(name="UVMap")
    for i, uv in enumerate(all_uv):
        uvl.data[i].uv = uv
else:
    sys.exit("!! UV 数与面环数不符（%d vs %d）—— 合并逻辑出错，别硬导"
             % (len(all_uv), len(me_new.loops)))

ob_new = bpy.data.objects.new(NAME, me_new)
bpy.context.scene.collection.objects.link(ob_new)

# 材质名 = 资源裸名（铁律 27 同制式；编辑器按材质名找材质资产）
mat = bpy.data.materials.new(NAME)
mat.use_nodes = True
me_new.materials.append(mat)

# 清场：只留武器（编辑器导入只认这一个）
for ob in list(bpy.data.objects):
    if ob is not ob_new:
        bpy.data.objects.remove(ob, do_unlink=True)

q = np.array([list(v.co) for v in me_new.vertices])
print("[OUT ] 变换后包围盒 x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]（米）"
      % (q[:, 0].min(), q[:, 0].max(), q[:, 1].min(), q[:, 1].max(),
         q[:, 2].min(), q[:, 2].max()))
print("[OUT ] 握把跨原点：z 负侧 %.3fm / 正侧 %.3fm" % (-q[:, 2].min(), q[:, 2].max()))

# ---------------- 导出 FBX（规格照 build_armor.py）----------------
bpy.context.scene.unit_settings.scale_length = 1.0
fbx_path = os.path.join(OUTDIR, NAME + ".fbx")
kw = dict(
    filepath=fbx_path, use_selection=False, object_types={'MESH'},
    global_scale=1.0, apply_unit_scale=False, apply_scale_options='FBX_SCALE_UNITS',
    bake_space_transform=False, use_mesh_modifiers=False, add_leaf_bones=False,
    axis_forward='Y', axis_up='Z', bake_anim=False, path_mode='COPY',
    embed_textures=False, use_custom_props=False,
)
try:
    bpy.ops.export_scene.fbx(**kw)
except TypeError as e:
    print("   fbx kwarg 问题:", e)
    kw.pop('apply_scale_options', None)
    bpy.ops.export_scene.fbx(**kw)
print("[OUT ] 导出 FBX -> %s (%.0f KB)" % (fbx_path, os.path.getsize(fbx_path) / 1024.0))

blob = open(fbx_path, 'rb').read()
tex = sorted(set(m.decode('latin1') for m in
                 re.findall(rb'[ -~]{4,120}\.(?:png|tga|dds|jpg|jpeg)', blob)))
print("[OUT ] 自检 贴图引用: %s" % (tex if tex else "无 ✓"))
print("[OUT ] 自检 UV: %s" % ("有 ✓" if (b'LayerElementUV' in blob or b'UVMap' in blob)
                              else "!! 缺 —— 贴图不会生效"))
print("DONE", NAME)
