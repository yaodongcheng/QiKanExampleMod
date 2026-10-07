# -*- coding: utf-8 -*-
"""check_winding.py —— 查网格的面朝向（有符号体积法）。

判据：闭合网格的有符号体积 V = Σ dot(v0, cross(v1, v2)) / 6
  · V > 0 ⇒ 面朝**外**（正常，能被看到）
  · V < 0 ⇒ 面朝**内**（背面剔除 = 从外面看不见！）

跑法：
   blender -b --python tools/grapple-model/scripts/check_winding.py -- --fbx a.fbx [b.fbx ...] [--obj c.obj]
"""
import bpy
import sys
import os


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


def signed_volume(objs):
    total = 0.0
    tris = 0
    for ob in objs:
        mw = ob.matrix_world
        vs = [mw @ v.co for v in ob.data.vertices]
        for p in ob.data.polygons:
            idx = list(p.vertices)
            if len(idx) < 3:
                continue
            for k in range(1, len(idx) - 1):
                a, b, c = vs[idx[0]], vs[idx[k]], vs[idx[k + 1]]
                total += a.dot(b.cross(c)) / 6.0
                tris += 1
    return total, tris


A = args()
jobs = [("fbx", f) for f in (A.get("fbx", "").split(",") if A.get("fbx") else [])]
jobs += [("obj", f) for f in (A.get("obj", "").split(",") if A.get("obj") else [])]

for kind, path in jobs:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if kind == "fbx":
        bpy.ops.import_scene.fbx(filepath=path)
    else:
        bpy.ops.wm.obj_import(filepath=path, forward_axis='Y', up_axis='Z')
    objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    vol, tris = signed_volume(objs)
    verdict = "朝外 ✓" if vol > 0 else "**朝内 ✗（背面剔除 ⇒ 从外面看不见）**"
    print("[WIND] %-40s 三角形=%d 有符号体积=%+.6f ⇒ %s" % (os.path.basename(path), tris, vol, verdict))
