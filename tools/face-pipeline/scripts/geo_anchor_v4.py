# geo_anchor v4 — 面片中心采样: 按三角形 3D 中心分类五官区域, 输出 uv 锚点
import re
import numpy as np

OBJ = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\meshes\head_male_a.obj"

verts, uvs, names, cur_mesh = [], [], [], -1
faces = {i: [] for i in range(10)}
for line in open(OBJ, encoding="utf-8", errors="ignore"):
    if line.startswith("o "):
        cur_mesh += 1
        names.append(line[2:].strip())
    elif line.startswith("v "):
        m = re.match(r"v\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)", line)
        verts.append(tuple(map(float, m.groups())))
    elif line.startswith("vt"):
        m = re.match(r"vt\s+([-\d.eE+]+)\s+([-\d.eE+]+)", line)
        uvs.append(tuple(map(float, m.groups())))
    elif line.startswith("f"):
        parts = line[2:].split()
        vi = [int(p.split("/")[0]) - 1 for p in parts]
        ti = [int(p.split("/")[1]) - 1 for p in parts]
        faces[cur_mesh].append((vi, ti))

P = np.array(verts); U = np.array(uvs)
V = P; Uv = U
zmax = V[:, 2].max()
HEAD_W = 0.093
pupil_x = 0.33 * HEAD_W

def probe(name, pred):
    hits = []
    for vi, ti in faces.get(5, []):
        for a, b, c in zip(vi[::3], vi[1::3], vi[2::3]):
            p = (P[a] + P[b] + P[c]) / 3.0
            t = (U[ti[0]] + U[ti[1]] + U[ti[2]]) / 3.0
            if pred(p):
                hits.append((p, t))
    if not hits:
        print(f"{name}: n=0"); return
    pts = np.array([h[0] for h in hits]); tas = np.array([h[1] for h in hits])
    print(f"{name}: n={len(hits)} uv_center=({tas[:,0].mean():.4f},{tas[:,1].mean():.4f}) "
          f"u[{tas[:,0].min():.4f},{tas[:,0].max():.4f}] v[{tas[:,1].min():.4f},{tas[:,1].max():.4f}]")
    return tas

def front(p):
    return p[2] > zmax - 0.06

probe("PUPIL_L", lambda p: front(p) and abs(p[0] + pupil_x) < 0.011 and 0.050 < p[1] < 0.082)
probe("PUPIL_R", lambda p: front(p) and abs(p[0] - pupil_x) < 0.011 and 0.050 < p[1] < 0.082)
probe("EYE_STRIP", lambda p: front(p) and 0.040 < p[1] < 0.085 and abs(p[0]) > 0.015)
probe("NOSE_TIP", lambda p: front(p) and abs(p[0]) < 0.015 and 0.028 < p[1] < 0.052)
probe("MOUTH_CENTER", lambda p: front(p) and abs(p[0]) < 0.035 and -0.005 < p[1] < 0.030)
probe("MOUTH_R", lambda p: front(p) and 0.015 < p[0] < 0.045 and 0.000 < p[1] < 0.028)
probe("MOUTH_L", lambda p: front(p) and -0.045 < p[0] < -0.015 and 0.000 < p[1] < 0.028)
probe("CHIN_TIP", lambda p: front(p) and abs(p[0]) < 0.030 and -0.028 < p[1] < -0.002)
probe("BROW_CENTER", lambda p: front(p) and abs(p[0]) < 0.050 and 0.082 < p[1] < 0.108)
