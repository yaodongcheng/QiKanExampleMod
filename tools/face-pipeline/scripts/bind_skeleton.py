# 给蒂法头壳补骨架绑定 —— 只加蒙皮（顶点组 + Armature 修改器），不改几何/UV/形变键/材质槽。
# 依据：官方 human_skeleton 的骨骼名自带引擎编号 —— bip01_head_13 = 引擎骨骼 13（头骨）。
# 原生头 head_female_a 与 xxFemale 的脸部件（嘴/眼/睫）全部刚性绑在 13；主壳跨 11~13。
# 这里统一刚性绑 13（脸壳不含脖子，头骨足够）。

import bpy, sys, mathutils

SKEL = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\modding_resources\skeletons\human_skeleton.fbx"
V3   = r"C:\Users\yaodongcheng\AppData\Local\Temp\lwn_fbx_preview\tifa_fit\head_tifa_a_v3.fbx"
OUT  = r"C:\Users\yaodongcheng\AppData\Local\Temp\lwn_fbx_preview\tifa_fit\head_tifa_a_v4.fbx"
HEAD_BONE = "bip01_head_13"

bpy.ops.wm.read_factory_settings(use_empty=True)

# --- 1) 官方骨架 ---
bpy.ops.import_scene.fbx(filepath=SKEL)
arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
if arm is None:
    print("FATAL: 骨架导入失败"); sys.exit(1)
print("ARMATURE =", arm.name, "bones =", len(arm.data.bones))
if HEAD_BONE not in [b.name for b in arm.data.bones]:
    print("FATAL: 骨架里没有", HEAD_BONE); sys.exit(1)

# --- 2) 蒂法头壳 v3 ---
bpy.ops.import_scene.fbx(filepath=V3)
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
print("导入网格:", [m.name for m in meshes])

# --- 3) 逐网格绑骨（保留世界变换） ---
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
    ob.matrix_world = world          # 复位世界变换，几何位置不变
    print("绑定 %-22s 顶点组=%s 权重=1.0 顶点数=%d" % (ob.name, HEAD_BONE, len(ob.data.vertices)))

# --- 4) 清掉只作容器用的空节点（网格已改挂到骨架下） ---
for ob in [o for o in bpy.data.objects if o.type == 'EMPTY']:
    nm = ob.name
    bpy.data.objects.remove(ob, do_unlink=True)
    print("删除空节点:", nm)

# --- 5) 导出 ---
bpy.ops.export_scene.fbx(
    filepath=OUT,
    use_selection=False,
    object_types={'MESH', 'ARMATURE'},
    global_scale=1.0,
    apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_NONE',
    axis_forward='-Z',
    axis_up='Y',
    use_mesh_modifiers=False,      # 不烘焙修改器（避免把蒙皮烘死）
    add_leaf_bones=False,
    bake_anim=False,
    mesh_smooth_type='OFF',
    use_tspace=False,
    path_mode='AUTO',
    embed_textures=False,
)
print("EXPORTED ->", OUT)
sys.stdout.flush()
