# -*- coding: utf-8 -*-
"""T3D 解析工具箱：把 UE 的 T3D 文本反成结构化数据 / 可读伪代码。

用法：
  python t3d_tools.py graph <file.t3d> [图名]      # 蓝图：按 exec 连线线性化成伪代码
  python t3d_tools.py props <file.t3d> [层数]      # 通用：打印对象树与属性
  python t3d_tools.py graphs <file.t3d>            # 蓝图：只列图名 + 节点统计
"""
import re, sys, io, os, json, collections

BEGIN = re.compile(r'^Begin Object\s+(?:Class=(\S+)\s+)?Name="([^"]*)"')
END = re.compile(r'^End Object')


def split_top(s):
    """按顶层逗号切分（尊重引号与括号嵌套）。"""
    out, buf, depth, q = [], [], 0, False
    i = 0
    while i < len(s):
        c = s[i]
        if q:
            buf.append(c)
            if c == '\\':
                if i + 1 < len(s):
                    buf.append(s[i + 1]); i += 2; continue
            elif c == '"':
                q = False
        else:
            if c == '"':
                q = True; buf.append(c)
            elif c in '([':
                depth += 1; buf.append(c)
            elif c in ')]':
                depth -= 1; buf.append(c)
            elif c == ',' and depth == 0:
                out.append(''.join(buf)); buf = []
            else:
                buf.append(c)
        i += 1
    if buf:
        out.append(''.join(buf))
    return out


def parse_pin(body):
    """CustomProperties Pin (...) / UserDefinedPin (...) → dict（值去掉外层引号）"""
    d = {}
    for part in split_top(body):
        if '=' not in part:
            continue
        k, v = part.split('=', 1)
        v = v.strip()
        if len(v) >= 2 and v[0] == '"' and v[-1] == '"':
            v = v[1:-1]
        d[k.strip()] = v
    return d


def parse_t3d(path):
    """返回 (root, scopes)
    scopes: 名称表 → {名字: entry}，其中「图作用域」的键为 ('graph', 图名)，
            顶层作用域为 ('root',)。
    注意：T3D 里 K2Node 的名字是**按 EdGraph 局部**的（同名节点在包内重复出现），
    所以必须按子树作用域合并，不能全局合并。"""
    root = {'cls': None, 'name': '__root__', 'props': [], 'pins': [], 'children': [], 'parent': None}
    stack = [root]
    graph_names = set()
    with io.open(path, encoding='utf-8', errors='replace') as f:
        for line in f:
            s = line.rstrip('\n').rstrip()
            t = s.strip()
            if not t:
                continue
            m = BEGIN.match(t)
            if m:
                cls, nm = m.group(1), m.group(2)
                is_graph = bool(cls and cls.endswith('.EdGraph')) or (cls is None and nm in graph_names)
                if cls and cls.endswith('.EdGraph'):
                    graph_names.add(nm)
                node = {'cls': cls, 'name': nm, 'props': [], 'pins': [],
                        'children': [], 'parent': stack[-1], 'is_graph': is_graph}
                stack[-1]['children'].append(node)
                stack.append(node)
                continue
            if END.match(t):
                if len(stack) > 1:
                    stack.pop()
                continue
            cur = stack[-1]
            if t.startswith('CustomProperties '):
                mm = re.match(r'CustomProperties\s+(\w+)\s*\((.*)\)\s*$', t)
                if mm:
                    pin = parse_pin(mm.group(2))
                    pin['__kind'] = mm.group(1)
                    cur['pins'].append(pin)
                    continue
            cur['props'].append(t)

    scopes = {}

    def mergesub(sub, key):
        m = scopes.setdefault(key, {})
        def walk(n):
            if n['name'] != '__root__':
                e = m.setdefault(n['name'], {'cls': None, 'props': [], 'pins': [], 'nodes': []})
                if n['cls']:
                    e['cls'] = n['cls']
                e['props'].extend(n['props'])
                e['pins'].extend(n['pins'])
                e['nodes'].append(n)
            for c in n['children']:
                walk(c)
        walk(sub)

    mergesub(root, ('root',))
    graphs = {}

    def walk(n):
        if n.get('is_graph'):
            mergesub(n, ('graph', n['name']))
            graphs[n['name']] = n
        for c in n['children']:
            walk(c)
    for c in root['children']:
        walk(c)
    return root, scopes, graphs


def prop(node_or_entry, key, default=None):
    props = node_or_entry['props']
    for p in props:
        if p.startswith(key + '='):
            return p[len(key) + 1:]
    return default


def prop_prefix(entry, key):
    out = []
    for p in entry['props']:
        if p.startswith(key):
            out.append(p)
    return out


def member_of(entry):
    """取节点代表的成员名：函数名 / 变量名 / 属性名。"""
    for p in entry['props']:
        if p.startswith('FunctionReference='):
            mm = re.search(r'MemberName="([^"]*)"', p)
            par = re.search(r'MemberParent=Class\'"([^"]*)"\'', p) or re.search(r'MemberParent=([^,)]+)', p)
            return ('fn', mm.group(1) if mm else '?', par.group(1) if par else '')
        if p.startswith('VariableReference='):
            mm = re.search(r'MemberName="([^"]*)"', p) or re.search(r'MemberName=([^,)]+)', p)
            return ('var', mm.group(1) if mm else '?', '')
        if p.startswith('PropertyReference=') or p.startswith('StructType='):
            mm = re.search(r'MemberName="([^"]*)"', p)
            if mm:
                return ('prop', mm.group(1), '')
        if p.startswith('DelegatePropertyName='):
            return ('delegate', p.split('=', 1)[1], '')
    return (None, None, '')


def pin_summary(pin):
    return {'name': pin.get('PinName'), 'dir': pin.get('Direction', 'EGPD_Input'),
            'category': pin.get('PinType.PinCategory'),
            'sub': pin.get('PinType.PinSubCategory'),
            'obj': pin.get('PinType.PinSubCategoryObject'),
            'default': pin.get('DefaultValue'),
            'defobj': pin.get('DefaultObject'),
            'linked': pin.get('LinkedTo'),
            'friendly': pin.get('PinFriendlyName')}


def linked_nodes(linked):
    """LinkedTo=(K2Node_X GUID, K2Node_Y GUID) → [K2Node_X, K2Node_Y]"""
    if not linked:
        return []
    mm = re.findall(r'([A-Za-z0-9_]+)\s+[0-9A-F]{32}', linked)
    return mm


def literal(pin):
    if pin.get('DefaultValue') not in (None, ''):
        return pin['DefaultValue']
    if pin.get('DefaultObject'):
        return pin['DefaultObject']
    if pin.get('DefaultValue') == '':
        return None
    return None


NODE_KIND = [
    ('K2Node_CallFunction', 'call'),
    ('K2Node_VariableGet', 'get'),
    ('K2Node_VariableSet', 'set'),
    ('K2Node_IfThenElse', 'branch'),
    ('K2Node_ExecutionSequence', 'seq'),
    ('K2Node_DynamicCast', 'cast'),
    ('K2Node_MakeStruct', 'make'),
    ('K2Node_BreakStruct', 'break'),
    ('K2Node_MakeArray', 'makearray'),
    ('K2Node_MakeMap', 'makemap'),
    ('K2Node_Select', 'select'),
    ('K2Node_Knot', 'knot'),
    ('K2Node_CommutativeAssociativeBinaryOperator', 'op'),
    ('K2Node_PromotableOperator', 'op'),
    ('K2Node_CallArrayFunction', 'callarr'),
    ('K2Node_CallParentFunction', 'super'),
    ('K2Node_FunctionEntry', 'entry'),
    ('K2Node_FunctionResult', 'result'),
    ('K2Node_Event', 'event'),
    ('K2Node_CustomEvent', 'cevent'),
    ('K2Node_Timeline', 'timeline'),
    ('K2Node_MacroInstance', 'macro'),
    ('K2Node_SpawnActorFromClass', 'spawn'),
    ('K2Node_AddComponent', 'addcomp'),
    ('K2Node_GetArrayItem', 'arrget'),
    ('K2Node_AssignmentStatement', 'assign'),
    ('K2Node_TemporaryVariable', 'localvar'),
    ('K2Node_Tunnel', 'tunnel'),
    ('K2Node_Composite', 'composite'),
    ('K2Node_SetFieldsInStruct', 'setstruct'),
    ('K2Node_ForEachElementInEnum', 'forenum'),
    ('K2Node_SwitchEnum', 'switchenum'),
    ('K2Node_SwitchInteger', 'switchint'),
    ('K2Node_SwitchString', 'switchstr'),
]


def kind_of(entry):
    c = entry['cls'] or ''
    for k, v in NODE_KIND:
        if k in c:
            return v
    return c.split('.')[-1] if c else '?'


def graph_of(entry):
    """节点所属图名：向上找 EdGraph 祖先名。"""
    return None


def linearize(grp, max_steps=400):
    """对一个 EdGraph 的节点集合按 exec 连线线性化。grp = {name: entry}"""
    # 找入口：优先函数/事件节点；否则取无 exec 入边的节点
    entries = [n for n, e in grp.items() if kind_of(e) in ('entry', 'event', 'cevent')]
    if not entries:
        def has_exec_in(e):
            for p in e['pins']:
                ps = pin_summary(p)
                if ps['category'] == 'exec' and ps['dir'] == 'EGPD_Input' and ps['linked']:
                    return True
            return False
        entries = [n for n, e in grp.items() if e['pins'] and not has_exec_in(e)]
    entries.sort()
    out = []
    visited = set()

    def node_label(n):
        e = grp[n]
        k = kind_of(e)
        mk, mn, mp = member_of(e)
        label = k
        if k in ('call', 'callarr', 'super') and mn:
            label = '%s()' % mn
        elif k in ('get', 'set') and mn:
            label = '%s %s' % ('GET' if k == 'get' else 'SET', mn)
        elif k in ('make', 'break') and mn:
            label = '%s %s' % ('MAKE' if k == 'make' else 'BREAK', mn)
        elif k == 'cast':
            tgt = prop(e, 'TargetType')
            label = 'CAST %s' % (tgt.split('.')[-1].rstrip("'") if tgt else '')
        elif k == 'op':
            label = prop(e, 'OperatorName') or 'op'
        elif k == 'cevent':
            label = 'EVENT %s' % (prop(e, 'CustomFunctionName') or n)
        elif k == 'event':
            ref = prop(e, 'EventReference') or ''
            m = re.search(r'MemberName="?([A-Za-z0-9_]+)', ref)
            label = 'EVENT %s' % (m.group(1) if m else n)
        elif k == 'entry':
            fr = prop(e, 'FunctionReference') or ''
            m = re.search(r'MemberName="?([A-Za-z0-9_]+)', fr)
            label = 'FUNC %s' % (m.group(1) if m else n)
        elif k == 'macro':
            mg = prop(e, 'MacroGraphReference') or ''
            m = re.search(r'EdGraph[^:]*:([A-Za-z0-9_]+)', mg)
            label = 'MACRO %s' % (m.group(1) if m else '')
        elif k == 'assign':
            label = 'ASSIGN'
        return label

    def ins(n):
        """输入引脚上的字面量"""
        e = grp[n]
        vals = []
        for p in e['pins']:
            ps = pin_summary(p)
            if ps['dir'] != 'EGPD_Input':
                continue
            if ps['name'] in ('execute', 'self', 'then'):
                continue
            lit = literal(ps)
            if lit is not None:
                v = lit
                if ps['obj']:
                    v = '%s (%s)' % (lit, ps['obj'].split('/')[-1].rstrip("'"))
                vals.append('%s=%s' % (ps['name'], v))
        return vals

    def outs(n):
        e = grp[n]
        vals = []
        for p in e['pins']:
            ps = pin_summary(p)
            if ps['dir'] != 'EGPD_Output':
                continue
            lit = literal(ps)
            if lit is not None:
                vals.append('%s=%s' % (ps['name'], lit))
        return vals

    def step(n, depth=0):
        if n in visited or len(out) > max_steps:
            return
        visited.add(n)
        e = grp[n]
        k = kind_of(e)
        lbl = node_label(n)
        extra = ins(n)
        ex = outs(n)
        line = '%s%s' % ('  ' * depth, lbl)
        if extra:
            line += '  [in: %s]' % ', '.join(extra[:8])
        if ex:
            line += '  [out: %s]' % ', '.join(ex[:6])
        out.append(line)
        # 找 exec 后继
        succ = []
        for p in e['pins']:
            ps = pin_summary(p)
            if ps['category'] == 'exec' and ps['dir'] == 'EGPD_Output':
                for t in linked_nodes(ps['linked']):
                    if t in grp:
                        succ.append((ps['name'], t))
        if k == 'branch':
            # then / else 两支
            for name, t in succ:
                out.append('%s  -> %s:' % ('  ' * depth, name))
                step(t, depth + 2)
        else:
            for name, t in succ:
                step(t, depth)

    for en in entries:
        out.append('--- 入口: %s (%s)' % (en, grp[en]['cls']))
        step(en)
    return out


def graphs_of(scopes):
    """直接给出 {图名: {局部节点名: entry}}"""
    out = {}
    for k, v in scopes.items():
        if isinstance(k, tuple) and k[0] == 'graph':
            out[k[1]] = v
    return out


def describe(entry):
    k = kind_of(entry)
    mk, mn, mp = member_of(entry)
    s = k
    if mn:
        s += ' :: ' + (mp.split('.')[-1] + '.' if mp else '') + mn
    lits = []
    for p in entry['pins']:
        ps = pin_summary(p)
        if ps['dir'] == 'EGPD_Input' and ps['name'] not in ('execute', 'self', 'then'):
            lit = literal(ps)
            if lit is not None:
                lits.append('%s=%s' % (ps['name'], lit))
    if lits:
        s += '   {' + ', '.join(lits[:8]) + '}'
    return s



def dataflow(grp, focus=None):
    """数据流视图：每个节点的输入（字面量 / 来自哪个节点），适合读纯函数（无 exec 链）。"""
    def label(n):
        e = grp[n]
        k = kind_of(e)
        mk, mn, mp = member_of(e)
        if k in ('call', 'callarr', 'super') and mn:
            return '%s()' % mn
        if k in ('get', 'set') and mn:
            return '%s %s' % ('GET' if k == 'get' else 'SET', mn)
        if k in ('make', 'break') and mn:
            return '%s %s' % ('MAKE' if k == 'make' else 'BREAK', mn)
        if k == 'op':
            return prop(e, 'OperatorName') or 'op'
        if k == 'cast':
            return 'CAST'
        if k == 'select':
            return 'SELECT'
        if k == 'entry':
            return 'FUNC-ENTRY'
        if k == 'result':
            return 'RETURN'
        return k

    def src(linked):
        ns = linked_nodes(linked)
        return ns[0] if ns else None

    out = []
    for n in sorted(grp):
        e = grp[n]
        if not e['pins']:
            continue
        lbl = label(n)
        if focus and focus.lower() not in lbl.lower():
            continue
        ins = []
        for p in e['pins']:
            ps = pin_summary(p)
            if ps['dir'] != 'EGPD_Input' or ps['name'] in ('execute', 'self', 'then'):
                continue
            lit = literal(ps)
            if lit is not None:
                val = lit
                if ps['obj']:
                    val = '%s : %s' % (lit, ps['obj'].split('/')[-1].rstrip("'"))
                ins.append('%s=%s' % (ps['name'], val[:70]))
            elif ps['linked']:
                s0 = src(ps['linked'])
                if s0 and s0 in grp:
                    ins.append('%s<-%s' % (ps['name'], label(s0)))
                elif s0:
                    ins.append('%s<-%s' % (ps['name'], s0))
        if ins:
            out.append('%-34s %s' % (lbl[:34], ' | '.join(ins)))
    return out



def pin_index(grp):
    """{(节点名, PinId): pin} —— 用来把 LinkedTo 里的 GUID 反查成对方引脚名。"""
    idx = {}
    for n, e in grp.items():
        for p in e['pins']:
            pid = p.get('PinId')
            if pid:
                idx[(n, pid)] = p
    return idx


def short_pin(name):
    """UDS 字段引脚名带 `_序号_GUID` 尾巴，显示时剥掉。"""
    return re.sub(r'_(\d{1,3})_[0-9A-Fa-f]{32}$', '', name or '')


def _fmt_val(ps):
    v = ps.get('default')
    if v not in (None, ''):
        return ' = `%s`' % str(v)[:80]
    if ps.get('defobj'):
        return ' = `%s`' % str(ps['defobj'])[:80]
    return ''


def _links(ps, grp, idx, want_dir):
    """把某个引脚的 LinkedTo 解析成 ['节点.引脚', ...]"""
    out = []
    for m in re.finditer(r'([A-Za-z0-9_]+)\s+([0-9A-F]{32})', ps.get('linked') or ''):
        node, pid = m.group(1), m.group(2)
        other = idx.get((node, pid))
        pin_name = short_pin(other.get('PinName')) if other else '?'
        out.append('%s.%s' % (node, pin_name))
    return out


def node_pin_detail(grp, indent='  '):
    """逐节点的引脚级明细：输入来自谁、输出去向谁、字面量是什么。供详情页使用。"""
    if not grp:
        return []
    idx = pin_index(grp)
    lines = []
    for n in sorted(grp, key=lambda x: (len(x), x)):
        e = grp[n]
        if not e['pins']:
            continue
        k = kind_of(e)
        mk, mn, mp = member_of(e)
        title = k + ((' :: ' + mn) if mn else '')
        lines.append('**`%s`** — %s' % (n, title))
        ins, outs = [], []
        for p in e['pins']:
            ps = pin_summary(p)
            if ps['name'] in ('self',) and not ps.get('linked'):
                continue
            cat = ps['category'] or ''
            entry = '`%s`%s' % (short_pin(ps['name']), _fmt_val(ps))
            if ps['dir'] != 'EGPD_Output':
                src = _links(ps, grp, idx, 'in')
                ins.append('%s %s%s' % (entry, ('← ' + ', '.join('`%s`' % x for x in src)) if src else '← (未连线)',
                                        ('  [%s]' % cat) if cat else ''))
            else:
                dst = _links(ps, grp, idx, 'out')
                outs.append('%s %s%s' % (entry, ('→ ' + ', '.join('`%s`' % x for x in dst)) if dst else '→ (未连线)',
                                         ('  [%s]' % cat) if cat else ''))
        if ins:
            lines.append(indent + 'in : ' + ' | '.join(ins))
        if outs:
            lines.append(indent + 'out: ' + ' | '.join(outs))
        lines.append('')
    return lines


def main():
    mode = sys.argv[1]
    path = sys.argv[2]
    root, scopes, _graphs = parse_t3d(path)
    if mode == 'props':
        depth = int(sys.argv[3]) if len(sys.argv) > 3 else 2
        def pr(n, d=0):
            if d > depth:
                return
            if n['cls']:
                print('%s%s  %s' % ('  ' * d, n['cls'].split('.')[-1], n['name']))
            seen = set()
            for p in n['props']:
                if p in seen:
                    continue
                seen.add(p)
                print('%s   | %s' % ('  ' * d, p[:300]))
            for c in n['children']:
                pr(c, d + 1)
        for c in root['children']:
            pr(c)
    elif mode == 'graphs':
        gs = graphs_of(scopes)
        print('图数 %d' % len(gs))
        for name, grp in gs.items():
            kinds = collections.Counter(kind_of(e) for e in grp.values())
            print('== %s  (%d 节点)  %s' % (name, len(grp), dict(kinds)))
            for n, e in sorted(grp.items()):
                print('    %-40s %s' % (n, describe(e)[:180]))
    elif mode == 'graph':
        gs = graphs_of(scopes)
        only = sys.argv[3] if len(sys.argv) > 3 else None
        for name, grp in gs.items():
            if only and only.lower() not in name.lower():
                continue
            print('================ EdGraph: %s' % name)
            for line in linearize(grp):
                print(line)
    elif mode == 'dataflow':
        gs = graphs_of(scopes)
        only = sys.argv[3] if len(sys.argv) > 3 else None
        focus = sys.argv[4] if len(sys.argv) > 4 else None
        for name, grp in gs.items():
            if only and only.lower() not in name.lower():
                continue
            print('================ EdGraph: %s' % name)
            for line in dataflow(grp, focus):
                print(line)
    else:
        print('unknown mode')


if __name__ == '__main__':
    main()


# ================================================================
# T3D 底座（公共三件套）：类型表 / RapidIterationParameters 解码 / 对象树遍历
# 🔴 这是 ue-dissect 与 particle-pipeline 共用的**唯一实现**，别在别处再抄一份。
#    搬自 tools/particle-pipeline/pipeline/t3d_parse.py（2026-09-24 合并），行为逐字保持一致。
# ================================================================
import struct as _struct

OBJ_RE = re.compile(r'^\s*Begin Object (?:(Class=)(\S+) )?Name="([^"]+)"\s*$')
END_RE = re.compile(r'^\s*End Object\s*$')
PROP_RE = re.compile(r'^\s{3,}([A-Za-z_][A-Za-z0-9_]*)(?:\((\d+)\))?=(.*)$')


def f32(raw):
    return _struct.unpack("<f", raw)[0]


TYPE_KIND = {53: "half", 54: "half", 55: "float", 56: "float", 57: "int",
             58: "bool", 59: "vec2", 60: "vec3", 61: "vec4", 62: "color",
             63: "quat", 64: "vec4"}
TYPE_SIZE = {53: 2, 54: 2, 55: 4, 56: 4, 57: 4, 58: 1, 59: 8, 60: 12,
             61: 16, 62: 16, 63: 16, 64: 16}


def decode_bytes(raw, kind, ti):
    """按 Niagara 类型索引解码一段字节。"""
    n = len(raw)
    try:
        if kind in ("float",) and n >= 4:
            return round(f32(raw[:4]), 6)
        if kind == "int" and n >= 4:
            return _struct.unpack("<i", raw[:4])[0]
        if kind == "bool" and n >= 1:
            return bool(raw[0])
        if kind == "half" and n >= 2:
            return round(_struct.unpack("<e", raw[:2])[0], 6)
        if kind == "vec2" and n >= 8:
            return [round(x, 6) for x in _struct.unpack("<2f", raw[:8])]
        if kind in ("vec3",) and n >= 12:
            return [round(x, 6) for x in _struct.unpack("<3f", raw[:12])]
        if kind in ("color", "vec4", "quat") and n >= 16:
            return [round(x, 6) for x in _struct.unpack("<4f", raw[:16])]
    except Exception:
        pass
    return {"raw": list(raw[:16]), "type_index": ti, "kind": kind}


RI_RE = re.compile(r'SortedParameterOffsets=\((.*?)\),ParameterData=\((.*?)\)(?:,DebugName="([^"]*)")?')
OFF_RE = re.compile(r'Offset=(\d+),Name="([^"]*)",TypeDefHandle=\(RegisteredTypeIndex=(\d+)\)')


def parse_rapid_iteration(text):
    """-> (debug_name, [(offset, name, type_index)], bytes) 或 None。"""
    m = RI_RE.search(text)
    if not m:
        return None
    offs, data, dbg = m.group(1), m.group(2), m.group(3)
    params = [(int(a), b, int(c)) for a, b, c in OFF_RE.findall(offs)]
    if not params:
        return None
    try:
        blob = bytes(int(x) for x in data.split(",") if x.strip() != "")
    except Exception:
        return None
    return dbg or "", params, blob


def decode_rapid_iteration(text):
    """-> ({常量全名: {type_index, kind, size, value, bytes}}, debug_name)。
    尺寸优先用「下一个偏移 − 当前偏移」推断，末尾用类型表兜底。"""
    r = parse_rapid_iteration(text)
    if not r:
        return {}, None
    dbg, params, blob = r
    ps = sorted(params, key=lambda p: p[0])
    out = {}
    for i, (off, name, ti) in enumerate(ps):
        if i + 1 < len(ps):
            size = ps[i + 1][0] - off
        else:
            size = TYPE_SIZE.get(ti, 4)
        if size <= 0:
            size = TYPE_SIZE.get(ti, 4)
        raw = blob[off:off + size]
        kind = TYPE_KIND.get(ti, "float")
        out[name] = {"type_index": ti, "kind": kind, "size": size,
                     "value": decode_bytes(raw, kind, ti), "bytes": list(raw)}
    return out, dbg


class T3DObj:
    """对象树节点（与 particle-pipeline 旧 walk_objects 的 Obj 同形，便于两条管线共用）。"""
    __slots__ = ("name", "cls", "props", "pins", "children", "line", "parent")

    def __init__(self, name, cls, line, parent):
        self.name, self.cls, self.line, self.parent = name, cls, line, parent
        self.props, self.pins, self.children = {}, [], []


def walk_compat(src):
    """把 T3D 走成一棵对象树（返回 (roots, classes)）。`src` 可以是**路径**或**行列表**。
    ⚠️ 语义与 particle-pipeline 旧实现逐字一致：
      · 同名对象在 T3D 里会重复声明 → **保留每一次出现**（首现带 Class=，后续只有 Name=）
      · 类名表是**文件级全局**的（后面不带 Class 的重开块也能解析出类）
      · 形如 `Modules(0)=` 的数组属性，下标并进键名 **并且** 原键也写一份
    """
    if isinstance(src, (list, tuple)):
        lines = list(src)
    else:
        lines = io.open(src, encoding='utf-8', errors='replace').read().splitlines()
    roots, stack, classes = [], [], {}
    for i, raw in enumerate(lines):
        m = OBJ_RE.match(raw)
        if m:
            cls, name = m.group(2), m.group(3)
            if cls:
                classes[name] = cls.split(".")[-1]
            o = T3DObj(name, classes.get(name, "?"), i, stack[-1] if stack else None)
            (stack[-1].children if stack else roots).append(o)
            stack.append(o)
            continue
        if END_RE.match(raw):
            if stack:
                stack.pop()
            continue
        if not stack:
            continue
        o = stack[-1]
        s = raw.strip()
        if s.startswith("CustomProperties Pin ("):
            o.pins.append(s)
            continue
        pm = PROP_RE.match(raw)
        if pm:
            key, idx, val = pm.group(1), pm.group(2), pm.group(3).rstrip()
            if idx is not None:
                o.props["%s(%s)" % (key, idx)] = val
            o.props[key] = val
    return roots, classes


def mergename(objs):
    """把同名对象的 props 合并（后出现的补齐先出现的空值）。"""
    merged = {}
    for o in objs:
        m = merged.setdefault(o.name, {"name": o.name, "cls": o.cls, "props": {}, "pins": []})
        if o.cls and o.cls != "?":
            m["cls"] = o.cls
        m["props"].update(o.props)
        m["pins"].extend(o.pins)
    return merged
