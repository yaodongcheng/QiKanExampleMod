# Render the head mesh (LOD0 skin + eye meshes) with a texture applied,
# orthographic front view. Used to (a) verify UV orientation and
# (b) preview final custom face results.
# usage: python render_head.py <texture.png> <out.png> <which_mesh: all|skin|eye> [obj] [axis: xy|xz] [skin_idx]
#   axis xy = 男头(x 左右,y 上下) ; xz = 女头(x 左右,z 上下, y=前后)
import re
import sys
import numpy as np
from PIL import Image

OBJ = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\meshes\head_male_a.obj"
tex_path = sys.argv[1]
out_path = sys.argv[2]
which = sys.argv[3] if len(sys.argv) > 3 else "all"
if len(sys.argv) > 4:
    OBJ = sys.argv[4]
AXIS = sys.argv[5] if len(sys.argv) > 5 else "xy"   # xy 男 / xz 女
SKIN_IDX = int(sys.argv[6]) if len(sys.argv) > 6 else 5

verts, uvs, faces = [], [], []   # faces: (mesh, [v0..v?], [t0..t?])
cur_mesh = -1
mesh_names = []
for line in open(OBJ, encoding="utf-8", errors="ignore"):
    if line.startswith("o "):
        cur_mesh += 1
        mesh_names.append(line[2:].strip())
    elif line.startswith("v "):
        m = re.match(r"v\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)", line)
        if m:
            verts.append((float(m.group(1)), float(m.group(2)), float(m.group(3))))
    elif line.startswith("vt"):
        m = re.match(r"vt\s+([-\d.eE+]+)\s+([-\d.eE+]+)", line)
        if m:
            uvs.append((float(m.group(1)), float(m.group(2))))
    elif line.startswith("f"):
        parts = line[2:].split()
        vi = [int(p.split("/")[0]) - 1 for p in parts]
        ti = [int(p.split("/")[1]) - 1 for p in parts]
        faces.append((cur_mesh, vi, ti))

tex = np.asarray(Image.open(tex_path).convert("RGB")).astype(np.float32)
H, W = tex.shape[:2]

verts = np.array(verts, dtype=np.float64)
uvs = np.array(uvs, dtype=np.float64)

# orthographic front view: +X right, +Y up (engine space). Screen: sx=x, sy=-y
Wp, Hp = 1024, 1024
img = np.zeros((Hp, Wp, 3), dtype=np.uint8)
img[:] = (10, 10, 14)

# bounds of mesh for scaling (axis-dependent)
allp = np.stack([verts[:, 0], verts[:, 2]], axis=1) if AXIS == "xz" else verts[:, :2]
sx_min, sx_max = allp[:, 0].min(), allp[:, 0].max()
sy_min, sy_max = allp[:, 1].min(), allp[:, 1].max()
sX = Wp * 0.9 / (sx_max - sx_min)
sY = Hp * 0.9 / (sy_max - sy_min)

def to_screen(p):
    sx = (p[0] - sx_min) * sX + Wp * 0.05
    sy = Hp * 0.95 - (p[2 if AXIS == "xz" else 1] - sy_min) * sY
    return sx, sy

def sample_uv(u, v):
    # xxFemaleHead 网格 vt: v=0 对应贴图顶行(实测: 额 v0.281->行575) -> py = v * H
    px = u * (W - 1)
    py = v * (H - 1)
    x0 = int(np.clip(px, 0, W - 2)); y0 = int(np.clip(py, 0, H - 2))
    fx = px - x0; fy = py - y0
    c = (tex[y0, x0] * (1 - fx) * (1 - fy) + tex[y0, x0 + 1] * fx * (1 - fy)
         + tex[y0 + 1, x0] * (1 - fx) * fy + tex[y0 + 1, x0 + 1] * fx * fy)
    return c

for mesh_idx, vi, ti in faces:
    name = mesh_names[mesh_idx]
    if which == "skin" and mesh_idx != SKIN_IDX:
        continue
    if which == "eye" and mesh_idx < (SKIN_IDX + 1):
        continue
    if mesh_idx < 5:  # skip LOD1-4 (男头布局); 女头贴图回显只有 0-3 低 LOD 头->不跳
        if AXIS == "xy":
            continue
    for i in range(0, len(vi) - 2, 3):
        a, b, c = vi[i], vi[i + 1], vi[i + 2]
        ta, tb, tc = ti[i], ti[i + 1], ti[i + 2]
        p0 = to_screen(verts[a]); p1 = to_screen(verts[b]); p2 = to_screen(verts[c])
        u0, v0 = uvs[ta]; u1, v1 = uvs[tb]; u2, v2 = uvs[tc]
        x0, y0 = p0; x1, y1 = p1; x2, y2 = p2
        minx = int(max(0, min(x0, x1, x2))); maxx = int(min(Wp - 1, max(x0, x1, x2)))
        miny = int(max(0, min(y0, y1, y2))); maxy = int(min(Hp - 1, max(y0, y1, y2)))
        if maxx < minx or maxy < miny:
            continue
        det = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
        if abs(det) < 1e-12:
            continue
        ys, xs = np.meshgrid(np.arange(miny, maxy + 1), np.arange(minx, maxx + 1), indexing="ij")
        x = xs.astype(np.float64); y = ys.astype(np.float64)
        l0 = ((y1 - y2) * (x - x2) + (x2 - x1) * (y - y2)) / det
        l1 = ((y2 - y0) * (x - x2) + (x0 - x2) * (y - y2)) / det
        l2 = 1.0 - l0 - l1
        mask = (l0 >= -1e-4) & (l1 >= -1e-4) & (l2 >= -1e-4) & (x >= 0) & (y >= 0) & (x <= Wp - 1) & (y <= Hp - 1)
        if not mask.any():
            continue
        xs2 = x[mask]; ys2 = y[mask]
        uu = (l0 * u0 + l1 * u1 + l2 * u2)[mask]
        vv = (l0 * v0 + l1 * v1 + l2 * v2)[mask]
        # iterate unique-ish pixels via flat sample per pixel (fast enough at this size)
        # sample per-pixel color (vectorized via index arrays, one by one loop is slow;
        # use bilinear per pixel with numpy gather)
        px = uu * (W - 1)
        py = vv * (H - 1)
        xs_f = np.clip(px, 0, W - 2); ys_f = np.clip(py, 0, H - 2)
        x0i = xs_f.astype(int); y0i = ys_f.astype(int)
        fx = (xs_f - x0i)[:, None]; fy = (ys_f - y0i)[:, None]
        c = (tex[y0i, x0i] * (1 - fx) * (1 - fy) + tex[y0i, x0i + 1] * fx * (1 - fy)
             + tex[y0i + 1, x0i] * (1 - fx) * fy + tex[y0i + 1, x0i + 1] * fx * fy)
        img[ys2.astype(int), xs2.astype(int)] = c

Image.fromarray(img).save(out_path)
print("saved", out_path)
