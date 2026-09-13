# 轴线组合矩阵：找出让 Blender 写出「无节点旋转」的 axis_forward/axis_up。
# 缩放参数固定用已证实的 FBX_SCALE_UNITS（→ UnitScaleFactor=100、无节点缩放）。
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

AXES = [
    ('fY_uZ',    'Y',  'Z'),
    ('fZ_uY',    'Z',  'Y'),
    ('fX_uZ',    'X',  'Z'),
    ('fnegY_uZ', '-Y', 'Z'),
    ('fnegZ_uY', '-Z', 'Y'),
]

for name, fwd, up in AXES:
    path = os.path.join(OUTDIR, "a_%s.fbx" % name)
    try:
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=False, object_types={'MESH'},
            global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
            axis_forward=fwd, axis_up=up,
            use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False,
            mesh_smooth_type='OFF', use_tspace=False, path_mode='AUTO', embed_textures=False)
        print("EXPORTED %s" % path)
    except Exception as e:
        print("FAILED %s : %s" % (name, e))

sys.stdout.flush()
