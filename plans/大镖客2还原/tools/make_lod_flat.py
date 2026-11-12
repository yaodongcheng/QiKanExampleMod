# -*- coding: utf-8 -*-
"""FINAL: LOD0 = the validated r33 file (untouched); LOD1-6 each decimated once
from the ORIGINAL geometry at ratio = part_factor * level_ratio.

  blender -b --factory-startup -P make_lod_final.py -- <orig.trf> <lod0.trf> <out.trf> <meshname> <levels>
"""
import bpy, io, os, re, sys
import numpy as np

MIN_POLY = 8
RATIOS = [1.0, 0.85, 0.68, 0.50, 0.36, 0.27, 0.22]
VS_V, ME_V, IDX_F, ME_F = 48.1, 103.0, 6.0, 12.0

argv = sys.argv
argv = argv[argv.index("--") + 1:] if "--" in argv else []
SRC, LOD0F, DST, MESHNAME = argv[0], argv[1], argv[2], argv[3]
LEVELS = int(argv[4]) if len(argv) > 4 else 7


def parse(path):
    lines = io.open(path, encoding="utf-8", errors="replace").read().split("\n")
    i, n, out = 0, len(lines), []
    while i < n:
        s = lines[i].strip()
        if not s:
            i += 1; continue
        if s.startswith("rfver") or s.startswith("mesh ") or s == "end":
            i += 1; continue
        tok = s.split()
        if len(tok) != 3 or tok[1] != "0":
            raise ValueError("%r" % s[:60])
        name, mat = tok[0], tok[2]; i += 1
        npos = int(lines[i].strip()); i += 1
        pos = np.fromstring(" ".join(lines[i:i + npos]), sep=" ").reshape(-1, 3)
        i += npos + 1
        nvert = int(lines[i].strip()); i += 1
        blk = np.fromstring(" ".join(lines[i:i + nvert * 3]), sep=" ").reshape(nvert, 9)
        i += nvert * 3
        pidx = blk[:, 0].astype(np.int64); nrm = blk[:, 2:5]; uv = blk[:, 5:7]
        bone = int(lines[i].strip()); i += 1
        if bone > 0:
            i += 1; w = int(lines[i].strip()); i += 1; i += w
        ntri = int(lines[i].strip()); i += 1
        tri = np.fromstring(" ".join(lines[i:i + ntri]), sep=" ").astype(np.int64).reshape(-1, 3)
        i += ntri
        out.append(dict(name=name, mat=mat, pos=pos, pidx=pidx, nrm=nrm, uv=uv, tri=tri))
    return out


def lod_of(s):
    m = re.search(r"\.lod(\d+)\.", s)
    return int(m.group(1)) if m else 0


def weld_pos(pos, nrm, uv, tri):
    _, first, inv = np.unique(np.round(pos, 5), axis=0, return_index=True, return_inverse=True)
    out = inv[tri]
    keep = (out[:, 0] != out[:, 1]) & (out[:, 1] != out[:, 2]) & (out[:, 0] != out[:, 2])
    return pos[first], nrm[first], uv[first], out[keep]


def make_obj(name, pos, nrm, uv, tri):
    me = bpy.data.meshes.new(name)
    me.from_pydata(pos.tolist(), [], tri.tolist())
    me.update()
    uvl = me.uv_layers.new(name="UVMap")
    lv = np.empty(len(me.loops), dtype=np.int32)
    me.loops.foreach_get("vertex_index", lv)
    uvl.data.foreach_set("uv", np.ascontiguousarray(uv[lv].astype(np.float32)).reshape(-1))
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    return ob


def drop(ob):
    me = ob.data
    bpy.data.objects.remove(ob, do_unlink=True)
    if me.users == 0:
        bpy.data.meshes.remove(me)


def decimate(ob, ratio):
    if ratio >= 0.999 or len(ob.data.polygons) < MIN_POLY:
        return
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    m = ob.modifiers.new("d", type="DECIMATE")
    m.decimate_type = "COLLAPSE"
    m.ratio = ratio
    bpy.ops.object.modifier_apply(modifier=m.name)


def mesh_arrays(ob):
    me = ob.data
    n = len(me.vertices)
    pos = np.empty(n * 3, dtype=np.float32); me.vertices.foreach_get("co", pos)
    pos = pos.reshape(-1, 3)
    nl = len(me.loops)
    lv = np.empty(nl, dtype=np.int32); me.loops.foreach_get("vertex_index", lv)
    nn = np.empty(nl * 3, dtype=np.float32); me.corner_normals.foreach_get("vector", nn)
    nn = nn.reshape(-1, 3)
    uvl = np.zeros((nl, 2), dtype=np.float32)
    if me.uv_layers.active:
        me.uv_layers.active.data.foreach_get("uv", uvl.reshape(-1))
    _, first = np.unique(lv, return_index=True)
    nrm = np.zeros((n, 3), dtype=np.float32); nrm[lv[first]] = nn[first]
    uv = np.zeros((n, 2), dtype=np.float32); uv[lv[first]] = uvl[first]
    me.calc_loop_triangles()
    m = len(me.loop_triangles)
    tl = np.empty(m * 3, dtype=np.int32); me.loop_triangles.foreach_get("loops", tl)
    return pos, nrm, uv, lv[tl.reshape(-1, 3)]


def write_trf(path, rows):
    out = ["rfver 4", "mesh %d" % len(rows)]
    for nm, mat, P, N, U, T, pid in rows:
        out.append("%s 0 %s" % (nm, mat))
        out.append(str(len(P)))
        out.extend(["%.6f %.6f %.6f" % (x, y, z) for x, y, z in P.tolist()])
        out.append("0")
        out.append(str(len(pid)))
        for i in range(len(pid)):
            k = pid[i]
            a, b, c = N[i]; u, v = U[i]
            out.append("%d %d %.6f %.6f %.6f" % (k, 4294967295, a, b, c))
            out.append("%.6f %.6f" % (u, v))
            out.append("0.000000 0.000000")
        out.append("0")
        out.append(str(len(T)))
        out.extend(["%d %d %d" % (a, b, c) for a, b, c in T.tolist()])
    out.append("end")
    io.open(path, "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")
    return os.path.getsize(path)


def part_factor(nf):
    if nf >= 2000:
        return 0.30
    if nf >= 600:
        return 0.65
    return 1.0


def cost(v, f):
    return (v * (VS_V + ME_V) + f * (IDX_F + ME_F)) / 1024 / 1024


bpy.ops.wm.read_factory_settings(use_empty=True)
orig = [p for p in parse(SRC) if lod_of(p["name"]) == 0]
lod0 = [p for p in parse(LOD0F) if len(p["tri"])]
assert len(orig) == len(lod0), "part count mismatch %d vs %d" % (len(orig), len(lod0))

rows = []
stats = []

# LOD0: exactly the validated file, untouched
fv = fF = 0
for i, p in enumerate(lod0):
    P = p["pos"][p["pidx"]]
    rows.append(("%s.%d" % (MESHNAME, i), p["mat"], P, p["nrm"], p["uv"], p["tri"], p["pidx"]))
    fv += len(p["pidx"]); fF += len(p["tri"])
stats.append((0, fF, fv, cost(fv, fF)))

for lv in range(1, LEVELS):
    fv = fF = 0
    for i, p in enumerate(orig):
        ratio = part_factor(len(p["tri"])) * RATIOS[lv]
        wp, wn, wu, wt = weld_pos(p["pos"][p["pidx"]], p["nrm"], p["uv"], p["tri"])
        ob = make_obj("%s.%d" % (MESHNAME, i), wp, wn, wu, wt)
        try:
            decimate(ob, ratio)
            P, N, U, T = mesh_arrays(ob)
        finally:
            drop(ob)
        rows.append(("%s.lod%d.%d" % (MESHNAME, lv, i), p["mat"], P, N, U, T, np.arange(len(P))))
        fv += len(P); fF += len(T)
    stats.append((lv, fF, fv, cost(fv, fF)))

print("\n== FINAL chain / %d levels ==" % LEVELS)
print("  %-5s %8s %8s %7s %10s %8s" % ("LOD", "faces", "verts", "v/f", "raw MB", "vs LOD0"))
b0 = stats[0][3]
for lv, f, v, c in stats:
    print("  %-5d %8d %8d %7.2f %10.3f %7.1f%%" % (lv, f, v, v / float(f), c, 100 * c / b0))
tot = sum(s[3] for s in stats)
print("  total = %.3f MB  (LOD0 %.3f, %.2fx)" % (tot, b0, tot / b0))
sz = write_trf(DST, rows)
print("  -> %s  %.2f MB  parts=%d" % (DST, sz / 1024 / 1024, len(rows)))

