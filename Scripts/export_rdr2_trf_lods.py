"""FBX → 带 LOD 链的 .trf（给 ModKit 导入，引擎按距离切档）。

为什么从 FBX 出发而不是 glTF：
  · 我们的 FBX 已经是 **Z-up、底面中心=原点、材质名统一** 的成品（上次倒过来就是坐标转换写反的锅），
    直接当 LOD0 母本最稳，不用再碰 glTF 瓦片。

口径（全部来自实测）：
  · 引擎 LOD 距离：0:8m 1:12m 2:16m 3:27m 4:38m 5:71m 6:116m（更远用最后一档）
  · 减面比率抄原版帝国建筑包统计（459 个多档网格的中位）：
        100% → 50% → 30% → 19% → 11% → 5% → 2.5% → 0.8%
  · 🔴 保留材质分组：先按材质拆子网格，再逐个 Decimate（原版每档子网格数几乎不变）。

命名：`<模型>.<i>` = LOD0；`<模型>.lod<N>.<i>` = 第 N 档。

用法（Blender 无头）：
    blender -b -P export_rdr2_trf_lods.py -- <fbx_list.json> <out_dir> [preview_dir]
        fbx_list.json = ["H:/.../a.fbx", "H:/.../b.fbx", ...]
"""
import importlib.util
import json
import os
import sys

import bpy
import mathutils

ROOT = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
EXPORTER = os.path.join(ROOT, "tools", "OpenTrf", "trf_meta_mesh_exporter.py")

# 实测：清理后 collapse 到 ~11% 已经极限（再低屋顶会拉长刺），所以只做 5 档，最后一档到无穷远
RATIOS = [1.0, 0.50, 0.30, 0.19, 0.12]
MIN_FACES = 8

argv = sys.argv[sys.argv.index("--") + 1:]
list_json, out_dir = argv[0], argv[1]
preview_dir = argv[2] if len(argv) > 2 else None
# 🔴 相对路径一律按工程根解析：Blender 的 render.filepath 是以它自己的 CWD 为基准的，
#    传相对路径会写到 C:\ 下面去（实测踩过一次）。
if not os.path.isabs(list_json):
    list_json = os.path.join(ROOT, list_json)
if not os.path.isabs(out_dir):
    out_dir = os.path.join(ROOT, out_dir)
if preview_dir and not os.path.isabs(preview_dir):
    preview_dir = os.path.join(ROOT, preview_dir)
fbx_list = json.load(open(list_json, encoding="utf-8"))
os.makedirs(out_dir, exist_ok=True)
if preview_dir:
    os.makedirs(preview_dir, exist_ok=True)

spec = importlib.util.spec_from_file_location("trfexp", EXPORTER)
trfexp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(trfexp)


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
    """🔴 减面前必须清理：FBX 里顶点按 UV 缝拆开（三角形各自为岛），
    不合并就 collapse 会把屋顶撕出洞（2026-10-26 实测）。"""
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=dist)
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')


def decimate(o, ratio):
    faces = len(o.data.polygons)
    if ratio >= 0.999 or faces < MIN_FACES:
        return faces
    cleanup(o)
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    mod = o.modifiers.new(name="lod", type='DECIMATE')
    mod.decimate_type = 'COLLAPSE'
    mod.ratio = max(0.01, ratio)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return len(o.data.polygons)


def render_preview(out_png):
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_WORKBENCH'
    scn.render.resolution_x = 720
    scn.render.resolution_y = 540
    scn.display.shading.light = 'STUDIO'
    scn.display.shading.color_type = 'SINGLE'
    scn.display.shading.single_color = (0.72, 0.70, 0.66)
    scn.render.filepath = out_png
    bpy.ops.render.render(write_still=True)


def frame_camera(objs):
    mn = mathutils.Vector((1e9, 1e9, 1e9))
    mx = mathutils.Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            for i in range(3):
                mn[i] = min(mn[i], w[i]); mx[i] = max(mx[i], w[i])
    c = (mn + mx) * 0.5
    r = max((mx - mn).length * 0.9, 1.0)
    cam = bpy.data.cameras.new("cam")
    co = bpy.data.objects.new("cam", cam)
    bpy.context.scene.collection.objects.link(co)
    co.location = c + mathutils.Vector((r * 0.8, -r * 0.8, r * 0.55))
    co.rotation_euler = (c - co.location).normalized().to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = co


summary = {}
for fbx in fbx_list:
    name = os.path.splitext(os.path.basename(fbx))[0]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    try:
        bpy.ops.import_scene.fbx(filepath=fbx, axis_forward='Y', axis_up='Z')
    except Exception as e:
        print("FAIL_IMPORT", name, e)
        continue
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    if not meshes:
        print("FAIL_EMPTY", name)
        continue
    for o in meshes:
        o.matrix_world = mathutils.Matrix.Identity(4)

    parts = split_by_material(meshes[0])
    originals = [p.data for p in parts]
    for i, p in enumerate(parts):
        p.name = "%s.%d" % (name, i)

    trf_meshes, lod_faces = [], {}
    for lv, ratio in enumerate(RATIOS):
        for i, p in enumerate(parts):
            p.data = originals[i].copy()
            decimate(p, ratio)
            p.name = ("%s.%d" % (name, i)) if lv == 0 else ("%s.lod%d.%d" % (name, lv, i))
        lod_faces[lv] = sum(len(p.data.polygons) for p in parts)
        for p in parts:
            trf_meshes.append(trfexp.create_trf_mesh(p))
        if preview_dir and lv in (0, 3, 5):
            frame_camera(parts)
            render_preview(os.path.join(preview_dir, "%s_lod%d.png" % (name, lv)))
            for c in [o for o in bpy.data.objects if o.type == 'CAMERA']:
                bpy.data.objects.remove(c, do_unlink=True)

    out = os.path.join(out_dir, name + ".trf")
    trfexp.export_trf_file(trf_meshes, out)
    summary[name] = lod_faces
    print("TRF %-34s 子网格=%d LOD=%s" % (name, len(parts), lod_faces))

json.dump(summary, open(os.path.join(out_dir, "_lod_faces.json"), "w", encoding="utf-8"), indent=1)
print("DONE", len(summary))
