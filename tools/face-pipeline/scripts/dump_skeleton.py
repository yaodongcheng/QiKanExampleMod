import bpy, sys

SKEL = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\modding_resources\skeletons\human_skeleton.fbx"

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SKEL)

print("======== IMPORTED OBJECTS ========")
for ob in bpy.data.objects:
    print("OBJ  type=%-10s name=%s" % (ob.type, ob.name))

for ob in bpy.data.objects:
    if ob.type != 'ARMATURE':
        continue
    bones = ob.data.bones
    print("======== ARMATURE %s : %d bones ========" % (ob.name, len(bones)))
    for i, b in enumerate(bones):
        par = b.parent.name if b.parent else "-"
        print("BONE %3d  %-40s parent=%s" % (i, b.name, par))
    sys.stdout.flush()
