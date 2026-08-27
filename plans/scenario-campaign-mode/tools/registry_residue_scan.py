# -*- coding: utf-8 -*-
"""registry_residue_scan.py — 注册表反向清洗语料，剩余即漏洞（2026-08-27 用户裁定）

与 registry_gap_check.py（正向扫「已知形态」）相反：本工具**扣除法**——
凡是 16a 翻译总表 + 生成器规则 + 名字表能解释的片段，就地从语料副本里扣掉；
扣不干净的就是漏洞。正向扫描只能发现「模型内」的缺失，扣除法能发现「模型外」的形态
（命令裸参数枚举 / 触发名 / BGM / 容器字段 …… 正向正则压根不看这些位置）。

🔴 只对副本操作：原语料 read-only，副本 + 残渣报告写在 --workdir（默认 Debug/residue_scan/）。

解析模型（v3：按**参数位**结构解析，不再逐字符猜）：
    命令行 = `命令:[头值](参数,参数)(参数)…[[台词]]//注释`
    ① 命令头   → CMD_MAP / SYNTAX_CMDS / cmd_rule
    ② 头值     → CMD_ARG_SPEC[cmd]['*']（屬性:一次｜弱、發生契機:據點畫面表示後）
    ③ 参数位   → CMD_ARG_SPEC[cmd][位序]（跨括号组连续编号，与语料一致）
    ④ 参数内部 → 域::值 / 域::主体.属性 / 域::主体.函数() / 数字 / 槽 / 特殊值 / 运算式
    ⑤ 台词正文 → 自然语言不检查，其中的 {变量}/<变量>/(主体.字段) 插值要检查

残渣分两类：
  A. 未识别形态 residue —— 连词法都没模型的字符（真·未知未知）
  B. 表外词条 gap    —— 形态认得、注册表/规则查不到落点（域/属性/函数/命令/域值/参数位/插值）

用法：
  python plans/scenario-campaign-mode/tools/registry_residue_scan.py [--top 40]
退出码：0 = 零残渣；1 = 有残渣（详单见 stdout 与 workdir/residue_report.txt）
"""
import argparse
import os
import re
import shutil
import sys
from collections import Counter

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..'))
os.chdir(REPO_ROOT)                       # gen_registry_tables 用相对路径读语料
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

DEFAULT_SOURCE = os.path.join('Knowledge', '太阁事件包', 'TK5AllEvents_merged.txt')
DEFAULT_WORKDIR = os.path.join('Debug', 'residue_scan')

import gen_registry_tables as G           # noqa: E402  域/属性/域值/命令/函数/参数位 解析规则
import tk5_to_json as T                   # noqa: E402  名字表

# ═══ 字符集 ═══
JA = r'々〆ヶ・·一-鿿㐀-䶿぀-ヿＡ-Ｚａ-ｚA-Za-z0-9０-９_'
RE_TOKEN = re.compile('[' + JA + ']+')
RE_DOMREF = re.compile(r'([' + JA + r']{1,8})::')
RE_NUM = re.compile(r'-?[0-9]+(?:\.[0-9]+)?')
RE_HEAD = re.compile(r'^([' + JA + r']{1,12}):')
RE_SLOT = re.compile(r'^([' + JA + r']{1,8}?)([Ａ-Ｅ])$')
RE_VARSLOT = re.compile(r'^[ａ-ｚ]$')
RE_EVENTID = re.compile(r'^(?:事件)?[A-Z]{1,3}[0-9A-F]{4,8}_[0-9]+$')
RE_HEXSEQ = re.compile(r'^(?:[0-9A-F]{2}\s*)+$')
RE_ARG_DOMREF = re.compile(r'^([' + JA + r']{1,8})::([^.=<>!+*/%]+)$')   # 整参式域引用（无属性访问/运算）
EXPR_OPS = ('==', '!=', '>=', '<=', '>', '<', '+', '=', '*', '/', '%')
RE_ARG_CALL = re.compile(r'^([' + JA + r']{1,10})[(（]([^()（）]*)[)）]$')   # 属性调用 外交同盟(大名家Ａ)

SKIP_CHARS = set(' \t、。：:')
GROUP_CHARS = set('()（）,，{}[]')
OPS = ['==', '!=', '>=', '<=', '>', '<', '+', '-', '=', '*', '/', '%', '｜']

RE_INTERP = re.compile(r'\{([^{}]*)\}|<([^<>]*)>|\(([^()]*)\)')


def load_names():
    names = set()
    for tbl in ('HERO_MAP', 'AGENT_MAP', 'CLAN_MAP', 'KINGDOM_MAP', 'SETTLEMENT_MAP',
                'REGION_MAP', 'FALLBACK_MAP', 'SLOT_NAME_MAP', 'TRIGGER_MAP'):
        names |= set(getattr(T, tbl))
    return names


NAMES = load_names()
CMDS_OK = set(G.CMD_MAP) | set(G.SYNTAX_CMDS)


def attr_known_anydomain(attr):
    """插值 `(人物Ａ.姓)` 不带域前缀：任一域能解释即算认识。"""
    return any(G.pair_side(d, attr) is not None for d in G.PREFIX_BY_DOMAIN)


def is_slotlike(tok):
    m = RE_SLOT.match(tok)
    if m and (m.group(1) in G.SLOT_CAT or m.group(1) in ('文字列', '數值', '變量', '容器')):
        return True
    return bool(RE_VARSLOT.match(tok))


def cmd_known(cmd):
    """命令是否有落点：专表 / 语法 / 规则前缀（代入/容器/ＳＥ/圖片/軍團/未知）。"""
    return cmd in CMDS_OK or G.cmd_rule(cmd) != '🔴 低频 → 降级/忽略'


class Scanner:
    def __init__(self):
        self.gap_domain = Counter()       # 域表外
        self.gap_attr = Counter()         # (域,属性) 表外
        self.gap_call = Counter()         # (域,函数) 表外
        self.gap_dval = Counter()         # (域,值) 表外
        self.gap_cmd = Counter()          # 命令表外
        self.gap_arg = Counter()          # 命令参数位表外
        self.gap_interp = Counter()       # 台词插值表外
        self.residue = Counter()          # 未识别字符片段
        self.ctx = {}
        self.consumed = Counter()

    def note(self, bucket, key, line, lineno):
        bucket[key] += 1
        self.ctx.setdefault((id(bucket), key), (lineno, line[:110]))

    # ── 域::… 形态 ──
    def eat_domref(self, s, i, line, lineno):
        m = RE_DOMREF.match(s, i)
        if not m:
            return None
        dom, j = m.group(1), m.end()
        mt = RE_TOKEN.match(s, j)
        subj = mt.group(0) if mt else ''
        j = mt.end() if mt else j
        while j < len(s) and s[j] == '.':
            ma = RE_TOKEN.match(s, j + 1)
            if not ma:
                break
            attr, j = ma.group(0), ma.end()
            is_call = j < len(s) and s[j] in '(（'
            if dom not in G.DOMAIN_MAP:
                self.note(self.gap_domain, dom, line, lineno)
                return j
            if is_call:
                if G.call_side(dom, attr) is None:
                    self.note(self.gap_call, '%s::…%s()' % (dom, attr), line, lineno)
                else:
                    self.consumed['函数调用'] += 1
            elif G.pair_side(dom, attr, subj) is None:
                # 🔴 patch13：带主体判定——纯数字属性位分三类（真属性位 / 域值的数字ID后缀 /
                #   转储原始数值引用），不带主体判不出 B 类。
                self.note(self.gap_attr, '%s::%s.%s' % (dom, subj, attr), line, lineno)
            else:
                self.consumed['属性访问'] += 1
        if dom not in G.DOMAIN_MAP:
            self.note(self.gap_domain, dom, line, lineno)
            return j
        self.consumed['域引用'] += 1
        self.check_val(dom, subj, line, lineno)
        return j

    def check_val(self, dom, val, line, lineno):
        if not val:
            return
        if RE_NUM.fullmatch(val) or RE_EVENTID.match(val):
            self.consumed['字面量/事件ID'] += 1
        elif val in G.SPECIAL_VALS or is_slotlike(val):
            self.consumed['槽/特殊值'] += 1
        elif dom in G.ENTITY_DOMAINS:
            self.consumed['具名实体（名字表/兜底）'] += 1
        elif G.val_side(dom, val) is not None:
            self.consumed['域值'] += 1
        else:
            self.note(self.gap_dval, '%s::%s' % (dom, val), line, lineno)

    # ── 台词正文：正文自然语言不检查，插值引用要检查 ──
    def eat_talk(self, payload, line, lineno):
        self.consumed['台词正文'] += 1
        for m in RE_INTERP.finditer(payload):
            inner = (m.group(1) or m.group(2) or m.group(3) or '').strip()
            if not inner or not RE_TOKEN.search(inner):
                continue                  # 纯标点/自然语言括注
            if '.' in inner:
                subj, attr = inner.split('.', 1)
                if attr not in G.TEXT_FIELDS and not attr_known_anydomain(attr):
                    self.note(self.gap_interp, '?.%s' % attr, line, lineno)
                    continue
                if not (is_slotlike(subj) or subj in G.SPECIAL_VALS or subj in NAMES
                        or G.entity_side(subj)):
                    self.note(self.gap_interp, subj, line, lineno)
                    continue
                self.consumed['台词插值（主体.字段）'] += 1
                continue
            if (inner in G.TEXT_VARS or inner in G.ENUM_SETS['主命目標類']
                    or G.RE_UNKNOWN_N.match(inner)):
                self.consumed['台词插值（文本变量）'] += 1
            elif (is_slotlike(inner) or inner in G.SPECIAL_VALS or inner in NAMES
                  or G.entity_side(inner) or RE_NUM.fullmatch(inner)):
                self.consumed['台词插值（槽/具名）'] += 1
            else:
                self.note(self.gap_interp, inner, line, lineno)

    # ── 参数位：一个参数的完整文本 ──
    def check_arg(self, cmd, pos, text, line, lineno, args=None):
        t = text.strip()
        if not t:
            self.consumed['空参'] += 1
            return
        if RE_NUM.fullmatch(t) or RE_EVENTID.match(t):
            self.consumed['字面量/事件ID'] += 1
            return
        if is_slotlike(t) or t in G.SPECIAL_VALS:
            self.consumed['槽/特殊值'] += 1
            return
        if t.startswith('[[') and t.endswith(']]'):
            self.eat_talk(t[2:-2], line, lineno)      # 字面文本参数（變名對話 的临时姓/名）
            return
        if G.RE_UNKNOWN_N.match(t) or RE_HEXSEQ.fullmatch(t):
            self.consumed['解析碎片（未知N/字节）'] += 1
            return
        md = RE_ARG_DOMREF.match(t)
        if md:                                    # 整参 = 域::值（值可含 、（） 等，如 儲存號::大阪之陣、治長役）
            dom, val = md.group(1), md.group(2)
            if dom not in G.DOMAIN_MAP:
                self.note(self.gap_domain, dom, line, lineno)
            else:
                self.consumed['域引用'] += 1
                self.check_val(dom, val, line, lineno)
            return
        mc = RE_ARG_CALL.match(t)
        if mc and G.call_side(None, mc.group(1)) is not None:
            # 带参属性调用（容器篩選:(忍者衆,外交同盟(大名家Ａ),2) 的第二参）
            self.consumed['带参属性调用'] += 1
            self.check_arg(cmd, pos, mc.group(2), line, lineno, args)
            return
        if '::' not in t and not any(o in t for o in EXPR_OPS):
            # 整参当作一个词条查（资源名可含括号/②/·，如 移動·船(メイン)、賭場特別②）
            if G.arg_side(cmd, pos, t, args) is not None:
                self.consumed['命令参数（已登记）'] += 1
            elif t in NAMES:
                self.consumed['命令参数（名字表具名）'] += 1
            else:
                self.note(self.gap_arg, '%s[pos%s]:%s' % (cmd, pos, t), line, lineno)
            return
        self.eat_expr(t, line, lineno, cmd, pos)      # 表达式：域引用/运算/嵌套调用

    # ── 表达式 / 无命令头行：逐段消费 ──
    def eat_expr(self, s, line, lineno, cmd=None, pos=None):
        i, n = 0, len(s)
        while i < n:
            c = s[i]
            if s.startswith('[[', i):
                e = s.find(']]', i)
                self.eat_talk(s[i + 2:e if e >= 0 else n], line, lineno)
                i = (e + 2) if e >= 0 else n
                continue
            if s.startswith('//', i):
                self.consumed['结构（注释）'] += 1
                return
            if c in SKIP_CHARS or c in GROUP_CHARS:
                i += 1
                continue
            op = next((o for o in OPS if s.startswith(o, i)), None)
            if op:
                self.consumed['操作符'] += 1
                i += len(op)
                continue
            j = self.eat_domref(s, i, line, lineno)
            if j is not None and j > i:
                i = j
                continue
            m = RE_TOKEN.match(s, i)
            if m:
                self.check_arg(cmd, pos, m.group(0), line, lineno)
                i = m.end()
                continue
            self.residue[c] += 1
            self.ctx.setdefault((id(self.residue), c), (lineno, line[:110]))
            i += 1

    # ── 命令行：头 → 头值 → 参数位（跨括号组连续编号）──
    def scan_line(self, line, lineno):
        s = line.strip()
        if not s or s.startswith('#'):
            return
        if s.startswith('}') or s.startswith('{'):
            self.consumed['结构（块首尾）'] += 1
            return
        if s == '太閣立志傳５事件源文件':
            self.consumed['结构（文件头）'] += 1
            return
        m = RE_HEAD.match(s)
        if not m:
            self.eat_expr(s, line, lineno)
            return
        cmd = m.group(1)
        if cmd_known(cmd):
            self.consumed['命令头'] += 1
        else:
            self.note(self.gap_cmd, cmd, line, lineno)
        rest, i, n, pos = s[m.end():], 0, len(s) - m.end(), 0
        seen_group, prev_args = False, []
        while i < n:
            c = rest[i]
            if rest.startswith('[[', i):
                e = rest.find(']]', i)
                self.eat_talk(rest[i + 2:e if e >= 0 else n], line, lineno)
                i = (e + 2) if e >= 0 else n
                continue
            if rest.startswith('//', i):
                self.consumed['结构（注释）'] += 1
                return
            if c == '(':
                depth, j = 1, i + 1
                while j < n and depth:
                    if rest[j] == '(':
                        depth += 1
                    elif rest[j] == ')':
                        depth -= 1
                    j += 1
                gargs = self.split_args(rest[i + 1:j - 1])
                allargs = gargs if pos == 0 else (prev_args + gargs)
                for arg in gargs:
                    self.check_arg(cmd, pos, arg, line, lineno, allargs)
                    pos += 1
                prev_args += gargs
                seen_group = True
                i = j
                continue
            if c in SKIP_CHARS or c in GROUP_CHARS:
                i += 1
                continue
            op = next((o for o in OPS if rest.startswith(o, i)), None)
            if op:
                self.consumed['操作符'] += 1
                i += len(op)
                continue
            mh = RE_TOKEN.match(rest, i)
            if mh and not seen_group:            # 命令头值（屬性:一次｜弱 / 發生契機:據點畫面表示後）
                self.check_head_val(cmd, mh.group(0), line, lineno)
                i = mh.end()
                continue
            if mh:
                self.check_arg(cmd, pos, mh.group(0), line, lineno)
                pos += 1
                i = mh.end()
                continue
            self.residue[c] += 1
            self.ctx.setdefault((id(self.residue), c), (lineno, line[:110]))
            i += 1

    def check_head_val(self, cmd, tok, line, lineno):
        if G.arg_side(cmd, '*', tok) is not None:
            self.consumed['命令头值（已登记）'] += 1
        elif G.entity_side(tok) or tok in NAMES or is_slotlike(tok) or tok in G.SPECIAL_VALS:
            self.consumed['命令头值（具名/槽）'] += 1
        elif RE_NUM.fullmatch(tok) or RE_EVENTID.match(tok):
            self.consumed['字面量/事件ID'] += 1
        else:
            self.note(self.gap_arg, '%s[头值]:%s' % (cmd, tok), line, lineno)

    @staticmethod
    def split_args(inner):
        """顶层逗号切分（跳过嵌套括号，保留全角括号内容如 決定音（バーン！））。"""
        out, buf, depth = [], '', 0
        for ch in inner:
            if ch == '(':
                depth += 1
            elif ch == ')':
                depth -= 1
            if ch in ',，' and depth == 0:
                out.append(buf)
                buf = ''
            else:
                buf += ch
        out.append(buf)
        return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--source', default=DEFAULT_SOURCE)
    ap.add_argument('--workdir', default=DEFAULT_WORKDIR)
    ap.add_argument('--top', type=int, default=40)
    args = ap.parse_args()

    os.makedirs(args.workdir, exist_ok=True)
    copy = os.path.join(args.workdir, 'TK5AllEvents_merged.copy.txt')
    shutil.copy2(args.source, copy)       # 🔴 原文件只读，全部操作走副本
    print('📄 语料副本: %s（原文件未改动）' % copy)

    sc = Scanner()
    with open(copy, encoding='utf-8') as f:
        for no, line in enumerate(f, 1):
            sc.scan_line(line.rstrip('\n'), no)

    print('\n✅ 已扣除（注册表/规则可解释）：')
    for k, v in sc.consumed.most_common():
        print('   %8d  %s' % (v, k))

    buckets = [('域表外', sc.gap_domain), ('属性表外', sc.gap_attr), ('函数表外', sc.gap_call),
               ('域值表外', sc.gap_dval), ('命令表外', sc.gap_cmd), ('命令参数位表外', sc.gap_arg),
               ('台词插值表外', sc.gap_interp), ('未识别字符', sc.residue)]
    total = sum(sum(b.values()) for _, b in buckets)
    print('\n%s' % ('🎉 零残渣' if total == 0 else '🔴 残渣合计 %d 处' % total))
    report = []
    for name, b in buckets:
        if not b:
            continue
        print('\n── %s：%d 种 / %d 处 ──' % (name, len(b), sum(b.values())))
        report.append('\n===== %s：%d 种 / %d 处 =====' % (name, len(b), sum(b.values())))
        for k, c in b.most_common(args.top):
            ln, ex = sc.ctx.get((id(b), k), ('', ''))
            print('   %6d  %s' % (c, k))
            print('           L%s: %s' % (ln, ex))
        for k, c in b.most_common():
            ln, ex = sc.ctx.get((id(b), k), ('', ''))
            report.append('%6d  %s\n        L%s: %s' % (c, k, ln, ex))
        if len(b) > args.top:
            print('   … 另有 %d 种（全量见 %s/residue_report.txt）' % (len(b) - args.top, args.workdir))

    with open(os.path.join(args.workdir, 'residue_report.txt'), 'w', encoding='utf-8') as f:
        f.write('\n'.join(report))
    return 0 if total == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
