# -*- coding: utf-8 -*-
"""
Created on Mon Dec 15 11:00:09 2025

@author: yaodongcheng
"""

import os
import re
import xml.etree.ElementTree as ET
from openpyxl import Workbook

# --- 配置部分 ---
DIR_KINGDOMS = 'spkingdoms'    # 王国文件夹
DIR_CULTURES = 'spcultures'    # 文化文件夹
DIR_CN = 'CNs'                 # 汉化文件夹

FILE_OUT_KINGDOM = 'Kingdom_Data_Output.xlsx' # 王国输出文件
FILE_OUT_CULTURE = 'Culture_Data_Output.xlsx' # 文化输出文件

def load_chinese_strings(loc_dir):
    """
    遍历 CNs 文件夹，构建 {key: 中文文本} 的映射字典
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
                    # 遍历所有 string 节点
                    for string_node in root.iter('string'):
                        key = string_node.get('id')
                        text = string_node.get('text')
                        if key and text:
                            strings_map[key] = text
                except Exception as e:
                    pass # 忽略解析错误的个别文件
    
    print(f"汉化加载完毕，共 {len(strings_map)} 条条目。")
    return strings_map

def extract_key_from_raw(raw_text):
    """
    从 {=Key}EnglishName 中提取 Key
    """
    if not raw_text:
        return None
    match = re.search(r'\{=(.*?)\}', raw_text)
    if match:
        return match.group(1)
    return None

def get_cn_name(raw_text, strings_map):
    """
    根据原始文本中的 Key 获取中文，如果没有 Key 或找不到中文，返回空字符串
    """
    key = extract_key_from_raw(raw_text)
    if key and key in strings_map:
        return strings_map[key]
    return "未找到汉化" if key else ""

def process_kingdoms(strings_map):
    """
    处理王国数据并导出 Excel
    """
    print(f"正在处理王国数据 ({DIR_KINGDOMS})...")
    wb = Workbook()
    ws = wb.active
    ws.title = "Kingdoms"
    # 表头：包含 ID, 原始名称, 中文名称, 简称为(ShortName), 统治者头衔(Title), 所属文化
    ws.append(["Kingdom ID", "Name (Raw)", "Name (CN)", "Short Name (CN)", "Title (CN)", "Culture ID"])

    if not os.path.exists(DIR_KINGDOMS):
        print(f"警告: 找不到王国目录 {DIR_KINGDOMS}")
        return

    for root_dir, _, files in os.walk(DIR_KINGDOMS):
        for file in files:
            if file.lower().endswith('.xml'):
                file_path = os.path.join(root_dir, file)
                try:
                    tree = ET.parse(file_path)
                    root = tree.getroot()
                    
                    # 查找 Kingdom 节点
                    for kingdom in root.iter('Kingdom'):
                        k_id = kingdom.get('id')
                        raw_name = kingdom.get('name')
                        raw_short_name = kingdom.get('short_name') # 王国简称
                        raw_title = kingdom.get('title')           # 统治者头衔
                        culture_ref = kingdom.get('culture')       # 关联的文化ID

                        # 处理 Culture 字段，通常格式为 "Culture.japan"，去掉前缀
                        culture_id = culture_ref.split('.')[-1] if culture_ref else ""

                        # 获取中文
                        cn_name = get_cn_name(raw_name, strings_map)
                        cn_short = get_cn_name(raw_short_name, strings_map)
                        cn_title = get_cn_name(raw_title, strings_map)

                        ws.append([k_id, raw_name, cn_name, cn_short, cn_title, culture_id])

                except ET.ParseError:
                    print(f"解析错误: {file}")

    wb.save(FILE_OUT_KINGDOM)
    print(f"王国数据已保存至: {FILE_OUT_KINGDOM}")

def process_cultures(strings_map):
    """
    处理文化数据并导出 Excel
    """
    print(f"正在处理文化数据 ({DIR_CULTURES})...")
    wb = Workbook()
    ws = wb.active
    ws.title = "Cultures"
    # 表头
    ws.append(["Culture ID", "Name (Raw)", "Name (CN)", "Is Main Culture"])

    if not os.path.exists(DIR_CULTURES):
        print(f"警告: 找不到文化目录 {DIR_CULTURES}")
        return

    for root_dir, _, files in os.walk(DIR_CULTURES):
        for file in files:
            if file.lower().endswith('.xml'):
                file_path = os.path.join(root_dir, file)
                try:
                    tree = ET.parse(file_path)
                    root = tree.getroot()
                    
                    # 查找 Culture 节点
                    for culture in root.iter('Culture'):
                        c_id = culture.get('id')
                        raw_name = culture.get('name')
                        is_main = culture.get('is_main_culture', 'false')

                        cn_name = get_cn_name(raw_name, strings_map)

                        ws.append([c_id, raw_name, cn_name, is_main])

                except ET.ParseError:
                    print(f"解析错误: {file}")

    wb.save(FILE_OUT_CULTURE)
    print(f"文化数据已保存至: {FILE_OUT_CULTURE}")

if __name__ == "__main__":
    # 1. 统一加载汉化文件（只加载一次，节省时间）
    cn_map = load_chinese_strings(DIR_CN)
    
    # 2. 导出王国
    process_kingdoms(cn_map)
    
    # 3. 导出文化
    process_cultures(cn_map)
    
    print("\n所有任务完成。")