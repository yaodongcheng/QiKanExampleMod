# 脖子测量（只读一次性探针）—— 为「蒂法头 → 骑砍2 女子身体」的脖子补段收集数据
#
# 量四件事：
#   A. 蒂法源模型：head / body 两对象在颈区(z 1.40~1.60 源空间)的横截面 + 材质/UV
#   B. 我们当前 v10 头：底部的横截面 + 开口边界环（脖子该接在哪）
#   C. 原版 head_female_a：颈区横截面 + 逐高度骨骼权重（权重渐变的配方）
#   D. 参照 mod xxFemale：同上
import bpy, inspect, collections, statistics, mathutils, os

def patch_fbx_importer():
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass  # patched: TWT morph without FullWeights")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)

patch_fbx_importer()

TIFA  = r"F:\下载\Tifa lockhart In Drees - FBX\Tifa.fbx"
OURS  = r"D:/BrainMaker/blend_projects/tifa_export/backup_20260913/head_tifa_a_v10.fbx"
VAN   = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline\core_game\fbx\head\head_female_a.fbx"
XX    = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline\face_probe\xx_fbx\head\head_xxfemale_a.fbx"


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    bpy.context.view_layer.update()
    return [o for o in bpy.data.objects if o.type == 'MESH'], [o for o in bpy.data.objects if o.type == 'ARMATURE']


def bbox(ob, idx=None):
    pts = pts_of(ob, idx)
    if not pts:
        return None
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    return lo, hi


def pts_of(ob, idx=None):
    mw = ob.matrix_world
    if idx is None:
        return [mw @ v.co for v in ob.data.vertices]
    return [mw @ ob.data.vertices[i].co for i in idx]


def bstr(lo, hi):
    return "x[%8.4f,%8.4f] y[%8.4f,%8.4f] z[%8.4f,%8.4f]" % (
        lo[0], hi[0], lo[1], hi[1], lo[2], hi[2])


def profile(ob, z_hi, z_lo, step, label, matnames=False):
    """按高度切片：顶点数 / x 宽 / y 深 / 中心"""
    print("\n--- %s  横截面（%.3f → %.3f，步长 %.3f）---" % (label, z_hi, z_lo, step))
    print("   z中心    顶点数   x宽     y深     x中心    y中心")
    mw = ob.matrix_world
    verts = [(mw @ v.co) for v in ob.data.vertices]
    z = z_hi
    while z > z_lo:
        band = [p for p in verts if z - step <= p.z < z]
        if band:
            print("   %.4f  %5d   %.4f  %.4f  %+.4f  %+.4f" % (
                z - step / 2, len(band),
                max(p.x for p in band) - min(p.x for p in band),
                max(p.y for p in band) - min(p.y for p in band),
                (max(p.x for p in band) + min(p.x for p in band)) / 2,
                (max(p.y for p in band) + min(p.y for p in band)) / 2))
        z -= step


def weights_profile(ob, arm, z_bands, label):
    """逐高度带的顶点组权重直方图"""
    if not ob.vertex_groups:
        print("\n--- %s 无顶点组 ---" % label)
        return
    names = [g.name for g in ob.vertex_groups]
    print("\n--- %s 权重分布（按 z 带，只统计 weight>0.01 的顶点数）---" % label)
    mw = ob.matrix_world
    for (z0, z1) in z_bands:
        cnt = collections.Counter()
        n = 0
        for v in ob.data.vertices:
            p = mw @ v.co
            if z0 <= p.z < z1:
                n += 1
                for g in v.groups:
                    if g.weight > 0.01:
                        cnt[names[g.group]] += 1
        if n:
            print("   z[%.3f,%.3f] 顶点 %4d : %s" % (z0, z1, n, dict(cnt.most_common(8))))


def mat_of_face_verts(ob, z0, z1, label):
    """某高度带内的面用到哪些材质（判断脖子是皮肤还是衣服）"""
    mw = ob.matrix_world
    cnt = collections.Counter()
    for poly in ob.data.polygons:
        c = mathutils.Vector((0, 0, 0))
        for vi in poly.vertices:
            c += mw @ ob.data.vertices[vi].co
        c /= len(poly.vertices)
        if z0 <= c.z < z1:
            m = ob.data.materials[poly.material_index] if poly.material_index < len(ob.data.materials) else None
            cnt[m.name if m else "None"] += 1
    print("   [%s] z[%.3f,%.3f] 面材质: %s" % (label, z0, z1, dict(cnt.most_common(8))))


def uv_of_band(ob, z0, z1, label):
    me = ob.data
    if not me.uv_layers:
        print("   [%s] 无 UV" % label)
        return
    mw = ob.matrix_world
    uv = me.uv_layers[0].data
    us, vs = [], []
    for poly in me.polygons:
        c = mathutils.Vector((0, 0, 0))
        for vi in poly.vertices:
            c += mw @ me.vertices[vi].co
        c /= len(poly.vertices)
        if z0 <= c.z < z1:
            for li in poly.loop_indices:
                us.append(uv[li].uv[0]); vs.append(uv[li].uv[1])
    if us:
        print("   [%s] z[%.3f,%.3f] UV范围 u[%.4f,%.4f] v[%.4f,%.4f]  (%d 个 loop)" % (
            label, z0, z1, min(us), max(us), min(vs), max(vs), len(us)))


def open_boundary(ob, z0, z1, label):
    """开口边界环：只被 1 个面用到的边（= 网格的自由边）"""
    me = ob.data
    mw = ob.matrix_world
    ec = collections.Counter()
    for poly in me.polygons:
        n = len(poly.vertices)
        for i in range(n):
            a = poly.vertices[i]; b = poly.vertices[(i + 1) % n]
            ec[(min(a, b), max(a, b))] += 1
    pts = []
    for (a, b), c in ec.items():
        if c == 1:
            pa = mw @ me.vertices[a].co; pb = mw @ me.vertices[b].co
            if z0 <= pa.z < z1 or z0 <= pb.z < z1:
                pts += [pa, pb]
    if pts:
        lo = [min(p[i] for p in pts) for i in range(3)]
        hi = [max(p[i] for p in pts) for i in range(3)]
        zs = sorted(set(round(p.z, 4) for p in pts))
        print("   [%s] z[%.3f,%.3f] 自由边顶点 %d 个  bbox %s" % (label, z0, z1, len(pts), bstr(lo, hi)))
        print("        出现的 z 层: %s%s" % (zs[:12], " ..." if len(zs) > 12 else ""))
    else:
        print("   [%s] z[%.3f,%.3f] 无自由边（闭合）" % (label, z0, z1))


print("=" * 100)
print("A. 蒂法源模型")
print("=" * 100)
ms, ar = load(TIFA)
print("网格对象：")
for ob in ms:
    lo, hi = bbox(ob)
    mats = [m.name if m else None for m in ob.data.materials]
    print("  MESH %-16s verts=%-7d %s  mats=%s" % (ob.name, len(ob.data.vertices), bstr(lo, hi), mats))
head = next((o for o in ms if o.name == 'head'), None)
body = next((o for o in ms if o.name == 'body'), None)
if head:
    profile(head, 1.60, 1.40, 0.01, "蒂法 head（源空间）")
    mat_of_face_verts(head, 1.40, 1.55, "蒂法 head")
if body:
    profile(body, 1.60, 1.38, 0.01, "蒂法 body（源空间）")
    for z0, z1 in ((1.40, 1.44), (1.44, 1.46), (1.46, 1.48), (1.48, 1.50), (1.50, 1.52), (1.52, 1.54)):
        mat_of_face_verts(body, z0, z1, "蒂法 body")
        uv_of_band(body, z0, z1, "蒂法 body")
    weights_profile(body, ar[0] if ar else None, [(1.40, 1.46), (1.46, 1.50), (1.50, 1.54)], "蒂法 body")

print()
print("=" * 100)
print("B. 我们的 v10 头")
print("=" * 100)
ms, ar = load(OURS)
for ob in ms:
    lo, hi = bbox(ob)
    print("  MESH %-18s verts=%-6d %s" % (ob.name, len(ob.data.vertices), bstr(lo, hi)))
shell = ms[0]
profile(shell, 1.62, 1.50, 0.005, "我们的脸壳")
open_boundary(shell, 1.50, 1.58, "我们的脸壳")
weights_profile(shell, ar[0] if ar else None, [(1.50, 1.55), (1.55, 1.60), (1.60, 1.65), (1.65, 1.70)], "我们的脸壳")

print()
print("=" * 100)
print("C. 原版 head_female_a")
print("=" * 100)
ms, ar = load(VAN)
for ob in ms:
    lo, hi = bbox(ob)
    print("  MESH %-18s verts=%-6d %s" % (ob.name, len(ob.data.vertices), bstr(lo, hi)))
v_shell = max(ms, key=lambda o: len(o.data.vertices))
print("  取最大件作为脸壳：%s" % v_shell.name)
profile(v_shell, 1.62, 1.40, 0.01, "原版脸壳")
open_boundary(v_shell, 1.38, 1.60, "原版脸壳")
weights_profile(v_shell, ar[0] if ar else None,
                [(1.40, 1.44), (1.44, 1.48), (1.48, 1.50), (1.50, 1.52), (1.52, 1.54), (1.54, 1.56), (1.56, 1.60)], "原版脸壳")

print()
print("=" * 100)
print("D. 参照 mod xxFemale")
print("=" * 100)
ms, ar = load(XX)
for ob in ms:
    lo, hi = bbox(ob)
    print("  MESH %-18s verts=%-6d %s" % (ob.name, len(ob.data.vertices), bstr(lo, hi)))
x_shell = max(ms, key=lambda o: len(o.data.vertices))
print("  取最大件作为脸壳：%s" % x_shell.name)
profile(x_shell, 1.62, 1.40, 0.01, "xxFemale 脸壳")
open_boundary(x_shell, 1.38, 1.60, "xxFemale 脸壳")
weights_profile(x_shell, ar[0] if ar else None,
                [(1.40, 1.44), (1.44, 1.48), (1.48, 1.50), (1.50, 1.52), (1.52, 1.54), (1.54, 1.56), (1.56, 1.60)], "xxFemale 脸壳")

print("\nPROBE DONE")
