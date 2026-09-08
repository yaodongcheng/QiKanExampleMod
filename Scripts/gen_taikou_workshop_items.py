#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_taikou_workshop_items.py — 工坊产出线生成器（第 12 颗雷修复，2026-09-08）
=====================================================================
背景：RunTownShopsAtGameStart → GetRandomItemAux(ItemCategory, 京) 按分类随机取"可产+文化匹配"物品。
  我们的库（29 物品）merchandise=true = 0，故除 meat/hides/tools（DefaultItems 兜底）外全部分类空
  → 取不到物品 → EquipmentElement(null) → TownMarketData.GetPrice NRE。
修法：按 spworkshops.xml 的全部 Outputs 分类，每分类从官方物品库取 1 个样板物品，
  生成 ikoku 文化 + is_merchandise=true 的"产成品"（id=ikoku_<cat>），产出到新文件
  taikou_produce_items.xml（Items 段），SubModule.xml 注册。

产出物：ModuleData/taikou_produce_items.xml（生成物，禁手改——改本脚本重跑）
纪律：跑完必过 check_taikou_xml_references.py。
用法：python Scripts/gen_taikou_workshop_items.py
"""
import re
from pathlib import Path

MB = Path(r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord")
TAIKOU_MD = MB / "Modules" / "Taikou" / "ModuleData"

# Outputs 分类 → 官方样板物品（原库全量中选取），category 来自官方 items（Type= 或 GetItemCategory 依据）
# 映射人工维护：分类名 → 官方样板 item id（文化/category 匹配即可；我们改 culture=ikoku + merchandise）
OUTPUT_SAMPLES = {
    "tools": "tools_pick_and_shovel",
    "meat": "meat",
    "hides": "hides",
    "wine": "wine",
    "beer": "ale",
    "oil": "olives",
    "velvet": "velvet_cloth",
    "linen": "linen_cloth",
    "leather": "leather",
    "pottery": "pottery",
    "jewelry": "gold",
    "wool": "wool",
    "garment": "wanderer_shirt",
    "light_armor": "leather_cavalier_boots",
    "medium_armor": "desert_lamellar",
    "heavy_armor": "reinforced_mail_mitten",
    "ultra_armor": "masks",
    "melee_weapons": "battania_mace_1_t2",
    "ranged_weapons": "nordic_shortbow",
    "arrows": "blunt_arrows",
    "shield": "leather_round_shield",
    "horse_equipment": "horse_harness_e",
    "war_horse": "charger",
}


def find_official_item(item_id):
    """在官方 SandBoxCore ModuleData/items/*.xml 里找 <Item id=...> 完整 cell 文本。"""
    haystack = {}
    for f in (MB / "Modules" / "SandBoxCore" / "ModuleData" / "items").glob("*.xml"):
        txt = f.read_text(encoding="utf-8-sig", errors="replace")
        for m in re.finditer(r'<Item\s+([^>]*?)\bid="' + re.escape(item_id) + r'"([^>]*?)>(.*?)</Item>', txt, re.S):
            return f"<Item{m.group(1)}id=\"{item_id}\"{m.group(2)}>{m.group(3)}</Item>"
    return None


def main():
    wsmd = TAIKOU_MD / "spworkshops.xml"
    txt = wsmd.read_text(encoding="utf-8-sig", errors="replace")
    cats = sorted(set(re.findall(r'output="ItemCategory\.([A-Za-z_0-9]+)"', txt)))
    print(f"Outputs 分类（{len(cats)}）:", cats)

    cells = []
    missing = []
    for cat in cats:
        sample = OUTPUT_SAMPLES.get(cat)
        if sample is None:
            missing.append(cat)
            continue
        cell = find_official_item(sample)
        if cell is None:
            print(f"[warn] 官方样板 {sample} 未找到")
            missing.append(cat)
            continue
        nid = f"ikoku_{cat}"
        # 改 id / culture=ikoku / is_merchandise=true（官方样板可能无 merchandise → 补；有则替换）
        cell = cell.replace(f'id="{sample}"', f'id="{nid}"', 1)
        cell = re.sub(r'culture="Culture\.[A-Za-z_0-9]+"', 'culture="Culture.ikoku"', cell)
        if 'is_merchandise="true"' not in cell:
            cell = cell.replace('name=', 'is_merchandise="true" name=', 1)
        cells.append(cell)
        print(f"[ok] {cat} -> {nid} (源自 {sample})")
    if missing:
        print("[warn] 无样板的分类:", missing)

    out = '<?xml version="1.0" encoding="utf-8"?>\n<!-- 生成物（gen_taikou_workshop_items.py，2026-09-08 第 12 雷修复）：'
    out += '工坊产出线（ikoku 文化 + merchandise）——改内容改本脚本重跑，禁手改 -->\n<Items>\n' + "\n".join(cells) + "\n</Items>\n"
    outfile = TAIKOU_MD / "taikou_produce_items.xml"
    outfile.write_text(out, encoding="utf-8-sig")
    import xml.dom.minidom as m
    m.parse(str(outfile))
    print(f"\n写入 {outfile}（{len(cells)} 个物品），parse OK")


if __name__ == "__main__":
    main()
