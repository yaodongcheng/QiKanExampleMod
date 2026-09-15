# -*- coding: utf-8 -*-
"""gen_hero_wiring_cols.py —— TaikouHero.csv 的「外观/装备绑定」三列（**数据侧唯一真源**）。

为什么要这张表（2026-09-15 用户裁定）
--------------------------------------
"谁长什么脸 / 谁穿什么甲 / 谁戴什么盔 / 谁拿什么武器" 都是**数据**，不该写死在生成器里。
所以统一进 `TaikouHero.csv`，`gen_taikou_era_world.py` 只负责读：

    | 列 | 键 | 谁写 | 消费方 |
    |---|---|---|---|
    | 甲   | `Armor`   | `gen_armor_items.py` | `gen_taikou_era_world.py`（战斗装 Body 槽） |
    | 头盔 | `Helmet`  | `gen_armor_items.py` | 同上（Head 槽，**待接**） |
    | 头   | `Race`    | 本脚本（从 `SPECIAL_RACE` 一次性搬进来） | 同上（`race=` 属性） |
    | 武器 | `Weapon`  | 做武器时填 | 同上（Item0 槽，**待做**） |

用法：
    python tools/sw2-pipeline/gen_hero_wiring_cols.py            # 写盘（幂等）
    python tools/sw2-pipeline/gen_hero_wiring_cols.py --check    # 只校验
"""
import argparse
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(REPO, "Scripts"))
from csv_dual import read_table, write_table  # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERO_CSV = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "TaikouHero.csv")
# (中文标签, 英文键, 初值来源)
COLS = [("头", "Race"), ("武器", "Weapon")]


def race_map():
    """从现在的 `SPECIAL_RACE`（生成器里的代码表）搬一次 —— 搬完它就是数据侧的真源。"""
    import importlib.util
    p = os.path.join(REPO, "Scripts", "gen_taikou_era_world.py")
    spec = importlib.util.spec_from_file_location("gew", p)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return dict(getattr(m, "SPECIAL_RACE", {}))


def build():
    cn, en, rows = read_table(HERO_CSV, head=2)
    cn, en = list(cn), list(en)
    added = []
    for c_cn, c_en in COLS:
        if c_en not in en:
            cn.append(c_cn); en.append(c_en)
            rows = [list(r) + [""] for r in rows]
            added.append(c_en)
    races = race_map()
    j = en.index("Race")
    n = 0
    for r in rows:
        v = races.get(r[0] if r else "", "")
        if v and (len(r) <= j or r[j] != v):
            while len(r) <= j:
                r.append("")
            r[j] = v
            n += 1
    return cn, en, rows, added, n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()
    cn, en, rows, added, n = build()
    cur_cn, cur_en, cur_rows = read_table(HERO_CSV, head=2)
    same = (list(cur_cn) == cn and list(cur_en) == en
            and [list(r) for r in cur_rows] == [list(r) for r in rows])
    if args.check:
        print("%s TaikouHero 外观绑定列" % ("✅" if same else "❌ 过期"))
        return 0 if same else 1
    if same:
        print("✅ 无变化（幂等）")
        return 0
    write_table(HERO_CSV, cn, en, rows)
    print("写出 TaikouHero.csv：新增列 %s · Race 填 %d 行 · 共 %d 列"
          % (added or "（无）", n, len(en)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
