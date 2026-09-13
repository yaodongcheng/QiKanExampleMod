import bpy, sys
print("======== export_scene.fbx PARAMS ========")
props = bpy.ops.export_scene.fbx.get_rna_type().properties
names = [p.identifier for p in props if p.identifier not in ('rna_type',)]
print(" ".join(sorted(names)))
for key in ('global_scale','apply_unit_scale','apply_scale_options','axis_forward','axis_up',
            'object_types','use_mesh_modifiers','add_leaf_bones','use_armature_deform_only',
            'bake_anim','bake_anim_use_all_bones','mesh_smooth_type','use_tspace',
            'primary_bone_axis','secondary_bone_axis','armature_nodetype','embed_textures',
            'path_mode','use_custom_props','bake_space_transform','use_mesh_edges'):
    p = props.get(key)
    if p is not None:
        print("  %-28s default=%s" % (key, getattr(p, 'default', '?')))
sys.stdout.flush()
