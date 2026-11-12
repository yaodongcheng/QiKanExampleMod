# -*- coding: utf-8 -*-
"""Render one still per LOD straight from a TRF (no blend needed)."""
import bpy, csv, io, math, os, re, sys
import numpy as np
from mathutils import Vector

argv = sys.argv
argv = argv[argv.index("--") + 1:] if "--" in argv else []
TRF, MATMAP, PNGDIR, OUTDIR = argv[0], argv[1], argv[2], argv[3]
os.makedirs(OUTDIR, exist_ok=True)
HDR = re.compile(r"^(\S+\.\d+) 0 (\S+)$")


def lod_of(s):
    m = re.search(r"\.lod(\d+)\.", s)
    return int(m.group(1)) if m else 0


def slot_of(s):
    return int(re.search(r"\.(\d+)$", s).group(1))


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
        nv = int(lines[i].strip()); i += 1
        blk = np.fromstring(" ".join(lines[i:i + nv * 3]), sep=" ").reshape(nv, 9)
        i += nv * 3
        pidx = blk[:, 0].astype(np.int64); uv = blk[:, 5:7]
        b = int(lines[i].strip()); i += 1
        if b > 0:
            i += 1; w = int(lines[i].strip()); i += 1; i += w
        nt = int(lines[i].strip()); i += 1
        tri = np.fromstring(" ".join(lines[i:i + nt]), sep=" ").astype(np.int64).reshape(-1, 3)
        i += nt
        out.append(dict(name=name, mat=mat, pos=pos, pidx=pidx, uv=uv, tri=tri))
    return out


albedo = {}
with io.open(MATMAP, encoding="utf-8-sig", newline="") as f:
    for row in csv.DictReader(f):
        albedo[row["material"]] = row["albedo"]

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
mats = {}
for mat, alb in albedo.items():
    m = bpy.data.materials.new(mat[:40]); m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes.get("Principled BSDF"); b.inputs["Roughness"].default_value = 0.85
    p = os.path.join(PNGDIR, alb + ".png") if alb else None
    if p and os.path.isfile(p):
        img = bpy.data.images.load(p, check_existing=False)
        nd = nt.nodes.new("ShaderNodeTexImage"); nd.image = img
        nt.links.new(nd.outputs["Color"], b.inputs["Base Color"])
    mats[mat] = m

parts = parse(TRF)
bylod = {}
for p in parts:
    bylod.setdefault(lod_of(p["name"]), {})[slot_of(p["name"])] = p
mat_order = [p["mat"] for s, p in sorted(bylod[min(bylod)].items())]
midx = {m: i for i, m in enumerate(mat_order)}

objs = {}
for lod in sorted(bylod):
    d = bylod[lod]
    verts, tris, ms, uvs = [], [], [], []
    voff = 0
    for s in sorted(d):
        p = d[s]; P = p["pos"][p["pidx"]]; T = p["tri"]
        verts.extend(P.tolist()); tris.extend((T + voff).tolist())
        ms.extend([midx.get(p["mat"], 0)] * len(T))
        uvs.extend(p["uv"][T].reshape(-1, 2).tolist()); voff += len(P)
    me = bpy.data.meshes.new("L%d" % lod)
    me.from_pydata(verts, [], tris); me.update()
    uvl = me.uv_layers.new(name="UV"); uvl.data.foreach_set("uv", np.asarray(uvs, np.float32).reshape(-1))
    me.polygons.foreach_set("material_index", np.asarray(ms, np.int32))
    for m in mat_order:
        me.materials.append(mats[m])
    me.update()
    ob = bpy.data.objects.new("L%d" % lod, me)
    sc.collection.objects.link(ob)
    objs[lod] = ob
    print("[lod%d] %d 面" % (lod, len(tris)))

vs = np.array([v.co[:] for v in objs[0].data.vertices])
mn, mx = vs.min(axis=0), vs.max(axis=0)
ctr = Vector(((mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, (mn[2] + mx[2]) / 2))
size = float(np.linalg.norm(mx - mn))
w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
bg = w.node_tree.nodes["Background"]
bg.inputs[0].default_value = (0.55, 0.62, 0.72, 1); bg.inputs[1].default_value = 1.0
ld = bpy.data.lights.new("S", type="SUN"); ld.energy = 3.2; ld.angle = math.radians(6)
lo = bpy.data.objects.new("S", ld); sc.collection.objects.link(lo)
lo.rotation_euler = (math.radians(52), 0, math.radians(38))
cd = bpy.data.cameras.new("C"); cd.type = "ORTHO"; cd.ortho_scale = size * 1.02
cam = bpy.data.objects.new("C", cd); sc.collection.objects.link(cam)
dv = Vector((1.0, -1.15, 0.60)).normalized()
cam.location = ctr + dv * size * 2.0
cam.rotation_euler = (dv * -1).to_track_quat("-Z", "Y").to_euler()
sc.camera = cam
sc.render.engine = "CYCLES"; sc.cycles.device = "CPU"; sc.cycles.samples = 24
sc.cycles.use_denoising = True
sc.render.resolution_x, sc.render.resolution_y = 960, 660

for lod in sorted(objs):
    for k, o in objs.items():
        o.hide_render = (k != lod)
    p = os.path.join(OUTDIR, "lod_%d.png" % lod)
    sc.render.filepath = p
    bpy.ops.render.render(write_still=True)
    print("[render] %s" % os.path.basename(p))
