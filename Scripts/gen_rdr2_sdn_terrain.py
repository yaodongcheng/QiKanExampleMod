#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""从解包高度图切出圣丹尼斯场景地形（给 ModKit 场景 Terrain 面板导入）。

背景：
  - 大地图用的是**整张**高度图（16×12 节点，见 gen_rdr2_terrain.py）。
  - 进城的 Mission 场景是 1:1 米、独立的场景地形，必须从同一张源图**切一块**再重采样。

口径：
  - 源 = `deliverable/heightmap/continent_height_4m.png`（4 米/像素，目前手上最细的一份）。
  - 场景 = 1500 × 1500 米，6 × 6 节点 × 250 米（与原版城镇 `empire_town_f` 同规格），
    圣丹尼斯中心 (2554.5, -1150.5) 居中；城区本体 862×955 米，四周留 ~270 米余量。
  - 高度图 1537 × 1537（6×256+1），min/max 取本块实际高程。

输出（Debug/RD2构造相关/terrain_sdn/）：高度图、7 层 mask、颜色参考、meta。
"""

import io
import json
import os
import numpy as np
from PIL import Image
from scipy import ndimage

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
LWN = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
OUT = os.path.join(LWN, "Debug", "RD2构造相关", "terrain_sdn")

CENTER_X, CENTER_Y = 2554.5, -1150.5     # 圣丹尼斯（settlements_world_v2.csv）
SIZE_M = 1500.0                          # 场景边长
NODES = 6                                # 6 × 6 节点
NODE_SIZE_M = SIZE_M / NODES             # 250 米
PIX_PER_NODE = 256
PX = NODES * PIX_PER_NODE + 1            # 1537
SHORE_WIDTH = 70.0                       # 河岸过渡宽度（米）：岸边→河床的 smoothstep 跨度
RIVER_DEPTH_MAX = 2.5                    # 河心比光滑面低多少米
MIN_WATER_PX = 3000                      # 小于这个像素数的水塘当噪声丢掉（≈53×53 米）

LAYERS = ["grass", "conifer", "rock", "dirt", "broadleaf", "water", "snow"]
ENGINE_LAYERS = {
    "default": "grass",
    "flora_forest": "conifer",
    "rock": "rock",
    "grass": "broadleaf",
    "dirt": "dirt",
    "river": "water",
    "snow": "snow",
    "dirt2": None,
    "forest2": None,
}


def _down(x, k):
    """2×2 加权下采样：只用已知像素求平均，返回 (值, 该格是否已知)。"""
    h, w = x.shape
    h2, w2 = h // 2, w // 2
    xb = x[:h2 * 2, :w2 * 2].reshape(h2, 2, w2, 2)
    kb = k[:h2 * 2, :w2 * 2].reshape(h2, 2, w2, 2)
    num = (xb * kb).sum(axis=(1, 3))
    den = kb.sum(axis=(1, 3))
    out = np.where(den > 0, num / np.maximum(den, 1e-9), 0.0)
    return out.astype(np.float32), den > 0


def _up(x, shape):
    z = ndimage.zoom(x, (shape[0] / x.shape[0], shape[1] / x.shape[1]), order=1)
    return z[:shape[0], :shape[1]]


def smooth_fill(z, known, levels=7, iters=300):
    """把 known=True 的像素当边界，把未知区域填补成**光滑曲面**。

    多尺度金字塔插值（push-pull），数学上就是拉普拉斯方程的调和插值：
    未知区域从粗尺度一路插到细尺度，结果是 C1 连续的光滑面，不会出现放射状扇形。
    known 像素全程原值不动。
    """
    stack = [(z.astype(np.float32), known)]
    zz, kk = stack[0]
    for _ in range(levels):
        if min(zz.shape) < 8:
            break
        zz, kk = _down(zz, kk)
        stack.append((zz, kk))
    if kk.any():
        zz = np.where(kk, zz, float(zz[kk].mean()))
    else:
        zz = np.zeros_like(zz)
    for i in range(len(stack) - 2, -1, -1):
        zh, kh = stack[i]
        up = _up(zz, zh.shape)
        zz = np.where(kh, zh, up)

    # 金字塔给的是好初值，但上采样不会严格贴合边界；再用 Jacobi 松弛把边界值"拉进去"，
    # 已知像素全程钉死。收敛量级 = 松弛次数（像素），所以只需几百次就能把近岸拉平。
    if iters > 0:
        for _ in range(iters):
            avg = 0.25 * (np.roll(zz, 1, 0) + np.roll(zz, -1, 0)
                          + np.roll(zz, 1, 1) + np.roll(zz, -1, 1))
            zz = np.where(known, z, avg)
    return zz


def bilinear(arr, fx, fy):
    h, w = arr.shape
    x0 = np.clip(np.floor(fx).astype(np.int64), 0, w - 1)
    y0 = np.clip(np.floor(fy).astype(np.int64), 0, h - 1)
    x1 = np.clip(x0 + 1, 0, w - 1)
    y1 = np.clip(y0 + 1, 0, h - 1)
    tx = np.clip(fx - x0, 0.0, 1.0)
    ty = np.clip(fy - y0, 0.0, 1.0)
    return (arr[y0, x0] * (1 - tx) * (1 - ty) + arr[y0, x1] * tx * (1 - ty)
            + arr[y1, x0] * (1 - tx) * ty + arr[y1, x1] * tx * ty)


def main():
    os.makedirs(OUT, exist_ok=True)

    meta = json.load(io.open(os.path.join(RDR2, "deliverable/heightmap/continent_height_meta.json"), encoding="utf-8"))
    hm = meta["heightmap"]
    sx0, sy1, res = meta["x0"], meta["y1"], meta["res_m"]
    src_z = np.array(Image.open(os.path.join(RDR2, "deliverable/heightmap", hm["file_16bit"])), dtype=np.float32)
    src_z = hm["zmin"] + src_z / 65535.0 * (hm["zmax"] - hm["zmin"])

    x0, x1 = CENTER_X - SIZE_M / 2, CENTER_X + SIZE_M / 2
    y0, y1 = CENTER_Y - SIZE_M / 2, CENTER_Y + SIZE_M / 2
    print("scene 1500x1500 m centred (%.1f, %.1f) -> x[%.1f,%.1f] y[%.1f,%.1f]"
          % (CENTER_X, CENTER_Y, x0, x1, y0, y1))

    # 行 0 = 北 ⇒ 第一行对应 y1
    xs = np.linspace(x0, x1, PX, dtype=np.float32)
    ys = np.linspace(y1, y0, PX, dtype=np.float32)
    fx = (xs - sx0) / res
    fy = (sy1 - ys) / res
    out_z = bilinear(src_z, fx[None, :], fy[:, None])
    src_land = out_z.copy()                          # 源地形原值（校验"陆地没被动过"用）
    src_land = out_z.copy()                          # 源地形原值（校验"陆地没被动过"用）
    water = np.clip(bilinear(np.array(Image.open(os.path.join(RDR2, "deliverable/terrain_weights", "w_water.png")),
                                       dtype=np.float32), fx[None, :], fy[:, None]), 0, 255) > 127
    print("water fraction of block (raw): %.1f%%" % (water.mean() * 100))
    water_raw = water.copy()

    # 掩膜清洗：源图是 4 米/像素，边界锯齿 + 城里夹着大量碎水塘。
    # 直接拿来切岸线 → 城里出现一圈圈白线、岸边出现台阶。先闭运算补齐 + 只留成片水体。
    water = ndimage.binary_closing(water, np.ones((5, 5)))
    lab, nlab = ndimage.label(water)
    if nlab:
        sizes = ndimage.sum(water, lab, range(1, nlab + 1))
        keep = np.zeros(nlab + 1, dtype=bool)
        keep[1:] = sizes >= MIN_WATER_PX
        water = keep[lab]
    print("water fraction of block (cleaned): %.1f%%  (components %d -> %d)"
          % (water.mean() * 100, nlab, int(keep.sum()) if nlab else 0))

    # 🔴 水面高度重建（2026-10-25 用户裁定：不要硬边、不要扇形三角）：
    #   把水面区域当**未知**、陆地当**已知边界**，做多尺度金字塔插值
    #   （数学上等价于在上面解 ∇²z=0 的拉普拉斯方程 / 调和插值）。
    #   结果：边界逐像素等于紧邻陆地（接缝 0），内部是光滑曲面，没有放射状扇形。
    #   然后在这个光滑面上按离岸距离下切一个河床（smoothstep，岸边坡度为 0）。
    dist_px = ndimage.distance_transform_edt(water)
    d_m = dist_px * (SIZE_M / (PX - 1))

    surface = smooth_fill(src_land, ~water_raw, iters=400)
    t = np.clip(d_m / SHORE_WIDTH, 0.0, 1.0)
    t = t * t * (3.0 - 2.0 * t)                     # smoothstep：岸边坡度 0 → 中段最陡 → 河心压平
    river = surface - RIVER_DEPTH_MAX * t

    near_shore = (~water) & (d_m <= 10.0)
    shore_level = float(np.median(src_land[near_shore])) if near_shore.any() else float(np.median(src_land))
    water_surface = float(np.median(river[water])) if water.any() else shore_level
    bed = water_surface - RIVER_DEPTH_MAX

    # 🔴 陆地一律不动（用户裁定）：建筑自带 Z 高度是和源地形对上的，改陆地 = 建筑悬空/埋地。
    # 只动"源图判定为水"的像素：成片水体走河床剖面，被丢弃的碎水塘用光滑面填平（不加深度）。
    out_z = np.where(water, river, np.where(water_raw, surface, src_land))
    print("  water rebuilt by harmonic fill: shore %.2f m, water median %.2f m, depth %.1f m"
          % (shore_level, water_surface, RIVER_DEPTH_MAX))
    # 岸线台阶检查：只看"水/陆相邻"的那一圈像素对，统计落差
    zw = water.astype(np.int8)
    dh = np.abs(np.diff(out_z, axis=1))[np.diff(zw, axis=1) != 0]
    dv = np.abs(np.diff(out_z, axis=0))[np.diff(zw, axis=0) != 0]
    steps = np.concatenate([dh, dv]) if dh.size + dv.size else np.zeros(1)
    print("  shore step: median %.2f m, p95 %.2f m, p99 %.2f m, max %.2f m, >5m: %d px"
          % (np.median(steps), np.percentile(steps, 95), np.percentile(steps, 99),
             steps.max(), int((steps > 5).sum())))
    # 陆地必须和源图逐像素一致（建筑自带 Z 是跟源地形对上的）——这里只做校验，不做任何抹平
    print("land unchanged: max |dz| = %.4f m"
          % float(np.abs(out_z[~water_raw] - src_land[~water_raw]).max()))
    print("shoreline fixed: shore %.2f m, water surface %.2f m, bed %.2f m, bank width %.0f m"
          % (shore_level, water_surface, bed, SHORE_WIDTH))
    zmin, zmax = float(out_z.min()), float(out_z.max())
    print("elevation in block: %.2f .. %.2f m" % (zmin, zmax))

    out_v = np.clip((out_z - zmin) / (zmax - zmin) * 65535.0, 0, 65535).astype(np.uint16)
    Image.fromarray(out_v, mode="I;16").save(os.path.join(OUT, "rdr2_sdn_height_1537.png"))
    Image.fromarray(np.clip((out_z - zmin) / (zmax - zmin) * 255.0, 0, 255).astype(np.uint8),
                    mode="L").save(os.path.join(OUT, "rdr2_sdn_height_preview.png"))
    print("height saved")

    for eng, key in ENGINE_LAYERS.items():
        out = np.zeros((PX, PX), dtype=np.uint8)
        if key:
            p = os.path.join(RDR2, "deliverable/terrain_weights", "w_%s.png" % key)
            src = np.array(Image.open(p), dtype=np.float32)
            out = np.clip(bilinear(src, fx[None, :], fy[:, None]), 0, 255).astype(np.uint8)
        elif eng == "default":
            out[:, :] = 255
        Image.fromarray(out, mode="L").save(os.path.join(OUT, "mask_%s.png" % eng))
        print("mask", eng, "->", key)

    params = {
        "center_m": [CENTER_X, CENTER_Y],
        "size_m": SIZE_M,
        "nodes": NODES,
        "node_size_m": NODE_SIZE_M,
        "height_px": [PX, PX],
        "z_min_m": zmin,
        "z_max_m": zmax,
        "water_surface_m": water_surface,
        "river_bed_m": bed,
        "shore_width_m": SHORE_WIDTH,
        "source": "continent_height_4m.png (4 m/px)",
        "note": "1:1 米的 Mission 场景地形；源图 4 米/像素，切出来只有 %d×%d 个真实采样点，其余为插值。"
                % (int(SIZE_M / res) + 1, int(SIZE_M / res) + 1),
    }
    with io.open(os.path.join(OUT, "terrain_sdn_meta.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(params, f, ensure_ascii=False, indent=1)
    print("meta saved; real samples in block: %d x %d" % (int(SIZE_M / res) + 1, int(SIZE_M / res) + 1))
    print("done ->", OUT)


if __name__ == "__main__":
    main()
