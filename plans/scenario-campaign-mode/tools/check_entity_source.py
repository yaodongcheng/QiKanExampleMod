# -*- coding: utf-8 -*-
r"""check_entity_source.py —— 实体查找表对账器（新读取器 vs 冻结产物 + 语料实测）

解决什么问题
------------
`entity_maps.py`（08-31 冻结的生成物）曾是翻译器的实体来源，但它跟 CSV 早就分叉了。
2026-09-14 换成 `entity_source.py`（每次 import 从 CSV 现读现建）后，需要一把
**可重复的尺子**回答两个问题：
  ①新的表和旧的表，差在哪？（逐域逐名，不靠印象）
  ②语料里真正出现的名字，有多少查得到？（这才是翻译器会不会停机的直接指标）

两种口径（都跑，缺一不可）
--------------------------
  · **对账口径**：新表 vs 冻结产物，逐键比对（冻结产物在 ⇒ 才比；缺失的键单列）
  · **语料口径**：语料里每个 `域::名` 的具名引用，按**翻译器真实路径**查表
    （先 `keys_of` 展开繁简/变体，再查）——只查原样会低估解析率

用法：
    python tools/check_entity_source.py            # 两种口径都跑
    python tools/check_entity_source.py --corpus   # 只跑语料口径
    python tools/check_entity_source.py --diff     # 只跑对账口径
Exit: 0 正常（有缺口也返回 0，缺口是数据待办不是脚本故障）/ 2 fatal。
"""
from __future__ import unicode_literals

import collections
import importlib.util
import io
import os
import re
import sys

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
CORPUS = os.path.join(REPO, 'Knowledge', '太阁5', '太阁事件包', 'TK5AllEvents_merged.txt')
OLD_PATH = os.path.join(HERE, 'entity_maps.py')          # 冻结产物（删除后本脚本自动跳过对账口径）
NEW_PATH = os.path.join(HERE, 'entity_source.py')


def _load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod
    spec.loader.exec_module(mod)
    return mod


# 语料域名 → 查表函数（与 tk5_to_json.py 的 _ENTITY_TABLES 同口径）
DOMAINS = ('人物', '大名家', '勢力', '城', '據點', '町', '里', '砦', '國', '地方', '物品', '交易品')


def corpus_names(dom):
    """语料里该域的具名引用（去槽变量 / 去特殊槽）。"""
    txt = io.open(CORPUS, encoding='utf-8', errors='replace').read()
    cnt = collections.Counter(re.findall(r'%s::([^\.\s　()（）]*)' % dom, txt))
    return {k: v for k, v in cnt.items()
            if k and not re.search(r'[Ａ-Ｚａ-ｚ０-９]', k) and k not in ('無', '主人公', '無效')}


def make_lookup(M):
    """→ 域 → (名 → 命中值 or None)，走翻译器真实路径（`M.lookup_settlement` / `M.lookup`）。"""
    def tk(d, n):
        return M.lookup_settlement(n) if d is M.SETTLEMENT_MAP else M.lookup(d, n)

    def look(dom, n):
        if dom == '人物':
            return tk(M.HERO_MAP, n) or tk(M.AGENT_MAP, n)
        if dom == '大名家':
            return tk(M.CLAN_BY_HERO, n) or tk(M.CLAN_BY_HERO, n + '家')
        if dom == '勢力':
            return (tk(M.KINGDOM_BY_NAME, n) or tk(M.KINGDOM_BY_NAME, re.sub('家$', '', n))
                    or tk(M.ORG_NAMES, n) or tk(M.KINGDOM_BY_HERO, n))
        if dom in ('城', '據點', '町', '里', '砦'):
            return tk(M.SETTLEMENT_MAP, n)
        if dom in ('國', '地方'):
            return tk(M.REGION_MAP, n)
        if dom == '物品':
            return tk(M.ITEM_MAP, n)
        if dom == '交易品':
            return tk(M.MERC_T_MAP, n)
        return None
    return look


def cmd_corpus(NEW):
    """语料口径：每个具名引用查得到吗。"""
    look = make_lookup(NEW)
    print('== 语料口径（%s 里的 域::名）==' % os.path.basename(CORPUS))
    print('%-8s %6s %8s %8s' % ('域', '具名', '已解析', '占比'))
    print('-' * 36)
    tot = hit = 0
    gaps = collections.OrderedDict()
    for dom in DOMAINS:
        ns = corpus_names(dom)
        ok = 0
        miss = []
        for n in sorted(ns):
            if look(dom, n):
                ok += 1
            else:
                miss.append(n)
        tot += len(ns)
        hit += ok
        if miss:
            gaps[dom] = miss
        print('%-8s %6d %8d %7.1f%%' % (dom, len(ns), ok, 100.0 * ok / max(1, len(ns))))
    print('-' * 36)
    print('%-8s %6d %8d %7.1f%%' % ('合计', tot, hit, 100.0 * hit / max(1, tot)))
    if gaps:
        print('\n== 仍查无（数据待办）==')
        for dom, miss in gaps.items():
            print('【%s】%d 个：%s' % (dom, len(miss), '、'.join(miss)))
    return 0


def cmd_diff(NEW):
    """对账口径：新表 vs 冻结产物，逐域逐键。"""
    if not os.path.isfile(OLD_PATH):
        print('对账口径跳过：冻结产物 %s 已不存在（这正是目标状态）' % os.path.basename(OLD_PATH))
        return 0
    OLD = _load(OLD_PATH, 'frozen_entity_maps')
    PAIRS = (('HERO_MAP', 'HERO_MAP'), ('CLAN_BY_HERO', 'CLAN_BY_HERO'),
             ('KINGDOM_BY_HERO', 'KINGDOM_BY_HERO'), ('KINGDOM_BY_NAME', 'KINGDOM_BY_NAME'),
             ('SETTLEMENT_MAP', 'SETTLEMENT_MAP'), ('AGENT_MAP', 'AGENT_MAP'),
             ('ITEM_MAP', 'ITEM_MAP'), ('MERC_T_MAP', 'MERC_T_MAP'),
             ('REGION_MAP', 'REGION_MAP'), ('ORG_NAMES', 'ORG_NAMES'))
    print('== 对账口径（新 entity_source vs 冻结 entity_maps）==')
    print('%-18s %8s %8s %8s %8s %8s' % ('表', '旧键', '新键', '两有', '仅旧', '仅新'))
    print('-' * 68)
    tot_only_old = tot_conflict = 0
    conflicts = []
    for attr, _ in PAIRS:
        d_old = getattr(OLD, attr, {}) or {}
        d_new = getattr(NEW, attr, {}) or {}
        both = set(d_old) & set(d_new)
        only_old = set(d_old) - set(d_new)
        only_new = set(d_new) - set(d_old)
        diff = [k for k in both if d_old[k] != d_new[k]]
        tot_only_old += len(only_old)
        tot_conflict += len(diff)
        print('%-18s %8d %8d %8d %8d %8d' % (attr, len(d_old), len(d_new),
                                             len(both), len(only_old), len(only_new)))
        for k in sorted(diff)[:5]:
            conflicts.append('%s：「%s」旧 %s → 新 %s' % (attr, k, d_old[k], d_new[k]))
    print('-' * 68)
    print('仅旧表有的键 %d 个（= 旧表多出来的，多为过期/错 ID）；值冲突 %d 个'
          % (tot_only_old, tot_conflict))
    if conflicts:
        print('\n== 值冲突样例（前 10）==')
        for c in conflicts[:10]:
            print('   ' + c)
    return 0


def main():
    if not os.path.isfile(NEW_PATH):
        print('[FATAL] 缺 %s' % NEW_PATH)
        return 2
    NEW = _load(NEW_PATH, 'entity_source')
    if '--diff' in sys.argv:
        return cmd_diff(NEW)
    if '--corpus' in sys.argv:
        return cmd_corpus(NEW)
    cmd_diff(NEW)
    print()
    return cmd_corpus(NEW)


if __name__ == '__main__':
    sys.exit(main())
