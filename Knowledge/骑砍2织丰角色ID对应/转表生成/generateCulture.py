import pandas as pd
import re
import os

# --- 配置部分 ---
FILE_NAME = 'mb_shokuho_taikou_1.xlsx'
SHEET_NAME = 'Culture'

def generate_bannerlord_xml():
    # 1. 检查文件是否存在
    if not os.path.exists(FILE_NAME):
        print(f"错误：在当前目录下找不到文件 [{FILE_NAME}]")
        return

    print(f"正在读取 {FILE_NAME}...")

    try:
        # 2. 读取 Excel
        # header=1 表示使用 Excel 的第2行（索引为1）作为英文表头（Key）
        # 第一行（中文标题）会被自动跳过
        df = pd.read_excel(FILE_NAME, sheet_name=SHEET_NAME, header=1)
    except Exception as e:
        print(f"读取 Excel 失败: {e}")
        return

    # 去除列名的空格，防止 'ID ' 这种错误
    df.columns = df.columns.str.strip()

    # 3. 检查必要的列是否存在
    required_cols = ['ID', 'ChineseName', 'OtherName','LocozationName', 'IsMainCulture', 'IsShokuho']
    for col in required_cols:
        if col not in df.columns:
            print(f"错误：在 Excel 的第2行中找不到列名 [{col}]")
            print(f"检测到的列名: {df.columns.tolist()}")
            return

    # 存储生成的 XML 行
    xml_cultures = []
    xml_strings = []

    # 4. 遍历数据
    count = 0
    for index, row in df.iterrows():
        # --- 筛选逻辑：IsShokuho 为 0 ---
        # 考虑到 Excel 可能把 0 存为 数字0 或 字符串'0'
        is_shokuho = row['IsShokuho']
        
        # 如果是 NaN (空值) 则认为是0
        if pd.isna(is_shokuho):
            is_shokuho = 0
            
        # 转换为整数判断，如果不是 0 则跳过
        try:
            if int(is_shokuho) != 0:
                continue
        except ValueError:
            # 如果转换失败（比如单元格里写的是文本），也跳过
            continue

        # --- 提取数据 ---
        # 这里的列名对应 Excel 第2行的英文标题
        c_id = str(row['ID']).strip()
        c_cn_name = str(row['ChineseName']).strip()
        c_other_name = row['OtherName'] # 暂时不用
        c_loc_name = str(row['LocozationName']).strip()
        c_is_main = str(row['IsMainCulture']).strip()

        # 处理 Boolean 值 (Excel 中的 TRUE/FALSE 转为 xml 的 true/false)
        if c_is_main.upper() == 'TRUE':
            c_is_main_str = 'true'
        else:
            c_is_main_str = 'false'

        # --- 生成 spcultures.xml 内容 ---
        # 基础结构
        culture_line = f'\t<Culture id="{c_id}" name="{c_loc_name}" is_main_culture="{c_is_main_str}" can_have_settlement="true" />'
        xml_cultures.append(culture_line)

        # --- 生成 strings (汉化) 内容 ---
        # 从 {=my_Ninja}Ninja 中提取 ID
        # 正则解释：查找 {= 开头， } 结尾，中间的内容
        match = re.search(r'\{=(.*?)\}', c_loc_name)
        if match:
            string_id = match.group(1)
            # 生成汉化行
            trans_line = f'\t<string id="{string_id}" text="{c_cn_name}" />'
            xml_strings.append(trans_line)
        else:
            print(f"警告：行 {index+3} (ID: {c_id}) 的本地化名格式不正确: {c_loc_name}")

        count += 1

    # 5. 写入文件
    
    # 写入 output_spcultures.xml
    with open('output_spcultures.xml', 'w', encoding='utf-8') as f:    
        f.write('<?xml version="1.0" encoding="utf-8"?>\n')
        f.write('<SPCultures>\n')
        f.write('\n'.join(xml_cultures))
        f.write('\n</SPCultures>')
    
    # 写入 output_strings.xml
    with open('output_strings.xml', 'w', encoding='utf-8') as f:
        f.write('<?xml version="1.0" encoding="utf-8"?>\n')
        f.write('<base xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" type="string">\n')
        f.write('  <tags>\n    <tag language="简体中文" />\n  </tags>\n')
        f.write('<strings>\n')
        f.write('\n'.join(xml_strings))
        f.write('\n</strings>')
        f.write('</base>')

    print(f"\n成功处理！共生成 {count} 个文化条目。")
    print("文件已生成：")
    print("1. output_spcultures.xml (放入 ModuleData)")
    print("2. output_strings.xml    (放入 ModuleData/Languages/CNs)")

if __name__ == '__main__':
    generate_bannerlord_xml()