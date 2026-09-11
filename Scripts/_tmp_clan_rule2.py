#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""临时探针 4：新家族规则 —— 完整算法原型（只读，不改数据）。

规则（用户 2026-09-11 裁定，逐条落地）：
  R1 家头 = 「有部下的人」（部下>0），或「无上司的当主」（光杆大名也算一家）
  R2 其他人 → 并入其直接上司的族；上司不在英雄表 → 往上找（当主），仍无 → 无从归属
  R3 无上司且非当主（浪人 / 无所属）→ 无族（骑砍侧 = 游荡者）
  R4 妻子 → 并入丈夫的族
  R5 其他亲人（父/母/祖父/亲戚）无上司者 → 并入该人的族
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
                "当主": rec.get("当主", "无"), "据点": rec.get("据点", "无"),
                "部下": int(rec.get("部下") or 0), "陪臣": int(rec.get("陪臣") or 0),
            }
    return blocks


blocks = parse_sup()
hero = [r for r in load("TaikouHero.csv") if r.get("模板NPC", "") == ""]
by_dx = {DXRE.match(r["ID"]).group(1): r for r in hero if DXRE.match(r["ID"])}
ERAS = [b["年"] for b in blocks]


def resolve(era_rows, keep):
    """返回 {pid: head_pid or None}"""
    byname = {}
    for pid, r in era_rows.items():
        if r["名"]:
            byname.setdefault(r["名"], pid)
    clan = {}
    for pid in keep:
        r = era_rows.get(pid)
        if r is None or not r["名"]:
            clan[pid] = None
            continue
        if r["部下"] > 0 or (r["上司"] in ("无", "") and r["立场"] == "当主"):
            clan[pid] = pid                                   # R1 家头
        elif r["上司"] not in ("无", ""):
            up = byname.get(r["上司"])                        # R2 归上司
            if up is None or up not in keep:
                # 上司不在英雄表 → 沿当主链上溯
                up2 = byname.get(r["当主"]) if r["当主"] not in ("无", "") else None
                up = up2 if (up2 and up2 in keep) else None
            clan[pid] = up
        else:
            clan[pid] = None                                  # R3 浪人/无所属
    return clan


def apply_family(clan, era_rows, by_dx):
    """R4 妻子 + R5 无上级的亲人"""
    inv = {}
    for pid, r in by_dx.items():
        for c in ("SpouseId",):
            v = r.get(c, "").strip()
            m = DXRE.match(v) if v else None
            if m:
                inv.setdefault(m.group(1), []).append((pid, c))
    moved_w, moved_k = 0, 0
    for tgt, srcs in inv.items():
        for src, _c in srcs:
            if clan.get(tgt) is None and clan.get(src) is not None:
                clan[tgt] = clan[src]
                moved_w += 1
            if clan.get(src) is None and clan.get(tgt) is not None:
                clan[src] = clan[tgt]
                moved_w += 1
    # R5 亲人
    for pid, r in by_dx.items():
        if clan.get(pid) is None:
            continue
        for c in ("FatherId", "MotherId", "GrandFatherId", "KinsId"):
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
                    moved_k += 1
    return moved_w, moved_k


P("=" * 96)
P("① 逐年代家族（完整规则）")
allfams, allclan = {}, {}
for b in blocks:
    cl = resolve(b["rows"], set(by_dx))
    mw, mk = apply_family(cl, b["rows"], by_dx)
    fam = collections.defaultdict(list)
    for pid, h in cl.items():
        if h:
            fam[h].append(pid)
    allfams[b["年"]], allclan[b["年"]] = fam, cl
    sizes = collections.Counter(len(v) for v in fam.values())
    noc = sum(1 for pid in by_dx if cl.get(pid) is None)
    P(f"  {b['年']}: 家族 {len(fam)}（单人 {sizes.get(1,0)}）· 无族 {noc} 人"
      f"（浪人/无所属）· 最大族 {max((len(v) for v in fam.values()), default=0)}"
      f" · 妻子并入 {mw} · 亲人并入 {mk}")
    P(f"       成员分布 {dict(sorted(sizes.items()))}")

P("=" * 96)
P("② 家头集合的跨年代并集 / 换族")
heads = collections.defaultdict(set)
for y, fam in allfams.items():
    for h in fam:
        heads[by_dx[h]["CNName"]].add(y)
P(f"  家头（按人名）并集 {len(heads)}；六代都是 {sum(1 for v in heads.values() if len(v)==6)}"
  f"；只 1 代 {sum(1 for v in heads.values() if len(v)==1)}")
memb = collections.defaultdict(set)
for y, fam in allfams.items():
    for h, ms in fam.items():
        for m in ms:
            memb[m].add(h)
P(f"  换过族的人 {sum(1 for v in memb.values() if len(v)>1)} / {len(memb)}")
nocl = collections.Counter()
for y, cl in allclan.items():
    for pid, h in cl.items():
        if h is None:
            nocl[pid] += 1
P(f"  无族者 1304 中：TaikouHero 里的 {sum(1 for p in by_dx if nocl.get(p,0)>0)} 人")
P(f"  六代都无族的 {sum(1 for p in by_dx if nocl.get(p,0)==6)} 人")

P("=" * 96)
P("③ 家头姓氏撞车（同年代内两个家头同姓）")
for b in blocks:
    fam = allfams[b["年"]]
    sur = collections.defaultdict(list)
    for h in fam:
        r = by_dx[h]
        sur[r.get("FirstName", "") or r["CNName"][:2]].append(r["CNName"])
    coll = {k: v for k, v in sur.items() if len(v) > 1}
    P(f"  {b['年']}: 同姓家头 {len(coll)} 组 → {list(coll.items())[:6]}")

P("=" * 96)
P("④ 归属链与势力一致性检查（1560）")
b60 = next(b for b in blocks if b["年"] == "1560")
fam60 = allfams["1560"]
bad_org = 0
for h, ms in fam60.items():
    orgs = {b60["rows"][m]["组织"] for m in ms if b60["rows"][m]["组织"] not in ("无",)}
    if len(orgs) > 1:
        bad_org += 1
P(f"  族内成员组织不一致的族 {bad_org} / {len(fam60)}")
P("  样例 5 个族（家头 → 成员）:")
for h, ms in sorted(fam60.items(), key=lambda kv: -len(kv[1]))[:5]:
    hr = b60["rows"][h]
    P(f"    {by_dx[h]['CNName']}({hr['立场']}/{hr['组织']}) ← "
      f"{[by_dx[m]['CNName'] for m in ms][:12]}")

P("=" * 96)
P("⑤ 上司不在英雄表 / 无所属者")
for b in blocks[:1]:
    rows = b["rows"]
    nb = [r for r in rows.values() if r["名"] and r["上司"] not in ("无", "")
          and r["上司"] not in {q["名"] for q in rows.values()}]
    P(f"  {b['年']} 上司名在名单里找不到的: {len(nb)}")
q142 = [r for r in b60["rows"].values() if r["势力"] == "?"]
inhero = [r for r in q142 if r["pid"] in by_dx]
P(f"  1560 无所属(势力=?) 142 人中，在英雄表的 {len(inhero)}："
  f"{[by_dx[r['pid']]['CNName'] for r in inhero][:20]}")
P(f"  英雄表里 1560 无所属者的立场 {collections.Counter(r['立场'] for r in inhero)}")

P("=" * 96)
P("⑥ 每人归属：抽 12 个人看链条")
for nm in ("织田信长", "柴田胜家", "明智光秀", "松平元康", "青山忠成", "浅井长政",
           "浅井久政", "真田信繁", "归蝶", "武田信虎", "弥助", "九鬼嘉隆"):
    pid = next((p for p, r in b60["rows"].items() if r["名"] == nm), None)
    if pid is None:
        P(f"  {nm}: 1560 名单里没有")
        continue
    h = allclan["1560"].get(pid)
    P(f"  {nm}: {b60['rows'][pid]['立场']}/{b60['rows'][pid]['组织']}/上司="
      f"{b60['rows'][pid]['上司']} 部下={b60['rows'][pid]['部下']}"
      f" → 族 = {by_dx[h]['CNName'] if h else '（无族）'}")

io.open(os.path.join(REPO, "Debug", "_tmp_clan_rule2.txt"), "w", encoding="utf-8").write("\n".join(out))
print("WROTE Debug/_tmp_clan_rule2.txt")
