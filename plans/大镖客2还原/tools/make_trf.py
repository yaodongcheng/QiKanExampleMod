# -*- coding: utf-8 -*-
"""Write LOD0-only TRF variants so they can be imported and compared in ModKit.

  blender -b --factory-startup -P make_trf.py -- <name> <outdir>
Produces:  原版LOD0/<name>.trf, 减面33/<name>.trf, 减面50/<name>.trf
"""
import bpy, io, math, os, re, sys
import numpy as np

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
MODELS = os.path.join(RDR2, "deliverable", "models")
argv = sys.argv
argv = argv[argv.index("--") + 1:] if "--" in argv else []
NAME = argv[0] if argv else "new_com_02_bank"
OUTROOT = argv[1] if len(argv) > 1 else os.path.join(RDR2, "out", "lod_trf")

COLOR_WHITE = 4294967295
BIG, BIG_R = 2000, 0.30
MID, MID_R = 600, 0.65


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


def strip_lod(s):
    """<name>.lod3.2 -> <name>.2   (so it becomes LOD0 on import)"""
    return re.sub(r"\.lod\d+\.", ".", s)


def write_trf(path, parts):
    out = ["rfver 4", "mesh %d" % len(parts)]
    for p in parts:
        pos = p["pos"]; pidx = p["pidx"]; nrm = p["nrm"]; uv = p["uv"]; tri = p["tri"]
        n = len(pidx)
        out.append("%s 0 %s" % (p["name"], p["mat"]))
        out.append(str(len(pos)))
        out.extend(["%.6f %.6f %.6f" % (x, y, z) for x, y, z in pos.tolist()])
        out.append("0")
        out.append(str(n))
        for i in range(n):
            a, b, c = nrm[i]
            u, v = uv[i]
            out.append("%d %d %.6f %.6f %.6f" % (pidx[i], COLOR_WHITE, a, b, c))
            out.append("%.6f %.6f" % (u, v))
            out.append("0.000000 0.000000")
        out.append("0")
        out.append(str(len(tri)))
        out.extend(["%d %d %d" % (a, b, c) for a, b, c in tri.tolist()])
    out.append("end")
    with io.open(path, "w", encoding="utf-8", errors="replace", newline="\n") as f:
        f.write("\n".join(out))
        f.write("\n")
    return os.path.getsize(path)


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
        try:
            nn = p["nrm"].astype(np.float64)
            ln = np.linalg.norm(nn, axis=1); ln[ln < 1e-9] = 1.0
            me.normals_split_custom_set_from_vertices((nn/ln[:, None]).tolist())
        except Exception:
            pass
        ob = bpy.data.objects.new(p["name"], me)
        bpy.context.collection.objects.link(ob)
        ob["mat"] = p["mat"]
        objs.append(ob)
    return objs


def dec(ob, ratio):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    m = ob.modifiers.new("d", type="DECIMATE")
    m.decimate_type = "COLLAPSE"
    m.ratio = ratio
    bpy.ops.object.modifier_apply(modifier=m.name)


def mesh_arrays(ob):
    """same semantics as export_rdr2_trf_lod.mesh_arrays"""
    me = ob.data
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
    return pos, nrm, uv, tri


def main():
    src = os.path.join(MODELS, NAME + ".trf")
    allp = parse(src)
    lod0 = [p for p in allp if lod_of(p["name"]) == 0]
    f0 = sum(len(p["tri"]) for p in lod0)
    print("[src] %d parts total, LOD0 = %d parts / %d faces" % (len(allp), len(lod0), f0))

    # ---- 1) original, LOD0 only, untouched
    d = os.path.join(OUTROOT, "1_原版LOD0")
    os.makedirs(d, exist_ok=True)
    dst = os.path.join(d, NAME + ".trf")
    sz = write_trf(dst, [dict(name=strip_lod(p["name"]), mat=p["mat"], pos=p["pos"],
                             pidx=p["pidx"], nrm=p["nrm"], uv=p["uv"], tri=p["tri"])
                         for p in lod0])
    print("[write] %-46s %d faces  %.1f MB" % (dst, f0, sz/1024/1024))

    # ---- 2) per-part budget (recommended)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    objs = build(lod0)
    for ob in objs:
        f = len(ob.data.polygons)
        if f >= BIG:
            dec(ob, BIG_R)
        elif f >= MID:
            dec(ob, MID_R)
    out2 = []
    for ob, p in zip(objs, [p for p in lod0 if len(p["tri"])]):
        P, N, U, T = mesh_arrays(ob)
        out2.append(dict(name=strip_lod(p["name"]), mat=p["mat"], pos=P,
                         pidx=np.arange(len(P)), nrm=N, uv=U, tri=T))
    f1 = sum(len(p["tri"]) for p in out2)
    d = os.path.join(OUTROOT, "2_减面33_按部件")
    os.makedirs(d, exist_ok=True)
    dst = os.path.join(d, NAME + ".trf")
    sz = write_trf(dst, out2)
    print("[write] %-46s %d faces (%.1f%%)  %.1f MB"
          % (dst, f1, 100.0*f1/f0, sz/1024/1024))

    # ---- 3) uniform 0.5 (what the current pipeline would give)
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    objs = build(lod0)
    for ob in objs:
        dec(ob, 0.5)
    out3 = []
    for ob, p in zip(objs, [p for p in lod0 if len(p["tri"])]):
        P, N, U, T = mesh_arrays(ob)
        out3.append(dict(name=strip_lod(p["name"]), mat=p["mat"], pos=P,
                         pidx=np.arange(len(P)), nrm=N, uv=U, tri=T))
    f2 = sum(len(p["tri"]) for p in out3)
    d = os.path.join(OUTROOT, "3_减面50_均匀")
    os.makedirs(d, exist_ok=True)
    dst = os.path.join(d, NAME + ".trf")
    sz = write_trf(dst, out3)
    print("[write] %-46s %d faces (%.1f%%)  %.1f MB"
          % (dst, f2, 100.0*f2/f0, sz/1024/1024))


main()
