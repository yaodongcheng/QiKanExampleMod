# -*- coding: utf-8 -*-
"""patch6：把「枚举值 / 资源名 / 文本变量 / 命令参数签名」写进 16a 翻译总表。

在此之前 CSV 只有 域/属性/域值/命令/语法/函数 六类——命令**裸参数位**上的词条
（身份枚举、BGM/SE/CG/背景/设施名、触发名、军团槽、决斗场地…）一条都没进表，
台词插值变量（{一人稱}/{年}）也没有。这批词条正是翻译器要查落点的大头。

本补丁给 build_registry_csv.py 加：
  ① 类别「枚举值」—— ENUM_SETS（语义枚举）+ RES_SETS（数据包资源名）逐 token 一行；
  ② 类别「文本变量」—— TEXT_VARS（{一人稱}）+ TEXT_FIELDS（{人物Ａ.姓}）逐条一行；
  ③ 命令/语法行「参数」列填入参数位签名（pos0=域名, pos1=属性名 …）；
  ④ 例句：单趟扫语料按参数位取首现行（不走 O(行数×词条) 兜底，否则跑不动）。
"""
import io
import ast
import sys

sys.stdout.reconfigure(encoding='utf-8')
P = 'plans/scenario-campaign-mode/tools/build_registry_csv.py'

IMPORT_OLD = "from gen_registry_tables import (DOMAIN_MAP, ATTR_MAP, CMD_MAP, FUNC_SIDE_NOARG,"
IMPORT_NEW = ("from gen_registry_tables import (ENUM_SETS, RES_SETS, RES_PREFIX, enum_side, arg_spec,\n"
              "                                 TEXT_VARS, TEXT_FIELDS, CMD_ARG_SPEC, CMD_ARG_PREFIX,\n"
              "                                 arg_positions, head_vals, talk_interps,\n"
              "                                 DOMAIN_MAP, ATTR_MAP, CMD_MAP, FUNC_SIDE_NOARG,")

# ── 插在「例句列」段之前：枚举值 / 文本变量 行 + 命令参数签名回填 ──
BLOCK = '''
# ═══════════════════════════════════════════════════════════════════════════
# 🔴 v4（2026-08-27）：命令参数位词条进表 —— 枚举值 / 资源名 / 文本变量 / 参数签名
#   翻译器查落点时，「對話:(上忍,主人公)」的 上忍、「ＢＧＭ變更:(合戰)」的 合戰
#   都要能在总表查到侧名；旧版这批词条一条都没有（只有 域::X 形态进表）。
# ═══════════════════════════════════════════════════════════════════════════

# 词条频率：语料里该 token 出现在命令参数位/头值的次数
ENUM_FREQ = Counter()
for (_c, _p), _toks in arg_positions.items():
    for _t, (_n, _sib) in _toks.items():
        ENUM_FREQ[_t] += _n
for (_c, _h), _n in head_vals.items():
    ENUM_FREQ[_h] += _n

# 枚举值例句：单趟扫语料，按「命令:(参数,参数)」取每个 token 的首现行
ENUM_TOKENS = set()
for _s, _m in ENUM_SETS.items():
    ENUM_TOKENS |= set(_m)
for _s, _m in RES_SETS.items():
    ENUM_TOKENS |= set(_m)
ENUM_TOKENS |= set(TEXT_VARS) | set(TEXT_FIELDS)

ENUM_EX = {}
for _line in txt.splitlines():
    _s = _line.strip()
    if not _s or _s.startswith('#') or ':' not in _s:
        continue
    for _piece in re.split(r'[():,\uff0c\[\]{}<>|\uff5c]', _s):   # 全角括号不切
        _piece = _piece.strip()
        if _piece in ENUM_TOKENS and _piece not in ENUM_EX:
            ENUM_EX[_piece] = _s

# 首趟按分隔符切分会漏两类：含半角括号的资源名（雪(メイン)）、插值字段（{人物Ａ.名前} 的 名前）
# -> 对剩余 token 做一趟子串兜底（数量个位数，一遍扫完即止）
_missing = {t for t in ENUM_TOKENS if t not in ENUM_EX}
if _missing:
    for _line in txt.splitlines():
        _s = _line.strip()
        if not _s or _s.startswith('#'):
            continue
        for _t in tuple(_missing):
            if _t in _s:
                ENUM_EX[_t] = _s
                _missing.discard(_t)
        if not _missing:
            break

# ── 枚举值行 ──
ENUM_KIND = {                      # 集名 → (值类型, 备注)
    **{k: ('资源', '🔴 数据包资源（05 演出/场景/角色模板）') for k in RES_PREFIX},
    '觸發': ('资源', '🔴 事件触发名（01 调度器 trigger 表）'),
}
for _set in sorted(set(ENUM_SETS) | set(RES_SETS)):
    _toks = ENUM_SETS.get(_set) or {t: None for t in RES_SETS.get(_set, ())}
    _typ, _note = ENUM_KIND.get(_set, ('枚举', '✅ 枚举字面量'))
    _sem = ('%s 资源名' % _set) if _typ == '资源' else ('%s 枚举值' % _set)
    for _tok in sorted(_toks):
        _side = enum_side(_set, _tok)
        if _side is None:
            continue               # 理论上不会发生（生成期自检已断言），保守跳过
        rows.append([_tok, ENUM_FREQ.get(_tok, 0), '枚举值', _set, _side, _typ, _sem, '—', _note])

# ── 文本变量行（台词插值 {一人稱} / {人物Ａ.姓}）──
for _v, _side in sorted(TEXT_VARS.items()):
    rows.append([_v, talk_interps.get(_v, 0), '文本变量', '插值变量', _side, '文本',
                 '台词插值变量（05 lines 渲染期替换）', '{%s}' % _v, '✅ 文本渲染'])
for _f, _side in sorted(TEXT_FIELDS.items()):
    rows.append([_f, sum(_n for _i, _n in talk_interps.items() if _i.endswith('.' + _f)),
                 '文本变量', '插值字段', 'Text%s' % _side, '文本',
                 '台词插值字段（主体.字段 → 取对象字段渲染）', '{主体.%s}' % _f, '✅ 文本渲染'])

# ── 命令/语法行「参数」列：回填参数位签名 ──
_KIND_CN = {'E': '具名实体', 'D': '域名', 'A': '属性名',
            'VA': '值（取值空间由属性参决定）', '*': '头值'}


def _kind_cn(k):
    if k in _KIND_CN:
        return _KIND_CN[k]
    if k.startswith('域:'):
        return '%s 域值' % k[2:]
    return '%s 枚举' % k


def _sig_of(cmd):
    spec = CMD_ARG_SPEC.get(cmd)
    if spec is None:
        spec = next((s for pre, s in CMD_ARG_PREFIX.items() if cmd.startswith(pre)), None)
    if not spec:
        return None
    out = []
    if '*' in spec:
        out.append('头值=%s' % '/'.join(_kind_cn(k) for k in spec['*']))
    for p in sorted(k for k in spec if k != '*'):
        out.append('pos%d=%s' % (p, '/'.join(_kind_cn(k) for k in spec[p])))
    return ', '.join(out)


for _r in rows:
    if _r[2] in ('命令', '语法') and _r[7] == '—':
        _sig = _sig_of(_r[0])
        if _sig:
            _r[7] = _sig

'''


def main():
    src = io.open(P, encoding='utf-8').read()
    if 'ENUM_FREQ' in src:
        print('已打过补丁，跳过')
        return

    assert IMPORT_OLD in src, '锚点缺失：import'
    src = src.replace(IMPORT_OLD, IMPORT_NEW, 1)

    anchor = '# ── 例句列：词条 → TK5 事件原句示范'
    assert anchor in src, '锚点缺失：例句段'
    src = src.replace(anchor, BLOCK.lstrip('\n') + anchor, 1)

    # 例句 key：枚举值/文本变量 与 域值 同样带「所属域」维度（真 ∈ 真偽 也 ∈ 狀態值）
    old_key = "    terms.add((r[2], r[3], r[0]) if r[2] == '域值' else (r[2], r[0]))"
    assert old_key in src
    src = src.replace(old_key,
                      "    if r[2] in ('枚举值', '文本变量'):\n"
                      "        continue          # 例句已由 ENUM_EX 单趟扫描给出，不进兜底（O(行×词条) 跑不动）\n"
                      "    terms.add((r[2], r[3], r[0]) if r[2] == '域值' else (r[2], r[0]))", 1)

    old_fill = """for r in rows:
    key = (r[2], r[3], r[0]) if r[2] == '域值' else (r[2], r[0])
    ex = example.get(key, '')"""
    assert old_fill in src
    src = src.replace(old_fill, """for r in rows:
    if r[2] in ('枚举值', '文本变量'):
        ex = ENUM_EX.get(r[0], '')
        r.append(ex if len(ex) <= EXAMPLE_LEN else ex[:EXAMPLE_LEN] + '…')
        continue
    key = (r[2], r[3], r[0]) if r[2] == '域值' else (r[2], r[0])
    ex = example.get(key, '')""", 1)

    # 侧名合法性断言覆盖新类别（枚举侧名允许数字字面量：零值 Ｚｅｒｏ→0）
    old_se = "    side_errors = [r[4] for r in rows if r[2] in ('属性', '域值', '函数') and not side_ok(r[4])]"
    assert old_se in src
    src = src.replace(old_se,
                      "    side_errors = [r[4] for r in rows\n"
                      "                   if r[2] in ('属性', '域值', '函数', '枚举值', '文本变量')\n"
                      "                   and not (side_ok(r[4]) or re.fullmatch(r'-?[0-9]+', r[4]))]", 1)

    old_cat = "        CAT_ORDER = {'域': 0, '属性': 1, '域值': 2, '命令': 3, '语法': 4, '函数': 5}"
    assert old_cat in src
    src = src.replace(old_cat,
                      "        CAT_ORDER = {'域': 0, '属性': 1, '域值': 2, '枚举值': 3, '文本变量': 4,\n"
                      "                     '命令': 5, '语法': 6, '函数': 7}", 1)

    old_print = ("    print(f'✅ CSV 生成完成：{len(rows)} 行（域 {len(domains)} + 属性 {n_attr} + "
                 "域值 {n_val} + 命令 {len(cmds)} + 函数 {len(CALL_MAP)}）')")
    assert old_print in src, '锚点缺失：完成打印'
    src = src.replace(old_print,
                      "    n_enum = len([r for r in rows if r[2] == '枚举值'])\n"
                      "    n_text = len([r for r in rows if r[2] == '文本变量'])\n" + old_print[:-2] +
                      " + 枚举值 {n_enum} + 文本变量 {n_text}）')", 1)

    ast.parse(src)
    io.open(P, 'w', encoding='utf-8').write(src)
    print('✅ patch6 已应用（枚举值 + 文本变量 + 参数签名 + 例句/排序/自检适配）')


if __name__ == '__main__':
    main()
