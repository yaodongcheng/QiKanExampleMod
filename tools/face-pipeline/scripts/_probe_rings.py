# 开口环测量（只读一次性探针）—— 量三处接合面：
#   ① 骑砍2 女子身体 body_female_a 的颈口（V 领开口？环？）
#   ② 我们 v10 头壳的底部开口
#   ③ 蒂法源 body 的脖子柱（上端开口 / 逐高度截面）
import bpy, bmesh, inspect, math, collections
from mathutils import Vector

def patch():
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in src:
        src = src.replace(bad, "pass")
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)
patch()

B = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline"
TIFA = r"F:\下载\Tifa lockhart In Drees - FBX\Tifa.fbx"
OURS = r"D:/BrainMaker/blend_projects/tifa_export/backup_20260913/head_tifa_a_v10.fbx"
VANB = B + r"\core_game\out\body\body_female_a.obj"
VANH = B + r"\core_game\fbx\head\head_female_a.fbx"

S, BY, BZ = 1.238, 0.01685, -0.2678


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


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


def imp_obj(p):
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=p, forward_axis='Y', up_axis='Z')
    bpy.context.view_layer.update()
    return [o for o in bpy.data.objects if o not in before and o.type == 'MESH']


def free_loops(ob, zmin, zmax, label):
    """返回落在 z 区间内的自由边（只连一个面的边）分成的环"""
    me = ob.data
    mw = ob.matrix_world
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    edges = [e for e in bm.edges if len(e.link_faces) == 1]
    # 并查集按顶点把边连成环
    parent = {}
    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]; a = parent[a]
        return a
    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb: parent[ra] = rb
    keep = []
    for e in edges:
        pa = mw @ e.verts[0].co; pb = mw @ e.verts[1].co
        if (zmin <= pa.z <= zmax) or (zmin <= pb.z <= zmax):
            keep.append(e)
            for v in e.verts:
                parent.setdefault(v.index, v.index)
            union(e.verts[0].index, e.verts[1].index)
    groups = collections.defaultdict(set)
    for e in keep:
        groups[find(e.verts[0].index)].update(v.index for v in e.verts)
    print("\n### %s：z[%.3f,%.3f] 内自由边 %d 条 → %d 个环" % (label, zmin, zmax, len(keep), len(groups)))
    out = []
    for gi, (root, vids) in enumerate(sorted(groups.items(), key=lambda kv: -len(kv[1]))):
        pts = [mw @ me.vertices[i].co for i in vids]
        cx = sum(p.x for p in pts) / len(pts); cy = sum(p.y for p in pts) / len(pts)
        zs = sorted(p.z for p in pts)
        print("  环%d: %d 点  x[%+.4f,%+.4f] y[%+.4f,%+.4f] z[%.4f,%.4f]  中心(%+.4f,%+.4f)"
              % (gi, len(pts), min(p.x for p in pts), max(p.x for p in pts),
                 min(p.y for p in pts), max(p.y for p in pts), zs[0], zs[-1], cx, cy))
        out.append((pts, (cx, cy)))
    bm.free()
    return out


def ring_table(pts, cx, cy, label, nbins=16):
    """按方位角把环点排开，看形状"""
    print("     方位角表（%s，中心 %+.4f,%+.4f）：" % (label, cx, cy))
    bins = [[] for _ in range(nbins)]
    for p in pts:
        a = math.atan2(p.x - cx, p.y - cy)          # 0 = +Y(脸朝的方向)
        k = int((a + math.pi) / (2 * math.pi) * nbins) % nbins
        bins[k].append(p)
    print("       角度     点数   x中位    y中位    z中位   到中心距离")
    for k in range(nbins):
        b = bins[k]
        if not b:
            continue
        ang = -180 + (k + 0.5) * 360.0 / nbins
        mx = sorted(p.x for p in b)[len(b) // 2]; my = sorted(p.y for p in b)[len(b) // 2]
        mz = sorted(p.z for p in b)[len(b) // 2]
        d = math.hypot(mx - cx, my - cy)
        print("       %+7.1f°  %3d   %+.4f  %+.4f  %.4f   %.4f" % (ang, len(b), mx, my, mz, d))


print("=" * 90)
print("① 骑砍2 女子身体 body_female_a —— 颈口在哪、什么形状")
print("=" * 90)
reset()
body = imp_obj(VANB)[0]
ps = [body.matrix_world @ v.co for v in body.data.vertices]
print("body bbox x[%.4f,%.4f] y[%.4f,%.4f] z[%.4f,%.4f]"
      % (min(p.x for p in ps), max(p.x for p in ps), min(p.y for p in ps), max(p.y for p in ps),
         min(p.z for p in ps), max(p.z for p in ps)))
loops = free_loops(body, 1.30, 1.60, "body_female_a")
for pts, (cx, cy) in loops[:2]:
    ring_table(pts, cx, cy, "身体颈口")
# 身体顶部逐高度截面
print("\n身体顶部截面（步长 5mm）：")
mw = body.matrix_world
verts = [mw @ v.co for v in body.data.vertices]
z = 1.52
while z > 1.40:
    band = [p for p in verts if z - 0.005 <= p.z < z]
    if band:
        print("   z%.4f  n=%3d  x[%+.4f,%+.4f]宽%.4f  y[%+.4f,%+.4f]深%.4f"
              % (z - 0.0025, len(band), min(p.x for p in band), max(p.x for p in band),
                 max(p.x for p in band) - min(p.x for p in band),
                 min(p.y for p in band), max(p.y for p in band),
                 max(p.y for p in band) - min(p.y for p in band)))
    z -= 0.005

print()
print("=" * 90)
print("② 我们的 v10 头壳 —— 底部开口")
print("=" * 90)
reset()
ms = imp_fbx(OURS)
shell = ms[0]
loops = free_loops(shell, 1.50, 1.60, "head_tifa_a.0")
for pts, (cx, cy) in loops[:2]:
    ring_table(pts, cx, cy, "我们头的底口")

print()
print("=" * 90)
print("③ 蒂法源 body 的脖子柱（换算到我们空间）")
print("=" * 90)
reset()
ms = imp_fbx(TIFA)
tb = next(o for o in ms if o.name == 'body')
th = next(o for o in ms if o.name == 'head')
print("源 body 材质: %s" % [m.name for m in tb.data.materials])
mw = tb.matrix_world
# 源 body 的横截面（换算到我们空间：缩放 1.238 + 平移），只在上半身
print("\n源 body 上半身截面（已换算到我们空间）：")
for zs in [1.5600, 1.5500, 1.5400, 1.5300, 1.5250, 1.5200, 1.5150, 1.5100, 1.5050, 1.5000]:
    src_z = (zs - BZ) / S
    band = []
    for v in tb.data.vertices:
        p = mw @ v.co
        if abs(p.z - src_z) < 0.006:
            band.append(Vector((S * p.x, -S * p.y + BY, S * p.z + BZ)))
    if not band:
        print("   我们 z%.4f (源%.4f)  —— 该高度无顶点" % (zs, src_z)); continue
    nrm = [(math.hypot(p.x, p.y), p) for p in band]
    print("   我们 z%.4f (源%.4f)  n=%3d  x[%+.4f,%+.4f]宽%.4f  y[%+.4f,%+.4f]深%.4f  最大径%.4f"
          % (zs, src_z, len(band), min(p.x for p in band), max(p.x for p in band),
             max(p.x for p in band) - min(p.x for p in band),
             min(p.y for p in band), max(p.y for p in band),
             max(p.y for p in band) - min(p.y for p in band),
             max(d for d, _ in nrm)))
# 源 body 脖子的最高开口
loops = free_loops(tb, 1.50 / 1.0, 1.70, "蒂法源 body（源空间 z1.50~1.70）")
for pts, (cx, cy) in loops[:3]:
    zz = sorted(p.z for p in pts)
    print("   [源空间] 环 %d 点 中心(%+.4f,%+.4f) z[%.4f,%.4f]" % (len(pts), cx, cy, zz[0], zz[-1]))

print()
print("=" * 90)
print("④ 原版 head_female_a 的底部（它拿什么盖住身体的 V 开口）")
print("=" * 90)
reset()
ms = imp_fbx(VANH)
vh = max(ms, key=lambda o: len(o.data.vertices))
loops = free_loops(vh, 1.38, 1.60, "head_female_a")
for pts, (cx, cy) in loops[:2]:
    ring_table(pts, cx, cy, "原版头底口", nbins=12)
print("\n原版头壳 截面（看 V 领盖布）：")
mw = vh.matrix_world
verts = [mw @ v.co for v in vh.data.vertices]
z = 1.52
while z > 1.38:
    band = [p for p in verts if z - 0.01 <= p.z < z]
    if band:
        print("   z%.3f  n=%3d  x宽%.4f  y[%+.4f,%+.4f]深%.4f"
              % (z - 0.005, len(band), max(p.x for p in band) - min(p.x for p in band),
                 min(p.y for p in band), max(p.y for p in band),
                 max(p.y for p in band) - min(p.y for p in band)))
    z -= 0.01
print("\nPROBE2 DONE")
