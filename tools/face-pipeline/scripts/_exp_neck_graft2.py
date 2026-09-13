# 实验2：同一个场景里放【我们的头 + 蒂法源脖子(换算后裁好)】，渲染判断接合
import bpy, bmesh, os, math, inspect
from mathutils import Vector, Matrix

OUT = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline\neck_probe"
TIFA = r"F:\下载\Tifa lockhart In Drees - FBX\Tifa.fbx"
OURS = r"D:/BrainMaker/blend_projects/tifa_export/backup_20260913/head_tifa_a_v10.fbx"
S, BY, BZ = 1.238, 0.01685, -0.2678
CUT = 1.500


def patch():
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)
patch()


def imp(p):
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


def tint(objs, rgb):
    m = bpy.data.materials.new("t"); m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    if b:
        b.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1)
    m.diffuse_color = (rgb[0], rgb[1], rgb[2], 1)
    for o in objs:
        o.data.materials.clear(); o.data.materials.append(m)


def setup(scale=0.30, res=600, cull=True):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_object_outline = True
    sc.display.shading.show_backface_culling = cull
    sc.render.resolution_x = res; sc.render.resolution_y = res
    sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.10, 0.10, 0.12)
    cd = bpy.data.cameras.new("cam"); cd.type = 'ORTHO'; cd.ortho_scale = scale
    cam = bpy.data.objects.new("cam", cd); sc.collection.objects.link(cam); sc.camera = cam
    return cam


def shoot(cam, name, loc, target):
    cam.location = Vector(loc)
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("  -> " + name)


bpy.ops.wm.read_factory_settings(use_empty=True)

heads = imp(OURS)
head = heads[0]                      # head_tifa_a.0 脸壳
tint([head], (0.88, 0.68, 0.58))

src = imp(TIFA)
tb = next(o for o in src if o.name == 'body')
other = [o for o in src if o is not tb]
for o in other:
    bpy.data.objects.remove(o, do_unlink=True)

# 换算到我们空间
mw = tb.matrix_world
me = tb.data
for v in me.vertices:
    p = mw @ v.co
    v.co = Vector((S * p.x, -S * p.y + BY, S * p.z + BZ))
tb.matrix_world = Matrix.Identity(4)
me.update()

# 裁掉 CUT 以下
bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
dead = [f for f in bm.faces if any(v.co.z < CUT for v in f.verts)]
bmesh.ops.delete(bm, geom=dead, context='FACES')
loose = [v for v in bm.verts if not v.link_faces]
if loose:
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
bm.to_mesh(me); bm.free(); me.update()
print("脖子网格：%d 顶点 %d 面" % (len(me.vertices), len(me.polygons)))
zs = [v.co.z for v in me.vertices]
print("  z[%.4f,%.4f]" % (min(zs), max(zs)))
tint([tb], (0.60, 0.82, 0.55))

cam = setup(0.30)
shoot(cam, "h_neck_front", (0, 0.9, 1.45), (0, 0.03, 1.58))
shoot(cam, "h_neck_side", (0.9, 0.0, 1.45), (0, 0.03, 1.58))
cam2 = setup(0.22)
shoot(cam2, "h_neck_side_close", (0.9, 0.0, 1.50), (0, 0.03, 1.56))
shoot(cam2, "h_neck_3q", (0.7, 0.7, 1.40), (0, 0.03, 1.56))
shoot(cam2, "h_neck_back", (0, -0.9, 1.45), (0, 0.0, 1.56))
print("EXP2 DONE")
