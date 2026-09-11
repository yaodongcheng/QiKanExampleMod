# -*- coding: utf-8 -*-
"""素材图打坐标网格（每 50px 细线 + 每 100px 标注），用于目视读地标像素坐标。"""
from PIL import Image, ImageDraw
Image.MAX_IMAGE_PIXELS = None

SRC = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\ExportHeightMatMap\SourceMap\TaikouMap2.png"
OUT = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\output\geo_grid.png"

im = Image.open(SRC).convert("RGB")
W, H = im.size
K = 2
im = im.resize((W * K, H * K), Image.LANCZOS)
d = ImageDraw.Draw(im)
for x in range(0, W, 50):
    col = (255, 0, 0) if x % 100 == 0 else (255, 160, 160)
    d.line([(x * K, 0), (x * K, H * K)], fill=col, width=1)
    if x % 100 == 0:
        d.text((x * K + 3, 4), str(x), fill=(255, 255, 0))
for y in range(0, H, 50):
    col = (255, 0, 0) if y % 100 == 0 else (255, 160, 160)
    d.line([(0, y * K), (W * K, y * K)], fill=col, width=1)
    if y % 100 == 0:
        d.text((4, y * K + 3), str(y), fill=(255, 255, 0))
im.save(OUT)
print("写出", OUT, im.size)
