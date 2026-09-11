#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""临时探针 2：家族归属派生预演 + 家族表全量画像（只读）。

① 663 家族的「有没有人 / 有没有据点」画像
② 40 格裁决建议（规则派生：当主 → 辈分最高 → 独立）
③ 派生后：每年有多少家族能挂上王国 / 各势力名下多少家族
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
SENT = {"", "无", "无效"}


def load(name):
    with io.open(os.path.join(CSV_DIR, name), encoding="utf-8-sig", newline="") as fh:
        return [{(k or "").strip(): (v or "").strip() for k, v in r.items()}
                for r in csv.DictReader(fh) if any((v or "").strip() for v in r.values())]


hero = [r for r in load("TaikouHero.csv") if r.get("模板NPC", "") == ""]
clan = load("Clan.csv")
sett = load("Settlements.csv")
byc = collections.defaultdict(list)
for r in hero:
    byc[r.get("ClanID", "")].append(r)


def birth(r):
    try:
        return int(r.get("BirthYear") or 9999)
    except ValueError:
        return 9999


print("=" * 92)
print("① 家族表画像（663 行）")
print("=" * 92)
sett_clans = set()
for r in sett:
    for e in ERAS:
        sett_clans.add(r.get("Clan_" + e, ""))
n_both = n_hero_only = n_sett_only = n_none = 0
orphans = []
for c in clan:
    cid = c["ID"]
    h, s = bool(byc.get(cid)), cid in sett_clans
    if h and s:
        n_both += 1
    elif h:
        n_hero_only += 1
    elif s:
        n_sett_only += 1
        orphans.append((cid, c.get("ChineseName", "")))
    else:
        n_none += 1
        orphans.append((cid, c.get("ChineseName", "")))
print("  有英雄 + 有据点  %d" % n_both)
print("  只有英雄        %d" % n_hero_only)
print("  只有据点        %d" % n_sett_only)
print("  两者皆无（孤儿） %d" % n_none)
print("  孤儿（据点也没有·无英雄）：%d 个 —— %s" % (
    len(orphans), "、".join("%s(%s)" % (c, n) for c, n in orphans[:30])))

print()
print("=" * 92)
print("② 派生规则预演（当主 → 辈分最高 → 独立）")
print("=" * 92)
NEED = []          # 要人裁的格
for cid, mem in sorted(byc.items()):
    if len(mem) < 2:
        continue
    for e in ERAS:
        vals = set(h.get("Kingdom_" + e, "") for h in mem) - SENT
        if len(vals) <= 1:
            continue
        heads = [h for h in mem if h.get("CareerStance_" + e) == "首领"
                 and h.get("Kingdom_" + e, "") not in SENT]
        elders = sorted([h for h in mem if h.get("Kingdom_" + e, "") not in SENT], key=birth)
        cname = next((c["ChineseName"] for c in clan if c["ID"] == cid), "?")
        if len(heads) == 1:
            rule, who = "规则1·当主", heads[0]
        elif len(heads) > 1:
            rule, who = "要裁·当主多个", None
        else:
            rule, who = "规则2·辈分最高", elders[0]
        NEED.append((cid, cname, e, rule, who, mem, vals))

for cid, cname, e, rule, who, mem, vals in NEED:
    print("\n%-22s %s  %s  %s" % (cid, e, rule,
                                  ("→ %s（%s）" % (who.get("CNName"), who.get("Kingdom_" + e))) if who else "⚠ 无自动解"))
    for h in sorted(mem, key=birth):
        k = h.get("Kingdom_" + e, "")
        mark = " ← 选中" if h is who else ""
        print("      %-16s 生%-5s %-8s %-10s 父=%-16s%s" % (
            h.get("CNName", ""), h.get("BirthYear", "?"), h.get("CareerStance_" + e, ""),
            k or "（无）", h.get("FatherId", "") or "-", mark))

print()
print("=" * 92)
print("③ 派生后规模：每年代家族挂王国 / 独立")
print("=" * 92)
for e in ERAS:
    withk = indep = nomem = 0
    kdc = collections.Counter()
    for cid, mem in byc.items():
        vals = set(h.get("Kingdom_" + e, "") for h in mem) - SENT
        if not vals:
            nomem += 1
        elif len(vals) == 1:
            withk += 1
            kdc[list(vals)[0]] += 1
        else:
            withk += 1
            kdc["（分歧未裁）"] += 1
    print("  %s：有主家 %d / 无主家（独立或当年没人） %d   头 5 势力：%s"
          % (e, withk, nomem, kdc.most_common(5)))
