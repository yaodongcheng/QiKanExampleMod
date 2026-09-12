#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""TaikouHero.csv 年代格就地修正（2026-09-11 用户裁定「修改真错」）
============================================================================
**管两类列**（`FIXES` 表里写全列名）：
  · `Kingdom_<年>` —— 该年此人效力哪个势力
  · `Identity_<年>` —— 该年此人的身份档位（2026-09-11 追加：与 Kingdom 同源的**配对错**一起修，
    如「Kingdom 写成自己家 + Identity 抬成大名」是同一处错误的两半）

**问题**：这些格与 **Snr 剧本快照**矛盾。

**权威依据**：`Knowledge/太阁5/骑砍2织丰角色ID对应/_analysis/decoded/era_v2/<年>/persons.csv`
  的 `force_name`（README 声明「文件直读、无推断」）。**join 键必须是编号**——
  `person_id` == TaikouHero.`原版编号`（实证：14=明智光秀 / 119=上杉谦信 / 379=柴田胜家）。
  🔴 **禁止按人名 join**：快照的 `name` 列是旧路径映射、自己 README 就标了
     「勿再引用本行作权威」，按名字 join 会把明智光秀、柴田胜家都算成「芦名家」。

**这 6 格是什么性质**：其余对不上的（赤松家/虎屋/秋田家/池田家…）是**口径差异**，
  不是错——英雄表记「当年跟着谁（含同族家名）」，快照记「势力槽」。
  **只有下面几格是两边都有值、但指向了两个不同的势力**，是真错。

🔴 **快照不是万能权威——「过继/改仕」类时间点必须查史料**（2026-09-11 用户裁定，实错一次）：
  `上杉景虎` 曾被本脚本按快照改成「1568=上杉家」，**改错了**：
    · 史料：他（北条氏康七子）成为上杉谦信养子是**元龟元年（1570）**；1568 只是越相同盟
      的交涉/议和年（同盟本身多记 1569）→ **1568 他仍是北条家的人**
    · 表内自证：同一行 `City_1568 = 小田原城`（北条本城）；快照城表也写小田原城属北条家
    · 快照为何不同：太阁5 把 1568 剧本里的他直接放进了上杉家（游戏简化，非史实）
  → **结论：涉及过继/改仕/改名的年代边界，以史料 + 表内 City 自洽为准，快照只能当旁证。**
    这类格子登记进下方 `KNOWN_DEVIATIONS`，脚本**不报错、不改正**，并在报告里显式列出。

**修法**：按 (人名, 年代) 定位单元格，改写为 `目标值`。
  · 改前必须等于 `旧值`（对不上就报错停手，防表格已被别处改过）
  · 改后必须等于快照值 **除非该格已登记进 `KNOWN_DEVIATIONS`**（登记格跳过快照核对，仅提示）

**纪律**：备份 + 往返校验（写回后逐格比对）+ 幂等两跑（第二次必须 0 改动）。
🔴 生成物·改数据走脚本（铁律 22 的同精神）——本表虽为手工维护源表，
  但改动一律经本脚本，留审计轨迹。

Usage:
  python Scripts/fix_taikou_hero_kingdom.py            # 报告 + 预览（不写）
  python Scripts/fix_taikou_hero_kingdom.py --apply    # 写回 CSV
Exit: 0 无待修或已改完 / 1 有格与预期不符（需人工看） / 2 fatal。
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
CSV_PATH = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "TaikouHero.csv")
SNAP_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "_analysis", "decoded", "era_v2")

# (人名, 列名, 旧值, 新值, 依据一句话)
FIXES = [
    ("天野隆重", "Kingdom_1554", "大内家", "毛利家", "大宁寺之变(1551)后即从属毛利元就，1554 已是毛利人（原表错）"),
    ("太田氏资", "Kingdom_1554", "太田家", "北条家", "太田家 1554 无势力槽（快照 0 人）；他是太田三乐斋陪臣、随主挂北条"),
    ("太田三乐斋", "Kingdom_1554", "太田家", "北条家", "太田家 1554 无势力槽；快照城表：岩槻城 城主=他 势力=北条家"),
    ("有马晴信", "Kingdom_1560", "有马家", "龙造寺家", "孤格：前后年（1554/1568/1575/1582）都是龙造寺家，只 1560 是有马家"),
    # 🔴 回退条目：本脚本上一版按快照误改，用户裁定按史料回退（见下方 KNOWN_DEVIATIONS）
    ("上杉景虎", "Kingdom_1568", "上杉家", "北条家", "1570 才过继上杉；1568 仍是北条（City_1568=小田原城 自证）"),
    ("吉见正赖", "Kingdom_1554", "毛利家", "大内家", "1557 大内灭亡后才从属毛利；1554 八月刚与大内/陶和睦（原表对）"),
    # ── Identity 列：与 Kingdom 同源的配对错，一起修 ──
    ("太田三乐斋", "Identity_1554", "大名", "城主",
     "快照 1554 城表：岩槻城 城主=他、势力=北条家 → 他是北条家的城主，不是独立大名；"
     "他自己 1568（改投佐竹后）的 Identity 就是「城主」，同一情形同一档。"
     "不改则北条家 1554 会同时出现两个「大名」（他与北条氏康）"),
]

# 🔴 已知「故意与快照不一致」的格子：(人名, 年代) → 为什么
#    登记在此的格子：跳过快照交叉核对（快照本身在这一点上是错的），其余校验照走。
KNOWN_DEVIATIONS = {
    ("上杉景虎", "1568"):
        "太阁5 把 1568 剧本里的他直接放进上杉家（游戏简化）；史料=1570 元龟元年才过继，"
        "1568 仍属北条，与同表 City_1568=小田原城、快照城表小田原城属北条家 三重自洽。",
    ("吉见正赖", "1554"):
        "史料：1553-10 对陶晴贤举兵 → 1554-08 与大内/陶和睦（送子入山口为质）→"
        "1555 严岛 → 1557 大内灭亡后才从属毛利（弘治3年）。1554 不是毛利人。"
        "快照与快照城表（益田城=毛利家）均把石见提前算作毛利领地，与史实不符。",
}


def load_csv(path):
    with io.open(path, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = [(c or "").strip() for c in (rd.fieldnames or [])]
        rows = []
        for r in rd:
            if any((v or "").strip() for v in r.values()):
                rows.append({(k or "").strip(): (v or "") for k, v in r.items()})
        return cols, rows


def snapshot_force(era, pid):
    """读快照 persons.csv：原版编号 -> force_name（编号 join，禁人名 join）。"""
    p = os.path.join(SNAP_DIR, era, "persons.csv")
    if not os.path.isfile(p):
        return None
    with io.open(p, encoding="utf-8-sig", newline="") as fh:
        for r in csv.DictReader(fh):
            if (r.get("person_id") or "").strip() == str(pid):
                return (r.get("force_name") or "").strip()
    return ""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="写回 CSV（默认只报告）")
    args = ap.parse_args()

    if not os.path.isfile(CSV_PATH):
        print("[FATAL] 缺文件 %s" % CSV_PATH, file=sys.stderr)
        return 2
    cols, rows = load_csv(CSV_PATH)
    for need in ("CNName", "OriginalID"):
        if need not in cols:
            print("[FATAL] CSV 缺列 %s（列结构变了，本脚本需同步）" % need, file=sys.stderr)
            return 2

    # 人名 -> 行。重名直接报错（本表人名应唯一；不唯一时必须改用 ID 定位）
    byname = {}
    for i, r in enumerate(rows):
        n = r.get("CNName", "")
        if n in byname:
            print("[FATAL] CNName 重复：%s（行 %d 与 %d）——本脚本按人名定位，重名必须先改用 ID"
                  % (n, byname[n], i), file=sys.stderr)
            return 2
        byname[n] = i

    problems, changes = [], []
    for who, col, old, new, why in FIXES:
        era = col.split("_", 1)[1]          # Kingdom_1554 / Identity_1554 → 1554
        if who not in byname:
            problems.append("%s：表里没有这个人" % who)
            continue
        r = rows[byname[who]]
        if col not in cols:
            problems.append("%s：表里没有列 %s" % (who, col))
            continue
        cur = (r.get(col) or "").strip()
        pid = (r.get("OriginalID") or "").strip()
        deviation = KNOWN_DEVIATIONS.get((who, era))
        if deviation:
            # 登记格：快照在这一点上是错的，跳过快照核对，只做「现值==目标值」幂等判定
            print("  %-8s %s  %-6s → %-6s   %s" % (who, era, cur, new, why))
            print("       🔴 KNOWN_DEVIATION（不对快照）：%s" % deviation)
            if cur == new:
                continue
            if cur != old:
                problems.append("%s %s：现值 %r 既不是旧值 %r 也不是新值 %r"
                                % (who, era, cur, old, new))
                continue
            changes.append((byname[who], col, who, era, cur, new))
            continue
        snap = snapshot_force(era, pid) if (pid and col.startswith("Kingdom_")) else None
        snap_tag = ""
        if not col.startswith("Kingdom_"):
            snap_tag = "  （Identity 列，快照只对 force_name，不适用）"
        elif snap is None:
            snap_tag = "  （快照文件缺，未交叉核对）"
        elif snap == "":
            snap_tag = "  ⚠️ 快照无此人（编号不在该年表里）"
        elif snap != new:
            problems.append("%s %s：新值 %s 与快照 %s 不一致——先查清再改" % (who, era, new, snap))
            continue
        else:
            snap_tag = "  快照=%s ✓" % snap
        print("  %-8s %s  %-6s → %-6s   %s%s" % (who, era, cur, new, why, snap_tag))
        if cur == new:
            continue                       # 已是目标值（幂等）
        if cur != old:
            problems.append("%s %s：现值 %r 既不是旧值 %r 也不是新值 %r——表格可能已被别处改过"
                            % (who, era, cur, old, new))
            continue
        changes.append((byname[who], col, who, era, cur, new))

    print()
    if problems:
        print("❌ 有 %d 处与预期不符（未做任何写入）：" % len(problems))
        for p in problems:
            print("   %s" % p)
        return 1
    if not changes:
        print("✅ %d 格全部已是目标值，无需改动（幂等）" % len(FIXES))
        return 0

    print("待改 %d 格。" % len(changes))
    if not args.apply:
        print("（未加 --apply，只报告不写）")
        return 0

    # ── 写入 ──
    bak = CSV_PATH + ".bak_kingdom_" + time.strftime("%Y%m%d_%H%M%S")
    shutil.copy2(CSV_PATH, bak)
    print("备份 → %s" % os.path.basename(bak))

    for idx, col, who, era, cur, new in changes:
        rows[idx][col] = new

    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=cols, lineterminator="\r\n",
                       quoting=csv.QUOTE_MINIMAL, extrasaction="ignore")
    w.writeheader()
    for r in rows:
        w.writerow({c: (r.get(c) or "") for c in cols})
    text = buf.getvalue()

    # 往返校验：写出的内容重读，逐格与内存一致
    back = list(csv.DictReader(io.StringIO(text)))
    if len(back) != len(rows):
        print("[FATAL] 往返行数不符 %d != %d" % (len(back), len(rows)))
        return 2
    for i, (a, b) in enumerate(zip(rows, back)):
        for c in cols:
            if (a.get(c) or "") != (b.get(c) or ""):
                print("[FATAL] 往返不一致 行%d 列%s: %r vs %r" % (i, c, a.get(c), b.get(c)))
                return 2

    with io.open(CSV_PATH, "w", encoding="utf-8-sig", newline="") as fh:
        fh.write(text)
    print("✅ 已写回 %d 格，往返校验通过" % len(changes))
    return 0


if __name__ == "__main__":
    sys.exit(main())
