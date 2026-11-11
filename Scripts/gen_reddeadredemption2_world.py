#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""RedDeadRedemption2 内容包 · 最小世界生成器（2026-11-11 建）

用途：生成一个**能启动**的最小 RDR2 世界（1 文化 / 1 王国 / 1 家族 / 2 领主 / 1 据点），
      并把官方基础数据、物品库、大地图场景骨架拷进本包。

世界定义（改世界 = 改下面「世界定义」段，然后重跑）：
  文化   rdr2_lemoyne（莱莫恩）
  王国   kingdom_rdr2_lemoyne（莱莫恩庄园联盟）
  家族   clan_rdr2_bronte（勃朗特家族）+ 玩家族 player_faction
  领主   lord_rdr2_bronte（安杰洛·勃朗特）/ lord_rdr2_saintdenis（亨利·勒米厄）
  据点   town_rdr2_sdn（圣丹尼斯）+ retirement_retreat（引擎硬编码点名的退休营地）

数据源（只读）：
  Modules/{Native,SandBoxCore,SandBox,Taikou}/ModuleData/*.xml
  Modules/SandBox/SceneObj/Main_map/*（大地图骨架，地形仍是原版卡拉迪亚占位）

铁律：
  - 生成物禁止手改；改内容 = 改本脚本，然后重跑（CLAUDE.md 铁律 22）。
  - 文化引用闭合在本包（不引用原版 8 文化）。
  - 内容包文本键一律 RDR2_ 前缀，不引用 LWN_ 键。
"""

import io
import os
import re
import shutil
import xml.etree.ElementTree as ET

# ──────────────────────────────────────────────────────────────
# 路径与模块身份
# ──────────────────────────────────────────────────────────────

# 🔴 拷贝源 = **运行客户端**（1.2.12）的官方模块，不是 Steam 主目录（1.5.x）——
#    版本必须跟引擎一致，否则拷来的 GameText 键/场景实体在旧引擎上对不上。
MODULES = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules"
# 输出模块 = 共享模块根（各客户端 junction/symlink 同源）
MODULE = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\RedDeadRedemption2"

MODULE_ID = "RedDeadRedemption2"
GAME_TYPE = "RedDeadRedemption2Campaign"   # = LWN 里的 thin 类名（逐字一致）

# ──────────────────────────────────────────────────────────────
# 世界定义（最小版）
# ──────────────────────────────────────────────────────────────

CULTURE = "rdr2_lemoyne"
CULTURE_EN, CULTURE_ZH = "Lemoyne", "莱莫恩"
CULTURE_SUFFIX = "sturgia"   # 官方角色模板后缀（Roles 段里的 merchant_sturgia 等）

KINGDOM_ID = "kingdom_rdr2_vdl"
KINGDOM_EN, KINGDOM_ZH = "Van der Linde Gang", "范德林德帮"

CLAN_ID = "clan_rdr2_vdl"
CLAN_EN, CLAN_ZH = "Van der Linde Gang", "范德林德帮"

# (hero_id, English, 中文, 年龄)
HEROES = [
    ("lord_rdr2_dutch", "Dutch van der Linde", "达奇·范德林德", 45),
    ("lord_rdr2_micah", "Micah Bell",          "迈卡·贝尔",     39),
]
RULER_HERO = "lord_rdr2_dutch"

# 据点：v0 只有 1 城（圣丹尼斯）+ 退休营地。
# 🔴 引擎对据点数没有下限——崩溃的真因是「城里没有帮派头目知名人士」（AlleyCampaignBehavior 里
#    `i % source.Count()` 除零），修法 = 文化的 notable 名单里要有 occupation=GangLeader 的模板。
# 场景先指向原版 empire_town_g；RDR2 自己的圣丹尼斯场景暂不启用。
# 坐标 = RDR2 大地图锚点（这张图 768×576）。
SETTLEMENTS = [
    dict(id="town_rdr2_sdn", en="Saint Denis", zh="圣丹尼斯", kind="town",
         x=670.324, y=261.978, z=5.686, bound="", scene="rdr2_sanit_denis"),
]

# 城镇场景（RDR2 自己的圣丹尼斯）：从 Publish 导出物拷进内容包，并补一个玩家出生点。
# 🔴 这张场景目前**只有美术**：没有 spawnpoint_player、没有 UsableMachine 站位点。
#    - 少 spawnpoint_player → 玩家出生在 (0,0,0)（场景角落）
#    - 少站位点 → NPC 全部挤在原点（不崩，但街上没人）
TOWN_SCENE = "rdr2_sanit_denis"
TOWN_PLAYER_SPAWN = (2153.9, 1806.7, 50.2)   # 圣丹尼斯主街（road 实体中位数附近，实测 z≈50）

# 大地图据点胶囊的 z：原版地图 (670,262) 一带的贴地高度约 2.8~5.5（按邻近原版胶囊实测），取 3.0
CAPSULE_Z = 3.0

FACTION_PLAYER_ID = "player_faction"
PLAYER_FACTION_EN, PLAYER_FACTION_ZH = "Van der Linde Gang", "范德林德帮"

DEFAULT_FACE_KEY = "000fa92e90004202aced5d976886573d5d679585a376fdd605877a7764b8987c00000000000007520000037f0000000f00000037049140010000000000000000"
BANNER_KEYS = [
    "11.148.148.1536.1536.764.764.1.0.0.984.171.171.483.483.764.764.0.0.0",
    "11.156.75.1536.1536.764.764.1.0.0.890.116.116.483.483.764.764.0.0.0",
    "11.154.154.1536.1536.764.764.1.0.0.877.171.171.483.483.764.764.0.0.0",
]

# 原版文化 → 本包文化（拷贝官方文件时清洗用）
VANILLA_CULTURE_MAP = {
    "aserai": CULTURE, "battania": CULTURE, "empire": CULTURE,
    "khuzait": CULTURE, "sturgia": CULTURE, "vlandia": CULTURE,
    "ikoku": CULTURE,
}

# 名字池（最小版：够用即可；以后接 RDR2 名册表）
MALE_NAMES = [
    ("Dutch", "达奇"), ("Hosea", "何西阿"), ("Arthur", "亚瑟"), ("John", "约翰"),
    ("Bill", "比尔"), ("Charles", "查尔斯"), ("Javier", "哈维尔"), ("Lenny", "兰尼"),
    ("Sean", "肖恩"), ("Kieran", "基兰"), ("Micah", "迈卡"), ("Josiah", "乔赛亚"),
    ("Angelo", "安杰洛"), ("Henri", "亨利"), ("Tavish", "塔维什"), ("Leigh", "利"),
]
FEMALE_NAMES = [
    ("Sadie", "莎迪"), ("Abigail", "阿比盖尔"), ("Mary", "玛丽"), ("Tilly", "蒂莉"),
    ("Karen", "卡伦"), ("Molly", "莫莉"), ("Susan", "苏珊"), ("Charlotte", "夏洛特"),
    ("Bonnie", "邦妮"), ("Hosea", "何西阿"),
]
CLAN_NAMES = [
    (CLAN_EN, CLAN_ZH),
    ("Lemoyne Planters", "莱莫恩庄园联盟"),
    ("Saint Denis Municipality", "圣丹尼斯市政"),
    ("Gray Family", "格雷家族"),
    ("Braithwaite Family", "布莱斯韦特家族"),
]

# 官方基础文件拷贝：(源模块, 源相对 ModuleData 路径, 目标相对 ModuleData 路径)
BASE_COPIES = [
    ("SandBoxCore", "spnpccharactertemplates.xml", "rdr2w_npc_templates.xml"),
    ("SandBoxCore", "spnpccharacters.xml", "rdr2w_roles.xml"),
    ("SandBoxCore", "sandboxcore_bodyproperties.xml", "rdr2w_bodyproperties.xml"),
    ("SandBoxCore", "sandboxcore_equipment_sets.xml", "rdr2w_equipment_sets.xml"),
    ("SandBoxCore", "sandboxcore_skill_sets.xml", "rdr2w_skill_sets.xml"),
    ("SandBox", "location_complex_templates.xml", "rdr2w_location_complex_templates.xml"),
    ("SandBox", "spworkshops.xml", "spworkshops.xml"),
    ("SandBox", "settlement_tracks.xml", "rdr2w_music_tracks.xml"),
    ("SandBox", "settlement_track_instruments.xml", "rdr2w_music_instruments.xml"),
    ("Native", "crafting_pieces.xml", "rdr2w_crafting_pieces.xml"),
    ("Taikou", "taikou_concepts.xml", "rdr2w_concepts.xml"),
    # 9 个官方 GameText 段（SandBox 带 GameType 白名单，自定义 GameType 下整文件被过滤 → 必须自家注册）
    ("SandBox", "module_strings.xml", "module_strings.xml"),
    ("SandBox", "world_lore_strings.xml", "world_lore_strings.xml"),
    ("SandBox", "companion_strings.xml", "companion_strings.xml"),
    ("SandBox", "wanderer_strings.xml", "wanderer_strings.xml"),
    ("SandBox", "comment_strings.xml", "comment_strings.xml"),
    ("SandBox", "comment_on_action_strings.xml", "comment_on_action_strings.xml"),
    ("SandBox", "trait_strings.xml", "trait_strings.xml"),
    ("SandBox", "voice_strings.xml", "voice_strings.xml"),
    ("SandBox", "action_strings.xml", "action_strings.xml"),
]

STRINGS = {}   # key -> (en, zh)


def L(key, en, zh):
    """登记一条本地化文本，返回数据 XML 里的 {=KEY}English 形式。"""
    STRINGS[key] = (en, zh)
    return "{=%s}%s" % (key, esc(en))


def esc(s):
    return (s or "").replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace('"', "&quot;")


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with io.open(path, "w", encoding="utf-8-sig", newline="\n") as f:
        f.write(text)


CULTURE_RE = re.compile(r"Culture\.([a-zA-Z0-9_]+)")


def sanitize_cultures(text):
    """把官方文件里的原版文化引用换成本包文化；neutral_culture 与本包文化原样保留。"""
    def repl(m):
        key = m.group(1)
        if key in ("neutral_culture", CULTURE):
            return m.group(0)
        return "Culture." + VANILLA_CULTURE_MAP.get(key, CULTURE)
    return CULTURE_RE.sub(repl, text)


# ──────────────────────────────────────────────────────────────
# 基础文件拷贝
# ──────────────────────────────────────────────────────────────

def copy_base():
    for src_mod, src_rel, dst_rel in BASE_COPIES:
        src = os.path.join(MODULES, src_mod, "ModuleData", src_rel)
        if not os.path.isfile(src):
            print("  [skip missing] %s" % src)
            continue
        with io.open(src, "r", encoding="utf-8-sig") as f:
            text = f.read()
        text = sanitize_cultures(text)
        if dst_rel == "rdr2w_roles.xml":
            # 地痞三档由 rdr2w_characters.xml 提供，这里删掉避免重复 id
            root = ET.fromstring(text)
            for el in list(root):
                if el.get("id") in ("gangster_1", "gangster_2", "gangster_3"):
                    root.remove(el)
            text = ET.tostring(root, encoding="unicode")
            text = '<?xml version="1.0" encoding="utf-8"?>\n' + text
        write(os.path.join(MODULE, "ModuleData", dst_rel), text)
        print("  copied %s -> %s" % (src_rel, dst_rel))

    # 官方物品库（整目录；铁律 33：每件必须有 Civilian="true"）
    src_items = os.path.join(MODULES, "SandBoxCore", "ModuleData", "items")
    dst_items = os.path.join(MODULE, "ModuleData", "rdr2w_items")
    for name in sorted(os.listdir(src_items)):
        if not name.endswith(".xml"):
            continue
        with io.open(os.path.join(src_items, name), "r", encoding="utf-8-sig") as f:
            text = f.read()
        text = sanitize_cultures(text)
        try:
            root = ET.fromstring(text)
            for item in root.iter("Item"):
                flags = item.find("Flags")
                if flags is None:
                    flags = ET.SubElement(item, "Flags")
                flags.set("Civilian", "true")
            text = '<?xml version="1.0" encoding="utf-8"?>\n' + ET.tostring(root, encoding="unicode")
        except Exception as e:
            print("  [warn] item flags patch failed", name, e)
        write(os.path.join(dst_items, name), text)
    print("  copied items/*.xml")

    # 文化默认装备模板（官方没有 neutral_battle_template，自建两份）
    eq_path = os.path.join(MODULE, "ModuleData", "rdr2w_equipment_sets.xml")
    if os.path.isfile(eq_path):
        eq = io.open(eq_path, encoding="utf-8-sig").read()
        if 'id="neutral_battle_template"' not in eq:
            extra = """
  <EquipmentRoster id="neutral_battle_template">
    <EquipmentSet>
      <Equipment slot="Item0" id="Item.aserai_sword_1_t2" />
      <Equipment slot="Body" id="Item.tunic_with_shoulder_pads" />
      <Equipment slot="Leg" id="Item.leather_boots" />
    </EquipmentSet>
  </EquipmentRoster>
  <EquipmentRoster id="neutral_civilian_template">
    <EquipmentSet civilian="true">
      <Equipment slot="Body" id="Item.tunic_with_shoulder_pads" />
      <Equipment slot="Leg" id="Item.leather_boots" />
    </EquipmentSet>
  </EquipmentRoster>
"""
            eq = eq.replace("</EquipmentRosters>", extra + "</EquipmentRosters>")
            write(eq_path, eq)
            print("  appended neutral equipment templates")


# ──────────────────────────────────────────────────────────────
# 世界 XML
# ──────────────────────────────────────────────────────────────

def troop_id(role):
    return "NPCCharacter.%s_%s" % (CULTURE, role)


def gen_cultures():
    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<!-- 生成物：Scripts/gen_reddeadredemption2_world.py → 禁止手改（铁律 22） -->',
           '<SPCultures>']

    name = L("RDR2_culture_" + CULTURE, CULTURE_EN, CULTURE_ZH)
    out.append('  <Culture id="%s" name="%s" color="0xff7a2f35" color2="0xffe3b7a8"' % (CULTURE, name))
    out.append('           basic_troop="%s" elite_basic_troop="%s"' % (troop_id("recruit"), troop_id("soldier")))
    out.append('           melee_militia_troop="%s" ranged_militia_troop="%s"' % (troop_id("militia"), troop_id("archer")))
    out.append('           melee_elite_militia_troop="%s" ranged_elite_militia_troop="%s"' % (troop_id("veteran"), troop_id("sharpshooter")))
    out.append('           is_main_culture="true" can_have_settlement="true" town_edge_number="16" prosperity_bonus="1"')
    out.append('           faction_banner_key="%s"' % BANNER_KEYS[0])
    out.append('           default_face_key="%s"' % DEFAULT_FACE_KEY)
    out.append('           encounter_background_mesh="encounter_empire"')
    out.append('           default_party_template="PartyTemplate.%s_lord_party_template"' % CULTURE)
    out.append('           militia_party_template="PartyTemplate.%s_militia_party_template"' % CULTURE)
    out.append('           villager_party_template="PartyTemplate.%s_villager_party_template"' % CULTURE)
    out.append('           caravan_party_template="PartyTemplate.%s_caravan_party_template"' % CULTURE)
    out.append('           elite_caravan_party_template="PartyTemplate.%s_elite_caravan_party_template"' % CULTURE)
    out.append('           rebels_party_template="PartyTemplate.%s_rebels_party_template"' % CULTURE)
    out.append('           bandit_boss_party_template="PartyTemplate.%s_bandit_party_template"' % CULTURE)
    out.append('           vassal_reward_party_template="PartyTemplate.%s_vassal_party_template"' % CULTURE)
    out.append('           default_battle_equipment_roster="EquipmentRoster.neutral_battle_template"')
    out.append('           default_civilian_equipment_roster="EquipmentRoster.neutral_civilian_template"')
    out.append('           duel_preset_equipment_roster="EquipmentRoster.neutral_battle_template"')
    out.append('           board_game_type="SixAndEight"')

    # 官方角色模板：文化后缀 id（merchant_sturgia 等），全部由 rdr2w_roles.xml 提供
    suffix = CULTURE_SUFFIX
    role_ids = {
        "armed_trader": "armed_trader_" + suffix,
        "armorer": "armorer_" + suffix,
        "artisan_notary": "artisan_notary_" + suffix,
        "barber": "barber_" + suffix,
        "beggar": "beggar_" + suffix,
        "blacksmith": "blacksmith_" + suffix,
        "caravan_guard": "caravan_guard_" + suffix,
        "caravan_master": "caravan_master_" + suffix,
        "female_beggar": "female_beggar_" + suffix,
        "female_dancer": "female_dancer_" + suffix,
        "gangleader_bodyguard": "gangleader_bodyguard_" + suffix,
        "gear_dummy": "gear_practice_dummy_" + suffix,
        "gear_practice_dummy": "gear_practice_dummy_" + suffix,
        "guard": "guard",
        # 🔴 进城必读：GuardsCampaignBehavior.CreatePrisonGuard 直接解引用 culture.PrisonGuard，
        #    缺这个属性 = 进任何城镇场景当场 NullReferenceException（2026-11-11 实机）。
        "prison_guard": "prison_guard_" + suffix,
        "horseMerchant": "horseMerchant_" + suffix,
        "merchant": "merchant_" + suffix,
        "merchant_notary": "merchant_notary_" + suffix,
        "musician": "musician_" + suffix,
        "preacher_notary": "preacher_notary_" + suffix,
        "ransom_broker": "ransom_broker_" + suffix,
        "rural_notable_notary": "rural_notable_notary_" + suffix,
        "shop_worker": "shop_worker_" + suffix,
        "tavern_wench": "tavern_wench_" + suffix,
        "taverngamehost": "taverngamehost_" + suffix,
        "tavernkeeper": "tavernkeeper_" + suffix,
        "tournament_master": "tournament_master_" + suffix,
        "townsman": "townsman_" + suffix,
        "townsman_child": "townsman_child_" + suffix,
        "townsman_infant": "townsman_infant_" + suffix,
        "townsman_teenager": "townsman_teenager_" + suffix,
        "townswoman": "townswoman_" + suffix,
        "townswoman_child": "townswoman_child_" + suffix,
        "townswoman_infant": "townswoman_infant_" + suffix,
        "townswoman_teenager": "townswoman_teenager_" + suffix,
        "village_woman": "village_woman_" + suffix,
        "villager": "villager_" + suffix,
        "villager_male_child": "villager_child_" + suffix,
        "villager_male_teenager": "villager_teenager_" + suffix,
        "villager_female_child": "village_woman_child_" + suffix,
        "villager_female_teenager": "village_woman_teenager_" + suffix,
        "veteran_caravan_guard": "veteran_caravan_guard_" + suffix,
        "weapon_practice_stage_1": "villager_" + suffix,
        "weapon_practice_stage_2": "guard",
        "weapon_practice_stage_3": "guard",
        "weaponsmith": "weaponsmith_" + suffix,
    }
    for attr, npc in role_ids.items():
        out.append('           %s="NPCCharacter.%s"' % (attr, npc))
    out.append('           >')
    out.append('    <notable_and_wanderer_templates>')
    # 🔴 必须覆盖引擎开局刷知名人士用到的 occupation（1.2.12 实测）：
    #    城 = Artisan / Merchant / **GangLeader**；缺 GangLeader 模板 → 城里 0 个帮派头目 →
    #    AlleyCampaignBehavior 里 `i % source.Count()` 抛 DivideByZeroException（2026-11-11 实机崩）。
    for t in ["main_hero", CULTURE + "_merchant", CULTURE + "_artisan", "gang_leader"]:
        out.append('      <template name="NPCCharacter.%s"/>' % t)
    out.append('    </notable_and_wanderer_templates>')
    out.append('    <lord_templates />')
    out.append('    <rebellion_hero_templates />')
    out.append('    <basic_mercenary_troops>')
    out.append('      <template name="NPCCharacter.rdr2w_mercenary"/>')
    out.append('    </basic_mercenary_troops>')
    out.append('    <cultural_feats />')
    out.append('    <default_policies />')
    out.append('    <clan_names>')
    for i, (en_cn, zh_cn) in enumerate(CLAN_NAMES):
        out.append('      <name name="%s"/>' % esc(L("RDR2_clanname_%d" % i, en_cn, zh_cn)))
    out.append('    </clan_names>')
    out.append('    <male_names>')
    for i, (en_n, zh_n) in enumerate(MALE_NAMES):
        out.append('      <name name="%s"/>' % esc(L("RDR2_malename_%d" % i, en_n, zh_n)))
    out.append('    </male_names>')
    out.append('    <female_names>')
    for i, (en_n, zh_n) in enumerate(FEMALE_NAMES):
        out.append('      <name name="%s"/>' % esc(L("RDR2_femalename_%d" % i, en_n, zh_n)))
    out.append('    </female_names>')
    out.append('    <possible_clan_banner_icon_ids />')
    out.append('    <tournament_team_templates_one_participant />')
    out.append('    <tournament_team_templates_two_participant />')
    out.append('    <tournament_team_templates_four_participant />')
    out.append('    <banner_bearer_replacement_weapons>')
    out.append('      <item id="Item.aserai_sword_1_t2"/>')
    out.append('    </banner_bearer_replacement_weapons>')
    out.append('    <vassal_reward_items />')
    out.append('  </Culture>')

    # neutral_culture（引擎 4 处 fallback 消费；必须与主文化同等完整）
    name = L("RDR2_culture_neutral", "Neutral", "中立")
    out.append('  <Culture id="neutral_culture" name="%s"' % name)
    out.append('           basic_troop="NPCCharacter.rdr2w_mercenary" elite_basic_troop="NPCCharacter.rdr2w_mercenary"')
    out.append('           melee_militia_troop="NPCCharacter.guard" ranged_militia_troop="NPCCharacter.guard"')
    out.append('           melee_elite_militia_troop="NPCCharacter.guard" ranged_elite_militia_troop="NPCCharacter.guard"')
    out.append('           default_party_template="PartyTemplate.rdr2w_lord_party_template"')
    out.append('           militia_party_template="PartyTemplate.rdr2w_militia_party_template"')
    out.append('           villager_party_template="PartyTemplate.rdr2w_villager_party_template"')
    out.append('           caravan_party_template="PartyTemplate.rdr2w_caravan_party_template"')
    out.append('           elite_caravan_party_template="PartyTemplate.rdr2w_elite_caravan_party_template"')
    out.append('           rebels_party_template="PartyTemplate.rdr2w_rebels_party_template"')
    out.append('           bandit_boss_party_template="PartyTemplate.rdr2w_bandit_party_template"')
    out.append('           vassal_reward_party_template="PartyTemplate.rdr2w_vassal_party_template"')
    out.append('           encounter_background_mesh="encounter_empire"')
    out.append('           default_battle_equipment_roster="EquipmentRoster.neutral_battle_template"')
    out.append('           default_civilian_equipment_roster="EquipmentRoster.neutral_civilian_template"')
    out.append('           >')
    out.append('    <notable_and_wanderer_templates>')
    out.append('      <template name="NPCCharacter.main_hero"/>')
    out.append('      <template name="NPCCharacter.merchant"/>')
    out.append('      <template name="NPCCharacter.artisan"/>')
    out.append('      <template name="NPCCharacter.gangster_1"/>')
    out.append('    </notable_and_wanderer_templates>')
    out.append('    <lord_templates />')
    out.append('    <rebellion_hero_templates />')
    out.append('    <clan_names>')
    for i, (en_cn, zh_cn) in enumerate(CLAN_NAMES):
        out.append('      <name name="%s"/>' % esc(L("RDR2_clanname_%d" % i, en_cn, zh_cn)))
    out.append('    </clan_names>')
    out.append('    <male_names>')
    for i, (en_n, zh_n) in enumerate(MALE_NAMES):
        out.append('      <name name="%s"/>' % esc(L("RDR2_malename_neutral_%d" % i, en_n, zh_n)))
    out.append('    </male_names>')
    out.append('    <female_names>')
    for i, (en_n, zh_n) in enumerate(FEMALE_NAMES):
        out.append('      <name name="%s"/>' % esc(L("RDR2_femalename_neutral_%d" % i, en_n, zh_n)))
    out.append('    </female_names>')
    out.append('    <basic_mercenary_troops><template name="NPCCharacter.rdr2w_mercenary"/></basic_mercenary_troops>')
    out.append('    <banner_bearer_replacement_weapons><item id="Item.aserai_sword_1_t2"/></banner_bearer_replacement_weapons>')
    out.append('  </Culture>')
    out.append('</SPCultures>')
    write(os.path.join(MODULE, "ModuleData", "spcultures.xml"), "\n".join(out) + "\n")


def gen_kingdoms():
    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<!-- 生成物：Scripts/gen_reddeadredemption2_world.py → 禁止手改 -->',
           '<Kingdoms>']
    name = L("RDR2_kingdom_lemoyne", KINGDOM_EN, KINGDOM_ZH)
    out.append('  <Kingdom id="%s" owner="Hero.%s" banner_key="%s"' % (KINGDOM_ID, RULER_HERO, BANNER_KEYS[0]))
    out.append('           primary_banner_color="0xff7a94d0" secondary_banner_color="0xff1d2c53"')
    out.append('           label_color="FF5573BE" color="FF5573BE" color2="FFDE9953"')
    out.append('           alternative_color="FFCBC25D" alternative_color2="FF5D6347"')
    out.append('           culture="Culture.%s" settlement_banner_mesh="encounter_flag_a"' % CULTURE)
    out.append('           flag_mesh="info_screen_flags_a"')
    out.append('           name="%s" short_name="%s" title="%s"' % (name, name, name))
    out.append('           ruler_title="%s"' % esc(L("RDR2_ruler_title", "Ruler", "统治者")))
    out.append('           text="%s">' % L("RDR2_kingdom_text", KINGDOM_EN, KINGDOM_ZH))
    out.append('  </Kingdom>')
    out.append('</Kingdoms>')
    write(os.path.join(MODULE, "ModuleData", "spkingdoms.xml"), "\n".join(out) + "\n")


def gen_clans():
    # 🔴 1.2.12 的 Faction XSD：**没有 title 属性**、**不接受 <Influence> 子元素**（只有 relationship /
    #    minor_faction_character_templates）。带上去 = 一堆 schema 报错，影响加载。
    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<!-- 生成物：Scripts/gen_reddeadredemption2_world.py → 禁止手改 -->',
           '<Factions>',
           '  <Faction id="%s" is_noble="true" owner="Hero.main_hero" banner_key="%s" is_minor_faction="false" label_color="FFD2C0AA" color="FF8D5C44" color2="FFE9A74D" alternative_color="FF6C5749" alternative_color2="FFB3A491" culture="Culture.%s" settlement_banner_mesh="encounter_flag_a" name="%s" tier="0" />'
           % (FACTION_PLAYER_ID, BANNER_KEYS[1], CULTURE, L("RDR2_player_faction", PLAYER_FACTION_EN, PLAYER_FACTION_ZH))]
    name = L("RDR2_clan_bronte", CLAN_EN, CLAN_ZH)
    out.append('  <Faction id="%s" is_noble="true" owner="Hero.%s" banner_key="%s" is_minor_faction="false" super_faction="Kingdom.%s" culture="Culture.%s" settlement_banner_mesh="encounter_flag_a" name="%s" short_name="%s" tier="3" />'
               % (CLAN_ID, RULER_HERO, BANNER_KEYS[2], KINGDOM_ID, CULTURE, name, name))
    out.append('</Factions>')
    write(os.path.join(MODULE, "ModuleData", "spclans.xml"), "\n".join(out) + "\n")


def gen_heroes():
    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<!-- 生成物：Scripts/gen_reddeadredemption2_world.py → 禁止手改 -->',
           '<Heroes>',
           '  <Hero id="main_hero" faction="Faction.%s" text="%s"/>' % (FACTION_PLAYER_ID, L("RDR2_main_hero", "Arthur Morgan", "亚瑟·摩根"))]
    lords = ['<?xml version="1.0" encoding="utf-8"?>',
             '<!-- 生成物：Scripts/gen_reddeadredemption2_world.py → 禁止手改 -->',
             '<NPCCharacters>']
    for hero_id, en, zh, age in HEROES:
        name = L("RDR2_hero_" + hero_id, en, zh)
        out.append('  <Hero id="%s" faction="Faction.%s" text="%s"/>' % (hero_id, CLAN_ID, name))
        # 同名 NPCCharacter 模板（雷 54：缺 = 英雄被引擎静默吞掉）
        lords.append('  <NPCCharacter id="%s" default_group="Infantry" age="%d" voice="curt" is_hero="true" is_female="false" culture="Culture.%s" name="%s" occupation="Lord" banner_symbol_mesh_name="test_symbol_a" banner_symbol_color="FF000000">'
                     % (hero_id, age, CULTURE, name))
        lords.append('    <face><BodyProperties version="3" age="%.1f" weight="0.5000" build="0.5000" key="%s" /></face>' % (float(age), DEFAULT_FACE_KEY))
        lords.append('    <Equipments>')
        lords.append('      <EquipmentRoster>')
        lords.append('        <equipment slot="Item0" id="Item.aserai_sword_1_t2" />')
        lords.append('        <equipment slot="Body" id="Item.tunic_with_shoulder_pads" />')
        lords.append('        <equipment slot="Leg" id="Item.leather_boots" />')
        lords.append('      </EquipmentRoster>')
        lords.append('      <EquipmentRoster civilian="true">')
        lords.append('        <equipment slot="Body" id="Item.tunic_with_shoulder_pads" />')
        lords.append('        <equipment slot="Leg" id="Item.leather_boots" />')
        lords.append('      </EquipmentRoster>')
        lords.append('    </Equipments>')
        lords.append('  </NPCCharacter>')
    out.append('</Heroes>')
    lords.append('</NPCCharacters>')
    write(os.path.join(MODULE, "ModuleData", "rdr2w_heroes.xml"), "\n".join(out) + "\n")
    write(os.path.join(MODULE, "ModuleData", "rdr2w_lords.xml"), "\n".join(lords) + "\n")


def gen_settlements():
    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<!-- 生成物：Scripts/gen_reddeadredemption2_world.py → 禁止手改 -->',
           '<Settlements>']
    for s in SETTLEMENTS:
        name = L("RDR2_sett_" + s["id"], s["en"], s["zh"])
        out.append('  <Settlement id="%s" name="%s" owner="Faction.%s" posX="%.1f" posY="%.1f" culture="Culture.%s">'
                   % (s["id"], name, CLAN_ID, s["x"], s["y"], CULTURE))
        out.append('    <Components>')
        if s["kind"] == "town":
            # 🔴 1.2.12 的 Town 没有 max_prosperity 属性（XSD 只认 prosperity）
            out.append('      <Town id="town_comp_sdn" is_castle="false" level="3" background_crop_position="0.0" background_mesh="menu_empire_4" wait_mesh="wait_empire_town" gate_rotation="0.208" prosperity="4500" />')
        else:
            out.append('      <Village id="village_comp_lag" village_type="VillageType.cattle_farm" hearth="120" gate_rotation="0.008" bound="Settlement.%s" background_crop_position="0.0" background_mesh="gui_bg_village_empire" wait_mesh="wait_empire_village" castle_background_mesh="gui_bg_castle_empire" />'
                       % s["bound"])
        out.append('    </Components>')
        if s["kind"] == "town":
            out.append('    <Locations complex_template="LocationComplexTemplate.town_complex">')
            out.append('      <Location id="center" scene_name="%s" scene_name_1="%s" scene_name_2="%s" scene_name_3="%s" />'
                       % (s["scene"], s["scene"], s["scene"], s["scene"]))
            out.append('      <Location id="arena" scene_name="arena_empire_a" />')
            out.append('      <Location id="tavern" scene_name="empire_house_c_tavern_a" />')
            out.append('    </Locations>')
            out.append('    <CommonAreas>')
            for area, zh in (("Backstreet", "后巷"), ("Clearing", "空地"), ("Waterfront", "滨水区")):
                out.append('      <Area type="%s" name="%s" />' % (area, L("RDR2_area_" + area.lower(), area, zh)))
            out.append('    </CommonAreas>')
        else:
            out.append('    <Locations complex_template="LocationComplexTemplate.village_complex">')
            out.append('      <Location id="village_center" scene_name="%s" />' % s["scene"])
            out.append('    </Locations>')
            out.append('    <CommonAreas>')
            for area, zh in (("Pasture", "牧场"), ("Thicket", "灌木丛"), ("Bog", "沼泽")):
                out.append('      <Area type="%s" name="%s" />' % (area, L("RDR2_area_" + area.lower(), area, zh)))
            out.append('    </CommonAreas>')
        out.append('  </Settlement>')

    # 退休营地：引擎硬编码查找（雷 31）——元素名必须是 RetirementSettlementComponent（1.2.12 XSD）
    out.append('  <Settlement id="retirement_retreat" name="%s" posX="424.0" posY="424.0" culture="Culture.neutral_culture">'
               % L("RDR2_sett_retirement_retreat", "The Retreat", "退休营地"))
    out.append('    <Components>')
    out.append('      <RetirementSettlementComponent id="retirement_component" map_icon="bandit_hideout_b" background_crop_position="0.0" background_mesh="gui_bg_village_battania" wait_mesh="retirement_wait" />')
    out.append('    </Components>')
    out.append('    <Locations complex_template="LocationComplexTemplate.retreat_complex">')
    out.append('      <Location id="retirement_retreat" scene_name="scn_retirement" max_prosperity="100" />')
    out.append('    </Locations>')
    out.append('  </Settlement>')
    out.append('</Settlements>')
    write(os.path.join(MODULE, "ModuleData", "settlements.xml"), "\n".join(out) + "\n")


def gen_party_templates():
    cid = CULTURE
    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<!-- 生成物：Scripts/gen_reddeadredemption2_world.py → 禁止手改 -->',
           '<partyTemplates>',
           '  <MBPartyTemplate id="main_hero_party_template"><stacks><PartyTemplateStack min_value="1" max_value="1" troop="NPCCharacter.main_hero" /></stacks></MBPartyTemplate>',
           '  <MBPartyTemplate id="militia_template"><stacks><PartyTemplateStack min_value="4" max_value="6" troop="NPCCharacter.guard" /></stacks></MBPartyTemplate>',
           '  <MBPartyTemplate id="%s_lord_party_template"><stacks>' % cid,
           '    <PartyTemplateStack min_value="20" max_value="20" troop="%s" />' % troop_id("recruit"),
           '    <PartyTemplateStack min_value="8" max_value="8" troop="%s" />' % troop_id("soldier"),
           '    <PartyTemplateStack min_value="10" max_value="10" troop="%s" />' % troop_id("archer"),
           '    <PartyTemplateStack min_value="3" max_value="3" troop="%s" />' % troop_id("veteran"),
           '  </stacks></MBPartyTemplate>',
           '  <MBPartyTemplate id="%s_militia_party_template"><stacks><PartyTemplateStack min_value="10" max_value="10" troop="%s" /></stacks></MBPartyTemplate>' % (cid, troop_id("militia")),
           '  <MBPartyTemplate id="%s_villager_party_template"><stacks><PartyTemplateStack min_value="10" max_value="10" troop="NPCCharacter.villager" /></stacks></MBPartyTemplate>' % cid,
           '  <MBPartyTemplate id="%s_caravan_party_template"><stacks><PartyTemplateStack min_value="12" max_value="12" troop="NPCCharacter.%s_caravan_guard" /><PartyTemplateStack min_value="4" max_value="4" troop="NPCCharacter.caravan_master" /></stacks></MBPartyTemplate>' % (cid, cid),
           '  <MBPartyTemplate id="%s_elite_caravan_party_template"><stacks><PartyTemplateStack min_value="16" max_value="16" troop="NPCCharacter.%s_caravan_guard" /><PartyTemplateStack min_value="4" max_value="4" troop="NPCCharacter.caravan_master" /></stacks></MBPartyTemplate>' % (cid, cid),
           '  <MBPartyTemplate id="%s_rebels_party_template"><stacks><PartyTemplateStack min_value="12" max_value="12" troop="%s" /></stacks></MBPartyTemplate>' % (cid, troop_id("soldier")),
           '  <MBPartyTemplate id="%s_bandit_party_template"><stacks><PartyTemplateStack min_value="12" max_value="12" troop="%s" /></stacks></MBPartyTemplate>' % (cid, troop_id("recruit")),
           '  <MBPartyTemplate id="%s_vassal_party_template"><stacks><PartyTemplateStack min_value="6" max_value="6" troop="%s" /></stacks></MBPartyTemplate>' % (cid, troop_id("soldier")),
           # neutral_culture 别名
           '  <MBPartyTemplate id="rdr2w_lord_party_template"><stacks><PartyTemplateStack min_value="10" max_value="10" troop="NPCCharacter.rdr2w_mercenary" /></stacks></MBPartyTemplate>']
    for suffix in ("militia", "villager", "caravan", "elite_caravan", "rebels", "bandit", "vassal"):
        out.append('  <MBPartyTemplate id="rdr2w_%s_party_template"><stacks><PartyTemplateStack min_value="8" max_value="8" troop="NPCCharacter.rdr2w_mercenary" /></stacks></MBPartyTemplate>' % suffix)
    out.append('</partyTemplates>')
    write(os.path.join(MODULE, "ModuleData", "partyTemplates.xml"), "\n".join(out) + "\n")


def gen_troops():
    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<!-- 生成物：Scripts/gen_reddeadredemption2_world.py → 禁止手改 -->',
           '<NPCCharacters>']

    def troop(tid, level, group, is_basic, upgrade, culture, name_en, name_zh, items):
        skill = "infantry_heavyinfantry_level%d_template_skills" % (6 if level < 10 else (11 if level < 20 else 21))
        out.append('  <NPCCharacter id="%s" default_group="%s" level="%d" name="%s" occupation="Soldier" is_basic_troop="%s" culture="Culture.%s" skill_template="SkillSet.%s">'
                   % (tid, group, level, L("RDR2_troop_" + tid, name_en, name_zh), "true" if is_basic else "false", culture, skill))
        out.append('    <face><face_key_template value="BodyProperty.fighter_empire" /></face>')
        out.append('    <upgrade_targets>')
        if upgrade:
            out.append('      <upgrade_target id="NPCCharacter.%s" />' % upgrade)
        out.append('    </upgrade_targets>')
        out.append('    <Equipments>')
        out.append('      <EquipmentRoster>')
        for slot, item in items:
            out.append('        <equipment slot="%s" id="Item.%s" />' % (slot, item))
        out.append('      </EquipmentRoster>')
        out.append('    </Equipments>')
        out.append('  </NPCCharacter>')

    cid, zh, en = CULTURE, CULTURE_ZH, CULTURE_EN
    troop("%s_recruit" % cid, 6, "Infantry", True, "%s_soldier" % cid, cid,
          "%s Recruit" % en, "%s新兵" % zh,
          [("Item0", "aserai_sword_1_t2"), ("Body", "tunic_with_shoulder_pads"), ("Leg", "leather_boots")])
    troop("%s_soldier" % cid, 16, "Infantry", False, "%s_veteran" % cid, cid,
          "%s Soldier" % en, "%s士兵" % zh,
          [("Item0", "aserai_sword_1_t2"), ("Body", "tunic_with_shoulder_pads"), ("Leg", "leather_boots")])
    troop("%s_veteran" % cid, 26, "Infantry", False, None, cid,
          "%s Veteran" % en, "%s老兵" % zh,
          [("Item0", "aserai_sword_1_t2"), ("Body", "tunic_with_shoulder_pads"), ("Leg", "leather_boots")])
    troop("%s_militia" % cid, 4, "Infantry", True, None, cid,
          "%s Militia" % en, "%s民兵" % zh,
          [("Item0", "aserai_sword_1_t2"), ("Body", "tunic_with_shoulder_pads"), ("Leg", "leather_boots")])
    troop("%s_archer" % cid, 8, "Ranged", True, "%s_sharpshooter" % cid, cid,
          "%s Archer" % en, "%s弓手" % zh,
          [("Item0", "hunting_bow"), ("Item1", "default_arrows"), ("Body", "tunic_with_shoulder_pads"), ("Leg", "leather_boots")])
    troop("%s_sharpshooter" % cid, 18, "Ranged", False, None, cid,
          "%s Sharpshooter" % en, "%s神射手" % zh,
          [("Item0", "hunting_bow"), ("Item1", "default_arrows"), ("Body", "tunic_with_shoulder_pads"), ("Leg", "leather_boots")])
    troop("rdr2w_mercenary", 10, "Infantry", False, None, "neutral_culture",
          "Mercenary", "佣兵",
          [("Item0", "aserai_sword_1_t2"), ("Body", "tunic_with_shoulder_pads"), ("Leg", "leather_boots")])
    out.append('</NPCCharacters>')
    write(os.path.join(MODULE, "ModuleData", "spnpccharacters.xml"), "\n".join(out) + "\n")


def gen_culture_text_variants():
    """17 个无 .default 的文化变体族（check_culture_text_variants 点名），逐条补。"""
    cid = CULTURE
    desc_en, desc_zh = "The warm bayous and plantations of Lemoyne.", "莱莫恩的温暖沼泽与种植园。"
    variants = {
        "str_culture_rich_name." + cid: (CULTURE_EN, CULTURE_ZH),
        "str_culture_description." + cid: (desc_en, desc_zh),
        "str_adjective_for_culture." + cid: (CULTURE_EN, CULTURE_ZH + "的"),
        "str_adjective_for_faction." + cid: (CULTURE_EN, CULTURE_ZH + "的"),
        "str_short_term_for_faction." + cid: (CULTURE_EN, CULTURE_ZH),
        "str_neutral_term_for_culture." + cid: ("local", "本地人"),
        "str_faction_formal_name_for_culture." + cid: ("People of " + CULTURE_EN, CULTURE_ZH + "人"),
        "str_faction_informal_name_for_culture." + cid: (CULTURE_EN + " folk", CULTURE_ZH + "人"),
        "str_faction_noble_name_with_title." + cid: ("Noble of " + CULTURE_EN, CULTURE_ZH + "贵族"),
        "str_faction_ruler_name_with_title." + cid: ("Ruler of " + CULTURE_EN, CULTURE_ZH + "的统治者"),
        "str_faction_ruler_term_in_speech." + cid: ("boss", "头儿"),
        "str_player_father_name." + cid: ("father", "父亲"),
        "str_player_mother_name." + cid: ("mother", "母亲"),
        "str_liege_title." + cid: ("Lord", "领主"),
        "str_liege_title_female." + cid: ("Lady", "女领主"),
        "str_faction_official." + cid: ("official", "官员"),
        "str_faction_official." + cid + "_f": ("official", "女官员"),
        "str_faction_ruler." + cid: ("Ruler", "统治者"),
        "str_faction_ruler." + cid + "_f": ("Ruler", "女统治者"),
    }
    for k, v in variants.items():
        STRINGS[k] = v
    STRINGS["str_culture_rich_name.neutral_culture"] = ("Neutral", "中立")
    STRINGS["str_culture_description.neutral_culture"] = ("No allegiance.", "无归属。")
    STRINGS["str_adjective_for_culture.neutral_culture"] = ("neutral", "中立的")


def gen_strings():
    # 🔴 两种文件格式不一样（2026-11-11 实机抓到的 schema 错）：
    #   ModuleData 的 GameText 段（本包注册给引擎）= 根元素 **<strings>**，没有 <base>/<tags>；
    #   Languages/ 下的语言文件 = <base type="string"><tags/><strings/>。
    body = "\n".join('  <string id="%s" text="%s" />' % (k, esc(STRINGS[k][0])) for k in sorted(STRINGS))
    write(os.path.join(MODULE, "ModuleData", "rdr2w_strings.xml"),
          '<?xml version="1.0" encoding="utf-8"?>\n<strings>\n' + body + '\n</strings>\n')
    write(os.path.join(MODULE, "ModuleData", "Languages", "std_RedDeadRedemption2_strings.xml"),
          '<?xml version="1.0" encoding="utf-8"?>\n<base type="string">\n  <tags><tag language="English" /></tags>\n  <strings>\n'
          + "\n".join('    <string id="%s" text="%s" />' % (k, esc(STRINGS[k][0])) for k in sorted(STRINGS))
          + '\n  </strings>\n</base>\n')
    write(os.path.join(MODULE, "ModuleData", "Languages", "CNs", "std_RedDeadRedemption2_strings.xml"),
          '<?xml version="1.0" encoding="utf-8"?>\n<base type="string">\n  <tags><tag language="简体中文" /></tags>\n  <strings>\n'
          + "\n".join('    <string id="%s" text="%s" />' % (k, esc(STRINGS[k][1])) for k in sorted(STRINGS))
          + '\n  </strings>\n</base>\n')
    write(os.path.join(MODULE, "ModuleData", "Languages", "language_data.xml"),
          '<?xml version="1.0" encoding="utf-8"?>\n'
          '<LanguageData id="English" name="English" subtitle_extension="en-GB" supported_iso="en-GB,en-US,en,eng,en-us,en-gb,en-au,en-bz,en-ca,en-ie,en-jm,en-nz,en-za,en-tt" text_processor="TaleWorlds.Localization.TextProcessor.LanguageProcessors.EnglishTextProcessor" under_development="false">\n'
          '  <LanguageFile xml_path="std_RedDeadRedemption2_strings.xml" />\n'
          '</LanguageData>\n')
    write(os.path.join(MODULE, "ModuleData", "Languages", "CNs", "language_data.xml"),
          '<?xml version="1.0" encoding="utf-8"?>\n'
          '<LanguageData id="简体中文" name="简体中文" subtitle_extension="zh-HANS" supported_iso="zh-HANS,zh,zho,chi,zh-cn,zh-sg" under_development="false">\n'
          '  <LanguageFile xml_path="CNs/std_RedDeadRedemption2_strings.xml" />\n'
          '</LanguageData>\n')


def extract_element(path, tag, elem_id):
    tree = ET.parse(path)
    for el in tree.getroot().iter(tag):
        if el.get("id") == elem_id:
            return el
    return None


def gen_characters():
    """main_hero 与地痞三档：官方定义在本包重注册（雷 7 / 雷 40）。"""
    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<!-- 生成物：Scripts/gen_reddeadredemption2_world.py → 禁止手改；内容取自官方定义，仅换文化/名字 -->',
           '<NPCCharacters>']

    main = extract_element(os.path.join(MODULES, "SandBox", "ModuleData", "lords.xml"), "NPCCharacter", "main_hero")
    if main is not None:
        main.set("culture", "Culture." + CULTURE)
        main.set("name", "{=RDR2_main_hero}Arthur Morgan")
        out.append(ET.tostring(main, encoding="unicode"))
    else:
        print("  [warn] main_hero not found")

    npc_path = os.path.join(MODULES, "SandBoxCore", "ModuleData", "spnpccharacters.xml")
    for gid in ("gangster_1", "gangster_2", "gangster_3"):
        el = extract_element(npc_path, "NPCCharacter", gid)
        if el is not None:
            el.set("culture", "Culture.neutral_culture")
            out.append(ET.tostring(el, encoding="unicode"))
        else:
            print("  [warn] %s not found" % gid)

    # 引擎硬编码点名的基础角色 id（雷 6/7/15/16/19/40）
    base_roles = [
        ("peasant_farmer", "Peasant Farmer", "农民", "Soldier", False, 1),
        ("villager", "Villager", "村民", "Villager", False, 1),
        ("townsman", "Townsman", "市民", "Townsfolk", False, 1),
        ("townswoman", "Townswoman", "女市民", "Townsfolk", True, 1),
        ("merchant", "Merchant", "商人", "Merchant", False, 1),
        ("artisan", "Artisan", "工匠", "Artisan", False, 2),
        ("caravan_master", "Caravan Master", "商队主", "CaravanGuard", False, 1),
        ("armed_trader", "Armed Trader", "武装商人", "GoodsTrader", False, 1),
        ("caravan_guard", "Caravan Guard", "商队护卫", "CaravanGuard", False, 26),
        ("gang_leader", "Gang Leader", "帮派头目", "GangLeader", False, 3),
    ]
    for rid, en, zh, occ, female, level in base_roles:
        out.append('  <NPCCharacter id="%s" default_group="Infantry" level="%d" is_female="%s" culture="Culture.neutral_culture" name="%s" occupation="%s">'
                   % (rid, level, "true" if female else "false", L("RDR2_npc_" + rid, en, zh), occ))
        out.append('    <face><face_key_template value="BodyProperty.villager_empire" /></face>')
        out.append('    <upgrade_targets />')
        out.append('    <Equipments><EquipmentRoster civilian="true" /><EquipmentRoster /></Equipments>')
        out.append('  </NPCCharacter>')

    # 每文化：Merchant / Artisan / 26 级步兵 CaravanGuard（引擎按文化查）
    for role, en_name, zh_name, occ, level in [
        ("merchant", CULTURE_EN + " Merchant", CULTURE_ZH + "商人", "Merchant", 1),
        ("artisan", CULTURE_EN + " Artisan", CULTURE_ZH + "工匠", "Artisan", 2),
        ("caravan_guard", CULTURE_EN + " Caravan Guard", CULTURE_ZH + "商队护卫", "CaravanGuard", 26),
    ]:
        rid = "%s_%s" % (CULTURE, role)
        out.append('  <NPCCharacter id="%s" default_group="Infantry" level="%d" is_female="false" culture="Culture.%s" name="%s" occupation="%s">'
                   % (rid, level, CULTURE, L("RDR2_npc_" + rid, en_name, zh_name), occ))
        out.append('    <face><face_key_template value="BodyProperty.villager_empire" /></face>')
        out.append('    <upgrade_targets />')
        out.append('    <Equipments><EquipmentRoster civilian="true" /><EquipmentRoster /></Equipments>')
        out.append('  </NPCCharacter>')

    out.append('</NPCCharacters>')
    write(os.path.join(MODULE, "ModuleData", "rdr2w_characters.xml"), "\n".join(out) + "\n")


def gen_submodule():
    gt = '<IncludedGameTypes><GameType value="%s"/></IncludedGameTypes>' % GAME_TYPE
    segs = [
        ("Items", "rdr2w_items"), ("SPCultures", "spcultures"),
        ("NPCCharacters", "rdr2w_npc_templates"),
        ("NPCCharacters", "spnpccharacters"),
        ("NPCCharacters", "rdr2w_lords"),
        ("NPCCharacters", "rdr2w_characters"),
        ("NPCCharacters", "rdr2w_roles"),
        ("partyTemplates", "partyTemplates"),
        ("Kingdoms", "spkingdoms"), ("Factions", "spclans"),
        ("WorkshopTypes", "spworkshops"), ("Settlements", "settlements"),
        ("BodyProperties", "rdr2w_bodyproperties"), ("SkillSets", "rdr2w_skill_sets"),
        ("EquipmentRosters", "rdr2w_equipment_sets"), ("Concepts", "rdr2w_concepts"),
        ("CraftingPieces", "rdr2w_crafting_pieces"), ("Heroes", "rdr2w_heroes"),
        ("LocationComplexTemplates", "rdr2w_location_complex_templates"),
        ("MusicInstruments", "rdr2w_music_instruments"),
        ("MusicTracks", "rdr2w_music_tracks"),
        ("GameText", "rdr2w_strings"),
        ("GameText", "module_strings"), ("GameText", "world_lore_strings"),
        ("GameText", "companion_strings"), ("GameText", "wanderer_strings"),
        ("GameText", "comment_strings"), ("GameText", "comment_on_action_strings"),
        ("GameText", "trait_strings"), ("GameText", "voice_strings"),
        ("GameText", "action_strings"),
    ]
    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<Module>',
           '  <Name value="%s"/>' % MODULE_ID,
           '  <Id value="%s"/>' % MODULE_ID,
           '  <Version value="e1.2.12"/>',
           '  <DefaultModule value="false"/>',
           '  <SinglePlayerModule value="true"/>',
           '  <ModuleCategory value="Singleplayer"/>',
           '  <Official value="false"/>',
           '  <DependedModules>',
           '    <DependedModule Id="Native"/>',
           '    <DependedModule Id="SandBoxCore"/>',
           '    <DependedModule Id="Sandbox"/>',
           '    <DependedModule Id="CustomBattle"/>',
           '    <DependedModule Id="StoryMode"/>',
           '    <DependedModule Id="LivingWorldNpcs"/>',
           '  </DependedModules>',
           '  <SubModules/>',
           '  <Xmls>']
    for typ, path in segs:
        out.append('    <XmlNode><XmlName id="%s" path="%s"/>%s</XmlNode>' % (typ, path, gt))
    out.append('  </Xmls>')
    out.append('</Module>')
    write(os.path.join(MODULE, "SubModule.xml"), "\n".join(out) + "\n")


# ──────────────────────────────────────────────────────────────
# 大地图场景（原版卡拉迪亚骨架 + 1 个据点胶囊）
# ──────────────────────────────────────────────────────────────

# 大地图源 = **Publish 出来的 RDR2 导出物**（`Modules\Publish\RDR2`）：
# 它带着编辑器导出的 navmesh、重新序列化的地形与场景（含我们注入的 border / 战役脚本实体 / 据点胶囊）。
# 🔴 为什么要拷进内容包：SettlementPositionScript 只从「场景所属模块」读 ModuleData/settlements.xml。
SRC_MODULE = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\Publish\RDR2"
SRC_SCENE = os.path.join(SRC_MODULE, "SceneObj", "Main_map")
DST_SCENE = os.path.join(MODULE, "SceneObj", "Main_map")
SCENE_FILES = ("terrain.bin", "navmesh.bin", "atmosphere.xml", "flora.bin", "references.txt")


def capsule_xml(idx, s, z):
    sid = s["id"]
    if s["kind"] == "town":
        tag, mesh, scale, bo_scale, circle = "town", "mi_emp_keep_1", "1.000", "2.618", "0.936"
    else:
        tag, mesh, scale, bo_scale, circle = "village", "village_empire_1", "0.600", "1.323", "0.600"
    return f'''
		<game_entity name="campaign_icon_capsule_{idx:03d}_{sid}" old_prefab_name="" mobility="1">
			<transform position="{s['x']:.1f}, {s['y']:.1f}, {z:.3f}" rotation_euler="0.000, 0.000, 0.000"/>
			<scripts>
				<script name="Town Entity Manager">
					<variables>
						<variable name="Override Factor Color" value="false"/>
						<variable name="Factor Color" value="1.000, 1.000, 1.000, 1.000"/>
					</variables>
				</script>
			</scripts>
			<children>
				<game_entity name="{sid}" old_prefab_name="" mobility="1">
					<tags>
						<tag name="{tag}"/>
					</tags>
					<transform rotation_euler="0.000, 0.000, 0.000" scale="{scale}, {scale}, 1.000"/>
					<children>
						<game_entity name="map_icon" old_prefab_name="" mobility="1">
							<components>
								<meta_mesh_component name="{mesh}"/>
							</components>
						</game_entity>
						<game_entity name="bo_town" old_prefab_name="bo_town" mobility="1">
							<tags>
								<tag name="bo_town"/>
							</tags>
							<transform position="0.000, 0.000, 0.000" rotation_euler="0.000, 0.000, 0.000" scale="{bo_scale}, {bo_scale}, {bo_scale}"/>
							<physics shape="bo_sphere_collider">
								<body_flags>
									<body_flag name="only_collide_with_raycast"/>
								</body_flags>
							</physics>
						</game_entity>
						<game_entity name="gate_position" old_prefab_name="gate_position" mobility="1">
							<tags>
								<tag name="main_map_city_gate"/>
							</tags>
							<transform position="0.000, -2.000, 0.000" rotation_euler="0.000, 0.000, 0.000"/>
						</game_entity>
						<game_entity name="town_circle_decal" old_prefab_name="town_circle_decal" mobility="1">
							<tags>
								<tag name="map_settlement_circle"/>
							</tags>
							<transform position="0.000, 0.000, 2.600" rotation_euler="0.000, 0.000, 0.000" scale="{circle}, {circle}, 1.151"/>
							<components>
								<decal_component material="decal_city_circle_a" argument="1.000, 1.000, 0.000, 0.000"/>
							</components>
						</game_entity>
					</children>
				</game_entity>
			</children>
		</game_entity>
'''


def copy_prefabs():
    """把 RDR2 的 prefab 定义拷进内容包——地图场景里 58 个远景实体引用它们，
    不拷 = 运行时不加载 RDR2 模块就找不到 prefab。"""
    src = os.path.join(SRC_MODULE, "Prefabs")
    dst = os.path.join(MODULE, "Prefabs")
    if not os.path.isdir(src):
        print("  [skip] RDR2/Prefabs 不存在")
        return
    os.makedirs(dst, exist_ok=True)
    for name in sorted(os.listdir(src)):
        if not name.endswith(".xml"):
            continue
        shutil.copy2(os.path.join(src, name), os.path.join(dst, name))
        print("  copied prefab", name)


def gen_map_scene():
    os.makedirs(DST_SCENE, exist_ok=True)
    for name in SCENE_FILES:
        s = os.path.join(SRC_SCENE, name)
        d = os.path.join(DST_SCENE, name)
        if os.path.isfile(s):
            shutil.copy2(s, d)
            print("  copied scene file", name)

    tree = ET.parse(os.path.join(SRC_SCENE, "scene.xscene"))
    root = tree.getroot()
    ents = root.find("entities")
    if ents is None:
        raise RuntimeError("no <entities> in source scene")

    removed = 0
    for e in list(ents):
        n = e.get("name") or ""
        if n.startswith("campaign_icon_capsule_"):
            ents.remove(e)
            removed += 1
    print("  removed vanilla settlement capsules:", removed)

    for i, s in enumerate(SETTLEMENTS):
        ents.append(ET.fromstring(capsule_xml(i, s, s.get("z", CAPSULE_Z))))
        print("  added settlement capsule:", s["id"])

    out = os.path.join(DST_SCENE, "scene.xscene")
    tree.write(out, encoding="utf-8", xml_declaration=True)
    print("  wrote", out, os.path.getsize(out), "bytes")


def gen_town_scene():
    """把 RDR2 的圣丹尼斯场景拷进内容包，并补一个玩家出生点（该场景原本没有任何玩法标记）。"""
    src = os.path.join(SRC_MODULE, "SceneObj", TOWN_SCENE)
    dst = os.path.join(MODULE, "SceneObj", TOWN_SCENE)
    if not os.path.isdir(src):
        print("  [skip] town scene not found:", src)
        return
    os.makedirs(dst, exist_ok=True)
    for name in SCENE_FILES:
        s = os.path.join(src, name)
        if os.path.isfile(s):
            shutil.copy2(s, os.path.join(dst, name))
            print("  copied town scene file", name)

    tree = ET.parse(os.path.join(src, "scene.xscene"))
    root = tree.getroot()
    ents = root.find("entities")
    if ents is None:
        raise RuntimeError("town scene has no <entities>")
    for e in list(ents):
        if (e.get("name") or "") == "spawnpoint_player":
            ents.remove(e)
    x, y, z = TOWN_PLAYER_SPAWN
    frag = ('<game_entity name="spawnpoint_player" old_prefab_name="" mobility="1">'
            '<tags><tag name="spawnpoint_player"/></tags>'
            '<transform position="%.1f, %.1f, %.3f" rotation_euler="0.000, 0.000, 0.000"/>'
            '</game_entity>' % (x, y, z))
    ents.insert(0, ET.fromstring(frag))
    out = os.path.join(dst, "scene.xscene")
    tree.write(out, encoding="utf-8", xml_declaration=True)
    print("  wrote", out, os.path.getsize(out), "bytes (spawnpoint_player @ %.1f,%.1f,%.1f)" % (x, y, z))


def main():
    print("RedDeadRedemption2 minimal world generator")
    print("module:", MODULE)
    print("copy base files...")
    copy_base()
    print("generate cultures...")
    gen_cultures()
    print("generate kingdoms...")
    gen_kingdoms()
    print("generate clans...")
    gen_clans()
    print("generate heroes...")
    gen_heroes()
    print("generate settlements...")
    gen_settlements()
    print("generate party templates...")
    gen_party_templates()
    print("generate troops...")
    gen_troops()
    print("generate characters...")
    gen_characters()
    print("generate culture text variants / strings...")
    gen_culture_text_variants()
    gen_strings()
    print("generate SubModule.xml...")
    gen_submodule()
    print("generate map scene...")
    copy_prefabs()
    gen_map_scene()
    print("generate town scene...")
    gen_town_scene()
    print("done. strings:", len(STRINGS))


if __name__ == "__main__":
    main()
