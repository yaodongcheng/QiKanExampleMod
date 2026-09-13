import bpy, sys, mathutils

SKEL = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\modding_resources\skeletons\human_skeleton.fbx"
V4   = r"C:\Users\yaodongcheng\AppData\Local\Temp\lwn_fbx_preview\tifa_fit\head_tifa_a_v4.fbx"

bpy.ops.wm.read_factory_settings(use_empty=True)

# --- 骨架单独导入，量头骨位置 ---
bpy.ops.import_scene.fbx(filepath=SKEL)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
print("ARMATURE obj matrix_world:")
for row in arm.matrix_world:
    print("   ", ["%.4f" % v for v in row])
print("ARMATURE scale =", tuple(round(v, 4) for v in arm.scale))
for b in arm.data.bones:
    if b.name in ("bip01_head_13", "bip01_neck_12", "bip01_spine2_11", "bip01_pelvis_0"):
        h = arm.matrix_world @ b.head_local
        t = arm.matrix_world @ b.tail_local
        print("BONE %-20s head=(%.4f,%.4f,%.4f)  tail=(%.4f,%.4f,%.4f)" % (b.name, h.x, h.y, h.z, t.x, t.y, t.z))

# --- v4 网格位置 ---
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=V4)
print()
for ob in sorted([o for o in bpy.data.objects if o.type == 'MESH'], key=lambda o: o.name):
    print("MESH %-18s obj_scale=%s" % (ob.name, tuple(round(v, 4) for v in ob.scale)))
    print("     matrix_world:")
    for row in ob.matrix_world:
        print("       ", ["%.4f" % v for v in row])
    pts = [ob.matrix_world @ mathutils.Vector(c) for c in ob.bound_box]
    mn = [min(p[i] for p in pts) for i in range(3)]
    mx = [max(p[i] for p in pts) for i in range(3)]
    print("     world bbox=(%.4f,%.4f,%.4f)..(%.4f,%.4f,%.4f)" % (mn[0], mn[1], mn[2], mx[0], mx[1], mx[2]))
    # 局部坐标（顶点原始值）
    lmn = [min(v.co[i] for v in ob.data.vertices) for i in range(3)]
    lmx = [max(v.co[i] for v in ob.data.vertices) for i in range(3)]
    print("     local bbox=(%.4f,%.4f,%.4f)..(%.4f,%.4f,%.4f)" % (lmn[0], lmn[1], lmn[2], lmx[0], lmx[1], lmx[2]))
arm2 = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
if arm2:
    print("v4 ARMATURE matrix_world:")
    for row in arm2.matrix_world:
        print("   ", ["%.4f" % v for v in row])
sys.stdout.flush()
