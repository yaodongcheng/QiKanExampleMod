#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""迁移：TaikouHero.csv 删除 ScriptName 列，繁体名并入 Alias
============================================================================
用户裁定（2026-09-11）
--------------------
  · **删 `ScriptName` 列** —— 它原本用于「从太阁5 剧本里按繁体名提取人物」，
    但现在它实际没被任何消费方使用（见下），列为冗余。
  · **繁体名并入 `Alias` 列** —— 保留繁体原名的可查性，且正合铁律 25 的
    「Alias 必须覆盖该实体所有年代的名字」。

为什么说 ScriptName 没被用（实测依据，不是印象）
----------------------------------------------
消费方只有 `plans/scenario-campaign-mode/tools/gen_entity_maps.py`：
    main = cn if cn else sn          # ← ScriptName 只是 CNName 为空时的兜底
    别名键只从 Alias 列建              # ← ScriptName 从不作为键
实测：1117 行里 **CNName 为空的有 0 行** → 那个兜底永不触发；ScriptName 作为键能命中的只有 4 行。
后果（顺带修掉的隐患）：剧本用繁体名引用人物（`更新:(人物::織田信長.205)`），
迁移前**繁体名一律查不到**（織田信長/武田勝賴/上杉謙信/德川家康/明智光秀/長尾景虎 逐个 MISS）。

🔴 为什么不能只是删
   48 行的繁体名**不是** CNName 的简单繁体形式（异体字，不是简繁差异）：
   鯰貝/鲇贝、北畠/北田、榊原/神原、宍戶/肉戶、雜賀/杂贺… 删了就推不回来。

迁移规则
--------
    Alias_new = ScriptName | Alias_old      （ScriptName 置首，去重，半角竖线分隔——铁律 24）
    若 ScriptName 已等于 CNName 或已在 Alias 里 → 不重复加。

幂等：ScriptName 列不存在时直接退出（0）。

用法
----
  python Scripts/migrate_taikou_hero_scriptname.py --dry-run
  python Scripts/migrate_taikou_hero_scriptname.py
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
COL_SN, COL_ALIAS, COL_CN = "ScriptName", "Alias", "CNName"


def main():
    ap = argparse.ArgumentParser(description="drop ScriptName column, merge into Alias")
    ap.add_argument("--dry-run", action="store_true", help="只报告，不写")
    args = ap.parse_args()

    with io.open(CSV_PATH, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = list(rd.fieldnames)
        rows = list(rd)

    if COL_SN not in cols:
        print(f"CSV 无 {COL_SN} 列 —— 已完成迁移或无需迁移，退出。")
        return 0
    if COL_ALIAS not in cols:
        print(f"[FATAL] 找不到 {COL_ALIAS} 列，无法合并")
        return 2

    merged = dup = same_as_cn = 0
    samples = []
    for i, r in enumerate(rows):
        sn = (r.get(COL_SN) or "").strip()
        cn = (r.get(COL_CN) or "").strip()
        al = (r.get(COL_ALIAS) or "").strip()
        parts = [p.strip() for p in al.split("|") if p.strip()]
        if sn and sn == cn:
            same_as_cn += 1
        elif sn and sn not in parts:
            parts.insert(0, sn)
            merged += 1
        elif sn:
            dup += 1
        r[COL_ALIAS] = "|".join(parts)
        # 铁律 24：值内禁止半角逗号
        if "," in r[COL_ALIAS]:
            print(f"[FATAL] 行{i} Alias 含半角逗号：{r[COL_ALIAS]!r}")
            return 2
        if i < 5 and parts:
            samples.append((i, sn, r[COL_ALIAS]))

    print(f"{len(rows)} 行：繁体名并入 Alias {merged} 行 / 已在 Alias 里 {dup} 行 / 与 CNName 相同 {same_as_cn} 行")
    print("样例：")
    for i, sn, al in samples:
        print(f"   行{i:>4} ScriptName={sn!r:<14} → Alias={al!r}")
    nonempty = sum(1 for r in rows if (r.get(COL_ALIAS) or "").strip())
    print(f"Alias 列非空行：{nonempty} / {len(rows)}")

    if args.dry_run:
        print("\n--dry-run：未写文件。")
        return 0

    new_cols = [c for c in cols if c != COL_SN]
    for r in rows:
        r.pop(COL_SN, None)
    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=new_cols, lineterminator="\r\n",
                       quoting=csv.QUOTE_MINIMAL, extrasaction="ignore")
    w.writeheader()
    for r in rows:
        w.writerow({c: (r.get(c) or "") for c in new_cols})
    text = buf.getvalue()

    back = list(csv.DictReader(io.StringIO(text)))
    if len(back) != len(rows):
        print(f"[FATAL] 往返行数不符 {len(back)} != {len(rows)}")
        return 1
    for i, (a, b) in enumerate(zip(rows, back)):
        for c in new_cols:
            if (a.get(c) or "") != (b.get(c) or ""):
                print(f"[FATAL] 往返不一致 行{i} 列{c!r}")
                return 1
    io.open(CSV_PATH, "w", encoding="utf-8-sig", newline="").write(text)
    print(f"\n已写回（{len(new_cols)} 列，去掉 {COL_SN}）")
    print("往返校验通过：其余列逐格一致")

    # ── 自检：繁体名现在真的能查到吗 ──
    name_alias = {}
    for r in back:
        cn = (r.get(COL_CN) or "").strip()
        main = cn
        if not main:
            continue
        for key in (r.get(COL_ALIAS) or "").split("|"):
            key = key.strip()
            if key and key != main and key not in name_alias:
                name_alias[key] = main
    print(f"\nNAME_ALIAS 条目数：{len(name_alias)}")
    miss = 0
    for nm in ("織田信長", "武田勝賴", "上杉謙信", "德川家康", "明智光秀", "長尾景虎",
               "豐臣秀吉", "誾千代", "北畠具教"):
        hit = name_alias.get(nm)
        if not hit:
            miss += 1
        print(f"   {nm:<10} → {hit or '**仍然查不到**'}")
    print("繁体名查询打通 ✓" if miss == 0 else f"仍有 {miss} 个查不到，需排查")
    return 0


if __name__ == "__main__":
    sys.exit(main())
