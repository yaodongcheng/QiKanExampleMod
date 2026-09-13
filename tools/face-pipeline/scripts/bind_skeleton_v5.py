# v5：在 v4 基础上修"骨架与网格不同尺度"——导入骨架后先量出尺度差，缩放到与网格一致再绑。
# 实测：官方骨架导入后 bip01_head_13 世界 z = 0.0157（厘米级），头网格世界 z = 1.466~1.673（米级），差 100×。

import bpy, sys, mathutils

SKEL = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\modding_resources\skeletons\human_skeleton.fbx"
V4   = r"C:\Users\yaodongcheng\AppData\Local\Temp\lwn_fbx_preview\tifa_fit\head_tifa_a_v4.fbx"
OUT  = r"C:\Users\yaodongcheng\AppData\Local\Temp\lwn_fbx_preview\tifa_fit\head_tifa_a_v5.fbx"
HEAD_BONE = "bip01_head_13"

def world_bbox(ob):
    pts = [ob.matrix_world @ mathutils.Vector(c) for c in ob.bound_box]
    return ([min(p[i] for p in pts) for i in range(3)], [max(p[i] for p in pts) for i in range(3)])

bpy.ops.wm.read_factory_settings(use_empty=True)

# ---------- 1) 骨架 ----------
bpy.ops.import_scene.fbx(filepath=SKEL)
bpy.context.view_layer.update()
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
bone = arm.data.bones[HEAD_BONE]
if not bone:
    print("FATAL: 无", HEAD_BONE); sys.exit(1)
head_z_before = (arm.matrix_world @ bone.head_local).z
print("骨架: obj_scale=%s  头骨世界 z = %.5f" % (tuple(round(v,4) for v in arm.scale), head_z_before))

# ---------- 2) 网格 ----------
bpy.ops.import_scene.fbx(filepath=V4)
bpy.context.view_layer.update()
meshes = sorted([o for o in bpy.data.objects if o.type == 'MESH'], key=lambda o: o.name)
main = next(o for o in meshes if o.name.endswith(".0"))
mn, mx = world_bbox(main)
mesh_cz = (mn[2] + mx[2]) / 2.0
print("网格: %s obj_scale=%s 世界 z = %.5f .. %.5f (中心 %.5f)" % (
    main.name, tuple(round(v,4) for v in main.scale), mn[2], mx[2], mesh_cz))

# ---------- 3) 把骨架缩放到网格尺度，并把变换烘进数据 ----------
k = mesh_cz / head_z_before
print("尺度比 k = %.4f（网格中心 / 头骨）" % k)
for ob in bpy.data.objects:
    ob.select_set(False)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
arm.scale = tuple(s * k for s in arm.scale)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.context.view_layer.update()
head_z_after = (arm.matrix_world @ arm.data.bones[HEAD_BONE].head_local).z
print("缩放后: obj_scale=%s  头骨世界 z = %.5f" % (tuple(round(v,4) for v in arm.scale), head_z_after))

# ---------- 4) 绑骨 ----------
for ob in meshes:
    world = ob.matrix_world.copy()
    vg = ob.vertex_groups.get(HEAD_BONE) or ob.vertex_groups.new(name=HEAD_BONE)
    vg.add([v.index for v in ob.data.vertices], 1.0, 'REPLACE')
    if not any(m.type == 'ARMATURE' for m in ob.modifiers):
        md = ob.modifiers.new(name="Armature", type='ARMATURE')
        md.object = arm
        md.use_vertex_groups = True
    ob.parent = arm
    ob.parent_type = 'OBJECT'
    ob.matrix_world = world
    print("绑定 %-18s 顶点=%d" % (ob.name, len(ob.data.vertices)))

for ob in [o for o in bpy.data.objects if o.type == 'EMPTY']:
    bpy.data.objects.remove(ob, do_unlink=True)
bpy.context.view_layer.update()

# ---------- 5) 导出 ----------
bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=False, object_types={'MESH', 'ARMATURE'},
    global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
    axis_forward='-Z', axis_up='Y', use_mesh_modifiers=False,
    add_leaf_bones=False, bake_anim=False, mesh_smooth_type='OFF',
    use_tspace=False, path_mode='AUTO', embed_textures=False,
)
print("EXPORTED ->", OUT)
sys.stdout.flush()
