# -*- coding: utf-8 -*-
"""
Created on Fri Dec 12 09:46:43 2025
Modified on Sun Dec 14 16:54:11 2025

@author: yaodongcheng
@modifier: gemini-3-pro
"""

import os
import xml.etree.ElementTree as ET
import csv
import re

# ================= 配置区域 =================
# 请确保这些路径相对于脚本文件是正确的，或者修改为绝对路径
CN_LOC_FILE = 'CNs/addition_2_CNs.xml'            # 中文本地化文件
EN_LOC_FILE = 'module_localization_strings.xml' # 英文本地化文件
HERO_DIR = 'heroes'                            # 包含 Hero 定义的文件夹
LORD_DIR = 'lords'                            # 包含 NPCCharacter 定义的文件夹
OUTPUT_CSV = 'character_names_table.csv'      # 输出文件名
# ===========================================

def load_localization(file_path):
    """
    读取本地化文件，返回一个字典 {string_id: text}
    """
    loc_map = {}
    if not os.path.exists(file_path):
        print(f"[警告] 未找到文件: {file_path}，将跳过加载。")
        return loc_map

    try:
        tree = ET.parse(file_path)
        root = tree.getroot()
        
        for string_node in root.iter('string'):
            key = string_node.get('id')
            text = string_node.get('text')
            if key and text:
                loc_map[key] = text
    except ET.ParseError:
        print(f"[错误] 无法解析 XML: {file_path}")
    except Exception as e:
        print(f"[错误] 读取 {file_path} 时发生未知错误: {e}")
        
    return loc_map

def parse_character_files(directory, tag_name, id_attr, name_attr, collected_data):
    """
    遍历目录下的XML文件，提取角色ID、名称、文化和家族
    collected_data: 结构变更为 {char_id: {'raw_name': str, 'faction': str, 'culture': str}}
    """
    if not os.path.exists(directory):
        print(f"[警告] 目录不存在: {directory}")
        return

    for filename in os.listdir(directory):
        if not filename.endswith('.xml'):
            continue
            
        file_path = os.path.join(directory, filename)
        try:
            tree = ET.parse(file_path)
            root = tree.getroot()
            
            # 查找所有目标标签
            for node in root.iter(tag_name):
                char_id = node.get(id_attr)
                raw_name = node.get(name_attr)
                
                # 获取 Faction 和 Culture 属性，如果没有则为空字符串
                # 注意：XML中属性名通常是小写
                faction = node.get('faction', '')
                culture = node.get('culture', '')
                
                if char_id and raw_name:
                    # 如果该ID不存在，初始化数据
                    if char_id not in collected_data:
                        collected_data[char_id] = {
                            'raw_name': raw_name,
                            'faction': faction,
                            'culture': culture
                        }
                    else:
                        # 如果ID已存在（例如先扫描了Lords又扫描Heros），则尝试补全缺失的信息
                        # 比如 NPCCharacter 可能有 culture 但没 faction，而 Hero 有 faction
                        existing_data = collected_data[char_id]
                        
                        if not existing_data['faction'] and faction:
                            existing_data['faction'] = faction
                        
                        if not existing_data['culture'] and culture:
                            existing_data['culture'] = culture
                            
                        # 如果需要，这里也可以更新 raw_name，目前保持先入为主
                        
        except ET.ParseError:
            print(f"[警告] 解析错误: {filename}")

def main():
    print("正在读取本地化文件...")
    cn_dict = load_localization(CN_LOC_FILE)
    en_dict = load_localization(EN_LOC_FILE)
    
    print(f"已加载中文词条: {len(cn_dict)} 条")
    print(f"已加载英文词条: {len(en_dict)} 条")

    # 2. 扫描角色文件
    # 结果字典结构: {StringId: {'raw_name': ..., 'faction': ..., 'culture': ...}}
    character_data = {}

    print("正在扫描 Lords (NPCCharacter)...")
    # Lords 通常包含 culture 属性
    parse_character_files(LORD_DIR, 'NPCCharacter', 'id', 'name', character_data)

    print("正在扫描 Heroes (Hero)...")
    # Heroes 通常包含 faction 属性
    parse_character_files(HERO_DIR, 'Hero', 'id', 'text', character_data)

    print(f"共找到 {len(character_data)} 个唯一角色 ID。")

    # 3. 处理数据并写入 CSV
    print(f"正在生成 CSV: {OUTPUT_CSV} ...")
    
    pattern = re.compile(r'\{=(.*?)\}(.*)')

    with open(OUTPUT_CSV, 'w', newline='', encoding='utf-8-sig') as csvfile:
        writer = csv.writer(csvfile)
        # 写入表头，新增 Faction 和 Culture
        writer.writerow(['StringId', 'LocalizationId', 'EnglishName', 'ChineseName', 'Faction', 'Culture'])

        for string_id, info in character_data.items():
            raw_name = info['raw_name']
            faction_id = info['faction']
            culture_id = info['culture']
            
            loc_id = ""
            english_name = ""
            chinese_name = ""
            
            # 解析原始名称字符串
            match = pattern.search(raw_name)
            
            if match:
                loc_id = match.group(1) 
                fallback_name = match.group(2).strip()
                
                english_name = en_dict.get(loc_id, fallback_name)
                chinese_name = cn_dict.get(loc_id, "")
            else:
                loc_id = "N/A"
                english_name = raw_name
                chinese_name = raw_name 

            # 写入一行，包含6列数据
            writer.writerow([string_id, loc_id, english_name, chinese_name, faction_id, culture_id])

    print("完成！")

if __name__ == "__main__":
    main()