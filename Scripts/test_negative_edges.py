#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""边台账负面测试（`check_reference_edges.py` 的「故意造坏数据」验证）
============================================================================
🔴 依据（必备清单「每条检查必须有脚本兜底」节）：**写完必须做负面测试**——
   故意造坏数据，确认脚本真能抓到、**exit 1**；只跑一遍真数据全绿不算数
   （真绿可能只是「检查根本没跑起来」或「判据写反了」）。

做法：把 `csv/` 拷到临时目录 → 在每个用例里**只改一格**（或用例自带合成 XML 模块）
   → 跑 `check_reference_edges.py --csv-dir <临时> --module <合成>` → 核对
   **退出码 + 输出里有没有点名那格**。跑完删临时目录，**绝不碰真数据**。

用例三类（缺一类都算防线不全）：
  ① **硬错误必须抓到**：id 悬空 / 织丰编号 / 多值列里某一项坏 / 立绘槽越界
  ② **非人物行必须豁免**（反向验证）：把坏值写进 `TemplateNPC` 非空的样板行 →
     **必须一声不吭**（不豁免就会 74 行假红；这是「非人物行不参与」口径的实证）
  ③ **孤儿必须报出来**：定义一行、全库无人引用 → 报「无人引用」
  ④ **XML 侧两个方向**：引用了注册表没有的 id → 硬错误；已登记豁免的 id → 只打印不报错

Usage:
  python Scripts/test_negative_edges.py            # 全部用例（约 10~20 秒）
  python Scripts/test_negative_edges.py -v         # 打印每个用例的脚本输出
Exit: 0 全部符合预期 / 1 有用例不符合 / 2 fatal。
"""
import argparse
import os
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
CHECKER = HERE / "check_reference_edges.py"

# ── CSV 用例：(说明, 文件, 主键列, 主键值, 目标列, 新值, 期望退出码, 必须出现/禁止出现) ──
CSV_CASES = [
    ("hero.clan 悬空（家族 id 不存在）", "TaikouHero.csv", "ID", "lord_tk5_195",
     "ClanID_1560", "clan_does_not_exist_1", 1, "clan_does_not_exist_1"),
    ("hero.culture 悬空", "TaikouHero.csv", "ID", "lord_tk5_195",
     "CultureID", "atlantis", 1, "atlantis"),
    ("hero.school 查无（名字式）", "TaikouHero.csv", "ID", "lord_tk5_195",
     "School_1560", "不存在的流派", 1, "不存在的流派"),
    ("hero.kins 多值列里坏一项", "TaikouHero.csv", "ID", "lord_tk5_195",
     "KinsId", "lord_tk5_999999", 1, "lord_tk5_999999"),
    ("hero.appearance 立绘槽越界", "TaikouHero.csv", "ID", "lord_tk5_195",
     "AppearanceID", "99999", 1, "99999"),
    ("clan.owner 悬空", "Clan.csv", "ID", "clan_oda_1",
     "Owner_1560", "lord_tk5_999999", 1, "lord_tk5_999999"),
    ("clan.kingdom 悬空", "Clan.csv", "ID", "clan_oda_1",
     "Kingdom_1560", "no_such_force", 1, "no_such_force"),
    ("force.owner 织丰编号（id 体系分叉）", "TaikouForce.csv", "ID", "oda",
     "Owner_1560", "lord_1_oda_nobunaga", 1, "lord_1_oda_nobunaga"),
    ("settle.owner 悬空", "Settlements.csv", "id", "town_tk000",
     "Owner_1560", "lord_tk5_999999", 1, "lord_tk5_999999"),
    ("settle.clan 悬空", "Settlements.csv", "id", "town_tk000",
     "Clan_1560", "clan_nope_1", 1, "clan_nope_1"),
    ("school.leader 非豁免的坏值", "School.csv", "SchoolID", "31",
     "Leader", "clan_not_a_hero", 1, "clan_not_a_hero"),

    # ② 反向验证：非人物行（TemplateNPC 非空）里的坏值**必须被豁免**
    ("非人物行豁免：样板行写坏 ClanID 必须静默", "TaikouHero.csv", "ID",
     "template_ninja_male_01", "ClanID_1560", "clan_does_not_exist_2", 0,
     None),                       # None = 输出里**不许**出现
    ("非人物行豁免：样板行写坏 City 必须静默", "TaikouHero.csv", "ID",
     "prounon_main_hero", "City_1560", "不存在的地名", 0, None),
]


def run_checker(csv_dir, module_dir):
    cmd = [sys.executable, str(CHECKER), "--csv-dir", str(csv_dir), "--module", str(module_dir)]
    r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    return r.returncode, (r.stdout or "") + (r.stderr or "")


def patch_cell(path, key_col, key_val, col, new_val):
    """用项目自己的读写器改一格（保留两行表头与换行风格）。"""
    sys.path.insert(0, str(HERE))
    from csv_dual import read_table, write_table
    cn, en, raw = read_table(str(path), head=2)
    if key_col not in en:
        raise SystemExit("[FATAL] %s 没有列 %s" % (path.name, key_col))
    ki, ci = en.index(key_col), en.index(col)
    hit = 0
    for r in raw:
        if len(r) > ki and (r[ki] or "").strip() == key_val:
            while len(r) <= ci:
                r.append("")
            r[ci] = new_val
            hit += 1
    if hit != 1:
        raise SystemExit("[FATAL] %s 里 %s=%s 命中 %d 行（应为 1）" % (path.name, key_col, key_val, hit))
    write_table(str(path), cn, en, raw)


def make_module(root, files):
    md = Path(root) / "ModuleData"
    md.mkdir(parents=True, exist_ok=True)
    for name, body in files.items():
        (md / name).write_text(body, encoding="utf-8")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", default=None,
                    help="兼容 run_all_checks 的接口（本检查自带合成模块，不用这个参数）")
    ap.add_argument("-v", "--verbose", action="store_true")
    args = ap.parse_args()

    if not CSV_SRC.is_dir():
        print("[FATAL] csv dir not found: %s" % CSV_SRC, file=sys.stderr)
        return 2

    tmp = Path(tempfile.mkdtemp(prefix="lwn_edge_neg_"))
    pristine, work = tmp / "pristine", tmp / "work"
    empty_mod, hand_mod = tmp / "mod_empty", tmp / "mod_hand"
    try:
        shutil.copytree(str(CSV_SRC), str(pristine))
        shutil.copytree(str(CSV_SRC), str(work))
        make_module(empty_mod, {})                       # 无 XML：CSV 用例用

        # ── XML 用例的合成模块（真实结构的最小复刻）──
        make_module(hand_mod, {
            "taikou_heroes.xml": '<?xml version="1.0" encoding="utf-8"?>\n<Heroes>\n'
                                 '\t<Hero id="lord_tk5_195" faction="Faction.clan_oda_1" '
                                 'text="{=X}Oda"/>\n</Heroes>\n',
            "spclans.xml": '<?xml version="1.0" encoding="utf-8"?>\n<Factions>\n'
                           '\t<Faction id="clan_oda_1" owner="Hero.lord_tk5_195" '
                           'culture="Culture.kinai"/>\n'
                           '\t<Faction id="clan_helper" owner="Hero.main_hero" '
                           'super_faction="Faction.player_faction" culture="Culture.kinai"/>\n'
                           '</Factions>\n',
        })

        results = []

        # ── ① / ② CSV 用例 ──
        for desc, fn, kcol, kval, col, new, want_code, needle in CSV_CASES:
            shutil.copy2(str(pristine / fn), str(work / fn))
            patch_cell(work / fn, kcol, kval, col, new)
            code, out = run_checker(work, empty_mod)
            if needle is None:
                ok = (code == want_code) and (new not in out)
            else:
                ok = (code == want_code) and (needle in out)
            results.append((desc, ok, code, want_code, out))
            if args.verbose or not ok:
                print("\n%s %s（exit %d，期望 %d）" % ("✅" if ok else "❌", desc, code, want_code))
                if not ok or args.verbose:
                    print("   " + "\n   ".join(out.strip().splitlines()[-12:]))
            shutil.copy2(str(pristine / fn), str(work / fn))

        # ── ③ 孤儿用例：Culture.csv 加一行无人引用的文化 ──
        sys.path.insert(0, str(HERE))
        from csv_dual import read_table, write_table
        culture = work / "Culture.csv"
        cn, en, raw = read_table(str(culture), head=2)
        raw.append(["noone_uses_me", "无人引用文化", ""])
        write_table(str(culture), cn, en, raw)
        code, out = run_checker(work, empty_mod)
        ok = (code == 0) and ("noone_uses_me" in out) and ("无人引用" in out)
        results.append(("孤儿：新增无人引用的文化必须报出", ok, code, 0, out))
        if args.verbose or not ok:
            print("\n%s 孤儿用例（exit %d，期望 0）" % ("✅" if ok else "❌", code))
            print("   " + "\n   ".join(l for l in out.splitlines() if "noone_uses_me" in l or "无人引用" in l))
        shutil.copy2(str(pristine / "Culture.csv"), str(culture))

        # ── ④ XML 用例 A：引用注册表里没有的家族 → 硬错误 ──
        mod_a = tmp / "mod_bad_ref"
        make_module(mod_a, {
            "taikou_heroes.xml": '<?xml version="1.0" encoding="utf-8"?>\n<Heroes>\n'
                                 '\t<Hero id="lord_tk5_195" faction="Faction.clan_ghost_9" '
                                 'text="{=X}Oda"/>\n</Heroes>\n',
        })
        code, out = run_checker(work, mod_a)
        ok = (code == 1) and ("clan_ghost_9" in out)
        results.append(("XML 悬空：英雄段引用不存在的家族", ok, code, 1, out))
        if args.verbose or not ok:
            print("\n%s XML 悬空用例（exit %d，期望 1）" % ("✅" if ok else "❌", code))
            print("   " + "\n   ".join(l for l in out.splitlines() if "clan_ghost_9" in l))

        # ── ④ XML 用例 B：已登记豁免的 id（player_faction / main_hero）→ 只打印不报错 ──
        code, out = run_checker(work, hand_mod)
        ok = (code == 0) and ("player_faction" in out) and ("[豁免]" in out) and ("悬空 0" in out)
        results.append(("XML 豁免：运行期自造对象只打印不报错", ok, code, 0, out))
        if args.verbose or not ok:
            print("\n%s XML 豁免用例（exit %d，期望 0）" % ("✅" if ok else "❌", code))
            print("   " + "\n   ".join(l for l in out.splitlines() if "豁免" in l or "悬空" in l))

        # ── 汇总 ──
        bad = [r for r in results if not r[1]]
        print("\n%s\n负面测试：%d 个用例，%d 通过 / %d 失败\n%s"
              % ("=" * 70, len(results), len(results) - len(bad), len(bad), "=" * 70))
        for desc, ok, code, want, _ in results:
            print("  %s %-46s exit %d（期望 %d）" % ("✅" if ok else "❌", desc, code, want))
        return 1 if bad else 0
    finally:
        shutil.rmtree(str(tmp), ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
