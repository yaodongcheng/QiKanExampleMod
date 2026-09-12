#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""确保 ForceTaikou.csv 带 `Owner_<年>` ×6（恢复 / 纠正列名）
============================================================================
**背景**：`migrate_taikou_force_source.py` 收编 Kingdom.csv 时把 `Owner_<年>` ×6 删了
（判据「死输入」）。用户裁定 **保留**。本脚本负责把这六列弄回来、**并保证列名就是 `Owner_<年>`**。

🔴 **列名一律 `Owner_<年>`，不要加任何前缀/描述词**（2026-09-12 用户两次裁定）：
  ①「需要保留，并且要时刻保持交叉验证」②「谁让你自己改列名的 擅自加快照 snapshot 这个描述」
  本脚本一度把列改名为 `快照当主_<年>`/`SnapshotOwner_<年>`，**属于未获授权的自作主张，已撤回**。
  ⚠️ 与产物 `TaikouForce.csv` 的 `Owner_<年>` 同名**不是问题**——两张表本来就是同一概念的两种口径，
     区分靠**文件名**，不靠列名（交叉验证脚本按文件读，见 `check_force_owner_crossval.py`）。

**三种输入状态都能收敛**（幂等）：
  · 已有 `Owner_<年>`            → 不动（0 改动退出）
  · 有被改错的 `快照当主_<年>` 等 → **只把表头改回 `Owner_<年>`**（值不动）
  · 六列不存在                    → 从迁移备份回填（备份没了退 git HEAD）

**取值来源**（回填时）：① `ForceTaikou.csv.bak_forcesrc_*` ② `git show HEAD:<path>`

**纪律**：备份 + 往返校验 + 幂等。

Usage:
  python Scripts/restore_force_owner_columns.py            # 报告 + 预览（不写盘）
  python Scripts/restore_force_owner_columns.py --apply    # 落盘
Exit: 0 正常/无需改动 / 1 有硬问题 / 2 fatal。
"""
import argparse
import csv
import glob
import io
import os
import shutil
import subprocess
import sys
import time

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
FORCE = os.path.join(CSV_DIR, "ForceTaikou.csv")

ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
COLS = ["Owner_" + e for e in ERAS]

# 快照列的合法取值：空 · `-` · `@商人/@忍者/@海贼`（模板标记）· 织丰编号（旧体系，只留档不解析）
TPL_MARKS = {"@商人", "@忍者", "@海贼"}
SHOKUHO_PREFIX = ("lord_1_", "lord_2_", "lord_3_", "dead_lord_", "spc_", "sho_")


def load_rows(path):
    with io.open(path, encoding="utf-8-sig", newline="") as fh:
        rows = list(csv.reader(fh))
    return rows[0], rows[1], [r for r in rows[2:] if r and r[0].strip()]


def find_source():
    """旧值来源：先找迁移备份，再退 git HEAD。返回 (说明, {ID: [6 个值]})。"""
    baks = sorted(glob.glob(FORCE + ".bak_forcesrc_*"), reverse=True)
    for b in baks:
        cn, _en, data = load_rows(b)
        if all(c in cn for c in COLS):
            return ("备份 " + os.path.basename(b),
                    {r[cn.index("ID")]: [r[cn.index(c)] for c in COLS] for r in data})
    rel = os.path.relpath(FORCE, REPO).replace("\\", "/")
    try:
        out = subprocess.run(["git", "show", "HEAD:" + rel], cwd=REPO, capture_output=True)
    except OSError:
        out = None
    if out and out.returncode == 0:
        text = out.stdout.decode("utf-8-sig")
        rows = list(csv.reader(io.StringIO(text)))
        cn, data = rows[0], [r for r in rows[2:] if r and r[0].strip()]
        if all(c in cn for c in COLS):
            return ("git HEAD:" + rel,
                    {r[cn.index("ID")]: [r[cn.index(c)] for c in COLS] for r in data})
    return (None, None)


def main():
    ap = argparse.ArgumentParser(description="确保 ForceTaikou.csv 带 Owner_<年> ×6")
    ap.add_argument("--apply", action="store_true", help="写盘（默认只报告）")
    args = ap.parse_args()

    if not os.path.isfile(FORCE):
        print("[FATAL] 缺 %s" % FORCE, file=sys.stderr)
        return 2
    cn, en, data = load_rows(FORCE)

    if all(c in cn for c in COLS):
        print("已有 `%s` ×6（列名正确）—— 幂等退出，0 改动" % COLS[0])
        return 0

    problems, out, mode = [], [], "restore"
    # ① 列名被改错（表头不是 Owner_<年>，但位置对得上）→ 只改表头，值不动
    if len(cn) == 6 + len(ERAS) and len(en) == 6 + len(ERAS):
        mode = "rename"
        print("检测到列名被改错：%s … → 改回 `Owner_<年>`（值不动）" % cn[6])
        for r in data:
            out.append(list(r))
    else:
        src_desc, old = find_source()
        if old is None:
            print("[FATAL] 找不到旧值来源（备份与 git HEAD 都没有 %s）" % " / ".join(COLS),
                  file=sys.stderr)
            return 2
        print("旧值来源：%s" % src_desc)
        n_kept = n_new = 0
        for r in data:
            rec = dict(zip(cn, r))
            vals = old.get(rec["ID"])
            if vals is None:
                vals = ["-"] * len(ERAS)
                n_new += 1
            else:
                n_kept += 1
            for v, c in zip(vals, COLS):
                v = (v or "").strip()
                if v and v != "-" and v not in TPL_MARKS and not v.startswith(SHOKUHO_PREFIX):
                    problems.append("%s %s = %s（既不是空/`-`/模板标记，也不是织丰编号）"
                                    % (rec["ID"], c, v))
            out.append([rec["ID"], rec["太阁编号"], rec["势力名"], rec["别名"],
                        rec["势力类型"], rec["Culture"]] + list(vals))
        print("  逐行回填：命中旧值 %d 行 / 新行（全 `-`）%d 行" % (n_kept, n_new))
        orphan = sorted(set(old) - {r[0] for r in data})
        if orphan:
            problems.append("旧表有而现表没有的 id %d 个：%s" % (len(orphan), " ".join(orphan[:8])))

    n_cell = len(out) * len(ERAS)
    n_empty = sum(1 for r in out for v in r[6:] if not v or v == "-")
    print("  六列共 %d 格：非空 %d · 空/`-` %d" % (n_cell, n_cell - n_empty, n_empty))

    buf = io.StringIO()
    w = csv.writer(buf, lineterminator="\r\n", quoting=csv.QUOTE_MINIMAL)
    w.writerow(list(cn[:6]) + COLS)
    w.writerow(list(en[:6]) + COLS)
    for r in out:
        w.writerow(r)
    text = buf.getvalue()

    if problems:
        print("\n❌ 硬问题 %d 条：" % len(problems))
        for p in problems[:30]:
            print("   %s" % p)
        return 1

    if not args.apply:
        print("\n（未加 --apply，只报告不写盘）")
        return 0

    stamp = time.strftime("%Y%m%d_%H%M%S")
    shutil.copy2(FORCE, FORCE + ".bak_ownercol_" + stamp)
    with io.open(FORCE, "w", encoding="utf-8-sig", newline="") as fh:
        fh.write(text)
    print("\n✅ 已写出 ForceTaikou.csv（%d 行 × %d 列，模式=%s）"
          % (len(out), len(cn[:6]) + len(COLS), mode))

    cn2, en2, data2 = load_rows(FORCE)
    if not (cn2 == list(cn[:6]) + COLS and en2 == list(en[:6]) + COLS):
        print("❌ 往返校验失败：表头不一致")
        return 1
    if len(data2) != len(out):
        print("❌ 往返校验失败：行数 %d ≠ %d" % (len(data2), len(out)))
        return 1
    print("  往返校验 ✅（%d 行 / 双行表头 = `Owner_<年>`）" % len(data2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
