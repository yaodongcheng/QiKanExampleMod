# -*- coding: utf-8 -*-
"""素材图地理配准·第一步：陆地掩膜的极值点 + 关键角落裁图（供目视认地标）。"""
import numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS = None

SRC = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\ExportHeightMatMap\SourceMap\TaikouMap2.png"
OUT = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\output"

im = Image.open(SRC).convert("RGB")
W, H = im.size
a = np.asarray(im).astype(np.int16)
r, g, b = a[..., 0], a[..., 1], a[..., 2]
sea = (b > r + 8) & (b >= g)
land = ~sea
# 海面有波浪纹理，亮crest 会被误判成陆地 → 改用「绿>蓝」判陆 + 开运算去噪
sea2 = b >= g
land2 = ~sea2
from scipy import ndimage
land2 = ndimage.binary_opening(land2, np.ones((3, 3)))
print("图 %dx%d  陆地(粗判) %.1f%%  陆地(绿>蓝+开运算) %.1f%%" % (W, H, 100 * land.mean(), 100 * land2.mean()))
land = land2

ys, xs = np.nonzero(land)
print("陆地 x 范围 %d..%d   y 范围 %d..%d" % (xs.min(), xs.max(), ys.min(), ys.max()))
for nm, sel in (("最北", ys.argmin()), ("最南", ys.argmax()), ("最东", xs.argmax()), ("最西", xs.argmin())):
    x, y = xs[sel], ys[sel]
    print("  %s: px=(%d,%d)  邻域陆地占比 %.2f" % (nm, x, y, land[max(0, y - 8):y + 9, max(0, x - 8):x + 9].mean()))

# 角落裁图（放大 3x）供目视
for name, box in (("TR", (W - 200, 0, W, 140)), ("BL", (0, H - 150, 180, H)),
                  ("L", (0, 120, 120, 300)), ("BR", (W - 220, H - 160, W, H))):
    c = im.crop(box).resize(((box[2] - box[0]) * 3, (box[3] - box[1]) * 3), Image.LANCZOS)
    p = OUT + r"\geo_" + name + ".png"
    c.save(p)
    print("裁图", name, box, "->", p)
