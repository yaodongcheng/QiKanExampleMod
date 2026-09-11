#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""TaikouHero.csv 结构重整（2026-09-11 用户裁定，一次做七件事）
============================================================================
1. 删 `GenerateType` 列 —— 含「男」「1」这类模板段错位脏值；唯一消费方
   `GenerateXml.py` 的 is_shokuho 一并清掉（织丰时代已过，该标志无意义）。
2. 删 `模板` 列 —— 1117 行**全空**，零消费方。
3. 删 `列传简体` 列 —— 保留繁体 `列传原文`；简体/英文改在生成 XML 时走正式本地化流程
   （`sync_taikou_bio_cns.py` 自己转简体）。
4. `BirthYear` 按 tkhack 实机日志（`太阁出生年、列传信息.log`）修正 —— 日志是游戏的事实。
5. `FatherName` / `GrandFatherName` / `KinsName` → **ID 列**（`lord_tk5_<DX号>`）。
   KinsName 是多值（空格分隔）→ 转成 `|` 分隔的 ID 串（铁律 24）。
   查不到的**保留原名**并逐条报告（宁可留原文让你看见，也不静默丢）。
6. 空列名那列（5 条「光荣版经典形象（太阁5）：…」）**并入 `外观描述_光荣`**（`|` 分隔）。
7. `立绘阶段`：`ref` 字段由**文件名**（`447_武田胜赖_朝右.png`，含会改的名字）改为
   **外观ID 值**（`"447"`）—— 名字会变、外观槽不会，引用才稳。

🔴 全部改完做逐格往返校验；名字→ID 解析率低于阈值则拒绝写回。
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

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSV_PATH = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "TaikouHero.csv")
BIO_LOG = r"E:\TKHACK\log\太阁出生年、列传信息.log"

DROP_COLS = ["GenerateType", "模板", "列传简体"]
KINS_COLS = ["FatherName", "GrandFatherName", "KinsName"]
COL_APPEAR_DESC = "外观描述_光荣"
COL_STAGE = "立绘阶段"
BIO_RE = re.compile(r"Log: (BIO\|.*)$")


def build_name_index(rows):
    """名字 → ID。键 = CNName + Alias 各段 + 年代名（Name_<era>）。"""
    idx = {}
    for r in rows:
        hid = (r.get("ID") or "").strip()
        if not hid:
            continue
        keys = [r.get("CNName", "")]
        keys += (r.get("Alias") or "").split("|")
        for c in r:
            if c.startswith("Name_"):
                keys.append(r.get(c, ""))
        for k in keys:
            k = (k or "").strip()
            if k and k not in idx:
                idx[k] = hid
    return idx


def parse_bio_log():
    """日志 番号 → 生年（只取 0-799，即 CSV 行号）"""
    if not os.path.isfile(BIO_LOG):
        return {}
    out, cur = {}, None
    for raw in io.open(BIO_LOG, encoding="utf-8", errors="replace"):
        m = BIO_RE.search(raw.rstrip("\r\n"))
        if m:
            p = m.group(1).split("|", 6)
            if len(p) >= 5 and p[2].strip().isdigit():
                cur = int(p[2])
                out[cur] = p[4].strip()
    return out


def main():
    ap = argparse.ArgumentParser(description="restructure TaikouHero.csv")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--min-resolve", type=float, default=0.90,
                    help="名字→ID 解析率下限（低于则拒绝写回）")
    args = ap.parse_args()

    with io.open(CSV_PATH, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = list(rd.fieldnames)
        rows = list(rd)

    name2id = build_name_index(rows)
    print(f"名字索引：{len(name2id)} 条")

    # ── 4. BirthYear 对账 ──
    log_year = parse_bio_log()
    year_fix = []
    for n in range(0, min(799, len(rows) - 1) + 1):
        lg = log_year.get(n)
        if not lg:
            continue
        cur = (rows[n].get("BirthYear") or "").strip()
        if cur != lg:
            year_fix.append((n, rows[n].get("CNName"), cur, lg))
    print(f"\nBirthYear 与日志不一致：{len(year_fix)} 条")
    for n, nm, a, b in year_fix:
        print(f"   行{n:>4} {nm:<12} {a} → {b}（改用日志）")

    # ── 5. 亲属列 → ID ──
    print("\n=== 亲属列 → ID ===")
    unresolved = []
    for c in KINS_COLS:
        tot = hit = 0
        for i, r in enumerate(rows):
            v = (r.get(c) or "").strip()
            if not v:
                continue
            names = [x for x in re.split(r"[\s　|]+", v) if x]
            for nm in names:
                tot += 1
                if nm not in name2id:
                    unresolved.append((i, r.get("CNName"), c, nm))
                else:
                    hit += 1
        print(f"  {c}: 值 {tot} 个，可解析 {hit}（{hit * 100 // max(tot,1)}%）")
    print(f"  未解析合计 {len(unresolved)} 个（保留原名）")
    for u in unresolved[:20]:
        print(f"     行{u[0]:>4} {str(u[1]):<12} {u[2]:<16} = {u[3]!r}")
    rate = 1 - len(unresolved) / max(sum(
        1 for r in rows for c in KINS_COLS
        for _ in re.split(r"[\s　|]+", (r.get(c) or "").strip()) if _), 1)
    print(f"  总解析率 {rate:.1%}（下限 {args.min_resolve:.0%}）")

    # ── 6. 空列 → 并入 外观描述_光荣 ──
    n_merge = sum(1 for r in rows
                  if (r.get("") or "").strip() and (r.get(COL_APPEAR_DESC) or "").strip())
    n_only = sum(1 for r in rows
                 if (r.get("") or "").strip() and not (r.get(COL_APPEAR_DESC) or "").strip())
    print(f"\n空列并入 {COL_APPEAR_DESC}：两边都有值 {n_merge} 行 / 只有空列有值 {n_only} 行")

    # ── 7. 立绘阶段 ref → 外观ID ──
    n_stage = ok_stage = 0
    bad_stage = []
    for i, r in enumerate(rows):
        v = (r.get(COL_STAGE) or "").strip()
        if not v:
            continue
        n_stage += 1
        try:
            arr = json.loads(v)
        except Exception:                                    # noqa: BLE001
            bad_stage.append((i, "JSON 解析失败"))
            continue
        good = True
        for e in arr:
            ref = str(e.get("ref") or "")
            m = re.match(r"^(\d+)_", ref)
            if m:
                e["ref"] = m.group(1)
            elif ref and not ref.isdigit():
                good = False
        if good:
            ok_stage += 1
        else:
            bad_stage.append((i, rows[i].get("CNName")))
    print(f"{COL_STAGE}：{n_stage} 行有值，ref 可转外观ID {ok_stage}")
    for b in bad_stage[:8]:
        print(f"   ! 行{b[0]} {b[1]}")

    if args.dry_run:
        print("\n--dry-run：未写文件。")
        return 0

    if rate < args.min_resolve:
        print(f"\n[FATAL] 亲属名解析率 {rate:.1%} 低于下限 {args.min_resolve:.0%}，拒绝写回")
        return 1

    # ── 落盘 ──
    for n, nm, a, b in year_fix:
        rows[n]["BirthYear"] = b
    for r in rows:
        for c in KINS_COLS:
            v = (r.get(c) or "").strip()
            if not v:
                continue
            out = []
            for nm in re.split(r"[\s　|]+", v):
                if nm:
                    out.append(name2id.get(nm, nm))          # 查不到 → 保留原名
            r[c] = "|".join(out)
        # 6
        extra = (r.get("") or "").strip()
        if extra:
            base = (r.get(COL_APPEAR_DESC) or "").strip()
            r[COL_APPEAR_DESC] = (base + "|" + extra) if base else extra
        r[""] = ""
        # 7
        v = (r.get(COL_STAGE) or "").strip()
        if v:
            try:
                arr = json.loads(v)
                for e in arr:
                    ref = str(e.get("ref") or "")
                    m = re.match(r"^(\d+)_", ref)
                    if m:
                        e["ref"] = m.group(1)
                r[COL_STAGE] = json.dumps(arr, ensure_ascii=False, separators=(",", ":"))
            except Exception:                                # noqa: BLE001
                pass

    new_cols = [c for c in cols if c not in DROP_COLS and c != ""]
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
    print(f"\n已写回：{len(cols)} → {len(new_cols)} 列（删 {DROP_COLS}）")
    print("往返校验通过")
    return 0


if __name__ == "__main__":
    sys.exit(main())
