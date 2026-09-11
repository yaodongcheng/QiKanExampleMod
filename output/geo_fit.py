# -*- coding: utf-8 -*-
"""太阁坐标 → 素材图像素 的自动拟合。
目标：274 个据点全部落在陆地上（据点都在陆上）——对 4 参数 (kx,ox,ky,oy) 做坐标下降。
排除 4 个「外国港口」（釜山/寧波/呂宋/那霸 = 太阁地图左上角的装饰性港口，素材图里没有）。"""
import csv, io, os
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
r, g, b = a[..., 0], a[..., 1], a[..., 2]
land = ndimage.binary_opening(~(b >= g), np.ones((3, 3)))
# 光滑成连续场（0=深海的远处, 1=陆地内部）→ 便于优化
score_field = ndimage.gaussian_filter(land.astype(np.float32), 6.0)
score_field = score_field / max(score_field.max(), 1e-6)

FOREIGN = {"吕宋", "宁波", "那霸", "釜山"}
rows = [r_ for r_ in csv.DictReader(io.open(CSV, encoding="utf-8-sig")) if r_["Name_All"] not in FOREIGN]
tx = np.array([float(r_["TK5_X"]) for r_ in rows], dtype=np.float32)
ty = np.array([float(r_["TK5_Y"]) for r_ in rows], dtype=np.float32)
print("参与拟合的据点: %d（排除外国港 %d）" % (len(rows), 274 - len(rows)))


def sample(kx, ox, ky, oy):
    px = tx * kx + ox
    py = ty * ky + oy
    ok = (px >= 0) & (px < W - 1) & (py >= 0) & (py < H - 1)
    xi = np.clip(px.astype(np.int32), 0, W - 1)
    yi = np.clip(py.astype(np.int32), 0, H - 1)
    s = score_field[yi, xi]
    s = np.where(ok, s, -1.0)
    return s


def objective(kx, ox, ky, oy):
    s = sample(kx, ox, ky, oy)
    return float(np.mean(s)) + 0.3 * float(np.min(s))      # 均值 + 最差点惩罚


# 坐标下降：交替细网格
kx, ox, ky, oy = 1.75, 0.0, 1.75, 0.0
print("初值 obj=%.4f" % objective(kx, ox, ky, oy))
for round_ in range(4):
    step_k = 0.15 / (1.6 ** round_)
    step_o = 24.0 / (1.6 ** round_)
    for _ in range(3):
        best = (objective(kx, ox, ky, oy), kx, ox)
        for k in kx + np.arange(-6, 7) * step_k / 6:
            for o in ox + np.arange(-6, 7) * step_o / 6:
                v = objective(k, o, ky, oy)
                if v > best[0]:
                    best = (v, k, o)
        _, kx, ox = best
        best = (objective(kx, ox, ky, oy), ky, oy)
        for k in ky + np.arange(-6, 7) * step_k / 6:
            for o in oy + np.arange(-6, 7) * step_o / 6:
                v = objective(kx, ox, k, o)
                if v > best[0]:
                    best = (v, k, o)
        _, ky, oy = best
    print("  round %d: kx=%.4f ox=%.2f ky=%.4f oy=%.2f obj=%.4f"
          % (round_, kx, ox, ky, oy, objective(kx, ox, ky, oy)))

s = sample(kx, ox, ky, oy)
print("最终：落在陆地上 %d/%d；最差样本分 %.3f" % (int((s > 0.5).sum()), len(s), s.min()))
print("等价太阁坐标空间 = %.1f x %.1f（= 图宽/ kx, 图高/ ky）" % (W / kx, H / ky))

# 叠加图
ov = im.copy()
d = ImageDraw.Draw(ov)
for r_, px, py in zip(rows, tx * kx + ox, ty * ky + oy):
    d.ellipse([px - 2, py - 2, px + 2, py + 2], fill=(255, 40, 40))
ov.save(OUT + r"\geo_overlay_fit.png")
print("叠加图 →", OUT + r"\geo_overlay_fit.png")

# 关键对照：京之町(tk212) / 松前(tk000) / 鹿儿岛(tk179 出水/内城) 落点
for want in ("京", "胜山馆", "内城", "江戶", "博多"):
    for r_ in rows:
        if r_["Name_All"].split("|")[0] == want:
            px, py = float(r_["TK5_X"]) * kx + ox, float(r_["TK5_Y"]) * ky + oy
            wx, wy = px / W * 2048, (1 - py / H) * 1280
            print("  %-4s TK5(%s,%s) → px(%.0f,%.0f) → 世界(%.0f,%.0f)"
                  % (want, r_["TK5_X"], r_["TK5_Y"], px, py, wx, wy))
            break
print("  [参照] mod 现有 京 = 世界(969, 421)")
