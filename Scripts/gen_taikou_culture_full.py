#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_taikou_culture_full.py — 织丰/原版字段交集全量一次性补全（2026-09-08 用户裁定：不要挤牙膏）
=====================================================================
依据（实测）：
  原版 Culture 全集 77 字段；织丰主文化 = 76/77（仅差 militia_bonus）；
  →「最小字段交集」= 织丰全集 ≈ 原版全集。本生成器把 ikoku + neutral 按该全集补齐，
    同时生成 40 个场景 NPC 模板（每职业最小模板，全字段引用 Taikou 自有资源）。

产物：
  1) spcultures.xml：完整 Culture.ikoku（全集 76 字段）——值全部指向自有资源（guard/peasant 复用做派）
  2) spnpccharacters.xml：新增 40 个 NPCCharacter 最小模板（occupation/culture/face/skill_template/空装备）
纪律：生成物禁止手改（铁律 22）——改表 = 改本脚本重跑；跑完必过 check_taikou_xml_references.py。

用法：python Scripts/gen_taikou_culture_full.py [--module PATH]
"""
import argparse
import re
import sys
from pathlib import Path

FACE = "BodyProperty.villager_empire"
SKILL = "SkillSet.infantry_heavyinfantry_level1_template_skills"

# (id, occupation, 显示名, is_female, level) —— 40 个场景 NPC（未成年变体与成人同款，织丰同思路）
NPC_TEMPLATES = [
    ("townsman", "Townsfolk", "Townsman", False, 1),
    ("townswoman", "Townsfolk", "Townswoman", True, 1),
    ("townsman_infant", "Townsfolk", "Townsman Infant", False, 1),
    ("townsman_child", "Townsfolk", "Townsman Child", False, 1),
    ("townsman_teenager", "Townsfolk", "Townsman Teenager", False, 1),
    ("townswoman_infant", "Townsfolk", "Townswoman Infant", True, 1),
    ("townswoman_child", "Townsfolk", "Townswoman Child", True, 1),
    ("townswoman_teenager", "Townsfolk", "Townswoman Teenager", True, 1),
    ("villager", "Villager", "Villager", False, 1),
    ("village_woman", "Villager", "Village Woman", True, 1),
    ("villager_male_child", "Villager", "Villager Boy", False, 1),
    ("villager_male_teenager", "Villager", "Villager Young Man", False, 1),
    ("villager_female_child", "Villager", "Villager Girl", True, 1),
    ("villager_female_teenager", "Villager", "Villager Young Woman", True, 1),
    ("merchant", "Merchant", "Merchant", False, 1),
    ("shop_worker", "ShopWorker", "Shop Worker", False, 1),
    ("blacksmith", "Blacksmith", "Blacksmith", False, 1),
    ("weaponsmith", "Weaponsmith", "Weaponsmith", False, 1),
    ("armorer", "Armorer", "Armorer", False, 1),
    ("barber", "Townsfolk", "Barber", False, 1),
    ("beggar", "Townsfolk", "Townsfolk", False, 1),
    ("female_beggar", "Townsfolk", "Beggar Woman", True, 1),
    ("female_dancer", "Townsfolk", "Townsfolk", True, 1),
    ("musician", "Musician", "Musician", False, 1),
    ("tavernkeeper", "Tavernkeeper", "Tavernkeeper", False, 1),
    ("tavern_wench", "TavernWench", "Tavern Wench", True, 1),
    ("taverngamehost", "TavernGameHost", "Game Host", False, 1),
    ("ransom_broker", "RansomBroker", "Ransom Broker", False, 1),
    ("merchant_notary", "Merchant", "Merchant Notary", False, 1),
    ("artisan_notary", "Artisan", "Artisan Notary", False, 1),
    ("preacher_notary", "Preacher", "Preacher Notary", False, 1),
    ("rural_notable_notary", "RuralNotable", "Rural Notable", False, 1),
    ("tournament_master", "ArenaMaster", "Tournament Master", False, 1),
    ("horseMerchant", "HorseTrader", "Horse Merchant", False, 1),
    ("caravan_master", "CaravanGuard", "Caravan Master", False, 1),
    ("caravan_guard", "CaravanGuard", "Caravan Guard", False, 1),
    ("veteran_caravan_guard", "CaravanGuard", "Caravan Guard (Veteran)", False, 1),
    ("armed_trader", "GoodsTrader", "Armed Trader", False, 1),
    ("gangleader_bodyguard", "Gangster", "Bodyguard", False, 1),
    ("gear_dummy", "Townsfolk", "Gear Dummy", False, 1),
    ("gear_practice_dummy", "Townsfolk", "Practice Dummy", False, 1),
]


def npc_cell(nt):
    nid, occ, name, female, lvl = nt
    return f'''	<NPCCharacter id="{nid}" default_group="Infantry" level="{lvl}" is_female="{'true' if female else 'false'}" culture="Culture.ikoku" name="{{=TAIKOU_npc_{nid}}}{name}" occupation="{occ}">
		<face>
			<face_key_template value="{FACE}"/>
		</face>
		<upgrade_targets></upgrade_targets>
		<Equipments>
			<EquipmentRoster civilian="true"/>
			<EquipmentRoster/>
		</Equipments>
	</NPCCharacter>'''


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    args = ap.parse_args()
    md = Path(args.module) / "ModuleData"
    if not md.is_dir():
        sys.exit(2)

    # 1) spcultures：完整 Culture.ikoku 属性块（76 全集——空节默认/复用做派）
    ikoku = f'''	<Culture id="ikoku"
			 name="{{=TAIKOU_culture_ikoku}}Japanese"
			 color="0xff9e1b32"
			 color2="0xffFCDE90"
			 basic_troop="NPCCharacter.peasant_farmer"
			 elite_basic_troop="NPCCharacter.guard"
			 melee_militia_troop="NPCCharacter.guard"
			 ranged_militia_troop="NPCCharacter.guard"
			 melee_elite_militia_troop="NPCCharacter.guard"
			 ranged_elite_militia_troop="NPCCharacter.guard"
			 is_main_culture="true"
			 can_have_settlement="true"
			 town_edge_number="16"
			 prosperity_bonus="1"
			 faction_banner_key="11.0.0.4345.4345.764.764.1.0.0.463.1.1.466.466.764.764.1.0.0"
			 default_face_key="000fa92e90004202aced5d976886573d5d679585a376fdd605877a7764b8987c00000000000007520000037f0000000f00000037049140010000000000000000"
			 encounter_background_mesh="encounter_empire"
			 default_party_template="PartyTemplate.main_hero_party_template"
			 militia_party_template="PartyTemplate.militia_template"
			 villager_party_template="PartyTemplate.militia_template"
			 caravan_party_template="PartyTemplate.militia_template"
			 elite_caravan_party_template="PartyTemplate.militia_template"
			 rebels_party_template="PartyTemplate.militia_template"
			 vassal_reward_party_template="PartyTemplate.militia_template"
			 default_battle_equipment_roster="EquipmentRoster.neutral_battle_template"
			 default_civilian_equipment_roster="EquipmentRoster.neutral_civilian_template"
			 duel_preset_equipment_roster="EquipmentRoster.neutral_battle_template"
			 board_game_type="SixAndEight"
			 armed_trader="NPCCharacter.armed_trader"
			 armorer="NPCCharacter.armorer"
			 artisan_notary="NPCCharacter.artisan_notary"
			 barber="NPCCharacter.barber"
			 beggar="NPCCharacter.beggar"
			 blacksmith="NPCCharacter.blacksmith"
			 caravan_guard="NPCCharacter.caravan_guard"
			 caravan_master="NPCCharacter.caravan_master"
			 female_beggar="NPCCharacter.female_beggar"
			 female_dancer="NPCCharacter.female_dancer"
			 gangleader_bodyguard="NPCCharacter.gangleader_bodyguard"
			 gear_dummy="NPCCharacter.gear_dummy"
			 gear_practice_dummy="NPCCharacter.gear_practice_dummy"
			 guard="NPCCharacter.guard"
			 horseMerchant="NPCCharacter.horseMerchant"
			 merchant="NPCCharacter.merchant"
			 merchant_notary="NPCCharacter.merchant_notary"
			 musician="NPCCharacter.musician"
			 preacher_notary="NPCCharacter.preacher_notary"
			 prison_guard="NPCCharacter.guard"
			 ransom_broker="NPCCharacter.ransom_broker"
			 rural_notable_notary="NPCCharacter.rural_notable_notary"
			 shop_worker="NPCCharacter.shop_worker"
			 tavern_wench="NPCCharacter.tavern_wench"
			 taverngamehost="NPCCharacter.taverngamehost"
			 tavernkeeper="NPCCharacter.tavernkeeper"
			 tournament_master="NPCCharacter.tournament_master"
			 townsman="NPCCharacter.townsman"
			 townsman_child="NPCCharacter.townsman_child"
			 townsman_infant="NPCCharacter.townsman_infant"
			 townsman_teenager="NPCCharacter.townsman_teenager"
			 townswoman="NPCCharacter.townswoman"
			 townswoman_child="NPCCharacter.townswoman_child"
			 townswoman_infant="NPCCharacter.townswoman_infant"
			 townswoman_teenager="NPCCharacter.townswoman_teenager"
			 village_woman="NPCCharacter.village_woman"
			 villager="NPCCharacter.villager"
			 villager_male_child="NPCCharacter.villager_male_child"
			 villager_male_teenager="NPCCharacter.villager_male_teenager"
			 villager_female_child="NPCCharacter.villager_female_child"
			 villager_female_teenager="NPCCharacter.villager_female_teenager"
			 veteran_caravan_guard="NPCCharacter.veteran_caravan_guard"
			 weapon_practice_stage_1="NPCCharacter.peasant_farmer"
			 weapon_practice_stage_2="NPCCharacter.guard"
			 weapon_practice_stage_3="NPCCharacter.guard"
			 weaponsmith="NPCCharacter.weaponsmith">
		<notable_and_wanderer_templates>
			<template name="NPCCharacter.lord_oda_nobunaga"/>
			<template name="NPCCharacter.lord_oda_shibata_katsuie"/>
			<template name="NPCCharacter.main_hero"/>
			<template name="NPCCharacter.merchant"/>
			<template name="NPCCharacter.artisan"/>
		</notable_and_wanderer_templates>
		<lord_templates></lord_templates>
		<rebellion_hero_templates></rebellion_hero_templates>
		<basic_mercenary_troops></basic_mercenary_troops>
		<cultural_feats></cultural_feats>
		<default_policies></default_policies>
		<clan_names></clan_names>
		<male_names></male_names>
		<female_names></female_names>
		<possible_clan_banner_icon_ids></possible_clan_banner_icon_ids>
		<tournament_team_templates_one_participant></tournament_team_templates_one_participant>
		<tournament_team_templates_two_participant></tournament_team_templates_two_participant>
		<tournament_team_templates_four_participant></tournament_team_templates_four_participant>
		<banner_bearer_replacement_weapons>
			<item id="Item.ridged_sabre_sword_t4"/>
		</banner_bearer_replacement_weapons>
		<vassal_reward_items></vassal_reward_items>
	</Culture>'''

    sc = md / "spcultures.xml"
    txt = sc.read_text(encoding="utf-8-sig", errors="replace")
    # 用完整版替换第一个 Culture.ikoku（保留 neutral_culture）
    txt = re.sub(r'\t<Culture id="ikoku".*?</Culture>', ikoku, txt, count=1, flags=re.S)
    # 🔴 原文件已含 XML 声明（utf-8-sig），只插生成物注释，不得重复写声明（2026-09-08 踩坑：重复声明 parse 失败）
    body = txt.split("?>", 1)[1]
    sc.write_text('<?xml version="1.0" encoding="utf-8"?>\n<!-- 生成物（2026-09-08 gen_taikou_culture_full.py）：织丰/原版字段交集 76 字段全量；值全引用自有资源；禁止手改（铁律 22） -->\n' + body, encoding="utf-8-sig")
    print("spcultures.xml: Culture.ikoku 已补全集")

    # 2) spnpccharacters：40 模板追加（🔴 幂等：已含 townsman = 已生成过，跳过——重跑不重复追加）
    npc = md / "spnpccharacters.xml"
    t = npc.read_text(encoding="utf-8-sig", errors="replace")
    if 'id="townsman"' in t:
        print("spnpccharacters.xml: 模板已存在，跳过追加（幂等）")
    else:
        cells = "\n\n".join(npc_cell(nt) for nt in NPC_TEMPLATES)
        t = t.replace("</NPCCharacters>", cells + "\n\n</NPCCharacters>", 1)
        npc.write_text(t, encoding="utf-8-sig")
        print(f"spnpccharacters.xml: +{len(NPC_TEMPLATES)} 个场景 NPC 模板")

    # 3) self-check：parse
    import xml.dom.minidom as m
    m.parse(str(sc)); m.parse(str(npc))
    print("parse OK")


if __name__ == "__main__":
    main()
