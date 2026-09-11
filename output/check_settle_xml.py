# -*- coding: utf-8 -*-
"""校验生成的 settlements XML：编码/条目/键/抽样内容。"""
import io
import re
import sys

p = sys.argv[1] if len(sys.argv) > 1 else r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\output\_tmp_module\ModuleData\settlements.xml"
s = io.open(p, encoding="utf-8").read()
print("=== 前 3 行 ===")
print("\n".join(s.splitlines()[:3]))
for tag in ("town_tk105", "village_tk212", "castle_tk258", "town_tk084"):
    m = re.search(r'<Settlement id="%s".*?</Settlement>' % tag, s, re.S)
    print("--- %s ---" % tag)
    print("\n".join(m.group(0).splitlines()[:4]) if m else "  未找到")
print("Settlement 数:", s.count("<Settlement "))
print("不同本地化键数:", len(set(re.findall(r"\{=(TAIKOU_[A-Za-z0-9_]+)\}", s))))
print("Village 组件数:", s.count("<Village "))
print("Town 组件数:", s.count("<Town "))
import xml.etree.ElementTree as ET
ET.fromstring(s)
print("XML parse ✓")
