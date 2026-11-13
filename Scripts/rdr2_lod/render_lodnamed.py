# -*- coding: utf-8 -*-
"""渲染带 .lodN. 命名的 TRF 各档。用法：
  blender -b --factory-startup -P render_lodnamed.py -- <trf> <texdir> <matmap.csv> <outdir> <tag>
"""
import csv
import io
import os
import re
import sys

import bpy
from mathutils import Vector


def parse(path):
    L = io.open(path, encoding="utf-8", errors="replace").read().split("\n")
    parts = []
    i = 2
    while i < len(L):
        m = re.match(r"^(\S+?)(?:\.lod(\d+))?\.(\d+) 0 (\S+)$", L[i].strip())
        if not m:
            i += 1
            continue
        lvl = int(m.group(2) or 0)
        mat = m.group(4)
        i += 1
        npos = int(L[i].strip())
        i += 1
        P = [tuple(map(float, L[i + k].split()[:3])) for k in range(npos)]
        i += npos
        if L[i].strip() == "0":
            i += 1
        nvert = int(L[i].strip())
        i += 1
        pidx, uv = [], []
        for k in range(nvert):
            pidx.append(int(L[i + k * 3].split()[0]))
            u = L[i + k * 3 + 1].split()
            uv.append((float(u[0]), float(u[1])))
        i += nvert * 3
        if L[i].strip() == "0":
            i += 1
        ntri = int(L[i].strip())
        i += 1
        tri = [tuple(map(int, L[i + k].split()[:3])) for k in range(ntri)]
        i += ntri
        parts.append(dict(lvl=lvl, mat=mat, P=P, pidx=pidx, uv=uv, tri=tri))
    return parts


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    src, texdir, matmap, outdir, tag = argv[0], argv[1], argv[2], argv[3], argv[4]
    os.makedirs(outdir, exist_ok=True)
    albedo = {r["material"]: r.get("albedo", "") for r in csv.DictReader(io.open(matmap, encoding="utf-8-sig"))}

    parts = parse(src)
    lev = {}
    for p in parts:
        lev.setdefault(p["lvl"], []).append(p)
    print("[src] %d 部件  档: %s" % (len(parts), {k: len(v) for k, v in sorted(lev.items())}))

    bpy.ops.wm.read_factory_settings(use_empty=True)
    scn = bpy.context.scene
    lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
    for p in lev[min(lev)]:
        for v in p["P"]:
            q = Vector(v)
            lo = Vector((min(lo.x, q.x), min(lo.y, q.y), min(lo.z, q.z)))
            hi = Vector((max(hi.x, q.x), max(hi.y, q.y), max(hi.z, q.z)))
    center = (lo + hi) * 0.5
    size = max((hi - lo).x, (hi - lo).y, (hi - lo).z)

    cd = bpy.data.cameras.new("c"); cd.type = "ORTHO"; cd.ortho_scale = size * 1.1
    cam = bpy.data.objects.new("c", cd); scn.collection.objects.link(cam); scn.camera = cam
    d = Vector((0.8, -1.0, 0.35)).normalized()
    cam.location = center + d * (size * 3)
    cam.rotation_euler = (center - cam.location).normalized().to_track_quat("-Z", "Y").to_euler()

    sd = bpy.data.lights.new("s", type="SUN"); sd.energy = 3.5
    sun = bpy.data.objects.new("s", sd); sun.rotation_euler = (0.9, 0.1, 0.7)
    scn.collection.objects.link(sun)
    w = bpy.data.worlds.new("w"); w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.42, 0.55, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 1.2
    scn.world = w
    try:
        scn.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        scn.render.engine = "BLENDER_EEVEE"
    scn.render.resolution_x = 1120
    scn.render.resolution_y = 840

    mats = {}
    for p in parts:
        m = p["mat"]
        if m in mats:
            continue
        mat = bpy.data.materials.new(m[:40]); mat.use_nodes = True
        b = mat.node_tree.nodes.get("Principled BSDF")
        a = albedo.get(m, "")
        png = os.path.join(texdir, a + ".png") if a else ""
        if png and os.path.isfile(png):
            t = mat.node_tree.nodes.new("ShaderNodeTexImage")
            t.image = bpy.data.images.load(png)
            mat.node_tree.links.new(t.outputs["Color"], b.inputs["Base Color"])
        else:
            b.inputs["Base Color"].default_value = (0.7, 0.7, 0.7, 1)
        b.inputs["Roughness"].default_value = 0.85
        mats[m] = mat

    for lv in sorted(lev):
        objs = []
        for p in lev[lv]:
            faces = [(p["pidx"][a], p["pidx"][b], p["pidx"][c]) for (a, b, c) in p["tri"]]
            if not faces:
                continue
            me = bpy.data.meshes.new("m")
            me.from_pydata(p["P"], [], faces)
            me.update()
            uvl = me.uv_layers.new(name="UVMap")
            flat = []
            for (a, b, c) in p["tri"]:
                for k in (a, b, c):
                    flat.extend([p["uv"][k][0], p["uv"][k][1]])
            uvl.data.foreach_set("uv", flat)
            me.materials.append(mats[p["mat"]])
            ob = bpy.data.objects.new("o", me); scn.collection.objects.link(ob); objs.append(ob)
        if not objs:
            continue
        scn.render.filepath = os.path.join(outdir, "%s_L%d.png" % (tag, lv))
        bpy.ops.render.render(write_still=True)
        print("[render] %s_L%d  %d 面" % (tag, lv, sum(len(o.data.polygons) for o in objs)))
        for o in objs:
            bpy.data.objects.remove(o, do_unlink=True)


if __name__ == "__main__":
    main()
