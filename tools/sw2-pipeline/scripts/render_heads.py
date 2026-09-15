# -*- coding: utf-8 -*-
"""render_heads.py —— 把批量产出的头部 FBX 排成一张对照图（肉眼验收用）。

做法：**逐头渲染成一张正方形小图**（相机的取景用现成的 autoframe，画布正方形 → 不可能出现
各向异性），再用 numpy 拼成大图。照的是盔甲管线 `render_textured.py` 的成熟做法。

🔴 为什么不直接拍一张大图：正交相机的 `ortho_scale` 配 `sensor_fit` 在非正方形画布上
   取景会各向异性 —— 实测头被纵向拉伸了 2~3 倍，看图完全没法判断好坏（踩过）。

用法:
  blender -b --python render_heads.py -- --dir <Debug/offline/sw2_build> --out <对照图.png>
                                            [--view front|side|back] [--cell 420]
"""
import bpy, sys, os, math
import numpy as np
from mathutils import Vector, Matrix

A = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def get(k, d=None):
    return A[A.index(k) + 1] if k in A else d


DIR = get("--dir")
OUT = get("--out")
VIEW = get("--view", "front")
CELL = int(get("--cell", "420"))

import inspect
import io_scene_fbx.import_fbx as mod
_s = inspect.getsource(mod)
_bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
if _bad in _s:
    exec(compile(_s.replace(_bad, "pass"), mod.__file__, "exec"), mod.__dict__)

items = []
for d in sorted(os.listdir(DIR)):
    p = os.path.join(DIR, d)
    if os.path.isdir(p):
        v2 = [f for f in os.listdir(p) if f.endswith("_v2.fbx")]
        if v2:
            items.append((d, os.path.join(p, v2[0])))
if not items:
    print("FATAL: %s 下没找到 *_v2.fbx" % DIR)
    sys.exit(1)

cols = int(math.ceil(math.sqrt(len(items))))
rows = int(math.ceil(len(items) / float(cols)))
DV = {"front": Vector((0, -1, 0)), "back": Vector((0, 1, 0)), "side": Vector((1, 0, 0))}[VIEW]

tiles = []
for i, (key, path) in enumerate(items):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scn = bpy.context.scene
    for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        try:
            scn.render.engine = eng
            break
        except TypeError:
            continue
    scn.view_settings.view_transform = "Standard"
    scn.render.image_settings.file_format = "PNG"
    scn.render.resolution_x = CELL
    scn.render.resolution_y = CELL
    scn.render.film_transparent = False
    w = bpy.data.worlds.new("W")
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.22, 0.24, 0.28, 1)
    scn.world = w
    for nm, rot, en in (("k", (0.9, 0.0, 0.5), 5.0), ("f", (1.3, 0.0, -1.1), 2.5),
                        ("b", (1.0, 0.0, 3.1), 2.0)):
        ld = bpy.data.lights.new(nm, "SUN")
        ld.energy = en
        ld.use_shadow = False
        lo = bpy.data.objects.new(nm, ld)
        lo.rotation_euler = rot
        scn.collection.objects.link(lo)
    cd = bpy.data.cameras.new("C")
    cd.type = "ORTHO"
    cd.sensor_fit = "AUTO"          # 画布是正方形 → AUTO 就是各向同性
    cd.clip_start = 0.01
    cd.clip_end = 1e6
    cam = bpy.data.objects.new("C", cd)
    scn.collection.objects.link(cam)
    scn.camera = cam

    bpy.ops.import_scene.fbx(filepath=path)
    # 🔴 必须把骨架按回【静止姿态】再渲染 —— 网格带蒙皮，导入的骨架姿态一变形，
    #    渲染出来就是个被拉长的怪形（实测：头被拉成 2~3 倍高，看图完全没法判断好坏）。
    #    老脚本 sw2_head_prep.py 里那句 `pose_position = 'REST'` 就是这个用途。
    for _o in bpy.data.objects:
        if _o.type == 'ARMATURE':
            _o.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    main = [o for o in meshes if o.name.endswith(".0")] or meshes
    for o in meshes:
        o.hide_render = (o not in main)
    lo3 = [1e9] * 3
    hi3 = [-1e9] * 3
    for o in main:
        for v in o.data.vertices:
            p = o.matrix_world @ v.co
            for j in range(3):
                lo3[j] = min(lo3[j], p[j])
                hi3[j] = max(hi3[j], p[j])
    ctr = Vector([(lo3[j] + hi3[j]) / 2 for j in range(3)])
    dim = max(hi3[j] - lo3[j] for j in range(3))
    cd.ortho_scale = max(1e-6, dim) * 1.15
    cam.location = ctr + DV * 800.0
    cam.rotation_euler = (-DV).to_track_quat("-Z", "Y").to_euler()
    fp = os.path.join(os.path.dirname(os.path.abspath(OUT)), "_tile_%02d.png" % i)
    scn.render.filepath = fp
    bpy.ops.render.render(write_still=True)
    im = bpy.data.images.load(fp)
    tiles.append(np.array(im.pixels[:], dtype=np.float32).reshape(CELL, CELL, 4))
    bpy.data.images.remove(im)
    os.remove(fp)
    print("  [tile] %-16s 对角=%.3f" % (key, dim))

# 拼大图（行优先，与 items 同序）
sheet = np.zeros((rows * CELL, cols * CELL, 4), dtype=np.float32)
sheet[:, :, 3] = 1.0
for i, t in enumerate(tiles):
    cx, cy = i % cols, i // cols
    # Blender 的像素是行从下往上 —— 行优先摆放要对齐
    r = rows - 1 - cy
    sheet[r * CELL:(r + 1) * CELL, cx * CELL:(cx + 1) * CELL] = t
o = bpy.data.images.new("sheet", cols * CELL, rows * CELL, alpha=True)
o.pixels = sheet.ravel().tolist()
o.filepath_raw = os.path.abspath(OUT)
o.file_format = "PNG"
o.save()
print("[HEADS] %d 个 → %s   （顺序：%s）" % (len(items), OUT, ",".join(k for k, _ in items)))
