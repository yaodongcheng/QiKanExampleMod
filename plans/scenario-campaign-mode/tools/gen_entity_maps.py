# -*- coding: utf-8 -*-
"""
gen_entity_maps.py —— 从《骑砍2太阁Mod表.xlsx》生成实体归一表 `tools/entity_maps.py`。

解决什么问题
------------
tk5_to_json.py 原来手写了 6 张名字→StringId 的字典（HERO_MAP / AGENT_MAP / CLAN_MAP /
KINGDOM_MAP / SETTLEMENT_MAP / REGION_MAP），只覆盖桶狭间那几十个人。全量 2594 事件一开跑，
手写表必然漏，而且漏了不报错——直接生成 `tk5_uXXXXXX` 哈希占位，静默错到底。

本脚本把这张表换成从织丰数据表机器生成：
    Knowledge/骑砍2织丰角色ID对应/csv/*.csv                （xlsx_to_csv.py 从织丰表转换的上游镜像）
        + Modules/ShokuhoTaikouExpansionPack/ModuleData/{Shokuho,DesignData}/*.xml  （存在性核对）
        → tools/entity_maps.py                          （生成物，禁止手改，铁律 22）

三条纪律
--------
1. **铁律 20**：产出的全是游戏内 StringId，中文只作查找键，不进 ID。
2. **铁律 22**：`entity_maps.py` 是生成物；要改映射 → 改本脚本（OVERRIDES / 后缀规则）→ 重跑。
3. **铁律 5**：每个 ID 都拿模块 XML 核对过存在性；核不到的进 `MISSING_IN_XML`，翻译器照常用
   （数据包可能后补），但报告里点名，禁止静默。

繁简
----
太阁源文是繁体（織田信長），织丰表是简体（织田信长）。生成期用 zhconv 把简体名转成繁体，
**两种写法都进表**，运行时不需要 zhconv 依赖。zhconv 缺失时只生成简体键并告警。

用法
----
    python tools/xlsx_to_csv.py             # 上游织丰表更新后：刷新镜像 CSV（一次性转换，见该脚本）
    python tools/gen_entity_maps.py            # 生成 + 打统计
    python tools/gen_entity_maps.py --report   # 只打统计与缺口清单，不写文件
"""
from __future__ import unicode_literals
import io
import os
import re
import sys
import csv
import collections
import hashlib

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))          # LivingWorldNpcs
MODULES = os.path.dirname(ROOT)                                        # …/Modules
CSV_DIR = os.path.join(ROOT, 'Knowledge', '骑砍2织丰角色ID对应', 'csv')
SHOKUHO = os.path.join(MODULES, 'ShokuhoTaikouExpansionPack', 'ModuleData')
# 🔴 活数据在**基础织丰 mod**（Modules/Shokuho），不在扩展包：扩展包的 Shokuho/settlements.xml
# 只有 5 条示例、spkingdoms.xml 只有 1 条。存在性核对必须扫基础 mod，否则会误报几百条缺失。
SHOKUHO_BASE = os.path.join(MODULES, 'Shokuho', 'ModuleData')
P_OUT = os.path.join(HERE, 'entity_maps.py')

# 剧本年份（桶狭间 = 1560 日轮之章）——决定取哪一组「登场/身份/势力/据点」列
SCENARIO_YEAR = '1560'

try:
    import zhconv
    def to_trad(s):
        return zhconv.convert(s, 'zh-hant')
    HAS_ZH = True
except Exception:                                                      # pragma: no cover
    def to_trad(s):
        return s
    HAS_ZH = False


# ---------------------------------------------------------------------------
# 人工覆盖：太阁原文里的写法 ≠ 织丰表里的写法（生成期一次性对齐，运行时无中文参与）
# ---------------------------------------------------------------------------
NAME_ALIAS = {
    # 太阁写法 → 织丰表写法（简体）
    '豐臣秀吉': '木下藤吉郎',
    '豐臣秀長': '木下小一郎',
    '德川家康': '松平元康',
    '寧寧': '宁宁',
}

# 势力别名：太阁按 1560 年的家名叫，织丰表按代表家名建 Kingdom
# （长尾景虎 1561 年才继上杉家；织丰表只有 uesugi 一条，所以 1560 年的「长尾家」要指过去）
KINGDOM_ALIAS = {
    '长尾': '上杉',
}

# 太阁独有、织丰表里没有的角色 → 占位 ID（有主占位，07 数据包补齐后回填这里重跑）
TK5_ONLY_HERO = {
    '服部小平太': 'tk5_hattori_koheita',
    '毛利新介': 'tk5_mori_shinsuke',
    '簗田政綱': 'tk5_yanada_masatsuna',
}

# 模板角色（无 Hero 身份的路人）→ Agent:: 模板引用；织丰表里不会有，本表就是事实源
AGENT_MAP = {
    '忍者': 'tk5_ninja', '小姓': 'tk5_kosho', '家臣': 'tk5_kashin', '傳令': 'tk5_denrei',
    '侍從': 'tk5_jiju', '足輕': 'tk5_ashigaru', '備大將': 'tk5_bitaisho', '部將': 'tk5_busho',
    '武將': 'tk5_busho_generic', '僧侶': 'tk5_monk', '旅人': 'tk5_traveler',
    '守將': 'tk5_shusho', '守軍': 'tk5_shugun', '今川兵': 'tk5_imagawa_soldier',
    '士兵': 'tk5_soldier', '功勳家臣': 'tk5_kashin_merit', '武力家臣': 'tk5_kashin_martial',
    '外交家臣': 'tk5_kashin_diplomat', '功勳陪臣': 'tk5_hikan_merit',
}

# 地方（令制国）——织丰表**有**分区数据，但不是这一层：CityTaikou.Area 是 62 个细分区，
# Culture 表是 9 个文化大区（畿内/关东/东海/北陆/东山/山阳/南海/西海/奥羽）。太阁的
# `地方::近畿` 用的是现代地方名，与 Culture 的古代八道一一对得上（近畿=畿内、中国=山阳、
# 四国=南海、九州=西海、东北=奥羽、甲信=东山），只是叫法不同——缺的是这张 9 行对照表，
# 不是数据（07b §五-1）。令制国这一层织丰确实没有，下表兜底。
REGION_MAP = {
    '駿河': 'tk5_suruga', '遠江': 'tk5_totomi', '三河': 'tk5_mikawa', '尾張': 'tk5_owari',
    '美濃': 'tk5_mino', '伊勢': 'tk5_ise', '近江': 'tk5_omi', '山城': 'tk5_yamashiro',
    '甲斐': 'tk5_kai', '信濃': 'tk5_shinano', '相模': 'tk5_sagami', '武藏': 'tk5_musashi',
}

# 🔴 据点两列的语义不一样，不能混用（2026-08-28 查表实证）：
#   MatchSettlement = 骑砍地图上就是这座城（99 条）→ 直接用它的 StringId；
#   NearSettlement  = 骑砍地图上**没有**这座城，只给了最近的一个（81 条）→ 拿它当同一个地方就错了
#                     （太阁「鸣海城」的 near 是「那古野」——这是两座城，用了会让鸣海攻防打到那古野去）。
#   所以：只有 near 的城 → 自己发一个 tk5_city_NNN 占位 ID（07 数据包补真城），
#         同时把 near 记进 SETTLEMENT_ANCHOR，让事件知道该在地图哪一带发生。
# 例外白名单：near 其实就是同一座城，只是汉字写法不同（清洲/清须、河越/川越…），这些直接用真 ID。
SAME_AS_NEAR = {
    '清洲城': '清须城',      # 清洲 = 清須，织田本城
    '河越城': '川越城',      # 河越 = 川越
    '阪本城': '坂本',        # 阪本 = 坂本
    '踯躅崎馆': '甲府城',    # 武田居馆即甲府
    '石山本愿寺': '大坂御坊',  # 石山 = 大坂
    '芥川城': '芥川山城',
}

# 据点名后缀：太阁「鳴海」/「鳴海城」/「岡崎之町」↔ 织丰「鸣海城」
SUFFIXES = ('', '城', '馆', '館', '之町', '町', '之砦', '砦', '港', '之港')


# ---------------------------------------------------------------------------
# 读织丰表（CSV 镜像，由 tools/xlsx_to_csv.py 从《骑砍2太阁Mod表.xlsx》转换）
# ---------------------------------------------------------------------------
def read_sheets():
    """读 csv/ 下一张 sheet 一个 CSV。返回同旧格式：{sheet名: [dict]}。

    列名 = 表头行；数据行缺列补 ''；类型/注释行（string/int/编号/骑砍ID/内置番号 开头）
    跳过（织丰表格式，不是数据）。
    🔴 镜像 CSV 禁止手改（铁律 22 精神）：改映射 → 本文件映射表；改数据 → 改 xlsx 后
       重跑 xlsx_to_csv.py 刷新镜像。
    """
    sheets = {}
    if not os.path.isdir(CSV_DIR):
        print('找不到 CSV 数据目录：%s（先跑 `python tools/xlsx_to_csv.py` 从织丰表转换）' % CSV_DIR)
        return None
    for fn in sorted(f for f in os.listdir(CSV_DIR) if f.endswith('.csv')):
        with io.open(os.path.join(CSV_DIR, fn), encoding='utf-8-sig', newline='') as f:
            rows = list(csv.reader(f))
        if not rows:
            continue
        head = rows[0]
        body = [r for r in rows[1:] if any(r)]
        # 第 2~3 行 = 类型行 / 中文注释行（织丰表格式），不是数据
        while body and body[0] and body[0][0] in ('string', 'int', '编号', '骑砍ID', '内置番号'):
            body.pop(0)
        sheets[fn[:-4]] = [dict(zip(head, r + [''] * (len(head) - len(r)))) for r in body]
    return sheets


def keys_of(name):
    """一个中文名 → 查找键集合（原样 + 繁体）。"""
    ks = {name}
    t = to_trad(name)
    if t:
        ks.add(t)
    return ks


def put(d, name, value, conflicts, tag):
    for k in keys_of(name):
        if k in d and d[k] != value:
            conflicts.append('%s：「%s」既指向 %s 又指向 %s（保留先到的）' % (tag, k, d[k], value))
            continue
        d.setdefault(k, value)


# ---------------------------------------------------------------------------
# 模块 XML 存在性核对（铁律 5）
# ---------------------------------------------------------------------------
def xml_ids(*globs):
    """扫模块 XML 收集所有 id=""。参数是相对 Modules/ 的 glob（基础 mod + 扩展包都扫）。"""
    import glob as _g
    ids = set()
    for pat in globs:
        for p in _g.glob(os.path.join(MODULES, *pat.split('/'))):
            if not os.path.isfile(p):
                continue
            txt = io.open(p, encoding='utf-8', errors='replace').read()
            ids |= set(re.findall(r'id="([^"]+)"', txt))
    return ids


def main():
    report_only = '--report' in sys.argv
    sh = read_sheets()
    if sh is None:
        return 1
    need = ('TaikouHero', 'Clan', 'Kingdom', 'Settlements', 'CityTaikou')
    for n in need:
        if n not in sh:
            print('xlsx 缺工作表「%s」（现有：%s）' % (n, ' / '.join(sh)))
            return 1

    conflicts = []
    year = SCENARIO_YEAR

    # ---- 据点：CityTaikou（太阁城）→ Settlements（骑砍据点）----
    settle_by_name = collections.defaultdict(list)
    settle_ids = set()
    for r in sh['Settlements']:
        if r.get('ID'):
            settle_by_name[r.get('CityName', '')].append(r['ID'])
            settle_ids.add(r['ID'])

    def find_settle(n):
        if not n:
            return None
        for s in SUFFIXES:
            if n + s in settle_by_name:
                return settle_by_name[n + s][0]
        for s in ('城', '馆', '館', '之町', '之砦'):
            if n.endswith(s) and n[:-len(s)] in settle_by_name:
                return settle_by_name[n[:-len(s)]][0]
        return None

    def strip_suffix(n):
        return re.sub('(城|馆|館|御所|之町|町|之砦|砦|湊|港)$', '', n or '')

    SETTLEMENT_MAP, SETTLEMENT_ANCHOR = {}, {}
    placeholder_city, anchorless_city = [], []
    for r in sh['CityTaikou']:
        cn = r.get('ChineseName', '')
        if not cn:
            continue
        near = r.get('NearSettlement', '')
        sid = find_settle(r.get('MatchSettlement', ''))
        if not sid and near:
            # near 与本城同名（只差后缀）或在白名单里 → 同一座城；否则只当锚点
            if strip_suffix(near) == strip_suffix(cn) or SAME_AS_NEAR.get(cn) == near:
                sid = find_settle(near)
        if not sid:
            sid = find_settle(cn)                     # 城名本身在 Settlements 里
        anchor = None
        if not sid:
            sid = 'tk5_city_%03d' % int(r.get('ID') or 0)
            anchor = find_settle(near)
            (placeholder_city if anchor else anchorless_city).append(cn)

        # 🔴 同一座城的不同区都指向同一个据点：太阁把「岡崎城」（城）和「岡崎之町」（町区）
        # 当两个地点写，骑砍这边 town_CHUB10 一个据点就把城和町都包了。不注册变体键的话，
        # 「岡崎之町」查不到 → 发独立占位 → 事件里「筛选所属据点=岡崎之町的人」一个也筛不到。
        # 🔴 据点表和锚点表必须挂**同一套查找键**（2026-08-28 修）：原来锚点只挂在表里的正名
        # （鳴海城）上，事件里写的是别名（鳴海館）→ 据点查得到、锚点查不到，报告里显示成
        # 「占位据点无锚点」，事件就不知道该在地图哪一带发生。实测键覆盖率只有 20.7%。
        names = [cn]
        base = re.sub('(城|馆|館|之町|町|之砦|砦)$', '', cn)
        if base and base != cn:
            names.append(base)
            names.extend(base + suf for suf in ('城', '之町', '町', '之砦', '砦',
                                                '館', '馆', '港', '之港') if base + suf != cn)
        for n in names:
            put(SETTLEMENT_MAP, n, sid, conflicts, '据点')
            if not anchor:
                continue
            for k in keys_of(n):
                if SETTLEMENT_MAP.get(k) == sid:      # 键确实指向这座占位城，才给它挂锚点
                    SETTLEMENT_ANCHOR.setdefault(k, anchor)

    # ---- 势力：Kingdom 表 ----
    kingdom_ids = set()
    KINGDOM_BY_NAME = {}
    for r in sh['Kingdom']:
        if not r.get('ID'):
            continue
        kingdom_ids.add(r['ID'])
        for n in (r.get('ChineseName', ''), (r.get('ChineseName', '') + '家')):
            if n:
                put(KINGDOM_BY_NAME, n, r['ID'], conflicts, '势力')

    def find_kingdom(n):
        if not n or n == '无':
            return None
        bare = re.sub('家$', '', n)
        bare = KINGDOM_ALIAS.get(bare, bare)
        return KINGDOM_BY_NAME.get(n) or KINGDOM_BY_NAME.get(bare)

    # ---- 家族：Clan 表 ----
    clan_ids = set(r['ID'] for r in sh['Clan'] if r.get('ID'))

    # ---- 人物：TaikouHero 表（本剧本年份那一组列）----
    hero_rows = [r for r in sh['TaikouHero'] if r.get('ID')]
    alias_rev = {}
    for tk, zf in NAME_ALIAS.items():
        alias_rev.setdefault(zf, []).append(tk)

    HERO_MAP, HERO_META, CLAN_BY_HERO, KINGDOM_BY_HERO = {}, {}, {}, {}
    org_names = collections.Counter()
    for r in hero_rows:
        cn = r.get('CNName', '')
        if not cn:
            continue
        hid = r['ID']
        names = [cn] + alias_rev.get(cn, [])
        # 本年份的姓名列（改名角色：木下藤吉郎 → 羽柴秀吉）
        yname = r.get('Name_%s' % year, '')
        if yname and yname not in names:
            names.append(yname)
        for n in names:
            put(HERO_MAP, n, hid, conflicts, '人物')
        kname = r.get('Kingdom_%s' % year, '')
        kid = find_kingdom(kname)
        if kname and kname != '无' and not kid:
            org_names[kname] += 1
        HERO_META[hid] = {
            'clan': r.get('ClanID', ''),
            'kingdom': kid or '',
            'kingdom_name': kname,
            'city': SETTLEMENT_MAP.get(r.get('City_%s' % year, ''), ''),
            'appear': r.get('Appear_%s' % year, ''),
            'identity': r.get('Identity_%s' % year, ''),
            'stance': r.get('CareerStance_%s' % year, ''),
        }
        for n in names:
            if r.get('ClanID'):
                put(CLAN_BY_HERO, n, r['ClanID'], conflicts, '家族')
            if kid:
                put(KINGDOM_BY_HERO, n, kid, conflicts, '势力(按人)')

    for n, hid in TK5_ONLY_HERO.items():
        put(HERO_MAP, n, hid, conflicts, '人物')
        HERO_META.setdefault(hid, {'clan': '', 'kingdom': '', 'kingdom_name': '',
                                   'city': '', 'appear': '太阁独有', 'identity': '', 'stance': ''})

    # ---- 存在性核对 ----
    xml_hero = xml_ids('Shokuho/ModuleData/heroes/*.xml', 'Shokuho/ModuleData/lords/*.xml',
                       'ShokuhoTaikouExpansionPack/ModuleData/*/heroes.xml',
                       'ShokuhoTaikouExpansionPack/ModuleData/*/lords.xml')
    xml_clan = xml_ids('Shokuho/ModuleData/spclans/*.xml',
                       'ShokuhoTaikouExpansionPack/ModuleData/*/clans.xml')
    xml_kingdom = xml_ids('Shokuho/ModuleData/spkingdoms/*.xml')
    xml_settle = xml_ids('Shokuho/ModuleData/settlements.xml',
                         'Shokuho/ModuleData/port_location_settlements.xml',
                         'Shokuho/ModuleData/*_location_settlements.xml')
    missing = collections.OrderedDict()
    def check(label, ids, pool):
        if not pool:
            return []
        bad = sorted(i for i in ids if i not in pool and not i.startswith('tk5_'))
        if bad:
            missing[label] = bad
        return bad
    check('人物', set(HERO_MAP.values()), xml_hero)
    check('家族', clan_ids, xml_clan)
    check('势力', kingdom_ids, xml_kingdom)
    check('据点', settle_ids, xml_settle)

    # ---- 统计 ----
    print('== 织丰表 → 实体归一表 ==')
    print('人物 %d 行 → %d 个 ID，查找键 %d 个%s'
          % (len(hero_rows), len(set(HERO_MAP.values())), len(HERO_MAP),
             '' if HAS_ZH else '（⚠️ 无 zhconv，只生成了简体键，繁体源文会查不到）'))
    real_city = set(v for v in SETTLEMENT_MAP.values() if not v.startswith('tk5_city_'))
    print('家族 %d ／ 势力 %d ／ 据点 %d' % (len(clan_ids), len(kingdom_ids), len(settle_ids)))
    print('太阁城 %d 条：对上骑砍真城 %d 座，占位 %d 座（其中 %d 座连锚点都没有）'
          % (len(sh['CityTaikou']), len(real_city),
             len(placeholder_city) + len(anchorless_city), len(anchorless_city)))
    if anchorless_city:
        print('  连最近据点都查不到（07 数据包要连位置一起定）：' + '、'.join(anchorless_city))
    if org_names:
        print('  %d 个「势力」在 Kingdom 表里没有条目 = 组织（水军/众/屋）→ 走 Org:: 占位（16b T3-预留）：%s'
              % (len(org_names), '、'.join(n for n, _ in org_names.most_common(8)) + ' …'))
    for label, bad in missing.items():
        print('  ⚠️ %s：%d 个 ID 在模块 XML 里查不到 → %s' % (label, len(bad), '、'.join(bad[:6])))
    for c in conflicts[:10]:
        print('  冲突 ' + c)
    if len(conflicts) > 10:
        print('  …还有 %d 条冲突' % (len(conflicts) - 10))

    if report_only:
        return 0

    # ---- 生成 ----
    def lit(s):
        return "'" + str(s).replace('\\', '\\\\').replace("'", "\\'") + "'"

    def dump(name, d, comment):
        buf = ['# %s\n' % comment, '%s = {\n' % name]
        for k in sorted(d):
            buf.append('    %s: %s,\n' % (lit(k), lit(d[k])))
        buf.append('}\n\n')
        return ''.join(buf)

    out = ['# -*- coding: utf-8 -*-\n',
           '# 🔴 自动生成，勿手改（铁律 22）。由 tools/gen_entity_maps.py 从\n',
           '# Knowledge/骑砍2织丰角色ID对应/csv/*.csv（xlsx_to_csv.py 从织丰表转换的镜像）生成，'
           '剧本年份 = %s。\n' % year,
           '# 要改映射 → 改 gen_entity_maps.py（NAME_ALIAS / TK5_ONLY_HERO / SUFFIXES）→ 重跑。\n',
           'from __future__ import unicode_literals\n\n',
           'SCENARIO_YEAR = %s\n\n' % lit(year)]
    out.append(dump('HERO_MAP', HERO_MAP, '中文名（繁/简）→ Hero StringId'))
    out.append(dump('CLAN_BY_HERO', CLAN_BY_HERO, '当主名 → Clan StringId（太阁 `大名家::織田信長` = 织田信长的家）'))
    out.append(dump('KINGDOM_BY_HERO', KINGDOM_BY_HERO, '当主名 → Kingdom StringId（%s 年在籍势力）' % year))
    out.append(dump('KINGDOM_BY_NAME', KINGDOM_BY_NAME, '势力名 → Kingdom StringId'))
    out.append(dump('SETTLEMENT_MAP', SETTLEMENT_MAP,
                    '太阁据点名 → Settlement StringId（tk5_city_NNN = 骑砍地图上没有这座城，07 数据包补）'))
    out.append(dump('SETTLEMENT_ANCHOR', SETTLEMENT_ANCHOR,
                    '占位据点 → 最近的骑砍真据点（决定事件在地图哪一带发生，不是同一个地方）'))
    out.append(dump('AGENT_MAP', AGENT_MAP, '模板角色名 → CharacterObject 模板占位 ID（织丰表无此类，本表即事实源）'))
    out.append(dump('REGION_MAP', REGION_MAP, '令制国名 → Region 占位 ID（织丰表只到「文化大区/细分区」两层，无令制国层，本表兜底）'))
    out.append('# 人物在本剧本年份的状态：{hero_id: {clan, kingdom, kingdom_name, city, appear, identity, stance}}\n')
    out.append('HERO_META = {\n')
    for hid in sorted(HERO_META):
        m = HERO_META[hid]
        out.append('    %s: {%s},\n' % (lit(hid), ', '.join(
            '%s: %s' % (lit(k), lit(m[k])) for k in ('clan', 'kingdom', 'kingdom_name',
                                                     'city', 'appear', 'identity', 'stance'))))
    out.append('}\n\n')
    out.append('# 在 Kingdom 表里查不到的「势力」= 组织（忍者众/海贼众/商家），走 Org:: 占位\n')
    out.append('ORG_NAMES = {\n')
    for n in sorted(org_names):
        oid = 'tk5_org_' + hashlib.md5(n.encode('utf-8')).hexdigest()[:6]
        for k in sorted(keys_of(n)):
            out.append('    %s: %s,\n' % (lit(k), lit(oid)))
    out.append('}\n\n')
    out.append('# 模块 XML 里查不到的 ID（数据包待补；翻译器照常引用，报告点名）\n')
    out.append('MISSING_IN_XML = {\n')
    for label, bad in missing.items():
        out.append('    %s: [%s],\n' % (lit(label), ', '.join(lit(b) for b in bad)))
    out.append('}\n')
    io.open(P_OUT, 'w', encoding='utf-8').write(''.join(out))
    print('已生成 %s' % os.path.relpath(P_OUT, ROOT))
    return 0


if __name__ == '__main__':
    sys.exit(main())
