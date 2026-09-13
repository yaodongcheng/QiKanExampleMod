# 脖子区域渲染（只读一次性探针）—— 把四份网格摆一起看图，别光看数字
import bpy, os, math, inspect
from mathutils import Vector

OUT = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline\neck_probe"
os.makedirs(OUT, exist_ok=True)

B = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline"
TIFA = r"F:\下载\Tifa lockhart In Drees - FBX\Tifa.fbx"
OURS = r"D:/BrainMaker/blend_projects/tifa_export/backup_20260913/head_tifa_a_v10.fbx"
VANH = B + r"\core_game\fbx\head\head_female_a.fbx"
XXH  = B + r"\face_probe\xx_fbx\head\head_xxfemale_a.fbx"
VANB = B + r"\core_game\out\body\body_female_a.obj"


def patch():
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)
patch()


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def imp_fbx(p):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=p)
    bpy.context.view_layer.update()
    objs = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
    # 🔴 TWT 的 morph 会带一条动画，Blender 默认停在当前帧 → 渲染出的是"被形变过的脸"。
    #    渲染前一律清动画 + 把形状键值归零（只留 Basis）。
    for o in objs:
        for a in list(o.animation_data or []):
            pass
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
    # tpaccli dump 出来的 OBJ 已经是 Z-up 米制 → 必须显式声明，否则导入器按 Y-up 转，模型躺倒跑出取景框
    bpy.ops.wm.obj_import(filepath=p, forward_axis='Y', up_axis='Z')
    bpy.context.view_layer.update()
    return [o for o in bpy.data.objects if o not in before and o.type == 'MESH']


COLORS = [(0.85, 0.65, 0.55), (0.45, 0.65, 0.85), (0.9, 0.85, 0.4), (0.6, 0.85, 0.55),
          (0.85, 0.5, 0.8), (0.5, 0.8, 0.8)]


def tint(objs, rgb, alpha=1.0):
    m = bpy.data.materials.new("tint")
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], alpha)
        if "Roughness" in bsdf.inputs:
            bsdf.inputs["Roughness"].default_value = 0.8
        if "Metallic" in bsdf.inputs:
            bsdf.inputs["Metallic"].default_value = 0.0
    m.diffuse_color = (rgb[0], rgb[1], rgb[2], alpha)
    for o in objs:
        o.data.materials.clear()
        o.data.materials.append(m)
        for p in o.data.polygons:
            p.material_index = 0


def setup(cx, cy, cz, scale, res=560):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_object_outline = True
    sc.render.resolution_x = res
    sc.render.resolution_y = res
    sc.render.film_transparent = False
    sc.world = bpy.data.worlds.new("w")
    sc.world.color = (0.12, 0.12, 0.14)
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = scale
    cam = bpy.data.objects.new("cam", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    return cam


def shoot(cam, name, view, cx, cy, cz, d=3.0):
    """view: 'front'(+Y 看向 -Y) / 'side'(+X) / 'q'(斜 45°)"""
    if view == 'front':
        cam.location = (cx, cy + d, cz)
        cam.rotation_euler = (math.radians(90), 0, math.radians(180))
    elif view == 'side':
        cam.location = (cx + d, cy, cz)
        cam.rotation_euler = (math.radians(90), 0, math.radians(90))
    else:
        a = math.radians(35)
        cam.location = (cx + d * math.sin(a), cy + d * math.cos(a), cz + d * 0.25)
        cam.rotation_euler = (math.radians(78), 0, math.radians(180 - 35))
    bpy.context.scene.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("  -> " + name)


# ---------- 1) 原版组合：BL 身体 + 原版头（目标形态） ----------
reset()
b = imp_obj(VANB); h = imp_fbx(VANH)
tint(b, (0.55, 0.58, 0.62)); tint(h, (0.85, 0.65, 0.55))
cam = setup(0, 0, 1.60, 0.42)
shoot(cam, "1_vanilla_front", 'front', 0, 0.03, 1.60)
shoot(cam, "1_vanilla_side", 'side', 0, 0.03, 1.60)
cam.data.ortho_scale = 0.75
shoot(cam, "1_vanilla_wide", 'front', 0, 0.03, 1.45)

# ---------- 2) 我们的现状：BL 身体 + 我们 v10 头 ----------
reset()
b = imp_obj(VANB); h = imp_fbx(OURS)
tint(b, (0.55, 0.58, 0.62)); tint(h, (0.85, 0.65, 0.55))
cam = setup(0, 0, 1.60, 0.42)
shoot(cam, "2_ours_front", 'front', 0, 0.03, 1.60)
shoot(cam, "2_ours_side", 'side', 0, 0.03, 1.60)
cam.data.ortho_scale = 0.75
shoot(cam, "2_ours_wide", 'front', 0, 0.03, 1.45)

# ---------- 3) 蒂法源：head + body（看她的脖子怎么长） ----------
reset()
ms = imp_fbx(TIFA)
head = [o for o in ms if o.name == 'head']
body = [o for o in ms if o.name == 'body']
tint(body, (0.55, 0.58, 0.62)); tint(head, (0.85, 0.65, 0.55))
cam = setup(0, 0, 1.50, 0.42)
shoot(cam, "3_tifa_src_front", 'front', 0, -0.03, 1.50)
shoot(cam, "3_tifa_src_side", 'side', 0, -0.03, 1.50)
cam.data.ortho_scale = 0.75
shoot(cam, "3_tifa_src_wide", 'front', 0, -0.03, 1.40)

# ---------- 4) xxFemale 头 + BL 身体 ----------
reset()
b = imp_obj(VANB); h = imp_fbx(XXH)
tint(b, (0.55, 0.58, 0.62))
for i, o in enumerate(h):
    tint([o], COLORS[i % len(COLORS)])
cam = setup(0, 0, 1.60, 0.42)
shoot(cam, "4_xx_front", 'front', 0, 0.03, 1.60)
shoot(cam, "4_xx_side", 'side', 0, 0.03, 1.60)
cam.data.ortho_scale = 0.75
shoot(cam, "4_xx_wide", 'front', 0, 0.03, 1.45)

print("RENDER DONE -> " + OUT)
