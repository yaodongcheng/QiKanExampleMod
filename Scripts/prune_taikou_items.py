#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
prune_taikou_items.py — 物品库/装备模板「引用闭包」裁剪（只留有人用 + 引擎必留）
=====================================================================
背景（2026-09-08 用户裁定：没实际挂载的物品/装备模板可以删）：
  Taikou 世界最小化后——真正挂载链 = Hero/NPCCharacter 内联装备（3 物品）
  + neutral_culture 的 2 个 EquipmentRoster（被 culture 属性引用）+ 引擎硬编码必留 51 个。
  其余 1180 物品 + 188 个 EquipmentRoster（官方 npc_wanderer 全家筒）无人引用 → 纯占内存。

裁剪口径（使用链闭包）：
  keep_item = ① 引擎必留集（反编译固化 51）② 内联装备（spnpccharacters 的 equipment id）
             ③ 保留 roster（= 被 culture default_*_equipment_roster 引用）的 equipment id
             ④ banner_bearer_replacement_weapons（culture 引用）
  keep_roster = 被 culture 属性引用的 roster（值 = roster.<id>）
产出：taikou_items/*.xml + taikou_equipment_sets.xml 重写（ElementTree round-trip），统计报表。
跑完配 check_taikou_xml_references.py 验证 0 悬空（checker 已含引擎物品白名单）。

用法：python Scripts/prune_taikou_items.py [--dry-run] [--module PATH]
"""
import argparse
import re
import sys
from pathlib import Path
import xml.etree.ElementTree as ET

ENGINE_ITEMS = {
    "battania_mace_1_t2", "blunt_arrows", "butter", "charger", "cheese", "chicken",
    "cleaver_sword_t3", "cow", "desert_lamellar", "falchion_sword_t2", "fish", "goose",
    "grain", "grappling_hook", "hardwood", "hog", "horse_harness_e", "horse_whip", "iron",
    "leafblade_throwing_knife", "leather", "leather_cavalier_boots", "leather_round_shield",
    "linen", "meat", "mule", "nasal_helmet_with_mail", "nordic_shortbow", "pottery",
    "pugio", "reinforced_mail_mitten", "sheep", "short_padded_robe", "short_sword_t3",
    "sumpter_horse", "torch", "vlandia_horse", "vlandia_lance_2_t4", "wine",
    "wooden_sword_t1", "wool",
    "hides", "tools", "charcoal", "ironIngot1", "ironIngot2", "ironIngot3",
    "ironIngot4", "ironIngot5", "ironIngot6", "trash",
}

# 引擎硬编码 MBEquipmentRoster 引用集（Campaign.InitializeDefaultEquipments 等，2026-09-08 反编译实锤全集：
#   default_*_neutral = 死装备；player_char_creation_default = CC 建号预览；npc_disguised_hero = 乔装；
#   conspirator_cutscene = 剧情过场——缺一即 NRE）
ENGINE_ROSTER_IDS = {
    "default_battle_equipment_roster_neutral",
    "default_civilian_equipment_roster_neutral",
    "player_char_creation_default",
    "npc_disguised_hero_equipment_template",
    "conspirator_cutscene_template",
}

# Item 注册的 XML 根元素 = <Item id="x">（id 通常不带 Item. 前缀，剥前缀归一）
ITEM_PREFIX = "Item."


def collect_usage(module_data):
    """使用链：返回 (keep_items, keep_rosters)。
    规则①内联装备：spnpccharacters.xml 的 <equipment id="Item.x">
    规则②roster 链：culture XML 的 default_*_equipment_roster="EquipmentRoster.x" → roster cell 的 equipment id
    规则③banner_bearer_replacement_weapons：culture XML <item id="Item.x">
    规则④引擎必留集并入 keep_items（外部传入）
    """
    keep_items = set()
    keep_rosters = set()

    npc_char = module_data / "spnpccharacters.xml"
    if npc_char.exists():
        root = ET.parse(str(npc_char)).getroot()
        for eq in root.iter("equipment"):
            iid = eq.get("id") or ""
            if iid.startswith(ITEM_PREFIX):
                keep_items.add(iid[len(ITEM_PREFIX):])

    # culture 引用的 roster（对全部 spcultures.xml cell 的 default_*_equipment_roster 属性）
    cultures = module_data / "spcultures.xml"
    if cultures.exists():
        root = ET.parse(str(cultures)).getroot()
        for culture in root.iter("Culture"):
            for attr in ("default_battle_equipment_roster", "default_civilian_equipment_roster",
                         "default_battle_equipment_roster", "default_civilian_equipment_roster"):
                val = culture.get(attr) or ""
                if val.startswith("EquipmentRoster."):
                    keep_rosters.add(val[len("EquipmentRoster."):])

        # banner_bearer_replacement_weapons <item id="Item.x">
        for item in root.iter("item"):
            iid = item.get("id") or ""
            if iid.startswith(ITEM_PREFIX):
                keep_items.add(iid[len(ITEM_PREFIX):])

    # roster cell 里的 equipment id（保留 roster 的内容——从 equipment_sets 文件读）
    eq_sets = module_data / "taikou_equipment_sets.xml"
    if eq_sets.exists():
        root = ET.parse(str(eq_sets)).getroot()
        for roster in root.iter("EquipmentRoster"):
            rid = roster.get("id")
            if rid in keep_rosters:
                for eq in roster.iter("Equipment"):
                    iid = eq.get("id") or ""
                    if iid.startswith(ITEM_PREFIX):
                        keep_items.add(iid[len(ITEM_PREFIX):])

    return keep_items, keep_rosters


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    module_data = Path(args.module) / "ModuleData"

    keep_items, keep_rosters = collect_usage(module_data)
    keep_items = keep_items | ENGINE_ITEMS
    keep_rosters = keep_rosters | ENGINE_ROSTER_IDS   # 引擎硬编码 roster 必须存在（织丰同款做法）

    def prune_into(path, keep_set, tag, dry):
        root = ET.parse(str(path)).getroot()
        before = len(list(root))
        for child in list(root):
            cid = child.get("id")
            if cid is not None and cid not in keep_set:
                root.remove(child)
        after = len(list(root))
        if not dry:
            if hasattr(ET, "indent"):
                ET.indent(root, space="\t")
            path.write_text('<?xml version="1.0" encoding="utf-8"?>\n' + ET.tostring(root, encoding="unicode"), encoding="utf-8-sig")
        return before, after

    ti, td = 0, 0
    for path in sorted((module_data / "taikou_items").glob("*.xml")):
        b, a = prune_into(path, keep_items, "Item", args.dry_run)
        ti += a; td += b - a
        print(f"[{'DRY ' if args.dry_run else 'KEEP'}] items/{path.name}: {b} -> {a}")

    rb, ra = prune_into(module_data / "taikou_equipment_sets.xml", keep_rosters, "Roster", args.dry_run)
    print(f"[{'DRY ' if args.dry_run else 'KEEP'}] equipment_sets: {rb} -> {ra}")

    print(f"\nsummary: items kept={ti} deleted={td} | rosters kept={ra} deleted={rb - ra} "
          f"(engine_items={len(ENGINE_ITEMS)} usage_items={len(keep_items - ENGINE_ITEMS)})")


if __name__ == "__main__":
    main()
