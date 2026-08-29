# Parse tpaccli OBJ dump of head_male_a and rasterize UV triangles into a
# layout map, one color per submesh, 2048x2048. Writes uv_layout.png.
import re
import numpy as np
from PIL import Image, ImageDraw

OBJ = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\meshes\head_male_a.obj"
OUT = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\uv_layout.png"

verts = []   # (x,y,z)
uvs = []     # (u,v)
faces = []   # (mesh_idx, [v0,v1,v2],[t0,t1,t2])
cur_mesh = -1
mesh_names = []

COLORS = [
    (255, 60, 60), (60, 255, 60), (60, 120, 255), (255, 210, 60),
    (220, 90, 255), (90, 255, 240), (255, 150, 90), (180, 180, 180),
    (255, 90, 160), (130, 255, 120),
]

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

print(f"meshes={len(mesh_names)} verts={len(verts)} uvs={len(uvs)} faces={len(faces)}")

W = 2048
img = Image.new("RGB", (W, W), (24, 24, 28))
d = ImageDraw.Draw(img)
arr = np.zeros((W, W, 3), dtype=np.uint8)
arr[:] = (24, 24, 28)

def raster(uvtri, color):
    pts = np.array([[t[0] * (W - 1), t[1] * (W - 1)] for t in uvtri])
    (x0, y0), (x1, y1), (x2, y2) = pts
    minx, maxx = int(max(0, min(x0, x1, x2))), int(min(W - 1, max(x0, x1, x2)))
    miny, maxy = int(max(0, min(y0, y1, y2))), int(min(W - 1, max(y0, y1, y2)))
    if maxx < minx or maxy < miny:
        return
    xs, ys = np.meshgrid(np.arange(minx, maxx + 1), np.arange(miny, maxy + 1))
    x = xs.astype(np.float64); y = ys.astype(np.float64)
    det = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2)
    if abs(det) < 1e-12:
        return
    l0 = ((y1 - y2) * (x - x2) + (x2 - x1) * (y - y2)) / det
    l1 = ((y2 - y0) * (x - x2) + (x0 - x2) * (y - y2)) / det
    l2 = 1 - l0 - l1
    mask = (l0 >= -1e-6) & (l1 >= -1e-6) & (l2 >= -1e-6)
    arr[ys[mask], xs[mask]] = color

for mesh_idx, vi, ti in faces:
    col = COLORS[mesh_idx % len(COLORS)]
    for a, b, c in zip(vi[::3], vi[1::3], vi[2::3]):
        if a < len(uvs) and b < len(uvs) and c < len(uvs):
            raster([uvs[a], uvs[b], uvs[c]], col)

img = Image.fromarray(arr)
img.save(OUT)
print("saved", OUT)

# report per-mesh uv bounding box (of uv-space)
import collections
bbox = collections.defaultdict(lambda: [1e9, 1e9, -1e9, -1e9])
seen_uv = collections.defaultdict(set)
for mesh_idx, vi, ti in faces:
    for t in ti:
        u, v = uvs[t]
        seen_uv[mesh_idx].add((u, v))
for m, pts in seen_uv.items():
    us = [p[0] for p in pts]; vs = [p[1] for p in pts]
    bbox[m] = [min(us), min(vs), max(us), max(vs)]

for m in range(len(mesh_names)):
    if m in bbox:
        b = bbox[m]
        print(f"mesh {m} {mesh_names[m]!r}: uv bbox x[{b[0]:.4f},{b[2]:.4f}] y[{b[1]:.4f},{b[3]:.4f}] nUvPts={len(seen_uv[m])}")
