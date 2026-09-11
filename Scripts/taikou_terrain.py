#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Taikou 地形采样（世界米 ↔ 高度图）

用途
----
1. `height_at(x, y)`   —— 据点 Z 贴合地形
2. `is_sea(x, y)`      —— 判「落海里」（水面 = 1.65m，见场景 terrain/water 配置）
3. `nearest_land(x, y)` —— 把落海的据点吸附到最近的可站陆地

数据源（按优先级）
------------------
1. `tools/ExportHeightMatMap/Output/hm_taikou_scene_export_4097x2561.png`
   = **实机编辑器导出的真地形**（2026-09-11 用户 ModKit 导出）：4097×2561 = 节点 16×10 × 256 顶点 + 1（官方规格），
   16bit 归一化（0..65535 = 0..max_height）。**权威源**。
2. `tools/ExportHeightMatMap/Output/hm_japanmap_hires_4096x2560_16bit.png`
   = 管线生成的导入素材（近似；导出缺失时兜底）。

米制换算：h = 值 / 65535 × max_height（场景 terrain 节点 max_height = 19.136，min = 0）；海 = 值 0。
世界↔像素：px = x/2048×(W−1)，py = (1 − y/1280)×(H−1)（世界 y 向北，图 row0 = 北 —— 已与陆地形状对照验证）。
"""
import io
import os

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None

WORLD_W, WORLD_H = 2048.0, 1280.0
WATER_LEVEL = 1.80          # 🔴 水面高度（米）——2026-09-11 用户实测报值（此前误用旧记录的 1.65，
                            #    导致落点停在 1.66~1.75 = 仍在 1.8 水面下，48 个据点泡海）
SAFE_LAND_MARGIN = 0.80     # 落海据点吸附目标：高出水面至少这么多（2.6m；够干地即可，别推远）
INLAND_MIN_M = 3.0          # 🔴 吸附后离海岸线至少这么远（米）。
                            #    2026-09-11 用户裁定：25m / 10m 都过头 → **3m**，判据是「模型整体在陆地」
                            #    （模型半径：塔 0.82m / 村 1.35m，3m 足以让整个模型不压水面）
SNAP_IF_INLAND_BELOW_M = 3.0  # 🔴 只有「离海岸线 < 这个值」的据点才动；其余保持仿射原位，不做任何推移
MAX_HEIGHT = 19.136         # 场景 terrain 节点 max_height
_OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                    "tools", "ExportHeightMatMap", "Output")
DEFAULT_PNG = os.path.join(_OUT, "hm_taikou_scene_export_4097x2561.png")     # 权威：实机导出
FALLBACK_PNG = os.path.join(_OUT, "hm_japanmap_hires_4096x2560_16bit.png")   # 兜底：导入素材


class Terrain:
    def __init__(self, png=None, max_height=MAX_HEIGHT):
        if png is None:
            png = DEFAULT_PNG if os.path.exists(DEFAULT_PNG) else FALLBACK_PNG
        im = Image.open(png)
        a = np.asarray(im).astype(np.float32)
        self.h = a / float(np.iinfo(a.dtype).max if a.dtype != np.float32 else 65535) * max_height \
            if a.max() > 255 else a / 255.0 * max_height
        self.H, self.W = self.h.shape
        self.land = self.h > WATER_LEVEL
        self.max_height = max_height
        # 离海距离场（米）：每个陆地像素到最近海面的欧氏距离 → 「靠里一点」的判据
        from scipy import ndimage
        self.inland_m = ndimage.distance_transform_edt(self.land).astype(np.float32) * 0.5   # 1px = 0.5m

    # ---- 坐标换算：世界米 → 像素（世界 y 向北，图 row0 = 北）----
    def _px(self, x, y):
        return (x / WORLD_W * self.W, (1.0 - y / WORLD_H) * self.H)

    def height_at(self, x, y):
        px, py = self._px(x, y)
        xi = int(round(min(max(px, 0), self.W - 1)))
        yi = int(round(min(max(py, 0), self.H - 1)))
        return float(self.h[yi, xi])

    def is_sea(self, x, y):
        return self.height_at(x, y) <= WATER_LEVEL

    def inland_dist(self, x, y):
        """该点离海岸线的距离（米）；海上 = 0"""
        px, py = self._px(x, y)
        xi = int(round(min(max(px, 0), self.W - 1)))
        yi = int(round(min(max(py, 0), self.H - 1)))
        return float(self.inland_m[yi, xi])

    def nearest_land(self, x, y, max_radius_m=120.0, prefer_max_h=12.0, safe=True,
                     min_inland_m=None, avoid=(), min_sep_m=0.0):
        """找最近的可站陆地：逐圈扩半径找「干地」。

        safe=True（默认）：
          · 高度必须高出水面 SAFE_LAND_MARGIN（不会又贴回水线）
          · 离海岸线至少 min_inland_m（默认 INLAND_MIN_M；0 = 不要求）——「靠里一点」
        半径内找不到达标的，退一步只要求「高出水面 0.3m」并在返回值里标 relax。
        优先取高度 ≤ prefer_max_h 的点（平地/海岸，别吸到山顶）。
        返回 (nx, ny, 移动距离m, 新高度, 是否放宽) 或 None（半径内无陆地）。
        """
        if min_inland_m is None:
            min_inland_m = INLAND_MIN_M
        px, py = self._px(x, y)
        r_max = int(max_radius_m / 0.5)                     # 1px = 0.5m
        H, W = self.H, self.W
        # 分级放宽（逐级降低要求，取第一个有解的一级）：
        #   ① 高 ≥3.0 且 离海 ≥25m（理想） ② 高 ≥2.6 且 离海 ≥25m（低地半岛：如松前）
        #   ③ 高 ≥3.0（只保高度） ④ 高 ≥2.1（保命，仍在安全高度之上）
        tiers = ([(WATER_LEVEL + SAFE_LAND_MARGIN, min_inland_m),
                  (WATER_LEVEL + 0.8, min_inland_m),
                  (WATER_LEVEL + SAFE_LAND_MARGIN, 0.0),
                  (WATER_LEVEL + 0.3, 0.0)] if safe else [(WATER_LEVEL + 0.3, 0.0)])
        best, used_tier = None, 0
        avoid_arr = np.asarray(avoid, dtype=np.float32).reshape(-1, 2) if len(avoid) else None
        # 分离约束逐级放宽：避免多个据点吸到同一个像素上叠成一堆（2026-09-11 实测 24 组重合）
        seps = [min_sep_m, min_sep_m * 0.6, min_sep_m * 0.3, 0.0] if min_sep_m > 0 else [0.0]
        for ti, (h_min, need_inland) in enumerate(tiers):
            for sep in seps:
                for r in range(1, r_max + 1):
                    y0, y1 = max(0, int(py) - r), min(H - 1, int(py) + r)
                    x0, x1 = max(0, int(px) - r), min(W - 1, int(px) + r)
                    if y0 > y1 or x0 > x1:
                        break
                    ring = []
                    for yy in range(y0, y1 + 1):
                        for xx in (x0, x1):
                            ring.append((yy, xx))
                    for xx in range(x0 + 1, x1):
                        for yy in (y0, y1):
                            ring.append((yy, xx))
                    for yy, xx in ring:
                        if not self.land[yy, xx]:
                            continue
                        hh = float(self.h[yy, xx])
                        if hh < h_min or hh > prefer_max_h:
                            continue                        # 太贴水线 / 山顶都不要
                        if self.inland_m[yy, xx] < need_inland:
                            continue                        # 离海太近 → 继续往里找
                        if avoid_arr is not None and sep > 0:
                            wx_ = xx / self.W * WORLD_W
                            wy_ = (1.0 - yy / self.H) * WORLD_H
                            dd = np.min(np.hypot(avoid_arr[:, 0] - wx_, avoid_arr[:, 1] - wy_))
                            if dd < sep:
                                continue                    # 与已有据点太近 → 换一个落点
                        d = ((xx - px) * 0.5) ** 2 + ((yy - py) * 0.5) ** 2
                        if best is None or d < best[0]:
                            best = (d, xx, yy, hh)
                    if best is not None:
                        break
                if best is not None:
                    break
            if best is not None:
                used_tier = ti
                break
        if best is None:
            return None
        d, xx, yy, hh = best
        nx = xx / self.W * WORLD_W
        ny = (1.0 - yy / self.H) * WORLD_H
        return nx, ny, d ** 0.5, hh, used_tier >= 2


if __name__ == "__main__":
    t = Terrain()
    print("高度图 %dx%d  陆地占比 %.1f%%  高度域 %.2f..%.2f m"
          % (t.W, t.H, 100 * t.land.mean(), t.h.min(), t.h.max()))
    for nm, (x, y) in (("京", (969, 436)), ("江户", (1441, 394)), ("松前", (1789, 1031)),
                       ("博多", (356, 447)), ("鹿儿岛", (265, 213)), ("远海", (300, 1100))):
        h = t.height_at(x, y)
        print("  %-6s (%.0f,%.0f) 高度 %.2f m %s" % (nm, x, y, h, "← 海" if h <= WATER_LEVEL else ""))
