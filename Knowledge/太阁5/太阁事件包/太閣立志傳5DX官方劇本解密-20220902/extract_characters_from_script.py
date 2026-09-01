import re
import os
import sys
from collections import Counter
import unicodedata

def normalize_text(text):
    """
    标准化文本，将全角字符转换为半角字符，统一处理
    """
    # 首先规范化Unicode
    text = unicodedata.normalize('NFKC', text)
    
    # 将全角字母、数字、符号转换为半角
    result = []
    for char in text:
        code = ord(char)
        # 全角字母、数字、符号范围
        if 0xFF01 <= code <= 0xFF5E:
            result.append(chr(code - 0xFEE0))
        else:
            result.append(char)
    
    return ''.join(result)

def extract_characters_from_script(file_path):
    """
    从剧本文件中提取所有角色名
    
    Args:
        file_path: 剧本文件路径
    
    Returns:
        排序后的角色列表
    """
    characters = set()
    line_count = 0
    processed_line_count = 0
    
    # 定义各种模式的正则表达式 - 更精确的匹配
    patterns = [
        # case1: {角色名.属性} - 确保角色名不包含特殊字符
        r'\{([\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff][\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff\s]*?)\.\w+?\}',
        # case2: (角色名.属性) - 确保角色名不包含特殊字符
        r'\(([\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff][\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff\s]*?)\.[^)]*?\)',
        # case3: (角色名) 没有点 - 确保是完整的括号内容
        r'^\(([\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff][\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff\s]*?)\)$',
        # case4: (人物::角色名.属性) 或 (人物::角色名)
        r'\(人物::([\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff][\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff\s]*?)(?:\.[^)]*?)?\)',
        # case5: 對話:(角色名,角色名) - 精确匹配
        r'^對話:\s*\(([\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff][\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff\s]*?)\s*,\s*([\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff][\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff\s]*?)\)$',
        # case6: 武將死亡:(角色名)
        r'^武將死亡:\s*\(([\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff][\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff\s]*?)\)$',
        # case7: 主人公分歧:(角色名)
        r'^主人公分歧:\s*\(([\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff][\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff\s]*?)\)$',
    ]
    
    # 简单模式（用于行内匹配）
    simple_patterns = [
        # 匹配 {角色名.属性} 或 (角色名.属性) 在行内任意位置
        r'[{(]([\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff][\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff\s]*?)\.[^)}]*?[})]',
        # 匹配 (人物::角色名) 或 (人物::角色名.属性)
        r'\(人物::([\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff][\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff\s]*?)(?:\.[^)]*?)?\)',
    ]
    
    # 特殊模式处理
    special_patterns = [
        # 处理 (角色名) 格式，但需要排除一些常见非角色名
        r'\(([\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff]{2,5})\)',
    ]
    
    # 排除列表（非角色名的常见词）
    exclude_list = ['一人稱', 'size', '無效', '宴席', '事件ＣＧ', '圓形擦出', 
                   '主人公', '主人', 'ＭＰ', 'ｇ', 'g', 'mp', '主人公', '主人公', 
                   '無効', '無效', '無', '有', '是', '否', '真', '假']
    
    # 读取文件
    try:
        with open(file_path, 'r', encoding='utf-8') as file:
            lines = file.readlines()
    except UnicodeDecodeError:
        # 如果utf-8失败，尝试其他编码
        try:
            with open(file_path, 'r', encoding='gbk') as file:
                lines = file.readlines()
        except:
            try:
                with open(file_path, 'r', encoding='shift-jis') as file:
                    lines = file.readlines()
            except:
                print(f"错误: 无法读取文件 {file_path}，请检查文件编码")
                return []
    
    print(f"正在分析文件: {file_path}")
    print(f"总行数: {len(lines)}")
    print("-" * 50)
    
    # 先收集所有可能的角色名
    potential_characters = set()
    
    for line_num, line in enumerate(lines, 1):
        line = line.strip()
        if not line:
            continue
            
        line_count += 1
        
        # 标准化行文本
        normalized_line = normalize_text(line)
        
        # 跳过注释行（以//开头）
        if normalized_line.startswith('//'):
            continue
        
        # 方法1: 使用精确的正则表达式匹配
        for pattern in patterns:
            matches = re.findall(pattern, normalized_line)
            if matches:
                processed_line_count += 1
                # 处理不同类型的匹配结果
                if pattern == r'^對話:\s*\(([\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff][\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff\s]*?)\s*,\s*([\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff][\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff\s]*?)\)$':
                    # 對話模式有两个角色名
                    for match in matches:
                        if isinstance(match, tuple):
                            for character in match:
                                character = character.strip()
                                if character and len(character) >= 2:  # 至少2个字符
                                    potential_characters.add(character)
                        else:
                            character = match.strip()
                            if character and len(character) >= 2:
                                potential_characters.add(character)
                else:
                    for match in matches:
                        if isinstance(match, tuple):
                            # 如果匹配结果是元组，取第一个元素
                            for item in match:
                                if item:
                                    character = item.strip()
                                    if character and len(character) >= 2:
                                        potential_characters.add(character)
                        else:
                            character = match.strip()
                            if character and len(character) >= 2:
                                potential_characters.add(character)
        
        # 方法2: 使用简单模式匹配行内内容
        for pattern in simple_patterns:
            matches = re.findall(pattern, normalized_line)
            for match in matches:
                if isinstance(match, tuple):
                    for character in match:
                        character = character.strip()
                        if character and len(character) >= 2:
                            potential_characters.add(character)
                else:
                    character = match.strip()
                    if character and len(character) >= 2:
                        potential_characters.add(character)
        
        # 方法3: 查找所有括号中的内容（备用）
        if '(' in normalized_line or '{' in normalized_line:
            # 查找 {...} 或 (...)
            bracket_matches = re.findall(r'[{(]([^)}]+)[})]', normalized_line)
            for match in bracket_matches:
                match = match.strip()
                
                # 检查是否有"人物::"前缀
                if match.startswith('人物::'):
                    parts = match[4:].split('.')  # 移除"人物::"，然后按点分割
                    if parts and parts[0] and len(parts[0]) >= 2:
                        potential_characters.add(parts[0])
                # 检查是否有"."分隔符
                elif '.' in match:
                    parts = match.split('.')
                    if parts and parts[0] and len(parts[0]) >= 2:
                        # 检查是否为排除词
                        if parts[0] not in exclude_list:
                            potential_characters.add(parts[0])
                else:
                    # 没有点，直接检查是否为排除词
                    if match and len(match) >= 2 and match not in exclude_list:
                        # 使用特殊模式验证
                        for pattern in special_patterns:
                            if re.match(pattern, f'({match})'):
                                potential_characters.add(match)
                                break
    
    # 过滤角色名：排除明显不是角色名的词
    for char in list(potential_characters):
        # 排除单个字符或数字
        if len(char) < 2 or char.isdigit():
            potential_characters.discard(char)
            continue
            
        # 排除包含特殊字符的（除了允许的字符）
        if re.search(r'[^\w\u4e00-\u9fff\u3040-\u309f\u30a0-\u30ff\s]', char):
            potential_characters.discard(char)
            continue
            
        # 排除排除列表中的词
        if char in exclude_list:
            potential_characters.discard(char)
            continue
    
    # 转换为排序列表
    characters = sorted(list(potential_characters))
    
    print(f"处理了 {processed_line_count} 行有匹配内容的行")
    print(f"总共有 {len(lines)} 行，其中 {line_count} 行非空")
    
    return characters

def analyze_character_frequency(file_path):
    """
    分析角色出现的频率
    
    Args:
        file_path: 剧本文件路径
    
    Returns:
        角色频率计数器
    """
    character_counter = Counter()
    
    # 提取所有角色
    characters = extract_characters_from_script(file_path)
    
    if not characters:
        print("没有找到任何角色")
        return character_counter
    
    # 读取文件内容
    try:
        with open(file_path, 'r', encoding='utf-8') as file:
            content = file.read()
    except UnicodeDecodeError:
        try:
            with open(file_path, 'r', encoding='gbk') as file:
                content = file.read()
        except:
            try:
                with open(file_path, 'r', encoding='shift-jis') as file:
                    content = file.read()
            except:
                print(f"错误: 无法读取文件 {file_path}，请检查文件编码")
                return character_counter
    
    # 标准化内容
    normalized_content = normalize_text(content)
    
    # 为每个角色创建搜索模式
    for character in characters:
        # 创建多个可能的模式来匹配角色
        patterns = [
            # {角色名.属性}
            r'\{' + re.escape(character) + r'\.[^}]*\}',
            # (角色名.属性)
            r'\(' + re.escape(character) + r'\.[^)]*\)',
            # (人物::角色名) 或 (人物::角色名.属性)
            r'\(人物::' + re.escape(character) + r'(?:\.[^)]*)?\)',
            # 對話:(角色名,其他)
            r'對話:\s*\(' + re.escape(character) + r'[^)]*\)',
            # 武將死亡:(角色名)
            r'武將死亡:\s*\(' + re.escape(character) + r'\)',
            # 主人公分歧:(角色名)
            r'主人公分歧:\s*\(' + re.escape(character) + r'\)',
        ]
        
        count = 0
        for pattern in patterns:
            matches = re.findall(pattern, normalized_content)
            count += len(matches)
        
        character_counter[character] = count
    
    return character_counter

def main():
    """
    主函数：处理合并后的源事件文件（2026-08-25 起 = TK5AllEvents_merged.txt，旧 merged_E_files.txt 已删除）
    """
    # 获取当前脚本所在目录
    script_dir = os.path.dirname(os.path.abspath(__file__))
    
    # 构建文件路径
    default_file = "TK5AllEvents_merged.txt"  # 🔴 2026-08-25：标准源 = 带文件名前缀的单一合并文件（旧 merged_E_files.txt 已删除）
    file_path = os.path.join(script_dir, default_file)
    
    # 检查文件是否存在
    if not os.path.exists(file_path):
        print(f"错误: 找不到文件 '{file_path}'")
        print(f"请确保 '{default_file}' 文件和脚本在同一个目录下")
        
        # 显示当前目录下的文件
        print(f"\n当前目录下的文件:")
        for f in os.listdir(script_dir):
            if f.endswith('.txt'):
                print(f"  - {f}")
        
        sys.exit(1)
    
    # 检查是否显示频率统计
    show_frequency = len(sys.argv) > 1 and (sys.argv[1] in ['-f', '--frequency'])
    
    try:
        if show_frequency:
            # 显示角色出现频率
            frequency = analyze_character_frequency(file_path)
            
            if not frequency:
                print("没有找到任何角色，请检查文件格式是否正确")
                return
            
            print("\n角色出现频率统计:")
            print("-" * 60)
            
            # 按出现次数排序
            sorted_characters = sorted(frequency.items(), key=lambda x: x[1], reverse=True)
            
            total_appearances = 0
            for i, (character, count) in enumerate(sorted_characters, 1):
                print(f"{i:4}. {character:15} : {count:4}次")
                total_appearances += count
            
            print("-" * 60)
            print(f"总共找到 {len(frequency)} 个角色")
            print(f"角色总共出现 {total_appearances} 次")
            
            # 统计出现频率分布
            print("\n出现频率分布:")
            freq_dist = Counter(frequency.values())
            for freq in sorted(freq_dist.keys(), reverse=True):
                chars_with_freq = sum(1 for c, cnt in frequency.items() if cnt == freq)
                print(f"出现{freq:3}次的角色: {chars_with_freq:3}个")
            
            # 将频率结果保存到文件
            output_file = os.path.join(script_dir, "characters_frequency.txt")
            with open(output_file, 'w', encoding='utf-8') as f:
                f.write("剧本角色出现频率统计\n")
                f.write("=" * 60 + "\n")
                f.write(f"文件: {default_file}\n")
                f.write(f"提取时间: {os.path.getctime(file_path)}\n")
                f.write("=" * 60 + "\n\n")
                
                f.write("角色列表（按出现次数排序）:\n")
                f.write("-" * 40 + "\n")
                for i, (character, count) in enumerate(sorted_characters, 1):
                    f.write(f"{i:4}. {character:15} : {count:4}次\n")
                
                f.write("\n" + "=" * 60 + "\n")
                f.write(f"总共找到 {len(frequency)} 个角色\n")
                f.write(f"角色总共出现 {total_appearances} 次\n\n")
                
                f.write("出现频率分布:\n")
                for freq in sorted(freq_dist.keys(), reverse=True):
                    chars_with_freq = sum(1 for c, cnt in frequency.items() if cnt == freq)
                    f.write(f"出现{freq:3}次的角色: {chars_with_freq:3}个\n")
            
            print(f"\n频率统计结果已保存到: {output_file}")
            
        else:
            # 只显示角色列表
            characters = extract_characters_from_script(file_path)
            
            if not characters:
                print("没有找到任何角色，请检查文件格式是否正确")
                return
            
            print("\n找到的角色列表:")
            print("-" * 40)
            for i, character in enumerate(characters, 1):
                print(f"{i:4}. {character}")
            print("-" * 40)
            print(f"总共找到 {len(characters)} 个角色")
            
            # 将结果保存到文件
            output_file = os.path.join(script_dir, "characters_list.txt")
            with open(output_file, 'w', encoding='utf-8') as f:
                f.write("剧本角色列表\n")
                f.write("=" * 40 + "\n")
                f.write(f"文件: {default_file}\n")
                f.write(f"提取时间: {os.path.getctime(file_path)}\n")
                f.write("=" * 40 + "\n\n")
                
                for i, character in enumerate(characters, 1):
                    f.write(f"{i:4}. {character}\n")
                
                f.write("\n" + "=" * 40 + "\n")
                f.write(f"总计: {len(characters)} 个角色\n")
            print(f"结果已保存到: {output_file}")
            
    except Exception as e:
        print(f"处理文件时发生错误: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)

if __name__ == "__main__":
    print("=" * 60)
    print("剧本角色提取工具")
    print("=" * 60)
    
    # 检查是否有参数
    if len(sys.argv) > 1 and sys.argv[1] in ['-f', '--frequency', '-h', '--help']:
        if sys.argv[1] in ['-h', '--help']:
            print("\n使用方法:")
            print("  python script.py           # 提取角色列表")
            print("  python script.py -f        # 提取角色列表并显示出现频率")
            print("  python script.py --frequency  # 同上")
            print("\n说明:")
            print("  脚本会自动处理同一目录下的 TK5AllEvents_merged.txt 文件（2026-08-25 起，旧 merged_E_files.txt 已删除）")
            print("  结果会保存到 characters_list.txt 或 characters_frequency.txt")
            sys.exit(0)
        else:
            main()
    else:
        main()
    
    print("\n" + "=" * 60)
    print("处理完成！")
    input("按 Enter 键退出...")