#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""烘 RDR2 战役大地图地形（给 ModKit Terrain 面板导入）。

🔴 口径（2026-10-25 用户裁定）：
  - **只用解包高度图这一份数据**（`deliverable/heightmap/continent_height_4m.png`）。
    不掺 `raw/campaign/campaign_height.r16` —— 那份是 848² 低清、自带阶梯与斜纹，
    掺进来会在解包图边界留下大方框接缝（上一版就是这么废掉的）。
  - **世界范围 = 解包图覆盖的真实范围**，按地图比例换算成地图单位：
    10,768 × 8,024 米 ÷ 14.5 = **742.62 × 553.38 单位**。
    大地图是缩微沙盘，不是 1:1 —— 真实米数不能直接当世界尺寸。
  - **图不补边、不裁**：地形长宽比按 16 × 12 节点（差 0.65%，肉眼不可见）。
  - 瓜玛岛不在这张图里（按用户裁定走独立跳转 mission），所以不需要补外圈。

输出（Debug/RD2构造相关/terrain/）：
  rdr2_height_4097.png        16bit 灰度；4097×3073；对应 16×12 节点 × 256 + 1
  rdr2_height_preview.png     8bit 灰度预览
  mask_default.png            ← RDR2 grass
  mask_flora_forest.png       ← RDR2 conifer
  mask_rock.png               ← RDR2 rock
  mask_grass.png              ← RDR2 broadleaf
  mask_dirt.png               ← RDR2 dirt
  mask_river.png              ← RDR2 water
  mask_snow.png               ← RDR2 snow
  mask_dirt2.png / mask_forest2.png（全 0，占位）
  rdr2_color_ref_2048.png     颜色参考（不导入，只对照）
  terrain_meta.json           世界范围 / 节点 / min-max（下游脚本读它，禁止手抄）

高度口径：输出值 0..65535 ↔ 米 [-34.931, 690.347]（解包高度图自己的范围）；场景 min/max = 米 ÷ 14.5。
"""

import io
import json
import os
import numpy as np
from PIL import Image
from scipy import ndimage

# 河岸过渡用圣丹尼斯那套现成的调和插值，别抄第二份
from gen_rdr2_sdn_terrain import smooth_fill

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
OUT = os.path.join(r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\RD2构造相关\terrain")

# 解包高度图覆盖的真实范围（continent_height_meta.json）
SRC_X0, SRC_Y1 = -6844.0, 3224.0
SRC_W_M, SRC_H_M = 10768.0, 8024.0
SRC_RES = 4.0

SCALE = 14.5                 # 真实米 → 地图单位（大地图是缩微沙盘）
NODES_X, NODES_Y = 16, 12    # 长宽比 1.333，与解包图 2692:2006 = 1.342 差 0.65%
NODE_SIZE = 48.0             # 🔴 用户 2026-10-25 在 ModKit 里实际填的值（不是 46.4138）
PIX_PER_NODE = 256           # 与主地图同口径：4097 = 16×256+1
W, H = NODES_X * PIX_PER_NODE + 1, NODES_Y * PIX_PER_NODE + 1

MAP_W_U = NODES_X * NODE_SIZE     # 768
MAP_H_U = NODES_Y * NODE_SIZE     # 576
# ModKit 只问 scale/offset（地形底面固定 0）：这张图满量程映射到 0..VERTICAL_UNITS
VERTICAL_UNITS = 50.0

Z_MIN, Z_MAX = -34.93079376220703, 690.3469848632812

# 🔴 全局水位（2026-10-25 用户裁定 B 方案）：骑砍大地图只有**一个**海平面。
# RDR2 的西南盆地底在 −25 米左右 ⇒ 水位只要高过它就会淹掉沙漠。
# 解法：把水位放到盆地底以下（−25 米），海/大湖挖到 −30 米（低于水位才有真水面），
# 陆地**不做任何压平**，保留原本起伏；只有低于水位的极少量陆地会被垫到水位以上。
WATER_LEVEL_M = -25.0        # 全局水位（米）
LAND_FLOOR_M = -24.5         # 陆地最低高度（米）= 水位 + 0.5，只影响水位以下的极深点
SEA_CARVE_MAX_M = 25.0       # 低于这个高程的水体算"海/大河"，统一挖到海床
SEA_BED_M = -30.0            # 海床高度（米），必须低于水位、且 ≥ Z_MIN
# 低地平滑抬升：把西南盆地从"低于水面"抬上来（否则水位一开就淹成湖）
LIFT_M = 10.0                # 最大抬升（米）：只作用在低地
LIFT_Z_FULL = -15.0          # 高程 ≤ 这个值 → 抬满
LIFT_Z_ZERO = 15.0           # 高程 ≥ 这个值 → 不抬
LAND_BANK_M = 45.0           # 岸坡宽度（米）：把水 mask 外侧的陆地按斜坡降到水面
DEEP_WIDTH_M = 90.0          # 水下坡宽度（米）：水面 → 海床（这一段在水里，不影响水面面积）
SHORE_ITERS = 120            # 调和插值的 Jacobi 松弛次数（次数×2.6 米 ≈ 边界影响半径）

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
    src_zmin, src_zmax = hm["zmin"], hm["zmax"]

    src_img = np.array(Image.open(os.path.join(RDR2, "deliverable/heightmap", hm["file_16bit"])), dtype=np.uint16)
    src_z = (src_zmin + src_img.astype(np.float32) / 65535.0 * (src_zmax - src_zmin)).astype(np.float32)
    print("source height:", src_z.shape, src_zmin, src_zmax)
    print("world (map units): %.2f x %.2f | node %dx%d @ %.4f | out %dx%d"
          % (MAP_W_U, MAP_H_U, NODES_X, NODES_Y, NODE_SIZE, W, H))

    # 纯重采样：整张解包图铺满整个地形范围，不补边、不裁
    sx_full = np.linspace(0.0, src_z.shape[1] - 1, W, dtype=np.float32)
    sy_full = np.linspace(0.0, src_z.shape[0] - 1, H, dtype=np.float32)
    out_z = np.empty((H, W), dtype=np.float32)
    for y0 in range(0, H, 128):
        y1 = min(H, y0 + 128)
        out_z[y0:y1] = bilinear(src_z, sx_full[None, :], sy_full[y0:y1][:, None])
        print("height rows", y1, "/", H)

    # 🔴 B 方案：全局单水位下把陆地垫起来、只挖真正的海/大河
    wsrc = np.array(Image.open(os.path.join(RDR2, "deliverable/terrain_weights", "w_water.png")), dtype=np.float32)
    water = np.zeros((H, W), dtype=bool)
    for y0 in range(0, H, 256):
        y1 = min(H, y0 + 256)
        water[y0:y1] = bilinear(wsrc, sx_full[None, :], sy_full[y0:y1][:, None]) > 127

    low_land = (~water) & (out_z < LAND_FLOOR_M)
    n_land = int(low_land.sum())
    out_z[~water] = np.maximum(out_z[~water], LAND_FLOOR_M)

    # 🔴 低地平滑抬升（2026-10-25 用户要求）：西南盆地比水面还低 ⇒ 水面一开就把整片沙漠淹成湖。
    # 按**高度**做平滑抬升（低处抬得多、高处为 0），而不是按东南西北乘系数 ——
    # 乘系数会把西南的山一起拔高，按高度则只动真正低的地方，且过渡连续。
    t_lift = np.clip((out_z - LIFT_Z_FULL) / (LIFT_Z_ZERO - LIFT_Z_FULL), 0.0, 1.0)
    w_lift = 1.0 - t_lift * t_lift * (3.0 - 2.0 * t_lift)     # z ≤ LIFT_Z_FULL → 1；z ≥ LIFT_Z_ZERO → 0
    out_z[~water] += (LIFT_M * w_lift)[~water]
    print("  basin lift: up to %.1f m (full below %.0f m, zero above %.0f m); land min now %.2f m"
          % (LIFT_M, LIFT_Z_FULL, LIFT_Z_ZERO, float(out_z[~water].min())))

    # 🔴 岸线处理（2026-10-25 用户裁定：**水 mask 是权威，水面不能被岸坡吃掉**）
    #   ① 水区：整片压到水面以下 —— 边界处 = 水面，向内 DEEP_WIDTH_M 米降到海床（这才叫"刻深"）；
    #   ② 岸：做在**水 mask 外面的陆地**上 —— 临水 LAND_BANK_M 米内把地"只降不抬"降到水面，
    #      走出这条带就回到原始地形。
    #   这样水面面积 = 水体掩膜 100% 保留，岸边是连续斜坡而不是垂直悬崖。
    carve = water & (out_z < SEA_CARVE_MAX_M)
    n_carve = int(carve.sum())
    if n_carve:
        mpp = SRC_W_M / (W - 1)
        # ① 水下：边界 = 水面，向内降到海床
        d_in = ndimage.distance_transform_edt(carve) * mpp
        t2 = np.clip(d_in / DEEP_WIDTH_M, 0.0, 1.0)
        t2 = t2 * t2 * (3.0 - 2.0 * t2)
        z_water = WATER_LEVEL_M * (1.0 - t2) + SEA_BED_M * t2
        # ② 岸：水外的陆地条带，从水线斜坡回到原始地形（只降不抬）
        d_out = ndimage.distance_transform_edt(~carve) * mpp
        near = (~carve) & (d_out <= LAND_BANK_M)
        t1 = np.clip(d_out / LAND_BANK_M, 0.0, 1.0)
        t1 = t1 * t1 * (3.0 - 2.0 * t1)
        ramp = WATER_LEVEL_M + (out_z - WATER_LEVEL_M) * t1
        lowered = np.minimum(out_z, ramp)

        src_before = out_z[near].copy()
        out_z = np.where(carve, z_water, np.where(near, lowered, out_z))
        n_near = int(near.sum())
        d_lower = float((src_before - out_z[near]).mean()) if n_near else 0.0
        print("  shore band: %d px land lowered (mean %.2f m, %.2f%% of land)"
              % (n_near, d_lower, n_near / max(int((~water).sum()), 1) * 100))

    out_v = np.clip((out_z - Z_MIN) / (Z_MAX - Z_MIN) * 65535.0, 0, 65535).astype(np.uint16)
    Image.fromarray(out_v, mode="I;16").save(os.path.join(OUT, "rdr2_height_4097.png"))
    preview = np.clip((out_z - Z_MIN) / (Z_MAX - Z_MIN) * 255.0, 0, 255).astype(np.uint8)
    Image.fromarray(preview, mode="L").save(os.path.join(OUT, "rdr2_height_preview.png"))
    print("height saved; range m:", float(out_z.min()), float(out_z.max()))

    # 7 层权重（与高度图同一套重采样）
    weights = {}
    for key in LAYERS:
        p = os.path.join(RDR2, "deliverable/terrain_weights", "w_%s.png" % key)
        if not os.path.isfile(p):
            print("  [warn] missing weight", key)
            continue
        weights[key] = np.array(Image.open(p), dtype=np.uint8)
        print("weight", key, weights[key].shape)

    for eng, src_key in ENGINE_LAYERS.items():
        out = np.zeros((H, W), dtype=np.uint8)
        if src_key and src_key in weights:
            src = weights[src_key].astype(np.float32)
            sx = np.linspace(0.0, src.shape[1] - 1, W, dtype=np.float32)
            sy = np.linspace(0.0, src.shape[0] - 1, H, dtype=np.float32)
            for y0 in range(0, H, 256):
                y1 = min(H, y0 + 256)
                v = bilinear(src, sx[None, :], sy[y0:y1][:, None])
                out[y0:y1] = np.clip(v, 0, 255).astype(np.uint8)
        elif eng == "default":
            out[:, :] = 255
        Image.fromarray(out, mode="L").save(os.path.join(OUT, "mask_%s.png" % eng))
        print("mask", eng, "->", src_key)

    # 雪没有独立图层（原版用 rock 的灰白岩石表现雪山）→ 把 snow 权重并进 rock
    rock = np.array(Image.open(os.path.join(OUT, "mask_rock.png")), dtype=np.float32)
    snow = np.array(Image.open(os.path.join(OUT, "mask_snow.png")), dtype=np.float32)
    Image.fromarray(np.clip(rock + snow, 0, 255).astype(np.uint8), mode="L").save(
        os.path.join(OUT, "mask_rock_snow.png"))
    print("mask rock_snow -> rock + snow")

    # 颜色参考（同一范围，只对照不导入）
    color_path = os.path.join(RDR2, "export/world/world_color_4096.png")
    if os.path.isfile(color_path):
        im = Image.open(color_path).convert("RGB")
        mpp = 12296.0 / im.size[0]
        ox, oy = -7684.0, -8196.0
        box = (int((SRC_X0 - ox) / mpp), int((oy + 12296 - SRC_Y1) / mpp),
               int((SRC_X0 + SRC_W_M - ox) / mpp), int((oy + 12296 - (SRC_Y1 - SRC_H_M)) / mpp))
        im.crop(box).resize((1024, int(1024 * SRC_H_M / SRC_W_M)), Image.LANCZOS).save(
            os.path.join(OUT, "rdr2_color_ref_2048.png"))
        print("color ref saved", box)

    # 下游脚本读这个，禁止手抄数值
    params = {
        "src": {"x0": SRC_X0, "y1": SRC_Y1, "w_m": SRC_W_M, "h_m": SRC_H_M, "res_m": SRC_RES},
        "scale_m_per_unit": SCALE,
        "map_w_units": MAP_W_U,
        "map_h_units": MAP_H_U,
        "nodes_x": NODES_X,
        "nodes_y": NODES_Y,
        "node_size": NODE_SIZE,
        "height_px": [W, H],
        "z_min_m": Z_MIN,
        "z_max_m": Z_MAX,
        # ModKit 的 Import Heightmap 只有 scale / offset，地形底面固定为 0（没有负高度）。
        # 满量程 0..65535 → 0..VERTICAL_UNITS 场景单位。
        "min_height_units": 0.0,
        "max_height_units": VERTICAL_UNITS,
        "import_scale": VERTICAL_UNITS,
        "import_offset": 0.0,
        "water_level_m": WATER_LEVEL_M,
        "water_level_units": (WATER_LEVEL_M - Z_MIN) / (Z_MAX - Z_MIN) * VERTICAL_UNITS,
        "land_floor_m": LAND_FLOOR_M,
        "sea_bed_m": SEA_BED_M,
        "heightmap": "rdr2_height_4097.png",
        "note": "世界范围 = 解包高度图覆盖范围；地图是缩微沙盘，真实米 ÷ 14.5 = 地图单位。瓜玛岛不在此范围（独立 mission）。",
    }
    with io.open(os.path.join(OUT, "terrain_meta.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(params, f, ensure_ascii=False, indent=1)
    print("meta saved -> terrain_meta.json")
    print("done ->", OUT)


if __name__ == "__main__":
    main()
