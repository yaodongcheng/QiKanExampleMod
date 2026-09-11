# -*- coding: utf-8 -*-
"""从官方 Main_map 场景抽：村实体 / 城堡实体 的完整结构（作为生成模板）。"""
import xml.etree.ElementTree as ET

MB2 = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord"
p = MB2 + r"\Modules\SandBox\SceneObj\Main_map\scene.xscene"
root = ET.parse(p).getroot()

want = {"village": None, "castle": None, "town": None}


def tagsof(el):
    return [t.get("name") for t in el.findall("./tags/tag")]


for el in root.iter("game_entity"):
    nm = el.get("name") or ""
    tg = tagsof(el)
    if want["village"] is None and "village" in tg and nm.startswith("village_"):
        want["village"] = el
    if want["castle"] is None and "castle" in tg and nm.startswith("castle_"):
        want["castle"] = el
    if want["town"] is None and "town" in tg and nm.startswith("town_"):
        want["town"] = el
    if all(want.values()):
        break

for k, el in want.items():
    print("=" * 24, k, "=" * 24)
    if el is None:
        print("  未找到")
        continue
    ET.indent(el, space=" ")
    s = ET.tostring(el, encoding="unicode")
    print(s[:2200])
