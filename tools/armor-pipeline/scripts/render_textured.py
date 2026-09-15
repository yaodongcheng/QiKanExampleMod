# -*- coding: utf-8 -*-
"""render_textured.py — 用生成的 _d/_n/_s 三张贴图，给甲上 PBR 材质渲染。

这是最终检验：量数字看不出"像不像甲"，得看图。
(_s 通道约定：R=金属度 / G=255−粗糙度 / B=环境光遮蔽)

用法:
  blender -b --python render_textured.py -- <甲.fbx> <贴图目录> <outdir> <name> [身体.fbx|none]
"""
import bpy
import sys
import os
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
armor_path, texdir, outdir, name = argv[0], argv[1], argv[2], argv[3]
body_path = argv[4] if len(argv) > 4 else "none"
SIMPLE = "--simple" in argv          # 只挂漫反射，用于隔离 法线/高光 的问题
TAG = "simple" if SIMPLE else "pbr"
# 🔴 输出目录转绝对路径（同 check_fit.py，2026-09-15）：相对路径下 Blender 会打印"写出"但磁盘无文件
outdir = os.path.abspath(outdir)
os.makedirs(outdir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=armor_path)
allm = [o for o in bpy.data.objects if o.type == 'MESH']
lod0 = [o for o in allm if o.name == name]
armor = lod0 if lod0 else allm
# 🔴 必须把其余 LOD 藏起来：6 个 LOD 网格位置完全重合，而 FBX 里带的是**指向已失效文件的
#    贴图节点**（导出时 embed_textures=False）。Blender 用**品红**画贴图丢失的面，
#    于是 LOD1~5 和 LOD0 抢像素 -> 满身品红斑块，看起来像贴图坏了，其实数据没问题。
for o in allm:
    if o not in armor:
        o.hide_render = True
print("甲:", [o.name for o in armor], " 已藏起其余 LOD:", len(allm) - len(armor))

body = None
if body_path.lower() not in ("none", "-", ""):
    before = set(o.name for o in bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=body_path)
    new = [o for o in bpy.data.objects if o.name not in before]
    ms = [o for o in new if o.type == 'MESH']
    for o in new:
        if o.type == 'ARMATURE':
            bpy.data.objects.remove(o, do_unlink=True)
    ms.sort(key=lambda o: -len(o.data.vertices))
    body = ms[0] if ms else None
    for o in ms:
        if o is not body:
            bpy.data.objects.remove(o, do_unlink=True)
    print("身体:", body.name if body else None)


def load_img(p, srgb):
    im = bpy.data.images.load(p)
    im.colorspace_settings.name = 'sRGB' if srgb else 'Non-Color'
    return im


mats = {}
for o in armor:
    m = bpy.data.materials.new('pbr_' + o.name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])

    d = load_img(os.path.join(texdir, name + "_d.png"), True)
    n = load_img(os.path.join(texdir, name + "_n.png"), False)
    s = load_img(os.path.join(texdir, name + "_s.png"), False)

    td = nt.nodes.new('ShaderNodeTexImage'); td.image = d; td.location = (-700, 300)
    tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = n; tn.location = (-700, 0)
    ts = nt.nodes.new('ShaderNodeTexImage'); ts.image = s; ts.location = (-700, -300)
    nm = nt.nodes.new('ShaderNodeNormalMap'); nm.location = (-400, 0)
    sep = nt.nodes.new('ShaderNodeSeparateColor'); sep.location = (-400, -300)
    inv = nt.nodes.new('ShaderNodeMath'); inv.operation = 'SUBTRACT'
    inv.inputs[0].default_value = 1.0; inv.location = (-200, -400)

    nt.links.new(td.outputs['Color'], bsdf.inputs['Base Color'])
    if not SIMPLE:
        nt.links.new(tn.outputs['Color'], nm.inputs['Color'])
        nt.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])
        nt.links.new(ts.outputs['Color'], sep.inputs['Color'])
        nt.links.new(sep.outputs['Red'], bsdf.inputs['Metallic'])       # R = 金属度
        nt.links.new(sep.outputs['Green'], inv.inputs[1])               # G = 255-粗糙
        nt.links.new(inv.outputs['Value'], bsdf.inputs['Roughness'])    # 粗糙 = 1-G
    else:
        bsdf.inputs['Metallic'].default_value = 0.0
        bsdf.inputs['Roughness'].default_value = 0.9
    o.data.materials.clear()
    o.data.materials.append(m)
    mats[o.name] = m

if body:
    bm = bpy.data.materials.new('m_body')
    bm.use_nodes = True
    b = next(n for n in bm.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = (0.60, 0.55, 0.50, 1)
    b.inputs['Roughness'].default_value = 0.85
    body.data.materials.clear()
    body.data.materials.append(bm)

scn = bpy.context.scene
for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try:
        scn.render.engine = eng
        break
    except TypeError:
        continue
scn.view_settings.view_transform = 'Standard'
scn.render.image_settings.file_format = 'PNG'
scn.render.resolution_x = 1080
scn.render.resolution_y = 900
scn.render.film_transparent = False

world = bpy.data.worlds.new('W')
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.42, 0.45, 0.52, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = 1.0
scn.world = world
# 要有一点方向光才能看出金属反光
for nm_, rot, en in (('k', (0.85, 0.0, 0.45), 4.0), ('f', (1.35, 0.0, -1.0), 2.0),
                     ('b', (1.0, 0.0, 3.0), 1.5)):
    ld = bpy.data.lights.new(nm_, 'SUN')
    ld.energy = en
    ld.use_shadow = False
    lo = bpy.data.objects.new(nm_, ld)
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


cd = bpy.data.cameras.new('C')
cd.type = 'ORTHO'
cd.sensor_fit = 'VERTICAL'
cam = bpy.data.objects.new('C', cd)
scn.collection.objects.link(cam)
scn.camera = cam
VIS = [o for o in bpy.data.objects if o.type == 'MESH' and not o.hide_render]
ctr = autoframe(scn, cd, VIS)
print('   自动取景 ortho_scale=%.3f 中心 z=%.3f' % (cd.ortho_scale, ctr.z))

tiles = []
for tag, dv in (('F', (0, 1, 0)), ('side', (1, 0, 0)), ('B', (0, -1, 0))):
    d = Vector(dv)
    cam.location = ctr + d * 6.0
    cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    fp = os.path.join(outdir, 'tex_%s_%s_%s.png' % (name, TAG, tag))
    scn.render.filepath = fp
    bpy.ops.render.render(write_still=True)
    im = bpy.data.images.load(fp)
    tiles.append(np.array(im.pixels[:], dtype=np.float32).reshape(
            scn.render.resolution_y, scn.render.resolution_x, 4))
    bpy.data.images.remove(im)

strip = np.concatenate(tiles, axis=1)
o = bpy.data.images.new('t', 3 * scn.render.resolution_x, scn.render.resolution_y, alpha=True)
o.pixels = strip.ravel().tolist()
o.filepath_raw = os.path.join(outdir, 'tex_%s_%s_strip.png' % (name, TAG))
o.file_format = 'PNG'
o.save()
print("SAVED", os.path.join(outdir, 'tex_%s_%s_strip.png' % (name, TAG)))
