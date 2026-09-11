# -*- coding: utf-8 -*-
"""抽官方三类据点条目（城=town / 城堡=castle / 村=village），作为生成器模板。"""
import xml.etree.ElementTree as ET
import io

MB2 = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord"
root = ET.parse(MB2 + r"\Modules\SandBox\ModuleData\settlements.xml").getroot()

want = {"castle": None, "empire_village": None, "empire_town": None}
for s in root:
    if s.tag != "Settlement":
        continue
    sid = s.get("id", "")
    comp = s.find("Components")
    if comp is None:
        continue
    kinds = [c.tag for c in comp]
    if "Castle" in kinds and want["castle"] is None:
        want["castle"] = s
    if "Village" in kinds and s.get("culture") == "Culture.empire" and want["empire_village"] is None:
        want["empire_village"] = s
    if "Town" in kinds and s.get("culture") == "Culture.empire" and want["empire_town"] is None:
        want["empire_town"] = s

for k, s in want.items():
    print("=" * 20, k, "=" * 20)
    if s is None:
        print("  未找到")
        continue
    ET.indent(s, space="  ")
    print(ET.tostring(s, encoding="unicode")[:2600])
