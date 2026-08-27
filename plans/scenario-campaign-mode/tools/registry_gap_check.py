# -*- coding: utf-8 -*-
"""注册表覆盖度自检（2026-08-27 用户裁定：语料反向清洗，剩余 = 未识别漏洞）

思路：16a CSV 是"已识别词条"权威（域/属性/域值/命令/函数 + 槽 + 特殊值 + 名字表）。
对语料逐行扫描所有 DSL 形态（域::值 / 域::X.属性 / 域::X.函数( / 行首命令 / 参数裸值），
未命中任何词条集的形态 = 表外漏洞（生成器缺陷），全部列出。

用法：python registry_gap_check.py [--source merged路径] [--registry 16a路径]
输出：未识别清单（形态 + 次数 + 行样例），exit 0 = 零残留。
"""
import argparse
import csv
import os
import re
import sys
from collections import Counter

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
DEFAULT_SOURCE = os.path.join(REPO_ROOT, "Knowledge", "太阁事件包", "TK5AllEvents_merged.txt")
DEFAULT_REGISTRY = os.path.join(REPO_ROOT, "plans", "scenario-campaign-mode", "16a-DSL翻译总表.csv")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from gen_registry_tables import SPECIAL_VALS, SLOT_CAT     # noqa: E402
import tk5_to_json as T                                     # noqa: E402  名字表（HERO_MAP/CLAN_MAP/…）

# 🔴 中文字符集（含假名，2026-08-27 用户裁定）
JA = r'一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ0-9０-９'
RE_DOM = re.compile(r'[' + JA + r']{1,6}::')
RE_DVAL = re.compile(r'([' + JA + r']{1,6})::([' + JA + r']{1,16})')
RE_CMD = re.compile(r'^([一-鿿぀-ヿＡ-Ｚａ-ｚA-Za-z]{2,8}):')
RE_SLOT = re.compile(r'^[一-鿿぀-ヿA-Za-zＡ-Ｚａ-ｚ]{1,8}[Ａ-Ｅ]$|^[ａ-ｚ]$')
RE_NUM = re.compile(r'^-?\d+$')

# 台词/旁白行：正文不检查（[[…]] 是自然语言）
TALK_CMDS = ('對話', '旁白', '訊息', 'ＴＥＸＴ', '自立', '自言自語')


def load_registry(csv_path):
    domains, attrs, dvals, cmds, funcs = set(), set(), set(), set(), set()
    with open(csv_path, encoding='utf-8-sig') as f:
        for r in csv.DictReader(f):
            src = r['太阁原词']
            if r['类别'] == '域':
                domains.add(src)
            elif r['类别'] == '属性':
                attrs.add(src)
            elif r['类别'] == '域值':
                dvals.add((r['所属域'], src))
            elif r['类别'] in ('命令', '语法'):
                cmds.add(src)
            elif r['类别'] == '函数':
                funcs.add(src)
    return domains, attrs, dvals, cmds, funcs


def load_names():
    names = set()
    for tbl in (T.HERO_MAP, T.CLAN_MAP, T.SETTLEMENT_MAP, T.REGION_MAP, T.AGENT_MAP, T.FALLBACK_MAP):
        names.update(tbl.keys())
    return names


def classify(subject):
    """主体 → 类别：槽/特殊值/具名/其他。"""
    if RE_SLOT.match(subject):
        return '槽'
    if subject in SPECIAL_VALS:
        return '特殊'
    if subject in NAMES:
        return '具名'
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--source', default=DEFAULT_SOURCE)
    ap.add_argument('--registry', default=DEFAULT_REGISTRY)
    args = ap.parse_args()

    global NAMES
    domains, attrs, dvals, cmds, funcs = load_registry(args.registry)
    NAMES = load_names()
    txt = open(args.source, encoding='utf-8').read()

    gaps_dv, gaps_attr, gaps_cmd, gaps_func = Counter(), Counter(), Counter(), Counter()
    gap_ctx = {}
    lines_total = 0

    for line in txt.splitlines():
        s = line.strip()
        if not s or s.startswith('#'):
            continue
        lines_total += 1
        is_talk = s.startswith(TALK_CMDS)
        body = s
        # 行首命令
        m = RE_CMD.match(s)
        if m:
            cmd = m.group(1)
            if cmd not in cmds:
                gaps_cmd[cmd] += 1
                gap_ctx.setdefault(('命令', cmd), s[:70])
            body = s[len(m.group(0)):]
        if is_talk:
            continue                      # 台词正文不检查
        # 域::X 形态（含 .属性 / .函数( 后缀）
        for dm in RE_DVAL.finditer(body):
            dom, subj = dm.group(1), dm.group(2)
            rest = body[dm.end():]
            if dom not in domains:
                gaps_dv[f'{dom}::{subj}'] += 1
                gap_ctx.setdefault(('域', f'{dom}::{subj}'), s[:70])
                continue
            if '.' in subj:
                pre, attr = subj.rsplit('.', 1)
                if rest.startswith('(') and attr in funcs:
                    continue              # 函数调用 ✓
                if attr in attrs:
                    continue              # 属性访问 ✓
                if pre in NAMES or RE_SLOT.match(pre) or pre in SPECIAL_VALS:
                    if attr in attrs:
                        continue
                gaps_attr[f'{dom}::{pre}.{attr}'] += 1
                gap_ctx.setdefault(('属性', f'{dom}::{pre}.{attr}'), s[:70])
                continue
            if (dom, subj) in dvals:
                continue                  # 域值 ✓
            if classify(subj):
                continue                  # 槽/特殊/具名 ✓
            # 未识别域值
            gaps_dv[f'{dom}::{subj}'] += 1
            gap_ctx.setdefault(('域值', f'{dom}::{subj}'), s[:70])

    print(f'✅ 扫描 {lines_total} 行（含台词跳过）')
    total = sum(gaps_dv.values()) + sum(gaps_attr.values()) + sum(gaps_cmd.values()) + sum(gaps_func.values())
    if total == 0:
        print('🎉 零残留：语料全部 DSL 形态均命中注册表')
        return 0
    print(f'🔴 未识别 {total} 处：')
    for label, cnt in (('域值', gaps_dv), ('属性', gaps_attr), ('函数', gaps_func), ('命令', gaps_cmd)):
        for k, n in cnt.most_common(30):
            print(f'  {label:<4} {n:5d}  {k}')
            print(f'        例: {gap_ctx.get((label, k), "")}')
    return 1


if __name__ == '__main__':
    sys.exit(main())
