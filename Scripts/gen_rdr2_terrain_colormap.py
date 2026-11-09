# -*- coding: utf-8 -*-
"""把 RDR2 的 1 m/px 彩图裁成【每张骑砍2 场景地形范围】的 vista 彩图，并出方向标记测试图。

引擎口径（实读 Shaders/Sources/terrain_pixel_functions.rsh）：
  vista 彩图 UV = world_xy / terrain_size，v.y = 1 - y
  ⇒ 整张图在地形上只铺一次；图片行 0 = 北、列 0 = 西（与交付彩图一致）。

输出（Debug/RD2构造相关/terrain_colormap/）：
  rdr2_map_colormap.png        大地图（8192x8192，正方，安全尺寸）
  rdr2_map_colormap_TEST.png   方向标记版（同尺寸）
  rdr2_sdn_colormap.png        圣丹尼斯 1500x1500 m（2048x2048）
  rdr2_sdn_colormap_TEST.png

用法：
  python Scripts/gen_rdr2_terrain_colormap.py --what all --stage-test
  python Scripts/gen_rdr2_terrain_colormap.py --what map
"""
import argparse
import io
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFont

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
LWN = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
OUT = os.path.join(LWN, "Debug", "RD2构造相关", "terrain_colormap")
STAGE = os.path.join(LWN, "..", "RDR2", "AssetSources", "ImortReady", "rdr2_terrain_colormap")

# 世界框（deliverable/terrain/*/meta.json）
WX0, WY0, WX1, WY1 = -6844.0, -4800.0, 3924.0, 3224.0
COLOR = os.path.join(RDR2, "deliverable", "terrain", "color_1m.png")

# 圣丹尼斯场景（Scripts/gen_rdr2_sdn_terrain.py 的口径：13×15 @256 = 3328×3840）
# 真实 far_rect 是 3328×3968；北边裁 128 m 以凑正方形节点，活动区完整保留。
SDN_RECT = (512.0, 3840.0, -3072.0, 768.0)


def crop_world(im, x0, x1, y0, y1):
    """按 RDR2 世界米裁 color_1m（1 m/px，行 0 = 北、列 0 = 西）。"""
    px0 = int(round(x0 - WX0))
    px1 = int(round(x1 - WX0))
    py0 = int(round(WY1 - y1))          # 北在上
    py1 = int(round(WY1 - y0))
    px0, py0 = max(0, px0), max(0, py0)
    px1, py1 = min(im.width, px1), min(im.height, py1)
    return im.crop((px0, py0, px1, py1))


def save_rgb(im, path):
    """编辑器要的格式：8bit RGB，只留 IHDR/IDAT/IEND。"""
    rgb = np.asarray(im.convert("RGB"))
    Image.fromarray(rgb, "RGB").save(path, format="PNG", optimize=False)
    d = open(path, "rb").read()
    chunks, i = [], 8
    while i < len(d) - 8:
        ln = int.from_bytes(d[i:i + 4], "big")
        nm = d[i + 4:i + 8].decode("latin1")
        chunks.append(nm)
        i += 12 + ln
        if nm == "IEND":
            break
    extra = [c for c in chunks if c not in ("IHDR", "IDAT", "IEND")]
    if extra:
        raise SystemExit("FAIL: %s 仍带附加块 %s" % (path, extra))
    print("saved %-46s %dx%d  %.1f MB" % (os.path.basename(path), im.width, im.height,
                                          os.path.getsize(path) / 1e6))


def marker(size):
    """方向/覆盖测试图：四象限色块 + N/E/S/W + 8x8 网格 + 中心十字。"""
    w, h = size
    im = Image.new("RGB", (w, h), (255, 255, 255))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, w // 2, h // 2], fill=(200, 30, 30))          # 西北 红
    d.rectangle([w // 2, 0, w, h // 2], fill=(30, 170, 60))          # 东北 绿
    d.rectangle([0, h // 2, w // 2, h], fill=(30, 70, 200))          # 西南 蓝
    d.rectangle([w // 2, h // 2, w, h], fill=(230, 200, 30))         # 东南 黄
    step = w // 8
    for i in range(1, 8):
        d.line([(i * step, 0), (i * step, h)], fill=(255, 255, 255), width=max(2, w // 400))
        d.line([(0, i * step), (w, i * step)], fill=(255, 255, 255), width=max(2, w // 400))
    d.line([(w // 2, 0), (w // 2, h)], fill=(0, 0, 0), width=max(3, w // 250))
    d.line([(0, h // 2), (w, h // 2)], fill=(0, 0, 0), width=max(3, w // 250))
    try:
        font = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", w // 7)
    except Exception:
        font = ImageFont.load_default()

    def put(txt, xy, fill=(0, 0, 0)):
        bbox = d.textbbox((0, 0), txt, font=font)
        d.text((xy[0] - (bbox[2] - bbox[0]) / 2, xy[1] - (bbox[3] - bbox[1]) / 2),
               txt, font=font, fill=fill)

    put("N", (w / 2, h * 0.09))
    put("S", (w / 2, h * 0.91))
    put("W", (w * 0.08, h / 2))
    put("E", (w * 0.92, h / 2))
    return im


def stage(src, name):
    os.makedirs(STAGE, exist_ok=True)
    dst = os.path.join(STAGE, name + ".png")
    with open(src, "rb") as f:
        data = f.read()
    with open(dst, "wb") as f:
        f.write(data)
    print("staged ->", dst)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--what", choices=["map", "sdn", "all"], default="all")
    ap.add_argument("--stage-test", action="store_true")
    args = ap.parse_args()
    os.makedirs(OUT, exist_ok=True)

    src = Image.open(COLOR).convert("RGB")
    print("source", COLOR, src.size)

    jobs = []
    if args.what in ("map", "all"):
        # 🔴 2026-11-09 用户裁定：**保持 4:3，禁止改正方形**。
        # 非 2 次幂 ⇒ 编辑器会警告不能生成 mipmap；用户接受该警告，按此交付。
        jobs.append(("rdr2_map_colormap", (WX0, WX1, WY0, WY1), (8192, 6144)))
    if args.what in ("sdn", "all"):
        jobs.append(("rdr2_sdn_colormap", SDN_RECT, (1664, 1920)))

    for name, (x0, x1, y0, y1), size in jobs:
        c = crop_world(src, x0, x1, y0, y1)
        print("%-22s crop %dx%d m -> %dx%d px" % (name, round(x1 - x0), round(y1 - y0),
                                                  c.width, c.height))
        real = c.resize(size, Image.LANCZOS)
        real_path = os.path.join(OUT, name + ".png")
        save_rgb(real, real_path)

        test_path = os.path.join(OUT, name + "_TEST.png")
        tw = 2048
        th = max(1, int(round(tw * size[1] / size[0])))
        save_rgb(marker((tw, th)), test_path)
        if args.stage_test:
            stage(test_path, name + "_TEST")


if __name__ == "__main__":
    main()
