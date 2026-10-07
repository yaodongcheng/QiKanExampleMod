# -*- coding: utf-8 -*-
"""build_proxy.py —— 生成一枚「隐形代理」网格（默认 1.2 cm 的小方块），给钩索那两件物品用。

【为什么需要它】
   物品的 `mesh` 一旦留空（`mesh=""`），装备时会 **AccessViolation 崩**（2026-10-07 实机：
   `custom.grapple equip` → `Agent.EquipWeaponWithNewEntity` → native `WeaponEquipped` 读到空网格）。
   而钩索的"看得见的钩"是**运行时实体**（`Combat/GrappleHook.cs`），物品网格本来就该是隐藏的。
   ⇒ 给它一件**存在、但小到看不见**的网格（塞在拳头里）当代理，替代"空网格"。

【产物】
   `lwn_proxy_invisible.fbx`（无骨架、无蒙皮、单面正方体，边长默认 1.2 cm，原点在中心）

【怎么用】
   1. 本脚本把 FBX 写到中转沙箱的 `AssetSources/ImortReady/GrappleModel/`；
   2. **你在 ModKit 里 Import**（Import meshes only，别勾 Skinning）→ 编辑器会建成网格资产；
   3. 之后两件物品的 `mesh=` 指到它（改 XML 是我的事）。

跑法（Windows）：
   "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --python tools/grapple-model/scripts/build_proxy.py -- --out <目录> [--size 0.012] [--name lwn_proxy_invisible]
"""
import bpy
import sys


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
OUT = A["out"]
SIZE = float(A.get("size", "0.012"))
NAME = A.get("name", "lwn_proxy_invisible")

bpy.ops.wm.read_factory_settings(use_empty=True)

h = SIZE / 2.0
verts = [(-h, -h, -h), (h, -h, -h), (h, h, -h), (-h, h, -h),
         (-h, -h, h), (h, -h, h), (h, h, h), (-h, h, h)]
# 🔴 绕序必须朝外（有符号体积为正）：下面这份是**反过来**的写法 —— 用 `check_winding.py` 验过
faces = [(3, 2, 1, 0), (5, 6, 7, 4), (1, 5, 4, 0),
         (2, 6, 5, 1), (3, 7, 6, 2), (0, 4, 7, 3)]

me = bpy.data.meshes.new(NAME)
me.from_pydata(verts, [], faces)
me.update()

uv = me.uv_layers.new(name="UVMap")
for poly in me.polygons:
    for i in range(poly.loop_total):
        uv.data[poly.loop_start + i].uv = (0.0, 0.0)   # 只为"有 UV 层"，用不到

mat = bpy.data.materials.new(NAME + "_mat")
mat.use_nodes = True
# 深色不反光（它是 1.2 cm 的隐形代理，任何观感都行；这里只是别留"白板"）
bsdf = mat.node_tree.nodes.get("Principled BSDF")
if bsdf:
    bsdf.inputs["Base Color"].default_value = (0.08, 0.08, 0.08, 1.0)
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = 1.0
me.materials.append(mat)

ob = bpy.data.objects.new(NAME, me)
bpy.context.collection.objects.link(ob)
bpy.context.view_layer.objects.active = ob
ob.select_set(True)

out_fbx = OUT.rstrip("/\\") + "/" + NAME + ".fbx"
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
