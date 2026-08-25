# -*- coding: utf-8 -*-
"""
🔴 已废弃（2026-08-25）：无前缀拼接，事件号跨剧本撞号。标准合并 = 同目录 merge_prefixed.py（输出 Knowledge/太阁事件包/TK5AllEvents_merged.txt）。
Created on Fri Dec 12 11:12:25 2025

@author: yaodongcheng
"""

import os
import glob

# 简单版本
def merge_e_files_simple(directory="."):
    """简单合并当前目录下以E开头的文件"""
    
    # 查找所有以E开头的文件
    e_files = glob.glob(os.path.join(directory, 'E*'))
    
    if not e_files:
        print("没有找到以E开头的文件")
        return
    
    # 合并文件
    with open("merged_E_files.txt", "w", encoding="utf-8") as out_file:
        for e_file in e_files:
            try:
                with open(e_file, "r", encoding="utf-8") as in_file:
                    # 写入文件名作为分隔
                    out_file.write(f"\n--- {os.path.basename(e_file)} ---\n")
                    out_file.write(in_file.read())
                    out_file.write("\n")  # 添加换行
                print(f"已添加: {os.path.basename(e_file)}")
            except:
                print(f"跳过: {os.path.basename(e_file)} (无法读取)")
    
    print(f"\n合并完成！共合并 {len(e_files)} 个文件")

# 使用示例
merge_e_files_simple(".")  # 当前目录