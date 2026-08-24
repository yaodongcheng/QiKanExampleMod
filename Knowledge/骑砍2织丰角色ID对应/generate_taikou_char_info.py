import pandas as pd
from thefuzz import process, fuzz
import pykakasi
import collections

# === 配置区域 ===
FILE_NAME = 'your_file.xlsx'   # 请替换为你的文件名
OUTPUT_FILE = 'taikou_final_v9_inference.xlsx'
FUZZY_THRESHOLD = 80        # 模糊匹配阈值
CANDIDATE_LIMIT = 3         # 候补名单显示数量

# === 历史人物别名库 (人工修正) ===
HISTORICAL_ALIASES = {
    '木下秀吉': '丰臣秀吉',
    '羽柴秀吉': '丰臣秀吉',
    '松平元康': '德川家康',
    '长尾景虎': '上杉谦信',
    '大友义镇': '大友宗麟',
    '竹中重治': '竹中半兵卫',
    '黑田孝高': '黑田官兵卫',
    '山本晴幸': '山本勘助',
    '木下小一郎': '丰臣秀长',
    '斋藤利三': '斋藤利三',
    '真田幸村': '真田信繁',
 '真田信幸': '真田信之',
 '黑田如水': '黑田官兵卫',
}

# 初始化
kks = pykakasi.kakasi()

# === 全局字典 ===
# ID查重集合
ALL_IDS = set()
# 姓氏推断库: {'武田': {'Faction': 'clan_takeda', 'Culture': '...', 'RomajiSurname': 'Takeda'}}
SURNAME_DB = {}

def clean_text(text):
    """强力清洗：去除所有类型的空格"""
    if pd.isna(text): return ""
    return str(text).replace(' ', '').replace('\u3000', '').strip()

def get_pure_romaji(text):
    """纯计算罗马音 (用于名字部分)"""
    if not text: return ""
    try:
        result = kks.convert(text)
        # 仅仅简单拼接
        return ''.join([item['hepburn'] for item in result])
    except:
        return text

def build_surname_knowledge_base(df_mod):
    """
    扫描Mod数据，建立【前缀(姓氏) -> 家族/文化/罗马音】的映射库
    逻辑：取名字前2个字作为Key，统计该Key下出现最多的Faction和Culture
    """
    print("正在建立姓氏推断知识库...")
    temp_map = {} # Key: Surname2Char, Value: List of records

    for _, row in df_mod.iterrows():
        c_name = row['CleanName']
        faction = row['Faction']
        culture = row['Culture']
        e_name = str(row['EnglishName'])
        
        # 必须有名字且有家族信息才值得学习
        if not c_name or len(c_name) < 2 or not faction:
            continue
            
        # 提取前两个字作为疑似姓氏 (大多数日本姓氏为2字)
        surname_key = c_name[:2]
        
        # 尝试提取英文姓氏 (假设格式为 "Surname GivenName")
        parts = e_name.strip().split(' ')
        romaji_surname = parts[0] if parts else ""
        
        if surname_key not in temp_map:
            temp_map[surname_key] = {'factions': [], 'cultures': [], 'romaji': []}
        
        temp_map[surname_key]['factions'].append(faction)
        if culture: temp_map[surname_key]['cultures'].append(culture)
        if romaji_surname: temp_map[surname_key]['romaji'].append(romaji_surname)

    # 汇总数据：取众数（出现最多次的）
    for k, v in temp_map.items():
        if not v['factions']: continue
        
        # 获取出现最多次的 Faction
        most_common_faction = collections.Counter(v['factions']).most_common(1)[0][0]
        # 获取出现最多次的 Culture (如果有)
        most_common_culture = collections.Counter(v['cultures']).most_common(1)[0][0] if v['cultures'] else ""
        # 获取出现最多次的 Romaji (如果有)
        most_common_romaji = collections.Counter(v['romaji']).most_common(1)[0][0] if v['romaji'] else ""
        
        SURNAME_DB[k] = {
            'Faction': most_common_faction,
            'Culture': most_common_culture,
            'RomajiSurname': most_common_romaji
        }
    
    print(f"姓氏库构建完成，共收录 {len(SURNAME_DB)} 个姓氏前缀。")

def generate_unique_id(base_id):
    """确保ID全局唯一，重复则追加 _1, _2"""
    # 转小写，去空格
    clean_id = base_id.lower().replace(' ', '_').strip()
    # 移除连字符等特殊符号，只留字母数字下划线
    import re
    clean_id = re.sub(r'[^a-z0-9_]', '', clean_id)
    
    if not clean_id: clean_id = "unknown_hero"
    
    final_id = clean_id
    counter = 1
    while final_id in ALL_IDS:
        final_id = f"{clean_id}_{counter}"
        counter += 1
    final_id = f"lord_1_{final_id}"
    ALL_IDS.add(final_id)
    return final_id

def main():
    print(f"=== 开始处理 (v9 智能推断版) ===")
    
    # 1. 读取数据
    try:
        df_mod = pd.read_excel(FILE_NAME, sheet_name='shokuho', dtype=str).fillna('')
        df_tk = pd.read_excel(FILE_NAME, sheet_name='taikou5', dtype=str).fillna('')
    except Exception as e:
        print(f"读取失败: {e}")
        return

    # 2. 预处理 Mod 数据
    df_mod['CleanName'] = df_mod['ChineseName'].apply(clean_text)
    
    # 记录已有的 Mod ID，防止新生成的冲突
    global ALL_IDS
    valid_ids = df_mod['StringId'].dropna().unique()
    for vid in valid_ids:
        if vid: ALL_IDS.add(str(vid).lower())

    # 3. 建立映射和知识库
    mod_lookup = {}
    mod_names = []
    
    # 建立 Mod 查找表
    for idx, row in df_mod.iterrows():
        name = row['CleanName']
        if name:
            mod_lookup[name] = row
            mod_names.append(name)
            
    # 建立姓氏推断库
    build_surname_knowledge_base(df_mod)

    # 4. 处理太阁数据
    results = []
    
    print(f"正在处理 {len(df_tk)} 名太阁武将...")

    for idx, row in df_tk.iterrows():
        original_name = clean_text(row['Name']) # 太阁原名
        # 1. 别名转换
        search_name = HISTORICAL_ALIASES.get(original_name, original_name)
        
        match_data = {}
        match_type = "未匹配"
        candidates_str = ""
        
        # --- 匹配逻辑 Level 1: 精确匹配 ---
        if search_name in mod_lookup:
            mod_row = mod_lookup[search_name]
            match_data = mod_row.to_dict()
            match_type = "精确匹配"
            match_score = 100
            match_name = search_name
            
        else:
            # --- 匹配逻辑 Level 2: 模糊匹配 ---
            best_match, score = process.extractOne(search_name, mod_names, scorer=fuzz.ratio) or (None, 0)
            
            # 获取候补名单
            candidates = process.extract(search_name, mod_names, limit=CANDIDATE_LIMIT, scorer=fuzz.ratio)
            candidates_str = ", ".join([f"{n}({s})" for n, s in candidates])
            
            if score >= FUZZY_THRESHOLD:
                mod_row = mod_lookup[best_match]
                match_data = mod_row.to_dict()
                match_type = "模糊匹配"
                match_score = score
                match_name = best_match
            else:
                # --- 匹配逻辑 Level 3: 姓氏推断 (同姓继承) ---
                match_score = score
                match_name = ""
                # 尝试取前2字作为姓氏
                surname_key = search_name[:2]
                
                if surname_key in SURNAME_DB:
                    # 命中同姓！继承家族和文化
                    inference = SURNAME_DB[surname_key]
                    match_type = "姓氏推断"
                    
                    # 构造推断数据
                    match_data = {
                        'Faction': inference['Faction'],
                        'Culture': inference['Culture'],
                        'EnglishName': '', # 名字不同，不能照搬英文全名
                    }
                    
                    # 智能拼接罗马音: 继承的姓 + 计算的名
                    # 假设 search_name = "真田幸村" (未匹配), surname_key="真田"
                    # 剩下的名字 = "幸村"
                    given_name_part = search_name[2:]
                    romaji_surname = inference['RomajiSurname']
                    romaji_given = get_pure_romaji(given_name_part).capitalize()
                    
                    inferred_romaji_full = f"{romaji_surname} {romaji_given}".strip()
                    
                else:
                    match_type = "需新建"

        # --- 结果组装 ---
        
        # 1. 确定 ID
        if match_type in ["精确匹配", "模糊匹配"] and match_data.get('StringId'):
            final_id = match_data['StringId'] # 复用现有ID
        else:
            # 需要生成 ID
            if match_type == "姓氏推断":
                # 使用推断出的罗马音
                base_romaji = (inferred_romaji_full)
            else:
                # 全自动计算罗马音
                base_romaji = (get_pure_romaji(original_name))
            
            final_id = generate_unique_id(base_romaji)

        # 2. 确定 Faction/Culture
        final_faction = match_data.get('Faction', '')
        final_culture = match_data.get('Culture', '')
        
        # 3. 确定 罗马音 (用于参考)
        mod_ref_english = match_data.get('EnglishName', '')
        if match_type == "姓氏推断":
            final_romaji = inferred_romaji_full
        elif mod_ref_english:
            final_romaji = mod_ref_english
        else:
            final_romaji = get_pure_romaji(original_name) # 兜底

        # 输出行
        out_row = {
            'TaikouName': original_name,
            'MatchStatus': match_type,     # 状态：精确/模糊/推断/需新建
            'MatchedWith': match_name,     # 匹配到的织丰名字
            'Score': match_score,          # 分数
            
            'Final_ID': final_id,          # 最终决定的ID
            'Final_Faction': final_faction, # 最终家族
            'Final_Culture': final_culture, # 最终文化
            'Final_Romaji': final_romaji,   # 最终罗马音
            
            'Mod_EnglishName': match_data.get('EnglishName', ''), # 参考：Mod原版英文
            'Candidates': candidates_str    # 候补参考
        }
        results.append(out_row)

    # 5. 保存结果
    df_out = pd.DataFrame(results)
    df_out.to_excel(OUTPUT_FILE, index=False)
    print(f"处理完成！结果已保存至: {OUTPUT_FILE}")
    print("请特别检查 MatchStatus 为 '姓氏推断' 的行，确认家族归属是否正确。")

if __name__ == '__main__':
    main()