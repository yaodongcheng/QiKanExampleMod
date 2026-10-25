#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把原版卡拉迪亚的**地形图层集合**补进 RDR2 大地图场景（解决"地形没贴图"）。

背景（2026-10-25）：
  ModKit 新建的场景默认只有 1 个 `default` 图层、`vista_tileset=""`，导入 materialmap 也没地方放。
  日本那张图（Taikou）的做法是：
    ① `<terrain vista_tileset="WorldMap">` —— 贴图走整套 WorldMap vista tileset，不是每层自带图；
    ② 原样抄原版 Main_map 的 8 个 `<layer>` 定义（default / flora_forest / rock / grass /
       dirt / dirt2 / forest2 / river）—— 正好和我们烘的 7 张 mask 一一对应；
    ③ 场景目录放一份 `references.txt`（资源引用清单），编辑器靠它把贴图拉进来；
    ④ 模块依赖 SandBox ⇒ tileset 从 SandBox 资产包解析，**不需要** TileSets junction。

本脚本只做 ①②③：
  · 不改 terrain.bin（你导进去的高度不动）
  · 不改 node_dimension / node_size / min_height / max_height
  · 改前备份 scene.xscene → scene.xscene.bak
"""

import io
import os
import re
import shutil

MODULES = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules"
VANILLA = os.path.join(r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules",
                       "SandBox", "SceneObj", "Main_map", "scene.xscene")
DST_DIR = os.path.join(MODULES, "RDR2", "SceneObj", "Main_map")
DST = os.path.join(DST_DIR, "scene.xscene")
REFS = os.path.join(DST_DIR, "references.txt")

# 从原版 terrain 抄过来的视觉属性（节点数/高度范围不抄）
WANT_ATTRS = [
    "lod_multiplier", "vista_normalmap", "vista_diffuse_name", "vista_diffuse_blend_type",
    "vista_diffuse_blend_amount", "vista_layer_detail_distance", "vista_albedo_multiplier",
    "vista_detail_albedo_name", "vista_detail_normal_name", "vista_detail_tile",
    "vista_tileset", "use_dynamic_weather_effects", "dynamic_flowmap_texture_name",
    "colormap_detail_level", "ao_intensity",
]


def attr(tag, name):
    m = re.search(r'%s="([^"]*)"' % name, tag)
    return m.group(1) if m else None


def main():
    van = io.open(VANILLA, encoding="utf-8", errors="ignore").read()
    dst = io.open(DST, encoding="utf-8", errors="ignore").read()

    van_terrain = re.search(r"<terrain[^>]*>", van).group(0)
    van_layers = re.search(r"<layers version=\"1\".*?</layers>", van, re.S).group(0)

    # 原版 8 层里没有 snow —— 追加一层（复制 default 换贴图 snow_a_d，Native core.tpac 里实证存在）
    if 'name="snow"' not in van_layers:
        m = re.search(r'(\t*<layer name="default">.*?</layer>)', van_layers, re.S)
        if m:
            block = m.group(1)
            snow = block.replace('<layer name="default">', '<layer name="snow">', 1)
            snow = re.sub(r'(<texture type="diffusemap" name=")[^"]*(")', r'\g<1>snow_a_d\g<2>', snow)
            snow = re.sub(r'(<texture type="displacementmap" name=")[^"]*(")', r'\g<1>\g<2>', snow)
            snow = re.sub(r'(<texture type="(?:areamap|normalmap|specularmap|splattingmap)" name=")[^"]*(")',
                          r'\g<1>none\g<2>', snow)
            van_layers = van_layers.replace("</layers>", snow + "\n\t\t</layers>")
            print("appended snow layer")
    print("vanilla layers:", re.findall(r'<layer name="([^"]+)"', van_layers))

    dst_terrain = re.search(r"<terrain[^>]*>", dst).group(0)
    new_terrain = dst_terrain
    for a in WANT_ATTRS:
        v = attr(van_terrain, a)
        if v is None:
            continue
        if re.search(r'%s="[^"]*"' % a, new_terrain):
            new_terrain = re.sub(r'%s="[^"]*"' % a, '%s="%s"' % (a, v), new_terrain)
        else:
            new_terrain = new_terrain[:-1].rstrip() + ' %s="%s">' % (a, v)
    print("terrain attrs applied; vista_tileset =", attr(new_terrain, "vista_tileset"))

    # 备份
    if not os.path.isfile(DST + ".bak"):
        shutil.copy2(DST, DST + ".bak")
        print("backup ->", DST + ".bak")

    # 换图层块
    dst_layers = re.search(r"<layers version=\"1\".*?</layers>", dst, re.S)
    if dst_layers:
        out = dst.replace(dst_layers.group(0), van_layers)
    else:
        body = re.search(r"<terrain[^>]*>(.*?)</terrain>", dst, re.S)
        out = dst.replace(body.group(1), body.group(1) + van_layers)
    out = out.replace(dst_terrain, new_terrain)
    io.open(DST, "w", encoding="utf-8", newline="\n").write(out)
    print("wrote", DST)

    # references.txt：保留已有条目，补上图层引用到的贴图
    texs = sorted(set(re.findall(r'name="([a-z0-9_]+_(?:d|n|s|h|mainmap|main_map|flowmap))"', van_layers)))
    texs += sorted(set(re.findall(r'name="(ground_[a-z0-9_]+|main_map_[a-z0-9_]+|cliff_[a-z0-9_]+|water_ground|rock_a|gloss_s|stone_[a-z0-9_]+)"', van_layers)))
    texs = sorted(set(texs))
    old = []
    if os.path.isfile(REFS):
        old = [l.strip() for l in io.open(REFS, encoding="utf-8").read().splitlines() if l.strip()]
    # 纯数字行是条目计数（首行），重跑时会被当成一条资源累加 —— 一律丢掉
    old = [l for l in old if not l.isdigit()]
    entries = set(old)
    for t in texs:
        entries.add("texture " + t)
    header = [str(len(entries))] + sorted(entries)
    io.open(REFS, "w", encoding="utf-8", newline="\n").write("\n".join(header) + "\n")
    print("wrote", REFS, "(%d entries)" % len(entries))


if __name__ == "__main__":
    main()
