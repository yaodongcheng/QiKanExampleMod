# v2: classify skin-mesh verts into facial regions by 3D position,
# then average UV per region -> anatomical UV anchors.
# Output: printed anchor table + anchored_points.csv
import re
import numpy as np

OBJ = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\meshes\head_male_a.obj"

verts, uvs, cur_mesh, mesh_range, cur_range = [], [], -1, {}, None
for line in open(OBJ, encoding="utf-8", errors="ignore"):
    if line.startswith("o "):
        if cur_mesh >= 0:
            mesh_range[cur_mesh] = (cur_range[0], cur_range[1])
        cur_mesh += 1
        cur_range = [len(verts), len(verts)]
    elif line.startswith("v "):
        m = re.match(r"v\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)", line)
        verts.append((float(m.group(1)), float(m.group(2)), float(m.group(3))))
        cur_range[1] = len(verts)
    elif line.startswith("vt"):
        m = re.match(r"vt\s+([-\d.eE+]+)\s+([-\d.eE+]+)", line)
        uvs.append((float(m.group(1)), float(m.group(2))))
mesh_range[cur_mesh] = (cur_range[0], cur_range[1])

i0, i1 = mesh_range[5]
P = np.array(verts[i0:i1]); U = np.array(uvs[i0:i1])
y = P[:, 1]; z = P[:, 2]; x = P[:, 0]

zs = np.percentile(z, [90, 95, 98])
print("z percentiles 90/95/98:", zs)

# head forward = +z. Eyes: two lump at ±x, y center band, front-most.
# We probe bands and print counts so thresholds can be tuned here.
def probe(name, mask):
    n = mask.sum()
    if n < 2:
        print(f"{name}: n<2")
        return None
    uu, vv = U[mask][:, 0], U[mask][:, 1]
    print(f"{name}: n={n} y[{y[mask].min():.3f},{y[mask].max():.3f}]  uv({uu.mean():.4f},{vv.mean():.4f}) span u[{uu.min():.4f},{uu.max():.4f}] v[{vv.min():.4f},{vv.max():.4f}]")
    return mask

# globals from previous render: face spans roughly y -0.07..0.17, front z top around 1.81
front = z > zs[0]          # front-most band picks front face
print("front band count:", front.sum())

probe("EYE_L_band", front & (y > 0.055) & (y < 0.085) & (x < -0.015))
probe("EYE_R_band", front & (y > 0.055) & (y < 0.085) & (x > 0.015))
probe("BROW_band", front & (y > 0.085) & (y < 0.11) & (np.abs(x) > 0.01))
probe("NOSE_tip", front & (np.abs(x) < 0.02) & (y > 0.03) & (y < 0.062))
probe("MOUTH_band", front & (np.abs(x) < 0.045) & (y > -0.005) & (y < 0.035))
probe("CHIN_band", front & (np.abs(x) < 0.045) & (y < -0.008))
probe("FOREHEAD", front & (y > 0.115))

# Deliberate wider bands in case eye regions are sparse:
probe("CENTRAL_FACE", front & (y > -0.02) & (y < 0.10))
