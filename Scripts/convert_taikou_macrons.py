#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""太阁表 macron 清除：`ō/ū/ā/ī/ē` → 双写罗马音（2026-09-11 用户裁定）
============================================================================
**用户原话**：「你给英雄、clan、kingdom 取名字和 id，都不要带 `ō` 为代表的这些，需要转化下。」

**为什么是双写而不是去掉**：库里 `clan_mori_1`（森，短音）与 `clan_mōri_1`（毛利，长音）
**同时存在**——去掉 macron 两者会撞成同一个 id。所以按日语长音惯例**双写**：
  `ō→oo` / `ū→uu` / `ā→aa` / `ē→ee` / `ī→ii`（大写同理）
这也是库里的主流写法（43 个 id 已在用 `ou/uu`：`clan_ouchi_1` 大内、`clan_gamou_1` 蒲生…）。

**🔴 柳生家三合一（2026-09-11 用户裁定「三个柳生是一家」）**
  `clan_yagyū_1` / `clan_yagyūū_1` / `clan_yagyūūū_1` **不是三个条目**——是**祖孙三代同一家**
  （石舟斋 / 兵库助 / 宗矩各占一行），当年靠**重复 macron** 凑出三个不同 id。三者合并为一家
  `clan_yagyuu_1`（plan 里一直挂着的「柳生家三合一」待办，本次落实）：
    · 三个 ClanID → `clan_yagyuu_1`，`Clan.csv` **去重**只留一行
    · 留存行字段取三家共识：`Kingdom=noKingdom`、`Culture=neutral_culture`（各 2/3）
    · **名字取「柳生」**：另两行的 ScriptName/ChineseName 是个人名（柳生石/柳生兵），只有第三行是家名
    · 两位英雄的 `EnglishName` 一并修（`Yagyuuuu Hyoogo no suke`→`Yagyuu …`、`Yagyuuuuuu Munenori`→`Yagyuu …`）
  （`check_englishname_clan_prefix.py` 原先把这两行报成「长音符重复 typo」——方向对，但结论应是**合并**而非改名。）

**动作**：逐表逐列扫 macron 就地转换（含 id / 外键列 / 罗马音名 / 本地化键的回退文本）。
  `TaikouForce.csv` **不在此列**——它是 `gen_taikou_force_csv.py` 的产物，改源表后重跑生成器即可。

**纪律**：备份 + 往返校验 + 幂等两跑；写前做**撞车预检**（转换后 id 不得与既有 id 冲突）。

Usage:
  python Scripts/convert_taikou_macrons.py            # 报告 + 预览（不写）
  python Scripts/convert_taikou_macrons.py --apply    # 写回
Exit: 0 无待改或已改完 / 1 撞车或有硬问题 / 2 fatal。
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

# 转换表 = **用户既定规范**（原文见 `csv/ReadMe.csv`，是当年给 Excel VBA 写的规则）：
#   ā/Ā/â/Â/á/Á/ã/Ã → aa ; ē/Ē/ê/Ê/é/É → ee ; ī/Ī/î/Î/í/Í → ii
#   ō/Ō/ô/Ô/ó/Ó/õ/Õ → oo ; ū/Ū/û/Û/ú/Ú → uu ; ñ/Ñ → n ; ç/Ç → c
# 🔴 要点：①**输出一律小写**（`Ā`→`aa`）；②**acute 与 macron/circumflex 同等对待**
#    （`ó`→`oo`，不是「去掉重音」——早期版本按去掉做，与规范不符，已回滚重做）；
#    ③全库实际只出现过 `ō ū ô ē ó` 五种，其余条目是照规范补全防将来。
MAC = [("ā", "aa"), ("Ā", "aa"), ("â", "aa"), ("Â", "aa"), ("á", "aa"), ("Á", "aa"),
       ("ã", "aa"), ("Ã", "aa"),
       ("ē", "ee"), ("Ē", "ee"), ("ê", "ee"), ("Ê", "ee"), ("é", "ee"), ("É", "ee"),
       ("ī", "ii"), ("Ī", "ii"), ("î", "ii"), ("Î", "ii"), ("í", "ii"), ("Í", "ii"),
       ("ō", "oo"), ("Ō", "oo"), ("ô", "oo"), ("Ô", "oo"), ("ó", "oo"), ("Ó", "oo"),
       ("õ", "oo"), ("Õ", "oo"),
       ("ū", "uu"), ("Ū", "uu"), ("û", "uu"), ("Û", "uu"), ("ú", "uu"), ("Ú", "uu"),
       ("ñ", "n"), ("Ñ", "n"), ("ç", "c"), ("Ç", "c")]
MAC_CHARS = set("".join(k for k, _ in MAC))

# 柳生三合一：三个 id → 一个（同一家的祖孙三代，靠重复 macron 凑出的假 id）
ID_REMAP = {
    "clan_yagyū_1": "clan_yagyuu_1",
    "clan_yagyūū_1": "clan_yagyuu_1",
    "clan_yagyūūū_1": "clan_yagyuu_1",
}
# 合并后留存行的字段覆盖（名字取家名「柳生」——另两行是个人名）
#   ⚠️ 2026-09-12 起 Clan.csv 的列换成 `Name`（原 `ScriptName`/`ChineseName` 是逐字相同的两列，已合并）
ROW_MERGE_FILL = {"Clan.csv": {"clan_yagyuu_1": {"Name": "柳生"}}}
# 英雄名退化后缀修正（同源问题：靠重复 u 凑唯一名）——**在 macron 转换之后**按键匹配
#   TaikouHero 与 BaseInfo 是同一批人的两份表，写法略有出入（Hyoogo/Hyougo），各一条
HERO_NAME_FIX = {
    "Yagyuuuu Hyoogo no suke": "Yagyuu Hyoogo no suke",
    "Yagyuuuu Hyougo no suke": "Yagyuu Hyougo no suke",
    "Yagyuuuuuu Munenori": "Yagyuu Munenori",
}

# 要处理的表（TaikouForce.csv 是产物，排除）
#   ⚠️ 2026-09-12：Kingdom.csv 已归档（`csv/_archive/`）——它的 Culture/noKingdom 收编进 ForceTaikou.csv，
#      本表不再处理它（留着会报缺文件）。
TABLES = ["Clan.csv", "TaikouHero.csv", "Settlements.csv", "BaseInfo.csv"]
#   ⚠️ 2026-09-12：ForceTaikou.csv 已退役删除（快照口径是错误数据，以日志口径为准）

# 🔴 允许「按首列 id 去重」的表 —— **只放 Clan.csv**（柳生三合一是唯一需要合并的场景）。
#    BaseInfo.csv 的首列「内置番号」天然不唯一（1506 行 → 1047 个号），去重会误删 459 行。
ROW_DEDUP_TABLES = {"Clan.csv"}

ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]


def conv(s):
    if not s:
        return s
    for a, b in MAC:
        s = s.replace(a, b)
    return s


def has_mac(s):
    return any(c in MAC_CHARS for c in (s or ""))


def convert_value(v):
    """单元格转换的唯一入口。
    🔴 顺序要紧：`ID_REMAP` 的**键是转换前**的原值（`clan_yagyūū_1`），值已是终态——
       早期版本写成 `ID_REMAP.get(conv(v), conv(v))`，先转再查，**永远查不中** → 柳生三合一静默不生效。"""
    if v in ID_REMAP:
        return ID_REMAP[v]
    nv = conv(v)
    return HERO_NAME_FIX.get(nv, nv)


def load(path):
    with io.open(path, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = [(c or "").strip() for c in (rd.fieldnames or [])]
        rows = [{(k or "").strip(): (v or "") for k, v in r.items()} for r in rd]
        return cols, [r for r in rows if any(r.values())]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true")
    args = ap.parse_args()

    data, changes = {}, []
    for fn in TABLES:
        p = os.path.join(CSV_DIR, fn)
        if not os.path.isfile(p):
            print("[FATAL] 缺文件 %s" % p, file=sys.stderr)
            return 2
        cols, rows = load(p)
        data[fn] = (p, cols, rows)
        for i, r in enumerate(rows):
            for c in cols:
                v = r.get(c, "")
                if not (has_mac(v) or v in ID_REMAP or v in HERO_NAME_FIX):
                    continue
                nv = convert_value(v)
                if nv != v:
                    changes.append((fn, i, c, v, nv))

    # ── 撞车预检：转换后第一列（id）不得与既有 id 冲突 ──
    problems = []
    for fn, (p, cols, rows) in data.items():
        ids = set(r.get(cols[0], "") for r in rows)
        for i, r in enumerate(rows):
            old = r.get(cols[0], "")
            new = convert_value(old)
            if new != old and new in ids:
                problems.append("%s：%s → %s 与既有 id 冲突" % (fn, old, new))
        # 两个不同源 id 映射到同一目标 —— 只有登记在 ID_REMAP 同组的（柳生合并）放行
        tgt = {}
        for r in rows:
            old = r.get(cols[0], "")
            new = convert_value(old)
            prev = tgt.get(new)
            if prev is not None and prev != old:
                if not (old in ID_REMAP and prev in ID_REMAP and ID_REMAP[old] == ID_REMAP[prev]):
                    problems.append("%s：%s 与 %s 都映射到 %s（未登记的合并）" % (fn, prev, old, new))
            tgt[new] = old

    print("=== 待转换 %d 格 ===" % len(changes))
    by = {}
    for fn, i, c, v, nv in changes:
        by.setdefault(fn, []).append((c, v, nv))
    for fn in TABLES:
        if fn not in by:
            continue
        print("  %s（%d 格）" % (fn, len(by[fn])))
        seen = set()
        for c, v, nv in by[fn]:
            if (c, v) in seen:
                continue
            seen.add((c, v))
            print("      %-16s %-30s → %s" % (c, v, nv))

    if problems:
        print("\n❌ 撞车 %d 条：" % len(problems))
        for p in problems:
            print("   %s" % p)
        return 1
    if not changes:
        print("\n✅ 全表已无 macron，无需改动（幂等）")
        return 0
    if not args.apply:
        print("\n（未加 --apply，只报告不写）")
        return 0

    stamp = time.strftime("%Y%m%d_%H%M%S")
    for fn in TABLES:
        p, cols, rows = data[fn]
        if fn not in by:
            continue
        shutil.copy2(p, p + ".bak_macron_" + stamp)
        for i, r in enumerate(rows):
            for c in cols:
                v = r.get(c, "")
                if has_mac(v) or v in ID_REMAP or v in HERO_NAME_FIX:
                    r[c] = convert_value(v)
        # 🔴 合并去重**只对 Clan.csv 生效**（柳生三合一）。
        #    早期版本对所有表无条件去重 → BaseInfo.csv 的「内置番号」首列本来就不唯一，
        #    1506 行被砍成 1047 行（已从备份回滚）。加表名白名单，别再放开。
        if fn in ROW_DEDUP_TABLES:
            seen, kept = set(), []
            for r in rows:
                i = r.get(cols[0], "")
                if i in seen:
                    continue
                seen.add(i)
                kept.append(r)
            if len(kept) != len(rows):
                print("      %s：合并去重 %d 行 → %d 行" % (fn, len(rows), len(kept)))
                rows = kept
        # 合并后留存行的字段覆盖
        for r in rows:
            fill = ROW_MERGE_FILL.get(fn, {}).get(r.get(cols[0], ""))
            if fill:
                r.update(fill)
        buf = io.StringIO()
        w = csv.DictWriter(buf, fieldnames=cols, lineterminator="\r\n",
                           quoting=csv.QUOTE_MINIMAL, extrasaction="ignore")
        w.writeheader()
        for r in rows:
            w.writerow({c: (r.get(c) or "") for c in cols})
        text = buf.getvalue()
        # 往返校验
        back = list(csv.DictReader(io.StringIO(text)))
        if len(back) != len(rows):
            print("[FATAL] %s 往返行数不符" % fn)
            return 2
        for a, b in zip(rows, back):
            for c in cols:
                if (a.get(c) or "") != ((b.get(c) or "").strip()):
                    print("[FATAL] %s 往返不一致 列%s: %r vs %r" % (fn, c, a.get(c), b.get(c)))
                    return 2
        with io.open(p, "w", encoding="utf-8-sig", newline="") as fh:
            fh.write(text)
        print("  ✓ %s 已写回（备份 %s.bak_macron_%s）" % (fn, fn, stamp))

    # 终检：全表无 macron 残留
    left = []
    for fn in TABLES:
        _, rows = load(os.path.join(CSV_DIR, fn))
        for r in rows:
            for c, v in r.items():
                if has_mac(v):
                    left.append("%s.%s" % (fn, c))
    if left:
        print("[FATAL] 仍有 macron 残留：%s" % sorted(set(left)))
        return 2
    print("\n✅ 全部写回，macron 残留 0；往返校验通过")
    return 0


if __name__ == "__main__":
    sys.exit(main())
