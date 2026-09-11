# -*- coding: utf-8 -*-
"""配准探针 1：太阁 MapLand_j.TR5 陆地掩膜 ↔ 素材图(TaikouMap2 / japanmap_hires) 海陆形状对照。"""
import numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS = None

TK5 = r"E:\taikou5\Taikou5.Green.Edition-ALI213\Taikou5\MapLand_j.TR5"
SRC = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\ExportHeightMatMap\SourceMap\TaikouMap2.png"

raw = np.frombuffer(open(TK5, "rb").read(), dtype=np.uint8).reshape(448, 550)
vals, cnts = np.unique(raw, return_counts=True)
print("TK5 地形格值分布(前12):", list(zip(vals.tolist(), cnts.tolist()))[:12])
tk_land = raw != 0xFE
print("TK5 陆地占比: %.1f%%" % (100 * tk_land.mean()))

im = Image.open(SRC).convert("RGB")
a = np.asarray(im).astype(np.int16)
print("素材图尺寸:", im.size)
r, g, b = a[..., 0], a[..., 1], a[..., 2]
# 海 = 偏蓝（b 最高）且不太亮；其余算陆
src_sea = (b > r + 8) & (b >= g)
src_land = ~src_sea
print("素材陆地占比: %.1f%%" % (100 * src_land.mean()))


def ascii_map(mask, w=104, h=40):
    H, W = mask.shape
    out = []
    for j in range(h):
        row = ""
        for i in range(w):
            blk = mask[j * H // h:(j + 1) * H // h, i * W // w:(i + 1) * W // w]
            f = blk.mean()
            row += "#" if f > 0.6 else ("+" if f > 0.3 else ".")
        out.append(row)
    return out


print("\n===== 左：太阁 MapLand_j.TR5（550x448）  右：TaikouMap2.png（704x448）=====")
L, R = ascii_map(tk_land), ascii_map(src_land)
for j in range(len(L)):
    print(L[j] + " | " + R[j])
