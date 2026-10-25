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
SCENE = os.environ.get("RDR2_SCENE", "Main_map")     # 场景目录名：Main_map / rdr2_sanit_denis
DST_DIR = os.path.join(MODULES, "RDR2", "SceneObj", SCENE)
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
    print("scene:", SCENE)
    van = io.open(VANILLA, encoding="utf-8", errors="ignore").read()
    dst = io.open(DST, encoding="utf-8", errors="ignore").read()

    van_terrain = re.search(r"<terrain[^>]*>", van).group(0)
    van_layers = re.search(r"<layers version=\"1\".*?</layers>", van, re.S).group(0)

    # 原版 8 层里没有 snow —— 雪区靠 rock 层（main_map_mountains_d 灰白岩石）表现，
    # 所以不再追加 snow 层；雪的权重并进 mask_rock（见 gen_rdr2_terrain.py 的 mask_rock_snow）。
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

    # 🔴 关键一步（wheels.d/terrain.md 2026-09-06 实锤）：层数变了，节点的
    # `layer_is_used_mask_*` **不会**自动更新 —— 新建场景按"当时层数"写成 1（只认 layer0），
    # 注入 8 层后除 default 外全不渲染。必须把四个季节的 mask 全部置 255。
    n_mask = 0
    for season in ("summer", "fall", "winter", "spring"):
        out, k = re.subn(r'layer_is_used_mask_%s="\d+"' % season,
                         'layer_is_used_mask_%s="255"' % season, out)
        n_mask += k
    print("layer_is_used_mask -> 255 :", n_mask, "处")

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
