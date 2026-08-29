# Render head mesh (LOD0 skin) visualized by UV coordinates:
#   R channel = u, G channel = v  (scaled to 255)
# The front-view render thus BEARS the answer: each facial feature's UV
# coords are stored as colors at its rendered location.
# usage: python render_uvview.py [out.png] [face_only=1]
import re
import sys
import numpy as np
from PIL import Image

OBJ = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\meshes\head_male_a.obj"
out_path = sys.argv[1] if len(sys.argv) > 1 else r"C:\Users\yaodongcheng\AppData\Local\Temp\head_export\uvview.png"
face_only = len(sys.argv) > 2 and sys.argv[2] == "face"

verts, uvs, faces, mesh_names, cur_mesh = [], [], [], [], -1
for line in open(OBJ, encoding="utf-8", errors="ignore"):
    if line.startswith("o "):
        cur_mesh += 1
        mesh_names.append(line[2:].strip())
    elif line.startswith("v "):
        m = re.match(r"v\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)", line)
        verts.append((float(m.group(1)), float(m.group(2)), float(m.group(3))))
    elif line.startswith("vt"):
        m = re.match(r"vt\s+([-\d.eE+]+)\s+([-\d.eE+]+)", line)
        uvs.append((float(m.group(1)), float(m.group(2))))
    elif line.startswith("f"):
        parts = line[2:].split()
        vi = [int(p.split("/")[0]) - 1 for p in parts]
        ti = [int(p.split("/")[1]) - 1 for p in parts]
        faces.append((cur_mesh, vi, ti))

verts = np.array(verts); uvs = np.array(uvs)
Wp = Hp = 1024
img = np.zeros((Hp, Wp, 3), dtype=np.uint8)
img[:] = (60, 60, 70)

def to_screen(p):
    sx = (p[0] + 0.11) / 0.22 * Wp
    sy = Hp * 0.92 - (p[1] + 0.10) / 0.30 * Hp
    return sx, sy

for mesh_idx, vi, ti in faces:
    if mesh_idx < 5:
        continue
    if face_only and mesh_idx != 5:
        continue
    for i in range(0, len(vi) - 2, 3):
        a, b, c = vi[i], vi[i + 1], vi[i + 2]
        ta, tb, tc = ti[i], ti[i + 1], ti[i + 2]
        p0 = to_screen(verts[a]); p1 = to_screen(verts[b]); p2 = to_screen(verts[c])
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
        mask = (l0 >= -1e-4) & (l1 >= -1e-4) & (l2 >= -1e-4)
        uu = (l0 * uvs[ta][0] + l1 * uvs[tb][0] + l2 * uvs[tc][0])[mask]
        vv = (l0 * uvs[ta][1] + l1 * uvs[tb][1] + l2 * uvs[tc][1])[mask]
        color = np.clip(np.stack([uu, vv, np.full_like(uu, 0.0)], axis=1) * 255, 0, 255).astype(np.uint8)
        img[ys[mask].astype(int), xs[mask].astype(int)] = color

Image.fromarray(img).save(out_path)
print("saved", out_path)

# crop helper: print color at given xy to read uv
print("to read uv at a point: (u,v) = color/255 (R,G channels)")
