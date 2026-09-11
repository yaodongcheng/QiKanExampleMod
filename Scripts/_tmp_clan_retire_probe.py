#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""临时探针 3：ClanID_<年> 落地的连带影响 + 退役 Kingdom 列后的局面预演（只读）。

① 若给「改家」的人加 ClanID_<年>，Settlements 的 Clan_<年>/Owner_<年> 有没有连带要改的格
② 退役 Clan.csv.Kingdom 后，「武家无家族」还剩几个
③ 派生家族归属 vs 据点归属 是否打架（族主在 X 家，却拥有 Y 家的城）
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
force = [r for r in load("TaikouForce.csv") if r.get("ID") != "ID"]
byid = {r["ID"]: r for r in hero}
byc = collections.defaultdict(list)
for r in hero:
    byc[r.get("ClanID", "")].append(r)

# 势力名/别名 → id
alias = {}
for f in force:
    for n in [f.get("势力名", ""), f.get("短名", "")] + (f.get("别名", "") or "").split("|"):
        n = (n or "").strip()
        if n:
            alias[n] = f["ID"]
            if n.endswith("家"):
                alias.setdefault(n[:-1], f["ID"])


def bid(r):
    try:
        return int(r.get("BirthYear") or 9999)
    except ValueError:
        return 9999


print("=" * 92)
print("① 若加 ClanID_<年>：Settlements 连带面")
print("=" * 92)
# 候选：盛重(34) 1554-1582 → clan_satake_1；景虎(117) 1554-1568 → clan_hojo_1
CAND = {"lord_tk5_34": (["1554", "1560", "1568", "1575", "1582"], "clan_satake_1"),
        "lord_tk5_117": (["1554", "1560", "1568"], "clan_hojo_1")}
for hid, (eras, newc) in CAND.items():
    h = byid[hid]
    print("\n%s %s：现 Clan=%s → %s 改 ClanID_%s" % (hid, h["CNName"], h["ClanID"], newc, "/".join(eras)))
    hit = 0
    for r in sett:
        for e in eras:
            if r.get("Owner_" + e) == hid:
                hit += 1
                print("    ⚠ 据点 %s Owner_%s=%s，Clan_%s=%s → 需改成 %s"
                      % (r["id"], e, h["CNName"], e, r.get("Clan_" + e), newc))
    print("    据点连带：%d 格" % hit)

print()
print("=" * 92)
print("② 退役 Clan.csv.Kingdom 后：武家「一个家族都没有」还剩几个")
print("=" * 92)
# 派生：家族 → 六年出现过的势力集合
derived = collections.defaultdict(set)
for cid, mem in byc.items():
    for h in mem:
        for e in ERAS:
            k = h.get("Kingdom_" + e, "")
            if k not in SENT:
                derived[cid].add(k)
withclan_new = set()
for cid, ks in derived.items():
    for k in ks:
        if k in alias:
            withclan_new.add(alias[k])
old_withclan = set(r.get("Kingdom", "") for r in clan)
warrior = [f for f in force if f.get("势力类型") == "Warrior" and f.get("ID") != "noKingdom"]
old_missing = sorted(f["ID"] for f in warrior if f["ID"] not in old_withclan)
new_missing = sorted(f["ID"] for f in warrior if f["ID"] not in withclan_new)
print("  旧口径（Clan.csv.Kingdom）无家族的武家：%d 个" % len(old_missing))
print("  派生口径（成员归属推导）无家族的武家：%d 个" % len(new_missing))
print("  仍无家族：%s" % "、".join(new_missing))
print()
print("  旧有、派生后有了（自动修好）：%d 个 → %s"
      % (len(set(old_missing) - set(new_missing)), "、".join(sorted(set(old_missing) - set(new_missing)))))
print("  派生后新增的缺口（旧有现在没了）：%s" % "、".join(sorted(set(new_missing) - set(old_missing))))

print()
print("=" * 92)
print("③ 派生家族归属 vs 据点归属 打架预演")
print("=" * 92)
bad = 0
for cid, mem in byc.items():
    for e in ERAS:
        owned = [r for r in sett if r.get("Clan_" + e) == cid]
        if not owned:
            continue
        vals = set(h.get("Kingdom_" + e, "") for h in mem) - SENT
        if not vals:
            bad += 1
            print("  %s %s：拥有 %d 个据点，但族内当年无一人有主家"
                  % (cid, e, len(owned)))
print("  合计 %d 例" % bad)
