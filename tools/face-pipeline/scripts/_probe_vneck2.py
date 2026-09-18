# 射线判定 V 领是洞还是面 + 从下方看我们头的底口
import bpy, bmesh, os, math, inspect
from mathutils import Vector
from mathutils.bvhtree import BVHTree

OUT = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline\自定义头\neck_probe"
B = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline\自定义头"
BODY = B + r"\core_game\out\body\body_female_a.obj"
OURS = r"D:/BrainMaker/blend_projects/tifa_export/backup_20260913/head_tifa_a_v10.fbx"


def imp_obj(p):
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=p, forward_axis='Y', up_axis='Z')
    bpy.context.view_layer.update()
    return [o for o in bpy.data.objects if o not in before and o.type == 'MESH']


def imp_fbx(p):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=p)
    bpy.context.view_layer.update()
    objs = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
    for o in objs:
        if o.animation_data:
            o.animation_data_clear()
        sk = o.data.shape_keys
        if sk:
            if sk.animation_data:
                sk.animation_data_clear()
            for kb in sk.key_blocks:
                kb.value = 0.0
        for m in list(o.modifiers):
            if m.type == 'ARMATURE':
                o.modifiers.remove(m)
    bpy.context.view_layer.update()
    return objs


bpy.ops.wm.read_factory_settings(use_empty=True)
body = imp_obj(BODY)[0]
mw = body.matrix_world
bm = bmesh.new(); bm.from_mesh(body.data)
bm.transform(mw)
bvh = BVHTree.FromBMesh(bm)
print("从正前方 (+Y) 朝 -Y 打射线，看第一击中的 y（>0.05=有胸面；<0=穿洞打到背内壁）")
print("   z       x     第一击中 y      命中数")
for z in (1.42, 1.44, 1.46, 1.48, 1.50):
    for x in (0.0, 0.04, -0.04, 0.08, -0.08):
        origin = Vector((x, 0.60, z)); d = Vector((0, -1, 0))
        hits = []
        o = origin.copy()
        for _ in range(6):
            r = bvh.ray_cast(o, d, 2.0)
            if r[0] is None:
                break
            hits.append(r[0].y)
            o = r[0] + d * 1e-4
        print("  %.3f  %+.3f   %s   %d" % (z, x, ("%+.4f" % hits[0]) if hits else "无命中", len(hits)))
bm.free()

# 从下方 45° 看我们头的底口
def setup(res=560, scale=0.30):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_object_outline = True
    sc.display.shading.show_backface_culling = False
    sc.render.resolution_x = res; sc.render.resolution_y = res
    sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.12, 0.12, 0.14)
    cd = bpy.data.cameras.new("cam"); cd.type = 'ORTHO'; cd.ortho_scale = scale
    cam = bpy.data.objects.new("cam", cd); sc.collection.objects.link(cam); sc.camera = cam
    return cam


def tint(objs, rgb):
    m = bpy.data.materials.new("t"); m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    if b:
        b.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1)
    m.diffuse_color = (rgb[0], rgb[1], rgb[2], 1)
    for o in objs:
        o.data.materials.clear(); o.data.materials.append(m)


bpy.ops.wm.read_factory_settings(use_empty=True)
ms = imp_fbx(OURS)
tint([ms[0]], (0.85, 0.65, 0.55))
cam = setup(560, 0.30)
# 前下方 40°
cam.location = (0, 1.2, 1.25); cam.rotation_euler = (math.radians(40), 0, math.radians(180))
bpy.context.scene.render.filepath = os.path.join(OUT, "t2_ours_below_front.png")
bpy.ops.render.render(write_still=True)
# 正下方
cam.location = (0, 0.04, 1.0); cam.rotation_euler = (0, 0, 0)
bpy.context.scene.render.filepath = os.path.join(OUT, "t2_ours_bottom.png")
bpy.ops.render.render(write_still=True)
# 后下方
cam.location = (0, -1.2, 1.25); cam.rotation_euler = (math.radians(40), 0, 0)
bpy.context.scene.render.filepath = os.path.join(OUT, "t2_ours_below_back.png")
bpy.ops.render.render(write_still=True)
print("RENDER3 DONE")
