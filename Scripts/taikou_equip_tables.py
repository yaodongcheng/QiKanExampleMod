#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""taikou_equip_tables.py —— 装备数据表的统一读取器（兵种表 + 武将装备档表）。

为什么要有它
------------
2026-09-16 用户裁定：**兵种装备、武将装备档都要数据驱动** —— 表放 `csv/`，生成器读表，
不再把装备写死在生成器的 Python 表里（与 `TaikouHero.csv` 的「甲/兜/武器」列同口径，
铁律 28：源 → 生成器 → 产物）。

两张表（都是**源表，手维护**，在 `Knowledge/太阁5/骑砍2织丰角色ID对应/csv/`）
---------------------------------------------------------------------
① **`TaikouTroop.csv`** —— 兵种表：等级 / 兵种组 / 技能组 / 文化 / 各槽装备 / 升级目标 / 民用套装。
   消费方：`Scripts/gen_taikou_culture_full.py`（出 `spnpccharacters.xml` 的兵种段）、
           `tools/sw2-pipeline/gen_troop_armor_items.py` 与 `gen_troop_weapon_items.py`
           （「谁穿了什么」= 该生成哪些物品定义）。
② **`HeroEquip.csv`** —— 武将装备档表：**没指名专属装备的武将**按身份查这张表拿装备。
   一行一个身份，**铠甲 / 头盔 / 武器各占一列**（2026-09-16 用户裁定：三者必须分开列，
   原来挤在「甲兜候选」一列里读不出也改不动）；另有女将专用的甲/盔两列
   （**有值则女将只穿它** —— 覆盖，不是追加；留空则用通用池）。
   候选写的是竖线分隔的**兵种 slug**；铠与盔**各自独立挑**（不是成对挑）。
   消费方：`Scripts/gen_taikou_era_world.py`（出 `taikou_lords*.xml`）。

列多值口径（铁律 24）
--------------------
· 一律**半角竖线 `|`** 分隔；值内禁止出现半角逗号 `,` 或竖线 `|`（本模块会校验并报错）。
· **武器列多一层**：`;` 分隔**多套随机装备**（引擎每人生成时随机挑一套），套内再用 `|` 分槽位。
  例：`wooden_sword_t1;peasant_pickaxe_1_t1` = 两套，一套木刀一套锄头。
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from csv_dual import read_table                                  # noqa: E402

CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
TROOP_CSV = os.path.join(CSV_DIR, "TaikouTroop.csv")
HERO_CSV = os.path.join(CSV_DIR, "HeroEquip.csv")

# 兵种表的「一列 → 装备槽」对照（顺序 = 写进 XML 的顺序）。武器是多值列，单列处理。
SLOT_COLS = [("Armor", "Body"), ("Helmet", "Head"), ("Leg", "Leg"),
             ("Gloves", "Gloves"), ("Horse", "Horse"), ("HorseHarness", "HorseHarness")]

# 武将装备档表里的「无身份」行键（身份列是空的武将查这一行）
DEFAULT_IDENTITY = "none"


def _cells(row, header, path):
    """双行表头取值 + 逗号自检。

    🔴 **两种逗号都拦**：半角 `,` 直接裂列（铁律 24）；全角 `，` 不裂列但**归一化
    （全角→半角，或换分隔符重导）之后就会裂** —— 2026-09-16 用户在中文表头里抓到过。
    要分隔就换词（「用竖线分隔」），正文断句用顿号 `、`。
    """
    d = dict(zip(header, row))
    for k, v in d.items():
        for ch, name in ((",", "半角逗号"), ("，", "全角逗号")):
            if v and ch in v:
                sys.exit("FAIL: %s 的「%s」列值里有%s（会裂列）：%r" % (path, k, name, v))
    return d


def _split(v):
    """竖线分隔 → list（空串 → 空 list）。"""
    v = (v or "").strip()
    return [x.strip() for x in v.split("|") if x.strip()]


# ───────────────────────── ① 兵种表 ─────────────────────────
def troops(path=None):
    """`TaikouTroop.csv` → [dict]，按表里的行序。

    每行 dict 的键 = 英文列名；另加两个算好的键：
      · `variants` = [[(slot, item_id), …], …] —— 每套随机装备的槽位表（引擎随机挑一套）
      · `upgrades` = [兵种 id, …]
    """
    path = path or TROOP_CSV
    _, en, rows = read_table(path, head=2)
    out = []
    for r in rows:
        # 空行 = 整行全空（不能只看第一列：兵种表第一列是 ID、武将表第一列是档位，
        # 它们空了整行会被静默丢掉 —— 2026-09-16 踩到）
        if not r or not any((x or "").strip() for x in r):
            continue
        d = _cells(r, en, path)
        variants = []
        for arm in (d.get("Weapons") or "").split(";"):
            slots = [("Item%d" % i, it) for i, it in enumerate(_split(arm))]
            for col, slot in SLOT_COLS:
                it = (d.get(col) or "").strip()
                if it:
                    slots.append((slot, it))
            variants.append(slots)
        d["variants"] = variants
        d["upgrades"] = _split(d.get("Upgrades"))
        d["is_basic"] = (d.get("IsBasic") or "").strip() in ("1", "true", "True")
        out.append(d)
    return out


def equip_item_ids(path=None):
    """兵种表里出现过的**所有** `Item.` id（去重）→ set。

    用途：物品生成器据此决定「该给谁出物品定义」——**有人穿才出**，
    免得生成孤儿物品（`prune_taikou_items.py` 会剪掉、剪完 `--check` 又报过期）。
    """
    ids = set()
    for d in troops(path):
        for variant in d["variants"]:
            for _slot, it in variant:
                ids.add(it)
    return ids


# ───────────────────────── ② 武将装备档表 ─────────────────────────
def hero_equip(path=None):
    """`HeroEquip.csv` → (by_identity, default_row)。

    行 dict 含：`Tier` / `Identity` / `Armors`(list) / `Helmets`(list) /
    `FemaleArmors`(list) / `FemaleHelmets`(list) / `Weapons`(list)。
    **铠与盔是各自独立的候选池**（不是成对的）—— 挑法见 `pick_hero_equip()`。
    """
    path = path or HERO_CSV
    _, en, rows = read_table(path, head=2)
    by_identity, default_row = {}, None
    for r in rows:
        # 空行 = 整行全空（不能只看第一列：兵种表第一列是 ID、武将表第一列是档位，
        # 它们空了整行会被静默丢掉 —— 2026-09-16 踩到）
        if not r or not any((x or "").strip() for x in r):
            continue
        d = _cells(r, en, path)
        for col in ("Armors", "Helmets", "FemaleArmors", "FemaleHelmets", "Weapons"):
            d[col] = _split(d.get(col))
        ident = (d.get("Identity") or "").strip()
        if ident == DEFAULT_IDENTITY:
            default_row = d
        elif ident:
            by_identity[ident] = d
    if default_row is None:
        sys.exit("FAIL: %s 里没有「%s」行（无身份武将没装备可查）" % (path, DEFAULT_IDENTITY))
    return by_identity, default_row


def hero_armor_slugs(path=None):
    """武将装备档表里用到的**铠甲** slug → set（头盔的那些由 `hero_helmet_slugs()` 管）。

    用途同 `equip_item_ids()`：武将也成了这些甲的穿戴者，所以它们也要出物品定义
    （护卫 4 套 / 九州兵 1 套本来没有对应**兵种**，全靠这里兜住）。
    """
    by_identity, default_row = hero_equip(path)
    out = set()
    for d in list(by_identity.values()) + [default_row]:
        out |= set(d["Armors"]) | set(d["FemaleArmors"])
    return out


def hero_helmet_slugs(path=None):
    """武将装备档表里用到的**头盔** slug → set（有的盔对应的甲没进甲池，比如当备选）。"""
    by_identity, default_row = hero_equip(path)
    out = set()
    for d in list(by_identity.values()) + [default_row]:
        out |= set(d["Helmets"]) | set(d["FemaleHelmets"])
    return out


def pick_hero_equip(row, hero_id, female):
    """身份行 + 武将 id + 性别 → (甲 item id, 兜 item id)。

    **甲与盔各自独立挑**（2026-09-16 用户裁定）—— 同一武将固定穿其中一件（按 id 定，
    六代一致）；两边的种子错开，免得「甲第 N 个」永远配「盔第 N 个」。

    🔴 **女将专用列是「覆盖」不是「追加」**（2026-09-16 用户裁定「女将统一穿女甲」）：
       `FemaleArmors` 有值 → 女将**只**从这个池子挑（战无2 的具足是按男性身形做的，
       女将穿会偏大；樱色女具足是照女性身形做的）；
       `FemaleHelmets` 有值 → 同理覆盖，**留空则用通用头盔池**
       （用户原话：「头盔倒是可以通用斗笠之类的」—— 笠/头巾这类不分男女）。
    """
    import zlib
    armors = list(row["FemaleArmors"]) if (female and row["FemaleArmors"]) else list(row["Armors"])
    helmets = (list(row["FemaleHelmets"]) if (female and row["FemaleHelmets"])
               else list(row["Helmets"]))
    seed = zlib.crc32(hero_id.encode("utf-8"))
    a = armors[seed % len(armors)]
    h = helmets[(seed // 7 + 1) % len(helmets)]
    return "taikou_troop_%s_do_a" % a, "taikou_troop_%s_helmet_a" % h


def armor_item_ids(slug):
    """兵种 slug → (甲 item id, 兜 item id)。id 口径与 `gen_troop_armor_items.py` 一致。"""
    return ("taikou_troop_%s_do_a" % slug, "taikou_troop_%s_helmet_a" % slug)
