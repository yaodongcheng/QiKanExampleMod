# -*- coding: utf-8 -*-
"""生成 16a-DSL翻译总表.csv —— 太阁5 ↔ 骑砍2 唯一翻译大表（正式 plan 数据文件，单一事实源）

列：类别, 太阁原词, 所属域, 我们侧名, 值类型, 语义, 参数, 备注, 频率
- 类别（第一列）：域 / 属性 / 域值 / 命令 / 函数——排序即可分区
- 所属域（第三列）：属性/域值行 = 语料实际出现的域（人物 / 城 / 大名家 / 多域用「/」分隔）；
  域/命令/函数行 = —
- 我们侧名：干净的 DSL 映射名（Hero:: / Settlement.owner / kill_hero / exists），不带括号说明
- 值类型（第五列）：仅 属性/域值 行有值——DSL 值的数据类型（数字/布尔/枚举/引用/家族引用…），
  validator 用做「比较左右同型」检查；域/命令/函数行 = —（🔴 2026-08-27 用户裁定：与类别区分、写清楚）
- 语义：我们侧名的中文释义（高频从白名单/动作表提取；低频词条名自解释）
- 备注（第八列）：🔴 2026-08-27 用户裁定——原「实现用法+状态」合并；人读规划信息（翻译程序不消费）：
  `✅ 引擎查询器` / `🔴 需新增（13 主命 / 02 PartyBrain / 17 官职 / 数据包 / mod 外置属性）` / `❌ 放弃` 等
- 频率（最后一列）：语料出现次数（不太重要，排最后）
运行：仓库根目录 `python plans/scenario-campaign-mode/tools/build_registry_csv.py`

🔴 v2（2026-08-27 结构性修复）：
- 属性行从「属性名 → 单一侧名」改为「域.属性 → 侧名」二维（语料实测域，杜绝跨域同名错配：
  大名家.本城 2298 次曾被登记成 人物域 Hero.home → 下游全量 🔴待注册）
- 新增「域值」类别行（域::值 形态：身份枚举/狀況值/命名槽——旧版零提取）
- 新增函数 allControlled（全城壓制 481 次带参调用）
- 生成期自检（verify_coverage）：全语料 (域,属性)/(域,值)/带参调用/命令 必须全部可解析，
  表外词条 = 生成失败 exit(1)；侧名合法性断言防中文侧名再犯
"""
import csv
import re
import sys
from collections import Counter

from gen_registry_tables import (DOMAIN_MAP, ATTR_MAP, CMD_MAP, FUNC_SIDE_NOARG,
                                 CALL_MAP, DOMAIN_VAL_MAP, PAIR_OVERRIDE, ENTITY_DOMAINS, SYNTAX_CMDS,
                                 SPECIAL_VALS, SPECIAL_TYPES,
                                 domains, attr_pairs, domain_vals, calls, cmds,
                                 pair_side, val_side, call_side, verify_coverage)

txt = open('Knowledge/太阁事件包/TK5AllEvents_merged.txt', encoding='utf-8').read()

# ── 值类型体系（🔴 2026-08-27 用户裁定统一）：布尔 / 数字 / 字符串 / 枚举（受限字符串）/ 空 /
#    对象:子类型（据点/人物/家族/王国/区域/部队/组织/卡/物品/设施/官职/任务/旗标/位置）/ 未知 / 🔴 待定
ATTR_TYPES = {
    'year': '数字', 'month': '数字', 'day': '数字',
    'owner': '对象:王国', 'clan': '对象:家族', 'faction': '对象:王国', 'type': '枚举', 'region': '对象:区域',
    'garrison': '数字', 'food': '数字', 'prosperity': '数字', 'security': '数字', 'position': '对象:位置',
    'defense': '数字', 'morale': '数字', 'funds': '数字', 'training': '数字', 'rebellion': '布尔',
    'materials': '数字', 'kokudaka': '数字', 'mine': '数字', 'vessels': '数字',
    'suppressed': '布尔', 'movable': '布尔', 'attackable': '布尔', 'siege': '布尔',
    'alive': '布尔', 'state': '枚举', 'leader': '布尔', 'gender': '枚举', 'identity': '枚举:身份（带序：17 身份链）', 'age': '数字',
    'home': '对象:据点', 'settlement': '对象:据点', 'party': '对象:部队', 'superior': '对象:人物',
    'spouse': '对象:人物', 'reputation': '数字', 'infamy': '数字', 'gold': '数字',
    'relation_to': '数字', 'available': '布尔', 'merit': '数字', 'loyalty': '数字',
    'known': '布尔',
    'health': '数字', 'title': '枚举:官職（带序：17 官职品级）', 'tendency': '枚举',
    'kingdom': '对象:王国', 'done': '布尔', 'value': '数字/字符串/对象', '持有': '布尔', '等级': '数字',
    'result': '枚举（BattleResult）', 'strategy': '枚举', 'policy': '枚举',
    'goal': '枚举', 'intent': '枚举', 'power': '数字', 'settlements': '数字', 'unknown': '未知',
}

# 域值类型（值类型体系同 ATTR_TYPES）
DOMAIN_VAL_TYPES = {
    '身份': '枚举:身份（带序：17 身份链）', '狀況': '数字/布尔/对象', '據點': '对象:据点', '忍者衆': '对象:组织', '商家': '对象:组织',
    '戰鬥結束種類': '枚举', '軍團': '对象:部队', '人物類別': '枚举', '事件標誌': '布尔', '真偽': '布尔',
    '天氣': '枚举', '日數計數器': '数字', '變量': '数字/字符串/对象', '儲存號': '数字/字符串/对象',
    '場面': '对象:设施', '物品類型': '枚举', '軍團方針': '枚举', '官位': '枚举:官位（带序：17 官职品级）', '官職': '枚举:官職（带序：17 官职品级）',
    '工作': '枚举:工作', '事件主命': '枚举:事件主命', '主命': '枚举:主命',
}
# 按具体 (域,值) 精确化（語料例句判定：劇本==(2) 数字、場面==(場面::自宅) 设施、評定期間標誌 布尔）
DOMAIN_VAL_TYPE_OVERRIDE = {
    ('狀況', '年'): '数字', ('狀況', '月'): '数字', ('狀況', '日'): '数字',
    ('狀況', '24'): '数字', ('狀況', '遊戲經過日數'): '数字',
    ('狀況', '戰爭禁止日數'): '数字', ('狀況', '空閒大名家數'): '数字', ('狀況', '劇本'): '数字',
    ('狀況', '評定期間標誌'): '布尔', ('狀況', '評定期限結束標誌'): '布尔',
    ('狀況', '場面'): '对象:设施', ('狀況', '天氣'): '枚举:天氣',
}


# 🔴 域内容类型（2026-08-27 用户裁定：域 = 值域，「域::值」里值的类型就是域的内容类型——
# 人物域值=人物对象、身份域值=身份枚举、真偽域值=布尔；与 16.md 值类型体系（42 域对照）一致）
DOMAIN_CTYPE = {
    '人物': '对象:人物', '大名家': '对象:家族', '城': '对象:据点', '據點': '对象:据点',
    '砦': '对象:据点', '町': '对象:据点', '里': '对象:据点',
    '勢力': '对象:王国', '國': '对象:区域', '地方': '对象:区域',
    '軍團': '对象:部队', '忍者衆': '对象:组织', '商家': '对象:组织', '海賊衆': '对象:组织',
    '卡': '对象:卡', '流派': '对象:卡', '物品': '对象:物品', '交易品': '对象:物品',
    '場面': '对象:设施', '主命': '枚举:主命', '工作': '枚举:工作', '事件主命': '枚举:事件主命',
    '事件標誌': '对象:旗标', '事件': '对象:事件', '事件發生狀態': '对象:事件',
    '身份': '枚举:身份（带序：17 身份链）', '官位': '枚举:官位（带序：17 官职品级）', '官職': '枚举:官職（带序：17 官职品级）',
    '天氣': '枚举:天氣', '人物類別': '枚举:人物類別', '戰鬥結束種類': '枚举:戰鬥結束種類', '物品類型': '枚举:物品類型', '軍團方針': '枚举:軍團方針',
    '真偽': '布尔', '日數計數器': '数字',
    '狀況': '数字/布尔/对象', '變量': '数字/字符串/对象', '儲存號': '数字/字符串/对象',
    '主命屬性': '数字（编号）', '遊戲通關種類': '🔴 待定', '環境變量': '🔴 待定', '背景音樂': '🔴 待定',
}


# 🔴 槽引用推断接管表（2026-08-27 用户裁定）：语料只有 (ａ) 槽赋值 → 推断「数字/对象」（动态类型）模糊；
#   语义明确（如五维属性 = 数字）→ 此表接管。⚠️ 不放进 ATTR_TYPES——备注判定把 ATTR_TYPES 当
#   「引擎已有查询器」清单，放进去会谎报实现状态（武力 被标 ✅ 引擎查询器）
FUZZY_TYPE_OVERRIDE = {
    'might': '数字', 'governance': '数字', 'leadership': '数字', 'intellect': '数字', 'charm': '数字',
    'might_base': '数字', 'governance_base': '数字', 'leadership_base': '数字', 'intellect_base': '数字', 'charm_base': '数字',
}


def _rtype_of(v):
    """右值 → 类型（语料推断用）：数字字面量→数字、真偽→布尔、域::值→域内容类型（DOMAIN_CTYPE）、
    槽引用（ａ/人物Ｄ）→数字/对象（动态）、裸值→枚举（状态值）"""
    v = v.strip()
    if re.match(r'^-?\d+$', v):
        return '数字'
    if v.startswith('真偽::'):
        return '布尔'
    if v in ('真', '偽'):
        return '布尔'      # 🔴 真偽 裸写（2026-08-27 用户裁定：更新:(商家::X.未知３)(偽) → 布尔）
    if re.match(r'^[ａ-ｚＡ-Ｅ]$', v) or re.match(r'^(?:人物|據點|城|大名家|勢力|國|忍者衆|商家|海賊衆|地方|町|砦|里|軍團)[Ａ-Ｅ]$', v):
        return '数字/对象'      # 变量槽引用（动态类型）
    if '::' in v:
        return DOMAIN_CTYPE.get(v.split('::', 1)[0], '数字/字符串/对象')
    return '枚举'      # 裸值 = 状态枚举（出撃中/未出現/死刑…）


def infer_attr_types():
    """🔴 语料驱动属性值类型推断（2026-08-27 用户裁定：属性类型从比较/赋值的右值类型推断，
    不靠人工表——自动且能纠错：城主 比较右值 = 大名家 → 对象:家族 而非人工标的 对象:王国）。
    扫描 調查/更新 行：恰好 1 个属性表达式（域::X.属性）时，与同行右值（括号组）配对计数取主流。"""
    from collections import Counter as _C
    inf, inf_vals = {}, {}
    for line in txt.splitlines():
        s = line.strip()
        if not s or s.startswith('#') or not s.startswith(('調查', '更新', '代入', '場合別', '場合分歧')):
            continue
        groups = re.findall(r'\(([^()]*)\)', s)
        attrs = [g for g in groups
                 if re.match(r'^(?:[一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6})::[^.（()]+\.([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ0-9０-９]+)$', g)]
        if not attrs:
            continue                          # 无属性 → 跳过
        # 🔴 属性对属性 算式/不等式（两侧都是属性表达式，2026-08-27 用户裁定：算式与不等式 = 数字——
        #   格>格 / 現石高>現石高 / 技能+技能 两侧都计数字；单属性算式/不等式走下方 RHS 计数——
        #   身份>=(身份::城主)、官位>=(官位::X) 是带序枚举比较，保持枚举，不进此分支）
        if len(attrs) >= 2 and (re.search(r'\)\s*(?:>=|<=|>|<)\s*\(', s)
                                or re.search(r'\)\s*[\*\+\-/]\s*\(', s)):
            for g in attrs:
                inf.setdefault(re.match(r'^[一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6}::[^.（()]+\.([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ0-9０-９]+)$', g).group(1),
                               _C())['数字'] += 1
            continue
        if len(attrs) != 1:
            continue                          # 多属性（非算式/不等式）/嵌套调用（函数形态）→ 跳过
        attr = re.match(r'^(?:[一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6})::[^.（()]+\.([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ0-9０-９]+)$', attrs[0]).group(1)
        # 🔴 单属性不等式（>= <= > <）= 数字强证据（2026-08-27 用户裁定：基準石高>=(變量::ｇ) → 数字，
        #   右值是什么不影响——不等式本身只对数字有意义）；**带序枚举右值除外**——
        #   身份>=(身份::城主) 是带序枚举合法比较（17 等级链），不是数字证据
        if re.search(r'\)\s*(?:>=|<=|>|<)\s*\(', s):
            ordered = any(DOMAIN_VAL_TYPES.get(g.split('::', 1)[0], '').find('带序') >= 0
                          for g in groups if '::' in g and g != attrs[0])
            if not ordered:
                inf.setdefault(attr, _C())['数字'] += 1
                continue
        # 🔴 switch 分支（2026-08-27 用户裁定）：場合別/場合分歧 的条件属性——分支值 = 数字
        #   （喜好 等 = 数字编码的枚举，具体枚举含义未知；類型 = 数字）
        if s.startswith(('場合別', '場合分歧')):
            inf.setdefault(attr, _C())['数字'] += 1
            continue
        # 🔴 算术运算（2026-08-27 用户裁定）：`)*(`/`)+(`/`)-(`/`)/(` 括号间加减乘除 =
        #   两侧同量纲 = 数字强证据（代入ｄ:(...個人戰敗北数)*(4) → 个人戰敗北数 = 数字）
        if re.search(r'\)\s*[\*\+\-/]\s*\(', s):
            inf.setdefault(attr, _C())['数字'] += 1
            continue
        # 🔴 代入槽反向推断（2026-08-27 用户裁定：代入槽:(属性) → 属性类型 = 槽类型——
        #   代入勢力Ａ:(町::町Ａ.商人司) → 商人司 = 对象:王国；ｐ/ｂ 通用变量槽 → 数字/对象 模糊
        #   （弱证据，具体优先规则在属性行生成处处理））
        if s.startswith('代入') and len(attrs) == 1:
            inf.setdefault(attr, _C())[slot_ctype(s.split(':', 1)[0])] += 1
            continue
        for g in groups:
            if g == attrs[0]:
                continue                      # 左值（属性表达式）
            if len(g) > 24:
                continue                      # 超长 = 复杂表达式，跳过
            inf.setdefault(attr, _C())[_rtype_of(g)] += 1
            inf_vals.setdefault(attr, _C())[g] += 1      # 🔴 枚举值集合收集（原屬下標誌 → 原上司/原同事/原屬下…）
    # 🔴 标誌族二态判定（2026-08-27 用户裁定：标誌类二态属性统一布尔——TK5 的 0/1 只是数字拼写，
    #   与语义词（已出現/未出現…）指同一状态；已發生/未發生 是跨域借用拼写，归入成立轴不计数）：
    #   语义词 ≤2 个且无 ≥2 数字 → 布尔（出現/死亡/所持/戰鬥/出撃/生病/離家/鑑定/死刑標誌…）；
    #   语义词 ≥3 → 枚举（原屬下標誌 3 态）；出现 ≥2 数字 → 数字（天覧試合標誌==(3)）
    bool_flag_attrs = set()
    for attr, cnt in list(inf.items()):
        if not attr.endswith('標誌'):
            continue
        vals = set(inf_vals.get(attr, ()))
        bare = [w for w in vals if not re.match(r'^-?\d+$', w) and '::' not in w
                and w not in ('真', '偽', '已發生', '未發生')]
        nums = [int(w) for w in vals if re.match(r'^-?\d+$', w)]
        if not bare and any(n >= 2 for n in nums):
            inf[attr] = _C({'数字': sum(cnt.values())})
        elif len(bare) >= 3:
            inf[attr] = _C({'枚举': sum(cnt.values())})
        else:
            inf[attr] = _C({'布尔': sum(cnt.values())})
            bool_flag_attrs.add(attr)
    return inf, inf_vals, bool_flag_attrs


def infer_domain_val_types():
    """🔴 域值类型语料驱动推断（2026-08-27 用户裁定）：值类型一致性纪律（16 §四）反向利用——
    代入槽:(域::值) → 槽类型（代入人物Ｂ:(儲存號::本能寺呼寄武将) → 对象:人物）；
    更新:(域::值)(X) / 調查 与 X 比较 → X 类型（X 为数字字面量/具名槽/域值/属性表达式/真偽）。
    域默认动态（数字/字符串/对象）时接管为具体类型；域默认明确（據點=对象:据点 等）不接管。"""
    from collections import Counter as _C
    res = {}
    DV = re.compile(r'^([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6})::([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ0-9０-９]{1,14})$')
    SLOT = re.compile(r'^(?:[一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6})?[Ａ-Ｅ]$')
    ATTR = re.compile(r'^[一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6}::[^.（()]+\.([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ0-9０-９]+)$')
    FUZZY = ('数字/对象', '数字/字符串/对象', '数字/字符串', '枚举')

    def other_t(g):
        g = g.strip()
        if SLOT.match(g):
            t = slot_ctype('代入' + g)
            return t if t not in FUZZY else None          # 具名槽（人物Ｂ → 对象:人物）；ａ-ｚ 动态槽无强证据
        m = DV.match(g)
        if m:
            return DOMAIN_VAL_TYPES.get(m.group(1))        # 对方域值 → 其域默认（明确时）
        m2 = ATTR.match(g)
        if m2:
            return ATTR_TYPES.get(m2.group(1))             # 属性表达式 → 人工表类型（妻 → 对象:人物）
        t = _rtype_of(g)
        return t if t not in FUZZY else None               # 数字字面量/真偽 → 数字/布尔

    for line in txt.splitlines():
        s = line.strip()
        if not s or s.startswith('#') or not s.startswith(('調查', '更新', '代入')):
            continue
        groups = re.findall(r'\(([^()]*)\)', s)
        if not groups:
            continue
        if s.startswith('代入'):
            t = slot_ctype(s.split(':', 1)[0])
            if t not in FUZZY:
                m = DV.match(groups[0].strip())
                if m:
                    res.setdefault((m.group(1), m.group(2)), _C())[t] += 1
            continue
        for g in groups:
            m = DV.match(g.strip())
            if not m:
                continue
            key = (m.group(1), m.group(2))
            for h in groups:
                if h == g:
                    continue
                t = other_t(h)
                if t:
                    res.setdefault(key, _C())[t] += 1
    return res


def slot_ctype(cmd):
    """代入XX 命令 → 槽值类型（🔴 2026-08-27 用户裁定：赋值对象类型写清楚，城Ａ = 对象:据点）。"""
    m = re.match(r'^代入([一-鿿぀-ヿＡ-Ｚａ-ｚA-Za-z]+)$', cmd)
    if not m:
        return None
    body = m.group(1)
    if re.match(r'^[ａ-ｚ]$', body):
        return '数字/对象'                              # ａ-ｚ 通用变量槽（士气值/年份/人物引用都可能）
    if body.startswith('文字列'):
        return '字符串'
    for dom, t in (('人物', '对象:人物'), ('城', '对象:据点'), ('據點', '对象:据点'),
                   ('町', '对象:据点'), ('里', '对象:据点'), ('砦', '对象:据点'),
                   ('大名家', '对象:家族'), ('勢力', '对象:王国'), ('國', '对象:区域'), ('地方', '对象:区域'),
                   ('忍者衆', '对象:组织'), ('商家', '对象:组织'), ('海賊衆', '对象:组织'),
                   ('軍團', '对象:部队'), ('卡', '对象:卡'), ('流派', '对象:流派'),
                   ('物品', '对象:物品'), ('交易品', '对象:物品'),
                   ('主命目標', '对象:任务'), ('事件主命', '对象:任务')):
        if body.startswith(dom):
            return t
    return '数字/字符串/对象'

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

# ── 函数（16 函数表；v2 加入 allControlled——全城壓制 语料 481 次带参调用）──
FUNCTIONS = {
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
    '砦': '据点（砦）', '地方': '地方/区域', '交易品': '交易品', '儲存號': '存档槽', '官職': '官职', '流派': '流派（后续补充）',
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
    'attack': '攻城计数器', 'greed': '物欲', 'taste': '喜好', 'appearance': '容貌', 'farming': '开垦',
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
# 域值类型定义见上方 ATTR_TYPES 后的 DOMAIN_VAL_TYPES（值类型体系统一，2026-08-27 用户裁定）

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
    m = re.search(r'([A-Za-z_]+)(?: 动作| 函数| 指令)', label)
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
            '🔴 数据包（07 核对）' if '数据包' in label and '07' in label else
            '🔴 数据包（后续补充）' if '数据包' in label else            '🔴 需新加' if st.startswith('🔴') else
            '✅ 引擎域')
    rows.append([k, v, '域', '—', side, '域', DOMAIN_SEM.get(k, k), '—', note])  # 🔴 域 = 容器：值类型列 = '域'（2026-08-27 用户裁定：容器无类型，域值才有类型）

# ── v2：属性行 = 属性名单键（太阁原词 = 属性名），所属域 = 语料实测域（多域 ' / ' 分隔），
#    侧名按语料域聚合多段；备注：侧名尾段 ∈ ATTR_TYPES（引擎查询器已有）→ ✅；否则 🔴 mod 需新增外置属性 ──
attr_agg = {}       # 属性名 → [频率合计, [侧名段去重], {域集合}]
attr_infer, attr_vals, bool_flag_attrs = infer_attr_types()     # 🔴 语料驱动值类型推断 + 枚举值集合（2026-08-27 用户裁定）
for (d, a), c in attr_pairs.items():
    if a in CALL_MAP:
        continue        # 🔴 带参调用词条 = 函数（卡持有/外交同盟/全城壓制…，语料零无参形态），
                        #   只归函数区，不进属性区（2026-08-27 用户裁定）
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
    # 🔴 2026-08-27 用户裁定：属性行值类型**永不**是"函数"（值类型体系 = 布尔/数字/字符串/枚举/空/对象:子类型）——
    #   亲密度/認識標誌 的属性形态已改映射为 Hero.relation_to/Hero.known，走统一推断
    # 🔴 值类型 = 语料推断主流（比较/赋值右值类型）；无推断 → 人工表 ATTR_TYPES 兜底
    infer = attr_infer.get(a)
    sem = ATTR_SEM.get(tail, a)
    # 🔴 具体类型优先（2026-08-27 用户裁定：赋值/比较对方类型 = 强证据；「数字/对象」等模糊类型
    #   是"推断不出"的标记、不是类型——具体证据存在时忽略模糊证据：
    #   補正值 (7) 字面量 1 票 > (ｐ) 槽 2 票 → 数字）
    FUZZY_TYPES = ('数字/对象', '数字/字符串/对象', '数字/布尔/对象', '数字/字符串', '数字/布尔')
    if infer:
        concrete = Counter({k: v for k, v in infer.items() if k not in FUZZY_TYPES})
        top = (concrete.most_common(1)[0][0] if concrete
               else infer.most_common(1)[0][0])
    else:
        top = None
    # 🔴 槽引用推断 = 动态类型（模糊证据，2026-08-27 用户裁定）→ FUZZY_TYPE_OVERRIDE 明确类型接管：
    #   武力/統率力 语料只有 (ａ) 槽赋值 → 数字/对象；五维属性语义 = 数字，接管表修正（政務/智謀/魅力 有数字字面量证据则语料直接推断）
    if top == '数字/对象' and tail in FUZZY_TYPE_OVERRIDE:
        top = FUZZY_TYPE_OVERRIDE[tail]
    if top and top not in FUZZY_TYPES:
        typ = top
    elif re.fullmatch(r'[0-9０-９]+', a):
        # 🔴 编号属性兜底（2026-08-27 用户裁定）：全数字属性名 = 域::具名值.编号 的数值编码
        #   （人物類別::泛用對手.60 → Identity.attr_60、官位::正一位.16 → court_rank.attr_16、
        #   天氣::晴.147 → weather.attr_147）→ 数字——**无推断 或 推断为动态类型**
        #   （数字/布尔/对象 等宽泛类型 = 弱证据，如 更新:(狀況::天氣)(天氣::晴.147) 右值是动态域）都兜底数字；
        #   有具体类型推断（==真偽 → 布尔、==(0) → 数字、==(城::X) → 对象:据点）保持推断
        typ = '数字'
    elif top:
        # 全模糊推断 → 人工表 ATTR_TYPES 兜底（大筒 → Settlement.materials，ATTR_TYPES['materials']=数字）；
        # 人工表无登记 → 保持模糊类型如实
        typ = ATTR_TYPES.get(tail, top)
    else:
        typ = ATTR_TYPES.get(tail, ('布尔' if a.endswith(('標誌', '可能')) else '🔴 待定'))
    if typ == '枚举':
        typ = f'枚举:{a}'   # 🔴 属性枚举 = 属性名（原屬下標誌 → 枚举:原屬下標誌）
        # 🔴 枚举值集合（语料右值事实，2026-08-27 用户裁定：写了枚举类型就要定义全部值）
        vs = [v for v, _ in attr_vals.get(a, Counter()).most_common(8)]
        if vs:
            sem = f'{sem}（值：{" / ".join(vs)}）'
    elif a in bool_flag_attrs:
        # 🔴 布尔标誌族：语义列附 TK5 拼写清单（0/1 与语义词 → true/false 的映射依据，2026-08-27 用户裁定）
        vs = [v for v, _ in attr_vals.get(a, Counter()).most_common(8)]
        if vs:
            sem = f'{sem}（TK5 拼写：{" / ".join(vs)} → true/false）'
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
slot_inf = infer_domain_val_types()     # 🔴 域值类型用法推断（2026-08-27 用户裁定：代入槽/比较/赋值的对方类型）
for (d, v), c in domain_vals.most_common():
    if d in ENTITY_DOMAINS and not re.match(r'^[一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,8}[Ａ-Ｅ]$', v) and not re.match(r'^[ａ-ｚ]$', v) and v not in SPECIAL_VALS:
        continue            # 具名实体域：不进 CSV（人物::伊藤總十郎 等，2026-08-27 用户裁定）；
                            # 🔴 槽形态（人物Ｂ）与特殊值（主人公/無效）例外——进表（2026-08-27 用户裁定）
    side = val_side(d, v)
    if side is None:
        continue
    val_seen.add((d, v))
    if v in SPECIAL_TYPES:
        typ = SPECIAL_TYPES[v]            # 🔴 特殊值类型（主人公 = 对象:人物、無效 = 空）
    elif re.match(r'^[一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,8}[Ａ-Ｅ]$', v) or re.match(r'^[ａ-ｚ]$', v):
        # 🔴 槽域值：类型 = 槽类型（人物Ｂ → 对象:人物，slot_ctype 判定），与 tk5_to_json 一致
        typ = slot_ctype('代入' + v)
    else:
        typ = DOMAIN_VAL_TYPE_OVERRIDE.get((d, v), DOMAIN_VAL_TYPES.get(d, '枚举'))
    # 🔴 域默认动态（數字/字符串/对象 等）→ 用法推断接管（代入人物Ｂ:(儲存號::X) → 对象:人物；
    #   調查:(儲存號::X)<=(45) → 数字）；域默认明确（據點=对象:据点）不接管；人工 OVERRIDE 最高优先
    if (d, v) not in DOMAIN_VAL_TYPE_OVERRIDE and DOMAIN_VAL_TYPES.get(d, '') in ('数字/字符串/对象', '数字/对象', '数字/字符串', '数字/布尔/对象'):
        si = slot_inf.get((d, v))
        if si:
            t = si.most_common(1)[0][0]
            if t not in ('枚举',):
                typ = t
    if typ == '枚举':
        typ = f'枚举:{d}'   # 🔴 域值枚举 = 所属域类型（2026-08-27 用户裁定：枚举太宽泛，标具体类型）
    sem = v
    impl = DOMAIN_VAL_IMPL.get(d, '引擎')
    note = '✅ 引擎' if impl == '引擎' else f'🔴 需新加（{impl}）'
    rows.append([v, c, '域值', d, side, typ, sem, '—', note])

# 命令（纯 TK5——mod 原生 18 动作 token 已移出 16a，权威 = 16.md §六 动作 token 注册表，2026-08-27 用户裁定）；
# 🔴 语法词（條件/流程/事件结构）类别 = 语法，不占「命令」（2026-08-27 用户裁定：语法全量进表）
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
    cat = '语法' if k in SYNTAX_CMDS else '命令'
    typ = slot_ctype(k) if k.startswith('代入') else '—'                # 🔴 代入槽：值类型 = 赋值对象类型（2026-08-27 用户裁定）
    if k == '更新':
        # 🔴 双括号语法 + 类型一致性（2026-08-27 用户裁定）：
        #   更新:(目标属性)(新值)——目标属性值类型必须 == 值的类型（例：所屬據點=对象:据点 == 町::松江=对象:据点）
        param = '(目标)(值)——双括号：更新:(人物::X.所屬據點)(町::松江)；🔴 两侧值类型必须一致（同型约束）'
        sem = '状态写入（事件完成/旗标/数值/归属/状态）'
    rows.append([k, v, cat, '—', side, typ, sem, param, impl])         # 备注 = 实现归属（含 🔴/❌ 状态，label 自含）

# ── 函数区：只收 TK5 调用词翻译行（外交同盟→isAllied…）──
# mod DSL 函数 token（atWar/isAllied/…）权威 = 16.md §三 函数注册表（2026-08-27 用户裁定：CSV 太阁原词列只收 TK5 词）
CALL_SEM = {
    'isAllied': 'a 与 b 同盟（数值：!=0 即同盟）', 'relation': '势力间外交关系数值', 'isNeighbor': 'a 与 b 相邻',
    'allControlled': '区域全部据点由 clan 控制', 'hasCard': '是否持有技能卡',
    'canMove': '角色能否前往该据点', 'canAttack': '角色能否攻击该据点',
    'region_attr_1': '国属性位 1（参数=家族；返回布尔，具体语义待数据包）', 'unknown_2': '未知属性位 2（解析碎片，待 07 数据包）',
    'unknown_8': '未知属性位 8（解析碎片，待 07 数据包）',
}
# 🔴 v3（2026-08-27 用户裁定）：函数 = 带返回值的函数——所属域列 = 语料调用方域并集（全城壓制→國、
#   卡持有→人物、外交同盟→大名家/商家…）；值类型列 = 返回值类型（数字/布尔）
call_doms = {}
for (d, a) in calls:
    if a in CALL_MAP:
        call_doms.setdefault(a, set()).add(d)
for call_word, (pred, params, ret) in CALL_MAP.items():
    doms = ' / '.join(sorted(call_doms.get(call_word, [])))
    rows.append([call_word, '—', '函数', doms, pred, ret, CALL_SEM.get(pred, pred), ', '.join(params), '注册表加行（函数引擎）'])

# ── 例句列：词条 → TK5 事件原句示范（首次出现行截断；🔴 2026-08-27 用户裁定，给人检查用）──
# 🔴 域值行例句 key 必须带域（事件標誌::95 vs 日數計數器::95 同值不同域，纯值 key 会串例句）
EXAMPLE_LEN = 60
example = {}
terms = set()
for r in rows:
    if r[0] == '—':
        continue
    terms.add((r[2], r[3], r[0]) if r[2] == '域值' else (r[2], r[0]))
for _line in txt.splitlines():
    s = _line.strip()
    if not s or s.startswith('#'):
        continue        # 🔴 跳过注释/说明行（# 文件内事件标志引用… 含 事件:: 字样会污染例句，2026-08-27 用户裁定）
    m = re.match(r'^([一-鿿぀-ヿＡ-Ｚａ-ｚA-Za-z]{2,8}):', s)
    if m and (('命令', m.group(1)) in terms or ('语法', m.group(1)) in terms):
        key = ('命令', m.group(1)) if ('命令', m.group(1)) in terms else ('语法', m.group(1))
        example.setdefault(key, s)
    for dm in re.finditer(r'([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,6})::([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ0-9０-９.]{1,16})', s):
        dom, rest = dm.group(1), dm.group(2)
        if rest.endswith('.'):
            continue
        if '.' in rest:
            attr = rest.split('.')[-1]
            if ('属性', attr) in terms:
                example.setdefault(('属性', attr), s)
            # 🔴 数字主体.数字属性（主命屬性::5288.80）——域值词条 = 末尾数字（2026-08-27 用户裁定消灭无例句行）
            if re.fullmatch(r'[0-9０-９]+\.[0-9０-９]+', rest):
                tail = rest.split('.')[-1]
                if ('域值', dom, tail) in terms:
                    example.setdefault(('域值', dom, tail), s)
        else:
            if ('域值', dom, rest) in terms:
                example.setdefault(('域值', dom, rest), s)
            if ('域', dom) in terms:
                example.setdefault(('域', dom), s)
    for cm in re.finditer(r'\.([一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]+)\(', s):
        if ('函数', cm.group(1)) in terms:
            example.setdefault(('函数', cm.group(1)), s)
for r in rows:
    key = (r[2], r[3], r[0]) if r[2] == '域值' else (r[2], r[0])
    ex = example.get(key, '')
    if not ex:
        # 🔴 宽松兜底：无例句词条 → 语料含词条名的任意**非注释**行（evm 只在文件头注释出现等，2026-08-27 用户裁定消灭无例句）
        for _line in txt.splitlines():
            if r[0] in _line and not _line.strip().startswith('#'):
                ex = _line.strip()
                break
        if not ex:
            # 词条只出现在注释/文件名（evm 解析碎片）→ 取其出处（去 # 前缀，如实标注来源）
            for _line in txt.splitlines():
                if r[0] in _line:
                    ex = _line.strip().lstrip('#').strip()
                    break
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
    # 侧名合法性断言：DSL token 只收 ASCII（防「角色引用」式中文侧名）；只查 属性/域值/函数 行
    #（命令行侧名是描述性 label，翻译器另有动作映射，不在此列）
    side_errors = [r[4] for r in rows if r[2] in ('属性', '域值', '函数') and not side_ok(r[4])]
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
        # 🔴 行序（2026-08-27 用户裁定）：类别 → 所属域 → 太阁原词 三级排序
        CAT_ORDER = {'域': 0, '属性': 1, '域值': 2, '命令': 3, '语法': 4, '函数': 5}
        rows.sort(key=lambda r: (CAT_ORDER.get(r[2], 9), r[3], r[0]))
        # 🔴 列序（2026-08-27 用户裁定）：类别第一列、所属域第二列、太阁原词第三列、例句第四列、
        #    频率最后一列；所属域第二列 = 排序分区第二级（属性按 大名家/城/人物… 分组、域值按域分组）
        # 内部 rows 保持 [原词, 频率, 类别, 所属域, 侧名, 值类型, 语义, 参数, 备注, 例句] → 写文件重排
        ORDER = [2, 3, 0, 9, 4, 5, 6, 7, 8, 1]
        w.writerow(['类别', '所属域', '太阁原词', '例句', '我们侧名', '值类型', '语义', '参数', '备注', '频率'])
        for r in rows:
            w.writerow([r[i] for i in ORDER])
    n_attr = len([r for r in rows if r[2] == '属性'])
    n_val = len([r for r in rows if r[2] == '域值'])
    print(f'✅ CSV 生成完成：{len(rows)} 行（域 {len(domains)} + 属性 {n_attr} + 域值 {n_val} + 命令 {len(cmds)} + 函数 {len(CALL_MAP)}）')
    print('  全语料覆盖自检通过：属性(域,属性)、域值(域::值)、带参调用、命令 全部可解析')


if __name__ == "__main__":
    main()
