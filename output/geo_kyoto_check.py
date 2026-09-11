# -*- coding: utf-8 -*-
"""验证 mod 京坐标 ↔ 素材图：把 world(969.424,421.563) 反推成素材图像素并裁图查看。
假设：世界 2048x1280 = 整幅素材图线性铺满，y 向北（图 row0 = 北）。"""
from PIL import Image, ImageDraw
Image.MAX_IMAGE_PIXELS = None

SRC = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\ExportHeightMatMap\SourceMap\TaikouMap2.png"
OUT = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\output"
im = Image.open(SRC).convert("RGB")
W, H = im.size

def world_to_px(wx, wy):
    return wx / 2048.0 * W, (1.0 - wy / 1280.0) * H

def px_to_world(px, py):
    return px / W * 2048.0, (1.0 - py / H) * 1280.0

print("世界→素材图像素: 京(969.424,421.563) →", world_to_px(969.424, 421.563))
print("            出生点(985,428) →", world_to_px(985, 428))

# 裁京周边 160x120（放大 4x）+ 打十字线标出换算点
cx, cy = world_to_px(969.424, 421.563)
box = (int(cx) - 80, int(cy) - 60, int(cx) + 80, int(cy) + 60)
c = im.crop(box).resize((160 * 4, 120 * 4), Image.LANCZOS)
d = ImageDraw.Draw(c)
d.line([(cx - box[0]) * 4 - 40, (cy - box[1]) * 4, (cx - box[0]) * 4 + 40, (cy - box[1]) * 4], fill=(255, 0, 0), width=2)
d.line([(cx - box[0]) * 4, (cy - box[1]) * 4 - 40, (cx - box[0]) * 4, (cy - box[1]) * 4 + 40], fill=(255, 0, 0), width=2)
c.save(OUT + r"\geo_kyoto_check.png")
print("裁图 box=", box, "→", OUT + r"\geo_kyoto_check.png")

# 顺便：把 274 个太阁坐标按「图 = 太阁坐标空间线性映射」的两种猜测叠上去，看哪一种像日本
# 猜测 A：x_px = tx/550*704, y_px = ty/448*448 （旧文档 550x448 说法）
# 猜测 B：x_px = tx/402.29*704, y_px = ty/256*448 （底图/35）
import csv, io, os
rows = list(csv.DictReader(io.open(os.path.join(
    r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Knowledge\太阁5\骑砍2织丰角色ID对应\csv",
    "Settlements.csv"), encoding="utf-8-sig")))
for tag, (sw, sh) in (("A550", (550, 448)), ("B402", (402.286, 256.0))):
    ov = im.copy()
    d = ImageDraw.Draw(ov)
    for r in rows:
        px = float(r["TK5_X"]) / sw * W
        py = float(r["TK5_Y"]) / sh * H
        d.ellipse([px - 2, py - 2, px + 2, py + 2], fill=(255, 40, 40))
    ov.save(OUT + r"\geo_overlay_" + tag + ".png")
    print("叠加图", tag, "→", OUT + r"\geo_overlay_" + tag + ".png")
