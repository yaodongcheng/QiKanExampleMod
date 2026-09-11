#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""把「同一历史人物被拆成两行」并成一行（2026-09-11 用户裁定）
============================================================================
🔴 规则（用户裁定）：**只要是同一个人，表里只能有一行。**

与 `merge_taikou_hero_alias_rows.py` 的分工：
  那个处理 **`_alt` 后缀**的重复行（上一轮为避免 ID 撞号的临时处置）；
  本脚本处理**名字完全不同**的同人两行（幼名版 vs 实名版），两者互不重叠。

判定依据（必须有硬证据，不靠猜）：
  TK5 自己的列传日志（`E:\TKHACK\log\太阁出生年、列传信息.log`）里写死的幼名/初名 ——
  例：idx 196 织田秀信「信忠之嫡子。信長之孫。**幼名三法師**。」⇒ 行 1177「三法师」是同一人。

合并动作：
  ① 主行 `Alias` += 被并行的 [CNName] + [Alias 各段]（去重、`|` 分隔）
  ② 主行 `立绘阶段` += 被并行的立绘（`stage` 用它的名字标注，如「三法师」）——立绘不丢
  ③ 全表亲属列里指向被并行的引用 → 改指主行
  ④ 删除被并行
  ⑤ 往返校验

用法：
  python Scripts/merge_taikou_duplicate_person_rows.py --dry-run
  python Scripts/merge_taikou_duplicate_person_rows.py
"""
import argparse
import csv
import io
import json
import os
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSV_PATH = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "TaikouHero.csv")
KINS_COLS = ["FatherId", "MotherId", "SpouseId", "GrandFatherId", "KinsId"]

# 被并行 → (主行, 依据)。每加一对都必须附「哪来的硬证据」。
MERGE = {
    "lord_tk5_1177": ("lord_tk5_196",
                      "三法师 = 织田秀信幼名。TK5 列传 idx196 原文「信忠之嫡子。信長之孫。幼名三法師。」"
                      "（另有事件脚本「已去世的信忠大人嫡子三法師大人」佐证父子链）",
                      ["三法師"]),
}

report = []


def say(line=""):
    print(line)
    report.append(line)


def main():
    ap = argparse.ArgumentParser(description="merge same-person duplicate rows")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    with io.open(CSV_PATH, encoding="utf-8-sig", newline="") as fh:
        rd = csv.DictReader(fh)
        cols = list(rd.fieldnames)
        rows = list(rd)
    by = {r["ID"]: r for r in rows}

    live = {k: v for k, v in MERGE.items() if k in by}
    if not live:
        print("待并行都已不存在（已并过）—— 无操作。")
        return 0

    say("# 同人两行合并（TaikouHero.csv）\n")
    say(f"输入：{len(rows)} 行 / {len(cols)} 列；本次合并 {len(live)} 对\n")

    drop = set()
    for dup, (main_id, why, extra) in live.items():
        m, d = by[main_id], by[dup]
        say(f"## {d['CNName']}（{dup}）→ {m['CNName']}（{main_id}）")
        say(f"- 依据：{why}")

        # ① Alias
        al = [x.strip() for x in (m.get("Alias") or "").split("|") if x.strip()]
        add = [d["CNName"]] + list(extra) + [x.strip() for x in (d.get("Alias") or "").split("|") if x.strip()]
        new = [x for x in add if x and x not in al]
        m["Alias"] = "|".join(al + new)
        say(f"- Alias：+{new}　⇒ `{m['Alias']}`")

        # ② 立绘阶段
        try:
            ms = json.loads(m.get("立绘阶段") or "[]")
        except ValueError:
            ms = []
        try:
            ds = json.loads(d.get("立绘阶段") or "[]")
        except ValueError:
            ds = []
        have = {str(e.get("tkid")) for e in ms}
        for e in ds:
            if str(e.get("tkid")) in have:
                continue
            e = dict(e)
            e["stage"] = d["CNName"]          # 用被并行的名字当阶段标签
            ms.append(e)
        m["立绘阶段"] = json.dumps(ms, ensure_ascii=False, separators=(",", ":"))
        say(f"- 立绘阶段：并入 {len(ds)} 张（stage 标为「{d['CNName']}」）")

        # ③ 引用改指
        repointed = 0
        for r in rows:
            if r["ID"] in (dup, main_id):
                continue
            for c in KINS_COLS:
                toks = [t.strip() for t in (r.get(c) or "").split("|") if t.strip()]
                if dup in toks:
                    toks = [main_id if t == dup else t for t in toks]
                    r[c] = "|".join(dict.fromkeys(toks))
                    repointed += 1
        say(f"- 引用改指：{repointed} 处")

        # 主行自己的父/祖若原本为空，用被并行的补齐（同一个人，数据互补）
        for c in KINS_COLS:
            if not (m.get(c) or "").strip() and (d.get(c) or "").strip():
                m[c] = d[c]
                say(f"- 主行空字段补齐：{c} = {d[c]}")
        for c in ("原版编号", "外观ID"):
            if not (m.get(c) or "").strip() and (d.get(c) or "").strip():
                m[c] = d[c]
        drop.add(dup)
        say("")

    if args.dry_run:
        say(f"--dry-run：未写文件（将删除 {len(drop)} 行）。")
        return 0

    out = [r for r in rows if r["ID"] not in drop]
    buf = io.StringIO()
    w = csv.DictWriter(buf, fieldnames=cols, lineterminator="\r\n",
                       quoting=csv.QUOTE_MINIMAL, extrasaction="ignore")
    w.writeheader()
    for r in out:
        w.writerow({c: (r.get(c) or "") for c in cols})
    text = buf.getvalue()

    back = list(csv.DictReader(io.StringIO(text)))
    if len(back) != len(out):
        say(f"[FATAL] 往返行数不符 {len(back)} != {len(out)}")
        return 1
    for i, b in enumerate(back):
        for c in cols:
            if (out[i].get(c) or "") != (b.get(c) or ""):
                say(f"[FATAL] 往返不一致 行{i} 列{c!r}")
                return 1
    if any(r["ID"] in drop for r in back):
        say("[FATAL] 被并行仍在产物里")
        return 1

    io.open(CSV_PATH, "w", encoding="utf-8-sig", newline="").write(text)
    say(f"## 落盘\n- {len(rows)} → {len(out)} 行（删 {sorted(drop)}），往返校验通过")
    return 0


if __name__ == "__main__":
    sys.exit(main())
