#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""世界规模账（每代的王国数 / 家族数 / 据点分布）—— 铺年代数据前的体检
============================================================================
**回答一个问题：这个世界铺下去会不会「碎得离谱」。**
  官方卡拉迪亚的量级是「**8 个王国 / ~150 家族 / ~190 据点**」≈ **每个王国 20+ 据点**；
  日本战国按势力表铺开是「~96 个王国 / 270+ 据点」≈ 每个王国 **2~3 个据点**——
  差一个数量级。这不是数据错误（战国本来就是几十个大名并立），但**要不要照单全收**得先看数。

**为什么先算再铺**（plan T2 原话）：纯分析、零风险，且**决定后面要不要返工**——
  如果结论是"太碎、要并家"，那改的是 `TaikouForce.Owner_<年>` 的口径（数据侧），
  铺完 XML 再回头改 = 全部重铺。

看三样（逐年代）：
  ① **王国数**（= 该年 `Owner_<年>` 非 `-` 的势力数）与**据点分布**（空国几个 / 1 个 / 2~3 个 / 4+ 个）
  ② **家族数**（`Clan.Kingdom_<年>` 非 `-`）与「一家独大」程度（最大王国的家族数占比）
  ③ **异常格**：有据点却无当主的据点、有家族却无王国的家族（各自列出）

Usage:
  python Scripts/report_world_scale.py                 # 全部六代
  python Scripts/report_world_scale.py --era 1560      # 单代
  python Scripts/report_world_scale.py --csv-dir DIR   # 换数据表（新内容包复用）
Exit: 0 正常（分析不是检查，永远 0）/ 2 fatal。
"""
import argparse
import collections
import os
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_CSV = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]


def load(csv_dir, name, head=2):
    sys.path.insert(0, os.path.join(REPO, "Scripts"))
    from csv_dual import read_table
    cn, en, rows = read_table(os.path.join(csv_dir, name), head=head)
    return [{k: (r[i] if i < len(r) else "").strip() for i, k in enumerate(en)}
            for r in rows if any((x or "").strip() for x in r)]


def main():
    ap = argparse.ArgumentParser(description="World scale report")
    ap.add_argument("--csv-dir", default=DEFAULT_CSV)
    ap.add_argument("--era", default=None, help="只报某一年代（缺省=六代全报）")
    args = ap.parse_args()

    if not os.path.isdir(args.csv_dir):
        print("[FATAL] csv dir not found: %s" % args.csv_dir, file=sys.stderr)
        return 2

    force = load(args.csv_dir, "TaikouForce.csv")
    clan = load(args.csv_dir, "Clan.csv")
    sett = load(args.csv_dir, "Settlements.csv")
    hero = load(args.csv_dir, "TaikouHero.csv")

    eras = [args.era] if args.era else ERAS
    print("世界规模账（数据表：%s）" % args.csv_dir)
    print("  表：势力 %d / 家族 %d / 据点 %d / 英雄 %d"
          % (len(force), len(clan), len(sett), len(hero)))

    for e in eras:
        oc, kc, cc = "Owner_" + e, "Kingdom_" + e, "Clan_" + e
        live_force = {r["ID"]: r for r in force if (r.get(oc) or "").strip() not in ("", "-")}
        # 🔴 据点 → 势力**不能拿当主对当主**（2026-09-12 修正）：
        #    `TaikouForce.Owner_<年>` = 该势力的**当主（大名）**；`Settlements.Owner_<年>` = 该城的**城主**
        #    （常是大名的家臣）。正确路径 = 据点 `Clan_<年>` → 那家当年的 `Kingdom_<年>` → 势力。
        clan_kingdom = {r["ID"]: (r.get(kc) or "").strip() for r in clan}
        clan_type = {r["ID"]: (r.get("ForceType") or "") for r in force}
        per_force = collections.Counter()
        orphan_sett, no_owner, no_clan = [], [], []
        for s in sett:
            if not (s.get(oc) or "").strip():
                no_owner.append(s["id"])
                continue
            cid = (s.get(cc) or "").strip()
            fid = clan_kingdom.get(cid, "")
            if not cid:
                no_clan.append(s["id"])
            elif fid in ("", "-"):
                orphan_sett.append(s["id"])          # 城主无主家 / 该家当年不属任何势力
            elif fid in live_force:
                per_force[fid] += 1
            else:
                orphan_sett.append(s["id"])          # 所属势力该年不存在（= 悬空，真问题）
        # 势力类型分档：武家才该有据点；商家/忍者/海贼按设计不占城
        by_type = collections.defaultdict(collections.Counter)
        for fid, f in live_force.items():
            n = per_force.get(fid, 0)
            key = "0（空国）" if n == 0 else "1" if n == 1 else "2~3" if n <= 3 else "4+"
            by_type[(f.get("ForceType") or "?").strip()][key] += 1
        buckets = collections.Counter()
        for fid in live_force:
            n = per_force.get(fid, 0)
            buckets["0（空国）" if n == 0 else "1" if n == 1 else "2~3" if n <= 3 else "4+"] += 1
        live_clan = [r for r in clan if (r.get(kc) or "").strip() not in ("", "-")]
        clan_per_force = collections.Counter(r[kc] for r in live_clan)
        biggest = clan_per_force.most_common(1)[0] if clan_per_force else ("-", 0)

        with_sett = len(sett) - len(no_owner) - len(no_clan) - len(orphan_sett)
        # 🔴 立国口径（2026-09-12 用户裁定）：**武家 + 忍者 + 海贼立国；商家不立国**
        #    （商家做独立家族 is_minor_faction="true"、无 super_faction——范本 = 现有最小集的「纳屋」）。
        #    所以「该国该年有没有据点」不是判据，**判据是「该年 Owner_<年> 有没有值」**：
        #    某年 `-` = 该年不建国，该年代的段里根本不出现它。
        KINGDOM_TYPES = ("Warrior", "Ninja", "Pirate")
        kingdom = [fid for fid, f in live_force.items() if (f.get("ForceType") or "") in KINGDOM_TYPES]
        no_kingdom = [fid for fid, f in live_force.items() if (f.get("ForceType") or "") not in KINGDOM_TYPES]
        king_sett = sum(per_force.get(fid, 0) for fid in kingdom)

        print("\n== %s ==" % e)
        print("  势力表该年有当主的 %d 家 → **立国 %d 个**（武家 %d + 忍者 %d + 海贼 %d）· "
              "不立国 %d 家（商家等，做独立家族）"
              % (len(live_force), len(kingdom),
                 sum(1 for f in kingdom if live_force[f].get("ForceType") == "Warrior"),
                 sum(1 for f in kingdom if live_force[f].get("ForceType") == "Ninja"),
                 sum(1 for f in kingdom if live_force[f].get("ForceType") == "Pirate"),
                 len(no_kingdom)))
        print("  据点 %d 个：%d 个归到王国 / %d 个该年无人驻守 / %d 个城主无家 / %d 个归属悬空"
              % (len(sett), king_sett, len(no_owner), len(no_clan), len(orphan_sett)))
        if kingdom:
            print("  每国据点分布（只算立国的）：%s"
                  % " · ".join("%s → %d 国" % (k, v) for k, v in sorted(collections.Counter(
                      "0（空国）" if not per_force.get(fid) else "1" if per_force.get(fid) == 1
                      else "2~3" if per_force.get(fid) <= 3 else "4+" for fid in kingdom).items())
                      if v))
            print("  平均 %.1f 据点/国（官方卡拉迪亚 ~20+）· 家族/国 平均 %.1f"
                  % (king_sett / len(kingdom), len(live_clan) / len(kingdom)))
        if kingdom and king_sett / len(kingdom) < 5:
            print("  ⚠️ 比官方碎一个数量级（官方 ~8 国 / ~190 据点 ≈ 20+ 据点/国）——"
                  "要并家的话改的是 `TaikouForce.Owner_<年>` 口径，**铺 XML 前决定**")
        warrior_empty = [fid for fid in kingdom
                         if not per_force.get(fid) and live_force[fid].get("ForceType") == "Warrior"]
        if warrior_empty:
            names = [live_force[f].get("ForceName", f) for f in warrior_empty]
            print("  ⚠️ **武家空国**（有当主、一个据点都没有）%d 个：%s"
                  % (len(names), ", ".join(names[:10])))
        tk_empty = [fid for fid in kingdom
                    if not per_force.get(fid) and live_force[fid].get("ForceType") != "Warrior"]
        if tk_empty:
            print("  非武家立国但暂无据点 %d 个（忍者里/海贼砦该年可能无主）：%s"
                  % (len(tk_empty), ", ".join(live_force[f].get("ForceName", f) for f in tk_empty[:8])))
        if orphan_sett:
            print("  ⚠️ 归属悬空（城主所属势力该年不存在）%d 个：%s"
                  % (len(orphan_sett), ", ".join(orphan_sett[:6])))
    return 0


if __name__ == "__main__":
    sys.exit(main())
