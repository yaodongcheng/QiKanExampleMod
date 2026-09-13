# 复现编辑器里看到的领口现象：单独渲染 v11 头（不带身体），同角度同材质
import bpy, os, math, inspect
from mathutils import Vector

OUT = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline\neck_probe"
P = r"H:/SteamLibrary/steamapps/common/MB2_Version/MB2_1.2.12/Mount & Blade II Bannerlord/Modules/TifaHead2/AssetSources/head_tifa_a_v11.fbx"
TDIR = r"H:/SteamLibrary/steamapps/common/MB2_Version/MB2_1.2.12/Mount & Blade II Bannerlord/Modules/TifaHead2/AssetSources/import_ready"
TEX = {"head_tifa_a": "head_tifa_a_d.png", "head_tifa_a_mouth": "head_tifa_a_mouth_d.png",
       "head_tifa_a_lash": "head_tifa_a_lash_d.png", "head_tifa_a_eye": "head_tifa_a_eye_d.png"}


def patch():
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)
patch()

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=P)
bpy.context.view_layer.update()
objs = [o for o in bpy.data.objects if o.type == 'MESH']
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
for a in [o for o in bpy.data.objects if o.type == 'ARMATURE']:
    a.data.pose_position = 'REST'
bpy.context.view_layer.update()

# 把每个材质换成"带该贴图的 workbench 材质"
for o in objs:
    for i, m in enumerate(o.data.materials):
        if m is None or m.name not in TEX:
            continue
        img = bpy.data.images.load(os.path.join(TDIR, TEX[m.name]))
        nt = bpy.data.node_groups.new("nt", 'ShaderNodeTree')
        nm = bpy.data.materials.new("wb_" + m.name)
        nm.use_nodes = True
        bsdf = nm.node_tree.nodes.get("Principled BSDF")
        tx = nm.node_tree.nodes.new("ShaderNodeTexImage")
        tx.image = img
        nm.node_tree.links.new(tx.outputs["Color"], bsdf.inputs["Base Color"])
        nm.diffuse_color = (1, 1, 1, 1)
        o.data.materials[i] = nm
print("材质：%s" % [[m.name if m else None for m in o.data.materials] for o in objs])

# 领口的法线朝向自检：统计领口区域朝内/朝外的面
import bmesh
shell = next(o for o in objs if o.name.endswith(".0"))
bm = bmesh.new(); bm.from_mesh(shell.data); bm.transform(shell.matrix_world)
bm.faces.ensure_lookup_table()
inward = 0; outward = 0
for f in bm.faces:
    c = f.calc_center_median()
    if 1.40 <= c.z < 1.545:
        rad = Vector((c.x, c.y - 0.02, 0.0))
        if rad.length < 1e-6:
            continue
        (outward if f.normal.dot(rad.normalized()) > 0 else inward).__class__
        if f.normal.dot(rad.normalized()) > 0:
            outward += 1
        else:
            inward += 1
print("领口区(z1.40~1.545) 面法线：朝外 %d ／ 朝内(反了) %d" % (outward, inward))
bm.free()

sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'STUDIO'
sc.display.shading.color_type = 'TEXTURE'
sc.display.shading.show_object_outline = True
sc.display.shading.show_backface_culling = False
sc.render.resolution_x = 720; sc.render.resolution_y = 720
sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.45, 0.5, 0.35)
cd = bpy.data.cameras.new("cam"); cd.type = 'ORTHO'
cam = bpy.data.objects.new("cam", cd); sc.collection.objects.link(cam); sc.camera = cam


def shoot(name, loc, target, scale):
    cd.ortho_scale = scale
    cam.location = Vector(loc)
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("  -> " + name)


shoot("r_v11_3q", (0.9, 1.1, 1.55), (0, 0.03, 1.55), 0.40)      # 编辑器同款 3/4 角
shoot("r_v11_3q_low", (0.9, 1.1, 1.30), (0, 0.03, 1.48), 0.40)
shoot("r_v11_front", (0, 1.6, 1.50), (0, 0.03, 1.50), 0.40)
# 背面剔除打开，看法线朝向（漏面会变背景色）
sc.display.shading.show_backface_culling = True
shoot("r_v11_3q_cull", (0.9, 1.1, 1.55), (0, 0.03, 1.55), 0.40)
print("REPRO DONE")
