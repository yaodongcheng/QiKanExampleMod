# -*- coding: utf-8 -*-
"""把 w_water.png（4 m/px，行 0 = 北）重采样成地形层的 materialmap（8 bit 灰度）。

用途：不用全局 water_level，改用「water 地形材质层 + 本 mask」画局部水面
（原版 1.5.x Main_map 就是这么干的：water_exists=false + river 层 water_ground）。

输出：
  Debug/RD2构造相关/terrain_mask/mask_water_map_4097x3073.png
  Debug/RD2构造相关/terrain_mask/mask_water_sdn_3329x3841.png
"""
import os
import numpy as np
from PIL import Image

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
LWN = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
OUT = os.path.join(LWN, "Debug", "RD2构造相关", "terrain_mask")
STAGE = os.path.join(LWN, "..", "RDR2", "AssetSources", "ImortReady", "rdr2_terrain_mask")

WX0, WY1 = -6844.0, 3224.0
RES = 4.0
SRC = os.path.join(RDR2, "deliverable", "terrain", "weights", "w_water.png")

JOBS = [
    # name, x0, x1, y0, y1, out_w, out_h
    ("mask_water_map_4097x3073", WX0, 3924.0, -4800.0, 3224.0, 4097, 3073),
    ("mask_water_sdn_3329x3841", 512.0, 3840.0, -3072.0, 768.0, 3329, 3841),
]


def main():
    os.makedirs(OUT, exist_ok=True)
    im = Image.open(SRC).convert("L")
    print("source", SRC, im.size)
    for name, x0, x1, y0, y1, w, h in JOBS:
        px0 = int(round((x0 - WX0) / RES))
        px1 = int(round((x1 - WX0) / RES))
        py0 = int(round((WY1 - y1) / RES))
        py1 = int(round((WY1 - y0) / RES))
        sub = im.crop((px0, py0, px1, py1)).resize((w, h), Image.BILINEAR)
        p = os.path.join(OUT, name + ".png")
        sub.save(p, format="PNG", optimize=False)
        print("%-28s crop %dx%d -> %dx%d  %.1f MB" %
              (name, px1 - px0, py1 - py0, w, h, os.path.getsize(p) / 1e6))
        os.makedirs(STAGE, exist_ok=True)
        sub.save(os.path.join(STAGE, name + ".png"), format="PNG", optimize=False)
    print("staged ->", STAGE)


if __name__ == "__main__":
    main()
