#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""家族 id ↔ 家头罗马音 一致性检查（2026-09-11 家族重建后重写）
============================================================================
**背景**：家族定义在 2026-09-11 推倒重来（用户裁定：家族 = 家臣团，按侍奉关系分，
生成器 `Scripts/gen_taikou_clan_csv.py`）。旧检查「每个英雄的 EnglishName 前缀 = 他的 ClanID」
**随之作废**——新规则下家族成员与家头未必同姓（柴田胜家在织田家是常态），
成员的 EnglishName 前缀与家族无关。

**新规则（本检查查的就是它）**：
  1. **家族 id 的罗马音块 = 家头（`Owner_<年>`）EnglishName 的首块**——
     id 由家头派生，两边必须一致（历史教训：前缀写错会造出假家族，如 `clan_yagyūū_1`）。
  2. 家头 EnglishName 为空 → 走生成器的苗字罗马音兜底表（旧家族表存档），
     本检查对它**只报不计**（列出来供人核）。

  ~~旧规则②「`LocozationName` 的罗马音 = 同一个值」~~ **已于 2026-09-12 删除**：该列随家族表列裁剪
  一起退役（它是 `{=TAIKOU_<id>}<罗马音>` 的机械拼接、282/283 的键在 Taikou 语言包根本不存在，
  罗马音本就从 id 派生）→ 那条检查成了同义反复。罗马音与家头的对应由规则①把守。

**罗马音写法约定**（2026-09-11 用户裁定，别当错误）：
  长音符在 `EnglishName` 里**一律写作双写**——`ō→oo` / `ū→uu` / `â→aa`。
  ⚠️ 这是**设计预期**：ASCII 名里无法表示长音符，用双写代替，**否则同音姓氏/名字会相撞**
  （例：佐藤 Satō 与 佐東 Sato 必须能区分）。所以 `clan_katoo_1` ↔ `Katoo Kiyomasa` **是合法的**。
  归一化 = 把 clan id 的**长音符展开成双写** → 两边都变成 `katoo` → 精确比对。
  🔴 **禁止**「连续相同字母压成一个」那种归一化：它会把刻意区分的 `oo`/`o` 合并掉。
  🔴 旧表的 `-shi`（=「氏」）后缀**已随重建退役**——别再对它做 `[- ]?shi$` 剥离
     （会把 Konishi/Miyoshi/Takahashi 这类苗字吃掉尾巴）。

Usage:
  python Scripts/check_englishname_clan_prefix.py            # 独立跑
  python Scripts/check_englishname_clan_prefix.py --module X # 兼容 run_all_checks 的接口（忽略）
Exit: 0 全绿 / 1 有违规。
"""
import argparse
import collections
import csv
import io
import os
import re
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSV_DIR = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv")
CLAN_CSV = os.path.join(CSV_DIR, "Clan.csv")
HERO_CSV = os.path.join(CSV_DIR, "TaikouHero.csv")
ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]

MACRON = {"ō": "oo", "ū": "uu", "â": "aa", "ê": "ee", "î": "ii", "ô": "oo", "û": "uu",
          "Ō": "oo", "Ū": "uu", "Â": "aa", "Ê": "ee", "Î": "ii", "Ô": "oo", "Û": "uu"}


def norm_clan(s):
    """长音符展开成双写（ō→oo），再只留字母数字小写。"""
    s = "".join(MACRON.get(ch, ch) for ch in (s or ""))
    return re.sub(r"[^a-z0-9]", "", s.lower())


def clan_base(cid):
    return re.sub(r"_\d+$", "", re.sub(r"^clan_", "", (cid or "").strip()))


def main():
    ap = argparse.ArgumentParser(description="家族 id ↔ 家头罗马音")
    ap.add_argument("--module", default=None, help="兼容 run_all_checks 接口（本检查不读模块）")
    args = ap.parse_args()

    for p in (CLAN_CSV, HERO_CSV):
        if not os.path.isfile(p):
            print("[FATAL] 找不到 %s" % p)
            return 2
    with io.open(CLAN_CSV, encoding="utf-8-sig", newline="") as fh:
        clans = list(csv.DictReader(fh))
    with io.open(HERO_CSV, encoding="utf-8-sig", newline="") as fh:
        heroes = list(csv.DictReader(fh))
    byid = {r["ID"]: r for r in heroes}

    bad_id, nogap, checked = [], [], 0
    for c in clans:
        cid = (c.get("ID") or "").strip()
        if not cid.startswith("clan_"):
            continue
        rid = clan_base(cid)

        # ① id ↔ 家头 EnglishName 首块（六年代任一位家头对上即可）
        heads = [c.get("Owner_" + e, "") for e in ERAS]
        heads = [h for h in heads if h and h != "-"]
        if not heads:
            bad_id.append((cid, c.get("Name"), "（没有家头）"))
            continue
        ok_head, gaps = False, []
        for h in heads:
            en = ((byid.get(h) or {}).get("EnglishName") or "").strip()
            if not en:
                gaps.append((h, (byid.get(h) or {}).get("CNName", "?")))
                continue
            if norm_clan(en.split()[0]) == norm_clan(rid):
                ok_head = True
                break
        checked += 1
        if not ok_head:
            if gaps and len(gaps) == len(heads):
                nogap.append((cid, c.get("Name"), "、".join(n for _i, n in gaps)))
            else:
                bad_id.append((cid, c.get("Name"),
                               " / ".join("%s=%s" % (h, ((byid.get(h) or {}).get("EnglishName") or "（空）"))
                                          for h in heads)))

        # ② 已退役（2026-09-12）：原查 `LocozationName` 的罗马音 ↔ id。该列已删——
        #    它是 `{=TAIKOU_<id>}<罗马音>` 的机械拼接（282/283 的键在 Taikou 语言包根本不存在），
        #    罗马音本就从 id 派生 → 这条检查成了同义反复。罗马音与家头的对应由 ① 把守。

    print("家族 id ↔ 家头罗马音：受检 %d 个家族" % checked)
    if nogap:
        print("\n[?] 家头全无 EnglishName（走苗字罗马音兜底，列出来供核）%d 条" % len(nogap))
        for cid, sc, who in nogap:
            print("    %-24s 名=%s  家头=%s" % (cid, sc, who))
    if bad_id:
        print("\n[X] id 与家头英文名对不上 %d 条" % len(bad_id))
        for cid, sc, why in bad_id:
            print("    %-24s 名=%-6s  %s" % (cid, sc, why))

    if bad_id:
        print("\n结果：红（id↔家头 %d）" % len(bad_id))
        return 1
    print("\n结果：绿")
    return 0


if __name__ == "__main__":
    sys.exit(main())
