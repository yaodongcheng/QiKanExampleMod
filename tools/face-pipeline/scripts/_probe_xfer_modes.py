# _probe_xfer_modes.py —— 搬运算法对照实验：同一帧、不同搬法，各渲一张。
#
#   nn            = 现行算法（最近邻 3 点反距离加权）—— 会把"上唇不动/下唇下移"这道折线抹平
#   bary          = 最近三角形重心坐标插值 —— 保折线，但目标顶点离源面远时会找到错三角形（实测撕碎）
#   bary-guard:N  = 距离 ≤ N mm 用 bary，超过则退回 nn
#
# 用法：blender --background --python _probe_xfer_modes.py -- <源FBX> <源对象> <目标FBX> <目标对象> <帧> <模式> <输出前缀>
import math
import os
import sys

import bpy
from mathutils import Vector, kdtree
from mathutils.bvhtree import BVHTree

args = sys.argv[sys.argv.index("--") + 1:]
SRC, SRC_OBJ, DST, DST_OBJ, FRAME, MODE, OUT = args[0], args[1], args[2], args[3], int(args[4]), args[5], args[6]
sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def patch_fbx_importer():
    import inspect
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)


def load(path, name):
    patch_fbx_importer()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    bpy.context.view_layer.update()
    for o in bpy.data.objects:
        if o.type == 'MESH' and o.name == name:
            return o
    raise SystemExit("找不到 %s（现有 %s）" % (name, [o.name for o in bpy.data.objects]))


def chan(ob, frame):
    for kb in ob.data.shape_keys.key_blocks:
        t = kb.name.rsplit("_", 1)
        if len(t) == 2 and t[1].isdigit() and int(t[1]) == frame:
            base = [v.co.copy() for v in ob.data.vertices]
            return [kb.data[i].co - base[i] for i in range(len(base))]
    raise SystemExit("源没有帧 %d" % frame)


s = load(SRC, SRC_OBJ)
s_co = [v.co.copy() for v in s.data.vertices]
s_delta = chan(s, FRAME)
s_tris = [tuple(p.vertices) for p in s.data.polygons]
print("源 %s：%d 顶点 / %d 三角 · 帧 %d 最大 %.2f mm" % (s.name, len(s_co), len(s_tris), FRAME,
                                                     max(d.length for d in s_delta) * 1000))

d = load(DST, DST_OBJ)
d_co = [v.co.copy() for v in d.data.vertices]

kd = kdtree.KDTree(len(s_co))
for i, c in enumerate(s_co):
    kd.insert(c, i)
kd.balance()
bvh = BVHTree.FromPolygons(s_co, s_tris, all_triangles=True)
guard = 0.008
if MODE.startswith("bary-guard:"):
    guard = float(MODE.split(":")[1]) / 1000.0


def bary(p, a, b, c):
    v0 = b - a; v1 = c - a; v2 = p - a
    d00 = v0.dot(v0); d01 = v0.dot(v1); d11 = v1.dot(v1); d20 = v2.dot(v0); d21 = v2.dot(v1)
    den = d00 * d11 - d01 * d01
    if abs(den) < 1e-20:
        return 1.0, 0.0, 0.0
    v = (d11 * d20 - d01 * d21) / den
    w = (d00 * d21 - d01 * d20) / den
    return 1.0 - v - w, v, w


def nn_delta(p):
    hits = kd.find_n(p, 3)
    if hits[0][2] < 1e-6:
        return s_delta[hits[0][1]]
    ws = [1.0 / ((h[2] + 1e-4) ** 2) for h in hits]
    tot = sum(ws)
    v = Vector((0, 0, 0))
    for h, w in zip(hits, ws):
        v = v + s_delta[h[1]] * (w / tot)
    return v


def bary_delta(p):
    loc, nrm, idx, dist = bvh.find_nearest(p)
    if loc is None:
        return nn_delta(p), 1e9
    a, b, c = s_tris[idx]
    wa, wb, wc = bary(loc, s_co[a], s_co[b], s_co[c])
    return s_delta[a] * wa + s_delta[b] * wb + s_delta[c] * wc, dist


used = {"bary": 0, "nn": 0}
for i, p in enumerate(d_co):
    if MODE == "nn":
        dv, _ = nn_delta(p), 0
        used["nn"] += 1
    else:
        dv, dist = bary_delta(p)
        if MODE == "bary" or dist <= guard:
            used["bary"] += 1
        else:
            dv = nn_delta(p)
            used["nn"] += 1
    d.data.vertices[i].co = p + dv
print("模式 %s：bary %d / nn %d" % (MODE, used["bary"], used["nn"]))
bpy.context.view_layer.update()

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
a = math.radians(20)
cam.location = TARGET + Vector((math.sin(a) * 0.6, math.cos(a) * 0.6, 0.02))
cam.rotation_euler = (TARGET - cam.location).to_track_quat('-Z', 'Y').to_euler()
scene.render.filepath = OUT + ".png"
bpy.ops.render.render(write_still=True)
print("渲染 -> %s.png" % OUT)
sys.stdout.flush()
