# 判定：骑砍2 女子身体的 V 领到底是「洞」还是「面」
#   ① 统计 V 区内是否有前表面（面中心 y>0.03 且在 z 1.41~1.50 之间）
#   ② 渲染：背面剔除 开/关 对照 —— 有洞则剔背后露出背景色
import bpy, bmesh, os, math, collections
from mathutils import Vector

OUT = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline\neck_probe"
B = OUT.replace("\\neck_probe", "")
BODY = B + r"\core_game\out\body\body_female_a.obj"
HEADV = B + r"\core_game\fbx\head\head_female_a.fbx"
OURS = r"D:/BrainMaker/blend_projects/tifa_export/backup_20260913/head_tifa_a_v10.fbx"


def patch():
    import io_scene_fbx.import_fbx as mod
    src = inspect_src = None
    import inspect
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)


def imp_fbx(p):
    import inspect
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


def imp_obj(p):
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=p, forward_axis='Y', up_axis='Z')
    bpy.context.view_layer.update()
    return [o for o in bpy.data.objects if o not in before and o.type == 'MESH']


bpy.ops.wm.read_factory_settings(use_empty=True)
body = imp_obj(BODY)[0]
mw = body.matrix_world
me = body.data
front = 0; back = 0; side = 0
for poly in me.polygons:
    c = Vector((0, 0, 0))
    for vi in poly.vertices:
        c += mw @ me.vertices[vi].co
    c /= len(poly.vertices)
    if 1.40 <= c.z <= 1.50 and abs(c.x) < 0.10:
        if c.y > 0.03:
            front += 1
        elif c.y < -0.03:
            back += 1
        else:
            side += 1
print("V 领区(z1.40~1.50, |x|<0.10) 面数统计：前面 y>0.03 = %d，后面 y<-0.03 = %d，中间 = %d" % (front, back, side))
print("   → 前面面数为 0 或极少 = V 区是【洞】（无前胸表面覆盖）")
# 法线朝向：看看 V 区里有没有朝后的面（= 从内部看到的胸腔后壁）
bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
inward = 0
for f in bm.faces:
    c = mw @ f.calc_center_median()
    if 1.40 <= c.z <= 1.50 and abs(c.x) < 0.10 and c.y > 0.0:
        n = (mw.to_3x3() @ f.normal).normalized()
        if n.y < 0:
            inward += 1
print("V 区内 y>0 且法线朝后的面（=内表面）= %d" % inward)

# 渲染对照
def setup(res=560):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_object_outline = True
    sc.render.resolution_x = res; sc.render.resolution_y = res
    sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.12, 0.12, 0.14)
    cd = bpy.data.cameras.new("cam"); cd.type = 'ORTHO'; cd.ortho_scale = 0.42
    cam = bpy.data.objects.new("cam", cd); sc.collection.objects.link(cam); sc.camera = cam
    return cam

def tint(objs, rgb):
    m = bpy.data.materials.new("t")
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    if b:
        b.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1)
    m.diffuse_color = (rgb[0], rgb[1], rgb[2], 1)
    for o in objs:
        o.data.materials.clear(); o.data.materials.append(m)

tint([body], (0.85, 0.65, 0.55))
cam = setup()
cam.location = (0, 3, 1.46); cam.rotation_euler = (math.radians(90), 0, math.radians(180))
bpy.context.scene.display.shading.show_backface_culling = False
bpy.context.scene.render.filepath = os.path.join(OUT, "t_body_nocull.png")
bpy.ops.render.render(write_still=True)
bpy.context.scene.display.shading.show_backface_culling = True
bpy.context.scene.render.filepath = os.path.join(OUT, "t_body_cull.png")
bpy.ops.render.render(write_still=True)

# 我们的头：低角度看底口
bpy.ops.wm.read_factory_settings(use_empty=True)
ms = imp_fbx(OURS)
tint([ms[0]], (0.85, 0.65, 0.55))
cam = setup()
cam.data.ortho_scale = 0.28
cam.location = (0, 1.5, 1.30); cam.rotation_euler = (math.radians(55), 0, math.radians(180))
bpy.context.scene.render.filepath = os.path.join(OUT, "t_ours_low.png")
bpy.ops.render.render(write_still=True)
cam.location = (0, -1.5, 1.30); cam.rotation_euler = (math.radians(55), 0, 0)
bpy.context.scene.render.filepath = os.path.join(OUT, "t_ours_lowback.png")
bpy.ops.render.render(write_still=True)
print("RENDER2 DONE")
