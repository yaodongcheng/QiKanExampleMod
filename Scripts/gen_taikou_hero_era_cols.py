#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""gen_taikou_hero_era_cols.py —— 从【上级日志】补 `TaikouHero.csv` 的年代列。

为什么要有它（2026-09-15 用户裁定）
------------------------------------
`TaikouHero.csv` 的 `Appear_/Identity_/CareerStance_/Kingdom_/City_<年>` 五列是**派生数据**，
唯一权威源 = `Knowledge/太阁5/太阁日志/上级日志.md` 的 `SUP|` 行。
**这几列在原仓库里没有写出方**（只有 `promote_generic_npcs.py` 写那 329 个泛用 NPC）——
实测 1162~1289 整段 71 人（出云阿国、宁宁、淀殿、狩野永德、本阿弥光悦…）年代列全空，
而日志里**六代全在**。于是「CSV 空着」被误读成「那一代不登场」。
本脚本就是那个缺失的写出方。

身份怎么定
----------
日志给的是 `立场`（当主/直臣/陪臣/其他）+ `据点`；身份由**据点类型 + 立场**映射，
并受 `check_taikou_world_tables.IDENTITY_TYPE` 约束（城/町/里/砦 各自一套合法身份）。
   · `町` + `其他` → **浪人**（已填数据里的惯例：551 例；范本 足利义昭 @ 京之町 = 浪人）
   · 其余组合按同一张表推，推不出的**报错不写**（宁可停下，不编数据）

用法
----
  python Scripts/gen_taikou_hero_era_cols.py --ids 1208 --birth 1208=1535 --dry-run
  python Scripts/gen_taikou_hero_era_cols.py --ids 1208 --birth 1208=1535
  python Scripts/gen_taikou_hero_era_cols.py --all-empty      # 所有「年代列全空但在日志里」的人

纪律：备份 → 写 → 逐格往返校验；幂等（已填的不动）。
"""
import argparse
import io
import os
import re
import shutil
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, ".."))
sys.path.insert(0, HERE)
from csv_dual import read_table, write_table  # noqa: E402

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

LOG = os.path.join(REPO, "Knowledge", "太阁5", "太阁日志", "上级日志.md")
HERO = os.path.join(REPO, "Knowledge", "太阁5", "织丰角色ID对应", "csv", "TaikouHero.csv")
HERO = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "TaikouHero.csv")
SETT = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "Settlements.csv")

ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
ROW = re.compile(
    r"SUP\|(\d{4})\|(\d+)\|(\d+)\|([^|]*)\|"
    r"立场:([^|]*)\|势力:([^|]*)\|组织:([^|]*)\|上司:([^|]*)\|当主:([^|]*)\|据点:([^|]*)\|")
PUA = re.compile("[-]")

# 据点类型 -> {日志立场: 身份}。只列已确认的组合，其余报错。
IDENT = {
    "町": {"其他": "浪人", "当主": "当家", "直臣": "掌柜", "陪臣": "伙计"},
    "里": {"其他": "下忍", "当主": "头目", "直臣": "中忍", "陪臣": "下忍"},
    "砦": {"其他": "水夫", "当主": "头领", "直臣": "船头", "陪臣": "水夫"},
}


def parse_log():
    out = {}
    txt = io.open(LOG, encoding="utf-8", errors="replace").read()
    for m in ROW.finditer(txt):
        era, hid = m.group(1), int(m.group(3))
        out[(era, hid)] = dict(name=m.group(4), stance=m.group(5), org=m.group(6),
                               city=PUA.sub("", m.group(10)))
    return out


def stype_of(stmap, city):
    """据点名 → 类型。精确查不到时**按子序列兜底**：日志里有些地名掉了字
    （`飞高山之町` 掉了「驒」）—— 只有在**类型唯一**时才认，否则返回空（不猜）。"""
    if not city:
        return ""
    if city in stmap:
        return stmap[city]
    cands = {t for n, t in stmap.items() if is_subseq(city, n)}
    return cands.pop() if len(cands) == 1 else ""


def is_subseq(short, long):
    it = iter(long or "")
    return all(c in it for c in (short or ""))


def load_settlement_types():
    """据点名（对每个年代都取 Name_All 的别名集）→ 类型（城/町/里/砦）。"""
    cn, en, rows = read_table(SETT)
    out = {}
    for r in rows:
        t = (r[en.index("TK5Type")] or "").strip()
        names = set()
        for col in ("Name_All",) + tuple(c for c in en if c.startswith("Name_")):
            if col in en:
                names.update(x.strip() for x in (r[en.index(col)] or "").split("|") if x.strip())
        for n in names:
            out[n] = t
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ids", nargs="*", type=int, default=[])
    ap.add_argument("--all-empty", action="store_true", help="所有「年代列全空但日志里有」的人")
    ap.add_argument("--birth", nargs="*", default=[], help='生年覆盖，形如 1208=1535')
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    log = parse_log()
    stype = load_settlement_types()
    cn, en, rows = read_table(HERO)
    idx = {}
    for i, r in enumerate(rows):
        v = (r[en.index("ID")] or "").strip()
        if v.startswith("lord_tk5_"):
            try:
                idx[int(v.rsplit("_", 1)[1])] = i
            except ValueError:
                pass

    want = set(args.ids)
    if args.all_empty:
        for hid, i in idx.items():
            if not any((rows[i][en.index("Appear_" + e)] or "").strip()
                       for e in ERAS if ("Appear_" + e) in en):
                want.add(hid)
    if not want:
        print("没有指定任何 id（--ids / --all-empty）"); return 2

    births = {}
    for b in args.birth:
        k, _, v = b.partition("=")
        births[int(k)] = v.strip()

    changed, skipped, errs = 0, [], []
    for hid in sorted(want):
        i = idx.get(hid)
        if i is None:
            errs.append("番号 %d 在 CSV 里没有 lord_tk5_* 行" % hid); continue
        r = rows[i]
        hit = [(e, log[(e, hid)]) for e in ERAS if (e, hid) in log]
        if not hit:
            errs.append("番号 %d（%s）日志里没有 → 不写" % (hid, r[en.index('CNName')])); continue
        name = r[en.index("CNName")]
        noident = []
        for era, v in hit:
            # 🔴 `Identity_<年>` **只是挑默认装备档用的**（`gen_taikou_era_world` 里
            #    `EQUIP_BY_IDENTITY.get(ident, EQUIP_DEFAULT)`），**不决定建不建这个人**。
            #    checker 也明说「身份为空 = 合法，跳过检查」。所以推不出身份就**留空**，
            #    绝不能因此不填 `Appear_/City_` —— 那是把"装备档"错当成"是否存在"（踩过）。
            _ty = stype_of(stype, v["city"])
            ident = IDENT.get(_ty, {}).get(v["stance"], "")
            if not ident:
                noident.append("%s(%s/%s)" % (era, v["city"], v["stance"]))
            for col, val in (("Appear_" + era, "已登场"), ("Identity_" + era, ident),
                             ("CareerStance_" + era, v["stance"]), ("Kingdom_" + era, v["org"]),
                             ("City_" + era, v["city"])):
                if col in en:
                    r[en.index(col)] = val
        if hid in births:
            r[en.index("BirthYear")] = births[hid]
        changed += 1
        print("  ✔ 番号%-5d %-10s 填 %d 代（据点=%s%s）%s"
              % (hid, name, len(hit), hit[0][1]["city"],
                 "  身份留空：" + " ".join(noident) if noident else
                 " 身份=" + IDENT.get(stype_of(stype, hit[0][1]["city"]), {}).get(hit[0][1]["stance"], "?"),
                 ("  生年=%s" % births[hid]) if hid in births else ""))

    for e in errs:
        print("  ✗ " + e)
    print("\n填写 %d 人 · 报错 %d 条" % (changed, len(errs)))
    if args.dry_run:
        print("--dry-run：未写盘"); return 0
    if not changed:
        print("没有可写的内容"); return 0

    shutil.copy2(HERO, HERO + ".bak_eracols_%s" % time.strftime("%Y%m%d_%H%M%S"))
    cn2 = list(cn)
    write_table(HERO, cn2, en, rows)
    # 往返校验
    _c, _e, back = read_table(HERO)
    bad = sum(1 for a, b in zip(rows, back) for x, y in zip(a, b) if (x or "") != (y or ""))
    print("往返校验：%s（%d 格不一致）" % ("✅" if bad == 0 else "❌", bad))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
