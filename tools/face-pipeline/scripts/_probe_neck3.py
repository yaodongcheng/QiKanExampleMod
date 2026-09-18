# 并排量：蒂法源 body 脖子 vs 原版 head_female_a 脖子（同一 z，同一算法）
# 目的：确认两个脖子在 y 方向的错位到底有多大（决定接法）
import bpy, inspect, statistics
from mathutils import Vector

def patch():
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)
patch()

B = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline\自定义头"
TIFA = r"F:\下载\Tifa lockhart In Drees - FBX\Tifa.fbx"
VANH = B + r"\core_game\fbx\head\head_female_a.fbx"
SKEL = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_wEditor\modding_resources\skeletons\human_skeleton.fbx"
S, BY, BZ = 1.238, 0.01685, -0.2678


def imp(p):
    bpy.ops.wm.read_factory_settings(use_empty=True)
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


print("=" * 96)
print("A. 蒂法源 body 脖子（已换算到我们空间）—— 逐 5mm 截面")
print("=" * 96)
ms = imp(TIFA)
tb = next(o for o in ms if o.name == 'body')
mw = tb.matrix_world
pts = [Vector((S * (mw @ v.co).x, -S * (mw @ v.co).y + BY, S * (mw @ v.co).z + BZ)) for v in tb.data.vertices]
print(" 我们z   源z     n    x范围             x宽     y范围             y深    y中心")
z = 1.575
while z > 1.495:
    band = [p for p in pts if z - 0.005 <= p.z < z]
    if band:
        print(" %.4f %.4f  %3d  [%+.4f,%+.4f] %.4f  [%+.4f,%+.4f] %.4f  %+.4f"
              % (z - 0.0025, (z - 0.0025 - BZ) / S, len(band),
                 min(p.x for p in band), max(p.x for p in band), max(p.x for p in band) - min(p.x for p in band),
                 min(p.y for p in band), max(p.y for p in band), max(p.y for p in band) - min(p.y for p in band),
                 (min(p.y for p in band) + max(p.y for p in band)) / 2))
    z -= 0.005

print()
print("=" * 96)
print("B. 蒂法源 head 底口 + 我们的头底口（同一空间）")
print("=" * 96)
th = next(o for o in ms if o.name == 'head')
mw = th.matrix_world
hpts = [Vector((S * (mw @ v.co).x, -S * (mw @ v.co).y + BY, S * (mw @ v.co).z + BZ)) for v in th.data.vertices]
z = 1.60
while z > 1.53:
    band = [p for p in hpts if z - 0.005 <= p.z < z]
    if band:
        print(" 源head 我们z %.4f  n=%3d x[%+.4f,%+.4f] y[%+.4f,%+.4f]"
              % (z - 0.0025, len(band), min(p.x for p in band), max(p.x for p in band),
                 min(p.y for p in band), max(p.y for p in band)))
    z -= 0.005

print()
print("=" * 96)
print("C. 原版 head_female_a 脖子 —— 逐 5mm 截面 + 顶点组名")
print("=" * 96)
ms = imp(VANH)
vh = max(ms, key=lambda o: len(o.data.vertices))
print("顶点组：%s" % [g.name for g in vh.vertex_groups])
mw = vh.matrix_world
vpts = [(mw @ v.co, v) for v in vh.data.vertices]
z = 1.62
while z > 1.40:
    band = [(p, v) for (p, v) in vpts if z - 0.005 <= p.z < z]
    if band:
        ys = [p.y for p, _ in band]
        xs = [p.x for p, _ in band]
        print(" z%.4f  n=%3d  x[%+.4f,%+.4f] %.4f  y[%+.4f,%+.4f] %.4f  y中心%+.4f"
              % (z - 0.0025, len(band), min(xs), max(xs), max(xs) - min(xs),
                 min(ys), max(ys), max(ys) - min(ys), (min(ys) + max(ys)) / 2))
    z -= 0.005

print()
print("=" * 96)
print("D. 官方骨架人形骨骼名（找 neck / spine / clavicle 的编号）")
print("=" * 96)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SKEL)
bpy.context.view_layer.update()
for o in bpy.data.objects:
    if o.type == 'ARMATURE':
        names = [b.name for b in o.data.bones]
        print("骨架 %s 共 %d 骨" % (o.name, len(names)))
        for n in names:
            low = n.lower()
            if any(k in low for k in ("head", "neck", "spine", "clavicle", "pelvis", "chest", "shoulder")):
                print("   ", n)
        break
print("\nPROBE3 DONE")
