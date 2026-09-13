# 从下方多角度看我们的头壳底口（相机用 to_track_quat 对准，避免手算欧拉角）
import bpy, os, math
from mathutils import Vector

OUT = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline\neck_probe"
OURS = r"D:/BrainMaker/blend_projects/tifa_export/backup_20260913/head_tifa_a_v10.fbx"
VANH = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline\core_game\fbx\head\head_female_a.fbx"


def _patch():
    import inspect
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)


_patch()


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
    sc.display.shading.show_backface_culling = False
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


TGT = (0.0, 0.035, 1.58)

bpy.ops.wm.read_factory_settings(use_empty=True)
ms = imp_fbx(OURS)
tint([ms[0]], (0.88, 0.68, 0.58))
cam = setup(0.26)
shoot(cam, "v_ours_frontlow", (0.0, 0.9, 1.20), TGT)
shoot(cam, "v_ours_lowfront45", (0.0, 0.6, 0.95), TGT)
shoot(cam, "v_ours_bottom", (0.0, 0.035, 0.80), TGT)
shoot(cam, "v_ours_backlow", (0.0, -0.9, 1.25), TGT)

bpy.ops.wm.read_factory_settings(use_empty=True)
ms = imp_fbx(VANH)
vh = max(ms, key=lambda o: len(o.data.vertices))
tint([vh], (0.88, 0.68, 0.58))
cam = setup(0.26)
shoot(cam, "v_van_frontlow", (0.0, 0.9, 1.20), TGT)
shoot(cam, "v_van_bottom", (0.0, 0.035, 0.80), TGT)
cam2 = setup(0.60)
shoot(cam2, "v_van_wide_side", (2.0, 0.0, 1.55), (0.0, 0.0, 1.55))
shoot(cam2, "v_ours_wide_side", (2.0, 0.0, 1.55), (0.0, 0.0, 1.55)) if False else None
print("RENDER4 DONE")
