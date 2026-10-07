# -*- coding: utf-8 -*-
"""build_hook_from_obj.py —— 把 dump 出来的原版三爪钩 OBJ 转成**我们自己的网格资产**（FBX）。

【为什么需要转】
   原版 `hook` 在包里是**场景道具**那一类资产 —— 运行时按名字取不到（实机日志：
   `[Spell] 网格 'hook' 查不到`），所以钩头实体一直是空壳。转成我们自己的资产（经 ModKit 导入）
   才有 MetaMesh 可引用。

【形状】完全照原版（用户 2026-10-07 认可的 `_grapple_ref2/preview2` 那把三爪钩）——
   尾环在原点、钩体沿 **+Z**、全长 0.664 m。**不做任何缩放/旋转**：坐标原样搬，
   运行期的缩放仍走 `GrappleHook.MeshScale`（默认 0.30 ⇒ 钩头 20 cm）。

【产物】
   `lwn_grapple_hook.fbx`（无骨架/无蒙皮，单件；自带一个材质槽 `lwn_grapple_hook_mat`）
   → 写到中转沙箱的 `AssetSources/ImortReady/GrappleModel/`，由**用户在 ModKit 里 Import**。

跑法：
   "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --python tools/grapple-model/scripts/build_hook_from_obj.py -- \\
       --obj <hook.obj> --out <目录> [--name lwn_grapple_hook] [--scale 1.0]
"""
import bpy
import sys
import os
from mathutils import Vector


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    o, i = {}, 0
    while i < len(a):
        if a[i].startswith("--"):
            o[a[i][2:]] = a[i + 1] if i + 1 < len(a) else ""
            i += 2
        else:
            i += 1
    return o


A = args()
OBJ = A["obj"]
OUT = A["out"]
NAME = A.get("name", "lwn_grapple_hook")
SCALE = float(A.get("scale", "1.0"))

bpy.ops.wm.read_factory_settings(use_empty=True)

# 🔴 轴向：tpaccli dump 出来的 OBJ 是**游戏坐标系**（Z 上、Y 前）—— 按 Z-up 读，
#    导出 FBX 时用默认轴向转换，ModKit 那边再转回来 ⇒ 原样往返（脚本末尾会自检包围盒）。
bpy.ops.wm.obj_import(filepath=OBJ, forward_axis='Y', up_axis='Z')

meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
if not meshes:
    print("ERROR: OBJ 里没有网格")
    sys.exit(1)


def bbox(objs):
    pts = []
    for ob in objs:
        for c in ob.bound_box:
            pts.append(ob.matrix_world @ Vector(c))
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx


mn0, mx0 = bbox(meshes)
print("[HOOK] 读入包围盒 min=%s max=%s" % (tuple(round(v, 4) for v in mn0), tuple(round(v, 4) for v in mx0)))
# 期望包围盒 = 读入值 × SCALE（下面拿它跟回读结果比）
mn0, mx0 = mn0 * SCALE, mx0 * SCALE

# 合并成一个对象（原版是单件，这里只是防呆）+ 建一个材质槽
for ob in meshes:
    ob.select_set(True)
if len(meshes) > 1:
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
ob = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
ob.name = NAME
ob.data.name = NAME

if SCALE != 1.0:
    for v in ob.data.vertices:
        v.co *= SCALE

if not ob.data.materials:
    mat = bpy.data.materials.new(NAME + "_mat")
    mat.use_nodes = True
    ob.data.materials.append(mat)

# 🔴 把"金属"的观感**烘进 FBX 的材质默认值**（base color / metallic / roughness）——
#    编辑器每次重编译会把材质设置刷回 FBX 默认值，写在这儿至少不会刷成白板。
for mat in ob.data.materials:
    if mat is None:
        continue
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF") if mat.node_tree else None
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (0.28, 0.29, 0.31, 1.0)   # 深铁灰
        bsdf.inputs["Metallic"].default_value = 1.0
        bsdf.inputs["Roughness"].default_value = 0.42

# UV：原版网格自带的 UV 会被 import/export 原样带走；没有就补一个空层（防呆）
if not ob.data.uv_layers:
    ob.data.uv_layers.new(name="UVMap")

out_fbx = os.path.join(OUT, NAME + ".fbx")
bpy.ops.export_scene.fbx(
    filepath=out_fbx,
    use_selection=True,
    apply_unit_scale=True,
    global_scale=1.0,
    object_types={"MESH"},
    mesh_smooth_type="OFF",
    add_leaf_bones=False,
    use_mesh_modifiers=False,
    bake_anim=False,
    path_mode="COPY",
)
print("EXPORTED", out_fbx)

# ── 自检：把刚写出的 FBX 读回来，比包围盒（防轴向/缩放被悄悄改掉）──
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=out_fbx)
back = [o for o in bpy.context.scene.objects if o.type == 'MESH']
mn1, mx1 = bbox(back)
d = max(abs(mn1.x - mn0.x), abs(mn1.y - mn0.y), abs(mn1.z - mn0.z),
        abs(mx1.x - mx0.x), abs(mx1.y - mx0.y), abs(mx1.z - mx0.z))
print("[HOOK] 回读包围盒 min=%s max=%s  最大偏差=%.6f m" % (
    tuple(round(v, 4) for v in mn1), tuple(round(v, 4) for v in mx1), d))
print("[HOOK] 往返自检:", "OK" if d < 0.002 else "!! 偏差过大（轴向/缩放被动过）")
