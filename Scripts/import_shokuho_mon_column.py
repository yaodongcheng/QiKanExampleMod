#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Clan.csv / TaikouForce.csv 的「家纹」列初始填充器（一次性导入，2026-09-16）
========================================================================
干什么：给两张表各补一列 `Mon`（中文标签「家纹」），值 = **家纹键** `图集_格位`
（如 `kinki_1_5` = kinki_1 图集第 5 格），供 `gen_taikou_era_world.py`
翻成 spclans/spkingdoms 的 `banner_key`。

为什么家族和势力都要配：
  城池名牌挂的是 `Settlement.Banner` = **城主家族**的旗；
  但若该家族是所属王国的**统治家族**（`Clan.Banner` 的特例），挂的是**王国旗**。
  所以大名居城看的是「势力」这一列 —— 两列缺一，另一半城池就是纯色旗。

值的来源（按优先级取第一个命中）：
  家族（Clan.csv）
    1. **同名家族**：与织丰完全同名 StringId（如 `clan_akamatsu_1`）→ 抄织丰
    2. **同家分家**：剥掉尾部 `_数字` 后同名（`clan_akamatsu_2` ← `clan_akamatsu_1`）
       —— 分家史实上沿用本家纹
  势力（TaikouForce.csv）
    1. **当主所领家族**：`Owner_<年代>` 与 Clan.csv 同一英雄 → 该家族的家纹（主导路径）
    2. **当主名字桥**：当主 → TaikouHero.CNName → shokuho_heroes_full.ChineseName → 织丰家族
  取不到 → 留空（生成器输出**纯色无图标**的旗，避免混进欧洲纹章）

🔴 这是一次性迁移脚本，不是日常生成器：
  跑完后 `Mon` 列就是**人工维护数据**（工作层），重跑会**覆盖人工改动**
  —— 故已有该列时默认拒绝执行，需显式 `--force`。

Usage:
  python Scripts/import_shokuho_mon_column.py --dry-run   # 只统计，不写盘
  python Scripts/import_shokuho_mon_column.py --force     # 真写
"""
import argparse
import collections
import csv as _csv
import glob
import io
import os
import re
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO, "Scripts"))
import csv_dual                       # noqa: E402
import taikou_mon_atlas as MON        # noqa: E402

CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
SHOKUHO = os.path.join(os.path.dirname(REPO), "Shokuho")

CN_LABEL, EN_KEY = "家纹", "Mon"
SHO_MAT_PREFIX = "sho_cl_mon_"
ERAS = ("1554", "1560", "1568", "1575", "1582", "1598")


def _read(p):
    return io.open(p, encoding="utf-8", errors="replace").read()


def shokuho_icon_to_mon():
    """织丰 banner_icons.xml → {图标id: 家纹键}。"""
    p = os.path.join(SHOKUHO, "ModuleData", "banner_icons.xml")
    if not os.path.isfile(p):
        print("[FATAL] 找不到织丰 banner_icons.xml: %s" % p, file=sys.stderr)
        sys.exit(2)
    out = {}
    for m in re.finditer(r'<Icon\s+id="(\d+)"\s+material_name="([^"]+)"\s+texture_index="(\d+)"', _read(p)):
        iid, mat, cell = int(m.group(1)), m.group(2), int(m.group(3))
        if mat.startswith(SHO_MAT_PREFIX):
            atlas = mat[len(SHO_MAT_PREFIX):]
            if atlas in MON.ATLASES:
                out[iid] = "%s_%d" % (atlas, cell)
    return out


def shokuho_clan_icon():
    """织丰家族 → {家族id: 图标id}（只取有图标的 banner_key）。"""
    out = {}
    for f in glob.glob(os.path.join(SHOKUHO, "ModuleData", "spclans", "*.xml")):
        for m in re.finditer(r'<Faction id="([^"]+)"[^>]*?banner_key="([^"]+)"', _read(f), re.S):
            cid, key = m.group(1), m.group(2)
            parts = key.split(".")
            if cid not in out and len(parts) >= 20:
                out[cid] = int(parts[10])
    return out


def _base(cid):
    return re.sub(r"_[0-9]+$", "", cid)


def hero_cn_to_sho_clan():
    """中文名 → 织丰家族id。"""
    p = os.path.join(CSV_DIR, "shokuho_heroes_full.csv")
    out = {}
    if not os.path.isfile(p):
        return out
    with io.open(p, encoding="utf-8-sig", errors="replace") as f:
        for r in _csv.DictReader(f):
            cn = (r.get("ChineseName") or "").replace(" ", "")
            fac = (r.get("Faction") or "").replace("Faction.", "")
            if cn and fac and cn not in out:
                out[cn] = fac
    return out


def taikou_hero_cn():
    """TaikouHero ID → 中文名。"""
    path = os.path.join(CSV_DIR, "TaikouHero.csv")
    _, hdr, rows = csv_dual.read_table(path)
    ix = {k: i for i, k in enumerate(hdr)}
    return {r[ix["ID"]]: (r[ix.get("CNName", 0)] or "").replace(" ", "")
            for r in rows if len(r) > ix["ID"]}


def mon_of_sho_clan(cid, clan_icon, icon_mon, by_base):
    if cid in clan_icon:
        return icon_mon.get(clan_icon[cid], ""), "同名家族"
    for cand in by_base.get(_base(cid), []):
        if cand != cid:
            mk = icon_mon.get(clan_icon[cand], "")
            if mk:
                return mk, "同家分家"
    return "", ""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--force", action="store_true", help="已存在 Mon 列时也覆盖（会丢人工改动）")
    args = ap.parse_args()

    icon_mon = shokuho_icon_to_mon()
    clan_icon = shokuho_clan_icon()
    by_base = collections.defaultdict(list)
    for cid in clan_icon:
        by_base[_base(cid)].append(cid)
    for v in by_base.values():
        v.sort()
    print("织丰侧：%d 个家族有家纹，%d 个图标可映射到家纹键" % (len(clan_icon), len(icon_mon)))

    # ── 家族表 ──────────────────────────────────────────────
    clan_path = os.path.join(CSV_DIR, "Clan.csv")
    cn_cols, en_cols, rows = csv_dual.read_table(clan_path)
    orig_en = list(en_cols)
    ix = {k: i for i, k in enumerate(orig_en)}
    clan_mon, owner_mon, stats = {}, {}, collections.Counter()
    out_rows = []
    for r in rows:
        cid = r[ix["ID"]] if len(r) > ix["ID"] else ""
        mk, src = mon_of_sho_clan(cid, clan_icon, icon_mon, by_base)
        stats[src or "未命中"] += 1
        clan_mon[cid] = mk
        for era in ERAS:
            own = (r[ix.get("Owner_" + era, 0)] if len(r) > ix.get("Owner_" + era, 0) else "").strip()
            if own and own != "-" and mk:
                owner_mon.setdefault(own, mk)
        out_rows.append(list(r) + [mk])
    tot = len(rows); hit = tot - stats["未命中"]
    print("\n== Clan.csv ==  %d 行" % tot)
    for k, v in stats.most_common():
        print("   %-8s %4d" % (k, v))
    print("   覆盖率 %.0f%%" % (100.0 * hit / max(tot, 1)))

    # ── 势力表（王国旗：大名的居城挂这面）────────────────────
    force_path = os.path.join(CSV_DIR, "TaikouForce.csv")
    fcn, fen, frows = csv_dual.read_table(force_path)
    forig = list(fen)
    fix = {k: i for i, k in enumerate(forig)}
    hcn = taikou_hero_cn()
    name_bridge = hero_cn_to_sho_clan()
    fstats = collections.Counter()
    fout = []
    for r in frows:
        fid = r[fix["ID"]] if len(r) > fix["ID"] else ""
        mk, src = clan_mon.get(fid, ""), "同名家族"
        if not mk:
            for era in ERAS:
                own = (r[fix.get("Owner_" + era, 0)] if len(r) > fix.get("Owner_" + era, 0) else "").strip()
                if own and own in owner_mon:
                    mk, src = owner_mon[own], "当主所领家族"
                    break
        if not mk:
            for era in ERAS:
                own = (r[fix.get("Owner_" + era, 0)] if len(r) > fix.get("Owner_" + era, 0) else "").strip()
                fac = name_bridge.get(hcn.get(own, ""))
                cand = mon_of_sho_clan(fac, clan_icon, icon_mon, by_base) if fac else ("", "")
                if cand[0]:
                    mk, src = cand[0], "当主名字桥"
                    break
        fstats[src if mk else "未命中"] += 1
        fout.append(list(r) + [mk])
    ftot = len(frows); fhit = ftot - fstats["未命中"]
    print("\n== TaikouForce.csv ==  %d 行" % ftot)
    for k, v in fstats.most_common():
        print("   %-12s %4d" % (k, v))
    print("   覆盖率 %.0f%%" % (100.0 * fhit / max(ftot, 1)))

    if args.dry_run:
        print("\n样例（Clan）:", [(r[0], r[-1]) for r in out_rows if r[-1]][:6])
        print("样例（Force）:", [(r[1], r[-1]) for r in fout if r[-1]][:6])
        print("(dry-run，未写盘)")
        return 0

    for path, cc, ec, rr in ((clan_path, cn_cols, en_cols, out_rows),
                             (force_path, fcn, fen, fout)):
        if EN_KEY in ec and not args.force:
            print("[skip] %s 已有 %s 列（需 --force）" % (os.path.basename(path), EN_KEY))
            continue
        if EN_KEY not in ec:
            cc, ec = list(cc) + [CN_LABEL], list(ec) + [EN_KEY]
        csv_dual.write_table(path, cc, ec, rr)
        print("[write] %s" % os.path.basename(path))
    return 0


if __name__ == "__main__":
    sys.exit(main())
