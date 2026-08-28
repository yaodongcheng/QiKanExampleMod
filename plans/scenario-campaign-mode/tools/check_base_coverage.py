# -*- coding: utf-8 -*-
"""
check_base_coverage.py —— 【检查用】太阁5 事件具名对象 ÷ 织丰基础 mod 实扫存活率

与 07b 覆盖率（织丰表口径）不同：这里按「运行时真存在」口径——只认基础织丰 mod
（游戏根/Modules/Shokuho/ModuleData）里扫得到的 id；扩展包生成物（未注册）不算存活。

用法：
    python tools/check_base_coverage.py          # 摘要 + 缺失清单
    python tools/check_base_coverage.py --people  # 只打人物域

依赖：tools/entity_maps.py（gen_entity_maps.py 产物）。
"""
from __future__ import unicode_literals
import io
import os
import re
import sys
import glob
import collections

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
# tools/.. = scenario-campaign-mode → ../.. = plans → ../../.. = LivingWorldNpcs（仓库根）
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
# 仓库根/.. = Modules → ../.. = 游戏根
GAME = os.path.abspath(os.path.join(REPO, '..', '..'))
BASE = os.path.join(GAME, 'Modules', 'Shokuho', 'ModuleData')
EXP = os.path.join(GAME, 'Modules', 'ShokuhoTaikouExpansionPack', 'ModuleData')
SRC = os.path.join(REPO, 'Knowledge', '太阁事件包', 'TK5AllEvents_merged.txt')

sys.path.insert(0, HERE)
import entity_maps as EM  # noqa: E402


def xml_ids(root, pats):
    ids = set()
    for pat in pats:
        for p in glob.glob(os.path.join(root, pat), recursive=True):
            if not os.path.isfile(p):
                continue
            txt = io.open(p, encoding='utf-8', errors='replace').read()
            ids |= set(re.findall(r'id="([^"]+)"', txt))
    return ids


def dscan(dom):
    dd = collections.Counter()
    src = io.open(SRC, encoding='utf-8', errors='replace').read()
    for m in re.finditer(r'%s::([^\s)，,。.()（(]{1,30})' % dom, src):
        v = m.group(1).strip()
        if re.search(r'[Ａ-Ｚａ-ｚ0-9]$', v) or v in ('無', '主人公'):
            continue
        dd[v] += 1
    return dd


def main():
    if not os.path.exists(BASE):
        print('找不到基础织丰 mod：%s' % BASE)
        return 1
    base_ids = xml_ids(BASE, ['**/*.xml'])
    exp_ids = xml_ids(EXP, ['**/*.xml'])
    print('基础 mod id：%d（heroes+lords 文件 %d）｜ 扩展包 id：%d'
          % (len(base_ids), len(xml_ids(BASE, ['heroes/*.xml', 'lords/*.xml'])), len(exp_ids)))

    hv = set(EM.HERO_MAP.values())
    print('HERO_MAP 共 %d id → 基础 %d / 仅生成物 %d / 全新占位 %d'
          % (len(hv), len(hv & base_ids), len((hv - base_ids) & exp_ids), len(hv - base_ids - exp_ids)))

    src = io.open(SRC, encoding='utf-8', errors='replace').read()
    cnt = dscan('人物')
    miss = []
    for n, c in cnt.most_common():
        hid = EM.HERO_MAP.get(n)
        if not hid:
            miss.append((n, '无键'))
        elif hid.startswith('tk5_'):
            miss.append((n, 'tk5占位'))
        elif hid not in base_ids:
            miss.append((n, '仅生成物'))
    print('人物具名 %d → 无键 %d／tk5占位 %d／仅生成物 %d'
          % (len(cnt), sum(1 for x in miss if x[1] == '无键'),
             sum(1 for x in miss if x[1] == 'tk5占位'),
             sum(1 for x in miss if x[1] == '仅生成物')))
    if '--people' not in sys.argv:
        for kind in ('无键', 'tk5占位', '仅生成物'):
            print('\n【%s】（%d）' % (kind, sum(1 for x in miss if x[1] == kind)))
            for n, k in miss:
                if k == kind:
                    print('  - %s' % n)

    # 非人物域（简查）
    def lookup_clan(n):
        return (EM.CLAN_BY_HERO.get(n) or EM.CLAN_BY_HERO.get(n + '家')
                or EM.KINGDOM_BY_NAME.get(n) or EM.KINGDOM_BY_NAME.get(re.sub(r'家$', '', n)))
    for dom, fn in [('大名家', lookup_clan),
                    ('勢力', lambda n: EM.KINGDOM_BY_NAME.get(n) or EM.ORG_NAMES.get(n)),
                    ('城', lambda n: EM.SETTLEMENT_MAP.get(n)),
                    ('據點', lambda n: EM.SETTLEMENT_MAP.get(n)),
                    ('町', lambda n: EM.SETTLEMENT_MAP.get(n)),
                    ('地方', lambda n: EM.REGION_MAP.get(n))]:
        dd = dscan(dom)
        ok = sum(1 for n in dd if fn(n))
        print('  %-4s 具名 %d → 有映射 %d（%.0f%%）' % (dom, len(dd), ok, 100.0 * ok / max(1, len(dd))))
    return 0


if __name__ == '__main__':
    sys.exit(main())
