# -*- coding: utf-8 -*-
"""Measure faces vs VERTICES vs bytes across decimation ratios."""
import bpy, io, math, os, re, sys
import numpy as np

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
MODELS = os.path.join(RDR2, "deliverable", "models")
argv = sys.argv
argv = argv[argv.index("--") + 1:] if "--" in argv else []
NAME = argv[0] if argv else "new_com_02_bank"


def parse(path):
    lines = io.open(path, encoding="utf-8", errors="replace").read().split("\n")
    i, n, parts = 0, len(lines), []
    while i < n:
        s = lines[i].strip()
        if not s or s == "end":
            i += 1; continue
        tok = s.split()
        if len(tok) != 3 or tok[1] != "0":
            i += 1; continue
        pname, mat = tok[0], tok[2]; i += 1
        npos = int(lines[i].strip()); i += 1
        pos = np.fromstring(" ".join(lines[i:i+npos]), sep=" ").reshape(-1, 3)
        i += npos + 1
        nvert = int(lines[i].strip()); i += 1
        blk = np.fromstring(" ".join(lines[i:i+nvert*3]), sep=" ").reshape(nvert, 9)
        i += nvert*3
        pidx = blk[:, 0].astype(np.int64); nrm, uv = blk[:, 2:5], blk[:, 5:7]
        bone = int(lines[i].strip()); i += 1
        if bone > 0:
            i += 1; w = int(lines[i].strip()); i += 1; i += w
        ntri = int(lines[i].strip()); i += 1
        tri = np.fromstring(" ".join(lines[i:i+ntri]), sep=" ").astype(np.int64).reshape(-1, 3)
        i += ntri
        parts.append(dict(name=pname, mat=mat, pos=pos, pidx=pidx, nrm=nrm, uv=uv, tri=tri))
    return parts


def lod_of(s):
    m = re.search(r"\.lod(\d+)\.", s)
    return int(m.group(1)) if m else 0


def weld_uv_safe(pos, nrm, uv, tri):
    key = np.concatenate([np.round(pos, 5), np.round(uv, 5), np.round(nrm, 4)], axis=1)
    _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
    out = inv[tri]
    keep = (out[:, 0] != out[:, 1]) & (out[:, 1] != out[:, 2]) & (out[:, 0] != out[:, 2])
    return pos[first], nrm[first], uv[first], out[keep]


def build(parts):
    objs = []
    for p in parts:
        v = p["pos"][p["pidx"]]
        if len(v) == 0 or len(p["tri"]) == 0:
            continue
        me = bpy.data.meshes.new(p["name"])
        me.from_pydata(v.tolist(), [], p["tri"].tolist())
        me.update()
        uvl = me.uv_layers.new(name="UVMap")
        lv = np.empty(len(me.loops), dtype=np.int32)
        me.loops.foreach_get("vertex_index", lv)
        uv = p["uv"][lv].astype(np.float32)
        uvl.data.foreach_set("uv", np.ascontiguousarray(uv).reshape(-1))
        ob = bpy.data.objects.new(p["name"], me)
        bpy.context.collection.objects.link(ob)
        objs.append(ob)
    return objs


def stats(objs):
    f = v = 0
    for ob in objs:
        ob.data.calc_loop_triangles()
        f += len(ob.data.loop_triangles)
        v += len(ob.data.vertices)
    return f, v


def dec(ob, ratio):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    m = ob.modifiers.new("d", type="DECIMATE")
    m.decimate_type = "COLLAPSE"
    m.ratio = ratio
    bpy.ops.object.modifier_apply(modifier=m.name)


bpy.ops.wm.read_factory_settings(use_empty=True)
parts = [p for p in parse(os.path.join(MODELS, NAME + ".trf")) if lod_of(p["name"]) == 0]

# baseline: raw (unwelded, as shipped LOD0)
objs = build(parts)
f0, v0 = stats(objs)
print("\n%-12s %9s %9s %7s %10s %10s" % ("stage", "faces", "verts", "v/f", "VStream MB", "vs LOD0"))
print("%-12s %9d %9d %7.3f %10.2f %9.1f%%"
      % ("raw LOD0", f0, v0, v0 / float(f0), (v0 * 108 + f0 * 12) / 1e6, 100.0))
for ob in list(bpy.data.objects):
    bpy.data.objects.remove(ob, do_unlink=True)

# welded (UV-safe)
wp = []
for p in parts:
    P, N, U, T = weld_uv_safe(p["pos"][p["pidx"]], p["nrm"], p["uv"], p["tri"])
    q = dict(p); q["pos"] = P; q["pidx"] = np.arange(len(P)); q["nrm"] = N; q["uv"] = U; q["tri"] = T
    wp.append(q)
objs = build(wp)
fw, vw = stats(objs)
print("%-12s %9d %9d %7.3f %10.2f %9.1f%%"
      % ("UV-safe weld", fw, vw, vw / float(fw), (vw * 108 + fw * 12) / 1e6, 100.0 * (vw * 108 + fw * 12) / (v0 * 108 + f0 * 12)))

# decimate the welded set progressively
for r in (0.7, 0.5, 0.3, 0.15):
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    objs = build(wp)
    for ob in objs:
        dec(ob, r)
    f, v = stats(objs)
    print("%-12s %9d %9d %7.3f %10.2f %9.1f%%"
          % ("dec %.2f" % r, f, v, v / float(f), (v * 108 + f * 12) / 1e6,
             100.0 * (v * 108 + f * 12) / (v0 * 108 + f0 * 12)))
print()
