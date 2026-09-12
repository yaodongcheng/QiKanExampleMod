#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""泛用 NPC 提升为 hero —— 写进 TaikouHero.csv（2026-09-12 用户裁定）
============================================================================
**做什么**：把 `泛用heroID表_20260912.csv` 里的 329 人写成 `TaikouHero.csv` 的行。
**数据来源**：`泛用hero槽位对齐_20260912.csv`（一人一行 × 六年代列）+ 上级日志。

**年代列怎么填**（全部从日志推，不猜）：
  · `Appear_<年>`   = 该年他出现在日志里 → `已登场`
  · `Identity_<年>` = 类型 × 立场 → 职名（下表）——**必须落在 checker 的 IDENTITY_TYPE 里**，
                       否则 `TaikouHero：身份对应的据点类型必须与命中的据点一致` 会报红
  · `CareerStance_<年>` = 日志的 `立场`（首领/直臣/陪臣/其他）
  · `Kingdom_<年>`  = 日志的 `组织`
  · `City_<年>`     = 日志的 `据点`
  · `School_<年>`   = `无`

  | 类型 | 当主 | 直臣 | 陪臣 | 其他 |
  |---|---|---|---|---|
  | 忍者众（据点在「里」） | 头目 | 中忍 | 下忍 | 下忍 |
  | 海贼众（据点在「砦」） | 头领 | 船头 | 水夫 | 水夫 |
  | 商家（据点在「町」）   | 当家 | 掌柜 | 伙计 | 伙计 |

**不填的列**（不知道，不编）：
  · `原版编号` / `外观ID` / `模板NPC` / `Gender` / 生卒年 / 亲属 / 能力值 / 卡片
  · 🔴 **`FirstName`（苗字）= 组织名 —— 只给 16 个当主填**（2026-09-12 裁定）：
    家族生成器用家头的 `FirstName or CNName` 当家族名，留空 → 立出来的家叫「六郎次」
    「仙左卫门」（人名当家名）。现按当主的**组织名去掉类型尾缀**填（`透波众`→`透波`、
    `安东水军`→`安东`），家名即「透波家」「安东家」。
    连带 `EnglishName` 写成 `<苗字罗马音> <名罗马音>`（罗马音取自 `TaikouForce` 的组织 id，
    不另造读音）——家族 id 由它派生，于是 id ↔ 家名一致（`clan_suppa_1` ↔ 透波家）。
    非当主**不填**（他们不命名家族；苗字沿用其主家）。
  · `EnglishName` = 名字罗马音（家族 id 由它派生 → `check_englishname_clan_prefix` 要它非空）
  · 🔴 **`外观ID` = 从 5 个模板的立绘槽池里按 (职业, 性别) 轮转分配**（2026-09-12 用户裁定）：
    池子 = `template_ninja_male_01`（55 槽）/ `template_kunoichi_01`（2）/ `template_kaizoku_01`（55）/
    `template_kaizoku_female_01`（2）/ `template_merchant_01`（63）——**池子从模板行现读**（单一来源，不写死）。
    ⚠️ **槽池只有 177 个而人有 329** → 必然复用：按 id 排序**轮转**分配（均匀，不堆在同一个槽上），分布见脚本报告。
    ⚠️ **性别数据里没有**（`Gender` 列全空、日志也没有该字段）→ 女性池只按**名字判据** `FEMALE_NAMES` 分配，
    该名单是人工判断、逐条可改（报告里会列出谁被判成女性）。
  · 🔴 **`CultureID` = 职业对应的身份文化**（2026-09-12 用户裁定）：
    `ninja`→`ninja`（忍者文化）/ `pirate`→`pirate`（海贼文化）/ `trader`→`trader`（商人文化）。
    为什么必须填：家族文化取「成员多数文化」，成员没文化 → **家文化空**（实测 69 家：
    忍者 20 / 海贼 24 / 商家 25）；而骑砍里 Hero/Clan 的 Culture 为 null 有踩雷史。
    三个文化 id 与 `lord_tk5_<职业>_*` 同词，见 `Culture.csv`（`Is_Shokuho=0` 的自家文化，
    定义在 `Taikou/ModuleData/spcultures.xml`，由 `gen_taikou_culture_full.py` 产出）。


**纪律**：备份 + 往返校验 + 幂等（已存在的 hero_id 跳过，不重复写）。

Usage:
  python Scripts/promote_generic_npcs.py            # 报告 + 预览（不写盘）
  python Scripts/promote_generic_npcs.py --apply    # 落盘
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
IDS = os.path.join(os.path.dirname(CSV_DIR), "泛用heroID表_20260912.csv")
ALIGN = os.path.join(os.path.dirname(CSV_DIR), "泛用hero槽位对齐_20260912.csv")
HERO = os.path.join(CSV_DIR, "TaikouHero.csv")
FORCE = os.path.join(CSV_DIR, "TaikouForce.csv")
LOG = os.path.join(REPO, "Knowledge", "太阁5", "太阁日志", "上级日志.md")

ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]
IDENT = {
    "忍者众": {"当主": "头目", "直臣": "中忍", "陪臣": "下忍", "其他": "下忍"},
    "海贼众": {"当主": "头领", "直臣": "船头", "陪臣": "水夫", "其他": "水夫"},
    "商家": {"当主": "当家", "直臣": "掌柜", "陪臣": "伙计", "其他": "伙计"},
}
# 组织名的类型尾缀（去掉 = 苗字）：透波众→透波 / 安东水军→安东
ZOK_TAIL = ("众", "水军")

# 一次性 id 迁移（2026-09-12）：本轮修定了 3 个读音 → 旧 id 作废，改到新 id。
#  ①`lord_tk5_trader_xE413_兵卫` / `..._xE413_右卫门`：旧 id 前缀是私用区码点占位、
#    尾部带中文——中文进 id 违反铁律 20 → 换成纯 ASCII（前缀读不出，见 ID 表）
#  ②`lord_tk5_ninja_senzuiboo` → `senshikiboo`：音读名改正（泉識坊 = せんしきぼう）
# 三行迁完即可删掉本表。
RENAME = {
    "lord_tk5_trader_xE413_兵卫": "lord_tk5_trader_xe413bee",
    "lord_tk5_trader_xE413_右卫门": "lord_tk5_trader_xe413uemon",
    "lord_tk5_ninja_senzuiboo": "lord_tk5_ninja_senshikiboo",
}


# 🔴 立绘槽池来源模板（按职业 + 性别）与女性名字判据（人工判断，逐条可改）
POOL_TEMPLATE = {"ninja": "template_ninja_male_01", "kunoichi": "template_kunoichi_01",
                 "pirate": "template_kaizoku_01", "kaizoku_female": "template_kaizoku_female_01",
                 "trader": "template_merchant_01"}
# ⚠️ 数据里没有性别字段，这一批是**按名字判的**（日语中偏女性用名）：
#    吹雪/枣/枫/红叶（忍者系）· 多津/泷/鹤（海贼系）。
#    未收「鹤千代/虎千代/百千代」= 〜千代在江户commoner名里男女都用（同列表里虎千代/百千代是男名）。
FEMALE_NAMES = {"吹雪", "枣", "枫", "红叶", "多津", "泷", "鹤"}


def load_pools():
    """5 个模板的立绘槽池（从模板行现读，单一来源）。"""
    from csv_dual import dict_rows
    rows = {r["ID"]: r for r in dict_rows(HERO) if r.get("ID")}
    return {k: [x.strip() for x in rows[v]["AppearanceID"].split("|") if x.strip()]
            for k, v in POOL_TEMPLATE.items() if v in rows}


def assign_appearance(align, pools):
    """按 (职业, 性别) 轮转分配外观槽 → {hero_id: 槽号}。

    确定性：**按 hero_id 排序后依次取池子里的下一个**（第 n 个人拿 池[n % len(池)]）。
    """
    groups = collections.defaultdict(list)
    for a in sorted(align, key=lambda x: x["hero_id"]):
        tp = a["职业"]
        female = a["名前"] in FEMALE_NAMES
        if tp == "ninja":
            key = "kunoichi" if female else "ninja"
        elif tp == "pirate":
            key = "kaizoku_female" if female else "pirate"
        else:
            key = "trader"          # 商家没有女性模板（女商人→商人池）
        groups[key].append(a["hero_id"])
    out = {}
    for key, ids in groups.items():
        pool = pools[key]
        for n, hid in enumerate(ids):
            out[hid] = pool[n % len(pool)]
    return out, groups


def load_org_zok():
    """组织名 → (苗字, 苗字罗马音)。

    苗字 = 组织名去掉类型尾缀；罗马音**直接取 `TaikouForce` 的组织 id**
    （`org_ninja_suppa`→suppa、`org_pirate_antousuigun`→antou）——不另造读音，与势力表同源。
    """
    out = {}
    with io.open(FORCE, encoding="utf-8-sig", newline="") as fh:
        for r in csv.DictReader(fh):
            fid, nm = r.get("ID", ""), r.get("势力名", "")
            if not fid.startswith(("org_ninja_", "org_pirate_")):
                continue
            slug = fid.split("_", 2)[2]
            if slug.endswith("suigun"):
                slug = slug[:-len("suigun")]
            for t in ZOK_TAIL:
                if nm.endswith(t):
                    out[nm] = (nm[:-len(t)], slug)
                    break
    return out



def load_sup():
    """(=年, 槽号=) → {立场, 组织, 据点}"""
    sup = {}
    cur = None
    for line in io.open(LOG, encoding="utf-8", errors="replace"):
        m = re.search(r"Log: SUP\|(.*)$", line.rstrip("\n"))
        if not m:
            continue
        p = m.group(1).split("|")
        if p[0] == "HDR":
            cur = dict(x.split(":", 1) for x in p[1:] if ":" in x).get("年")
            continue
        if cur is None or len(p) < 5:
            continue
        f = dict(x.split(":", 1) for x in p[4:] if ":" in x)
        sup[(cur, p[2])] = {"名": restore(p[3]), "立场": f.get("立场", "其他"),
                            "组织": restore(f.get("组织", "无")), "据点": restore(f.get("据点", ""))}
    return sup


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true")
    args = ap.parse_args()

    for p in (IDS, ALIGN, HERO):
        if not os.path.isfile(p):
            print("[FATAL] 缺文件 %s" % p, file=sys.stderr)
            return 2

    with io.open(HERO, encoding="utf-8-sig", newline="") as fh:
        rows = list(csv.reader(fh))
    # TaikouHero.csv 双行表头（中文行 + 英文行）→ 键取第 2 行、数据从第 3 行起
    cn_hdr, hdr = rows[0], rows[1]
    body = [r for r in rows[2:] if r and any(x.strip() for x in r)]
    have = {r[hdr.index("ID")] for r in body}

    with io.open(ALIGN, encoding="utf-8-sig", newline="") as fh:
        align = list(csv.DictReader(fh))
    with io.open(IDS, encoding="utf-8-sig", newline="") as fh:
        idtab = {r["ID"]: r for r in csv.DictReader(fh)}
    sup = load_sup()
    org2zok = load_org_zok()
    pools = load_pools()
    appear, groups = assign_appearance(align, pools)

    # 一次性 id 迁移（见文件头 RENAME）
    by_id = {r[hdr.index("ID")]: r for r in body}
    renamed = []
    for old, new in RENAME.items():
        if old in by_id and new not in by_id:
            r = by_id.pop(old)
            r[hdr.index("ID")] = new
            by_id[new] = r
            renamed.append("%s → %s" % (old, new))

    new_rows, changes, problems = [], [], []
    for a in align:
        hid = a["hero_id"]
        it = idtab.get(hid)
        if it is None:
            problems.append("%s 不在 ID 表里" % hid)
            continue
        row = {c: "" for c in hdr}
        row["ID"] = hid
        row["CNName"] = a["名前"]
        row["CultureID"] = a["职业"]      # 身份文化：ninja/pirate/trader（见文件头）
        row["AppearanceID"] = appear.get(hid, "")   # 立绘槽（按职业+性别从模板池轮转，见文件头）
        row["EnglishName"] = it["罗马音"].capitalize() if not it["罗马音"].startswith("x") else ""
        tp = {"ninja": "忍者众", "pirate": "海贼众", "trader": "商家"}[a["职业"]]
        owner_org = ""
        for e in ERAS:
            slot = a.get("槽号_" + e, "").strip()
            if not slot:
                continue
            rec = sup.get((e, slot))
            if rec is None:
                problems.append("%s %s 槽号 %s 在日志里查无" % (hid, e, slot))
                continue
            ident = IDENT[tp].get(rec["立场"])
            if ident is None:
                problems.append("%s %s 立场 %r 无对应职名" % (hid, e, rec["立场"]))
                continue
            if rec["立场"] == "当主":
                owner_org = rec["组织"] or owner_org
            row["Appear_" + e] = "已登场"
            row["Identity_" + e] = ident
            row["CareerStance_" + e] = rec["立场"]
            row["Kingdom_" + e] = rec["组织"]
            row["City_" + e] = rec["据点"]
            row["School_" + e] = "无"
        # 当主 → 苗字 = 组织名（家族名），EnglishName 带上苗字（家族 id 由它派生）
        zok = org2zok.get(owner_org)
        if zok:
            row["FirstName"] = zok[0]
            row["EnglishName"] = zok[1].capitalize() + " " + it["罗马音"].capitalize()

        cur = by_id.get(hid)
        if cur is None:
            new_rows.append([row.get(c, "") for c in hdr])
        else:
            # 已存在的行**只更新本脚本负责的派生列**（其余列归别的生成器/来源，不覆盖）
            for c in ("FirstName", "EnglishName", "CultureID", "AppearanceID"):
                k = hdr.index(c)
                if cur[k] != row[c]:
                    changes.append((hid, c, cur[k], row[c]))
                    cur[k] = row[c]

    print("新增 %d 行 · 更新 %d 格 · 迁移 id %d 个（现有 %d 行 / 英雄 %d 个）"
          % (len(new_rows), len(changes), len(renamed), len(body), len(have)))
    for x in renamed:
        print("   ↪ %s" % x)
    if changes:
        print("   更新明细：")
        for hid, c, a_, b_ in changes[:20]:
            print("   %-34s %-12s %r → %r" % (hid, c, a_, b_))
        if len(changes) > 20:
            print("   …（其余 %d 格同类）" % (len(changes) - 20))
    print("立绘槽分配（池 %s）：" % " / ".join("%s=%d" % (k, len(pools[k])) for k in pools))
    for key in sorted(groups):
        ids = groups[key]
        n_slot = len(pools[key])
        cnt = collections.Counter(appear[h] for h in ids)
        dist = collections.Counter(cnt.values())
        print("   %-15s %3d 人 / %2d 槽 → %s；最多一个槽 %d 人"
              % (key, len(ids), n_slot,
                 " · ".join("%d 人槽 × %d" % (k, v) for k, v in sorted(dist.items())),
                 max(cnt.values())))
    fem = [h for h in appear if any(a["hero_id"] == h and a["名前"] in FEMALE_NAMES for a in align)]
    print("   判成女性（名字判据，可改）：%s"
          % " ".join("%s(%s)" % (next(a["名前"] for a in align if a["hero_id"] == h), h.split("_")[-1])
                     for h in sorted(fem)))
    if new_rows:
        i = {c: k for k, c in enumerate(hdr)}
        print("\n样例 3 行（截关键列）：")
        for r in new_rows[:3]:
            print("   %-34s %-8s EN=%-18s | 1554: %s/%s/%s/%s"
                  % (r[i["ID"]], r[i["CNName"]], r[i["EnglishName"]],
                     r[i["Appear_1554"]], r[i["Identity_1554"]], r[i["Kingdom_1554"]], r[i["City_1554"]]))
    if problems:
        print("\n❌ 硬问题 %d 条：" % len(problems))
        for x in problems[:20]:
            print("   %s" % x)
        return 1
    if not args.apply:
        print("\n（未加 --apply，只报告不写盘）")
        return 0

    stamp = time.strftime("%Y%m%d_%H%M%S")
    shutil.copy2(HERO, HERO + ".bak_promote_" + stamp)
    with io.open(HERO, "w", encoding="utf-8-sig", newline="") as fh:
        w = csv.writer(fh, lineterminator="\r\n", quoting=csv.QUOTE_MINIMAL)
        w.writerow(hdr)
        for r in body:
            w.writerow(r)
        for r in new_rows:
            w.writerow(r)
    print("\n✅ 已写入 TaikouHero.csv（%d → %d 行）" % (len(body), len(body) + len(new_rows)))
    with io.open(HERO, encoding="utf-8-sig", newline="") as fh:
        back = [r for r in csv.reader(fh) if r and any(x.strip() for x in r)]
    print("  往返校验：%d 行（应为 %d）；列数 %d（应为 %d）"
          % (len(back) - 2, len(body) + len(new_rows), len(back[1]), len(hdr)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
