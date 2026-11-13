# -*- coding: utf-8 -*-
"""招牌面 UV 对照：同一面、四种 UV 变换，看哪种字是对的

用法： blender -b --factory-startup -P render_sign_test.py -- <trf> <outdir> [--tex <alb>]

输出：sign_{A,B,C,D}.png + facade_front.png（正视立面）
  A: U 不翻, V 翻   ← 当前渲染器 / 游戏内口径（TRF 存 1-v，渲染时再翻回来）
  B: U 不翻, V 不翻 ← 直接把 TRF 里的 UV 原样送进 Blender
  C: U 翻,  V 翻
  D: U 翻,  V 不翻
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
    while i < len(L):
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
        nvert = int(L[i].strip()); i += 1
        pidx, uv = [], []
        for k in range(nvert):
            pidx.append(int(L[i + k * 3].split()[0]))
            t = L[i + k * 3 + 1].split()
            uv.append((float(t[0]), float(t[1])))
        i += nvert * 3
        if L[i].strip() == "0":
            i += 1
        ntri = int(L[i].strip()); i += 1
        tri = [tuple(map(int, L[i + k].split()[:3])) for k in range(ntri)]; i += ntri
        parts.append(dict(lvl=lvl, mat=mat, P=P, pidx=pidx, uv=uv, tri=tri))
    return parts


def get_mat(name, texdir, cache):
    if name in cache:
        return cache[name]
    m = bpy.data.materials.new(name=name[:50])
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes.get("Principled BSDF")
    b.inputs["Roughness"].default_value = 0.8
    p = os.path.join(texdir, name + ".png") if texdir else ""
    if p and os.path.exists(p):
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(p, check_existing=True)
        nt.links.new(tex.outputs["Color"], b.inputs["Base Color"])
    else:
        b.inputs["Base Color"].default_value = (0.6, 0.6, 0.58, 1)
    cache[name] = m
    return m


def build(parts, texdir, flip_u, flip_v):
    cache = {}
    for p in parts:
        me = bpy.data.meshes.new("m")
        faces = [(p["pidx"][a], p["pidx"][b], p["pidx"][c]) for (a, b, c) in p["tri"]]
        me.from_pydata(p["P"], [], faces)
        me.update()
        uvl = me.uv_layers.new(name="UVMap")
        li = 0
        for (a, b, c) in p["tri"]:
            for vi in (a, b, c):
                u, v = p["uv"][vi]
                uvl.data[li].uv = ((1.0 - u) if flip_u else u, (1.0 - v) if flip_v else v)
                li += 1
        me.materials.append(get_mat(p["mat"], texdir, cache))
        ob = bpy.data.objects.new("o", me)
        bpy.context.collection.objects.link(ob)


def bbox(objs):
    lo = Vector((1e9,) * 3)
    hi = Vector((-1e9,) * 3)
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector((min(lo[i], w[i]) for i in range(3)))
            hi = Vector((max(hi[i], w[i]) for i in range(3)))
    return lo, hi


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    trf, outdir = argv[0], argv[1]
    texdir = argv[argv.index("--tex") + 1] if "--tex" in argv else ""
    os.makedirs(outdir, exist_ok=True)
    parts = [p for p in parse_trf(trf) if p["lvl"] == 0]

    bpy.ops.wm.read_factory_settings(use_empty=True)
    w = bpy.data.worlds.new("W")
    bpy.context.scene.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.55, 0.58, 0.62, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 1.1
    ld = bpy.data.lights.new("s", "SUN")
    ld.energy = 3.0
    so = bpy.data.objects.new("s", ld)
    bpy.context.collection.objects.link(so)
    so.rotation_euler = (math.radians(60), 0, math.radians(35))

    cd = bpy.data.cameras.new("c")
    cd.lens = 55
    co = bpy.data.objects.new("c", cd)
    bpy.context.collection.objects.link(co)
    bpy.context.scene.camera = co

    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.samples = 24
    sc.cycles.use_denoising = True
    sc.cycles.device = "CPU"
    sc.render.resolution_x = 1100
    sc.render.resolution_y = 620
    sc.view_settings.view_transform = "Standard"

    variants = [("A", False, True), ("B", False, False), ("C", True, True), ("D", True, False)]
    for tag, fu, fv in variants:
        for ob in list(bpy.data.objects):
            if ob.type == "MESH":
                bpy.data.objects.remove(ob, do_unlink=True)
        build(parts, texdir, fu, fv)
        objs = [o for o in bpy.data.objects if o.type == "MESH"]
        lo, hi = bbox(objs)
        c = (lo + hi) / 2
        diag = (hi - lo).length
        # 正视立面（az 50°，仰角 0）= 招牌那一面
        a = math.radians(50)
        dist = diag * 1.02
        pos = Vector((c.x + dist * math.cos(a), c.y + dist * math.sin(a), c.z + diag * 0.02))
        co.location = pos
        co.rotation_euler = (c - pos).to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = os.path.join(outdir, "sign_%s.png" % tag)
        bpy.ops.render.render(write_still=True)
        print("[sign] %s -> %s" % (tag, sc.render.filepath))


if __name__ == "__main__":
    main()
