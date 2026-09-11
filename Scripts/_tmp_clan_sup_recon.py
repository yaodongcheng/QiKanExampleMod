#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""临时探针：上级日志（SUP）结构勘察 v2 —— 为「按陪臣关系重建 clan」提供事实底数（只读）。

日志字段为简体：立场/势力/组织/上司/当主/据点/部下/陪臣
join 键 = TaikouHero.csv 的 `ID` 列（DX 人物番号）↔ SUP 的 人物番号
"""
import collections
import csv
import io
import os
import re
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
SUP = os.path.join(REPO, "Knowledge", "太阁5", "太阁日志", "上级日志.md")
LINE = re.compile(r"Log: SUP\|(.*)$")


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
                "no": parts[1], "pid": parts[2], "名": parts[3],
                "立场": rec.get("立场", "?"), "势力": rec.get("势力", "?"),
                "组织": rec.get("组织", "无"), "上司": rec.get("上司", "无"),
                "当主": rec.get("当主", "无"), "据点": rec.get("据点", "无"),
                "部下": int(rec.get("部下") or 0), "陪臣": int(rec.get("陪臣") or 0),
            }
    return blocks


blocks = parse_sup()
out = []
P = lambda *a: out.append(" ".join(str(x) for x in a))

P("=" * 92)
P("① 六剧本块（立场分布）")
for b in blocks:
    c = collections.Counter(r["立场"] for r in b["rows"].values())
    org = collections.Counter(r["势力"] for r in b["rows"].values())
    P(f"  {b['年']}  人数 {len(b['rows'])}  立场 {dict(c)}")
    P(f"        势力 {dict(org)}")

P("=" * 92)
P("② 1560 上级链深度 / 每层人数")
b60 = next(x for x in blocks if x["年"] == "1560")
rows = b60["rows"]
by_name = {}
dupn = collections.Counter(r["名"] for r in rows.values())
for r in rows.values():
    by_name.setdefault(r["名"], r)


def chain(r):
    seen, cur_ = set(), r
    while cur_["上司"] not in ("无", "") and cur_["名"] not in seen:
        seen.add(cur_["名"])
        nxt = by_name.get(cur_["上司"])
        if nxt is None:
            return -1
        cur_ = nxt
    return len(seen)


depth = collections.Counter(chain(r) for r in rows.values())
P("  深度分布（0=无上级）:", dict(sorted(depth.items())))
dnames = collections.Counter(rows[k]["立场"] for k, v in depth.items() if v == 3 for k in [k]) if False else None
P("  深度3（三层下属）的人:", [(r["名"], r["立场"], r["上司"]) for r in rows.values() if chain(r) == 3][:20])
P(f"  重名（同年代内）: {sum(1 for k, v in dupn.items() if v > 1)} 个名字，样例 "
  f"{[k for k, v in dupn.most_common(8) if v > 1]}")

P("=" * 92)
P("③ 新规则下 1560 的 clan 规模")
n_sub = [r for r in rows.values() if r["部下"] > 0]
n_leaf = [r for r in rows.values() if r["部下"] == 0]
P(f"  有部下 {len(n_sub)} / 无部下 {len(n_leaf)}")
# 每个有部下的人，看他的直属部下里有多少是「陪臣」（陪臣栏）vs「直臣」
combos = collections.Counter((r["部下"], r["陪臣"]) for r in n_sub)
P("  (部下,陪臣) 组合分布 top15:", combos.most_common(15))
# 新 clan 头 = 有部下的人；其成员 = 自己 + 部下中「无部下者」
heads = {}
for r in n_sub:
    heads[r["名"]] = r
member_of = {}
for r in rows.values():
    if r["名"] in heads:
        member_of[r["名"]] = r["名"]                    # 自己是家头
    elif r["上司"] in ("无", ""):
        member_of[r["名"]] = r["名"]                    # 无上级（当主/浪人）= 自立
    else:
        member_of[r["名"]] = r["上司"]                  # 归直接上司
clans = collections.Counter(member_of[r["名"]] for r in rows.values())
P(f"  家头数（含无上级者）{len(clans)}；成员数分布 top15:",
  collections.Counter(clans.values()).most_common(15))
P("  成员最多的 12 家:", [(k, v) for k, v in clans.most_common(12)])
P("  单人 clan 数:", sum(1 for v in clans.values() if v == 1))

P("=" * 92)
P("④ join TaikouHero.csv（ID ↔ 人物番号）")
hero = [r for r in load("TaikouHero.csv") if r.get("模板NPC", "") == ""]
DXRE = re.compile(r"lord_tk5_(\d+)$")
by_id = {DXRE.match(r["ID"]).group(1): r for r in hero if DXRE.match(r["ID"])}
pids = set(rows)
P(f"  TaikouHero 人物行 {len(hero)}；SUP pid {len(pids)}")
miss = pids - set(by_id)
P(f"  SUP 有、TaikouHero 无: {len(miss)}")
P("    样例:", [(p, rows[p]["名"], rows[p]["立场"], rows[p]["势力"]) for p in
              sorted(miss, key=lambda x: int(x))[:15]])
extra = set(by_id) - pids
P(f"  TaikouHero 有、SUP 无: {len(extra)}")
P("    样例:", [(p, by_id[p]["CNName"], by_id[p]["Identity_1560"]) for p in
              sorted(extra, key=lambda x: int(x))[:15]])

P("=" * 92)
P("⑤ 年代稳定性（跨六剧本）")
allp = set()
for bl in blocks:
    allp |= set(bl["rows"])
changed_up, changed_org, changed_stance, gone = set(), set(), set(), collections.Counter()
for pid in allp:
    ups, orgs, sts, seen = set(), set(), set(), 0
    for bl in blocks:
        r = bl["rows"].get(pid)
        if r is None:
            continue
        seen += 1
        ups.add(r["上司"])
        orgs.add(r["组织"])
        sts.add(r["立场"])
    gone[seen] += 1
    if len(ups) > 1:
        changed_up.add(pid)
    if len(orgs) > 1:
        changed_org.add(pid)
    if len(sts) > 1:
        changed_stance.add(pid)
P(f"  pid 并集 {len(allp)}；出现在几个剧本里的分布 {dict(sorted(gone.items()))}")
P(f"  上司变过 {len(changed_up)} / 组织变过 {len(changed_org)} / 立场变过 {len(changed_stance)}")

P("=" * 92)
P("⑥ 女性 / 配偶（TaikouHero）")
fem = [r for r in hero if r.get("Gender") == "2" or r.get("Gender") == "女"]
P(f"  女性行 {len(fem)}；有 SpouseId 的行 {sum(1 for r in hero if r.get('SpouseId','').strip())}")
fem_sup = collections.Counter()
for r in fem:
    s = rows.get(r["ID"])
    fem_sup[s["立场"] if s else "SUP无"] += 1
P("  女性在 1560 的立场分布:", dict(fem_sup))
sp = [(r["ID"], r["CNName"], r.get("SpouseId", "")) for r in hero if r.get("SpouseId", "").strip()]
P(f"  有配偶样例: {sp[:8]}")

io.open(os.path.join(REPO, "Debug", "_tmp_clan_sup_recon.txt"), "w", encoding="utf-8").write("\n".join(out))
print("\n".join(out))
