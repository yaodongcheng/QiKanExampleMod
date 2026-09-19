# _render_mouth.py —— 把某个形变帧拉满，怼近嘴部渲染（判断"张嘴"到底开没开）。
# 用法：blender --background --python _render_mouth.py -- <fbx> <帧号> <输出png> [对象名子串]
#
# 规矩照工程既有约定：形状键全部归零 → 只把目标帧设 1.0；不删骨架。
import os
import sys

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
FBX, FRAME, OUT = args[0], int(args[1]), args[2]
HINT = args[3] if len(args) > 3 else None
sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def patch_fbx_importer():
    """tpac → FBX 的 dump 里 morph 缺 FullWeights，Blender 5.2 导入器会断言崩（工程老坑）。"""
    import inspect
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)
        print("[patch] import_fbx 断言已内存级替换")


patch_fbx_importer()

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
print("导入 %d 件：%s" % (len(meshes), [o.name for o in meshes]))

hit = 0
for o in meshes:
    if not o.data.shape_keys:
        continue
    for kb in o.data.shape_keys.key_blocks:
        kb.value = 0.0
    for kb in o.data.shape_keys.key_blocks:
        tail = kb.name.rsplit("_", 1)
        if len(tail) == 2 and tail[1].isdigit() and int(tail[1]) == FRAME:
            kb.value = 1.0
            hit += 1
            print("  帧 %d -> %s = 1.0" % (FRAME, kb.name))
print("命中 %d 个网格" % hit)
bpy.context.view_layer.update()

# 只留嘴附近可见：把非目标件隐藏（眼睛/嘴件留着，牙也看得见）
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'SINGLE'
scene.display.shading.single_color = (0.75, 0.72, 0.70)
scene.render.resolution_x = 640
scene.render.resolution_y = 640
scene.render.film_transparent = False

TARGET = Vector((0.0, 0.15, 1.605))       # 嘴中心
cam_data = bpy.data.cameras.new("cam")
cam_data.type = 'ORTHO'
cam_data.ortho_scale = 0.11               # 11cm 视野 = 嘴部特写
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam


def shoot(name, ang_deg, out):
    import math
    a = math.radians(ang_deg)
    dist = 0.6
    cam.location = TARGET + Vector((math.sin(a) * dist, math.cos(a) * dist, 0.02))
    d = (TARGET - cam.location)
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print("  渲染 -> %s" % out)


base = os.path.splitext(OUT)[0]
shoot("front", 0, base + "_front.png")
shoot("q", 35, base + "_q.png")
sys.stdout.flush()
