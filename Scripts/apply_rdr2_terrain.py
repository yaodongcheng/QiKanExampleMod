#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 ModKit 烘好的 RDR2 地形从 RD2Map 沙箱搬进 RD2 内容包。

前置：用户已在 ModKit 打开 RD2Map / Main_map，Import Heightmap + 7 层 mask 并保存
（编辑器会把烘好的 `terrain.bin` 写进 RD2Map/SceneObj/Main_map/）。

做四件事：
  1. 守卫：RD2Map 的 terrain.bin 必须已经变过（不等于原版 SandBox 那份），否则报错退出；
  2. 拷 terrain.bin → RD2/SceneObj/Main_map/；
  3. 把 RD2Map 场景里的 `<terrain>` 整段（含 9 个图层定义）搬到 RD2 场景 —— 图层顺序必须与
     烘好的 terrain.bin 权重通道一一对应，所以整段替换，不逐项改；
  4. 物理边界对齐 0..848（与原版 -973 的差别：原版那是给背景山实体留的，我们没有）。

RD2 场景里的 46 个据点实体（gen_rdr2_map_scene.py 生成）**不动**。

🔴 生成顺序：先 `gen_rdr2_map_scene.py`（场景 + 据点实体，地形是卡拉迪亚占位）→ 用户 ModKit 烘地形
→ 再跑本脚本（只替换 `<terrain>` 段与 terrain.bin）。**本脚本跑完之后不要再跑 `gen_rdr2_map_scene.py`**
——它会用原版地形重写场景；为防手滑，本脚本会写一个标记文件，`gen_rdr2_map_scene.py` 见到标记
就把现有 `<terrain>` 原样带过去。
"""

import copy
import hashlib
import io
import os
import shutil
import sys
import xml.etree.ElementTree as ET

MODULES = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules"
SANDBOX = os.path.join(MODULES, "SandBox", "SceneObj", "Main_map")
SRC = os.path.join(MODULES, "RD2Map", "SceneObj", "Main_map")
DST = os.path.join(MODULES, "RD2", "SceneObj", "Main_map")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rdr2_world_meta


def sha1(path):
    h = hashlib.sha1()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main():
    src_bin = os.path.join(SRC, "terrain.bin")
    dst_bin = os.path.join(DST, "terrain.bin")
    if not os.path.isfile(src_bin):
        sys.exit("ERROR: %s not found" % src_bin)

    vanilla = os.path.join(SANDBOX, "terrain.bin")
    if os.path.isfile(vanilla) and sha1(src_bin) == sha1(vanilla):
        sys.exit("ERROR: RD2Map terrain.bin is still the vanilla copy -- "
                 "ModKit import has not happened yet (Import Heightmap + masks, then Save).")

    os.makedirs(DST, exist_ok=True)
    shutil.copy2(src_bin, dst_bin)
    print("terrain.bin copied: %.1f MB" % (os.path.getsize(dst_bin) / 1e6))

    sc_src = os.path.join(SRC, "ShaderCache")
    sc_dst = os.path.join(DST, "ShaderCache")
    if os.path.isdir(sc_src) and not os.path.isdir(sc_dst):
        shutil.copytree(sc_src, sc_dst)
        print("ShaderCache copied")

    src_tree = ET.parse(os.path.join(SRC, "scene.xscene"))
    dst_tree = ET.parse(os.path.join(DST, "scene.xscene"))
    src_root = src_tree.getroot()
    dst_root = dst_tree.getroot()

    src_terrain = src_root.find("terrain")
    if src_terrain is None:
        sys.exit("ERROR: no <terrain> in RD2Map scene")

    old = dst_root.find("terrain")
    idx = list(dst_root).index(old)
    dst_root.remove(old)
    dst_root.insert(idx, copy.deepcopy(src_terrain))
    t = dst_root.find("terrain")
    print("terrain params: %sx%s node @ %s | min %s max %s" % (
        t.get("node_dimension_x"), t.get("node_dimension_y"), t.get("node_size"),
        t.get("min_height"), t.get("max_height")))
    print("layers:", [l.get("name") for l in t.find("layers")])

    props = {p.get("name"): p for p in dst_root.find("environment_properties").iter("property")}
    if "physics_world_min" in props:
        props["physics_world_min"].set("value", "0.000, 0.000")
    if "physics_world_max_" in props:
        props["physics_world_max_"].set("value", "%.3f, %.3f" % (rdr2_world_meta.map_w(), rdr2_world_meta.map_h()))

    out = os.path.join(DST, "scene.xscene")
    dst_tree.write(out, encoding="utf-8", xml_declaration=True)
    print("wrote", out, os.path.getsize(out), "bytes")

    # 标记：已经换成 RDR2 地形了（gen_rdr2_map_scene.py 见到它会保留现有 <terrain>）
    marker = os.path.join(DST, ".rdr2_terrain_baked")
    io.open(marker, "w", encoding="utf-8").write("baked by apply_rdr2_terrain.py\n")
    print("marker:", marker)


if __name__ == "__main__":
    main()
