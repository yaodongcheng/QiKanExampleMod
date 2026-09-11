#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""① 72 处「两版异名」的另一写法并入 Alias  ② 全表按 DX 号重排
============================================================================
用户裁定（2026-09-11）
--------------------
  ① 「73 个人如果确定是同一个人，名字写到 alias 里面」
  ② 「按照 dx 号重排」

① 的分类怎么做出来的（纯数据判据，不靠史实知识）
-------------------------------------------------
  对每个「两版日志名字不同」的槽 i（原版名 A、DX 名 B）：
    · A 若在 **DX 日志的别的槽**出现 → A 那个人在 DX 里另有位置 → **不同人**
    · B 若在 **原版日志的别的槽**出现 → 同理 → **不同人**
    · 两边名字都只在这一个槽出现 → **同人异名**
  结果：**73 处里只有 1 处是不同人**（槽 300 河野通直/河野牛福丸 = 父子，已由
  `fix_kouno_father_son.py` 单独处理）；**其余 72 处全部同人异名**。
  旁证：BUSTUP 目录名常把两个名字都写上（`秋山虎繁(秋山信友)`、`北畠具教(北田具教)`…）。

② 排序
------
  键 = ID 里的 DX 号；模板行（template_*）/ 变量行（pronoun_*）无号，**保持原相对序排在最后**。
  行 0-959 本来就是 DX 0-959 顺序 → 重排后**前 960 行位置不变**，
  故 `import_taikou_hero_bios.py` 里「行 0-799 = 日志池」的假设仍然成立。

用法
----
  python Scripts/merge_era_names_and_sort.py --dry-run
  python Scripts/merge_era_names_and_sort.py
"""
import argparse
import csv
import io
import os
import re
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tk5_pua_names import restore          # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSV_PATH = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "TaikouHero.csv")
LOG_DIR = r"E:\TKHACK\log"
LINE_RE = re.compile(r"Log: (\d+)\|(.*)$")
NON_PERSON = ("template_", "pronoun_", "prounon")


def load_log(fn):
    p = os.path.join(LOG_DIR, fn)
    d = {}
    if not os.path.isfile(p):
        return d
    for raw in io.open(p, encoding="utf-8", errors="replace"):
        m = LINE_RE.search(raw.rstrip("\r\n"))
        if m:
            d[int(m.group(1))] = restore(m.group(2).rstrip()).strip()
    return d


def main():
    ap = argparse.ArgumentParser(description="merge era names into Alias + sort by DX no")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    og, dx = load_log("原版角色ID.log"), load_log("DX角色ID.log")
    with io.open(CSV_PATH, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = list(rd.fieldnames)
        rows = list(rd)

    # ── ① 异名并入 Alias（只处理 0-799 段：两版槽号一致的那一段）──
    n_add = 0
    samples = []
    for i in range(0, min(800, len(rows))):
        r = rows[i]
        if r["ID"].startswith(NON_PERSON):
            continue
        cn = (r.get("CNName") or "").strip()
        have = [x.strip() for x in (r.get("Alias") or "").split("|") if x.strip()]
        if cn:
            have_set = set(have) | {cn}
        else:
            have_set = set(have)
        add = []
        for v in (og.get(i, ""), dx.get(i, "")):
            v = (v or "").strip()
            if v and v not in have_set and v not in add:
                add.append(v)
        if add:
            r["Alias"] = "|".join(have + add)
            n_add += 1
            if len(samples) < 8:
                samples.append((i, cn, have, add))
    print(f"① 异名并入 Alias：{n_add} 行")
    for i, cn, have, add in samples:
        print(f"   行{i:>4} {cn:<12} Alias {have} += {add}")

    # ── ② 按 DX 号排序 ──
    def sort_key(item):
        i, r = item
        m = re.match(r"^lord_tk5_(\d+)(_alt)?$", r["ID"])
        if m:
            return (0, int(m.group(1)), i)
        return (1, 0, i)                       # 模板/变量行殿后，保持原相对序

    ordered = [r for _, r in sorted(enumerate(rows), key=sort_key)]
    moved = sum(1 for a, b in zip(rows, ordered) if a is not b)
    print(f"\n② 排序：{moved} 行位置变化")

    if args.dry_run:
        print("\n--dry-run：未写文件。")
        print("排序后前 6 行:", [r["ID"] for r in ordered[:6]])
        print("排序后 958-964 行:", [r["ID"] for r in ordered[958:965]])
        print("排序后 1035-1040 行:", [r["ID"] for r in ordered[1035:1041]])
        return 0

    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=cols, lineterminator="\r\n",
                       quoting=csv.QUOTE_MINIMAL, extrasaction="ignore")
    w.writeheader()
    for r in ordered:
        w.writerow({c: (r.get(c) or "") for c in cols})
    text = buf.getvalue()

    back = list(csv.DictReader(io.StringIO(text)))
    if len(back) != len(rows):
        print(f"[FATAL] 往返行数不符 {len(back)} != {len(rows)}")
        return 1
    # 行序变了，按 ID 对齐比对（内容必须一字不差）
    by_id_a = {r["ID"]: r for r in rows}
    by_id_b = {r["ID"]: r for r in back}
    if set(by_id_a) != set(by_id_b):
        print("[FATAL] ID 集合变化")
        return 1
    for hid, a in by_id_a.items():
        b = by_id_b[hid]
        for c in cols:
            if (a.get(c) or "") != (b.get(c) or ""):
                print(f"[FATAL] 往返不一致 {hid} 列{c!r}: {a.get(c)!r} vs {b.get(c)!r}")
                return 1
    print("往返校验通过（按 ID 对齐，逐格一致）")

    io.open(CSV_PATH, "w", encoding="utf-8-sig", newline="").write(text)
    print(f"已写回：{len(back)} 行")

    # 单调性检查
    nums = []
    for r in back:
        m = re.match(r"^lord_tk5_(\d+)", r["ID"])
        if m:
            nums.append(int(m.group(1)))
    bad = [(nums[k - 1], nums[k]) for k in range(1, len(nums)) if nums[k] < nums[k - 1]]
    print(f"lord_tk5_* 单调递增: {'✓' if not bad else '✗ ' + str(bad[:5])}（共 {len(nums)} 个）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
