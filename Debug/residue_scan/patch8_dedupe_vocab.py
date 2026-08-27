# -*- coding: utf-8 -*-
"""patch8：词汇表单一来源 —— 消灭「一套词两处定义」（2026-08-27 用户抓包）

症状：CSV 里 `枚举值|身份|元締` 与 `域值|身份|元締` 同时存在，20 组同域同词双行；
其中 10 组两个侧名不一样（元締 overseer/boss、城主 city_lord/castle_lord …），
更糟的是 `ashigaru_captain` 跨表指两个不同品级（域值=足輕大將 / 枚举=足輕組頭），
`chief` 在域值表里同时指 頭 和 頭領（表内塌缩）。身份是**带序**枚举，token 撞车 = 品级链坏。

根因：同一套词汇被写了两遍 —— DOMAIN_VAL_MAP['身份'] 30 条 + ENUM_SETS['身份'] 19 条，
两张表各自演化，没有任何断言要求它们一致。

修法（三条，全在生成器）：
  ① 集名 == 域名 的枚举集（身份/人物類別/真偽/物品類型）不再手写，一律从 DOMAIN_VAL_MAP 派生；
     枚举侧独有的 token（町人/最高位/事件人物）回填进 DOMAIN_VAL_MAP，冲突一律以域值表为准。
  ② 頭 拆出 head（原与 頭領 同为 chief）；物品類型 武器/茶器 登记（原走 hash 兜底）。
  ③ 生成期新断言：每个词汇表内 token→侧名必须单射；派生集必须与域值表逐条一致。
"""
import ast
import io
import sys

sys.stdout.reconfigure(encoding='utf-8')
P = 'plans/scenario-campaign-mode/tools/gen_registry_tables.py'

OLD_HEAD = ("    ('身份', '茶人'): 'tea_master', ('身份', '姑娘'): 'girl', ('身份', '頭'): 'chief',")
NEW_HEAD = (
    "    ('身份', '茶人'): 'tea_master', ('身份', '姑娘'): 'girl',\n"
    "    # 🔴 2026-08-27：頭 原与 頭領 同为 chief（表内塌缩，两个不同品级共用 token → 品级链坏）\n"
    "    ('身份', '頭'): 'head',\n"
    "    # 🔴 2026-08-27：原 ENUM_SETS['身份'] 独有的三个 token 回填（词汇表单一来源）\n"
    "    ('身份', '町人'): 'townsman', ('身份', '最高位'): 'top_rank',\n"
    "    ('身份', '事件人物'): 'event_person',       # 与 人物類別::事件人物 同侧名（同一概念）\n"
    "    # 🔴 2026-08-27：物品類型域值原走 hash 兜底（ItemType::tk5_u44a3d9），\n"
    "    #   与 枚举 物品種類 是同一套词 → 对齐 物品種類 侧名\n"
    "    ('物品類型', '武器'): 'weapon', ('物品類型', '茶器'): 'tea_ware',")

OLD_ENUM = """    '真偽': {'真': 'true', '偽': 'false'},
    '其他分支': {'其他': 'else'},
    '身份': {'大名': 'daimyo', '城主': 'castle_lord', '國主': 'province_lord', '家老': 'elder',
            '侍大將': 'samurai_general', '足輕大將': 'ashigaru_general', '足輕組頭': 'ashigaru_captain',
            '頭領': 'chief', '頭': 'head', '上忍': 'jonin', '元締': 'boss', '支配人': 'manager',
            '大老闆': 'big_merchant', '船大將': 'fleet_captain', '町人': 'townsman', '浪人': 'ronin',
            '師範代': 'instructor', '最高位': 'top_rank', '事件人物': 'event_hero'},
    '人物類別': {'泛用對手': 'generic_rival'},
"""
NEW_ENUM = """    '其他分支': {'其他': 'else'},
    # 🔴 真偽 / 身份 / 人物類別 / 物品類型 不在此手写——见下方 DERIVED_ENUM_SETS（词汇表单一来源）
"""

DERIVE = '''
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

'''

ASSERT = '''    # 🔴 词汇表内 token→侧名单射（2026-08-27）：两个不同的词共用一个侧名 = 语义塌缩
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
    return errors'''


def main():
    src = io.open(P, encoding='utf-8').read()
    if 'DERIVED_ENUM_SETS' in src:
        print('已打过补丁，跳过')
        return

    assert OLD_HEAD in src, '锚点缺失：身份域值尾行'
    src = src.replace(OLD_HEAD, NEW_HEAD, 1)

    assert OLD_ENUM in src, '锚点缺失：ENUM_SETS 身份/人物類別/真偽'
    src = src.replace(OLD_ENUM, NEW_ENUM, 1)

    anchor = "\ndef enum_side(setname, tok):"
    assert anchor in src, '锚点缺失：enum_side'
    src = src.replace(anchor, '\n' + DERIVE + anchor, 1)

    old_ret = """    for i_, n_ in talk_interps.items():
        if interp_side(i_) is None:
            errors.append(f'台词插值表外: {{{i_}}} ×{n_}')
    return errors"""
    assert old_ret in src, '锚点缺失：verify_coverage 结尾'
    src = src.replace(old_ret, old_ret[:-len('    return errors')] + ASSERT, 1)

    ast.parse(src)
    io.open(P, 'w', encoding='utf-8').write(src)
    print('✅ patch8 已应用（词汇表单一来源 + 頭/head 拆分 + 物品類型登记 + 两条新断言）')


if __name__ == '__main__':
    main()
