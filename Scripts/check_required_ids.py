#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Engine-hardcoded-id checker (自定义世界「引擎点名要的 id 必须存在」离线体检)
========================================================================
为什么需要它：一批 id 被**引擎代码硬编码**查找（`MBObjectManager.GetObject<T>("xxx")` /
`Settlement.Find("xxx")`），自定义世界不自备就是运行期 null → NRE 或静默失效。
这些条目散落在「自定义世界内容包从零起步必备清单」§1.3/§1.4/阶段 0 里**全靠人记**——
本脚本把它们变成可执行断言（清单纪律：每条检查都必须有脚本兜底，不能只有文字）。

判定规则：
  「存在」= 该 id 定义在**目标 GameType 下真正会被加载的段**里（DependedModules 闭包 +
  SubModule.xml 的 IncludedGameTypes 白名单判定）。**只写在未注册/被过滤的文件里 = 不算存在**
  ——那正是雷 3/4/5 的形态（数据在，但运行时没装载 → GetObject null → 崩）。
  为便于定位，缺失时分两种报法：①完全找不到 ②找得到但在未加载的段里。

Usage:
  python Scripts/check_required_ids.py [--module PATH] [--official-root PATH] [--game-type NAME]
  --official-root 缺省 = 读注册表 MB2_PATH（铁律 19：环境变量以注册表为准）
  --game-type 缺省 = 由 --module 的目录名推断（Taikou → TaikouCampaign）
Exit: 0 全部存在 / 1 有缺失 / 2 fatal。
"""
import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# GBK 控制台下 ✓ 等符号会崩——统一 UTF-8 输出（py3.7+）
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

if sys.platform == "win32":
    import winreg

# 元素标签 → 规范类型名（同一对象在不同文件里标签写法不同：MBPartyTemplate / PartyTemplate）
TAG_TO_TYPE = {
    "NPCCharacter": "CharacterObject",
    "MBCharacterObject": "CharacterObject",
    "Item": "Item",
    "MBItem": "Item",
    "MBPartyTemplate": "PartyTemplate",
    "PartyTemplate": "PartyTemplate",
    "MBEquipmentRoster": "EquipmentRoster",
    "EquipmentRoster": "EquipmentRoster",
    "Settlement": "Settlement",
    "MBSettlement": "Settlement",
    "Culture": "Culture",
    "MBCulture": "Culture",
    "Faction": "Clan",
    "MBFaction": "Clan",
    "Kingdom": "Kingdom",
    "MBKingdom": "Kingdom",
    "Hero": "Hero",
    "MBHero": "Hero",
    "MBWorkshopType": "WorkshopType",
    "WorkshopType": "WorkshopType",
}

# 🔴 引擎硬编码点名的 id 一览（每条：id / 规范类型 / 谁点名要它 / 归属雷号）
#    新内容包照这张表逐个自备即可；增减条目 = 改这里（生成物禁手改，本表是唯一真源）。
REQUIRED = [
    # ── 阶段 0 / 1.1：主队与基础引用 ──
    ("main_hero", "CharacterObject", "引擎 InitializeGamePlayReferences 链（PlayerTroop）", "雷 7"),
    ("main_hero_party_template", "PartyTemplate", "Campaign.InitializeMainParty 硬查（Native 无 / SandBox 带白名单不载入）", "雷 6"),
    ("default_battle_equipment_roster_neutral", "EquipmentRoster", "Campaign.InitializeDefaultEquipments（新战役不加载该段则 GetObject null → NRE）", "雷 5"),
    ("peasant_farmer", "CharacterObject", "文化 basic_troop 基础兵", "雷 11"),
    # ── 1.4：场景生成必撞的路人 / 兵种 ──
    ("guard", "CharacterObject", "城镇守卫生成", "1.4"),
    ("villager", "CharacterObject", "村庄路人生成", "1.4"),
    ("townsman", "CharacterObject", "城镇路人生成", "1.4"),
    ("townswoman", "CharacterObject", "城镇路人生成", "1.4"),
    ("merchant", "CharacterObject", "CreateHeroAtOccupation（工坊名流，N&W 池须产得出）", "雷 15"),
    ("artisan", "CharacterObject", "同上", "雷 15"),
    ("caravan_master", "CharacterObject", "商队组建", "雷 16"),
    ("armed_trader", "CharacterObject", "商队组建", "雷 16"),
    ("caravan_guard", "CharacterObject", "InitializeCaravanOnCreation 的 First 查询（另有「等级 ≥26」断言，见 check_data_fields）", "雷 16"),
    # ── 1.3 / 1.4：黑巷与地痞 ──
    ("gang_leader", "CharacterObject", "黑巷巷主（IsGangLeader 判定）", "雷 19"),
    ("gangster_1", "CharacterObject", "DefaultAlleyModel 巷战消费（缺 = 进城 TownCenter 崩）", "雷 40"),
    ("gangster_2", "CharacterObject", "同上", "雷 40"),
    ("gangster_3", "CharacterObject", "同上", "雷 40"),
    # ── 1.3：服务性据点 ──
    ("retirement_retreat", "Settlement", "RetirementCampaignBehavior 硬查（缺 = 每小时 tick NRE）", "雷 31"),
    # ── 1.4：官方场景消费的物品（马 / 动物）——场景 prefab 点名，与 check_scene_consumables 互补 ──
    ("aserai_horse", "Item", "官方 town 场景 sp_horse_* prefab tags[1]", "雷 41"),
    ("battania_horse", "Item", "同上", "雷 41"),
    ("empire_horse", "Item", "同上", "雷 41"),
    ("khuzait_horse", "Item", "同上", "雷 41"),
    ("sturgia_horse", "Item", "同上", "雷 41"),
    ("sheep", "Item", "MissionHelper.SpawnSheeps", "雷 41"),
    ("cow", "Item", "MissionHelper.SpawnCows", "雷 41"),
    ("hog", "Item", "MissionHelper.SpawnHogs", "雷 41"),
    ("goose", "Item", "MissionHelper.SpawnGeese", "雷 41"),
    ("chicken", "Item", "MissionHelper.SpawnChickens", "雷 41"),
]

# 阶段 0：捏脸模板段（FaceGen 阶段 ctor 盘查 facgen_template_test_char_0..9）
REQUIRED += [(f"facgen_template_test_char_{i}", "CharacterObject",
              "FaceGen 阶段构造尾部模板盘查（缺 = 建号捏脸崩）", "雷 27") for i in range(10)]


def registry_mb2_path():
    """读注册表 MB2_PATH（User 级优先，再 Machine）——铁律 19。"""
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


def module_closure(mod_root, start):
    """DependedModules 递归闭包（含自身）——只有被依赖的模块才会一起加载。"""
    seen, order = set(), []

    def visit(mod_id):
        if mod_id in seen:
            return
        seen.add(mod_id)
        sm = mod_root / mod_id / "SubModule.xml"
        if sm.is_file():
            try:
                root = ET.parse(str(sm)).getroot()
            except Exception:
                order.append(mod_id)
                return
            dep = root.find("DependedModules")
            if dep is not None:
                for d in dep.iter("DependedModule"):
                    if d.get("Id"):
                        visit(d.get("Id"))
        order.append(mod_id)

    visit(start)
    return order


def loaded_sections(mod_root, mod_id, game_type):
    """该模块在 game_type 下会被加载的 (XmlName.id, path)。"""
    out = []
    sm = mod_root / mod_id / "SubModule.xml"
    if not sm.is_file():
        return out
    try:
        root = ET.parse(str(sm)).getroot()
    except Exception:
        return out
    for node in root.iter("XmlNode"):
        name = node.find("XmlName")
        if name is None or not name.get("path"):
            continue
        gt = node.find("IncludedGameTypes")
        if gt is None:
            out.append((name.get("id"), name.get("path")))
        elif any(g.get("value") == game_type for g in gt.iter("GameType")):
            out.append((name.get("id"), name.get("path")))
    return out


def section_files(mod_root, mod_id, path):
    """段 path → 实际文件（单文件 `path.xml` 或目录型段 `path/`）。"""
    data = mod_root / mod_id / "ModuleData"
    for cand in (data / (path + ".xml"), data / path):
        if cand.is_file():
            return [cand]
        if cand.is_dir():
            return sorted(cand.rglob("*.xml"))
    return []


def collect_ids(files):
    """扫文件集 → {(规范类型, id): 来源}。"""
    found = {}
    for f in files:
        try:
            root = ET.parse(str(f)).getroot()
        except Exception:
            continue
        for el in root.iter():
            eid = el.get("id")
            if not eid:
                continue
            t = TAG_TO_TYPE.get(el.tag, el.tag)
            found.setdefault((t, eid), f"{f.parent.name}/{f.name}")
    return found



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
    ap = argparse.ArgumentParser(description="Engine-hardcoded-id checker")
    ap.add_argument("--module",
                    default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    ap.add_argument("--official-root", default=None, help="游戏根（缺省=注册表 MB2_PATH）")
    ap.add_argument("--game-type", default=None, help="缺省=SubModule.xml 里本模块全部时代 GameType，各跑一遍")
    args = ap.parse_args()

    mod_path = Path(args.module)
    if not mod_path.is_dir():
        print(f"[FATAL] module not found: {mod_path}")
        return 2
    mod_root = mod_path.parent

    root = Path(args.official_root) if args.official_root else None
    if root is None:
        mb2 = registry_mb2_path()
        root = Path(mb2) if mb2 else None
    if root is not None and (root / "Modules").is_dir() and root != mod_root:
        mod_root = root / "Modules"
    if not mod_root.is_dir():
        print(f"[FATAL] Modules root not found: {mod_root}")
        return 2

    exit_codes = []
    for game_type in inferred_game_types(mod_path, args.game_type):
        print(f"Module   : {mod_path}")
        print(f"GameType : {game_type}")

        # ① 目标 GameType 下真正会被加载的文件（判定基准）
        closure = module_closure(mod_root, mod_path.name)
        loaded_files, section_count = [], 0
        for mod_id in closure:
            for _sec_id, path in loaded_sections(mod_root, mod_id, game_type):
                section_count += 1
                loaded_files.extend(section_files(mod_root, mod_id, path))
        print(f"加载闭包 : {' → '.join(closure)}")
        print(f"扫描     : {len(closure)} 模块 / {section_count} 段 / {len(loaded_files)} 文件（仅**已加载**段）\n")

        loaded_ids = collect_ids(loaded_files)
        # ② 兜底对照：包内全部 XML（用来区分「完全缺失」与「在未加载的段里」）
        all_pack_files = sorted((mod_path / "ModuleData").rglob("*.xml")) if (mod_path / "ModuleData").is_dir() else []
        all_ids = collect_ids(all_pack_files)

        missing, unloaded, mismatch = [], [], []
        for eid, etype, who, lei in REQUIRED:
            src = loaded_ids.get((etype, eid))
            if src:
                continue
            src_any = all_ids.get((etype, eid))
            if src_any:
                unloaded.append((eid, etype, src_any, who, lei))
            else:
                # 类型对但标签不匹配？再宽松找一次（同 id 不同类型 = 定义错了类型）
                other = [t for (t, i) in all_ids if i == eid]
                if other:
                    mismatch.append((eid, etype, other[0], all_ids[(other[0], eid)], who, lei))
                else:
                    missing.append((eid, etype, who, lei))

        print(f"== 引擎硬编码点名 id 体检（共 {len(REQUIRED)} 条） ==")
        if not (missing or unloaded or mismatch):
            print("  （全部存在 ✓）")
        for eid, etype, who, lei in missing:
            print(f"  [缺失] {etype} {eid} —— {who}（{lei}）")
        for eid, etype, src, who, lei in unloaded:
            print(f"  [未加载] {etype} {eid} 定义在 {src}，但该段在 {game_type} 下**不会被加载**"
                  f"（= 运行时照样 GetObject null）—— {who}（{lei}）")
        for eid, etype, got, src, who, lei in mismatch:
            print(f"  [类型不符] {eid} 期望 {etype}，实得 {got}（{src}）—— {who}（{lei}）")

        bad = len(missing) + len(unloaded) + len(mismatch)
        print(f"\nSummary: required={len(REQUIRED)} missing={len(missing)} "
              f"unloaded={len(unloaded)} type_mismatch={len(mismatch)}")
    exit_codes.append(1) if bad else 0



    return max(exit_codes) if exit_codes else 0

if __name__ == "__main__":
    sys.exit(main())
