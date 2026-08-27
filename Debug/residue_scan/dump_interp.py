# -*- coding: utf-8 -*-
import os, re, sys
from collections import Counter, defaultdict
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, os.path.join(os.path.abspath('.'), 'plans/scenario-campaign-mode/tools'))
import gen_registry_tables as G
JA = r'々〆ヶ・·一-鿿㐀-䶿぀-ヿＡ-Ｚａ-ｚA-Za-z0-9０-９_'
txt = open('Knowledge/太阁事件包/TK5AllEvents_merged.txt', encoding='utf-8').read()
ent = set()
for d, v in re.findall(r'([' + JA + r']{1,8})::([' + JA + r']{1,16})', txt):
    if d in G.ENTITY_DOMAINS: ent.add(v)
RE_INTERP = re.compile(r'\{([^{}]*)\}|<([^<>]*)>|\(([^()]*)\)')
RE_TOK = re.compile('[' + JA + ']')
fields, bare = Counter(), Counter()
for line in txt.splitlines():
    s = line.strip()
    i = 0
    while True:
        a = s.find('[[', i)
        if a < 0: break
        b = s.find(']]', a)
        payload = s[a+2:b if b>=0 else len(s)]
        for m in RE_INTERP.finditer(payload):
            inner = (m.group(1) or m.group(2) or m.group(3) or '').strip()
            if not inner or not RE_TOK.search(inner): continue
            if '.' in inner:
                subj, attr = inner.split('.', 1)
                fields[attr] += 1
            else:
                bare[inner] += 1
        i = (b+2) if b>=0 else len(s)
print('── 插值属性/字段（%d 种）' % len(fields))
for k,v in fields.most_common(): print('   %6d %s' % (v,k))
unk = Counter({k:v for k,v in bare.items() if k not in ent and not re.fullmatch(r'未知[0-9０-９]+', k) and not re.fullmatch(r'[0-9０-９]+', k)})
print('\n── 裸插值：具名实体 %d 种 / 未知N %d 种 / 其余 %d 种' % (
    len([k for k in bare if k in ent]), len([k for k in bare if re.fullmatch(r'未知[0-9０-９]+',k)]), len(unk)))
for k,v in unk.most_common(): print('   %6d %s' % (v,k))
