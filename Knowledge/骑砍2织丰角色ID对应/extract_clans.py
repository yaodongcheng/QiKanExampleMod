# -*- coding: utf-8 -*-
"""
Created on Mon Dec 15 10:46:11 2025

@author: yaodongcheng
"""

import os
import re
import xml.etree.ElementTree as ET
from openpyxl import Workbook

# 配置文件夹名称
CLANS_DIR = 'spclans'
LOC_DIR = 'CNs'
OUTPUT_FILE = 'Clan_Data_Output.xlsx'

def load_chinese_strings(loc_dir):
    """
    遍历 CNs 文件夹，提取所有 <string id="key" text="中文" /> 的数据
    返回一个字典: {'key': '中文名'}
    """
    strings_map = {}
    
    if not os.path.exists(loc_dir):
        print(f"警告: 找不到目录 {loc_dir}")
        return strings_map

    print(f"正在加载汉化文件 ({loc_dir})...")
    for root_dir, _, files in os.walk(loc_dir):
        for file in files:
            if file.lower().endswith('.xml'):
                file_path = os.path.join(root_dir, file)
                try:
                    tree = ET.parse(file_path)
                    root = tree.getroot()
                    
                    # 骑砍2的语言文件通常结构为 <base> -> <strings> -> <string>
                    # 或者直接在根节点下有 string 节点，这里做通用遍历
                    for string_node in root.iter('string'):
                        key = string_node.get('id')
                        text = string_node.get('text')
                        if key and text:
                            strings_map[key] = text
                except ET.ParseError:
                    print(f"解析错误: {file_path}")
                except Exception as e:
                    print(f"读取错误 {file_path}: {e}")
    
    print(f"共加载了 {len(strings_map)} 条汉化文本。")
    return strings_map

def extract_key_from_name(raw_name):
    """
    从形如 {=1O30HvkU}Bessho-shi 的字符串中提取 key (1O30HvkU)
    """
    if not raw_name:
        return None
    # 正则匹配 {=...} 里面的内容
    match = re.search(r'\{=(.*?)\}', raw_name)
    if match:
        return match.group(1)
    return None

def process_clans(clans_dir, strings_map):
    """
    遍历 spclans 文件夹，解析家族数据并匹配中文名
    """
    data_rows = [] # 存储最终数据 [id, super_faction, raw_name, cn_name]
    
    if not os.path.exists(clans_dir):
        print(f"错误: 找不到目录 {clans_dir}")
        return data_rows

    print(f"正在处理家族文件 ({clans_dir})...")
    for root_dir, _, files in os.walk(clans_dir):
        for file in files:
            if file.lower().endswith('.xml'):
                file_path = os.path.join(root_dir, file)
                try:
                    tree = ET.parse(file_path)
                    root = tree.getroot()
                    
                    # 家族通常定义为 <Faction> 标签
                    for faction in root.iter('Faction'):
                        clan_id = faction.get('id')
                        raw_name = faction.get('name')
                        
                        owner = faction.get('owner')
                        super_faction = faction.get('super_faction')
                        
                        # 如果没有ID或Name，可能不是有效的数据行
                        if not clan_id:
                            continue

                        # 获取中文名
                        cn_name = "未找到汉化"
                        loc_key = extract_key_from_name(raw_name)
                        
                        if loc_key and loc_key in strings_map:
                            cn_name = strings_map[loc_key]
                        elif loc_key is None:
                            # 如果名字里不包含 {=...}，则直接使用原名作为参考，或者留空
                            cn_name = raw_name 
                        
                        data_rows.append([clan_id, super_faction, raw_name, cn_name,owner])
                        
                except ET.ParseError:
                    print(f"XML解析错误: {file_path}")
                except Exception as e:
                    print(f"处理文件错误 {file_path}: {e}")
    
    return data_rows

def save_to_excel(data, output_filename):
    """
    将数据保存为 Excel 文件
    """
    wb = Workbook()
    ws = wb.active
    ws.title = "Clan Data"
    
    # 写入表头
    headers = ["家族ID (id)", "所属王国 (super_faction)", "原始名称 (name)", "中文名称 (Chinese)"]
    ws.append(headers)
    
    # 写入数据
    for row in data:
        ws.append(row)
    
    # 简单的列宽调整
    ws.column_dimensions['A'].width = 30
    ws.column_dimensions['B'].width = 30
    ws.column_dimensions['C'].width = 40
    ws.column_dimensions['D'].width = 20
    
    try:
        wb.save(output_filename)
        print(f"成功! 文件已生成: {output_filename}")
    except PermissionError:
        print(f"失败: 无法写入 {output_filename}，请检查文件是否被 Excel 打开。")

def main():
    # 1. 加载汉化字典
    translations = load_chinese_strings(LOC_DIR)
    
    # 2. 提取家族数据并匹配
    clan_data = process_clans(CLANS_DIR, translations)
    
    # 3. 输出结果
    if clan_data:
        print(f"共提取到 {len(clan_data)} 个家族数据。")
        save_to_excel(clan_data, OUTPUT_FILE)
    else:
        print("未提取到任何家族数据。")

if __name__ == "__main__":
    main()