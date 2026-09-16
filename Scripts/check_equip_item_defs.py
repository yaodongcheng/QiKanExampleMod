#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""check_equip_item_defs.py —— 三张「装备来源表」引用的物品**必须都有定义**（2026-09-16 用户要求）。

三张表（装备从这三处流进游戏）
------------------------------
  ① **`TaikouTroop.csv`**  —— 兵种表：各槽装备 + 武器（可多套，`;` 分隔、套内 `|` 分槽位）
  ② **`TaikouHero.csv`**   —— 武将表：`Armor` / `Helmet` / `Weapon` / `Ammo` 四列（指名专属装备）
  ③ **`HeroEquip.csv`**    —— 武将装备档表：`Armors` / `Helmets` / `FemaleArmors` / `FemaleHelmets`
                              （写的是兵种 slug → 推出 `taikou_troop_<slug>_do_a` / `_helmet_a`）
                              + `Weapons` 列

「有定义」= 三层**任意一层**命中（判据与 `check_taikou_xml_references.py` 同口径，不另造一套）
------------------------------------------------------------------------------------
  ① 本包自带：`Taikou/ModuleData/taikou_items/*.xml`（`<Items>` 根的直接子节点）
  ② 引擎模块 XML：`Native` / `SandBoxCore` / `SandBox` 的 `ModuleData/**/*.xml`
     ⚠️ 物品有两种元素：`<Item>`（普通）与 **`<CraftedItem>`（可锻造武器）**，两种都要收
  ③ **引擎 C# 硬编码**：`DefaultItems.Create` 在内存里现造、XML 里查不到的那批
     —— 清单 = `Scripts/prune_taikou_items.py` 的 `ENGINE_ITEMS`（反编译固化，**单一来源**）

**三层都不命中 = 硬错误**（拼错 / 漏定义 → 运行期裸装、悬空引用）。
命中 ②/③ = 合规，但**逐条列出来**（那是「本包没自带、靠外部提供」的部分，自给率一目了然）。

为什么必须单独查这道
--------------------
XML 侧的悬空引用 `check_taikou_xml_references.py` 会抓，但那是**产物**侧 ——
CSV 里拼错一个 id，要等生成器跑完、产物写盘才暴露，中间隔着好几步。
这道把检查前移到**源表**，改表就能立刻知道对不对。

用法：
    python Scripts/check_equip_item_defs.py                 # 体检（exit 1 = 有未定义的引用）
    python Scripts/check_equip_item_defs.py --module PATH
    python Scripts/check_equip_item_defs.py --csv-dir PATH  # 负面测试用临时目录
"""
import argparse
import csv
import io
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(REPO / "tools" / "sw2-pipeline"))

DEFAULT_MODULE = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
                  r"\Mount & Blade II Bannerlord\Modules\Taikou")
ENGINE_MODULES = ("Native", "SandBoxCore", "SandBox")
SKIP = {"", "-", "none"}


def cells(path):
    """双行表头 → [(行号, dict)]（行号含表头两行，从 3 起）。"""
    rows = list(csv.reader(io.open(path, encoding="utf-8-sig", newline="")))
    if len(rows) < 2:
        sys.exit("FAIL: %s 连两行表头都没有" % path)
    en = rows[1]
    out = []
    for i, r in enumerate(rows[2:], start=3):
        if r and any((x or "").strip() for x in r):      # 空行 = 整行全空（别只看第一列）
            out.append((i, dict(zip(en, r))))
    return out


def split_multi(v):
    """`;` 分多套、`|` 分槽位 —— 两种分隔都拆开，只留非空项。"""
    out = []
    for part in (v or "").split(";"):
        for x in part.split("|"):
            x = x.strip()
            if x and x.lower() not in SKIP:
                out.append(x)
    return out


def item_ids(paths):
    """物品表里定义的 id 集合。

    🔴 **不能只认 `<Item id=…>`**（2026-09-16 踩过）：Bannerlord 的物品有两种元素 ——
    `<Item>`（普通物品）与 **`<CraftedItem>`（可锻造武器**，带 crafting_template + 部件，
    如 `ridged_sabre_sword_t4` / `wooden_sword_t1` / `peasant_pickaxe_1_t1`）。
    只匹配 `<Item>` 会把 301 条可锻造武器全判成「未定义」。
    所以照 `check_taikou_xml_references.py` 的口径：**取根元素 `<Items>` 的直接子节点**
    （不猜标签名，两种都收）。
    """
    out = set()
    for p in paths:
        try:
            root = ET.parse(str(p)).getroot()
        except Exception:
            continue
        if root.tag != "Items":
            continue
        for child in root:
            iid = child.get("id")
            if iid:
                out.add(iid)
    return out


def definitions(module, official_root):
    """→ (本包定义 set, 引擎模块定义 set, 引擎硬编码 set)。"""
    local = item_ids(sorted((module / "ModuleData" / "taikou_items").glob("*.xml")))
    eng = set()
    for mod in ENGINE_MODULES:
        d = official_root / "Modules" / mod / "ModuleData"
        if d.is_dir():
            eng |= item_ids(sorted(d.rglob("*.xml")))
    import prune_taikou_items as P            # 引擎硬编码清单的**单一来源**
    return local, eng, set(P.ENGINE_ITEMS)


def collect_refs(csv_dir, hero_armor_ids):
    """→ [(表名, 行号, 列名, item_id)]。三张表的装备引用全在这。"""
    refs = []
    troop = csv_dir / "TaikouTroop.csv"
    if troop.is_file():
        for i, d in cells(troop):
            for col in ("Weapons", "Armor", "Helmet", "Leg", "Gloves", "Horse", "HorseHarness"):
                for x in split_multi(d.get(col)):
                    refs.append(("TaikouTroop.csv", i, col, x))
    hero = csv_dir / "TaikouHero.csv"
    if hero.is_file():
        for i, d in cells(hero):
            for col in ("Armor", "Helmet", "Weapon", "Ammo"):
                for x in split_multi(d.get(col)):
                    refs.append(("TaikouHero.csv", i, col, x))
    he = csv_dir / "HeroEquip.csv"
    if he.is_file():
        for i, d in cells(he):
            for x in split_multi(d.get("Weapons")):
                refs.append(("HeroEquip.csv", i, "Weapons", x))
            # 铠甲/头盔列写的是**兵种 slug** → 推出物品 id 后一并查
            for slug in split_multi(d.get("Armors")) + split_multi(d.get("FemaleArmors")):
                refs.append(("HeroEquip.csv", i, "Armors", hero_armor_ids[0] % slug))
            for slug in split_multi(d.get("Helmets")) + split_multi(d.get("FemaleHelmets")):
                refs.append(("HeroEquip.csv", i, "Helmets", hero_armor_ids[1] % slug))
    return refs


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", default=DEFAULT_MODULE)
    ap.add_argument("--official-root", default=None, help="游戏根（缺省 = 模块的上一级）")
    ap.add_argument("--csv-dir", default=None)
    args = ap.parse_args()

    module = Path(args.module)
    if not (module / "ModuleData").is_dir():
        sys.exit("FAIL: 找不到模块 %s" % module)
    official_root = Path(args.official_root) if args.official_root else module.parent.parent
    csv_dir = Path(args.csv_dir) if args.csv_dir else (
        REPO / "Knowledge" / "太阁5" / "骑砍2织丰角色ID对应" / "csv")

    local, eng, hard = definitions(module, official_root)
    refs = collect_refs(csv_dir, ("taikou_troop_%s_do_a", "taikou_troop_%s_helmet_a"))

    undef, from_eng, from_hard, ok_local = [], [], [], 0
    for (fname, line, col, iid) in refs:
        if iid in local:
            ok_local += 1
        elif iid in eng:
            from_eng.append((fname, line, col, iid))
        elif iid in hard:
            from_hard.append((fname, line, col, iid))
        else:
            undef.append((fname, line, col, iid))

    print("定义来源：本包 %d · 引擎模块 XML %d · 引擎硬编码清单 %d" % (len(local), len(eng), len(hard)))
    print("三张表引用次数：%d（本包自带 %d · 引擎模块 %d · 引擎硬编码 %d · **未定义 %d**）"
          % (len(refs), ok_local, len(from_eng), len(from_hard), len(undef)))

    if from_eng or from_hard:
        seen = sorted({x[3] for x in from_eng + from_hard})
        print("· 靠外部提供（合规，共 %d 种）：%s" % (len(seen), " ".join(seen)))
    if undef:
        print("== 未定义的引用（%d）==" % len(undef))
        for fname, line, col, iid in undef:
            print("  [✗] %s:%d [%s] 引用的物品没有任何定义：%s" % (fname, line, col, iid))
        print("Summary: undefined=%d" % len(undef))
        return 1
    print("Summary: 三张表引用的装备全部有定义 ✅")
    return 0


if __name__ == "__main__":
    sys.exit(main())
