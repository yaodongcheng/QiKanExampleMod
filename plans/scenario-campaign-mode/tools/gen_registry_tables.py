# -*- coding: utf-8 -*-
"""生成 16 第一部分三列对照表（太阁5 词条 | 次数 | 骑砍2 落点）并替换 1.1-1.3 节"""
import re
from collections import Counter

txt = open('Knowledge/太阁事件包/TK5AllEvents_merged.txt', encoding='utf-8').read()
domains = Counter(re.findall(r'([一-鿿A-Za-zＡ-Ｚａ-ｚ]{1,6})::', txt))
attrs = Counter(re.findall(r'::[^.（()]+\.([一-鿿A-Za-zＡ-Ｚａ-ｚ]+)', txt))
cmds = Counter()
for line in txt.splitlines():
    m = re.match(r'^\s*([一-鿿Ａ-Ｚａ-ｚA-Za-z]{2,8}):', line)
    if m:
        cmds[m.group(1)] += 1

# ═══ 域 42 → 骑砍2 落点 ═══
DOMAIN_MAP = {
    '人物': 'Hero::（引擎域）', '大名家': 'Clan::/Faction::（引擎域）', '城': 'Settlement::（引擎域）',
    '事件': 'Event::done（引擎域，调度器记录）', '勢力': 'Faction::（引擎域）', '據點': 'Settlement::（引擎域）',
    '軍團': '🔴 新加 02 PartyBrain 受控集合（原生 MobilePartyAi）', '身份': '枚举字面量（Hero.identity）',
    '變量': 'Variable::（引擎域）', '狀況': 'Time:: + Flag::（引擎域）', '真偽': '布尔字面量',
    '事件標誌': 'Flag::（引擎域）', '國': 'Settlement.region（Region）', '日數計數器': 'Time::day',
    '海賊衆': '🔴 组织域 Org::海賊衆（数据包，随海战）', '卡': 'Card::（数据包技能卡）', '物品': 'Item::（数据包映射表）',
    '忍者衆': '🔴 组织域 Org::忍者衆（数据包，07 核对）', '砦': 'Settlement::（type=砦）', '地方': 'Settlement.region',
    '交易品': 'Item::（数据包交易品）', '儲存號': '🔴 Ctx/Variable（存档槽变量）', '官職': 'Hero.title（17 官职）',
    '流派': '❌ 放弃（2026-08-24 用户裁定）', '主命': 'QuestDef（13 主命框架）', '町': 'Settlement::（type=町）',
    '官位': 'Hero.title（17）', '商家': '🔴 组织域 Org::商家（数据包，按需）', '里': 'Settlement::（type=里）',
    '天氣': '03 预设 weather（数据包）', '場面': '05 演出形态（scene/menu_dialogue）', '軍團方針': '🔴 02 PartyIntent（新加）',
    '工作': '13 主命/工作（QuestDef）', '人物類別': '枚举字面量', '事件主命': '13 事件主命（QuestDef 关联事件）',
    '戰鬥結束種類': '03 战果枚举（BattleResult）', '物品類型': 'Item 类型（数据包）', '主命屬性': '13 主命属性（数据包）',
    '遊戲通關種類': '🔴 剧本结局枚举（14/06）', '事件發生狀態': 'Event::state（调度器内部）',
    '環境變量': '🔴 05 演出环境（数据包）', '背景音樂': '05 bgm 指令（数据包）',
}

# ═══ 属性 199 → 落点（精确表 + 规则兜底）═══
ATTR_MAP = {}
ATTR_HI = {
    '存在': 'exists(域::X) 谓词', '所屬大名家': 'Hero.clan / Settlement.clan', '事件参加可能': 'Hero.available',
    '城主': 'Settlement.owner', '本城': 'Hero.home', '外交同盟': 'isAllied 谓词', '身份': 'Hero.identity 枚举',
    '外交感情': 'relation 谓词', '所屬據點': 'Hero.settlement', '性別': 'Hero.gender', '親密度': 'hasRelation 谓词',
    '死亡標誌': 'Hero.alive', '認識標誌': 'hasMet 谓词', '所屬國': 'Settlement.region', '所屬勢力類型': 'Hero.faction',
    '出現標誌': 'Hero.state（登场三态）', '所屬上司': 'Hero.superior', '所持標誌': '🔴 Item 持有查询（数据包）',
    '武將': '角色引用（武将类型）', '全城壓制': 'Settlement.suppressed', '使用狀況': '🔴 设施状态（降级）',
    '戰略': '🔴 14 drift 大名家战略', '妻': 'Hero.spouse', '主命狀態': 'QuestDef 状态（13）', '軍團長': 'Hero.party（军团长）',
    '兵士数': 'Settlement.garrison / Party 兵数', '離家標誌': 'Hero.state（离家）', '士氣': 'Settlement.morale',
    '年齡': 'Hero.age', '仕官傾向': 'Hero.tendency', '軍資金': '🔴 Clan 财政（数据包）', '外出禁止標誌': 'Hero.state',
    '悪名': 'Hero.infamy', '當主': 'Hero.leader', '所有者': '🔴 物品所有者（降级）', '戰略目標': '🔴 14 drift 战略目标',
    '兵糧': 'Settlement.food', '結果': '03 战果 flag（BattleResult）', '未知': '🔴 未知 token（解析碎片）',
    '武士功勳': 'Hero.merit（17）', '所在地方': 'Settlement.region', '名聲': 'Hero.reputation', '據點類型': 'Settlement.type',
    '交易品數量': 'Item 交易品（数据包）', '防御度': 'Settlement.defense', '鐵砲': 'Settlement.materials（织丰铁炮）',
    '出撃標誌': 'Hero.state（出战）', '支配力': '🔴 大名支配力（17 后续）', '立場': '🔴 立场枚举（降级）',
    '承擔主命': 'QuestDef 承接（13）', '本據': 'Hero.home', '所有個数': 'Item 持有数（数据包）', '官職': 'Hero.title（17）',
    '劍術師匠': '❌ 流派放弃', '所屬海賊衆': 'Org::海賊衆（数据包）', '所屬當主': 'Clan.leader', '戰鬥標誌': 'Hero.state（战斗）',
    '死刑標誌': 'Hero.state', '大方針': '🔴 14 drift 大名家方针', '停止進攻': 'not(atWar)（停战状态）',
}
ATTR_MID = {
    '所屬忍者衆': 'Org::忍者衆（数据包）', '訓練度': 'Settlement.training', '住民安定度': 'Settlement.security',
    '卡持有': 'Card.持有', '体力': 'Hero.health', '主命目標': 'QuestDef 目标（13）', '官位': 'Hero.title（17）',
    '鄰接大名家': 'isNeighbor 谓词', '現石高': 'Settlement.kokudaka', '城数': '🔴 Clan 领地聚合（数据包）',
    '妻性格': '🔴 数据包（降级）', '劍術流派': '❌ 流派放弃', '規模': '🔴 降级', '鐵甲船数': '🔴 海战扩展',
    '所屬勢力': 'Hero.faction', '移動可能': 'Settlement.movable', '生病標誌': 'Hero.state', '忠誠度': 'Hero.loyalty',
    '忍者功勳': 'Hero.merit（17）', '據點種類': 'Settlement.type', '關係經緯': '🔴 关系网（降级）', '軍馬': 'Settlement.materials',
    '現礦山': 'Settlement.mine', '商人功勳': 'Hero.merit（17）', '道場主人': '❌ 流派放弃', '與主人公關係': 'hasRelation',
    '基準石高': 'Settlement.kokudaka', '所持金': 'Hero.gold', '工作狀態': '13 工作（QuestDef）', '海賊功勳': 'Hero.merit（17）',
    '暴動標誌': 'Settlement.rebellion', '醫師評價': '🔴 数据包（降级）', '失蹤標誌': 'Hero.state', '原屬下標誌': '🔴 标志（数据包）',
    '繼承人標誌': '🔴 标志（数据包）', '貯金': 'Hero.gold', '兵法指南役大名家': '🔴 降级', '自宅鄰接工作場': '🔴 降级',
    '礦山最高值': 'Settlement.mine', '個人戰勝利数': '🔴 战绩统计（Card/数据包）', '鑑定標誌': '🔴 标志（数据包）',
    '印可狀標誌': '❌ 流派放弃', '攻擊可能': 'Settlement.attackable', '對手武將': '🔴 降级', '所有船舶数': '🔴 海战扩展',
    '印可': '❌ 流派放弃', '大方針目標': '🔴 14 drift', '大型船舶数': '🔴 海战扩展', '所屬商家': 'Org::商家（数据包）',
    '父母': '🔴 家系（06 人物池数据）', '支持大名家': '🔴 降级', '流派評價': '❌ 流派放弃', '本店': '🔴 商家（数据包）',
    '義理': '🔴 数据包（降级）', '士兵數': 'Party 兵数', '類別': '🔴 枚举（降级）', '價格': 'Item 价格', '茶席次數': '🔴 茶道 Card',
    '装備武器': 'Agent.Equipment（骑砍2 原生）', '價值': 'Item 价格', '鐵甲船建造技術': '🔴 海战扩展', '茶具經驗': '🔴 茶道 Card',
    '道場': '❌ 流派放弃', '宗家': '❌ 流派放弃', '出自': '🔴 家系（06 人物池）', '武力': 'Hero 六维（数据包扩展）',
    '合戰禁止標誌': 'not(atWar)（停战）', '飲酒': '🔴 数据包（降级）', '大筒': 'Settlement.materials', '製砲經驗': '🔴 降级',
    '製鐵經驗': '🔴 降级', '朝廷貢献度': '🔴 数据包（降级）', '國屬性': 'Settlement.region', '野心': 'Hero 六维（数据包）',
    '智謀': 'Hero 六维（数据包）', '喜好': '🔴 数据包（降级）', '装備防具': 'Agent.Equipment', '魅力': 'Hero 六维（数据包）',
    '素武力': 'Hero 六维基础值（数据包）', '素魅力': 'Hero 六维基础值（数据包）', '素智謀': 'Hero 六维基础值（数据包）',
    '素政務': 'Hero 六维基础值（数据包）', '製藥天數': '🔴 降级', '曾經訪問': '🔴 降级', '劍勝利回数': '🔴 战绩统计',
    '出奔計數器': 'Variable::（计数）', '性情': '🔴 数据包（降级）', '物品類型': 'Item 类型', '補正值': '🔴 降级',
    '最大載重量': '🔴 数据包（降级）', '勢力類型': 'Faction 类型', '道場打破可能標誌': '❌ 流派放弃',
    '大型船建造技術': '🔴 海战扩展', '格': '🔴 降级', '素統率力': 'Hero 六维基础值', '統率力': 'Hero 六维（数据包）',
    '主人公道場規模': '❌ 流派放弃', '義診天數': '🔴 降级', '武具種類': '🔴 降级', '精神': '🔴 数据包（降级）',
    '天覧試合標誌': '🔴 降级', '主人': '🔴 降级', '地形': '🔴 降级', '無敵標誌': '🔴 降级', '軍團方針': '02 PartyIntent',
    'evm': '🔴 解析碎片（忽略）', '商業圈数': '🔴 商家（数据包）', '個人戰敗北数': '🔴 战绩统计', '商人司': '🔴 商家（数据包）',
    '武具經驗': '🔴 降级', '知喜好標誌': '🔴 降级', '個人戰現在連勝数': '🔴 战绩统计', '賊遭遇計數器': 'Variable::（计数）',
    '攻': '🔴 降级', '壽命': '🔴 数据包（降级）', '工作目標': '13 工作（QuestDef）', '物欲': '🔴 数据包（降级）',
    '主命期限': 'QuestDef 期限（13）', '承擔工作': '13 工作承接（QuestDef）',
    '援軍對象軍團番號': '🔴 军团番号（02 PartyBrain）', '容貌': '🔴 数据包（降级）',
    '政務': 'Hero 六维（数据包扩展）', '開墾': '🔴 数据包（降级）',
}
ATTR_MAP.update(ATTR_HI)
ATTR_MAP.update(ATTR_MID)

def attr_rule(name):
    if name.endswith('技能') and name not in ATTR_MAP:
        return '🔴 技能 → Card 技能卡'
    if name.endswith('標誌') and name not in ATTR_MAP:
        return '🔴 标志 → Hero.state/数据包'
    if name.endswith(('数', '數', '回数', '人数')) and name not in ATTR_MAP:
        return '🔴 数值 → 数据包'
    return '🔴 低频 → 数据包/降级'

# ═══ 命令 174 → 落点（精确表 + 规则兜底）═══
CMD_MAP = {}
CMD_EXACT = {
    '對話': '05 lines[] speaker/textKey', '調查': 'condition 表达式（when→condition）', '分歧': 'script 分支（choice/goto）',
    '更新': '动作/ctx_set（16 动作表）', '旁白': '05 narrator 行', '容器篩選': '🔴 pick 谓词（后续扩展，首版静态引用）',
    '容器選擇': '🔴 pick 谓词（后续扩展）', '自語': '05 narrator/自语行', '容器設定': '🔴 pick 谓词（后续扩展）',
    'ＢＧＭ變更': '05 bgm 指令（异步）', '容器排除': '🔴 pick 谓词（后续扩展）', '事件': '事件 JSON id（头字段）',
    '屬性': '事件 JSON trigger/once/priority（头字段，2026-08-26 数据化）', '發生契機': '事件 JSON trigger 字段（头字段）',
    '發生條件': '事件 JSON condition 字段（头字段）', '執行': '事件 JSON script（头字段）', '容器清理': '🔴 pick 谓词（后续扩展）',
    '脫出模塊': '🔴 流程控制（事件内循环，首版线性展开）', '循環': '🔴 流程控制（首版线性展开）',
    'ＡＮＤ調查': 'condition and(…)', 'ＯＲ調查': 'condition or(…)', '變名對話': '05 变名节点（动作表现+台词）',
    '場合分歧': 'script 分支（choice/goto）', '關閉消息': '05 消息控制', '主人公分歧': 'script 分支（玩家选择）',
    '主人公別': 'script 分支（玩家身份门控 when）', 'ＳＥ開始': '05 se 指令（异步）', '圖片表示': '05 视觉（立绘/过场）',
    '圖片消去': '05 视觉（立绘/过场）', '人物解雇': 'fire_hero 动作（16）', '軍團指令': '02 PartyBrain（lock_party/army_gather）',
    '武將死亡': 'kill_hero 动作（16）', '文字列設定': '05 文本变量', '人物登用': 'spawn_hero 动作（16）',
    '離開設施': '🔴 场景退出（05）', '勢力滅亡': 'destroy_faction 动作（16）', '選擇': '05 choice 节点',
    '城主解任': 'set_owner 动作（16）', '軍團編成最強': '02 PartyBrain（army_gather）', '居城變更': '🔴 06 本城变更（Hero.home）',
    '進入設施': 'scene_enter 动作（05/16）', '容器排序': '🔴 pick 谓词（后续扩展）', '場合別': 'script 分支（when 门控）',
    '停止時間': 'pause_time 动作（01 调度）', '改名': 'rename 动作（16）', '背景變更': '05 场景切换', '遊戲中斷': '🔴 剧本结局（06/14）',
    '強制移動': 'teleport 动作（16）', '外出': '🔴 06 身份/移动（降级）', '腳本': '🔴 流程控制（事件内调用）',
    '家督讓位': 'change_clan_leader 动作（16）', '個人戰鬥': '03 battle（duel 预设）', '獨立': 'independence 动作（16）',
    '城主任命': 'set_owner 动作（16）', '所持金變更': 'gold_change 动作（16）', '軍團編成': '02 PartyBrain（army_gather）',
    '主命作成': 'create_order 动作（13）', '解除主命': '🔴 13 主命解除（QuestDef）', '據點改名': 'rename 动作（16）',
    '立場變更': '🔴 06 身份变更', '迷你遊戲': '🔴 降级（骑砍2 无对应小游戏）', '下個場面': '05 场景切换',
    '對話選擇': '05 choice 节点', '國主任命': '🔴 区域任命（降级）', '成為御用商人': '🔴 商家（数据包）',
    '自語選擇': '05 choice 节点', '事件主命作成': '13 事件主命（QuestDef）', '忍者軍團編成最強': '02 PartyBrain',
    '海賊軍團編成最強': '🔴 海战扩展', '強制武器交換': '🔴 降级', '畫面效果': '05 fx 指令', '容器存取': '🔴 pick 谓词（后续扩展）',
    '事件主命變更': '13 事件主命（QuestDef）', '旁白可否選擇': '05 choice 门控（narrator）', '對話可否選擇': '05 choice 门控',
    '國主解任': '🔴 区域任命（降级）', '模塊開始': '🔴 流程控制（首版线性展开）', '會議設定': '🔴 17 评定（council_start）',
    '物品改名': 'rename 动作（16）', '海賊軍團編成': '🔴 海战扩展', '忍者軍團編成': '02 PartyBrain',
    '暫存所有變量': '🔴 Variable 暂存（降级）', '暫存人物屬性': '🔴 属性暂存（降级）', '他歧': '🔴 解析碎片（忽略）',
    '數字輸入': '🔴 降级（无对应）', '會議開始': '🔴 17 评定（council_start）', '選擇項設定': '05 choice 选项',
    '容器檢索': '🔴 pick 谓词（后续扩展）', 'ＳＥ停止': '05 se 指令', 'ＳＥ循環': '05 se 指令（循环）',
}
CMD_MAP.update(CMD_EXACT)

def cmd_rule(name):
    if name.startswith('代入'):
        return 'Ctx / Variable / GlobalSlot 三档'
    if name.startswith('容器'):
        return '🔴 pick 谓词后续'
    if name.startswith(('ＳＥ', 'SE')):
        return '05 se 指令'
    if name.startswith('圖片'):
        return '05 视觉'
    if name.startswith(('軍團', '海賊軍團', '忍者軍團')):
        return '02 PartyBrain'
    return '🔴 低频 → 降级/忽略'

# ═══ 生成三列 markdown ═══
# 🔴 2026-08-26：不设节首规则说明——短标记自含语义，完整细节在权威处（第三节 Ctx 三档 / 二·域表 Card / 五·动作表 / 02 / 05），中间说明层 = 重复，删除

def gen(title, counter, unit, m, rule):
    rows = []
    for k, v in counter.most_common():
        label = m.get(k) or rule(k)
        rows.append('| %s | %d | %s |' % (k, v, label))
    head = '太阁5 域' if '域' in title else ('太阁5 属性' if '属性' in title else '太阁5 命令')
    return ('## %s（%d %s，按次数降序）\n\n'
            '| %s | 次数 | 骑砍2 落点（🔴 = 需新加/数据包，❌ = 放弃） |\n'
            '|---|---|---|\n%s\n' % (title, len(counter), unit, head, '\n'.join(rows)))

def main():
    b1 = gen('1.1 域全量', domains, '个', DOMAIN_MAP, lambda n: '🔴 低频 → 数据包/降级')
    b2 = gen('1.2 属性全量', attrs, '个', ATTR_MAP, attr_rule)
    b3 = gen('1.3 命令全量', cmds, '种', CMD_MAP, cmd_rule)
    new_block = b1 + '\n' + b2 + '\n' + b3

    p = 'plans/scenario-campaign-mode/16-DSL注册表全表.md'
    t = open(p, encoding='utf-8').read()
    t2, n = re.subn(r'## 1\.1 域全量.*?## 1\.4 特殊形态', new_block + '\n\n## 1.4 特殊形态', t, flags=re.DOTALL)
    assert n == 1, '替换失败 n=%d' % n
    open(p, 'w', encoding='utf-8').write(t2)

    missing_d = [k for k, _ in domains.most_common() if k not in DOMAIN_MAP]
    missing_a = [k for k, _ in attrs.most_common() if k not in ATTR_MAP]
    missing_c = [k for k, _ in cmds.most_common() if k not in CMD_MAP]
    print('域未精确标：%d（走规则）%s' % (len(missing_d), missing_d))
    print('属性未精确标：%d（走规则：技能/标志/数值/低频）' % len(missing_a))
    print('命令未精确标：%d（走规则：代入/容器/SE/图片/军团）' % len(missing_c))
    print('替换完成')


if __name__ == "__main__":
    main()
