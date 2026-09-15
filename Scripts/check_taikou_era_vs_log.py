#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""check_taikou_era_vs_log.py —— 把 `TaikouHero.csv` 的年代列和【上级日志】逐条对账。

为什么要有这个（2026-09-15 用户裁定）
--------------------------------------
`TaikouHero.csv` 的 `Appear_/City_/CareerStance_/Kingdom_<年>` 列是**派生数据**，
唯一权威源是 `Knowledge/太阁5/太阁日志/上级日志.md` 的 `SUP|` 行（tkhack 实机导出）。
「CSV 里空着」**不等于**「这个人那一代不登场」—— 实测踩过：出云阿国（1208）CSV 六代全空，
日志里六代都在。所以任何"谁登场/谁在哪"的结论**必须对本脚本**，不许只读 CSV 就下判断。

日志行格式
----------
    SUP|<年>|<槽位>|<人物番号>|<名字>|立场:X|势力:X|组织:X|上司:X|当主:X|据点:X|部下:N|陪臣:N
CSV 里对应的列：`Appear_<年>`（有行=已登场）/`CareerStance_<年>`（立场）/`Kingdom_<年>`（组织）/`City_<年>`（据点）

用法
----
    python Scripts/check_taikou_era_vs_log.py            # 报告
    python Scripts/check_taikou_era_vs_log.py --limit 40 # 每类只列前 N 条
退出码：0 = 无冲突；1 = 有冲突
"""
import argparse
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, ".."))
sys.path.insert(0, HERE)
from csv_dual import read_table  # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

LOG = os.path.join(REPO, "Knowledge", "太阁5", "太阁日志", "上级日志.md")
HERO = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "TaikouHero.csv")

def is_subseq(short, long):
    """short 是不是 long 的子序列（顺序保留、允许中间少字）。
    用来认【日志侧缺字】：日志的 `之町` 是 CSV `堺之町` 的子序列、`岩城` 是 `岩槻城` 的子序列
    —— 那几个字在日志里是 PUA 未还原被吞掉的，**不是 CSV 写错**。"""
    it = iter(long or "")
    return all(c in it for c in (short or ""))


# 私用区（PUA）：日志里那几个字是**未被还原的 PUA 码点**，终端看不见
# （显示成 `之町`，实际字符串是 `<PUA>之町`）—— 不剥掉会把它当"值不同"误报成冲突。
PUA = re.compile("[-]")


def log_char_loss(csv_v, log_v):
    """True = 差异来自【日志侧缺字 / PUA 未还原】—— CSV 是对的，不算冲突。"""
    if not log_v or log_v == csv_v:
        return False
    lv = PUA.sub("", log_v)
    return bool(lv) and is_subseq(lv, csv_v) and (len(csv_v) - len(lv)) <= 4


def norm_stance(v):
    """立场归一化：CSV 写成 `陪臣（松平元康）`（**多带上司**，更细）· 日志写 `陪臣`；
    另外 CSV 用「首领」、日志用「当主」——同一个意思。不归一化会造出几千条假冲突。"""
    v = re.sub(r"[（(].*?[)）]", "", (v or "")).strip()
    return {"首领": "当主"}.get(v, v)


ROW = re.compile(
    r"SUP\|(\d{4})\|(\d+)\|(\d+)\|([^|]*)\|"
    r"立场:([^|]*)\|势力:([^|]*)\|组织:([^|]*)\|上司:([^|]*)\|当主:([^|]*)\|据点:([^|]*)\|")


def parse_log():
    """→ {(年, 人物番号): dict(name=, stance=, force=, org=, boss=, lord=, city=)}"""
    out = {}
    txt = io.open(LOG, encoding="utf-8", errors="replace").read()
    for m in ROW.finditer(txt):
        era, _slot, hid = m.group(1), m.group(2), int(m.group(3))
        out[(era, hid)] = dict(name=m.group(4), stance=m.group(5), force=m.group(6),
                               org=m.group(7), boss=m.group(8), lord=m.group(9), city=m.group(10))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--limit", type=int, default=25)
    args = ap.parse_args()

    log = parse_log()
    eras = sorted({e for e, _ in log})
    print("日志：%s" % os.path.relpath(LOG, REPO))
    print("  %d 条 · 年代 %s · 人物 %d 个" % (len(log), eras, len({h for _, h in log})))

    cn, en, rows = read_table(HERO)
    by_id = {}
    for r in rows:
        i = (r[en.index("ID")] or "").strip()
        if i.startswith("lord_tk5_"):
            try:
                by_id[int(i.rsplit("_", 1)[1])] = r
            except ValueError:
                pass
    print("  CSV：%d 个 lord_tk5_* 行" % len(by_id))

    miss_log = []       # A 日志有 · CSV 空着（漏填）
    mismatch = []       # B CSV 有 · 与日志值不同
    no_row = []         # C 日志有 · CSV 没这一行
    charloss = []       # D 差异来自【日志侧缺字】（PUA 未还原），CSV 是对的
    cmp_cols = [("CareerStance_%s", "stance", "立场"), ("Kingdom_%s", "org", "组织"),
                ("City_%s", "city", "据点")]

    for (era, hid), v in sorted(log.items()):
        r = by_id.get(hid)
        if r is None:
            no_row.append((era, hid, v["name"]))
            continue
        ap_ = (r[en.index("Appear_" + era)] if ("Appear_" + era) in en else "").strip()
        if not ap_:
            miss_log.append((era, hid, v["name"], v["city"], v["stance"]))
        for tmpl, key, cnlab in cmp_cols:
            col = tmpl % era
            if col not in en:
                continue
            got = (r[en.index(col)] or "").strip()
            if not got:
                continue                      # 空的算 A 类，不重复报
            a, b = (norm_stance(got), norm_stance(v[key])) if key == "stance" else (got, v[key])
            if a != b:
                if key != "stance" and log_char_loss(a, b):
                    charloss.append((era, hid, v["name"], cnlab, got, v[key]))
                else:
                    mismatch.append((era, hid, v["name"], cnlab, got, v[key]))

    def dump(title, lst, fmt):
        print("\n%s：%d" % (title, len(lst)))
        for x in lst[:args.limit]:
            print("   " + fmt(x))
        if len(lst) > args.limit:
            print("   …（还有 %d 条）" % (len(lst) - args.limit))

    dump("A 日志里有、CSV 却空着（漏填）", miss_log,
         lambda t: "%s 番号%-5d %-10s 据点=%-8s 立场=%s" % t)
    dump("B CSV 有值、但与日志不符（冲突）", mismatch,
         lambda t: "%s 番号%-5d %-10s %s: CSV=%-10s 日志=%s" % t)
    dump("D 日志侧缺字（PUA 未还原）· CSV 是对的 —— 不算冲突", charloss,
         lambda t: "%s 番号%-5d %-10s %s: CSV=%-10s 日志=%s" % t)
    dump("C 日志里有、CSV 里没这一行", no_row, lambda t: "%s 番号%-5d %s" % t)

    nod = sorted({(h, n) for _, h, n in no_row})
    print("\nC 去重后：日志里出现、但没有 lord_tk5_* 行的番号 %d 个（多为泛用/模板 NPC，走 template_* 那批）"
          % len(nod))
    if nod:
        print("   番号区间：%d~%d" % (nod[0][0], nod[-1][0]))
    bad = len(miss_log) + len(mismatch)
    print("\n合计异常 %d 条（A 漏填 %d + B 冲突 %d）· C 另计 %d 条"
          % (bad, len(miss_log), len(mismatch), len(no_row)))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
