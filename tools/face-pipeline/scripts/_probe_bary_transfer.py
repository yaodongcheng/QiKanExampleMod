# _probe_bary_transfer.py —— 试验：把表情段的搬运从「最近邻 3 点反距离加权」换成
# 「最近三角形重心坐标插值」，看唇线那道突变能不能保住。
#
# 为什么要试：3 点加权在源网格稀疏时会把「上唇 0 / 下唇 −10mm」这道折线抹成渐变
#   → 目标上唇跟着往下走 = 嘴张不开（实测唇线位移只有源头的一半）。重心插值 = 原样复制
#   源网格三角形内的线性场，折线仍是折线。
#
# 用法：blender --background --python _probe_bary_transfer.py -- <源FBX> <源对象名> <目标FBX> <目标对象名> <帧号> <输出前缀>
import os
import sys
import math

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

args = sys.argv[sys.argv.index("--") + 1:]
SRC, SRC_OBJ, DST, DST_OBJ, FRAME, OUT = args[0], args[1], args[2], args[3], int(args[4]), args[5]
sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def patch_fbx_importer():
    import inspect
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)


def load(path, objname):
    patch_fbx_importer()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    bpy.context.view_layer.update()
    for o in bpy.data.objects:
        if o.type == 'MESH' and o.name == objname:
            return o
    raise SystemExit("找不到网格 %s（现有 %s）" % (objname, [o.name for o in bpy.data.objects]))


def chan(ob, frame):
    for kb in ob.data.shape_keys.key_blocks:
        tail = kb.name.rsplit("_", 1)
        if len(tail) == 2 and tail[1].isdigit() and int(tail[1]) == frame:
            base = [v.co.copy() for v in ob.data.vertices]
            return [kb.data[i].co - base[i] for i in range(len(base))]
    raise SystemExit("源网格没有帧 %d" % frame)


s = load(SRC, SRC_OBJ)
s_co = [v.co.copy() for v in s.data.vertices]
s_delta = chan(s, FRAME)
s_tris = [tuple(p.vertices) for p in s.data.polygons]
print("源 %s：%d 顶点 / %d 三角，帧 %d 最大位移 %.2f mm"
      % (s.name, len(s_co), len(s_tris), FRAME, max(d.length for d in s_delta) * 1000))

d = load(DST, DST_OBJ)
d_co = [v.co.copy() for v in d.data.vertices]
bvh = BVHTree.FromPolygons(s_co, s_tris, all_triangles=True)


def bary(p, a, b, c):
    v0 = b - a; v1 = c - a; v2 = p - a
    d00 = v0.dot(v0); d01 = v0.dot(v1); d11 = v1.dot(v1)
    d20 = v2.dot(v0); d21 = v2.dot(v1)
    den = d00 * d11 - d01 * d01
    if abs(den) < 1e-20:
        return 1.0, 0.0, 0.0
    v = (d11 * d20 - d01 * d21) / den
    w = (d00 * d21 - d01 * d20) / den
    return 1.0 - v - w, v, w


far = 0
for i, p in enumerate(d_co):
    loc, nrm, idx, dist = bvh.find_nearest(p)
    if loc is None:
        continue
    if dist > 0.01:
        far += 1
    a, b, c = s_tris[idx]
    wa, wb, wc = bary(loc, s_co[a], s_co[b], s_co[c])
    dv = s_delta[a] * wa + s_delta[b] * wb + s_delta[c] * wc
    d.data.vertices[i].co = p + dv
print("目标 %s：%d 顶点，映射距离 >10mm 的有 %d 个" % (d.name, len(d_co), far))
bpy.context.view_layer.update()

# 渲染（与 _render_mouth.py 同机位）
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'SINGLE'
scene.display.shading.single_color = (0.75, 0.72, 0.70)
scene.render.resolution_x = 640
scene.render.resolution_y = 640
TARGET = Vector((0.0, 0.15, 1.605))
cam_data = bpy.data.cameras.new("cam")
cam_data.type = 'ORTHO'
cam_data.ortho_scale = 0.11
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
for tag, ang in (("front", 0), ("q", 35)):
    a = math.radians(ang)
    cam.location = TARGET + Vector((math.sin(a) * 0.6, math.cos(a) * 0.6, 0.02))
    cam.rotation_euler = (TARGET - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = "%s_%s.png" % (OUT, tag)
    bpy.ops.render.render(write_still=True)
    print("渲染 -> %s_%s.png" % (OUT, tag))
sys.stdout.flush()
