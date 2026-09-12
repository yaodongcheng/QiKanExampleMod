#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Village-type checker — 村型 id 合法性 + 村型产出物在世界物品集里（雷 108 防线）
========================================================================
为什么需要它：`DefaultVillageTypes.AddProductions()` 是**按字符串 id 查物品**塞进村型产出表的：

    villageType.AddProductions(productions.Select(p => (GetObject<ItemObject>(p.Item1), p.Item2)));

查不到 → 表里就是 **null** → 建世界初次产出时 `Village.GetWerehouseCapacity()` →
`DefaultVillageProductionCalculatorModel.CalculateDailyProductionAmount(village, null)` →
`item.IsMountable` 裸解引用 → **建世界当场 NRE**（2026-09-12 实机，雷 108）。

同理，村型 id 写错（不在引擎 22 个 id 里）**不报错**——`RegisterPresumedObject` 造「推测桩村型」，
产出表为 null → 同一个崩点。两条规则本脚本一起查：

  规则 1：settlements.xml 里出现的 `village_type` 必须 ∈ 引擎 `DefaultVillageTypes.RegisterAll()`
          的 22 个 id（1.2.12 全 DLL 反编译实测，见 ENERGY_VILLAGE_TYPES）。
  规则 2：本包**实际用到**的村型，其 XML 产出物必须存在于本包物品集
          （引擎在 C# 里现造的不算——见 ENGINE_MADE_ITEMS）。

规则 1 对全部 22 个 id 生效；规则 2 只覆盖 VILLAGE_PRODUCTIONS 表里收录的村型（未收录的村型
一旦被启用会打 WARN「产出物清单未收录」——**补表即可**，不是硬错误）。

Usage:
  python Scripts/check_village_types_and_items.py [--module PATH]
Exit: 0 无 ERROR / 1 有 ERROR / 2 fatal。
"""
import argparse
import io
import os
import re
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

if sys.platform == "win32":
    import winreg


def registry_mb2_path():
    for hive, sub in ((winreg.HKEY_CURRENT_USER, "Environment"),
                      (winreg.HKEY_LOCAL_MACHINE,
                       r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")):
        try:
            with winreg.OpenKey(hive, sub) as k:
                v, _ = winreg.QueryValueEx(k, "MB2_PATH")
                if v:
                    return v
        except OSError:
            continue
    return None


# 引擎 `DefaultVillageTypes.RegisterAll()` 的 22 个 id（TaleWorlds.CampaignSystem.dll 反编译实测）
ENGINE_VILLAGE_TYPES = {
    "wheat_farm", "europe_horse_ranch", "steppe_horse_ranch", "desert_horse_ranch",
    "battanian_horse_ranch", "sturgian_horse_ranch", "vlandian_horse_ranch", "lumberjack",
    "clay_mine", "salt_mine", "iron_mine", "fisherman", "cattle_farm", "sheep_farm",
    "swine_farm", "vineyard", "flax_plant", "date_farm", "olive_trees", "silk_plant",
    "silver_mine", "trapper",
}

# 引擎在 C# 里现造的物品（`DefaultItems.Create` → `RegisterPresumedObject(new ItemObject(id))` +
# `InitializeTradeGood(...)`）——**不在 XML 里、也不需要进包**，任何战役都有
ENGINE_MADE_ITEMS = {
    "grain", "meat", "hides", "tools", "iron", "hardwood", "charcoal",
    "ironIngot1", "ironIngot2", "ironIngot3", "ironIngot4", "ironIngot5", "ironIngot6", "trash",
}

# 村型 → 产出物（`DefaultVillageTypes.AddProductions()` 反编译实测；只收录已核实的）
VILLAGE_PRODUCTIONS = {
    "wheat_farm": ["cow", "sheep", "hog"],
    "vineyard": ["grape"],
    "silk_plant": ["cotton"],
    "iron_mine": ["iron"],
    "fisherman": ["fish"],
    "europe_horse_ranch": ["empire_horse", "t2_empire_horse", "t3_empire_horse", "sumpter_horse",
                           "mule", "saddle_horse", "old_horse", "hunter", "charger"],
}


def main():
    ap = argparse.ArgumentParser(description="Taikou village-type / production-item checker")
    ap.add_argument("--module", default=None, help="内容包路径（缺省 = 注册表 MB2_PATH 下的 Taikou）")
    args = ap.parse_args()
    module = Path(args.module) if args.module else Path(registry_mb2_path() or "") / "Modules" / "Taikou"
    md = module / "ModuleData"
    if not (md / "settlements.xml").is_file():
        print(f"[FATAL] 找不到 {md / 'settlements.xml'}")
        return 2

    errors, warns = [], []
    sett = io.open(md / "settlements.xml", encoding="utf-8-sig", errors="replace").read()
    used = sorted(set(re.findall(r'village_type="VillageType\.([\w]+)"', sett)))
    print(f"Module: {module}\n本包用到的村型（{len(used)}）: {', '.join(used)}\n")

    # 规则 1：村型 id 合法性
    print("== 规则 1：村型 id ∈ 引擎 22 个 ==")
    for vt in used:
        if vt in ENGINE_VILLAGE_TYPES:
            print(f"  [ OK ] {vt}")
        else:
            errors.append(f"村型 id 不存在：{vt}（引擎无此 id → 推测桩村型 → 产出表 null → 建世界 NRE）")
            print(f"  [ERROR] {vt} —— 引擎无此 id")

    # 规则 2：产出物必须在世界物品集里
    print("\n== 规则 2：村型产出物 ∈ 世界物品集 ==")
    ids = set()
    for f in list(md.glob("items*.xml")) + list((md / "taikou_items").glob("*.xml")):
        txt = io.open(f, encoding="utf-8-sig", errors="replace").read()
        for m in re.finditer(r"<Item\s+([^>]*?)>", txt, re.S):
            d = dict(re.findall(r'([\w]+)="([^"]*)"', m.group(1)))
            if "id" in d:
                ids.add(d["id"])
    print(f"  世界物品集：{len(ids)} 件")
    for vt in used:
        if vt not in VILLAGE_PRODUCTIONS:
            warns.append(f"村型 {vt} 的产出物清单未收录（补 VILLAGE_PRODUCTIONS 表）")
            print(f"  [WARN]  {vt} 产出物清单未收录 —— 补表即可")
            continue
        for item in VILLAGE_PRODUCTIONS[vt]:
            if item in ENGINE_MADE_ITEMS:
                print(f"  [ OK ] {vt} → {item}（引擎 C# 现造，无需进包）")
            elif item in ids:
                print(f"  [ OK ] {vt} → {item}")
            else:
                errors.append(f"村型 {vt} 的产出物 {item} 不在世界物品集里 → 产出表塞 null → 建世界 NRE")
                print(f"  [ERROR] {vt} → {item} 缺失")

    print()
    for w in warns:
        print(f"  [WARN]  {w}")
    print(f"\nSummary: errors={len(errors)} warnings={len(warns)}")
    for e in errors:
        print(f"  [ERROR] {e}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
