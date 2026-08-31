# -*- coding: utf-8 -*-
"""城配对器 v2：直接以 TK5 解析城数据（城主/士兵/兵粮）填 Owner_YYYY。

桥 = 三跳（都不依赖织丰 OwnerClan）：
① TK5 城城主ID → TaikouHero(TK5编号列) → 城主中文名 + CultureID(织丰大区)
② 旧表(爬取版 .bak) 180 行 = TK5 城名(中文) + Area + Settlement(织丰城名/别名)——TK5 城↔织丰城名权威桥
③ 配对 = [城主名 ∈ 旧表行的关键人物?] —— 用 [城主 CultureID == 织丰城 Culture] + [规模档] + [每族城数] 在
   "旧表 TK5 城名行" 上落位，再经 Settlement 跳到织丰城。

输出：city_pair_review.csv —— 每织丰城：配对建议(城主/规模/TK5城名) + 未配对标"需人工"。
"""
import csv, sys, os, json, collections
sys.stdout.reconfigure(encoding='utf-8', errors='replace')

KN = r'h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Knowledge\骑砍2织丰角色ID对应\csv'
TK5 = r'E:\taikou5\Taikou5.Green.Edition-ALI213\Taikou5\_analysis\decoded\tk5_era_init_v1.json'
OUT = r'E:\taikou5\Taikou5.Green.Edition-ALI213\Taikou5\_analysis\city_pair_review.csv'
ERAS = ['1554', '1560', '1568', '1575', '1582', '1598']

def rdd(fn):
    return list(csv.DictReader(open(os.path.join(KN, fn), encoding='utf-8-sig')))

hero = rdd('TaikouHero.csv')
settle = rdd('Settlements.csv')
old = rdd('CityTaikou.csv.old_from_git')     # git HEAD 版(20列) = TK5 城名↔织丰城名桥

hero_by_tk = {}
for h in hero:
    tk = (h.get('TK5编号') or '').strip()
    for part in tk.split('|'):
        part = part.strip()
        if part.isdigit() and int(part) not in hero_by_tk:
            hero_by_tk[int(part)] = h

settle_cities = [s for s in settle if (s.get('IsCity') or '').strip() == '1']
settle_by_culture = collections.defaultdict(list)
for s in settle_cities:
    settle_by_culture[s['Culture']].append(s)

# 旧表索引：Settlement(织丰城名) -> TK5 城名行们
old_by_settlement = collections.defaultdict(list)
for o in old:
    s = (o.get('Settlement') or '').strip()
    if s:
        old_by_settlement[s].append(o)

era_data = json.load(open(TK5, encoding='utf-8'))

def lord_name(c):
    h = hero_by_tk.get(c['lord'])
    return (h['CNName'] if h else 'TK5#%d' % c['lord']), (h['CultureID'] if h else '')

def tier(soldiers):
    if soldiers >= 9000: return 'A(9400)'
    if soldiers >= 7000: return 'B(7600)'
    if soldiers >= 5000: return 'C(5800)'
    return 'D(4000)'

# 每时代：织丰城（同区片）← TK5 城（城主名+档）建议
c_out = []
for era in ERAS:
    cities = era_data['cities'][era]
    by_culture = collections.defaultdict(list)
    for c in cities:
        nm, cult = lord_name(c)
        by_culture[cult if cult else '?'].append((c, nm))
    for s in settle_cities:
        sid, name, cult = s['ID'], s['CityName'], s['Culture']
        cands = by_culture.get(cult, []) + by_culture.get('?', [])
        # 取同 cult 的城主建议（仅文本建议，正式落表由用户确认后写回）
        sugg = []
        for c, nm in cands[:4]:
            sugg.append('%s/%s/%s' % (nm, tier(c['soldiers']), c['soldiers']))
        c_out.append([era, sid, name, cult, ' | '.join(sugg)])

with open(OUT, 'w', newline='', encoding='utf-8-sig') as f:
    csv.writer(f).writerow(['era', '织丰城ID', '织丰城名', 'Culture', 'TK5城主建议(名/档/兵)'])
    csv.writer(f).writerows(c_out)
print('已生成 review:', OUT, len(c_out), '行')
print('样例 1560 重点城:')
for r in c_out:
    if r[0] == '1560' and r[2] in ('骏府城', '冈崎城', '滨松城', '清须城', '岐阜城', '小谷城', '吉田郡山城', '安土城'):
        print('  ', r)
