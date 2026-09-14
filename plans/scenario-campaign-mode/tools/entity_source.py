# -*- coding: utf-8 -*-
r"""entity_source.py —— 具名实体查找表（**每次 import 从 CSV 现读现建**）

为什么要这么写（2026-09-14 用户裁定）
------------------------------------
原来这里是一对「生成器 + 生成物」：`gen_entity_maps.py` 把一个 625KB 的
`entity_maps.py` 写到磁盘，翻译器 import 那个文件。实测该生成物**冻结在 08-31**，
而 CSV 已更新到 09-12；生成器本身在目录迁移（`Knowledge/骑砍2织丰角色ID对应`
→ `Knowledge/太阁5/骑砍2织丰角色ID对应`）后路径失配、**根本跑不起来**。
于是出现最坏的组合：数据在走、产物不动、没人发现。

现在改成：**CSV 是唯一事实源，进程启动时现读现建**——没有中间产物、
没有过期、没有分叉。改了 CSV 立刻生效，不需要"记得重跑某个生成器"。

数据来源（全部在 `Knowledge/太阁5/骑砍2织丰角色ID对应/csv/`，两行表头规范）
--------------------------------------------------------------------------
    TaikouHero.csv    人物 + 模板 NPC（TemplateNPC=1 行）      → HERO_MAP / AGENT_MAP / HERO_META
    Clan.csv          家族（当主/所属势力 按年代列）             → CLAN_BY_HERO
    TaikouForce.csv   势力（大名家/商家/忍者众/海贼众，含别名）  → KINGDOM_BY_NAME / KINGDOM_BY_HERO / ORG_NAMES
    Settlements.csv   据点（全部名称 = 别名集，id 列 = 游戏 StringId） → SETTLEMENT_MAP
    Kuni.csv          令制国 66 条（国:: 引用）                 → REGION_MAP
    Region.csv        地方 10 条（地方:: 引用）                 → REGION_MAP
    item.csv          物品/交易品的骑砍 StringId               → ITEM_MAP / MERC_T_MAP
    Modules/Taikou/ModuleData/*.xml  存在性核对（铁律 5）        → *_ORIGIN / MISSING_IN_XML

接口与历史上的 entity_maps.py 完全一致（消费方 tk5_to_json.py / check_base_coverage.py
不用改逻辑），只有 import 名从 entity_maps 换成 entity_source。
要加新实体域 = 加 CSV 表或列，**不要在本文件里写中文映射表**。
"""
from __future__ import unicode_literals

import collections
import csv
import glob
import io
import os
import re
import sys

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

# ---------------------------------------------------------------------------
# 路径
# ---------------------------------------------------------------------------
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))       # …/LivingWorldNpcs
MODULES = os.path.dirname(ROOT)                                     # …/Modules
CSV_DIR = os.path.join(ROOT, 'Knowledge', '太阁5', '骑砍2织丰角色ID对应', 'csv')
TAIKOU_MODULE = os.path.join(MODULES, 'Taikou')                     # 本内容包（游戏内 StringId 事实源）

sys.path.insert(0, os.path.join(ROOT, 'Scripts'))
try:
    from csv_dual import dict_rows                                 # noqa: E402
except ImportError:                                                 # pragma: no cover
    raise SystemExit('缺 Scripts/csv_dual.py —— 项目统一的两行表头 CSV 读写器')

# 剧本年份（桶狭间 = 1560 日轮之章）——决定取哪一组「登场/身份/势力/据点」列
SCENARIO_YEAR = '1560'

# 繁简：太阁源文是繁体（織田信長），CSV 主名是简体（织田信长）——两种写法都进查找表，
# 运行时不需要 zhconv 依赖。zhconv 缺失时只生成简体键（调用方看 SCENARIO 结束的告警）。
try:
    import zhconv

    def to_trad(s):
        return zhconv.convert(s, 'zh-hant')

    def to_simp(s):
        return zhconv.convert(s, 'zh-hans')
    HAS_ZH = True
except Exception:                                                   # pragma: no cover
    def to_trad(s):
        return s

    def to_simp(s):
        return s
    HAS_ZH = False

# 据点名后缀：太阁写「鳴海」/「鳴海城」/「岡崎之町」，骑砍表里是「鸣海城」
# 「里/之里」= 忍者村（伊贺之里/甲贺之里/轩猿之里/透波之里）——语料写「里::伊賀」少了后缀，
# 不登记这一类，忍者村就永远只能靠全名命中（实测 4 个全漏）。
SUFFIXES = ('', '城', '馆', '館', '之町', '町', '之里', '里', '之砦', '砦', '港', '之港')
# 全角中点（CSV 用）与表意中点（语料用）统一——「伊勢·志摩」vs「伊勢・志摩」
MIDDOTS = ('·', '・', '‧', '•')

# 🔴 日文新字体 → 中文写法（**显示变体归一，不是数据映射**）
#   为什么必须有这一层：太阁是日文游戏，语料用日本字形；CSV 的名字池是中文写法。
#   zhconv 只管**中文简繁**（專↔专），管不了**日本新字体**——日 姫（U+59EB）与中 姬（U+59EC）
#   是两个不同码点，转换器原样返回。实测（2026-09-14）：`姫路之町`/`雑賀`/`那覇`/`一乗谷`/
#   `安藝` 一批地名全因一个字形差查无。这类差异属**编码层**（同一件事的两种通常写法），
#   不该在数据里逐个补别名——这一层是「解码器」的一部分。
#   加登记 = 加一行；只登记见过实据的字，不猜。
VARIANTS = {
    '姫': '姬',      # 日 姫路 ↔ 中 姬路
    '雑': '杂',      # 日 雑賀 ↔ 中 杂贺
    '覇': '霸',      # 日 那覇 ↔ 中 那霸
    '乗': '乘',      # 日 一乗谷 ↔ 中 一乘谷
    '藝': '艺',      # 繁 安藝 ↔ 中 安艺
    '國': '国',      # 繁 中國 ↔ 中 中国（语料 `地方::中國`）
}

# 称呼变体：太阁语料用「◯公主」，CSV 名字池用「◯姬」（同一批人，两种叫法）。
# 数据侧已有零星收录（`德公主`/`菊公主` 在别名里），但不齐——放这一层统一，避免逐条补。
HIME_TITLES = (('公主', '姬'),)


def variant_forms(name):
    """名字 → 字形变体 / 称呼变体集合（原样 + 逐条规则）。

    两类：
      ① 单字字形替换（VARIANTS）：日文新字体 ↔ 中文写法，一次换一个字
      ② 称呼替换（HIME_TITLES）：`◯公主` ↔ `◯姬`——太阁语料用「公主」，CSV 名字池用「姬」
         （实测：`德公主`/`菊公主` 已作为别名收录、但 `冬公主`/`初公主` 等没有——
         逐条补别名会把同一约定抄 N 遍，故放这一层统一处理）
    """
    out = {name}
    for i, ch in enumerate(name):
        alt = VARIANTS.get(ch)
        if alt and alt != ch:
            out.add(name[:i] + alt + name[i + 1:])
    for a, b in HIME_TITLES:
        if a in name:
            out.add(name.replace(a, b))
    return out


def _csv(name):
    """读一张两行表头 CSV → [dict]（键 = 第 2 行英文键）。"""
    path = os.path.join(CSV_DIR, name)
    rows = dict_rows(path)
    if not rows:
        raise SystemExit('CSV 表为空或缺表头：%s' % path)
    return rows


def keys_of(name):
    """一个名字 → 查找键集合（原样 + 繁体 + 简体）。

    双向都要：CSV 主名是**简体**（织田信长 / 厩桥城），而语料是**繁体源文**
    （織田信長 / 厩橋之町）——只做简→繁单向，语料用繁体写时就查不到
    （实测：厩橋 / 姫路 / 雑賀 / 那覇 一批据点名全漏）。
    """
    ks = {name}
    t = to_trad(name)
    if t:
        ks.add(t)
    s = to_simp(name)
    if s:
        ks.add(s)
    return ks


def put(d, name, value, conflicts, tag):
    """写入所有写法；同键不同值记冲突（保留先到的）。

    三种写法都要进键：①原样 ②简繁互转（`keys_of`）③字形变体（`variant_forms`）。
    第三种只管**原样变体**——变体名再转简繁是另一轮，交给它自己的 `keys_of` 就够，
    否则键数指数膨胀。
    """
    if not name:
        return
    keys = set()
    for k in keys_of(name):
        keys |= variant_forms(k)
    for k in keys:
        if k in d and d[k] != value:
            conflicts.append('%s：「%s」既指向 %s 又指向 %s（保留先到的）'
                             % (tag, k, d[k], value))
            continue
        d.setdefault(k, value)


def lookup(table, name):
    """**正式查询入口**：一个源文名字 → 表里的值（查无 = None）。

    查表要展开「简繁 + 字形变体」再查——语料是日文繁体源文（`姫路之町`），表里的键是
    中文写法（`姬路之町`）。直接 `table.get(name)` 会漏掉每一个带变体字的名字。
    消费方一律走本函数，别裸 `.get()`。
    """
    if not name:
        return None
    cands = set()
    for k in keys_of(name):                   # 简繁
        cands |= variant_forms(k)             # 字形变体
        for v in variant_forms(k):
            cands |= keys_of(v)               # 变体后再简繁（中國 → 中国）
    for k in (name,) + tuple(cands):          # 原样优先
        if k in table:
            return table[k]
    return None


def lookup_settlement(name):
    """据点专用查询：除 `lookup` 的写法展开外，**再展开据点后缀**。

    太阁写「伊賀」，CSV 里是「伊贺之里」；写「鳴海」，表里是「鸣海城」——同一座城
    在两边带不带后缀不一致。建键时已按后缀规则生成过（`put` 那一侧），查询侧必须
    用同一规则反向展开，否则「伊賀」这种**比表里名短**的写法永远查不到。
    """
    if not name:
        return None
    v = lookup(SETTLEMENT_MAP, name)
    if v:
        return v
    for cand in variant_forms(name) | keys_of(name) | {strip_suffix(x) for x in variant_forms(name) | keys_of(name)}:
        base = strip_suffix(cand)
        if not base:
            continue
        for suf in SUFFIXES[1:]:
            v = lookup(SETTLEMENT_MAP, base + suf)
            if v:
                return v
    return None


def strip_suffix(n):
    return re.sub('(城|馆|館|御所|之町|町|之里|里|之砦|砦|湊|港)$', '', n or '')


def _norm_middot(s):
    for m in MIDDOTS[1:]:
        s = s.replace(m, MIDDOTS[0])
    return s


# ---------------------------------------------------------------------------
# 模块 XML 存在性核对（铁律 5：ID 引用前先确认它在本包里有定义）
# ---------------------------------------------------------------------------
def _xml_ids(*globs):
    """扫模块 XML 收集所有 id=""（参数相对 Modules/）。"""
    ids = set()
    for pat in globs:
        for p in glob.glob(os.path.join(MODULES, *pat.split('/'))):
            if not os.path.isfile(p):
                continue
            txt = io.open(p, encoding='utf-8', errors='replace').read()
            ids |= set(re.findall(r'\bid="([^"]+)"', txt))
    return ids


# ---------------------------------------------------------------------------
# 读源
# ---------------------------------------------------------------------------
_conflicts = []
_heroes = _csv('TaikouHero.csv')
_clans = _csv('Clan.csv')
_forces = _csv('TaikouForce.csv')
_settlements = _csv('Settlements.csv')
try:
    _items = _csv('item.csv')
except SystemExit:
    _items = []
try:
    _kuni = _csv('Kuni.csv')
except SystemExit:                                                   # 表未建 = 该域空，不挡其他域
    _kuni = []
try:
    _region = _csv('Region.csv')
except SystemExit:
    _region = []

_year = SCENARIO_YEAR

# ---- 势力 / 家族（TaikouForce = 一切政治集团：大名家 + 商家 + 忍者众 + 海贼众）----
_force_by_id = {}
for _r in _forces:
    _fid = (_r.get('ID') or '').strip()
    if not _fid:
        continue
    _names = [(_r.get('ForceName') or '').strip()]
    _names += [x.strip() for x in (_r.get('Alias') or '').split('|')]
    _force_by_id[_fid] = [n for n in _names if n]

# ---- 家族（Clan.csv：当主_年 / 所属势力_年 是年代列，一读直达）----
_clan_name = {}          # clan_id → 家名（如「织田」）
_clan_owner = {}         # clan_id → 当主 hero_id（本剧本年份）
_clan_kingdom = {}       # clan_id → 势力 id
for _r in _clans:
    _cid = (_r.get('ID') or '').strip()
    if not _cid:
        continue
    _clan_name[_cid] = (_r.get('Name') or '').strip()
    _owner = (_r.get('Owner_%s' % _year) or '').strip()
    _king = (_r.get('Kingdom_%s' % _year) or '').strip()
    _clan_owner[_cid] = '' if _owner in ('', '-') else _owner
    _clan_kingdom[_cid] = '' if _king in ('', '-') else _king

# ---- 势力名 → Kingdom StringId（含别名与「+家」写法）----
# 🔴 必须**先于人物循环**建好：TaikouHero 的 `Kingdom_<年>` 列装的是**中文家名**（今川家），
#   要在人物循环里过这张表换成 StringId（`kingdom_imagawa`），否则 KINGDOM_BY_HERO 会存
#   中文、再经消费方 `setdefault` 覆盖正确值 → 产出 `Faction::Kingdom.今川家` 这种非法 ID。
KINGDOM_BY_NAME = {}
for _fid, _names in sorted(_force_by_id.items()):
    for _n in _names:
        put(KINGDOM_BY_NAME, _n, _fid, _conflicts, '势力')


def _resolve_kingdom(kid_or_name):
    """势力 ID 或中文家名 → Kingdom StringId（两边写法都收）。"""
    if not kid_or_name:
        return ''
    if kid_or_name in _force_by_id:                     # 已经是 ID（Clan.csv 的写法）
        return kid_or_name
    for _k in keys_of(kid_or_name):                     # 中文家名（TaikouHero 的写法）
        if _k in KINGDOM_BY_NAME:
            return KINGDOM_BY_NAME[_k]
    return kid_or_name                                  # 查无 = 原样留，由报告点名




# ---- 人物 / 模板 NPC ----
HERO_MAP = {}            # 中文名（繁/简）→ Hero StringId
AGENT_MAP = {}           # 模板角色名 → CharacterObject 模板 StringId
CLAN_BY_HERO = {}        # 人物名 → 家族 StringId
KINGDOM_BY_HERO = {}     # 人物名 → 势力 StringId
HERO_META = {}           # hero_id → 本剧本年份的状态
ORG_NAMES = {}           # 势力名 → Org 占位 ID（TaikouForce 里没有家格的集团）

_hero_rows = [r for r in _heroes if (r.get('ID') or '').strip()]
_template_rows = [r for r in _hero_rows if (r.get('TemplateNPC') or '').strip() == '1']
_hero_rows = [r for r in _hero_rows if (r.get('TemplateNPC') or '').strip() != '1']

# 模板 NPC：模板行自己的 ID 就是游戏内 CharacterObject（铁律 8：身份匹配用 StringId）
for _r in _template_rows:
    _tid = _r['ID'].strip()
    for _n in [(_r.get('CNName') or '').strip()] + \
              [x.strip() for x in (_r.get('Alias') or '').split('|')]:
        if _n:
            AGENT_MAP.setdefault(_n, _tid)

for _r in _hero_rows:
    _cn = (_r.get('CNName') or '').strip()
    if not _cn:
        continue
    _hid = _r['ID'].strip()
    _names = [_cn] + [x.strip() for x in (_r.get('Alias') or '').split('|') if x.strip()]
    # 本年份姓名（改名角色：木下藤吉郎 → 羽柴秀吉）
    _yname = (_r.get('Name_%s' % _year) or '').strip()
    if _yname and _yname not in _names:
        _names.append(_yname)
    for _n in _names:
        put(HERO_MAP, _n, _hid, _conflicts, '人物')

    _cid = (_r.get('ClanID_%s' % _year) or '').strip()
    # 🔴 TaikouHero 的 Kingdom_<年> 是**中文家名**（今川家）→ 过 KINGDOM_BY_NAME 换成 StringId
    _kid = _resolve_kingdom((_r.get('Kingdom_%s' % _year) or '').strip())
    if _cid:
        for _n in _names:
            put(CLAN_BY_HERO, _n, _cid, _conflicts, '家族')
    if _kid:
        for _n in _names:
            put(KINGDOM_BY_HERO, _n, _kid, _conflicts, '势力(按人)')

    HERO_META[_hid] = {
        'clan': _cid,
        'kingdom': _kid,
        'kingdom_name': (_force_by_id.get(_kid) or [''])[0],
        'city': '',                                            # 下面 SETTLEMENT_MAP 建好后回填
        'appear': (_r.get('Appear_%s' % _year) or '').strip(),
        'identity': (_r.get('Identity_%s' % _year) or '').strip(),
        'stance': (_r.get('CareerStance_%s' % _year) or '').strip(),
    }
    _city_name = (_r.get('City_%s' % _year) or '').strip()
    if _city_name:
        HERO_META[_hid]['city'] = _city_name                      # 暂存名字，建表后换 ID



# 势力里「没有家格」的集团（商家/忍者众/海贼众）→ Org 占位（16b T3 预留域）。
# 判据 = 该势力的家名不以「家」结尾（大名家一律「XX家」），且不是中立文化。
for _fid, _names in sorted(_force_by_id.items()):
    _type = ''
    for _r in _forces:
        if (_r.get('ID') or '').strip() == _fid:
            _type = (_r.get('ForceType') or '').strip()
            break
    if _type in ('Trader', 'Ninja', 'Pirate'):
        for _n in _names:
            put(ORG_NAMES, _n, _fid, _conflicts, '组织')

# ---- 令制国 / 地方（国:: 与 地方:: 两种引用都进 REGION_MAP）----
REGION_MAP = {}
for _r in _kuni:
    _rid = (_r.get('ID') or '').strip()
    if not _rid:
        continue
    _names = [(_r.get('Name') or '').strip()]
    _names += [x.strip() for x in (_r.get('Alias') or '').split('|')]
    for _n in _names:
        if _n:
            put(REGION_MAP, _norm_middot(_n), _rid, _conflicts, '令制国')
for _r in _region:
    _rid = (_r.get('ID') or '').strip()
    if not _rid:
        continue
    _names = [(_r.get('Name') or '').strip()]
    _names += [x.strip() for x in (_r.get('Alias') or '').split('|')]
    for _n in _names:
        if _n:
            put(REGION_MAP, _n, _rid, _conflicts, '地方')

# ---- 据点（Settlements.csv：id 列 = 游戏内 StringId，如 town_tk080 / village_tk205）----
SETTLEMENT_MAP = {}          # 太阁据点名 → Settlement StringId
SETTLEMENT_ANCHOR = {}       # 兼容位：新数据每座城都有真 id，占位锚点机制已不需要
# 🔴 令制国名不能当据点键（「尾張」既是国又是城名时，先到的国名会污染据点查找）
_kuni_keys = set(REGION_MAP)

# ---- 第一遍：为每座据点展开全部查找键，按「键 → 哪个据点」收集 ----
#   展开包括：别名集 + 简繁 + 字形变体 + 后缀变体（鳴海/鳴海城/岡崎之町 指同一座城）。
# 🔴 变体要在**简繁两种写法上各展开一遍**：语料是繁体源文（厩橋之町 / 姫路之町），
#   而 CSV 主名是简体（厩桥之町）——只在原写法上展开，去尾得到的仍带日文繁体，
#   后面 keys_of 再转换就对不上已经展开过的键了（实测：厩橋/那覇/雑賀 一批全漏）。
_explicit = collections.defaultdict(set)     # 键 → 显式拥有它的据点（「厩桥城」）
_derived = collections.defaultdict(set)      # 键 → 由后缀展开推出来的据点（「厩桥」← 厩桥城）
_sid_names = {}                              # 据点 id → 显式名字（报告用）
for _r in _settlements:
    _sid = (_r.get('id') or '').strip()
    if not _sid:
        continue
    _names = set()
    _n1560 = (_r.get('Name_%s' % _year) or '').strip()
    if _n1560:
        _names.add(_n1560)
    for _x in (_r.get('Name_All') or '').split('|'):        # 全部名称 = 别名集（含改名前后）
        _x = _x.strip()
        if _x:
            _names.add(_x)
    _sid_names[_sid] = sorted(_names)
    # 该据点的**可用写法**：原样 + 简繁 + 字形变体
    _forms = set()
    for _n in _names:
        _forms |= keys_of(_n) | variant_forms(_n)
        for _v in list(keys_of(_n) | variant_forms(_n)):
            _forms |= keys_of(_v)
    for _k in _forms:
        if _k not in _kuni_keys:                           # 国名让位给令制国层
            _explicit[_k].add(_sid)
    # 后缀变体（語料写「鳴海」，表里是「鸣海城」）：基名与基名+各后缀都算派生候选
    for _k in _forms:
        _base = strip_suffix(_k)
        if not _base or _base == _k:
            continue
        for _suf in SUFFIXES[1:]:
            _v = _base + _suf
            if _v != _k and _v not in _kuni_keys:
                _derived[_v].add(_sid)

# ---- 第二遍：注册键。显式名优先于派生名；同优先级撞车 = 歧义，不注册（宁缺勿错）----
SETTLEMENT_MAP, SETTLEMENT_AMBIGUOUS = {}, {}
for _n in sorted(set(_explicit) | set(_derived)):
    _ex = _explicit.get(_n, set())
    if len(_ex) == 1:
        put(SETTLEMENT_MAP, _n, next(iter(_ex)), _conflicts, '据点')
    elif len(_ex) > 1:
        SETTLEMENT_AMBIGUOUS[_n] = sorted(_ex)             # 两座城同名 = 数据冲突
    else:
        _de = _derived.get(_n, set())
        if len(_de) == 1:
            put(SETTLEMENT_MAP, _n, next(iter(_de)), _conflicts, '据点')
        elif len(_de) > 1:
            SETTLEMENT_AMBIGUOUS[_n] = sorted(_de)

# 回填 HERO_META.city（暂存的是城名 → 换成 StringId）
#   走 lookup_settlement（同一套写法/后缀展开），查无或歧义 → 留空并进报告，不猜。
setattr(sys.modules[__name__], '_settlement_unresolved', [])
for _hid, _m in HERO_META.items():
    if _m['city']:
        _raw = _m['city']
        _sid = lookup_settlement(_raw)
        _m['city'] = _sid or ''
        if not _sid:
            _settlement_unresolved.append((_hid, _raw))

# ---- 物品 / 交易品（item.csv 的 ID 列 = 真 StringId 优先；tk5_item_* = 占位待 07 数据包）----
ITEM_MAP = {}
MERC_T_MAP = {}
for _r in _items:
    _n = (_r.get('TK5Name') or '').strip()
    _iid = (_r.get('ID') or '').strip()
    if not _n or not _iid:
        continue
    if (_r.get('Kind') or '').strip() == 'trade_good':
        put(MERC_T_MAP, _n, _iid, _conflicts, '交易品')
    else:
        put(ITEM_MAP, _n, _iid, _conflicts, '物品')

# ---------------------------------------------------------------------------
# 存在性核对（铁律 5）：ID 在本包 XML 里有没有定义
# ---------------------------------------------------------------------------
_xml_hero = _xml_ids('Taikou/ModuleData/spnpccharacters*.xml',
                     'Taikou/ModuleData/spnpccharactertemplates.xml',
                     'Taikou/ModuleData/spclans*.xml')
_xml_settle = _xml_ids('Taikou/ModuleData/settlements*.xml')


def _origin(i, pool, tag=''):
    return 'base' if i in pool else 'new'


HERO_ORIGIN = {i: _origin(i, _xml_hero) for i in set(HERO_MAP.values())}
CLAN_ORIGIN = {i: _origin(i, _xml_hero) for i in set(CLAN_BY_HERO.values())}
SETTLEMENT_ORIGIN = {i: _origin(i, _xml_settle) for i in set(SETTLEMENT_MAP.values())}

MISSING_IN_XML = collections.OrderedDict()
_bad_hero = sorted(i for i in set(HERO_MAP.values()) if i not in _xml_hero)
_bad_clan = sorted(i for i in set(CLAN_BY_HERO.values()) if i not in _xml_hero)
_bad_settle = sorted(i for i in set(SETTLEMENT_MAP.values()) if i not in _xml_settle)
if _bad_hero:
    MISSING_IN_XML['人物'] = _bad_hero
if _bad_clan:
    MISSING_IN_XML['家族'] = _bad_clan
if _bad_settle:
    MISSING_IN_XML['据点'] = _bad_settle


def stats():
    """一行行体检数字（调用方打日志用；不参与查找）。"""
    return collections.OrderedDict([
        ('heroes', len(_hero_rows)),
        ('templates', len(_template_rows)),
        ('clans', len(_clans)),
        ('forces', len(_forces)),
        ('settlements', len(_settlements)),
        ('kuni', len(_kuni)),
        ('region', len(_region)),
        ('items', len(ITEM_MAP)),
        ('trade_goods', len(MERC_T_MAP)),
        ('hero_keys', len(HERO_MAP)),
        ('clan_keys', len(CLAN_BY_HERO)),
        ('kingdom_by_name', len(KINGDOM_BY_NAME)),
        ('settlement_keys', len(SETTLEMENT_MAP)),
        ('org_keys', len(ORG_NAMES)),
        ('region_keys', len(REGION_MAP)),
        ('conflicts', len(_conflicts)),
    ])


def conflicts():
    return list(_conflicts)


# ---------------------------------------------------------------------------
# 异常字守卫（进离场自检）：名字里出现 Unicode 私用区码点 = 数据没清洗干净
# ---------------------------------------------------------------------------
# 后台：太阁5 DX 用自绘字形槽渲染 JIS 表外汉字，dump 出来是 U+E000–U+F8FF 私用区码点
# （看着像掉字：`岩<E426>城` 实为岩槻城）。`Scripts/tk5_pua_names.py` 是还原表——
# 数据侧应在 **写表之前** 过一遍它（本读取器不替数据擦屁股，只把问题喊出来）。
PUA_RANGES = ((0xE000, 0xF8FF), (0xF0000, 0xFFFFD), (0x100000, 0x10FFFD))


def scan_pua():
    """扫所有表的键与值里有没有私用区码点 → [(表名, 名字, 码点)]。"""
    hits = []
    tables = (('HERO_MAP', HERO_MAP), ('AGENT_MAP', AGENT_MAP), ('CLAN_BY_HERO', CLAN_BY_HERO),
              ('KINGDOM_BY_NAME', KINGDOM_BY_NAME), ('SETTLEMENT_MAP', SETTLEMENT_MAP),
              ('REGION_MAP', REGION_MAP), ('ORG_NAMES', ORG_NAMES),
              ('ITEM_MAP', ITEM_MAP), ('MERC_T_MAP', MERC_T_MAP))
    for label, d in tables:
        for k in d:
            for ch in k:
                if any(lo <= ord(ch) <= hi for lo, hi in PUA_RANGES):
                    hits.append((label, k, 'U+%04X' % ord(ch)))
                    break
    return hits


if __name__ == '__main__':                                             # pragma: no cover
    for _k, _v in stats().items():
        print('  %-18s %d' % (_k, _v))
    if not HAS_ZH:
        print('  ⚠️ 无 zhconv：只生成了简体键，繁体源文会查不到')
    _pua = scan_pua()
    if _pua:
        print('  🔴 私用区码点 %d 处（数据未过 tk5_pua_names.restore，先清洗再重跑）:' % len(_pua))
        for _t, _n, _c in _pua[:10]:
            print('     %s：%r %s' % (_t, _n, _c))
    else:
        print('  ✅ 无 Unicode 私用区码点')
    for _c in _conflicts[:10]:
        print('  冲突 ' + _c)
