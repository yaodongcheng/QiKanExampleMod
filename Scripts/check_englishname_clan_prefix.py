#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""EnglishName 家族名前缀 ↔ ClanID 一致性检查（2026-09-11 用户裁定）
============================================================================
**规则**：`EnglishName` 的**家族名前缀**（第一个空格分隔的 token）必须与 `ClanID` 对得上。

用户原话：「EnglishName 此列不管，之后会重新走正式本地化。只不过要注意 EnglishName 的
前半部分要和 clan 对应上。」——所以本检查**只看前缀 ↔ 家族是否对应**，不校验读音、
不校验名字部分内容（那部分会由正式本地化重做）。

**罗马音写法约定**（2026-09-11 用户裁定，别当错误）：
  长音符在 `EnglishName` 里**一律写作双写**——`ō→oo` / `ū→uu` / `â→aa`。
  ⚠️ 这是**设计预期**：ASCII 名里无法表示长音符，用双写代替，**否则同音姓氏/名字会相撞**
  （例：佐藤 Satō 与 佐東 Sato 必须能区分）。所以 `clan_katō_1` ↔ `Katoo Kiyomasa` **是合法的**。
  归一化 = 把 clan id 的**长音符展开成双写** → 两边都变成 `katoo` → 精确比对。
  🔴 **禁止**「连续相同字母压成一个」那种归一化：它会把刻意区分的 `oo`/`o` 合并掉。

**同时查**：ClanID 里出现**连续两个长音符**（`yagyūū` / `yagyūūū`）= 明显 typo
  （约定是「一个长音符 = 一个长音」，展开后应是 `yagyuu`；多打的那个一定是手滑）。

跳过：模板行（`template_*`/`pronoun_*`，这两列在模板行里本就是错位用法）、
通用家族（`clan_japanese_1` / 空 / `neutral_culture`）、任一列空的行。

豁免（人填，每条必须写理由）：
  lord_tk5_958 弥助 —— 織田信長的黑人随从，本无姓氏；挂 clan_oda_1 是「归属」而非「苗字」。

Usage:
  python Scripts/check_englishname_clan_prefix.py            # 独立跑
  python Scripts/check_englishname_clan_prefix.py --module X # 兼容 run_all_checks 的接口（忽略）
Exit: 0 全绿 / 1 有违规。
"""
import argparse
import csv
import io
import os
import re
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSV_PATH = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "TaikouHero.csv")
CLAN_CSV = os.path.join(REPO, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "Clan.csv")

GENERIC_CLANS = {"japanese", "neutral", ""}
EXEMPT = {
    "lord_tk5_958": "弥助——織田信長的黑人随从，本无姓氏；挂 clan_oda_1 是「归属」而非「苗字」",
}


MACRON = {"ō": "oo", "ū": "uu", "â": "aa", "ê": "ee", "î": "ii", "ô": "oo", "û": "uu",
          "Ō": "oo", "Ū": "uu", "Â": "aa", "Ê": "ee", "Î": "ii", "Ô": "oo", "Û": "uu"}


def norm_clan(s):
    """clan id 侧：长音符展开成双写（ō→oo），再只留字母数字小写。"""
    s = "".join(MACRON.get(ch, ch) for ch in (s or ""))
    return re.sub(r"[^a-z0-9]", "", s.lower())


def norm_en(s):
    """EnglishName 侧：原样小写去符号（双写已经在数据里），**不做任何合并**。"""
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


def clan_base(cid):
    return re.sub(r"_\d+$", "", re.sub(r"^clan_", "", (cid or "").strip()))


def main():
    ap = argparse.ArgumentParser(description="EnglishName prefix vs ClanID")
    ap.add_argument("--module", default=None, help="兼容 run_all_checks 接口（本检查不读模块）")
    ap.parse_args()

    if not os.path.isfile(CSV_PATH):
        print(f"[FATAL] 找不到 {CSV_PATH}")
        return 2
    with io.open(CSV_PATH, encoding="utf-8-sig") as fh:
        rows = list(csv.DictReader(fh))

    bad, typos, exempted, checked = [], [], [], 0
    for r in rows:
        tid = r["ID"]
        if tid.startswith(("template_", "pronoun_")):
            continue
        en = (r.get("EnglishName") or "").strip()
        cid = (r.get("ClanID") or "").strip()
        if not en or not cid:
            continue
        base = clan_base(cid)
        if base.lower() in GENERIC_CLANS:
            continue
        checked += 1
        if re.search(r"[ōūâêîôû]{2}", base):
            typos.append((tid, r["CNName"], cid, en))
        if norm_en(en.split()[0]) == norm_clan(base):
            continue
        if tid in EXEMPT:
            exempted.append((tid, r["CNName"], cid, en, EXEMPT[tid]))
            continue
        bad.append((tid, r["CNName"], cid, en))

    print(f"EnglishName 前缀 ↔ ClanID 检查：受检 {checked} 行")
    if exempted:
        print(f"\n[=] 豁免 {len(exempted)} 条（人填理由）")
        for t, n, c, e, why in exempted:
            print(f"    {t} {n}  {c} / {e}\n        ↳ {why}")
    if typos:
        print(f"\n[!] ClanID 长音符重复（明显 typo）{len(typos)} 条")
        for t, n, c, e in typos:
            print(f"    {t} {n}  {c} / {e}")
    if bad:
        print(f"\n[X] 前缀与家族对不上 {len(bad)} 条")
        for t, n, c, e in bad:
            print(f"    {t} {n}  clan={c}  EN={e!r}")

    # ── 第二关：家族 id ↔ Clan.csv 自己的罗马音 ──
    # 抓「两边都写错」的漏网（前缀检查只看两列是否自洽，两边一起错就看不出来）。
    if os.path.isfile(CLAN_CSV):
        with io.open(CLAN_CSV, encoding="utf-8-sig") as fh:
            clans = list(csv.DictReader(fh))
        mism = []
        for c in clans:
            cid = (c.get("ID") or "").strip()
            if not cid.startswith("clan_"):        # 势力/匪帮 id 不属 clan 命名空间
                continue
            m = re.match(r"^\{=[^}]*\}(.*)$", (c.get("LocozationName") or "").strip())
            if not m:
                continue
            romaji = re.sub(r"[- ]?shi$", "", m.group(1))
            rid = re.sub(r"_\d+$", "", cid[len("clan_"):])
            if norm_clan(rid) != norm_clan(romaji):
                mism.append((cid, m.group(1), c.get("ScriptName")))
        print(f"\nClan.csv 家族 id ↔ 自身罗马音：{len(clans)} 行")
        if mism:
            print(f"[X] id 与罗马音不符 {len(mism)} 条")
            for cid, rom, sc in mism:
                print(f"    {cid:<24} 罗马音={rom:<22} 名={sc}")
        else:
            print("[✓] 全部一致")
    else:
        mism = []
        print(f"\n（找不到 {CLAN_CSV}，跳过第二关）")

    if bad or typos or mism:
        print(f"\n结果：红（前缀不符 {len(bad)} + id typo {len(typos)} + id↔罗马音 {len(mism)}）")
        return 1
    print("\n结果：绿")
    return 0


if __name__ == "__main__":
    sys.exit(main())
