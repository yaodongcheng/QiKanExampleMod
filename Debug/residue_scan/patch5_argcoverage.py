# -*- coding: utf-8 -*-
"""patch5：把「命令参数位 / 命令头值 / 台词插值」接进**生成期自检**。

在此之前：这三类形态只有残渣扫描器（registry_residue_scan.py）在查，生成器 verify_coverage()
查不到——意味着往语料里加一条新命令参数，`build_registry_csv.py` 照样生成成功，
表外词条要等人另跑扫描器才发现。铁律 22 的「生成期自检」在这三类上是空的。

本补丁给生成器加：
  ① 结构化语料解析（命令头 / 头值 / 参数位 / 台词插值），产出三个 Counter；
  ② verify_coverage() 三条新断言（表外 = exit(1)，与属性/域值/函数/命令同级）；
  ③ 成為御用商人 第 2 参签名补登记（原来靠翻译器名字表兜底，自检不该依赖名字表）。
"""
import io
import re
import sys

sys.stdout.reconfigure(encoding='utf-8')
P = 'plans/scenario-campaign-mode/tools/gen_registry_tables.py'

BLOCK = '''
# ═══════════════════════════════════════════════════════════════════════════
# 🔴 v4（2026-08-27）：结构化语料解析 —— 命令头值 / 参数位 / 台词插值 全量抽取，
#     供 verify_coverage() 断言（表外 = 生成失败）。旧版只解析 `域::X` 形态，
#     命令裸参数位（枚举/资源名/容器字段/触发名）与台词插值不在自检范围内。
# ═══════════════════════════════════════════════════════════════════════════
_JA = r'々〆ヶ・·一-鿿㐀-䶿぀-ヿＡ-Ｚａ-ｚA-Za-z0-9０-９_'
RE_LINE_HEAD = re.compile(r'^([' + _JA + r']{1,12}):')
RE_TOKENISH = re.compile('[' + _JA + ']+')
RE_NUMLIT = re.compile(r'-?[0-9]+(?:\\.[0-9]+)?')
RE_SLOTLIKE = re.compile(r'^([' + _JA + r']{1,8}?)([Ａ-Ｅ])$')
RE_VARSLOT = re.compile(r'^[ａ-ｚ]$')
RE_EVENTID = re.compile(r'^(?:事件)?[A-Z]{1,3}[0-9A-F]{4,8}_[0-9]+$')
RE_HEXSEQ = re.compile(r'^(?:[0-9A-F]{2}\\s*)+$')
RE_INTERP = re.compile(r'\\{([^{}]*)\\}|<([^<>]*)>|\\(([^()]*)\\)')
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
                if a and not is_structural(a):
                    slot = argpos.setdefault((cmd, pos), {}).setdefault(a, [0, allargs])
                    slot[0] += 1
                pos += 1
    return heads, argpos, interps


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
    if RE_UNKNOWN_N.match(inner) or RE_NUMLIT.fullmatch(inner) or is_slotlike(inner) \\
            or inner in SPECIAL_VALS:
        return 'Text::raw'
    return entity_side(inner)


def head_val_side(cmd, tok):
    """命令头值（屬性:一次｜弱 / 發生契機:據點畫面表示後）→ 侧名；表外 None。"""
    if is_structural(tok):
        return 'Literal'
    return arg_side(cmd, '*', tok) or entity_side(tok)


'''

ASSERT = '''    for (c_, h_), n_ in head_vals.items():
        if head_val_side(c_, h_) is None:
            errors.append(f'命令头值表外: {c_}:{h_} ×{n_}')
    for (c_, p_), toks in arg_positions.items():
        for t_, (n_, sib_) in toks.items():
            if arg_side(c_, p_, t_, sib_) is None:
                errors.append(f'命令参数位表外: {c_}[pos{p_}]:{t_} ×{n_}')
    for i_, n_ in talk_interps.items():
        if interp_side(i_) is None:
            errors.append(f'台词插值表外: {{{i_}}} ×{n_}')
    return errors'''


def main():
    src = io.open(P, encoding='utf-8').read()
    if '_parse_command_lines' in src:
        print('已打过补丁，跳过')
        return

    # ① 成為御用商人 第 2 参（原靠翻译器名字表兜底；自检不依赖名字表）
    old = "    '成為御用商人': {0: ('E',)},"
    assert old in src, '锚点缺失：成為御用商人'
    src = src.replace(old, "    '成為御用商人': {0: ('E',), 1: ('E',)},", 1)

    # ② 结构化解析段插在 verify_coverage 之前
    anchor = '# ═══ 生成期自检：全语料覆盖断言（表外 = 生成失败）═══'
    assert anchor in src, '锚点缺失：自检段'
    src = src.replace(anchor, BLOCK.lstrip('\n') + anchor, 1)

    # ③ verify_coverage 三条新断言
    old_ret = """        if k not in CMD_MAP and cmd_rule(k) == '🔴 低频 → 降级/忽略':
            errors.append(f'命令表外: {k} ×{c}')
    return errors"""
    assert old_ret in src, '锚点缺失：verify_coverage 结尾'
    src = src.replace(old_ret, old_ret[:-len('    return errors')] + ASSERT, 1)

    io.open(P, 'w', encoding='utf-8').write(src)
    import ast
    ast.parse(src)
    print('✅ patch5 已应用（结构化解析 + 三条自检断言 + 成為御用商人 签名）')


if __name__ == '__main__':
    main()
