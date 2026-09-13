import bpy, sys, re, mathutils

path = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path)

print("FILE =", path)
print("scale_length =", bpy.context.scene.unit_settings.scale_length)

arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
print("ARMATURES = %d  %s" % (len(arms), [(a.name, len(a.data.bones)) for a in arms]))

for ob in sorted([o for o in bpy.data.objects if o.type == 'MESH'], key=lambda o: o.name):
    me = ob.data
    mats = [ms.material.name if ms.material else "None" for ms in ob.material_slots]
    nsk = len(me.shape_keys.key_blocks) if me.shape_keys else 0
    kt = 0
    if me.shape_keys:
        kt = len([kb for kb in me.shape_keys.key_blocks if re.match(r'^KeyTime_\d+$', kb.name)])
    pts = [ob.matrix_world @ mathutils.Vector(c) for c in ob.bound_box]
    mn = [min(p[i] for p in pts) for i in range(3)]
    mx = [max(p[i] for p in pts) for i in range(3)]
    vgs = [g.name for g in ob.vertex_groups]
    print("MESH %-20s v=%-6d p=%-6d sk=%-3d kt=%-3d mats=%-34s vg=%s" % (
        ob.name, len(me.vertices), len(me.polygons), nsk, kt, ",".join(mats), ",".join(vgs)))
    print("     bbox=(%.3f,%.3f,%.3f)..(%.3f,%.3f,%.3f)" % (mn[0], mn[1], mn[2], mx[0], mx[1], mx[2]))
sys.stdout.flush()
