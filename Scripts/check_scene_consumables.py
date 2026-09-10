#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Scene-consumable checker (自定义世界「官方场景消费」离线体检)
========================================================================
雷 40/41 族根因：自定义世界用官方场景（如 empire_town_a），场景里的 sp_* 出生点
prefab 会「点名要某个物品」（如 sp_horse_war_horse → aserai_horse 马物品）——
这些物品属于官方模块、被 GameType 过滤不装载，引擎直到进场景那一刻才检查
（GetObject 返回 null → NRE）。本脚本把这道检查提前到离线。

规则：
  1. 场景清单 = 本包 taikou_location_complex_templates.xml 的 scene_name* 属性值
  2. 逐个场景文件（官方 Native/SandBox/SandBoxCore 的 SceneObj + 本包 SceneObj）
     扫 prefab="sp_*" 实例
  3. prefab 定义（官方 */Prefabs/*.xml 的 <game_entity name=...> → <tags>）取 tag 清单，
     除类型 tag（sp_horse / sp_sheep / npc_wait 之类）以外的值 = 被消费的对象 id
  4. 判定：在【官方物品库】存在、但【本包物品库】不存在 → 缺失（exit 1）
         官方物品库也没有 → 非物品 tag（npc_wait 等），跳过

Usage:
  python Scripts/check_scene_consumables.py [--module PATH] [--official-root PATH]
  --official-root 缺省 = 读注册表 MB2_PATH（铁律 19：环境变量以注册表为准）
Exit: 0 no missing / 1 missing found / 2 fatal.
"""
import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# GBK 控制台下 ✓ 等符号会崩——统一 UTF-8 输出（py3.7+）
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

if sys.platform == "win32":
    import winreg


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


def strip_prefix(obj_id, prefix):
    return obj_id[len(prefix) + 1:] if obj_id.startswith(prefix + ".") else obj_id


def collect_item_ids(xml_files):
    """扫 root = <Items> 的文件，收集直接子元素 id（归一化为裸 id，含 CraftedItem）。"""
    ids = set()
    for path in xml_files:
        if "Languages" in path.parts:
            continue  # 语言文件不参与物品扫描（且 _tr 文件编码非法会刷警告）
        try:
            root = ET.parse(str(path)).getroot()
        except Exception as e:
            print(f"[FILE-WARN] 跳过无法解析: {path.name}: {e}")
            continue
        if root.tag != "Items":
            continue
        for child in root:
            cid = child.get("id")
            if cid:
                ids.add(strip_prefix(cid, "Item"))
    return ids


def collect_scene_names(module_data):
    """本包 location complex templates 引用的全部场景名。"""
    names = set()
    for path in module_data.rglob("*.xml"):
        if "Languages" in path.parts:
            continue
        try:
            root = ET.parse(str(path)).getroot()
        except Exception:
            continue
        for node in root.iter():
            for key, value in node.attrib.items():
                if key.startswith("scene_name") and value.strip():
                    names.add(value.strip())
    return names


def find_scene_file(name, module_path, official_root):
    """场景文件搜索序：本包 SceneObj → 官方 SandBoxCore/SandBox/Native SceneObj。"""
    candidates = [module_path / "SceneObj" / name / "scene.xscene"]
    for mod in ("SandBoxCore", "SandBox", "Native"):
        candidates.append(official_root / "Modules" / mod / "SceneObj" / name / "scene.xscene")
    for c in candidates:
        if c.is_file():
            return c
    return None


def collect_scene_prefabs(scene_file):
    """场景 xscene 里的 prefab="sp_*" 实例名集合。"""
    import re
    txt = scene_file.read_text(encoding="utf-8-sig", errors="ignore")
    return set(re.findall(r'prefab="(sp_[A-Za-z0-9_]+)"', txt))


def collect_prefab_tags(official_root):
    """官方 Prefabs 目录：{prefab_name: set(tags)}。"""
    table = {}
    prefab_dirs = [official_root / "Modules" / m / "Prefabs" for m in ("Native", "SandBox", "SandBoxCore")]
    for d in prefab_dirs:
        if not d.is_dir():
            continue
        for path in d.rglob("*.xml"):
            try:
                root = ET.parse(str(path)).getroot()
            except Exception:
                continue
            for entity in root.iter("game_entity"):
                name = entity.get("name")
                if not name or not name.startswith("sp_"):
                    continue
                tags = [t.get("name") for t in entity.iter("tag") if t.get("name")]
                table.setdefault(name, set()).update(tags)
    return table


def main():
    ap = argparse.ArgumentParser(description="Scene-consumable checker (offline)")
    ap.add_argument("--module", default=None, help="Taikou module path")
    ap.add_argument("--official-root", default=None, help="official game root (default: registry MB2_PATH)")
    args = ap.parse_args()

    repo_root = Path(__file__).resolve().parent.parent
    module_path = Path(args.module) if args.module else repo_root.parent / "Taikou"
    if not module_path.is_dir():
        print(f"[FATAL] 模块目录不存在: {module_path}")
        sys.exit(2)
    official_root = Path(args.official_root) if args.official_root else None
    if official_root is None:
        reg = registry_mb2_path()
        if not reg:
            print("[FATAL] 未传 --official-root 且注册表读不到 MB2_PATH")
            sys.exit(2)
        official_root = Path(reg)
    if not official_root.is_dir():
        print(f"[FATAL] 官方根目录不存在: {official_root}")
        sys.exit(2)

    module_data = module_path / "ModuleData"
    print(f"Module  : {module_path}")
    print(f"Official: {official_root}")

    # 物品库
    own_items = collect_item_ids(module_data.rglob("*.xml"))
    official_items = set()
    for mod in ("Native", "SandBox", "SandBoxCore"):
        d = official_root / "Modules" / mod / "ModuleData"
        if d.is_dir():
            official_items |= collect_item_ids(d.rglob("*.xml"))
    print(f"\n本包物品: {len(own_items)} 件 | 官方物品: {len(official_items)} 件")

    # 场景 → prefab → 消费对象
    scenes = sorted(collect_scene_names(module_data))
    print(f"本包引用场景: {len(scenes)} 个")
    prefab_tags = collect_prefab_tags(official_root)
    print(f"官方 sp_* prefab 定义: {len(prefab_tags)} 个\n")

    missing = []
    satisfied = []
    unknown_tags = []
    for scene in scenes:
        scene_file = find_scene_file(scene, module_path, official_root)
        if scene_file is None:
            print(f"[SKIP] 场景文件未找到（可能名字特殊/未使用）: {scene}")
            continue
        prefabs = collect_scene_prefabs(scene_file)
        hits = 0
        for prefab in sorted(prefabs):
            tags = prefab_tags.get(prefab)
            if tags is None:
                unknown_tags.append(f"{scene}:{prefab}（无 prefab 定义，跳过）")
                continue
            for tag in sorted(tags):
                if tag.startswith("sp_") or tag in ("npc_wait", "aiwaypoint", "ammopickup", "Wait", "Pilot"):
                    continue  # 行为/类型 tag，非消费对象
                if tag in own_items:
                    satisfied.append(f"{scene} / {prefab} -> {tag} [OK]")
                elif tag in official_items:
                    missing.append(f"{scene} / prefab={prefab} 点名物品 [{tag}] —— 官方有、本包缺")
            hits += 1
        print(f"[SCAN] {scene}: prefab {len(prefabs)} 个（有定义 {hits}）")

    print("\n== 命中且满足（本包已有该物品） ==")
    for s in satisfied:
        print("  " + s)
    if unknown_tags:
        print("\n== 无 prefab 定义（人工复核，可能是编辑器残留） ==")
        for u in unknown_tags:
            print("  " + u)

    print("\n== 缺失（官方场景点名、本包没有 → 进场景会崩） ==")
    if missing:
        for m in missing:
            print("  [MISSING] " + m)
        print(f"\nSummary: missing={len(missing)} satisfied={len(satisfied)}")
        sys.exit(1)
    print("  （无）")
    print(f"\nSummary: missing=0 satisfied={len(satisfied)}")
    sys.exit(0)


if __name__ == "__main__":
    main()
