# -*- coding: utf-8 -*-
"""patch9：CSV 侧同域同词合并成一行 + 唯一性断言（配合 patch8 的词汇表单一来源）

patch8 让 身份/真偽/人物類別/物品類型 只有一套侧名了，但 CSV 仍会出两行：
一行来自「域值」（语料里 `身份::城主` 形态），一行来自「枚举值」（命令参数位上的裸 `城主`）。
同一个词两行、两个频率，读者不知道该看哪个。

修法：
  ① 枚举值行发现同域同词已有域值行 → 不再另起一行，把频率并进那行（域值形态 + 参数位形态 = 该词总出现次数）；
  ② 落盘前断言：同 (所属域, 太阁原词) 只能一行 —— 以后再出重复直接生成失败。
"""
import ast
import io
import sys

sys.stdout.reconfigure(encoding='utf-8')
P = 'plans/scenario-campaign-mode/tools/build_registry_csv.py'

OLD = """    for _tok in sorted(_toks):
        _side = enum_side(_set, _tok)
        if _side is None:
            continue               # 理论上不会发生（生成期自检已断言），保守跳过
        rows.append([_tok, ENUM_FREQ.get(_tok, 0), '枚举值', _set, _side, _typ, _sem, '—', _note])
"""
NEW = """    for _tok in sorted(_toks):
        _side = enum_side(_set, _tok)
        if _side is None:
            continue               # 理论上不会发生（生成期自检已断言），保守跳过
        # 🔴 2026-08-27 用户抓包：同域同词禁止两行 —— `身份::元締`（域值形态）与命令参数位裸
        #   `元締`（枚举形态）是同一个词，旧版各出一行、各带一个频率，读者无从判断。
        #   已有域值行 → 频率并进去（域值形态次数 + 参数位次数 = 该词总出现次数），不另起行。
        _dup = _VAL_ROW.get((_set, _tok))
        if _dup is not None:
            _dup[1] += ENUM_FREQ.get(_tok, 0)
            continue
        rows.append([_tok, ENUM_FREQ.get(_tok, 0), '枚举值', _set, _side, _typ, _sem, '—', _note])
"""

INDEX = """# 🔴 域值行索引：(所属域, 原词) → 行（供枚举值行合并频率，见下方「枚举值行」）
_VAL_ROW = {(r[3], r[0]): r for r in rows if r[2] == '域值'}

"""

UNIQ = '''
    # 🔴 唯一性断言（2026-08-27 用户抓包：身份 一词两行）：同 (所属域, 太阁原词) 只能一行
    _seen = {}
    _dups = []
    for r in rows:
        k = (r[3], r[0])
        if k in _seen and {_seen[k][2], r[2]} <= {'域值', '枚举值', '属性'}:
            _dups.append('%s::%s（%s + %s）' % (r[3], r[0], _seen[k][2], r[2]))
        _seen[k] = r
    assert not _dups, '同域同词重复行 %d 条：%s' % (len(_dups), ' / '.join(_dups[:10]))
'''


def main():
    src = io.open(P, encoding='utf-8').read()
    if '_VAL_ROW' in src:
        print('已打过补丁，跳过')
        return

    anchor = '# ── 枚举值行 ──'
    assert anchor in src, '锚点缺失：枚举值行'
    src = src.replace(anchor, INDEX + anchor, 1)

    assert OLD in src, '锚点缺失：枚举值 rows.append'
    src = src.replace(OLD, NEW, 1)

    a2 = "    side_errors = [r[4] for r in rows"
    assert a2 in src, '锚点缺失：侧名断言'
    src = src.replace(a2, UNIQ.lstrip('\n') + a2, 1)

    ast.parse(src)
    io.open(P, 'w', encoding='utf-8').write(src)
    print('✅ patch9 已应用（枚举值行并入域值行 + 唯一性断言）')


if __name__ == '__main__':
    main()
