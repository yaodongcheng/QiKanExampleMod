#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""烘 RDR2 战役大地图地形（给 ModKit Terrain 面板导入）。

🔴 口径（2026-11-09 用户裁定；2026-11-08 改源）：
  - **只读 `deliverable/terrain/stages/01_plateau_plain/height_16bit_4m.png`**：
    01 里只保留「西北/东南地图外延扩展」，**02 的河床雕刻不要**。
  - **不做任何水面改造**：不垫陆地、不抬低地、不挖海床、不做岸坡 —— 高度 = 01 的纯重采样。
    2026-10-25 那套「B 方案」是临时实现，计划 2026-11-07 已标废弃，2026-11-09 从脚本删除。
  - 不掺 `raw/campaign/campaign_height.r16` —— 那份是 848² 低清、自带阶梯与斜纹，
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

高度口径：输出值 0..65535 ↔ Z_MIN/Z_MAX（−34.93 ~ 690.35 m；低于 −34.93 的海底截到 0）；
场景 min/max = 0 / 50 单位。
"""

import io
import json
import os
import numpy as np
from PIL import Image

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
OUT = os.path.join(r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\RD2构造相关\terrain")

# 大地图模式（三选一）：00_raw 底图 / 01_plateau_plain 平整 / 02_river_carve 刻河
STAGE = "01_plateau_plain"
# 地图框范围（与 stage meta 的 world 一致）
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

# 场景水位（米）：**只写进 meta 给场景侧参考，不再参与地形生成**。
# 2026-11-09 用户裁定：地形只做 01 阶段的纯重采样，水面怎么处理由场景单独决定。
WATER_LEVEL_M = -25.0

# 🔴 2026-11-09 用户裁定：**不做任何水域加深 / 低地抬升 / 岸线改造**。
# 大地图高度 = `01_plateau_plain` 的纯重采样（01 里只保留西北/东南「地图外延扩展」，
# 02 的河床雕刻不用）。旧版这里有一整套「B 方案」（把陆地垫到水位以上、水挖到 −30、
# 岸坡 45 m）——那是 2026-10-25 的临时实现，计划 2026-11-07 已标「旧实现废弃」，
# 但脚本一直没删；2026-11-09 彻底移除。水面怎么处理由场景侧单独决定，不在地形里做。

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

    stage_dir = os.path.join(RDR2, "deliverable/terrain/stages", STAGE)
    meta = json.load(io.open(os.path.join(stage_dir, "meta.json"), encoding="utf-8"))
    hm = meta["height"]
    src_zmin, src_zmax = hm["zmin"], hm["zmax"]

    src_img = np.array(Image.open(os.path.join(stage_dir, hm["file"])), dtype=np.uint16)
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

    # 🔴 2026-11-09：这里原本有「B 方案」水处理（陆地垫高 / 低地抬升 / 海床挖深 / 岸坡），
    # 已按用户裁定整体移除。高度 = 01_plateau_plain 的纯重采样，不做任何水面改造。

    # 🔴 自检：纯重采样 —— 任何水/陆地改造（垫陆地/抬低地/挖海床/岸坡）都会在这里失败。
    #    分块计算与整块计算可能有 1e-6 级浮点差异，所以用 0.1mm 容差，不要求逐位相等。
    ref = bilinear(src_z, sx_full[None, :], sy_full[:, None])
    diff = float(np.max(np.abs(out_z - ref)))
    if diff > 1e-4:
        raise SystemExit("FAIL: out_z was modified (max diff %.3f m) — pure resample only" % diff)
    print("pure resample check: max diff %.6f m" % diff)

    out_v = np.clip((out_z - Z_MIN) / (Z_MAX - Z_MIN) * 65535.0, 0, 65535).astype(np.uint16)
    Image.fromarray(out_v, mode="I;16").save(os.path.join(OUT, "rdr2_height_4097.png"))
    preview = np.clip((out_z - Z_MIN) / (Z_MAX - Z_MIN) * 255.0, 0, 255).astype(np.uint8)
    Image.fromarray(preview, mode="L").save(os.path.join(OUT, "rdr2_height_preview.png"))
    print("height saved; range m:", float(out_z.min()), float(out_z.max()))

    # 7 层权重（与高度图同一套重采样）
    weights = {}
    for key in LAYERS:
        p = os.path.join(RDR2, "deliverable/terrain/weights", "w_%s.png" % key)
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
        "water_rework": "none (2026-11-09: no land lift / no sea carve / no shore band)",
        "heightmap": "rdr2_height_4097.png",
        "note": "世界范围 = 解包高度图覆盖范围；地图是缩微沙盘，真实米 ÷ 14.5 = 地图单位。瓜玛岛不在此范围（独立 mission）。",
    }
    with io.open(os.path.join(OUT, "terrain_meta.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(params, f, ensure_ascii=False, indent=1)
    print("meta saved -> terrain_meta.json")
    print("done ->", OUT)


if __name__ == "__main__":
    main()
