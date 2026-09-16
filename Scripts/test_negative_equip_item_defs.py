#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""check_equip_item_defs.py 的「故意造坏数据」负面测试
============================================================================
🔴 依据（必备清单「每条检查必须有脚本兜底」节）：**写完必须做负面测试** ——
   故意造坏数据，确认脚本真能抓到、**exit 1**；只跑一遍真数据全绿不算数。

做法：把三张表拷到临时目录 → 每个用例**只改一格** → 跑 checker（`--csv-dir` 指临时目录）
   → 核对**退出码 + 输出里有没有点名那个坏 id**。跑完删临时目录，**绝不碰真数据**。

用例覆盖
  ① **硬错误必须抓到**：兵种表装备拼错 / 武将表专属装备拼错 / 装备档表武器拼错 /
     装备档表的**铠甲 slug 推出的物品 id** 不存在
  ② **反向验证（不能误报）**：把引用改成 `<CraftedItem>` 定义的可锻造武器
     （`ridged_sabre_sword_t4`）→ **必须一声不吭** —— 这是「只认 `<Item>` 不认
     `<CraftedItem>`」那个 bug 的回归防线（2026-09-16 实际踩过）
  ③ **正向对照**：真数据必须 exit 0

Usage:
  python Scripts/test_negative_equip_item_defs.py
  python Scripts/test_negative_equip_item_defs.py -v
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
CHECKER = HERE / "check_equip_item_defs.py"
DEFAULT_MODULE = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
                  r"\Mount & Blade II Bannerlord\Modules\Taikou")

FILES = ("TaikouTroop.csv", "TaikouHero.csv", "HeroEquip.csv")

# (说明, 文件, 主键列, 主键值, 目标列, 新值, 期望退出码, 必须出现 / 禁止出现)
CASES = [
    ("真数据（正向对照）", None, None, None, None, None, 0, "全部有定义"),
    ("兵种表装备拼错", "TaikouTroop.csv", "ID", "samurai", "Armor",
     "taikou_troop_samuraii_do_a", 1, "taikou_troop_samuraii_do_a"),
    ("兵种表武器拼错", "TaikouTroop.csv", "ID", "samurai", "Weapons",
     "no_such_weapon_at_all", 1, "no_such_weapon_at_all"),
    ("武将表专属装备拼错", "TaikouHero.csv", "ID", "lord_tk5_195", "Armor",
     "taikou_no_such_do_a", 1, "taikou_no_such_do_a"),
    ("装备档表武器拼错", "HeroEquip.csv", "Identity", "侍大将", "Weapons",
     "no_such_weapon_xyz", 1, "no_such_weapon_xyz"),
    ("装备档表铠甲 slug 推出的物品不存在", "HeroEquip.csv", "Identity", "侍大将", "Armors",
     "no_such_slug", 1, "taikou_troop_no_such_slug_do_a"),
    # ② 反向验证：可锻造武器（<CraftedItem>）**不能**被误判成未定义
    ("可锻造武器不算未定义（不许误报）", "HeroEquip.csv", "Identity", "大名", "Weapons",
     "ridged_sabre_sword_t4", 0, "全部有定义"),
    ("引擎硬编码物品不算未定义（不许误报）", "HeroEquip.csv", "Identity", "大名", "Weapons",
     "pugio", 0, "全部有定义"),
]


def read_rows(path):
    return list(csv.reader(io.open(path, encoding="utf-8-sig", newline="")))


def write_rows(path, rows):
    buf = io.StringIO()
    csv.writer(buf, lineterminator="\r\n", quoting=csv.QUOTE_MINIMAL).writerows(rows)
    io.open(path, "w", encoding="utf-8-sig", newline="").write(buf.getvalue())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("-v", "--verbose", action="store_true")
    ap.add_argument("--module", default=DEFAULT_MODULE,
                    help="（本脚本拿它当「物品定义」的参照物）")
    args = ap.parse_args()
    if not Path(args.module).is_dir():
        sys.exit("FAIL: 找不到模块 %s（负面测试要拿它当参照物）" % args.module)

    bad = []
    tmp = Path(tempfile.mkdtemp(prefix="lwn_itemdef_neg_"))
    try:
        for f in FILES:
            shutil.copy(CSV_SRC / f, tmp)

        for desc, fname, keycol, keyval, col, newval, want_rc, want_msg in CASES:
            if fname is not None:
                for f in FILES:
                    shutil.copy(CSV_SRC / f, tmp / f)
                rows = read_rows(tmp / fname)
                ki, ci = rows[1].index(keycol), rows[1].index(col)
                hit = False
                for r in rows[2:]:
                    if len(r) > ki and (r[ki] or "").strip() == keyval:
                        r[ci] = newval
                        hit = True
                        break
                if not hit:
                    bad.append("%s：用例自身无效 —— %s 里找不到 %s=%s" % (desc, fname, keycol, keyval))
                    continue
                write_rows(tmp / fname, rows)

            p = subprocess.run([sys.executable, str(CHECKER), "--module", args.module,
                                "--csv-dir", str(tmp)], capture_output=True, cwd=str(REPO))
            out = p.stdout.decode("utf-8", errors="replace") + p.stderr.decode("utf-8", errors="replace")
            if args.verbose:
                print(out)
            ok = (p.returncode == want_rc) and (want_msg in out)
            print("  %s %s" % ("✅" if ok else "❌", desc))
            if not ok:
                bad.append("%s：期望 rc=%d 且输出含 %r，实际 rc=%d" % (desc, want_rc, want_msg, p.returncode))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    if bad:
        print("== 不符合预期的用例（%d）==" % len(bad))
        for b in bad:
            print("  [✗] %s" % b)
        return 1
    print("负面测试全部符合预期（%d 例）✅" % len(CASES))
    return 0


if __name__ == "__main__":
    sys.exit(main())
