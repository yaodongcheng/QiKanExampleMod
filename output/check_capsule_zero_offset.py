# -*- coding: utf-8 -*-
"""核验：capsule 原点 = mesh 原点（子实体偏移 0）且 capsule 落在地形高度 + 0.3。"""
import io
import re
import sys
import os

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Scripts"))
from taikou_terrain import Terrain

t = Terrain()
f = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord"
     r"\Modules\Taikou\SceneObj\Main_map\scene.xscene")
s = io.open(f, encoding="utf-8-sig").read()

pat = re.compile(
    r'campaign_icon_capsule_\d+_([a-z0-9_]+)" old_prefab_name="" mobility="1">\s*'
    r'<transform position="([-\d.]+), ([-\d.]+), ([-\d.]+)"[^>]*/>.*?'
    r'<game_entity name="\1" old_prefab_name="" mobility="1">\s*<tags>.*?</tags>\s*'
    r'<transform position="([-\d.]+), ([-\d.]+), ([-\d.]+)"', re.S)

print("%-14s %-16s %-9s %-10s %-9s %-8s" % ("据点", "capsule(x,y)", "capsuleZ", "child偏移Z", "地形高度", "离地"))
n = n_ok = 0
worst = []
for m in pat.finditer(s):
    sid = m.group(1)
    x, y, cz, cx, cy, chz = (float(m.group(i)) for i in range(2, 8))
    h = t.height_at(x, y)
    n += 1
    good = abs(chz) < 1e-6 and abs(cz - (h + 0.3)) < 0.05
    n_ok += 1 if good else 0
    worst.append((abs(cz - h - 0.3), sid, cz, chz, h))
    if n <= 5:
        print("%-14s %-16s %-9.2f %-10.2f %-9.2f %+.2f" % (sid, "(%.0f, %.0f)" % (x, y), cz, chz, h, cz + chz - h))

print("\ncapsule 数 %d；「子偏移=0 且 capsuleZ = 地形+0.3」= %d 个" % (n, n_ok))
worst.sort(reverse=True)
print("偏差最大的 3 个：", [(w[1], round(w[0], 3)) for w in worst[:3]])
