#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""临时探针 3：新规则下的家族集合测算（只读）。

规则（用户 2026-09-11 裁定）：
  · 陪臣 → 并入其上司（直接侍奉的那位）
  · 家头 = 有「叶子部下」（部下里没有陪臣的人）的人；clan = 家头 + 叶子部下
  · 妻子 → 并入丈夫 clan；无上级的亲人 → 并入该人 clan
  · 浪人 → 无 clan
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
clan_cur = load("Clan.csv")

P("=" * 94)
P("① 势力='?' 的行是什么")
b60 = next(b for b in blocks if b["年"] == "1560")
q = [r for r in b60["rows"].values() if r["势力"] == "?"]
P(f"  1560 中 势力=? 共 {len(q)}；有名字的 {sum(1 for r in q if r['名'])}")
P("  前 10:", [(r["pid"], r["名"], r["立场"], r["组织"], r["上司"]) for r in q[:10]])
noname = [r for r in b60["rows"].values() if not r["名"]]
P(f"  1560 中 空名字行 {len(noname)}；其立场/势力 "
  f"{collections.Counter((r['立场'], r['势力']) for r in noname).most_common(5)}")

P("=" * 94)
P("② 其他 立场按 势力 拆分（六年代）")
for b in blocks:
    o = collections.Counter(r["势力"] for r in b["rows"].values() if r["立场"] == "其他")
    P(f"  {b['年']}: {dict(o)}")

P("=" * 94)
P("③ 逐年代按规则算家族（只算 TaikouHero 里的人）")
P("   家头判定：有 ≥1 个「叶子部下」（部下里没有自己部下的人）；无上司且不是浪人 → 自立")


def build_clans(rows, keep):
    """返回 {head_pid: [member_pid...]}，只保留 keep 里的 pid"""
    leaf = {}      # pid -> 该人有叶子部下吗
    for pid, r in rows.items():
        leaves = [q for q in rows.values()
                  if q["上司"] == r["名"] and q["名"] and q["部下"] == 0]
        leaf[pid] = len(leaves) > 0
    heads = {pid for pid, r in rows.items()
             if (r["名"] and pid in keep) and (leaf[pid] or
                 (r["上司"] in ("无", "") and r["势力"] != "浪人" and r["立场"] != "其他"))}
    # 名字 → pid（同年代内重名的取第一个非空）
    byname = {}
    for pid, r in rows.items():
        if r["名"]:
            byname.setdefault(r["名"], pid)
    member_of = {}
    for pid, r in rows.items():
        if pid not in keep or not r["名"]:
            continue
        if pid in heads:
            member_of[pid] = pid
        elif r["上司"] in ("无", ""):
            member_of[pid] = None                       # 浪人/其他 → 无 clan
        else:
            up = byname.get(r["上司"])
            member_of[pid] = up if up in heads else None
    fam = collections.defaultdict(list)
    for pid, h in member_of.items():
        if h:
            fam[h].append(pid)
    return fam, member_of


keep = set(by_dx)
P(f"  TaikouHero 人物 {len(keep)} 人")
allfam = {}
for b in blocks:
    fam, member_of = build_clans(b["rows"], keep)
    sizes = collections.Counter(len(v) for v in fam.values())
    nolclan = sum(1 for pid in keep if member_of.get(pid) is None)
    P(f"  {b['年']}: 家族 {len(fam)} 个（单人 {sizes.get(1,0)}）· "
      f"成员分布 {dict(sorted(sizes.items())[:8])} · 无家族者 {nolclan} 人 · "
      f"最大族 {max((len(v) for v in fam.values()), default=0)} 人")
    allfam[b["年"]] = fam

P("=" * 94)
P("④ 六年代家族并集 + 人名稳定性")
union_heads = collections.defaultdict(set)
for y, fam in allfam.items():
    for h in fam:
        union_heads[by_dx[h]["CNName"]].add(y)
P(f"  家头并集（按人名）= {len(union_heads)}")
stable = [n for n, ys in union_heads.items() if len(ys) == 6]
P(f"  六年代都是家头的 {len(stable)} 人；只在 1 个年代是家头的 "
  f"{sum(1 for ys in union_heads.values() if len(ys) == 1)} 人")

# 成员归族稳定性
memb = collections.defaultdict(set)
for y, fam in allfam.items():
    for h, ms in fam.items():
        for m in ms:
            memb[m].add(h)
chg = sum(1 for m, hs in memb.items() if len(hs) > 1)
P(f"  跨年代换过家族的人 {chg} / {len(memb)}")

P("=" * 94)
P("⑤ 现有 Clan.csv 663 行的去留测算（按 1560）")
fam60 = allfam["1560"]
head_names_60 = {by_dx[h]["CNName"] for h in fam60}
cur_names = {r["ChineseName"] for r in clan_cur}
P(f"  现有家族名 {len(cur_names)}；1560 家头名 {len(head_names_60)}")
P(f"  现有家族名 ∩ 1560 家头名 = {len(cur_names & head_names_60)}")
P(f"  现有家族名 中不是 1560 家头的 = {len(cur_names - head_names_60)}")
P(f"  1560 家头名 中现有没有的 = {len(head_names_60 - cur_names)}")

P("=" * 94)
P("⑥ 妻子 / 亲人（TaikouHero 亲属列）")
sex = collections.Counter(r.get("Gender", "") for r in hero)
P("  Gender 取值分布:", dict(sex))
fem = [r for r in hero if r.get("Gender") not in ("1", "")]
P(f"  非男性行 {len(fem)}")
fs = collections.Counter()
for r in fem:
    s = b60["rows"].get(DXRE.match(r["ID"]).group(1))
    fs[(s["立场"], s["势力"]) if s else ("无SUP", "")] += 1
P("  女性在 1560 的 (立场,势力) 分布:", fs.most_common(10))
sp = [(r["CNName"], r.get("SpouseId", ""), r.get("Gender", "")) for r in hero
      if r.get("SpouseId", "").strip()]
P(f"  有配偶 {len(sp)}；样例 {sp[:10]}")
kin = collections.Counter()
for r in hero:
    for c in ("FatherId", "MotherId", "GrandFatherId", "KinsId"):
        if r.get(c, "").strip():
            kin[c] += 1
P("  亲属列非空计数:", dict(kin))

io.open(os.path.join(REPO, "Debug", "_tmp_clan_rule_probe.txt"), "w",
        encoding="utf-8").write("\n".join(out))
print("WROTE Debug/_tmp_clan_rule_probe.txt")
