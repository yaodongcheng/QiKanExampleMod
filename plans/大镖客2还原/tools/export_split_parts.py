# -*- coding: utf-8 -*-
"""从 continent_lod_split_v25.blend 导出缺失的 6 个远景拆分件（只出 LOD0）。

  blender -b <blend> -P export_split_parts.py -- <outdir> <obj1> <obj2> ...

输出格式与 tools\export_rdr2_trf_lod.py 的 write_trf_fast 完全一致：
  rfver 4 / mesh <n> / 每部件 "<name>.<i> 0 <material>" / 逐顶点 FVF 三行 / 末尾 end
按材质拆部件（与已存在的同类件一致：1 材质 = 1 部件）。
"""
import io
import os
import re
import sys

import bpy
import numpy as np

COLOR_WHITE = 4294967295
argv = sys.argv
argv = argv[argv.index("--") + 1:] if "--" in argv else []
OUTDIR = argv[0]
TARGETS = argv[1:]


def clean_mat(n):
    """blend 里材质叫 alb_<name>_ab.001，交付约定是 <name>_ab。"""
    n = re.sub(r"\.\d{3}$", "", n)          # 去掉 .001 这类重名后缀
    if n.startswith("alb_"):
        n = n[4:]
    return n


def mesh_arrays(ob):
    """→ (pos[n,3], nrm[n,3], uv[n,2], tri[m,3], mat[m])；取最终求值网格"""
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()

    n = len(me.vertices)
    pos = np.empty(n * 3, dtype=np.float32)
    me.vertices.foreach_get("co", pos)
    pos = pos.reshape(-1, 3)

    nl = len(me.loops)
    lv = np.empty(nl, dtype=np.int32)
    me.loops.foreach_get("vertex_index", lv)

    nn = np.empty(nl * 3, dtype=np.float32)
    try:
        me.corner_normals.foreach_get("vector", nn)
    except Exception:
        me.calc_normals_split()
        me.corner_normals.foreach_get("vector", nn)
    nn = nn.reshape(-1, 3)

    uv_l = np.zeros((nl, 2), dtype=np.float32)
    if me.uv_layers.active:
        me.uv_layers.active.data.foreach_get("uv", uv_l.reshape(-1))

    _, first = np.unique(lv, return_index=True)
    nrm = np.zeros((n, 3), dtype=np.float32)
    uv = np.zeros((n, 2), dtype=np.float32)
    nrm[lv[first]] = nn[first]
    uv[lv[first]] = uv_l[first]

    me.calc_loop_triangles()
    m = len(me.loop_triangles)
    tl = np.empty(m * 3, dtype=np.int32)
    me.loop_triangles.foreach_get("loops", tl)
    tri = lv[tl.reshape(-1, 3)]

    mi = np.empty(m, dtype=np.int32)
    me.loop_triangles.foreach_get("material_index", mi)

    ev.to_mesh_clear()
    return pos, nrm, uv, tri, mi


def write_trf(path, parts):
    out = ["rfver 4", "mesh %d" % len(parts)]
    for name, mat, pos, nrm, uv, tri in parts:
        n = len(pos)
        out.append("%s 0 %s" % (name, mat))
        out.append(str(n))
        out.extend(["%.6f %.6f %.6f" % (x, y, z) for x, y, z in pos.tolist()])
        out.append("0")
        out.append(str(n))
        for i, (nn_, uu_) in enumerate(zip(nrm.tolist(), uv.tolist())):
            out.append("%d %d %.6f %.6f %.6f" % (i, COLOR_WHITE, nn_[0], nn_[1], nn_[2]))
            out.append("%.6f %.6f" % (uu_[0], uu_[1]))
            out.append("0.000000 0.000000")
        out.append("0")
        out.append(str(len(tri)))
        out.extend(["%d %d %d" % (a, b, c) for a, b, c in tri.tolist()])
    out.append("end")
    with io.open(path, "w", encoding="utf-8", errors="replace", newline="\n") as f:
        f.write("\n".join(out))
        f.write("\n")
    return os.path.getsize(path)


os.makedirs(OUTDIR, exist_ok=True)
print("\n=== 导出 %d 个拆分件 → %s ===" % (len(TARGETS), OUTDIR))
ok = fail = 0
for name in TARGETS:
    ob = bpy.data.objects.get(name)
    if ob is None or ob.type != "MESH":
        print("  [跳过] %-42s 对象不存在或非网格" % name)
        fail += 1
        continue
    mats = []
    for ms in ob.material_slots:
        mats.append(ms.material.name if ms.material else "None")
    pos, nrm, uv, tri, mi = mesh_arrays(ob)
    present = sorted(set(mi.tolist()))
    parts = []
    for k, midx in enumerate(present):
        sel = tri[mi == midx]
        if len(sel) == 0:
            continue
        used = np.unique(sel)
        remap = np.full(int(used.max()) + 1, -1, dtype=np.int64)
        remap[used] = np.arange(len(used))
        parts.append(("%s.%d" % (name, k), clean_mat(mats[midx]) if midx < len(mats) else "None",
                      pos[used], nrm[used], uv[used], remap[sel]))
    if not parts:
        print("  [跳过] %-42s 没有三角形" % name)
        fail += 1
        continue
    dst = os.path.join(OUTDIR, name + ".trf")
    sz = write_trf(dst, parts)
    ftot = sum(len(p[5]) for p in parts)
    vtot = sum(len(p[2]) for p in parts)
    print("  [写出] %-42s %d 部件 %6d 面 %6d 顶点  %8.1f KB  材质=%s"
          % (name, len(parts), ftot, vtot, sz / 1024, ",".join(mats)))
    ok += 1
print("\n成功 %d / 失败 %d" % (ok, fail))
