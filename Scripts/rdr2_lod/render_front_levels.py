# -*- coding: utf-8 -*-
"""正视立面渲染：对着招牌那一面（az 50°，平视 elev≈2°），逐档出图

用法： blender -b --factory-startup -P render_front_levels.py -- <trf> <outdir> --tex <alb> [--levels 0,3,4,5]
"""
import io
import math
import os
import re
import sys

import bpy
from mathutils import Vector

PART_RE = re.compile(r"^(\S+?)(?:\.lod(\d+))?\.(\d+) 0 (\S+)$")


def parse_trf(path):
    L = io.open(path, encoding="utf-8", errors="replace").read().split("\n")
    parts, i = [], 2
    while i < len(L) and L[i].strip() and L[i].strip() != "end":
        m = PART_RE.match(L[i].strip())
        if not m:
            i += 1
            continue
        lvl, mat = int(m.group(2) or 0), m.group(4)
        i += 1
        npos = int(L[i].strip()); i += 1
        P = [tuple(map(float, L[i + k].split()[:3])) for k in range(npos)]; i += npos
        if L[i].strip() == "0":
            i += 1
        nv = int(L[i].strip()); i += 1
        pidx, uv = [], []
        for k in range(nv):
            pidx.append(int(L[i + k * 3].split()[0]))
            t = L[i + k * 3 + 1].split()
            uv.append((float(t[0]), float(t[1])))
        i += nv * 3
        if L[i].strip() == "0":
            i += 1
        nt = int(L[i].strip()); i += 1
        tri = [tuple(map(int, L[i + k].split()[:3])) for k in range(nt)]; i += nt
        parts.append(dict(lvl=lvl, mat=mat, P=P, pidx=pidx, uv=uv, tri=tri))
    return parts


def get_mat(name, texdir, cache):
    if name in cache:
        return cache[name]
    m = bpy.data.materials.new(name=name[:50])
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes.get("Principled BSDF")
    b.inputs["Roughness"].default_value = 0.85
    p = os.path.join(texdir, name + ".png") if texdir else ""
    if p and os.path.exists(p):
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(p, check_existing=True)
        nt.links.new(tex.outputs["Color"], b.inputs["Base Color"])
    else:
        b.inputs["Base Color"].default_value = (0.62, 0.62, 0.6, 1)
    cache[name] = m
    return m


def build(parts, level, texdir):
    cache = {}
    for p in parts:
        if p["lvl"] != level:
            continue
        me = bpy.data.meshes.new("m")
        faces = [(p["pidx"][a], p["pidx"][b], p["pidx"][c]) for (a, b, c) in p["tri"]]
        me.from_pydata(p["P"], [], faces)
        me.update()
        uvl = me.uv_layers.new(name="UVMap")
        li = 0
        for (a, b, c) in p["tri"]:
            for vi in (a, b, c):
                uvl.data[li].uv = p["uv"][vi]
                li += 1
        me.materials.append(get_mat(p["mat"], texdir, cache))
        ob = bpy.data.objects.new("o", me)
        bpy.context.collection.objects.link(ob)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    trf, outdir = argv[0], argv[1]
    texdir = argv[argv.index("--tex") + 1] if "--tex" in argv else ""
    levels = [int(x) for x in argv[argv.index("--levels") + 1].split(",")] if "--levels" in argv else [0, 1, 2, 3, 4, 5, 6]
    dfac = float(argv[argv.index("--dist") + 1]) if "--dist" in argv else 1.02
    os.makedirs(outdir, exist_ok=True)
    parts = parse_trf(trf)

    pts = [q for p in parts if p["lvl"] == 0 for q in p["P"]] or [q for p in parts for q in p["P"]]
    lo = Vector((min(q[0] for q in pts), min(q[1] for q in pts), min(q[2] for q in pts)))
    hi = Vector((max(q[0] for q in pts), max(q[1] for q in pts), max(q[2] for q in pts)))
    c = (lo + hi) / 2
    diag = (hi - lo).length

    for lv in levels:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        w = bpy.data.worlds.new("W")
        bpy.context.scene.world = w
        w.use_nodes = True
        w.node_tree.nodes["Background"].inputs[0].default_value = (0.58, 0.62, 0.68, 1)
        w.node_tree.nodes["Background"].inputs[1].default_value = 1.15
        ld = bpy.data.lights.new("s", "SUN")
        ld.energy = 3.2
        so = bpy.data.objects.new("s", ld)
        bpy.context.collection.objects.link(so)
        so.rotation_euler = (math.radians(58), 0, math.radians(30))
        cd = bpy.data.cameras.new("c")
        cd.lens = 60
        co = bpy.data.objects.new("c", cd)
        bpy.context.collection.objects.link(co)
        bpy.context.scene.camera = co
        sc = bpy.context.scene
        sc.render.engine = "CYCLES"
        sc.cycles.samples = 28
        sc.cycles.use_denoising = True
        sc.cycles.device = "CPU"
        sc.render.resolution_x = 1200
        sc.render.resolution_y = 760
        sc.view_settings.view_transform = "Standard"

        build(parts, lv, texdir)
        # 正对招牌面：az 50°，几乎平视
        a = math.radians(50)
        dist = diag * dfac
        pos = Vector((c.x + dist * math.cos(a), c.y + dist * math.sin(a), c.z + diag * 0.03))
        co.location = pos
        co.rotation_euler = (c - pos).to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = os.path.join(outdir, "F%d.png" % lv)
        bpy.ops.render.render(write_still=True)
        print("[front] LOD%d -> %s" % (lv, sc.render.filepath))


if __name__ == "__main__":
    main()
