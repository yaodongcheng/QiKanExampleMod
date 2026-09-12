#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_taikou_culture_full.py — 织丰/原版字段交集全量一次性补全（2026-09-08 用户裁定：不要挤牙膏）
=====================================================================
⚠️ 来源与授权（2026-09-10 查明并标注）：本生成器内嵌的文化**名字池 622 条**
   （clan/male/female 三段，含 8 位键）**逐字取自织丰 Shokuho 的
   `Modules/Shokuho/ModuleData/spcultures/shokuho_main_cultures.xml`**（键与值 622/622 一致，实证比对）；
   其中文译文取自织丰中文包 `Shokuho_CNs`。自用无碍，**对外发布须标注出处并取得授权**
   （或改为自造名池 —— 见 plans 待办 C 路线）。其余字段值均为自有资源/官方拷贝，无此问题。

依据（实测）：
  原版 Culture 全集 77 字段；织丰主文化 = 76/77（仅差 militia_bonus）；
  →「最小字段交集」= 织丰全集 ≈ 原版全集。本生成器按该全集产出**本包的全部文化**，
    同时生成 40 个场景 NPC 模板（每职业最小模板，全字段引用 Taikou 自有资源）。

🔴 文化清单（2026-09-12 用户裁定「Taikou 与织丰零依赖 → 所有文化都自创」）：
  **17 个** = ikoku（主）+ 9 地域（saikai/nankai/sanyo/kinai/hokuriku/tosan/tokai/kanto/ou）
           + 6 身份（ninja/pirate/trader/looters/ronin/namban 南蛮）
           + neutral_culture（本文件原有、不由此生成）。
  改清单 = 改 main() 里的 `CULTURES` 重跑（铁律 22：生成物禁手改）。

产物：
  1) spcultures.xml：上述文化的完整 76 字段块（值全部指向自有资源）
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
    ("gang_leader", "GangLeader", "Gang Leader", False, 3),
    ("mercenary_ikoku", "Mercenary", "Mercenary", False, 6),
    ("gear_dummy", "Townsfolk", "Gear Dummy", False, 1),
    ("gear_practice_dummy", "Townsfolk", "Practice Dummy", False, 1),
]


def npc_cell(nt, culture="ikoku", name_key=None):
    """渲染一个场景 NPC 模板。`culture` = 文化 id（默认 ikoku）；
    `name_key` = 覆盖名字本地化键（默认按 id 拼）——地区版兵种复用通用键时不新增文案。"""
    nid, occ, name, female, lvl = nt
    key = name_key or ("TAIKOU_npc_" + nid)
    return f'''	<NPCCharacter id="{nid}" default_group="Infantry" level="{lvl}" is_female="{'true' if female else 'false'}" culture="Culture.{culture}" name="{{={key}}}{name}" occupation="{occ}">
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

    # 1) spcultures：完整 Culture 属性块（76 全集——空节默认/复用做派）
    #    🔴 本串是**模板**：`__CID__/__CKEY__/__CEN__/__COLOR__/__COLOR2__/__ISMAIN__` 六处占位，
    #       main() 里按文化逐个替换（ikoku + 4 个身份文化；名字池与 76 字段全套共用）。
    tmpl = f'''	<Culture id="__CID__"
			 name="{{=__CKEY__}}__CEN__"
			 color="__COLOR__"
			 color2="__COLOR2__"
			 basic_troop="NPCCharacter.peasant_farmer"
			 elite_basic_troop="NPCCharacter.guard"
			 melee_militia_troop="NPCCharacter.guard"
			 ranged_militia_troop="NPCCharacter.guard"
			 melee_elite_militia_troop="NPCCharacter.guard"
			 ranged_elite_militia_troop="NPCCharacter.guard"
			 is_main_culture="__ISMAIN__"
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
		<!-- 🔴 2026-09-12 去掉了原先列在这里的 7 个领主模板（lord_tk5_195/379/517/587/740/279/549）：
		     领主模板现在**按年代切段**（taikou_lords_<年>.xml，GameType 互斥），
		     而本表所在的 spcultures 段是**六代共用**的——信长 1598 年那份里根本不存在，
		     写在这里 = 该代引用悬空（雷 11 同族的裸桩/NRE 链）。
		     另：领主本来也不该进「名人/游荡者」池（会让酒馆刷出历史大名）。
		     ⚠️ 注意（雷 52）：下面这个元素是**盲读列表**，注释只能写在这里、不能写进元素里面。 -->
		<notable_and_wanderer_templates>
			<template name="NPCCharacter.main_hero"/>
			<template name="NPCCharacter.merchant"/>
			<template name="NPCCharacter.artisan"/>
			<template name="NPCCharacter.gang_leader"/>
		</notable_and_wanderer_templates>
		<lord_templates></lord_templates>
		<rebellion_hero_templates></rebellion_hero_templates>
		<basic_mercenary_troops>
			<template name="NPCCharacter.mercenary_ikoku"/>
		</basic_mercenary_troops>
		<cultural_feats></cultural_feats>
		<default_policies></default_policies>
		<!-- 名字池来源标注（用户裁定 2026-09-10「先 A：保留 + 标出处」）：
		     本池（clan/male/female 三段共 622 条，含 8 位键）**逐字取自织丰 Shokuho 的
		     Modules/Shokuho/ModuleData/spcultures/shokuho_main_cultures.xml**（键与罗马字值 622/622 完全一致，实证比对）。
		     中文译文取自织丰中文包 Modules/Shokuho_CNs/.../module_localization_strings_CNs.xml（见 Taikou 的 CN 语言文件）。
		     → 自用无碍；**对外发布/分享时须标注出处并取得织丰作者授权**（或按 C 路线改为自造名池，见 plan 待办）。
		     注：名字与译文都是**内嵌**在本包里的（不依赖织丰是否安装），运行时自洽。
		     ⚠️ 本注释必须留在 <Culture> 直接子节点层（雷 52）：引擎读名字池是**盲读所有子节点**的
		        （`foreach (child in clan_names.ChildNodes) ... child.Attributes["name"].Value`），
		        注释一旦塞进 <clan_names>/<male_names>/<female_names> 里面 → Attributes["name"] 为 null
		        → 反序列化中途 NRE → 该文化的三个模板列表**保持 null** → OnNewGameCreated 时
		        CompanionsCampaignBehavior.InitializeCompanionTemplateList NRE（雷 11 复发，2026-09-10 14:50 实录）。 -->
		<clan_names>
<name name="{{=ZdBXMo7L}}Akizuki"/>
			<name name="{{=46fsk7gw}}Asou"/>
			<name name="{{=JWeFzVlO}}Nuki"/>
			<name name="{{=pwYAuMyG}}Katsuki"/>
			<name name="{{=YgTsZMc0}}Kuroki"/>
			<name name="{{=i3yQgaJa}}Kusano"/>
			<name name="{{=UeORdILD}}Chikushi"/>
			<name name="{{=KssObQt2}}Saigo"/>
			<name name="{{=VSE45J5r}}Shiga"/>
			<name name="{{=JkFrZzdM}}Kamata"/>
			<name name="{{=0QTdZKo2}}Moji"/>
			<name name="{{=p0krwhS7}}Himeno"/>
			<name name="{{=TztBu7HI}}Shibue"/>
			<name name="{{=ac95GhXU}}Shisa"/>
			<name name="{{=pdwy8MTC}}Mitai"/>
			<name name="{{=gWlW80aW}}Fukabori"/>
			<name name="{{=Ie9Vn818}}Akahoshi"/>
			<name name="{{=uboP42ci}}Ijichi"/>
			<name name="{{=AuwGwWpz}}Hishikari"/>
			<name name="{{=ADarelik}}Shikine"/>
			<name name="{{=ZdBXMo7L}}Akizuki"/>
</clan_names>
		<male_names>
<name name="{{=686xcJ5S}}Taro"/>
			<name name="{{=xbwRkO5Z}}Ichiro"/>
			<name name="{{=0T1OCfRY}}Jiro"/>
			<name name="{{=1187gbgM}}Saburo"/>
			<name name="{{=BN54hR9V}}Shiro"/>
			<name name="{{=OfanclyF}}Goro"/>
			<name name="{{=9wz88kH7}}Rokuro"/>
			<name name="{{=pEnQ7qL8}}Shichiro"/>
			<name name="{{=BQEJpaKM}}Hachiro"/>
			<name name="{{=Zf6pqP1S}}Kuro"/>
			<name name="{{=F4wYBL82}}Juro"/>
			<name name="{{=3cTGorVo}}Jusuke"/>
			<name name="{{=4yJsfsiM}}Suketaro"/>
			<name name="{{=XBtOokns}}Sukejiro"/>
			<name name="{{=5P5FkBwH}}Sukesaburo"/>
			<name name="{{=tubOfaxz}}Sukeshiro"/>
			<name name="{{=3R5j7RYA}}Sukegoro"/>
			<name name="{{=Ds3cZg6r}}Sukebei"/>
			<name name="{{=nKMOkNFw}}Sukezaemon"/>
			<name name="{{=7iiIq9r6}}Sukeemon"/>
			<name name="{{=kD70iJ3J}}Shintaro"/>
			<name name="{{=fpdWiWfz}}Shinichiro"/>
			<name name="{{=A2GoIvbc}}Shinjiro"/>
			<name name="{{=Bti8upGR}}Sinsaburo"/>
			<name name="{{=OMBLQ8zN}}Shingoro"/>
			<name name="{{=CeFXPZ4y}}Shinjuro"/>
			<name name="{{=8AWswCXB}}Shinsuke"/>
			<name name="{{=u6o96I4Y}}Shinnosuke"/>
			<name name="{{=KT8o8D8j}}Shinkichi"/>
			<name name="{{=QXoI9qum}}Shinzo"/>
			<name name="{{=l4hS80xv}}Shinbei"/>
			<name name="{{=xGgjfTcH}}Shinzaemon"/>
			<name name="{{=nP12n3Fs}}Shinemon"/>
			<name name="{{=2HEjtotK}}Kantaro"/>
			<name name="{{=ctzwdIKa}}Kanichiro"/>
			<name name="{{=nWOBDgpj}}Kanjiro"/>
			<name name="{{=NbvnNk2S}}Kanzaburo"/>
			<name name="{{=kN3o6xzR}}Kangoro"/>
			<name name="{{=rtGBo128}}Kanjuro"/>
			<name name="{{=YRDygFj6}}Kansuke"/>
			<name name="{{=cGq2zXGn}}Kannosuke"/>
			<name name="{{=2BhHfnxd}}Kankichi"/>
			<name name="{{=eZOVCTrX}}Kanzo"/>
			<name name="{{=4Nkceif9}}Kanbei"/>
			<name name="{{=nP12n3Fs}}Shinemon"/>
			<name name="{{=xGgjfTcH}}Shinzaemon"/>
			<name name="{{=pr5IdVlQ}}Choutaro"/>
			<name name="{{=6get6Y8E}}Chouichiro"/>
			<name name="{{=b9VnjeKp}}Choujiro"/>
			<name name="{{=ZjsS13ws}}Chouzaburo"/>
			<name name="{{=QP1vL1Z5}}Chougoro"/>
			<name name="{{=onoKVXjp}}Choujuro"/>
			<name name="{{=8Lqk2NsA}}Chousuke"/>
			<name name="{{=XWldYuhm}}Chouunosuke"/>
			<name name="{{=q1a85TXt}}Choukichi"/>
			<name name="{{=QCC1D0p5}}Chouzo"/>
			<name name="{{=uA1I8WpX}}Choubei"/>
			<name name="{{=S34Gm8bG}}Chouzaemon"/>
			<name name="{{=7wmjr9N6}}Chouemon"/>
			<name name="{{=FyfAPTam}}Yotaro"/>
			<name name="{{=5yIPZNOC}}Yoichiro"/>
			<name name="{{=lK7DyjrE}}Yojiro"/>
			<name name="{{=MJ1Oqpvu}}Yosaburo"/>
			<name name="{{=JNR0uO1t}}Yogoro"/>
			<name name="{{=73ORmCAk}}Yojuro"/>
			<name name="{{=ZMYEFBjO}}Yosuke"/>
			<name name="{{=EgvAhszY}}Yonosuke"/>
			<name name="{{=NC96SIAV}}Yokichi"/>
			<name name="{{=Xr2ZmWZw}}Yozo"/>
			<name name="{{=29MCqLs8}}Yohei"/>
			<name name="{{=RPKbRH8T}}Yozaemon"/>
			<name name="{{=FcRcfWZy}}Yoemon"/>
			<name name="{{=DhCSfZzt}}Gentaro"/>
			<name name="{{=DvyQIA3d}}Genichiro"/>
			<name name="{{=ItTycCE3}}Genjiro"/>
			<name name="{{=8p1CMJc0}}Genzaburo"/>
			<name name="{{=TZVh0WX4}}Gengoro"/>
			<name name="{{=WFx5O7js}}Genjuro"/>
			<name name="{{=cJ2lc6t0}}Gensuke"/>
			<name name="{{=geoBdNT4}}Gennosuke"/>
			<name name="{{=m7TQ90rZ}}Genzo"/>
			<name name="{{=TkJw9Duw}}Gennai"/>
			<name name="{{=Z1zN5otw}}Genbei"/>
			<name name="{{=AbuVBtV8}}Genzaemon"/>
			<name name="{{=AtsmKH9M}}Genemon"/>
			<name name="{{=HG9UGvJg}}Kotaro"/>
			<name name="{{=8kVtgCIk}}Koichiro"/>
			<name name="{{=6vARoohf}}Kojiro"/>
			<name name="{{=Ak4DXEag}}Kosaburo"/>
			<name name="{{=xYhQ9Gab}}Kogoro"/>
			<name name="{{=KFlqyj5C}}Kojuro"/>
			<name name="{{=88kt2fqY}}Shotaro"/>
			<name name="{{=lUXZJHsr}}Shoichiro"/>
			<name name="{{=yylgHdJi}}Shojiro"/>
			<name name="{{=WJocHKZm}}Shosaburo"/>
			<name name="{{=aoezMSUO}}Shogoro"/>
			<name name="{{=rKb72D2I}}Shojuro"/>
			<name name="{{=8ZO6U0MK}}Shosuke"/>
			<name name="{{=FKcnKXmS}}Shonosuke"/>
			<name name="{{=w3AqbTQs}}Shokichi"/>
			<name name="{{=m10TL2CQ}}Shozo"/>
			<name name="{{=enQ9CtYF}}Shobei"/>
			<name name="{{=yOuFJRP1}}Shozaemon"/>
			<name name="{{=nitvYEGB}}Shoemon"/>
			<name name="{{=V8VowuPM}}Toutaro"/>
			<name name="{{=TaQeyQCL}}Touichiro"/>
			<name name="{{=qXPONqPW}}Toujiro"/>
			<name name="{{=FZDkXvEM}}Tousaburo"/>
			<name name="{{=2z2LglVH}}Tougoro"/>
			<name name="{{=V9bQ7Maa}}Toujuro"/>
			<name name="{{=1QBVd4da}}Tousuke"/>
			<name name="{{=SmVurwZt}}Tounosuke"/>
			<name name="{{=Wq5qYwL7}}Toukichi"/>
			<name name="{{=n1btMUnd}}Touzo"/>
			<name name="{{=wsOx7Udj}}Tounai"/>
			<name name="{{=ffaLCN8L}}Toubei"/>
			<name name="{{=WdoaF6Up}}Touzaemon"/>
			<name name="{{=0TrEYRKe}}Touemon"/>
			<name name="{{=cousoQ4k}}Mataichiro"/>
			<name name="{{=7GGb9pks}}Matajiro"/>
			<name name="{{=Mi6jEdGA}}Matasaburo"/>
			<name name="{{=50m6bXju}}Matagoro"/>
			<name name="{{=HEpg6hFQ}}Matajuro"/>
			<name name="{{=EjYlRDvH}}Matasuke"/>
			<name name="{{=irRONE36}}Matanosuke"/>
			<name name="{{=XGQmrAxZ}}Matakichi"/>
			<name name="{{=Faecpmvv}}Matazo"/>
			<name name="{{=hDYNv8cs}}Matabei"/>
			<name name="{{=iXpL1lbL}}Matazaemon"/>
			<name name="{{=P4kQzauj}}Mataemon"/>
			<name name="{{=5i4ToX51}}Tokutaro"/>
			<name name="{{=pMvZ04mm}}Tokuichiro"/>
			<name name="{{=INK1MJlR}}Tokujiro"/>
			<name name="{{=jlDVjQnu}}Tokusaburo"/>
			<name name="{{=HQ7qfRgD}}Tokugoro"/>
			<name name="{{=clrovKXi}}Tokujuro"/>
			<name name="{{=FQlRLvY9}}Tokusuke"/>
			<name name="{{=CCUwTZYL}}Tokunosuke"/>
			<name name="{{=9qQ9evZv}}Tokukichi"/>
			<name name="{{=ijnOHHSh}}Tokuzo"/>
			<name name="{{=30Z0KoSI}}Tokubei"/>
			<name name="{{=lIvG7CnE}}Tokuzaemon"/>
			<name name="{{=RdMNMHrY}}Tokuemon"/>
			<name name="{{=U58ZOdQG}}Yataro"/>
			<name name="{{=ny1aqEL7}}Yaichiro"/>
			<name name="{{=k0kehmUu}}Yajiro"/>
			<name name="{{=XKMzktuD}}Yasaburo"/>
			<name name="{{=EfW7Ru9p}}Yagoro"/>
			<name name="{{=UUNZapL6}}Yajuro"/>
			<name name="{{=4TAPNSDP}}Yasuke"/>
			<name name="{{=STLLrKL8}}Yanosuke"/>
			<name name="{{=fPHEbh56}}Yakichi"/>
			<name name="{{=dhkBuvlk}}Yazo"/>
			<name name="{{=Nh4tadmB}}Yahei"/>
			<name name="{{=u1LJWWKJ}}Yazaemon"/>
			<name name="{{=12lLmL2X}}Yaemon"/>
			<name name="{{=9CL8y7uV}}Magotaro"/>
			<name name="{{=W1Uk2bMW}}Magoichiro"/>
			<name name="{{=CpstFlRL}}Magojiro"/>
			<name name="{{=myxmbqU0}}Magozaburo"/>
			<name name="{{=BewFYzJe}}Magogoro"/>
			<name name="{{=k2aKue4A}}Magojuro"/>
			<name name="{{=Mszofuwe}}Magosuke"/>
			<name name="{{=PG1lH9J9}}Magonosuke"/>
			<name name="{{=DnJOcAzi}}Magokichi"/>
			<name name="{{=PM82Pqwb}}Magozo"/>
			<name name="{{=zIfoLUIX}}Magobei"/>
			<name name="{{=TTt1dATj}}Magozaemon"/>
			<name name="{{=7nUQ4by7}}Magoemon"/>
			<name name="{{=kISlg1tA}}Zenichiro"/>
			<name name="{{=w6XY8Pw4}}Zenjiro"/>
			<name name="{{=Qm5bAnQX}}Zenzaburo"/>
			<name name="{{=Z3BeJBAS}}Zengoro"/>
			<name name="{{=aFnWRxFk}}Zenjuro"/>
			<name name="{{=6xE2RZ7l}}Zensuke"/>
			<name name="{{=XJ9ajAGZ}}Zennosuke"/>
			<name name="{{=UhAHri58}}Zenkichi"/>
			<name name="{{=8dQZUa4y}}Zenzo"/>
			<name name="{{=WfUZsGO1}}Zennai"/>
			<name name="{{=E2TB5J7F}}Zenbei"/>
			<name name="{{=cs1H4EqR}}Zenzaemon"/>
			<name name="{{=6sEaLkkL}}Zenemon"/>
			<name name="{{=i62ROvkh}}Kiichiro"/>
			<name name="{{=usXhU7cq}}Kizaburo"/>
			<name name="{{=siR5eu2H}}Kijuro"/>
			<name name="{{=rptigya9}}Kisuke"/>
			<name name="{{=JRFFb5Yp}}Kinosuke"/>
			<name name="{{=aexJxPzD}}Kizo"/>
			<name name="{{=5iJKF6OL}}Souichiro"/>
			<name name="{{=bOaRvMKu}}Soujiro"/>
			<name name="{{=KOYIX6bo}}Souzaburo"/>
			<name name="{{=hQ8J9ehd}}Sougoro"/>
			<name name="{{=WpNXQ2Wg}}Soujuro"/>
			<name name="{{=MSQw9m26}}Sousuke"/>
			<name name="{{=MSA98yTk}}Sounosuke"/>
			<name name="{{=BcZmXIib}}Soukichi"/>
			<name name="{{=FDNTSgX5}}Souzo"/>
			<name name="{{=QQyFUrjM}}Soubei"/>
			<name name="{{=95u0n5Gw}}Souzaemon"/>
			<name name="{{=UuJecsoc}}Souemon"/>
			<name name="{{=rC6bGIYr}}Jinichiro"/>
			<name name="{{=tUuATMXL}}Jinjiro"/>
			<name name="{{=5CILZ1Mw}}Jinzaburo"/>
			<name name="{{=xntsULXS}}Jingoro"/>
			<name name="{{=3kZ6KnLw}}Jinjuro"/>
			<name name="{{=1YstbZrT}}Jinsuke"/>
			<name name="{{=ZUSD95RW}}Jinnosuke"/>
			<name name="{{=CifluU3w}}Jinkichi"/>
			<name name="{{=RtC7Igb8}}Jinzo"/>
			<name name="{{=WYjJfaRF}}Jinbei"/>
			<name name="{{=li6FcW6d}}Jinzaemon"/>
			<name name="{{=OFRiI1ZH}}Jinemon"/>
			<name name="{{=PT48Pdyv}}Hikoichiro"/>
			<name name="{{=qOHD4hmz}}Hikojiro"/>
			<name name="{{=claatCYX}}Hikozaburo"/>
			<name name="{{=LVSnchon}}Hikogoro"/>
			<name name="{{=Md3I4b9A}}Hikojuro"/>
			<name name="{{=NOhSVuEd}}Hikosuke"/>
			<name name="{{=IyWeSdrc}}Hikonosuke"/>
			<name name="{{=Wz5ivE3j}}Hikozo"/>
			<name name="{{=4LySx3FC}}Hikobei"/>
			<name name="{{=0PkorrCr}}Hikozaemon"/>
			<name name="{{=leI5JyG8}}Hikoemon"/>
			<name name="{{=nrW2LLb4}}Seiichiro"/>
			<name name="{{=YqIBnGJg}}Seijiro"/>
			<name name="{{=eZf87dj4}}Seizaburo"/>
			<name name="{{=CiuuF7Zt}}Seigoro"/>
			<name name="{{=Xtf9w0UQ}}Seijuro"/>
			<name name="{{=ryPxfNOi}}Seisuke"/>
			<name name="{{=OMsFEksS}}Seinosuke"/>
			<name name="{{=Bt3wxgJc}}Seizo"/>
			<name name="{{=qctPhh4n}}Seibei"/>
			<name name="{{=iwo72hty}}Seizaemon"/>
			<name name="{{=23onjdyq}}Seiemon"/>
			<name name="{{=RP6oIBv1}}Sentaro"/>
			<name name="{{=1XiYQZec}}Senjiro"/>
			<name name="{{=PHWNIb1U}}Senzaburo"/>
			<name name="{{=aIRIvtzt}}Sengoro"/>
			<name name="{{=jXS4Xyuz}}Senjuro"/>
			<name name="{{=wOXwXos9}}Sensuke"/>
			<name name="{{=o6ipx7CX}}Sennosuke"/>
			<name name="{{=GbzQmodT}}Senzo"/>
			<name name="{{=6HSVwyHm}}Senbei"/>
			<name name="{{=k0QkYUKf}}Senzaemon"/>
			<name name="{{=HDO43hmP}}Senemon"/>
			<name name="{{=BZenLkHE}}Mantaro"/>
			<name name="{{=EaxgVhEA}}Manjiro"/>
			<name name="{{=oHVTd16T}}Manzaburo"/>
			<name name="{{=W1f3Xyka}}Mangoro"/>
			<name name="{{=KQJB6tIF}}Manjuro"/>
			<name name="{{=Dgnokr0s}}Mansuke"/>
			<name name="{{=ZuY3PKvk}}Mannosuke"/>
			<name name="{{=2zvbXuvD}}Manzo"/>
			<name name="{{=DemRvjqX}}Manbei"/>
			<name name="{{=xqLuC100}}Manzaemon"/>
			<name name="{{=bKpT7ydh}}Manemon"/>
			<name name="{{=qTjJ6VNB}}Kouichiro"/>
			<name name="{{=9YfwfCjt}}Koujiro"/>
			<name name="{{=OiQWNnCC}}Kouzaburo"/>
			<name name="{{=alsRXQfE}}Kougoro"/>
			<name name="{{=tiQigL4M}}Koujuro"/>
			<name name="{{=5vy1Kcut}}Kousuke"/>
			<name name="{{=ewIhSqSg}}Kounosuke"/>
			<name name="{{=uUuyf0oq}}Kouzo"/>
			<name name="{{=6JYI2cM1}}Koubei"/>
			<name name="{{=Ucc1XJtA}}Kouzaemon"/>
			<name name="{{=c0POyeDy}}Kouemon"/>
			<name name="{{=8r6KNpJu}}Ichitaro"/>
			<name name="{{=adSaxUtg}}Ichijiro"/>
			<name name="{{=PHZYnyJG}}Ichisaburo"/>
			<name name="{{=2Zz5tOGM}}Ichigoro"/>
			<name name="{{=UE8Q9iLC}}Ichijuro"/>
			<name name="{{=A38aKm8V}}Ichisuke"/>
			<name name="{{=6maHAom2}}Ichinosuke"/>
			<name name="{{=jGDO6UVX}}Ichizo"/>
			<name name="{{=fRC2Sv0I}}Ichibei"/>
			<name name="{{=vvLImum3}}Ichizaemon"/>
			<name name="{{=qzEnN6b2}}Ichiemon"/>
			<name name="{{=Bv1SBqYA}}Dentaro"/>
			<name name="{{=0iGkPeDd}}Denichiro"/>
			<name name="{{=1jKdOkXK}}Denjiro"/>
			<name name="{{=oS7aAM2u}}Densaburo"/>
			<name name="{{=0676hgZd}}Dengoro"/>
			<name name="{{=4rt2PIJj}}Denjuro"/>
			<name name="{{=vrNgITIa}}Densuke"/>
			<name name="{{=bSnAdlUv}}Dennosuke"/>
			<name name="{{=n8ygcUdT}}Denzo"/>
			<name name="{{=uL8OvdBZ}}Dennai"/>
			<name name="{{=ENaKTTDb}}Denbei"/>
			<name name="{{=EVBmSHD1}}Denzaemon"/>
			<name name="{{=F3NFayMh}}Denemon"/>
			<name name="{{=5YYTcaY5}}Buntaro"/>
			<name name="{{=5PkRFEfl}}Bunichiro"/>
			<name name="{{=3kNHKBLR}}Bunjiro"/>
			<name name="{{=bxtXeEFE}}Bunsaburo"/>
			<name name="{{=0iBcR9Oj}}Bungoro"/>
			<name name="{{=Fg3jIKFw}}Bunjuro"/>
			<name name="{{=rvIN9ZTT}}Bunsuke"/>
			<name name="{{=7I5vZfW3}}Bunnosuke"/>
			<name name="{{=6wEnMxwM}}Bunzo"/>
			<name name="{{=csOTlDG2}}Bunnai"/>
			<name name="{{=Ild4lSX7}}Bunbei"/>
			<name name="{{=IhUPTgiY}}Bunzaemon"/>
			<name name="{{=xwzqFQHe}}Bunemon"/>
			<name name="{{=xNuFAChh}}Kyutaro"/>
			<name name="{{=PGONDRwL}}Kyuichiro"/>
			<name name="{{=YphWCH0O}}Kyujiro"/>
			<name name="{{=RK9XaxfL}}Kyusaburo"/>
			<name name="{{=jCPhfeQe}}Kyugoro"/>
			<name name="{{=whdi1PqW}}Kyujuro"/>
			<name name="{{=qboKlFcb}}Kyusuke"/>
			<name name="{{=IqtNcUKR}}Kyunosuke"/>
			<name name="{{=EnAKh2jl}}Kyuzo"/>
			<name name="{{=kmP42SIm}}Kyunai"/>
			<name name="{{=6lv28NaV}}Kyubei"/>
			<name name="{{=WtrhY68f}}Kyuzaemon"/>
			<name name="{{=UoyQxG88}}Kyuemon"/>
			<name name="{{=RgM49V8I}}Risuke"/>
			<name name="{{=rOuEyl4j}}Rihei"/>
			<name name="{{=WtGggyWu}}Tomotaro"/>
			<name name="{{=FALveYtm}}Tomoichiro"/>
			<name name="{{=cPCDrqQ9}}Tomojiro"/>
			<name name="{{=FrCZl1Kc}}Tomosaburo"/>
			<name name="{{=aTLCRfbo}}Tomogoro"/>
			<name name="{{=VJ8JjKdn}}Tomojuro"/>
			<name name="{{=eybQD7fW}}Tomosuke"/>
			<name name="{{=ouJSaXMs}}Tomonosuke"/>
			<name name="{{=5cUv40An}}Tomozo"/>
			<name name="{{=EkbEmCds}}Mosuke"/>
			<name name="{{=jskM3d0i}}Mohei"/>
			<name name="{{=NrA6HIZh}}Kintaro"/>
			<name name="{{=848CJxJF}}Kinichiro"/>
			<name name="{{=7Y0EXAUF}}Kinjiro"/>
			<name name="{{=YscUSvVM}}Kinsaburo"/>
			<name name="{{=XFTScKgz}}Kingoro"/>
			<name name="{{=NGdaEade}}Kinjuro"/>
			<name name="{{=TZ1EuCNG}}Kinsuke"/>
			<name name="{{=4MflqKiV}}Kinnosuke"/>
			<name name="{{=FNMFahJE}}Kinzo"/>
			<name name="{{=cRihXQNl}}Kinbei"/>
			<name name="{{=4CONhzCA}}Kinzaemon"/>
			<name name="{{=96CxYziS}}Kinemon"/>
			<name name="{{=x0UuCfZy}}Kametaro"/>
			<name name="{{=ZfUBVs4h}}Kameichiro"/>
			<name name="{{=a01TqKHx}}Kamejiro"/>
			<name name="{{=OfQZK6DU}}Kamesaburo"/>
			<name name="{{=xyMCyFya}}Kamegoro"/>
			<name name="{{=4enmAjQG}}Kamejuro"/>
			<name name="{{=2RTqEBHZ}}Kamesuke"/>
			<name name="{{=DsdZZ7ls}}Kamenosuke"/>
			<name name="{{=nhWIvnQN}}Kamezo"/>
			<name name="{{=22qdH8rc}}Tsurutaro"/>
			<name name="{{=sAfPsIZ8}}Tsuruichiro"/>
			<name name="{{=kfHjrOIs}}Tsurujiro"/>
			<name name="{{=5CSrGJfb}}Tsurusaburo"/>
			<name name="{{=wkcqyrPI}}Tsurugoro"/>
			<name name="{{=EugzWPXo}}Tsurujuro"/>
			<name name="{{=p0vR6eJa}}Tsurusuke"/>
			<name name="{{=7N515T5W}}Tsurunosuke"/>
			<name name="{{=oUA7SxtT}}Tsuruzo"/>
			<name name="{{=oTCqcyRD}}Sakutaro"/>
			<name name="{{=jJYN1RwQ}}Sakuichiro"/>
			<name name="{{=VHEgq8bQ}}Sakujiro"/>
			<name name="{{=7qfdlqBi}}Sakusaburo"/>
			<name name="{{=WeUjfIJ1}}Sakugoro"/>
			<name name="{{=ZzfKmRnj}}Sakujuro"/>
			<name name="{{=vSeWlxFv}}Sakusuke"/>
			<name name="{{=PruyWPPU}}Sakunosuke"/>
			<name name="{{=IqqhbpKT}}Sakuzo"/>
			<name name="{{=Jts7a19A}}Sakubei"/>
			<name name="{{=uzEmlox6}}Iichiro"/>
			<name name="{{=9iBq0QQK}}Izaburo"/>
			<name name="{{=2AiJGGSn}}Ijuro"/>
			<name name="{{=CTOORbUG}}Isuke"/>
			<name name="{{=jGQAK7wi}}Inosuke"/>
			<name name="{{=ZzQtwy55}}Izo"/>
			<name name="{{=1DpmL0dT}}Ihei"/>
			<name name="{{=M24CQ7LC}}Izaemon"/>
			<name name="{{=0HhVmhAb}}Iemon"/>
			<name name="{{=M7NqDxH8}}Sasuke"/>
			<name name="{{=mCtJGBTR}}Tarozaemon"/>
			<name name="{{=0dcfuOXl}}Taroemon"/>
			<name name="{{=MvkAmOXM}}Jirozaemon"/>
			<name name="{{=6xKoyc6E}}Hiroemon"/>
			<name name="{{=hNnejvkq}}Saburozaemon"/>
			<name name="{{=h8G0fO3K}}Saburoemon"/>
			<name name="{{=kNjiQUn5}}Shirozaemon"/>
			<name name="{{=KeKB3A1g}}Shiroemon"/>
			<name name="{{=p2JXgAFZ}}Gorozaemon"/>
			<name name="{{=Z62OstUb}}Goroemon"/>
</male_names>
		<female_names>
<name name="{{=ab4sZWzd}}Ai"/>
			<name name="{{=japJIRRy}}Ao"/>
			<name name="{{=VHbcxIU1}}Aka"/>
			<name name="{{=CMxdilZo}}Aki"/>
			<name name="{{=oldI3981}}Asa"/>
			<name name="{{=IrnQzBvz}}Ashi"/>
			<name name="{{=ONWqd9Hd}}Aso"/>
			<name name="{{=u9Pq27aa}}Ama"/>
			<name name="{{=bLckTBYg}}Ami"/>
			<name name="{{=Yk5AaGis}}Aya"/>
			<name name="{{=9spwFIMF}}Ayu"/>
			<name name="{{=icH6H6qJ}}Ara"/>
			<name name="{{=UgdrhMLn}}Awa"/>
			<name name="{{=Anthc16w}}Iki"/>
			<name name="{{=qimV9Hxm}}Iku"/>
			<name name="{{=T6f6o1Bn}}Isi"/>
			<name name="{{=G3bhQAAE}}Ise"/>
			<name name="{{=c7q2fLjb}}Iso"/>
			<name name="{{=p3I674Qb}}Iti"/>
			<name name="{{=0rDc1SBx}}Ito"/>
			<name name="{{=B3kJ5DP4}}Ina"/>
			<name name="{{=soDfj3wG}}Ine"/>
			<name name="{{=Tz4Dextk}}Ino"/>
			<name name="{{=gK8vPJRo}}Ima"/>
			<name name="{{=TQiCvmEX}}Iyo"/>
			<name name="{{=fif8CY4b}}Iwa"/>
			<name name="{{=hL7n98V7}}Uki"/>
			<name name="{{=Rjj08KZk}}Usu"/>
			<name name="{{=1tp1eT1f}}Uta"/>
			<name name="{{=FNz4NI40}}Une"/>
			<name name="{{=HnHrluJJ}}Umi"/>
			<name name="{{=gE3Hqou8}}Ume"/>
			<name name="{{=1I8FC31x}}Uya"/>
			<name name="{{=Sl3saZgp}}Ura"/>
			<name name="{{=qfFDtDwr}}Uri"/>
			<name name="{{=RJSAXUMm}}Uzu"/>
			<name name="{{=Tr73jAZQ}}Udo"/>
			<name name="{{=j4HdI45k}}Ena"/>
			<name name="{{=L7msLwYi}}Ema"/>
			<name name="{{=LT0jytMw}}Emi"/>
			<name name="{{=HFBFS90C}}Oki"/>
			<name name="{{=DsOfIrAJ}}Oku"/>
			<name name="{{=s9l9Q3k4}}Otu"/>
			<name name="{{=TxdyO9Ap}}Oto"/>
			<name name="{{=gyuAycqs}}Kai"/>
			<name name="{{=up78idxc}}Kane"/>
			<name name="{{=VT6HjYzj}}Kano"/>
			<name name="{{=8k3FCmKN}}Kaho"/>
			<name name="{{=KSdRaHkS}}Kame"/>
			<name name="{{=ujfb2pFG}}Kaya"/>
			<name name="{{=IOqdIxCq}}Kayo"/>
			<name name="{{=kv3ndFaH}}Kiku"/>
			<name name="{{=Pv7ea4X6}}Kisi"/>
			<name name="{{=EW5X2f0M}}Kise"/>
			<name name="{{=zdb2NTCA}}Kiso"/>
			<name name="{{=jLY0MV0K}}Kita"/>
			<name name="{{=6wI9KqdW}}Kiti"/>
			<name name="{{=FEKGGq4E}}Kitu"/>
			<name name="{{=UmIltDrq}}Kina"/>
			<name name="{{=RI8N5c4R}}Kima"/>
			<name name="{{=8WfG5S6b}}Kinu"/>
			<name name="{{=sfFxUyMo}}Kine"/>
			<name name="{{=mWIkRnp4}}Kino"/>
			<name name="{{=KmjDf7sk}}Kiyo"/>
			<name name="{{=1I1ml2CF}}Kin"/>
			<name name="{{=TelT9dGO}}Kusi"/>
			<name name="{{=gJqMupbd}}Kuni"/>
			<name name="{{=ClZg7i2D}}Kuma"/>
			<name name="{{=evYMHyLJ}}Kuri"/>
			<name name="{{=0VtFmtbM}}Kura"/>
			<name name="{{=zKgtrFlM}}Kuwa"/>
			<name name="{{=XvOb3Vwc}}Kesa"/>
			<name name="{{=1JS0UhVH}}Koi"/>
			<name name="{{=bmxZofcA}}Kosi"/>
			<name name="{{=3O8nA90w}}Koto"/>
			<name name="{{=9mxqMpWp}}Sai"/>
			<name name="{{=5m4AaikN}}Sae"/>
			<name name="{{=6RyBbZzh}}Saki"/>
			<name name="{{=TlcOPE8V}}Sati"/>
			<name name="{{=b5jjvG8v}}Sato"/>
			<name name="{{=A0nnN1Bs}}Sana"/>
			<name name="{{=TuSOLp1h}}Sano"/>
			<name name="{{=3ZG5q6fC}}Sane"/>
			<name name="{{=4Qq2nraH}}Saya"/>
			<name name="{{=LTGg9VfY}}Sayo"/>
			<name name="{{=te1zrRcu}}Sawa"/>
			<name name="{{=V8ZjxSqh}}San"/>
			<name name="{{=uH3PP2vd}}Sii"/>
			<name name="{{=cltMCIte}}Sie"/>
			<name name="{{=eIRxHAfM}}Sino"/>
			<name name="{{=p0pwjEGg}}Sima"/>
			<name name="{{=LkpYelD5}}Sira"/>
			<name name="{{=qq7IhZqX}}Sige"/>
			<name name="{{=txd54Uxq}}Sizu"/>
			<name name="{{=Rpp0GkVb}}Sue"/>
			<name name="{{=DsMiOMAl}}Sumi"/>
			<name name="{{=Co6CVsTn}}Suwa"/>
			<name name="{{=9PFPNnut}}Suzu"/>
			<name name="{{=LciAOoxg}}Seki"/>
			<name name="{{=dZN5LLR7}}Seto"/>
			<name name="{{=90tYcZnV}}Sena"/>
			<name name="{{=C0WToKkS}}Seri"/>
			<name name="{{=ALmaRDqh}}Sen"/>
			<name name="{{=LdhmLcqT}}Sono"/>
			<name name="{{=p7HhzIOW}}Some"/>
			<name name="{{=qJl74TrM}}Sora"/>
			<name name="{{=hFoAOFav}}Tae"/>
			<name name="{{=euu2KEpY}}Taka"/>
			<name name="{{=BdezqiyJ}}Taki"/>
			<name name="{{=KxoQul8K}}Take"/>
			<name name="{{=gHTh0qbu}}Tatu"/>
			<name name="{{=rqJlNuAP}}Tani"/>
			<name name="{{=DBzh6GCw}}Tane"/>
			<name name="{{=gGpyeF6m}}Tama"/>
			<name name="{{=BtvJlQvJ}}Tami"/>
			<name name="{{=788TRav9}}Tame"/>
			<name name="{{=3ndapyaU}}Tie"/>
			<name name="{{=gSk9DM6l}}Tika"/>
			<name name="{{=FPFqiv7a}}Tise"/>
			<name name="{{=aHHPOcUo}}Tita"/>
			<name name="{{=cfJP1a1D}}Tino"/>
			<name name="{{=OyxDo16q}}Tiyo"/>
			<name name="{{=UZrCJPZv}}Tuki"/>
			<name name="{{=fJzk4BNs}}Tuta"/>
			<name name="{{=whxtdqVI}}Tuna"/>
			<name name="{{=ZBK2n7tg}}Tune"/>
			<name name="{{=Imo98lXX}}Tuya"/>
			<name name="{{=PZbf10XQ}}Tuyu"/>
			<name name="{{=6HSzptnS}}Turu"/>
			<name name="{{=J0UsxkRC}}Tei"/>
			<name name="{{=g1qXRvel}}Teru"/>
			<name name="{{=a3VKcfJa}}Toi"/>
			<name name="{{=T62NIiA6}}Toki"/>
			<name name="{{=TFs4nM7I}}Toku"/>
			<name name="{{=ZzRx1abW}}Tosa"/>
			<name name="{{=ecTaQB7j}}Tosi"/>
			<name name="{{=TJMitVon}}Tose"/>
			<name name="{{=XZlG26oj}}Toti"/>
			<name name="{{=kFitx5pm}}Tone"/>
			<name name="{{=8BeZLj90}}Tomi"/>
			<name name="{{=VRx7ynCm}}Tome"/>
			<name name="{{=iqkzFqot}}Toyo"/>
			<name name="{{=VwDXADid}}Tora"/>
			<name name="{{=Di20AtpT}}Towa"/>
			<name name="{{=ZzWzXLht}}Nae"/>
			<name name="{{=nDpHHmR1}}Nao"/>
			<name name="{{=ZEmO4WfN}}Naka"/>
			<name name="{{=QsnLEOc2}}Nata"/>
			<name name="{{=3x8xcHOj}}Natu"/>
			<name name="{{=tT546r7c}}Nana"/>
			<name name="{{=eVbD6jeL}}Nami"/>
			<name name="{{=gvK789wT}}Nara"/>
			<name name="{{=VPUvGEii}}Nawa"/>
			<name name="{{=kVvV6i76}}Nisi"/>
			<name name="{{=NyCRcnms}}Nei"/>
			<name name="{{=AiQKTb3M}}Nene"/>
			<name name="{{=HEKsNIc0}}Noe"/>
			<name name="{{=nFeGwvdW}}Noto"/>
			<name name="{{=CGlpxIRk}}Haku"/>
			<name name="{{=4RaBnJJd}}Hasu"/>
			<name name="{{=bucIcjxy}}Hata"/>
			<name name="{{=oA19rmhg}}Hatu"/>
			<name name="{{=2uCie4Q2}}Hana"/>
			<name name="{{=OcCRaagX}}Haru"/>
			<name name="{{=ue0urULM}}Hisa"/>
			<name name="{{=EJpUBSCs}}Hisi"/>
			<name name="{{=6HoPLMn3}}Hina"/>
			<name name="{{=vY9Bp6uB}}Fuki"/>
			<name name="{{=9LTZ9XB7}}Fuku"/>
			<name name="{{=4UmAhqew}}Fusa"/>
			<name name="{{=UohnfxGD}}Fusi"/>
			<name name="{{=XCHQC5f3}}Futi"/>
			<name name="{{=KyQ1C73j}}Funa"/>
			<name name="{{=Gnl8tupV}}Fune"/>
			<name name="{{=4akZ5HEq}}Fumi"/>
			<name name="{{=kLALO29g}}Fuzi"/>
			<name name="{{=88hLDKtN}}Mai"/>
			<name name="{{=5OrsY3Wz}}Maki"/>
			<name name="{{=gxfZ2w3W}}Masa"/>
			<name name="{{=yJXssD0P}}Masu"/>
			<name name="{{=LJyp9AKZ}}Matu"/>
			<name name="{{=bj63fnCB}}Mana"/>
			<name name="{{=4rWDHouO}}Mame"/>
			<name name="{{=qXNWMbUO}}Mayu"/>
			<name name="{{=wblnzRnV}}Mari"/>
			<name name="{{=ufNTpzmv}}Mie"/>
			<name name="{{=ZqMNObKk}}Miki"/>
			<name name="{{=LuePJv7g}}Miti"/>
			<name name="{{=PV5iDOtM}}Mitu"/>
			<name name="{{=D7la9JKQ}}Mina"/>
			<name name="{{=Gu1BKtTu}}Mine"/>
			<name name="{{=6kg6dx0j}}Miya"/>
			<name name="{{=ZMQ1VDUl}}Miyo"/>
			<name name="{{=DvL7f4QU}}Moto"/>
			<name name="{{=aq9b50cV}}Momo"/>
			<name name="{{=wRPynz1h}}Yae"/>
			<name name="{{=eHd1rxID}}Yasu"/>
			<name name="{{=sufwawfp}}Yui"/>
			<name name="{{=vOc88PUp}}Yuki"/>
			<name name="{{=q8EmtRbW}}Yuna"/>
			<name name="{{=vnjOoU7B}}Yumi"/>
			<name name="{{=0b9APW9w}}Yume"/>
			<name name="{{=yjmoZDpL}}Yuri"/>
			<name name="{{=y3LihP27}}Yosi"/>
			<name name="{{=ynSceRn1}}Yona"/>
			<name name="{{=llXWtjvk}}Yone"/>
			<name name="{{=keDd7GwF}}Yono"/>
			<name name="{{=p2YaumtD}}Rie"/>
			<name name="{{=6BuhQ3yC}}Riku"/>
			<name name="{{=LVLqSHsN}}Risa"/>
			<name name="{{=dUUYeeqh}}Ritu"/>
			<name name="{{=xdlCLqcy}}Rin"/>
			<name name="{{=zLwhd8EY}}Rui"/>
			<name name="{{=IVbOeER4}}Ruri"/>
</female_names>
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
    raw = sc.read_bytes()
    # 🔴 保留原文件的换行风格（2026-09-12 踩坑：本文件是 LF，生成器按 Windows 默认写成 CRLF
    #    → 整份文件逐行"变化"，diff 不可读、也误导体检）
    eol = "\r\n" if b"\r\n" in raw else "\n"
    txt = raw.decode("utf-8-sig", errors="replace")

    def render(cid, key, en, color, color2, is_main):
        return (tmpl.replace("__CID__", cid).replace("__CKEY__", key).replace("__CEN__", en)
                .replace("__COLOR2__", color2).replace("__COLOR__", color)
                .replace("__ISMAIN__", "true" if is_main else "false"))

    # 🔴🔴 文化清单（2026-09-12 用户裁定：**与织丰零依赖 → 全部文化都定义在本包**）
    #    16 个 = ikoku（主文化）+ neutral_culture（本文件原有、不在此生成）+ 9 地域 + 5 身份文化。
    #    为什么必须全建：原先 21 个文化里 **19 个的定义在织丰**（Taikou 只定义 2 个），
    #    引用闭合检查因为"跨模块命中"而全绿 → 隐式依赖（见 CLAUDE.md 三单元架构铁则 6 / 必备清单雷 96）。
    CULTURES = [
        # (id, 本地化键, 英文名, color, color2, is_main_culture)
        ("ikoku", "TAIKOU_culture_ikoku", "Japanese", "0xff9e1b32", "0xffFCDE90", True),
        # ── 9 地域文化 ──
        ("saikai", "TAIKOU_culture_saikai", "Saikai", "0xff2e6d6d", "0xffa8d8d8", True),
        ("nankai", "TAIKOU_culture_nankai", "Nankai", "0xff2e5a6d", "0xffa8c8dc", True),
        ("sanyo", "TAIKOU_culture_sanyo", "Sanyo", "0xff5a6d2e", "0xffc8dc9e", True),
        ("kinai", "TAIKOU_culture_kinai", "Kinai", "0xff6d2e5a", "0xffdc9ec8", True),
        ("hokuriku", "TAIKOU_culture_hokuriku", "Hokuriku", "0xff2e3d6d", "0xff9ea8dc", True),
        ("tosan", "TAIKOU_culture_tosan", "Tosan", "0xff4a6d2e", "0xffb4dc9e", True),
        ("tokai", "TAIKOU_culture_tokai", "Tokai", "0xff6d4a2e", "0xffdcb49e", True),
        ("kanto", "TAIKOU_culture_kanto", "Kanto", "0xff6d2e2e", "0xffdc9e9e", True),
        ("ou", "TAIKOU_culture_ou", "Ou", "0xff2e6d4a", "0xff9edcb4", True),
        # ── 5 身份文化（忍者/海贼/商人 = 泛用 hero 与三类组织；强盗 = 吸收 8 个旧文化；浪人 = 游荡者）──
        ("ninja", "TAIKOU_culture_ninja", "Ninja", "0xff2f4f4f", "0xffb0c4de", False),
        ("pirate", "TAIKOU_culture_pirate", "Pirate", "0xff1f3d5c", "0xff9ec5e8", False),
        ("trader", "TAIKOU_culture_trader", "Merchant", "0xff6b4f1d", "0xffe8d8a0", False),
        ("looters", "TAIKOU_culture_looters", "Bandits", "0xff4a3a2a", "0xffc8b49e", False),
        ("ronin", "TAIKOU_culture_ronin", "Ronin", "0xff4a4a4a", "0xffc8c8c8", False),
        # ── 南蛮（2026-09-12 用户裁定：标记西洋/外国人——佛罗伊斯/阿鲁梅达/弥助/三浦按针）──
        ("namban", "TAIKOU_culture_namban", "Namban", "0xff7a5c2e", "0xffe8cfa8", False),
    ]

    # 逐个：本包已有 → 整块替换；本包没有 → 插到 </SPCultures> 前（幂等：重跑走替换分支）
    for c in CULTURES:
        blk = render(*c)
        if re.search(r'\t<Culture id="%s".*?</Culture>' % c[0], txt, flags=re.S):
            txt = re.sub(r'\t<Culture id="%s".*?</Culture>' % c[0], blk, txt, count=1, flags=re.S)
        else:
            txt = txt.replace("</SPCultures>", blk + "\n</SPCultures>", 1)

    # 🔴 原文件已含 XML 声明（utf-8-sig），只插生成物注释，不得重复写声明（2026-09-08 踩坑：重复声明 parse 失败）
    body = txt.split("?>", 1)[1]
    out = ('<?xml version="1.0" encoding="utf-8"?>\n'
           '<!-- 生成物（2026-09-08 gen_taikou_culture_full.py）：织丰/原版字段交集 76 字段全量；'
           '值全引用自有资源；禁止手改（铁律 22） -->\n' + body)
    sc.write_bytes(out.replace("\n", eol).encode("utf-8-sig"))
    print("spcultures.xml: 本包自建文化 %d 个（%s）+ 原有 neutral_culture"
          % (len(CULTURES), " ".join(c[0] for c in CULTURES)))

    # 2) spnpccharacters：40 模板追加（🔴 幂等：已含 townsman = 已生成过，跳过——重跑不重复追加）
    npc = md / "spnpccharacters.xml"
    t = npc.read_text(encoding="utf-8-sig", errors="replace")
    if 'id="townsman"' in t and 'id="gang_leader"' in t:
        print("spnpccharacters.xml: 模板已存在，跳过追加（幂等）")
    else:
        cells = "\n\n".join(npc_cell(nt) for nt in NPC_TEMPLATES)
        t = t.replace("</NPCCharacters>", cells + "\n\n</NPCCharacters>", 1)
        npc.write_text(t, encoding="utf-8-sig")
        print(f"spnpccharacters.xml: +{len(NPC_TEMPLATES)} 个场景 NPC 模板")

    # 2b) 每地域文化一个 caravan_guard（🔴 引擎硬查询，2026-09-12 据点文化分地域后暴露）：
    #     `InitializeCaravanOnCreation` 取 `Occupation==CaravanGuard ∧ DefaultFormationGroup==Infantry
    #     ∧ Level==26 ∧ Culture==**该文化**` 的 `First(...)` —— 通用那份是 ikoku 文化，
    #     9 个地域文化各有据点（有据点 = 商会建商队）→ 各需一份，否则组商队即抛（雷 16 同族）。
    #     幂等：逐个按 id 判断；名字键复用通用的 `TAIKOU_npc_caravan_guard`（同一兵种的地区版，不新增文案）。
    t = npc.read_text(encoding="utf-8-sig", errors="replace")
    cells, added = [], []
    for cid, _k, _en, _c1, _c2, _m in CULTURES:
        if cid == "ikoku":
            continue                             # ikoku 用通用那份（id=caravan_guard）
        tid = "caravan_guard_" + cid
        if 'id="%s"' % tid in t:
            continue
        cells.append(npc_cell((tid, "CaravanGuard", "Caravan Guard", False, 26),
                              culture=cid, name_key="TAIKOU_npc_caravan_guard"))
        added.append(tid)
    if cells:
        t = t.replace("</NPCCharacters>", "\n\n".join(cells) + "\n\n</NPCCharacters>", 1)
        npc.write_text(t, encoding="utf-8-sig")
    print("spnpccharacters.xml: 地域商队护卫 +%d（%s）" % (len(added), " ".join(added) or "无（幂等）"))

    # 3) self-check：parse
    import xml.dom.minidom as m
    m.parse(str(sc)); m.parse(str(npc))
    print("parse OK")


if __name__ == "__main__":
    main()
