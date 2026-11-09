#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""从解包高度图切出圣丹尼斯场景地形（给 ModKit 场景 Terrain 面板导入）。

背景：
  - 大地图用的是**整张**高度图（16×12 节点，见 gen_rdr2_terrain.py）。
  - 进城的 Mission 场景是 1:1 米、独立的场景地形，必须从同一张源图**切一块**再重采样。

口径：
  - 源 = `deliverable/terrain/scene_native/height_4m_16bit_native.png`（4 米/像素，100% 原生 H4；2026-11-08 由 terrain/final 改名）。
  - 场景 rect = blend 的真实 far_rect x[512,3840] y[-3072,896] = 3328×3968 m；
    为凑正方形节点（一个 node_size），取 **13×15 @256 = 3328×3840 m**：宽度正好，
    只裁掉 far_rect 北边 128 m（3.2%），城区活动区 884.8×587.9 m 完整保留。
  - 高度图 3329 × 3841（13/15 × 256 + 1），z 采用**绝对米**（import offset = zmin）。
  - 🔴 2026-11-09 用户裁定：**不做任何水面/河床改造**，地形 = scene_native 的纯重采样。

输出（Debug/RD2构造相关/terrain_sdn/）：高度图、7 层 mask、颜色参考、meta。
"""

import io
import json
import os
import numpy as np
from PIL import Image

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
LWN = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
OUT = os.path.join(LWN, "Debug", "RD2构造相关", "terrain_sdn")

# 🔴 2026-11-09 改口径：按 blend 的真实场景 rect 做，不再用 1500×1500 试点范围。
#   真实 far_rect（scene_report_sdn.json）= x[512,3840] × y[-3072,896] = 3328 × 3968 m。
#   引擎节点是正方形（一个 node_size），3328×3968 不能整除；取 13×15 @256 = 3328×3840：
#   宽度正好，只裁掉 far_rect 北边 128 m（3.2%），活动区 884.8×587.9 m 完整保留。
X0, X1 = 512.0, 3840.0
Y0, Y1 = -3072.0, 768.0
NODES_X, NODES_Y = 13, 15
NODE_SIZE_M = 256.0
PIX_PER_NODE = 256
PX = NODES_X * PIX_PER_NODE + 1          # 3329
PY = NODES_Y * PIX_PER_NODE + 1          # 3841
SIZE_X, SIZE_Y = X1 - X0, Y1 - Y0        # 3328 x 3840
ACTIVITY_RECT = (2150.8, -1579.0, 3035.6, -991.1)   # 城区活动区（必须完整保留）

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

    meta = json.load(io.open(os.path.join(RDR2, "deliverable/terrain/scene_native/meta.json"), encoding="utf-8"))
    hm, world = meta["height"], meta["world"]
    sx0, sy1, res = world["x0"], world["y1"], hm["m_per_px"]
    src_z = np.array(Image.open(os.path.join(RDR2, "deliverable/terrain/scene_native", hm["file"])), dtype=np.float32)
    src_z = hm["zmin"] + src_z / 65535.0 * (hm["zmax"] - hm["zmin"])

    x0, x1 = X0, X1
    y0, y1 = Y0, Y1
    print("sdn rect (%.1f, %.1f) -> x[%.1f,%.1f] y[%.1f,%.1f]  %d x %d nodes @%.0f m = %.0f x %.0f m"
          % (ACTIVITY_RECT[0], ACTIVITY_RECT[1], x0, x1, y0, y1,
             NODES_X, NODES_Y, NODE_SIZE_M, SIZE_X, SIZE_Y))
    print("activity rect must stay inside: %s" % (ACTIVITY_RECT,))

    # 行 0 = 北 ⇒ 第一行对应 y1
    xs = np.linspace(x0, x1, PX, dtype=np.float32)
    ys = np.linspace(y1, y0, PY, dtype=np.float32)
    fx = (xs - sx0) / res
    fy = (sy1 - ys) / res
    out_z = bilinear(src_z, fx[None, :], fy[:, None])
    # 🔴 2026-11-09 用户裁定：**不做任何水面/河床改造**（不重建水面、不挖河床、不清水塘）。
    # 地形 = scene_native 原生 H4 的纯重采样；水面由场景侧单独决定。
    zmin, zmax = float(out_z.min()), float(out_z.max())
    print("elevation in block: %.2f .. %.2f m" % (zmin, zmax))

    # 🔴 自检：纯重采样 —— 禁止水/河床改造（旧 harmonic fill / 河床下切已删）
    ref = bilinear(src_z, fx[None, :], fy[:, None])
    if not np.array_equal(out_z, ref):
        raise SystemExit("FAIL: out_z was modified — pure resample only (no water/river rework)")

    out_v = np.clip((out_z - zmin) / (zmax - zmin) * 65535.0, 0, 65535).astype(np.uint16)
    Image.fromarray(out_v, mode="I;16").save(os.path.join(OUT, "rdr2_sdn_height_%dx%d.png" % (PX, PY)))
    Image.fromarray(np.clip((out_z - zmin) / (zmax - zmin) * 255.0, 0, 255).astype(np.uint8),
                    mode="L").save(os.path.join(OUT, "rdr2_sdn_height_preview.png"))
    print("height saved")

    for eng, key in ENGINE_LAYERS.items():
        out = np.zeros((PY, PX), dtype=np.uint8)
        if key:
            p = os.path.join(RDR2, "deliverable/terrain/weights", "w_%s.png" % key)
            src = np.array(Image.open(p), dtype=np.float32)
            out = np.clip(bilinear(src, fx[None, :], fy[:, None]), 0, 255).astype(np.uint8)
        elif eng == "default":
            out[:, :] = 255
        Image.fromarray(out, mode="L").save(os.path.join(OUT, "mask_%s.png" % eng))
        print("mask", eng, "->", key)

    params = {
        "rect_m": [X0, Y0, X1, Y1],
        "size_m": [SIZE_X, SIZE_Y],
        "activity_rect_m": list(ACTIVITY_RECT),
        "nodes": [NODES_X, NODES_Y],
        "node_size_m": NODE_SIZE_M,
        "height_px": [PX, PY],
        "z_min_m": zmin,
        "z_max_m": zmax,
        "import_scale": zmax - zmin,
        "import_offset": zmin,
        "water_rework": "none (2026-11-09: no water rebuild / no river carve)",
        "source": "scene_native/height_4m_16bit_native.png (4 m/px)",
        "note": "1:1 米 Mission 场景；rect 取 blend far_rect，北边裁 128 m 以凑 13x15@256 正方形节点；"
                "活动区 884.8x587.9 m 完整保留。源图 4 米/像素，切出来 %d x %d 个真实采样点，其余为插值。"
                % (int(SIZE_X / res) + 1, int(SIZE_Y / res) + 1),
    }
    with io.open(os.path.join(OUT, "terrain_sdn_meta.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(params, f, ensure_ascii=False, indent=1)
    print("meta saved; real samples in block: %d x %d"
          % (int(SIZE_X / res) + 1, int(SIZE_Y / res) + 1))
    print("done ->", OUT)


if __name__ == "__main__":
    main()
