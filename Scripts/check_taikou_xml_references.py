#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Taikou data pack XML cross-reference checker (self-contained data audit)
========================================================================
Rules enforced:
  1. Every `Type.id` reference in any XML attribute must either resolve to a
     definition INSIDE Taikou/ModuleData, or belong to the engine-basic
     resource list below (Native module segments without IncludedGameTypes
     filter, loaded for every game type).
  2. Anything else = dangling ref. Under a custom GameType the official
     segments are filtered out, and a dangling ref causes
     GetPresumedObject() to create empty stub objects (the Companion NRE
     lineage).

Usage:
  python Scripts/check_taikou_xml_references.py [--module PATH]
Exit: 0 no dangling / 1 dangling found / 2 fatal.
"""
import argparse
import sys
from pathlib import Path
import xml.etree.ElementTree as ET

# Root element name -> reference prefix type (GameText/ModuleStrings = strings, skip)
ROOT_TO_TYPE = {
    "Items": "Item",
    "SPCultures": "Culture",
    "NPCCharacters": "NPCCharacter",
    "Kingdoms": "Kingdom",
    "Factions": "Clan",
    "Settlements": "Settlement",
    "Heroes": "Hero",
    "BodyProperties": "BodyProperty",
    "SkillSets": "SkillSet",
    "EquipmentRosters": "EquipmentRoster",
    "partyTemplates": "PartyTemplate",
    "Concepts": "Concept",
    "CraftingPieces": "CraftingPiece",
    "LocationComplexTemplates": "LocationComplexTemplate",
    "MusicInstruments": "MusicInstrument",
    "MusicTracks": "MusicTrack",
    "WeaponDescriptions": "WeaponDescription",
    "ItemModifiers": "ItemModifier",
    "ItemModifierGroups": "ItemModifierGroup",
    "Monsters": "Monster",
    "CraftingTemplates": "CraftingTemplate",
    "GameText": None,
    "ModuleStrings": None,
}

# Engine-basic resources: Native SubModule.xml segments WITHOUT IncludedGameTypes
# => loaded for every game type (verified 2026-09-07: Monsters/ItemModifiers/
# ItemModifierGroups/WeaponDescriptions/CraftingTemplates/SkeletonScales/SiegeEngines)
# plus enum-ish engine types that never come from XML.
ENGINE_BASIC_PREFIXES = {
    "Monster", "ItemModifier", "ItemModifierGroup", "WeaponDescription",
    "CraftingTemplate", "SkeletonScale", "SiegeEngine",
    "Skill", "SkillEffect", "Perk", "Trait", "BannerEffect", "BuildingType",
    "Policy", "Title", "Town", "Village", "ItemCategory",
}

# Alias: Faction in XML = Clan reference (engine alias, 织丰/Native 同款)
TYPE_ALIASES = {"Faction": "Clan"}

# 引擎「盲读子节点」的文化列表元素（雷 52）：这些元素里出现注释/裸文本 = NRE 或 null 条目
# （clan_names 读 child.Attributes["name"].Value；模板表走 ReadObjectReferenceFromXml → 缺属性返回 null）
BLIND_READ_LISTS = {
    "clan_names", "male_names", "female_names",
    "notable_and_wanderer_templates", "lord_templates", "rebellion_hero_templates",
    "basic_mercenary_troops", "banner_bearer_replacement_weapons", "vassal_reward_items",
    "cultural_feats", "default_policies", "possible_clan_banner_icon_ids",
}

# Token shapes that are not object references (ignore silently)
IGNORE_PATTERN_END = (".dll", ".xml", ".xslt", ".wav", ".ogg", ".flac",
                      ".ttf", ".png", ".jpg", ".json", ".txt")

REF_RE = __import__("re").compile(r"\b([A-Z][A-Za-z]{1,24})\.([A-Za-z_][A-Za-z0-9_.]*)\b")


def collect_definitions(xml_files):
    """id= of each root's direct child (cell) => {type_prefix: {id: source_path}}."""
    definitions = {}
    for path in xml_files:
        try:
            root = ET.parse(str(path)).getroot()
        except Exception as e:
            print(f"[FILE-ERROR] {path}: parse failed {e}")
            continue
        pfx = ROOT_TO_TYPE.get(root.tag)
        if pfx is None:
            continue
        tdefs = definitions.setdefault(pfx, {})
        for child in root:
            cid = child.get("id")
            if cid is None:
                continue
            if cid.startswith(pfx + "."):  # normalize prefixed ids
                cid = cid[len(pfx) + 1:]
            tdefs.setdefault(cid, str(path))
    return definitions


def collect_references(xml_files):
    """All non-comment attribute values containing Type.id tokens."""
    refs = {}
    unknown = []
    sane = set(ROOT_TO_TYPE.values()) - {None} | ENGINE_BASIC_PREFIXES | set(TYPE_ALIASES)
    for path in xml_files:
        try:
            root = ET.parse(str(path)).getroot()
        except Exception as e:
            continue
        for node in root.iter():
            if node.tag.startswith("<!--"):
                continue  # comments are real nodes in ET; skip them
            for key, value in node.attrib.items():
                for m in REF_RE.finditer(value):
                    pfx, rid = m.groups()
                    if value.endswith(IGNORE_PATTERN_END) or pfx in (key,):
                        continue
                    if pfx in sane:
                        refs.setdefault((pfx, rid), []).append(f"{path.name}:{node.tag}")
                    else:
                        unknown.append(f"{path} ({key}): {pfx}.{rid} = {value[:60]}")
    return refs, unknown


def main():
    ap = argparse.ArgumentParser(description="Taikou XML cross-reference checker")
    ap.add_argument("--module", default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    args = ap.parse_args()
    module_data = Path(args.module) / "ModuleData"
    if not module_data.is_dir():
        print(f"[FATAL] ModuleData not found: {module_data}")
        sys.exit(2)

    xml_files = sorted(module_data.rglob("*.xml"))
    print(f"Scanning {len(xml_files)} XML files under {module_data}")

    definitions = collect_definitions(xml_files)
    refs, unknown = collect_references(xml_files)

    # resolve type aliases (Faction -> Clan) before matching
    for (pfx, rid) in list(refs.keys()):
        if pfx in TYPE_ALIASES:
            refs.setdefault((TYPE_ALIASES[pfx], rid), []).extend(refs[(pfx, rid)])
            del refs[(pfx, rid)]

    print("\n== Defined objects (Taikou self) ==")
    for t in sorted(definitions):
        print(f"  {t:<28} {len(definitions[t])}")

    print("\n== DANGLING refs (not in Taikou, not engine-basic) ==")
    dangling = 0
    for (pfx, rid) in sorted(refs):
        if pfx in definitions and rid in definitions[pfx]:
            continue
        if pfx in ENGINE_BASIC_PREFIXES:
            continue
        dangling += 1
        locs = refs[(pfx, rid)]
        print(f"  [DANGLING] {pfx}.{rid}  ({len(locs)} refs)  {locs[:6]}")

    print("\n== 文化列表元素「注释/文本污染」体检（雷 52） ==")
    # 引擎读这些列表是**盲读所有子节点**的（clan_names：child.Attributes["name"].Value；模板表：
    # ReadObjectReferenceFromXml → 缺属性返回 null）→ 夹一个注释就 NRE/null 条目 → 该文化的模板列表
    # 保持 null → OnNewGameCreated 时 InitializeCompanionTemplateList NRE（雷 11 复发，2026-09-10 14:50 实录）
    # ⚠️ 必须用**保留注释的解析器**（insert_comments=True）：ET 默认丢注释查不出来，
    #    而改用正则又会把注释里写的字面标签（如说明文字里的 <clan_names>）当成真标签（两次踩过）
    import re as _re
    poisoned = 0
    for path in xml_files:
        if "spcultures" not in path.name:
            continue
        try:
            parser = ET.XMLParser(target=ET.TreeBuilder(insert_comments=True))
            root = ET.fromstring(path.read_text(encoding="utf-8", errors="replace"), parser=parser)
        except Exception as e:
            print(f"  [FILE-ERROR] {path.name}: {e}")
            poisoned += 1
            continue
        for lst in root.iter():
            if not isinstance(lst.tag, str) or lst.tag not in BLIND_READ_LISTS:
                continue
            for child in list(lst):
                if not isinstance(child.tag, str):          # 注释 / PI 节点
                    poisoned += 1
                    print(f"  [LIST-POLLUTED] {path.name} <{lst.tag}> 里夹了注释/PI → 引擎盲读会 NRE")
            if (lst.text or "").strip() or any((c.tail or "").strip() for c in list(lst)):
                poisoned += 1
                print(f"  [LIST-POLLUTED] {path.name} <{lst.tag}> 里有裸文本 → 引擎盲读会 NRE")
    if poisoned == 0:
        print("  （无）")

    print("\n== Refs to engine-basic (OK by contract) ==")
    pfx_count = {}
    for (pfx, rid) in refs:
        if pfx not in definitions and pfx in ENGINE_BASIC_PREFIXES:
            pfx_count.setdefault(pfx, set()).add(rid)
    for pfx in sorted(pfx_count):
        print(f"  {pfx}.<{len(pfx_count[pfx])} unique ids>")

    print("\n== Unknown prefixes (manual review) ==")
    if unknown:
        for u in unknown[:40]:
            print(f"  [?] {u}")
        if len(unknown) > 40:
            print(f"  ... {len(unknown) - 40} more")
    else:
        print("  none")

    print(f"\nSummary: dangling={dangling} unknown_prefixes={len(unknown)} list_polluted={poisoned}")
    sys.exit(1 if (dangling or poisoned) else 0)


if __name__ == "__main__":
    main()
