# -*- coding: utf-8 -*-
"""生成物 vs 16a 注册表 逐 token 对照审计（只读，不改任何文件）"""
import csv, re, collections, sys, glob

BASE = r"h:/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord/Modules/LivingWorldNpcs/plans/scenario-campaign-mode"

# ---------- 1) 16a 注册表 ----------
with open(BASE + '/16a-DSL翻译总表.csv', encoding='utf-8-sig') as f:
    rows = list(csv.DictReader(f))
side_by_cat = collections.defaultdict(set)
for r in rows:
    for seg in re.split(r'[;,/／]', r['我们侧名']):
        seg = seg.strip()
        if not seg or not re.match(r'^[A-Za-z_][A-Za-z0-9_.:]*$', seg):
            continue
        side_by_cat[r['类别']].add(seg)

domain_tokens = side_by_cat['域'] | {'Ctx', 'Variable', 'GlobalSlot', 'Agent', 'Hero', 'Clan',
                                     'Settlement', 'Time', 'Flag', 'Event', 'Faction', 'Region',
                                     'Text', 'Quest', 'Army', 'Org', 'Card', 'Item', 'Random'}
cmd_names  = side_by_cat['命令']
attr_names = side_by_cat['属性']
fn_names   = side_by_cat['函数'] | side_by_cat['语法'] | {'and', 'or', 'not', 'atWar', 'isVisible', 'sameSettlement', 'hasMet', 'exists'}

# ---------- 2) 剥注释（字符串感知） ----------
def strip_comments(txt):
    out, i, n = [], 0, len(txt)
    while i < n:
        c = txt[i]
        if c == '"':
            j = i + 1
            while j < n:
                if txt[j] == '\\':
                    j += 2
                    continue
                if txt[j] == '"':
                    break
                j += 1
            out.append(txt[i:j + 1])
            i = j + 1
        elif c == '/' and i + 1 < n and txt[i + 1] == '/':
            while i < n and txt[i] != '\n':
                i += 1
        else:
            out.append(c)
            i += 1
    return ''.join(out)

files = [BASE + '/story_event_json/okehazama/events.jsonc'] + sorted(
    glob.glob(BASE + '/story_event_json/okehazama/story/*.jsonc'))

jsonc_map = []  # (file, "key", value)
for fp in files:
    txt = strip_comments(open(fp, encoding='utf-8').read())
    for m in re.finditer(r'"([A-Za-z_][A-Za-z0-9_]*)":\s*"((?:[^"\\]|\\.)*)"', txt):
        jsonc_map.append((fp, m.group(1), m.group(2)))

steps, cmds, actions = collections.Counter(), collections.Counter(), collections.Counter()
params_cn, other_vals = set(), collections.Counter()
attr_refs, fn_refs, domain_used, id_known = collections.Counter(), collections.Counter(), collections.Counter(), set()

for fp, key, val in jsonc_map:
    if key in ('step', 'cmd'):
        (steps if key == 'step' else cmds)[val] += 1
    elif key == 'action':
        actions[val] += 1
    elif key in ('domain', 'attr', 'order', 'mode'):
        if re.search(r'[\u4e00-\u9fff]', val):
            params_cn.add(f'{key}={val}')
        other_vals[val] += 1
    # DSL 表达式字段（condition/when/value/row else）
    if key in ('condition', 'when', 'value') and re.match(r'^[A-Za-z()]', val):
        for m in re.finditer(r'([A-Za-z_][A-Za-z0-9_]*)\s*\(', val):
            fn_refs[m.group(1)] += 1
        for m in re.finditer(r'\(((?:Hero|Time|Settlement|Clan|Flag|Event|Ctx|Variable|GlobalSlot|Agent|Faction|Region|Org|Army|Quest|Text|Item|Random|Card)::([A-Za-z0-9_.]+))\)', val):
            domain_used[m.group(1).split('::')[0]] += 1
            ident = m.group(2)
            if '.' in ident:
                attr_refs[ident.split('.')[-1]] += 1
            else:
                id_known.add(ident)
    # speaker/listener/actor/slot 等域引用
    if key in ('speaker', 'listener', 'actor', 'slot', 'at', 'to', 'override'):
        if re.match(r'^(Hero|Time|Settlement|Clan|Flag|Event|Ctx|Variable|GlobalSlot|Agent|Faction|Region|Org|Army|Quest|Text|Item|Random|Card)::', val):
            domain_used[val.split('::')[0]] += 1
            ident = val.split('::')[1]
            if '.' in ident:
                attr_refs[ident.split('.')[-1]] += 1
            else:
                id_known.add(ident)

def reg(container, token):
    return token in container

print('### A. step / cmd / action 注册情况 ###')
for label, coll in [('step', steps), ('cmd', cmds), ('action', actions)]:
    print(f'\n-- {label} --')
    for k, v in sorted(coll.items()):
        ok = 'OK注册' if reg(cmd_names, k) or reg(side_by_cat['语法'], k) else ('??note' if k == 'note' else 'XX表外')
        print(f'  {k} x{v}  [{ok}]')

print('\n### B. 域使用 ###')
for k, v in sorted(domain_used.items()):
    print(f'  {k} x{v}  {"OK" if k in domain_tokens else "XX表外域"}')

print('\n### C. 属性引用（.attr） ###')
attr_set = {n.split('.')[-1] for n in attr_names}
for k, v in sorted(attr_refs.items()):
    print(f'  .{k} x{v}  {"OK在属性表" if k in attr_set else "XX不在属性表"}')

print('\n### D. DSL 函数 ###')
for k, v in sorted(fn_refs.items()):
    print(f'  {k} x{v}  {"OK" if k in fn_names else "XX表外函数"}')

print('\n### E. 容器/语法参数里的中文值 ###')
for x in sorted(params_cn):
    print(f'  {x}')
