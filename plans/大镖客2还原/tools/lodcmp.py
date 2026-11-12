# -*- coding: utf-8 -*-
"""Render LOD0 vs decimated variants of a TRF building, for visual QA.

  blender -b --factory-startup -P lodcmp.py -- <name> <outdir>
"""
import bpy, io, math, os, re, sys
import numpy as np
from mathutils import Vector

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
MODELS = os.path.join(RDR2, "deliverable", "models")
ALB = os.path.join(RDR2, "deliverable", "textures", "alb")

argv = sys.argv
argv = argv[argv.index("--") + 1:] if "--" in argv else []
NAME = argv[0] if argv else "new_com_02_bank"
OUT = argv[1] if len(argv) > 1 else os.path.join(os.environ.get("TEMP", "."), "rdr2_slim", "cmp")
os.makedirs(OUT, exist_ok=True)


# ------------------------------------------------------------------ parse
def parse_trf(path):
    lines = io.open(path, encoding="utf-8", errors="replace").read().split("\n")
    i, n, parts = 0, len(lines), []
    while i < n:
        s = lines[i].strip()
        if not s or s == "end":
            i += 1
            continue
        tok = s.split()
        if len(tok) != 3 or tok[1] != "0":
            i += 1
            continue
        pname, mat = tok[0], tok[2]
        i += 1
        npos = int(lines[i].strip()); i += 1
        pos = np.fromstring(" ".join(lines[i:i + npos]), sep=" ").reshape(-1, 3)
        i += npos + 1                              # positions + lone "0"
        nvert = int(lines[i].strip()); i += 1
        blk = np.fromstring(" ".join(lines[i:i + nvert * 3]), sep=" ").reshape(nvert, 9)
        i += nvert * 3
        pidx = blk[:, 0].astype(np.int64)
        nrm, uv = blk[:, 2:5], blk[:, 5:7]
        bone = int(lines[i].strip()); i += 1
        if bone > 0:
            i += 1
            w = int(lines[i].strip()); i += 1
            i += w
        ntri = int(lines[i].strip()); i += 1
        tri = np.fromstring(" ".join(lines[i:i + ntri]), sep=" ").astype(np.int64).reshape(-1, 3)
        i += ntri
        parts.append(dict(name=pname, mat=mat, pos=pos, pidx=pidx, nrm=nrm, uv=uv, tri=tri))
    return parts


def lod_of(pname):
    m = re.search(r"\.lod(\d+)\.", pname)
    return int(m.group(1)) if m else 0


# ------------------------------------------------------------------ welds
def weld_by_pos(pos, nrm, uv, tri, eps=0.002):
    """exactly what export_rdr2_trf_lod.py does (position only)"""
    q = np.round(pos / eps).astype(np.int64)
    _, first, inv = np.unique(q, axis=0, return_index=True, return_inverse=True)
    out = inv[tri]
    keep = (out[:, 0] != out[:, 1]) & (out[:, 1] != out[:, 2]) & (out[:, 0] != out[:, 2])
    return pos[first], nrm[first], uv[first], out[keep]


def weld_uv_safe(pos, nrm, uv, tri):
    """merge only true duplicates (same pos + uv + normal) -> UV seams survive"""
    key = np.concatenate([np.round(pos, 5), np.round(uv, 5), np.round(nrm, 4)], axis=1)
    _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
    out = inv[tri]
    keep = (out[:, 0] != out[:, 1]) & (out[:, 1] != out[:, 2]) & (out[:, 0] != out[:, 2])
    return pos[first], nrm[first], uv[first], out[keep]


# ------------------------------------------------------------------ scene
def clear():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)


def find_texture(matname):
    for cand in (matname + ".png", matname + "_ab.png"):
        f = os.path.join(ALB, cand)
        if os.path.isfile(f):
            return f
    best = None
    if os.path.isdir(ALB):
        for fn in os.listdir(ALB):
            if fn.lower().endswith(".png"):
                stem = fn[:-4]
                if matname.startswith(stem) and (best is None or len(stem) > len(best[0])):
                    best = (stem, fn)
    return os.path.join(ALB, best[1]) if best else None


_mat_cache = {}
_img_cache = {}


def get_mat(matname, flat=False):
    key = (matname, flat)
    if key in _mat_cache:
        return _mat_cache[key]
    mat = bpy.data.materials.new("M_%d" % len(_mat_cache))
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    if flat:
        bsdf.inputs["Base Color"].default_value = (0.82, 0.82, 0.84, 1)
        bsdf.inputs["Roughness"].default_value = 0.9
    else:
        tex = find_texture(matname)
        if tex:
            if tex not in _img_cache:
                _img_cache[tex] = bpy.data.images.load(tex, check_existing=True)
            node = nt.nodes.new("ShaderNodeTexImage")
            node.image = _img_cache[tex]
            node.location = (-420, 220)
            nt.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
            bsdf.inputs["Roughness"].default_value = 0.75
        else:
            bsdf.inputs["Base Color"].default_value = (0.5, 0.5, 0.52, 1)
    _mat_cache[key] = mat
    return mat


def build(parts, flat=False):
    made = []
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
        uv[:, 1] = 1.0 - uv[:, 1]           # TRF stores 1-v (engine); back for Blender
        uvl.data.foreach_set("uv", uv.reshape(-1))
        try:
            nn = p["nrm"].astype(np.float64)
            ln = np.linalg.norm(nn, axis=1)
            ln[ln < 1e-9] = 1.0
            me.normals_split_custom_set_from_vertices((nn / ln[:, None]).tolist())
        except Exception:
            pass
        ob = bpy.data.objects.new(p["name"], me)
        bpy.context.collection.objects.link(ob)
        ob.data.materials.append(get_mat(p["mat"], flat))
        made.append(ob)
    return made


def scene_setup(objs, res):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 40
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.film_transparent = False
    w = bpy.data.worlds.new("W")
    sc.world = w
    w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.55, 0.62, 0.72, 1)
    bg.inputs[1].default_value = 1.0
    ld = bpy.data.lights.new("Sun", type="SUN")
    ld.energy = 3.2
    ld.angle = math.radians(6)
    lo = bpy.data.objects.new("Sun", ld)
    bpy.context.collection.objects.link(lo)
    lo.rotation_euler = (math.radians(52), 0, math.radians(38))
    mn = np.full(3, 1e18); mx = np.full(3, -1e18)
    for ob in objs:
        vs = np.array([v.co[:] for v in ob.data.vertices])
        if len(vs):
            mn = np.minimum(mn, vs.min(axis=0)); mx = np.maximum(mx, vs.max(axis=0))
    ctr = Vector(((mn[0]+mx[0])/2, (mn[1]+mx[1])/2, (mn[2]+mx[2])/2))
    size = float(np.linalg.norm(mx - mn))
    cam_d = bpy.data.cameras.new("Cam")
    cam_d.type = "ORTHO"
    cam_d.ortho_scale = size * 1.02
    cam = bpy.data.objects.new("Cam", cam_d)
    bpy.context.collection.objects.link(cam)
    d = Vector((1.0, -1.15, 0.60)).normalized()
    cam.location = ctr + d * size * 2.0
    cam.rotation_euler = (d * -1).to_track_quat("-Z", "Y").to_euler()
    sc.camera = cam
    return size


def render_to(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("[render] %s" % path)


def decimate(objs, ratio):
    b = a = 0
    for ob in objs:
        b += len(ob.data.polygons)
        bpy.ops.object.select_all(action="DESELECT")
        ob.select_set(True)
        bpy.context.view_layer.objects.active = ob
        m = ob.modifiers.new(name="lod", type="DECIMATE")
        m.decimate_type = "COLLAPSE"
        m.ratio = ratio
        bpy.ops.object.modifier_apply(modifier=m.name)
        a += len(ob.data.polygons)
    return b, a


def add_wires(objs, size):
    made = []
    for ob in objs:
        dup = ob.copy(); dup.data = ob.data.copy()
        bpy.context.collection.objects.link(dup)
        m = dup.modifiers.new(name="wf", type="WIREFRAME")
        m.thickness = size * 0.0016
        m.use_replace = True
        mat = bpy.data.materials.get("WIRE")
        if mat is None:
            mat = bpy.data.materials.new("WIRE")
            mat.use_nodes = True
            bs = mat.node_tree.nodes.get("Principled BSDF")
            bs.inputs["Base Color"].default_value = (0.0, 0.0, 0.0, 1)
            bs.inputs["Roughness"].default_value = 1.0
        dup.data.materials.clear()
        dup.data.materials.append(mat)
        made.append(dup)
    return made


# ------------------------------------------------------------------ main
RES = (2600, 1800)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    parts = [p for p in parse_trf(os.path.join(MODELS, NAME + ".trf")) if lod_of(p["name"]) == 0]
    print("[parse] %d LOD0 parts" % len(parts))
    print("[lod0] faces=%d verts=%d" % (sum(len(p["tri"]) for p in parts),
                                        sum(p["pidx"].size for p in parts)))

    # A: original
    clear()
    objs = build(parts)
    size = scene_setup(objs, RES)
    render_to(os.path.join(OUT, "A_lod0.png"))
    wires = add_wires(objs, size)
    for ob in objs:
        ob.hide_render = False
    render_to(os.path.join(OUT, "A_lod0_wire.png"))
    for w in wires:
        bpy.data.objects.remove(w, do_unlink=True)

    # B: pipeline weld (pos only) + decimate 0.5
    clear()
    b_parts = []
    for p in parts:
        P, N, U, T = weld_by_pos(p["pos"][p["pidx"]], p["nrm"], p["uv"], p["tri"])
        q = dict(p); q["pos"] = P; q["pidx"] = np.arange(len(P)); q["nrm"] = N; q["uv"] = U; q["tri"] = T
        b_parts.append(q)
    objs = build(b_parts)
    fb = sum(len(p["tri"]) for p in b_parts)
    f0, f1 = decimate(objs, 0.5)
    print("[B] after weld faces=%d ; after decimate=%d" % (fb, f1))
    size = scene_setup(objs, RES)
    render_to(os.path.join(OUT, "B_r050_posweld.png"))
    wires = add_wires(objs, size)
    render_to(os.path.join(OUT, "B_r050_posweld_wire.png"))
    for w in wires:
        bpy.data.objects.remove(w, do_unlink=True)

    # C: UV-safe weld + decimate 0.5
    clear()
    c_parts = []
    for p in parts:
        P, N, U, T = weld_uv_safe(p["pos"][p["pidx"]], p["nrm"], p["uv"], p["tri"])
        q = dict(p); q["pos"] = P; q["pidx"] = np.arange(len(P)); q["nrm"] = N; q["uv"] = U; q["tri"] = T
        c_parts.append(q)
    objs = build(c_parts)
    fc = sum(len(p["tri"]) for p in c_parts)
    _, f2 = decimate(objs, 0.5)
    print("[C] after uv-safe weld faces=%d ; after decimate=%d" % (fc, f2))
    scene_setup(objs, RES)
    render_to(os.path.join(OUT, "C_r050_uvsafe.png"))

    print("[done] %s" % OUT)


main()
