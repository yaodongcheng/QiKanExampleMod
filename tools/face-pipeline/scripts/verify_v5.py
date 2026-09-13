import bpy, sys, re, mathutils

path = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path)
bpy.context.view_layer.update()

print("FILE =", path)
arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
if arm:
    b = arm.data.bones.get("bip01_head_13")
    if b:
        h = arm.matrix_world @ b.head_local
        print("骨架: %d 骨  obj_scale=%s  头骨世界 z = %.5f" % (
            len(arm.data.bones), tuple(round(v,4) for v in arm.scale), h.z))

for ob in sorted([o for o in bpy.data.objects if o.type == 'MESH'], key=lambda o: o.name):
    me = ob.data
    pts = [ob.matrix_world @ mathutils.Vector(c) for c in ob.bound_box]
    mn = [min(p[i] for p in pts) for i in range(3)]
    mx = [max(p[i] for p in pts) for i in range(3)]
    nsk = len(me.shape_keys.key_blocks) if me.shape_keys else 0
    kt = len([kb for kb in me.shape_keys.key_blocks if re.match(r'^KeyTime_\d+$', kb.name)]) if me.shape_keys else 0
    print("MESH %-18s obj_scale=%-22s v=%-6d sk=%-3d kt=%-3d vg=%s" % (
        ob.name, tuple(round(v,3) for v in ob.scale), len(me.vertices), nsk, kt,
        ",".join(g.name for g in ob.vertex_groups)))
    print("     world bbox z = %.5f .. %.5f" % (mn[2], mx[2]))
sys.stdout.flush()
