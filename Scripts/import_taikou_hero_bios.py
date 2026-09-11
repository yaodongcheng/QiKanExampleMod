#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""太阁5 人物「生年 + 列传」导入器（tkhack 实机日志 → TaikouHero.csv）
============================================================================
数据来源（唯一）
----------------
  `E:\\TKHACK\\log\\太阁出生年、列传信息.log`
    由 tkhack 脚本 `Knowledge/太阁5/太阁5脚本/导出全部人物生年列传.txt` 在 **1560 剧本**
    实机导出。格式：`BIO|序号|人物番号|名字|生年|生卒年|列传正文`，列传跨行（续行无前缀）。

  🔴 **人物番号 = 太阁正典编号**，已用权威对照表逐条验证
     （195=織田信長 / 110=今川義元 / 613=風魔小太郎 / 683=松浦鎮信 / 729=百地三太夫；
      506 在 1560 剧本里显示为当年龄名「松平元康」，同一人）。
     **CSV 第 n 行（0 基）= 人物番号 n** —— 800 行逐行对得上，本脚本按番号对齐，不靠名字
     （CSV 用后名「上杉謙信」，日志用 1560 年当年龄名「長尾景虎」，名字对不齐属正常）。

本脚本做什么
------------
  给 `csv/TaikouHero.csv` **追加三列**（其余列一律原样保留，改写后做逐格往返校验）：

    列传      列传的本地化 StringId，形如 `TAIKOU_bio_195`；无列传者留空
    列传原文  列传正文原文（**繁体**，与日志一字不差——可回溯、可复核）
    列传简体  上一列经 opencc t2s 转出的简体（**生成物**，供本地化层直接取用）

  🔴 为什么原文与简体都存（不二选一）
   ① CSV 既有约定就是「繁体原文 + 简体」并列（列传的 `列传原文` 繁体 / `列传简体` 简体 —— 与 `CNName` 主名同思路），列传照办；
   ② 简体的唯一消费方是本地化层，而 opencc 对人名会出错（澀谷/涉谷、麵/面）——
      落成 CSV 单元格 = 可人工复核可修正；只留在生成器里 = 错也看不见；
   ③ 转换只在**本脚本**（一次性、可重跑）发生，下游生成器读列即用，不背 opencc 依赖。

  并输出一份对账报告（番号覆盖 / 生年与 CSV 现有 BirthYear 的差异 / 空值统计）。

用法
----
  python Scripts/import_taikou_hero_bios.py --dry-run   # 只出报告，不写文件
  python Scripts/import_taikou_hero_bios.py             # 写回 CSV
"""
import argparse
import csv
import io
import json
import os
import re
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

LOG_PATH = r"E:\TKHACK\log\太阁出生年、列传信息.log"
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSV_PATH = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "TaikouHero.csv")

# 列传覆盖的人物番号段：1560 剧本的 800 人口池 = 番号 0-799（CSV 行 0-799）
POOL_LO, POOL_HI = 0, 799

# 日志行：`<时间戳> [INFO][...] Log: BIO|序号|人物番号|名字|生年|生卒年|列传…`
LINE_RE = re.compile(r"Log: (BIO\|.*)$")

BIO_KEY = "TAIKOU_bio_%d"
COL_KEY = "列传"
COL_TEXT = "列传原文"
COL_SIMP = "列传简体"

try:
    import opencc
    _CC = opencc.OpenCC("t2s")
except Exception as exc:                                    # noqa: BLE001
    _CC = None
    _CC_ERR = exc


def parse_log(path):
    """→ [ {no, name, birth_year, birth_death, bio}, … ]（列传续行已拼接）"""
    if not os.path.isfile(path):
        raise SystemExit(f"[FATAL] 找不到日志：{path}")
    recs, cur, malformed = [], None, 0
    with io.open(path, encoding="utf-8", errors="replace") as fh:
        for raw in fh:
            line = raw.rstrip("\r\n")
            m = LINE_RE.search(line)
            if m:
                if cur is not None:
                    recs.append(cur)
                parts = m.group(1).split("|", 6)
                if len(parts) < 6:
                    malformed += 1
                    cur = None
                    continue
                cur = {"serial": parts[1], "no": parts[2], "name": parts[3],
                       "birth_year": parts[4], "birth_death": parts[5],
                       "bio": parts[6] if len(parts) > 6 else ""}
            elif cur is not None:
                cur["bio"] += line          # 续行 = 上一条列传的换行处
    if cur is not None:
        recs.append(cur)
    for r in recs:
        r["no"] = int(r["no"])
    return recs, malformed


def main():
    ap = argparse.ArgumentParser(description="Taikou hero bio importer")
    ap.add_argument("--dry-run", action="store_true", help="只出报告，不写文件")
    ap.add_argument("--log", default=LOG_PATH, help="tkhack 日志路径")
    args = ap.parse_args()

    recs, malformed = parse_log(args.log)
    by_no = {r["no"]: r for r in recs}
    print(f"日志：{len(recs)} 条记录（解析异常 {malformed} 条）"
          f"  番号 {min(by_no)}..{max(by_no)}")

    with io.open(CSV_PATH, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = list(rd.fieldnames)
        rows = list(rd)

    problems, warns = [], []

    # ── ⓪ 前置断言：行号必须等于 DX 号（本脚本按行号对齐日志，不靠名字）──
    #    🔴 2026-09-11 补：CSV 已按 DX 号重排（`merge_era_names_and_sort.py`），行 0-799
    #       恰好是 DX 0-799。此假设若被破坏（有人手动插行/再排序），下面的番号对齐会**静默错位** ——
    #       故加断言当场拦下，不靠"记得"。
    bad_idx = [(n, rows[n].get("ID")) for n in range(POOL_LO, min(POOL_HI, len(rows) - 1) + 1)
               if rows[n].get("ID") != "lord_tk5_%d" % n]
    if bad_idx:
        problems.append(f"行号 ≠ DX 号的有 {len(bad_idx)} 行（前 5：{bad_idx[:5]}）——"
                        f"CSV 行序被改动过，本脚本不能按行号对齐，拒绝继续")

    # ── ① 覆盖检查：番号必须连续覆盖 0..799，且每行当前是 A 段 ──
    gaps = [n for n in range(POOL_LO, POOL_HI + 1) if n not in by_no]
    if gaps:
        problems.append(f"日志缺少番号 {len(gaps)} 个（前 10：{gaps[:10]}）——导出不完整，拒绝写回")
    extra = [n for n in by_no if not (POOL_LO <= n <= POOL_HI)]
    if extra:
        warns.append(f"日志含池外番号 {len(extra)} 个（T4 后再处理）：{sorted(extra)[:10]}")
    empty_bio = [r["no"] for r in recs if not r["bio"].strip()]
    if empty_bio:
        warns.append(f"列传为空的 {len(empty_bio)} 条：{empty_bio[:10]}")
    if POOL_HI >= len(rows):
        problems.append(f"CSV 只有 {len(rows)} 行，装不下番号 {POOL_HI}")

    # ── ② 生年对账（不自动改，只报告）──
    year_diff = []
    for n in range(POOL_LO, min(POOL_HI, len(rows) - 1) + 1):
        if n not in by_no:
            continue
        cur = (rows[n].get("BirthYear") or "").strip()
        log = by_no[n]["birth_year"].strip()
        if cur != log:
            year_diff.append((n, rows[n].get("CNName", ""), cur, log))
    print(f"生年与 CSV 现有 BirthYear 不一致：{len(year_diff)} 条")
    for d in year_diff[:15]:
        print(f"   番号 {d[0]:>4} {d[1]:<10} CSV={d[2]:<8} 日志={d[3]}")

    # ── ③ 列传存进新列（原文 + 简体）──
    if not _CC:
        print(f"[FATAL] 需要 opencc 做繁→简转换，导入失败：{_CC_ERR}\n"
              f"        安装：pip install opencc")
        return 2
    for c in (COL_KEY, COL_TEXT, COL_SIMP):
        if c in cols:
            warns.append(f"CSV 已存在列 {c}——本次为**重写**该列")
            cols.remove(c)
        for r in rows:
            r.pop(c, None)

    n_key = n_text = 0
    conv_changed = []
    for n in range(POOL_LO, min(POOL_HI, len(rows) - 1) + 1):
        rec = by_no.get(n)
        if not rec or not rec["bio"].strip():
            rows[n][COL_KEY] = ""
            rows[n][COL_TEXT] = ""
            rows[n][COL_SIMP] = ""
            continue
        orig = rec["bio"].strip()
        simp = _CC.convert(orig)
        rows[n][COL_KEY] = BIO_KEY % n
        rows[n][COL_TEXT] = orig
        rows[n][COL_SIMP] = simp
        n_key += 1
        n_text += 1
        if simp != orig:
            conv_changed.append(n)
    # 池外行留空
    for i in range(POOL_HI + 1, len(rows)):
        rows[i][COL_KEY] = ""
        rows[i][COL_TEXT] = ""
        rows[i][COL_SIMP] = ""

    print(f"写入列传：key {n_key} 条 / 正文 {n_text} 条 / 其中 {len(conv_changed)} 条繁→简有改动")

    if problems:
        print(f"\n[FATAL] 自检未通过（{len(problems)} 条）：")
        for p in problems:
            print("  - " + p)
        return 1
    for w in warns:
        print(f"  [WARN] {w}")

    if args.dry_run:
        print("\n--dry-run：未写文件。")
        return 0

    # ── ④ 写回 + 逐格往返校验 ──
    out_cols = cols + [COL_KEY, COL_TEXT, COL_SIMP]
    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=out_cols, lineterminator="\r\n",
                       quoting=csv.QUOTE_MINIMAL, extrasaction="ignore")
    w.writeheader()
    for r in rows:
        w.writerow({c: (r.get(c) or "") for c in out_cols})
    text = buf.getvalue()

    # 校验：重读写出的文本，原列必须逐格一致
    back = list(csv.DictReader(io.StringIO(text)))
    if len(back) != len(rows):
        print(f"[FATAL] 往返行数不符：{len(back)} != {len(rows)}")
        return 1
    for i, (a, b) in enumerate(zip(rows, back)):
        for c in cols:
            if (a.get(c) or "") != (b.get(c) or ""):
                print(f"[FATAL] 往返不一致：第 {i} 行 列 {c!r}\n"
                      f"        原={a.get(c)!r}\n        新={b.get(c)!r}")
                return 1
    print(f"往返校验通过：{len(rows)} 行 × {len(cols)} 原列逐格一致")

    with io.open(CSV_PATH, "w", encoding="utf-8-sig", newline="") as fh:
        fh.write(text)
    print(f"已写回 {CSV_PATH}（{len(out_cols)} 列）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
