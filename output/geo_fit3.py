# -*- coding: utf-8 -*-
"""比例重拟：偏移由「京锚」精确推出（保证京之町落在用户手工校准的 969.424/421.563），只优化 kx,ky。"""
import csv, io, json
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
Image.MAX_IMAGE_PIXELS = None

ROOT = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
SRC = ROOT + r"\tools\ExportHeightMatMap\SourceMap\TaikouMap2.png"
CSV = ROOT + r"\Knowledge\太阁5\骑砍2织丰角色ID对应\csv\Settlements.csv"
OUT = ROOT + r"\output"
im = Image.open(SRC).convert("RGB")
W, H = im.size
a = np.asarray(im).astype(np.int16)
land = ndimage.binary_opening(~(a[..., 2] >= a[..., 1]), np.ones((3, 3)))
field = ndimage.gaussian_filter(land.astype(np.float32), 5.0)
field /= field.max()

FOREIGN = {"吕宋", "宁波", "那霸", "釜山"}
rows = [r for r in csv.DictReader(io.open(CSV, encoding="utf-8-sig")) if r["Name_All"] not in FOREIGN]
tx = np.array([float(r["TK5_X"]) for r in rows], np.float32)
ty = np.array([float(r["TK5_Y"]) for r in rows], np.float32)

AX, AY = 214.0, 183.0                                     # 京之町（太阁）
PX0, PY0 = 969.424 / 2048.0 * W, (1 - 421.563 / 1280.0) * H  # 京（mod 手工校准）→ 素材图像素


def score(kx, ky):
    ox, oy = PX0 - kx * AX, PY0 - ky * AY
    px, py = tx * kx + ox, ty * ky + oy
    ok = (px >= 0) & (px < W - 1) & (py >= 0) & (py < H - 1)
    s = field[np.clip(py.astype(np.int32), 0, H - 1), np.clip(px.astype(np.int32), 0, W - 1)]
    s = np.where(ok, s, -2.0)
    return float(s.mean() + 0.35 * s.min()), s


best = None
for kx in np.arange(1.30, 1.905, 0.01):
    for ky in np.arange(1.30, 1.905, 0.01):
        v, _ = score(kx, ky)
        if best is None or v > best[0]:
            best = (v, kx, ky)
v, kx, ky = best
ox, oy = PX0 - kx * AX, PY0 - ky * AY
_, s = score(kx, ky)
print("best kx=%.3f ky=%.3f ox=%.2f oy=%.2f obj=%.4f" % (kx, ky, ox, oy, v))
print("落陆地 %d/%d（>0.5）；京锚精确穿过 ✓" % (int((s > 0.5).sum()), len(s)))

ov = im.copy()
d = ImageDraw.Draw(ov)
for r, px, py in zip(rows, tx * kx + ox, ty * ky + oy):
    col = {"城": (255, 40, 40), "町": (255, 200, 0), "里": (170, 60, 255), "砦": (0, 130, 255)}[r["TK5Type"]]
    d.ellipse([px - 2, py - 2, px + 2, py + 2], fill=col)
d.line([PX0 - 8, PY0, PX0 + 8, PY0], fill=(0, 255, 0), width=2)   # 绿十字 = 京锚
d.line([PX0, PY0 - 8, PX0, PY0 + 8], fill=(0, 255, 0), width=2)
ov.save(OUT + r"\geo_overlay_final.png")
print("叠加图 →", OUT + r"\geo_overlay_final.png")

json.dump({"kx": round(float(kx), 5), "ox": round(float(ox), 3),
           "ky": round(float(ky), 5), "oy": round(float(oy), 3),
           "img": [W, H], "world": [2048, 1280],
           "anchor_note": "偏移由京锚(太阁214,183 ↔ 世界969.424,421.563)精确推出，锚点零误差",
           "residual": "其余据点残差约 30~60m（风格化太阁图 vs 真实地理图非线性），待游戏内校准"},
          io.open(os.path.join(ROOT, r"Knowledge\太阁5\骑砍2织丰角色ID对应\_analysis\mod_coord_transform.json"),
                  "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("参数已写 → _analysis/mod_coord_transform.json")
