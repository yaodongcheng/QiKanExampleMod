# -*- coding: utf-8 -*-
"""Blend with BOTH chains as separate collection groups, so you can eyeball any LOD.

  blender -b --factory-startup -P make_lod_blend2.py -- "p1,trf1;p2,trf2" matmap.csv pngdir out.blend
Each collection is named  <prefix>_LODn  <faces>面  and only LOD0 of the first
prefix is visible on open.
"""
import bpy, csv, io, math, os, re, sys
import numpy as np
from mathutils import Vector

argv = sys.argv
argv = argv[argv.index("--") + 1:] if "--" in argv else []
SPEC, MATMAP, PNGDIR, OUT = argv[0], argv[1], argv[2], argv[3]
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

first = True
ref_size = None
for item in SPEC.split(";"):
    prefix, trf = item.split(",")
    parts = parse(trf)
    bylod = {}
    for p in parts:
        bylod.setdefault(lod_of(p["name"]), {})[slot_of(p["name"])] = p
    mat_order = [p["mat"] for s, p in sorted(bylod[min(bylod)].items())]
    midx = {m: i for i, m in enumerate(mat_order)}
    for lod in sorted(bylod):
        d = bylod[lod]
        verts, tris, ms, uvs = [], [], [], []
        voff = 0
        for s in sorted(d):
            p = d[s]; P = p["pos"][p["pidx"]]; T = p["tri"]
            verts.extend(P.tolist()); tris.extend((T + voff).tolist())
            ms.extend([midx.get(p["mat"], 0)] * len(T))
            uvs.extend(p["uv"][T].reshape(-1, 2).tolist()); voff += len(P)
        me = bpy.data.meshes.new("%s_LOD%d" % (prefix, lod))
        me.from_pydata(verts, [], tris); me.update()
        uvl = me.uv_layers.new(name="UV")
        uvl.data.foreach_set("uv", np.asarray(uvs, np.float32).reshape(-1))
        me.polygons.foreach_set("material_index", np.asarray(ms, np.int32))
        for m in mat_order:
            me.materials.append(mats[m])
        me.update()
        nm = "%s_LOD%d  %d面" % (prefix, lod, len(tris))
        ob = bpy.data.objects.new(nm, me)
        coll = bpy.data.collections.new(nm)
        sc.collection.children.link(coll)
        coll.objects.link(ob)
        coll.hide_viewport = not (first and lod == 0)
        coll.hide_render = not (first and lod == 0)
        if ref_size is None:
            vs = np.array([v.co[:] for v in me.vertices])
            mn, mx = vs.min(axis=0), vs.max(axis=0)
            ref_ctr = Vector(((mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, (mn[2] + mx[2]) / 2))
            ref_size = float(np.linalg.norm(mx - mn))
        print("[%s] %6d 面 / %6d 顶点" % (nm, len(tris), len(verts)))
    first = False

w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
bg = w.node_tree.nodes["Background"]
bg.inputs[0].default_value = (0.55, 0.62, 0.72, 1); bg.inputs[1].default_value = 1.0
ld = bpy.data.lights.new("Sun", type="SUN"); ld.energy = 3.2; ld.angle = math.radians(6)
lo = bpy.data.objects.new("Sun", ld); sc.collection.objects.link(lo)
lo.rotation_euler = (math.radians(52), 0, math.radians(38))
cd = bpy.data.cameras.new("Cam"); cd.type = "ORTHO"; cd.ortho_scale = ref_size * 1.02
cam = bpy.data.objects.new("Cam", cd); sc.collection.objects.link(cam)
dv = Vector((1.0, -1.15, 0.60)).normalized()
cam.location = ref_ctr + dv * ref_size * 2.0
cam.rotation_euler = (dv * -1).to_track_quat("-Z", "Y").to_euler()
sc.camera = cam
sc.render.resolution_x, sc.render.resolution_y = 1600, 1100

txt = bpy.data.texts.new("怎么用")
txt.write("""银行 LOD 对比（两套链）
==============================
Outliner 里两组集合，前缀区分：
  陡5_*  = 推荐方案：5 档，陡比率，远景只到 LOD4，落盘约 4.76 MiB（基准的 49.9%）
  平7_*  = 备选：7 档，平缓比率，LOD6 还有 3,946 面，落盘约 6.38 MiB（66.9%）

每个集合里是一个物体，所有物体【完全重叠】在原点。
点集合左边的小眼睛即可显示/隐藏：
  · 只勾 陡5_LOD0 → 近景
  · 只勾 陡5_LOD4 → 最远档（1,832 面，远景可接受）
  · 平7_LOD5 / 平7_LOD6 → 对比"平缓版远景为什么还完整"
  · 同时勾两档 → Z-fighting（正常）

注意：陡降 7 档那版的 LOD5/6（1,180 / 774 面）会碎，没有放进这个文件；
想看碎裂效果见 preview\\10_远景档_真实视距.png
""")

try:
    bpy.ops.file.pack_all()
    print("[pack] textures packed")
except Exception as e:
    print("[pack] skipped: %r" % (e,))

os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=OUT)
print("[saved] %s  %.1f MB" % (OUT, os.path.getsize(OUT) / 1024 / 1024))
