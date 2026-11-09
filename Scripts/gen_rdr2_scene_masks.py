#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""从子场景彩图识别地表材质 -> 每层一张 8bit mask（给 ModKit 地形层 Import Materialmap）。

口径（与 gen_rdr2_sdn_terrain.py / gen_rdr2_terrain_colormap.py 同一套，别改）：
  · 彩图源 = `deliverable/blender/scenes/tex/<id>_color.png`，1 米/像素，
    **行 0 = 北、列 0 = 西**，左上角 = far_rect 的 (x0, y1)。
  · 地形矩形 = far_rect 按正方形节点下取整（node_size = 256 m），**从北边裁**；
    sdn: far_rect 3328x3968 -> 13x15 @256 = 3328x3840。
  · 输出 mask 尺寸 = 高度图尺寸（nodes*256+1）：3329 x 3841，8bit 灰度。

分类口径（2026-11-09 用户裁定）：
  · **城区 = 泥土**（城区在彩图里是一整块深色，分不出街道/屋顶）。
  · 水 = RDR2 水网图 `water_mask.png`（4 m/px，全局）——彩图里的水是"画"出来的，
    颜色不唯一（深蓝黑 / 浅灰褐都有），单靠颜色会把城区误判成水。
  · 其余按色相/明度分：绿=草/林，棕=土，深绿=林。

输出（Debug/RD2构造相关/terrain_masks_sdn/）：
  mask_water.png / mask_dirt.png / mask_grass.png / mask_forest.png
  preview_classes.png   分类着色预览（1/4 缩略）
  mask_meta.json        每层占比 + 用到的贴图

用法：
  python Scripts/gen_rdr2_scene_masks.py --scene sdn
"""
import argparse
import io
import json
import os
import sys

import numpy as np
from PIL import Image

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
LWN = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
OUT_ROOT = os.path.join(LWN, "Debug", "RD2构造相关")
STAGE_ROOT = os.path.join(LWN, "..", "RDR2", "AssetSources", "ImortReady")

NODE_SIZE_M = 256.0

# 世界框（color_1m / water_mask 的公共口径）
WX0, WY1, RES_W = -6844.0, 3224.0, 4.0

# 水位图网格（export/world/water_level_map_2lv.npy）
LEVEL_MPP = 6.004
LEVEL_OX, LEVEL_OY = -7684.0, -8196.0

# 圣丹尼斯主水体水位（全局水面用同一个值）
DEFAULT_WATER_LEVEL = 40.5

# 层定义：名字 -> (预览色, 原版贴图)
LAYERS = [
    ("water", (40, 90, 200), "water_ground + gloss_s"),
    ("mud", (150, 105, 60), "ground_mud_a_d"),
    ("grass", (110, 170, 70), "ground_grass_b_d"),
    ("rock", (128, 128, 128), "rock_cliff_b_d"),
]


def scene_rect(scene_id):
    """far_rect -> 正方形节点约束下的地形矩形（从北边裁）。"""
    p = os.path.join(RDR2, "deliverable", "blender", "scenes", "scene_report_%s.json" % scene_id)
    d = json.load(io.open(p, encoding="utf-8"))
    x0, y0, x1, y1 = d["far_rect"]
    nx = int((x1 - x0) // NODE_SIZE_M)
    ny = int((y1 - y0) // NODE_SIZE_M)
    # 南边不动，从北边裁到整节点：sdn far_rect y[-3072,896] -> y[-3072,768]
    return x0, y0, x1, y0 + ny * NODE_SIZE_M, nx, ny


def rgb_to_hsv(a):
    """a: float32 HxWx3 0..1 -> h(0..360), s, v"""
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx = a.max(2)
    mn = a.min(2)
    d = mx - mn
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0.0)
    h = np.zeros_like(mx)
    m = d > 1e-6
    idx = m & (mx == r)
    h[idx] = (60.0 * ((g[idx] - b[idx]) / d[idx])) % 360.0
    idx = m & (mx == g)
    h[idx] = 60.0 * ((b[idx] - r[idx]) / d[idx]) + 120.0
    idx = m & (mx == b)
    h[idx] = 60.0 * ((r[idx] - g[idx]) / d[idx]) + 240.0
    return h, s, mx


def gauss_blur(a, sigma):
    """可分离高斯模糊（不依赖 scipy）。"""
    if sigma <= 0:
        return a
    rad = max(1, int(round(sigma * 3)))
    x = np.arange(-rad, rad + 1, dtype=np.float32)
    k = np.exp(-(x * x) / (2.0 * sigma * sigma))
    k /= k.sum()
    pad = np.pad(a, ((0, 0), (rad, rad)), mode="edge")
    out = np.zeros_like(a)
    for i, w in enumerate(k):
        out += w * pad[:, i:i + a.shape[1]]
    pad = np.pad(out, ((rad, rad), (0, 0)), mode="edge")
    out2 = np.zeros_like(a)
    for i, w in enumerate(k):
        out2 += w * pad[i:i + a.shape[0], :]
    return out2


def load_water(x0, x1, y0, y1, size):
    p = os.path.join(RDR2, "deliverable", "terrain", "water_mask.png")
    im = Image.open(p).convert("L")
    mx0 = int(round((x0 - WX0) / RES_W))
    mx1 = int(round((x1 - WX0) / RES_W))
    my0 = int(round((WY1 - y1) / RES_W))
    my1 = int(round((WY1 - y0) / RES_W))
    c = im.crop((mx0, my0, mx1, my1)).resize(size, Image.BILINEAR)
    return np.asarray(c, dtype=np.float32) / 255.0


def load_weight(name, x0, x1, y0, y1, size):
    """RDR2 自己的地表材质权重（4 m/px，全局同一世界框）。"""
    p = os.path.join(RDR2, "deliverable", "terrain", "weights", "w_%s.png" % name)
    im = Image.open(p).convert("L")
    mx0 = int(round((x0 - WX0) / RES_W))
    mx1 = int(round((x1 - WX0) / RES_W))
    my0 = int(round((WY1 - y1) / RES_W))
    my1 = int(round((WY1 - y0) / RES_W))
    c = im.crop((mx0, my0, mx1, my1)).resize(size, Image.BILINEAR)
    return np.asarray(c, dtype=np.float32) / 255.0


def load_water_level(x0, x1, y0, y1, size):
    """RDR2 逐像素水位（米）；没有水的地方是 NaN。

    网格：2048²、6.004 m/px、原点 (-7684, -8196)、**行 0 = 南**（与 PNG 相反）。
    """
    a = np.load(os.path.join(RDR2, "export", "world", "water_level_map_2lv.npy"))
    px, py = size
    col = x0 + np.arange(px, dtype=np.float32)
    row = y1 - np.arange(py, dtype=np.float32)          # 输出行 0 = 北
    c = np.clip(((col - LEVEL_OX) / LEVEL_MPP).astype(np.int64), 0, a.shape[1] - 1)
    r = np.clip(((row - LEVEL_OY) / LEVEL_MPP).astype(np.int64), 0, a.shape[0] - 1)
    return a[np.ix_(r, c)]


def load_height(scene_id, size):
    """读已生成的高度图（绝对米）。用来把主水体对齐到地形等高线。"""
    d = os.path.join(OUT_ROOT, "terrain_%s" % scene_id)
    meta = os.path.join(d, "terrain_%s_meta.json" % scene_id)
    if not os.path.isfile(meta):
        return None
    m = json.load(io.open(meta, encoding="utf-8"))
    hx, hy = m["height_px"]
    p = os.path.join(d, "rdr2_%s_height_%dx%d.png" % (scene_id, hx, hy))
    if not os.path.isfile(p):
        return None
    a = np.asarray(Image.open(p), dtype=np.float32)
    if a.shape != (size[1], size[0]):
        a = np.asarray(Image.open(p).resize(size, Image.BILINEAR), dtype=np.float32)
    return m["z_min_m"] + a / 65535.0 * (m["z_max_m"] - m["z_min_m"])


def dilate(m, n=1):
    o = m.copy()
    for _ in range(n):
        o = o | np.roll(o, 1, 0) | np.roll(o, -1, 0) | np.roll(o, 1, 1) | np.roll(o, -1, 1)
    return o


def load_color(scene_id, x0, x1, y0, y1, size):
    p = os.path.join(RDR2, "deliverable", "blender", "scenes", "tex", "%s_color.png" % scene_id)
    im = Image.open(p).convert("RGB")
    d = json.load(io.open(os.path.join(
        RDR2, "deliverable", "blender", "scenes", "scene_report_%s.json" % scene_id), encoding="utf-8"))
    fx0, _, fx1, fy1 = d["far_rect"]
    px0 = int(round(x0 - fx0))
    px1 = int(round(x1 - fx0))
    py0 = int(round(fy1 - y1))          # 行 0 = 北
    py1 = int(round(fy1 - y0))
    c = im.crop((px0, py0, px1, py1)).resize(size, Image.BILINEAR)
    return np.asarray(c, dtype=np.float32) / 255.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--scene", default="sdn")
    ap.add_argument("--blur", type=float, default=6.0, help="mask 边缘柔化半径（米）")
    ap.add_argument("--water-level", type=float, default=DEFAULT_WATER_LEVEL,
                    help="全局水面高度（米），仅 contour 模式用")
    ap.add_argument("--water-source", choices=["rdr2", "contour"], default="rdr2",
                    help="rdr2 = 用 RDR2 原生水栅格（默认）；contour = 按地形等高线重算（会改判定）")
    ap.add_argument("--merge-water-into-mud", action="store_true",
                    help="水只靠场景全局水：把 water 区域并进 mud，water 层输出全黑")
    ap.add_argument("--no-stage", action="store_true")
    args = ap.parse_args()

    sid = args.scene
    x0, y0, x1, y1, nx, ny = scene_rect(sid)
    px, py = nx * 256 + 1, ny * 256 + 1
    print("scene %s rect x[%.0f,%.0f] y[%.0f,%.0f]  %dx%d nodes @256 -> %dx%d px"
          % (sid, x0, x1, y0, y1, nx, ny, px, py))

    color = load_color(sid, x0, x1, y0, y1, (px, py))
    water = load_water(x0, x1, y0, y1, (px, py))
    w_grass = load_weight("grass", x0, x1, y0, y1, (px, py))
    w_dirt = load_weight("dirt", x0, x1, y0, y1, (px, py))
    w_rock = load_weight("rock", x0, x1, y0, y1, (px, py))

    # 彩图只负责两件权重图答不出来的事：
    #   ① 城区（深色块、不是水）——权重图把城区地面算成"草"，必须按彩图改成泥土；
    #   ② 水由 RDR2 水网图给（彩图的水有深蓝黑/浅灰褐两种画法，单看颜色会误判）
    smooth = np.stack([gauss_blur(color[..., i], 2.0) for i in range(3)], axis=2)
    h, s, v = rgb_to_hsv(smooth)

    painted = water > 0.5
    level = load_water_level(x0, x1, y0, y1, (px, py))
    z = load_height(sid, (px, py))

    if args.water_source == "contour" and z is not None:
        # 🔴 水按"谁决定水面"拆两半（2026-11-09）：
        #   ① 主水体（水位 <= 全局水面）：**按地形等高线描边** —— 全局水面是一张水平面，
        #      它的岸就是 z == 水位的等高线；照 RDR2 画的水网会偏约 20 m（水网是从
        #      下载地图配准出来的，5 个控制点、RMS ~2 px），于是岸边出现蓝斑。
        #   ② 高水位水塘（山里，全局水面够不着）：**保留 RDR2 画的**，这是它们唯一的表示。
        lvl_ok = np.isfinite(level)
        high = painted & lvl_ok & (level > args.water_level + 0.6)
        no_lvl = painted & ~lvl_ok
        main = dilate(z < args.water_level, 3)          # 外扩 3 m 当湿岸
        is_water = main | high | no_lvl
        print("water: contour(%.1fm) %.2f%% + high %.2f%% + no-level %.2f%% -> %.2f%%"
              % (args.water_level, 100 * main.mean(), 100 * high.mean(),
                 100 * no_lvl.mean(), 100 * is_water.mean()))
    else:
        is_water = painted
        if args.water_source == "contour":
            print("WARN: heightmap for %s not found -- fall back to RDR2 raster" % sid)

    city = (~is_water) & (v < 0.30)
    city = gauss_blur(city.astype(np.float32), 6.0) > 0.5

    # 草 / 土 / 岩 = RDR2 自己的地表材质权重（4 m/px，权威）；
    # 城区叠成泥土；水给一点加成保住岸线。
    scores = {
        "water": gauss_blur(is_water.astype(np.float32), 20.0) + 1e-4,
        "mud": gauss_blur(w_dirt, 4.0) + city.astype(np.float32) * 1.5,
        "rock": gauss_blur(w_rock, 4.0),
        "grass": gauss_blur(w_grass, 4.0),
    }
    order = ["water", "mud", "rock", "grass"]      # 平局优先级
    stack = np.stack([scores[k] for k in order], axis=0)
    lab = stack.argmax(0)
    masks = {k: (lab == i).astype(np.float32) for i, k in enumerate(order)}

    if args.merge_water_into_mud:
        # 用户裁定（2026-11-09）：水面完全交给 Scene Options 的全局水，
        # 地形层不再画水 —— 水区并进 mud（河床是泥），water 层输出全黑把它清掉。
        merged = float(masks["mud"].sum() + masks["water"].sum())
        masks["mud"] = np.clip(masks["mud"] + masks["water"], 0.0, 1.0)
        masks["water"] = np.zeros_like(masks["water"])
        print("merged water into mud: water %.2f%% -> mud %.2f%%"
              % (100 * merged / masks["mud"].size * 0 - 0, 100 * masks["mud"].mean()))

    outdir = os.path.join(OUT_ROOT, "terrain_masks_%s" % sid)
    os.makedirs(outdir, exist_ok=True)
    # 清掉上一次跑出来、这次已经不存在的层（防止误导旧 mask）
    keep = {"mask_%s.png" % k for k, _, _ in LAYERS}
    for f in os.listdir(outdir):
        if f.startswith("mask_") and f.endswith(".png") and f not in keep:
            os.remove(os.path.join(outdir, f))
            print("removed stale", f)

    # 柔化：先模糊再裁掉水面的软边（水边缘保持一点过渡更自然）
    sigma = args.blur / 2.0
    stats = {}
    prem = np.zeros((py, px, 3), dtype=np.float32)
    for name, colr, tex in LAYERS:
        m = gauss_blur(masks[name], sigma)
        m = np.clip(m, 0.0, 1.0)
        Image.fromarray((m * 255.0).astype(np.uint8), mode="L").save(
            os.path.join(outdir, "mask_%s.png" % name))
        stats[name] = {"share": float(masks[name].mean()), "texture": tex}
        prem += m[..., None] * np.array(colr, dtype=np.float32)[None, None, :] / 255.0

    # 预览：分类着色 + 原始彩图并排
    prem = np.clip(prem / np.maximum(prem.max(2, keepdims=True), 1e-6), 0, 1)
    tmp = (prem * 255).astype(np.uint8)
    side = np.concatenate([(color * 255).astype(np.uint8), tmp], axis=1)
    Image.fromarray(side).resize((side.shape[1] // 4, side.shape[0] // 4), Image.LANCZOS).save(
        os.path.join(outdir, "preview_classes.png"))

    meta = {
        "scene": sid,
        "rect_m": [x0, y0, x1, y0 + ny * NODE_SIZE_M],
        "nodes": [nx, ny],
        "node_size_m": NODE_SIZE_M,
        "mask_px": [px, py],
        "layers": stats,
        "rule": "water=RDR2 water_mask; green(h55-110,s>0.30) -> forest(v<0.34)/grass; "
                "city(v<0.30) & rest -> dirt",
    }
    with io.open(os.path.join(outdir, "mask_meta.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(meta, f, ensure_ascii=False, indent=1)

    print("shares:", {k: round(v["share"] * 100, 1) for k, v in stats.items()})
    print("out ->", outdir)

    if not args.no_stage:
        stage = os.path.join(STAGE_ROOT, "rdr2_terrain_masks_%s" % sid)
        os.makedirs(stage, exist_ok=True)
        for f in os.listdir(stage):
            if f.startswith("mask_") and f.endswith(".png") and f not in keep:
                os.remove(os.path.join(stage, f))
                print("removed stale (stage)", f)
        for name, _, _ in LAYERS:
            src = os.path.join(outdir, "mask_%s.png" % name)
            with open(src, "rb") as f:
                data = f.read()
            with open(os.path.join(stage, "mask_%s.png" % name), "wb") as f:
                f.write(data)
        print("staged ->", stage)


if __name__ == "__main__":
    main()
