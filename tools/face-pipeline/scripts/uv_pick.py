# uv_pick.py — 确定性 UV 测量: 3D 解剖点 -> 渲染屏幕坐标 -> UV 着色图采色 (= uv)
# 渲染参数与 render_uvview.py 完全一致（同投影 -> 同位置）
import re
import numpy as np
from PIL import Image

OBJ = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\meshes\head_male_a.obj"
UVVIEW = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\face-pipeline\data\uvview.png"

verts = []
cur_mesh = -1
for line in open(OBJ, encoding="utf-8", errors="ignore"):
    if line.startswith("o "):
        cur_mesh += 1
    elif line.startswith("v "):
        m = re.match(r"v\s+([-\d.eE+]+)\s+([-\d.eE+]+)\s+([-\d.eE+]+)", line)
        verts.append(tuple(map(float, m.groups())))

img = np.asarray(Image.open(UVVIEW).convert("RGB"))
Hp = Wp = 1024

def to_screen(p):
    sx = (p[0] + 0.11) / 0.22 * Wp
    sy = Hp * 0.92 - (p[1] + 0.10) / 0.30 * Hp
    return int(sx), int(sy)

def sample_uv(p, label):
    sx, sy = to_screen(p)
    if 0 <= sx < Wp and 0 <= sy < Hp:
        r, g, b = img[sy, sx]
        u, v = r / 255.0, g / 255.0
        print(f"{label:14s} 3D=({p[0]:+.4f},{p[1]:+.4f},{p[2]:.3f})  screen=({sx},{sy})  uv=({u:.4f},{v:.4f})")
    else:
        print(f"{label}: off-screen {sx},{sy}")

zmax = max(v[2] for v in verts)
# 解剖点 (从 head_male_a.obj 范围: x±0.093, y-0.07..0.17, z 前脸 ~1.76-1.79)
sample_uv((0.033, 0.065, zmax - 0.02), "R_EYE")
sample_uv((-0.033, 0.065, zmax - 0.02), "L_EYE")
sample_uv((0.0, 0.039, zmax - 0.003), "NOSE_TIP")
sample_uv((0.0, 0.015, zmax - 0.03), "MOUTH")
sample_uv((0.0, 0.095, zmax - 0.03), "BROW")
sample_uv((0.0, -0.015, zmax - 0.08), "CHIN")
sample_uv((0.055, 0.02, zmax - 0.11), "EAR_R")
sample_uv((0.0, 0.13, zmax - 0.06), "FOREHEAD")
sample_uv((0.0, -0.05, zmax - 0.15), "NECK_LOW")
