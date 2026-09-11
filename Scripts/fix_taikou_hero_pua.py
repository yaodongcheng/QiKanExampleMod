#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""TaikouHero.csv 私用区字符就地还原（2026-09-11 用户裁定「改成正确的」）
============================================================================
**问题**：太阁5 用自绘字形槽渲染部分汉字，tkhack dump 出来是 Unicode 私用区码点
（`高<U+E426>城` 实为「高槻城」）。英雄表里它们混在几个列里 —— 终端画不出来，
看着像掉字，实际是脏数据。**已漏进语言包**：`TAIKOU_bio_704` 的列传带着 U+F724 出到
`Languages/{std,CNs}/std_Taikou_strings.xml`，游戏里会显示成豆腐块。

**修法**：按 `Scripts/tk5_pua_names.PUA_TO_CHAR` 就地还原，**全列**扫（不只 City_<年> ——
初版我只查了 City，漏了 `Alias` / `Name_<年>` 的「三刀屋久<E41A>」和列传的 U+F724）。

**纪律**：往返校验（写回后重读逐格比对）+ 幂等两跑（第二次必须 0 改动）+ 备份。

Usage:
  python Scripts/fix_taikou_hero_pua.py            # 报告 + 预览（不写）
  python Scripts/fix_taikou_hero_pua.py --apply    # 写回 CSV
Exit: 0 无待修 / 1 有未识别码点（需先补表）/ 2 fatal。
"""
import argparse
import collections
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

sys.path.insert(0, HERE)
from tk5_pua_names import PUA_TO_CHAR, is_pua, restore   # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="写回 CSV（默认只报告）")
    args = ap.parse_args()

    with io.open(CSV_PATH, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = list(rd.fieldnames)
        rows = list(rd)

    hits, unknown = collections.Counter(), collections.Counter()
    for r in rows:
        for k, v in r.items():
            if not v:
                continue
            for ch in v:
                if is_pua(ch):
                    (hits if ord(ch) in PUA_TO_CHAR else unknown)[(k, ch)] += 1

    print("TaikouHero.csv：%d 行 × %d 列" % (len(rows), len(cols)))
    print("\n待还原（%d 格）：" % sum(hits.values()))
    for (k, ch), n in sorted(hits.items()):
        print("   %-16s U+%04X ×%d → %s" % (k, ord(ch), n, PUA_TO_CHAR[ord(ch)]))
    if unknown:
        print("\n🔴 还原表里没有的码点（先补 tk5_pua_names.PUA_TO_CHAR 再来，别硬猜）：")
        for (k, ch), n in sorted(unknown.items()):
            print("   %-16s U+%04X ×%d" % (k, ord(ch), n))
        return 1
    if not hits:
        print("\n✅ 无待还原项（幂等）")
        return 0
    if not args.apply:
        print("\n未写文件（加 --apply 写回）。")
        return 0

    # 备份 → 还原 → 写回
    bak = CSV_PATH + ".bak_pua_%s" % time.strftime("%Y%m%d_%H%M%S")
    shutil.copy2(CSV_PATH, bak)
    for r in rows:
        for k, v in list(r.items()):
            if v and any(is_pua(c) for c in v):
                r[k] = restore(v)
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
    # 写完不能还有私用区
    left = [(i, c) for i, r in enumerate(back) for c, v in r.items() if v and any(is_pua(x) for x in v)]
    if left:
        print("[FATAL] 写回后仍有私用区：%s" % left[:5])
        return 2

    io.open(CSV_PATH, "w", encoding="utf-8-sig", newline="").write(text)
    print("\n已写回 %s（%d 格，%d 列）" % (os.path.basename(CSV_PATH), sum(hits.values()), len(cols)))
    print("备份：%s" % os.path.basename(bak))
    print("⚠️ 下游要重跑：gen_taikou_hero_profiles.py（列传→语言包）+ 据点生成器")
    return 0


if __name__ == "__main__":
    sys.exit(main())
