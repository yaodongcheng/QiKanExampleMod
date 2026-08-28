# -*- coding: utf-8 -*-
"""
GenerateXml.py —— 织丰额外人物/家族 XML 生成器（迁移改造版，2026-08-28）

来源
----
原文件：`Modules/ShokuhoTaikouExpansionPack/ModuleData/DesignData/GenerateXml.py`
（2026-03-11 旧管线；本版本为迁移 + 改造，输入换为主表 csv/ 镜像）

改造点（2026-08-28）
--------------------
1. 🔴 输入改读 `Knowledge/骑砍2织丰角色ID对应/csv/`（TaikouHero/Clan/Kingdom）
   ——DesignData/*.csv 与镜像同源同量（实证），镜像为权威源，旧 CSV 已删。
2. 🔴 read_csv 去掉「类型行/注释行」两行 skip：镜像只有表头 + 数据
   （原 CSV 带织丰表类型行/注释行，共 3 行头；镜像清洗掉了后两行）。
3. 🔴 恢复「忍者专属装备」分支：原脚本此分支已被删除（文件里只剩注释，
   注释里保留着理想的忍者装备清单）——本次按注释清单恢复为代码分支，
   消除「同输入不同输出」的非确定性（lord_1_ninja 装备段）。
4. 🔴 空值防御：GenerateType / IsShukuho / CommandValue 可能为空
   （原版 `row.get('GenerateType').strip()` 空值会崩）。

用法（验证/生成）：
    cd plans/scenario-campaign-mode/tools
    python GenerateXml.py        # 在当前目录生成 heroes/lords/clans/output_strings2.xml

与其他生成器关系：`gen_entity_maps.py`（名字→StringId 表）是翻译层；
本脚本是「织丰增补人物 XML」层（07c 步骤 3 生成器的雏形——按 07c 裁定 1
作废旧管线后，新管线在本脚本基础上补 Charm/性格/装备拟合等缺失项）。
"""
import csv
import os

# ================= 配置区域 =================
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
CSV_DIR = os.path.join(REPO, 'Knowledge', '骑砍2织丰角色ID对应', 'csv')   # 主表 csv 镜像

CURRENT_YEAR = 1568
# 男性通用面部代码 (示例)
MALE_FACE_KEY = "001944000E0015C609748F687E35431497F60440966FF2B0A006E02F80A5772800C660030C34C625000000000000000000000000000000000000000027900140"
# 女性通用面部代码 (骑砍2默认女性代码之一，你可以替换)
FEMALE_FACE_KEY = "0002040DD2B44006128317117111116680F68735607DA402BDF6C6307C530016007A260307B061130044000005F030350000000000001A770000000040FC1000"

# 装备模板 (使用示例中提供的)
EQUIPMENT_SET_BATTLE = "kinai_bat_template_medium_male"
EQUIPMENT_SET_CIVILIAN = "kinai_civ_template_default_male"

# 忍者专属装备（原注释清单——2026-08-28 恢复为真分支，消除非确定性）
NINJA_EQUIPMENTS = [
    ('Item0', 'sho_katana_15'),
    ('Item1', 'sho_wakizashi_18'),
    ('Item2', 'hr_weapon_kunai'),
    ('Item3', 'hr_weapon_bomb'),
    ('Head', 'sho_monk_headwrap_b_1'),
    ('Body', 'sho_peasant_outfit_a'),
    ('Leg', 'sho_waraji_a'),
    ('Gloves', 'sho_tier_1_kote_a_7'),
]

# 输出文件名（写入当前目录）
OUT_HEROES = "heroes.xml"
OUT_LORDS = "lords.xml"
OUT_CLANS = "clans.xml"
OUT_KINGDOMS = "spkingdoms.xml"
OUT_STRINGS = "output_strings2.xml"

# ================= 数据容器 =================
heroes = []
clans = []
kingdoms = []
localization_strings = {}

clan_leader_map = {}
clan_kingdom_map = {}
kingdom_leader_map = {}

# ================= 读取函数 =================
def read_csv(filename):
    """读 csv/ 镜像：第一行表头，之后全为数据行（无类型行/注释行）。"""
    path = os.path.join(CSV_DIR, filename)
    data = []
    if not os.path.exists(path):
        print(f"警告: 找不到文件 {path}")
        return data

    with open(path, 'r', encoding='utf-8-sig') as f:
        reader = csv.reader(f)
        try:
            headers = next(reader)
            headers = [h.strip() for h in headers]
        except StopIteration:
            return data
        # 🔴 镜像无类型行/注释行（原 CSV 有 3 行头，这里不再 skip）
        for row in reader:
            if row:
                item = dict(zip(headers, row))
                data.append(item)
    return data

# ================= 逻辑处理函数 =================

def get_trait_level(val):
    """将0-100的数值映射到骑砍0-10的特质等级"""
    try:
        val = int(val)
    except:
        return 1
    level = val // 10
    if level > 10: level = 10
    if level == 0: level = 1
    return level

def process_data():
    raw_heroes = read_csv("TaikouHero.csv")
    raw_clans = read_csv("Clan.csv")
    raw_kingdoms = read_csv("Kingdom.csv")

    # 1. 处理家族 (Clan) 基本信息，建立索引
    for row in raw_clans:
        c_id = row.get('ID')
        if not c_id:
            continue
        clan_obj = {
            'id': c_id,
            'name_key': f"my_{c_id}",
            'name_text': row.get('ChineseName'),
            'engname_text': row.get('Surname'),
            'kingdom_id': row.get('Kingdom'),
            'is_shokuho': (row.get('IsShokuho') or '').strip() == '1',
            'culture_id': row.get('Culture'),
            'owner_id': row.get('Owner'),
            'members': []
        }
        clans.append(clan_obj)
        localization_strings[clan_obj['name_key']] = f"{clan_obj['name_text']}氏"

    # 2. 处理王国 (Kingdom) 基本信息
    kingdom_cn_map = {}
    for row in raw_kingdoms:
        k_id = row.get('ID')
        cn_name = row.get('ChineseName')
        if k_id and cn_name:
            kingdom_cn_map[cn_name.strip()] = k_id
        kd_obj = {
            'id': k_id,
            'name_key': f"my_{k_id}",
            'name_text': cn_name,
            'culture_id': row.get('Culture'),
            'is_shokuho': (row.get('IsShokuho') or '').strip() == '1',
            'owner_id': row.get('Owner'),
            'ruling_clan_candidates': []
        }
        kingdoms.append(kd_obj)
        localization_strings[kd_obj['name_key']] = cn_name

    # 3. 处理英雄 (Hero) 并分配给家族
    for row in raw_heroes:
        hero_id = row.get('ID')
        clan_id = row.get('ClanID')
        culture_id = row.get('CultureID')
        gender = row.get('Gender')

        try:
            birth_year = int(row.get('BirthYear')) if row.get('BirthYear') else 1550
            death_year = int(row.get('DieYear')) if row.get('DieYear') else 1620
        except:
            birth_year = 1550
            death_year = 1620

        age = CURRENT_YEAR - birth_year
        is_dead = CURRENT_YEAR > death_year

        alive_str = "true"
        if is_dead:
            alive_str = "false"

        if gender == '1':
            face_key = MALE_FACE_KEY
            is_female_str = "false"
        else:
            face_key = FEMALE_FACE_KEY
            is_female_str = "true"

        ld_stat = row.get('CommandValue')
        war_stat = row.get('ForceValue')
        pol_stat = row.get('GovernValue')
        int_stat = row.get('WisdomValue')
        cha_stat = row.get('CharmValue')

        raw_k_str = row.get('Kingdom_1568')
        k_1568_id = None
        if raw_k_str:
            clean_name = raw_k_str.replace("家", "").strip()
            k_1568_id = kingdom_cn_map.get(clean_name)

        hero_obj = {
            'id': hero_id,
            'name_key': f"my_{hero_id}",
            'name_text': row.get('CNName'),
            'engname_text': row.get('EnglishName'),
            'clan_id': clan_id,
            'culture_id': culture_id,
            'age': age,
            'is_female_str': is_female_str,
            'alive': alive_str,
            'face_key': face_key,
            'is_shokuho': (row.get('GenerateType') or '').strip() == '精确匹配织丰',
            'stats': {
                'Commander': get_trait_level(ld_stat),
                'Valor': get_trait_level(war_stat),
                'Politician': get_trait_level(pol_stat),
                'Manager': get_trait_level(int_stat),
                'Honor': 0,
                'Generosity': 0,
                'Mercy': 0
            },
            'leadership_val': int(ld_stat) if ld_stat and str(ld_stat).isdigit() else 0,
            'kingdom_1568': k_1568_id
        }
        heroes.append(hero_obj)
        localization_strings[hero_obj['name_key']] = hero_obj['name_text']

        for c in clans:
            if c['id'] == clan_id:
                c['members'].append(hero_obj)
                break

    kingdom_map = {k['id']: k for k in kingdoms}
    for c in clans:
        leader_id = None

        if c['owner_id'] and c['owner_id'] != "":
            leader_id = c['owner_id']
        else:
            if c['is_shokuho']:
                continue
            if not c['members']:
                print(f"报错 : 新的 Clan [{c['id']}] 没有成员，无法计算族长。")
            candidates = [h for h in c['members'] if h['alive'] == "true" and h['age'] >= 16]
            if not candidates:
                candidates = c['members']
            if candidates:
                candidates.sort(key=lambda x: x['leadership_val'], reverse=True)
                leader_id = candidates[0]['id']

        if leader_id:
            clan_leader_map[c['id']] = leader_id
        else:
            print(f"警告: Clan [{c['id']}] 没有任何成员，无法指定族长。")
            continue
        leader_hero = next((h for h in heroes if h['id'] == leader_id), None)
        if leader_hero:
            target_k_1568 = leader_hero.get('kingdom_1568')
            if target_k_1568 and target_k_1568 != '0' and target_k_1568 != '':
                target_kingdom = kingdom_map.get(target_k_1568)
                if target_kingdom and target_kingdom['is_shokuho'] and c['kingdom_id'] != target_k_1568:
                    print(f"提示: Clan [{c['id']}] 基于1568年修正了所属王国为{target_k_1568}。 ")
                c['kingdom_id'] = target_k_1568
        target_kingdom = kingdom_map.get(c['kingdom_id'])
        if target_kingdom is None:
            c['kingdom_id'] = 'noKingdom'
        elif not target_kingdom['is_shokuho']:
            c['kingdom_id'] = 'noKingdom'

        if c['kingdom_id'] in kingdom_map:
            kingdom_map[c['kingdom_id']]['ruling_clan_candidates'].append(c)
        elif c['kingdom_id'] != 'noKingdom':
            print(f"Clan {c['id']} 最终归属 {c['kingdom_id']} 但未找到该国家对象。")

    # 5. 确定 Kingdom 的领袖
    for k in kingdoms:
        if k['owner_id'] and k['owner_id'] != "":
            kingdom_leader_map[k['id']] = k['owner_id']
            continue

        k_id = k['id']
        ruling_clan = None
        current_vassal_clans = k.get('ruling_clan_candidates', [])

        k_search_pattern = f"_{k_id}_"
        target_match_clan = None
        for c in clans:
            if k_search_pattern in c['id']:
                if c['kingdom_id'] == k_id:
                    target_match_clan = c
                    break
                else:
                    print(f"警告 (规则1): 家族 [{c['id']}] 的ID包含王国 [{k_id}]，但该家族当前归属于 [{c['kingdom_id']}]。意味着原定统治者已出走，该王国可能无法按原计划成立。")

        if target_match_clan:
            ruling_clan = target_match_clan
        else:
            if current_vassal_clans:
                ruling_clan = current_vassal_clans[0]
                print(f"提示 (规则2): Kingdom [{k_id}] 未找到ID匹配的'正统'家族 (或已出走)，强制兜底：指定现有下属家族 [{ruling_clan['id']}] 为统治者。")
            else:
                print(f"错误 (规则3): Kingdom [{k_id}] 没有任何下属家族，且未找到正统家族。该王国将被跳过。")
                k['skip_export'] = True
                continue

        k['ruling_clan_id'] = ruling_clan['id']
        leader_id = clan_leader_map.get(ruling_clan['id'])
        if leader_id:
            kingdom_leader_map[k['id']] = leader_id
        else:
            print(f"严重错误: Kingdom [{k_id}] 的统治家族 [{ruling_clan['id']}] 居然没有族长！该王国将被跳过。")
            k['skip_export'] = True


# ================= XML 生成函数 =================

def generate_heroes_xml():
    xml = "<Heroes>\n"
    for h in heroes:
        if h['is_shokuho']:
            continue
        xml += f'    <Hero id="{h["id"]}"\n'
        xml += f'          name="{{={h["name_key"]}}}{h["engname_text"]}"\n'
        xml += f'          faction="Faction.{h["clan_id"]}"\n'
        xml += f'          culture="Culture.{h["culture_id"]}"\n'
        xml += f'          alive="{h["alive"]}"\n'
        xml += f'          is_noble="true"\n'
        xml += f'          occupation="Lord"\n'
        xml += f'          gold="5000"\n'
        xml += f'          age="{h["age"]}" />\n'
    xml += "</Heroes>"
    return xml

def generate_lords_xml():
    xml = '<?xml version="1.0" encoding="utf-8"?>\n<NPCCharacters>\n'
    for h in heroes:
        if h['is_shokuho']:
            continue
        xml += f'  <NPCCharacter id="{h["id"]}" name="{{={h["name_key"]}}}{h["engname_text"]}" age="{h["age"]}"  voice="curt" default_group="Cavalry" is_female="{h["is_female_str"]}"  is_hero="true" culture="Culture.{h["culture_id"]}" occupation="Lord" face_mesh_cache="true">\n'
        xml += f'    <face>\n'
        xml += f'      <BodyProperties version="4" age="{h["age"]}.00" weight="0.5" build="0.5" key="{h["face_key"]}"/>\n'
        xml += f'    </face>\n'
        xml += f'    <Traits>\n'
        xml += f'      <Trait id="Commander" value="{h["stats"]["Commander"]}"/><Trait id="Politician" value="{h["stats"]["Politician"]}"/>\n'
        xml += f'      <Trait id="Manager" value="{h["stats"]["Manager"]}"/><Trait id="Valor" value="{h["stats"]["Valor"]}"/>\n'
        xml += f'    </Traits>\n'
        if 'ninja' in h['id']:
            # 🔴 忍者专属：直接在 EquipmentRoster 下列出物品，不要加 EquipmentSet
            xml += f'    <Equipments>\n'
            xml += f'      <!-- 直接在 EquipmentRoster 下列出物品，不要加 EquipmentSet -->\n'
            xml += f'      <EquipmentRoster>\n'
            for slot, item in NINJA_EQUIPMENTS:
                xml += f'        <Equipment slot="{slot}" id="{item}"/>\n'
            xml += f'      </EquipmentRoster>\n'
            xml += f'    </Equipments>\n'
        else:
            xml += f'    <Equipments>\n'
            xml += f'      <EquipmentRoster/>\n'
            xml += f'      <EquipmentSet id="{EQUIPMENT_SET_BATTLE}"/>\n'
            xml += f'      <EquipmentSet id="{EQUIPMENT_SET_CIVILIAN}" civilian="true"/>\n'
            xml += f'    </Equipments>\n'
        xml += f'  </NPCCharacter>\n'
    xml += "</NPCCharacters>"
    return xml

def generate_clans_xml():
    xml = "<?xml version='1.0' encoding='UTF-8'?>\n<Factions>\n"
    for c in clans:
        if c['is_shokuho']:
            continue
        owner_id = clan_leader_map.get(c['id'])
        if not owner_id:
            print(f"错误: 家族 {c['id']} 找不到对应的 Hero 作为族长，跳过生成。")
            continue
        if not owner_id.startswith("Hero."):
            owner_id = f"Hero.{owner_id}"
        xml += f'    <Faction id="{c["id"]}" is_noble="true" name="{{={c["name_key"]}}}{c["engname_text"]}-shi" tier="3" owner="{owner_id}" culture="Culture.{c["culture_id"]}" super_faction="Kingdom.{c["kingdom_id"]}" banner_key="11.163.166.1528.1528.764.764.1.0.0.722.171.171.483.483.764.764.0.0.0"/>\n'
    xml += "</Factions>"
    return xml

def generate_kingdoms_xml():
    xml = "<?xml version='1.0' encoding='UTF-8'?>\n<Kingdoms>\n"
    for k in kingdoms:
        if k['is_shokuho']:
            continue
        if k.get('skip_export'):
            continue
        owner_id = kingdom_leader_map.get(k['id'])
        if not owner_id:
            print(f"错误: 王国 {k['id']} 找不到 Ruling Clan Leader，跳过生成。")
            continue
        if not owner_id.startswith("Hero."):
            owner_id = f"Hero.{owner_id}"
        xml += f'    <Kingdom id="{k["id"]}"\n'
        xml += f'             owner="{owner_id}"\n'
        xml += f'             culture="Culture.{k["culture_id"]}"\n'
        xml += f'             banner_key="11.163.166.1528.1528.764.764.1.0.0.743.171.171.483.483.764.764.0.0.0"\n'
        xml += f'             primary_banner_color="0xff564438"\n'
        xml += f'             secondary_banner_color="0xfff6dfaa"\n'
        xml += f'             label_color="FFDB8330"\n'
        xml += f'             name="{{={k["name_key"]}}}{k["id"]}"\n'
        xml += f'             short_name="{{={k["name_key"]}}}{k["id"]}"\n'
        xml += f'             title="{{={k["name_key"]}}}{k["id"]}"\n'
        xml += f'             ruler_title="{{={k["name_key"]}}}{k["id"]}"\n'
        xml += f'             text="{{={k["name_key"]}}}{k["id"]}">\n'
        xml += f'        <relationships> </relationships>\n'
        xml += f'        <policies> </policies>\n'
        xml += f'    </Kingdom>\n'
    xml += "</Kingdoms>"
    return xml

def generate_strings_xml():
    xml = '<?xml version="1.0" encoding="utf-8"?>\n<base xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" type="string">\n'
    xml += '  <tags>\n    <tag language="简体中文" />\n  </tags>\n<strings>\n'
    for k, v in localization_strings.items():
        xml += f'    <string id="{k}" text="{v}" />\n'
    xml += "</strings></base>"
    return xml

# ================= 主程序 =================

if __name__ == "__main__":
    print("开始处理数据...")
    process_data()

    print(f"解析到 {len(heroes)} 名英雄")
    print(f"解析到 {len(clans)} 个家族")
    print(f"解析到 {len(kingdoms)} 个国家")

    print("正在生成XML...")

    with open(OUT_HEROES, "w", encoding="utf-8") as f:
        f.write(generate_heroes_xml())

    with open(OUT_LORDS, "w", encoding="utf-8") as f:
        f.write(generate_lords_xml())

    with open(OUT_CLANS, "w", encoding="utf-8") as f:
        f.write(generate_clans_xml())

# =============================================================================
#     with open(OUT_KINGDOMS, "w", encoding="utf-8") as f:
#         f.write(generate_kingdoms_xml())
# =============================================================================

    with open(OUT_STRINGS, "w", encoding="utf-8") as f:
        f.write(generate_strings_xml())

    print("完成！所有文件已生成在当前目录。")
