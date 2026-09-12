#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""河野父子分号（2026-09-11 用户裁定：河野通直与河野牛福丸是**父子**，不是同一人）
============================================================================
背景
----
上一轮我把这两行当成「同一人两行」，给后一行加了 `_alt` 后缀。用户指出是父子关系
（河野家父子同名「通直」，牛福丸是儿子的幼名），**不能合并、ID 必须区分**。

实机日志铁证（两版人物番号）
----------------------------
    原版[300] = 河野通直     ← 父（生1500 卒1572）
    DX[300]   = 河野牛福丸   ← 子（生1564 卒1587）
    DX[906]   = 河野通直     ← 父
    原版[906] = 八左衛門     ← 别人
    「牛福丸」在原版日志里**完全不存在**（全表 0 命中）

  ⇒ **两版的 300 号不是同一人**：原版 300 是父，DX 300 是子（父子互换）。
    故两行各有各的 DX 号（300 / 906），各有各的原版号（无 / 300）——
    上一轮那行 `lord_tk5_300_alt` 是错的，应还原为 `lord_tk5_906`。

本次改什么
----------
  行 906（父 河野通直）：ID `lord_tk5_300_alt` → `lord_tk5_906`；原版编号保持 300
  行 300（子 牛福丸）  ：原版编号 300 → **空**（原版表里没有这个人，不能占别人的号）
  外观ID：两行原本都是 `300|1234` → 按 APPEAR_SPLIT 拆开（一人一槽，不再共用）

用法
----
  python Scripts/fix_kouno_father_son.py --dry-run
  python Scripts/fix_kouno_father_son.py
"""
import argparse
import csv
import io
import os
import re
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSV_PATH = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "TaikouHero.csv")

SON, FATHER = 300, 906                      # 行号：300=子（牛福丸），906=父（通直）

# 外观槽怎么分 —— 🔴 **2026-09-12 用户裁定：交换为 子=300 / 父=1234**（本脚本 2026-09-11 的原裁定
#   是「父=300 / 子=1234」，用户 2026-09-12 要求互换；互换已由一次性操作落地：
#   `TaikouHero.csv` 两行的 `外观ID` + `立绘阶段` 互换，`AssetRegistry/ProfileStages.csv` 的
#   `通/300` 行 StringId 由 `lord_tk5_906` 改挂 `lord_tk5_300`——此前该行错挂在父名下、**子一行立绘都没有**）。
#   依据不变：BUSTUP 目录里两个槽**都叫「河野通直」**（300_河野通直 / 1234_河野通直），没有「牛福丸」的槽。
APPEAR_SPLIT = {SON: "300", FATHER: "1234"}


def main():
    ap = argparse.ArgumentParser(description="split Kouno father/son ids")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    with io.open(CSV_PATH, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = list(rd.fieldnames)
        rows = list(rd)

    if rows[SON]["ID"] != "lord_tk5_300_alt" and rows[FATHER]["ID"] != "lord_tk5_300_alt":
        print("两行 ID 都不是 lord_tk5_300_alt —— 已修过或无操作。")
        return 0

    for idx, who in ((SON, "子 河野牛福丸"), (FATHER, "父 河野通直")):
        r = rows[idx]
        print(f"行{idx}（{who}）")
        print(f"   ID        {r['ID']!r}  →  {'lord_tk5_%d' % (idx if idx == FATHER else 300)!r}")
        print(f"   原版编号   {r['原版编号']!r}  →  "
              f"{(str(300) if idx == FATHER else '')!r}")
        print(f"   外观ID     {r['外观ID']!r}  →  {APPEAR_SPLIT[idx]!r}")
        st = r.get("ProfileStages") or ""
        ents = []
        try:
            import json
            for e in json.loads(st):
                if str(e.get("ref")) == APPEAR_SPLIT[idx]:
                    ents.append(e)
        except Exception:                                    # noqa: BLE001
            pass
        print(f"   立绘阶段   保留 ref={APPEAR_SPLIT[idx]} 的 {len(ents)} 条 → "
              f"{json.dumps(ents, ensure_ascii=False, separators=(',', ':'))}")

    if args.dry_run:
        print("\n--dry-run：未写文件。")
        return 0

    import json
    rows[FATHER]["ID"] = "lord_tk5_%d" % FATHER
    rows[SON]["OriginalID"] = ""
    rows[FATHER]["OriginalID"] = str(300)
    for idx in (SON, FATHER):
        rows[idx]["AppearanceID"] = APPEAR_SPLIT[idx]
        try:
            ents = [e for e in json.loads(rows[idx].get("ProfileStages") or "[]")
                    if str(e.get("ref")) == APPEAR_SPLIT[idx]]
            rows[idx]["ProfileStages"] = json.dumps(ents, ensure_ascii=False,
                                              separators=(",", ":"))
        except Exception:                                    # noqa: BLE001
            pass

    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=cols, lineterminator="\r\n",
                       quoting=csv.QUOTE_MINIMAL, extrasaction="ignore")
    w.writeheader()
    for r in rows:
        w.writerow({c: (r.get(c) or "") for c in cols})
    text = buf.getvalue()

    back = list(csv.DictReader(io.StringIO(text)))
    if len(back) != len(rows):
        print(f"[FATAL] 往返行数不符")
        return 1
    for i, (a, b) in enumerate(zip(rows, back)):
        for c in cols:
            if (a.get(c) or "") != (b.get(c) or ""):
                print(f"[FATAL] 往返不一致 行{i} 列{c!r}")
                return 1
    io.open(CSV_PATH, "w", encoding="utf-8-sig", newline="").write(text)
    print("\n已写回，往返校验通过")
    ids = [r["ID"] for r in back]
    print(f"ID 唯一性: {'✓' if len(set(ids)) == len(ids) else '✗'}；"
          f"仍带 _alt: {[x for x in ids if x.endswith('_alt')]}")

    # 模块侧同步：_alt 改名 + 立绘表删掉不属于本人那条
    module = _resolve_module()
    if module:
        ps = os.path.join(module, "ModuleData", "AssetRegistry", "ProfileStages.csv")
        if os.path.isfile(ps):
            raw = io.open(ps, encoding="utf-8-sig", newline="").read()
            out = raw.replace("lord_tk5_300_alt", "lord_tk5_906")
            io.open(ps, "w", encoding="utf-8-sig", newline="").write(out)
            print("ProfileStages.csv：lord_tk5_300_alt → lord_tk5_906")
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
