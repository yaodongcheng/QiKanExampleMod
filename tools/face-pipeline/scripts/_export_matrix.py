# 导出参数矩阵实测：找出让 Blender 写出「UnitScaleFactor=100 + 节点无缩放/无旋转」的组合。
# 只搭一次场景，然后按不同参数组合各导一份，供外部 fbx_probe.py 逐个核对。
import bpy, sys, mathutils, os

V5 = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\TifaHead2\AssetSources\head_tifa_a_v5.fbx"
OUTDIR = r"C:\Users\yaodongcheng\AppData\Local\Temp\lwn_fbx_preview\export_matrix"
os.makedirs(OUTDIR, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=V5)
bpy.context.view_layer.update()
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
for ob in [o for o in bpy.data.objects if o.type != 'MESH']:
    bpy.data.objects.remove(ob, do_unlink=True)
for ob in meshes:
    for md in [m for m in ob.modifiers if m.type == 'ARMATURE']:
        ob.modifiers.remove(md)
    ob.parent = None
    ob.matrix_world = mathutils.Matrix.Identity(4)

bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1.0
bpy.context.view_layer.update()

print("场景单位 scale_length = %s" % bpy.context.scene.unit_settings.scale_length)
for ob in meshes:
    m = ob.matrix_world
    print("  %s  loc=%s scale=%s" % (ob.name,
          tuple(round(m[i][3], 6) for i in range(3)),
          tuple(round(mathutils.Vector((m[0][0], m[1][1], m[2][2])).length, 6) for _ in [0])))

COMBOS = [
    ("us_true_none",  dict(apply_unit_scale=True,  apply_scale_options='FBX_SCALE_NONE')),
    ("us_false_none", dict(apply_unit_scale=False, apply_scale_options='FBX_SCALE_NONE')),
    ("us_true_units", dict(apply_unit_scale=True,  apply_scale_options='FBX_SCALE_UNITS')),
    ("us_false_units",dict(apply_unit_scale=False, apply_scale_options='FBX_SCALE_UNITS')),
    ("us_true_all",   dict(apply_unit_scale=True,  apply_scale_options='FBX_SCALE_ALL')),
]

for name, extra in COMBOS:
    path = os.path.join(OUTDIR, "m_%s.fbx" % name)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=False, object_types={'MESH'},
        global_scale=1.0, axis_forward='-Y', axis_up='Z',
        use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False,
        mesh_smooth_type='OFF', use_tspace=False, path_mode='AUTO', embed_textures=False,
        **extra)
    print("EXPORTED %s" % path)

# 再测一组：轴线声明用 Y-up（Blender 默认），看引擎友好的到底是哪种
for name, extra in [("yup_us_false_none", dict(apply_unit_scale=False, apply_scale_options='FBX_SCALE_NONE'))]:
    path = os.path.join(OUTDIR, "m_%s.fbx" % name)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=False, object_types={'MESH'},
        global_scale=1.0, axis_forward='-Z', axis_up='Y',
        use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False,
        mesh_smooth_type='OFF', use_tspace=False, path_mode='AUTO', embed_textures=False,
        **extra)
    print("EXPORTED %s" % path)

sys.stdout.flush()
