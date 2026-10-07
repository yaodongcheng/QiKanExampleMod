# -*- coding: utf-8 -*-
"""inspect_fbx.py —— 把 FBX 读回来量尺寸/原点/朝向（一次性核对用）。

跑法：
   "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --python tools/grapple-model/scripts/inspect_fbx.py -- --dir <目录> [--filter lwn_]
"""
import bpy, sys, os, glob
from mathutils import Vector


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    o, i = {}, 0
    while i < len(a):
        if a[i].startswith("--"):
            o[a[i][2:]] = a[i + 1] if i + 1 < len(a) else ""
            i += 2
        else:
            i += 1
    return o


A = args()
D = A["dir"]
FILT = A.get("filter", "")

for f in sorted(glob.glob(os.path.join(D, FILT + "*.fbx"))):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=f)
    obs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    pts, nv, nf = [], 0, 0
    for ob in obs:
        nv += len(ob.data.vertices)
        nf += len(ob.data.polygons)
        for c in ob.bound_box:
            pts.append(ob.matrix_world @ Vector(c))
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    print("=== %s ===" % os.path.basename(f))
    print("  顶点 %d / 面 %d" % (nv, nf))
    print("  包围盒 X[%.4f, %.4f]  Y[%.4f, %.4f]  Z[%.4f, %.4f]  （尺寸 %.3f × %.3f × %.3f m）"
          % (mn.x, mx.x, mn.y, mx.y, mn.z, mx.z, mx.x - mn.x, mx.y - mn.y, mx.z - mn.z))
    print("  原点是否在几何内: X %s / Y %s / Z %s"
          % ("是" if mn.x <= 0 <= mx.x else "否",
             "是" if mn.y <= 0 <= mx.y else "否",
             "是" if mn.z <= 0 <= mx.z else "否"))
