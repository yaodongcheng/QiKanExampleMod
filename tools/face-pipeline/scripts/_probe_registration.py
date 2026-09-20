# _probe_registration.py —— 量目标网格各区域与源网格表面的距离（判断"能不能用法线投影搬场"）。
# 用法：blender --background --python _probe_registration.py -- <源FBX> <源对象> <目标FBX> <目标对象>
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

args = sys.argv[sys.argv.index("--") + 1:]
SRC, SRC_OBJ, DST, DST_OBJ = args[0], args[1], args[2], args[3]
sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def patch():
    import inspect
    import io_scene_fbx.import_fbx as mod
    s = inspect.getsource(mod)
    bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
    if bad in s:
        s = s.replace(bad, "pass  # patched")
        exec(compile(s, mod.__file__, "exec"), mod.__dict__)


def load(path, name):
    patch()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    bpy.context.view_layer.update()
    for o in bpy.data.objects:
        if o.type == 'MESH' and o.name == name:
            return o
    raise SystemExit("找不到 %s" % name)


s = load(SRC, SRC_OBJ)
s_co = [v.co.copy() for v in s.data.vertices]
s_name = s.name      # 🔴 名字在第二次 load 前拷出来
tris = []
for p in s.data.polygons:
    vs = list(p.vertices)
    for k in range(1, len(vs) - 1):
        tris.append((vs[0], vs[k], vs[k + 1]))
bvh = BVHTree.FromPolygons(s_co, tris, all_triangles=True)

d = load(DST, DST_OBJ)
co = [v.co.copy() for v in d.data.vertices]
nrm = [v.normal.copy() for v in d.data.vertices]
print("源 %s 顶点 %d / 目标 %s 顶点 %d" % (s_name, len(s_co), d.name, len(co)))

REGIONS = [
    ("上唇  z1.615-1.640", lambda c: abs(c.x) <= 0.04 and c.y >= 0.10 and 1.615 <= c.z <= 1.640),
    ("唇线  z1.598-1.615", lambda c: abs(c.x) <= 0.04 and c.y >= 0.10 and 1.598 <= c.z <= 1.615),
    ("下唇  z1.580-1.598", lambda c: abs(c.x) <= 0.04 and c.y >= 0.10 and 1.580 <= c.z <= 1.598),
    ("下巴  z1.545-1.580", lambda c: abs(c.x) <= 0.04 and c.y >= 0.10 and 1.545 <= c.z <= 1.580),
    ("鼻子  z1.635-1.690", lambda c: abs(c.x) <= 0.03 and c.y >= 0.12 and 1.635 <= c.z <= 1.690),
]
print("\n%-18s %5s %8s %8s %8s %8s" % ("区域", "点数", "最近距中位", "P90", "法线投影命中率", "命中距中位"))
for name, f in REGIONS:
    sel = [i for i, c in enumerate(co) if f(c)]
    if not sel:
        print("%-18s （无点）" % name)
        continue
    ds = []
    hit = 0
    hits = []
    for i in sel:
        p = co[i]
        loc, n, idx, dist = bvh.find_nearest(p)
        ds.append(dist if loc else 99)
        nvec = nrm[i]
        for origin, direction in ((p + nvec * 2e-4, -nvec), (p - nvec * 2e-4, nvec)):
            l2, n2, i2, d2 = bvh.ray_cast(origin, direction, 0.03)   # 放宽到 30mm 看看到底多远
            if i2 is not None and n2 is not None and abs(n2.dot(nvec)) >= 0.3:
                hit += 1
                hits.append(d2)
                break
    ds.sort(); hits.sort()
    med = ds[len(ds) // 2] * 1000
    p90 = ds[int(len(ds) * 0.9)] * 1000
    hm = (hits[len(hits) // 2] * 1000) if hits else -1
    print("%-18s %5d %8.1f %8.1f %11.0f%% %8.1f"
          % (name, len(sel), med, p90, 100.0 * hit / len(sel), hm))
sys.stdout.flush()
