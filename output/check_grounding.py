# -*- coding: utf-8 -*-
"""核验：① 所有据点不在水下（水面 1.8m） ② mesh 底面正好贴地（按新结构：实体 scale × 图标子件 scale）。"""
import io
import os
import re
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Scripts"))
from taikou_terrain import Terrain, WATER_LEVEL, INLAND_MIN_M

BASE = {"mi_aserai_tower_2": 0.86, "village_empire_1": 0.82}
t = Terrain()
f = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord"
     r"\Modules\Taikou\SceneObj\Main_map\scene.xscene")
s = io.open(f, encoding="utf-8-sig").read()

pat = re.compile(
    r'campaign_icon_capsule_\d+_([a-z0-9_]+)" old_prefab_name="" mobility="1">\s*'
    r'<transform position="([-\d.]+), ([-\d.]+), ([-\d.]+)"[^>]*/>.*?'
    r'<game_entity name="\1" old_prefab_name="" mobility="1">\s*<tags>.*?</tags>\s*'
    r'<transform position="[^"]*" rotation_euler="[^"]*" scale="([\d.]+), ([\d.]+), ([\d.]+)"/>\s*<children>\s*'
    r'<game_entity name="map_icon"[^>]*>\s*'
    r'<transform position="[^"]*" rotation_euler="[^"]*" scale="([\d.]+), ([\d.]+), ([\d.]+)"/>\s*'
    r'<components>\s*<meta_mesh_component name="([^"]+)"', re.S)

print("%-14s %-16s %-8s %-18s %-9s %-9s" % ("据点", "capsule(x,y)", "capsuleZ", "mesh(scale)", "地形高度", "底面离地"))
n = under = ok = 0
inl = []
bad = []
for m in pat.finditer(s):
    sid = m.group(1)
    x, y, cz = float(m.group(2)), float(m.group(3)), float(m.group(4))
    ez = float(m.group(7))                                   # 实体 Z 缩放
    mz = float(m.group(10))                                  # 图标 Z 缩放
    mesh = m.group(11)
    h = t.height_at(x, y)
    bottom = cz - BASE.get(mesh, 0.0) * mz                   # mesh 底面绝对高度
    n += 1
    ok += 1 if abs(bottom - h) < 0.05 else 0
    if h <= WATER_LEVEL:
        under += 1
        bad.append((sid, x, y, h))
    inl.append((t.inland_dist(x, y), sid, h))
    if n <= 5:
        print("%-14s %-16s %-8.2f %-18s %-9.2f %+.2f" % (sid, "(%.0f, %.0f)" % (x, y), cz,
              "%s×%.2f" % (mesh, mz), h, bottom - h))

print("\n据点 %d 个；底面正好贴地 %d 个；**水面(%.1fm)以下 %d 个**" % (n, ok, WATER_LEVEL, under))
for b in bad[:10]:
    print("   ⚠ %-14s (%.0f,%.0f) h=%.2f" % b)

inl.sort()
near = [(d, sid, h) for d, sid, h in inl if d < 10]
print("离海岸线距离：最小 %.0fm  中位 %.0fm（落海吸附目标 ≥%.0fm）" % (inl[0][0], inl[len(inl) // 2][0], INLAND_MIN_M))
if near:
    print("  离海 <10m 的 %d 个（多为本来就在海边的据点、未被吸附）：" % len(near))
    for d, sid, h in near[:8]:
        print("     %-14s 离海 %.0fm  高度 %.2f" % (sid, d, h))
