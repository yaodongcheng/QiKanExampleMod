# -*- coding: utf-8 -*-
"""首过换算：以 mod 已手工校准的「京」为锚 + 比例 1.75（素材图 704x448 = 太阁坐标空间 402x256 的 1.75 倍）。
输出叠加图供目视检查，并写出换算表（供 XML 生成器用）。"""
import csv, io, json
import numpy as np
from PIL import Image, ImageDraw
Image.MAX_IMAGE_PIXELS = None

ROOT = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
SRC = ROOT + r"\tools\ExportHeightMatMap\SourceMap\TaikouMap2.png"
OUT = ROOT + r"\output"
CSV = ROOT + r"\Knowledge\太阁5\骑砍2织丰角色ID对应\csv\Settlements.csv"
im = Image.open(SRC).convert("RGB")
W, H = im.size

# 锚：京之町 TK5(214,183) ↔ mod town_kyoto 世界(969.424, 421.563)
K = 1.75
anchor_tk = (214.0, 183.0)
anchor_px = (969.424 / 2048.0 * W, (1 - 421.563 / 1280.0) * H)
ox = anchor_px[0] - K * anchor_tk[0]
oy = anchor_px[1] - K * anchor_tk[1]
print("锚像素 = (%.1f, %.1f) → ox=%.2f oy=%.2f" % (anchor_px[0], anchor_px[1], ox, oy))


def tk5_to_world(tx, ty):
    px = K * tx + ox
    py = K * ty + oy
    return px / W * 2048.0, (1 - py / H) * 1280.0, px, py


rows = list(csv.DictReader(io.open(CSV, encoding="utf-8-sig")))
ov = im.copy()
d = ImageDraw.Draw(ov)
pts = []
for r in rows:
    wx, wy, px, py = tk5_to_world(float(r["TK5_X"]), float(r["TK5_Y"]))
    pts.append((r["id"], round(wx, 1), round(wy, 1)))
    col = {"城": (255, 40, 40), "町": (255, 200, 0), "里": (170, 60, 255), "砦": (0, 130, 255)}[r["TK5Type"]]
    d.ellipse([px - 2, py - 2, px + 2, py + 2], fill=col)
ov.save(OUT + r"\geo_overlay_anchor.png")
print("叠加图（红=城 黄=町 紫=里 蓝=砦）→", OUT + r"\geo_overlay_anchor.png")

# 抽样打印几个地标城的换算结果
for tag, want in (("松前(北海道西南)", "胜山馆"), ("京", "京"), ("江户(关东)", "江戶"),
                  ("博多(九州北)", "博多"), ("鹿儿岛(九州南)", "鹿兒島")):
    for r in rows:
        if r["Name_All"].split("|")[0] == want:
            wx, wy, px, py = tk5_to_world(float(r["TK5_X"]), float(r["TK5_Y"]))
            print("  %-16s TK5(%s,%s) → 世界(%.0f, %.0f)  px(%.0f,%.0f)"
                  % (tag, r["TK5_X"], r["TK5_Y"], wx, wy, px, py))
            break

json.dump({"K": K, "ox": ox, "oy": oy, "W": W, "H": H, "world_w": 2048, "world_h": 1280,
           "anchor": {"tk5": anchor_tk, "world": [969.424, 421.563]},
           "note": "首过估算：以京为锚 + 比例1.75；待游戏内目视校准后改本文件重跑"},
          io.open(OUT + r"\mod_coord_transform.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("换算参数 →", OUT + r"\mod_coord_transform.json")
