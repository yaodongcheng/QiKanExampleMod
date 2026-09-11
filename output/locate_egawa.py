# -*- coding: utf-8 -*-
"""江川之砦 方位 + 全表坐标重合检查。"""
import collections
import csv
import io
import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Scripts"))
from taikou_terrain import Terrain, WORLD_W, WORLD_H

t = Terrain()
rows = list(csv.DictReader(io.open(
    os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                 "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "Settlements.csv"),
    encoding="utf-8-sig")))
me = next(r for r in rows if r["id"] == "castle_tk272")
mx, my = float(me["MOD_X"]), float(me["MOD_Y"])
print("江川之砦 castle_tk272")
print("  太阁坐标 TK5 = (%s, %s)   （全表最西的几个之一：x 越小越偏西）" % (me["TK5_X"], me["TK5_Y"]))
print("  mod 世界坐标 = (%.1f, %.1f)   地图 2048 x 1280" % (mx, my))
print("  地形高度 %.2f m，离海岸线 %.0f m；距地图左边缘 %.0f m、下边缘 %.0f m"
      % (t.height_at(mx, my), t.inland_dist(mx, my), mx, my))
print("  最近据点：")
ds = sorted(((math.hypot(float(r["MOD_X"]) - mx, float(r["MOD_Y"]) - my), r["id"], r["Name_All"])
             for r in rows if r["id"] != "castle_tk272"), key=lambda z: z[0])
for d, sid, nm in ds[:6]:
    print("     %-14s %-14s %.0f m" % (sid, nm, d))

pos = collections.Counter((r["MOD_X"], r["MOD_Y"]) for r in rows)
dup = [(k, v) for k, v in pos.items() if v > 1]
print("\n坐标完全重合的据点组: %d 组，涉及 %d 个据点" % (len(dup), sum(v for _k, v in dup)))
for k, v in sorted(dup, key=lambda z: -z[1])[:8]:
    ids = ["%s/%s" % (r["id"], r["Name_All"]) for r in rows if (r["MOD_X"], r["MOD_Y"]) == k]
    print("   (%s) ×%d: %s" % (k[0] + "," + k[1], v, " | ".join(ids)))
near = [(math.hypot(float(a["MOD_X"]) - float(b["MOD_X"]), float(a["MOD_Y"]) - float(b["MOD_Y"])), a["id"], b["id"])
        for i, a in enumerate(rows) for b in rows[i + 1:]
        if 0 < math.hypot(float(a["MOD_X"]) - float(b["MOD_X"]), float(a["MOD_Y"]) - float(b["MOD_Y"])) < 25]
print("\n相互距离 <25m 的据点对: %d 对" % len(near))
for d, a, b in sorted(near)[:8]:
    print("   %-14s ↔ %-14s %.1f m" % (a, b, d))
