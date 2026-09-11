# -*- coding: utf-8 -*-
"""临时探针：核对 tkhack 坐标转储（序号 1..274）与 era_v2 cities.csv（city_idx 0..273）的对位。"""
import csv, io, os, re

ROOT = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
COORD = os.path.join(ROOT, "Knowledge", "太阁5", "太阁5_据点坐标_20260901.md")
ERA = os.path.join(ROOT, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "_analysis", "decoded", "era_v2")

# 1) 读坐标转储（只取第一轮 13:43 那批）
coords = {}
with io.open(COORD, encoding="utf-8") as f:
    for line in f:
        m = re.search(r"Log: (\d+)\|([^|]+)\|(\d+)\|(\d+)\|(\d+)$", line.strip())
        if not m:
            continue
        k = int(m.group(1))
        if k not in coords:
            coords[k] = dict(name=m.group(2), kind=int(m.group(3)), x=int(m.group(4)), y=int(m.group(5)))
print("坐标条数:", len(coords), "序号区间:", min(coords), max(coords))

# 2) 读 1554 城表
rows = list(csv.DictReader(io.open(os.path.join(ERA, "1554", "cities.csv"), encoding="utf-8-sig")))
print("1554 cities 行数:", len(rows))

# 3) 对位核对：序号-1 == city_idx，逐条比名字
mismatch, kindstat = [], {}
for r in rows:
    idx = int(r["city_idx"])
    c = coords.get(idx + 1)
    if c is None:
        mismatch.append((idx, r["name_official"], "NO_COORD"))
        continue
    kindstat.setdefault(r["type"], set()).add(c["kind"])
    nm_era = r["name_official"].replace("城", "").replace("之町", "").replace("之里", "").replace("之砦", "")
    nm_co = c["name"]
    for suf in ("城", "之町", "之里", "之砦", "館"):
        nm_co = nm_co.replace(suf, "")
    if nm_era != nm_co:
        mismatch.append((idx, r["name_official"], c["name"]))
print("名字不一致条数:", len(mismatch))
for m in mismatch[:40]:
    print("   idx=%s era=%s coord=%s" % m)
print("type→坐标类型字段:", {k: sorted(v) for k, v in kindstat.items()})

# 4) 坐标范围
xs = [c["x"] for c in coords.values()]; ys = [c["y"] for c in coords.values()]
print("x 范围:", min(xs), max(xs), " y 范围:", min(ys), max(ys))
