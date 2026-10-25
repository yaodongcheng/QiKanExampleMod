#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""给 RDR2 大地图场景的水面配材质与颜色（默认值太淡）。

背景（2026-10-25）：
  新建场景的水属性是引擎默认值 —— `water_material="water_default"`、
  `water_shallow_color` / `water_deep_color` 都是 **纯白 1,1,1** ⇒ 水面一片惨白。
  原版卡拉迪亚的 `water_exists` 是 false（海画在贴图里），它那套参数是历史遗留，
  我们这张图是真靠水面平面，必须自己给颜色和材质。

动作（幂等，可重复跑）：
  · water_material      → wat_main_map_ocean_g（原版大地图海面材质）
  · water_shallow_color → 0.060, 0.200, 0.220
  · water_deep_color    → 0.010, 0.060, 0.140
  · water_strength      → 1.500
  · choppiness          → 2.000（没有这个属性就补一条，原版节里有）
  · water_level / water_exists 不动

改前备份 scene.xscene.
"""

import io
import os
import re
import shutil

SCENE_NAME = os.environ.get("RDR2_SCENE", "Main_map")
SCENE = os.path.join(r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2\SceneObj", SCENE_NAME, "scene.xscene")
LEVEL = os.environ.get("RDR2_WATER_LEVEL", "0.800")   # 场景单位；Main_map=0.8，圣丹尼斯=11.745

WANT = [
    ("water_material", "wat_main_map_ocean_g"),
    ("water_shallow_color", "0.060, 0.200, 0.220"),
    ("water_deep_color", "0.010, 0.060, 0.140"),
    ("water_strength", "1.500"),
    ("choppiness", "2.000"),
    ("water_level", LEVEL),
]


def main():
    print("scene:", SCENE_NAME, "| water_level:", LEVEL)
    s = io.open(SCENE, encoding="utf-8", errors="ignore").read()
    m = re.search(r"<water_properties.*?</water_properties>", s, re.S)
    if not m:
        raise SystemExit("ERROR: 场景里没有 <water_properties>")
    block = m.group(0)
    new_block = block
    for name, value in WANT:
        pat = r'<property name="%s"[^>]*value="[^"]*"' % name
        if re.search(pat, new_block):
            new_block = re.sub(pat, '<property name="%s" value="%s"' % (name, value), new_block)
            print("  set %s = %s" % (name, value))
        else:
            new_block = new_block.replace(
                "</water_properties>",
                '\t<property name="%s" value="%s"/>\n\t</water_properties>' % (name, value))
            print("  add %s = %s" % (name, value))
    if new_block == block:
        print("no change")
        return
    if not os.path.isfile(SCENE + ".bak"):
        shutil.copy2(SCENE, SCENE + ".bak")
        print("backup ->", SCENE + ".bak")
    io.open(SCENE, "w", encoding="utf-8", newline="\n").write(s.replace(block, new_block))
    print("wrote", SCENE)

    # 回读校验
    s2 = io.open(SCENE, encoding="utf-8", errors="ignore").read()
    for name, value in WANT:
        got = re.search(r'<property name="%s"[^>]*value="([^"]*)"' % name, s2)
        print("  verify %-22s = %s" % (name, got.group(1) if got else "MISSING"))


if __name__ == "__main__":
    main()
