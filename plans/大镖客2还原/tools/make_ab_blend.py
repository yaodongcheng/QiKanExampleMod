# -*- coding: utf-8 -*-
"""Build an A/B .blend: original LOD0 and decimated version as two OVERLAPPING
objects, so visibility can be toggled with the eye icon in the outliner.

  blender -b --factory-startup -P make_ab_blend.py -- <name> <out.blend>
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
OUTBLEND = argv[1] if len(argv) > 1 else os.path.join(
    RDR2, "out", "lod_preview", "银行_原版vs减面对比.blend")

# ---- per-part budget (faces -> ratio). Small ornamental parts are left intact.
BIG, BIG_R = 2000, 0.30
MID, MID_R = 600, 0.65


def parse_trf(path):
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


def lod_of(p):
    m = re.search(r"\.lod(\d+)\.", p)
    return int(m.group(1)) if m else 0


def resolve(mn):
    f = os.path.join(ALB, mn + ".png")
    if os.path.isfile(f):
        return f
    best = None
    for fn in os.listdir(ALB):
        if fn.lower().endswith(".png"):
            st = fn[:-4]
            if mn.startswith(st) and (best is None or len(st) > len(best[0])):
                best = (st, fn)
    return os.path.join(ALB, best[1]) if best else None


_img, _mat = {}, {}


def get_mat(mn):
    if mn in _mat:
        return _mat[mn]
    m = bpy.data.materials.new(mn[:50])
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes.get("Principled BSDF")
    tp = resolve(mn)
    if tp:
        if tp not in _img:
            _img[tp] = bpy.data.images.load(tp, check_existing=True)
        nd = nt.nodes.new("ShaderNodeTexImage")
        nd.image = _img[tp]
        nt.links.new(nd.outputs["Color"], b.inputs["Base Color"])
        b.inputs["Roughness"].default_value = 0.78
    _mat[mn] = m
    return m


def weld_uv_safe(pos, nrm, uv, tri):
    key = np.concatenate([np.round(pos, 5), np.round(uv, 5), np.round(nrm, 4)], axis=1)
    _, first, inv = np.unique(key, axis=0, return_index=True, return_inverse=True)
    out = inv[tri]
    keep = (out[:, 0] != out[:, 1]) & (out[:, 1] != out[:, 2]) & (out[:, 0] != out[:, 2])
    return pos[first], nrm[first], uv[first], out[keep]


def make_object(p, suffix):
    v = p["pos"][p["pidx"]]
    me = bpy.data.meshes.new(p["name"] + suffix)
    me.from_pydata(v.tolist(), [], p["tri"].tolist())
    me.update()
    uvl = me.uv_layers.new(name="UVMap")
    lv = np.empty(len(me.loops), dtype=np.int32)
    me.loops.foreach_get("vertex_index", lv)
    uv = p["uv"][lv].astype(np.float32)
    uv[:, 1] = 1.0 - uv[:, 1]        # TRF stores 1-v
    uvl.data.foreach_set("uv", uv.reshape(-1))
    try:
        nn = p["nrm"].astype(np.float64)
        ln = np.linalg.norm(nn, axis=1); ln[ln < 1e-9] = 1.0
        me.normals_split_custom_set_from_vertices((nn/ln[:, None]).tolist())
    except Exception as e:
        print("[warn] normals %r" % (e,))
    ob = bpy.data.objects.new(p["name"] + suffix, me)
    bpy.context.collection.objects.link(ob)
    ob.data.materials.append(get_mat(p["mat"]))
    return ob


def join_all(objs, final_name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = final_name
    ob.data.name = final_name
    ob.location = (0.0, 0.0, 0.0)
    return ob


def decimate(ob, ratio):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    m = ob.modifiers.new("d", type="DECIMATE")
    m.decimate_type = "COLLAPSE"
    m.ratio = ratio
    bpy.ops.object.modifier_apply(modifier=m.name)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene

    parts = [p for p in parse_trf(os.path.join(MODELS, NAME + ".trf")) if lod_of(p["name"]) == 0]
    print("[parse] %d LOD0 parts" % len(parts))

    # ---------- A: original
    a_objs = [make_object(p, "_A") for p in parts]
    face_a = sum(len(p["tri"]) for p in parts)
    oa = join_all(a_objs, "原版 LOD0 (%d 面)" % face_a)
    print("[A] %d faces" % face_a)

    # ---------- B: uv-safe weld + per-part decimate
    b_objs = []
    for p in parts:
        P, N, U, T = weld_uv_safe(p["pos"][p["pidx"]], p["nrm"], p["uv"], p["tri"])
        q = dict(p); q["pos"] = P; q["pidx"] = np.arange(len(P))
        q["nrm"] = N; q["uv"] = U; q["tri"] = T
        ob = make_object(q, "_B")
        f = len(T)
        r = BIG_R if f >= BIG else (MID_R if f >= MID else 1.0)
        if r < 1.0:
            decimate(ob, r)
        b_objs.append(ob)
    face_b = sum(len(o.data.polygons) for o in b_objs)
    ob_b = join_all(b_objs, "减面 按部件 (%d 面 / 原样 %d%%)" % (face_b, round(100.0*face_b/face_a)))
    print("[B] %d faces (%.1f%%)" % (face_b, 100.0*face_b/face_a))

    # both exactly at origin -> perfectly overlapping
    oa.location = (0, 0, 0)
    ob_b.location = (0, 0, 0)

    # default: original visible, reduced hidden  (toggle with the eye)
    oa.hide_set(False)
    ob_b.hide_set(True)
    oa.hide_viewport = False
    ob_b.hide_viewport = False          # keep it togglable in the outliner
    ob_b.hide_render = True

    # ---------- camera / light / world
    mn = np.full(3, 1e18); mx = np.full(3, -1e18)
    for ob in (oa,):
        vs = np.array([v.co[:] for v in ob.data.vertices])
        mn = np.minimum(mn, vs.min(axis=0)); mx = np.maximum(mx, vs.max(axis=0))
    ctr = Vector(((mn[0]+mx[0])/2, (mn[1]+mx[1])/2, (mn[2]+mx[2])/2))
    size = float(np.linalg.norm(mx-mn))

    w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.55, 0.62, 0.72, 1)
    bg.inputs[1].default_value = 1.0

    ld = bpy.data.lights.new("Sun", type="SUN"); ld.energy = 3.2; ld.angle = math.radians(6)
    lo = bpy.data.objects.new("Sun", ld); bpy.context.collection.objects.link(lo)
    lo.rotation_euler = (math.radians(52), 0, math.radians(38))

    cd = bpy.data.cameras.new("Cam"); cd.type = "ORTHO"; cd.ortho_scale = size * 1.02
    cam = bpy.data.objects.new("Cam", cd); bpy.context.collection.objects.link(cam)
    d = Vector((1.0, -1.15, 0.60)).normalized()
    cam.location = ctr + d * size * 2.0
    cam.rotation_euler = (d * -1).to_track_quat("-Z", "Y").to_euler()
    sc.camera = cam
    sc.render.resolution_x, sc.render.resolution_y = 1600, 1100

    # pack textures -> self-contained blend
    try:
        bpy.ops.file.pack_all()
        print("[pack] textures packed")
    except Exception as e:
        print("[pack] skipped: %r" % (e,))

    os.makedirs(os.path.dirname(OUTBLEND), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=OUTBLEND)
    print("[saved] %s (%.1f MB)" % (OUTBLEND, os.path.getsize(OUTBLEND)/1024/1024))


main()
