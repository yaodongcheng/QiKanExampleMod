import os
import re
import xml.etree.ElementTree as ET
from openpyxl import Workbook

# --- 配置部分 ---
DIR_HEROES = 'heroes'      # 包含 <Hero> 的文件夹
DIR_LORDS = 'lords'        # 包含 <NPCCharacter> 的文件夹
DIR_CN = 'CNs'             # 汉化文件夹
OUTPUT_FILE = 'Merged_Heroes_Data.xlsx' # 输出为 xlsx

def load_chinese_strings(loc_dir):
    """
    加载所有汉化文本，返回 {key: text} 字典
    """
    strings_map = {}
    if not os.path.exists(loc_dir):
        print(f"警告: 找不到汉化目录 {loc_dir}")
        return strings_map

    print(f"正在加载汉化文件 ({loc_dir})...")
    for root_dir, _, files in os.walk(loc_dir):
        for file in files:
            if file.lower().endswith('.xml'):
                file_path = os.path.join(root_dir, file)
                try:
                    tree = ET.parse(file_path)
                    root = tree.getroot()
                    for string_node in root.iter('string'):
                        key = string_node.get('id')
                        text = string_node.get('text')
                        if key and text:
                            strings_map[key] = text
                except Exception:
                    pass 
    
    print(f"汉化库加载完毕，共 {len(strings_map)} 条数据。")
    return strings_map

def parse_raw_name(raw_text):
    """
    解析原始名称，返回 (Key, EnglishName)
    例如: "{=yAchbdS0}Asakura Yoshikage" -> ('yAchbdS0', 'Asakura Yoshikage')
    如果没有 Key，返回 (None, raw_text)
    """
    if not raw_text:
        return None, ""
    
    match = re.search(r'\{=(.*?)\}(.*)', raw_text)
    if match:
        return match.group(1), match.group(2).strip()
    return None, raw_text

def process_data():
    # 1. 加载汉化
    cn_map = load_chinese_strings(DIR_CN)
    
    # 2. 初始化英雄数据字典
    # 结构: { 'hero_id': {'raw_name': str, 'cn_name': str, 'culture': str, 'clan': str} }
    heroes_data = {}

    def get_or_create_hero(hero_id):
        if hero_id not in heroes_data:
            heroes_data[hero_id] = {
                'raw_name': '',
                'cn_name': '',
                'culture': '',
                'clan': ''
            }
        return heroes_data[hero_id]

    # 3. 处理 Heroes 文件夹 (主要获取 家族/Clan 和 名字)
    if os.path.exists(DIR_HEROES):
        print(f"正在扫描 {DIR_HEROES} 文件夹...")
        for root_dir, _, files in os.walk(DIR_HEROES):
            for file in files:
                if file.lower().endswith('.xml'):
                    try:
                        tree = ET.parse(os.path.join(root_dir, file))
                        root = tree.getroot()
                        # Heroes 文件结构: <Hero id="..." text="..." faction="...">
                        if root.tag == 'Heroes':
                            for node in root.iter('Hero'):
                                h_id = node.get('id')
                                raw_text = node.get('text')
                                faction = node.get('faction') # 这里对应家族 Clan

                                if not h_id or not raw_text:
                                    continue

                                key, en_name = parse_raw_name(raw_text)
                                # 只有有中文名的才处理
                                if key and key in cn_map:
                                    entry = get_or_create_hero(h_id)
                                    entry['raw_name'] = raw_text
                                    entry['cn_name'] = cn_map[key]
                                    if faction:
                                        entry['clan'] = faction
                    except Exception as e:
                        print(f"解析错误 {file}: {e}")

    # 4. 处理 Lords 文件夹 (主要获取 文化/Culture 和 名字)
    if os.path.exists(DIR_LORDS):
        print(f"正在扫描 {DIR_LORDS} 文件夹...")
        for root_dir, _, files in os.walk(DIR_LORDS):
            for file in files:
                if file.lower().endswith('.xml'):
                    try:
                        tree = ET.parse(os.path.join(root_dir, file))
                        root = tree.getroot()
                        # Lords 文件结构: <NPCCharacter id="..." name="..." culture="...">
                        if root.tag == 'NPCCharacters':
                            for node in root.iter('NPCCharacter'):
                                h_id = node.get('id')
                                raw_text = node.get('name') # 注意这里属性是 name
                                culture = node.get('culture')

                                if not h_id or not raw_text:
                                    continue
                                
                                key, en_name = parse_raw_name(raw_text)
                                # 只有有中文名的才处理
                                if key and key in cn_map:
                                    entry = get_or_create_hero(h_id)
                                    # 如果之前在 Heroes 没读到名字，这里补上，或者覆盖以确保一致
                                    entry['raw_name'] = raw_text
                                    entry['cn_name'] = cn_map[key]
                                    if culture:
                                        entry['culture'] = culture
                    except Exception as e:
                        print(f"解析错误 {file}: {e}")

    # 5. 输出 Excel
    print("正在生成 Excel...")
    wb = Workbook()
    ws = wb.active
    ws.title = "Heroes Data"
    
    # 写入表头
    ws.append(['ID', '原始名称 (Name)', '中文名称 (CN)', '所属文化 (Culture)', '所属家族 (Clan/Faction)'])

    # 写入数据
    count = 0
    for h_id, info in heroes_data.items():
        # 再次确认：只有字典里有中文名的才输出 (防止虽然建立了key但没匹配到中文的情况)
        if info['cn_name']:
            ws.append([
                h_id,
                info['raw_name'],
                info['cn_name'],
                info['culture'],
                info['clan']
            ])
            count += 1
    
    wb.save(OUTPUT_FILE)
    print(f"处理完成！共提取 {count} 名角色信息。")
    print(f"文件已保存为: {OUTPUT_FILE}")

if __name__ == '__main__':
    process_data()