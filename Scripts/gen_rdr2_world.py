#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""RD2 内容包 · 最小世界数据生成器（2026-10-24 建）

用途：从 RDR2 解包交接清单生成一个能启动的最小世界（文化/王国/家族/英雄/据点/兵种/模板/语言），
并拷贝+清洗官方基础数据文件。

数据源（只读）：
  D:/BrainMaker/大镖客2解包分析/export/levels/world/settlements_bannerlord.csv
  D:/BrainMaker/大镖客2解包分析/deliverable/handoff/settlements_world_v2.csv
  D:/BrainMaker/大镖客2解包分析/export/levels/world/npc_roster.csv

铁律：
  - 生成物禁止手改；改内容 = 改本脚本 / 改数据源，然后重跑（CLAUDE.md 铁律 22）。
  - 内容包数据只用本包前缀的文本键（RD2_），不引用 LWN_ 键（雷 57）。
  - 文化引用必须闭合在本包（不引用原版 8 文化）。
"""

import csv
import io
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

MODULES = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules"
MODULE = os.path.join(MODULES, "RD2")
RDR2 = r"D:\BrainMaker\大镖客2解包分析"

# ──────────────────────────────────────────────────────────────
# 世界模型
# ──────────────────────────────────────────────────────────────

# 6 个地区文化 + neutral_culture
CULTURES = [
    # id, 中文, English, color, color2
    ("rd2_ambarino", "安巴里诺", "Ambarino", "0xff5b7f8c", "0xffcfe6ea"),
    ("rd2_new_hanover", "新汉诺威", "New Hanover", "0xff8a6b3d", "0xffe8d6b0"),
    ("rd2_lemoyne", "莱莫恩", "Lemoyne", "0xff7a2f35", "0xffe3b7a8"),
    ("rd2_west_elizabeth", "西伊丽莎白", "West Elizabeth", "0xff3f6b4a", "0xffcfe3c3"),
    ("rd2_new_austin", "新奥斯汀", "New Austin", "0xffb06a2c", "0xfff0d0a8"),
    ("rd2_guarma", "瓜玛", "Guarma", "0xff2f7a6a", "0xffbfe6d8"),
]

CULTURE_SUFFIX = {
    "rd2_ambarino": "battania",
    "rd2_new_hanover": "empire",
    "rd2_lemoyne": "sturgia",
    "rd2_west_elizabeth": "vlandia",
    "rd2_new_austin": "aserai",
    "rd2_guarma": "khuzait",
}

# 据点 → 文化（v1 手工地理归属；后续可改数据表）
SET_CULTURE = {
    # Ambarino
    "col": "rd2_ambarino", "wap": "rd2_ambarino", "adl": "rd2_ambarino", "che": "rd2_ambarino",
    # New Hanover
    "ann": "rd2_new_hanover", "van": "rd2_new_hanover", "but": "rd2_new_hanover",
    "cor": "rd2_new_hanover", "old": "rd2_new_hanover", "eme": "rd2_new_hanover",
    "wal": "rd2_new_hanover", "dow": "rd2_new_hanover", "hso": "rd2_new_hanover",
    "roc": "rd2_new_hanover", "sod": "rd2_new_hanover", "abe": "rd2_new_hanover",
    # Lemoyne
    "sdn": "rd2_lemoyne", "rho": "rd2_lemoyne", "cal": "rd2_lemoyne", "bra": "rd2_lemoyne",
    "ora": "rd2_lemoyne", "lag": "rd2_lemoyne", "dewclm": "rd2_lemoyne", "sha": "rd2_lemoyne",
    "lon": "rd2_lemoyne", "lak": "rd2_lemoyne", "cle": "rd2_lemoyne",
    # West Elizabeth
    "bla": "rd2_west_elizabeth", "bee": "rd2_west_elizabeth", "man": "rd2_west_elizabeth",
    "str": "rd2_west_elizabeth", "mic": "rd2_west_elizabeth", "han": "rd2_west_elizabeth",
    "pro": "rd2_west_elizabeth", "sis": "rd2_west_elizabeth",
    # New Austin
    "arm": "rd2_new_austin", "tum": "rd2_new_austin", "rid": "rd2_new_austin",
    "fom": "rd2_new_austin", "gap": "rd2_new_austin", "twi": "rd2_new_austin",
    "mfr": "rd2_new_austin", "rat": "rd2_new_austin",
    # Guarma
    "agu": "rd2_guarma", "gua": "rd2_guarma",
}

# 势力 → 王国 id / 名称 / 文化 / 执政家族（中文名）
FACTIONS = {
    "US":   dict(id="kingdom_rd2_us",   zh="联邦秩序",       en="United States",     culture="rd2_new_hanover",      ruling="联邦军"),
    "COR":  dict(id="kingdom_rd2_cor",  zh="康沃尔工业帝国", en="Cornwall United",    culture="rd2_new_hanover",      ruling="康沃尔公司"),
    "LEM":  dict(id="kingdom_rd2_lem",  zh="莱莫恩庄园联盟", en="Lemoyne Planters",   culture="rd2_lemoyne",          ruling="勃朗特家族"),
    "VDL":  dict(id="kingdom_rd2_vdl",  zh="范德林德帮",     en="Van der Linde Gang", culture="rd2_new_hanover",      ruling="范德林德帮"),
    "OUT":  dict(id="kingdom_rd2_out",  zh="边陲匪帮联盟",   en="Frontier Outlaws",   culture="rd2_west_elizabeth",   ruling="奥德里斯科尔帮"),
    "WAP":  dict(id="kingdom_rd2_wap",  zh="瓦匹缇部落",     en="Wapiti Tribe",       culture="rd2_ambarino",         ruling="瓦匹缇部落"),
    "GUA":  dict(id="kingdom_rd2_gua",  zh="瓜玛军政府",     en="Guarma Junta",       culture="rd2_guarma",           ruling="富萨尔军政府"),
    "FREE": dict(id="kingdom_rd2_free", zh="自由市镇",       en="Free Towns",         culture="rd2_new_hanover",      ruling="范霍恩自由民"),
}

# 家族中文名 → 英文名 / slug
CLAN_INFO = {
    "康沃尔公司":       ("Cornwall Company", "cornwall"),
    "范霍恩自由民":     ("Van Horn Freeholders", "vanhorn"),
    "富萨尔军政府":     ("Fussar Junta", "fussar"),
    "瓜玛反抗军":       ("Guarma Rebels", "guarma_rebels"),
    "勃朗特家族":       ("Bronte Family", "bronte"),
    "格雷家族":         ("Gray Family", "gray"),
    "布莱斯韦特家族":   ("Braithwaite Family", "braithwaite"),
    "圣丹尼斯市政":     ("Saint Denis Municipality", "saintdenis"),
    "联邦军":           ("Federal Army", "federal_army"),
    "犰狳镇公所":       ("Armadillo Town Hall", "armadillo"),
    "瓦伦丁镇公所":     ("Valentine Town Hall", "valentine"),
    "草莓镇公所":       ("Strawberry Town Hall", "strawberry"),
    "风滚草镇公所":     ("Tumbleweed Town Hall", "tumbleweed"),
    "黑水镇公所":       ("Blackwater Town Hall", "blackwater"),
    "阿德勒家族":       ("Adler Family", "adler"),
    "麦克法兰家族":     ("MacFarlane Family", "macfarlane"),
    "波特家族":         ("Porter Family", "porter"),
    "中央铁路公司":     ("Central Railway", "railway"),
    "叉角羚牧场":       ("Pronghorn Ranch", "pronghorn"),
    "唐恩家族":         ("Downes Family", "downes"),
    "马斯顿家族":       ("Marston Family", "marston"),
    "曼萨尼塔贸易站":   ("Manzanita Trading Post", "manzanita"),
    "脊木农场":         ("Ridgewood Farm", "ridgewood"),
    "奥德里斯科尔帮":   ("O'Driscoll Gang", "odriscoll"),
    "德尔·洛博帮":      ("Del Lobo Gang", "dellobo"),
    "迈卡帮":           ("Micah's Gang", "micah"),
    "瓦匹缇部落":       ("Wapiti Tribe", "wapiti"),
    "范德林德帮":       ("Van der Linde Gang", "vdl"),
    "岔路酒馆自由民":   ("Rathskeller Freemen", "rathskeller"),
    "阿伯丁家族":       ("Aberdeen Family", "aberdeen"),
    "翡翠牧场":         ("Emerald Ranch", "emerald"),
    "西西卡监狱":       ("Sisika Penitentiary", "sisika"),
    "贝尔福家族":       ("Balfour Family", "balfour"),
    "范霍恩自由民代表": ("Van Horn Representative", "vanhorn_rep"),
}

# 未具名据点的“主人”展示名与英文
UNNAMED_OWNER_EN = {
    "切斯·波特（未具名）": "Chez Porter",
    "华莱士车站站长（未具名）": "Wallace Station Master",
    "叉角羚牧场主（未具名）": "Pronghorn Rancher",
    "双子岩营地（未具名）": "Twin Rocks Camper",
    "唐斯牧场主（未具名）": "Downes Rancher",
    "奥兰治种植园管事（未具名）": "Orange Plantation Overseer",
    "岔路酒馆店主（未具名）": "Rathskeller Innkeeper",
    "布彻河村村长（未具名）": "Butcher Creek Headman",
    "德尔·洛博帮首领（未具名）": "Del Lobo Leader",
    "拉格拉斯村长（未具名）": "Lagras Headman",
    "曼萨尼塔站长（未具名）": "Manzanita Station Master",
    "朗尼（未具名）": "Lonnie",
    "翡翠牧场主（未具名）": "Emerald Ranch Owner",
    "脊木农场主（未具名）": "Ridgewood Farmer",
    "草皮屋住户（未具名）": "Sod House Resident",
    "西西卡典狱长（未具名）": "Sisika Warden",
    "阿伯丁兄妹（未具名）": "Aberdeen Siblings",
    "露莓溪农户（未具名）": "Dewberry Creek Farmer",
    "麦克法兰牧场主（未具名）": "MacFarlane Rancher",
    "无（无治安）": "Van Horn Representative",
}

# 老地图文化引用 → 本包文化（拷贝官方文件时清洗用）
VANILLA_CULTURE_MAP = {
    "aserai": "rd2_new_austin",
    "battania": "rd2_ambarino",
    "empire": "rd2_new_hanover",
    "khuzait": "rd2_new_austin",
    "sturgia": "rd2_ambarino",
    "vlandia": "rd2_west_elizabeth",
    "ikoku": "rd2_new_hanover",
}

# 官方基础文件拷贝：(源模块, 源相对 ModuleData 路径, 目标相对 ModuleData 路径)
BASE_COPIES = [
    ("SandBoxCore", "spnpccharactertemplates.xml", "spnpccharactertemplates.xml"),
    ("SandBoxCore", "spnpccharacters.xml", "rd2_roles.xml"),
    ("SandBoxCore", "sandboxcore_bodyproperties.xml", "rd2_bodyproperties.xml"),
    ("SandBoxCore", "sandboxcore_equipment_sets.xml", "rd2_equipment_sets.xml"),
    ("SandBoxCore", "sandboxcore_skill_sets.xml", "rd2_skill_sets.xml"),
    ("SandBox", "location_complex_templates.xml", "rd2_location_complex_templates.xml"),
    ("SandBox", "spworkshops.xml", "spworkshops.xml"),
    ("SandBox", "settlement_tracks.xml", "rd2_music_tracks.xml"),
    ("SandBox", "settlement_track_instruments.xml", "rd2_music_instruments.xml"),
    ("Native", "crafting_pieces.xml", "rd2_crafting_pieces.xml"),
    ("Taikou", "taikou_concepts.xml", "rd2_concepts.xml"),
    # 9 个官方 GameText 段（SandBox 白名单外，必须自家注册）
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
    """登记一条本地化文本，返回数据 XML 里的 {=KEY}English 形式（fallback 做 XML 转义）。"""
    STRINGS[key] = (en, zh)
    return "{=%s}%s" % (key, esc(en))


def esc(s):
    return (s or "").replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace('"', "&quot;")


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with io.open(path, "w", encoding="utf-8-sig", newline="\n") as f:
        f.write(text)


def read_csv(path):
    with io.open(path, "r", encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


# ──────────────────────────────────────────────────────────────
# 读取数据源
# ──────────────────────────────────────────────────────────────

def load_world():
    bl = read_csv(os.path.join(RDR2, "export/levels/world/settlements_bannerlord.csv"))
    v2 = {r["code"]: r for r in read_csv(os.path.join(RDR2, "deliverable/handoff/settlements_world_v2.csv"))}
    roster = {r["name_zh"]: r["name_en"] for r in read_csv(os.path.join(RDR2, "export/levels/world/npc_roster.csv"))}

    rows = []
    for r in bl:
        code = r["code"]
        v = v2.get(code, {})
        x = float(v.get("world_x") or 0)
        y = float(v.get("world_y") or 0)
        # 世界米 → 大地图单位（848×848，1 单位 = 14.5 米）
        u = (x + 7684.0) / 14.5
        vv = (4100.0 - y) / 14.5
        btype = r["btype"]
        faction = r["faction"]
        clan = r["clan"]
        # 威拉德之息：独立家族（用户裁定，2026-10-24）
        if code == "roc":
            clan = "贝尔福家族"
        owner = r["owner"]
        rows.append(dict(
            code=code, zh=r["name_zh"], en=r["name_en"], btype=btype,
            faction=faction, clan=clan, owner=owner,
            bound_to=r.get("bound_to", ""),
            u=u, v=vv, x=x, y=y,
            size_w=float(r.get("size_w") or 0), size_d=float(r.get("size_d") or 0),
        ))
    return rows, roster


def slug_of_clan(zh):
    info = CLAN_INFO.get(zh)
    if info:
        return info[1]
    s = re.sub(r"[^a-z0-9]+", "_", zh.lower()).strip("_")
    return s or "clan"


def clan_en(zh):
    if zh in CLAN_INFO:
        return CLAN_INFO[zh][0]
    return zh


def settlement_id(row):
    t = row["btype"]
    if t == "town":
        return "town_" + row["code"]
    if t == "castle":
        return "castle_" + row["code"]
    if t == "hideout":
        return "hideout_" + row["code"]
    return "village_" + row["code"]


def settlement_type(row):
    return {"town": "Town", "castle": "Castle", "hideout": "Hideout"}.get(row["btype"], "Village")


# ──────────────────────────────────────────────────────────────
# 基础文件拷贝 + 文化清洗
# ──────────────────────────────────────────────────────────────

CULTURE_RE = re.compile(r"Culture\.([a-zA-Z0-9_]+)")


def sanitize_cultures(text):
    def repl(m):
        key = m.group(1)
        if key == "neutral_culture":
            return m.group(0)
        return "Culture." + VANILLA_CULTURE_MAP.get(key, "neutral_culture")
    return CULTURE_RE.sub(repl, text)


def copy_base():
    for src_mod, src_rel, dst_rel in BASE_COPIES:
        src = os.path.join(MODULES, src_mod, "ModuleData", src_rel)
        if not os.path.isfile(src):
            print("  [skip missing] %s" % src)
            continue
        with io.open(src, "r", encoding="utf-8-sig") as f:
            text = f.read()
        text = sanitize_cultures(text)
        if dst_rel == "rd2_roles.xml":
            # 地痞三档由 rd2_characters.xml 提供，这里删掉避免重复 id
            import xml.etree.ElementTree as _ET
            root = _ET.fromstring(text)
            for el in list(root):
                if el.get("id") in ("gangster_1", "gangster_2", "gangster_3"):
                    root.remove(el)
            text = _ET.tostring(root, encoding="unicode")
            text = '<?xml version="1.0" encoding="utf-8"?>\n' + text
        write(os.path.join(MODULE, "ModuleData", dst_rel), text)
        print("  copied %s -> %s" % (src_rel, dst_rel))

    # 官方物品库（整目录）
    src_items = os.path.join(MODULES, "SandBoxCore", "ModuleData", "items")
    dst_items = os.path.join(MODULE, "ModuleData", "rd2_items")
    for name in sorted(os.listdir(src_items)):
        if not name.endswith(".xml"):
            continue
        with io.open(os.path.join(src_items, name), "r", encoding="utf-8-sig") as f:
            text = f.read()
        text = sanitize_cultures(text)
        # 铁律 33：每个 <Item> 的 Flags 必须有 Civilian="true"（照抄原版会漏）
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
    eq_path = os.path.join(MODULE, "ModuleData", "rd2_equipment_sets.xml")
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

DEFAULT_FACE_KEY = "000fa92e90004202aced5d976886573d5d679585a376fdd605877a7764b8987c00000000000007520000037f0000000f00000037049140010000000000000000"
BANNER_KEYS = [
    "11.148.148.1536.1536.764.764.1.0.0.984.171.171.483.483.764.764.0.0.0",
    "11.156.75.1536.1536.764.764.1.0.0.890.116.116.483.483.764.764.0.0.0",
    "11.154.154.1536.1536.764.764.1.0.0.877.171.171.483.483.764.764.0.0.0",
    "11.146.146.1536.1536.764.764.1.0.0.984.171.171.483.483.764.764.0.0.0",
    "11.147.147.1536.1536.764.764.1.0.0.853.171.171.483.483.764.764.0.0.0",
    "11.154.116.1536.1536.768.768.1.0.0.984.171.171.483.483.764.764.0.0.0",
    "11.148.148.1536.1536.764.764.1.0.0.908.171.171.483.483.764.764.0.0.0",
    "11.156.75.1536.1536.764.764.1.0.0.984.116.116.483.483.764.764.0.0.0",
]


def culture_troop(cid, role):
    return "NPCCharacter.%s_%s" % (cid, role)


def gen_cultures(world):
    # 名字池：从 npc_roster 取英文名，中文也取；缺失用备用
    names_m = []
    names_f = []
    roster = {r["name_zh"]: r["name_en"] for r in read_csv(os.path.join(RDR2, "export/levels/world/npc_roster.csv"))}
    for zh in sorted(roster):
        en = roster[zh]
        if not en:
            continue
        if zh.endswith("夫人") or zh.endswith("小姐") or zh.endswith("女士"):
            names_f.append((en, zh))
        else:
            names_m.append((en, zh))
    if len(names_m) < 30:
        names_m += [("John", "约翰"), ("Bill", "比尔"), ("Charles", "查尔斯"), ("Javier", "哈维尔"),
                    ("Hosea", "何西阿"), ("Sean", "肖恩"), ("Lenny", "兰尼"), ("Kieran", "基兰")]
    if len(names_f) < 15:
        names_f += [("Sadie", "莎迪"), ("Abigail", "阿比盖尔"), ("Mary", "玛丽"), ("Tilly", "蒂莉"),
                    ("Karen", "卡伦"), ("Molly", "莫莉"), ("Susan", "苏珊"), ("Charlotte", "夏洛特")]

    clan_names = sorted(set(r["clan"] for r in world))

    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<!-- 生成物：Scripts/gen_rdr2_world.py → 禁止手改（铁律 22） -->',
           '<SPCultures>']
    all_cultures = list(CULTURES)
    for idx, (cid, zh, en, color, color2) in enumerate(all_cultures):
        name = L("RD2_culture_" + cid, en, zh)
        is_main = "true" if cid == "rd2_new_hanover" else "false"
        out.append('  <Culture id="%s" name="%s" color="%s" color2="%s"' % (cid, name, color, color2))
        out.append('           basic_troop="%s" elite_basic_troop="%s"' % (culture_troop(cid, "recruit"), culture_troop(cid, "soldier")))
        out.append('           melee_militia_troop="%s" ranged_militia_troop="%s"' % (culture_troop(cid, "militia"), culture_troop(cid, "archer")))
        out.append('           melee_elite_militia_troop="%s" ranged_elite_militia_troop="%s"' % (culture_troop(cid, "veteran"), culture_troop(cid, "sharpshooter")))
        out.append('           is_main_culture="%s" can_have_settlement="true" town_edge_number="16" prosperity_bonus="1"' % is_main)
        out.append('           faction_banner_key="%s"' % BANNER_KEYS[idx % len(BANNER_KEYS)])
        out.append('           default_face_key="%s"' % DEFAULT_FACE_KEY)
        out.append('           encounter_background_mesh="encounter_empire"')
        out.append('           default_party_template="PartyTemplate.%s_lord_party_template"' % cid)
        out.append('           militia_party_template="PartyTemplate.%s_militia_party_template"' % cid)
        out.append('           villager_party_template="PartyTemplate.%s_villager_party_template"' % cid)
        out.append('           caravan_party_template="PartyTemplate.%s_caravan_party_template"' % cid)
        out.append('           elite_caravan_party_template="PartyTemplate.%s_elite_caravan_party_template"' % cid)
        out.append('           rebels_party_template="PartyTemplate.%s_rebels_party_template"' % cid)
        out.append('           bandit_boss_party_template="PartyTemplate.%s_bandit_party_template"' % cid)
        out.append('           vassal_reward_party_template="PartyTemplate.%s_vassal_party_template"' % cid)
        out.append('           default_battle_equipment_roster="EquipmentRoster.neutral_battle_template"')
        out.append('           default_civilian_equipment_roster="EquipmentRoster.neutral_civilian_template"')
        out.append('           duel_preset_equipment_roster="EquipmentRoster.neutral_battle_template"')
        out.append('           board_game_type="SixAndEight"')
        # 角色模板：官方角色是「文化后缀」id（如 merchant_empire / armorer_battania），
        # 按本文化的对应原版文化取；不存在的角色用近似件（gear_dummy / weapon_practice）。
        suffix = CULTURE_SUFFIX[cid]
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
        for t in ["main_hero", cid + "_merchant", cid + "_artisan", "rural_notable_notary_" + suffix, "gangster_1"]:
            out.append('      <template name="NPCCharacter.%s"/>' % t)
        out.append('    </notable_and_wanderer_templates>')
        out.append('    <lord_templates />')
        out.append('    <rebellion_hero_templates />')
        out.append('    <basic_mercenary_troops>')
        out.append('      <template name="NPCCharacter.rd2_mercenary"/>')
        out.append('    </basic_mercenary_troops>')
        out.append('    <cultural_feats />')
        out.append('    <default_policies />')
        out.append('    <clan_names>')
        for cn in clan_names:
            en_cn = clan_en(cn)
            out.append('      <name name="%s"/>' % esc(L("RD2_clanname_%s" % slug_of_clan(cn), en_cn, cn)))
        out.append('    </clan_names>')
        out.append('    <male_names>')
        for en_n, zh_n in names_m[:120]:
            out.append('      <name name="%s"/>' % esc(L("RD2_malename_%s_%s" % (cid, re.sub(r"[^A-Za-z0-9]+", "", en_n) or "n"), en_n, zh_n)))
        out.append('    </male_names>')
        out.append('    <female_names>')
        for en_n, zh_n in names_f[:60]:
            out.append('      <name name="%s"/>' % esc(L("RD2_femalename_%s_%s" % (cid, re.sub(r"[^A-Za-z0-9]+", "", en_n) or "n"), en_n, zh_n)))
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

    # neutral_culture
    name = L("RD2_culture_neutral", "Neutral", "中立")
    out.append('  <Culture id="neutral_culture" name="%s"' % name)
    out.append('           basic_troop="NPCCharacter.rd2_mercenary" elite_basic_troop="NPCCharacter.rd2_mercenary"')
    out.append('           melee_militia_troop="NPCCharacter.guard" ranged_militia_troop="NPCCharacter.guard"')
    out.append('           melee_elite_militia_troop="NPCCharacter.guard" ranged_elite_militia_troop="NPCCharacter.guard"')
    out.append('           default_party_template="PartyTemplate.rd2_lord_party_template"')
    out.append('           militia_party_template="PartyTemplate.rd2_militia_party_template"')
    out.append('           villager_party_template="PartyTemplate.rd2_villager_party_template"')
    out.append('           caravan_party_template="PartyTemplate.rd2_caravan_party_template"')
    out.append('           elite_caravan_party_template="PartyTemplate.rd2_elite_caravan_party_template"')
    out.append('           rebels_party_template="PartyTemplate.rd2_rebels_party_template"')
    out.append('           bandit_boss_party_template="PartyTemplate.rd2_bandit_party_template"')
    out.append('           vassal_reward_party_template="PartyTemplate.rd2_vassal_party_template"')
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
    for cn in clan_names:
        out.append('      <name name="%s"/>' % esc(L("RD2_clanname_%s" % slug_of_clan(cn), clan_en(cn), cn)))
    out.append('    </clan_names>')
    out.append('    <male_names>')
    for en_n, zh_n in names_m[:80]:
        out.append('      <name name="%s"/>' % esc(L("RD2_malename_neutral_%s" % (re.sub(r"[^A-Za-z0-9]+", "", en_n) or "n"), en_n, zh_n)))
    out.append('    </male_names>')
    out.append('    <female_names>')
    for en_n, zh_n in names_f[:40]:
        out.append('      <name name="%s"/>' % esc(L("RD2_femalename_neutral_%s" % (re.sub(r"[^A-Za-z0-9]+", "", en_n) or "n"), en_n, zh_n)))
    out.append('    </female_names>')
    out.append('    <basic_mercenary_troops><template name="NPCCharacter.rd2_mercenary"/></basic_mercenary_troops>')
    out.append('    <banner_bearer_replacement_weapons><item id="Item.aserai_sword_1_t2"/></banner_bearer_replacement_weapons>')
    out.append('  </Culture>')
    out.append('</SPCultures>')
    write(os.path.join(MODULE, "ModuleData", "spcultures.xml"), "\n".join(out) + "\n")


def gen_kingdoms(world):
    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<!-- 生成物：Scripts/gen_rdr2_world.py → 禁止手改 -->',
           '<Kingdoms>']
    for i, (code, f) in enumerate(FACTIONS.items()):
        ruling_slug = slug_of_clan(f["ruling"])
        ruler = "lord_rd2_" + ruling_slug
        name = L("RD2_kingdom_" + code.lower(), f["en"], f["zh"])
        bk = BANNER_KEYS[i % len(BANNER_KEYS)]
        out.append('  <Kingdom id="%s" owner="Hero.%s" banner_key="%s"' % (f["id"], ruler, bk))
        out.append('           primary_banner_color="0xff7a94d0" secondary_banner_color="0xff1d2c53"')
        out.append('           label_color="FF5573BE" color="FF5573BE" color2="FFDE9953"')
        out.append('           alternative_color="FFCBC25D" alternative_color2="FF5D6347"')
        out.append('           culture="Culture.%s" settlement_banner_mesh="encounter_flag_a"' % f["culture"])
        out.append('           flag_mesh="info_screen_flags_a"')
        out.append('           name="%s" short_name="%s" title="%s"' % (name, name, name))
        out.append('           ruler_title="%s"' % esc(L("RD2_ruler_title_" + code.lower(), "Ruler", "统治者")))
        out.append('           text="%s">' % L("RD2_kingdom_text_" + code.lower(), f["en"], f["zh"]))
        out.append('  </Kingdom>')
    out.append('</Kingdoms>')
    write(os.path.join(MODULE, "ModuleData", "spkingdoms.xml"), "\n".join(out) + "\n")


def gen_clans(world):
    # 建立 clan → 文化 / 首个据点
    clans = {}
    for r in world:
        clans.setdefault(r["clan"], r)
    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<!-- 生成物：Scripts/gen_rdr2_world.py → 禁止手改 -->',
           '<Factions>',
           '  <Faction id="player_faction" is_noble="true" owner="Hero.main_hero" banner_key="%s" is_minor_faction="false" label_color="FFD2C0AA" color="FF8D5C44" color2="FFE9A74D" alternative_color="FF6C5749" alternative_color2="FFB3A491" culture="Culture.rd2_new_hanover" settlement_banner_mesh="encounter_flag_a" name="%s" tier="0">' % (BANNER_KEYS[0], L("RD2_player_faction", "Van der Linde Gang", "范德林德帮")),
           '    <Influence><base_influence value="30.0"/></Influence>',
           '  </Faction>']
    for i, (zh, first) in enumerate(sorted(clans.items())):
        slug = slug_of_clan(zh)
        fac = FACTIONS[first["faction"]]
        cult = SET_CULTURE.get(first["code"], fac["culture"])
        name = L("RD2_clan_" + slug, clan_en(zh), zh)
        out.append('  <Faction id="clan_rd2_%s" is_noble="true" owner="Hero.lord_rd2_%s" banner_key="%s" is_minor_faction="false" super_faction="Kingdom.%s" culture="Culture.%s" settlement_banner_mesh="encounter_flag_a" name="%s" short_name="%s" title="%s" tier="3">'
                   % (slug, slug, BANNER_KEYS[(i + 2) % len(BANNER_KEYS)], fac["id"], cult, name, name, name))
        out.append('    <Influence><base_influence value="60.0"/></Influence>')
        out.append('  </Faction>')
    out.append('</Factions>')
    write(os.path.join(MODULE, "ModuleData", "spclans.xml"), "\n".join(out) + "\n")


def owner_display(row):
    zh = row["owner"]
    if zh in UNNAMED_OWNER_EN:
        en = UNNAMED_OWNER_EN[zh]
        zh_clean = zh.replace("（未具名）", "").replace("（无治安）", "")
        if zh == "无（无治安）":
            zh_clean = "范霍恩自由民代表"
        return en, zh_clean
    return None, zh


def gen_heroes(world):
    clans = {}
    for r in world:
        clans.setdefault(r["clan"], r)
    roster = {r["name_zh"]: r["name_en"] for r in read_csv(os.path.join(RDR2, "export/levels/world/npc_roster.csv"))}
    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<!-- 生成物：Scripts/gen_rdr2_world.py → 禁止手改 -->',
           '<Heroes>',
           '  <Hero id="main_hero" faction="Faction.player_faction" text="%s"/>'
           % L("RD2_main_hero", "Arthur Morgan", "亚瑟·摩根")]
    lord_out = ['<?xml version="1.0" encoding="utf-8"?>',
                '<!-- 生成物：Scripts/gen_rdr2_world.py → 禁止手改 -->',
                '<NPCCharacters>']
    for zh_clan, first in sorted(clans.items()):
        slug = slug_of_clan(zh_clan)
        en, zh_name = owner_display(first)
        if en is None:
            en = roster.get(zh_name)
            if not en:
                en = "Leader of " + clan_en(zh_clan)
        name = L("RD2_hero_" + slug, en, zh_name)
        out.append('  <Hero id="lord_rd2_%s" faction="Faction.clan_rd2_%s" text="%s"/>' % (slug, slug, name))
        # 同名 NPCCharacter 模板（雷 54，缺 = 英雄被静默吞掉）
        cult = SET_CULTURE.get(first["code"], FACTIONS[first["faction"]]["culture"])
        lord_out.append('  <NPCCharacter id="lord_rd2_%s" default_group="Infantry" age="35" voice="curt" is_hero="true" is_female="false" culture="Culture.%s" name="%s" occupation="Lord" banner_symbol_mesh_name="test_symbol_a" banner_symbol_color="FF000000">' % (slug, cult, name))
        lord_out.append('    <face><BodyProperties version="3" age="35.0" weight="0.5000" build="0.5000" key="%s" /></face>' % DEFAULT_FACE_KEY)
        lord_out.append('    <Equipments>')
        lord_out.append('      <EquipmentRoster>')
        lord_out.append('        <equipment slot="Item0" id="Item.aserai_sword_1_t2" />')
        lord_out.append('        <equipment slot="Body" id="Item.tunic_with_shoulder_pads" />')
        lord_out.append('        <equipment slot="Leg" id="Item.leather_boots" />')
        lord_out.append('      </EquipmentRoster>')
        lord_out.append('      <EquipmentRoster civilian="true">')
        lord_out.append('        <equipment slot="Body" id="Item.tunic_with_shoulder_pads" />')
        lord_out.append('        <equipment slot="Leg" id="Item.leather_boots" />')
        lord_out.append('      </EquipmentRoster>')
        lord_out.append('    </Equipments>')
        lord_out.append('  </NPCCharacter>')
    out.append('</Heroes>')
    lord_out.append('</NPCCharacters>')
    write(os.path.join(MODULE, "ModuleData", "rd2_heroes.xml"), "\n".join(out) + "\n")
    write(os.path.join(MODULE, "ModuleData", "rd2_lords.xml"), "\n".join(lord_out) + "\n")


def nearest_settlement(world, row, kinds=("town", "castle")):
    best = None
    bestd = 1e18
    for o in world:
        if o is row or o["btype"] not in kinds:
            continue
        d = (o["x"] - row["x"]) ** 2 + (o["y"] - row["y"]) ** 2
        if d < bestd:
            bestd, best = d, o
    return best


def gen_settlements(world):
    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<!-- 生成物：Scripts/gen_rdr2_world.py → 禁止手改 -->',
           '<Settlements>']
    for r in world:
        sid = settlement_id(r)
        typ = settlement_type(r)
        cult = SET_CULTURE.get(r["code"], "rd2_new_hanover")
        owner = "Faction.clan_rd2_" + slug_of_clan(r["clan"])
        name = L("RD2_sett_" + r["code"], r["en"], r["zh"])
        type_attr = ' type="Hideout"' if typ == "Hideout" else ""
        out.append('  <Settlement id="%s" name="%s" owner="%s" posX="%.1f" posY="%.1f" culture="Culture.%s"%s>'
                   % (sid, name, owner, r["u"], r["v"], cult, type_attr))
        out.append('    <Components>')
        if typ == "Town":
            out.append('      <Town id="town_comp_%s" is_castle="false" level="3" background_crop_position="0.0" background_mesh="menu_empire_4" wait_mesh="wait_empire_town" gate_rotation="0.208" max_prosperity="70" prosperity="4500" />' % r["code"])
        elif typ == "Castle":
            out.append('      <Town id="castle_comp_%s" is_castle="true" level="2" background_crop_position="0.0" background_mesh="menu_empire_1" wait_mesh="wait_empire_town" gate_rotation="0.908" prosperity="700" />' % r["code"])
        elif typ == "Hideout":
            out.append('      <Hideout id="hideout_comp_%s" map_icon="bandit_hideout_b" background_crop_position="0.0" background_mesh="empire_twn_scene_bg" wait_mesh="wait_hideout_forest" gate_rotation="6.283" />' % r["code"])
        else:
            # 村庄 / 营地 / POI：绑定最近的城或堡
            bound = None
            if r["bound_to"]:
                for o in world:
                    if o["code"] == r["bound_to"]:
                        bound = o
                        break
            if bound is None:
                bound = nearest_settlement(world, r)
            bound_id = settlement_id(bound) if bound else ""
            hearth = 20 if r["btype"] == "poi" else (40 if r["btype"] == "camp" else 120)
            village_type = {"town": "VillageType.cattle_farm"}.get("", "VillageType.cattle_farm")
            if r["btype"] == "camp":
                village_type = "VillageType.lumberjack"
            out.append('      <Village id="village_comp_%s" village_type="%s" hearth="%d" gate_rotation="0.008" bound="Settlement.%s" background_crop_position="0.0" background_mesh="gui_bg_village_empire" wait_mesh="wait_empire_village" castle_background_mesh="gui_bg_castle_empire" />'
                       % (r["code"], village_type, hearth, bound_id))
        out.append('    </Components>')
        if typ == "Town":
            out.append('    <Locations complex_template="LocationComplexTemplate.town_complex">')
            out.append('      <Location id="center" scene_name="empire_town_g" scene_name_1="empire_town_g" scene_name_2="empire_town_g" scene_name_3="empire_town_g" />')
            out.append('      <Location id="arena" scene_name="arena_empire_a" />')
            out.append('      <Location id="tavern" scene_name="empire_house_c_tavern_a" />')
            out.append('    </Locations>')
            out.append('    <CommonAreas>')
            for area, key in (("Backstreet", "RD2_area_backstreet"), ("Clearing", "RD2_area_clearing"), ("Waterfront", "RD2_area_waterfront")):
                out.append('      <Area type="%s" name="%s" />' % (area, L(key, area, {"Backstreet": "后巷", "Clearing": "空地", "Waterfront": "滨水区"}[area])))
            out.append('    </CommonAreas>')
        elif typ == "Castle":
            out.append('    <Locations complex_template="LocationComplexTemplate.castle_complex">')
            out.append('      <Location id="center" scene_name="empire_siege_001" scene_name_1="empire_siege_001" scene_name_2="empire_siege_001" scene_name_3="empire_siege_001" />')
            out.append('      <Location id="lordshall" scene_name="empire_castle_keep_a_l1_interior" />')
            out.append('      <Location id="prison" scene_name="empire_dungeon_a" />')
            out.append('    </Locations>')
        elif typ == "Hideout":
            out.append('    <Locations complex_template="LocationComplexTemplate.hideout_complex">')
            out.append('      <Location id="hideout_center" scene_name="bandit_forest_sv" />')
            out.append('    </Locations>')
        else:
            out.append('    <Locations complex_template="LocationComplexTemplate.village_complex">')
            out.append('      <Location id="village_center" scene_name="empire_village_005" />')
            out.append('    </Locations>')
            out.append('    <CommonAreas>')
            for area, key in (("Pasture", "RD2_area_pasture"), ("Thicket", "RD2_area_thicket"), ("Bog", "RD2_area_bog")):
                out.append('      <Area type="%s" name="%s" />' % (area, L(key, area, {"Pasture": "牧场", "Thicket": "灌木丛", "Bog": "沼泽"}[area])))
            out.append('    </CommonAreas>')
        out.append('  </Settlement>')
    out.append('  <Settlement id="retirement_retreat" name="%s" posX="424.0" posY="424.0" culture="Culture.neutral_culture">' % L("RD2_sett_retirement_retreat", "The Retreat", "退休营地"))
    out.append('    <Components>')
    out.append('      <CustomSettlementComponent id="retirement_component" component_name="RetirementSettlementComponent" map_icon="bandit_hideout_b" background_crop_position="0.0" background_mesh="gui_bg_village_battania" wait_mesh="retirement_wait" />')
    out.append('    </Components>')
    out.append('    <Locations complex_template="LocationComplexTemplate.retreat_complex">')
    out.append('      <Location id="retirement_retreat" scene_name="scn_retirement" max_prosperity="100" />')
    out.append('    </Locations>')
    out.append('  </Settlement>')
    out.append('</Settlements>')
    write(os.path.join(MODULE, "ModuleData", "settlements.xml"), "\n".join(out) + "\n")


def gen_party_templates(world):
    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<!-- 生成物：Scripts/gen_rdr2_world.py → 禁止手改 -->',
           '<partyTemplates>',
           '  <MBPartyTemplate id="main_hero_party_template"><stacks><PartyTemplateStack min_value="1" max_value="1" troop="NPCCharacter.main_hero" /></stacks></MBPartyTemplate>',
           '  <MBPartyTemplate id="militia_template"><stacks><PartyTemplateStack min_value="4" max_value="6" troop="NPCCharacter.guard" /></stacks></MBPartyTemplate>']
    for cid, zh, en, _, _ in CULTURES:
        out.append('  <MBPartyTemplate id="%s_lord_party_template"><stacks>' % cid)
        out.append('    <PartyTemplateStack min_value="20" max_value="20" troop="%s" />' % culture_troop(cid, "recruit"))
        out.append('    <PartyTemplateStack min_value="8" max_value="8" troop="%s" />' % culture_troop(cid, "soldier"))
        out.append('    <PartyTemplateStack min_value="10" max_value="10" troop="%s" />' % culture_troop(cid, "archer"))
        out.append('    <PartyTemplateStack min_value="3" max_value="3" troop="%s" />' % culture_troop(cid, "veteran"))
        out.append('  </stacks></MBPartyTemplate>')
        out.append('  <MBPartyTemplate id="%s_militia_party_template"><stacks><PartyTemplateStack min_value="10" max_value="10" troop="%s" /></stacks></MBPartyTemplate>' % (cid, culture_troop(cid, "militia")))
        out.append('  <MBPartyTemplate id="%s_villager_party_template"><stacks><PartyTemplateStack min_value="10" max_value="10" troop="NPCCharacter.villager" /></stacks></MBPartyTemplate>' % cid)
        out.append('  <MBPartyTemplate id="%s_caravan_party_template"><stacks><PartyTemplateStack min_value="12" max_value="12" troop="NPCCharacter.%s_caravan_guard" /><PartyTemplateStack min_value="4" max_value="4" troop="NPCCharacter.caravan_master" /></stacks></MBPartyTemplate>' % (cid, cid))
        out.append('  <MBPartyTemplate id="%s_elite_caravan_party_template"><stacks><PartyTemplateStack min_value="16" max_value="16" troop="NPCCharacter.%s_caravan_guard" /><PartyTemplateStack min_value="4" max_value="4" troop="NPCCharacter.caravan_master" /></stacks></MBPartyTemplate>' % (cid, cid))
        out.append('  <MBPartyTemplate id="%s_rebels_party_template"><stacks><PartyTemplateStack min_value="12" max_value="12" troop="%s" /></stacks></MBPartyTemplate>' % (cid, culture_troop(cid, "soldier")))
        out.append('  <MBPartyTemplate id="%s_bandit_party_template"><stacks><PartyTemplateStack min_value="12" max_value="12" troop="%s" /></stacks></MBPartyTemplate>' % (cid, culture_troop(cid, "recruit")))
        out.append('  <MBPartyTemplate id="%s_vassal_party_template"><stacks><PartyTemplateStack min_value="6" max_value="6" troop="%s" /></stacks></MBPartyTemplate>' % (cid, culture_troop(cid, "soldier")))
    # neutral 别名
    out.append('  <MBPartyTemplate id="rd2_lord_party_template"><stacks><PartyTemplateStack min_value="10" max_value="10" troop="NPCCharacter.rd2_mercenary" /></stacks></MBPartyTemplate>')
    for suffix in ("militia", "villager", "caravan", "elite_caravan", "rebels", "bandit", "vassal"):
        out.append('  <MBPartyTemplate id="rd2_%s_party_template"><stacks><PartyTemplateStack min_value="8" max_value="8" troop="NPCCharacter.rd2_mercenary" /></stacks></MBPartyTemplate>' % suffix)
    out.append('</partyTemplates>')
    write(os.path.join(MODULE, "ModuleData", "partyTemplates.xml"), "\n".join(out) + "\n")


def gen_troops(world):
    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<!-- 生成物：Scripts/gen_rdr2_world.py → 禁止手改 -->',
           '<NPCCharacters>']
    def troop(tid, level, group, is_basic, upgrade, culture, name_en, name_zh, items, ranged=False, mount=None):
        skill = "infantry_heavyinfantry_level%d_template_skills" % (6 if level < 10 else (11 if level < 20 else 21))
        out.append('  <NPCCharacter id="%s" default_group="%s" level="%d" name="%s" occupation="Soldier" is_basic_troop="%s" culture="Culture.%s" skill_template="SkillSet.%s">'
                   % (tid, group, level, L("RD2_troop_" + tid, name_en, name_zh), "true" if is_basic else "false", culture, skill))
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

    for cid, zh, en, _, _ in CULTURES:
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
    # 佣兵（neutral 用）
    troop("rd2_mercenary", 10, "Infantry", False, None, "neutral_culture",
          "Mercenary", "佣兵",
          [("Item0", "aserai_sword_1_t2"), ("Body", "tunic_with_shoulder_pads"), ("Leg", "leather_boots")])
    out.append('</NPCCharacters>')
    write(os.path.join(MODULE, "ModuleData", "spnpccharacters.xml"), "\n".join(out) + "\n")


def gen_culture_text_variants():
    """17 个无 .default 的文化变体族（check_culture_text_variants 点名），每个文化逐条补。"""
    desc = {
        "rd2_ambarino": ("The highlands and pine forests of Ambarino.", "安巴里诺的高地与松林。"),
        "rd2_new_hanover": ("The wide plains and river valleys of New Hanover.", "新汉诺威的平原与河谷。"),
        "rd2_lemoyne": ("The warm bayous and plantations of Lemoyne.", "莱莫恩的温暖沼泽与种植园。"),
        "rd2_west_elizabeth": ("The wooded frontier of West Elizabeth.", "西伊丽莎白的森林边陲。"),
        "rd2_new_austin": ("The dry desert and mesas of New Austin.", "新奥斯汀的干旱沙漠与台地。"),
        "rd2_guarma": ("The tropical island of Guarma.", "瓜玛的热带岛屿。"),
    }
    for cid, zh, en, _, _ in CULTURES:
        d_en, d_zh = desc[cid]
        variants = {
            "str_culture_rich_name." + cid: (en, zh),
            "str_culture_description." + cid: (d_en, d_zh),
            "str_adjective_for_culture." + cid: (en, zh + "的"),
            "str_adjective_for_faction." + cid: (en, zh + "的"),
            "str_short_term_for_faction." + cid: (en, zh),
            "str_neutral_term_for_culture." + cid: ("local", "本地人"),
            "str_faction_formal_name_for_culture." + cid: ("People of " + en, zh + "人"),
            "str_faction_informal_name_for_culture." + cid: (en + " folk", zh + "人"),
            "str_faction_noble_name_with_title." + cid: ("Noble of " + en, zh + "贵族"),
            "str_faction_ruler_name_with_title." + cid: ("Ruler of " + en, zh + "的统治者"),
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
    # neutral_culture 也可能出现在建号/文本里（不参与文化变体检查，但补上不亏）
    STRINGS["str_culture_rich_name.neutral_culture"] = ("Neutral", "中立")
    STRINGS["str_culture_description.neutral_culture"] = ("No allegiance.", "无归属。")
    STRINGS["str_adjective_for_culture.neutral_culture"] = ("neutral", "中立的")


def gen_strings():
    # 数据里的 {=KEY}English 走 rd2_strings.xml（GameText 段）；语言层另出英文/中文两份。
    text = '<?xml version="1.0" encoding="utf-8"?>\n<base type="string">\n  <tags><tag language="English" /></tags>\n  <strings>\n'
    text += "\n".join('    <string id="%s" text="%s" />' % (k, esc(STRINGS[k][0])) for k in sorted(STRINGS))
    text += '\n  </strings>\n</base>\n'
    write(os.path.join(MODULE, "ModuleData", "rd2_strings.xml"), text)
    write(os.path.join(MODULE, "ModuleData", "Languages", "std_RD2_strings.xml"), text)
    write(os.path.join(MODULE, "ModuleData", "Languages", "CNs", "std_RD2_strings.xml"),
          '<?xml version="1.0" encoding="utf-8"?>\n<base type="string">\n  <tags><tag language="简体中文" /></tags>\n  <strings>\n'
          + "\n".join('    <string id="%s" text="%s" />' % (k, esc(STRINGS[k][1])) for k in sorted(STRINGS))
          + '\n  </strings>\n</base>\n')
    write(os.path.join(MODULE, "ModuleData", "Languages", "language_data.xml"),
          '<?xml version="1.0" encoding="utf-8"?>\n'
          '<LanguageData id="English" name="English" subtitle_extension="en-GB" supported_iso="en-GB,en-US,en,eng,en-us,en-gb,en-au,en-bz,en-ca,en-ie,en-jm,en-nz,en-za,en-tt" text_processor="TaleWorlds.Localization.TextProcessor.LanguageProcessors.EnglishTextProcessor" under_development="false">\n'
          '  <LanguageFile xml_path="std_RD2_strings.xml" />\n'
          '</LanguageData>\n')
    write(os.path.join(MODULE, "ModuleData", "Languages", "CNs", "language_data.xml"),
          '<?xml version="1.0" encoding="utf-8"?>\n'
          '<LanguageData id="简体中文" name="简体中文" subtitle_extension="zh-HANS" supported_iso="zh-HANS,zh,zho,chi,zh-cn,zh-sg" under_development="false">\n'
          '  <LanguageFile xml_path="CNs/std_RD2_strings.xml" />\n'
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
           '<!-- 生成物：Scripts/gen_rdr2_world.py → 禁止手改；内容取自官方定义，仅换文化/名字 -->',
           '<NPCCharacters>']

    main = extract_element(os.path.join(MODULES, "SandBox", "ModuleData", "lords.xml"), "NPCCharacter", "main_hero")
    if main is not None:
        main.set("culture", "Culture.rd2_new_hanover")
        main.set("name", "{=RD2_main_hero}Arthur Morgan")
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
                   % (rid, level, "true" if female else "false", L("RD2_npc_" + rid, en, zh), occ))
        out.append('    <face><face_key_template value="BodyProperty.villager_empire" /></face>')
        out.append('    <upgrade_targets />')
        out.append('    <Equipments><EquipmentRoster civilian="true" /><EquipmentRoster /></Equipments>')
        out.append('  </NPCCharacter>')

    # 每文化：Merchant / Artisan / 26 级步兵 CaravanGuard（引擎按文化查）
    for cid, zh, en, _, _ in CULTURES:
        for role, en_name, zh_name, occ, level in [
            ("merchant", en + " Merchant", zh + "商人", "Merchant", 1),
            ("artisan", en + " Artisan", zh + "工匠", "Artisan", 2),
            ("caravan_guard", en + " Caravan Guard", zh + "商队护卫", "CaravanGuard", 26),
        ]:
            rid = "%s_%s" % (cid, role)
            out.append('  <NPCCharacter id="%s" default_group="Infantry" level="%d" is_female="false" culture="Culture.%s" name="%s" occupation="%s">'
                       % (rid, level, cid, L("RD2_npc_" + rid, en_name, zh_name), occ))
            out.append('    <face><face_key_template value="BodyProperty.villager_empire" /></face>')
            out.append('    <upgrade_targets />')
            out.append('    <Equipments><EquipmentRoster civilian="true" /><EquipmentRoster /></Equipments>')
            out.append('  </NPCCharacter>')

    out.append('</NPCCharacters>')
    write(os.path.join(MODULE, "ModuleData", "rd2_characters.xml"), "\n".join(out) + "\n")


def gen_submodule():
    gt = '<IncludedGameTypes><GameType value="RD2Campaign"/></IncludedGameTypes>'
    segs = [
        ("Items", "rd2_items"), ("SPCultures", "spcultures"),
        ("NPCCharacters", "spnpccharactertemplates"),
        ("NPCCharacters", "spnpccharacters"),
        ("NPCCharacters", "rd2_lords"),
        ("NPCCharacters", "rd2_characters"),
        ("NPCCharacters", "rd2_roles"),
        ("partyTemplates", "partyTemplates"),
        ("Kingdoms", "spkingdoms"), ("Factions", "spclans"),
        ("WorkshopTypes", "spworkshops"), ("Settlements", "settlements"),
        ("BodyProperties", "rd2_bodyproperties"), ("SkillSets", "rd2_skill_sets"),
        ("EquipmentRosters", "rd2_equipment_sets"), ("Concepts", "rd2_concepts"),
        ("CraftingPieces", "rd2_crafting_pieces"), ("Heroes", "rd2_heroes"),
        ("LocationComplexTemplates", "rd2_location_complex_templates"),
        ("MusicInstruments", "rd2_music_instruments"),
        ("MusicTracks", "rd2_music_tracks"),
        ("GameText", "rd2_strings"),
        ("GameText", "module_strings"), ("GameText", "world_lore_strings"),
        ("GameText", "companion_strings"), ("GameText", "wanderer_strings"),
        ("GameText", "comment_strings"), ("GameText", "comment_on_action_strings"),
        ("GameText", "trait_strings"), ("GameText", "voice_strings"),
        ("GameText", "action_strings"),
    ]
    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<Module>',
           '  <Name value="RD2"/>',
           '  <Id value="RD2"/>',
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


def main():
    print("RD2 generator")
    print("copy base files...")
    copy_base()
    world, roster = load_world()
    print("world rows:", len(world))
    print("generate cultures...")
    gen_cultures(world)
    print("generate kingdoms...")
    gen_kingdoms(world)
    print("generate clans...")
    gen_clans(world)
    print("generate heroes...")
    gen_heroes(world)
    print("generate settlements...")
    gen_settlements(world)
    print("generate party templates...")
    gen_party_templates(world)
    print("generate troops...")
    gen_troops(world)
    print("generate characters...")
    gen_characters()
    print("generate strings...")
    gen_culture_text_variants()
    gen_strings()
    print("generate SubModule.xml...")
    gen_submodule()
    print("done. strings:", len(STRINGS))


if __name__ == "__main__":
    main()
