# -*- coding: utf-8 -*-
"""render_lod_v1.py —— 从 TRF 反读渲染各档（验收用）

用法：
    blender -b --factory-startup -P render_lod_v1.py -- <trf> <outdir> <tag> [--tex <albedo目录>] [--views all|contact|detail|wire]

要点：
  · 几何**从 TRF 反读**——渲染本身就是一次读回验证（命名坏 → 档数就不对）
  · TRF 里 UV 是 v=1-源（引擎口径）；Blender 的 UV 原点在左下，所以预览时再翻回来 1-v
  · 贴图按材质名找 <albedo目录>/<材质名>.png，找不到用灰
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
    parts = []
    i = 2
    while i < len(L):
        m = PART_RE.match(L[i].strip())
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
        if i < len(L) and L[i].strip() == "0":
            i += 1
        nvert = int(L[i].strip())
        i += 1
        pidx, uv = [], []
        for k in range(nvert):
            pidx.append(int(L[i + k * 3].split()[0]))
            t = L[i + k * 3 + 1].split()
            uv.append((float(t[0]), float(t[1])))
        i += nvert * 3
        if i < len(L) and L[i].strip() == "0":
            i += 1
        ntri = int(L[i].strip())
        i += 1
        tri = [tuple(map(int, L[i + k].split()[:3])) for k in range(ntri)]
        i += ntri
        parts.append(dict(lvl=lvl, mat=mat, P=P, pidx=pidx, uv=uv, tri=tri))
    return parts


def clean():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def get_mat(name, texdir, cache):
    if name in cache:
        return cache[name]
    m = bpy.data.materials.new(name=name[:60])
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = 0.75
    bsdf.inputs["Metallic"].default_value = 0.0
    p = None
    if texdir:
        for cand in (name + ".png", name.lower() + ".png"):
            f = os.path.join(texdir, cand)
            if os.path.exists(f):
                p = f
                break
    if p:
        img = bpy.data.images.load(p, check_existing=True)
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = img
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    else:
        bsdf.inputs["Base Color"].default_value = (0.62, 0.60, 0.56, 1)
    cache[name] = m
    return m


def build(parts, texdir):
    cache = {}
    objs = {}
    for p in parts:
        me = bpy.data.meshes.new("m%d_%d" % (p["lvl"], len(objs)))
        verts = p["P"]
        faces = []
        for (a, b, c) in p["tri"]:
            faces.append((p["pidx"][a], p["pidx"][b], p["pidx"][c]))
        me.from_pydata(verts, [], faces)
        me.update()
        uvl = me.uv_layers.new(name="UVMap")
        # 每个 loop 的 UV：loop 顺序 == faces 顺序 * 3
        li = 0
        for ti, (a, b, c) in enumerate(p["tri"]):
            for j, vi in enumerate((a, b, c)):
                u, v = p["uv"][vi] if False else p["uv"][vi]
                uvl.data[li].uv = (u, v)   # TRF 存的 UV 直接用：实测这样字是正的（sign_B 实验）
                li += 1
        me.materials.append(get_mat(p["mat"], texdir, cache))
        ob = bpy.data.objects.new("o%d_%d" % (p["lvl"], len(objs)), me)
        bpy.context.collection.objects.link(ob)
        objs.setdefault(p["lvl"], []).append(ob)
    # 按档合并，方便隐藏
    for lvl, os_ in list(objs.items()):
        bpy.ops.object.select_all(action="DESELECT")
        for o in os_:
            o.select_set(True)
        bpy.context.view_layer.objects.active = os_[0]
        if len(os_) > 1:
            bpy.ops.object.join()
        objs[lvl] = [bpy.context.view_layer.objects.active]
    return objs


def scene_bbox(objs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for c in o.bound_box:
            w = o.matrix_world @ Vector(c)
            lo = Vector((min(lo[i], w[i]) for i in range(3)))
            hi = Vector((max(hi[i], w[i]) for i in range(3)))
    return lo, hi


def setup_light():
    w = bpy.data.worlds.new("W")
    bpy.context.scene.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.45, 0.50, 0.58, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 0.85

    ld = bpy.data.lights.new("sun", "SUN")
    ld.energy = 3.2
    ld.angle = math.radians(3)
    so = bpy.data.objects.new("sun", ld)
    bpy.context.collection.objects.link(so)
    so.rotation_euler = (math.radians(55), 0, math.radians(140))


def add_camera(center, diag):
    cd = bpy.data.cameras.new("cam")
    cd.lens = 50
    cd.sensor_width = 36
    co = bpy.data.objects.new("cam", cd)
    bpy.context.collection.objects.link(co)
    bpy.context.scene.camera = co
    return co


def aim(co, target, az, elev, dist):
    a = math.radians(az)
    e = math.radians(elev)
    pos = Vector((target.x + dist * math.cos(e) * math.cos(a),
                  target.y + dist * math.cos(e) * math.sin(a),
                  target.z + dist * math.sin(e)))
    co.location = pos
    d = target - pos
    co.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


def render_to(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    trf, outdir, tag = argv[0], argv[1], argv[2]
    texdir = ""
    if "--tex" in argv:
        texdir = argv[argv.index("--tex") + 1]
    os.makedirs(outdir, exist_ok=True)

    clean()
    parts = parse_trf(trf)
    levels = sorted({p["lvl"] for p in parts})
    tris = {}
    for p in parts:
        tris.setdefault(p["lvl"], 0)
        tris[p["lvl"]] += len(p["tri"])
    objs = build(parts, texdir)
    setup_light()
    lo, hi = scene_bbox(objs[levels[0]])
    center = (lo + hi) / 2
    diag = (hi - lo).length
    cam = add_camera(center, diag)

    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.samples = 24
    sc.cycles.use_denoising = True
    sc.cycles.device = "CPU"
    sc.render.resolution_x = 900
    sc.render.resolution_y = 640
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "Standard"

    dist_fit = diag * 1.55
    # ---- 接触表：同机位逐档
    aim(cam, center, az=50, elev=26, dist=dist_fit)
    for lv in levels:
        for k, o in objs.items():
            o[0].hide_render = (k != lv)
        render_to(os.path.join(outdir, "L%d.png" % lv))

    # ---- 细节：拉近 2.4 倍，只出 LOD0 / LOD1 / LOD3
    aim(cam, center, az=50, elev=26, dist=dist_fit / 2.4)
    for lv in [x for x in (0, 1, 3) if x in levels]:
        for k, o in objs.items():
            o[0].hide_render = (k != lv)
        render_to(os.path.join(outdir, "D%d.png" % lv))

    # ---- 线框：LOD3 / LOD5（用 Wireframe 修改器 + 自发光）
    wmat = bpy.data.materials.new("w")
    wmat.use_nodes = True
    wmat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.05, 0.05, 0.06, 1)
    wr = bpy.data.materials.new("wr")
    wr.use_nodes = True
    em = wr.node_tree.nodes.new("ShaderNodeEmission")
    em.inputs[0].default_value = (1.0, 0.72, 0.25, 1)
    wr.node_tree.links.new(em.outputs[0], wr.node_tree.nodes["Material Output"].inputs["Surface"])

    for lv in [x for x in (3, 5) if x in levels]:
        for k, o in objs.items():
            o[0].hide_render = (k != lv)
        ob = objs[lv][0]
        saved = ob.data.copy()
        mod = ob.modifiers.new("wire", "WIREFRAME")
        mod.thickness = max(0.01, diag * 0.0016)
        ob.data.materials.clear()
        ob.data.materials.append(wmat)
        ob.data.materials.append(wr)
        # 线框修改器输出到材质槽 1
        mod.material_offset = 1
        aim(cam, center, az=50, elev=26, dist=dist_fit)
        render_to(os.path.join(outdir, "W%d.png" % lv))
        ob.modifiers.remove(mod)
        ob.data = saved

    io.open(os.path.join(outdir, "readback.txt"), "w", encoding="utf-8").write(
        "trf=%s\nlevels=%s\ntris=%s\n" % (trf, levels, tris))
    print("[render] %s  档位=%s  面数=%s" % (tag, levels, tris))


if __name__ == "__main__":
    main()
