#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Culture text-variant checker (自定义世界「按文化取 variation 的文本」离线体检 — 雷 23/35 族防线)
========================================================================
症状（实机 2026-09-10 12:47:20）：
  城镇中心菜单正文里**直接印出** `ERROR: Text with id str_faction_ruler doesn't exist! Variation: ikoku`
  ——玩家可见的报错文本（KCD2 水准红线）。

根因：
  引擎取文本走「`id + '.' + 文化`」的 variation 机制（如 str_culture_rich_name.empire）。
  官方一部分文本族**只给八个原版文化的变体、连一个 `.default` 都没有**（Native/module_strings.xml 的
  str_faction_ruler* 全家 = 12 + 6 + 6 条）→ 自定义文化不自备 `<base>.<自家文化>` 就必然查空。
  ⚠️ 「9 个 GameText 段全量拷贝」这条纪律挡不住它——拷贝只保证 id 在，按文化取的那一族照样缺（同雷 23）。

本脚本把这道检查提前到离线：
  1. 段清单 = 内容包 DependedModules 闭包内、目标 GameType 下会被加载的段（复用 check_culture_references 的枚举）
  2. 自家文化 = 本包 SPCultures 段里定义的 `<Culture id="...">`
  3. 收集所有 GameText 段里的 `<string id="...">`，按「最后一个点」切成 (base, suffix)
  4. suffix 是文化（或其 `_f` 女性变体）= 文化变体族；该族**没有 `.default`** 时：
     每个自家文化必须自备 `<base>.<文化>`（族里有 `_f` 约定的，还要 `<base>.<文化>_f`）
  5. 缺 = 运行时会向玩家印 ERROR 文本 → exit 1

Usage:
  python Scripts/check_culture_text_variants.py [--module PATH] [--game-type NAME] [--pack-culture ID ...]
  --pack-culture 缺省 = 自动从本包 SPCultures 段读；手填可做反面验证（填个不存在的文化应当报缺）
Exit: 0 no missing / 1 missing found / 2 fatal.
"""
import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# GBK 控制台下 ✓ 等符号会崩——统一 UTF-8 输出
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# 复用文化引用 checker 的「加载面枚举」（同一目录，python Scripts/x.py 时 sys.path[0] = Scripts/）
sys.path.insert(0, str(Path(__file__).resolve().parent))
from check_culture_references import (  # noqa: E402
    module_closure, loaded_sections, section_files, registry_mb2_path)

# 官方八文化里实际用作 variation 后缀的六个（player_faction / neutral_culture 不参与文本 variation）
VANILLA_CULTURES = {"empire", "vlandia", "sturgia", "aserai", "khuzait", "battania"}


def collect_ids(files):
    """收集 GameText 段里所有 <string id=...>（id → 出处文件名）。"""
    out = {}
    for f in files:
        try:
            root = ET.fromstring(f.read_text(encoding="utf-8", errors="replace"))
        except Exception:
            continue
        for node in root.iter():
            sid = node.get("id")
            if sid:
                out.setdefault(sid, f.name)
    return out



def inferred_game_types(mod_path, explicit=None):
    """本模块要体检的 GameType 列表。
    显式 --game-type → 只跑那一个（单跑语义不变）。
    缺省 = SubModule.xml 里所有「<模块名> + 数字年份」的 GameType（时代切换后一模块多 GameType：
    Taikou → TaikouCampaign1560 / TaikouCampaign1582 …），**每个都要跑**——
    只跑一个 = 另一个时代的段查不到 = 静默假绿（时代切换 spike 的核心风险点）。
    """
    if explicit:
        return [explicit]
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


def main():
    ap = argparse.ArgumentParser(description="Content-pack culture text-variant checker")
    ap.add_argument("--module",
                    default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    ap.add_argument("--official-root", default=None, help="缺省 = 注册表 MB2_PATH（铁律 19）")
    ap.add_argument("--game-type", default=None, help="缺省=SubModule.xml 里本模块全部时代 GameType，各跑一遍")
    ap.add_argument("--pack-culture", action="append", default=None,
                    help="可重复；缺省从本包 SPCultures 段自动读")
    ap.add_argument("--ignore-culture", action="append", default=None,
                    help="显式豁免（打印说明，非静默吞）；缺省 = neutral_culture，"
                         "理由：官方自家 gangster_1/2/3 也用 neutral_culture 且官方同样没给它变体 = 与官方同级。"
                         "传空串可关闭豁免（--ignore-culture \"\"）")
    args = ap.parse_args()

    mod_path = Path(args.module)
    if not mod_path.is_dir():
        print(f"[FATAL] module not found: {mod_path}")
        return 2
    mod_root = mod_path.parent
    if args.official_root:
        root = Path(args.official_root)
        if (root / "Modules").is_dir():
            mod_root = root / "Modules"
    else:
        mb2 = registry_mb2_path()
        if mb2 and (Path(mb2) / "Modules").is_dir():
            mod_root = Path(mb2) / "Modules"

    exit_codes = []
    for game_type in inferred_game_types(mod_path, args.game_type):
        closure = module_closure(mod_root, mod_path.name)
        print(f"Module   : {mod_path}\nModules根: {mod_root}\nGameType : {game_type}")
        print(f"加载模块闭包: {' → '.join(closure)}\n")

        # ---- 枚举加载面 ----
        gametext_files, culture_ids = [], {}
        section_count = 0
        for mod_id in closure:
            for sec_id, path in loaded_sections(mod_root, mod_id, game_type):
                section_count += 1
                files = section_files(mod_root, mod_id, path)
                if not files:
                    continue
                if sec_id == "GameText":
                    gametext_files += files
                elif sec_id == "SPCultures":
                    for f in files:
                        try:
                            for c in ET.fromstring(f.read_text(encoding="utf-8", errors="replace")).iter("Culture"):
                                if c.get("id"):
                                    culture_ids.setdefault(c.get("id"), f"{mod_id}/{f.name}")
                        except Exception:
                            pass

        if args.pack_culture:
            pack_cultures = list(args.pack_culture)
            print(f"自家文化（命令行指定）: {pack_cultures}")
        else:
            # 只认本包定义的（官方模块定义的八个原版文化不算「自家」）
            pack_cultures = sorted(c for c, src in culture_ids.items() if src.startswith(mod_path.name + "/"))
            print(f"自家文化（本包 SPCultures）: {pack_cultures}")

        ignore = ["neutral_culture"] if args.ignore_culture is None else [c for c in args.ignore_culture if c]
        skipped = [c for c in pack_cultures if c in ignore]
        if skipped:
            print(f"已豁免（--ignore-culture，显式打印非静默）：{skipped}"
                  f" —— 官方自家 gangster_1/2/3 也用 neutral_culture 且官方同样没给它变体 = 与官方同级")
        pack_cultures = [c for c in pack_cultures if c not in ignore]

        strings = collect_ids(gametext_files)
        print(f"扫描：{section_count} 段（GameText {len(gametext_files)} 文件）/ {len(strings)} 条文本 id\n")

        # ---- 找「文化变体族」----
        # 注意：族的建立条件是「出现文化后缀」，但**该 base 下的所有后缀都要计进 variants**
        # （尤其 `.default`——漏收它会把这族误判成「无兜底」而报一堆假缺，2026-09-10 首跑踩过）。
        known_suffixes = VANILLA_CULTURES | set(pack_cultures)
        families = {}
        for sid, src in strings.items():
            if "." not in sid:
                continue
            base, _, suffix = sid.rpartition(".")
            core = suffix[:-2] if suffix.endswith("_f") else suffix
            fam = families.get(base)
            if core in known_suffixes:
                fam = families.setdefault(base, {"variants": set(), "src": src})
            if fam is not None:
                fam["variants"].add(suffix)

        no_default, missing = [], {}
        for base, info in families.items():
            if "default" in info["variants"]:
                continue                      # 有 .default 兜底 → 引擎查不到自家文化会落回 default
            no_default.append(base)
            has_f = any(v.endswith("_f") for v in info["variants"])
            for c in pack_cultures:
                for want in ([c, c + "_f"] if has_f else [c]):
                    if want not in info["variants"]:
                        missing.setdefault(base, []).append(want)

        print(f"== 无 .default 兜底的文化变体族（共 {len(no_default)}）==")
        for base in sorted(no_default):
            ok = "OK" if base not in missing else "缺: " + ", ".join(sorted(missing[base]))
            print(f"  {base:<52} [{ok}]  ({families[base]['src']})")

        print(f"\n== 缺失（运行时会把 ERROR 文本印给玩家） ==")
        if not missing:
            print("  （无）")
        else:
            for base in sorted(missing):
                print(f"  [{base}] 需要补: {', '.join(sorted(missing[base]))}")

        print(f"\nSummary: families={len(families)} no_default={len(no_default)} missing={len(missing)}")
    exit_codes.append(1) if missing else 0



    return max(exit_codes) if exit_codes else 0

if __name__ == "__main__":
    sys.exit(main())
