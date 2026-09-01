"""太阁5 时代初始化数据包 v1 生成器

输入: _analysis/decoded/Snr0-5.plain (已按 DeCode.rtg 解码)
输出: _analysis/decoded/tk5_era_init_v1.json

- 人物: per era {personID: {force, rank, superior, salary, ambition, loyalty, kamon}}
- 城池: per era {cityIdx: {lord, soldiers, food, gold, train, morale, force}}
- 势力: {forceID: {lord}} (来自高亚男截图 0~20 + 城表城主顺推补全)
"""
import sys, os, re, json, struct
sys.stdout.reconfigure(encoding='utf-8', errors='replace')

DEC = os.path.join(os.path.dirname(__file__), 'editor124', 'DeCode.rtg')
def decode_snr(scn):
    de = open(DEC, 'rb').read()
    table = de[scn*256:(scn+1)*256]
    raw = open(os.path.join(r'E:/taikou5/Taikou5.Green.Edition-ALI213/Taikou5/TW', 'Snr%d_TW.TR5' % scn), 'rb').read()
    return bytes(table[b] for b in raw)

ERAS = ['1554', '1560', '1568', '1575', '1582', '1598']
ANCHOR = bytes.fromhex('6046615b6313421000084d04000064642779')
FALLBACK_BASE = {5: 0x9540}

def attr_base(plain):
    hits = [m.start() for m in re.finditer(re.escape(ANCHOR), plain)]
    for h in hits:
        if h - 195*36 >= 0:
            return h - 195*36
    return None

def person_records(plain, base):
    rows = {}
    for pid in range(1, 1400):
        off = base + pid*36
        if off + 36 > len(plain):
            break
        rec = plain[off:off+36]
        if all(b == 0 for b in rec):
            continue
        rows[pid] = {
            'force': rec[0x05], 'rank': rec[0x09],
            'superior': int.from_bytes(rec[0x0A:0x0C], 'little'),
            'salary': int.from_bytes(rec[0x0C:0x0E], 'little'),
            'ambition': rec[0x0E], 'loyalty': rec[0x0F],
            'kamon': rec[0x10],
            'x07': rec[0x07], 'x11': rec[0x11],
            'x16': int.from_bytes(rec[0x16:0x18], 'little'),
            'x18': int.from_bytes(rec[0x18:0x1A], 'little'),
        }
    return rows

def city_records(plain):
    """城记录: [前2=城主u16][士兵u16][兵粮u32][资金u32][训练u8][士气u8], 训练>0 且士兵范围合理"""
    hits = []
    n = len(plain) - 14
    for i in range(n):
        soldiers = int.from_bytes(plain[i:i+2], 'little')
        food = int.from_bytes(plain[i+2:i+6], 'little')
        gold = int.from_bytes(plain[i+6:i+10], 'little')
        train, morale = plain[i+10], plain[i+11]
        if 1000 <= soldiers <= 40000 and 5000 <= food <= 300000 and 5000 <= gold <= 300000 and 1 <= train <= 100 and 1 <= morale <= 100:
            lord = int.from_bytes(plain[i-2:i], 'little')
            if 0 < lord < 1400:
                hits.append((lord, soldiers, food, gold, train, morale))
    return hits

# 势力表: 截图 0..20 (势力ID -> 当主人ID) + 由城表城主顺推补全
FORCES_KNOWN = {0:8, 1:16, 2:23, 3:31, 4:32, 5:36, 6:42, 7:45, 8:56, 9:86, 10:93,
                11:98, 12:110, 13:136, 14:140, 15:160, 16:164, 17:167, 18:188, 19:195, 20:204}

out = {'eras': ERAS, 'persons': {}, 'cities': {}, 'forces': FORCES_KNOWN, 'notes': []}
for scn, era in enumerate(ERAS):
    plain = decode_snr(scn)
    base = attr_base(plain) or FALLBACK_BASE.get(scn)
    if base is None:
        out['notes'].append('era %s: person table not found' % era)
        continue
    rows = person_records(plain, base)
    out['persons'][era] = rows
    cities = city_records(plain)
    # 去重: 同一城主+数值组即同一城 (城ID 未定位, 以索引排序输出)
    seen = set()
    cl = []
    for l, s, f, g, t, m in cities:
        key = (l, s, f)
        if key in seen:
            continue
        seen.add(key)
        cl.append({'lord': l, 'soldiers': s, 'food': f, 'gold': g, 'train': t, 'morale': m})
    out['cities'][era] = cl
    # 顺推势力表: 城主都是各势力当主
    for c in cl:
        # 城主 -> 其势力
        fid = rows.get(c['lord'], {}).get('force', None)
        if fid in FORCES_KNOWN:
            continue
        if fid and fid < 60:
            FORCES_KNOWN[fid] = c['lord']
    out['forces'] = dict(FORCES_KNOWN)

path = os.path.join(os.path.dirname(__file__), '..', '_analysis', 'decoded', 'tk5_era_init_v1.json')
with open(path, 'w', encoding='utf-8') as f:
    json.dump(out, f, ensure_ascii=False, indent=1)
print('written:', path)
for era in ERAS:
    print('%s: persons=%d cities=%d' % (era, len(out['persons'].get(era, {})), len(out['cities'].get(era, []))))
print('forces inferred:', len(FORCES_KNOWN))
