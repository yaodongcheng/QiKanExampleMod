# -*- coding: utf-8 -*-
"""带约束的精细拟合：kx,ky ∈ [1.40,1.80]，偏移限制在「京锚」±40px 内。
目标 = 274 据点落在陆地上（光滑陆地面场采样），加最差点惩罚。"""
import csv, io, json
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
Image.MAX_IMAGE_PIXELS = None

ROOT = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
SRC = ROOT + r"\tools\ExportHeightMatMap\SourceMap\TaikouMap2.png"
OUT = ROOT + r"\output"
CSV = ROOT + r"\Knowledge\太阁5\骑砍2织丰角色ID对应\csv\Settlements.csv"
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

# 京锚（用户手工校准点）
AX, AY = 214.0, 183.0
PX0, PY0 = 969.424 / 2048.0 * W, (1 - 421.563 / 1280.0) * H


def score(kx, ox, ky, oy):
    px, py = tx * kx + ox, ty * ky + oy
    ok = (px >= 0) & (px < W - 1) & (py >= 0) & (py < H - 1)
    s = field[np.clip(py.astype(np.int32), 0, H - 1), np.clip(px.astype(np.int32), 0, W - 1)]
    s = np.where(ok, s, -2.0)
    return float(s.mean() + 0.35 * s.min()), s


best = None
for kx in np.arange(1.40, 1.805, 0.02):
    for ky in np.arange(1.40, 1.805, 0.02):
        # 偏移由京锚决定（允许 ±40px 平移搜索）
        bx = PX0 - kx * AX
        by = PY0 - ky * AY
        for dx in np.arange(-40, 41, 5):
            for dy in np.arange(-40, 41, 5):
                v, _ = score(kx, bx + dx, ky, by + dy)
                if best is None or v > best[0]:
                    best = (v, kx, bx + dx, ky, by + dy)
print("粗搜 best obj=%.4f  kx=%.3f ox=%.1f ky=%.3f oy=%.1f" % best)

# 细搜
v0, kx0, ox0, ky0, oy0 = best
for kx in np.arange(kx0 - 0.03, kx0 + 0.031, 0.005):
    for ky in np.arange(ky0 - 0.03, ky0 + 0.031, 0.005):
        bx, by = PX0 - kx * AX, PY0 - ky * AY
        for dx in np.arange(-12, 12.1, 2):
            for dy in np.arange(-12, 12.1, 2):
                v, _ = score(kx, bx + dx, ky, by + dy)
                if v > best[0]:
                    best = (v, kx, bx + dx, ky, by + dy)
v, kx, ox, ky, oy = best
_, s = score(kx, ox, ky, oy)
print("细搜 best obj=%.4f  kx=%.4f ox=%.2f ky=%.4f oy=%.2f" % best)
print("落陆地 %d/%d；最差 %.3f" % (int((s > 0.5).sum()), len(s), s.min()))
print("京锚位移 = (%.1f, %.1f) px" % (kx * AX + ox - PX0, ky * AY + oy - PY0))

ov = im.copy()
d = ImageDraw.Draw(ov)
for r, px, py in zip(rows, tx * kx + ox, ty * ky + oy):
    col = {"城": (255, 40, 40), "町": (255, 200, 0), "里": (170, 60, 255), "砦": (0, 130, 255)}[r["TK5Type"]]
    d.ellipse([px - 2, py - 2, px + 2, py + 2], fill=col)
ov.save(OUT + r"\geo_overlay_fit2.png")
print("叠加图 →", OUT + r"\geo_overlay_fit2.png")

for tag, want in (("松前", "胜山馆"), ("江户", "江戶"), ("博多", "博多"), ("鹿儿岛", "鹿兒島")):
    for r in rows:
        if r["Name_All"].split("|")[0] == want:
            px, py = float(r["TK5_X"]) * kx + ox, float(r["TK5_Y"]) * ky + oy
            print("  %-4s px(%.0f,%.0f) → 世界(%.0f,%.0f)"
                  % (tag, px, py, px / W * 2048, (1 - py / H) * 1280))
            break

json.dump({"kx": round(float(kx), 5), "ox": round(float(ox), 3),
           "ky": round(float(ky), 5), "oy": round(float(oy), 3),
           "img": [W, H], "world": [2048, 1280],
           "anchor_check_px": [round(kx * AX + ox, 1), round(ky * AY + oy, 1)],
           "note": "约束拟合（陆地约束 + 京锚 ±40px）；待游戏内目视校准"},
          io.open(OUT + r"\mod_coord_transform.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("参数 →", OUT + r"\mod_coord_transform.json")
