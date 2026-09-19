# build_shield.py —— KCD 鸢盾 → 骑砍2 盾网格（静态件，不要骨架、不勾 Skinning）
#
# 为什么不能直接用 build_weapon.py
# --------------------------------
# 武器的约定是「原点=握持点 · 长轴 +Z · 刃宽 X · 刃厚 Y」；**盾不是** ——
# 盾是「平放存储」的，靠 item XML 的 `rotation` 摆正。实测原版 `kite_shield_a`
# （`tpaccli dump --format fbx`）得到它的空间约定：
#
#     · 盾面**法线沿 Z**（外侧朝 +Z）
#     · 盾的**高度沿 Y**（1.390 m）
#     · 盾的**宽度沿 X**（0.522 m）
#     · **握把凸在 −Z 侧**（顶点里 z 分位：0%=−0.016 而 75%=0.132 → 一小簇凸在低 z）
#
# ⇒ 正确做法不是去推"握在手里是什么姿势"，而是**把源件对到与原版同一套空间** ——
#   这样 item XML 里那套 `rotation="0.0,10.0,40.00"` / `item_holsters` / `position`
#   一个字都不用改，天然可复用。
#
# KCD 侧实测（`Debug/offline/_kcd_recon/_kcd_shield.py`）：
#     法线沿 **X**（正面/盾心凸起在 +X）· 高度沿 **Y**（0.657 m）· 宽度沿 **Z**（0.535 m）
# ⇒ 映射 R = 「法线→+Z、高度→+Y、宽度→−X」= 刚体旋转（det +1，**不用反绕序**），
#   而且握把正好落在 −Z 侧 —— **与原版一致**（这是映射选对没选反的独立佐证）。
#
# 用法（Blender）:
#   blender -b --python build_shield.py -- --src <源.fbx> --out <目录> --name kcd_henry_shield_a \
#       [--pick shield] [--target-height 1.390] [--no-scale] [--origin center|planebbox-min]
import bpy
import sys
import os
import inspect
import numpy as np
from mathutils import Vector, Matrix


def get(a, k, d=None):
    return a[a.index(k) + 1] if k in a else d


def patch_importer():
    """KCD 的剑/盾蒙皮到的骨头不在骨架子树下 → 导入器 `KeyError: None`。只在该崩时兜底。"""
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "                    (mmat, amat) = mesh.armature_setup[self]"
    if bad in src:
        src = src.replace(bad, (
            "                    if self not in mesh.armature_setup:\n"
            "                        mesh.armature_setup[self] = (mesh.bind_matrix, self.bind_matrix)\n"
            + bad))
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)


A = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC = get(A, "--src")
OUTDIR = os.path.abspath(get(A, "--out"))
NAME = get(A, "--name")
PICK = get(A, "--pick", "shield")
TARGET_H = float(get(A, "--target-height", "1.390"))     # 原版 kite_shield_a 的高度（米）
NO_SCALE = "--no-scale" in A
ORIGIN = get(A, "--origin", "center")
if not (SRC and OUTDIR and NAME):
    sys.exit("用法: --src <源.fbx> --out <目录> --name <资源名> [--pick shield] "
             "[--target-height 1.390] [--no-scale] [--origin center|planebbox-min]")
os.makedirs(OUTDIR, exist_ok=True)

patch_importer()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC, use_anim=False, ignore_leaf_bones=True)

obs = [o for o in bpy.data.objects if o.type == "MESH" and PICK.lower() in o.name.lower()]
if not obs:
    sys.exit("!! 没有名字含 %r 的网格" % PICK)
print("[SHIELD] 件 %d 个：%s" % (len(obs), [o.name for o in obs]))

P = np.array([list(o.matrix_world @ v.co) for o in obs for v in o.data.vertices])
c = P.mean(axis=0)
Xc = P - c
ev, evec = np.linalg.eigh(np.cov(Xc.T))
order = np.argsort(ev)
normal, short, long_ = evec[:, order[0]], evec[:, order[1]], evec[:, order[2]]
print("[SHIELD] 法线 %.3f/%.3f/%.3f · 高度轴 %.3f/%.3f/%.3f · 宽度轴 %.3f/%.3f/%.3f"
      % (*normal, *long_, *short))

# R：目标空间 = 【外侧→+Z、高度→+Y、宽度→−X】（宽度取负是为了 det=+1，不触发反绕序）
# 🔴 外侧朝哪一侧是**渲染看出来的**，不是推出来的（2026-09-19 踩一次）：
#    第一版按"顶点分位里 +法线侧那根长尾 = 盾心凸起"把法线取正 → 渲染出来**握把在 +Z**，
#    与原版相反（原版握把在 −Z）。实际那根长尾是**握把**，外侧在 −法线侧。取负后对齐。
FLIP_FACE = "--flip-face" in A
_s = -1.0 if not FLIP_FACE else 1.0
R = np.array([-_s * short, long_, _s * normal])
if np.linalg.det(R) < 0:            # PCA 给的是左手基时兜一下
    R = np.array([_s * short, long_, _s * normal])
    print("[SHIELD] !! PCA 基是左手的，宽度轴取正（det 已修正）")
print("[SHIELD] det(R) = %.6f（应 +1）" % np.linalg.det(R))

Q = (R @ Xc.T).T                    # 摆正后的坐标（原点仍在重心）
lo, hi = Q.min(axis=0), Q.max(axis=0)
print("[SHIELD] 摆正后 x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f]（米）"
      % (lo[0], hi[0], lo[1], hi[1], lo[2], hi[2]))

s = 1.0
if not NO_SCALE:
    s = TARGET_H / (hi[1] - lo[1])
    Q *= s
    lo, hi = Q.min(axis=0), Q.max(axis=0)
    print("[SHIELD] 高度对齐原版 %.3f m → 缩放 ×%.4f  ⇒ 高 %.3f / 宽 %.3f / 厚 %.3f"
          % (TARGET_H, s, hi[1] - lo[1], hi[0] - lo[0], hi[2] - lo[2]))
else:
    print("[SHIELD] --no-scale：保持源尺寸（高 %.3f m）" % (hi[1] - lo[1]))

# 原点：放在**盾面 bbox 的中心**（x/y 居中），z 取盾面厚度带的中点（不含握把）
# 🔴 原版 kite_shield_a 的原点在 x 33% / y 34% 处（不在正中）—— 那是美术放握把的位置。
#    这里先给"居中"这个可解释的默认；实机若见盾偏高/偏低，改这一个数即可（见 --origin）。
mid_z = lo[2] + (hi[2] - lo[2]) * 0.5
origin = np.array([(lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, mid_z])
if ORIGIN == "planebbox-min":
    origin = np.array([lo[0], lo[1], lo[2]])
Q -= origin
print("[SHIELD] 原点 → %s（盾面中心）· 落位后 x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f]"
      % (ORIGIN, Q[:, 0].min(), Q[:, 0].max(), Q[:, 1].min(), Q[:, 1].max(),
         Q[:, 2].min(), Q[:, 2].max()))

# 合并成一件（静态网格：不要骨架、不要顶点组）
me = bpy.data.meshes.new(NAME)
all_v, all_f, all_uv = [], [], []
for o in obs:
    mw = o.matrix_world
    base = len(all_v)
    for vt in o.data.vertices:
        all_v.append(R @ (np.array(list(mw @ vt.co)) - c))
    uvl = o.data.uv_layers.active
    for poly in o.data.polygons:
        all_f.append([i + base for i in poly.vertices])
    for li in range(len(o.data.loops)):
        all_uv.append(tuple(uvl.data[li].uv) if uvl else (0.0, 0.0))
me.from_pydata([Vector(v * s - origin) for v in all_v], [], all_f)
me.update()
if len(all_uv) == len(me.loops):
    uvl = me.uv_layers.new(name="UVMap")
    for i, uv in enumerate(all_uv):
        uvl.data[i].uv = uv
else:
    sys.exit("!! UV 数与面环数不符（%d vs %d）" % (len(all_uv), len(me.loops)))

ob = bpy.data.objects.new(NAME, me)
bpy.context.scene.collection.objects.link(ob)
mat = bpy.data.materials.new(NAME)
mat.use_nodes = True
me.materials.append(mat)
for o in list(bpy.data.objects):
    if o is not ob:
        bpy.data.objects.remove(o, do_unlink=True)

bpy.context.scene.unit_settings.scale_length = 1.0
fbx_path = os.path.join(OUTDIR, NAME + ".fbx")
bpy.ops.export_scene.fbx(
    filepath=fbx_path, use_selection=False, object_types={'MESH'},
    global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
    bake_space_transform=False, use_mesh_modifiers=False, add_leaf_bones=False,
    axis_forward='Y', axis_up='Z', bake_anim=False, path_mode='COPY',
    embed_textures=False, use_custom_props=False)
print("[SHIELD] 导出 -> %s (%.0f KB)" % (fbx_path, os.path.getsize(fbx_path) / 1024.0))
blob = open(fbx_path, "rb").read()
print("[SHIELD] 自检 UV: %s" % ("有 ✓" if b'LayerElementUV' in blob else "!! 缺"))
print("DONE", NAME)
sys.stdout.flush()
