#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Item mesh-field checker (物品的网格字段不许留空 —— 雷 166 的同一族)
============================================================================
背景（2026-10-08 实机崩因，登记为雷 168）：
  **空网格名在引擎里等于 null，而引擎取网格的每条路径都没做 null 兜底**，崩法按字段而异：

  · `mesh=""` → **装备那一刻 `AccessViolation`**（native `WeaponEquipped` 取网格，雷 166）
  · `holster_mesh=""` 且物品 **Type = Arrows / Bolts** → **背包 / 装备界面搜到这件就 `NullReferenceException`**
    （雷 168）。原因：箭类物品的**背包图标取的是 `holster_mesh` 而不是 `mesh` ——
    反编译 `ItemCollectionElementViewExtensions.GetItemMeshForInventory`：这两种类型直接
    `return item.GetHolsterMeshCopy()`，空串被 `string.IsNullOrEmpty` 拦下 ⇒ 返回 null 网格 ⇒
    `TableauCacheManager.AddItem` 里那条分支只打一句 `MBDebug.ShowWarning`（发布版无声），
    随后仍执行 `val.SetVisibilityExcludeParents(false)` ⇒ NRE。图标懒加载 ⇒ 症状是"搜到才崩"。
  · `mesh` 属性**整个缺失** → 与空串同源（`MeshName == null`）⇒ 一并报错。
    依据：全库 4424 件真物品定义**无一例外**都有 `mesh`（缺 = 我们的笔误）。

判据（三档）：
  [ERROR] ① `mesh` 缺失 或 `mesh` 为空串（任何物品类型）
  [ERROR] ② `holster_mesh` 为空串 **且** Type ∈ {Arrows, Bolts}（背包图标必崩）
  [WARN]  ③ 其余网格字段（`holster_mesh` / `holster_mesh_with_weapon` / `flying_mesh` …）留空
          —— 引擎对这些类型不查该字段（第三方 mod HikageRising 就有 35 件空 `holster_mesh`，
          都在单手 / 投掷类，实机不崩），但绝大多数是"照抄时删漏"，故只告警不改红。

扫描口径：`ModuleData/**/*.xml` 里**根元素是 `<Items>`** 的每一份，其**直接子元素** `<Item>`。
  （与 check_items_civilian.py 同口径：`<Cosmetic>` 里的 `<Item id="…"/>` 是**引用**不是定义；
   `<CraftingPiece mesh="">` 是原版惯例的"空件"，都不适用本规则。）

容错（铁律 1）：单个文件解析失败 → 记一条日志、继续扫其余文件，不抛。
`--strict-empty-holster` 可将 ③ 升为错误（默认关 —— 免得把第三方 mod 的合法写法当红）。

Usage:
  python Scripts/check_items_mesh_fields.py [--module PATH] [--strict-empty-holster]
Exit: 0 全合规 / 1 有 ERROR / 2 fatal。
负面测试：`Scripts/test_negative_checks.py`「物品：空网格字段必须抓到（雷 168）」。
"""
import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# 背包图标走 holster_mesh 的类型（其余类型图标走 mesh）—— 见文件头反编译结论
HOLSTER_ICON_TYPES = {"Arrows", "Bolts"}


def main():
    ap = argparse.ArgumentParser(description="Item mesh-field checker")
    ap.add_argument("--module",
                    default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    ap.add_argument("--strict-empty-holster", action="store_true",
                    help="把「非箭类物品的空网格字段」也当错误（默认只告警）")
    args = ap.parse_args()

    mod = Path(args.module)
    data = mod / "ModuleData"
    if not data.is_dir():
        print(f"[FATAL] ModuleData not found: {data}")
        return 2

    errors, warns, total, files = [], [], 0, 0
    for f in sorted(data.rglob("*.xml")):
        try:
            root = ET.parse(str(f)).getroot()
        except Exception as e:
            print(f"  [WARN] 解析失败，跳过 {f.relative_to(data)}: {e}")
            continue
        if root.tag != "Items":
            continue
        files += 1
        for item in root.findall("Item"):
            total += 1
            rel = f.relative_to(data).as_posix()
            iid = item.get("id") or "(无 id)"
            itype = item.get("Type") or ""

            # ① mesh 缺失 / 空串
            mesh = item.get("mesh")
            if mesh is None:
                errors.append((rel, iid, "mesh", "属性缺失（全库真物品无一例外都有 mesh）"))
            elif not mesh.strip():
                errors.append((rel, iid, "mesh", "空串 —— 装备那一刻 AccessViolation（雷 166）"))

            # ②③ 其余网格字段
            for key, val in item.attrib.items():
                if key == "mesh" or "mesh" not in key.lower():
                    continue
                if (val or "").strip():
                    continue
                if itype in HOLSTER_ICON_TYPES and key == "holster_mesh":
                    errors.append((rel, iid, key,
                                   f"空串 + Type={itype} —— 背包图标取 holster_mesh ⇒ 搜到这件就 NRE（雷 168）"))
                elif args.strict_empty_holster:
                    errors.append((rel, iid, key, "空串（--strict-empty-holster）"))
                else:
                    warns.append((rel, iid, key,
                                  f"空串（本类型图标不走它，引擎容忍；但多半是照抄时删漏）"))

    print(f"== 物品网格字段体检（根元素为 <Items> 的文件 {files} 份 / {total} 件物品） ==")
    if warns:
        print(f"  [WARN] {len(warns)} 处空网格字段（不崩，但建议改成 1 cm 代理件或不写该属性）")
        for rel, iid, key, why in warns[:10]:
            print(f"      {rel}  →  {iid}.{key}  {why}")
        if len(warns) > 10:
            print(f"      …（其余 {len(warns) - 10} 处同上）")
    if errors:
        print(f"  [ERROR] {len(errors)} 处必须修：")
        for rel, iid, key, why in errors:
            print(f"      {rel}  →  {iid}.{key}  {why}")
        print("\n  修法：给它一个真实存在的网格名 —— 「看不见」用 1 cm 代理件")
        print("        （tools/grapple-model/scripts/build_proxy.py），**不许留空**。")
    else:
        print("  （网格字段全部合规 ✓）")
    print(f"\nSummary: files={files} items={total} errors={len(errors)} warns={len(warns)}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
