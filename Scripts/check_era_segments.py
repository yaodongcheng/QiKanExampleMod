#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Era-segment registration checker (时代段注册 + 据点 id 跨时代稳定性)
========================================================================
背景（时代剧本切换，2026-09-10 spike）：
  「一模块多 GameType + 每时代一套数据段」——引擎按 SubModule.xml 的
  `<IncludedGameTypes>` 过滤段。两个**必须守住的规则**：

  ① **同名 XmlName 的多份 path，GameType 不得重叠**
     引擎对同名 XmlName 的多段是**合并加载**（官方 SandBox 自己就注册了 7 份
     NPCCharacters，全部命中 Campaign → 都加载）。若两套「同一批 id」的段
     同时命中同一 GameType → 同一个 Faction/Hero 被定义两遍 = 静默数据污染。
     ⇒ 差异段必须互斥：A 套 = TaikouCampaign/A，B 套 = B。

  ② **据点 id 跨时代稳定**（铁律 20：引用一律 StringId）
     据点 id 只换归属、id 永不变 → 距离缓存（按据点 id 存据点对）天然可共用；
     若某时代凭空多/少一个据点 id，缓存立刻过期（雷 53 复发）。

本脚本体检这两条 + 列出时代段全景（人工核对用）。
适用：任何「一模块多 GameType（同内容包多时代）」的内容包。

Usage:
  python Scripts/check_era_segments.py [--module PATH]
Exit: 0 clean / 1 problem found / 2 fatal.
"""
import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# 一个 GameType 形如 <模块名>CampaignYYYY（TaikouCampaign1560）——本检查只关心这类「时代 GameType」
ERA_RE = re.compile(r"^(?P<mod>.+Campaign)(?P<year>\d{4})$")


def era_game_types(root, mod_name):
    """本模块 SubModule.xml 里出现的所有「时代 GameType」（<模块名>Campaign + 4 位年份）。"""
    out = []
    pat = re.compile(re.escape(mod_name) + r"Campaign\d{4}$")
    for g in root.iter("GameType"):
        v = g.get("value")
        if v and pat.fullmatch(v) and v not in out:
            out.append(v)
    return sorted(out)


def section_files(mod_root, mod_id, path):
    data = mod_root / mod_id / "ModuleData"
    for cand in (data / (path + ".xml"), data / path):
        if cand.is_file():
            return [cand]
        if cand.is_dir():
            return sorted(cand.rglob("*.xml"))
    return []


def settlement_ids(files):
    ids = set()
    for f in files:
        try:
            root = ET.parse(str(f)).getroot()
        except Exception:
            continue
        for el in root.iter():
            if el.tag in ("Settlement", "Settlements") and el.get("id"):
                ids.add(el.get("id"))
    return ids


# 「定义」用的元素标签白名单——只有这些标签上的 id 才是**定义对象**；
# 其它带 id 的元素是**引用**（`<skill id="Bow">` / `<equipment id="Item.xxx">` / `<perk id="…">`），
# 采集它们 = 把「引用」误当「定义」→ 假阳性（本项目踩过）。
DEFINING_TAGS = {
    "Settlement", "Faction", "Kingdom", "Hero", "NPCCharacter", "Culture", "Item",
    "PartyTemplate", "WorkshopType", "CraftingPiece", "SkillSet", "EquipmentRoster",
    "EquipmentSet", "Concept", "BodyProperty", "LocationComplexTemplate", "MusicTrack",
    "MusicInstrument", "string",
}


def object_ids(files):
    """段内**对象定义 id 集合**（元素标签 ∈ DEFINING_TAGS 且带 id）。"""
    ids = set()
    for f in files:
        try:
            root = ET.parse(str(f)).getroot()
        except Exception:
            continue
        for el in root.iter():
            eid = el.get("id")
            if not eid:
                continue
            if el.tag == "string":          # GameText：纯键名（同键 = 真重复）
                ids.add(eid)
            elif el.tag in DEFINING_TAGS or el is root:
                ids.add(f"{el.tag}.{eid}")  # 其它：标签限定，避免跨类同名误判
    return ids


def main():
    ap = argparse.ArgumentParser(description="Era-segment registration checker")
    ap.add_argument("--module", default=None, help="内容包目录（缺省=注册表 MB2_PATH 下的 Taikou）")
    ap.add_argument("--official-root", default=None, help="游戏根（缺省=注册表 MB2_PATH）")
    args = ap.parse_args()

    if args.module:
        mod_path = Path(args.module)
    else:
        mb2 = None
        if sys.platform == "win32":
            import winreg
            for hive, sub in ((winreg.HKEY_CURRENT_USER, "Environment"),
                              (winreg.HKEY_LOCAL_MACHINE,
                               r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")):
                try:
                    with winreg.OpenKey(hive, sub) as k:
                        mb2, _ = winreg.QueryValueEx(k, "MB2_PATH")
                        if mb2:
                            break
                except OSError:
                    continue
        mod_path = Path(mb2) / "Modules" / "Taikou" if mb2 else Path("Taikou")

    if not mod_path.is_dir():
        print(f"[FATAL] module not found: {mod_path}")
        return 2
    mod_root, mod_id = mod_path.parent, mod_path.name

    sm = mod_path / "SubModule.xml"
    if not sm.is_file():
        print(f"[FATAL] SubModule.xml not found: {sm}")
        return 2
    root = ET.parse(str(sm)).getroot()

    eras = era_game_types(root, mod_id)
    if not eras:
        print(f"[SKIP] 未发现时代 GameType（<{mod_id}Campaign + 年份>）——本包非多时代结构")
        return 0
    print(f"Module   : {mod_path}")
    print(f"时代      : {' / '.join(eras)}\n")

    # ── 段全景：XmlName.id → [(path, {gametypes})] ──
    sections = {}
    for node in root.iter("XmlNode"):
        name = node.find("XmlName")
        if name is None or not name.get("path"):
            continue
        gt = node.find("IncludedGameTypes")
        gts = frozenset(g.get("value") for g in gt.iter("GameType")) if gt is not None else None
        sections.setdefault(name.get("id"), []).append((name.get("path"), gts))

    errors, warns = [], []

    print("== ① 同名 XmlName 的多份 path：GameType 重叠 **且** 对象 id 重叠 = 同一对象被定义两遍 ==")
    print("   （注：GameType 重叠但 id 不重叠 = 合法——官方 SandBox 就注册 7 份 NPCCharacters）")
    print("   （分级：冲突涉及**时代差异段**= ERROR——正是本检查要防的；纯共用段= WARN，历史遗留）")
    _id_cache = {}

    def ids_of(path):
        if path not in _id_cache:
            _id_cache[path] = object_ids(section_files(mod_root, mod_id, path))
        return _id_cache[path]

    # 时代差异段 = 各时代的 path 集合不同者（如 Settlements: settlements / settlements_1582）
    era_diff_sections = set()
    for sid, entries in sections.items():
        per_era = {e: tuple(p for p, g in entries if g is not None and e in g) for e in eras}
        if len(set(per_era.values())) > 1:
            era_diff_sections.add(sid)

    overlap_found = False
    for sid, entries in sorted(sections.items()):
        if len(entries) < 2:
            continue
        for i in range(len(entries)):
            for j in range(i + 1, len(entries)):
                p1, g1 = entries[i]
                p2, g2 = entries[j]
                if g1 is None or g2 is None:      # 无白名单 = 对所有 GameType 生效
                    shared = set(g1) if g2 is None else (set(g2) if g1 is None else set())
                    label = "（其中一份无 IncludedGameTypes = 对所有 GameType 生效）"
                else:
                    shared = set(g1) & set(g2)
                    label = ""
                if not shared:
                    continue
                same_ids = ids_of(p1) & ids_of(p2)
                if not same_ids:
                    continue
                overlap_found = True
                msg = (f"{sid}: {p1} 与 {p2} 同时命中 {sorted(shared)} "
                       f"且同定义 {sorted(same_ids)[:6]} {label}")
                if sid in era_diff_sections:
                    errors.append(msg)
                    print(f"  [ERROR] {msg}")
                else:
                    warns.append(msg)
                    print(f"  [WARN] {msg}")
    if not overlap_found:
        print("  （无冲突 ✓）")

    print("\n== ② 据点 id 跨时代稳定（id 只换归属、不得增减；否则距离缓存过期 = 雷 53） ==")
    by_era = {era: set() for era in eras}
    for sid, entries in sections.items():
        if sid != "Settlements":
            continue
        for path, gts in entries:
            files = section_files(mod_root, mod_id, path)
            ids = settlement_ids(files)
            if gts is None:
                hit = eras
            else:
                hit = [e for e in eras if e in gts]
            for e in hit:
                by_era[e] |= ids
            print(f"  {path:34} → {sorted(ids)}  命中 {sorted(hit) or '（无时代 GameType）'}")
    if len(by_era) > 1:
        base_era = eras[0]
        base = by_era[base_era]
        for era in eras[1:]:
            only_base = base - by_era[era]
            only_era = by_era[era] - base
            if only_base or only_era:
                msg = (f"{base_era} 独有 {sorted(only_base)} / {era} 独有 {sorted(only_era)}"
                       f"（建议加「启用」标记处理，见 plans/时代剧本切换-验证.md）")
                warns.append(f"据点 id 跨时代不一致：{msg}")
                print(f"  [WARN] {msg}")
            else:
                print(f"  ({base_era} 与 {era} 据点 id 集合一致 ✓ — 距离缓存可共用)")

    print("\n== ③ 时代段全景（每个时代的 path 列表；有差异的 = 时代差异段） ==")
    for sid, entries in sorted(sections.items()):
        per_era = {e: tuple(p for p, g in entries if g is not None and e in g) for e in eras}
        diff = len(set(per_era.values())) > 1
        tag = "【时代差异段】" if diff else "【共用段】  "
        desc = " | ".join(f"{e.split('Campaign')[-1]}:{','.join(per_era[e]) or '-'}" for e in eras)
        print(f"  {tag} {sid:26} {desc}")

    print(f"\nSummary: eras={len(eras)} errors={len(errors)} warnings={len(warns)}")
    for w in warns:
        print(f"  [WARN] {w}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
