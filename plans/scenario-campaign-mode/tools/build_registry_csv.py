# -*- coding: utf-8 -*-
"""生成 16a-DSL翻译总表.csv —— 太阁5 ↔ 骑砍2 唯一翻译大表（正式 plan 数据文件，单一事实源）

列：类别, 太阁原词, 所属域, 我们侧名, 值类型, 语义, 参数, 备注, 频率
- 类别（第一列）：域 / 属性 / 域值 / 命令 / 谓词——排序即可分区
- 所属域（第三列）：属性/域值行 = 语料实际出现的域（人物 / 城 / 大名家 / 多域用「/」分隔）；
  域/命令/谓词行 = —
- 我们侧名：干净的 DSL 映射名（Hero:: / Settlement.owner / kill_hero / exists），不带括号说明
- 值类型（第五列）：仅 属性/域值 行有值——DSL 值的数据类型（数字/布尔/枚举/引用/家族引用…），
  validator 用做「比较左右同型」检查；域/命令/谓词行 = —（🔴 2026-08-27 用户裁定：与类别区分、写清楚）
- 语义：我们侧名的中文释义（高频从白名单/动作表提取；低频词条名自解释）
- 备注（第八列）：🔴 2026-08-27 用户裁定——原「实现用法+状态」合并；人读规划信息（翻译程序不消费）：
  `✅ 引擎查询器` / `🔴 需新增（13 主命 / 02 PartyBrain / 17 官职 / 数据包 / mod 外置属性）` / `❌ 放弃` 等
- 频率（最后一列）：语料出现次数（不太重要，排最后）
运行：仓库根目录 `python plans/scenario-campaign-mode/tools/build_registry_csv.py`

🔴 v2（2026-08-27 结构性修复）：
- 属性行从「属性名 → 单一侧名」改为「域.属性 → 侧名」二维（语料实测域，杜绝跨域同名错配：
  大名家.本城 2298 次曾被登记成 人物域 Hero.home → 下游全量 🔴待注册）
- 新增「域值」类别行（域::值 形态：身份枚举/狀況值/命名槽——旧版零提取）
- 新增谓词 allControlled（全城壓制 481 次带参调用）
- 生成期自检（verify_coverage）：全语料 (域,属性)/(域,值)/带参调用/命令 必须全部可解析，
  表外词条 = 生成失败 exit(1)；侧名合法性断言防中文侧名再犯
"""
import csv
import re
import sys
from collections import Counter

from gen_registry_tables import (DOMAIN_MAP, ATTR_MAP, CMD_MAP, PRED_SIDE_NOARG,
                                 CALL_MAP, DOMAIN_VAL_MAP, PAIR_OVERRIDE, ENTITY_DOMAINS,
                                 domains, attr_pairs, domain_vals, calls, cmds,
                                 pair_side, val_side, call_side, verify_coverage)

txt = open('Knowledge/太阁事件包/TK5AllEvents_merged.txt', encoding='utf-8').read()

# ── 属性类型（16 属性白名单类型列）──
ATTR_TYPES = {
    'year': '数字', 'month': '数字', 'day': '数字',
    'owner': '势力引用', 'clan': '家族引用', 'faction': '势力引用', 'type': '枚举', 'region': '引用',
    'garrison': '数字', 'food': '数字', 'prosperity': '数字', 'security': '数字', 'position': '位置',
    'defense': '数字', 'morale': '数字', 'funds': '数字', 'training': '数字', 'rebellion': '布尔',
    'materials': '数字/物品引用', 'kokudaka': '数字', 'mine': '数字', 'vessels': '数字',
    'suppressed': '布尔', 'movable': '布尔', 'attackable': '布尔', 'siege': '布尔',
    'alive': '布尔', 'state': '枚举', 'leader': '布尔', 'gender': '枚举', 'identity': '枚举', 'age': '数字',
    'home': '据点引用', 'settlement': '据点引用', 'party': '部队引用', 'superior': '角色引用',
    'spouse': '角色引用', 'reputation': '数字', 'infamy': '数字', 'gold': '数字',
    'relation_to': '数字（带参）', 'available': '布尔', 'merit': '数字', 'loyalty': '数字',
    'health': '数字', 'title': '枚举', 'tendency': '枚举',
    'kingdom': '势力引用', 'done': '布尔', 'value': '数字/字符串/引用', '持有': '布尔', '等级': '数字',
    'result': '枚举（BattleResult）', 'leader2': '角色引用', 'strategy': '枚举', 'policy': '枚举',
    'goal': '枚举', 'intent': '枚举', 'power': '数字', 'settlements': '数字', 'unknown': '未知',
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

# ── 谓词（16 谓词表；v2 加入 allControlled——全城壓制 语料 481 次带参调用）──
PREDICATES = {
    'exists': ('引用', '对象存在', '✅ 已设计'), 'atWar': ('a, b（势力引用）', 'a 与 b 交战', '✅ 已设计'),
    'isAllied': ('a, b（势力引用）', 'a 与 b 同盟', '注册表加行'), 'isNeighbor': ('a, b（据点引用）', 'a 与 b 相邻', '注册表加行'),
    'hasRelation': ('hero, hero, op, 数字', '亲密度比较', '注册表加行'), 'relation': ('a, b, op, 数字', '势力间外交关系数值', '注册表加行'),
    'hasMet': ('a, b（角色引用）', '是否认识', '注册表加行'), 'sameSettlement': ('hero, hero', '同据点', '注册表加行'),
    'canPromote': ('hero', '功勋 ≥ 晋升链下一级阈值', '注册表加行（17）'),
    'allControlled': ('region, clan', '区域全部据点由 clan 控制', '注册表加行'),
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

# ── 属性高频中文语义（16 属性白名单语义列；key = 侧名最后段）──
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
    'movable': '可移动', 'attackable': '可攻击', 'siege': '战斗/围城状态', 'result': '战斗结果',
    'strategy': '战略', 'strategy_goal': '战略目标', 'policy': '大方针', 'policy_goal': '大方针目标',
    'power': '支配力', 'settlements': '城数', 'intent': '军团方针', 'unknown': '未知（解析碎片）',
    'leadership': '统率力', 'might': '武力', 'intellect': '智谋', 'governance': '政务', 'charm': '魅力',
    'ambition': '野心', 'leadership_base': '素统率力', 'might_base': '素武力', 'intellect_base': '素智谋',
    'governance_base': '素政务', 'charm_base': '素魅力', 'troops': '兵数', 'general': '武将槽',
    'quest_state': '主命状态（13）', 'quest_assigned': '承担主命（13）', 'quest_goal': '主命目标（13）',
    'quest_deadline': '主命期限（13）', 'work_state': '工作状态（13）', 'work_goal': '工作目标（13）',
    'work_assigned': '承担工作（13）', 'category': '类别', 'reinforce_id': '援军军团番号', 'lifespan': '寿命',
    'ceasefire': '停止进攻', 'court_favor': '朝廷贡献度', 'deserter_count': '出奔计数器', 'item_flag': '持有标记',
    'stamina': '体力', 'gender2': '性别', 'spouse_personality': '妻性格', 'relation_graph': '关系经纬',
    'dojo_owner': '道场主人', 'doctor_rank': '医师评价', 'former_subordinate': '原属下标记', 'heir_flag': '继承人标记',
    'tactics_advisor': '兵法指南役大名家', 'workplace': '自宅邻接工作场', 'mine_max': '矿山最高值',
    'duel_wins': '个人战胜利数', 'appraisal_flag': '鉴定标记', 'license_flag': '印可状标记', 'rival': '对手武将',
    'license': '印可', 'supporter': '支持大名家', 'sword_rank': '流派评价', 'merchant_hq': '本店',
    'merchant_net': '商业圈数', 'merchant_office': '商人司', 'weapon_type': '武具种类', 'spirit': '精神',
    'tournament_flag': '天览试合标记', 'master': '主人', 'terrain': '地形', 'invincible': '无敌标记',
    'weapon_exp': '武具经验', 'knows_taste': '知喜好标记', 'duel_streak': '个人战连胜数', 'bandit_enc': '贼遭遇计数器',
    'attack': '攻', 'greed': '物欲', 'taste': '喜好', 'appearance': '容貌', 'farming': '开垦',
    'origin': '出自', 'stance': '立场', 'personality': '性情', 'bonus': '补正值', 'capacity': '最大载重量',
    'rank': '格', 'clinic_days': '义诊天数', 'medicine_days': '制药天数', 'sword_wins': '剑胜利回数',
    'scale': '规模', 'smith_exp': '制铁经验', 'cannon_exp': '制炮经验', 'visited': '曾经访问',
    'drinking': '饮酒', 'no_battle_flag': '合战禁止标记', 'parents': '父母', 'owner2': '所有者',
}

# ── 域值实现归属（域 :: 值 → 实现）──
DOMAIN_VAL_IMPL = {
    '身份': '17 身份系统', '狀況': '引擎', '據點': '引擎', '忍者衆': '数据包（07 核对）', '商家': '数据包（07 核对）',
    '戰鬥結束種類': '03 战果', '軍團': '02 PartyBrain', '人物類別': '枚举', '事件標誌': '引擎 Flag',
}
DOMAIN_VAL_TYPES = {
    '身份': '枚举', '狀況': '引用/布尔', '據點': '引用', '忍者衆': '组织引用', '商家': '组织引用',
    '戰鬥結束種類': '枚举', '軍團': '部队引用', '人物類別': '枚举', '事件標誌': '旗标',
}

# ── 命令常用中文语义（动作/演出/流程）──
CMD_SEM = {
    '05 lines[] speaker/textKey': '对白', 'condition 表达式（when→condition）': '条件判断', 'script 分支（if 步骤，01 骨架）': '条件分支',
    'script 分支（主人公分派，01/05）': '按主人公分派',
    '动作/ctx_set（16 动作表）': '变量/槽赋值', '05 narrator 行': '旁白', '05 bgm 指令（异步）': 'BGM', '05 se 指令': '音效',
    '05 视觉': '视觉/立绘', '05 消息控制': '消息控制', '05 choice 节点': '玩家选择', '05 choice 门控': '选择门控',
    '05 choice 门控（narrator）': '旁白选择', '05 变名节点（动作表现+台词）': '变名对白', '05 文本变量': '文本变量',
    '05 场景切换': '场景切换', '05 fx 指令': '画面效果', '05 bgm 指令': '背景音乐', '05 se 指令（循环）': '音效循环',
    '05 lines[]': '对白', '05 narrator/自语行': '自语/旁白', '05 choice 选项': '选项设定',
    '05 场景退出': '离开设施', '05 视觉（立绘/过场）': '视觉/过场',
    '事件 JSON id（头字段）': '事件 ID', '事件 JSON trigger/once/priority（头字段，2026-08-26 数据化）': '事件头字段',
    '事件 JSON trigger 字段（头字段）': '触发时机', '事件 JSON condition 字段（头字段）': '发生条件', '事件 JSON script（头字段）': '执行脚本',
    'condition and(…)': '且条件', 'condition or(…)': '或条件', 'Ctx / Variable / GlobalSlot 三档': '代入槽赋值',
    'container_filter': '集合筛选', 'container_pick': '取元素到槽', 'container_set': '按类别初始化',
    'container_exclude': '排除', 'container_clear': '移除', 'container_sort': '排序', 'container_query': '检索',
    'container_access': '存取', '02 PartyBrain': '军团指令', '02 PartyBrain（lock_party/army_gather）': '军团锁定/集结',
    'pause_time 动作（01 调度）': '停止时间', 'teleport 动作（16）': '强制移动', '🔴 流程控制（事件内循环，首版线性展开）': '流程控制',
    '🔴 流程控制（首版线性展开）': '流程控制', '🔴 流程控制（事件内调用）': '事件内调用', '🔴 剧本结局（06/14）': '剧本结局',
    '🔴 06 身份/移动（降级）': '外出/身份', '🔴 06 本城变更（Hero.home）': '居城变更', '🔴 06 身份变更': '立场变更',
    '🔴 17 评定（council_start）': '评定/会议', '🔴 区域任命（降级）': '国主任命', '🔴 商家（数据包）': '商家',
    '🔴 海战扩展': '海战（扩展）', '🔴 降级（骑砍2 无对应小游戏）': '小游戏（降级）', '🔴 Variable 暂存（降级）': '变量暂存',
    '🔴 属性暂存（降级）': '属性暂存', '🔴 解析碎片（忽略）': '解析碎片', '🔴 降级（无对应）': '数字输入（降级）',
    '13 事件主命（QuestDef）': '事件主命', '🔴 13 主命解除（QuestDef）': '解除主命', '🔴 低频 → 降级/忽略': '低频杂项（降级）',
    '05 演出环境（数据包）': '环境变量', 'scene_exit 动作（05 场景退出）': '离开设施',
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


# ── v2：侧名合法性断言（防「角色引用」式中文侧名再犯——DSL token 只收 ASCII；多段 ' / ' 每段校验）──
RE_SIDE_OK = re.compile(r'^[A-Za-z][A-Za-z0-9_.:]*$|^(exists|isAllied|isNeighbor|hasMet|hasRelation|relation|allControlled|unknown)$')


def side_ok(side):
    for part in side.split(' / '):
        p = part.strip()
        if not (RE_SIDE_OK.match(p) or '::' in p and all(RE_SIDE_OK.match(q) for q in p.split('::'))):
            return False
    return True


rows = []
# 域
for k, v in domains.most_common():
    label = DOMAIN_MAP.get(k, '🔴 低频 → 数据包/降级')
    side = side_name(label)
    st = status_of(label)
    note = ('❌ 放弃' if st == '❌ 放弃' else
            '🔴 数据包（07 核对）' if '数据包' in label else
            '🔴 需新加' if st.startswith('🔴') else
            '✅ 引擎域')
    rows.append([k, v, '域', '—', side, '—', DOMAIN_SEM.get(k, k), '—', note])

# ── v2：属性行 = 属性名单键（太阁原词 = 属性名），所属域 = 语料实测域（多域 ' / ' 分隔），
#    侧名按语料域聚合多段；状态列：侧名尾段 ∈ ATTR_TYPES（引擎查询器已有）→ ✅；否则 🔴 mod 需新增外置属性 ──
attr_agg = {}       # 属性名 → [频率合计, [侧名段去重], {域集合}]
for (d, a), c in attr_pairs.items():
    side = pair_side(d, a)
    if side is None:
        continue        # 自检已报错（verify_coverage 在 main 里先行 exit）
    agg = attr_agg.setdefault(a, [0, [], set()])
    agg[0] += c
    if side not in agg[1]:
        agg[1].append(side)
    agg[2].add(d)
for a, (c, sides, doms) in sorted(attr_agg.items(), key=lambda kv: -kv[1][0]):
    side_all = ' / '.join(sides)
    dom_all = ' / '.join(sorted(doms))
    tail = sides[0].split('.')[-1]
    if sides[0].startswith('exists') or sides[0] in PRED_SIDE_NOARG or sides[0] in ('allControlled',):
        typ, sem = '谓词', ATTR_SEM.get(tail, a)
        note = '谓词引擎（01 条件求值）'
    else:
        typ = ATTR_TYPES.get(tail, ('布尔' if a.endswith(('標誌', '可能')) else '🔴 待定'))
        sem = ATTR_SEM.get(tail, a)
        if sides[0] == 'unknown':
            note = '🔴 解析碎片'
        elif all(s.split('.')[-1] not in ATTR_TYPES for s in sides):
            # 🔴 mod 需新增外置属性（用户裁定标注）；归属从侧名前缀推
            owner = ('13 主命' if sides[0].startswith(('Hero.quest', 'Hero.work')) else
                     '02 PartyBrain' if sides[0].startswith('Army') else
                     '17 官职' if sides[0].startswith(('court_rank', 'title')) else
                     '13 主命' if sides[0].startswith('QuestDef') else
                     '03 预设' if sides[0].startswith('weather') else
                     '05 演出设施' if sides[0].startswith('Facility') else
                     '数据包' if sides[0].startswith(('Item', 'Card', 'Org', 'ItemType', 'env', 'bgm', 'ending')) else
                     'mod 外置属性')
            note = f'🔴 需新增（{owner}）'
        else:
            note = '✅ 引擎查询器'
    rows.append([a, c, '属性', dom_all, side_all, typ, sem, '—', note])

# ── v2：域值行（域::值 形态：身份枚举/狀況值/命名槽）——实体域不生成行（名字表/fallback 的事）；
#    太阁原词 = 纯值（元締），所属域列 = 域（身份）——第二列不掺符号（2026-08-27 用户裁定）──
val_seen = set()
for (d, v), c in domain_vals.most_common():
    if d in ENTITY_DOMAINS:
        continue            # 实体引用域：不进 CSV（人物::伊藤總十郎 等，2026-08-27 用户裁定）
    side = val_side(d, v)
    if side is None:
        continue
    val_seen.add((d, v))
    typ = DOMAIN_VAL_TYPES.get(d, '枚举')
    sem = v
    impl = DOMAIN_VAL_IMPL.get(d, '引擎')
    note = '✅ 引擎' if impl == '引擎' else f'🔴 需新加（{impl}）'
    rows.append([v, c, '域值', d, side, typ, sem, '—', note])

# 命令（纯 TK5——mod 原生 18 动作 token 已移出 16a，权威 = 16.md §六 动作 token 注册表，2026-08-27 用户裁定）
for k, v in cmds.most_common():
    label = CMD_MAP.get(k)
    if label is None:
        label = ('Ctx / Variable / GlobalSlot 三档' if k.startswith('代入') else
                 'container_*（pick 组）' if k.startswith('容器') else
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
    rows.append([k, v, '命令', '—', side, '—', sem, param, impl])      # 备注 = 实现归属（含 🔴/❌ 状态，label 自含）

# ── 谓词区：只收 TK5 调用词翻译行（外交同盟→isAllied…）──
# mod DSL 谓词 token（atWar/isAllied/…）权威 = 16.md §三 谓词注册表（2026-08-27 用户裁定：CSV 太阁原词列只收 TK5 词）
CALL_SEM = {
    'isAllied': 'a 与 b 同盟', 'relation': '势力间外交关系数值', 'isNeighbor': 'a 与 b 相邻',
    'allControlled': '区域全部据点由 clan 控制', 'hasCard': '是否持有技能卡',
    'canMove': '角色能否前往该据点', 'canAttack': '角色能否攻击该据点',
}
for call_word, (pred, params) in CALL_MAP.items():
    rows.append([call_word, '—', '谓词', '—', pred, '—', CALL_SEM.get(pred, pred), ', '.join(params), '注册表加行（谓词引擎）'])

# ── 例句列：词条 → TK5 事件原句示范（首次出现行截断；🔴 2026-08-27 用户裁定，给人检查用）──
EXAMPLE_LEN = 60
example = {}
terms = {(r[2], r[0]) for r in rows if r[0] != '—'}
for _line in txt.splitlines():
    s = _line.strip()
    if not s:
        continue
    m = re.match(r'^([一-鿿Ａ-Ｚａ-ｚA-Za-z]{2,8}):', s)
    if m and ('命令', m.group(1)) in terms:
        example.setdefault(('命令', m.group(1)), s)
    for dm in re.finditer(r'([一-鿿A-Za-zＡ-Ｚａ-ｚ]{1,6})::([一-鿿A-Za-zＡ-Ｚａ-ｚ0-9０-９.]{1,16})', s):
        dom, rest = dm.group(1), dm.group(2)
        if rest.endswith('.'):
            continue
        if '.' in rest:
            attr = rest.split('.')[-1]
            if ('属性', attr) in terms:
                example.setdefault(('属性', attr), s)
        else:
            if ('域值', rest) in terms:
                example.setdefault(('域值', rest), s)
            if ('域', dom) in terms:
                example.setdefault(('域', dom), s)
    for cm in re.finditer(r'\.([一-鿿A-Za-zＡ-Ｚａ-ｚ]+)\(', s):
        if ('谓词', cm.group(1)) in terms:
            example.setdefault(('谓词', cm.group(1)), s)
for r in rows:
    ex = example.get((r[2], r[0]), '')
    r.append(ex if len(ex) <= EXAMPLE_LEN else ex[:EXAMPLE_LEN] + '…')

# ── 防再犯自检 ──
def main():
    errors = verify_coverage()
    if errors:
        print('❌ 16a CSV 生成中止：全语料覆盖自检失败（表外词条 = 生成器缺陷）')
        for e in errors[:80]:
            print('  ', e)
        print(f'  …共 {len(errors)} 条表外')
        sys.exit(1)
    # 侧名合法性断言：DSL token 只收 ASCII（防「角色引用」式中文侧名）；只查 属性/域值/谓词 行
    #（命令行侧名是描述性 label，翻译器另有动作映射，不在此列）
    side_errors = [r[4] for r in rows if r[2] in ('属性', '域值', '谓词') and not side_ok(r[4])]
    if side_errors:
        print('❌ 侧名非法（DSL token 必须 ASCII）：')
        for s in side_errors[:30]:
            print('  ', s)
        sys.exit(1)
    # 🔴 v2 纯净性断言：太阁原词列（第二列）禁止掺符号——「::」「.」属于提取残渣（2026-08-27 用户裁定）
    dirty = [r[0] for r in rows if '::' in str(r[0]) or '.' in str(r[0])]
    if dirty:
        print('❌ 太阁原词列不纯净（混入 :: / .，域信息应放「所属域」列）：')
        for d in dirty[:30]:
            print('  ', d)
        sys.exit(1)

    with open('plans/scenario-campaign-mode/16a-DSL翻译总表.csv', 'w', encoding='utf-8-sig', newline='') as f:
        w = csv.writer(f)
        # 🔴 列序（2026-08-27 用户裁定）：类别第一列（排序分区看），所属域第三列（属性类别），
        #    频率倒数第二，例句最后一列（TK5 原句示范，人检查用）
        # 内部 rows 保持 [原词, 频率, 类别, 所属域, 侧名, 值类型, 语义, 参数, 备注, 例句] → 写文件重排
        ORDER = [2, 0, 3, 4, 5, 6, 7, 8, 1, 9]
        w.writerow(['类别', '太阁原词', '所属域', '我们侧名', '值类型', '语义', '参数', '备注', '频率', '例句'])
        for r in rows:
            w.writerow([r[i] for i in ORDER])
    n_attr = len([r for r in rows if r[2] == '属性'])
    n_val = len([r for r in rows if r[2] == '域值'])
    print(f'✅ CSV 生成完成：{len(rows)} 行（域 {len(domains)} + 属性 {n_attr} + 域值 {n_val} + 命令 {len(cmds)} + 谓词 {len(CALL_MAP)}）')
    print('  全语料覆盖自检通过：属性(域,属性)、域值(域::值)、带参调用、命令 全部可解析')


if __name__ == "__main__":
    main()
