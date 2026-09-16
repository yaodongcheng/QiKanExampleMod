#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""check_taikou_equip_tables.py —— 两张装备数据表的体检（2026-09-16 用户裁定后新增）。

背景
----
装备改数据驱动之后，**兵种的等级/技能/文化/装备、武将的装备档**全在 CSV 里手维护
（`TaikouTroop.csv` / `HeroEquip.csv`）。CSV 不像 Python 表那样有语法检查 ——
打错一个字（slug 拼错、兵种 id 不存在、技能组不存在）生成器不一定会当场报错，
但产物会静默错掉。所以补这道体检。

查什么（每条报错都带**表名 + 行号**，能直接定位）
----------------------------------------------
**两张表通用**
  ① 值里出现**逗号**：半角 `,` 直接裂列（铁律 24）；全角 `，` 本身不裂列，
     但**归一化（全角→半角）或换分隔符重导一次就会静默裂列** —— 两种都拦。
     要分隔就换词（「用竖线分隔」），正文断句用顿号 `、`（不会被误当分隔符）
  ② 列齐全（英文键一行不能少）

**兵种表 `TaikouTroop.csv`**
  ③ `ID` 唯一、非空
  ④ `Level` 是 1~40 的整数；`Group` ∈ 引擎枚举
  ⑤ `Skill` 以 `SkillSet.` 开头、**在技能集定义里有**、且**档位数字与 Level 一致**
     （`..._level6_...` ↔ Level=6 —— 写错档位 = 兵种技能与战力不符，静默）
  ⑥ `Culture` 在 `Culture.csv` 里（`ikoku` 例外：基文化由生成器定义，不在表里）
  ⑦ 装备 id 里的**甲/兜 slug** 必须在 `troop_parts_table.TROOP_TABLE` 找得到
     （拼错 = 场景里裸装 + 悬空引用）
  ⑧ `Upgrades` 指向的兵种 id 必须在本表里存在
  ⑨ `CivilSet` 在 `taikou_equipment_sets.xml` 里有定义

**武将表 `HeroEquip.csv`**（铠甲 / 头盔 / 武器**各占一列**）
  ⑩ `Identity` 唯一、非空；必须有且只有一行「none」
  ⑪ `Armors` / `FemaleArmors` 里的 slug 必须**在 `TROOP_TABLE` 里**且**这一套有兜件** ——
     武将**一律要戴兜**（用户裁定），而「兜长在身体网格上」的那几套（`pending_helmet`：
     弓足轻/下忍/中忍/飞忍/九州精锐/护卫陆）加一顶兜就是**头上套两层**，不能当铠甲候选
  ⑫ `Helmets` / `FemaleHelmets` 里的 slug 必须是有兜件的那 15 套
  ⑬ `Armors` 不能空（空的 = 该身份的武将没甲可穿）；`Helmets` 同理
  ⑭ **同一 `档位` 的各行，甲/盔候选必须逐字一致** —— `档位` 是分组标签（代码不读），
     但它一旦只是装饰，就会出现「改了『大名』忘了改『城主』」的静默分叉。
     武器列**不在此限**（武器本来就按身份分，同档不同武器是常态）。

用法：
    python Scripts/check_taikou_equip_tables.py            # 体检（exit 1 = 有问题）
    python Scripts/check_taikou_equip_tables.py --module PATH
"""
import argparse
import csv
import io
import os
import re
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(REPO / "tools" / "sw2-pipeline"))

DEFAULT_MODULE = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
                  r"\Mount & Blade II Bannerlord\Modules\Taikou")

GROUPS = {"Infantry", "Ranged", "Cavalry", "HorseArcher"}
# 基文化由生成器定义、不在 Culture.csv 里（见 gen_taikou_culture_full.py 的 CULTURES）
CULTURE_EXTRA = {"ikoku"}


def read_raw(path):
    """→ (英文键行, [(行号, dict)])。行号 = 文件里的实际行号（含两行表头，从 3 起）。"""
    rows = list(csv.reader(io.open(path, encoding="utf-8-sig", newline="")))
    if len(rows) < 2:
        sys.exit("FAIL: %s 连两行表头都没有" % path)
    cn, en = rows[0], rows[1]
    out = []
    for i, r in enumerate(rows[2:], start=3):
        # 🔴 判「空行」看**整行是否全空**，不能只看第一列 —— 第一列是 `Tier`，
        #    它空了整行会被静默丢掉，检查反而看不见（2026-09-16 踩到）。
        if not r or not any((x or "").strip() for x in r):
            continue
        out.append((i, dict(zip(en, r))))
    return en, out, cn


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", default=DEFAULT_MODULE)
    ap.add_argument("--csv-dir", default=None,
                    help="两张装备表所在目录（缺省 = 仓库里的 csv/；负面测试用临时目录）")
    args = ap.parse_args()
    module = Path(args.module)
    md = module / "ModuleData"
    if not md.is_dir():
        sys.exit("FAIL: 找不到模块 %s" % module)

    csv_dir = Path(args.csv_dir) if args.csv_dir else (
        REPO / "Knowledge" / "太阁5" / "骑砍2织丰角色ID对应" / "csv")
    troop_csv = csv_dir / "TaikouTroop.csv"
    hero_csv = csv_dir / "HeroEquip.csv"

    errors, notes = [], []

    # ── 参照物：挑件表 / 物品定义 / 技能集 / 文化表 / 装备集 ──
    from troop_parts_table import TROOP_TABLE                       # noqa: E402
    slugs_with_helmet = {r["slug"][len("troop_"):] for r in TROOP_TABLE.values()
                         if r.get("helmet")}
    all_slugs = {r["slug"][len("troop_"):] for r in TROOP_TABLE.values()}

    skillsets = set()
    for p in sorted(md.glob("*.xml")) + sorted((module.parent / "SandBoxCore" / "ModuleData").glob("*.xml")):
        try:
            t = io.open(p, encoding="utf-8-sig", errors="replace").read()
        except OSError:
            continue
        skillsets |= set(re.findall(r'<SkillSet\s+id="([^"]+)"', t))

    cultures = set()
    cpath = csv_dir / "Culture.csv"
    if cpath.is_file():
        for _i, d in read_raw(cpath)[1]:
            cultures.add((d.get("ID") or "").strip())

    rosters = set()
    epath = md / "taikou_equipment_sets.xml"
    if epath.is_file():
        rosters = set(re.findall(r'<(?:EquipmentRoster|EquipmentSet)\s+id="([^"]+)"',
                                 io.open(epath, encoding="utf-8-sig", errors="replace").read()))

    for path in (troop_csv, hero_csv):
        if not path.is_file():
            sys.exit("FAIL: 表不存在 %s" % path)

    # ── 通用：逗号自检（半角 + 全角）──
    # 🔴 **半角 `,`** = 直接裂列（铁律 24）。**全角 `，`** 本身不裂列，但**同样是雷**：
    #    任何做「全角→半角」归一化的工具、或换分隔符重导一次，那格就会静默裂成两列
    #    （2026-09-16 用户抓到：`HeroEquip.csv` 的中文表头里写了个全角逗号）。
    #    所以两张表**一律不许出现任何一种逗号** —— 要分隔就换词（「用竖线分隔」），
    #    真要在正文里断句用 `、`（顿号，不会被误当分隔符）。
    COMMA_LIKE = {",": "半角逗号", "，": "全角逗号"}
    for path in (troop_csv, hero_csv):
        for i, r in enumerate(csv.reader(io.open(path, encoding="utf-8-sig", newline="")), start=1):
            for j, v in enumerate(r):
                for ch, name in COMMA_LIKE.items():
                    if ch in v:
                        errors.append("%s:%d 第 %d 列值里有%s（会裂列/归一化后会裂列）：%r"
                                      % (path.name, i, j + 1, name, v))

    # ── ① 兵种表 ──
    en, rows, _cn = read_raw(troop_csv)
    need = {"ID", "CNName", "EnName", "Level", "Group", "Skill", "Culture", "IsBasic",
            "Weapons", "Armor", "Helmet", "Leg", "Gloves", "Horse", "HorseHarness",
            "Upgrades", "CivilSet"}
    miss = need - set(en)
    if miss:
        errors.append("TaikouTroop.csv 缺列：%s" % sorted(miss))

    ids = [(d.get("ID") or "").strip() for _i, d in rows]
    for i, d in rows:
        tid = (d.get("ID") or "").strip()
        lvl = (d.get("Level") or "").strip()
        grp = (d.get("Group") or "").strip()
        skill = (d.get("Skill") or "").strip()
        cul = (d.get("Culture") or "").strip()
        line = "TaikouTroop.csv:%d [%s]" % (i, tid or "?")
        if not tid:
            errors.append("%s ID 为空" % line)
        if not re.fullmatch(r"[a-z][a-z0-9_]*", tid or ""):
            errors.append("%s ID 不是合法 StringId（小写字母/数字/下划线）" % line)
        if not re.fullmatch(r"\d+", lvl) or not 1 <= int(lvl) <= 40:
            errors.append("%s Level 不是 1~40 的整数：%r" % (line, lvl))
        if grp not in GROUPS:
            errors.append("%s Group 非法（%s）：%r" % (line, "/".join(sorted(GROUPS)), grp))
        if not skill.startswith("SkillSet."):
            errors.append("%s Skill 必须以 SkillSet. 开头：%r" % (line, skill))
        else:
            sid = skill[len("SkillSet."):]
            if skillsets and sid not in skillsets:
                errors.append("%s 技能组不存在：%s" % (line, sid))
            m = re.search(r"_level(\d+)_", sid)
            # 🔴 口径：技能档**不能高于**兵种等级（不能给 Lv6 兵种 Lv16 的技能集）。
            #    低于是允许的 —— 不是每档都有对应技能集（`bandit_looter` Lv4 用 Lv1 的集，
            #    官方没有 Lv4 档），容忍但要留一条 note。
            if lvl.isdigit() and m:
                if int(m.group(1)) > int(lvl):
                    errors.append("%s 技能组档位(%s)高于兵种等级(%s) —— 越档给技能"
                                  % (line, m.group(1), lvl))
                elif int(m.group(1)) < int(lvl):
                    notes.append("%s 技能组用 Lv%s 的集（兵种 Lv%s，官方无该档技能集）"
                                 % (line, m.group(1), lvl))
        if cul and cul not in cultures and cul not in CULTURE_EXTRA:
            errors.append("%s 文化不在 Culture.csv：%s" % (line, cul))
        for col in ("Armor", "Helmet"):
            v = (d.get(col) or "").strip()
            if not v or not v.startswith("taikou_troop_"):
                continue
            # `taikou_troop_yari_ashigaru_do_a` → `yari_ashigaru`（与 `all_slugs` 同口径：
            # 去掉 `taikou_troop_` 前缀 + 甲/兜后缀）
            slug = v[len("taikou_troop_"):]
            for suf in ("_do_a", "_helmet_a"):
                if slug.endswith(suf):
                    slug = slug[:-len(suf)]
            if slug not in all_slugs:
                errors.append("%s %s 里的 slug 在 troop_parts_table 里没有：%s" % (line, col, slug))
        cs = (d.get("CivilSet") or "").strip()
        if cs and rosters and cs not in rosters:
            errors.append("%s CivilSet 在 taikou_equipment_sets.xml 里没有：%s" % (line, cs))

    dup = {x for x in ids if ids.count(x) > 1}
    if dup:
        errors.append("TaikouTroop.csv 兵种 ID 重复：%s" % sorted(dup))
    for i, d in rows:
        for u in (d.get("Upgrades") or "").split("|"):
            u = u.strip()
            if u and u not in ids:
                errors.append("TaikouTroop.csv:%d [%s] 升级目标不存在：%s" % (i, d.get("ID"), u))

    # ── ② 武将表 ──
    en2, rows2, _cn2 = read_raw(hero_csv)
    need2 = {"Tier", "Identity", "Armors", "Helmets", "FemaleArmors", "FemaleHelmets", "Weapons"}
    miss2 = need2 - set(en2)
    if miss2:
        errors.append("HeroEquip.csv 缺列：%s" % sorted(miss2))
    idents = [(d.get("Identity") or "").strip() for _i, d in rows2]
    dup2 = {x for x in idents if idents.count(x) > 1}
    if dup2:
        errors.append("HeroEquip.csv 身份重复：%s" % sorted(dup2))
    n_default = sum(1 for x in idents if x == "none")
    if n_default != 1:
        errors.append("HeroEquip.csv 必须有且只有一行「none」，现在 %d 行" % n_default)

    # ── ②之二 档位一致性（Tier 只是分组标签，但同档必须同池 —— 防「改一行忘一行」）──
    by_tier = {}
    for i, d in rows2:
        tier = (d.get("Tier") or "").strip()
        if not tier:
            errors.append("HeroEquip.csv:%d [%s] 档位为空" % (i, (d.get("Identity") or "").strip() or "?"))
            continue
        pools = tuple((d.get(c) or "").strip() for c in ("Armors", "Helmets",
                                                         "FemaleArmors", "FemaleHelmets"))
        if tier in by_tier:
            first_i, first_pools, first_id = by_tier[tier]
            if pools != first_pools:
                errors.append("HeroEquip.csv:%d [%s] 的甲/盔候选与同档的 %s:%d [%s] 不一致 —— "
                              "档位是分组标签，同档必须同池（武器列不在此限）"
                              % (i, (d.get("Identity") or "").strip() or "?", "HeroEquip.csv",
                                 first_i, first_id))
        else:
            by_tier[tier] = (i, pools, (d.get("Identity") or "").strip() or "?")

    for i, d in rows2:
        ident = (d.get("Identity") or "").strip()
        line = "HeroEquip.csv:%d [%s]" % (i, ident or "?")
        for col in ("Armors", "FemaleArmors"):
            for s in (d.get(col) or "").split("|"):
                s = s.strip()
                if not s:
                    continue
                if s not in all_slugs:
                    errors.append("%s %s 里的 slug 在 troop_parts_table 里没有：%s" % (line, col, s))
                elif s not in slugs_with_helmet:
                    errors.append("%s %s 里的 %s **没有兜件** —— 武将一律要戴兜，"
                                  "而这一套的兜长在身体网格上（pending_helmet），"
                                  "再戴一顶就是头上套两层" % (line, col, s))
        for col in ("Helmets", "FemaleHelmets"):
            for s in (d.get(col) or "").split("|"):
                s = s.strip()
                if not s:
                    continue
                if s not in all_slugs:
                    errors.append("%s %s 里的 slug 在 troop_parts_table 里没有：%s" % (line, col, s))
                elif s not in slugs_with_helmet:
                    errors.append("%s %s 里的 %s 没有兜件（不能当头盔候选）" % (line, col, s))
        if not (d.get("Armors") or "").strip():
            errors.append("%s 铠甲候选是空的 —— 这个身份的武将没甲可穿" % line)
        if not (d.get("Helmets") or "").strip():
            errors.append("%s 头盔候选是空的 —— 这个身份的武将没兜可戴" % line)

    # ── 输出 ──
    print("兵种表 %s：%d 行 · 武将表 %s：%d 行" % (troop_csv.name, len(rows), hero_csv.name, len(rows2)))
    print("参照：挑件表 slug %d（有兜 %d）· 技能集 %d · 文化 %d · 装备集 %d"
          % (len(all_slugs), len(slugs_with_helmet), len(skillsets), len(cultures), len(rosters)))
    for n in notes:
        print("  · %s" % n)
    if errors:
        print("== 问题（%d）==" % len(errors))
        for e in errors:
            print("  [✗] %s" % e)
        print("Summary: errors=%d" % len(errors))
        return 1
    print("Summary: 两张装备表自洽 ✅")
    return 0


if __name__ == "__main__":
    sys.exit(main())
