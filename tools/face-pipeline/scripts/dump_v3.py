import bpy, sys, re

V3 = r"C:\Users\yaodongcheng\AppData\Local\Temp\lwn_fbx_preview\tifa_fit\head_tifa_a_v3.fbx"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=V3)

print("======== SCENE UNITS ========")
print("scale_length =", bpy.context.scene.unit_settings.scale_length)

print("======== OBJECTS ========")
for ob in bpy.data.objects:
    extra = ""
    if ob.type == 'MESH':
        me = ob.data
        mats = [ms.material.name if ms.material else "None" for ms in ob.material_slots]
        extra = " verts=%d polys=%d shapekeys=%d mats=%s" % (
            len(me.vertices), len(me.polygons),
            (len(ob.data.shape_keys.key_blocks) if ob.data.shape_keys else 0), mats)
    print("OBJ type=%-9s name=%-28s parent=%-20s%s" % (ob.type, ob.name, (ob.parent.name if ob.parent else "-"), extra))

print("======== SHAPE KEY NAMES (per mesh) ========")
for ob in bpy.data.objects:
    if ob.type != 'MESH' or not ob.data.shape_keys:
        continue
    names = [kb.name for kb in ob.data.shape_keys.key_blocks]
    kt = [n for n in names if re.match(r'^KeyTime_\d+$', n)]
    print("MESH %-24s total=%-4d KeyTime=%-4d first=%s last=%s" % (
        ob.name, len(names), len(kt), names[0] if names else "-", names[-1] if names else "-"))
    print("     names:", ",".join(names[:6]), "...", ",".join(names[-3:]))

print("======== BOUNDING (world) ========")
for ob in bpy.data.objects:
    if ob.type == 'MESH':
        import mathutils
        pts = [ob.matrix_world @ mathutils.Vector(c) for c in ob.bound_box]
        mn = [min(p[i] for p in pts) for i in range(3)]
        mx = [max(p[i] for p in pts) for i in range(3)]
        print("MESH %-24s min=(%.3f,%.3f,%.3f) max=(%.3f,%.3f,%.3f)" % (ob.name, mn[0],mn[1],mn[2], mx[0],mx[1],mx[2]))
sys.stdout.flush()
