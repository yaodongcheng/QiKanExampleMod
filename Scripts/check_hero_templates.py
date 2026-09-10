#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Hero-template pairing checker (英雄必须有同名 CharacterObject 模板)
========================================================================
🔴 规则（2026-09-10 实机教训，跨 5 个模块验证成立）：
  `<Heroes>` 段里的每条 `<Hero id="X"/>`，**只有在存在同名 `<NPCCharacter id="X">` 时**
  才会被引擎真正建出来；缺模板的那条**静默被吞**——不报错、不提示，只是英雄数少一个。
  后果（实机链条）：
    英雄被吞 → 家族没有可用的领主 → `HeroSpawnCampaignBehavior.GetBestAvailableCommander` NRE
    → 新战役在 OnNewGameCreated 阶段直接崩（堆栈只指引擎，看不出是数据问题）。

  为什么容易被漏：英雄条目本身**语法完全合法**，交叉引用检查也**查不出问题**
  （它引用的家族/文化都解析得了）——这是"生效条件"缺失，不是"引用悬空"。
  本检查器就是补这一条。

**规则证据**（拿现存数据反证，2026-09-10）：Shokuho 1382 / ShokuhoTaikouExpansionPack 753 /
SandBox 399 个英雄 **全部有同名模板**，零例外；唯一的 `main_hero` 缺模板属引擎建号自造，豁免。

口径：**逐 GameType** 各查一遍（多时代内容包：某时代加载的英雄必须由该时代加载的模板段覆盖）。

Usage:
  python Scripts/check_hero_templates.py [--module PATH] [--game-type NAME ...]
Exit: 0 clean / 1 problem found / 2 fatal.
"""
import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

if sys.platform == "win32":
    import winreg

# 引擎自造、不需要模板的英雄（建号流程创建；官方同款例外，实测 YiGuThreeKingdoms 亦如此）
EXEMPT_HERO_IDS = {"main_hero"}


def registry_mb2_path():
    for hive, sub in ((winreg.HKEY_CURRENT_USER, "Environment"),
                      (winreg.HKEY_LOCAL_MACHINE,
                       r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")):
        try:
            with winreg.OpenKey(hive, sub) as k:
                val, _ = winreg.QueryValueEx(k, "MB2_PATH")
                if val:
                    return val
        except OSError:
            continue
    return None


def inferred_game_types(mod_path, explicit=None):
    """本模块要体检的 GameType（同 check_era_segments 口径）。"""
    if explicit:
        return list(explicit)
    out = []
    sm = mod_path / "SubModule.xml"
    if sm.is_file():
        try:
            root = ET.parse(str(sm)).getroot()
        except Exception:
            root = None
        if root is not None:
            pat = re.compile(re.escape(mod_path.name) + r"Campaign\d{4}$")
            for g in root.iter("GameType"):
                v = g.get("value")
                if v and pat.fullmatch(v) and v not in out:
                    out.append(v)
    return out or [mod_path.name + "Campaign"]


def section_files(mod_root, mod_id, path):
    data = mod_root / mod_id / "ModuleData"
    for cand in (data / (path + ".xml"), data / path):
        if cand.is_file():
            return [cand]
        if cand.is_dir():
            return sorted(cand.rglob("*.xml"))
    return []


def collect_ids(files, tag):
    out = {}
    for f in files:
        try:
            root = ET.parse(str(f)).getroot()
        except Exception:
            continue
        for el in root.iter(tag):
            if el.get("id"):
                out.setdefault(el.get("id"), f.name)
    return out


def main():
    ap = argparse.ArgumentParser(description="Hero-template pairing checker")
    ap.add_argument("--module", default=None, help="内容包目录（缺省=注册表 MB2_PATH 下的 Taikou）")
    ap.add_argument("--game-type", action="append", default=None, help="只查指定 GameType（可重复）")
    args = ap.parse_args()

    if args.module:
        mod_path = Path(args.module)
    else:
        mb2 = registry_mb2_path()
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

    # 段 → (path, gametypes)
    sections = []
    for node in root.iter("XmlNode"):
        name = node.find("XmlName")
        if name is None or not name.get("path"):
            continue
        gt = node.find("IncludedGameTypes")
        gts = frozenset(g.get("value") for g in gt.iter("GameType")) if gt is not None else None
        sections.append((name.get("id"), name.get("path"), gts))

    game_types = inferred_game_types(mod_path, args.game_type)
    print(f"Module   : {mod_path}")
    print(f"GameType : {' / '.join(game_types)}\n")

    total_errors = 0
    for gt in game_types:
        hero_src, char_src = {}, {}
        for sid, path, gts in sections:
            if gts is not None and gt not in gts:
                continue
            files = section_files(mod_root, mod_id, path)
            if sid == "Heroes":
                for k, v in collect_ids(files, "Hero").items():
                    hero_src.setdefault(k, v)
            elif sid == "NPCCharacters":
                for k, v in collect_ids(files, "NPCCharacter").items():
                    char_src.setdefault(k, v)

        missing = sorted(k for k in hero_src
                         if k not in char_src and k not in EXEMPT_HERO_IDS)
        exempt = sorted(k for k in hero_src if k in EXEMPT_HERO_IDS)
        tag = "✅" if not missing else "❌"
        print(f"{tag} [{gt}] Hero {len(hero_src)} 个 / NPCCharacter 模板 {len(char_src)} 个"
              f" / 豁免 {len(exempt)} / **缺模板 {len(missing)}**")
        for k in missing:
            print(f"     [ERROR] Hero '{k}'（{hero_src[k]}）**没有同名 NPCCharacter 模板**"
                  f" → 该条会被引擎静默吞掉，英雄不会出现在世界里")
            print(f"            修法：在某个 NPCCharacters 段加 "
                  f"<NPCCharacter id=\"{k}\" ... is_hero=\"true\" occupation=\"Lord\">")
        total_errors += len(missing)

    print(f"\nSummary: errors={total_errors}")
    if total_errors:
        print("  （缺模板 = 该英雄不生效 → 家族无领主 → 新战役可能直接崩，必须修）")
    return 1 if total_errors else 0


if __name__ == "__main__":
    sys.exit(main())
