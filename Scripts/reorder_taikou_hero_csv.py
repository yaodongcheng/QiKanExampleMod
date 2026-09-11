#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""TaikouHero.csv 列序规整器
============================================================================
为什么要有它
------------
TaikouHero.csv 会不断被各生成器/导入器改（加列传列、加原版编号列、重编 ID…），
列序容易散。本脚本把**前几列固定成约定顺序**，其余保持原序不动：

    ID | 原版编号 | 外观ID | 模板NPC | ScriptName | CNName | ...（其余原序）

约定（2026-09-11 用户裁定）
  · `原版编号` 紧跟 `ID` —— 两把人物钥匙并排，一眼可比
  · `外观ID` 再跟在 `原版编号` 之后 —— 三把钥匙（DX/原版/外观）连续排列

🔴 为什么必须脚本化（铁律 22）：这个 CSV 是生成器的读面，手改列序 = 与生成器分叉，
   下次任何生成器重写就乱回去。改列序 = 改本脚本的 CANONICAL 表 → 重跑。

用法
----
  python Scripts/reorder_taikou_hero_csv.py --check   # 只校验列序（不一致 exit 1）
  python Scripts/reorder_taikou_hero_csv.py           # 写回
"""
import argparse
import csv
import io
import os
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSV_PATH = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "TaikouHero.csv")

# 前段固定列（其余列保持 CSV 里的原序，不重排）
# 🔴 2026-09-11 用户裁定：`Alias` 提前到 `CNName` 之后 —— 名字的三种形态（主名/别名/年代名）
#    紧挨着看，查名字时不用横跨整表（表有 128 列宽）。
CANONICAL_HEAD = ["ID", "原版编号", "外观ID", "模板NPC", "CNName", "Alias"]


def reorder(cols):
    """→ (新列序, 变了没)。CANONICAL_HEAD 里存在的列按序提到最前，其余原序跟随。"""
    head = [c for c in CANONICAL_HEAD if c in cols]
    new = head + [c for c in cols if c not in head]
    return new, new != cols


def main():
    ap = argparse.ArgumentParser(description="TaikouHero.csv column order normalizer")
    ap.add_argument("--check", action="store_true", help="只校验，不写")
    args = ap.parse_args()

    with io.open(CSV_PATH, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = list(rd.fieldnames)
        rows = list(rd)

    new_cols, changed = reorder(cols)
    missing = [c for c in CANONICAL_HEAD if c not in cols]
    print(f"现有列序前 8：{cols[:8]}")
    print(f"目标列序前 8：{new_cols[:8]}")
    if missing:
        print(f"  [WARN] 约定列缺失（跳过）：{missing}")

    if args.check:
        if changed:
            print("[STALE] 列序与约定不一致 —— 重跑本脚本")
            return 1
        print("列序已合规 ✓")
        return 0

    if not changed:
        print("列序未变，未写文件")
        return 0

    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=new_cols, lineterminator="\r\n",
                       quoting=csv.QUOTE_MINIMAL, extrasaction="ignore")
    w.writeheader()
    for r in rows:
        w.writerow({c: (r.get(c) or "") for c in new_cols})
    text = buf.getvalue()

    # 往返校验：只允许列序变，逐格内容必须一字不差
    back = list(csv.DictReader(io.StringIO(text)))
    if len(back) != len(rows):
        print(f"[FATAL] 往返行数不符 {len(back)} != {len(rows)}")
        return 1
    for i, (a, b) in enumerate(zip(rows, back)):
        for c in cols:
            if (a.get(c) or "") != (b.get(c) or ""):
                print(f"[FATAL] 往返不一致 行{i} 列{c!r}: {a.get(c)!r} vs {b.get(c)!r}")
                return 1
    print(f"往返校验通过：{len(rows)} 行 × {len(cols)} 列逐格一致（只换列序，内容未动）")

    io.open(CSV_PATH, "w", encoding="utf-8-sig", newline="").write(text)
    print(f"已写回（{len(new_cols)} 列）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
