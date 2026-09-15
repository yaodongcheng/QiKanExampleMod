# -*- coding: utf-8 -*-
"""check_fit.py — 把做好的甲套到原版身体上渲染，看合不合身。

用法:
  blender -b --python check_fit.py -- <甲.fbx> <原版身体.fbx> <outdir> [tag]
"""
import bpy
import sys
import os
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
armor_path, body_path, outdir = argv[0], argv[1], argv[2]
tag = argv[3] if len(argv) > 3 else "fit"
# 🔴 输出目录必须转绝对路径（2026-09-15 踩）：Blender 的 `render.filepath` / `image.save()`
#    对相对路径是按 **blend 文件位置**（无 blend 时行为不可靠）解析的 —— 症状是打印了
#    "SAVED ..." 但磁盘上没有文件（同 tools/sw2-pipeline/README 坑表第 2 条）。
outdir = os.path.abspath(outdir)
os.makedirs(outdir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)

# --- 甲（含骨架）---
bpy.ops.import_scene.fbx(filepath=armor_path)
allm = [o for o in bpy.data.objects if o.type == 'MESH']
# 🔴 6 个 LOD 网格位置完全重合。只留 LOD0，其余 hide_render —— 否则它们在同一个位置抢像素。
lod0 = [o for o in allm if '.lod' not in o.name]
lod0.sort(key=lambda o: -len(o.data.vertices))
armor_meshes = lod0[:1] if lod0 else allm
for o in allm:
    o.hide_render = (o not in armor_meshes)
print("甲:", [o.name for o in armor_meshes], " 已藏起其余 LOD:", len(allm) - len(armor_meshes))

# --- 原版身体（只要网格，骨架丢掉——两边共用 human_skeleton，同空间）---
body_main = None
if body_path.lower() not in ("none", "-", ""):
    before = set(o.name for o in bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=body_path)
    body_new = [o for o in bpy.data.objects if o.name not in before]
    body_meshes = [o for o in body_new if o.type == 'MESH']
    for o in body_new:
        if o.type == 'ARMATURE':
            bpy.data.objects.remove(o, do_unlink=True)
    # 身体只留顶点最多的那件（主体），附属件（肩/儿童）不要
    body_meshes.sort(key=lambda o: -len(o.data.vertices))
    body_main = body_meshes[0] if body_meshes else None
    print("身体:", [o.name for o in body_meshes], "-> 用", body_main.name if body_main else None)
    for o in body_meshes:
        if o is not body_main:
            bpy.data.objects.remove(o, do_unlink=True)
else:
    print("身体: （跳过，只渲甲）")

# 身体给个灰白材质，甲保留原色（无贴图 -> 给个红调，便于区分）
def mk(name, rgb, rough=0.6):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if b:
        b.inputs['Base Color'].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
        b.inputs['Roughness'].default_value = rough
        b.inputs['Metallic'].default_value = 0.0
    return m

if body_main:
    body_main.data.materials.clear()
    body_main.data.materials.append(mk('m_body', (0.62, 0.60, 0.58)))
for o in armor_meshes:
    o.data.materials.clear()
    o.data.materials.append(mk('m_armor', (0.45, 0.14, 0.12)))

scn = bpy.context.scene
for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try:
        scn.render.engine = eng
        break
    except TypeError:
        continue
scn.view_settings.view_transform = 'Standard'
scn.render.image_settings.file_format = 'PNG'
scn.render.film_transparent = False
W, H = 1080, 900
scn.render.resolution_x = W
scn.render.resolution_y = H

world = bpy.data.worlds.new('W')
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.5, 0.5, 0.53, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = 1.6
scn.world = world
for name, rot, en in (('k', (0.85, 0.0, 0.45), 3.4), ('f', (1.35, 0.0, -1.0), 2.4),
                      ('b', (1.0, 0.0, 3.0), 1.8)):
    ld = bpy.data.lights.new(name, 'SUN')
    ld.energy = en
    ld.use_shadow = False
    lo = bpy.data.objects.new(name, ld)
    lo.rotation_euler = rot
    scn.collection.objects.link(lo)

# 🔴 取景必须从**可见网格的包围盒**自动算，不能写死。
#    踩过（2026-09-14）：写死 ortho_scale=1.95 时，A-pose 伸出去的小臂/籠手落到画框外，
#    看起来像"只有上臂、没有小臂"，其实是被裁掉了。
def autoframe(scn, cam_data, objs, margin=1.12):
    from mathutils import Vector as _V
    mn = _V((1e9, 1e9, 1e9)); mx = _V((-1e9, -1e9, -1e9))
    for o in objs:
        if o.hide_render or o.type != 'MESH':
            continue
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            for i in range(3):
                mn[i] = min(mn[i], w[i]); mx[i] = max(mx[i], w[i])
    ctr = (mn + mx) * 0.5
    w = (mx.x - mn.x) * margin
    h = (mx.z - mn.z) * margin
    rx, ry = scn.render.resolution_x, scn.render.resolution_y
    # 相机 sensor_fit='VERTICAL' -> ortho_scale 是**竖直**取景高度
    cam_data.ortho_scale = max(h, w * ry / rx)
    return ctr


cam_data = bpy.data.cameras.new('C')
cam_data.type = 'ORTHO'
cam_data.sensor_fit = 'VERTICAL'
cam = bpy.data.objects.new('C', cam_data)
scn.collection.objects.link(cam)
scn.camera = cam
VIS = [o for o in bpy.data.objects if o.type == 'MESH' and not o.hide_render]
ctr = autoframe(scn, cam_data, VIS)
print('   自动取景 ortho_scale=%.3f 中心 z=%.3f' % ((cd if 'cd' in dir() else cam_data).ortho_scale, ctr.z))

VIEWS = (('F', (0.0, 1.0, 0.0)), ('side', (1.0, 0.0, 0.0)), ('B', (0.0, -1.0, 0.0)))
cols = []
for vname, d in VIEWS:
    dv = Vector(d)
    cam.location = ctr + dv * 6.0
    cam.rotation_euler = (-dv).to_track_quat('-Z', 'Y').to_euler()
    fp = os.path.join(outdir, 'fit_%s_%s.png' % (tag, vname))
    scn.render.filepath = fp
    bpy.ops.render.render(write_still=True)
    im = bpy.data.images.load(fp)
    cols.append(np.array(im.pixels[:], dtype=np.float32).reshape(H, W, 4))
    bpy.data.images.remove(im)

strip = np.concatenate(cols, axis=1)
out = bpy.data.images.new('fit', 3 * W, H, alpha=True)
out.pixels = strip.ravel().tolist()
out.filepath_raw = os.path.join(outdir, 'fit_%s_strip.png' % tag)
out.file_format = 'PNG'
out.save()
print("SAVED", os.path.join(outdir, 'fit_%s_strip.png' % tag))
