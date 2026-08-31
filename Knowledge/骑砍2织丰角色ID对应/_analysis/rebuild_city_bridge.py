# -*- coding: utf-8 -*-
"""CityTaikou.csv 最终结构重建（桥信息全保留）

主键 = 织丰城 StringId（铁律 20 模式，同 TaikouHero）；
TK5 侧信息（TK5_ID / TK5_Name / TK5_Area / IsMerge）来自旧表（git HEAD 20 列版）逐城保留；
一个织丰城可收多个 TK5 城（多值列 | 分隔，铁律 24）。
"""
import csv, sys, os, collections, re
sys.stdout.reconfigure(encoding='utf-8', errors='replace')

KN = r'h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Knowledge\骑砍2织丰角色ID对应\csv'
OLD = os.path.join(KN, 'CityTaikou.csv.old_from_git')   # 旧表（TK5 视角, 20列版）
OUT = os.path.join(KN, 'CityTaikou.csv')
ERAS = ['1554', '1560', '1568', '1575', '1582', '1598']

def rdd(fn):
    return list(csv.DictReader(open(fn, encoding='utf-8-sig')))

old = rdd(OLD)
settle = rdd(os.path.join(KN, 'Settlements.csv'))
# 匹配范围 = 全部 settlements（城+町+村+港——TK5 的"城"在织丰可能是町/村级）
settle_by_name = {}

def _strip_city(nm):
    return (nm or '').replace('城', '').strip()

def _rank(s):
    # 匹配优先: 城/镇 > 村(同名撞车时城级为正统)
    return 0 if str(s.get('IsCity') or '').strip() == '1' else (1 if s.get('ID', '').startswith('town_') else 2)

for s in settle:
    nm = (s.get('CityName') or s.get('ScriptName') or '').strip()
    if nm:
        settle_by_name.setdefault(nm, s)

# 旧表按 Settlement(织丰城名) 聚合：一织丰城 ← 多个 TK5 城行
by_zf = collections.defaultdict(list)
for o in old:
    zf = (o.get('Settlement') or '').strip()
    if not zf:
        continue
    by_zf[zf].append(o)

hdr = ['ID', 'ChineseName', 'Culture', 'CultureCN', 'TK5Region', 'TK5_Country', 'TK5_ID',
       'TK5_Name', 'IsMerge', 'Alias', 'TK5_merged']
for y in ERAS:
    hdr += ['Owner_%s' % y, 'Soldiers_%s' % y]

# Culture.csv 字典（权威）：ID -> ChineseName(织丰文化名) / OtherName(太阁区名)
cult = {}
for c in rdd(os.path.join(KN, 'Culture.csv')):
    cid = (c.get('ID') or '').strip()
    if cid:
        cult[cid] = (c.get('ChineseName', '').strip(), c.get('OtherName', '').strip())

def cult_names(culture_id):
    c = cult.get(culture_id)
    if c is None:
        raise SystemExit('⚠ Culture 不在 Culture.csv: %r' % culture_id)
    cn, other = c
    if not cn or not other:
        raise SystemExit('⚠ Culture %r 缺 ChineseName/OtherName' % culture_id)
    return cn, other

rows = []
used = set()
agg = {}    # sid -> row（合并同城多 TK5 行）
for zf, items in by_zf.items():
    # 统一匹配: 双方去'城'后相同 → 城/镇优先；同级时精确名优先（同名撞车保护: 大浦城(ko) vs 大浦(village)）
    cands = [(key, s2) for key, s2 in settle_by_name.items()
             if key and _strip_city(key) == _strip_city(zf)]
    if cands:
        s = min(cands, key=lambda kv: (_rank(kv[1]), 0 if kv[0] == zf else 1))[1]
    else:
        s = None
    if s is None:
        print('⚠ 旧表指向未匹配织丰城: %s' % zf)
        # 保留 TK5 信息（织丰侧留空，供人工后补）
        key = 'PENDING:' + zf
        o0 = items[0]
        rows.append([key, '', '', '', '', (o0.get('Area') or ''),
                     (o0.get('ID') or '').strip(), o0.get('ChineseName') or '', o0.get('IsMerge') or '',
                     o0.get('Alias') or '', o0.get('ChineseName') or ''] + [''] * 12)
        continue
    sid = s['ID']
    used.add(sid)
    tk_ids = '|'.join(sorted({(o.get('ID') or '').strip() for o in items if o.get('ID')}, key=lambda x: int(x) if x.isdigit() else 0))
    tk_names = '|'.join(sorted({o.get('ChineseName') or '' for o in items}))
    tk_areas = '|'.join(sorted({(o.get('Area') or '').strip() for o in items if o.get('Area')}))
    ismerge = '|'.join(sorted({(o.get('IsMerge') or '').strip() for o in items if (o.get('IsMerge') or '').strip()}))
    aliases = '|'.join(sorted({(o.get('Alias') or '').strip() for o in items if (o.get('Alias') or '').strip()}))
    ccn, tcreg = cult_names(s['Culture'])
    row = [sid, s['CityName'] or s['ScriptName'], s['Culture'], ccn, tcreg, tk_areas,
           tk_ids, tk_names, ismerge, aliases, tk_names] + [''] * 12
    if sid in agg:
        # 同织丰城多来源行 → 合并 TK5 侧
        p = agg[sid]
        for c in (5, 6, 7, 8, 9, 10):
            vals = {x for x in (p[c] or '').split('|') + (row[c] or '').split('|') if x}
            p[c] = '|'.join(sorted(vals))
    else:
        agg[sid] = row
rows = list(agg.values()) + [r for r in rows if r[0].startswith('PENDING:')]

# 纯织丰补充（无 TK5 对应）
for s in settle:
    if s['ID'] not in used:
        ccn, tcreg = cult_names(s['Culture'])
        rows.append([s['ID'], s['CityName'] or s['ScriptName'], s['Culture'], ccn, tcreg,
                     '', '', '', '', '', ''] + [''] * 12)

# ---- PENDING 补 Culture 片：国名→织丰 Culture（学自桥行，兜底地理字典）----
learn = collections.defaultdict(set)
for r in rows:
    if not r[0].startswith('PENDING') and r[2] and r[5]:
        for c in r[5].split('|'):
            if c:
                learn[c].add(r[2])
FALLBACK = {
    '但马': 'sanyo', '美作': 'sanyo', '备前': 'sanyo', '备中': 'sanyo', '备后': 'sanyo',
    '安芸': 'sanyo', '周防长门': 'sanyo', '出云': 'sanyo', '石见': 'sanyo', '因幡伯耆': 'sanyo',
    '赞歧': 'nankai', '伊予': 'nankai', '阿波淡路': 'nankai', '土佐': 'nankai',
    '筑前对马': 'saikai', '筑后': 'saikai', '肥前': 'saikai', '肥后': 'saikai',
    '丰前': 'saikai', '丰後': 'saikai', '日向': 'saikai', '大隅': 'saikai', '萨摩': 'saikai',
}
for r in rows:
    if r[0].startswith('PENDING') and not r[2]:
        cid = None
        for cnt in r[5].split('|'):
            if cnt in learn and len(learn[cnt]) == 1:
                cid = list(learn[cnt])[0]
            elif cnt in FALLBACK:
                cid = FALLBACK[cnt]
            if cid:
                break
        if cid and cid in cult:
            r[2] = cid
            r[3] = cult[cid][0]
            r[4] = cult[cid][1]

# ---- 母实体国名赋值 + 一致性体检 ----
pat = re.compile(r'^(?:castle_|village_|castle_village_)?([A-Z]+)(\d+)(?:_\d+)?$')
by_id = {r[0]: r for r in rows}
for r in rows:
    m = pat.match(r[0])
    if not m:
        continue
    base = m.group(1) + m.group(2)
    if r[0].startswith(('castle_village_', 'castle_')):
        pid = 'castle_' + base
    else:
        pid = None
        if 'town_' + base in by_id:
            pid = 'town_' + base
        elif 'castle_' + base in by_id:
            pid = 'castle_' + base
    if pid and pid in by_id:
        pc = by_id[pid][5].strip()
        if pc and not r[5].strip():
            r[5] = pc

with open(OUT, 'w', newline='', encoding='utf-8-sig') as f:
    csv.writer(f).writerow(hdr)
    csv.writer(f).writerows(rows)
print('写回 %d 行（旧桥 %d + 纯织丰 %d）' % (len(rows), len(used), len(rows) - len(used)))
print('表头:', hdr)
print('样例:')
for r in rows:
    if r[1] in ('清须城', '骏府城', '冈崎城', '小谷城'):
        print('  ', r[:10])
