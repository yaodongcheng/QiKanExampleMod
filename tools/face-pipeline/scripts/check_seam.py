# 接缝体检：头 + 身体 组装后，沿领口一圈逐角度逐高度打射线，看"从外面看有没有洞"
#
# 判据：从外侧朝中轴打射线，第一击若是【正面朝向射线源】= 有表皮盖着（合格）；
#       若是背面 = 打穿到对侧内壁（= 这里漏了，会看到身体内部）。
#
# 同时对照：原版 head_female_a + body_female_a（已知能用的组合）走同一套判据。
import bpy, bmesh, sys, math
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

B = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline\自定义头"
BODY_OBJ = B + r"\core_game\out\body\body_female_a.obj"
VAN_FBX = B + r"\core_game\fbx\head\head_female_a.fbx"
OURS = r"D:/BrainMaker/blend_projects/tifa_export/backup_20260913/head_tifa_a_v11.fbx"
Y_AXIS = 0.02
Z_LO, Z_HI = 1.38, 1.56


def patch():
    import inspect
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)
patch()


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def add_fbx(p, keep=None):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=p)
    bpy.context.view_layer.update()
    out = []
    for o in bpy.data.objects:
        if o in before or o.type != 'MESH':
            continue
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
        out.append(o)
    return out


def add_obj(p):
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=p, forward_axis='Y', up_axis='Z')
    bpy.context.view_layer.update()
    return [o for o in bpy.data.objects if o not in before]


def build_bvh(objs):
    bm = bmesh.new()
    for o in objs:
        tmp = bmesh.new(); tmp.from_mesh(o.data); tmp.transform(o.matrix_world)
        me = bpy.data.meshes.new("t"); tmp.to_mesh(me); tmp.free()
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
    bvh = BVHTree.FromBMesh(bm)
    bm.free()
    return bvh


def rim_of(body_objs, n=360):
    """量身体的领口 z_rim(θ)（同 build_neck 的方法，作为扫描基准）"""
    bvh = build_bvh(body_objs)
    out = []
    for k in range(n):
        th = 2 * math.pi * k / n
        d = Vector((math.sin(th), math.cos(th), 0.0))
        z, last = Z_LO, None
        while z <= Z_HI:
            o = Vector((0.0, Y_AXIS, z)) + d * 0.8
            hit = bvh.ray_cast(o, -d, 1.6)
            if hit[0] is not None and hit[1].dot(-d) < 0:
                last = z
            elif last is not None and z > last + 0.01:
                break
            z += 0.002
        out.append((th, last))
    return out


def seam_scan(bvh, rim, label, half=0.008, step=0.002, nring=361, detail=None):
    """沿领口上下各 half 米扫一圈，统计"漏"的采样点。
    detail = (领口BVH, 身体BVH)：给了就打印每个漏点的第一击是谁、法线朝向。"""
    gaps = []
    total = 0
    for (th, zr) in rim:
        if zr is None:
            continue
        d = Vector((math.sin(th), math.cos(th), 0.0))
        z = zr - half
        while z <= zr + half:
            o = Vector((0.0, Y_AXIS, z)) + d * 0.8
            hit = bvh.ray_cast(o, -d, 1.6)
            total += 1
            if hit[0] is None or hit[1].dot(-d) > 0:
                gaps.append((math.degrees(th) - 180.0, z, hit, th))
            z += step
    print("\n=== %s ===" % label)
    print("  采样 %d 点，其中【漏】（从外面看得到内壁/空）%d 点" % (total, len(gaps)))
    if gaps and detail is None:
        byz = {}
        for deg, z, _, _ in gaps:
            byz.setdefault(round(z, 3), []).append(deg)
        for z in sorted(byz)[:8]:
            degs = byz[z]
            print("    z=%.3f  角度 %d 个：%s%s" % (z, len(degs), ["%+.0f" % d for d in degs[:8]],
                                                 " …" if len(degs) > 8 else ""))
    if gaps and detail is not None:
        collar_bvh, body_bvh = detail
        for deg, z, hit, th in gaps:
            dd = Vector((math.sin(th), math.cos(th), 0.0))
            o = Vector((0.0, Y_AXIS, z)) + dd * 0.8
            hc = collar_bvh.ray_cast(o, -dd, 1.6)
            hb = body_bvh.ray_cast(o, -dd, 1.6)
            who = []
            if hc[0] is not None:
                who.append("领口 d=%.4f 法线朝%s" % (hc[2], "外" if hc[1].dot(-dd) < 0 else "内(反!)"))
            if hb[0] is not None:
                who.append("身体 d=%.4f 法线朝%s" % (hb[2], "外" if hb[1].dot(-dd) < 0 else "内(反!)"))
            print("    漏点 角度%+.0f° z=%.4f  z_rim=%.4f  第一击: %s" % (
                deg, z, [r for (t, r) in rim if abs(t - th) < 1e-9][0] or -1, " / ".join(who) or "无"))
    return len(gaps)


print("=" * 92)
print("① 基准：原版头 head_female_a + 女子身体")
print("=" * 92)
reset()
body = add_obj(BODY_OBJ)
rim = rim_of(body)
van = add_fbx(VAN_FBX)
vh = max(van, key=lambda o: len(o.data.vertices))
g_ref = seam_scan(build_bvh([vh] + body), rim, "原版头 + 身体")

print()
print("=" * 92)
print("② 我们的：head_tifa_a_v11 + 女子身体")
print("=" * 92)
reset()
body = add_obj(BODY_OBJ)
ours = add_fbx(OURS)
print("导入件：%s" % [o.name for o in ours])
collar = [o for o in ours if o.name.endswith(".0")]
bvh_all = build_bvh(ours + body)
g_ours = seam_scan(bvh_all, rim, "我们 v11 + 身体",
                   detail=(build_bvh(collar), build_bvh(body)))

print()
print("=" * 92)
print("③ 对照：只用我们的头（不接身体）—— 应当大面积漏（证明体检方法有效）")
print("=" * 92)
g_head = seam_scan(build_bvh(ours), rim, "只有头（阴性对照）")

print()
print("=" * 92)
print("结论")
print("=" * 92)
print("  原版（可用参照）漏点 = %d" % g_ref)
print("  我们 v11       漏点 = %d" % g_ours)
print("  只头不接身体   漏点 = %d（阴性对照，应远大于上面两个）" % g_head)
if g_ours <= max(g_ref, 0) + 4:
    print("  ⇒ 与原版同档，接缝合格")
else:
    print("  ⇒ 有 %d 处漏，需修" % g_ours)
print("\nSEAM CHECK DONE")
