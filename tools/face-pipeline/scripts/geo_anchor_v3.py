# geo_anchor v3 — 精确定位瞳孔/鼻尖/嘴心的 3D 区域 -> uv 锚点
# 瞳孔: 人眼瞳孔横坐标约 ±0.33 头宽（头宽 x±0.093），y 眼窝带内, z 前脸
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
y, z, x = P[:, 1], P[:, 2], P[:, 0]

HEAD_W = 0.093  # half width of head from earlier bbox
pupil_x = 0.33 * HEAD_W

def probe(name, mask):
    n = mask.sum()
    if n < 2:
        print(f"{name}: n<2"); return
    uu, vv = U[mask][:, 0], U[mask][:, 1]
    print(f"{name}: n={n}  uv_center=({uu.mean():.4f},{vv.mean():.4f})  u[{uu.min():.4f},{uu.max():.4f}] v[{vv.min():.4f},{vv.max():.4f}]")

front = z > np.percentile(z, 90)
pupL = front & (np.abs(x + pupil_x) < 0.012) & (y > 0.045) & (y < 0.085)
pupR = front & (np.abs(x - pupil_x) < 0.012) & (y > 0.045) & (y < 0.085)
nose = front & (np.abs(x) < 0.012) & (y > 0.030) & (y < 0.055)
mouth = front & (np.abs(x) < 0.030) & (y > -0.005) & (y < 0.030)
chin = front & (np.abs(x) < 0.030) & (y > -0.030) & (y < -0.005)
brow  = front & (np.abs(x) < 0.055) & (y > 0.080) & (y < 0.105)

probe("PUPIL_L", pupL)
probe("PUPIL_R", pupR)
probe("NOSE_V2", nose)
probe("MOUTH_V2", mouth)
probe("CHIN_V2", chin)
probe("BROW_V2", brow)
