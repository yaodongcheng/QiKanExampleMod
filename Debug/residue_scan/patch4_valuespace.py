# -*- coding: utf-8 -*-
"""patch4：补齐参数位模型的四块缺口（改生成器，不改产物 — 铁律 22）

① 属性取值空间 ATTR_VALUE_SPACE —— 容器篩選/排除/設定 的第三参 = 「第二参那个属性的取值」，
   不建模就只能瞎猜（紀伊 到底是国名还是枚举？）。
② 具名据点补录 —— 大垣城/岡崎之町 这类只在**参数位**出现、从不写成 城::大垣城 的据点名。
③ 容器字段属性 —— 人物番號/城番號/人口/物品種類 只作容器字段出现，属性表里没有。
④ 资源集重建 —— 事件ＣＧ「上洛」被误当状态值排掉了；ＳＥ 带括号名（雪(メイン)）要整参收录。
"""
import io
import re
import sys
from collections import Counter

sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, 'plans/scenario-campaign-mode/tools')
import gen_registry_tables as G  # noqa: E402

P = 'plans/scenario-campaign-mode/tools/gen_registry_tables.py'
JA = r'々〆ヶ・·一-鿿㐀-䶿぀-ヿＡ-Ｚａ-ｚA-Za-z0-9０-９_'
RE_HEAD = re.compile(r'^([' + JA + r']{1,12}):')
txt = io.open('Knowledge/太阁事件包/TK5AllEvents_merged.txt', encoding='utf-8').read()


def split_groups(rest):
    args, i, n, head = [], 0, len(rest), ''
    m = re.match(r'^([^(\[/]+)', rest)
    if m and not rest.startswith('('):
        head, i = m.group(1).strip(), m.end()
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
            inner, buf, d = rest[i + 1:j - 1], '', 0
            for ch in inner:
                if ch == '(':
                    d += 1
                elif ch == ')':
                    d -= 1
                if ch == ',' and d == 0:
                    args.append(buf.strip())
                    buf = ''
                else:
                    buf += ch
            args.append(buf.strip())
            i = j
            continue
        i += 1
    return head, args


by_pos, heads = {}, Counter()
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


RE_SLOT = re.compile(r'^([' + JA + r']{1,8}?)([Ａ-Ｅ])$')


def is_slot(tok):
    m = RE_SLOT.match(tok)
    return bool(m and m.group(1) in G.SLOT_CAT)


def structural(tok):
    """数字/槽/特殊值/域引用/解析碎片 —— 不是词条，跳过。"""
    return (not tok or '::' in tok or re.fullmatch(r'-?[0-9０-９]+', tok)
            or tok in G.SPECIAL_VALS or is_slot(tok) or re.fullmatch(r'[ａ-ｚ]', tok)
            or G.RE_UNKNOWN_N.match(tok))


# ═══ ① 具名据点/人物补录：CMD_ARG_SPEC 里声明为实体位（'E'）、但 entity_side 认不出的 token ═══
ent_pos = [(c, p) for c, sp in G.CMD_ARG_SPEC.items() for p, ks in sp.items()
           if 'E' in ks and p != '*']
missing = Counter()
for key in ent_pos:
    for tok, n in by_pos.get(key, {}).items():
        if not structural(tok) and not G.entity_side(tok):
            missing[tok] += n
SUF = (('城', 'Settlement'), ('之町', 'Settlement'), ('之里', 'Settlement'), ('之村', 'Settlement'))
extra_ent, leftover = {}, []
for tok in sorted(missing):
    pre = next((v for s, v in SUF if tok.endswith(s)), None)
    if pre:
        extra_ent[tok] = pre
    else:
        leftover.append(tok)
print('② 补录具名实体 %d 个；非实体残留 %d 个：%s' % (len(extra_ent), len(leftover), ' '.join(leftover)))

# ═══ ② 资源集重建：只排除结构性 token 与已登记的**位类型枚举**，不排除状态值 ═══
POS_ENUMS = ('圖片類型', '背景類型', '轉場', '軍團槽', '軍團指令', '難度', '真偽')
RES_SPEC = {
    'ＢＧＭ': [('ＢＧＭ變更', 0)],
    'ＳＥ': [('ＳＥ開始', 0), ('ＳＥ循環', 0), ('ＳＥ停止', 0)],
    '事件ＣＧ': [('圖片表示', 1)],
    '背景': [('背景變更', 1)],
    '設施': [('進入設施', 0), ('下個場面', 0), ('發生契機', 1)],
    '模板NPC': [('對話', 0), ('對話', 1), ('變名對話', 0), ('變名對話', 1), ('對話選擇', 0),
              ('對話選擇', 1), ('對話可否選擇', 0), ('主人公分歧', 0), ('迷你遊戲', 1)]
    + [('個人戰鬥', i) for i in range(2, 8)],
}
res_sets = {}
for name, keys in RES_SPEC.items():
    toks = set()
    for tok in grab(*keys):
        if structural(tok) or G.entity_side(tok) or tok in extra_ent:
            continue
        if any(tok in G.ENUM_SETS[e] for e in POS_ENUMS):
            continue
        if name == '模板NPC' and (tok in G.ENUM_SETS['身份'] or G.val_side('身份', tok)):
            continue           # 對話:(上忍,主人公) 的 上忍 = 身份，不是模板 NPC
        toks.add(tok)
    res_sets[name] = sorted(toks)
res_sets['觸發'] = sorted({h for (c, h), _ in heads.items() if c == '發生契機'})
for k, v in res_sets.items():
    print('   %-8s %d 种' % (k, len(v)))


def fmt_set(name, toks, ind='    '):
    out = ["%s'%s': {" % (ind, name)]
    buf = ''
    for t in toks:
        piece = "'%s', " % t
        if len(buf) + len(piece) > 86:
            out.append(ind + '    ' + buf.rstrip())
            buf = ''
        buf += piece
    if buf:
        out.append(ind + '    ' + buf.rstrip().rstrip(','))
    out.append(ind + '},')
    return out


# ═══ 生成新代码块 ═══
lines = ['RES_SETS = {   # 🔴 资源型枚举 token 清单（数据包资源；不列清单 = 万能接收器，自检失效）']
for name in ('ＢＧＭ', 'ＳＥ', '事件ＣＧ', '背景', '設施', '模板NPC', '觸發'):
    lines += fmt_set(name, res_sets[name])
lines.append('}')
NEW_RES = '\n'.join(lines)

ENT_BLOCK = ['EXTRA_SETTLEMENT_NAMES = {   # 只在命令参数位出现、从不写成 城::X 的具名据点']
buf = ''
for t in sorted(extra_ent):
    piece = "'%s', " % t
    if len(buf) + len(piece) > 86:
        ENT_BLOCK.append('    ' + buf.rstrip())
        buf = ''
    buf += piece
if buf:
    ENT_BLOCK.append('    ' + buf.rstrip().rstrip(','))
ENT_BLOCK.append('}')

BLOCK = '\n'.join(ENT_BLOCK) + '''
for _t in EXTRA_SETTLEMENT_NAMES:
    EXTRA_ENTITY_NAMES.setdefault(_t, 'Settlement')

# ── 属性取值空间：「容器篩選:(城,所屬國,紀伊)」第三参收什么，由第二参那个属性决定 ──
#    '域:X' = 该值是 X 域的成员（走 val_side）；'枚:X' = 该值是枚举集 X 的 token
ATTR_VALUE_SPACE = {
    '所屬國': '域:國', '所在地方': '域:地方', '所屬據點': '域:據點', '本據': '域:據點',
    '本城': '域:城', '所屬大名家': '域:大名家', '所屬當主': '域:人物', '所屬上司': '域:人物',
    '城主': '域:人物', '當主': '域:人物', '所有者': '域:人物', '妻': '域:人物',
    '所屬勢力': '域:勢力', '所屬海賊衆': '域:海賊衆', '所屬忍者衆': '域:忍者衆',
    '類別': '域:人物類別', '身份': '域:身份', '官位': '域:官位', '官職': '域:官職',
    '戰略': '域:戰略', '戰略目標': '域:戰略', '立場': '域:立場', '承擔主命': '域:主命',
    '物品種類': '枚:物品種類', '武器種類': '枚:武器種類', '性別': '枚:性別',
    '出現標誌': '枚:出現狀態', '死亡標誌': '枚:生存狀態', '武將': '枚:真偽',
}

# ── 容器字段属性：只作「容器篩選/排序/排除的字段名」出现，没有 域::主体.属性 形态 ──
ATTR_EXTRA = {
    '人口': 'Settlement.population', '物品種類': 'Item.category', '武器種類': 'Item.weapon_class',
    '類別': 'Hero.category', '石高': 'Settlement.income', '商業': 'Settlement.trade',
}
RE_INDEX_ATTR = re.compile(r'^[^0-9]{1,6}番號$')       # 人物番號/城番號/物品番號… = 对象序号


def attr_side_any(tok):
    """属性名（不带域）→ 侧名。容器字段/序号字段走补充表与规则。"""
    if tok in ATTR_EXTRA:
        return ATTR_EXTRA[tok]
    if RE_INDEX_ATTR.match(tok):
        return '%s.index' % ENTITY_DOMAINS.get(tok[:-2], 'Object')
    return next((pair_side(d, tok) for d in PREFIX_BY_DOMAIN if pair_side(d, tok)), None)


def value_space_side(attr, tok):
    """按属性的取值空间解释一个值（容器三参式）。"""
    sp = ATTR_VALUE_SPACE.get(attr)
    if not sp:
        return None
    kind, name = sp.split(':', 1)
    return val_side(name, tok) if kind == '域' else enum_side(name, tok)
'''

src = io.open(P, encoding='utf-8').read()
if 'ATTR_VALUE_SPACE' in src:
    print('已打过补丁，跳过')
    sys.exit(0)

# 1) 替换 RES_SETS 整块
m = re.search(r'RES_SETS = \{.*?\n\}\n', src, re.S)
assert m, 'RES_SETS 块未找到'
src = src[:m.start()] + NEW_RES + '\n' + src[m.end():]

# 2) 追加实体补录 + 属性取值空间（插在 CMD_ARG_SPEC 之前）
anchor = '# ═══ 命令参数位签名'
if anchor not in src:
    anchor = 'CMD_ARG_SPEC = {'
assert anchor in src
src = src.replace(anchor, BLOCK + '\n\n' + anchor, 1)

# 3) 参数位签名修正
FIX = [
    ("'容器篩選': {0: ('D',), 1: ('A',), 2: ('E', '真偽', '狀態值', '身份', '物品種類',\n"
     "                                        '武器種類', '生存狀態', '軍團槽', '人物類別')},",
     "'容器篩選': {0: ('D',), 1: ('A',), 2: ('VA', 'E', '真偽', '狀態值', '身份', '物品種類',\n"
     "                                        '武器種類', '生存狀態', '軍團槽', '人物類別')},"),
    ("'容器排除': {0: ('D',), 1: ('A',), 2: ('E', '身份', '真偽', '狀態值', '人物類別', '物品種類')},",
     "'容器排除': {0: ('D',), 1: ('A',), 2: ('VA', 'E', '身份', '真偽', '狀態值', '人物類別', '物品種類')},"),
    ("'容器設定': {0: ('D',), 1: ('A',), 2: ('E', '真偽', '人物類別', '物品種類', '狀態值')},",
     "'容器設定': {0: ('D',), 1: ('A',), 2: ('VA', 'E', '真偽', '人物類別', '物品種類', '狀態值')},"),
    ("'對話': {0: ('E', '模板NPC'), 1: ('E', '模板NPC')},",
     "'對話': {0: ('E', '模板NPC', '域:身份'), 1: ('E', '模板NPC', '域:身份')},"),
    ("'變名對話': {0: ('E', '模板NPC'), 1: ('E', '模板NPC')},",
     "'變名對話': {0: ('E', '模板NPC', '域:身份'), 1: ('E', '模板NPC', '域:身份')},"),
    ("'對話選擇': {0: ('E', '模板NPC'), 1: ('E', '模板NPC')},",
     "'對話選擇': {0: ('E', '模板NPC', '域:身份'), 1: ('E', '模板NPC', '域:身份')},"),
    ("'對話可否選擇': {0: ('E', '模板NPC'), 1: ('E', '模板NPC')},",
     "'對話可否選擇': {0: ('E', '模板NPC', '域:身份'), 1: ('E', '模板NPC', '域:身份')},"),
    ("    '零值': {'Ｚｅｒｏ': '0'},",
     "    '零值': {'Ｚｅｒｏ': '0'},\n"
     "    '通關方式': {'統一（完全）': 'unify_full', '統一（通常）': 'unify_normal',\n"
     "                '輔佐統一天下': 'assist_unify'},"),
    ("'發生契機': {'*': ('觸發',), 0: ('E', '設施', '軍團槽', '生存狀態', '身份', '觸發'),\n"
     "                1: ('E', '設施'), 2: ('軍團指令', 'E'), 3: ('E', '軍團槽')},",
     "'發生契機': {'*': ('觸發',), 0: ('E', '設施', '軍團槽', '生存狀態', '身份', '觸發', '通關方式'),\n"
     "                1: ('E', '設施', 'D'), 2: ('軍團指令', 'E'), 3: ('E', '軍團槽')},"),
    ("'主命作成': {0: ('E',), 1: ('E',)},",
     "'主命作成': {0: ('E',), 1: ('E',), 2: ('域:主命',)},"),
    ("'事件主命作成': {0: ('E',)},",
     "'事件主命作成': {0: ('E',), 1: ('域:主命',), 2: ('域:主命',)},"),
    ("    '軍團指令': {'據點移動': 'move_to', '軍團攻擊': 'attack_party', '據點攻擊': 'siege',\n"
     "                '歸還': 'return_home', '終結': 'disband', '平局': 'draw'},",
     "    '軍團指令': {'據點移動': 'move_to', '軍團攻擊': 'attack_party', '據點攻擊': 'siege',\n"
     "                '歸還': 'return_home', '終結': 'disband', '平局': 'draw',\n"
     "                '統一（完全）': 'unify_full', '統一（通常）': 'unify_normal'},"),
]
for old, new in FIX:
    assert old in src, '签名锚点缺失: %s' % old[:30]
    src = src.replace(old, new, 1)

# 4) arg_side 支持 'VA'（按 args[1] 属性取值空间）/'域:X'（定域值）/'A'（补充属性表）
old_argside = """def arg_side(cmd, pos, tok):
    \"\"\"命令裸参数 → 侧名。表外返回 None（生成期报错）。\"\"\"
    kinds = arg_spec(cmd, pos)
    for k in kinds:
        if k == 'E':
            s = entity_side(tok)
        elif k == 'D':
            s = DOMAIN_MAP.get(tok) and ('Domain::' + tok)
        elif k == 'A':
            s = next((pair_side(d, tok) for d in PREFIX_BY_DOMAIN if pair_side(d, tok)), None)
        else:
            s = enum_side(k, tok)
        if s:
            return s
    return None"""
new_argside = """def arg_side(cmd, pos, tok, args=None):
    \"\"\"命令裸参数 → 侧名。表外返回 None（生成期报错）。

    args = 同一条命令的全部参数（有的位要看兄弟参数才知道收什么：
           容器篩選:(城,所屬國,紀伊) 第三参的取值空间由第二参属性 所屬國 决定）。
    \"\"\"
    for k in arg_spec(cmd, pos):
        if k == 'E':
            s = entity_side(tok)
        elif k == 'D':
            s = DOMAIN_MAP.get(tok) and ('Domain::' + tok)
        elif k == 'A':
            s = attr_side_any(tok)
        elif k == 'VA':
            s = value_space_side(args[1], tok) if args and len(args) > 1 else None
        elif k.startswith('域:'):
            s = val_side(k[2:], tok)
        else:
            s = enum_side(k, tok)
        if s:
            return s
    return None"""
assert old_argside in src
src = src.replace(old_argside, new_argside, 1)

io.open(P, 'w', encoding='utf-8').write(src)
print('✅ patch4 已应用')
