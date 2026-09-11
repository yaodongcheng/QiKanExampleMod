#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""临时探针：家族归属分歧 40 格 + 派生可行性分析（只读，不改任何数据）。

输出三节：
  ① 40 格分歧明细（含成员生年、CareerStance、是否有父在族内）
  ② 663 家族的「当年有没有当主」分布（按期）
  ③ 派生结果预览：按「有当主→当主 / 无当主→最年长」推出的 家族×年代 → 势力
"""
import collections
import csv
import io
import os
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
SENT = {"", "无效", "无"}


def load(name):
    with io.open(os.path.join(CSV_DIR, name), encoding="utf-8-sig", newline="") as fh:
        return [{(k or "").strip(): (v or "").strip() for k, v in r.items()}
                for r in csv.DictReader(fh) if any((v or "").strip() for v in r.values())]


hero = [r for r in load("TaikouHero.csv") if r.get("模板NPC", "") == ""]
clan = load("Clan.csv")
byc = collections.defaultdict(list)
for r in hero:
    byc[r.get("ClanID", "")].append(r)


def birthday(r):
    try:
        return int(r.get("BirthYear") or 9999)
    except ValueError:
        return 9999


print("=" * 90)
print("① 分歧格明细（家族 × 年代）")
print("=" * 90)
n_cells = 0
for cid, mem in sorted(byc.items()):
    if len(mem) < 2:
        continue
    for e in ERAS:
        vals = set(h.get("Kingdom_" + e, "") for h in mem) - SENT
        if len(vals) <= 1:
            continue
        n_cells += 1
        heads = [h for h in mem if h.get("CareerStance_" + e) == "首领"
                 and h.get("Kingdom_" + e, "") not in SENT]
        print("\n%s %s  [%s]  家族共 %d 人" % (
            cid, e, "当主×%d" % len(heads) if heads else "无当主", len(mem)))
        for h in sorted(mem, key=birthday):
            k = h.get("Kingdom_" + e, "")
            if k in SENT:
                continue
            print("    %-18s 生%s  位=%-6s %-10s 首领=%s  父=%s" % (
                h.get("CNName", ""), h.get("BirthYear", "?"), h.get("CareerStance_" + e, ""),
                k, "✓" if h in heads else " ", h.get("FatherId", "") or "-"))
print("\n合计 %d 格 / %d 家族" % (n_cells, len(set(
    cid for cid, mem in byc.items() if len(mem) >= 2 and any(
        len(set(h.get("Kingdom_" + e, "") for h in mem) - SENT) > 1 for e in ERAS)))))

print()
print("=" * 90)
print("② 派生可行性：家族 × 年代 有无当主 / 有无成员")
print("=" * 90)
stat = collections.Counter()
nohead_but_div = []
for cid, mem in sorted(byc.items()):
    for e in ERAS:
        vals = set(h.get("Kingdom_" + e, "") for h in mem) - SENT
        heads = [h for h in mem if h.get("CareerStance_" + e) == "首领"
                 and h.get("Kingdom_" + e, "") not in SENT]
        if not vals:
            stat["无成员有主家（全无效/无）"] += 1
        elif len(vals) == 1:
            stat["唯一归属（无分歧）"] += 1
        elif len(heads) == 1:
            stat["分歧·有当主（规则1可自动）"] += 1
        elif len(heads) > 1:
            stat["分歧·当主多个（要人裁）"] += 1
        else:
            stat["分歧·无当主（要人裁）"] += 1
for k, v in stat.most_common():
    print("  %-28s %d" % (k, v))
