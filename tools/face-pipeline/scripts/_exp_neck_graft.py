# 实验：把蒂法源 body 的脖子（换算到我们空间）直接接到我们 v10 头下，看图判断
import bpy, bmesh, os, math, inspect
from mathutils import Vector

OUT = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline\neck_probe"
TIFA = r"F:\下载\Tifa lockhart In Drees - FBX\Tifa.fbx"
OURS = r"D:/BrainMaker/blend_projects/tifa_export/backup_20260913/head_tifa_a_v10.fbx"
VANH = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline\core_game\fbx\head\head_female_a.fbx"
S, BY, BZ = 1.238, 0.01685, -0.2678
CUT = 1.505     # 脖子下沿先随便切一刀看效果


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


def setup(scale=0.30, res=600):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_object_outline = True
    sc.display.shading.show_backface_culling = True
    sc.render.resolution_x = res; sc.render.resolution_y = res
    sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.10, 0.10, 0.12)
    cd = bpy.data.cameras.new("cam"); cd.type = 'ORTHO'; cd.ortho_scale = scale
    cam = bpy.data.objects.new("cam", cd); sc.collection.objects.link(cam); sc.camera = cam
    return cam


def shoot(cam, name, loc, target):
    cam.location = Vector(loc)
    d = Vector(target) - Vector(loc)
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("  -> " + name)


# ---- 造脖子：蒂法 body 取 z>CUT 的部分，换算到我们空间 ----
bpy.ops.wm.read_factory_settings(use_empty=True)
ms = imp(TIFA)
tb = next(o for o in ms if o.name == 'body')
bpy.context.view_layer.objects.active = tb
for o in bpy.data.objects:
    o.select_set(o is tb)

# 先把顶点换算到我们空间（在世界矩阵之外再套一次线性变换：直接改 mesh 数据，world 保持原样）
me = tb.data
mw = tb.matrix_world
for v in me.vertices:
    p = mw @ v.co
    v.co = Vector((S * p.x, -S * p.y + BY, S * p.z + BZ))
tb.matrix_world = bpy.data.objects['body'].matrix_world.Identity(4) if False else __import__('mathutils').Matrix.Identity(4)
me.update()

bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
dead = [f for f in bm.faces if any(v.co.z < CUT for v in f.verts)]
bmesh.ops.delete(bm, geom=dead, context='FACES')
loose = [v for v in bm.verts if not v.link_faces]
if loose:
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
bm.to_mesh(me); bm.free(); me.update()
print("脖子网格：%d 顶点 %d 面" % (len(me.vertices), len(me.polygons)))
zs = [v.co.z for v in me.vertices]
ys = [v.co.y for v in me.vertices]
print("  z[%.4f,%.4f]  y[%.4f,%.4f]" % (min(zs), max(zs), min(ys), max(ys)))
neck = tb
tint([neck], (0.72, 0.80, 0.62))

# ---- 我们的头 ----
hs = imp(OURS)
tint([hs[0]], (0.88, 0.68, 0.58))
cam = setup(0.30)
shoot(cam, "g_tifa_neck_front", (0, 0.9, 1.45), (0, 0.03, 1.58))
shoot(cam, "g_tifa_neck_side", (0.9, 0.0, 1.45), (0, 0.03, 1.58))
shoot(cam, "g_tifa_neck_low", (0, 0.7, 1.15), (0, 0.03, 1.55))

# ---- 对照：原版头 ----
bpy.ops.wm.read_factory_settings(use_empty=True)
vs = imp(VANH)
vh = max(vs, key=lambda o: len(o.data.vertices))
tint([vh], (0.88, 0.68, 0.58))
cam = setup(0.30)
shoot(cam, "g_van_front", (0, 0.9, 1.45), (0, 0.03, 1.58))
shoot(cam, "g_van_side", (0.9, 0.0, 1.45), (0, 0.03, 1.58))
print("GRAFT EXP DONE")
