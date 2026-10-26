"""Blender 侧：把一个预览瓦片里的若干模型各导出成一个 FBX（给 ModKit 导入）。

由 export_rdr2_bld_fbx.py 调用，不单独用。

关键口径（和旧脚本 export_sdn_fbx.py 的差别，别改回去）：
  🔴 **不烘实例的平移/旋转**。glTF 的顶点已经是「模型本地坐标」（Y-up），
     节点的 translation/rotation 只是它在大地图上的摆位。烘进去 = 同一个模型
     在不同朝向的实例上会转错（旧脚本对 3 栋样板楼没暴露，是因为那几栋 yaw 特殊）。
     这里只做两件事：① Y-up → Z-up（绕 X +90°）② 平移到「底面中心 = 原点」。
  · 材质名去掉 Blender 的 `.001` 去重后缀，并应用大小写规范化表（Windows 大小写不敏感）。
"""
import json
import math
import os
import sys

import bpy
import mathutils
import hashlib

# 🔴 Blender 导 FBX 时材质名超过 63 字符会被**截断并加哈希**
#    （实测：`nbx_overlays_plastercorner_01_abnbx_overlays_plastercorner_01_al`
#      → `nbx_overlays_plastercorner_01_abnbx_overlays_plastercor_3433e6a`），
#    截断后与工程里的材质资产对不上 ⇒ 子网格挂空。这里先自己截到 55 字符，
#    生成材质时用**同一个函数**，两边必须一致（在 gen_rdr2_mat_aliases.py 里）。
SHORT_LIMIT = 55


def short_name(name):
    if len(name) <= SHORT_LIMIT:
        return name
    h = hashlib.md5(name.encode("utf-8")).hexdigest()[:8]
    return name[:SHORT_LIMIT - 9] + "_" + h

argv = sys.argv[sys.argv.index("--") + 1:]
gltf, names_json, out_dir, matmap_json = argv[0], argv[1], argv[2], argv[3]
names = json.load(open(names_json, encoding="utf-8"))
matmap = json.load(open(matmap_json, encoding="utf-8")) if os.path.isfile(matmap_json) else {}
os.makedirs(out_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=gltf)
scene = bpy.context.scene

# 对象名 → 基名（Blender 对重名会加 .001）
by_name = {}
for o in bpy.data.objects:
    if o.type != 'MESH':
        continue
    base = o.name
    if '.' in base:
        head, tail = base.rsplit('.', 1)
        if tail.isdigit():
            base = head
    by_name.setdefault(base, []).append(o)

# 🔴 Y-up(glTF) → Z-up(Blender) 的转换**直接取父级空物体 `RDR2_Zup` 的世界矩阵**，
#    不要手写 ±90°：实测这个 importer 给的是 (x, y, z) → (x, z, -y)。
#    手写成 +(x,-z,y) 会让模型**上下颠倒**（2026-10-25 实机：楼是倒着立起来的）。
def global_conv(o):
    p = o.parent
    if p is None:
        return mathutils.Matrix.Identity(4)
    m = p.matrix_world.copy()
    m.translation = mathutils.Vector((0.0, 0.0, 0.0))
    return m


def canonical_material_name(m):
    base = m.name
    if '.' in base:
        head, tail = base.rsplit('.', 1)
        if tail.isdigit():
            base = head
    return short_name(matmap.get(base, base))


def export_one(want, objs):
    o = objs[0]
    me = o.data.copy()                      # 🔴 复制：同一 glTF mesh 可能被多个节点共享
    me.transform(global_conv(o))
    if len(me.vertices) == 0:
        bpy.data.meshes.remove(me)
        print("EMPTY", want)
        return False
    co = [v.co for v in me.vertices]
    mn = mathutils.Vector((min(v.x for v in co), min(v.y for v in co), min(v.z for v in co)))
    mx = mathutils.Vector((max(v.x for v in co), max(v.y for v in co), max(v.z for v in co)))
    base = mathutils.Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))
    me.transform(mathutils.Matrix.Translation(-base))

    # 材质名规范化（按名字合并 .001）
    for i, m in enumerate(me.materials):
        if m is None:
            continue
        tgt = canonical_material_name(m)
        if tgt != m.name:
            found = bpy.data.materials.get(tgt)
            if found is None:
                m.name = tgt
                found = m
            me.materials[i] = found

    tmp = bpy.data.objects.new(want, me)
    scene.collection.objects.link(tmp)
    tmp.matrix_world = mathutils.Matrix.Identity(4)

    bpy.ops.object.select_all(action='DESELECT')
    tmp.select_set(True)
    bpy.context.view_layer.objects.active = tmp
    out = os.path.join(out_dir, want + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=out,
        use_selection=True,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='Y', axis_up='Z',      # 骑砍要 Z-up
        object_types={'MESH'},
        mesh_smooth_type='FACE',
        use_mesh_modifiers=False,
        # 🔴 STRIP：不把贴图拷成 <模型>.fbm/ —— 编辑器按**材质名**绑定工程里的材质资产，
        #    贴图文件一个都用不上；COPY 会让每个模型旁边多一个 2~3 MB 的贴图副本（5 GB 垃圾）。
        path_mode='STRIP',
        embed_textures=False,
        bake_space_transform=False,
    )
    bpy.data.objects.remove(tmp, do_unlink=True)
    bpy.data.meshes.remove(me)
    print("OK %s  size=%.2f,%.2f,%.2f  verts=%d"
          % (want, mx.x - mn.x, mx.y - mn.y, mx.z - mn.z, len(co)))
    return True


done = 0
for want in names:
    objs = by_name.get(want)
    if not objs:
        print("MISS", want)
        continue
    if export_one(want, objs):
        done += 1
print("TILE_DONE exported=%d requested=%d" % (done, len(names)))
