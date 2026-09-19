# -*- coding: utf-8 -*-
"""render_weapons.py —— 把做好的武器排成一张对照图（验收用）。

每格一件武器，**按导出后的真实姿态**摆（原点=握持点、长轴 +Z → 图上都是"竖直、握把在格心"），
标英文 slug（Blender 文字对象画不了中文，slug 与角色的对应见 parts_table.py）。

用法（Blender）:
    blender -b --python render_weapons.py -- --dir <武器目录> --out <输出目录> [--keys a b c]
"""
import bpy
import sys
import os
import math
import fnmatch
from mathutils import Vector

def get(a, k, d=None):
    return a[a.index(k) + 1] if k in a else d

def add_label(text, loc, size):
    bpy.ops.object.text_add(location=loc)
    ob = bpy.context.object
    ob.data.body = text
    ob.data.size = size
    ob.data.align_x = 'LEFT'
    ob.data.align_y = 'TOP'
    ob.rotation_euler = (math.radians(90), 0, 0)
    m = bpy.data.materials.new("lbl_%s" % text)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    em = nt.nodes.new('ShaderNodeEmission')
    em.inputs['Color'].default_value = (1.0, 0.85, 0.0, 1)
    em.inputs['Strength'].default_value = 2.0
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    nt.links.new(em.outputs['Emission'], out.inputs['Surface'])
    ob.data.materials.append(m)
    return ob

a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
WDIR = os.path.abspath(get(a, "--dir"))
OUT = os.path.abspath(get(a, "--out"))
keys = [x for x in a[a.index("--keys") + 1:] if not x.startswith("--")] if "--keys" in a else None
# --side：排版时把每件绕 Z 转 90°，这样从同一台正面相机就能看到"侧面"（刃口朝向/厚度）
SIDE = "--side" in a
# --textures：给每件挂上同目录的 `<名>_d.png`（验证 UV/贴图有没有跟出来；否则是白模）
TEX = "--textures" in a
# --glob：目录里怎么找武器文件（默认战无2 命名；KCD 给 "kcd_*.fbx"）
PATTERN = get(a, "--glob", "*_weapon_a.fbx")
os.makedirs(OUT, exist_ok=True)

names = keys or sorted(f[:-4] for f in os.listdir(WDIR)
                       if fnmatch.fnmatch(f, PATTERN))
# --keys 允许四种写法：完整资源名 / slug（yukimura）/ 角色 key（L00_yukimura）/ 目录里真实存在的文件名
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
try:
    from parts_table import TABLE
    _by_key = {k: TABLE[k]["asset"][len("head_"):-len("_a")] for k in TABLE}
except Exception:
    _by_key = {}
def _full(n):
    # 目录里已有同名 fbx ⇒ 就是完整资源名，原样用（KCD 等非战无2 命名走这条）
    if os.path.isfile(os.path.join(WDIR, n + ".fbx")):
        return n
    if n.endswith("_weapon_a"):
        return n
    slug = _by_key.get(n, n)
    return "taikou_%s_weapon_a" % slug
names = [_full(n) for n in names]
paths = [os.path.join(WDIR, n + ".fbx") for n in names]
names = [n for n, p in zip(names, paths) if os.path.isfile(p)]
paths = [p for p in paths if os.path.isfile(p)]
print("[SHEET] %d 件武器" % len(paths))
if not paths:
    sys.exit("!! 没有可渲染的武器（--dir 下没有匹配 %s 的 fbx）" % PATTERN)

bpy.ops.wm.read_factory_settings(use_empty=True)
CELL_W, CELL_H = 300, 420
COLS = 7
ROWS = int(math.ceil(len(paths) / float(COLS)))
cells = []
for i, p in enumerate(paths):
    before = set(o.name for o in bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=p)
    new = [o for o in bpy.data.objects if o.type == "MESH" and o.name not in before]
    if not new:
        print("   !! %s 没导入进来" % p); continue
    bpy.ops.object.select_all(action='DESELECT')
    for o in new:
        o.select_set(True)
    bpy.context.view_layer.objects.active = new[0]
    if len(new) > 1:
        bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    vs = [ob.matrix_world @ v.co for v in ob.data.vertices]
    lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
    hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    cx, cy = i % COLS, i // COLS
    d = max(hi.x - lo.x, hi.z - lo.z)
    s = (CELL_W * 0.62) / max(1e-6, d) if (hi.z - lo.z) > (hi.x - lo.x) else (CELL_H * 0.70) / max(1e-6, d)
    ctr = (lo + hi) / 2
    ox = (cx - (COLS - 1) / 2.0) * CELL_W
    oz = -((cy - (ROWS - 1) / 2.0) * CELL_H)
    from mathutils import Matrix
    spin = Matrix.Rotation(math.radians(90), 4, 'Z') if SIDE else Matrix.Identity(4)
    ob.matrix_world = (Matrix.Translation(Vector((ox, 0, oz))) @ spin @ Matrix.Scale(s, 4)
                       @ Matrix.Translation(-ctr) @ ob.matrix_world)
    cells.append((i, ox, oz, names[i]))
    if TEX:
        # 贴图 = 同目录的 <名>_d.png（就是导出时跟着的那张源图集）
        png = os.path.join(WDIR, names[i] + "_d.png")
        if os.path.isfile(png):
            img = bpy.data.images.load(png)
            m = bpy.data.materials.new("tex_" + names[i])
            m.use_nodes = True
            nt = m.node_tree
            for n in list(nt.nodes):
                nt.nodes.remove(n)
            tx = nt.nodes.new('ShaderNodeTexImage')
            tx.image = img
            bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
            bsdf.inputs['Roughness'].default_value = 0.6
            out = nt.nodes.new('ShaderNodeOutputMaterial')
            nt.links.new(tx.outputs['Color'], bsdf.inputs['Base Color'])
            nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])
            ob.data.materials.clear()
            ob.data.materials.append(m)
        else:
            print("   !! 缺贴图 %s" % os.path.basename(png))

scn = bpy.context.scene
for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try:
        scn.render.engine = eng; break
    except TypeError:
        continue
scn.view_settings.view_transform = 'Standard'
scn.render.image_settings.file_format = 'PNG'
w = bpy.data.worlds.new('W'); w.use_nodes = True
w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.22, 0.23, 0.27, 1)
scn.world = w
for nm, rot, en in (('k', (0.9, 0.0, 0.5), 5.0), ('f', (1.3, 0.0, -1.1), 3.0),
                    ('b', (1.0, 0.0, 3.1), 2.0)):
    ld = bpy.data.lights.new(nm, 'SUN'); ld.energy = en; ld.use_shadow = False
    lo_ = bpy.data.objects.new(nm, ld); lo_.rotation_euler = rot
    scn.collection.objects.link(lo_)
cd = bpy.data.cameras.new('C'); cd.type = 'ORTHO'
cd.clip_start = 0.01; cd.clip_end = 100000.0
cam = bpy.data.objects.new('C', cd); scn.collection.objects.link(cam)
scn.camera = cam

# 标签：slab 名字放格子下沿（比所有件更靠近相机）
for i, ox, oz, nm in cells:
    add_label(nm.replace("taikou_", "").replace("_weapon_a", ""),
              (ox - CELL_W * 0.46, -1.0, oz + CELL_H * 0.42), CELL_W * 0.055)

scn.render.resolution_x = COLS * CELL_W
scn.render.resolution_y = ROWS * CELL_H
cd.ortho_scale = COLS * CELL_W
cam.location = (0, -900, 0)
cam.rotation_euler = (math.radians(90), 0, 0)
scn.render.filepath = os.path.join(OUT, "weapons_sheet_%s.png" % ("S" if SIDE else "F"))
bpy.ops.render.render(write_still=True)
print("[SHEET] 出图 ->", scn.render.filepath)
