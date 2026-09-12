#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""据点归属 Owner_<年> —— 以「据点日志」为**唯一信源**（2026-09-12 用户裁定）
============================================================================
**数据源**：`Knowledge/太阁5/太阁日志/据点日志.md` 的 `Log: SET|<年>|<序号>|<类型>|<名>|当主:<人>|势力:<家>|…`
—— **每个据点每年一行**，直接给出该城当年的 **当主**（= 城主）。274 据点 × 6 代 = 1644 行。

**为什么用它而不是「上级日志」**（原先的做法，已弃）：
  上级日志是**按人记**的（每人一个 `据点` 字段 = 当年在哪），**没人驻守的城就没有行**——
  实测 1644 格里 **436 格当年无人驻守**（八户城全六代没人），只能沿用退役快照兜底。
  据点日志是**按据点记**的、每格都有当主 → 全列可推、**零兜底**。（两条路在 1156 格上结论一致。）

**规则（一条）**：`Owner_<年>` = 该城当年 `当主`；当主 = `-` → 空（实测 398 格，与我们表里的空值完全吻合）。

**当主名字 → 英雄 id**（名字是**当年显示名**，会随袭名变，必须带年代过滤）：
  ① 先按 `CNName` 精确匹配，再退到 `Alias`（`|` 分隔，铁律 25）
  ② **只保留该年 `Appear_<年>` = `已登场` 的候选**——反例：`河野通直` 父子同名，
     1582 当主是**儿子**（`lord_tk5_300`，当年袭用父名），父亲（`lord_tk5_906`）当年已死亡
  ③ 仍多个候选 → **报硬错误**（不猜）

**对位**：日志 `序号` − 1 = 我们的 `tk` 编号（0 处不符，脚本内断言名字前缀）。

**改数据一律改本脚本再重跑**（铁律 22）。⚠️ 只动 `Owner_<年>` 六列；
`Clan_<年>` 由 `gen_taikou_clan_csv.py` 从 Owner 派生，**改完必须跟着重跑**。

Usage:
  python Scripts/gen_taikou_settlement_owner.py            # 报告（不写盘）
  python Scripts/gen_taikou_settlement_owner.py --apply    # 落盘
  python Scripts/gen_taikou_settlement_owner.py --check    # 校验产物（进一键体检）
Exit: 0 正常 / 1 有硬问题 / 2 fatal。
"""
import argparse
import collections
import csv
import io
import os
import re
import shutil
import sys
import time

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from tk5_pua_names import restore  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
SETT = os.path.join(CSV_DIR, "Settlements.csv")
HERO = os.path.join(CSV_DIR, "TaikouHero.csv")
LOG = os.path.join(REPO, "Knowledge", "太阁5", "太阁日志", "据点日志.md")

ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
NO_LORD = ("", "-")

# 袭名歧义（名字撞车）：「当年显示名 → 那一年到底指谁」——逐条带证据，别加无证据的条目。
# 🔴 为什么不能用 Alias 解决：`finalize_alias_and_ids.py` 的闸门明令「一个别名键只能映射一个 id」
#    （撞车 = 报错回滚，因为 `_load_hero_aliases` 是先到先得，会静默解析错人）。
# 河野通直：父子同名（父 lord_tk5_906 卒 1572 / 子 lord_tk5_300 生 1564），
#   子袭父名 → 1582 年日志显示的都是「河野通直」，但当年在汤筑城当主的是**子**。
#   证据（上级日志逐人数据）：`SUP|1582|301|300|河野通直|立场:当主|组织:河野家|据点:汤筑城`；
#   父是 `SUP|1582|907|906|河野通直|立场:其他|势力:浪人|据点:胜山馆`（收容槽 = 已死亡）。
NAME_AMBIGUOUS = {("河野通直", "1582"): "lord_tk5_300"}


def load_log():
    """据点日志 → {(年, 序号): {"名","当主"}}。"""
    out = {}
    for line in io.open(LOG, encoding="utf-8", errors="replace"):
        m = re.search(r"Log: SET\|(.*)$", line.rstrip("\n"))
        if not m:
            continue
        p = m.group(1).split("|")
        if p[0] in ("HDR", "SEG"):
            continue
        f = dict(x.split(":", 1) for x in p[4:] if ":" in x)
        out[(p[0], int(p[1]))] = {"名": restore(p[3]).strip(),
                                  "当主": restore(f.get("当主", "")).strip()}
    return out


def load_heroes():
    with io.open(HERO, encoding="utf-8-sig", newline="") as fh:
        return [r for r in csv.DictReader(fh) if r.get("ID")]


def tk_no(sid):
    """表 id → 日志序号（tk 编号 + 1）。"""
    return int(re.search(r"_tk(\d+)$", sid).group(1)) + 1


def resolver(heroes):
    """当主名 → 英雄 id。带年代过滤（当年显示名）。返回 (id, 诊断) 或 (None, 原因)。"""
    by_cn, by_alias = collections.defaultdict(list), collections.defaultdict(list)
    for r in heroes:
        by_cn[r["CNName"].strip()].append(r)
        for a in (r.get("Alias") or "").split("|"):
            if a.strip():
                by_alias[a.strip()].append(r)

    def resolve(nm, era):
        pin = NAME_AMBIGUOUS.get((nm, era))
        if pin:
            return pin, ""
        exact = by_cn.get(nm) or by_alias.get(nm)
        if not exact:
            return None, "英雄表里没有这个人"
        alive = [r for r in exact if r.get("Appear_" + era) == "已登场"]
        if not alive:
            # 🔴 不静默退回：日志说他是该城当主、但他当年「未登场/已死亡」= 要么袭名撞车
            #    （登记进 NAME_AMBIGUOUS）、要么数据本身有问题——两种都要人看一眼。
            return None, "该年无人登场（候选 %s，检查袭名撞车）" % [r["ID"] for r in exact]
        if len(alive) > 1:
            return None, "当年候选中 %d 个：%s" % (len(alive), [r["ID"] for r in alive])
        return alive[0]["ID"], ""
    return resolve


def build():
    sett, heroes = load_log(), load_heroes()
    resolve = resolver(heroes)
    with io.open(SETT, encoding="utf-8-sig", newline="") as fh:
        rows = list(csv.reader(fh))
    # Settlements.csv = 双行表头（中文行 + 英文行，见 CLAUDE.md CSV 表头规范）→ 键取第 2 行
    cn_hdr, hdr = rows[0], rows[1]
    body = [r for r in rows[2:] if r and any(x.strip() for x in r)]
    problems, changes, warns = [], [], []
    seen = collections.Counter()
    for r in body:
        sid = r[hdr.index("id")]
        no = tk_no(sid)
        for e in ERAS:
            rec = sett.get((e, no))
            if rec is None:
                problems.append("%s %s：日志缺这一格" % (sid, e))
                continue
            seen[e] += 1
            cur = r[hdr.index("Owner_" + e)].strip()
            nm = rec["当主"]
            if nm in NO_LORD:
                exp = ""
            else:
                exp, why = resolve(nm, e)
                if exp is None:
                    problems.append("%s %s 当主「%s」：%s" % (sid, e, nm, why))
                    continue
                if why:
                    warns.append("%s %s 当主「%s」：%s" % (sid, e, nm, why))
            if exp != cur:
                changes.append((sid, rec["名"], e, cur, exp))
        # 对位断言：日志名必须是表里名字的前缀（序号↔tk 编号 的护栏）
        for e in ERAS:
            rec = sett.get((e, no))
            if rec and rec["名"] and not any(
                    (r[hdr.index("Name_" + e)] or "").startswith(rec["名"]) or
                    (r[hdr.index("Name_All")] or "").startswith(rec["名"]) for _ in [0]):
                problems.append("%s %s：日志名「%s」与表里名「%s」对不上（序号对位错？）"
                                % (sid, e, rec["名"], r[hdr.index("Name_" + e)]))
    if len(sett) != len(ERAS) * len(body):
        problems.append("日志格数 %d ≠ 表 %d 行 × %d 代" % (len(sett), len(body), len(ERAS)))
    return {"cn_hdr": cn_hdr, "hdr": hdr, "body": body, "changes": changes,
            "problems": problems, "warns": warns}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--module", default=None, help="兼容 run_all_checks 的接口（忽略）")
    args = ap.parse_args()

    for p in (SETT, HERO, LOG):
        if not os.path.isfile(p):
            print("[FATAL] 缺文件 %s" % p, file=sys.stderr)
            return 2
    data = build()
    n = len(data["changes"])
    print("据点日志口径：应改 %d 格（当主 = `-` → 清空 %d 格）"
          % (n, sum(1 for c in data["changes"] if not c[4])))
    for sid, nm, e, cur, exp in data["changes"]:
        print("   %-14s %-7s %s  %s → %s" % (sid, nm, e, cur or "(空)", exp or "(空)"))
    for w in data["warns"][:10]:
        print("   ⚠ %s" % w)

    if data["problems"]:
        print("\n❌ 硬问题 %d 条：" % len(data["problems"]))
        for x in data["problems"][:20]:
            print("   %s" % x)
        return 1
    if args.check:
        if n:
            print("[CHECK] ❌ 有 %d 格与据点日志不符（跑 --apply 修正）" % n)
            return 1
        print("[CHECK] ✅ 与据点日志一致（1644 格全部由日志推出）")
        return 0
    if not args.apply:
        print("\n（未加 --apply，只报告不写盘）")
        return 0
    if not n:
        print("\n无需改动。")
        return 0

    idx = {c: k for k, c in enumerate(data["hdr"])}
    pos = {r[idx["id"]]: r for r in data["body"]}
    applied = 0
    for sid, nm, e, cur, exp in data["changes"]:
        pos[sid][idx["Owner_" + e]] = exp
        applied += 1

    stamp = time.strftime("%Y%m%d_%H%M%S")
    shutil.copy2(SETT, SETT + ".bak_owner_" + stamp)
    with io.open(SETT, "w", encoding="utf-8-sig", newline="") as fh:
        w = csv.writer(fh, lineterminator="\r\n", quoting=csv.QUOTE_MINIMAL)
        for r in [data["cn_hdr"], data["hdr"]] + data["body"]:
            w.writerow(r)
    back = list(csv.reader(io.open(SETT, encoding="utf-8-sig", newline="")))
    print("\n✅ 已写出 Settlements.csv（改 %d 格；%d 行 / %d 列）；往返读回 %d 行 / %d 列"
          % (applied, len(data["body"]), len(data["hdr"]), len(back) - 1, len(back[0])))
    again = build()
    print("   幂等复跑：剩余应改 %d 格（应为 0）" % len(again["changes"]))
    if again["changes"]:
        for sid, nm, e, cur, exp in again["changes"][:5]:
            print("     ✗ %s %s %s %s → %s" % (sid, nm, e, cur, exp))
        return 1
    print("   ⚠️ 连带：Clan_<年> 由 Owner 派生 → 必须重跑 `python Scripts/gen_taikou_clan_csv.py --apply`")
    return 0


if __name__ == "__main__":
    sys.exit(main())
