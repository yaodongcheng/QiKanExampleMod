#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Item civilian-flag checker (所有物品都必须「平民可用」)
============================================================================
背景（2026-09-21 用户裁定，已升为 CLAUDE.md 铁律）：
  **任何装备都要允许平民装使用** —— 物品必须带 `<Flags Civilian="true" />`。
  否则玩家在城镇/据点里换日常装时该物品**不可选**（典狱长/进城/潜入等要求平民装的场合全被挡住），
  一个"能在战场上用、进了城就消失"的物品对玩家是莫名其妙的。

判据（唯一）：
  内容包 `ModuleData/**/*.xml` 里，根元素是 `<Items>` 的每一份，其**每一个** `<Item>`
  都必须有一个 `<Flags>` 子元素且 `Civilian="true"`。缺 Flags、或缺该属性 = 报错。

  为什么看「根元素是不是 `<Items>`」：同一份 ModuleData 下还有 `<CraftingPiece>` /
  `<EquipmentRoster>` / `<Culture>` 等别的根，那些不是物品、不适用本规则。

例外：**没有**。本题的裁定是"任何装备"，马匹、弹药、盾牌、旗帜全都要（2026-09-21 全库实测 197/197 合规）。

容错（铁律 1）：单个文件解析失败 → 记一条日志、继续扫其余文件，不抛。

Usage:
  python Scripts/check_items_civilian.py [--module PATH]
Exit: 0 全合规 / 1 有缺 / 2 fatal。
负面测试：`Scripts/test_negative_checks.py` 里「物品：缺 Civilian 必须抓到」那一例。
"""
import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def is_civilian(item):
    """`<Item>` 是否声明了平民可用。Flags 缺失 / 属性缺失 / 值不是 true 都算不合规。"""
    flags = item.find("Flags")
    if flags is None:
        return False
    return (flags.get("Civilian") or "").strip().lower() == "true"


def main():
    ap = argparse.ArgumentParser(description="Item civilian flag checker")
    ap.add_argument("--module",
                    default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    args = ap.parse_args()

    mod = Path(args.module)
    data = mod / "ModuleData"
    if not data.is_dir():
        print(f"[FATAL] ModuleData not found: {data}")
        return 2

    bad, total, files = [], 0, 0
    for f in sorted(data.rglob("*.xml")):
        try:
            root = ET.parse(str(f)).getroot()
        except Exception as e:
            print(f"  [WARN] 解析失败，跳过 {f.relative_to(data)}: {e}")
            continue
        if root.tag != "Items":
            continue                       # 不是物品文件（CraftingPiece / Culture / EquipmentRoster …）
        files += 1
        for item in root.findall("Item"):
            total += 1
            if not is_civilian(item):
                bad.append((f.relative_to(data).as_posix(), item.get("id") or "(无 id)"))

    print(f"== 物品平民装可用性（根元素为 <Items> 的文件 {files} 份） ==")
    if bad:
        print(f"  [ERROR] {len(bad)} 件物品缺 <Flags Civilian=\"true\" /> —— 玩家在城镇换日常装时选不到它")
        for rel, iid in bad:
            print(f"      {rel}  →  {iid}")
        print(f"\n  修：给该 <Item> 的 <Flags> 补 Civilian=\"true\"（没有 <Flags> 就加一个）")
        print(f"      例：<Flags UseTeamColor=\"true\" Civilian=\"true\" />")
    else:
        print(f"  （{total} 件物品全部平民可用 ✓）")
    print(f"\nSummary: files={files} items={total} missing={len(bad)}")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
