# bake_face_overlays.py —— 把睫毛 / 眼影 / 泪线「画」进脸贴图
#
# 为什么这么做
# ------------
# 骑砍**男头只有 3 个子网格**（脸 → 眼 → 嘴），没有睫毛/眼影/泪线的槽位。
# 多出来的件会被引擎回落到脸皮材质 → 眼睛和嘴一起糊（换头工程 §13.7 ①）。
# 所以这三件不能作为独立子网格存在 —— 要么丢掉，要么**把它们的外观烘进脸贴图**。
#
# 做法（不靠渲染器，纯几何投影）
# ------------------------------
#   ① 把三件各自的三角形在 3D 上密集采样
#   ② 每个采样点：用它自己的 UV 去采自己的贴图（含 alpha）
#   ③ 把该 3D 点**投影到脸壳表面**（BVH 最近点），取落点的**脸壳 UV**（重心插值）
#   ④ 按 alpha 把颜色画进「脸壳 UV 空间」的一张覆盖层
#   ⑤ 覆盖层按 alpha 合成到脸贴图上
#
# 为什么是"投影到脸壳表面"而不是"直接用三件自己的 UV"：三件的 UV 指向**各自的贴图**，
# 跟脸贴图不是一个布局。只有落到脸壳表面的那个位置，才知道它在脸贴图上该画在哪。
#
# 用法（Blender）:
#   blender -b --python bake_face_overlays.py -- \
#       --src <NPC_Henry.fbx> --face m_head_henry \
#       --overlays "m_head_henry_Eyelashes=eyelash:m_head_henry_Eyeshadows=eye_overlay:m_head_henry_Tearline=eye_water" \
#       --maps <源贴图目录> --base <脸 _d.png> --out <输出 _d.png> [--size 2048] [--brush 3] [--dens 7]
import bpy
import sys
import os
import inspect
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from PIL import Image

Image.MAX_IMAGE_PIXELS = None


def get(a, k, d=None):
    return a[a.index(k) + 1] if k in a else d


def patch_importer():
    import io_scene_fbx.import_fbx as mod
    src = inspect.getsource(mod)
    bad = "                    (mmat, amat) = mesh.armature_setup[self]"
    if bad in src:
        src = src.replace(bad, (
            "                    if self not in mesh.armature_setup:\n"
            "                        mesh.armature_setup[self] = (mesh.bind_matrix, self.bind_matrix)\n"
            + bad))
        exec(compile(src, mod.__file__, "exec"), mod.__dict__)


A = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC = get(A, "--src")
FACE = get(A, "--face", "m_head_henry")
OVL = get(A, "--overlays", "")
MAPS = os.path.abspath(get(A, "--maps"))
BASE = os.path.abspath(get(A, "--base"))
OUT = os.path.abspath(get(A, "--out"))
SIZE = int(get(A, "--size", "2048"))
BRUSH = int(get(A, "--brush", "3"))       # 笔刷半径（像素）—— 让睫毛线连续
DENS = int(get(A, "--dens", "7"))         # 每个三角形每边的采样数
if not (SRC and OVL and BASE and OUT):
    sys.exit("需要 --src --overlays --maps --base --out")

specs = []
for tok in OVL.split(":"):
    if not tok.strip():
        continue
    mesh, tex = tok.split("=")
    specs.append((mesh.strip(), tex.strip()))

patch_importer()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC, use_anim=False, ignore_leaf_bones=True)


def find_mesh(name):
    for o in bpy.data.objects:
        if o.type == "MESH" and o.name == name:
            return o
    for o in bpy.data.objects:      # 精确名没命中时退回"以它开头且不是那几件附件"
        if o.type == "MESH" and o.name.startswith(name):
            return o
    return None


face_ob = find_mesh(FACE)
if face_ob is None:
    sys.exit("!! 找不到脸壳 %r" % FACE)
mw = face_ob.matrix_world
fverts = [mw @ v.co for v in face_ob.data.vertices]
fpolys = [list(p.vertices) for p in face_ob.data.polygons]
fuvl = face_ob.data.uv_layers.active
floop_uv = [tuple(d.uv) for d in fuvl.data]
floops = [list(p.loop_indices) for p in face_ob.data.polygons]
print("[BAKE] 脸壳 %s：%d 顶点 %d 面" % (face_ob.name, len(fverts), len(fpolys)))

bvh = BVHTree.FromPolygons([tuple(v) for v in fverts], fpolys, all_triangles=False)
print("[BAKE] BVH 建好")

W = SIZE
acc_rgb = np.zeros((W, W, 3), np.float32)
acc_a = np.zeros((W, W), np.float32)


def sample_tex(img, u, v):
    """UV → RGBA（v 轴翻转：UV v=0 在下、图像 row 0 在上）"""
    x = int(np.clip(u % 1.0, 0, 0.999999) * img.width)
    y = int(np.clip(1.0 - (v % 1.0), 0, 0.999999) * img.height)
    return img.getpixel((x, y))


def bary_uv(p, tri_pts, tri_uvs):
    """p 在三角形内的重心坐标 → 插值 UV（退化时退回最近顶点）"""
    a, b, c = tri_pts
    v0, v1, v2 = b - a, c - a, p - a
    d00, d01, d11 = v0.dot(v0), v0.dot(v1), v1.dot(v1)
    d20, d21 = v2.dot(v0), v2.dot(v1)
    den = d00 * d11 - d01 * d01
    if abs(den) < 1e-12:
        i = min(range(3), key=lambda k: (tri_pts[k] - p).length)
        return tri_uvs[i]
    w1 = (d11 * d20 - d01 * d21) / den
    w2 = (d00 * d21 - d01 * d20) / den
    w0 = 1.0 - w1 - w2
    u = w0 * tri_uvs[0][0] + w1 * tri_uvs[1][0] + w2 * tri_uvs[2][0]
    v = w0 * tri_uvs[0][1] + w1 * tri_uvs[1][1] + w2 * tri_uvs[2][1]
    return (u, v)


for mesh_name, tex in specs:
    ob = find_mesh(mesh_name)
    if ob is None:
        print("   !! 找不到 %r，跳过" % mesh_name)
        continue
    tex_path = os.path.join(MAPS, tex + "_COLOR.png")
    if not os.path.isfile(tex_path):
        print("   !! 缺贴图 %s，跳过" % tex_path)
        continue
    img = Image.open(tex_path).convert("RGBA")
    mw2 = ob.matrix_world
    vs = [mw2 @ v.co for v in ob.data.vertices]
    uv = [tuple(d.uv) for d in ob.data.uv_layers.active.data]
    lo = [list(p.loop_indices) for p in ob.data.polygons]
    hit = 0
    for poly, ls in zip(ob.data.polygons, lo):
        vi = list(poly.vertices)
        tp = [vs[i] for i in vi]
        tu = [uv[l] for l in ls]
        # 三角形扇形细分采样（多边形按 0/i/i+1 拆）
        for k in range(1, len(tp) - 1):
            tri_p = [tp[0], tp[k], tp[k + 1]]
            tri_u = [tu[0], tu[k], tu[k + 1]]
            for i in range(DENS + 1):
                for j in range(DENS + 1 - i):
                    w0 = i / float(DENS)
                    w1 = j / float(DENS)
                    w2 = 1.0 - w0 - w1
                    p = tri_p[0] * w0 + tri_p[1] * w1 + tri_p[2] * w2
                    su = (tri_u[0][0] * w0 + tri_u[1][0] * w1 + tri_u[2][0] * w2,
                          tri_u[0][1] * w0 + tri_u[1][1] * w1 + tri_u[2][1] * w2)
                    r, g, b, al = sample_tex(img, su[0], su[1])
                    if al < 8:
                        continue
                    loc, nrm, idx, dist = bvh.find_nearest(p)
                    if loc is None:
                        continue
                    fuv = bary_uv(loc, [fverts[i] for i in fpolys[idx]],
                                  [floop_uv[l] for l in floops[idx]])
                    px = int(fuv[0] * W)
                    py = int((1.0 - fuv[1]) * W)
                    for dy in range(-BRUSH, BRUSH + 1):
                        for dx in range(-BRUSH, BRUSH + 1):
                            if dx * dx + dy * dy > BRUSH * BRUSH:
                                continue
                            X, Y = px + dx, py + dy
                            if not (0 <= X < W and 0 <= Y < W):
                                continue
                            a = al / 255.0
                            if a > acc_a[Y, X]:
                                acc_a[Y, X] = a
                                acc_rgb[Y, X] = (r, g, b)
                    hit += 1
    print("   %-32s <- %-16s 采样命中 %d" % (mesh_name, tex, hit))

base = Image.open(BASE).convert("RGB")
if base.size != (W, W):
    base = base.resize((W, W), Image.LANCZOS)
b = np.asarray(base).astype(np.float32)
a = acc_a[:, :, None]
out = b * (1.0 - a) + acc_rgb * a
img_out = Image.fromarray(np.clip(out, 0, 255).astype(np.uint8))
img_out.save(OUT)
painted = int((acc_a > 0).sum())
print("[BAKE] 覆盖 %d 像素（%.2f%%）-> %s" % (painted, 100.0 * painted / (W * W), OUT))
print("DONE")
sys.stdout.flush()
