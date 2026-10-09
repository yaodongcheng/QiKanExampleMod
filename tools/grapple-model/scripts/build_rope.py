# -*- coding: utf-8 -*-
"""build_rope.py —— 造"绳"那件物品的网格：**一盘绳**（`lwn_grapple_rope`）。

【它是谁】`lwn_grapple_rope`（弓型那件，玩家握着的那件）的 `mesh` —— 引擎把它挂**左手**，
   所以形状 = "左手里盘着一盘绳"；同一件网格也是它在背包/装备界面里的 **2D 图标**。

【口径 = 照抄 `build_hand_props.py`（左手那枚环，**实机验证过**）】：
   · 几何**直接建**（`from_pydata`），对象不带任何变换 —— 不碰轴向转换那套；
   · **盘轴 = Y**（盘面 = XZ）—— 与环的"法线 = Y"同一口径；
   · **原点 = 盘心**（与环的"圆心在原点"一致）；
   · 绕序 = 复制 `build_hand_props.py` 里那套**已验过**的朝外绕序（判据 `check_winding.py`）。
   默认：盘半径 45 mm、管半径 9 mm、3.25 圈、螺距 12 mm ⇒ 约 110 × 40 × 110 mm（一手可握）。

【产物】`lwn_grapple_rope.fbx`（单件、无骨架、自带材质槽、含一层 UV）→ 写到中转沙箱
   `LwnAnim/AssetSources/ImortReady/GrappleModel/`，由**用户在 ModKit 里 Import Create**。
   ⚠️ **导入之前不要启动游戏**：物品 `mesh` 已指向 `lwn_grapple_rope`，网格不在 = 图标渲染时 NRE（雷 168）。

跑法：
  "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --python tools/grapple-model/scripts/build_rope.py -- \\
      --out <目录> [--name lwn_grapple_rope] [--radius 0.045] [--tube 0.009] [--turns 3.25] [--pitch 0.012]
"""
import bpy
import sys
import os
import math
import mathutils


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
    """与 build_hand_props.py 同款：直接建网格 + 一层 UV + 一个材质槽。"""
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    uv = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        for i in range(poly.loop_total):
            uv.data[poly.loop_start + i].uv = (0.0, 0.0)
    mat = bpy.data.materials.new(name + "_mat")
    mat.use_nodes = True
    # 🔴 观感烘进 FBX 材质默认值（编辑器重编译会刷回 FBX 默认值，写这儿至少不会变白板）
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (0.40, 0.32, 0.21, 1.0)   # 麻绳黄褐
        bsdf.inputs["Metallic"].default_value = 0.0
        bsdf.inputs["Roughness"].default_value = 0.88
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


def tube_geo(points, radius, seg=10):
    """沿折线铺管（圆截面）。

    🔴 **绕序**（2026-10-08 实测修正）：`build_hand_props.py` 里那份 `tube_geo` 的绕序
       **是朝内的**（有符号体积为负 ⇒ 背面剔除 ⇒ 从外面完全看不见）—— 那份脚本里的"绳段"
       当年**作废、从没导入过**，所以这个错一直没被发现（实机验过的是**环**，环走的是 `torus_geo`）。
       本脚本用它时被 `check_winding.py` 抓到（体积 -0.000224）⇒ 四个顶点顺序整体反过来。
       判据：`blender -b --python tools/grapple-model/scripts/check_winding.py -- --fbx lwn_grapple_rope.fbx`
       （有符号体积 **> 0** 才算对）。"""
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
            faces.append((i * seg + j, (i + 1) * seg + j, (i + 1) * seg + j2, i * seg + j2))
    return verts, faces


def bbox(objs):
    pts = []
    for o in objs:
        for c in o.bound_box:
            pts.append(o.matrix_world @ mathutils.Vector(c))
    mn = mathutils.Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = mathutils.Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return mn, mx


A = args()
OUT = A["out"]
NAME = A.get("name", "lwn_grapple_rope")
RADIUS = float(A.get("radius", "0.045"))       # 盘半径（到绳中心线）
TUBE = float(A.get("tube", "0.009"))           # 绳的半径
TURNS = float(A.get("turns", "3.25"))
PITCH = float(A.get("pitch", "0.012"))         # 每圈沿盘轴前进多少
TAIL = float(A.get("tail", "0.075"))           # 甩出去的绳头长度（0 = 不要）
PREVIEW = A.get("preview", "")
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)

# ── 螺旋线：**盘轴 = Y**（盘面 = XZ，与左手那枚环同一口径；坐标直接就是游戏坐标，不做任何轴转换）──
pts = []
N = max(96, int(TURNS * 48))
for i in range(N + 1):
    th = 2.0 * math.pi * TURNS * (i / float(N))
    s = (i / float(N)) * TURNS - TURNS / 2.0            # -T/2 … +T/2
    pts.append((RADIUS * math.cos(th), PITCH * s, RADIUS * math.sin(th)))

# 绳头：从末端**沿切线**甩出去一段，并往下垂一点（像绳子从盘里出来搭在手上）
if TAIL > 0:
    th_end = 2.0 * math.pi * TURNS
    tx, tz = -math.sin(th_end), math.cos(th_end)
    ex, ey, ez = pts[-1]
    steps = 12
    for k in range(1, steps + 1):
        t = TAIL * (k / float(steps))
        pts.append((ex + tx * t, ey - 0.10 * t, ez + tz * t - 0.55 * t * t))

verts, faces = tube_geo(pts, TUBE)
ob = new_object(NAME, verts, faces)

mn0, mx0 = bbox([ob])
print("[ROPE] 顶点 %d  包围盒 X=%.3f Y=%.3f Z=%.3f" % (
    len(ob.data.vertices), mx0.x - mn0.x, mx0.y - mn0.y, mx0.z - mn0.z))

export(ob, OUT, NAME)

# ── 预览图（**导出之后**再渲染：预览不许污染交付物）──
if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'OBJECT'
    scene.render.resolution_x = 640
    scene.render.resolution_y = 640
    ob.color = (0.62, 0.60, 0.56, 1.0)
    ctr = (mn0 + mx0) / 2
    size = max((mx0 - mn0).x, (mx0 - mn0).y, (mx0 - mn0).z)
    cam_data = bpy.data.cameras.new("cam")
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    for vname, vdir in (("iso", mathutils.Vector((0.8, -0.9, 0.6))),
                        ("front", mathutils.Vector((0, -1, 0))),
                        ("top", mathutils.Vector((0, 0, 1))),
                        ("side", mathutils.Vector((1, 0, 0)))):
        cam.location = ctr + vdir.normalized() * size * 2.6
        cam.rotation_euler = (cam.location - ctr).to_track_quat('Z', 'Y').to_euler()
        scene.render.filepath = os.path.join(PREVIEW, "%s_%s.png" % (NAME, vname))
        bpy.ops.render.render(write_still=True)
        print("[ROPE] wrote", scene.render.filepath)

print("[ROPE] 完成：盘径 %.0f mm · 管径 %.0f mm · %.2f 圈" % (RADIUS * 2000, TUBE * 2000, TURNS))
