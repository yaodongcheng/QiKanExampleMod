# -*- coding: utf-8 -*-
"""生成 16a-DSL翻译总表.csv —— 太阁5 ↔ 骑砍2 唯一翻译大表（正式 plan 数据文件，单一事实源）

列：太阁原词, 频率, 类别, 我们侧名, 类型, 语义, 参数, 实现用法, 状态
- 我们侧名：干净的 DSL 映射名（Hero:: / Settlement.owner / kill_hero / exists），不带括号说明
- 语义：我们侧名的中文释义（高频从白名单/动作表提取；低频词条名自解释）
- 实现用法：实现归属（引擎域 / 06 / 02 PartyBrain / 05 / 数据包 / 降级）
运行：仓库根目录 `python plans/scenario-campaign-mode/tools/build_registry_csv.py`
"""
import csv
import re
from collections import Counter

from gen_registry_tables import DOMAIN_MAP, ATTR_MAP, CMD_MAP

txt = open('Knowledge/太阁事件包/TK5AllEvents_merged.txt', encoding='utf-8').read()
domains = Counter(re.findall(r'([一-鿿A-Za-zＡ-Ｚａ-ｚ]{1,6})::', txt))
attrs = Counter(re.findall(r'::[^.（()]+\.([一-鿿A-Za-zＡ-Ｚａ-ｚ]+)', txt))
cmds = Counter()
for line in txt.splitlines():
    m = re.match(r'^\s*([一-鿿Ａ-Ｚａ-ｚA-Za-z]{2,8}):', line)
    if m:
        cmds[m.group(1)] += 1

# ── 属性类型（16 属性白名单类型列）──
ATTR_TYPES = {
    'year': '数字', 'month': '数字', 'day': '数字',
    'owner': '角色引用', 'clan': '家族引用', 'faction': '势力引用', 'type': '枚举', 'region': '引用',
    'garrison': '数字', 'food': '数字', 'prosperity': '数字', 'security': '数字', 'position': '位置',
    'defense': '数字', 'morale': '数字', 'funds': '数字', 'training': '数字', 'rebellion': '布尔',
    'materials': '数字/物品引用', 'kokudaka': '数字', 'mine': '数字', 'vessels': '数字',
    'suppressed': '布尔', 'movable': '布尔', 'attackable': '布尔',
    'alive': '布尔', 'state': '枚举', 'leader': '布尔', 'gender': '枚举', 'identity': '枚举', 'age': '数字',
    'home': '据点引用', 'settlement': '据点引用', 'party': '部队引用', 'superior': '角色引用',
    'spouse': '角色引用', 'reputation': '数字', 'infamy': '数字', 'gold': '数字',
    'relation_to': '数字（带参）', 'available': '布尔', 'merit': '数字', 'loyalty': '数字',
    'health': '数字', 'title': '枚举', 'tendency': '枚举',
    'kingdom': '势力引用', 'done': '布尔', 'value': '数字/字符串/引用', '持有': '布尔', '等级': '数字',
}

# ── 动作参数 + 实现（16 动作表）──
ACTIONS = {
    'set_flag': ('flag', '本 Phase'), 'clear_flag': ('flag', '本 Phase'),
    'set_variable': ('variable, value', '本 Phase'), 'ctx_set': ('slot, value', 'Phase 1'),
    'card_gain': ('hero, card', '数据包扩展（存档持久）'), 'card_lose': ('hero, card', '数据包扩展（存档持久）'),
    'declare_war': ('a, b', '本 Phase'), 'make_peace': ('a, b', '本 Phase'),
    'set_owner': ('settlement, clan', '本 Phase'), 'kill_hero': ('actor', '06'),
    'spawn_hero': ('actor, clan', '06'), 'spawn_clan': ('clanId, leader, home', '06（需核实 CreateClan）'),
    'make_alliance': ('a, b', '02（需核实 StanceType.Allied）'), 'relation_change': ('a, b, value', '本 Phase（ChangeRelationAction）'),
    'fire_hero': ('actor', '06'), 'change_clan': ('actor, clan', '06'), 'change_clan_leader': ('actor, clan', '06'),
    'independence': ('clan', '06'), 'rename': ('actor, name', '06'), 'destroy_faction': ('faction', '02/06'),
    'lock_party': ('leader, target, behavior', '02 PartyBrain'), 'release_party': ('leader', '02 PartyBrain'),
    'army_gather': ('leader, target, behavior', '02 PartyBrain'), 'teleport': ('party, pos', '06'),
    'grant_troops': ('troopIds, counts', '06'), 'gold_change': ('hero, amount', 'AgentControlHelper'),
    'grant_merit': ('actor, value', '17（WorldActionExecutor Scenario 层）'), 'set_title': ('actor, titleId', '17'),
    'promote': ('actor', '17'), 'cutscene': ('sceneId, textKey', '05'), 'perform': ('compiledId', '05'),
    'scene_enter': ('sceneId', '05'), 'im_message': ('channel, actor, textKey', 'IM 管线'),
    'battle': ('presetId', '03'), 'duel': ('opponent, outcomeSlot', '03（个人战，CombatManager）'),
    'create_order': ('orderId', '13'), 'pause_time': ('无', '01 调度'),
    'global_set': ('slot, 引用', '🔴 新加（存档）'),
}

# ── 谓词（16 谓词表）──
PREDICATES = {
    'exists': ('引用', '对象存在', '✅ 已设计'), 'atWar': ('a, b（势力引用）', 'a 与 b 交战', '✅ 已设计'),
    'isAllied': ('a, b（势力引用）', 'a 与 b 同盟', '注册表加行'), 'isNeighbor': ('a, b（据点引用）', 'a 与 b 相邻', '注册表加行'),
    'hasRelation': ('hero, hero, op, 数字', '亲密度比较', '注册表加行'), 'relation': ('a, b, op, 数字', '势力间外交关系数值', '注册表加行'),
    'hasMet': ('a, b（角色引用）', '是否认识', '注册表加行'), 'sameSettlement': ('hero, hero', '同据点', '注册表加行'),
    'canPromote': ('hero', '功勋 ≥ 晋升链下一级阈值', '注册表加行（17）'),
}

# ── 域 42 中文语义 ──
DOMAIN_SEM = {
    '人物': '角色', '大名家': '家族/势力', '城': '据点（城）', '事件': '事件状态查询', '勢力': '势力', '據點': '据点',
    '軍團': '军团', '身份': '身份枚举', '變量': '剧本变量', '狀況': '全局时间状态', '真偽': '布尔', '事件標誌': '旗标',
    '國': '国/区域', '日數計數器': '天数计数', '海賊衆': '海贼组织', '卡': '技能卡', '物品': '物品', '忍者衆': '忍者组织',
    '砦': '据点（砦）', '地方': '地方/区域', '交易品': '交易品', '儲存號': '存档槽', '官職': '官职', '流派': '流派（放弃）',
    '主命': '主命/任务', '町': '据点（町）', '官位': '官位', '商家': '商家组织', '里': '据点（里）', '天氣': '天气',
    '場面': '场面/演出形态', '軍團方針': '军团方针', '工作': '工作', '人物類別': '人物类别', '事件主命': '事件主命',
    '戰鬥結束種類': '战斗结束种类', '物品類型': '物品类型', '主命屬性': '主命属性', '遊戲通關種類': '通关种类',
    '事件發生狀態': '事件状态', '環境變量': '环境变量', '背景音樂': '背景音乐',
}

# ── 属性高频中文语义（16 属性白名单语义列）──
ATTR_SEM = {
    'owner': '归属/城主', 'clan': '归属家族', 'faction': '归属势力', 'type': '类型', 'region': '所在国/地方',
    'garrison': '驻军', 'food': '兵粮', 'prosperity': '繁荣', 'security': '治安', 'position': '位置',
    'alive': '存活', 'state': '登场状态', 'leader': '是否家主', 'gender': '性别', 'identity': '身份', 'age': '年龄',
    'home': '本城', 'settlement': '所在据点', 'party': '所在部队', 'superior': '所属上司', 'spouse': '配偶',
    'reputation': '名声', 'infamy': '恶名', 'gold': '所持金', 'available': '事件参加可能',
    'merit': '功勋', 'loyalty': '忠诚', 'health': '体力', 'title': '官职', 'tendency': '仕官志向',
    'kingdom': '所属势力', 'done': '已触发完成', 'value': '值', '持有': '持有', '等级': '等级',
    'defense': '防御度', 'morale': '士气', 'funds': '军资金', 'training': '训练度', 'rebellion': '叛乱状态',
    'materials': '物资储备', 'kokudaka': '石高', 'mine': '矿山', 'vessels': '船舶', 'suppressed': '全城压制',
    'movable': '可移动', 'attackable': '可攻击',
}

# ── 命令常用中文语义（动作/演出/流程）──
CMD_SEM = {
    '05 lines[] speaker/textKey': '对白', 'condition 表达式（when→condition）': '条件判断', 'script 分支（choice/goto）': '分支选择',
    '动作/ctx_set（16 动作表）': '变量/槽赋值', '05 narrator 行': '旁白', '05 bgm 指令（异步）': 'BGM', '05 se 指令': '音效',
    '05 视觉': '视觉/立绘', '05 消息控制': '消息控制', '05 choice 节点': '玩家选择', '05 choice 门控': '选择门控',
    '05 choice 门控（narrator）': '旁白选择', '05 变名节点（动作表现+台词）': '变名对白', '05 文本变量': '文本变量',
    '05 场景切换': '场景切换', '05 fx 指令': '画面效果', '05 bgm 指令': '背景音乐', '05 se 指令（循环）': '音效循环',
    '05 lines[]': '对白', '05 narrator/自语行': '自语/旁白', '05 choice 选项': '选项设定',
    '05 场景退出': '离开设施', '05 视觉（立绘/过场）': '视觉/过场',
    '事件 JSON id（头字段）': '事件 ID', '事件 JSON trigger/once/priority（头字段，2026-08-26 数据化）': '事件头字段',
    '事件 JSON trigger 字段（头字段）': '触发时机', '事件 JSON condition 字段（头字段）': '发生条件', '事件 JSON script（头字段）': '执行脚本',
    'condition and(…)': '且条件', 'condition or(…)': '或条件', 'Ctx / Variable / GlobalSlot 三档': '代入槽赋值',
    '🔴 pick 谓词后续': '集合筛选（后续）', '02 PartyBrain': '军团指令', '02 PartyBrain（lock_party/army_gather）': '军团锁定/集结',
    'pause_time 动作（01 调度）': '停止时间', 'teleport 动作（16）': '强制移动', '🔴 流程控制（事件内循环，首版线性展开）': '流程控制',
    '🔴 流程控制（首版线性展开）': '流程控制', '🔴 流程控制（事件内调用）': '事件内调用', '🔴 剧本结局（06/14）': '剧本结局',
    '🔴 06 身份/移动（降级）': '外出/身份', '🔴 06 本城变更（Hero.home）': '居城变更', '🔴 06 身份变更': '立场变更',
    '🔴 17 评定（council_start）': '评定/会议', '🔴 区域任命（降级）': '国主任命', '🔴 商家（数据包）': '商家',
    '🔴 海战扩展': '海战（扩展）', '🔴 降级（骑砍2 无对应小游戏）': '小游戏（降级）', '🔴 Variable 暂存（降级）': '变量暂存',
    '🔴 属性暂存（降级）': '属性暂存', '🔴 解析碎片（忽略）': '解析碎片', '🔴 降级（无对应）': '数字输入（降级）',
    '13 事件主命（QuestDef）': '事件主命', '🔴 13 主命解除（QuestDef）': '解除主命', '🔴 低频 → 降级/忽略': '低频杂项（降级）',
    '05 演出环境（数据包）': '环境变量', '🔴 场景退出（05）': '离开设施',
}


# ── 动作中文语义（16 动作表"语义"列）──
ACT_SEM = {
    'set_flag': '剧本标志', 'clear_flag': '剧本标志', 'set_variable': '剧本变量', 'ctx_set': '代入槽赋值',
    'card_gain': '获得能力卡', 'card_lose': '失去能力卡', 'declare_war': '宣战', 'make_peace': '停战',
    'set_owner': '换城主', 'kill_hero': '杀角色', 'spawn_hero': '造角色', 'spawn_clan': '新建家族',
    'make_alliance': '结盟', 'relation_change': '关系变更', 'fire_hero': '解雇', 'change_clan': '阵营变更',
    'change_clan_leader': '家督让位', 'independence': '独立', 'rename': '改名', 'destroy_faction': '势力灭亡',
    'lock_party': '部队锁定', 'release_party': '释放部队', 'army_gather': '集结', 'teleport': '传送',
    'grant_troops': '给兵', 'gold_change': '金钱变更', 'grant_merit': '功勋增减', 'set_title': '设官职',
    'promote': '晋升', 'cutscene': '过场', 'perform': '预编译演出', 'scene_enter': '进入设施',
    'im_message': '私信', 'battle': '程序化战斗', 'duel': '个人战（1v1）', 'create_order': '主命作成', 'pause_time': '停止时间',
    'global_set': '全局槽赋值',
}

def status_of(label):
    if label.startswith('❌'):
        return '❌ 放弃'
    if label.startswith('🔴'):
        return '🔴 需新加/数据包'
    return '✅ 引擎/映射'


def side_name(label):
    m = re.search(r'[（(]([a-z_/]+)[）)]', label)   # （lock_party/army_gather）括号动作组（全角兼容）
    if m and '/' in m.group(1):
        return m.group(1)
    m = re.search(r'([A-Za-z]+::(?:[A-Za-z_]+)?(?:\.\w+)?)', label)
    if m:
        return m.group(1)
    m = re.search(r'([A-Za-z_]+)(?: 动作| 谓词| 指令)', label)
    if m:
        return m.group(1)
    if label.startswith('Ctx /'):
        return 'Ctx/Variable/GlobalSlot'
    return label.split('（')[0].strip()


def impl_of(label, side):
    """实现用法：引擎域 / 系统编号 / 数据包 / 降级"""
    if '引擎域' in label:
        return '引擎域'
    if label.startswith('❌'):
        return '❌ 放弃'
    if '数据包' in label:
        return '数据包'
    m = re.search(r'([0-9]{2}|IM 管线|AgentControlHelper)', label)
    if m:
        return m.group(1)
    return side


rows = []
# 域
for k, v in domains.most_common():
    label = DOMAIN_MAP.get(k, '🔴 低频 → 数据包/降级')
    side = side_name(label)
    rows.append([k, v, '域', side, '域', DOMAIN_SEM.get(k, k), '—', impl_of(label, side), status_of(label)])
# 属性
for k, v in attrs.most_common():
    label = ATTR_MAP.get(k)
    if label is None:
        label = ('🔴 技能 → Card 技能卡' if k.endswith('技能') else
                 '🔴 标志 → Hero.state/数据包' if k.endswith('標誌') else
                 '🔴 数值 → 数据包' if k.endswith(('数', '數', '回数', '人数')) else
                 '🔴 低频 → 数据包/降级')
    side = side_name(label)
    typ = ATTR_TYPES.get(side.split('.')[-1], ('布尔' if '標誌' in k or '可能' in k else
                                '数字' if re.search(r'[数數回]|金|粮|兵|石高|礦|船|馬|鐵砲', k) else '🔴 待定'))
    sem = ATTR_SEM.get(side, k)   # 语义：高频从白名单，低频词条名自解释
    impl = impl_of(label, side)
    if impl == side and status_of(label) == '✅ 引擎/映射':
        impl = '引擎属性查询器'
    rows.append([k, v, '属性', side, typ, sem, '—', impl, status_of(label)])
# 命令
for k, v in cmds.most_common():
    label = CMD_MAP.get(k)
    if label is None:
        label = ('Ctx / Variable / GlobalSlot 三档' if k.startswith('代入') else
                 '🔴 pick 谓词后续' if k.startswith('容器') else
                 '05 se 指令' if k.startswith(('ＳＥ', 'SE')) else
                 '05 视觉' if k.startswith('圖片') else
                 '02 PartyBrain' if k.startswith(('軍團', '海賊軍團', '忍者軍團')) else
                 '🔴 低频 → 降级/忽略')
    side = side_name(label)
    param, impl = '—', label
    for an, (ap, ai) in ACTIONS.items():
        if side == an or an in label:
            param, impl = ap, ai
            break
    sem = CMD_SEM.get(label, CMD_SEM.get(side, ACT_SEM.get(side, k)))   # 语义：动作/演出/流程表，兜底词条名
    rows.append([k, v, '命令', side, '动作' if side in ACTIONS else '演出/流程/系统', sem, param, impl, status_of(label)])
# ── mod 原生动作（无 TK5 命令源词——09b/01/09c 在用 token 登记，2026-08-26；追加为 CSV 命令行动作行）──
MOD_NATIVE = ['set_flag', 'clear_flag', 'set_variable', 'global_set', 'declare_war', 'make_peace',
              'spawn_clan', 'make_alliance', 'relation_change', 'change_clan', 'release_party',
              'grant_troops', 'card_gain', 'card_lose', 'grant_merit', 'set_title', 'promote', 'duel']
for tok in MOD_NATIVE:
    param, impl = ACTIONS[tok]
    st = '🔴 需新加/数据包' if tok == 'global_set' else '✅ 引擎/映射'
    rows.append(['—', '—', '命令', tok, '动作', ACT_SEM.get(tok, tok), param, impl, st])

# 谓词
for k, (param, sem, st) in PREDICATES.items():
    rows.append([k, '—', '谓词', k, '谓词', sem, param, '谓词引擎（01 条件求值）', st])

with open('plans/scenario-campaign-mode/16a-DSL翻译总表.csv', 'w', encoding='utf-8-sig', newline='') as f:
    w = csv.writer(f)
    w.writerow(['太阁原词', '频率', '类别', '我们侧名', '类型', '语义', '参数', '实现用法', '状态'])
    w.writerows(rows)
print('CSV 生成完成：%d 行（域 %d + 属性 %d + 命令 %d + 谓词 %d）' %
      (len(rows), len(domains), len(attrs), len(cmds), len(PREDICATES)))
