# -*- coding: utf-8 -*-
"""
fill_registry.py —— 把 16b《落点裁定表》正文里的裁定表读出来，生成 tools/registry_verdicts.py。

用法：
    python tools/fill_registry.py            # 生成 + 跑六道自检
    python tools/fill_registry.py --check     # 只跑自检，不写文件

铁律 22：registry_verdicts.py 是生成物，禁止手改；要改裁定去改 16b 正文表。
"""
from __future__ import unicode_literals
import io
import os
import re
import sys
import codecs

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
P_16B = os.path.join(ROOT, '16b-落点裁定表.md')
P_16A = os.path.join(ROOT, '16a-DSL翻译总表.csv')
P_11 = os.path.join(ROOT, '11-存档与配置.md')
P_OUT = os.path.join(HERE, 'registry_verdicts.py')

TIERS = ('T0', 'T1', 'T2', 'T3', 'T3-预留')
CARRIERS = ('引擎', '外置仓', '旗标仓', '计数器仓', '数据包', 'Ctx', 'Variable', 'GlobalSlot',
            '13主命', '17功勋', '02战略', '03战斗', '05演出', '无')
SAVEKEYS = ('lwn_scn_attr', 'lwn_scn_state', '13', '17', '无')
# 需要落盘的载体：出现这些载体时，存档键不许是「无」
PERSIST_CARRIERS = ('外置仓', '旗标仓', '计数器仓', 'Variable', 'GlobalSlot', '13主命', '17功勋')
VALTYPES = ('布尔', '数字', '字符串', '空', '—')

ROW_COLS = ('太阁原词', '侧名', '档', '值类型', '载体', '读实现', '写实现', '存档键', '降级', '待核实')
GRP_COLS = ('匹配（类别/域/前缀）', '档', '值类型', '载体', '侧名规则', '存档键', '降级', '说明')

ERRORS = []
WARNS = []


def err(msg):
    ERRORS.append(msg)


def warn(msg):
    WARNS.append(msg)


# --------------------------------------------------------------------------
# 1. 解析 16b
# --------------------------------------------------------------------------
def _cells(line):
    s = line.strip()
    if s.startswith('|'):
        s = s[1:]
    if s.endswith('|'):
        s = s[:-1]
    # 表格里 `\|` 是转义的竖线，先换成占位符再切
    s = s.replace('\\|', '\x00')
    return [c.strip().replace('\x00', '|') for c in s.split('|')]


def parse_16b(path):
    lines = io.open(path, encoding='utf-8').read().split('\n')
    rows, groups = [], []
    i = 0
    while i < len(lines):
        m = re.match(r'^<!--\s*verdict:(row|group)(?:\s+类别=(\S+))?\s*-->\s*$', lines[i])
        if not m:
            i += 1
            continue
        kind, cat = m.group(1), m.group(2)
        j = i + 1
        while j < len(lines) and not lines[j].lstrip().startswith('|'):
            j += 1
        if j >= len(lines):
            err('第 %d 行的 verdict 标记后面没有表格' % (i + 1))
            i = j
            continue
        header = _cells(lines[j])
        j += 1
        if j < len(lines) and set(lines[j].replace('|', '').replace('-', '').replace(':', '').strip()) <= set(' '):
            j += 1
        need = ROW_COLS if kind == 'row' else GRP_COLS
        idx = {}
        for c in need:
            if c not in header:
                err('第 %d 行表头缺列「%s」（现有：%s）' % (j, c, ' / '.join(header)))
            else:
                idx[c] = header.index(c)
        while j < len(lines) and lines[j].lstrip().startswith('|'):
            cs = _cells(lines[j])
            rec = dict((c, cs[idx[c]] if c in idx and idx[c] < len(cs) else '') for c in need)
            rec['_line'] = j + 1
            if kind == 'row':
                rec['类别'] = cat or ''
                if rec['太阁原词']:
                    rows.append(rec)
            else:
                if rec[GRP_COLS[0]]:
                    groups.append(rec)
            j += 1
        i = j
    return rows, groups


# --------------------------------------------------------------------------
# 2. 匹配式
# --------------------------------------------------------------------------
class Matcher(object):
    def __init__(self, expr, line):
        self.expr = expr
        self.line = line
        parts = expr.split('/')
        self.cat = parts[0].strip()
        self.tests = []
        for p in parts[1:]:
            p = p.strip()
            if not p:
                continue
            mm = re.match(r'^(域|词|侧名)(\^=|∈|~=|=)(.*)$', p)
            if not mm:
                err('第 %d 行匹配式看不懂：%s' % (line, p))
                continue
            field, op, arg = mm.group(1), mm.group(2), mm.group(3).strip()
            if op == '∈':
                arg = set(x.strip() for x in arg.strip('{}').split('，') if x.strip())
            elif op == '~=':
                arg = re.compile(arg)
            self.tests.append((field, op, arg))

    def _val(self, field, r):
        return {'域': r['所属域'], '词': r['太阁原词'], '侧名': r['我们侧名']}[field]

    def match(self, r):
        if r['类别'] != self.cat:
            return False
        for field, op, arg in self.tests:
            v = self._val(field, r)
            if op == '=' and v != arg:
                return False
            if op == '^=' and not v.startswith(arg):
                return False
            if op == '∈' and v not in arg:
                return False
            if op == '~=' and not arg.search(v):
                return False
        return True


# --------------------------------------------------------------------------
# 3. 语料
# --------------------------------------------------------------------------
def load_corpus(path):
    import csv
    if sys.version_info[0] >= 3:
        f = io.open(path, encoding='utf-8-sig', newline='')
        return list(csv.DictReader(f))
    f = open(path, 'rb')
    raw = f.read().decode('utf-8-sig').encode('utf-8')
    import StringIO
    rd = csv.DictReader(StringIO.StringIO(raw))
    return [dict((k.decode('utf-8'), (v or b'').decode('utf-8')) for k, v in r.items()) for r in rd]


# --------------------------------------------------------------------------
# 4. 裁定解析
# --------------------------------------------------------------------------
def resolve(corpus, rows, groups):
    """给 16a 的每一行配一条裁定。

    键是（类别, 所属域, 太阁原词）三元组——同一个词在不同域里是不同的东西
    （`24` 在「事件標誌」域是旗标 24、在「日數計數器」域是计数器 24），
    只用（类别, 词）会把 128 组这样的词压成一条，裁定就串了。
    行裁定只覆盖 属性/命令/函数/域 四类（这四类没有跨域重名），所以按（类别, 词）查即可。
    """
    by_word = {}
    for r in rows:
        key = (r['类别'], r['太阁原词'])
        if key in by_word:
            err('行裁定重复：%s / %s（第 %d、%d 行）'
                % (key[0], key[1], by_word[key]['_line'], r['_line']))
        by_word[key] = r
    matchers = [(Matcher(g[GRP_COLS[0]], g['_line']), g) for g in groups]

    out = {}
    unclaimed = []
    used_rows, used_groups = set(), set()
    for c in corpus:
        key = (c['类别'], c['所属域'], c['太阁原词'])
        r = by_word.get((c['类别'], c['太阁原词']))
        if r is not None:
            used_rows.add((c['类别'], c['太阁原词']))
            out[key] = dict(
                源='row', 行=r['_line'],
                侧名=r['侧名'] or c['我们侧名'], 档=r['档'], 值类型=r['值类型'],
                载体=r['载体'], 存档键=r['存档键'], 降级=r['降级'], 待核实=r['待核实'],
                实现锚点=' / '.join(x for x in (r['读实现'], r['写实现']) if x and x != '—') or '—')
            continue
        hit = None
        for mt, g in matchers:
            if mt.match(c):
                hit = (mt, g)
                break
        if hit is None:
            unclaimed.append(c)
            continue
        mt, g = hit
        used_groups.add(g['_line'])
        out[key] = dict(
            源='group', 行=g['_line'], 匹配=g[GRP_COLS[0]],
            侧名=c['我们侧名'], 档=g['档'], 值类型=g['值类型'],
            载体=g['载体'], 存档键=g['存档键'], 降级=g['降级'], 待核实='',
            实现锚点=g['侧名规则'] or '—')
    return out, unclaimed, used_rows, used_groups


# --------------------------------------------------------------------------
# 5. 六道自检
# --------------------------------------------------------------------------
ASCII_OK = re.compile(r'^[\x20-\x7e]+$')


def type_compatible(declared, derived):
    """16b 声明的值类型，和语料词法推导出来的类型，能不能对上。

    语料的类型比 16 §四 更细（`枚举:官職` / `对象:人物` / `数字/对象`），
    所以「相容」= 相等，或者语料是声明类型的更细的说法。
    """
    if not derived or derived == '—':
        return True
    if declared == derived:
        return True
    if declared == '数字':
        return derived.startswith('数字')
    if declared == '字符串':
        return (derived.startswith('枚举:') or derived.startswith('对象:')
                or derived in ('字符串', '文本', '枚举', '资源'))
    if declared == '布尔':
        return derived == '布尔'
    if declared == '空':
        return derived in ('空', '域')
    if declared.startswith('枚举:'):
        return derived.startswith('枚举')
    if declared.startswith('对象:'):
        return derived.startswith('对象')
    return False


def selfcheck(corpus, rows, groups, verdicts, unclaimed, used_rows, used_groups):
    # ① 正向扣除：每一行语料都必须被认领
    if unclaimed:
        err('自检①（正向扣除）未认领 %d 行，前 20 行：' % len(unclaimed))
        for c in unclaimed[:20]:
            ERRORS.append('      %s / %s / %s / %s'
                          % (c['类别'], c['所属域'], c['太阁原词'], c['我们侧名']))

    # ② 反向扣除：每条行裁定的词必须真的在语料里
    corpus_keys = set((c['类别'], c['太阁原词']) for c in corpus)
    for r in rows:
        if (r['类别'], r['太阁原词']) not in corpus_keys:
            err('自检②（反向扣除）16b 第 %d 行「%s / %s」语料里查无此词'
                % (r['_line'], r['类别'], r['太阁原词']))
    for g in groups:
        if g['_line'] not in used_groups:
            warn('自检②：16b 第 %d 行组裁定「%s」一条都没命中（被前面的规则吃掉了？）'
                 % (g['_line'], g[GRP_COLS[0]]))

    # ③ 完备性
    for key, v in sorted(verdicts.items()):
        tag = '%s / %s / %s' % key
        if v['档'] not in TIERS:
            err('自检③ %s：档「%s」不在 %s' % (tag, v['档'], '/'.join(TIERS)))
        for c in [x.strip() for x in v['载体'].split('/')]:
            if c and c not in CARRIERS:
                err('自检③ %s：载体「%s」不在词表' % (tag, c))
        if v['存档键'] and v['存档键'] not in SAVEKEYS:
            err('自检③ %s：存档键「%s」不在词表' % (tag, v['存档键']))
        carriers = [x.strip() for x in v['载体'].split('/')]
        if v['档'].startswith('T3'):
            if not v['载体'] or v['载体'] == '无':
                err('自检③ %s：T3 必须写载体' % tag)
            elif any(c in PERSIST_CARRIERS for c in carriers) and v['存档键'] in ('', '无'):
                err('自检③ %s：载体 %s 要落盘，存档键不能是「无」' % (tag, v['载体']))
        if v['档'] == 'T0' and not v['降级']:
            err('自检③ %s：T0 必须写降级形态' % tag)
        if v['档'] == 'T3-预留' and v['降级'] != '空执行':
            err('自检③ %s：T3-预留 的降级必须写「空执行」，现在是「%s」' % (tag, v['降级']))
        if v['档'] == 'T1' and v['待核实']:
            err('自检③ %s：待核实非空（%s）就不许判 T1' % (tag, v['待核实']))

    # ④ 侧名合法：ASCII（允许游戏 StringId 里的长音符/连字符）+ 同域不塌缩
    # 断言范围：本表逐行给出侧名的行裁定，以及类别 ∈ {属性, 域, 函数}（这三类侧名就是 DSL token）。
    # 命令 / 语法 被组裁定认领时，侧名由各自注册表定义（05 演出步骤名 / 01 语法节点），不在本断言范围。
    TOKEN_CATS = ('属性', '域', '函数')
    idref = re.compile(r'^[A-Za-z0-9_\-]+$')
    for key, v in sorted(verdicts.items()):
        if v['源'] != 'row' and key[0] not in TOKEN_CATS:
            continue
        for seg in [s.strip() for s in v['侧名'].split('/')]:
            if not seg or seg == '—':
                continue
            if ASCII_OK.match(seg) or idref.match(seg):
                continue
            if all(ord(ch) < 0x300 or ch in 'āīūēōĀĪŪĒŌ' for ch in seg):
                continue
            err('自检④ %s / %s / %s：侧名「%s」不是纯 ASCII' % (key[0], key[1], key[2], seg))
    seen = {}
    for c in corpus:
        if c['类别'] != '属性':
            continue
        k = (c['所属域'], c['我们侧名'])
        seen.setdefault(k, []).append(c['太阁原词'])
    for k, ws in sorted(seen.items()):
        if len(ws) > 1:
            err('自检④ 侧名塌缩：域 %s 的「%s」被 %d 个原词共用：%s'
                % (k[0], k[1], len(ws), ' / '.join(ws)))

    # ⑤ 值类型合法 + 与语料词法推导相容
    #    16b 填「—」= 沿用 gen_registry_tables.py 的词法推导（§9.4 边界，绝大多数如此）；
    #    填了具体类型 = 一条断言：语料推出来的类型必须与它相容，否则两边分叉，报错。
    corpus_type = dict(((c['类别'], c['所属域'], c['太阁原词']), c['值类型']) for c in corpus)
    for key, v in sorted(verdicts.items()):
        t = v['值类型']
        if not t or t == '—':
            continue
        if not (t in VALTYPES or t.startswith('枚举:') or t.startswith('对象:')):
            err('自检⑤ %s / %s / %s：值类型「%s」不在 16 §四 体系' % (key[0], key[1], key[2], t))
            continue
        ct = corpus_type.get(key, '')
        if not type_compatible(t, ct):
            err('自检⑤ %s / %s / %s：16b 说是「%s」，语料推导是「%s」，两边对不上'
                % (key[0], key[1], key[2], t, ct))

    # ⑥ 存档键闭环：本表用到的每个存档键，都要在 11 的登记表里写清「存在哪」和「怎么清」
    used_keys = set(v['存档键'] for v in verdicts.values()) - set(['', '无'])
    reg = parse_savekeys(P_11)
    if reg is None:
        return
    for k in sorted(used_keys):
        if k not in reg:
            err('自检⑥：存档键「%s」没在 11-存档与配置.md 的 `<!-- savekeys -->` 登记表里' % k)
            continue
        if not reg[k][0]:
            err('自检⑥：存档键「%s」在 11 的登记表里没写「存在哪（注册点）」' % k)
        if not reg[k][1]:
            err('自检⑥：存档键「%s」在 11 的登记表里没写「新档怎么清」' % k)
    for k in sorted(set(reg) - used_keys):
        warn('自检⑥：11 登记了存档键「%s」，但 16b 的裁定表里一条都没用到' % k)


SK_STORE = '存在哪（注册点）'
SK_CLEAR = '新档怎么清'


def parse_savekeys(path):
    """读 11-存档与配置.md 的 `<!-- savekeys -->` 表，返回 {键: (存在哪, 怎么清)}。"""
    if not os.path.exists(path):
        err('自检⑥：找不到 11-存档与配置.md')
        return None
    lines = io.open(path, encoding='utf-8').read().split('\n')
    i = 0
    while i < len(lines) and not re.match(r'^<!--\s*savekeys\s*-->\s*$', lines[i]):
        i += 1
    if i >= len(lines):
        err('自检⑥：11-存档与配置.md 里没有 `<!-- savekeys -->` 存档键登记表')
        return None
    while i < len(lines) and not lines[i].lstrip().startswith('|'):
        i += 1
    header = _cells(lines[i])
    for c in ('存档键', SK_STORE, SK_CLEAR):
        if c not in header:
            err('自检⑥：11 的登记表缺列「%s」（现有：%s）' % (c, ' / '.join(header)))
            return None
    ik, isv, ic = header.index('存档键'), header.index(SK_STORE), header.index(SK_CLEAR)
    i += 2  # 跳过表头分隔行
    out = {}
    while i < len(lines) and lines[i].lstrip().startswith('|'):
        cs = _cells(lines[i])
        if len(cs) > max(ik, isv, ic):
            k = cs[ik].strip().strip('`')
            if k:
                out[k] = (cs[isv].strip(), cs[ic].strip())
        i += 1
    return out


# --------------------------------------------------------------------------
# 6. 生成
# --------------------------------------------------------------------------
HEAD = '''# -*- coding: utf-8 -*-
# 🔴 自动生成，勿手改。由 tools/fill_registry.py 从 16b-落点裁定表.md 正文表生成。
# 要改裁定 → 改 16b 正文表 → 重跑 `python tools/fill_registry.py`（铁律 22）。
from __future__ import unicode_literals

'''


def emit(verdicts):
    def lit(s):
        return "'" + s.replace('\\', '\\\\').replace("'", "\\'") + "'"

    buf = [HEAD, 'VERDICTS = {\n']
    for key in sorted(verdicts):
        v = verdicts[key]
        buf.append('    (%s, %s, %s): {' % (lit(key[0]), lit(key[1]), lit(key[2])))
        buf.append(', '.join('%s: %s' % (lit(k), lit(v[k])) for k in
                             ('侧名', '档', '值类型', '载体', '存档键', '降级', '待核实', '实现锚点')))
        buf.append('},\n')
    buf.append('}\n\n')
    buf.append('''
def verdict(category, domain, word):
    """按（类别, 所属域, 太阁原词）取裁定；查不到返回 None。"""
    return VERDICTS.get((category, domain, word))
''')
    io.open(P_OUT, 'w', encoding='utf-8').write(''.join(buf))


def main():
    check_only = '--check' in sys.argv
    rows, groups = parse_16b(P_16B)
    corpus = load_corpus(P_16A)
    verdicts, unclaimed, ur, ug = resolve(corpus, rows, groups)
    selfcheck(corpus, rows, groups, verdicts, unclaimed, ur, ug)

    out = codecs.getwriter('utf-8')(sys.stdout.buffer) if sys.version_info[0] >= 3 else sys.stdout
    print('16b：行裁定 %d 条，组裁定 %d 条' % (len(rows), len(groups)))
    print('16a：语料 %d 行，认领 %d 行，未认领 %d 行' % (len(corpus), len(verdicts), len(unclaimed)))
    if len(verdicts) + len(unclaimed) != len(corpus):
        print('ERROR 认领数 + 未认领数 != 语料行数（键塌缩了）')
        return 1
    import collections
    print('分档：' + '  '.join('%s=%d' % kv for kv in
                             sorted(collections.Counter(v['档'] for v in verdicts.values()).items())))
    for w in WARNS:
        print('WARN  ' + w)
    if ERRORS:
        for e in ERRORS:
            print('ERROR ' + e)
        print('---- 自检不通过：%d 条错误 ----' % len(ERRORS))
        return 1
    if not check_only:
        emit(verdicts)
        print('已生成 %s' % os.path.relpath(P_OUT, ROOT))
    print('---- 六道自检全过 ----')
    return 0


if __name__ == '__main__':
    sys.exit(main())
