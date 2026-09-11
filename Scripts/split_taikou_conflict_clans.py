#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""同姓异家自动分家（2026-09-11 用户裁定「分家」）
============================================================================
**问题**：`Clan.csv` 里有些家族**内部装了两拨人**——成员在同一年效力不同势力，
导致「家族归属 = 成员共同归属」这条推导规则失效（推不出、或推出错的）。

**判定**：某家族在某年代，其成员的 `Kingdom_<年>`（剔除 `无/无效/空`）**不止一个值** → 分歧家族。

**分组规则（数据驱动，不猜）**：把成员按「**归属集合是否相交**」并查集聚类——
  集合 = 该成员六年 `Kingdom_<年>` 的取值（剔除 `无/无效`）。
  相交 = 同一拨人（一起效力过同一家）；不相交 = 两拨人。
  实测样例：
    · `clan_naito_1`（内藤）→ 4 组：如安{三好,织田,小西} / 昌丰{武田} / 正成{今川,德川} / 兴盛{}
    · `clan_murakami_2`（村上）→ 2 组：通康·吉充{村上水军} / 国清·义清{长尾,上杉}
    · `clan_tsuda_1`（津田）→ 3 组：商人{天王寺屋} / 信澄{织田} / 忍者{根来众}

**命名**：名字（ScriptName/ChineseName/Surname/Culture/Kingdom）**克隆自原家族**，只换 ID；
  按规模排序，**最大的一组保留原 ID**，其余取下一个空闲后缀（`clan_X_2` → `_3`…，跳过已占用的）。

**动作**（`--apply` 才写；备份 + 往返校验 + 幂等）：
  ① `Clan.csv` 追加新家族行
  ② `TaikouHero.csv` 把分出去的成员的 `ClanID` 改成新 ID
  ③ `Settlements.csv` 同步这些人的 `Clan_<年>`（不做会被体检当场抓住）

Usage:
  python Scripts/split_taikou_conflict_clans.py            # 报告 + 完整分组预览（不写）
  python Scripts/split_taikou_conflict_clans.py --apply    # 写回三张表
Exit: 0 无待改或已改完 / 1 有硬问题 / 2 fatal。
"""
import argparse
import collections
import csv
import io
import os
import re
import shutil
import sys
import time

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
CLAN = os.path.join(CSV_DIR, "Clan.csv")
HERO = os.path.join(CSV_DIR, "TaikouHero.csv")
SETT = os.path.join(CSV_DIR, "Settlements.csv")

ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
SENTINEL = {"", "无", "无效"}


def load(path):
    with io.open(path, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = [(c or "").strip() for c in (rd.fieldnames or [])]
        rows = [{(k or "").strip(): (v or "") for k, v in r.items()} for r in rd]
        return cols, [r for r in rows if any(r.values())]


def write(path, cols, rows):
    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=cols, lineterminator="\r\n",
                       quoting=csv.QUOTE_MINIMAL, extrasaction="ignore")
    w.writeheader()
    for r in rows:
        w.writerow({c: (r.get(c) or "") for c in cols})
    with io.open(path, "w", encoding="utf-8-sig", newline="") as fh:
        fh.write(buf.getvalue())


def affset(h):
    return set(h.get("Kingdom_" + e, "") for e in ERAS) - SENTINEL


def compute(ccols, crows, hrows):
    """返回 [(原 clan id, [ [成员ID…] 组 … ])] —— 只含真分歧的家族。"""
    byc = collections.defaultdict(list)
    for h in hrows:
        if not h.get("模板NPC"):
            byc[h["ClanID"]].append(h)
    plan = []
    for r in crows:
        mem = byc.get(r["ID"], [])
        if len(mem) < 2:
            continue
        diverges = any(len(set(h.get("Kingdom_" + e, "") for h in mem) - SENTINEL) > 1
                       for e in ERAS)
        if not diverges:
            continue
        sets = {h["ID"]: affset(h) for h in mem}
        parent = {h["ID"]: h["ID"] for h in mem}

        def find(x):
            while parent[x] != x:
                parent[x] = parent[parent[x]]
                x = parent[x]
            return x

        for a in sets:
            for b in sets:
                if a < b and (sets[a] & sets[b]):
                    ra, rb = find(a), find(b)
                    if ra != rb:
                        parent[rb] = ra
        grp = collections.defaultdict(list)
        for h in mem:
            grp[find(h["ID"])].append(h["ID"])
        # 规模降序；同规模按首个成员 ID 排（结果确定，不随字典序漂）
        groups = sorted(grp.values(), key=lambda g: (-len(g), sorted(g)[0]))
        # 🔴 归属集为空的成员（女性/隐居/已故/浪人）**并回最大的一组**——他们是一家人，只是当年无主家。
        #    不这么做会把「阿松（前田利家之妻）」「武田信虎（隐居前当主）」「相模/三条（武田家女性）」
        #    单独拆成家族（实测 78 个新家族里近半是这种假拆）。
        empty = [hid for g in groups for hid in g if not sets[hid]]
        if empty and groups:
            main = groups[0]
            for g in groups[1:]:
                keep, move = [], []
                for hid in g:
                    (move if not sets[hid] else keep).append(hid)
                if not keep:                 # 整组都是无主家 → 并入主组
                    main.extend(move)
                    g.clear()                # 🔴 必须清空：否则该成员在主组和原组各出现一次（拆出重复家族）
                else:
                    g[:] = keep
                    main.extend(move)
            groups = [g for g in groups if g]
            groups = sorted(groups, key=lambda g: (-len(g), sorted(g)[0]))
        plan.append((r["ID"], groups))
    return plan


def next_free(cid, used):
    """clan_X_2 → 找下一个没被占用的后缀号。"""
    m = re.match(r"^(.*_)(\d+)$", cid)
    base = m.group(1) if m else cid + "_"
    n = int(m.group(2)) + 1 if m else 2
    while base + str(n) in used:
        n += 1
    return base + str(n)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true")
    args = ap.parse_args()

    for p in (CLAN, HERO, SETT):
        if not os.path.isfile(p):
            print("[FATAL] 缺文件 %s" % p, file=sys.stderr)
            return 2
    ccols, crows = load(CLAN)
    hcols, hrows = load(HERO)
    scols, srows = load(SETT)
    cby = {r["ID"]: r for r in crows}
    hby = {}
    for h in hrows:
        n = h.get("CNName", "")
        if n in hby:
            print("[FATAL] 英雄表 CNName 重复：%s" % n, file=sys.stderr)
            return 2
        hby[n] = h
    name = {h["ID"]: h.get("CNName", "") for h in hrows}

    plan = compute(ccols, crows, hrows)
    used = set(cby)
    moves = []                      # (英雄ID, 原clan, 新clan)
    news = []                       # (新clan行, 原clan id)
    for cid, groups in plan:
        for k, g in enumerate(groups):
            if k == 0:
                continue            # 最大一组留原 ID
            nid = next_free(cid, used)
            used.add(nid)
            row = {c: "" for c in ccols}
            row.update(cby[cid])
            row["ID"] = nid
            row["Owner"] = ""
            news.append((nid, cid, g))
            for hid in g:
                moves.append((hid, cid, nid))

    print("分歧家族 %d 个 → 拆成 %d 个（新增 %d 个家族行，改 %d 个成员的 ClanID）"
          % (len(plan), len(plan) + len(news), len(news), len(moves)))
    print()
    for cid, groups in plan:
        print("  【%s（%s）】现列 Kingdom=%s" % (cid, cby[cid]["ChineseName"], cby[cid]["Kingdom"]))
        for k, g in enumerate(groups):
            tag = "留原ID" if k == 0 else "拆出"
            aff = set()
            for hid in g:
                aff |= affset(next(h for h in hrows if h["ID"] == hid))
            print("      [%s] %-34s 归属={%s}" % (tag, "、".join(name[x] for x in g),
                                                 "、".join(sorted(aff)) or "（无主家）"))
    print()
    if not news:
        print("✅ 无待拆，无需改动（幂等）")
        return 0
    if not args.apply:
        print("（未加 --apply，只报告不写）")
        return 0

    stamp = time.strftime("%Y%m%d_%H%M%S")
    for p in (CLAN, HERO, SETT):
        shutil.copy2(p, p + ".bak_split2_" + stamp)

    for nid, cid, g in news:
        row = {c: "" for c in ccols}
        row.update(cby[cid])
        row["ID"] = nid
        row["Owner"] = ""
        crows.append(row)
        cby[nid] = row
    for hid, old, new in moves:
        hby[name[hid]]["ClanID"] = new
    # Settlements 连带
    fix = set((hid, new) for hid, old, new in moves)
    sid_fix = 0
    for r in srows:
        for e in ERAS:
            o, c = r.get("Owner_" + e, ""), r.get("Clan_" + e, "")
            for hid, new in fix:
                if o == hid and c != new:
                    r["Clan_" + e] = new
                    sid_fix += 1

    write(CLAN, ccols, crows)
    write(HERO, hcols, hrows)
    if sid_fix:
        write(SETT, scols, srows)

    # 往返校验
    _, c2 = load(CLAN)
    _, h2 = load(HERO)
    c2by = {r["ID"]: r for r in c2}
    h2by = {r["CNName"]: r for r in h2}
    for nid, cid, g in news:
        if nid not in c2by:
            print("[FATAL] 写回后 Clan.csv 仍无 %s" % nid)
            return 2
    for hid, old, new in moves:
        if h2by[name[hid]].get("ClanID") != new:
            print("[FATAL] 写回后 %s 的 ClanID 不是 %s" % (name[hid], new))
            return 2
    print("✅ 已写回：Clan.csv +%d 行、TaikouHero.csv 改 %d 格、Settlements.csv 改 %d 格"
          % (len(news), len(moves), sid_fix))
    return 0


if __name__ == "__main__":
    sys.exit(main())
