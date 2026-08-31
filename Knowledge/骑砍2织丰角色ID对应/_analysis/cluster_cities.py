# -*- coding: utf-8 -*-
"""TK5 城 -> 织丰城聚类映射器（1560 演示 → 全时代）

规则（用户裁定）：
1. 城归属 = TK5 城主(人物ID) → TaikouHero.ClanID(织丰家族) → Settlements.OwnerClan 同族城（同 Culture 片）。
2. 规模档位 = TK5 士兵数 (9400/7600/5800/4000…)；同族内 TK5 城按档降序 ↔ 织丰城按清单序 1:1；
   TK5 城多于织丰城 → 多出并入同族最大档城（多对一允许）；织丰城多于 TK5 → 剩余标"无TK5数据"。
3. Culture 过滤：织丰城 Culture ∈ 城主 Clan 所在 Culture（Clan.csv Culture / Kingdom 归属近似）。
输出：temp 摘要（stdout）+ city_cluster_map.json
"""
import csv, sys, os, json, collections
sys.stdout.reconfigure(encoding='utf-8', errors='replace')

KN = r'h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Knowledge\骑砍2织丰角色ID对应\csv'
TK5 = r'E:\taikou5\Taikou5.Green.Edition-ALI213\Taikou5\_analysis\decoded\tk5_era_init_v1.json'

def rdd(fn):
    return list(csv.DictReader(open(os.path.join(KN, fn), encoding='utf-8-sig')))

hero = rdd('TaikouHero.csv')
settle = rdd('Settlements.csv')
clans = rdd('Clan.csv')

# 索引
hero_by_tk = {}          # TK5编号(int) -> hero row
for h in hero:
    tk = h.get('TK5编号', '').strip()
    if tk.isdigit():
        hero_by_tk[int(tk)] = h
clan_by_id = {c['ID']: c for c in clans}
settle_cities = [s for s in settle if s.get('IsCity', '').strip() == '1']

# 城主 ClanID 待映射统计
era_data = json.load(open(TK5, encoding='utf-8'))
summary = {}
for era in era_data['eras']:
    misses = 0
    lord_ids = [c['lord'] for c in era_data['cities'][era]]
    for lid in lord_ids:
        if lid not in hero_by_tk:
            misses += 1
    summary[era] = {'城数': len(lord_ids), '城主无织丰映射': misses}

print('城主→织丰映射统计（每时代）:')
for era, v in summary.items():
    print('  %s: %s' % (era, v))

# 1560 聚类演示：织田/今川/浅井
era = '1560'
cities = era_data['cities'][era]
grp = collections.defaultdict(list)
for c in cities:
    h = hero_by_tk.get(c['lord'])
    if h is None:
        continue
    grp[h['ClanID']].append((c['lord'], c['soldiers'], c['food'], h['CNName']))

for clan_id, items in sorted(grp.items()):
    if clan_id not in ('clan_oda_1', 'clan_imagawa_1', 'clan_azai_1', 'clan_tokugawa_1', 'clan_mori_1'):
        continue
    owned = [s for s in settle_cities if s['OwnerClan'] == 'Faction.' + clan_id]
    print('\n== %s | TK5 城 %d 座 / 织丰城 %d 座' % (clan_id, len(items), len(owned)))
    for it in sorted(items, key=lambda x: -x[1]):
        print('   TK5 城: 城主=%s(%d) 兵=%d 粮=%d' % (it[3], it[0], it[1], it[2]))
    for o in owned:
        print('   织丰城: %s %s (%s)' % (o['ID'], o['CityName'], o['Culture']))
