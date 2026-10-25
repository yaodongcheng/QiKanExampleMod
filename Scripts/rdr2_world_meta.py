#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""世界范围 / 地形规格的唯一来源 —— 读 gen_rdr2_terrain.py 产出的 terrain_meta.json。

🔴 规矩：脚本里禁止手抄 848 / 14.5 / min_height 之类的数值，一律走这里。
   （数值随地形烘焙变，手抄必然过期 —— 2026-10-25 就是因为写死 848 与解包图范围对不上。）
"""

import io
import json
import os

MODULES = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules"
LWN = os.path.join(MODULES, "LivingWorldNpcs")
META_PATH = os.path.join(LWN, "Debug", "RD2构造相关", "terrain", "terrain_meta.json")

_cache = None


def load():
    global _cache
    if _cache is None:
        with io.open(META_PATH, encoding="utf-8") as f:
            _cache = json.load(f)
    return _cache


def world_uv(x, y):
    """世界米 (x, y) → 大地图单位 (u, v)：按解包图范围直接归一到地形范围。"""
    m = load()
    s = m["src"]
    return ((x - s["x0"]) / s["w_m"] * m["map_w_units"],
            (s["y1"] - y) / s["h_m"] * m["map_h_units"])


def inside(x, y):
    """是否落在地形覆盖范围内（瓜玛岛等在图外 = False）。"""
    m = load()
    s = m["src"]
    return (s["x0"] <= x <= s["x0"] + s["w_m"]) and ((s["y1"] - s["h_m"]) <= y <= s["y1"])


def map_w():
    return load()["map_w_units"]


def map_h():
    return load()["map_h_units"]


def min_height():
    return load()["min_height_units"]


def max_height():
    return load()["max_height_units"]


def nodes():
    m = load()
    return m["nodes_x"], m["nodes_y"], m["node_size"]


def water_level_units():
    """全局水位（场景单位）。骑砍大地图只有一个海平面。"""
    return load().get("water_level_units", 0.0)
