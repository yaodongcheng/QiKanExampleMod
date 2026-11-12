# -*- coding: utf-8 -*-
"""Build a .blend to eyeball-compare LOD0-6 of the new chain.

One collection per LOD (each a single joined object, 12 material slots), all
overlapping at the origin -> toggle the eye icon in the Outliner to compare.

  blender -b --factory-startup -P make_lod_blend.py -- <trf> <matmap.csv> <pngdir> <out.blend>
"""
import bpy, csv, io, math, os, re, sys
import numpy as np
from mathutils import Vector

argv = sys.argv
argv = argv[argv.index("--") + 1:] if "--" in argv else []
TRF, MATMAP, PNGDIR, OUT = argv[0], argv[1], argv[2], argv[3]

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
        pidx = blk[:, 0].astype(np.int64); nrm = blk[:, 2:5]; uv = blk[:, 5:7]
        b = int(lines[i].strip()); i += 1
        if b > 0:
            i += 1; w = int(lines[i].strip()); i += 1; i += w
        nt = int(lines[i].strip()); i += 1
        tri = np.fromstring(" ".join(lines[i:i + nt]), sep=" ").astype(np.int64).reshape(-1, 3)
        i += nt
        out.append(dict(name=name, mat=mat, pos=pos, pidx=pidx, nrm=nrm, uv=uv, tri=tri))
    return out


# ---------- materials (albedo names come from matmap.csv, already validated)
albedo = {}
with io.open(MATMAP, encoding="utf-8-sig", newline="") as f:
    for row in csv.DictReader(f):
        albedo[row["material"]] = row["albedo"]

mats = {}
for mat, alb in albedo.items():
    pass  # materials are created after the file reset below

# ---------- geometry
parts = parse(TRF)
bylod = {}
for p in parts:
    bylod.setdefault(lod_of(p["name"]), {})[slot_of(p["name"])] = p

mat_order = [p["mat"] for s, p in sorted(bylod[min(bylod)].items())]
mat_index = {m: i for i, m in enumerate(mat_order)}

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
# materials must be re-created after the file reset
mats2 = {}
for mat, alb in albedo.items():
    m = bpy.data.materials.new(mat[:40])
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes.get("Principled BSDF")
    b.inputs["Roughness"].default_value = 0.85
    p = os.path.join(PNGDIR, alb + ".png") if alb else None
    if p and os.path.isfile(p):
        img = bpy.data.images.load(p, check_existing=False)
        img.name = alb
        nd = nt.nodes.new("ShaderNodeTexImage")
        nd.image = img
        nd.location = (-420, 180)
        nt.links.new(nd.outputs["Color"], b.inputs["Base Color"])
    mats2[mat] = m

objs = []
for lod in sorted(bylod):
    d = bylod[lod]
    verts, tris, mslot, uvs = [], [], [], []
    voff = 0
    for s in sorted(d):
        p = d[s]
        P = p["pos"][p["pidx"]]
        T = p["tri"]
        verts.extend(P.tolist())
        tris.extend((T + voff).tolist())
        mslot.extend([mat_index.get(p["mat"], 0)] * len(T))
        uvs.extend(p["uv"][T].reshape(-1, 2).tolist())
        voff += len(P)
    me = bpy.data.meshes.new("bank_LOD%d" % lod)
    me.from_pydata(verts, [], tris)
    me.update()
    uvl = me.uv_layers.new(name="UVMap")
    uvl.data.foreach_set("uv", np.asarray(uvs, dtype=np.float32).reshape(-1))
    mi = np.asarray(mslot, dtype=np.int32)
    me.polygons.foreach_set("material_index", mi)
    for m in mat_order:
        me.materials.append(mats2[m])
    me.update()
    ob = bpy.data.objects.new("LOD%d  (%d 面)" % (lod, len(tris)), me)
    coll = bpy.data.collections.new("LOD%d  %d面" % (lod, len(tris)))
    sc.collection.children.link(coll)
    coll.objects.link(ob)
    coll.hide_viewport = (lod != 0)          # 默认只显示 LOD0
    coll.hide_render = (lod != 0)
    objs.append(ob)
    print("[lod%d] %6d 面 / %6d 顶点  材质槽 %d" % (lod, len(tris), len(verts), len(mat_order)))

# ---------- camera / light / world
vs = np.array([v.co[:] for v in objs[0].data.vertices])
mn, mx = vs.min(axis=0), vs.max(axis=0)
ctr = Vector(((mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, (mn[2] + mx[2]) / 2))
size = float(np.linalg.norm(mx - mn))

w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
bg = w.node_tree.nodes["Background"]
bg.inputs[0].default_value = (0.55, 0.62, 0.72, 1)
bg.inputs[1].default_value = 1.0

ld = bpy.data.lights.new("Sun", type="SUN"); ld.energy = 3.2; ld.angle = math.radians(6)
lo = bpy.data.objects.new("Sun", ld); sc.collection.objects.link(lo)
lo.rotation_euler = (math.radians(52), 0, math.radians(38))

cd = bpy.data.cameras.new("Cam"); cd.type = "ORTHO"; cd.ortho_scale = size * 1.02
cam = bpy.data.objects.new("Cam", cd); sc.collection.objects.link(cam)
dv = Vector((1.0, -1.15, 0.60)).normalized()
cam.location = ctr + dv * size * 2.0
cam.rotation_euler = (dv * -1).to_track_quat("-Z", "Y").to_euler()
sc.camera = cam
sc.render.resolution_x, sc.render.resolution_y = 1600, 1100
sc.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in [e.identifier for e in
    bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE_NEXT"

# ---------- in-file instructions
txt = bpy.data.texts.new("怎么用")
txt.write("""银行 LOD0-6 对比（新链）
============================
Outliner 里有 7 个集合：LOD0 17731面 / LOD1 14018 / LOD2 8538 / LOD3 3946 / LOD4 1832 / LOD5 1180 / LOD6 774
每个集合里是一个物体，7 个物体【完全重叠】在原点。

默认只显示 LOD0。点集合左边的小眼睛即可显示/隐藏某一档：
  · 只勾 LOD0 → 近景效果
  · 只勾 LOD3 → 中距离
  · 同时勾两档 → 会看到 Z-fighting（正常，说明重叠）
  · 全勾 → 最远那档的面会把近档盖住

物体名里带面数，方便对照。
材质 12 个，贴图已打包，可直接拷走。
""")

try:
    bpy.ops.file.pack_all()
    print("[pack] textures packed")
except Exception as e:
    print("[pack] skipped: %r" % (e,))

os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=OUT)
print("[saved] %s  %.1f MB" % (OUT, os.path.getsize(OUT) / 1024 / 1024))
