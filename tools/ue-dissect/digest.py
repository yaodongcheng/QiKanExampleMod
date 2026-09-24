# -*- coding: utf-8 -*-
"""对 out/t3d 下的 T3D 生成结构化摘要：
 - bp:   每个蓝图 → 图清单(节点数/类型分布) + 每图调用的引擎函数集合 + 变量(来自 CDO T3D) + SCS 组件
 - struct: 每个 UserDefinedStruct → 字段(名/友好名/类型/子类型/默认值)
 - enum: 每个 UserDefinedEnum → 有序枚举项
 - anim: 动画 → 通知(名/时间) + 段落 + 长度
产出 out/digest/*.txt 与 *.tsv
"""
import os, io, re, sys, json, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import t3d_tools as T

from paths import DUMP_ROOT as OUT
D = os.path.join(OUT, "digest")
os.makedirs(D, exist_ok=True)
os.makedirs(os.path.join(D, "graph"), exist_ok=True)


def unq(s):
    if s is None:
        return s
    s = s.strip()
    if len(s) >= 2 and s[0] == '"' and s[-1] == '"':
        return s[1:-1]
    return s


def bp_digest(name):
    p = os.path.join(OUT, "t3d", "bp", name + ".t3d")
    if not os.path.exists(p):
        return None
    root, scopes, graphs = T.parse_t3d(p)
    gs = T.graphs_of(scopes)
    lines = ["### 蓝图 %s" % name]
    # 父类
    top = scopes.get(('root',), {})
    t = top.get(name)
    if t:
        for pr in t['props'][:14]:
            lines.append("  " + pr[:200])
    lines.append("  图数: %d" % len(gs))
    calls_all = collections.Counter()
    for gname in sorted(gs):
        grp = gs[gname]
        kinds = collections.Counter(T.kind_of(e) for e in grp.values())
        calls = []
        for n, e in grp.items():
            k = T.kind_of(e)
            if k in ('call', 'callarr', 'super'):
                mk, mn, mp = T.member_of(e)
                if mn:
                    calls.append(mn)
                    calls_all[mn] += 1
        lines.append("  == %s  (%d 节点) %s" % (gname, len(grp), dict(kinds)))
        if calls:
            lines.append("     调用: " + ", ".join(sorted(set(calls))))
    # 变量来自 CDO
    cp = os.path.join(OUT, "t3d", "cdo", name + "_CDO.t3d")
    if os.path.exists(cp):
        try:
            cr, csc, cg = T.parse_t3d(cp)
            ct = csc.get(('root',), {})
            entry = None
            for k, v in ct.items():
                if v['cls'] and 'BlueprintGeneratedClass' in (v['cls'] or '') or k.startswith("Default__"):
                    entry = v
                    break
            if entry is None:
                # 取第一个有 props 的
                for k, v in ct.items():
                    if v['props']:
                        entry = v
                        break
            if entry:
                lines.append("  -- 默认值/变量(%d):" % len(entry['props']))
                for pr in entry['props']:
                    lines.append("     " + pr[:400])
        except Exception as e:
            lines.append("  CDO解析失败 %r" % e)
    txt = "\n".join(lines)
    io.open(os.path.join(D, "graph", name + ".txt"), "w", encoding="utf-8").write(txt)
    return len(gs), len(calls_all), txt


def struct_digest(name):
    p = os.path.join(OUT, "t3d", "struct", name + ".t3d")
    if not os.path.exists(p):
        return None
    root, scopes, graphs = T.parse_t3d(p)
    allents = scopes.get(('root',), {})
    fields = []
    allprops = []
    for _k, _v in allents.items():
        allprops.extend(_v['props'])
    for pr in allprops:
        if pr.startswith('VariablesDescriptions'):
            m = re.match(r'VariablesDescriptions\((\d+)\)=\((.*)\)$', pr)
            if not m:
                continue
            idx = int(m.group(1))
            kv = dict()
            for part in T.split_top(m.group(2)):
                if '=' in part:
                    k, v = part.split('=', 1)
                    kv[k.strip()] = v.strip()
            fields.append((idx, kv))
    fields.sort()
    out = ["### 结构体 %s  (%d 字段)" % (name, len(fields))]
    for idx, kv in fields:
        fn = kv.get('FriendlyName', '').strip('"')
        vn = kv.get('VarName', '')
        vn_short = re.sub(r'_\d+_[0-9A-F]{32}$', '', vn)
        cat = kv.get('Category', '')
        sub = kv.get('SubCategoryObject', '')
        dv = kv.get('DefaultValue', '')
        extra = ''
        if sub:
            extra = ' of ' + sub.split('.')[-1].rstrip("'")
        if dv:
            extra += '  default=%s' % dv[:160]
        isarr = kv.get('bIsArray', '')
        out.append("  [%02d] %-42s %-10s%s%s" % (idx, fn or vn_short, cat + extra, '', ''))
    return "\n".join(out)


def enum_digest(name):
    p = os.path.join(OUT, "t3d", "enum", name + ".t3d")
    if not os.path.exists(p):
        return None
    txt = io.open(p, encoding="utf-8", errors="replace").read()
    m = re.search(r'DisplayNameMap=\((.*)\)\s*$', txt, re.M)
    if not m:
        return "### 枚举 %s  (无 DisplayNameMap)" % name
    items = re.findall(r'\("([^"]+)",\s*NSLOCTEXT\([^)]*?"([^"]*)"\)\)', m.group(1))
    out = ["### 枚举 %s  (%d 项)" % (name, len(items))]
    for i, (raw, disp) in enumerate(items):
        out.append("  %2d = %s   (%s)" % (i, disp, raw))
    return "\n".join(out)


def anim_digest(name):
    p = os.path.join(OUT, "t3d", "anim", name + ".t3d")
    if not os.path.exists(p):
        return None
    root, scopes, graphs = T.parse_t3d(p)
    t = scopes.get(('root',), {}).get(name)
    tnode = None
    def find(n):
        global tnode
        if n['name'] == name and tnode is None:
            tnode = n
        for c in n['children']:
            find(c)
    for c in root['children']:
        find(c)
    if not t:
        return None
    out = ["### 动画 %s" % name]
    for pr in t['props']:
        if pr.startswith(('SequenceLength=', 'RateScale=', 'Notifies(', 'NotifyTrackNames(',
                          'CompositeSections(', 'SlotAnimTracks(', 'bEnableRootMotion=')):
            out.append("  " + pr[:300])
    # 嵌套对象：通知
    def walk(n, d=0):
        for c in n['children']:
            if c['cls'] and ('AnimNotify' in c['cls'] or 'AnimNotifyState' in c['cls']):
                out.append("  通知: %s  %s" % (c['cls'].split('.')[-1], c['name']))
                for pr in c['props'][:12]:
                    out.append("      " + pr[:200])
            walk(c, d + 1)
    walk(t)
    return "\n".join(out)


def main():
    inv = json.load(io.open(os.path.join(OUT, "01_inventory.json"), encoding="utf-8"))
    cl = collections.Counter()
    index = []
    for r in inv:
        c = r.get("class")
        n = r.get("name")
        cl[c] += 1
        if c in ("Blueprint", "WidgetBlueprint", "AnimBlueprint"):
            res = bp_digest(n)
            if res:
                index.append((n, c, res[0], res[1]))
    io.open(os.path.join(D, "_bp_index.tsv"), "w", encoding="utf-8").write(
        "\n".join("%s\t%s\t%d\t%d" % t for t in sorted(index)))
    # struct
    parts = []
    for r in inv:
        if r.get("class") == "UserDefinedStruct":
            s = struct_digest(r["name"])
            if s:
                parts.append(s)
    io.open(os.path.join(D, "_structs.txt"), "w", encoding="utf-8").write("\n\n".join(parts))
    # enum
    parts = []
    for r in inv:
        if r.get("class") == "UserDefinedEnum":
            s = enum_digest(r["name"])
            if s:
                parts.append(s)
    io.open(os.path.join(D, "_enums.txt"), "w", encoding="utf-8").write("\n\n".join(parts))
    # anim
    parts = []
    for r in inv:
        if r.get("class") in ("AnimMontage", "AnimSequence", "AnimComposite"):
            s = anim_digest(r["name"])
            if s:
                parts.append(s)
    io.open(os.path.join(D, "_anims.txt"), "w", encoding="utf-8").write("\n\n".join(parts))
    print("DIGEST_DONE bp=%d" % len(index))


if __name__ == '__main__':
    main()
