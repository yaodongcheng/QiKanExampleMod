#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""临时探针 5：新家族规则的连动面测算（只读）。

① 据点表 Owner_<年> → 新家族：对不上会怎样（无族城主 / 织丰编号 / 空）
② 家族 id 命名测算：EnglishName 首块 → clan_<roman>_<n>，看撞车与复用率
③ 现 Clan.csv 663 行的 id 里，有多少能被新家族复用（同姓同名）
④ 家族 → 王国映射：家头组织 → TaikouForce.id 覆盖率
"""
import collections
import csv
import io
import os
import re
import sys
import unicodedata

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
SUP = os.path.join(REPO, "Knowledge", "太阁5", "太阁日志", "上级日志.md")
LINE = re.compile(r"Log: SUP\|(.*)$")
DXRE = re.compile(r"lord_tk5_(\d+)$")
out = []
P = lambda *a: out.append(" ".join(str(x) for x in a))


def load(name):
    with io.open(os.path.join(CSV_DIR, name), encoding="utf-8-sig", newline="") as fh:
        return [{(k or "").strip(): (v or "").strip() for k, v in r.items()}
                for r in csv.DictReader(fh) if any((v or "").strip() for v in r.values())]


def parse_sup():
    blocks, cur = [], None
    with io.open(SUP, encoding="utf-8", errors="replace") as fh:
        for raw in fh:
            m = LINE.search(raw.rstrip("\n"))
            if not m:
                continue
            parts = m.group(1).split("|")
            if parts[0] == "HDR":
                d = dict(p.split(":", 1) for p in parts[1:] if ":" in p)
                cur = {"年": d.get("年"), "rows": {}}
                blocks.append(cur)
                continue
            if cur is None or len(parts) < 5:
                continue
            rec = dict(p.split(":", 1) for p in parts[4:] if ":" in p)
            cur["rows"][parts[2]] = {
                "pid": parts[2], "名": parts[3].strip(),
                "立场": rec.get("立场", "?"), "势力": rec.get("势力", "?"),
                "组织": rec.get("组织", "无"), "上司": rec.get("上司", "无"),
                "當主": rec.get("当主", "无"), "部下": int(rec.get("部下") or 0),
            }
    return blocks


blocks = parse_sup()
hero = [r for r in load("TaikouHero.csv") if r.get("模板NPC", "") == ""]
by_dx = {DXRE.match(r["ID"]).group(1): r for r in hero if DXRE.match(r["ID"])}
sett = load("Settlements.csv")
force = load("TaikouForce.csv")
force = [r for r in force if r.get("ID") != "ID"]


def resolve(era_rows):
    byname = {}
    for pid, r in era_rows.items():
        if r["名"]:
            byname.setdefault(r["名"], pid)
    clan = {}
    for pid in by_dx:
        r = era_rows.get(pid)
        if r is None or not r["名"]:
            clan[pid] = None
            continue
        if r["部下"] > 0 or (r["上司"] in ("无", "") and r["立场"] == "当主"):
            clan[pid] = pid
        elif r["上司"] not in ("无", ""):
            up = byname.get(r["上司"])
            clan[pid] = up if (up in by_dx) else None
        else:
            clan[pid] = None
    # 妻子
    for pid, r in by_dx.items():
        v = (r.get("SpouseId") or "").strip()
        m = DXRE.match(v) if v else None
        if not m:
            continue
        s = m.group(1)
        if clan.get(pid) is None and clan.get(s) is not None:
            clan[pid] = clan[s]
        elif clan.get(s) is None and clan.get(pid) is not None:
            clan[s] = clan[pid]
    # 亲人（无上司、无部下者）
    for pid, r in by_dx.items():
        if clan.get(pid) is None:
            continue
        for c in ("FatherId", "GrandFatherId", "KinsId", "MotherId"):
            for v in (r.get(c, "") or "").split("|"):
                m = DXRE.match(v.strip()) if v.strip() else None
                if not m:
                    continue
                k = m.group(1)
                kr = era_rows.get(k)
                if kr is None or clan.get(k) is not None:
                    continue
                if kr["上司"] in ("无", "") and kr["部下"] == 0:
                    clan[k] = clan[pid]
    return clan


res = {b["年"]: resolve(b["rows"]) for b in blocks}
sup = {b["年"]: b["rows"] for b in blocks}

P("=" * 96)
P("① 据点表 Owner_<年> → 新家族")
byid_force = {}
for f in force:
    for n in [f.get("势力名", ""), f.get("短名", "")] + (f.get("别名", "") or "").split("|"):
        if n.strip():
            byid_force[n.strip()] = f["ID"]
unmapped, noclan, mapped = collections.Counter(), 0, 0
for r in sett:
    for y in res:
        ow = r.get(f"Owner_{y}", "").strip()
        cn = r.get(f"Clan_{y}", "").strip()
        if not ow:
            continue
        m = DXRE.match(ow)
        if not m:
            unmapped[ow] += 1
            continue
        h = res[y].get(m.group(1))
        if h is None:
            noclan += 1
            if noclan <= 12:
                P(f"    无族城主: {r['id']} {y} owner={ow} "
                  f"({by_dx.get(m.group(1),{}).get('CNName','?')}) 原Clan={cn}")
        else:
            mapped += 1
P(f"  可映射 {mapped} / 无族城主 {noclan} / 非 lord_tk5 编号 {sum(unmapped.values())} {unmapped.most_common(5)}")

P("=" * 96)
P("② 家族 id 测算（clan_<EnglishName首块小写>_<n>）")
ROMAN = re.compile(r"[^A-Za-z]")


def rom(r):
    en = r.get("EnglishName", "") or ""
    s = en.split(" ")[0]
    return ROMAN.sub("", s).lower() or "unknown"


houses = {}          # (年, head_pid) → (id, name)
id_used = collections.Counter()
# 先把每个年代的家族按「家头名望（成员数）」排序给 n
for y in sorted(res, key=int):
    fam = collections.defaultdict(list)
    for pid, h in res[y].items():
        if h:
            fam[h].append(pid)
    seen_sur = collections.Counter()
    for h, ms in sorted(fam.items(), key=lambda kv: -len(kv[1])):
        s = rom(by_dx[h])
        seen_sur[s] += 1
        houses[(y, h)] = f"clan_{s}_{seen_sur[s]}"
        id_used[f"clan_{s}_{seen_sur[s]}"] += 1
P(f"  (年,家头) 实例 {len(houses)}；不同 id {len(id_used)}；"
  f"跨年代复用的 id {sum(1 for v in id_used.values() if v > 1)}")
P(f"  复用最多的 15 个: {id_used.most_common(15)}")

P("=" * 96)
P("③ 现 Clan.csv 复用")
cur = load("Clan.csv")
curids = {r["ID"] for r in cur}
P(f"  现 Clan.csv {len(cur)} 行；新家族实例用到的 id 里，现表已有的 "
  f"{sum(1 for i in id_used if i in curids)} / {len(id_used)}")
sample_new = [i for i in id_used if i not in curids][:20]
P(f"  现表没有的样例: {sample_new}")
sample_re = [i for i in id_used if i in curids][:10]
P(f"  现表已有的样例: {sample_re}")

P("=" * 96)
P("④ 家族 → 王国（家头组织 → TaikouForce.id）")
for y in ("1560",):
    fam = collections.defaultdict(list)
    for pid, h in res[y].items():
        if h:
            fam[h].append(pid)
    bad = collections.Counter()
    for h, ms in fam.items():
        org = sup[y][h]["组织"]
        if org in ("无", ""):
            bad["家头无组织"] += 1
        elif org not in byid_force:
            bad[f"组织无对应势力:{org}"] += 1
    P(f"  {y} 家族 {len(fam)}；问题 {dict(bad) if bad else '无'}")
    P(f"  家头无组织的样例: "
      f"{[(by_dx[h]['CNName'], sup[y][h]['立场'], sup[y][h]['势力']) for h,ms in fam.items() if sup[y][h]['组织'] in ('无','')][:10]}")

P("=" * 96)
P("⑤ TaikouHero 里 1560 无族者的画像")
nocl = [pid for pid in by_dx if res["1560"].get(pid) is None]
P(f"  1560 无族 {len(nocl)} 人；按势力 "
  f"{collections.Counter(sup['1560'][p]['势力'] for p in nocl)}")
P(f"  样例: {[(by_dx[p]['CNName'], sup['1560'][p]['立场'], sup['1560'][p]['势力']) for p in nocl[:25]]}")

P("=" * 96)
P("⑥ 每家规模 top20（1560）")
fam = collections.defaultdict(list)
for pid, h in res["1560"].items():
    if h:
        fam[h].append(pid)
for h, ms in sorted(fam.items(), key=lambda kv: -len(kv[1]))[:20]:
    hr = sup["1560"][h]
    P(f"  {id_used and houses[('1560',h)]:>22} | {by_dx[h]['CNName']:<8} "
      f"{hr['立场']}/{hr['组织']:<8} 成员 {len(ms)}")

io.open(os.path.join(REPO, "Debug", "_tmp_clan_rule3.txt"), "w", encoding="utf-8").write("\n".join(out))
print("WROTE Debug/_tmp_clan_rule3.txt")
