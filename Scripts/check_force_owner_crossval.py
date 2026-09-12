#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""🔴 已停用（2026-09-12）—— 输入之一 ForceTaikou.csv 已退役删除，本检查无输入，不再进一键体检。
保留作历史：它验证过的结论见 plans「0.8 五·A3」。

势力当主「双口径」交叉验证（ForceTaikou.csv × TaikouForce.csv）
============================================================================
**为什么有这条检查**（2026-09-12 用户裁定：「需要保留，并且要时刻保持交叉验证」）：
两张表各有一套 `Owner_<年>` ×6，是**同一个概念的两种口径**：

| 表 | 口径 | 来历 |
|---|---|---|
| `ForceTaikou.csv`（源表） | 势力槽快照 | 当年从游戏数据挖的（织丰编号，本项目不解析，只留档） |
| `TaikouForce.csv`（产物） | 上级日志 | 生成器按运行时导出的日志重算 —— **进游戏的就是这套** |

🔴 **两张表的列名故意都叫 `Owner_<年>`**——区分靠**文件名**，不靠列名
   （2026-09-12 用户裁定：不许自作主张给列名加前缀/描述词）。
  两套口径留着**互相对照**才有价值：快照有**解码空洞**（雷 85/87，把真势力误标成不存在），
  所以口径换成了日志；但快照不能删——它是那份口径的唯一逐格记录
  （`_analysis/` 下的派生表只是**中间状态文件**，不作留档依据）。

**桥：织丰 id → `lord_tk5_*`**（两套编号不是一套，必须先桥）
  ① slug 直桥：`lord_1_amago_haruhisa` → 去掉前缀与尾序号 → 对英雄表 `EnglishName`
  ② 转储桥：`shokuho_heroes_full.csv` 的 `StringId` → `NameRaw` → 对英雄表 `EnglishName`
  实测 188/188 全部可解析 —— **桥断了 = 英雄表或转储变了 = 红**（否则后面的比对全是假的）。

**逐格判定四类**（185 行 × 6 年代）：
  · `一致`           —— 两口径同一个人
  · `日志有/快照无`  —— 日志发现了快照没有的当主（雷 85/87：快照解码空洞的主战场）
  · `都有但人不同`   —— 口径差：快照记「这家派出的代表」，日志记「谁是一家之主」
  · `快照有/日志无`  —— 快照说存在、日志说没有（**最值得看的一类，通常个位数**）

**判定口径（两级，同全项目纪律）**：
  ❌ 硬错误：源表 `Owner_<年>` 六列缺失/被清空 · 值非法（既非空/`-`/`@模板`/织丰编号）· 桥解析率 < 100%
  ❌ 漂移：四类计数 ≠ `BASELINE` —— **改任一源表 / 重导上级日志 / 改生成器都会动这个数**，
     红了就是「有人动了其中一侧」，列出来给人判（判完把 `BASELINE` 更新成新值即为确认）

Usage:
  python Scripts/check_force_owner_crossval.py            # 摘要
  python Scripts/check_force_owner_crossval.py -v         # 列出全部分歧格
  python Scripts/check_force_owner_crossval.py --module X # 兼容 run_all_checks 接口（忽略）
Exit: 0 一致 / 1 有硬错误或漂移。
"""
import argparse
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

ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
# 🔴 两张表的列名相同（都叫 `Owner_<年>`）——区分靠**文件**，不靠列名（2026-09-12 用户裁定）
OWNER_COLS = ["Owner_" + e for e in ERAS]

# 快照列的合法取值：空 · `-` · `@商人/@忍者/@海贼`（模板标记）· 织丰编号（旧体系，只留档不解析）
TPL_MARKS = {"@商人", "@忍者", "@海贼"}
SHOKUHO_PREFIX = ("lord_1_", "lord_2_", "lord_3_", "dead_lord_", "spc_", "sho_")

# 🔴 基线（2026-09-12 首次建立）。四类计数变了 = 有人动了快照 / 日志 / 生成器 → 红 → 人来判。
#    确认无误后把本表更新成新值即为「确认」。
BASELINE = {
    "一致": 324,
    "日志有/快照无": 158,
    "都有但人不同": 71,
    "快照有/日志无": 1,
}
# 🔴 2026-09-12 更新（原 332/130/63/1）：泛用 NPC 提升为 hero 后，TaikouForce.Owner 变了——
#    ①原来 28 格「当主是泛用 NPC → 该年不出势力」现在有真当主 → 日志有/快照无 +28
#    ②15 格「实名替补」消失（换成真当主）→ 「一致」与「都有但人不同」重新归类
#    两类变动都由本次提升解释、无意外。

PREFIX_RE = re.compile(r"^(lord_\d+_|dead_lord_\d+_|spc_|sho_)")
SUFFIX_RE = re.compile(r"_\d+$")


def norm(s):
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


def load(path):
    with io.open(path, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = [(c or "").strip() for c in (rd.fieldnames or [])]
        rows = [{(k or "").strip(): (v or "").strip() for k, v in r.items()} for r in rd
                if any((v or "").strip() for v in r.values())]
    return cols, rows


def main():
    ap = argparse.ArgumentParser(description="势力当主双口径交叉验证")
    ap.add_argument("-v", "--verbose", action="store_true", help="列出全部分歧格")
    ap.add_argument("--module", default=None, help="兼容 run_all_checks 接口（本检查不读模块）")
    args = ap.parse_args()

    src_p = os.path.join(CSV_DIR, "ForceTaikou.csv")
    prod_p = os.path.join(CSV_DIR, "TaikouForce.csv")
    hero_p = os.path.join(CSV_DIR, "TaikouHero.csv")
    dump_p = os.path.join(CSV_DIR, "shokuho_heroes_full.csv")
    for p in (src_p, prod_p, hero_p, dump_p):
        if not os.path.isfile(p):
            print("[FATAL] 缺文件 %s" % p, file=sys.stderr)
            return 2

    _, src = load(src_p)
    _, prod = load(prod_p)
    _, heroes = load(hero_p)
    _, dump = load(dump_p)

    hard, drift = [], []

    # ── 结构闸门①：快照六列必须在，且没被清空 ──
    cols = list(src[0].keys()) if src else []
    missing = [c for c in OWNER_COLS if c not in cols]
    if missing:
        hard.append("ForceTaikou.csv 缺 %s —— 被误删了？（恢复：Scripts/restore_force_owner_columns.py）"
                    % " ".join(missing))
    n_cell = 0
    n_empty = 0
    for r in src:
        if r.get("ID") == "ID":                 # 英文表头行（DictReader 会当数据行读进来）
            continue
        for c in OWNER_COLS:
            v = r.get(c, "")
            n_cell += 1
            if not v or v == "-":
                n_empty += 1
            elif v not in TPL_MARKS and not v.startswith(SHOKUHO_PREFIX):
                hard.append("%s %s = %s（既不是空/`-`/模板标记，也不是织丰编号）"
                            % (r.get("ID"), c, v))
    if n_cell and n_empty == n_cell:
        hard.append("ForceTaikou.csv 的 Owner_<年> 六列**全是空** —— 数据被清空")

    # ── 建桥：织丰 id → lord_tk5_* ──
    by_en = {}
    for r in heroes:
        if r.get("EnglishName"):
            by_en.setdefault(norm(r["EnglishName"]), r["ID"])
    dump_en = {r["StringId"]: norm(r.get("NameRaw")) for r in dump if r.get("StringId")}

    def bridge(v):
        if not v or v == "-" or v in TPL_MARKS:
            return None
        return (by_en.get(norm(SUFFIX_RE.sub("", PREFIX_RE.sub("", v))))
                or by_en.get(dump_en.get(v) or ""))

    # ── 逐格交叉验证 ──
    prod_by_id = {r["ID"]: r for r in prod if r.get("ID") != "ID"}
    src_by_id = {r["ID"]: r for r in src if r.get("ID") != "ID"}
    cnt = collections.Counter()
    detail = collections.defaultdict(list)
    unbridged = set()
    for fid, p in sorted(prod_by_id.items()):
        s = src_by_id.get(fid)
        if s is None:
            continue
        for e in ERAS:
            raw = s.get("Owner_" + e, "")
            a = bridge(raw)
            if raw and raw != "-" and raw not in TPL_MARKS and a is None:
                unbridged.add(raw)
            b = p.get("Owner_" + e, "")
            b = None if (not b or b == "-") else b
            if a and b and a == b:
                cnt["一致"] += 1
            elif a and b:
                cnt["都有但人不同"] += 1
                detail["都有但人不同"].append((p.get("势力名"), e, raw, a, b))
            elif a and not b:
                cnt["快照有/日志无"] += 1
                detail["快照有/日志无"].append((p.get("势力名"), e, raw, a, ""))
            elif b and not a:
                cnt["日志有/快照无"] += 1
                detail["日志有/快照无"].append((p.get("势力名"), e, raw, "", b))
    if unbridged:
        hard.append("桥不上（织丰 id 解析不出 lord_tk5_*）%d 个：%s"
                    % (len(unbridged), " ".join(sorted(unbridged)[:8])))

    # ── 基线比对（漂移 = 有人动了其中一侧）──
    total = sum(cnt.values())
    for k, base in BASELINE.items():
        if cnt.get(k, 0) != base:
            drift.append("%s：%d → %d（基线 %d）" % (k, base, cnt.get(k, 0), base))

    # ── 报告 ──
    print("势力当主双口径交叉验证（ForceTaikou.csv 快照口径 × TaikouForce.csv 日志口径，列名同为 Owner_<年>）")
    print("  逐格 %d 格（%d 行 × 6 年代；快照非空 %d 格 / 日志非空 %d 格）"
          % (total, len(prod_by_id), n_cell - n_empty, sum(1 for p in prod_by_id.values()
                                                          for c in OWNER_COLS if p.get(c, "") not in ("", "-"))))
    for k in ("一致", "日志有/快照无", "都有但人不同", "快照有/日志无"):
        n = cnt.get(k, 0)
        print("  %-16s %4d  (%.0f%%)%s" % (k, n, 100.0 * n / total if total else 0,
                                           "  ← 基线 %d" % BASELINE[k] if k in BASELINE else ""))

    for k in ("快照有/日志无", "都有但人不同"):
        items = detail.get(k) or []
        if not items:
            continue
        show = items if args.verbose else items[:8]
        print("\n【%s】%d 条%s：" % (k, len(items), "" if args.verbose else "（-v 看全）"))
        for x in show:
            fid, e, raw, a, b = x[0], x[1], x[2], x[3], x[4]
            print("    %-10s %s  快照=%-28s 日志=%s" % (fid, e, raw or "-", b or "-"))

    if hard:
        print("\n❌ 硬错误 %d 条：" % len(hard))
        for x in hard[:20]:
            print("   %s" % x)
    if drift:
        print("\n❌ 与基线不一致（有人动了快照/日志/生成器之一）%d 条：" % len(drift))
        for x in drift:
            print("   %s" % x)
        print("   → 确认无误后，把本脚本的 BASELINE 更新为新值即为确认")

    if hard or drift:
        print("\n结果：红（硬错误 %d + 漂移 %d）" % (len(hard), len(drift)))
        return 1
    print("\n结果：绿（两口径逐格已对照，计数与基线一致）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
