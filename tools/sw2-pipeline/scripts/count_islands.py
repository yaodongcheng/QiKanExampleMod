# -*- coding: utf-8 -*-
"""count_islands.py —— 数每个零件的「连通域」个数（验证"一个对象里混了几样东西"）。

背景：实测发现源模型里多个对象内部**混着互不相连的另一块**（头发+外套同一块、
甲+兜同一块…）。换头/换甲是整块拿走的，整块拿就会把不相干的东西一起带走，
所以要把这一项量出来（见 tools/sw2-pipeline/README.md 第七节）。

用法:
  blender -b --python count_islands.py -- --src <源.fbx> [--min-verts 8]
"""
import bpy, sys, os, bmesh

def get(a, k, d=None):
    return a[a.index(k) + 1] if k in a else d

A = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC = get(A, "--src")
MINV = int(get(A, "--min-verts", "8"))
if not SRC:
    print("FATAL: 需要 --src"); sys.exit(1)

import inspect
import io_scene_fbx.import_fbx as mod
_s = inspect.getsource(mod)
_bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
if _bad in _s:
    exec(compile(_s.replace(_bad, "pass"), mod.__file__, "exec"), mod.__dict__)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
bpy.context.view_layer.update()

stem = os.path.splitext(os.path.basename(SRC))[0]
objs = sorted([o for o in bpy.data.objects if o.type == 'MESH'], key=lambda o: o.name)
print("=" * 92)
print("[ISLANDS] %s" % stem)
for i, ob in enumerate(objs):
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    seen = [False] * len(bm.verts)
    groups = []
    for v in bm.verts:
        if seen[v.index]:
            continue
        stack = [v]; seen[v.index] = True; comp = []
        while stack:
            cur = stack.pop(); comp.append(cur.index)
            for e in cur.link_edges:
                o2 = e.other_vert(cur)
                if not seen[o2.index]:
                    seen[o2.index] = True; stack.append(o2)
        groups.append(comp)
    bm.free()
    groups.sort(key=len, reverse=True)
    big = [g for g in groups if len(g) >= MINV]
    flag = "  <== 多块" if len(big) > 1 else ""
    print("  %2d %-40s v=%-5d 连通域=%d（>=%d 顶点的有 %d 个：%s）%s"
          % (i, ob.name[:40], len(me.vertices), len(groups), MINV, len(big),
             ",".join(str(len(g)) for g in big[:6]), flag))
sys.exit(0)
