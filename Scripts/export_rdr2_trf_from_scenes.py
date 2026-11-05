# -*- coding: utf-8 -*-
"""【验证原型】场景 blend → 7 档 TRF（不经过 FBX、不读 lib_native）。

为什么从 blend 出发：场景 blend 里模型、材质、贴图已经齐了（用户在 Blender 里能看到），
比旧的"glTF 瓦片 → FBX"覆盖更全（野地/大地图模型也在场景里）。

本脚本只做验证：打开一张场景 blend，按 hash 去重取模型，直接生成 7 档 TRF。
验证通过后再决定是否并入正式管线（`trf_lod_common.py`）。

用法：
    blender -b "...\\scenes\\sdn.blend" -P tools\\_unbuffer.py -P Scripts\\export_rdr2_trf_from_scenes.py -- \
        --out "Debug/offline/_trf_scene_test/sdn" --min-faces 2000 --limit 3

参数：
    --out        输出目录
    --min-faces  只导 ≥N 面的模型（默认 2000）
    --limit      最多导几个（验证用，默认 3）
    --classes    逗号分隔的导入类（默认 = 21 类里的导入集；给 all 表示不筛）
    --prefer-ring0  取活动区实例优先（默认开）
"""
import argparse
import importlib.util
import json
import math
import os
import sys

import bpy
import mathutils

ROOT = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
EXPORTER = os.path.join(ROOT, "tools", "OpenTrf", "trf_meta_mesh_exporter.py")

RATIOS = [1.0, 0.50, 0.30, 0.19, 0.12, 0.12, 0.12]
MIN_FACES = 8
DEFAULT_CLASSES = {
    'bldg', 'part', 'prop', 'junk', 'veg', 'rock', 'stone', 'struct', 'ruin', 'veh',
    'rail', 'rail_decal', 'road', 'decal', 'unknown', 'lod_out',
}

ARGS = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
ap = argparse.ArgumentParser()
ap.add_argument('--out', required=True)
ap.add_argument('--min-faces', type=int, default=2000)
ap.add_argument('--limit', type=int, default=3)
ap.add_argument('--classes', default='')
ap.add_argument('--prefer-ring0', action='store_true', default=True)
ap.add_argument('--preview', action='store_true', help='给 LOD0/3/6 各渲一张预览图')
ap.add_argument('--planar', action='store_true', help='减面前先做平面溶解，保住屋顶/大平板')
ap.add_argument('--planar-angle', type=float, default=1.0, help='平面溶解角度（度），默认 1')
ap.add_argument('--match', default='', help='只取名字含该子串的模型（验证指定样本用）')
a = ap.parse_args(ARGS)

out_dir = a.out if os.path.isabs(a.out) else os.path.join(ROOT, a.out)
os.makedirs(out_dir, exist_ok=True)

spec = importlib.util.spec_from_file_location("trfexp", EXPORTER)
trfexp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(trfexp)

import_classes = DEFAULT_CLASSES
if a.classes:
    import_classes = None if a.classes.strip().lower() == 'all' else set(
        x.strip() for x in a.classes.split(',') if x.strip())


def tris(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def split_by_material(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    if len(o.data.materials) <= 1:
        return [o]
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.separate(type='MATERIAL')
    bpy.ops.object.mode_set(mode='OBJECT')
    return [x for x in bpy.context.selected_objects if x.type == 'MESH']


def cleanup(o, dist=0.002):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=dist)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')


def decimate(o, ratio):
    if ratio >= 0.999 or len(o.data.polygons) < MIN_FACES:
        return
    cleanup(o)
    if a.planar:
        # 大平板（屋顶/钢板）先平面溶解：共面的三角合成大面，COLLAPSE 就不会把平面折成尖刺
        bpy.ops.object.select_all(action='DESELECT')
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.dissolve_limited(angle_limit=math.radians(a.planar_angle))
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    mod = o.modifiers.new(name="lod", type='DECIMATE')
    mod.decimate_type = 'COLLAPSE'
    mod.ratio = max(0.01, ratio)
    bpy.ops.object.modifier_apply(modifier=mod.name)


def frame_camera(objs):
    mn = mathutils.Vector((1e18,) * 3)
    mx = mathutils.Vector((-1e18,) * 3)
    for o in objs:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            for i in range(3):
                mn[i] = min(mn[i], w[i])
                mx[i] = max(mx[i], w[i])
    c = (mn + mx) * 0.5
    r = max((mx - mn).length * 0.9, 1.0)
    cam = bpy.data.cameras.new('preview_cam')
    cam.lens = 50
    co = bpy.data.objects.new('preview_cam', cam)
    bpy.context.scene.collection.objects.link(co)
    co.location = c + mathutils.Vector((r * 0.8, -r * 0.8, r * 0.55))
    co.rotation_euler = (c - co.location).normalized().to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = co
    return co


def render_lod(parts, out_png):
    """只留当前 LOD 的件渲染（场景里 2 万个对象，不隐藏会挡满画面）。"""
    sc = bpy.context.scene
    keep = {id(o) for o in parts}
    saved = []
    for o in sc.objects:
        if o.type == 'MESH' and id(o) not in keep and not o.hide_render:
            o.hide_render = True
            saved.append(o)
    cam = frame_camera(parts)
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.display.shading.show_shadows = True
    sc.display.shading.show_cavity = True
    sc.render.resolution_x = 800
    sc.render.resolution_y = 600
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'
    sc.render.filepath = out_png
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)
    for o in saved:
        o.hide_render = False


# ---------- 收集模型（按 hash 去重，活动区实例优先） ----------
cands = {}
for o in bpy.context.scene.objects:
    if o.type != 'MESH' or o.name.startswith(('Terrain', 'Water')):
        continue
    h = o.get('hash') or o.get('split_of')
    if not h:
        continue
    cls = o.get('cls_key') or ''
    if import_classes is not None and cls not in import_classes:
        continue
    if a.match and a.match.lower() not in o.name.lower():
        continue
    f = tris(o.data)
    if f < a.min_faces:
        continue
    rec = cands.get(h)
    if rec is None:
        cands[h] = o
    else:
        # 活动区实例优先（ring=0 / 无 ring）；避免取到被远景框裁过的残件
        cur_ring = rec.get('ring') not in (0, None)
        new_ring = o.get('ring') not in (0, None)
        if cur_ring and not new_ring:
            cands[h] = o

picked = list(cands.values())[:a.limit]
print('[scene-trf] 候选模型 %d 个，取前 %d 个验证' % (len(cands), len(picked)))

summary = {}
for src in picked:
    h = src.get('hash') or src.get('split_of')
    name = src.name
    base_faces = tris(src.data)
    # 复制网格到临时对象（不动场景原对象）
    me = src.data.copy()
    me.name = 'tmp_' + me.name
    ob = bpy.data.objects.new('tmp_' + src.name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.matrix_world = mathutils.Matrix.Identity(4)
    temp = [ob]
    try:
        parts = split_by_material(ob)
        temp = list({id(x): x for x in ([ob] + parts)}.values())
        originals = [p.data for p in parts]
        for i, p in enumerate(parts):
            p.name = "%s.%d" % (name, i)
        trf_meshes, lod_faces = [], {}
        prev = {}
        for lv, ratio in enumerate(RATIOS):
            reuse = lv > 0 and abs(ratio - RATIOS[lv - 1]) < 1e-9
            for i, p in enumerate(parts):
                p.data = prev[i].copy() if reuse else originals[i].copy()
                if not reuse:
                    decimate(p, ratio)
                prev[i] = p.data
                p.name = ("%s.%d" % (name, i)) if lv == 0 else ("%s.lod%d.%d" % (name, lv, i))
            lod_faces[lv] = sum(len(p.data.polygons) for p in parts)
            for p in parts:
                trf_meshes.append(trfexp.create_trf_mesh(p))
            if a.preview and lv in (0, 3, 6):
                pv = os.path.join(out_dir, 'preview')
                os.makedirs(pv, exist_ok=True)
                render_lod(parts, os.path.join(pv, '%s_lod%d.png' % (name, lv)))
        out = os.path.join(out_dir, name + '.trf')
        trfexp.export_trf_file(trf_meshes, out)
        summary[name] = {'hash': h, 'materials': len(parts), 'base_faces': base_faces,
                         'lod_faces': lod_faces, 'trf_bytes': os.path.getsize(out)}
        print('[scene-trf] %-40s 材质%2d 面%7d LOD=%s' % (name, len(parts), base_faces, lod_faces))
    except Exception as e:
        print('[scene-trf] FAIL %s: %r' % (name, e))
        summary[name] = {'hash': h, 'error': repr(e)}
    finally:
        for o in temp:
            try:
                bpy.data.objects.remove(o, do_unlink=True)
            except Exception:
                pass

json.dump(summary, open(os.path.join(out_dir, '_summary.json'), 'w', encoding='utf-8'),
          ensure_ascii=False, indent=1)
print('[scene-trf] DONE', len(summary))
