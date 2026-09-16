#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""check_taikou_equip_tables.py 的「故意造坏数据」负面测试
============================================================================
🔴 依据（必备清单「每条检查必须有脚本兜底」节）：**写完必须做负面测试** ——
   故意造坏数据，确认脚本真能抓到、**exit 1**；只跑一遍真数据全绿不算数
   （真绿可能只是「检查根本没跑起来」或「判据写反了」）。

做法：把两张装备表拷到临时目录 → 每个用例**只改一格** → 跑 checker（`--csv-dir` 指临时目录）
   → 核对**退出码 + 输出里有没有点名那个坏值**。跑完删临时目录，**绝不碰真数据**。

用例覆盖（每一类判据至少一个）
  ① 硬错误必须抓到：ID 重复 / Level 非法 / 技能越档 / slug 拼错 / 升级目标不存在 /
     武将用了「没兜」的套 / 值里半角逗号 / 值里与**表头里**全角逗号
  ② 结构性缺失必须抓到：武将表少了「none」行
  ③ **正向对照**：真数据必须 exit 0（防止判据写成「永远报错」）

Usage:
  python Scripts/test_negative_equip_tables.py            # 全部用例
  python Scripts/test_negative_equip_tables.py -v         # 打印每个用例的脚本输出
Exit: 0 全部符合预期 / 1 有用例不符合 / 2 fatal。
"""
import argparse
import csv
import io
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
CSV_SRC = REPO / "Knowledge" / "太阁5" / "骑砍2织丰角色ID对应" / "csv"
CHECKER = HERE / "check_taikou_equip_tables.py"
DEFAULT_MODULE = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
                  r"\Mount & Blade II Bannerlord\Modules\Taikou")

# (说明, 文件, 主键列, 主键值, 目标列, 新值, 期望退出码, 输出里必须出现)
CASES = [
    ("真数据（正向对照）", None, None, None, None, None, 0, "自洽"),
    ("兵种 ID 重复", "TaikouTroop.csv", "ID", "samurai", "ID", "taisho", 1, "ID 重复"),
    ("Level 非整数", "TaikouTroop.csv", "ID", "samurai", "Level", "十六", 1, "Level 不是"),
    ("技能越档（Lv6 兵用 Lv16 集）", "TaikouTroop.csv", "ID", "yari_ashigaru", "Skill",
     "SkillSet.infantry_heavyinfantry_level16_template_skills", 1, "高于兵种等级"),
    ("技能组不存在", "TaikouTroop.csv", "ID", "samurai", "Skill",
     "SkillSet.no_such_set_template_skills", 1, "技能组不存在"),
    ("甲 slug 拼错", "TaikouTroop.csv", "ID", "samurai", "Armor",
     "taikou_troop_samuraii_do_a", 1, "samuraii"),
    ("升级目标不存在", "TaikouTroop.csv", "ID", "yari_ashigaru", "Upgrades",
     "no_such_troop", 1, "升级目标不存在"),
    ("文化不在 Culture.csv", "TaikouTroop.csv", "ID", "samurai", "Culture", "atlantis",
     1, "文化不在"),
    ("兵种组非法", "TaikouTroop.csv", "ID", "samurai", "Group", "Navy", 1, "Group 非法"),
    ("值里半角逗号", "TaikouTroop.csv", "ID", "samurai", "CNName", "武,士", 1, "半角逗号"),
    ("武将用了「没兜」的套", "HeroEquip.csv", "Identity", "大名", "Armors",
     "genin|guard4", 1, "没有兜件"),
    ("武将 slug 拼错", "HeroEquip.csv", "Identity", "大名", "Armors",
     "no_such_slug|guard4", 1, "no_such_slug"),
    ("铠甲候选清空", "HeroEquip.csv", "Identity", "大名", "Armors", "", 1, "铠甲候选是空的"),
    ("武将身份重复", "HeroEquip.csv", "Identity", "城主", "Identity", "大名", 1, "身份重复"),
    ("头盔候选用了没兜的 slug", "HeroEquip.csv", "Identity", "大名", "Helmets",
     "genin|guard4", 1, "没有兜件"),
    ("头盔候选清空", "HeroEquip.csv", "Identity", "大名", "Helmets", "", 1, "头盔候选是空的"),
    ("档位标签为空", "HeroEquip.csv", "Identity", "大名", "Tier", "", 1, "档位为空"),
    ("同档两行的甲候选不一致（改一行忘一行）", "HeroEquip.csv", "Identity", "城主", "Armors",
     "guard4", 1, "不一致"),
    ("值里全角逗号（归一化后会裂列）", "HeroEquip.csv", "Identity", "大名", "Armors",
     "guard4，guard3", 1, "全角逗号"),
    ("表头里全角逗号（2026-09-16 实际踩到的那格）", "HeroEquip.csv", "Identity", "大名",
     "Armors", "@@HDR@@", 1, "全角逗号"),
]

# 删行用例：(说明, 文件, 主键列, 主键值, 期望退出码, 必须出现)
DEL_CASES = [
    ("武将表少了「none」行", "HeroEquip.csv", "Identity", "none", 1, "只有一行"),
]


def read_rows(path):
    return list(csv.reader(io.open(path, encoding="utf-8-sig", newline="")))


def write_rows(path, rows):
    buf = io.StringIO()
    w = csv.writer(buf, lineterminator="\r\n", quoting=csv.QUOTE_MINIMAL)
    w.writerows(rows)
    io.open(path, "w", encoding="utf-8-sig", newline="").write(buf.getvalue())


def run_checker(csv_dir, verbose):
    p = subprocess.run([sys.executable, str(CHECKER), "--module", DEFAULT_MODULE,
                        "--csv-dir", str(csv_dir)],
                       capture_output=True, cwd=str(REPO))
    out = p.stdout.decode("utf-8", errors="replace") + p.stderr.decode("utf-8", errors="replace")
    if verbose:
        print(out)
    return p.returncode, out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("-v", "--verbose", action="store_true")
    ap.add_argument("--module", default=DEFAULT_MODULE, help="（本脚本用默认模块做参照物）")
    args = ap.parse_args()
    if not Path(args.module).is_dir():
        sys.exit("FAIL: 找不到模块 %s（负面测试要拿它当参照物）" % args.module)

    bad = []
    tmp = Path(tempfile.mkdtemp(prefix="lwn_equip_neg_"))
    try:
        shutil.copy(CSV_SRC / "TaikouTroop.csv", tmp)
        shutil.copy(CSV_SRC / "HeroEquip.csv", tmp)
        shutil.copy(CSV_SRC / "Culture.csv", tmp)      # checker 查文化要用
        clean = tmp / "_clean"

        for desc, fname, keycol, keyval, col, newval, want_rc, want_msg in CASES:
            if fname is None:
                rc, out = run_checker(tmp, args.verbose)
            else:
                for f in ("TaikouTroop.csv", "HeroEquip.csv"):
                    shutil.copy(CSV_SRC / f, tmp / f)
                rows = read_rows(tmp / fname)
                if newval == "@@HDR@@":            # 专用：往**中文表头**那格里塞逗号
                    rows[0][rows[1].index(col)] = "候选（甲，乙）"
                    write_rows(tmp / fname, rows)
                    rc, out = run_checker(tmp, args.verbose)
                    ok = (rc == want_rc) and (want_msg in out)
                    print("  %s %s" % ("✅" if ok else "❌", desc))
                    if not ok:
                        bad.append("%s：期望 rc=%d 且输出含 %r，实际 rc=%d"
                                   % (desc, want_rc, want_msg, rc))
                    continue
                hdr = rows[1]
                ki, ci = hdr.index(keycol), hdr.index(col)
                hit = 0
                for r in rows[2:]:
                    if len(r) > ki and (r[ki] or "").strip() == keyval:
                        r[ci] = newval
                        hit += 1
                        break
                if not hit:
                    bad.append("%s：用例自身无效 —— %s 里找不到 %s=%s" % (desc, fname, keycol, keyval))
                    continue
                write_rows(tmp / fname, rows)
                rc, out = run_checker(tmp, args.verbose)
            ok = (rc == want_rc) and (want_msg in out)
            print("  %s %s" % ("✅" if ok else "❌", desc))
            if not ok:
                bad.append("%s：期望 rc=%d 且输出含 %r，实际 rc=%d" % (desc, want_rc, want_msg, rc))

        for desc, fname, keycol, keyval, want_rc, want_msg in DEL_CASES:
            for f in ("TaikouTroop.csv", "HeroEquip.csv"):
                shutil.copy(CSV_SRC / f, tmp / f)
            rows = read_rows(tmp / fname)
            ki = rows[1].index(keycol)
            kept = [rows[0], rows[1]] + [r for r in rows[2:]
                                         if not (len(r) > ki and (r[ki] or "").strip() == keyval)]
            write_rows(tmp / fname, kept)
            rc, out = run_checker(tmp, args.verbose)
            ok = (rc == want_rc) and (want_msg in out)
            print("  %s %s" % ("✅" if ok else "❌", desc))
            if not ok:
                bad.append("%s：期望 rc=%d 且输出含 %r，实际 rc=%d" % (desc, want_rc, want_msg, rc))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    if bad:
        print("== 不符合预期的用例（%d）==" % len(bad))
        for b in bad:
            print("  [✗] %s" % b)
        return 1
    print("负面测试全部符合预期（%d 例）✅" % (len(CASES) + len(DEL_CASES)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
