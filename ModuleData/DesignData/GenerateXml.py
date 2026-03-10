import csv 
import os

# ================= 配置区域 =================
CURRENT_YEAR = 1568
# 男性通用面部代码 (示例)
MALE_FACE_KEY = "001944000E0015C609748F687E35431497F60440966FF2B0A006E02F80A5772800C660030C34C625000000000000000000000000000000000000000027900140"
# 女性通用面部代码 (骑砍2默认女性代码之一，你可以替换)
FEMALE_FACE_KEY = "0002040DD2B44006128317117111116680F68735607DA402BDF6C6307C530016007A260307B061130044000005F030350000000000001A770000000040FC1000"

# 装备模板 (使用示例中提供的)
EQUIPMENT_SET_BATTLE = "kinai_bat_template_medium_male"
EQUIPMENT_SET_CIVILIAN = "kinai_civ_template_default_male"

# 输出文件名
OUT_HEROES = "heroes.xml"
OUT_LORDS = "lords.xml"
OUT_CLANS = "clans.xml"
OUT_KINGDOMS = "spkingdoms.xml"
OUT_STRINGS = "output_strings2.xml"

# ================= 数据容器 =================
heroes = []
clans = []
kingdoms = []
localization_strings = {} # key: id, value: text

# 辅助字典，用于快速查找
clan_leader_map = {} # clan_id -> hero_id (统率最高的)
clan_kingdom_map = {} # clan_id -> kingdom_id
kingdom_leader_map = {} # kingdom_id -> hero_id (Ruling clan's leader)

# ================= 读取函数 =================
def read_csv(filename):
    data = []
    if not os.path.exists(filename):
        print(f"警告: 找不到文件 {filename}")
        return data
    
    # 建议使用 utf-8-sig 以防 Excel 保存的 CSV 带有 BOM 字符
    with open(filename, 'r', encoding='utf-8-sig') as f:
        reader = csv.reader(f)
        
        # 1. 读取第一行作为 表头 (Keys)
        try:
            headers = next(reader)
            # 去除表头前后可能存在的空格，防止 'Name ' 这种错误
            headers = [h.strip() for h in headers]
        except StopIteration:
            return data

        # 2. 跳过中间两行 (Types 和 Chinese Names)
        # 原逻辑是跳过前三行，现在第一行已经被读走了，所以只需要再跳过两行
        try:
            next(reader) 
            next(reader) 
        except StopIteration:
            pass
        
        # 3. 读取剩余数据并转换为字典
        for row in reader:
            if row:
                # 只有当行内有数据时才处理
                # zip会自动匹配长度，如果row比header短，后面的None会自动处理或截止
                # 这是一个 {'HeroID': '1', 'Name': 'Nobunaga', ...} 的字典
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
    # 简单的 /10 映射，最高10
    level = val // 10
    if level > 10: level = 10
    if level == 0: level  = 1
    return level

def process_data():
    raw_heroes = read_csv("Hero.csv")
    raw_clans = read_csv("Clan.csv")
    raw_kingdoms = read_csv("Kingdom.csv")

    # 1. 处理家族 (Clan) 基本信息，建立索引
    # Clan CSV Indices based on provided header:
    # 0:ID, 1:ScriptName, 2:ChineseName, 3:LocozationName, 4:Kingdom, 5:IsShokuho, 6:Surname, 7:Culture 8:OtherName（可能为空) 9:Owner（可能为空）
    for row in raw_clans:        
        c_id = row.get('ID')
        if not c_id: continue
    
        clan_obj = {
            'id': row.get('ID'),
            'name_key': f"my_{c_id}",
            'name_text': row.get('ChineseName'), # 中文名
            'engname_text': row.get('Surname'), # 英文名
            'kingdom_id': row.get('Kingdom'),
            'is_shokuho': row.get('IsShokuho').strip() == '1',
            'culture_id': row.get('Culture'),
            'owner_id':row.get('Owner'),
            'members': [] # 暂存成员
        }
        
       
        
        
        
        clans.append(clan_obj)
        # 本地化 这里可能要手动改下
        localization_strings[clan_obj['name_key']] = f"{clan_obj['name_text']}氏" # 加上"氏"

    # 2. 处理王国 (Kingdom) 基本信息
    # Kingdom CSV Indices:
    # 0:ID, 1:ScriptName, 2:ChineseName, 3:LocozationName, 4:Culture, 5:IsShokuho 6：Owner（可能为空)
    kingdom_cn_map = {}
    for row in raw_kingdoms:        
        k_id = row.get('ID')
        cn_name = row.get('ChineseName')
        if k_id and cn_name:
            clean_cn = cn_name.strip()
            kingdom_cn_map[clean_cn] = k_id
        
        kd_obj = {
            
            'id': k_id,
            'name_key': f"my_{k_id}",
            'name_text': cn_name,
            'culture_id': row.get('Culture'),
            'is_shokuho': row.get('IsShokuho').strip() == '1',
            'owner_id':row.get('Owner'),
            'ruling_clan_candidates': []
        }
        kingdoms.append(kd_obj)
        localization_strings[kd_obj['name_key']] = cn_name




    # 3. 处理英雄 (Hero) 并分配给家族
    # Hero CSV Indices based on provided sample (Counting columns manually based on headers):
    # 0:ID, 2:CNName, 6:ClanID, 7:CultureID, 10:BirthYear, 12:DeathYear
    # Stats: 32:统率, 33:武力, 34:政务, 35:智谋, 36:魅力
    # 假设性别需要根据外观ID或名字推断，这里简化处理，默认男，除非特定ID
    
    for row in raw_heroes:
        hero_id = row.get('ID')
        clan_id = row.get('ClanID')
        culture_id = row.get('CultureID')
        gender = row.get('Gender')
        
        # 处理出生死亡
        try:
            birth_year = int(row.get('BirthYear')) if row.get('BirthYear') else 1550
            death_year = int(row.get('DieYear')) if row.get('DieYear') else 1620
        except:
            birth_year = 1550
            death_year = 1620

        age = CURRENT_YEAR - birth_year
        is_dead = CURRENT_YEAR > death_year
        
        # 骑砍中未出生的人也可以写入，age设为负数或0，但alive要设为false吗？
        # 通常骑砍Mod制作中，未出生的人 alive="false" 且 age 可以是负数，等到时间到了游戏引擎会处理出生(需要mod支持)
        # 或者为了简单，全部设为 alive="true" age="0" (婴儿)。
        # 这里策略：死人标记死亡，未出生标记为婴儿或负岁数。
        
        alive_str = "true"
        if is_dead:
            alive_str = "false"
        
        # 简单性别判断 (实际应在CSV加一列Gender)
        # 示例数据中没有明确性别列，暂时全部设为男性面部
        # 如果名字里有"姬"、"夫人"、"阿"开头等可以特殊处理，这里先统一
        if(gender == '1'):
            face_key = MALE_FACE_KEY
            is_female_str = "false"
        else:
            face_key = FEMALE_FACE_KEY
            is_female_str = "true"
        
        # 属性
        ld_stat = row.get('CommandValue') # 统率
        war_stat = row.get('ForceValue') # 武力
        pol_stat = row.get('GovernValue') # 政务
        int_stat = row.get('WisdomValue') # 智谋
        cha_stat = row.get('CharmValue') # 魅力
        
        #不同年份的所属，这里只以1568年为例
        raw_k_str = row.get('Kingdom_1568') 
        if raw_k_str:
            clean_name = raw_k_str.replace("家", "").strip()
            k_1568_id = kingdom_cn_map.get(clean_name)

        hero_obj = {
            'id': hero_id,
            'name_key': f"my_{hero_id}",
            'name_text': row.get('CNName'), # 中文名
            'engname_text': row.get('EnglishName'), # 英文名
            'clan_id': clan_id,
            'culture_id': culture_id,
            'age': age,
            'is_female_str':is_female_str,
            'alive': alive_str,
            'face_key': face_key,
            'is_female': is_female_str,
            'is_shokuho': row.get('GenerateType').strip() == '精确匹配织丰',
            'stats': {
                'Commander': get_trait_level(ld_stat), # 统率 -> 指挥
                'Valor': get_trait_level(war_stat),    # 武力 -> 勇猛
                'Politician': get_trait_level(pol_stat), # 政务 -> 政治
                'Manager': get_trait_level(int_stat),    # 智谋 -> 管理
                'Honor': 0, # 默认
                'Generosity': 0,
                'Mercy': 0
            },
            'leadership_val': int(ld_stat) if ld_stat.isdigit() else 0, # 用于选族长
            'kingdom_1568': k_1568_id 
        }
        heroes.append(hero_obj)
        localization_strings[hero_obj['name_key']] = hero_obj['name_text']

        # 归类到家族
        for c in clans:
            if c['id'] == clan_id:
                c['members'].append(hero_obj)
                break

    
    kingdom_map = {k['id']: k for k in kingdoms}
    for c in clans:           


        
        leader_id = None
        
        
        
        if c['owner_id'] and c['owner_id']!="":     
            leader_id =  c['owner_id']      
        else:        
            
            #在这一步才跳过，是为了想让kingdom能统计到原版的自己下属clan
            if(c['is_shokuho']): 
                continue                     
            
            
            # 选统率最高的活人作为族长
            if not c['members']:
                print(f"报错 : 新的 Clan [{c['id']}] 没有成员，无法计算族长。 ")
                
            
            candidates = [h for h in c['members'] if h['alive'] == "true" and h['age'] >= 16]
            if not candidates:
                # 如果都死了或没成年，选任意一个
                candidates = c['members']
            if candidates:
                # 按统率倒序排
                candidates.sort(key=lambda x: x['leadership_val'], reverse=True)
                leader_id = candidates[0]['id']
        
        # 记录族长映射
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
                if target_kingdom['is_shokuho'] and c['kingdom_id'] != target_k_1568:
                    print(f"提示: Clan [{c['id']}] 基于1568年修正了所属王国为{target_k_1568}。 ")
                c['kingdom_id'] = target_k_1568
        target_kingdom = kingdom_map.get(c['kingdom_id'])
        if target_kingdom is None:
            c['kingdom_id'] = 'noKingdom'
        elif not target_kingdom['is_shokuho']: 
            #本次不新建王国
            c['kingdom_id'] = 'noKingdom'
        
        if c['kingdom_id'] in kingdom_map:
            kingdom_map[c['kingdom_id']]['ruling_clan_candidates'].append(c)
        elif c['kingdom_id'] != 'noKingdom':
            print(f"Clan {c['id']} 最终归属 {c['kingdom_id']} 但未找到该国家对象。")
           
            
            
            
            
                    
# -----------------------------------------------------------
    # 5. 确定 Kingdom 的领袖 (核心逻辑修改版)
    # -----------------------------------------------------------
    for k in kingdoms:
        
        if k['owner_id'] and k['owner_id']!="":     
            kingdom_leader_map[k['id']] = k['owner_id']
            continue
        
        
        k_id = k['id']
        ruling_clan = None
        
        # 候选人列表（已经在前面步骤通过 c['kingdom_id'] == k['id'] 填充）
        current_vassal_clans = k.get('ruling_clan_candidates', [])

        # === 规则 1 & 2: 优先通过 ID 包含关系寻找“正统”统治家族 ===
        # 遍历【所有】家族 (raw_clans/clans)，而不仅仅是当前下属家族
        # 只要 Clan ID 包含了 Kingdom ID，就认为是正统家族
        k_search_pattern = f"_{k_id}_"
        target_match_clan = None
        
        for c in clans:
            # 核心判断：Kingdom ID 包含在 Clan ID 中 (例如 Kingdom 'oda' 在 Clan 'clan_oda' 中)
            if k_search_pattern in c['id']:
                if c['kingdom_id'] == k_id:
                    # 匹配且忠诚：这是最完美的情况
                    target_match_clan = c
                    break # 找到正主，直接锁定
                else:
                    # 规则 1: 匹配但不忠诚 (原主跑去别的国家了)
                    print(f"警告 (规则1): 家族 [{c['id']}] 的ID包含王国 [{k_id}]，但该家族当前归属于 [{c['kingdom_id']}]。意味着原定统治者已出走，该王国可能无法按原计划成立。")
                    # 继续循环，看看有没有其他匹配的家族，或者最终走兜底逻辑
        
        if target_match_clan:
            ruling_clan = target_match_clan
        else:
            # === 规则 2 (兜底): ID匹配失败，在现有下属中找最强的 ===
            if current_vassal_clans:
                # 这里按逻辑应该找统率最高的，这里默认取列表第一个 (通常是CSV里最早读入的)
                # 你可以在这里加排序逻辑，比如 sorted(..., key=lambda x: x['tier'])
                ruling_clan = current_vassal_clans[0]
                print(f"提示 (规则2): Kingdom [{k_id}] 未找到ID匹配的'正统'家族 (或已出走)，强制兜底：指定现有下属家族 [{ruling_clan['id']}] 为统治者。")
            else:
                # === 规则 3: 既没正统，也没下属 (空壳) ===
                print(f"错误 (规则3): Kingdom [{k_id}] 没有任何下属家族，且未找到正统家族。该王国将被跳过。")
                k['skip_export'] = True
                continue

        # === 规则 4: 只要通过了上面的筛选，必须打印提示 (已包含在上述 print 中) ===
        
        # 最终赋值
        k['ruling_clan_id'] = ruling_clan['id']
        
        # 寻找国王本人 (Hero)
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
        if h['is_shokuho'] : continue
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
        if h['is_shokuho'] : continue
        xml += f'  <NPCCharacter id="{h["id"]}" name="{{={h["name_key"]}}}{h["engname_text"]}" age="{h["age"]}"  voice="curt" default_group="Cavalry" is_female="{h["is_female_str"]}"  is_hero="true" culture="Culture.{h["culture_id"]}" occupation="Lord" face_mesh_cache="true">\n'
        xml += f'    <face>\n'
        xml += f'      <BodyProperties version="4" age="{h["age"]}.00" weight="0.5" build="0.5" key="{h["face_key"]}"/>\n'
        xml += f'    </face>\n'
        xml += f'    <Traits>\n'
        xml += f'      <Trait id="Commander" value="{h["stats"]["Commander"]}"/><Trait id="Politician" value="{h["stats"]["Politician"]}"/>\n'
        xml += f'      <Trait id="Manager" value="{h["stats"]["Manager"]}"/><Trait id="Valor" value="{h["stats"]["Valor"]}"/>\n'
        xml += f'    </Traits>\n'
        xml += f'    <Equipments>\n'
        xml += f'      <EquipmentRoster/>\n'
        xml += f'      <EquipmentSet id="{EQUIPMENT_SET_BATTLE}"/>\n'
        xml += f'      <EquipmentSet id="{EQUIPMENT_SET_CIVILIAN}" civilian="true"/>\n'
        xml += f'    </Equipments>\n'
        xml += f'  </NPCCharacter>\n'
    xml += "</NPCCharacters>"
    
    
# =============================================================================
#     如果是忍者，即名字包含Ninja，需要吧装备部分改成
#     <Equipments>
#       <!-- 直接在 EquipmentRoster 下列出物品，不要加 EquipmentSet -->
#       <EquipmentRoster>
#         <Equipment slot="Item0" id="sho_katana_15"/>
#         <Equipment slot="Item1" id="sho_wakizashi_18"/>
#         <Equipment slot="Item2" id="hr_weapon_kunai"/>
#         <Equipment slot="Item3" id="hr_weapon_bomb"/>
#         <Equipment slot="Head" id="sho_monk_headwrap_b_1"/>
#         <Equipment slot="Body" id="sho_peasant_outfit_a"/>
#         <Equipment slot="Leg" id="sho_waraji_a"/>
#         <Equipment slot="Gloves" id="sho_tier_1_kote_a_7"/>
#       </EquipmentRoster>
#     </Equipments>
# =============================================================================
    
    return xml

def generate_clans_xml():
    xml = "<?xml version='1.0' encoding='UTF-8'?>\n<Factions>\n"
    for c in clans:
        if c['is_shokuho']: continue # 原版已有的跳过
        
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
        if k['is_shokuho']: continue # 原版已有的跳过
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