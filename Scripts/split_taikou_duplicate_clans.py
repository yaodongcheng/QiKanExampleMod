#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""同姓异家拆分：一家一个 ClanID（2026-09-11 用户裁定）
============================================================================
**问题**：英雄表把**两个无血缘的同姓家**并成了一个 ClanID，导致「该家当主是谁」不唯一——
`Identity_<年>=大名 且 Kingdom_<年>=某势力` 会一次捞出两个人，重建 Kingdom 的 Owner 时无法裁决。

**用户裁定原话**：「那就是两家不同，stringid 要区分 _1 或者 _2，但是名字可以相同。」

**本次拆的三对**（`_1` 归属 = 太阁编号集合内人物番号靠前者）：

| 势力 | 保留 `_1` | 拆出 `_2` | 人物番号 | 说明 |
|---|---|---|---|---|
| 加藤家 | 加藤清正 | 加藤嘉明 | 230 / 233 | 清正=肥後熊本（尾張出身）、嘉明=伊予松山（三河出身），毫无血缘 |
| 小早川家 | 小早川秀秋 | 小早川秀包 | 315 / 316 | 秀秋=隆景養子、本家継承；秀包=隆景実子，后归毛利（大田氏） |
| 京极家 | 京极高次 | 京极高知 | 271 / 272 | 高次=京極本家（若狭小浜）；高知=其弟（丹後宮津） |

**动作**（`--apply` 才写，均带备份 + 往返校验 + 幂等）：
  ① `Clan.csv` 加 3 行（字段克隆自 `_1`，ID 换 `_2`，本地化键换 `{=my_<id>}`）
  ② `TaikouHero.csv` 把 3 位「次家当主」的 `ClanID` 改成 `_2`
  ③ `Settlements.csv` 把**这些人的据点**的 `Clan_<年>` 同步改成 `_2`
     🔴 不做③会被体检当场抓住（`Settlements：Owner_<年> 的家族必须等于 Clan_<年>`）——
        实测 4 格：town_tk066/068 1598（高知）、town_tk145 1598（嘉明）、town_tk159 1598（秀包）。

🔴 **不改的**（记录在案，另行处理）：
  · **Kingdom 不拆**：太阁5 自己的势力表就是一个 `katō`（太阁编号 350|351|352|353 覆盖全部四人）
    → 两个 ClanID 共属同一个 Kingdom，靠「取 `_1` 家当主」裁决。
  · **既有问题不顺手改**：`加藤段藏`（忍者，轩猿众/透波众）现挂 `clan_katō_1` = 同名撞车；
    `clan_kobayakawa_1` 的 `Kingdom=mori`（隆景是毛利一门）→ `kobayakawa` 势力至今 0 家族。

Usage:
  python Scripts/split_taikou_duplicate_clans.py            # 报告 + 预览（不写）
  python Scripts/split_taikou_duplicate_clans.py --apply    # 写回两张表
Exit: 0 无待改或已改完 / 1 有硬问题 / 2 fatal。
"""
import argparse
import csv
import io
import os
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

# (保留 _1 的人, 拆去 _2 的人, _1 的 ClanID, 依据)
SPLITS = [
    ("加藤清正", "加藤嘉明", "clan_katō_1",
     "清正=肥後熊本（人物番号 230）、嘉明=伊予松山（233），两个无血缘的同姓家"),
    ("小早川秀秋", "小早川秀包", "clan_kobayakawa_1",
     "秀秋=隆景養子、本家継承（315）；秀包=隆景実子、后归毛利大田氏（316）"),
    ("京极高次", "京极高知", "clan_kyōgoku_1",
     "高次=京極本家若狭小浜（271）；高知=其弟、丹後宮津（272）"),
]


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
    for r in hrows:
        n = r.get("CNName", "")
        if n in hby:
            print("[FATAL] 英雄表 CNName 重复：%s" % n, file=sys.stderr)
            return 2
        hby[n] = r

    problems, plan = [], []
    for keep, move, cid1, why in SPLITS:
        cid2 = cid1[:-1] + "2"
        if cid2 in cby:
            plan.append((keep, move, cid1, cid2, why, "已拆（幂等）"))
            continue
        if cid1 not in cby:
            problems.append("Clan.csv 没有 %s" % cid1)
            continue
        h = hby.get(move)
        if h is None:
            problems.append("英雄表没有 %s" % move)
            continue
        if h.get("ClanID") != cid1:
            problems.append("%s 的 ClanID=%r，预期 %r（表格可能已被改过）"
                            % (move, h.get("ClanID"), cid1))
            continue
        plan.append((keep, move, cid1, cid2, why, "待拆"))

    # ③ 连带：被搬家的人当主的据点，Clan_<年> 要跟着改
    moved = {}
    for keep, move, cid1, why in SPLITS:
        h = hby.get(move)
        if h:
            moved[h["ID"]] = (cid1, cid1[:-1] + "2")
    sett_fix = []
    for r in srows:
        for e in ERAS:
            o, c = r.get("Owner_" + e, ""), r.get("Clan_" + e, "")
            if o in moved and c == moved[o][0]:
                sett_fix.append((r["id"], e, o, moved[o][0], moved[o][1]))

    print("=== 拆分计划 ===")
    for keep, move, cid1, cid2, why, st in plan:
        print("  [%s] %s 保留 %s ；%s → %s" % (st, keep, cid1, move, cid2))
        print("        %s" % why)
    if sett_fix:
        print("\n=== 据点连带（%d 格）===" % len(sett_fix))
        for sid, e, o, a, b in sett_fix:
            print("  %s %s：当主 %s 的家族 %s → %s" % (sid, e, o, a, b))
    if problems:
        print("\n❌ 硬问题 %d 条：" % len(problems))
        for p in problems:
            print("   %s" % p)
        return 1
    to_do = [x for x in plan if x[5] == "待拆"]
    if not to_do and not sett_fix:
        print("\n✅ 3 对全部已拆、据点无残留，无需改动（幂等）")
        return 0
    if not args.apply:
        print("\n（未加 --apply，只报告不写）")
        return 0

    stamp = time.strftime("%Y%m%d_%H%M%S")
    if to_do:
        shutil.copy2(CLAN, CLAN + ".bak_split_" + stamp)
        shutil.copy2(HERO, HERO + ".bak_split_" + stamp)
    if sett_fix:
        shutil.copy2(SETT, SETT + ".bak_split_" + stamp)

    for keep, move, cid1, cid2, why, st in to_do:
        src = cby[cid1]
        row = {c: "" for c in ccols}
        row.update(src)
        row["ID"] = cid2
        row["LocozationName"] = "{=my_%s}%s" % (cid2, (src.get("Surname") or "") + "-shi")
        row["Owner"] = ""
        crows.append(row)
        cby[cid2] = row
        hby[move]["ClanID"] = cid2
        print("  ✓ %s → %s（新家族行已加）" % (move, cid2))

    sby = {r["id"]: r for r in srows}
    for sid, e, o, a, b in sett_fix:
        sby[sid]["Clan_" + e] = b
        print("  ✓ %s %s：Clan_%s %s → %s" % (sid, e, e, a, b))

    if to_do:
        write(CLAN, ccols, crows)
        write(HERO, hcols, hrows)
    if sett_fix:
        write(SETT, scols, srows)

    # 往返校验：写回后重读，确认改动到位
    if to_do:
        _, c2 = load(CLAN)
        _, h2 = load(HERO)
        c2by = {r["ID"]: r for r in c2}
        h2by = {r["CNName"]: r for r in h2}
        for keep, move, cid1, cid2, why, st in to_do:
            if cid2 not in c2by:
                print("[FATAL] 写回后 Clan.csv 仍无 %s" % cid2)
                return 2
            if h2by[move].get("ClanID") != cid2:
                print("[FATAL] 写回后 %s 的 ClanID 不是 %s" % (move, cid2))
                return 2
    if sett_fix:
        _, s2 = load(SETT)
        s2by = {r["id"]: r for r in s2}
        for sid, e, o, a, b in sett_fix:
            if s2by[sid].get("Clan_" + e) != b:
                print("[FATAL] 写回后 %s 的 Clan_%s 不是 %s" % (sid, e, b))
                return 2
    print("\n✅ 已写回：Clan.csv %+d 行、TaikouHero.csv 改 %d 格、Settlements.csv 改 %d 格；往返校验通过"
          % (len(to_do), len(to_do), len(sett_fix)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
