# -*- coding: utf-8 -*-
"""生成 16 第一部分对照表（太阁5 词条 | 次数 | 骑砍2 落点）并替换 1.1-1.3 节。

🔴 v2（2026-08-27 结构性修复）：属性表从「属性名 → 单一侧名」改为「(域, 属性) → 侧名」二维——
   旧版正则 `::X.属性` 丢弃域前缀，导致跨域同名属性只登记一条且侧名域错配
   （大名家.本城 2298 次被登记成 人物域 Hero.home → 下游全部 🔴待注册）。
   同时新增「域::值」形态提取（身份枚举/狀況值/命名槽）与带参调用提取（谓词候选），
   并在生成期跑全语料覆盖自检：**表外词条 = 生成失败，禁止带病产出**（下游不再可能出现待注册）。
"""
import hashlib
import re
import sys
from collections import Counter

txt = open('Knowledge/太阁事件包/TK5AllEvents_merged.txt', encoding='utf-8').read()

# ═══ 提取（v2：保留域维度）═══
domains = Counter(re.findall(r'([一-鿿A-Za-zＡ-Ｚａ-ｚ]{1,6})::', txt))
# (域, 属性) 对：`域::主体.属性`（属性名保留原域，跨域同名属性各行一条；含全角数字：武將２）
attr_pairs = Counter(re.findall(r'([一-鿿A-Za-zＡ-Ｚａ-ｚ]{1,6})::[^.（()]+\.([一-鿿A-Za-zＡ-Ｚａ-ｚ0-9０-９]+)', txt))
# 域::值（无主体无点）：身份枚举 / 狀況值 / 命名槽（據點::主人公當主據點）——旧版零提取
domain_vals = Counter(re.findall(r'([一-鿿A-Za-zＡ-Ｚａ-ｚ]{1,6})::([一-鿿A-Za-zＡ-Ｚａ-ｚ0-9０-９]{1,14})(?=[),，）])', txt))
# 带参调用：`域::主体.属性(参数)` → 谓词候选（外交同盟/全城壓制…）
calls = Counter(re.findall(r'([一-鿿A-Za-zＡ-Ｚａ-ｚ]{1,6})::[^.（()]+\.([一-鿿A-Za-zＡ-Ｚａ-ｚ]+)\(', txt))
cmds = Counter()
for line in txt.splitlines():
    m = re.match(r'^\s*([一-鿿Ａ-Ｚａ-ｚA-Za-z]{2,8}):', line)
    if m:
        cmds[m.group(1)] += 1

# ═══ 域 42 → 骑砍2 落点 ═══
DOMAIN_MAP = {
    '人物': 'Hero::（引擎域）', '大名家': 'Clan::/Faction::（引擎域）', '城': 'Settlement::（引擎域）',
    '事件': 'Event::done（引擎域，调度器记录）', '勢力': 'Faction::（引擎域）', '據點': 'Settlement::（引擎域）',
    '軍團': 'Army::（02 PartyBrain 受控集合）', '身份': 'Identity:: 枚举值（17）', '變量': 'Variable::（引擎域）',
    '狀況': 'Time:: + Flag::（引擎域）', '真偽': '布尔字面量',
    '事件標誌': 'Flag::（引擎域）', '國': 'Settlement.region（Region）', '日數計數器': 'Time::day',
    '海賊衆': 'Org::海賊衆（数据包，随海战）', '卡': 'Card::（数据包技能卡）', '物品': 'Item::（数据包映射表）',
    '忍者衆': 'Org::忍者衆（数据包，07 核对）', '砦': 'Settlement::（type=砦）', '地方': 'Settlement.region',
    '交易品': 'Item::（数据包交易品）', '儲存號': 'Ctx/Variable（存档槽变量）', '官職': 'Hero.title（17 官职）',
    '流派': '❌ 放弃（2026-08-24 用户裁定）', '主命': 'QuestDef（13 主命框架）', '町': 'Settlement::（type=町）',
    '官位': 'Hero.title（17）', '商家': 'Org::商家（数据包，按需）', '里': 'Settlement::（type=里）',
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
    '朝廷貢献度': 'Clan.court_favor', '出奔計數器': 'Clan.deserter_count', '與主人公關係': 'relation',
    '本據': 'Faction.home / Org.home', '支持大名家': 'Org.supporter',
    '鐵甲船建造技術': 'Org.tech_vessel', '大型船建造技術': 'Org.tech_large_vessel',
    '商業圈数': 'Org.merchant_net', '本店': 'Org.hq',
    # Settlement 域（城/據點/砦/町/里）
    '戰鬥標誌': 'Settlement.siege', '所在地方': 'Settlement.region',
    '道場': 'Settlement.dojo', '道場主人': 'Settlement.dojo_owner',
    '商人司': 'Settlement.merchant_office', '曾經訪問': 'Settlement.visited',
    '鐵甲船数': 'Settlement.vessels_ironclad', '所有船舶数': 'Settlement.vessels',
    '大型船舶数': 'Settlement.vessels_large', '主人': 'Settlement.owner',
    '攻': 'Settlement.attack', '地形': 'Settlement.terrain',
    '所屬忍者衆': 'Hero.ninja_group / Settlement.ninja_group', '所屬海賊衆': 'Hero.pirate_group / Settlement.pirate_group',
    # 軍團域（02 PartyBrain 受控集合）
    '軍團長': 'Army.leader', '武將': 'Army.general', '結果': 'Army.result',
    '士氣': 'Settlement.morale / Army.morale', '使用狀況': 'Army.state', '士兵數': 'Army.troops',
    '援軍對象軍團番號': 'Army.reinforce_id', '所屬勢力': 'Hero.faction / Army.faction',
    '軍馬': 'Army.materials / Settlement.materials', '鐵砲': 'Settlement.materials / Army.materials', '軍團方針': 'Army.intent',
    # 人物域
    '主命狀態': 'Hero.quest_state', '承擔主命': 'Hero.quest_assigned',
    '主命目標': 'Hero.quest_goal', '主命期限': 'Hero.quest_deadline',
    '事件参加可能': 'Hero.available', '認識標誌': 'hasMet', '親密度': 'relation',
    '仕官傾向': 'Hero.tendency', '義理': 'Hero.loyalty', '忠誠度': 'Hero.loyalty',
    '身份': 'Hero.identity',
    '統率力': 'Hero.leadership', '武力': 'Hero.might', '智謀': 'Hero.intellect',
    '政務': 'Hero.governance', '魅力': 'Hero.charm', '野心': 'Hero.ambition',
    '素統率力': 'Hero.leadership_base', '素武力': 'Hero.might_base',
    '素智謀': 'Hero.intellect_base', '素政務': 'Hero.governance_base', '素魅力': 'Hero.charm_base',
    '所持金': 'Hero.gold', '貯金': 'Hero.gold',
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
    '所有者': 'Item.owner', '所有個数': 'Item.count', '價值': 'Item.price',
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

# ═══ 谓词候选：带参调用 → 谓词（v2：全城壓制 等从语料提取，不再硬编码待注册）═══
CALL_MAP = {
    '外交同盟': ('isAllied', ('a', 'b')),        # a,b = 势力
    '外交感情': ('relation', ('a', 'b')),          # a,b = 势力
    '鄰接大名家': ('isNeighbor', ('a', 'b')),
    '全城壓制': ('allControlled', ('region', 'clan')),
    '卡持有': ('hasCard', ('hero', 'card')),       # 语料: (人物::X.卡持有(卡::Y)) = 是否持有技能卡
    '移動可能': ('canMove', ('settlement', 'hero')),    # 语料: (城::X.移動可能(人物::Y)) = Y 能否前往 X
    '攻擊可能': ('canAttack', ('settlement', 'hero')),
}
# 无参但语义为关系谓词的属性侧名（认识標誌/親密度 → 与主人公的关系）
PRED_SIDE_NOARG = {'hasMet', 'relation', 'hasRelation'}

# ═══ 域::值 注册表（v2 新增；侧名 = 英文枚举 token / 完整 DSL 引用）═══
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
    ('身份', '茶人'): 'tea_master', ('身份', '姑娘'): 'girl', ('身份', '頭'): 'chief',
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
    ('軍團', '軍團１'): 'Army::army_1', ('軍團', '軍團２'): 'Army::army_2',
    ('軍團', '主人公軍團'): 'Army::main_army', ('軍團', '事件用１軍團'): 'Army::event_army_1',
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
    ('場面', '自宅'): 'Facility::home', ('場面', '發生設施'): 'Facility::event_facility', ('場面', '海賊宅'): 'Facility::pirate_den',
    ('場面', '評定間'): 'Facility::council', ('場面', '城主間'): 'Facility::lord_room',
    # 狀況域值（无主体属性形态）
    ('狀況', '戰爭禁止日數'): 'Variable::war_ban_days', ('狀況', '空閒大名家數'): 'Variable::idle_clans', ('狀況', '場面'): 'Variable::scene',
}

# 🔴 v2：实体引用域——域::值 = 具名实体（人名/城名/组织名…），由翻译器名字表查 StringId +
# 确定性 fallback 兜底，**不进 CSV 域值区**（人物::伊藤總十郎 这类行 = 污染，2026-08-27 用户裁定）
ENTITY_DOMAINS = {
    '人物': 'Hero', '大名家': 'Clan', '城': 'Settlement', '據點': 'Settlement', '勢力': 'Faction::Kingdom',
    '國': 'Region', '砦': 'Settlement', '町': 'Settlement', '里': 'Settlement',
    '忍者衆': 'Org', '商家': 'Org', '海賊衆': 'Org', '卡': 'Card', '流派': 'Card',
    '事件': 'Event', '物品': 'Item', '交易品': 'Item', '地方': 'Region',
    '官位': 'court_rank', '官職': 'title', '工作': 'QuestDef', '事件主命': 'QuestDef',
}

# ═══ 域::值 规则兜底（词条域专用；实体域由 ENTITY_DOMAINS 处理，不进 CSV）═══
def domain_val_rule(dom, val):
    if dom == '事件標誌':
        return f'Flag::{fallback_id(val)}'            # flag 名是运行数据，确定性 hash + report 登记中文名
    if dom == '狀況':
        return f'Variable::{fallback_id(val)}'         # 全局状态值（除专表外）
    if dom == '日數計數器':
        return 'Time::day'                             # 天数计数比较（日數計數器::X → 与天数计数比）
    if dom == '變量':
        return f'Variable::{ascii_translit(val) or fallback_id(val)}'   # 全角→半角转写优先
    if dom == '儲存號':
        return f'Variable::{fallback_id(val)}'         # 存档槽变量
    if dom == '場面':
        return f'Facility::{fallback_id(val)}'         # 05 演出设施（专表外）
    if dom == '軍團':
        return f'Army::{fallback_id(val)}'             # 命名军团实例（专表外）
    if dom == '人物類別':
        return f'category_{fallback_id(val)}'          # 容器类别枚举（专表外）
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
    '外交感情': 'relation', '所屬據點': 'Hero.settlement', '性別': 'Hero.gender', '親密度': 'hasRelation',
    '死亡標誌': 'Hero.alive', '認識標誌': 'hasMet', '所屬國': 'Settlement.region', '所屬勢力類型': 'Hero.faction',
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
    '体力': 'Hero.health', '主命目標': 'Hero.quest_goal', '官位': 'Hero.title', '鄰接大名家': 'isNeighbor',
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


_PRED_SIDES = ('exists', 'isAllied', 'isNeighbor', 'allControlled', 'hasMet', 'hasRelation', 'relation', 'unknown')


def pair_side(dom, attr):
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
        if s in _PRED_SIDES:
            return s        # 谓词/碎片侧名与域无关
    # 规则兜底
    if attr.endswith('技能'):
        tok = SKILL_TOKENS.get(attr[:-2]) or fallback_id(attr)
        return f'Hero.skill_{tok}'                          # 技能数值 0-100，数据包实现
    if attr.isdigit():
        # TK5 编号属性（交易品::玻璃瓶.3 / 人物::X.205 = 对象编号属性槽）；语义待 TK5 属性表核对
        return f'{prefix or "obj"}.attr_{attr}'
    m = re.search(r'[0-9０-９]+', attr)
    if m:
        base = attr[:m.start()] + attr[m.end():]            # 编号槽属性：武將２→武將、道場２主人→道場主人
        if base and base != attr:
            s = pair_side(dom, base)
            if s:
                return s
    if attr.startswith('未知'):
        return 'unknown'                                    # 未知NN 解析碎片
    if dom == '人物' and attr.endswith('標誌') and not cands:
        return 'Hero.state'
    return None


def val_side(dom, val):
    """(域, 值) → 侧名/引用。专表 > 实体域规则 > 数据型规则。"""
    if (dom, val) in DOMAIN_VAL_MAP:
        return DOMAIN_VAL_MAP[(dom, val)]
    if val == '無效':
        return 'null'
    if dom in ENTITY_DOMAINS:
        prefix = ENTITY_DOMAINS[dom]
        return f'{prefix}::{fallback_id(val)}'          # 具名实体：翻译器名字表先行，fallback 兜底
    return domain_val_rule(dom, val)


def call_side(dom, attr):
    """带参调用 → 谓词。表外 = None（生成期报错）。"""
    return CALL_MAP.get(attr)


# ═══ 生成期自检：全语料覆盖断言（表外 = 生成失败）═══
def verify_coverage():
    errors = []
    for (d, a), c in attr_pairs.items():
        if pair_side(d, a) is None:
            errors.append(f'属性表外: {d}::{a} ×{c}')
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
    if name.startswith('未知'):
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
    print('  属性(域,属性) %d 对 / 域值(域::值) %d 对 / 带参调用 %d 种 / 命令 %d 种' % (len(attr_pairs), len(domain_vals), len(calls), len(cmds)))


if __name__ == "__main__":
    main()
