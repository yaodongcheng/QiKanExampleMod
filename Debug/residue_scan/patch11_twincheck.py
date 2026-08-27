# -*- coding: utf-8 -*-
"""patch11：全局词→slug 兜底 + 跨表侧名分叉自检（把「同一套词写两遍」这类漏洞自动化）。

背景：身份 双表分叉（10 个词两个侧名，ashigaru_captain 跨表指两个不同品级）是人肉查出来的。
     同族的病还有 場面/設施/背景/決鬥場地、軍團/軍團槽——都靠人查不现实。
     本补丁把这条检查焊进生成期：以后任何两张词表给同一个 TK5 词不同 slug，直接 exit(1)。

三件事：
① WORD_SLUG —— 所有已注册词表汇成一张「TK5 词 → slug」总表（词在多表且 slug 一致才收，
   歧义词不收）。ＢＧＭ/ＳＥ/事件ＣＧ 这三张纯媒体资产表查它兜底：曲名叫「上洛」的
   CG 不再是 Cg::tk5_u43c855，而是 Cg::march_kyoto。
② 容器統計::容器記錄數 由 container.count 改 container_count —— 与 變量::容器記錄數 对齐
   （同一个东西两种拼法 = 同一个病的小号）。
③ verify_coverage 新增「跨表侧名分叉」断言，带 TWIN_EXEMPT 白名单（合法一词多义）。
"""
import ast
import io
import sys

sys.stdout.reconfigure(encoding='utf-8')
P = 'plans/scenario-campaign-mode/tools/gen_registry_tables.py'

# ── ① WORD_SLUG 总表（插在 ENUM_SETS 派生块之后，此时所有词表已就位）──
WORDSLUG = '''

# ═══════════════════════════════════════════════════════════════════════════
# 🔴 全局「TK5 词 → slug」总表：把各词表已注册的 slug 汇总一张，供**无专表**的
#   媒体资产集（ＢＧＭ/ＳＥ/事件ＣＧ）兜底——曲名/CG 名往往就是某个场所或事件词
#   （自宅 / 上洛 / 統一天下），有现成 slug 就别再吐 hash。
#   歧义词（多张表给了不同 slug）不收，宁可 hash 也不猜。
# ═══════════════════════════════════════════════════════════════════════════
def _collect_word_slugs():
    seen, bad = {}, set()
    tables = [PLACE_TOKENS, TRIGGER_TOKENS, NPC_TOKENS]
    tables += [{_v: _s for (_d, _v), _s in DOMAIN_VAL_MAP.items()}]
    tables += list(ENUM_SETS.values())
    for tbl in tables:
        for w, s in tbl.items():
            sl = str(s).split('::')[-1]
            if not sl or not re.fullmatch(r'[A-Za-z][A-Za-z0-9_.]*', sl):
                continue                       # 数字字面量/空 slug 不进总表
            if w in seen and seen[w] != sl:
                bad.add(w)                     # 歧义：两张表给了不同 slug
            seen.setdefault(w, sl)
    for w in bad:
        seen.pop(w, None)
    return seen


WORD_SLUG = _collect_word_slugs()

'''

OLD_RES = """    p = RES_PREFIX[setname]
    tbl = RES_TOKEN_TABLE.get(setname)
    if tbl is not None and tok in tbl:
        return '%s::%s' % (p, tbl[tok])
    return '%s::%s' % (p, ascii_translit(tok) or fallback_id(tok))"""
NEW_RES = """    p = RES_PREFIX[setname]
    tbl = RES_TOKEN_TABLE.get(setname)
    if tbl is not None and tok in tbl:
        return '%s::%s' % (p, tbl[tok])
    # 媒体资产（ＢＧＭ/ＳＥ/事件ＣＧ）：名字若已在别处注册过（自宅/上洛/統一天下），复用其 slug
    g = WORD_SLUG.get(tok) if 'WORD_SLUG' in globals() else None
    return '%s::%s' % (p, g or ascii_translit(tok) or fallback_id(tok))"""

# ── ② 容器統計 对齐 ──
OLD_CNT = "    '容器統計': {'容器記錄數': 'container.count'},"
NEW_CNT = "    '容器統計': {'容器記錄數': 'container_count'},   # 与 變量::容器記錄數 同 slug（勿分叉）"

# ── ③ 跨表分叉断言 ──
TWIN = '''

# ═══════════════════════════════════════════════════════════════════════════
# 🔴 跨表侧名分叉自检（2026-08-27）：同一个 TK5 词在两张词表里给出不同 slug =
#   翻译器不知道该按哪个走（身份 双表分叉的教训：ashigaru_captain 一名两指）。
#   合法的一词多义写进 TWIN_EXEMPT，其余一律生成期报错。
# ═══════════════════════════════════════════════════════════════════════════
MEDIA_SETS = ('ＢＧＭ', 'ＳＥ', '事件ＣＧ')     # 媒体资产名 ≠ 语义词，与语义表重名不算分叉

TWIN_EXEMPT = {
    # (TK5 词, slugA, slugB) —— 确实是两个意思，不是分叉
    ('終結', 'ended', 'disband'),                   # 戰鬥結束種類「战斗以终结收场」/ 軍團指令「解散军团」
    ('歸還', 'intent_return_home', 'return_home'),  # 軍團方針 = 持续方针（02 PartyIntent）/ 軍團指令 = 一次性命令
}


def all_vocabs():
    """所有词表 → {表名: {TK5 词: 侧名}}（域值 / 枚举 / 资源集）。"""
    out, dv = {}, {}
    for (_d, _v), _s in DOMAIN_VAL_MAP.items():
        dv.setdefault(_d, {})[_v] = _s
    for _d, _m in dv.items():
        out['域:' + _d] = _m
    for _n, _m in ENUM_SETS.items():
        out['枚:' + _n] = _m
    for _n, _ts in RES_SETS.items():
        out['资:' + _n] = {_t: res_side(_n, _t) for _t in _ts}
    return out


def twin_divergences():
    """返回 [(词, 表A, slugA, 表B, slugB)] —— 同词异 slug 的分叉清单。"""
    vocabs = all_vocabs()
    keys = sorted(vocabs)
    out = []
    for i in range(len(keys)):
        for j in range(i + 1, len(keys)):
            ka, kb = keys[i], keys[j]
            if ka[2:] in MEDIA_SETS or kb[2:] in MEDIA_SETS:
                continue
            a, b = vocabs[ka], vocabs[kb]
            for w in sorted(set(a) & set(b)):
                sa, sb = str(a[w]).split('::')[-1], str(b[w]).split('::')[-1]
                if sa == sb or (w, sa, sb) in TWIN_EXEMPT or (w, sb, sa) in TWIN_EXEMPT:
                    continue
                out.append((w, ka, sa, kb, sb))
    return out

'''

ASSERT = """    for w_, ka_, sa_, kb_, sb_ in twin_divergences():
        errors.append(f'跨表侧名分叉: {w_} 在 {ka_}={sa_} / {kb_}={sb_}'
                      f'（同一个词两个侧名 → 翻译器按哪个走？合并到单一词表，或写进 TWIN_EXEMPT）')
    return errors"""


def main():
    src = io.open(P, encoding='utf-8').read()
    if 'WORD_SLUG' in src:
        print('已打过补丁，跳过')
        return

    anchor = "ENUM_SETS['軍團槽'] = dict(ARMY_SLOTS)"
    assert anchor in src, '锚点缺失：派生块尾'
    src = src.replace(anchor, anchor + WORDSLUG.rstrip('\n'), 1)

    for name, old, new in (('res_side', OLD_RES, NEW_RES), ('容器統計', OLD_CNT, NEW_CNT)):
        assert old in src, '锚点缺失：%s' % name
        src = src.replace(old, new, 1)

    a2 = '# ═══ 生成期自检：全语料覆盖断言（表外 = 生成失败）═══'
    assert a2 in src, '锚点缺失：自检段'
    src = src.replace(a2, TWIN.strip('\n') + '\n\n\n' + a2, 1)

    old_ret = """        if ENUM_SETS.get(n_) != want_:
            errors.append(f'词汇表分叉: ENUM_SETS[{n_}] 与 DOMAIN_VAL_MAP 的 {n_} 域值不一致')
    return errors"""
    assert old_ret in src, '锚点缺失：verify_coverage 结尾'
    src = src.replace(old_ret, old_ret[:-len('    return errors')] + ASSERT, 1)

    ast.parse(src)
    io.open(P, 'w', encoding='utf-8').write(src)
    print('✅ patch11 已应用（WORD_SLUG 兜底 + 容器統計 对齐 + 跨表分叉断言）')


if __name__ == '__main__':
    main()
