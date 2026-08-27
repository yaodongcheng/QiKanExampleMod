# -*- coding: utf-8 -*-
"""生成 16 第一部分对照表（太阁5 词条 | 次数 | 骑砍2 落点）并替换 1.1-1.3 节。

🔴 v2（2026-08-27 结构性修复）：属性表从「属性名 → 单一侧名」改为「(域, 属性) → 侧名」二维——
   旧版正则 `::X.属性` 丢弃域前缀，导致跨域同名属性只登记一条且侧名域错配
   （大名家.本城 2298 次被登记成 人物域 Hero.home → 下游全部 🔴待注册）。
   同时新增「域::值」形态提取（身份枚举/狀況值/命名槽）与带参调用提取（函数候选），
   并在生成期跑全语料覆盖自检：**表外词条 = 生成失败，禁止带病产出**（下游不再可能出现待注册）。

🔴 v4（2026-08-27）：自检范围补齐「命令参数位 / 命令头值 / 台词插值」——
   旧版只解析 `域::X` 形态，命令裸参数（枚举/资源名/容器字段/触发名）与台词插值
   完全没有模型，5.4 万处词条游离在总表与自检之外。现在结构化解析每行
   `命令:[头值](参数,参数)…[[台词]]`，每个参数位声明收什么（CMD_ARG_SPEC），
   表外即生成失败。配套残渣扫描器：tools/registry_residue_scan.py（扣除法查漏）。
"""
import hashlib
import re
import sys
from collections import Counter

txt = open('Knowledge/太阁事件包/TK5AllEvents_merged.txt', encoding='utf-8').read()
# 🔴 提取前过滤注释行（# 开头——文件名/说明行含 事件:: 等字样且全角右括号不在排除集，
#   贪婪跨行匹配会污染词条：evm 就是 `# 文件内事件标志引用（事件::N）…EC500000.evm` 提取出来的，2026-08-27 用户裁定）
body = '\n'.join(l for l in txt.splitlines() if not l.strip().startswith('#'))

# ═══ 提取（v2：保留域维度）═══
domains = Counter(re.findall(r'([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6})::', body))
# (域, 属性) 对：`域::主体.属性`（属性名保留原域，跨域同名属性各行一条；含全角数字：武將２）
attr_pairs = Counter(re.findall(r'([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6})::[^.（()）]+\.([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ0-9０-９]+)', body))
# 域::值（无主体无点）：身份枚举 / 狀況值 / 命名槽（據點::主人公當主據點）——旧版零提取；
# 🔴 2026-08-27 用户裁定：lookahead 含全角/半角左括号——`主命::獲取貴重品（忍者）` = 具名值带
#   职业变体参数（武士/商人/海賊/忍者，167 条），值 = 括号前部分，参数留在例句原文（X( = 带参形态警惕）
domain_vals = Counter(re.findall(r'([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6})::([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ0-9０-９]{1,14})(?=[),，）(（])', body))
# 🔴 2026-08-27 用户裁定：`域::值.属性` 形态（流派::流派Ａ.宗家）——槽/具名值后跟属性访问，
#   值也要提取为域值；值须以非数字开头（主命屬性::5288.80 的数字主体 5288 不提取，防垃圾行）
domain_vals.update(re.findall(r'([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6})::([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ][一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ0-9０-９]{0,13})(?=\.)', body))
# 🔴 2026-08-27 patch13：同一模式再抓一次「主体」——纯数字属性位（域::X.27）要靠主体形态
#   三分类（真属性位 / 域值的数字ID后缀 / 转储原始数值引用），见 digit_attr_class()。
#   模式与 attr_pairs 逐字一致（只多一个捕获组），故 (域,属性) 对集恒等，不会多出/漏掉词条。
attr_triples = Counter(re.findall(r'([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6})::([^.（()）]+)\.([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ0-9０-９]+)', body))
ATTR_SUBJECTS = {}
for (_d, _s, _a), _c in attr_triples.items():
    ATTR_SUBJECTS.setdefault((_d, _a), Counter())[_s.strip().splitlines()[-1] if _s.strip() else ''] += _c

# 带参调用：`域::主体.属性(参数)` → 函数候选（外交同盟/全城壓制…）
calls = Counter(re.findall(r'([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6})::[^.（()）]+\.([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]+)\(', body))
cmds = Counter()
for line in txt.splitlines():
    m = re.match(r'^\s*([一-鿿぀-ヿＡ-Ｚａ-ｚA-Za-z]{2,8}):', line)
    if m:
        cmds[m.group(1)] += 1

# ═══ 域 42 → 骑砍2 落点 ═══
DOMAIN_MAP = {
    '人物': 'Hero::（引擎域）', '大名家': 'Clan::/Faction::（引擎域）', '城': 'Settlement::（引擎域）',
    '事件': 'Event::done（引擎域，调度器记录）', '勢力': 'Faction::（引擎域）', '據點': 'Settlement::（引擎域）',
    '軍團': 'Army::（02 PartyBrain 受控集合）', '身份': 'Identity:: 枚举值（17）', '變量': 'Variable::（引擎域）',
    '狀況': 'Time:: + Flag::（引擎域）', '真偽': '布尔字面量',
    '事件標誌': 'Flag::（引擎域）', '國': 'Settlement.province（Region，省级）', '日數計數器': 'Time::counter_N（编号计时器，N = 太阁编号）',
    '海賊衆': 'Org::海賊衆（数据包，随海战）', '卡': 'Card::（数据包技能卡）', '物品': 'Item::（数据包映射表）',
    '忍者衆': 'Org::忍者衆（数据包，07 核对）', '砦': 'Settlement::（type=砦）', '地方': 'Settlement.region（大区）',
    '交易品': 'Item::（数据包交易品）', '儲存號': 'Ctx/Variable（存档槽变量）', '官職': 'Hero.title（17 官职）',
    '流派': '🔴 数据包（流派系统，后续补充——2026-08-27 用户裁定：可能做，不放弃）', '主命': 'QuestDef（13 主命框架）', '町': 'Settlement::（type=町）',
    '官位': 'Hero.court_rank（17 官位品级链）', '商家': 'Org::商家（数据包，按需）', '里': 'Settlement::（type=里）',
    '天氣': '03 预设 weather（数据包）', '場面': '05 演出形态（scene/menu_dialogue）', '軍團方針': '02 PartyIntent（新加）',
    '工作': '13 主命/工作（QuestDef）', '人物類別': '枚举字面量', '事件主命': '13 事件主命（QuestDef 关联事件）',
    '戰鬥結束種類': '03 战果枚举（BattleResult）', '物品類型': 'Item 类型（数据包）', '主命屬性': '13 主命属性（数据包）',
    '遊戲通關種類': '🔴 剧本结局枚举（14/06）', '事件發生狀態': 'Event::state（调度器内部）',
    '環境變量': '🔴 05 演出环境（数据包）', '背景音樂': '05 bgm 指令（数据包）',
}
# 域 → 侧名前缀（旧表迁移/自检的域验证基准；v2 补全全部域）
PREFIX_BY_DOMAIN = {
    '人物': 'Hero', '城': 'Settlement', '據點': 'Settlement', '砦': 'Settlement', '町': 'Settlement', '里': 'Settlement',
    '大名家': 'Clan', '勢力': 'Faction', '國': 'Region', '地方': 'Region',
    '軍團': 'Army', '事件': 'Event', '狀況': 'Time', '事件標誌': 'Flag', '變量': 'Variable',
    '主命': 'QuestDef', '官職': 'title', '官位': 'court_rank', '人物類別': 'Identity',
    '忍者衆': 'Org', '商家': 'Org', '海賊衆': 'Org', '卡': 'Card', '流派': 'Card',
    '物品': 'Item', '交易品': 'Item', '工作': 'QuestDef', '事件主命': 'QuestDef', '主命屬性': 'QuestDef',
    '遊戲通關種類': 'ending', '事件發生狀態': 'Event', '環境變量': 'env', '背景音樂': 'bgm',
    '天氣': 'weather', '軍團方針': 'intent', '物品類型': 'ItemType',
    '日數計數器': 'Time', '儲存號': 'Variable', '場面': 'Facility',
    '戰鬥結束種類': 'BattleResult', '真偽': 'Bool', '身份': 'Identity',
}

# ═══ 属性：单键专表（属性名 → 干净 DSL 侧名，多域同名用 ' / ' 分段）═══
# 🔴 v2（2026-08-27 用户裁定）：属性行 = 属性名单键 + 侧名；域错配由生成期自检按语料实际域拦截，
#   不再把域写进行名（「人物.離家標誌」式行名废弃）；状态列标注「mod 需新增外置属性」。
PAIR_OVERRIDE = {
    # 大名家域（旧表把 本城 登记成 Hero.home——语料实测 2298 次全为大名家域）
    '本城': 'Clan.home', '當主': 'Clan.leader / Faction.leader / Org.leader',
    '支配力': 'Clan.power / Org.power', '城数': 'Clan.settlements',
    '戰略': 'Clan.strategy / Org.strategy', '戰略目標': 'Clan.strategy_goal / Org.strategy_goal',
    '停止進攻': 'Clan.ceasefire', '大方針': 'Clan.policy / Org.policy', '大方針目標': 'Clan.policy_goal',
    '朝廷貢献度': 'Clan.court_favor', '出奔計數器': 'Clan.deserter_count',
    '本據': 'Faction.home / Org.home', '支持大名家': 'Org.supporter',
    '鐵甲船建造技術': 'Org.tech_vessel', '大型船建造技術': 'Org.tech_large_vessel',
    '商業圈数': 'Org.merchant_net', '本店': 'Org.hq',
    # Settlement 域（城/據點/砦/町/里）
    '戰鬥標誌': 'Settlement.siege', '所在地方': 'Settlement.region',
    '道場': 'Settlement.dojo', '道場主人': 'Settlement.dojo_owner',
    '商人司': 'Settlement.merchant_office', '曾經訪問': 'Settlement.visited',
    '鐵甲船数': 'Settlement.vessels_ironclad', '所有船舶数': 'Settlement.vessels',
    '大型船舶数': 'Settlement.vessels_large', '主人': 'Settlement.owner',
    '攻め取りカウンタ': 'Settlement.attack', '地形': 'Settlement.terrain',
    '所屬忍者衆': 'Hero.ninja_group / Settlement.ninja_group', '所屬海賊衆': 'Hero.pirate_group / Settlement.pirate_group',
    # 🔴 patch12（2026-08-27）侧名塌缩拆分：下列每组原先挤在一个侧名里，翻译器分不开、写回会串。
    #   判定依据 = 语料值空间实测（据點種類 0~3 vs 據點類型 0~5；價值 2~7 品级 vs 價格 5000~65535 钱）。
    '基準石高': 'Settlement.kokudaka_base',      # 基准石高 ≠ 現石高（Settlement.kokudaka）
    '所屬國': 'Settlement.province',             # 国（省级）≠ 地方（大区，Settlement.region）
    '據點種類': 'Settlement.kind',               # 取值 0~3，与 據點類型（0~5，Settlement.type）不同
    '軍馬': 'Settlement.materials_horse / Army.materials_horse',
    '鐵砲': 'Settlement.materials_gun / Army.materials_gun',
    '大筒': 'Settlement.materials_cannon / Army.materials_cannon',
    # Item 域
    '價值': 'Item.value_grade',                  # 品级 2~7（≠ 價格 = 钱数，Item.price）
    # 軍團域（02 PartyBrain 受控集合）
    '軍團長': 'Army.leader', '武將': 'Army.general', '結果': 'Army.result',
    '士氣': 'Settlement.morale / Army.morale', '使用狀況': 'Army.state', '士兵數': 'Army.troops',
    '援軍對象軍團番號': 'Army.reinforce_id', '所屬勢力': 'Hero.faction / Army.faction',
    '軍團方針': 'Army.intent',
    # 人物域
    '主命狀態': 'Hero.quest_state', '承擔主命': 'Hero.quest_assigned',
    '主命目標': 'Hero.quest_goal', '主命期限': 'Hero.quest_deadline',
    '事件参加可能': 'Hero.available', '認識標誌': 'Hero.known', '親密度': 'Hero.relation_to',
    '仕官傾向': 'Hero.tendency', '忠誠度': 'Hero.loyalty',
    # 🔴 patch12：7 个独立布尔标志原先全塌缩成 Hero.state（一个字段存七件事，写回必串）
    '出現標誌': 'Hero.flag_appeared', '生病標誌': 'Hero.flag_sick',
    '外出禁止標誌': 'Hero.flag_confined', '死刑標誌': 'Hero.flag_executed',
    '仇敵標誌': 'Hero.flag_nemesis', '出撃標誌': 'Hero.flag_sortie',
    '失蹤標誌': 'Hero.flag_missing',
    # 🔴 patch12：4 种职业功勋各自独立（原先都叫 Hero.merit）
    '武士功勳': 'Hero.merit_samurai', '忍者功勳': 'Hero.merit_ninja',
    '商人功勳': 'Hero.merit_merchant', '海賊功勳': 'Hero.merit_pirate',
    # 🔴 patch12：随身钱 ≠ 存款；忠诚 ≠ 义理；势力 ≠ 势力类型
    '貯金': 'Hero.savings', '義理': 'Hero.honor',
    '所屬勢力類型': 'Hero.faction_type',
    # 🔴 patch12：對主人公的关系（属性形态）≠ 外交感情（带参调用，走 CALL_MAP → relation）
    '與主人公關係': 'Clan.relation_to_player / Org.relation_to_player',
    '身份': 'Hero.identity',
    '統率力': 'Hero.leadership', '武力': 'Hero.might', '智謀': 'Hero.intellect',
    '政務': 'Hero.governance', '魅力': 'Hero.charm', '野心': 'Hero.ambition',
    '素統率力': 'Hero.leadership_base', '素武力': 'Hero.might_base',
    '素智謀': 'Hero.intellect_base', '素政務': 'Hero.governance_base', '素魅力': 'Hero.charm_base',
    '所持金': 'Hero.gold',
    '名聲': 'Hero.reputation', '悪名': 'Hero.infamy', '壽命': 'Hero.lifespan',
    '工作狀態': 'Hero.work_state', '工作目標': 'Hero.work_goal', '承擔工作': 'Hero.work_assigned',
    '類別': 'Hero.category', '卡持有': 'Hero.card_held',
    '劍術師匠': 'Hero.sword_master', '劍術流派': 'Hero.sword_style',
    '印可': 'Hero.license', '流派評價': 'Hero.sword_rank', '兵法指南役大名家': 'Hero.tactics_advisor',
    '所屬商家': 'Hero.merchant_group', '所屬當主': 'Hero.clan_leader', '關係經緯': 'Hero.relation_graph',
    '裝備武器': 'Hero.equipment_weapon', '裝備防具': 'Hero.equipment_armor',
    '装備武器': 'Hero.equipment_weapon', '装備防具': 'Hero.equipment_armor',   # 语料简体变体
    '個人戰勝利数': 'Hero.duel_wins', '個人戰敗北数': 'Hero.duel_losses', '個人戰現在連勝数': 'Hero.duel_streak',
    '茶席次數': 'Hero.tea_count', '茶具經驗': 'Hero.tea_exp', '主人公道場規模': 'Hero.dojo_scale',
    '武具種類': 'Hero.weapon_type', '賊遭遇計數器': 'Variable::bandit_enc',
    '對手武將': 'Hero.rival', '劍勝利回数': 'Hero.sword_wins',
    '離家標誌': 'Hero.away_flag',          # 🔴 mod 需新增外置属性（引擎无，状态列标注）
    # 物品/交易品域（Item:: 数据包）
    '所有者': 'Item.owner', '所有個数': 'Item.count',
    '價格': 'Item.price', '鑑定標誌': 'Item.appraised', '物品類型': 'Item.type',
    '補正值': 'Item.bonus', '交易品數量': 'Item.count',
    # 卡域 / 流派域（2026-08-24 用户裁定放弃真实招式；称号层走 Card，数据包降级）
    '所持標誌': 'Card.held',
    '印可狀標誌': 'Card.license_flag', '宗家': 'Card.sword_hq', '道場打破可能標誌': 'Card.dojo_break',
    # 國域（Region）
    '全城壓制': 'allControlled', '國屬性': 'Region.name',
}

# 技能类（TK5 数值技能 0-100 → Hero.skill_<token>，数据包实现）
SKILL_TOKENS = {
    '忍術': 'ninjutsu', '軍学': 'military_tactics', '礼法': 'etiquette', '辯才': 'eloquence',
    '開墾': 'farming', '礦山': 'mining', '茶道': 'tea_ceremony', '鐵砲': 'firearms',
    '足輕': 'infantry', '騎馬': 'cavalry', '水軍': 'naval', '武藝': 'martial_arts',
    '弓術': 'archery', '算術': 'arithmetic', '醫術': 'medicine', '建築': 'construction',
}

# ═══ 函数候选：带参调用 → 函数（v2：全城壓制 等从语料提取，不再硬编码待注册）═══
# 🔴 v3（2026-08-27 用户裁定）：函数 = 带返回值的函数——第三元 = 返回值类型：
#   外交同盟/外交感情 返回数字（例句 外交感情(...)+(10) 做算术、外交同盟(...)!=(2) 与数字比较），
#   其余返回布尔（==(真偽::真) 或裸布尔）
CALL_MAP = {
    '外交同盟': ('isAllied', ('a', 'b'), '数字'),          # 同盟状态值（!=0 即同盟；例句 !=(2)）
    '外交感情': ('relation', ('a', 'b'), '数字'),          # 关系数值（例句 +(10) 算术）
    '鄰接大名家': ('isNeighbor', ('a', 'b'), '布尔'),
    '全城壓制': ('allControlled', ('region', 'clan'), '布尔'),
    '卡持有': ('hasCard', ('hero', 'card'), '布尔'),
    '移動可能': ('canMove', ('settlement', 'hero'), '布尔'),
    '攻擊可能': ('canAttack', ('settlement', 'hero'), '布尔'),
    # 🔴 2026-08-27 用户裁定：带参调用形态（域::X.属性(参数)）= 函数，不是属性——属性行值类型永不"函数"，
    #   而带括号的属性必须归函数区（属性行生成 `a in CALL_MAP` 自动拦截）：
    '國屬性1': ('region_attr_1', ('clan',), '布尔'),     # 国属性位 1（== 真偽 判定）
    '未知2': ('unknown_2', ('clan',), '布尔'),           # 未知属性位 2（地方域，== 真偽 判定）
    '未知8': ('unknown_8', ('faction',), '数字'),        # 未知属性位 8（大名家域，更新(59) 赋值）
}
# 🔴 2026-08-27 已废弃：属性形态（人物::X.親密度/認識標誌）不再映射函数侧名——属性就是属性，
#   值类型走推断（親密度=数字、認識標誌=布尔）；函数侧名（hasMet/relation/hasRelation）只留给
#   带参调用形态（外交感情(a,b) → relation）。本集合保留仅为 build_registry_csv import 兼容，无使用。
FUNC_SIDE_NOARG = {'hasMet', 'relation', 'hasRelation'}

# ═══ 域::值 注册表（v2 新增；侧名 = 英文枚举 token / 完整 DSL 引用）═══
# ═══════════════════════════════════════════════════════════════════════════
# 🔴 场所词汇单一来源（2026-08-27 补注册）：設施 / 背景 / 場面 / 決鬥場地 四张表
#   说的是同一套「地方」词——之前四张表各写各的（場面::城主間=lord_room、
#   設施::城主間=hash、決鬥場地::城主間=lord_room），跟 身份 双表分叉同一个病。
#   现在只此一张表，四处按各自前缀取用（Facility:: / Background:: / 裸 slug）。
#   ✅16 = 16-DSL注册表全表.md §二 facility 注册表 v1 已声明，原样采用。
# ═══════════════════════════════════════════════════════════════════════════
PLACE_TOKENS = {
    # ── 16.md §二 已声明（权威，勿改）──
    '自宅': 'house',                   # ✅16
    '酒場': 'tavern',                  # ✅16
    '城主間': 'castle_hall',           # ✅16（原 場面/決鬥場地 写的 lord_room，以 16.md 为准）
    '評定間': 'council_room',          # ✅16（原 場面 写的 council）
    '座': 'za',                        # ✅16
    '主人公診療所': 'clinic',           # ✅16
    '主人公道場': 'dojo',               # ✅16
    '民家': 'house_min',               # ✅16
    '商家': 'shop',                    # ✅16
    '南蠻商館': 'nanban_trade',         # ✅16
    '主人公鍛冶屋': 'smithy',           # ✅16
    '主人公茶室': 'tea_room',           # ✅16
    '寺': 'temple',                    # ✅16
    # ── 16.md「其余（≤4 次，无场景一律降级 menu_dialogue）」——本次补注册 token ──
    '主人公評定': 'council_own',        # 主人公自己主持的评定（区别于 評定間 场所）
    '公家宅': 'kuge_house',             # 公家 = 朝廷贵族
    '南蠻寺': 'nanban_temple',          # 南蛮寺 = 天主教堂
    '城練兵場': 'castle_drill',
    '宿屋': 'inn',
    '御所': 'imperial_palace',          # 御所 = 天皇居所
    '忍屋敷': 'ninja_manor',
    '忍者宅': 'ninja_house',
    '據點內畫面': 'settlement_screen',  # 不是设施，是「据点主界面」占位
    '武家宅': 'samurai_house',
    '海外交易所': 'overseas_trade',
    '海賊宅': 'pirate_den',
    '海賊屋敷': 'pirate_manor',
    '砦修業場': 'fort_training',        # 砦 = 要塞
    '砦練兵場': 'fort_drill',
    '米屋': 'rice_shop',
    '職人宅': 'artisan_house',
    '茶人宅': 'tea_master_house',
    '造船所': 'shipyard',
    '道場': 'dojo_town',               # 城里的公用道场（主人公道場 = dojo，两者不同）
    '醫師宅': 'doctor_house',
    '里修業場': 'village_training',
    '里練兵場': 'village_drill',
    '鍛冶屋': 'smithy_town',           # 城里的公用铁匠铺（主人公鍛冶屋 = smithy）
    '馬屋': 'stable',
    # ── 場面 域独有：占位符，指「本事件的发生设施」──
    '發生設施': 'event_facility',
    # ── 背景独有（场地背景图，不是可进入设施）──
    '初期設定': 'initial_setup',
    '合戰畫面': 'battle_screen',
    '海道': 'sea_route',
    '賭博所': 'gambling_den',
    '路口': 'crossroad',
    '陸道': 'land_route',
    '黑暗': 'darkness',
    # ── 決鬥場地独有（庭院/野外）──
    '原野': 'field',
    '忍者宅庭院': 'ninja_yard',
    '武家宅庭院': 'samurai_yard',
    '民家庭院': 'house_yard',
    '沙灘': 'beach',
    '船的甲板': 'ship_deck',
}

# 🔴 触发时机 token（16-DSL注册表全表.md §二 trigger 注册表 v1 = 权威，✅16 标记原样采用）
TRIGGER_TOKENS = {
    '每日處理的開頭': 'daily',              # ✅16
    '每月處理的最後': 'monthly',            # ✅16
    '遊戲開始時': 'game_start',             # ✅16
    '據點畫面表示後': 'settlement_enter',   # ✅16
    '室內畫面表示後': 'house_enter',        # ✅16（第二参 = facility，见 PLACE_TOKENS）
    '評定開始時': 'council_start',          # ✅16
    '移動畫面表示後': 'travel_screen',      # ✅16
    '野戰開始時': 'field_battle_start',     # ✅16
    '野戰結束時': 'field_battle_end',       # ✅16
    '攻城戰開始時': 'siege_battle_start',   # ✅16
    '攻城戰結束時': 'siege_battle_end',     # ✅16
    '軍團移動結束時': 'army_move_end',      # ✅16
    '章節凍結時': 'chapter_freeze',         # ✅16
    '遊戲通關時': 'game_clear',             # ✅16
    # ── 语料里有、16.md v1 未声明——本次补注册（16.md §二 表需同步补行）──
    '人物對話時': 'npc_talk',               # 77 次，最大的未声明契機
    '合戰決定時': 'battle_decided',         # 合战（会战）判定打不打之后
    '大名家滅亡時': 'clan_destroyed',
    '每月處理的最後絕對': 'monthly_forced',  # 「絕對」= 不参与互斥选路，必执行
    '選擇移動畫面時': 'travel_screen_select',   # 点开移动画面的瞬间（travel_screen 是画面显示完）
    '選擇設施時': 'facility_select',        # 点设施图标的瞬间（house_enter 是进去之后）
    '軍團移動開始時': 'army_move_start',
    '遊戲結束時': 'game_over',              # 败亡结束（game_clear 是通关）
}

# 🔴 模板 NPC token（05 演出的无名角色模板；07 素材表落 CharacterObject）
NPC_TOKENS = {
    '凄腕用心棒': 'elite_bodyguard', '喝醉的女人': 'drunk_woman', '喝醉的男人': 'drunk_man',
    '奇怪的姑娘': 'strange_girl', '女の子': 'young_girl', '婆さん': 'old_woman',
    '小孩': 'child', '明國商人': 'ming_merchant', '槍術師範代': 'spear_instructor',
    '琉球商人': 'ryukyu_merchant', '米屋的老闆': 'rice_shop_owner', '賊': 'bandit',
    '頭目': 'boss',
}

# 🔴 军团槽单一来源：軍團(域值) 与 軍團槽(枚举) 是同一套槽位，之前两张表各写各的
#   （主人公軍團 = main_army / player 两个侧名，事件用１軍團 = event_army_1 / event_1）
ARMY_SLOTS = {
    '主人公軍團': 'Army::player',
    '軍團１': 'Army::army_1', '軍團２': 'Army::army_2',
    '事件用１軍團': 'Army::event_1', '事件用２軍團': 'Army::event_2',
    '事件用３軍團': 'Army::event_3', '事件用４軍團': 'Army::event_4',
    '事件用５軍團': 'Army::event_5',
}

DOMAIN_VAL_MAP = {
    # 身份枚举（17 功勋/身份系统，token 定稿权在 17）
    ('身份', '大名'): 'daimyo', ('身份', '城主'): 'city_lord', ('身份', '國主'): 'province_lord',
    ('身份', '浪人'): 'ronin', ('身份', '家老'): 'elder', ('身份', '部將'): 'general',
    ('身份', '侍大將'): 'samurai_captain', ('身份', '足輕大將'): 'ashigaru_captain', ('身份', '足輕組頭'): 'ashigaru_leader',
    ('身份', '番頭'): 'foreman', ('身份', '支配人'): 'manager', ('身份', '元締'): 'overseer',
    ('身份', '大老闆'): 'merchant_owner', ('身份', '手代'): 'clerk', ('身份', '見習'): 'apprentice',
    ('身份', '師範'): 'sword_master', ('身份', '師範代'): 'sword_deputy', ('身份', '頭領'): 'chief',
    ('身份', '船頭'): 'boatswain', ('身份', '船大將'): 'naval_captain', ('身份', '水夫頭'): 'boat_leader',
    ('身份', '水夫'): 'sailor', ('身份', '上忍'): 'ninja_high', ('身份', '中忍'): 'ninja_mid',
    ('身份', '下忍'): 'ninja_low', ('身份', '鍛冶匠'): 'smith', ('身份', '醫師'): 'doctor',
    ('身份', '茶人'): 'tea_master', ('身份', '姑娘'): 'girl',
    # 🔴 2026-08-27：頭 原与 頭領 同为 chief（表内塌缩，两个不同品级共用 token → 品级链坏）
    ('身份', '頭'): 'head',
    # 🔴 2026-08-27：原 ENUM_SETS['身份'] 独有的三个 token 回填（词汇表单一来源）
    ('身份', '町人'): 'townsman', ('身份', '最高位'): 'top_rank',
    ('身份', '事件人物'): 'event_person',       # 与 人物類別::事件人物 同侧名（同一概念）
    # 🔴 2026-08-27：物品類型域值原走 hash 兜底（ItemType::tk5_u44a3d9），
    #   与 枚举 物品種類 是同一套词 → 对齐 物品種類 侧名
    ('物品類型', '武器'): 'weapon', ('物品類型', '茶器'): 'tea_ware',
    # 官位域值（🔴 2026-08-27：16.md §四 裁定「官位不是对象，值 = 枚举 token」——
    #   原走 ENTITY_DOMAINS 哈希兜底 = 万能接收器，自检失效；改逐值登记。
    #   品级序（正一位 > 從一位 > 正二位 …）= 17 产出的等级表，不编进 token）
    ('官位', '正一位'): 'shoichii', ('官位', '從一位'): 'juichii',
    ('官位', '正二位'): 'shonii', ('官位', '從二位'): 'junii',
    ('官位', '正三位'): 'shosanmi', ('官位', '從三位'): 'jusanmi',
    ('官位', '正四位上'): 'shoshii_jo', ('官位', '正四位下'): 'shoshii_ge',
    ('官位', '從四位上'): 'jushii_jo', ('官位', '從四位下'): 'jushii_ge',
    ('官位', '正五位上'): 'shogoi_jo', ('官位', '正五位下'): 'shogoi_ge',
    ('官位', '從五位上'): 'jugoi_jo', ('官位', '從五位下'): 'jugoi_ge',
    ('官位', '正六位上'): 'shorokui_jo', ('官位', '正六位下'): 'shorokui_ge',
    # 官職域值（同上裁定；称号无序，17 官职表权威）
    ('官職', '征夷大將軍'): 'seii_taishogun', ('官職', '左大臣'): 'sadaijin',
    ('官職', '右大臣'): 'udaijin', ('官職', '內大臣'): 'naidaijin',
    ('官職', '大納言'): 'dainagon', ('官職', '中納言'): 'chunagon',
    ('官職', '權中納言'): 'gon_chunagon', ('官職', '左近衛大將'): 'sakonoe_taisho',
    ('官職', '左近衛中將'): 'sakonoe_chujo', ('官職', '右近衛中將'): 'ukonoe_chujo',
    ('官職', '左衛門佐'): 'saemon_no_suke', ('官職', '刑部少輔'): 'gyobu_shoyu',
    ('官職', '治部少輔'): 'jibu_shoyu', ('官職', '修理亮'): 'shuri_no_suke',
    ('官職', '右京大夫'): 'ukyo_daibu', ('官職', '筑前守'): 'chikuzen_no_kami',
    ('官職', '日向守'): 'hyuga_no_kami',
    # 真偽域值（布尔字面量）
    ('真偽', '真'): 'true', ('真偽', '偽'): 'false',
    # 天氣域值（03 预设 weather 数据包）
    ('天氣', '晴'): 'weather_clear', ('天氣', '雨'): 'weather_rain', ('天氣', '雲'): 'weather_cloudy',
    # 狀況域值（无主体属性形态）
    ('狀況', '年'): 'Time::year', ('狀況', '月'): 'Time::month', ('狀況', '日'): 'Time::day',
    ('狀況', '評定期間標誌'): 'Time::assessment_flag',
    ('狀況', '24'): 'Variable::last_battle_result',     # 语料: (狀況::24)==(戰鬥結束種類::終結) = 上次战斗结果
    ('狀況', '劇本'): 'Variable::scenario',             # 语料: (狀況::劇本)==(2/3/4) = 当前剧本编号
    # 據點域命名槽
    ('據點', '主人公據點'): 'Hero::MainHero.settlement', ('據點', '主人公當主據點'): 'Hero::MainHero.home',
    # 战果枚举（03 BattleResult）
    ('戰鬥結束種類', '終結'): 'ended',
    # 軍團实例（02 PartyBrain 受控集合标识）
    # 🔴 軍團 域值不在此手写——见 ARMY_SLOTS（军团槽单一来源），domain_val_rule('軍團') 取用
    # 人物类别（容器筛选枚举）
    ('人物類別', '武將'): 'general', ('人物類別', '浪人'): 'ronin', ('人物類別', '忍者'): 'ninja', ('人物類別', '海賊'): 'pirate',
    ('人物類別', '泛用對手'): 'generic_rival', ('人物類別', '町人'): 'townsman', ('人物類別', '事件人物'): 'event_person',
    # 真偽域值（布尔字面量）
    ('真偽', '真'): 'true', ('真偽', '偽'): 'false',
    # 天氣域值（03 预设 weather 数据包）
    ('天氣', '晴'): 'weather_clear', ('天氣', '雨'): 'weather_rain', ('天氣', '雲'): 'weather_cloudy',
    # 事件上下文槽（發生人物/發生據點 = 触发者上下文，Ctx 引用）
    ('人物', '發生人物'): 'Ctx::event_hero', ('據點', '發生據點'): 'Ctx::event_settlement',
    ('大名家', '發生大名家'): 'Ctx::event_clan', ('勢力', '發生勢力'): 'Ctx::event_clan',
    ('人物', '主人公'): 'Hero::MainHero',
    ('人物', '無效'): 'null', ('據點', '無效'): 'null', ('城', '無效'): 'null', ('大名家', '無效'): 'null',
    # 場面域值（05 演出形态 / 01 facility 注册表）
    # 🔴 場面 域值不在此手写——见 PLACE_TOKENS（场所词汇单一来源），domain_val_rule('場面') 取用
    # 狀況域值（无主体属性形态）
    ('狀況', '戰爭禁止日數'): 'Variable::war_ban_days', ('狀況', '空閒大名家數'): 'Variable::idle_clans', ('狀況', '場面'): 'Variable::scene',
    ('狀況', '天氣'): 'Variable::weather', ('狀況', '遊戲經過日數'): 'Variable::days_elapsed',
    ('狀況', '評定期限結束標誌'): 'Variable::assessment_deadline_flag',
    ('變量', '容器記錄數'): 'Variable::container_count', ('變量', '參考值'): 'Variable::ref_value',
    ('軍團方針', '歸還'): 'intent_return_home',   # 与 軍團指令::歸還=return_home 同义，加 intent_ 前缀区分层
}

# 🔴 v2：实体引用域——域::值 = 具名实体（人名/城名/组织名…），由翻译器名字表查 StringId +
# 确定性 fallback 兜底，**不进 CSV 域值区**（人物::伊藤總十郎 这类行 = 污染，2026-08-27 用户裁定）
ENTITY_DOMAINS = {
    '人物': 'Hero', '大名家': 'Clan', '城': 'Settlement', '據點': 'Settlement', '勢力': 'Faction::Kingdom',
    '國': 'Region', '砦': 'Settlement', '町': 'Settlement', '里': 'Settlement',
    '忍者衆': 'Org', '商家': 'Org', '海賊衆': 'Org', '卡': 'Card', '流派': 'Card',
    '事件': 'Event', '物品': 'Item', '交易品': 'Item', '地方': 'Region',
    '工作': 'QuestDef', '事件主命': 'QuestDef',   # 🔴 官位/官職 已移出（16.md §四：不是对象，是枚举）
}

# 🔴 槽域前缀 → Ctx 槽名（2026-08-27 用户裁定：与 tk5_to_json SLOT_NAME_MAP/_SLOT_CAT 一致，CSV 侧名权威）
SLOT_CAT = {
    '人物': 'hero', '城': 'settlement', '據點': 'place', '大名家': 'clan', '勢力': 'faction',
    '國': 'region', '砦': 'fort', '町': 'town', '里': 'village', '忍者衆': 'ninja', '商家': 'merchant',
    '海賊衆': 'pirate', '卡': 'card', '流派': 'school', '物品': 'item', '交易品': 'trade',
    '軍團': 'army', '地方': 'area',
}

# ═══ 域::值 规则兜底（词条域专用；实体域由 ENTITY_DOMAINS 处理，不进 CSV）═══
def domain_val_rule(dom, val):
    if dom == '事件標誌':
        # 编号旗标（语料实测值全为数字：38/95/167…，用法 更新/調查:(事件標誌::38)）——
        # 编号即稳定 ID，保留可读（Flag::flag_38）；事件完成状态是另一套：事件::X → Event::<id>.done
        if val.isdigit():
            return f'Flag::flag_{val}'
        return f'Flag::{fallback_id(val)}'            # 非数字 flag 名：确定性 hash + report 登记中文名
    if dom == '狀況':
        return f'Variable::{fallback_id(val)}'         # 全局状态值（除专表外）
    if dom == '日數計數器':
        # 🔴 2026-08-27：编号计数器（语料实测 85 个全数字编号，用法 更新:(日數計數器::12)(0) /
        #   調查:(日數計數器::14)>=(5)）——每个编号是**一个独立计时器**，与 事件標誌::N 同构。
        #   旧版全部返回 Time::day = 85 个计数器塌缩成一个字段，写回必串。
        if val.isdigit():
            return f'Time::counter_{val}'
        return f'Time::counter_{fallback_id(val)}'
    if dom == '變量':
        return f'Variable::{ascii_translit(val) or fallback_id(val)}'   # 全角→半角转写优先
    if dom == '儲存號':
        return f'Variable::{fallback_id(val)}'         # 存档槽变量
    if dom == '場面':
        # 🔴 场所词汇单一来源：設施/背景/場面/決鬥場地 同一张 PLACE_TOKENS
        return f'Facility::{PLACE_TOKENS[val]}' if val in PLACE_TOKENS else f'Facility::{fallback_id(val)}'
    if dom == '軍團':
        return ARMY_SLOTS.get(val) or f'Army::{fallback_id(val)}'   # 军团槽单一来源
    if dom == '人物類別':
        return f'category_{fallback_id(val)}'          # 容器类别枚举（专表外）
    if dom == '主命':
        return f'tk5_u{hashlib.md5(val.encode("utf-8")).hexdigest()[:6]}'   # 🔴 2026-08-27 用户裁定：主命 = 枚举（类型标识）——
        #   语料 169 次全为 ==/!= 纯比较，零对象用法（无属性访问/代入）；token 占位 = tk5_uXXXX，13 QuestDef 表产出语义 token
    if dom == '物品類型':
        return f'ItemType::{fallback_id(val)}'         # 物品类型数据包
    if dom == '軍團方針':
        return f'intent_{fallback_id(val)}'            # 02 PartyIntent 定稿前占位
    return None


def fallback_id(w):
    """确定性 ASCII 占位 ID（08b 踩坑 5：tk5_uXXXXXX + report 登记中文名）。"""
    h = hashlib.md5(w.encode('utf-8')).hexdigest()[:6]
    return f'tk5_u{h}'


def ascii_translit(w):
    """全角字母/数字 → 半角（Ｒｎｄ１００ → Rnd100）；含其他字符 → None（走 fallback_id）。"""
    out = []
    for ch in w:
        if 'Ａ' <= ch <= 'Ｚ':
            out.append(chr(ord(ch) - 0xFEE0))
        elif 'ａ' <= ch <= 'ｚ':
            out.append(chr(ord(ch) - 0xFEE0))
        elif '０' <= ch <= '９':
            out.append(chr(ord(ch) - 0xFEE0))
        elif re.match(r'[A-Za-z0-9_\-]', ch):
            out.append(ch)
        else:
            return None
    return ''.join(out) or None


# ═══ 旧表迁移（保留原精确表作为候选侧名；按语料域验证前缀）═══
ATTR_MAP = {
    '存在': 'exists', '所屬大名家': 'Hero.clan / Settlement.clan', '事件参加可能': 'Hero.available',
    '城主': 'Settlement.owner', '本城': 'Hero.home', '外交同盟': 'isAllied', '身份': 'Hero.identity',
    '外交感情': 'relation', '所屬據點': 'Hero.settlement', '性別': 'Hero.gender', '親密度': 'Hero.relation_to',
    '死亡標誌': 'Hero.alive', '認識標誌': 'Hero.known', '所屬國': 'Settlement.region', '所屬勢力類型': 'Hero.faction',
    '出現標誌': 'Hero.state', '所屬上司': 'Hero.superior', '所持標誌': 'Hero.item_flag', '武將': 'Hero.general',
    '全城壓制': 'allControlled', '使用狀況': 'Hero.state', '戰略': 'Hero.strategy', '妻': 'Hero.spouse',
    '主命狀態': 'Hero.quest_state', '軍團長': 'Hero.party', '兵士数': 'Settlement.garrison', '離家標誌': 'Hero.state',
    '士氣': 'Settlement.morale', '年齡': 'Hero.age', '仕官傾向': 'Hero.tendency', '軍資金': 'Settlement.funds',
    '外出禁止標誌': 'Hero.state', '悪名': 'Hero.infamy', '當主': 'Hero.leader', '所有者': 'Hero.owner',
    '戰略目標': 'Hero.strategy_goal', '兵糧': 'Settlement.food', '結果': 'Army.result', '未知': 'unknown',
    '武士功勳': 'Hero.merit', '所在地方': 'Settlement.region', '名聲': 'Hero.reputation', '據點類型': 'Settlement.type',
    '交易品數量': 'Item.count', '防御度': 'Settlement.defense', '鐵砲': 'Settlement.materials',
    '出撃標誌': 'Hero.state', '支配力': 'Clan.power', '立場': 'Hero.stance', '承擔主命': 'Hero.quest_assigned',
    '本據': 'Hero.home', '所有個数': 'Item.count', '官職': 'Hero.title', '劍術師匠': 'sword_master',
    '所屬海賊衆': 'Org::pirate_group', '所屬當主': 'Clan.leader', '戰鬥標誌': 'Hero.state', '死刑標誌': 'Hero.state',
    '大方針': 'Hero.policy', '停止進攻': 'Hero.ceasefire', '所屬忍者衆': 'Org::ninja_group',
    '訓練度': 'Settlement.training', '住民安定度': 'Settlement.security', '卡持有': 'Card.held',
    '体力': 'Hero.health', '主命目標': 'Hero.quest_goal', '官位': 'Hero.court_rank', '鄰接大名家': 'isNeighbor',
    '現石高': 'Settlement.kokudaka', '城数': 'Clan.settlements', '妻性格': 'Hero.spouse_personality',
    '劍術流派': 'sword_style', '規模': 'Settlement.scale', '鐵甲船数': 'vessels', '所屬勢力': 'Hero.faction',
    '移動可能': 'Settlement.movable', '生病標誌': 'Hero.state', '忠誠度': 'Hero.loyalty',
    '忍者功勳': 'Hero.merit', '據點種類': 'Settlement.type', '關係經緯': 'relation_graph', '軍馬': 'Settlement.materials',
    '現礦山': 'Settlement.mine', '商人功勳': 'Hero.merit', '道場主人': 'dojo_owner', '與主人公關係': 'relation',
    '基準石高': 'Settlement.kokudaka', '所持金': 'Hero.gold', '工作狀態': 'Hero.work_state', '海賊功勳': 'Hero.merit',
    '暴動標誌': 'Settlement.rebellion', '醫師評價': 'Hero.doctor_rank', '失蹤標誌': 'Hero.state', '原屬下標誌': 'Hero.former_subordinate',
    '繼承人標誌': 'Hero.heir_flag', '貯金': 'Hero.gold', '兵法指南役大名家': 'Clan.tactics_advisor', '自宅鄰接工作場': 'Hero.workplace',
    '礦山最高值': 'Settlement.mine_max', '個人戰勝利数': 'duel_wins', '鑑定標誌': 'Hero.appraisal_flag',
    '印可狀標誌': 'license_flag', '攻擊可能': 'Settlement.attackable', '對手武將': 'Hero.rival', '所有船舶数': 'vessels',
    '印可': 'license', '大方針目標': 'Hero.policy_goal', '大型船舶数': 'vessels', '所屬商家': 'Org::merchant_group',
    '父母': 'Hero.parents', '支持大名家': 'Clan.supporter', '流派評價': 'sword_rank', '本店': 'Org::merchant_hq',
    '義理': 'Hero.loyalty', '士兵數': 'Army.troops', '類別': 'Identity.category', '價格': 'Item.price',
    '茶席次數': 'Card.tea', '装備武器': 'Agent.Equipment', '價值': 'Item.price', '鐵甲船建造技術': 'tech_vessel',
    '茶具經驗': 'Card.tea_exp', '道場': 'dojo', '宗家': 'sword_hq', '出自': 'Hero.origin', '武力': 'Hero.might',
    '合戰禁止標誌': 'Hero.no_battle_flag', '飲酒': 'Hero.drinking', '大筒': 'Settlement.materials', '製砲經驗': 'Hero.cannon_exp',
    '製鐵經驗': 'Hero.smith_exp', '朝廷貢献度': 'Clan.court_favor', '國屬性': 'Region.name', '野心': 'Hero.ambition',
    '智謀': 'Hero.intellect', '喜好': 'Hero.taste', '装備防具': 'Agent.Equipment', '魅力': 'Hero.charm',
    '素武力': 'Hero.might_base', '素魅力': 'Hero.charm_base', '素智謀': 'Hero.intellect_base', '素政務': 'Hero.governance_base',
    '製藥天數': 'Hero.medicine_days', '曾經訪問': 'Hero.visited', '劍勝利回数': 'sword_wins', '出奔計數器': 'Variable::deserter',
    '性情': 'Hero.personality', '物品類型': 'Item.type', '補正值': 'Hero.bonus', '最大載重量': 'Hero.capacity',
    '勢力類型': 'Faction.type', '道場打破可能標誌': 'dojo_break', '大型船建造技術': 'tech_large_vessel',
    '格': 'Hero.rank', '素統率力': 'Hero.leadership_base', '統率力': 'Hero.leadership',
    '主人公道場規模': 'dojo_scale', '義診天數': 'Hero.clinic_days', '武具種類': 'weapon_type', '精神': 'Hero.spirit',
    '天覧試合標誌': 'Hero.tournament_flag', '主人': 'Hero.master', '地形': 'Region.terrain', '無敵標誌': 'Hero.invincible',
    '軍團方針': 'Army.intent', 'evm': 'unknown', '商業圈数': 'Org::merchant_net', '個人戰敗北数': 'duel_losses',
    '商人司': 'Org::merchant_office', '武具經驗': 'Hero.weapon_exp', '知喜好標誌': 'Hero.knows_taste',
    '個人戰現在連勝数': 'duel_streak', '賊遭遇計數器': 'Variable::bandit_enc', '攻': 'Hero.attack',
    '壽命': 'Hero.lifespan', '工作目標': 'Hero.work_goal', '物欲': 'Hero.greed', '主命期限': 'Hero.quest_deadline',
    '承擔工作': 'Hero.work_assigned', '援軍對象軍團番號': 'Army.reinforce_id', '容貌': 'Hero.appearance',
    '政務': 'Hero.governance', '開墾': 'Hero.farming', '軍團長': 'Army.leader', '武將': 'Army.general', '結果': 'Army.result',
}


# ═══════════════════════════════════════════════════════════════════════════
# 🔴 纯数字属性位（2026-08-27 patch13，废掉「万能接收器」）
#   旧规则 `if attr.isdigit(): return f'{prefix}.attr_{attr}'` 能接住任何数字 → 这一块的自检
#   等于没查。现在按**主体形态**三分类，只有 A 类是真属性、且必须逐条登记：
#     A 真数字属性位  —— 主体是实体/命名槽，取值空间有意义 → DIGIT_ATTR 逐条登记（表外 = 报错）
#     B 域值的数字ID后缀 —— 主体是**已登记的域值**，`.N` 是 TK5 内部编号（官位::無效.3 /
#        工作::助陣.5 / 天氣::晴.147）。它不是属性：整体就是那个域值，翻译期丢掉 .N。
#        → 不产属性行（域值行已覆盖它），不算漏。
#     C 转储原始数值引用 —— 主体也是纯数字（環境變量::5270.88），TK5 转储未命名的原始 ID 对。
#        → 合并成 CSV 单行「解析碎片」，不逐个产行。
# ═══════════════════════════════════════════════════════════════════════════
# (域, 数字属性位) → (侧名, 语义)。语义 = 语料值空间实证，不臆测；未知处如实标注。
DIGIT_ATTR = {
    ('軍團', '27'):  ('Army.attr_27',   '军团目标据点槽（== 城::X / 據點::X）'),
    ('交易品', '3'): ('Item.attr_3',    '交易品开关位（==1 / !=1，随 商業圈数 门控）'),
    ('官職', '1'):   ('title.attr_1',   '官职现任者（== 人物::X / 人物::無效）'),
    ('人物', '176'): ('Hero.attr_176',  '剑豪线布尔位（== 真偽::真/偽；主体为柳生/佐佐木等剑豪）'),
    ('人物', '205'): ('Hero.attr_205',  '人物数值槽（婚礼改名时从原人物整体拷贝；家督让位赋 1154）'),
    ('人物', '206'): ('Hero.attr_206',  '人物数值槽（与 205 成对拷贝/赋值）'),
    ('軍團', '8'):   ('Army.attr_8',    '军团方针位（== 軍團方針::歸還；軍團方針 的数字位别名）'),
    ('大名家', '10'): ('Clan.attr_10',  '大名家据点目标槽（与 戰略目標 并列写入同一据点）'),
    ('人物', '207'): ('Hero.attr_207',  '主人公数值槽（剑豪线门槛 >=5 / >=15 / >=25）'),
    ('大名家', '26'): ('Clan.attr_26',  '大名家位掩码槽（赋 31 / 2097183）'),
    ('大名家', '24'): ('Clan.attr_24',  '大名家关联人物槽（== 商家Ａ.當主 / 今井宗久，御用商人形态）'),
    ('軍團', '28'):  ('Army.attr_28',   '军团数值槽（赋 10000 / 30000，军资金形态）'),
    ('人物', '94'):  ('Hero.attr_94',   '主人公数值槽（==0 / >500 / >4000）'),
    ('大名家', '25'): ('Clan.attr_25',  '大名家数值槽（<1 / 赋 12）'),
    ('人物', '161'): ('Hero.attr_161',  '主人公数值槽（赋 0 / 200 / 1000，与 所持金 同段写入）'),
    ('人物', '118'): ('Hero.attr_118',  '主人公布尔位（== 真偽::真，事件门控）'),
    ('人物', '148'): ('Hero.attr_148',  '主人公数值位（==0，与 事件標誌 同段门控）'),
    ('人物', '154'): ('Hero.attr_154',  '主人公数值位（==1，每月处理门控）'),
    ('地方', '1'):   ('Region.attr_1',  '地方支配大名家（== 大名家::X）'),
}


def digit_attr_class(dom, attr, subj=None):
    """纯数字属性位三分类 → ('A'|'B'|'C'|None, 侧名)。subj=None 时只认 A（无主体信息不放行）。"""
    if (dom, attr) in DIGIT_ATTR:
        return 'A', DIGIT_ATTR[(dom, attr)][0]
    if subj is None:
        for sj in ATTR_SUBJECTS.get((dom, attr), ()):          # 无显式主体 → 用语料主体集判定
            k, _ = digit_attr_class(dom, attr, sj)
            if k:
                return k, _
        return None, None
    if subj.isdigit():
        return 'C', (PREFIX_BY_DOMAIN.get(dom) or 'obj') + '.raw_ref'
    if val_side(dom, subj) is not None:
        return 'B', val_side(dom, subj)                        # 整体 = 那个域值，.N 是内部编号
    return None, None


def halfwidth(t):
    """全角数字 → 半角（武將２ 的 ２ → 2）。"""
    return t.translate(str.maketrans('０１２３４５６７８９', '0123456789'))


def clean_side(raw):
    """旧表侧名清洗：'Settlement.garrison / Party 兵数' → 'Settlement.garrison'；去中文注释。"""
    s = raw.split(' / ')[0]
    s = re.sub(r'[（(].*$', '', s)
    return s.strip()


def side_segments(raw):
    """旧表多域侧名分段：'Hero.clan / Settlement.clan' → ['Hero.clan', 'Settlement.clan']（每段去注释）。"""
    segs = []
    for part in raw.split(' / '):
        s = re.sub(r'[（(].*$', '', part).strip()
        if s and '.' in s:
            segs.append(s)
    return segs


def side_candidates(attr):
    """属性名 → 候选侧名段列表（单键专表 > 旧表多段 > 旧表单段）。"""
    o = PAIR_OVERRIDE.get(attr)
    if o:
        return [s.strip() for s in o.split(' / ')]
    old = ATTR_MAP.get(attr)
    if old:
        segs = side_segments(old)
        if segs:
            return segs
        c = clean_side(old)
        if c:
            return [c]
    return []


_FUNC_SIDES = ('exists', 'isAllied', 'isNeighbor', 'allControlled', 'hasMet', 'hasRelation', 'relation', 'unknown')


def pair_side(dom, attr, subj=None):
    """(域, 属性) → 干净侧名。候选段按语料域前缀选段；无匹配段 = None（自检报错 = 域错配/表外）。"""
    prefix = PREFIX_BY_DOMAIN.get(dom)
    cands = side_candidates(attr)
    if prefix:
        for s in cands:
            if s.startswith(prefix + '.') or s == prefix:
                return s
    for s in cands:
        if s.startswith(('Variable::', 'Ctx::')):
            return s        # 全局变量侧名与域无关（賊遭遇計數器 → Variable::bandit_enc）
    for s in cands:
        if s in _FUNC_SIDES:
            return s        # 函数/碎片侧名与域无关
    # 规则兜底
    if attr.endswith('技能'):
        tok = SKILL_TOKENS.get(attr[:-2]) or fallback_id(attr)
        return f'Hero.skill_{tok}'                          # 技能数值 0-100，数据包实现
    if attr.startswith('未知'):
        # 未知NN = 转储未命名的属性位（解析碎片）。保留编号——与 CALL_MAP 的 unknown_2/unknown_8 同口径。
        mu = re.search(r'[0-9０-９]+', attr)
        return 'unknown_' + halfwidth(mu.group(0)) if mu else 'unknown'
    if attr.isdigit():
        return digit_attr_class(dom, attr, subj)[1]     # A/B/C 三分类；表外 = None = 生成期报错
    m = re.search(r'[0-9０-９]+', attr)
    if m:
        num = halfwidth(m.group(0))
        base = attr[:m.start()] + attr[m.end():]            # 编号槽属性：武將２→武將、道場２主人→道場主人
        if base and base != attr:
            s = pair_side(dom, base)
            if s:
                # 🔴 2026-08-27 patch12：保留编号（武將２→Army.general_2、容貌１→Hero.appearance_1），
                #   否则 2/3/4/5 号槽全塌缩成同一个侧名。带参调用形态走 CALL_MAP，不编号。
                return s if attr in CALL_MAP else f'{s}_{num}'
    if dom == '人物' and attr.endswith('標誌') and not cands:
        return 'Hero.state'
    return None


# 🔴 特殊引用值（2026-08-27 用户裁定：代词/特殊值 ≠ 具名实体，进 CSV 登记；与 tk5_to_json
#   translate_subject 语义一致——主人公 跨域统一 Hero::MainHero，無效 → null，發生X → Ctx 事件槽）
SPECIAL_VALS = {
    '主人公': 'Hero::MainHero',
    '主人公據點': 'Hero::MainHero.settlement',
    '主人公當主據點': 'Hero::MainHero.home',
    '發生人物': 'Ctx::event_hero',
    '發生據點': 'Ctx::event_settlement',
    '無效': 'null',
}
SPECIAL_TYPES = {
    '主人公': '对象:人物', '主人公據點': '对象:据点', '主人公當主據點': '对象:据点',
    '發生人物': '对象:人物', '發生據點': '对象:据点', '無效': '空',
}


def val_side(dom, val):
    """(域, 值) → 侧名/引用。专表 > 特殊值 > 槽 > 实体域规则 > 数据型规则。"""
    if (dom, val) in DOMAIN_VAL_MAP:
        return DOMAIN_VAL_MAP[(dom, val)]
    if val in SPECIAL_VALS:
        return SPECIAL_VALS[val]
    if val == '無效':
        return 'null'
    # 🔴 槽形态例外（2026-08-27 用户裁定：人物Ｂ/城Ａ/據點Ｃ = 命名槽引用，不是具名实体——
    #   具名实体（人物::伊藤總十郎）不进 CSV（查名字表），槽引用进表 Ctx::hero_B；与 tk5_to_json 槽名一致）
    m = re.match(r'^([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,8})([Ａ-Ｅ])$', val)
    if m:
        cat = SLOT_CAT.get(m.group(1), 'slot')
        return f'Ctx::{cat}_{chr(ord("A") + (ord(m.group(2)) - ord("Ａ")))}'
    if re.match(r'^[ａ-ｚ]$', val):
        return f'Ctx::var_{chr(ord("a") + (ord(val) - ord("ａ")))}'
    if dom in ENTITY_DOMAINS:
        prefix = ENTITY_DOMAINS[dom]
        return f'{prefix}::{fallback_id(val)}'          # 具名实体：翻译器名字表先行，fallback 兜底
    return domain_val_rule(dom, val)


def call_side(dom, attr):
    """带参调用 → 函数。表外 = None（生成期报错）。"""
    return CALL_MAP.get(attr)


# ═══════════════════════════════════════════════════════════════════════════
# 🔴 v3（2026-08-27）：命令参数位登记 —— 补上「域::X 形态」之外的最大盲区
#   旧自检只查 域/属性/域值/函数/命令头 五种形态，命令**裸参数位**（枚举/资源名/
#   容器字段/军团槽/触发名）完全没有模型 → 5.4 万处词条游离在总表之外。
#   现在：每个命令的每个参数位声明「收什么」，枚举集逐 token 登记，表外即生成失败。
# ═══════════════════════════════════════════════════════════════════════════

# ── 枚举集：token → 侧名（语义型手写；资源型走 res_side 规则生成）──
ENUM_SETS = {
    # 容器操作
    '容器位置': {'先頭': 'first', '指針': 'cursor', '末尾': 'last'},
    '容器清理': {'消去': 'clear', '保留': 'keep'},
    '容器存取': {'保存': 'save', '恢復': 'restore'},
    '排序方向': {'降順': 'desc', '升順': 'asc'},
    '排序特殊鍵': {'亂序': 'random'},
    '容器統計': {'容器記錄數': 'container_count'},   # 与 變量::容器記錄數 同 slug（勿分叉）
    # 军团（02 PartyBrain）
    # 🔴 軍團槽 不在此手写——下方从 ARMY_SLOTS 派生（军团槽单一来源）
    '軍團槽': {},
    '軍團指令': {'據點移動': 'move_to', '軍團攻擊': 'attack_party', '據點攻擊': 'siege',
                '歸還': 'return_home', '終結': 'disband', '平局': 'draw',
                '統一（完全）': 'unify_full', '統一（通常）': 'unify_normal'},
    '零值': {'Ｚｅｒｏ': '0'},
    '通關方式': {'統一（完全）': 'unify_full', '統一（通常）': 'unify_normal',
                '輔佐統一天下': 'assist_unify'},
    # 演出（05）
    '轉場': {'褪去': 'fade', '無效果': 'none', '圓形擦出': 'circle_wipe', '回旋擦出': 'spiral_wipe'},
    '畫面效果': {'暗出': 'fade_out', '暗入': 'fade_in'},
    '圖片類型': {'事件ＣＧ': 'cg', '物品': 'item', '人物': 'portrait'},
    '背景類型': {'場面': 'scene', '場地背景': 'field', '據點': 'settlement'},
    # 身份 / 立场 / 状态
    '從屬類型': {'直臣': 'direct_vassal', '陪臣': 'sub_vassal'},
    '獨立方式': {'只有陪臣': 'sub_only', '通常': 'normal'},
    '出現狀態': {'已出現': 'appeared', '未出現': 'hidden'},
    '其他分支': {'其他': 'else'},
    # 🔴 真偽 / 身份 / 人物類別 / 物品類型 不在此手写——见下方 DERIVED_ENUM_SETS（词汇表单一来源）
    '物品種類': {'武器': 'weapon', '書物': 'book', '兵法書': 'strategy_book', '茶器': 'tea_ware',
                '藝術品': 'art', '南蠻物': 'exotic', '酒': 'sake', '財寶': 'treasure', '海外': 'overseas',
                '小粒金': 'gold_nugget'},
    '武器種類': {'刀劍': 'sword', '鎖鎌': 'kusarigama', '弓': 'bow', '槍': 'spear',
                '苦無': 'kunai', '火繩槍': 'matchlock'},
    '性別': {'男': 'male', '女': 'female'},
    '生存狀態': {'生存': 'alive', '被殺': 'killed'},
    # 个人战斗 / 迷你游戏
    '逃跑許可': {'不可逃跑': 'no_flee', '可逃跑': 'can_flee'},
    '護衛': {'無護衛': 'no_guard', '有護衛': 'guarded'},
    # 🔴 決鬥場地 不在此手写——下方从 PLACE_TOKENS 派生（场所词汇单一来源）
    '決鬥場地': {},
    '迷你遊戲': {'調製藥物': 'medicine', '鐵砲打靶': 'gun_range', '建設灌溉水路': 'irrigation',
                '閃躲飛矢': 'dodge_arrows', '組合木材': 'carpentry', '二十一計': 'twentyone',
                '排列茶器': 'tea_arrange', '組合九張畫': 'picture_puzzle', '破壞方陣': 'break_formation',
                '算術填空': 'arithmetic', '弓箭射鵠': 'archery', '尋找黃金礦脈': 'gold_vein',
                '搜索人物': 'search_person', '破壞工作': 'sabotage'},
    '難度': {'初學': 'novice', '進階': 'adept', '高手': 'master'},
    # 事件文件头
    '事件屬性': {'一次': 'once', '多次': 'repeat', '弱': 'weak'},
    '主命字段': {'事件主命成果': 'quest.result', '事件主命對象': 'quest.target', '事件主命目標': 'quest.goal'},
    '主命目標類': {'主命目標銷路': 'quest.market', '主命目標商業圈': 'quest.trade_zone', '主命目標海域': 'quest.sea'},
    # 状态写入右值（更新:(左)(右) 的右值）
    '狀態值': {
        '已發生': 'done', '未發生': 'not_done', '成立': 'established', '不成立': 'not_established',
        '已認識': 'known', '不認識': 'unknown', '已出現': 'appeared', '未出現': 'hidden',
        '死亡': 'dead', '死刑': 'executed', '健康': 'healthy', '生病': 'sick',
        '在家': 'at_home', '離家': 'away', '出撃中': 'sortied', '真': 'true', '偽': 'false',
        '可能': 'available', '沒持有': 'not_owned', '持有中': 'owned', '已鑑定': 'appraised',
        '量産可能': 'mass_producible',
        # 外交 / 感情
        '盟友': 'ally', '同盟': 'alliance', '無同盟': 'no_alliance', '絶交': 'broken_off',
        '從屬': 'subordinate', '支配': 'dominated', '友好': 'friendly', '普通': 'neutral',
        '良好': 'good', '圓滿': 'excellent', '險惡': 'strained', '敵視': 'hostile',
        '沒那個意思': 'no_interest',
        # 战略目标
        '統一天下': 'unify_realm', '國內統一': 'unify_province', '分國統一': 'unify_domain',
        '地方統一': 'unify_region', '大名攻略': 'conquer_clan', '敵城攻略': 'take_castle',
        '領土守備': 'defend_territory', '國內守備': 'defend_province', '里守備': 'defend_village',
        '領土發展': 'develop_territory', '大名支援': 'support_clan', '砦增強': 'fortify',
        '上洛': 'march_kyoto',
        # 关系经纬
        '原上司': 'ex_superior', '原同事': 'ex_peer', '原屬下': 'ex_subordinate',
        '背叛了': 'betrayed', '主人公背叛': 'betrayed_by_player', '此武將背叛': 'betrayed_by_general',
        # 性格 / 其他
        '重視情義': 'values_loyalty', '活潑好動': 'lively', '小家碧玉': 'demure', '平常': 'normal',
        '全職種': 'all_classes', '只限武將': 'generals_only', '其他': 'else',
        '沒有主命': 'no_quest', '藤原氏': 'fujiwara',
    },
}

# ── 资源型枚举集：token 是数据包资源名（BGM/SE/CG/背景/设施/模板 NPC），
#    数量大且无语义映射价值 → 侧名走确定性规则（前缀 + ascii 转写/hash），逐 token 进 CSV 登记 ──
RES_SETS = {   # 🔴 资源型枚举 token 清单（数据包资源；不列清单 = 万能接收器，自检失效）
    'ＢＧＭ': {
        '上級武士主旋律', '下級武士主旋律', '事件危機', '事件希望', '事件悲愴', '事件本能寺', '事件決意', '事件溫馨', '京都', '個人戰鬥',
        '初期設定', '劍豪主旋律', '南蠻', '商人主旋律', '大名主旋律', '大老闆主旋律', '忍者主旋律', '忍者頭主旋律', '攻城戰', '正規結局',
        '沒有ＢＧＭ', '浪人主旋律', '海賊主旋律', '自宅', '茶人宅', '評定', '軍團移動', '迷你結局', '遊戲結束', '酒場', '野戰',
        '默認音樂'
    },
    'ＳＥ': {
        'ししおどし（メイン）', 'ねずみの鳴き真似（メイン）', 'アラシ知らせる（メイン）', 'クワで地面を掘る（メイン）', 'コイン（賭博）',
        'シッピン知らせる（メイン）', 'バー上昇', 'バー減少', 'プレイヤー勝利（メイン）', 'プレイヤー勝利（賭博）', 'ミニゲーム開始音（メイン）',
        '人物卡獲得音', '休養（メイン）', '休養（女）', '刀で斬られる２（メイン）', '刀を鞘から抜く', '刀碰撞', '初期設定的停止',
        '単発げんこつ（メイン）', '単発平手（メイン）', '取消音', '同名牌獲得音', '大筒の弾が飛来する', '失敗音', '建設工事音（商人司）',
        '引き戸を開ける', '忍者報告', '成功音', '提高水平', '播放聲音ー勝利', '播放聲音ー敗北', '暴風雨Ａ', '木魚（メイン）',
        '札めくり音（賭博）', '札配り音（賭博）', '歓声（メイン）', '残念な音（メイン）', '殴られる（メイン）', '決定音（バーン！）', '決定音（ポン）',
        '決定音（ｄｏ）', '液體倒入茶碗聲', '烏鴉', '無敵状態', '物音（メイン）', '猫の鳴き真似（メイン）', '生薬をくだく（メイン）', '畫面轉換音',
        '禁止音', '移動·大型船', '移動·步兵', '移動·船', '移動·船(メイン)', '移動·騎兵', '蟬鳴', '賭場特別①', '賭場特別②',
        '賭場特別③', '跳入', '通常牌獲得音', '選取音', '酒宴', '鍛冶屋', '鐵砲·射擊聲', '鐵砲擊中', '開門', '雑踏（メイン）', '雨',
        '雨(メイン)', '雪(メイン)', '骰子', '鳥（メイン）', '黑暗指令', '鼓の音（メイン）'
    },
    '事件ＣＧ': {
        'さらば親父', 'エンディング作家', 'エンディング修羅', 'エンディング旅人', 'エンディング義賊', 'エンディング軍師', '三日天下', '三枝箭',
        '上洛', '中國地方大折返', '信長的葬禮', '傾奇舞劇團', '光秀打擲', '出征', '切腹', '剣豪将軍の最期', '勝利的歡呼', '北野大茶會',
        '和寧寧結婚', '城攻陷', '填平掘溝', '大名臣服', '天下布武的象徵', '婚禮', '安和樂利的街町', '宴席', '小山評定', '小粒金',
        '巖流島的決戰', '川中島的二英雄', '強右衛門的磔刑', '拜受正一位', '攻城戰', '救出官兵衛', '敗戰', '敦盛之舞', '斬刑', '方廣寺鐘銘',
        '暗殺', '本能寺之變', '桶狹間奇襲', '正德寺的會見', '死去', '海戰', '火燒寺社', '真田十勇士', '真田隊衝鋒', '祥瑞', '空城之計',
        '結局劍豪１', '結局劍豪２', '結局商人１', '結局商人２', '結局奇人', '結局忍者１', '結局忍者２', '結局海賊１', '結局海賊２',
        '結局茶人', '結局農民', '結局醫師', '結局鍛冶工匠', '結局風雅之士', '統一天下', '義昭的陰謀', '義隆の最期', '議論', '輔佐統一天下',
        '輝宗之死', '野戰', '鍋煮五右衛門', '鐵甲船出渠', '長篠合戰', '阿市御寮人', '阿鼻交換地獄', '骷髏之酒', '鹿介對月兒發誓'
    },
    '背景': {
        '主人公評定', '初期設定', '合戰畫面', '商家', '城主間', '寺', '忍屋敷', '據點內畫面', '武家宅', '民家', '海賊屋敷', '海道',
        '自宅', '評定間', '賭博所', '路口', '道場', '陸道', '黑暗'
    },
    '設施': {
        '主人公茶室', '主人公診療所', '主人公評定', '主人公道場', '主人公鍛冶屋', '公家宅', '南蠻商館', '南蠻寺', '商家', '城主間',
        '城練兵場', '宿屋', '寺', '座', '御所', '忍屋敷', '忍者宅', '據點內畫面', '武家宅', '民家', '海外交易所', '海賊宅',
        '海賊屋敷', '砦修業場', '砦練兵場', '米屋', '職人宅', '自宅', '茶人宅', '評定間', '造船所', '道場', '酒場', '醫師宅',
        '里修業場', '里練兵場', '鍛冶屋', '馬屋'
    },
    '模板NPC': {
        '凄腕用心棒', '喝醉的女人', '喝醉的男人', '奇怪的姑娘', '女の子', '婆さん', '小孩', '明國商人', '槍術師範代',
        '琉球商人', '米屋的老闆', '賊', '頭目'
    },
    '觸發': {
        '人物對話時', '合戰決定時', '大名家滅亡時', '室內畫面表示後', '據點畫面表示後', '攻城戰結束時', '攻城戰開始時', '每日處理的開頭',
        '每月處理的最後', '每月處理的最後絕對', '移動畫面表示後', '章節凍結時', '評定開始時', '軍團移動結束時', '軍團移動開始時', '遊戲結束時',
        '遊戲通關時', '遊戲開始時', '選擇移動畫面時', '選擇設施時', '野戰結束時', '野戰開始時'
    },
}

RES_PREFIX = {
    'ＢＧＭ': 'Bgm', 'ＳＥ': 'Se', '事件ＣＧ': 'Cg',
    '設施': 'Facility', '背景': 'Background', '模板NPC': 'Npc', '觸發': 'Trigger',
}


RES_TOKEN_TABLE = {           # 资源集 → 单一词表（表内有 = 用可读 token；BGM/SE/CG 无表，走转写/hash）
    '設施': PLACE_TOKENS, '背景': PLACE_TOKENS, '觸發': TRIGGER_TOKENS, '模板NPC': NPC_TOKENS,
}


def res_side(setname, tok):
    """资源型枚举侧名。有单一词表的查表（設施/背景/觸發/模板NPC），
    其余（ＢＧＭ/ＳＥ/事件ＣＧ = 纯媒体资产名，07 素材表落文件）走全角转写 + hash 兜底。"""
    p = RES_PREFIX[setname]
    tbl = RES_TOKEN_TABLE.get(setname)
    if tbl is not None and tok in tbl:
        return '%s::%s' % (p, tbl[tok])
    # 媒体资产（ＢＧＭ/ＳＥ/事件ＣＧ）：名字若已在别处注册过（自宅/上洛/統一天下），复用其 slug
    g = WORD_SLUG.get(tok) if 'WORD_SLUG' in globals() else None
    return '%s::%s' % (p, g or ascii_translit(tok) or fallback_id(tok))



# ═══════════════════════════════════════════════════════════════════════════
# 🔴 词汇表单一来源（2026-08-27 用户抓包）：集名 == 域名 时，枚举集**派生**自 DOMAIN_VAL_MAP。
#   在此之前 身份 被写了两遍（域值 30 条 / 枚举 19 条），10 个词两个侧名，
#   ashigaru_captain 跨表指两个不同品级。手写副本 = 迟早分叉，改成派生 + 断言。
#   要加新词只改 DOMAIN_VAL_MAP 一处。
# ═══════════════════════════════════════════════════════════════════════════
DERIVED_ENUM_SETS = ('真偽', '身份', '人物類別', '物品類型')
for _n in DERIVED_ENUM_SETS:
    ENUM_SETS[_n] = {_v: _s for (_d, _v), _s in DOMAIN_VAL_MAP.items() if _d == _n}
    assert ENUM_SETS[_n], '派生枚举集为空：%s（DOMAIN_VAL_MAP 里没有该域的值）' % _n

# 決鬥場地 = PLACE_TOKENS 的子集（决斗能打的那几个场所）；軍團槽 = ARMY_SLOTS 全量
DUEL_PLACES = ('原野', '城主間', '民家庭院', '武家宅庭院', '道場', '酒場',
               '忍者宅庭院', '沙灘', '船的甲板')
ENUM_SETS['決鬥場地'] = {_p: PLACE_TOKENS[_p] for _p in DUEL_PLACES}
ENUM_SETS['軍團槽'] = dict(ARMY_SLOTS)

# ═══════════════════════════════════════════════════════════════════════════
# 🔴 全局「TK5 词 → slug」总表：把各词表已注册的 slug 汇总一张，供**无专表**的
#   媒体资产集（ＢＧＭ/ＳＥ/事件ＣＧ）兜底——曲名/CG 名往往就是某个场所或事件词
#   （自宅 / 上洛 / 統一天下），有现成 slug 就别再吐 hash。
#   歧义词（多张表给了不同 slug）不收，宁可 hash 也不猜。
# ═══════════════════════════════════════════════════════════════════════════
def _collect_word_slugs():
    seen, bad = {}, set()
    tables = [PLACE_TOKENS, TRIGGER_TOKENS, NPC_TOKENS]
    tables += [{_v: _s for (_d, _v), _s in DOMAIN_VAL_MAP.items()}]
    tables += list(ENUM_SETS.values())
    for tbl in tables:
        for w, s in tbl.items():
            sl = str(s).split('::')[-1]
            if not sl or not re.fullmatch(r'[A-Za-z][A-Za-z0-9_.]*', sl):
                continue                       # 数字字面量/空 slug 不进总表
            if w in seen and seen[w] != sl:
                bad.add(w)                     # 歧义：两张表给了不同 slug
            seen.setdefault(w, sl)
    for w in bad:
        seen.pop(w, None)
    return seen


WORD_SLUG = _collect_word_slugs()


def enum_side(setname, tok):
    """枚举 token → 侧名。语义集查表（表外 = None → 生成期报错）；资源集走规则。"""
    if setname in RES_PREFIX:
        return res_side(setname, tok) if tok in RES_SETS.get(setname, ()) else None
    return ENUM_SETS.get(setname, {}).get(tok)


# ── 具名实体名单 ──
#   ① 语料实测：域::名 且 域 ∈ ENTITY_DOMAINS（人物::織田信長 → 織田信長 是具名人物）
#   ② EXTRA_ENTITY_NAMES：只在**参数位/台词插值**出现、从未写成 域::名 的具名实体（补录）
ENTITY_NAMES = set()
for _d, _v in re.findall(r'([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6})::([々〆ヶ・·一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ0-9０-９_]{1,16})', body):
    if _d in ENTITY_DOMAINS:
        ENTITY_NAMES.add(_v)

EXTRA_ENTITY_NAMES = {
    # 只作为命令裸参数/台词插值出现的具名人物（无 人物::X 形态）
    '高阪甚內': 'Hero', '新庄': 'Hero', '阿福': 'Hero', '安岐': 'Hero', '三條': 'Hero',
    '小督': 'Hero', '彦鶴': 'Hero', '煕子': 'Hero', '幸圓': 'Hero', '春桃': 'Hero',
    '細川晴元': 'Hero', '吉見正賴': 'Hero', '木曾義昌': 'Hero', '南姫': 'Hero', '德公主': 'Hero',
    '立花直次': 'Hero', '北信愛': 'Hero', '安芸國虎': 'Hero', '谷忠澄': 'Hero',
    '三法師': 'Hero', '九郎判官義経': 'Hero', '肉戶梅軒': 'Hero', '長谷川宗仁': 'Hero',
    '里璐': 'Hero', '瀬名': 'Hero', '拉斐耶魯': 'Hero',
}

RE_HERO_NUM = re.compile(r'^人物[0-9０-９]+$')          # 人物１００８ = 编号人物引用
RE_UNKNOWN_N = re.compile(r'^未知[0-9０-９]+$')          # 未知13 / 未知４９６ = 反编译碎片


def entity_side(tok):
    """具名实体 → 侧名（名字表先行，fallback_id 兜底）。非实体返回 None。"""
    if tok in EXTRA_ENTITY_NAMES:
        return '%s::%s' % (EXTRA_ENTITY_NAMES[tok], fallback_id(tok))
    if tok in ENTITY_NAMES:
        return 'Entity::%s' % fallback_id(tok)
    if RE_HERO_NUM.match(tok):
        return 'Hero::hero_%s' % (ascii_translit(tok[2:]) or tok[2:])
    return None


# ── 台词插值登记（[[正文]] 里的 {变量} / <变量> / (主体.字段)）──
TEXT_FIELDS = {                     # 主体.字段
    '姓': '.family_name', '名': '.given_name', '名前': '.name',
    '姓名': '.full_name', '代名詞': '.pronoun',
}
TEXT_VARS = {                       # 裸变量
    '一人稱': 'Text::first_person', '二人稱': 'Text::second_person',
    '二人稱名前': 'Text::second_person_name', '武器': 'Text::weapon',
    '年': 'Time::year', '月': 'Time::month', '日': 'Time::day',
}


EXTRA_SETTLEMENT_NAMES = {   # 只在命令参数位出现、从不写成 城::X 的具名据点
    '三木城', '丸龜之町', '二本松城', '二股城', '伊賀之里', '佐東銀山城', '八代城', '勝龍寺城', '厩橋之町', '吉田城', '唐澤山城',
    '墨股築城', '大和郡山城', '大垣城', '大津城', '大阪之町', '大阪城', '大阪築城', '姫路城', '宇都宮城', '安土之町', '安土城',
    '安土築城', '富山之町', '小倉之町', '小山城', '小濱之町', '小田原城', '小諸之町', '尾道之町', '岐阜之町', '岐阜城', '岡崎之町',
    '岡崎城', '岩槻城', '岩殿城', '平戶之町', '府內城', '弘前之町', '忍城', '戶石城', '曳馬城', '有岡城', '木曾福島城',
    '松倉之町', '柳川之町', '櫻尾城', '水攻高松城', '江戶之町', '江戶城', '河越城', '沼田城', '浦戶之町', '湯築城', '濱松之町',
    '濱松城', '玉繩城', '甲府之町', '甲府城', '白石城', '白鹿城', '石山本願城', '稻村城', '興國寺城', '躑躅崎城', '軒猿之里',
    '那古野城', '長濱之町', '長濱城', '長篠城', '長船之町', '須賀川城', '飯田城', '飯盛城', '駿府之町', '駿府城', '高天神城',
    '魚津城', '鳥羽城', '鳴門之町', '鹿兒島之町', '黑川城'
}
for _t in EXTRA_SETTLEMENT_NAMES:
    EXTRA_ENTITY_NAMES.setdefault(_t, 'Settlement')

# ── 属性取值空间：「容器篩選:(城,所屬國,紀伊)」第三参收什么，由第二参那个属性决定 ──
#    '域:X' = 该值是 X 域的成员（走 val_side）；'枚:X' = 该值是枚举集 X 的 token
ATTR_VALUE_SPACE = {
    '所屬國': '域:國', '所在地方': '域:地方', '所屬據點': '域:據點', '本據': '域:據點',
    '本城': '域:城', '所屬大名家': '域:大名家', '所屬當主': '域:人物', '所屬上司': '域:人物',
    '城主': '域:人物', '當主': '域:人物', '所有者': '域:人物', '妻': '域:人物',
    '所屬勢力': '域:勢力', '所屬海賊衆': '域:海賊衆', '所屬忍者衆': '域:忍者衆',
    '類別': '域:人物類別', '身份': '域:身份', '官位': '域:官位', '官職': '域:官職',
    '戰略': '域:戰略', '戰略目標': '域:戰略', '立場': '域:立場', '承擔主命': '域:主命',
    '物品種類': '枚:物品種類', '武器種類': '枚:武器種類', '性別': '枚:性別',
    '出現標誌': '枚:出現狀態', '死亡標誌': '枚:生存狀態', '武將': '枚:真偽',
}

# ── 容器字段属性：只作「容器篩選/排序/排除的字段名」出现，没有 域::主体.属性 形态 ──
ATTR_EXTRA = {
    '人口': 'Settlement.population', '物品種類': 'Item.category', '武器種類': 'Item.weapon_class',
    '類別': 'Hero.category', '石高': 'Settlement.income', '商業': 'Settlement.trade',
}
RE_INDEX_ATTR = re.compile(r'^[^0-9]{1,6}番號$')       # 人物番號/城番號/物品番號… = 对象序号


def attr_side_any(tok):
    """属性名（不带域）→ 侧名。容器字段/序号字段走补充表与规则。"""
    if tok in ATTR_EXTRA:
        return ATTR_EXTRA[tok]
    if RE_INDEX_ATTR.match(tok):
        return '%s.index' % ENTITY_DOMAINS.get(tok[:-2], 'Object')
    return next((pair_side(d, tok) for d in PREFIX_BY_DOMAIN if pair_side(d, tok)), None)


def value_space_side(attr, tok):
    """按属性的取值空间解释一个值（容器三参式）。"""
    sp = ATTR_VALUE_SPACE.get(attr)
    if not sp:
        return None
    kind, name = sp.split(':', 1)
    return val_side(name, tok) if kind == '域' else enum_side(name, tok)


# ═══ 命令参数位签名（位 → 收什么）═══
#   位类型：'E' 具名实体 / 'D' 域名 / 'A' 属性名 / '<枚举集名>'
#   任何位都隐含允许：数字、槽（人物Ａ/ａ）、特殊值（主人公/無效…）、域::X 形态、事件 ID、空参
#   键 '*' = 命令头值（發生契機:據點畫面表示後(…) 里冒号后、括号前那一段）
CMD_ARG_SPEC = {
    # ── 容器（pick 组）──
    '容器篩選': {0: ('D',), 1: ('A',), 2: ('VA', 'E', '真偽', '狀態值', '身份', '物品種類',
                                        '武器種類', '生存狀態', '軍團槽', '人物類別')},
    '容器排除': {0: ('D',), 1: ('A',), 2: ('VA', 'E', '身份', '真偽', '狀態值', '人物類別', '物品種類')},
    '容器設定': {0: ('D',), 1: ('A',), 2: ('VA', 'E', '真偽', '人物類別', '物品種類', '狀態值')},
    '容器排序': {0: ('D',), 1: ('A', '排序特殊鍵'), 2: ('排序方向',)},
    '容器選擇': {1: ('容器位置',)},
    '容器清理': {0: ('容器清理',)},
    '容器存取': {0: ('容器存取',)},
    '容器檢索': {0: ('D',), 1: ('A',)},
    # ── 状态写入 / 条件 ──
    '更新': {1: ('狀態值',)},
    '調查': {0: ('容器統計',), 1: ('狀態值', '出現狀態', '性別', '生存狀態', 'D', 'E')},
    '場合分歧': {0: ('其他分支', '狀態值')},
    '主人公分歧': {0: ('E', '其他分支', '模板NPC')},
    '遊戲中斷': {0: ('真偽',)},
    # ── 演出（05）──
    'ＢＧＭ變更': {0: ('ＢＧＭ',)},
    'ＳＥ開始': {0: ('ＳＥ',)}, 'ＳＥ循環': {0: ('ＳＥ',)}, 'ＳＥ停止': {0: ('ＳＥ',)},
    '圖片表示': {0: ('圖片類型',), 1: ('事件ＣＧ', 'E'), 4: ('轉場',)},
    '圖片消去': {0: ('轉場',)},
    '背景變更': {0: ('背景類型',), 1: ('背景', 'E'), 2: ('轉場',)},
    '背景恢復': {0: ('轉場',)},
    '畫面效果': {0: ('畫面效果',)},
    '進入設施': {0: ('設施',)},
    '下個場面': {0: ('設施',)},
    # ── 事件文件头 ──
    '屬性': {'*': ('事件屬性',)},
    '發生契機': {'*': ('觸發',), 0: ('E', '設施', '軍團槽', '生存狀態', '身份', '觸發', '通關方式'),
                1: ('E', '設施', 'D'), 2: ('軍團指令', 'E'), 3: ('E', '軍團槽')},
    # ── 对话 ──
    '對話': {0: ('E', '模板NPC', '域:身份'), 1: ('E', '模板NPC', '域:身份')},
    '變名對話': {0: ('E', '模板NPC', '域:身份'), 1: ('E', '模板NPC', '域:身份')},
    '對話選擇': {0: ('E', '模板NPC', '域:身份'), 1: ('E', '模板NPC', '域:身份')},
    '對話可否選擇': {0: ('E', '模板NPC', '域:身份'), 1: ('E', '模板NPC', '域:身份')},
    # ── 军团（02）──
    '軍團指令': {0: ('軍團槽',), 1: ('軍團指令',), 2: ('E', '軍團槽'), 3: ('軍團槽',)},
    # ── 人事 / 势力 ──
    '人物登用': {0: ('E',), 1: ('從屬類型',), 2: ('E',)},
    '人物解雇': {0: ('E',), 1: ('E',), 2: ('出現狀態',)},
    '立場變更': {0: ('E',), 1: ('從屬類型',), 2: ('E',)},
    '獨立': {0: ('E',), 1: ('E',), 2: ('獨立方式',)},
    '改名': {0: ('E',)}, '據點改名': {0: ('E',), 1: ('E',)},
    '強制移動': {0: ('E',)}, '居城變更': {1: ('E',)},
    '城主任命': {0: ('E',), 1: ('E',)}, '城主解任': {0: ('E',)},
    '國主任命': {0: ('E',), 1: ('E',), 2: ('E',)}, '國主解任': {0: ('E',)},
    '家督讓位': {0: ('E',), 1: ('E',)},
    '勢力滅亡': {0: ('E',), 1: ('E',)}, '武將死亡': {0: ('E',)},
    '成為御用商人': {0: ('E',), 1: ('E',)},
    '主命作成': {0: ('E',), 1: ('E',), 2: ('域:主命',)}, '事件主命作成': {0: ('E',), 1: ('域:主命',), 2: ('域:主命',)}, '解除主命': {0: ('E',)},
    '事件主命變更': {0: ('主命字段',)},
    # ── 战斗 / 小游戏 ──
    '個人戰鬥': {0: ('逃跑許可',), 1: ('護衛',), 2: ('E', '身份', '模板NPC'),
                3: ('E', '身份', '模板NPC'), 4: ('E', '身份', '模板NPC'),
                5: ('E', '身份', '模板NPC'), 6: ('E', '身份', '模板NPC'),
                7: ('E', '身份', '模板NPC'), 8: ('決鬥場地',), 9: ('真偽',), 10: ('真偽',)},
    '迷你遊戲': {0: ('迷你遊戲',), 1: ('E', '模板NPC'), 2: ('難度',)},
    '強制武器交換': {0: ('武器種類',)},
}

# 军团编成家族（軍團編成/軍團編成最強/海賊軍團編成*/忍者軍團編成*）参数位一致 → 共用签名
_ARMY_FORM = {0: ('軍團槽',), 1: ('E',), 2: ('軍團指令',), 3: ('E', '軍團槽'), 4: ('軍團槽', 'E'),
              5: ('E',), 6: ('E',), 7: ('E',), 8: ('E',), 9: ('E',), 10: ('E',),
              11: ('零值',), 12: ('零值',), 13: ('零值',), 14: ('零值',), 15: ('零值',),
              16: ('零值',), 17: ('零值',)}
for _p in ('', '海賊', '忍者'):
    for _s in ('軍團編成', '軍團編成最強'):
        CMD_ARG_SPEC[_p + _s] = dict(_ARMY_FORM)

# 代入族（代入ａ/代入ｔ…）：右值可为容器统计量
CMD_ARG_PREFIX = {'代入': {1: ('容器統計',)}}


def arg_spec(cmd, pos):
    """(命令, 位) → 允许的类型元组；未登记位返回 ()（只收数字/槽/特殊值/域引用）。"""
    spec = CMD_ARG_SPEC.get(cmd)
    if spec is None:
        for pre, s in CMD_ARG_PREFIX.items():
            if cmd.startswith(pre):
                spec = s
                break
    return (spec or {}).get(pos, ())


def arg_side(cmd, pos, tok, args=None):
    """命令裸参数 → 侧名。表外返回 None（生成期报错）。

    args = 同一条命令的全部参数（有的位要看兄弟参数才知道收什么：
           容器篩選:(城,所屬國,紀伊) 第三参的取值空间由第二参属性 所屬國 决定）。
    """
    for k in arg_spec(cmd, pos):
        if k == 'E':
            s = entity_side(tok)
        elif k == 'D':
            s = DOMAIN_MAP.get(tok) and ('Domain::' + tok)
        elif k == 'A':
            s = attr_side_any(tok)
        elif k == 'VA':
            s = value_space_side(args[1], tok) if args and len(args) > 1 else None
        elif k.startswith('域:'):
            s = val_side(k[2:], tok)
        else:
            s = enum_side(k, tok)
        if s:
            return s
    return None


# ═══════════════════════════════════════════════════════════════════════════
# 🔴 v4（2026-08-27）：结构化语料解析 —— 命令头值 / 参数位 / 台词插值 全量抽取，
#     供 verify_coverage() 断言（表外 = 生成失败）。旧版只解析 `域::X` 形态，
#     命令裸参数位（枚举/资源名/容器字段/触发名）与台词插值不在自检范围内。
# ═══════════════════════════════════════════════════════════════════════════
_JA = r'々〆ヶ・·一-鿿㐀-䶿぀-ヿＡ-Ｚａ-ｚA-Za-z0-9０-９_'
RE_LINE_HEAD = re.compile(r'^([' + _JA + r']{1,12}):')
RE_TOKENISH = re.compile('[' + _JA + ']+')
RE_NUMLIT = re.compile(r'-?[0-9]+(?:\.[0-9]+)?')
RE_SLOTLIKE = re.compile(r'^([' + _JA + r']{1,8}?)([Ａ-Ｅ])$')
RE_VARSLOT = re.compile(r'^[ａ-ｚ]$')
RE_EVENTID = re.compile(r'^(?:事件)?[A-Z]{1,3}[0-9A-F]{4,8}_[0-9]+$')
RE_HEXSEQ = re.compile(r'^(?:[0-9A-F]{2}\s*)+$')
RE_INTERP = re.compile(r'\{([^{}]*)\}|<([^<>]*)>|\(([^()]*)\)')
_SLOT_EXTRA = ('文字列', '數值', '變量', '容器')


def is_slotlike(tok):
    """槽位记号：人物Ａ / 大名家Ｂ / 文字列Ａ / ａ（代入变量）。"""
    m = RE_SLOTLIKE.match(tok)
    if m and (m.group(1) in SLOT_CAT or m.group(1) in _SLOT_EXTRA):
        return True
    return bool(RE_VARSLOT.match(tok))


def is_structural(tok):
    """结构性 token（数字/事件ID/槽/特殊值/解析碎片）—— 不需要词条落点。"""
    return bool(not tok or RE_NUMLIT.fullmatch(tok) or RE_EVENTID.match(tok)
                or is_slotlike(tok) or tok in SPECIAL_VALS
                or RE_UNKNOWN_N.match(tok) or RE_HEXSEQ.fullmatch(tok))


def _split_args(inner):
    """顶层逗号切分（跳过嵌套括号，保留 決定音（バーン！）这类含括号的整参）。"""
    out, buf, depth = [], '', 0
    for ch in inner:
        if ch == '(':
            depth += 1
        elif ch == ')':
            depth -= 1
        if ch in ',，' and depth == 0:
            out.append(buf.strip())
            buf = ''
        else:
            buf += ch
    out.append(buf.strip())
    return out


def _parse_command_lines(text):
    """语料 → (头值 Counter, 参数位 dict, 插值 Counter)。

    参数位 dict：(命令, 位) → {token: [次数, 兄弟参数列表]}——兄弟参数供 'VA' 位
    （容器篩選 第三参的取值空间由第二参属性决定）解析用。
    位序跨括号组连续编号，与语料一致（更新:(左)(右) = pos0/pos1）。
    """
    heads, argpos, interps = Counter(), {}, Counter()
    for raw in text.splitlines():
        s = raw.strip()
        if not s or s.startswith(('#', '{', '}')):
            continue
        m = RE_LINE_HEAD.match(s)
        if not m:
            continue
        cmd, rest = m.group(1), s[m.end():]
        i, n, pos, seen_group, allargs = 0, len(s) - m.end(), 0, False, []
        groups = []
        while i < n:
            if rest.startswith('[[', i):
                e = rest.find(']]', i)
                interps.update(_talk_interps(rest[i + 2:e if e >= 0 else n]))
                i = (e + 2) if e >= 0 else n
                continue
            if rest.startswith('//', i):
                break
            if rest[i] == '(':
                depth, j = 1, i + 1
                while j < n and depth:
                    if rest[j] == '(':
                        depth += 1
                    elif rest[j] == ')':
                        depth -= 1
                    j += 1
                g = _split_args(rest[i + 1:j - 1])
                groups.append(g)
                allargs += g
                seen_group = True
                i = j
                continue
            mh = RE_TOKENISH.match(rest, i)
            if mh and not seen_group:
                heads[(cmd, mh.group(0))] += 1
                i = mh.end()
                continue
            i += 1
        for g in groups:
            for a in g:
                t = a
                mc = RE_ARG_CALL.match(t)
                if mc and CALL_MAP.get(mc.group(1)):
                    t = mc.group(2)          # 带参属性调用 外交同盟(大名家Ａ) → 查内层参数
                if is_term_arg(t):
                    slot = argpos.setdefault((cmd, pos), {}).setdefault(t, [0, allargs])
                    slot[0] += 1
                pos += 1
    return heads, argpos, interps


EXPR_OPS = ('==', '!=', '>=', '<=', '>', '<', '+', '=', '*', '/', '%', '｜')
RE_ARG_CALL = re.compile(r'^([' + _JA + r']{1,10})[(（]([^()（）]*)[)）]$')


def is_term_arg(a):
    """这个参数是不是「需要词条落点」的整参？

    排除：结构性 token（数字/槽/碎片）、`域::X` 形态（另有域/属性/域值断言覆盖）、
    运算表达式（同上）、`[[字面文本]]`（變名對話 的临时姓名，走台词插值断言）。
    """
    if not a or is_structural(a) or '::' in a or a.startswith('[['):
        return False
    return not any(o in a for o in EXPR_OPS)


def _talk_interps(payload):
    """台词正文里的 {变量} / <变量> / (主体.字段) —— 正文自然语言不检查，引用要检查。"""
    out = Counter()
    for m in RE_INTERP.finditer(payload):
        inner = (m.group(1) or m.group(2) or m.group(3) or '').strip()
        if not inner or not RE_TOKENISH.search(inner):
            continue                    # 纯标点 / 自然语言括注
        out[inner] += 1
    return out


head_vals, arg_positions, talk_interps = _parse_command_lines(txt)


def interp_side(inner):
    """台词插值 → 侧名；表外返回 None。"""
    if '.' in inner:
        subj, attr = inner.split('.', 1)
        fld = TEXT_FIELDS.get(attr) or attr_side_any(attr)
        if not fld:
            return None
        if is_slotlike(subj) or subj in SPECIAL_VALS or entity_side(subj):
            return 'Text%s' % fld if fld.startswith('.') else fld
        return None
    if inner in TEXT_VARS:
        return TEXT_VARS[inner]
    if inner in ENUM_SETS['主命目標類']:
        return ENUM_SETS['主命目標類'][inner]
    if RE_UNKNOWN_N.match(inner) or RE_NUMLIT.fullmatch(inner) or is_slotlike(inner) \
            or inner in SPECIAL_VALS:
        return 'Text::raw'
    return entity_side(inner)


def head_val_side(cmd, tok):
    """命令头值（屬性:一次｜弱 / 發生契機:據點畫面表示後）→ 侧名；表外 None。"""
    if is_structural(tok):
        return 'Literal'
    return arg_side(cmd, '*', tok) or entity_side(tok)


# ═══════════════════════════════════════════════════════════════════════════
# 🔴 跨表侧名分叉自检（2026-08-27）：同一个 TK5 词在两张词表里给出不同 slug =
#   翻译器不知道该按哪个走（身份 双表分叉的教训：ashigaru_captain 一名两指）。
#   合法的一词多义写进 TWIN_EXEMPT，其余一律生成期报错。
# ═══════════════════════════════════════════════════════════════════════════
MEDIA_SETS = ('ＢＧＭ', 'ＳＥ', '事件ＣＧ')     # 媒体资产名 ≠ 语义词，与语义表重名不算分叉

TWIN_EXEMPT = {
    # (TK5 词, slugA, slugB) —— 确实是两个意思，不是分叉
    ('終結', 'ended', 'disband'),                   # 戰鬥結束種類「战斗以终结收场」/ 軍團指令「解散军团」
    ('歸還', 'intent_return_home', 'return_home'),  # 軍團方針 = 持续方针（02 PartyIntent）/ 軍團指令 = 一次性命令
}


# 🔴 属性侧名塌缩自检（2026-08-27 patch12）：同一个域内，两个不同的太阁属性映射到同一个
#   我们侧字段 = 翻译器分不开、写回会串（旧塌缩断言只查词汇表，不查属性表，漏了整整一类）。
#   数据源 = 语料实测的「域-属性」对 attr_pairs；真同义词写进 ATTR_SYNONYM_EXEMPT。
ATTR_SYNONYM_EXEMPT = {
    # (域, 属性A, 属性B) —— 确实是同一个字段的两种拼写，不是塌缩
    ('人物', '裝備武器', '装備武器'),      # 繁/简两种转储拼写
    ('人物', '裝備防具', '装備防具'),
}


def attr_synonym_collisions():
    """返回 [(域, [属性…], 侧名)] —— 同域内多个属性共用一个侧名的塌缩清单。"""
    by = {}
    for (d, a), c in attr_pairs.items():
        if a in CALL_MAP:
            continue            # 带参调用形态归函数区，不产属性行（外交感情/未知8/國屬性1）
        if a.isdigit() and digit_attr_class(d, a)[0] in ('B', 'C'):
            continue            # B=域值的数字ID后缀 / C=转储原始数值引用：本就不是属性
        sd = pair_side(d, a)
        if sd is None:
            continue            # 表外 → 由覆盖自检单独报错
        by.setdefault((d, sd), []).append(a)
    out = []
    for (d, sd), attrs in sorted(by.items()):
        if len(attrs) < 2:
            continue
        attrs = sorted(attrs)
        if any((d, x, y) in ATTR_SYNONYM_EXEMPT or (d, y, x) in ATTR_SYNONYM_EXEMPT
               for i, x in enumerate(attrs) for y in attrs[i + 1:]) and len(attrs) == 2:
            continue
        out.append((d, attrs, sd))
    return out


def val_synonym_collisions():
    """同一个域内两个不同的域值共用一个侧名 = 塌缩（日數計數器::10/12 都叫 Time::day 那种）。"""
    by = {}
    for (d, v) in domain_vals:
        if d in ENTITY_DOMAINS:
            continue        # 具名实体域不进 CSV（走名字表）
        sd = val_side(d, v)
        if sd is None:
            continue
        by.setdefault((d, sd), set()).add(v)
    return [(d, sorted(vs), sd) for (d, sd), vs in sorted(by.items()) if len(vs) > 1]


def enum_member_collisions():
    """同一个枚举里两个不同的太阁词共用一个成员名 = 塌缩。"""
    out = []
    for name, tbl in ENUM_SETS.items():
        by = {}
        for k, v in tbl.items():
            by.setdefault(v, []).append(k)
        for v, ks in sorted(by.items()):
            if len(ks) > 1:
                out.append((name, sorted(ks), v))
    return out


def all_vocabs():
    """所有词表 → {表名: {TK5 词: 侧名}}（域值 / 枚举 / 资源集）。"""
    out, dv = {}, {}
    for (_d, _v), _s in DOMAIN_VAL_MAP.items():
        dv.setdefault(_d, {})[_v] = _s
    for _d, _m in dv.items():
        out['域:' + _d] = _m
    for _n, _m in ENUM_SETS.items():
        out['枚:' + _n] = _m
    for _n, _ts in RES_SETS.items():
        out['资:' + _n] = {_t: res_side(_n, _t) for _t in _ts}
    return out


def twin_divergences():
    """返回 [(词, 表A, slugA, 表B, slugB)] —— 同词异 slug 的分叉清单。"""
    vocabs = all_vocabs()
    keys = sorted(vocabs)
    out = []
    for i in range(len(keys)):
        for j in range(i + 1, len(keys)):
            ka, kb = keys[i], keys[j]
            if ka[2:] in MEDIA_SETS or kb[2:] in MEDIA_SETS:
                continue
            a, b = vocabs[ka], vocabs[kb]
            for w in sorted(set(a) & set(b)):
                sa, sb = str(a[w]).split('::')[-1], str(b[w]).split('::')[-1]
                if sa == sb or (w, sa, sb) in TWIN_EXEMPT or (w, sb, sa) in TWIN_EXEMPT:
                    continue
                out.append((w, ka, sa, kb, sb))
    return out


# ═══ 生成期自检：全语料覆盖断言（表外 = 生成失败）═══
def verify_coverage():
    errors = []
    for (d, sj, a), c in attr_triples.items():
        subj = sj.strip().splitlines()[-1] if sj.strip() else ''
        if pair_side(d, a, subj) is None:
            errors.append(f'属性表外: {d}::{subj}.{a} ×{c}')
    for (d, v), c in domain_vals.items():
        if d in ENTITY_DOMAINS:
            continue        # 实体引用域：翻译器名字表/确定性兜底，不要求 CSV 行
        if val_side(d, v) is None:
            errors.append(f'域值表外: {d}::{v} ×{c}')
    for (d, a), c in calls.items():
        if call_side(d, a) is None:
            errors.append(f'调用表外: {d}::{a}(…) ×{c}')
    for k, c in cmds.items():
        if k not in CMD_MAP and cmd_rule(k) == '🔴 低频 → 降级/忽略':
            errors.append(f'命令表外: {k} ×{c}')
    for (c_, h_), n_ in head_vals.items():
        if head_val_side(c_, h_) is None:
            errors.append(f'命令头值表外: {c_}:{h_} ×{n_}')
    for (c_, p_), toks in arg_positions.items():
        for t_, (n_, sib_) in toks.items():
            if arg_side(c_, p_, t_, sib_) is None:
                errors.append(f'命令参数位表外: {c_}[pos{p_}]:{t_} ×{n_}')
    for i_, n_ in talk_interps.items():
        if interp_side(i_) is None:
            errors.append(f'台词插值表外: {{{i_}}} ×{n_}')
    # 🔴 词汇表内 token→侧名单射（2026-08-27）：两个不同的词共用一个侧名 = 语义塌缩
    #   （身份是带序枚举，頭/頭領 都叫 chief 会把品级链压扁）
    _vocab = {}
    for (d_, v_), s_ in DOMAIN_VAL_MAP.items():
        _vocab.setdefault('域值:' + d_, {}).setdefault(s_, []).append(v_)
    for n_, m_ in ENUM_SETS.items():
        if n_ in DERIVED_ENUM_SETS:
            continue                      # 派生自域值表，查一遍即可
        for t_, s_ in m_.items():
            _vocab.setdefault('枚举:' + n_, {}).setdefault(s_, []).append(t_)
    for name_, bys_ in _vocab.items():
        for s_, ts_ in bys_.items():
            if len(ts_) > 1:
                errors.append(f'侧名塌缩: {name_} 的 {"/".join(sorted(ts_))} 共用侧名 {s_}')
    # 🔴 派生集与域值表逐条一致（防以后有人再手写一份副本复辟分叉）
    for n_ in DERIVED_ENUM_SETS:
        want_ = {v_: s_ for (d_, v_), s_ in DOMAIN_VAL_MAP.items() if d_ == n_}
        if ENUM_SETS.get(n_) != want_:
            errors.append(f'词汇表分叉: ENUM_SETS[{n_}] 与 DOMAIN_VAL_MAP 的 {n_} 域值不一致')
    for d_, attrs_, sd_ in attr_synonym_collisions():
        errors.append(f'属性侧名塌缩: {d_} 域的 {"/".join(attrs_)} 共用侧名 {sd_}'
                      f'（一个字段存多件事 → 翻译器分不开、写回会串；拆开或写进 ATTR_SYNONYM_EXEMPT）')
    for d_, vals_, sd_ in val_synonym_collisions():
        errors.append(f'域值侧名塌缩: {d_} 域的 {len(vals_)} 个值（{"/".join(vals_[:6])}…）共用侧名 {sd_}')
    for e_, ks_, mb_ in enum_member_collisions():
        errors.append(f'枚举成员塌缩: {e_} 里的 {"/".join(ks_)} 共用成员名 {mb_}')
    for w_, ka_, sa_, kb_, sb_ in twin_divergences():
        errors.append(f'跨表侧名分叉: {w_} 在 {ka_}={sa_} / {kb_}={sb_}'
                      f'（同一个词两个侧名 → 翻译器按哪个走？合并到单一词表，或写进 TWIN_EXEMPT）')
    return errors


# ═══ 命令 174 → 落点（不变）═══
CMD_MAP = {}
CMD_EXACT = {
    '對話': '05 lines[] speaker/textKey', '調查': 'condition 表达式（when→condition）', '分歧': 'script 分支（if 步骤，01 骨架）',
    '更新': '动作/ctx_set（16 动作表）', '旁白': '05 narrator 行', '容器篩選': 'container_filter',
    '容器選擇': 'container_pick', '自語': '05 narrator/自语行', '容器設定': 'container_set',
    'ＢＧＭ變更': '05 bgm 指令（异步）', '容器排除': 'container_exclude', '事件': '事件 JSON id（头字段）',
    '屬性': '事件 JSON trigger/once/priority（头字段，2026-08-26 数据化）', '發生契機': '事件 JSON trigger 字段（头字段）',
    '發生條件': '事件 JSON condition 字段（头字段）', '執行': '事件 JSON script（头字段）', '容器清理': 'container_clear',
    '脫出模塊': '🔴 流程控制（事件内循环，首版线性展开）', '循環': '🔴 流程控制（首版线性展开）',
    'ＡＮＤ調查': 'condition and(…)', 'ＯＲ調查': 'condition or(…)', '變名對話': '05 变名节点（动作表现+台词）',
    '場合分歧': 'script 分支（if 步骤，01 骨架）', '關閉消息': '05 消息控制', '主人公分歧': 'script 分支（主人公分派，01/05）',
    '主人公別': 'script 分支（玩家身份门控 when）', 'ＳＥ開始': '05 se 指令（异步）', '圖片表示': '05 视觉（立绘/过场）',
    '圖片消去': '05 视觉（立绘/过场）', '人物解雇': 'fire_hero 动作（16）', '軍團指令': '02 PartyBrain（lock_party/army_gather）',
    '武將死亡': 'kill_hero 动作（16）', '文字列設定': '05 文本变量', '人物登用': 'spawn_hero 动作（16）',
    '離開設施': 'scene_exit 动作（05 场景退出）', '勢力滅亡': 'destroy_faction 动作（16）', '選擇': '05 choice 节点',
    '城主解任': 'set_owner 动作（16）', '軍團編成最強': '02 PartyBrain（army_gather）', '居城變更': '🔴 06 本城变更（Hero.home）',
    '進入設施': 'scene_enter 动作（05/16）', '容器排序': 'container_sort', '場合別': 'script 分支（when 门控）',
    '停止時間': 'pause_time 动作（01 调度）', '改名': 'rename 动作（16）', '背景變更': '05 场景切换', '遊戲中斷': '🔴 剧本结局（06/14）',
    '背景恢復': '05 场景切换（还原）',
    '強制移動': 'teleport 动作（16）', '外出': '🔴 06 身份/移动（降级）', '腳本': '🔴 流程控制（事件内调用）',
    '家督讓位': 'change_clan_leader 动作（16）', '個人戰鬥': '03 battle（duel 预设）', '獨立': 'independence 动作（16）',
    '城主任命': 'set_owner 动作（16）', '所持金變更': 'gold_change 动作（16）', '軍團編成': '02 PartyBrain（army_gather）',
    '主命作成': 'create_order 动作（13）', '解除主命': '🔴 13 主命解除（QuestDef）', '據點改名': 'rename 动作（16）',
    '立場變更': '🔴 06 身份变更', '迷你遊戲': '🔴 降级（骑砍2 无对应小游戏）', '下個場面': '05 场景切换',
    '對話選擇': '05 choice 节点', '國主任命': '🔴 区域任命（降级）', '成為御用商人': '🔴 商家（数据包）',
    '自語選擇': '05 choice 节点', '事件主命作成': '13 事件主命（QuestDef）', '忍者軍團編成最強': '02 PartyBrain',
    '海賊軍團編成最強': '🔴 海战扩展', '強制武器交換': '🔴 降级', '畫面效果': '05 fx 指令', '容器存取': 'container_access',
    '事件主命變更': '13 事件主命（QuestDef）', '旁白可否選擇': '05 choice 门控（narrator）', '對話可否選擇': '05 choice 门控',
    '國主解任': '🔴 区域任命（降级）', '模塊開始': '🔴 流程控制（首版线性展开）', '會議設定': '🔴 17 评定（council_start）',
    '物品改名': 'rename 动作（16）', '海賊軍團編成': '🔴 海战扩展', '忍者軍團編成': '02 PartyBrain',
    '暫存所有變量': '🔴 Variable 暂存（降级）', '暫存人物屬性': '🔴 属性暂存（降级）', '他歧': '🔴 解析碎片（忽略）',
    '數字輸入': '🔴 降级（无对应）', '會議開始': '🔴 17 评定（council_start）', '選擇項設定': '05 choice 选项',
    '容器檢索': 'container_query', 'ＳＥ停止': '05 se 指令', 'ＳＥ循環': '05 se 指令（循环）',
}
CMD_MAP.update(CMD_EXACT)

# 🔴 语法类别（2026-08-27 用户裁定：条件/流程/事件结构 词不是「命令」——
# 不产生独立执行步骤，只参与条件/分支/循环/文件结构；命令区纯化为真·命令）
# 「更新」= 状态写入（事件完成/旗标成立/士气值），翻译器 = 机制行 note 承接（无执行步骤），归语法（2026-08-27 用户裁定）
SYNTAX_CMDS = {
    # 条件子句/组合器/分支
    '調查', 'ＡＮＤ調查', 'ＯＲ調查', '場合分歧', '場合別', '主人公分歧', '主人公別',
    '對話可否選擇', '旁白可否選擇',
    # 流程控制
    '循環', '脫出模塊', '模塊開始', '腳本', '遊戲中斷',
    # 事件文件结构（头字段）
    '事件', '屬性', '發生契機', '發生條件', '執行',
    # 状态写入（机制行，翻译器 note 承接）
    '更新',
}

def cmd_rule(name):
    if name.startswith('代入'):
        return 'Ctx / Variable / GlobalSlot 三档'
    if name.startswith('容器'):
        return 'container_*（pick 组）'
    if name.startswith(('ＳＥ', 'SE')):
        return '05 se 指令'
    if name.startswith('圖片'):
        return '05 视觉'
    if name.startswith(('軍團', '海賊軍團', '忍者軍團')):
        return '02 PartyBrain'
    if re.search(r'未知[0-9\uff10-\uff19]+$', name) or name.startswith('未知'):
        return '🔴 解析碎片（忽略）'            # 未知NN:(2B 00 00 00) 原始字节命令
    return '🔴 低频 → 降级/忽略'

# ═══ 生成三列 markdown ═══
def gen(title, counter, unit, m, rule):
    rows = []
    for k, v in counter.most_common():
        label = m.get(k) or rule(k)
        rows.append('| %s | %d | %s |' % (k, v, label))
    head = '太阁5 域' if '域' in title else ('太阁5 域.属性' if '属性' in title else '太阁5 命令')
    return ('## %s（%d %s，按次数降序）\n\n'
            '| %s | 次数 | 骑砍2 落点（🔴 = 需新加/数据包，❌ = 放弃） |\n'
            '|---|---|---|\n%s\n' % (title, len(counter), unit, head, '\n'.join(rows)))


def main():
    """v2：16a CSV 是词表单一事实源（2026-08-26 重构），本脚本不再生成 16.md 词表节；
    职责 = 语料提取 + 全量覆盖自检（表外词条 → exit(1)，禁止带病产出）。"""
    errors = verify_coverage()
    if errors:
        print('❌ 生成中止：全语料覆盖自检失败（表外词条 = 生成器缺陷；回填映射表后重跑）')
        for e in errors[:80]:
            print('  ', e)
        print(f'  …共 {len(errors)} 条表外')
        sys.exit(1)
    print('✅ 全语料覆盖自检通过')
    print('  属性(域,属性) %d 对 / 域值(域::值) %d 对 / 带参调用 %d 种 / 命令 %d 种'
          % (len(attr_pairs), len(domain_vals), len(calls), len(cmds)))
    print('  命令头值 %d 种 / 参数位 %d 个（词条 %d 种）/ 台词插值 %d 种'
          % (len(head_vals), len(arg_positions),
             sum(len(v) for v in arg_positions.values()), len(talk_interps)))


if __name__ == "__main__":
    main()
