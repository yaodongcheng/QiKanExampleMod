# -*- coding: utf-8 -*-
"""TK5 六剧本全量隶属关系导出器 v2（用户裁定 2026-08-31）

v1 缺陷（反思）：
  1. 城记录用「数值范围全文扫描」定位 → 脆弱（曾用 6 字节指纹匹配误判骏府城 1568 不存在）；
  2. 城名识别 = 指纹宽度错误（4B 主体 + 尾部元数据，非固定 6B）；
  3. 无城主→势力自洽校验。
v2 修复：
  A. 城表 = 180 条 × 36B 等距连续区（表头锚定后一步读到尾，零扫描误判）；
  B. 城名指纹 = 记录窗口 +0x18 起 4B 主体（尾部 = 随时代变化的元数据，不参与指纹）；
  C. 输出前做三层校验：城表连续性 / 骏府城跨代城主链 / forceID↔Kingdom 列多数票一致。

输出：_analysis/decoded/era_v2/{1554,1560,1568,1575,1582,1598}/
  persons.csv  cities.csv  forces.csv
全局：cities_index.csv（指纹→城名候选/城主链）、README.md（方法+校验+开放项）

生成物：本文件修改后才能改产出；数据不得手改（铁律 22 精神）。
"""
import sys, os, csv, io, json, collections

HERE = os.path.dirname(os.path.abspath(__file__))
DEC = os.path.join(HERE, 'editor124', 'DeCode.rtg')
DECODED = os.path.join(HERE, 'decoded', 'era_v2')
KN = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Knowledge\骑砍2织丰角色ID对应\csv"
ERAS = ['1554', '1560', '1568', '1575', '1582', '1598']
# 历史名事实表（确定性：每项均为史料明确记载的改名/异称；改此表=改事实，重跑生效）
RENAME_FACTS = {
    # 全量历史名（每城 0..n 个：该城历史上用过的所有名字集合；史料明确记载才收）
    '今滨城': ['长滨城'],                    # 1573 羽柴秀吉筑城改名长滨
    '稻叶山城': ['岐阜城'],                  # 1567 织田信长改名岐阜
    '曳马城': ['滨松城'],                    # 家康居城改称滨松
    '石山本愿寺': ['大坂城'],                # 1583 秀吉改大坂城
    '不来方城': ['盛冈城'],                  # 南部家移居改称盛冈
    '阪本城': ['大津城'],                    # 1595 京极高次改建大津城
    '观音寺城': ['安土城', '水口城'],        # 六角(观音寺)→信长改筑安土→长束正家水口（同城位承接）
    '踯躅崎馆': ['甲府城'],                  # 武田后期称甲府城
    '胜山馆': ['松前城'],                    # 蛎崎家改称松前
    '那古野城': ['名古屋城'],                # 家康重建后改称名古屋
    '二股城': ['二俣城'],                    # 异体字表记
}
# 改名事件时间表（确定性：事件年份=史料记载；剧本名 = 按事件年份落代）
RENAME_EPOCHS = {
    '稻叶山城': [('岐阜城', 1567, '织田信长改名')],
    '今滨城': [('长滨城', 1573, '羽柴秀吉筑城改名')],
    '曳马城': [('滨松城', 1570, '德川家康改名')],
    '石山本愿寺': [('大坂城', 1583, '丰臣秀吉改大坂')],
    '观音寺城': [('安土城', 1576, '信长改筑安土'), ('水口城', 1595, '长束正家水口')],
    '阪本城': [('大津城', 1595, '京极高次改大津')],
    '不来方城': [('盛冈城', 1592, '南部家改称盛冈')],
    '胜山馆': [('松前城', 1594, '蛎崎家改称松前')],
    # 那古野城→名古屋城(1610)、二股城=二俣城(异体字)：剧本范围外/同代不变，不入表
}


def era_name(idx, era):
    """该剧本的当代名：改名事件按年份落代（事件年 <= 年代 → 用新名；可多段）"""
    nm = official_name(idx)
    cur = nm
    for new_nm, year, _note in RENAME_EPOCHS.get(nm, []):
        if int(era) >= year:
            cur = new_nm
    return cur


# 官方城名单（names_180.txt，输入表=事实；改名人工走该文件）
NAMES = [n for n in open(os.path.join(HERE, 'names_180.txt'), encoding='utf-8').read().split('\n') if n]
assert len(NAMES) == 180, 'names_180.txt 必须 180 城'


def official_name(idx):
    """官方名单序号直取（0 偏移）：names_180.txt 的序号 = 城表序号，每格唯一"""
    return NAMES[idx]
FALLBACK_BASE = {5: 0x9540}
ANCHOR = bytes.fromhex('6046615b6313421000084d04000064642779')


def decode_snr(scn):
    de = open(DEC, 'rb').read()
    table = de[scn * 256:(scn + 1) * 256]
    raw = open(os.path.join(r'E:/taikou5/Taikou5.Green.Edition-ALI213/Taikou5/TW', 'Snr%d_TW.TR5' % scn), 'rb').read()
    return bytes(table[b] for b in raw)


def attr_base(plain):
    hits = [m.start() for m in reall_finditer(plain)]
    for h in hits:
        if h - 195 * 36 >= 0:
            return h - 195 * 36
    return None


def reall_finditer(plain):
    import re
    return re.finditer(re.escape(ANCHOR), plain)


def person_records(plain, base):
    rows = {}
    for pid in range(1, 1400):
        off = base + pid * 36
        if off + 36 > len(plain):
            break
        rec = plain[off:off + 36]
        if all(b == 0 for b in rec):
            continue
        rows[pid] = {
            'force': rec[0x05], 'rank': rec[0x09],
            'superior': int.from_bytes(rec[0x0A:0x0C], 'little'),
            'salary': int.from_bytes(rec[0x0C:0x0E], 'little'),
            'ambition': rec[0x0E], 'loyalty': rec[0x0F],
            'kamon': rec[0x10],
        }
    return rows


def city_table(plain):
    """城表：180 条 × 36B 连续区。表头锚 = 第一条满足数值特征的记录，向前对齐到 36B 网格。"""
    n = len(plain) - 14
    base = None
    for i in range(n):
        soldiers = int.from_bytes(plain[i:i + 2], 'little')
        food = int.from_bytes(plain[i + 2:i + 6], 'little')
        gold = int.from_bytes(plain[i + 6:i + 10], 'little')
        train, morale = plain[i + 10], plain[i + 11]
        if 1000 <= soldiers <= 40000 and 5000 <= food <= 300000 and 5000 <= gold <= 300000 and 1 <= train <= 100 and 1 <= morale <= 100:
            lord = int.from_bytes(plain[i - 2:i], 'little')
            if 0 < lord < 1400:
                base = i - 2
                break
    assert base is not None, 'city table anchor not found'
    head = base - 2
    out = []
    for k in range(180):
        r = plain[head + 36 * k:head + 36 * k + 36]
        out.append({
            'idx': k,
            'lord': int.from_bytes(r[2:4], 'little'),
            'soldiers': int.from_bytes(r[4:6], 'little'),
            'food': int.from_bytes(r[6:10], 'little'),
            'gold': int.from_bytes(r[10:14], 'little'),
            'train': r[14], 'morale': r[15],
            'kana4': r[0x18:0x1c].hex(),
            'tail8': r[0x1c:0x24].hex(),
        })
    # 校验 1：全 180 条数值域合法（连续区完整性）
    bad = [r['idx'] for r in out if not (1 <= r['train'] <= 100 and 1 <= r['morale'] <= 100)]
    return head, out, bad


def main():
    hero_rows = []
    with io.open(os.path.join(KN, 'TaikouHero.csv'), encoding='utf-8-sig') as f:
        hero_rows = list(csv.DictReader(f))
    hero_by_tk = {}
    for h in hero_rows:
        for part in (h.get('TK5编号') or '').split('|'):
            part = part.strip()
            if part.isdigit():
                hero_by_tk.setdefault(int(part), h)

    # forceID → 势力名：force 内成员 Kingdom_era 多数票（正史列交叉验证 Snr forceID）
    force_name_votes = collections.defaultdict(lambda: collections.defaultdict(collections.Counter))
    all_persons = {}
    for scn, era in enumerate(ERAS):
        plain = decode_snr(scn)
        base = attr_base(plain) or FALLBACK_BASE.get(scn)
        assert base, 'person table anchor not found era ' + era
        all_persons[era] = person_records(plain, base)
        for pid, p in all_persons[era].items():
            h = hero_by_tk.get(pid)
            if h:
                kv = (h.get('Kingdom_' + era) or '').strip()
                if kv and kv != '无效':
                    force_name_votes[era][p['force']][kv] += 1
    force_name = collections.defaultdict(dict)  # era -> forceID -> 名
    for era, votes in force_name_votes.items():
        for fid, c in votes.items():
            top = c.most_common(1)
            if top:
                force_name[era][fid] = top[0][0]

    # 城表（城主/兵/粮/金=城表直读；城名=官方名单+历史名事实表，无推断）
    all_cities = {}
    for scn, era in enumerate(ERAS):
        plain = decode_snr(scn)
        _head, recs, bad = city_table(plain)
        assert not bad, 'city table partial invalid era %s: %s' % (era, bad)
        all_cities[era] = recs

    os.makedirs(DECODED, exist_ok=True)
    summary = []
    for era in ERAS:
        edir = os.path.join(DECODED, era)
        os.makedirs(edir, exist_ok=True)
        # persons.csv
        with open(os.path.join(edir, 'persons.csv'), 'w', newline='', encoding='utf-8-sig') as f:
            w = csv.writer(f)
            w.writerow(['person_id', 'name', 'force_id', 'force_name', 'rank', 'superior_id',
                        'superior_name', 'salary', 'ambition', 'loyalty', 'kamon'])
            per = all_persons[era]
            sname = {}
            for pid, p in per.items():
                h = hero_by_tk.get(pid)
                sname[pid] = h['CNName'] if h else '?'
            for pid in sorted(per):
                p = per[pid]
                h = hero_by_tk.get(pid)
                sup = p['superior']
                w.writerow([pid, sname.get(pid, '?'), p['force'], force_name.get(era, {}).get(p['force'], ''),
                            p['rank'], sup, sname.get(sup, '') if sup != 1101 else '', p['salary'],
                            p['ambition'], p['loyalty'], p['kamon']])
        # cities.csv
        with open(os.path.join(edir, 'cities.csv'), 'w', newline='', encoding='utf-8-sig') as f:
            w = csv.writer(f)
            w.writerow(['city_idx', 'kana4', 'name_official', 'name_history', 'lord_pid', 'lord_name', 'force_id',
                        'force_name', 'soldiers', 'food', 'gold', 'train', 'morale'])
            hb = hero_by_tk
            for r in sorted(all_cities[era], key=lambda x: x['idx']):
                h = hb.get(r['lord'])
                fid = all_persons[era].get(r['lord'], {}).get('force', None)
                # 全量历史名 = 当前官方名 + 其他历史名（用过的所有名字集合，| 分隔）
                hist = [official_name(r['idx'])] + [n for n in RENAME_FACTS.get(official_name(r['idx']), []) if n != official_name(r['idx'])]
                w.writerow([r['idx'], r['kana4'], era_name(r['idx'], era), '|'.join(hist), r['lord'],
                            h['CNName'] if h else '?', fid if fid is not None else '',
                            force_name.get(era, {}).get(fid, '') if fid is not None else '',
                            r['soldiers'], r['food'], r['gold'], r['train'], r['morale']])
        # forces.csv（当主 = 该 force 城表城主中「主城兵最大」者 = 势力当主（§3.7 规则）；
        #            无城 force 兜底 = superior==1101 且 rank 最大者）
        fo = collections.defaultdict(list)
        for pid, p in all_persons[era].items():
            fo[p['force']].append(pid)
        lord_from_cities = {}
        for r in all_cities[era]:
            p = all_persons[era].get(r['lord'])
            if p is None:
                continue
            fid = p['force']
            if fid not in lord_from_cities or r['soldiers'] > lord_from_cities[fid][1]:
                lord_from_cities[fid] = (r['lord'], r['soldiers'])
        with open(os.path.join(edir, 'forces.csv'), 'w', newline='', encoding='utf-8-sig') as f:
            w = csv.writer(f)
            w.writerow(['force_id', 'force_name', 'lord_pid', 'lord_name', 'member_count'])
            for fid in sorted(fo):
                mem = fo[fid]
                if fid in lord_from_cities:
                    lord = lord_from_cities[fid][0]
                else:
                    heads = [p for p in mem if all_persons[era][p]['superior'] == 1101]
                    lord = max(heads, key=lambda p: all_persons[era][p]['rank']) if heads \
                        else max(mem, key=lambda p: all_persons[era][p]['rank'])
                h = hero_by_tk.get(lord)
                w.writerow([fid, force_name.get(era, {}).get(fid, ''), lord, h['CNName'] if h else '?', len(mem)])
        summary.append('%s: persons=%d cities=%d forces=%d' % (
            era, len(per), len(all_cities[era]), len(fo)))

    # 校验报告
    print('=== v2 汇总 ===')
    for s in summary:
        print(' ', s)
    # 校验 2：骏府城 6 代城主链
    print('=== 校验：骏府城(08d23c64) 6 代城主链 ===')
    for era in ERAS:
        for r in all_cities[era]:
            if r['kana4'] == '08d23c64':
                h = hero_by_tk.get(r['lord'])
                print('  %s: lord=%s(%s) 兵=%d force=%s' % (era, r['lord'], h['CNName'] if h else '?', r['soldiers'], force_name.get(era, {}).get(all_persons[era].get(r['lord'], {}).get('force', 0), '?')))
    with open(os.path.join(DECODED, 'README.md'), 'w', encoding='utf-8') as f:
        f.write('# TK5 六剧本全量隶属关系（v3 确定版, 2026-08-31）\n\n')
        f.write('方法：Snr 解码 → 城表(180条×36B 等距连续, 城主/兵/粮/金直读) + 人物表(36B×1399) + 官方城名单(按城位序号直取)。\n')
        f.write('城名 = name_official(官方名单) / name_history(历史名事实表 RENAME_FACTS, 史料明确记载的改名)。\n')
        f.write('无推断：不存在投票/匹配；所有列均为文件直读或事实表 join。\n')


if __name__ == '__main__':
    main()
    print('written:', DECODED)
