# Derive facial landmark UV anchors from head geometry:
# 1) load mesh 5 (head_male_a LOD0 skin) from the OBJ
# 2) classify 3D regions: eyes, nose, mouth (by z depth & y height & x symmetry)
# 3) report the UV bbox (in uv space) of each region
# This gives us exact target spots on the male head UV layout for warping.
import re
import numpy as np

OBJ = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\meshes\head_male_a.obj"

verts, uvs = [], []
cur_mesh = -1
mesh_verts = {}   # per-mesh vert index ranges
mesh_uvs = {}
cur_range = [0, 0]
names = []
for line in open(OBJ, encoding="utf-8", errors="ignore"):
    if line.startswith("o "):
        if cur_mesh >= 0:
            mesh_verts[cur_mesh] = cur_range
        cur_mesh += 1
        names.append(line[2:].strip())
        cur_range = [len(verts), len(verts)]
        mesh_uvs[cur_mesh] = [len(uvs), len(uvs)]
    elif line.startswith("v "):
        m = re.match(r"v\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)", line)
        verts.append((float(m.group(1)), float(m.group(2)), float(m.group(3))))
        cur_range[1] = len(verts)
    elif line.startswith("vt"):
        m = re.match(r"vt\s+([-\d.eE+]+)\s+([-\d.eE+]+)", line)
        uvs.append((float(m.group(1)), float(m.group(2))))
mesh_verts[cur_mesh] = cur_range
mesh_uvs[cur_mesh] = [len(uvs), len(uvs)]

# mesh 5 = 'head_male_a' (LOD0 skin). indices into verts.
i0, i1 = mesh_verts[5]
P = np.array(verts[i0:i1])
U = np.array(uvs[i0:i1])

print("skin verts:", len(P))
print("x range", P[:, 0].min(), P[:, 0].max())
print("y range", P[:, 1].min(), P[:, 1].max())
print("z range", P[:, 2].min(), P[:, 2].max())

# Front of face = higher z. Take top-z chunk.
zcut = np.percentile(P[:, 2], 92)
w = P[:, 2] > zcut
print("front-face verts:", w.sum())

# normalize head: y between min..max
ymin, ymax = P[:, 1].min(), P[:, 1].max()
y = (P[:, 1] - ymin) / (ymax - ymin)

# eyes: front verts at y~0.62-0.72, x split left/right
def region(mask, label):
    if mask.sum() < 3:
        print(label, "too few", mask.sum())
        return
    us, vs = U[mask][:, 0], U[mask][:, 1]
    print(f"{label}: n={mask.sum()} u[{us.min():.4f},{us.max():.4f}] v[{vs.min():.4f},{vs.max():.4f}] center=({us.mean():.4f},{vs.mean():.4f})")

front = w & (y > 0.5)
left_eye = front & (P[:, 0] < -0.02) & (y > 0.60) & (y < 0.74) & (P[:, 2] > np.percentile(P[:, 2], 95))
right_eye = front & (P[:, 0] > 0.02) & (y > 0.60) & (y < 0.74) & (P[:, 2] > np.percentile(P[:, 2], 95))
nose = front & (np.abs(P[:, 0]) < 0.015) & (y > 0.48) & (y < 0.62) & (P[:, 2] > np.percentile(P[:, 2], 96))
mouth = front & (np.abs(P[:, 0]) < 0.04) & (y > 0.36) & (y < 0.50) & (P[:, 2] > np.percentile(P[:, 2], 93))
eyebrows = front & (y > 0.72) & (y < 0.80) & (P[:, 2] > np.percentile(P[:, 2], 95))

region(left_eye, "LEFT_EYE")
region(right_eye, "RIGHT_EYE")
region(nose, "NOSE")
region(mouth, "MOUTH")
region(eyebrows, "EYEBROWS")

# also dump the whole front-face bbox
region(front & (y > 0.55) & (y < 0.85), "MIDFACE")
region(front & (y > 0.30) & (y < 0.55), "LOWFACE")

# vertices with their y & uv directly aligned — save a CSV for later use
out = r"C:\Users\yaodongcheng\AppData\Local\Temp\head_export\frontal_uv.csv"
with open(out, "w", encoding="utf-8") as f:
    f.write("x,y,z,u,v,ynorm\n")
    for i in np.where(front)[0]:
        f.write(f"{P[i,0]:.6f},{P[i,1]:.6f},{P[i,2]:.6f},{U[i,0]:.6f},{U[i,1]:.6f},{y[i]:.4f}\n")
print("saved", out)
