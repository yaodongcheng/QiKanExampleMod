# -*- coding: utf-8 -*-
"""本轮位移统计：多少据点被移动、移动量分布、离海距离、水面以下数量。"""
import csv
import io
import math
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "Scripts"))
from taikou_terrain import Terrain, WATER_LEVEL

t = Terrain()
rows = list(csv.DictReader(io.open(
    os.path.join(ROOT, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "Settlements.csv"),
    encoding="utf-8-sig")))

# 重算「仿射原位」用于对比（读参数）
import json
tr = json.load(io.open(os.path.join(ROOT, "Knowledge", "太阁5", "骑砍2织丰角色ID对应",
                                    "_analysis", "mod_coord_transform.json"), encoding="utf-8"))
a = tr["affine"]


def aff(tx, ty):
    return a["m11"] * tx + a["m12"] * ty + a["tx"], a["m21"] * tx + a["m22"] * ty + a["ty"]


moves, under, near = [], 0, 0
for r in rows:
    tx, ty = float(r["TK5_X"]), float(r["TK5_Y"])
    ax, ay = aff(tx, ty)
    x, y = float(r["MOD_X"]), float(r["MOD_Y"])
    d = math.hypot(x - ax, y - ay)
    if d > 0.5:
        moves.append((d, r["id"], r["Name_All"]))
    if t.height_at(x, y) <= WATER_LEVEL:
        under += 1
    if t.inland_dist(x, y) < 3.0:
        near += 1

moves.sort(reverse=True)
print("被移动的据点 %d / %d" % (len(moves), len(rows)))
if moves:
    ds = [m[0] for m in moves]
    print("  位移：最大 %.0fm  中位 %.0fm  最小 %.0fm  合计 %.0f km"
          % (max(ds), sorted(ds)[len(ds) // 2], min(ds), sum(ds) / 1000))
    print("  位移最大的 8 个：")
    for d, sid, nm in moves[:8]:
        print("     %-14s %-14s %.0f m" % (sid, nm, d))
print("水面(%.1fm)以下: %d 个   离海岸线 <3m: %d 个" % (WATER_LEVEL, under, near))
