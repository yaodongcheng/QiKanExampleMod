# -*- coding: utf-8 -*-
"""换算验证：太阁坐标 → 素材图 px → 世界米(2048x1280)。用京(town_kyoto 969.424/421.563)当锚点。

链路假设：
  1) 太阁坐标空间 = 底图14080x8960 / 35 = 402.286 x 256，y 向南增大（y=0 = 最北）
  2) 素材图 TaikouMap2.png 704x448 = 该空间 x1.75（同比例 1.5714）
  3) 管线把源图「陆地包围盒居中扩到 1.6 比例」后铺满世界 2048x1280
"""
import numpy as np
from PIL import Image
from scipy import ndimage

SRC = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\ExportHeightMatMap\SourceMap\TaikouMap2.png"
TARGET_RATIO = 2048 / 1280.0
MASTER_W = 15840
WSRC, HSRC = 704, 448          # 原图尺寸（用于缩放到 MASTER 尺度前的记录）
SPACE_W, SPACE_H = 14080 / 35.0, 8960 / 35.0   # 太阁坐标空间


def crop_box(im):
    """复刻 make_heightmap.py 的 auto_crop：小图先放大到 MASTER_W，再取陆地包围盒居中扩到 1.6。"""
    im = im.convert("RGB")
    sw, sh = im.size
    if sw < MASTER_W * 0.5:
        up = MASTER_W / sw
        im2 = im.resize((MASTER_W, max(1, int(sh * up))), Image.LANCZOS)
    else:
        im2 = im
    a = np.asarray(im2).astype(np.int16)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    land = (g > r) & (g > b * 0.95)
    land = ndimage.binary_opening(land, np.ones((3, 3)))
    cols = land.sum(0).astype(np.float64)
    rows = land.sum(1).astype(np.float64)
    cx = np.where(cols > 0.005 * cols.max())[0]
    cy = np.where(rows > 0.005 * rows.max())[0]
    W2, H2 = im2.size
    sx, sy = W2 / 396.0, H2 / 264.0
    x0, x1 = (cx[0] - 6) * sx, (cx[-1] + 6) * sx
    y0, y1 = (cy[0] - 6) * sy, (cy[-1] + 6) * sy
    for _ in range(6):
        w, h = x1 - x0, y1 - y0
        if abs(w / h - TARGET_RATIO) < 0.002:
            break
        if w / h > TARGET_RATIO:
            need = w / TARGET_RATIO - h
            y0 -= need / 2; y1 += need / 2
        else:
            need = h * TARGET_RATIO - w
            x0 -= need / 2; x1 += need / 2
        x0 = max(0.0, x0); y0 = max(0.0, y0)
        x1 = min(W2, x1); y1 = min(H2, y2) if False else min(H2, y1)
    # 归一到原始源图坐标系（放大倍数还原）
    k = sw / W2
    return x0 * k, y0 * k, x1 * k, y1 * k


box = crop_box(Image.open(SRC))
print("素材图尺寸 %dx%d  裁切盒 = (%.1f, %.1f, %.1f, %.1f)  比例 %.4f" %
      (WSRC, HSRC, box[0], box[1], box[2], box[3], (box[2] - box[0]) / (box[3] - box[1])))


def tk5_to_world(x, y, k=None):
    """太阁坐标 → 世界米。k = 坐标空间→素材图px 的比例（默认按素材图/空间同比例推导）"""
    if k is None:
        k = WSRC / SPACE_W
    px, py = x * k, y * k
    x0, y0, x1, y1 = box
    wx = (px - x0) / (x1 - x0) * 2048.0
    wy = (1.0 - (py - y0) / (y1 - y0)) * 1280.0
    return wx, wy


# 已知锚点：京之町 太阁(214,183) ↔ mod town_kyoto (969.424, 421.563)
for name, (tx, ty) in (("京之町", (214, 183)), ("二條城", (214, 180))):
    wx, wy = tk5_to_world(tx, ty)
    print("%s 太阁(%d,%d) → 世界(%.1f, %.1f)   mod 已知(969.4, 421.6)  误差(%.1f, %.1f)m"
          % (name, tx, ty, wx, wy, wx - 969.424, wy - 421.563))

# 空间比例 k 反解：若只有 k 未知（其余链路成立），解出能让京落在 mod 坐标的 k
x0, y0, x1, y1 = box
import scipy.optimize as so
for target, axis in (((969.424, 421.563), "both"),):
    def err(k):
        wx, wy = tk5_to_world(214, 183, k)
        return (wx - 969.424) ** 2 + (wy - 421.563) ** 2
    res = so.minimize_scalar(err, bounds=(0.5, 5.0), method="bounded")
    print("反解最优 k = %.4f （残差 %.1f m）→ 等价坐标空间 = %.1f x %.1f"
          % (res.x, res.fun ** 0.5, WSRC / res.x, HSRC / res.x))
