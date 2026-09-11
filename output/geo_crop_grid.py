# -*- coding: utf-8 -*-
"""区域裁图 + 20px 网格，用于精确读地标像素。"""
from PIL import Image, ImageDraw
Image.MAX_IMAGE_PIXELS = None
SRC = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\ExportHeightMatMap\SourceMap\TaikouMap2.png"
OUT = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\output"
im = Image.open(SRC).convert("RGB")

def crop_grid(name, box, k):
    c = im.crop(box).resize(((box[2] - box[0]) * k, (box[3] - box[1]) * k), Image.LANCZOS)
    d = ImageDraw.Draw(c)
    for x in range(box[0] - box[0] % 20 + 20, box[2], 20):
        d.line([((x - box[0]) * k, 0), ((x - box[0]) * k, c.size[1])], fill=(255, 0, 0), width=1)
        d.text(((x - box[0]) * k + 2, 3), str(x), fill=(255, 255, 0))
    for y in range(box[1] - box[1] % 20 + 20, box[3], 20):
        d.line([(0, (y - box[1]) * k), (c.size[0], (y - box[1]) * k)], fill=(255, 0, 0), width=1)
        d.text((3, (y - box[1]) * k + 2), str(y), fill=(255, 255, 0))
    c.save(OUT + "\\geo_" + name + ".png")
    print(name, box, "→", OUT + "\\geo_" + name + ".png", c.size)

crop_grid("honshu_c", (420, 230, 620, 340), 4)   # 本州中部（东京湾/伊势湾/大阪湾/伊豆/房总）
crop_grid("kyushu",   (90, 290, 260, 420), 4)    # 九州
crop_grid("hokkaido", (560, 0, 704, 170), 4)     # 北海道
