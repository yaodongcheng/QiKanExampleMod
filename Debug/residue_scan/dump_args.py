# -*- coding: utf-8 -*-
import os, re, sys, json
from collections import Counter, defaultdict
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.abspath('.')
sys.path.insert(0, os.path.join(ROOT, 'plans/scenario-campaign-mode/tools'))
import gen_registry_tables as G
import tk5_to_json as T

JA = r'々〆ヶ・·一-鿿㐀-䶿぀-ヿＡ-Ｚａ-ｚA-Za-z0-9０-９_'
RE_TOKEN = re.compile('^[' + JA + ']+$')
RE_HEAD = re.compile(r'^([' + JA + r']{1,12}):')
NAMES = set()
for tbl in ('HERO_MAP','AGENT_MAP','CLAN_MAP','KINGDOM_MAP','SETTLEMENT_MAP','REGION_MAP','FALLBACK_MAP','SLOT_NAME_MAP','TRIGGER_MAP'):
    NAMES |= set(getattr(T, tbl))

# 语料里出现过 域::同名 且域∈ENTITY_DOMAINS → 具名实体
ent_names = defaultdict(set)
txt = open('Knowledge/太阁事件包/TK5AllEvents_merged.txt', encoding='utf-8').read()
for d, v in re.findall(r'([' + JA + r']{1,8})::([' + JA + r']{1,16})', txt):
    if d in G.ENTITY_DOMAINS:
        ent_names[v].add(d)

def split_args(s):
    """cmd 后的 (a,b,c)(d) 组，返回扁平 arg 列表（跳过 [[台词]]）。"""
    out, i, n = [], 0, len(s)
    while i < n:
        if s.startswith('[[', i):
            e = s.find(']]', i); i = (e+2) if e>=0 else n; continue
        if s.startswith('//', i): break
        if s[i] == '(':
            depth, j = 1, i+1
            while j < n and depth:
                if s[j] == '(': depth += 1
                elif s[j] == ')': depth -= 1
                j += 1
            out.extend(x.strip() for x in s[i+1:j-1].split(','))
            i = j; continue
        i += 1
    return out

def is_slot(t):
    m = re.match(r'^([' + JA + r']{1,8}?)([Ａ-Ｅ])$', t)
    return bool(m and (m.group(1) in G.SLOT_CAT or m.group(1) in ('文字列','數值','變量','容器'))) or bool(re.match(r'^[ａ-ｚ]$', t))

pos_tokens = defaultdict(Counter)
for line in txt.splitlines():
    s = line.strip()
    if not s or s.startswith('#') or s.startswith('}'): continue
    m = RE_HEAD.match(s)
    if not m: continue
    cmd = m.group(1)
    for k, a in enumerate(split_args(s[m.end():])):
        if not a or '::' in a or not RE_TOKEN.match(a): continue
        if re.fullmatch(r'-?[0-9]+', a) or is_slot(a) or a in G.SPECIAL_VALS: continue
        if re.match(r'^(?:事件)?[A-Z]{1,3}[0-9A-F]{4,8}_[0-9]+$', a): continue
        if cmd.startswith('未知') and re.fullmatch(r'[0-9A-F]{2}', a): continue
        if a in NAMES: continue
        pos_tokens[(cmd, k)][a] += 1

ent, enum = {}, {}
for key, c in pos_tokens.items():
    e = Counter({k: v for k, v in c.items() if k in ent_names})
    u = Counter({k: v for k, v in c.items() if k not in ent_names})
    if e: ent[key] = e
    if u: enum[key] = u

with open('Debug/residue_scan/args_enum.txt', 'w', encoding='utf-8') as f:
    for (cmd, k), c in sorted(enum.items(), key=lambda kv: -sum(kv[1].values())):
        f.write('\n### %s [pos%d]  %d种/%d处\n' % (cmd, k, len(c), sum(c.values())))
        f.write('  ' + '  '.join('%s(%d)' % (t, n) for t, n in c.most_common()) + '\n')
with open('Debug/residue_scan/args_entity.txt', 'w', encoding='utf-8') as f:
    for (cmd, k), c in sorted(ent.items(), key=lambda kv: -sum(kv[1].values())):
        doms = Counter()
        for t in c: doms.update(ent_names[t])
        f.write('%s [pos%d] %d种/%d处 域=%s\n' % (cmd, k, len(c), sum(c.values()), dict(doms)))
print('enum (cmd,pos)=%d tokens=%d 处=%d' % (len(enum), len(set(t for c in enum.values() for t in c)), sum(sum(c.values()) for c in enum.values())))
print('ent  (cmd,pos)=%d tokens=%d 处=%d' % (len(ent), len(set(t for c in ent.values() for t in c)), sum(sum(c.values()) for c in ent.values())))
