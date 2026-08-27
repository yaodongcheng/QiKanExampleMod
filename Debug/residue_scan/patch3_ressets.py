# -*- coding: utf-8 -*-
"""patch3：抽取资源型枚举集的 token 清单，作为字面量写进 gen_registry_tables.py。

资源集（BGM/SE/CG/背景/设施/模板 NPC/触发名）= 数据包资源清单，必须逐条登记；
不登记就变成「万能接收器」（任何 token 都能被规则生成侧名），自检形同虚设。
"""
import io
import re
import sys
from collections import Counter

sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, 'plans/scenario-campaign-mode/tools')
import gen_registry_tables as G  # noqa: E402

P = 'plans/scenario-campaign-mode/tools/gen_registry_tables.py'
ANCHOR = "RES_PREFIX = {"
JA = r'々〆ヶ・·一-鿿㐀-䶿぀-ヿＡ-Ｚａ-ｚA-Za-z0-9０-９_'
RE_HEAD = re.compile(r'^([' + JA + r']{1,12}):')
txt = io.open('Knowledge/太阁事件包/TK5AllEvents_merged.txt', encoding='utf-8').read()


def split_groups(rest):
    """命令冒号后的部分 → (头值, [参数…])；参数按顶层逗号切，保留原文（含全角括号内容）。"""
    args, i, n, head = [], 0, len(rest), ''
    m = re.match(r'^([^(\[/]+)', rest)
    if m and not rest.startswith('('):
        head = m.group(1).strip()
        i = m.end()
    while i < n:
        if rest.startswith('[[', i):
            e = rest.find(']]', i)
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
            args.extend(x.strip() for x in rest[i + 1:j - 1].split(','))
            i = j
            continue
        i += 1
    return head, args


by_pos = {}
heads = Counter()
for line in txt.splitlines():
    s = line.strip()
    if not s or s.startswith('#') or s.startswith('}'):
        continue
    m = RE_HEAD.match(s)
    if not m:
        continue
    cmd = m.group(1)
    head, args = split_groups(s[m.end():])
    if head:
        heads[(cmd, head)] += 1
    for k, a in enumerate(args):
        if a:
            by_pos.setdefault((cmd, k), Counter())[a] += 1


def grab(*keys):
    c = Counter()
    for k in keys:
        c.update(by_pos.get(k, {}))
    return c


def is_plain(tok):
    """排除：数字 / 槽 / 特殊值 / 域引用 / 已是具名实体 / 已登记语义枚举。"""
    if not tok or '::' in tok or re.fullmatch(r'-?[0-9０-９]+', tok):
        return False
    if tok in G.SPECIAL_VALS or G.entity_side(tok):
        return False
    if re.match(r'^([' + JA + r']{1,8}?)([Ａ-Ｅ])$', tok) or re.fullmatch(r'[ａ-ｚ]', tok):
        return False
    if G.RE_UNKNOWN_N.match(tok):
        return False
    for st in ('身份', '軍團槽', '軍團指令', '圖片類型', '背景類型', '狀態值', '真偽',
               '出現狀態', '物品種類', '生存狀態', '難度'):
        if tok in G.ENUM_SETS[st]:
            return False
    return True


SETS = {
    'ＢＧＭ': grab(('ＢＧＭ變更', 0)),
    'ＳＥ': grab(('ＳＥ開始', 0), ('ＳＥ循環', 0), ('ＳＥ停止', 0)),
    '事件ＣＧ': grab(('圖片表示', 1)),
    '背景': grab(('背景變更', 1)),
    '設施': grab(('進入設施', 0), ('下個場面', 0), ('發生契機', 1)),
    '模板NPC': grab(('對話', 0), ('對話', 1), ('變名對話', 0), ('變名對話', 1),
                  ('對話選擇', 0), ('對話選擇', 1), ('對話可否選擇', 0), ('主人公分歧', 0),
                  ('迷你遊戲', 1), *[('個人戰鬥', i) for i in range(2, 8)]),
    '觸發': Counter({h: c for (cmd, h), c in heads.items() if cmd == '發生契機'}),
}

lines = ['RES_SETS = {   # 🔴 资源型枚举 token 清单（数据包资源；不列清单 = 万能接收器，自检失效）']
for name, c in SETS.items():
    toks = sorted(t for t in c if is_plain(t))
    lines.append("    '%s': {" % name)
    buf = ''
    for t in toks:
        piece = "'%s', " % t
        if len(buf) + len(piece) > 88:
            lines.append('        ' + buf.rstrip())
            buf = ''
        buf += piece
    if buf:
        lines.append('        ' + buf.rstrip().rstrip(','))
    lines.append('    },')
    print('%-8s %d 种' % (name, len(toks)))
lines.append('}')
lines.append('')

src = io.open(P, encoding='utf-8').read()
if 'RES_SETS = {' in src:
    print('已打过补丁，跳过')
    sys.exit(0)
assert ANCHOR in src
src = src.replace(ANCHOR, '\n'.join(lines) + '\n' + ANCHOR, 1)

# enum_side：资源集必须查清单（表外 = None → 生成期报错）
old = """    if setname in RES_PREFIX:
        return res_side(setname, tok)"""
new = """    if setname in RES_PREFIX:
        return res_side(setname, tok) if tok in RES_SETS.get(setname, ()) else None"""
assert old in src
src = src.replace(old, new, 1)
io.open(P, 'w', encoding='utf-8').write(src)
print('✅ 插入 RES_SETS + enum_side 收紧')
