#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Module-registration checker (自定义世界「段注册 / 源文件登记」离线体检)
========================================================================
覆盖「必备清单」里这两组**只有文字、没有脚本**的条目：
  阶段 0「SubModule.xml 完整段清单」——缺段 = 每少一段崩一次（雷 3/4/5）
  1.6「SandBox 9 个 GameText 文本段全量拷贝」——被 GameType 白名单过滤 = 整文件不加载（雷 35）
  阶段 2「新增 .cs 必须登记 csproj」——漏登记 = 静默不编译，build 报 0 错是假象（雷 40）

三条规则：
  1. **必需段在位**：id 清单（Items/SPCultures/…/GameText）+ 9 个官方 GameText path，
     且必须在本包 GameType 下生效（无 IncludedGameTypes 或白名单含它）
  2. **段 path 可解析**：每个注册的 path 必须能落到实际文件/目录（写错路径 = 引擎加载失败或静默空）
  3. **孤儿数据文件**：ModuleData 下的 XML 既不被任何段覆盖、又不在引擎惯例名单里、**且定义了对象**
     = 数据写了但运行时不存在（0 定义的空模板只提示）
  4. **csproj 漏登记**：`ExampleModVS/**/*.cs` 与 `<Compile Include>` 清单比对，未登记 = ERROR

Usage:
  python Scripts/check_module_registration.py [--module PATH] [--repo PATH] [--game-type NAME]
Exit: 0 无问题 / 1 有 ERROR / 2 fatal。
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

# ① 必需段 id（缺 = 每次崩一个，雷 3/4/5）——新内容包照抄 Taikou 的 SubModule.xml 即可全中
REQUIRED_SECTION_IDS = [
    "Items", "SPCultures", "NPCCharacters", "partyTemplates", "Kingdoms", "Factions",
    "WorkshopTypes", "Settlements", "BodyProperties", "SkillSets", "EquipmentRosters",
    "Concepts", "CraftingPieces", "Heroes", "LocationComplexTemplates",
    "MusicInstruments", "MusicTracks", "GameText",
]
# ② 必需 GameText path（SandBox 9 段全被 GameType 白名单过滤 → 自定义 GameType 下必须自备，雷 35）
REQUIRED_GAMETEXT_PATHS = [
    "module_strings", "world_lore_strings", "companion_strings", "wanderer_strings",
    "comment_strings", "comment_on_action_strings", "trait_strings", "voice_strings", "action_strings",
]
# ③ 引擎按**惯例文件名**加载、不经 XmlNode 段注册的文件（官方模块同款；实测 Native/SandBox 里存在）
CONVENTIONAL_FILES = {
    "action_sets", "action_types", "collision_infos", "combat_parameters", "face_animations",
    "item_holsters", "module_sounds", "native_parameters", "physics_materials", "skins", "items",
}
# Languages/ 由语言系统按清单加载，不走段注册
# AssetRegistry/ = **运行期自读目录**（不经 MBObjectManager）：立绘表 ProfileStages.csv、
#   ProfileEmotion.csv，以及选人详情页的画像表 HeroProfiles.xml 都放这里——
#   这些文件由 C# 直接读盘（PortraitRegistry / HeroProfileRegistry），
#   **故意不注册**（注册了反而要求配套一个 MBObjectManager 类，徒增负担）。
#   见 plans/选人流程复刻太阁5-设计.md §五·补。
CONVENTIONAL_DIRS = {"Languages", "AssetRegistry"}


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
    ap = argparse.ArgumentParser(description="Module registration checker")
    ap.add_argument("--module",
                    default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    ap.add_argument("--official-root", default=None)
    ap.add_argument("--game-type", default=None)
    ap.add_argument("--repo", default=None, help="仓库根（缺省=脚本上一级）——用于 csproj 检查")
    args = ap.parse_args()

    mod_path = Path(args.module)
    if not mod_path.is_dir():
        print(f"[FATAL] module not found: {mod_path}")
        return 2
    exit_codes = []
    for game_type in inferred_game_types(mod_path, args.game_type):
        print(f"Module   : {mod_path}")
        print(f"GameType : {game_type}\n")

        sm = mod_path / "SubModule.xml"
        if not sm.is_file():
            print(f"[FATAL] SubModule.xml not found: {sm}")
            return 2
        try:
            root = ET.parse(str(sm)).getroot()
        except Exception as e:
            print(f"[FATAL] SubModule.xml 解析失败: {e}")
            return 2

        sections = []   # (id, path, [gametypes] or None, 是否对本 GameType 生效)
        for node in root.iter("XmlNode"):
            name = node.find("XmlName")
            if name is None or not name.get("path"):
                continue
            gt = node.find("IncludedGameTypes")
            gts = [g.get("value") for g in gt.iter("GameType")] if gt is not None else None
            active = gts is None or game_type in gts
            sections.append((name.get("id"), name.get("path"), gts, active))

        errors, warns = [], []
        data = mod_path / "ModuleData"

        # ── 1. 必需段在位且对本 GameType 生效 ──
        print("== 必需段注册（缺 = 每次崩一个，雷 3/4/5；9 个文本段 = 雷 35） ==")
        have = {sid for sid, _p, _g, act in sections if act}
        for sid in REQUIRED_SECTION_IDS:
            if sid not in have:
                errors.append(f"缺段 {sid}")
                print(f"  [ERROR] 缺段 {sid}（或该段未包含 {game_type}）")
        for p in REQUIRED_GAMETEXT_PATHS:
            if not any(sid == "GameText" and path == p and act for sid, path, _g, act in sections):
                errors.append(f"缺 GameText 段 {p}")
                print(f"  [ERROR] 缺 GameText 段 {p}（SandBox 该段被 GameType 过滤 → 必须自备，雷 35）")
        if not errors:
            print(f"  （{len(REQUIRED_SECTION_IDS)} 个必需段 id 全在位；9 个文本段全在位 ✓）")

        # ── 2. 段 path 可解析 ──
        print("\n== 段 path 可解析（写错路径 = 引擎加载失败/静默空） ==")
        bad_path = 0
        for sid, path, _g, act in sections:
            if not act:
                continue
            if not ((data / (path + ".xml")).is_file() or (data / path).is_dir()):
                bad_path += 1
                errors.append(f"段 {sid} 的 path 找不到: {path}")
                print(f"  [ERROR] 段 {sid} path={path!r} 在 ModuleData 下既不是 .xml 也不是目录")
        if not bad_path:
            print(f"  （{len(sections)} 段全部可解析 ✓）")

        # ── 3. 孤儿数据文件 ──
        print("\n== 孤儿数据文件（写了但没有任何段加载它） ==")
        covered = set()
        for _sid, path, _g, _act in sections:
            covered.add(path)
            d = data / path
            if d.is_dir():
                for f in d.rglob("*.xml"):
                    covered.add(f.relative_to(data).as_posix()[:-4])
        orphans = 0
        for f in sorted(data.rglob("*.xml")):
            rel = f.relative_to(data).as_posix()
            stem = rel[:-4]
            if stem in covered or stem.split("/")[0] in covered:
                continue
            if stem in CONVENTIONAL_FILES or stem.split("/")[0] in CONVENTIONAL_DIRS:
                continue
            try:
                n = sum(1 for el in ET.parse(str(f)).getroot().iter() if el.get("id"))
            except Exception:
                n = -1
            if n > 0:
                orphans += 1
                errors.append(f"孤儿数据文件 {rel}（{n} 个定义）")
                print(f"  [ERROR] {rel} —— 有 {n} 个定义，但没有任何段加载它（运行时不存在）")
            else:
                warns.append(f"空模板 {rel}")
                print(f"  [INFO]  {rel} —— 0 个定义（模板/占位，忽略）")
        if not orphans:
            print("  （无 ✓）")

        # ── 4. csproj 漏登记 .cs ──
        repo = Path(args.repo) if args.repo else Path(__file__).resolve().parent.parent
        proj = None
        for cand in repo.glob("ExampleModVS/**/*.csproj"):
            proj = cand
            break
        print(f"\n== csproj 编译清单（漏登记 = 静默不编译，build 0 错是假象，雷 40） ==")
        if proj is None:
            warns.append("未找到 .csproj（跳过）")
            print("  [WARN] 未找到 .csproj，跳过")
        else:
            txt = proj.read_text(encoding="utf-8-sig", errors="replace")
            # 只取**未被注释掉**的 Compile 行
            active_includes = set()
            for line in txt.splitlines():
                if line.lstrip().startswith("<!--"):
                    continue
                m = re.search(r'<Compile\s+Include="([^"]+)"', line)
                if m:
                    active_includes.add(m.group(1).replace("/", "\\"))
            proj_dir = proj.parent
            on_disk = []
            for f in proj_dir.rglob("*.cs"):
                rel = f.relative_to(proj_dir).as_posix()
                if rel.startswith("obj/") or rel.startswith("bin/"):
                    continue
                on_disk.append(rel)
            missing = [r for r in on_disk if r.replace("/", "\\") not in active_includes]
            print(f"  项目: {proj}")
            print(f"  磁盘 .cs: {len(on_disk)} | csproj 已登记: {len(active_includes)}")
            if missing:
                for m in missing:
                    # 有意未登记 vs 真漏：csproj 里（含注释）提到过这个名字 = 作者有交代 → INFO
                    # （注释里通常写类名不带 .cs —— 两种写法都要认）
                    if Path(m).name in txt or Path(m).stem in txt:
                        warns.append(f"有意未登记 {m}")
                        print(f"  [INFO]  {m} —— csproj 注释里有说明（有意未登记/已废弃）")
                    else:
                        errors.append(f"未登记 {m}")
                        print(f"  [ERROR] 未登记: {m} —— 改 csproj 加 <Compile Include=\"{m.replace('/', chr(92))}\" />")
            else:
                print("  （无漏登记 ✓）")

        print(f"\nSummary: sections={len(sections)} errors={len(errors)} warnings={len(warns)}")
        exit_codes.append(1) if errors else 0    # <- 必须在循环内：循环外只剩最后一趟的 errors（假绿，2026-09-12 修）



    return max(exit_codes) if exit_codes else 0

if __name__ == "__main__":
    sys.exit(main())
