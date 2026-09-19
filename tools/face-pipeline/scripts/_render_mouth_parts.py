# _render_mouth_parts.py —— 嘴部特写 + **按件上色**（脸壳/眼/嘴各一色），看清开口里堵的是哪一件。
# 用法：blender --background --python _render_mouth_parts.py -- <fbx> <帧号> <输出png前缀>
import math
import os
import sys

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
FBX, FRAME, OUT = args[0], int(args[1]), args[2]
sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def patch_fbx_importer():
    import inspect
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)


patch_fbx_importer()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
COL = {"0": (0.72, 0.70, 0.68, 1.0), "1": (0.25, 0.45, 0.95, 1.0), "2": (0.90, 0.25, 0.20, 1.0)}
for o in meshes:
    if o.data.shape_keys:
        for kb in o.data.shape_keys.key_blocks:
            kb.value = 0.0
        for kb in o.data.shape_keys.key_blocks:
            t = kb.name.rsplit("_", 1)
            if len(t) == 2 and t[1].isdigit() and int(t[1]) == FRAME:
                kb.value = 1.0
    suffix = o.name.rsplit(".", 1)[-1]
    o.color = COL.get(suffix, (0.5, 0.5, 0.5))
    print("  %-20s -> 颜色 %s" % (o.name, o.color[:]))
bpy.context.view_layer.update()

scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'OBJECT'
scene.render.resolution_x = 640
scene.render.resolution_y = 640
TARGET = Vector((0.0, 0.15, 1.605))
cam_data = bpy.data.cameras.new("cam")
cam_data.type = 'ORTHO'
cam_data.ortho_scale = 0.11
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
for tag, ang in (("front", 0), ("q", 30)):
    a = math.radians(ang)
    cam.location = TARGET + Vector((math.sin(a) * 0.6, math.cos(a) * 0.6, 0.02))
    cam.rotation_euler = (TARGET - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = "%s_%s.png" % (OUT, tag)
    bpy.ops.render.render(write_still=True)
    print("渲染 -> %s_%s.png" % (OUT, tag))
sys.stdout.flush()
