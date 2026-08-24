# -*- coding: utf-8 -*-
"""
Created on Mon Dec 15 14:14:56 2025

@author: yaodongcheng
"""

import os
import re
import xml.etree.ElementTree as ET
from openpyxl import Workbook

# --- 配置部分 ---
DIR_SETTLEMENTS = 'settlements'  # 据点文件夹
DIR_CN = 'CNs'                   # 汉化文件夹
OUTPUT_FILE = 'Settlement_Data_Output.xlsx' # 输出文件

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
    例如: "{=Key}Name" -> ('Key', 'Name')
    如果没有 Key，返回 (None, raw_text)
    """
    if not raw_text:
        return None, ""
    
    # 匹配 {=Key}Name 格式
    match = re.search(r'\{=(.*?)\}(.*)', raw_text)
    if match:
        return match.group(1), match.group(2).strip()
    
    # 也就是纯文本的情况
    return None, raw_text

def process_settlements():
    # 1. 加载汉化
    cn_map = load_chinese_strings(DIR_CN)
    
    # 2. 准备 Excel
    wb = Workbook()
    ws = wb.active
    ws.title = "Settlements"
    
    # 写入表头
    headers = ['据点ID (ID)', '原始名称 (Original Name)', '中文名称 (CN Name)', '所属文化 (Culture)','所属家族 (Owner)']
    ws.append(headers)
    
    processed_count = 0
    
    # 3. 遍历 settlements 文件夹
    if not os.path.exists(DIR_SETTLEMENTS):
        print(f"错误: 找不到据点目录 {DIR_SETTLEMENTS}")
        return

    print(f"正在扫描据点文件 ({DIR_SETTLEMENTS})...")
    for root_dir, _, files in os.walk(DIR_SETTLEMENTS):
        for file in files:
            if file.lower().endswith('.xml'):
                file_path = os.path.join(root_dir, file)
                try:
                    tree = ET.parse(file_path)
                    root = tree.getroot()
                    
                    # 遍历 <Settlement> 节点
                    for node in root.iter('Settlement'):
                        s_id = node.get('id')
                        
                        # 获取名称：有些模组用 name="{=...}"，有些用 text="{=...}"
                        raw_name = node.get('name') or node.get('text')
                        
                        # 获取文化：通常是 culture="Culture.xxx"
                        s_culture = node.get('culture')
                        
                        # 获取家族：通常是 culture="Culture.xxx"
                        s_owner = node.get('owner')
                        
                        if s_id and raw_name:
                            # 解析名称
                            key, eng_name = parse_raw_name(raw_name)
                            
                            # 获取中文名
                            cn_name = ""
                            if key and key in cn_map:
                                cn_name = cn_map[key]
                            
                            # 筛选逻辑：这里我们设定只要有ID和名字就提取，
                            # 如果你只想提取有汉化的据点，可以取消下面这行的注释:
                            # if not cn_name: continue

                            # 写入 Excel
                            ws.append([s_id, raw_name, cn_name, s_culture,s_owner])
                            processed_count += 1
                            
                except ET.ParseError:
                    print(f"XML 解析警告: 无法解析 {file}")
                except Exception as e:
                    print(f"读取警告 {file}: {e}")

    # 4. 保存文件
    wb.save(OUTPUT_FILE)
    print(f"处理完成！共提取 {processed_count} 个据点。")
    print(f"结果已保存至: {OUTPUT_FILE}")

if __name__ == '__main__':
    process_settlements()