#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把「别名重复行」并进主行（并把 Alias 列提到 CNName 之后）
============================================================================
用户裁定（2026-09-11）
--------------------
「id 里带 _alt」不该存在 —— 同一人两行应当**并成一行**：重复行的名字收进主行的 `Alias`，
重复行删除。`_alt` 只是上一轮为避免 ID 撞号的临时处置。

合并哪些（逐组核实过数据）
--------------------------
  ✅ 行970 蒲生乡舍 → 行243 蒲生赖乡     （重复行生卒/五维/列传全空，只有别名）
  ✅ 行973 长阪长闲 → 行530 长坂钓闲     （同上；且其 Alias 本就含主行名）
  ✅ 行960 三好政胜 → 行705 三好为三
  ✅ 行961 三好政康 → 行706 三好宗渭
  ❌ 行906 河野通直 → 行300 河野通直牛福丸 —— **不并**。两行数据完整且矛盾：
     主行 生1564卒1587 五维44/41/50/36/48；重复行 生1500卒1572 五维32/26/77/63/42。
     生年差 64 年 = **同名两人**（河野家父子同名「通直」，牛福丸是儿子幼名）。
     硬并会丢一整套数据 → 保留两行，等用户裁定（其「原版编号=300」是按名查得的，也可能本就错）。

合并规则
--------
  主行 Alias += 重复行的 [CNName] + [Alias 各段]（去重、`|` 分隔）
  删除重复行

用法
----
  python Scripts/merge_taikou_hero_alias_rows.py --dry-run
  python Scripts/merge_taikou_hero_alias_rows.py
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

# 重复行 → 主行（只列**数据核实过可并**的；300/906 有意排除，见文件头）
MERGE = {970: 243, 973: 530, 960: 705, 961: 706}


def main():
    ap = argparse.ArgumentParser(description="merge alias-duplicate hero rows")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    with io.open(CSV_PATH, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = list(rd.fieldnames)
        rows = list(rd)

    # 幂等：重复行已不存在 → 直接退出
    if not all(i < len(rows) and rows[i]["ID"].endswith("_alt") for i in MERGE):
        missing = [i for i in MERGE if not (i < len(rows) and rows[i]["ID"].endswith("_alt"))]
        print(f"待并行已不存在或已并过（{missing}）—— 无操作。")
        return 0

    plan = []
    for dup, owner in MERGE.items():
        d, o = rows[dup], rows[owner]
        add = []
        for src in [d.get("CNName", "")] + (d.get("Alias") or "").split("|"):
            s = (src or "").strip()
            if s and s != (o.get("CNName") or "").strip():
                add.append(s)
        old = [x.strip() for x in (o.get("Alias") or "").split("|") if x.strip()]
        new = old + [x for x in add if x not in old]
        plan.append((dup, owner, d.get("CNName"), o.get("CNName"), old, new))
        print(f"行{dup} {d.get('CNName')!r}  →  行{owner} {o.get('CNName')!r}")
        print(f"     Alias: {old}  →  {new}")

    if args.dry_run:
        print(f"\n--dry-run：将删 {len(plan)} 行、改 {len(plan)} 行，未写文件。")
        return 0

    for dup, owner, _, _, _, new in plan:
        rows[owner]["Alias"] = "|".join(new)
    drop = set(MERGE)
    kept = [r for i, r in enumerate(rows) if i not in drop]
    print(f"\n删 {len(drop)} 行，剩 {len(kept)} 行")

    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=cols, lineterminator="\r\n",
                       quoting=csv.QUOTE_MINIMAL, extrasaction="ignore")
    w.writeheader()
    for r in kept:
        w.writerow({c: (r.get(c) or "") for c in cols})
    text = buf.getvalue()

    back = list(csv.DictReader(io.StringIO(text)))
    if len(back) != len(kept):
        print(f"[FATAL] 往返行数不符 {len(back)} != {len(kept)}")
        return 1
    for i, (a, b) in enumerate(zip(kept, back)):
        for c in cols:
            if (a.get(c) or "") != (b.get(c) or ""):
                print(f"[FATAL] 往返不一致 行{i} 列{c!r}")
                return 1
    io.open(CSV_PATH, "w", encoding="utf-8-sig", newline="").write(text)
    print("往返校验通过，已写回")

    # ── 同步模块侧：立绘表里被并掉的那几行要删 ──
    #    ProfileStages.csv 的 StringId 与 CSV 的 ID 同键；并掉的行留着就是幽灵条目
    #    （check_hero_profile_keys 会报「模板 ↔ 画像表 ↔ 立绘表」三处对不上）。
    module = _resolve_module()
    if module:
        ps = os.path.join(module, "ModuleData", "AssetRegistry", "ProfileStages.csv")
        if os.path.isfile(ps):
            dead = {"lord_tk5_%d_alt" % d for d in MERGE}
            raw = io.open(ps, encoding="utf-8-sig", newline="").read()
            lines = raw.splitlines(True)
            keep = [ln for ln in lines
                    if not any(ln.startswith(k + ",") for k in dead)]
            gone = len(lines) - len(keep)
            if gone:
                io.open(ps, "w", encoding="utf-8-sig", newline="").write("".join(keep))
            print(f"ProfileStages.csv：删掉 {gone} 条被并行的立绘条目")
    else:
        print("[WARN] 未找到内容包目录，ProfileStages.csv 未清理")

    ids = [r["ID"] for r in back]
    print(f"ID 唯一性: {'✓' if len(set(ids)) == len(ids) else '✗'}")
    print(f"仍带 _alt 的: {[x for x in ids if x.endswith('_alt')]}")
    return 0


def _resolve_module():
    if sys.platform != "win32":
        return None
    import winreg
    mb2 = None
    for hive, sub in ((winreg.HKEY_CURRENT_USER, "Environment"),
                      (winreg.HKEY_LOCAL_MACHINE,
                       r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")):
        try:
            with winreg.OpenKey(hive, sub) as k:
                mb2 = winreg.QueryValueEx(k, "MB2_PATH")[0] or mb2
        except OSError:
            continue
    return os.path.join(mb2, "Modules", "Taikou") if mb2 else None


if __name__ == "__main__":
    sys.exit(main())
