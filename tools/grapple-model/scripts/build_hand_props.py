# -*- coding: utf-8 -*-
"""build_hand_props.py —— 造钩索"手里那两件"小道具（2026-10-07 用户方案）。

【用户要的画面】
   左手握着**绳末端的环**（弓类物品的网格就挂左手）· 右手**捏着绳子中间某段**（弹药网格挂右手）·
   钩在右手外侧转（钩是运行时实体，绳是 verlet 绳：左手 → 钩，正好从右手旁边穿过去）。

【产物（两件 FBX，都进中转沙箱 ImortReady，由用户在 ModKit Import）】
   · `lwn_grapple_ring.fbx`      —— 环（外径 8 cm / 管径 1.5 cm，原点在环心，塞在拳头里）
   · `lwn_grapple_rope_grip.fbx` —— 一截绳（长 14 cm / 径 1.6 cm，微微下凹；原点在**正中** ⇒ 拳头捏中段）

【约定】
   两件都是**静态网格**（无骨架、不勾 Skinning）、**原点即"手里那一点"**；
   UV 随便给一层（贴图靠材质，不靠 UV 铺开）。两件的坐标都是**米**、Z 上。

跑法：
   "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --python tools/grapple-model/scripts/build_hand_props.py -- \\
       --out <目录> [--ring-outer 0.08] [--rope-len 0.14]
"""
import bpy
import sys
import os
import math

V = None  # mathutils.Vector


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


def new_object(name, verts, faces):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    uv = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        for i in range(poly.loop_total):
            uv.data[poly.loop_start + i].uv = (0.0, 0.0)
    mat = bpy.data.materials.new(name + "_mat")
    mat.use_nodes = True
    # 🔴 把观感烘进 FBX 材质默认值（编辑器重编译会刷回 FBX 默认值，写这儿至少不会变白板）
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        if name.endswith("ring"):
            bsdf.inputs["Base Color"].default_value = (0.30, 0.31, 0.33, 1.0)   # 深铁灰
            bsdf.inputs["Metallic"].default_value = 1.0
            bsdf.inputs["Roughness"].default_value = 0.45
        else:
            bsdf.inputs["Base Color"].default_value = (0.36, 0.30, 0.22, 1.0)   # 麻绳色
            bsdf.inputs["Metallic"].default_value = 0.0
            bsdf.inputs["Roughness"].default_value = 0.9
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    return ob


def export(ob, outdir, name):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    path = os.path.join(outdir, name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path,
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
    print("EXPORTED", path)
    bpy.data.objects.remove(ob, do_unlink=True)


def torus_geo(major_r, minor_r, major_seg=28, minor_seg=12):
    """环（甜甜圈）：ZX 平面上的圆环（法线 = Y，像竖着拎的一枚环）。"""
    verts, faces = [], []
    for i in range(major_seg):
        a = 2 * math.pi * i / major_seg
        cx, cz = major_r * math.cos(a), major_r * math.sin(a)
        for j in range(minor_seg):
            b = 2 * math.pi * j / minor_seg
            r = major_r + minor_r * math.cos(b)
            verts.append((r * math.cos(a), minor_r * math.sin(b), r * math.sin(a)))
    for i in range(major_seg):
        for j in range(minor_seg):
            i2, j2 = (i + 1) % major_seg, (j + 1) % minor_seg
            a0 = i * minor_seg + j
            a1 = i2 * minor_seg + j
            a2 = i2 * minor_seg + j2
            a3 = i * minor_seg + j2
            # 🔴 绕序必须**逆时针朝外**：`(a0,a1,a2,a3)` 这个顺序算出来有符号体积为负 = 面朝内
            #    ⇒ 引擎背面剔除 ⇒ **从外面完全看不见**（2026-10-07 实机：左手的环隐形就是这么来的）。
            #    判据脚本 `tools/grapple-model/scripts/check_winding.py`（有符号体积 > 0 才算对）。
            faces.append((a3, a2, a1, a0))
    return verts, faces


def tube_geo(points, radius, seg=10):
    """沿折线铺管（圆截面）。points = [(x,y,z), ...]"""
    import mathutils
    verts, faces = [], []
    up = mathutils.Vector((0.0, 0.0, 1.0))
    for idx, p in enumerate(points):
        p = mathutils.Vector(p)
        if idx == 0:
            d = mathutils.Vector(points[1]) - p
        elif idx == len(points) - 1:
            d = p - mathutils.Vector(points[-2])
        else:
            d = mathutils.Vector(points[idx + 1]) - mathutils.Vector(points[idx - 1])
        d.normalize()
        side = d.cross(up)
        if side.length < 1e-5:
            side = d.cross(mathutils.Vector((1.0, 0.0, 0.0)))
        side.normalize()
        up2 = side.cross(d).normalized()
        for j in range(seg):
            a = 2 * math.pi * j / seg
            off = side * (radius * math.cos(a)) + up2 * (radius * math.sin(a))
            verts.append((p.x + off.x, p.y + off.y, p.z + off.z))
    n = len(points)
    for i in range(n - 1):
        for j in range(seg):
            j2 = (j + 1) % seg
            # 同 torus_geo：绕序朝外（有符号体积为正），否则背面剔除看不见
            faces.append((i * seg + j2, (i + 1) * seg + j2, (i + 1) * seg + j, i * seg + j))
    return verts, faces


A = args()
OUT = A["out"]
RING_OUTER = float(A.get("ring_outer", "0.08"))
ROPE_LEN = float(A.get("rope_len", "0.14"))
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)

# ① 左手那枚环（8 cm 外径 / 1.5 cm 管径 ⇒ 外半径 0.04、管半径 0.0075）
v, f = torus_geo(RING_OUTER / 2.0 - 0.0075, 0.0075)
export(new_object("lwn_grapple_ring", v, f), OUT, "lwn_grapple_ring")

# ② 右手捏的那截绳（长 14 cm、径 1.6 cm；**原点在正中**、微微下凹 ⇒ 像被捏着中段）
half = ROPE_LEN / 2.0
pts = []
N = 9
for i in range(N):
    t = -1.0 + 2.0 * i / (N - 1)
    x = t * half
    z = -0.012 * (1.0 - t * t)     # 中间下垂一点点（不是直线，看着像绳）
    pts.append((x, 0.0, z))
v, f = tube_geo(pts, 0.008)
export(new_object("lwn_grapple_rope_grip", v, f), OUT, "lwn_grapple_rope_grip")

print("[PROPS] 完成：环（外径 %.0f cm）+ 绳（长 %.0f cm）" % (RING_OUTER * 100, ROPE_LEN * 100))
