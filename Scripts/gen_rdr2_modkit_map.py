#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""RD2Map 沙箱：生成 ModKit 能打开的干净 Main_map 场景骨架。

做这几件事：
  1. 拷 atmosphere.xml / terrain.bin / navmesh.bin / ShaderCache（占位；导入 RDR2 高度图后
     terrain.bin 会被编辑器覆盖）；
  2. TileSets 用目录 junction 指到原版（2.1 GB 不复制）—— 引擎按 `SceneObj/<场景名>/TileSets/`
     找 .gts（native 字符串实证），沙箱自己的场景目录里必须能找到；
  3. 删掉原版卡拉迪亚的所有实体（只留 border_min / border_max），避免看地形时被原版山脉/城镇干扰；
  4. 把 `<terrain>` 改成 RDR2 战役地图规格（节点数/尺寸/min-max 全部读 terrain_meta.json）；
  5. 物理边界对齐地形范围；开全局水面，水位 = 0 米（地形成员已把陆地垫到 ≥0.5 米）；
  6. 追加 snow 层（snow_a_d，490 个原版场景在用这个材质名），让 7 层 RDR2 权重都有落点；
  7. 写导入说明 README_导入步骤.md（模块根）。

生成物禁止手改；改内容 = 改本脚本再重跑。
"""

import copy
import io
import os
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import rdr2_world_meta

MODULES = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules"
MODULE = os.path.join(MODULES, "RD2Map")
SRC = os.path.join(MODULES, "SandBox", "SceneObj", "Main_map")
DST = os.path.join(MODULE, "SceneObj", "Main_map")
TERRAIN_DIR = os.path.join(
    MODULES, "LivingWorldNpcs", "Debug", "RD2构造相关", "terrain")

_META = rdr2_world_meta.load()
NODES_X, NODES_Y, NODE_SIZE = rdr2_world_meta.nodes()
MAP_W, MAP_H = rdr2_world_meta.map_w(), rdr2_world_meta.map_h()
MIN_H, MAX_H = rdr2_world_meta.min_height(), rdr2_world_meta.max_height()

README = """# ModKit 地形导入步骤（RD2Map 沙箱）

> 这一步是整条地形链里唯一必须人工做的环节（`terrain.bin` 只能由编辑器生成）。
> 高度图 + 材质 mask 已经烘好，在
> `h:\\SteamLibrary\\steamapps\\common\\Mount & Blade II Bannerlord\\Modules\\LivingWorldNpcs\\Debug\\RD2构造相关\\terrain\\`

## 操作

1. ModKit 打开模块 **RD2Map**，场景 **Main_map**（不是 SandBox 里的同名场景）。
   - 若模块列表里没有 RD2Map：ModKit 要跑在**能看见这个模块**的客户端上（两个客户端都建了 junction）。
2. 打开 **Terrain** 面板，先确认四件套（不对就手填）：
   - X = %d，Y = %d，Single Node Size = %.4f，Single Node Dim. = 256x256
3. 点 **Import Heightmap**，选 `rdr2_height_4097.png`（16bit 灰度，4097×3073）。
   - 🔴 ModKit 只问 **scale / offset**（地形底面固定为 0，没有负高度）：
     **scale = %.4f，offset = 0**
   - 判据：导完最高点应 ≈ %.2f 场景单位。
4. 逐层导入材质 mask —— **先在图层列表里选中那一层，再 Import Materialmap**：

   | 场景图层 | 导入文件 | 对应 RDR2 层 |
   |---|---|---|
   | default | mask_default.png | grass（草） |
   | flora_forest | mask_flora_forest.png | conifer（针叶林，带松树网格） |
   | rock | mask_rock.png | rock（岩） |
   | grass | mask_grass.png | broadleaf（阔叶林） |
   | dirt | mask_dirt.png | dirt（泥） |
   | river | mask_river.png | water（水） |
   | snow | mask_snow.png | snow（雪） |
   | dirt2 / forest2 | 不导（保持 0） | — |

5. **保存场景**。编辑器写出新的 `terrain.bin`，视口里就是 RDR2 地形。

## 验收：应该看到什么

- 轮廓与 `rdr2_color_ref_2048.png`（RDR2 地面彩图缩略，只对照、不导入）一致：
  北边亮（安巴里诺雪山）、西南暗（新奥斯汀低地）、东南是河湖与沼泽。
- 地形高低差 ≈ 725 米 ÷ 14.5 ≈ 50 个场景单位；边界（border 实体）正好在地形四角 0..%.1f / 0..%.1f。

## 出问题怎么判

| 症状 | 原因 | 处理 |
|---|---|---|
| 地形全白 / 材质方块 | `WorldMap` tileset 没找到（本模块 `TileSets` 是指向原版的目录 junction，被删就会这样） | 跑 `python "h:\\SteamLibrary\\steamapps\\common\\Mount & Blade II Bannerlord\\Modules\\LivingWorldNpcs\\Scripts\\gen_rdr2_modkit_map.py"` 重建 |
| 地形是卡拉迪亚老样子 | 高度图没导进去（或导到了别的场景） | 重导 `rdr2_height_4097.png`，确认选中的是 RD2Map 的 Main_map |
| 雪区是白板 | `snow_a_d` 在 Native 的 `core.tpac` 里（已核，随每个模块加载），正常不会缺 | 若真缺：把 `mask_snow` 并进 rock 层，雪等换 colormap 方案再处理 |
| 陆地大面积变成水面 | 水位开得太高（全局只有一个海平面） | 地形成员已按 B 方案处理：陆地垫到 ≥0.5 米、低洼水体挖到 −2 米；水位固定 0 米，别往上调 |

## 导完之后

把 `terrain.bin` 与场景里的 `<terrain>` 段交给下一步 —— 跑
`python "h:\\SteamLibrary\\steamapps\\common\\Mount & Blade II Bannerlord\\Modules\\LivingWorldNpcs\\Scripts\\apply_rdr2_terrain.py"`
把它们搬进 RD2 内容包（见 `plans\\大镖客2还原\\00-总纲.md`）。
""" % (NODES_X, NODES_Y, NODE_SIZE, (rdr2_world_meta.max_height() - rdr2_world_meta.min_height()),
       rdr2_world_meta.max_height(), MAP_W, MAP_H)


def ensure_tilesets_link():
    """TileSets 不复制（2.1 GB），建目录 junction 指到原版 Main_map 的 TileSets。

    引擎按 `SceneObj/<场景名>/TileSets/` 找 .gts（TaleWorlds.Native.dll 里的路径串实证），
    沙箱自己的场景目录里没有它就会找不到 tileset（地形白板 / 材质缺失）。
    """
    link = os.path.join(DST, "TileSets")
    src = os.path.join(SRC, "TileSets")
    if os.path.exists(link):
        return "exists"
    if not os.path.isdir(src):
        return "source missing"
    r = subprocess.run(["cmd", "/c", "mklink", "/J", link, src],
                       capture_output=True, text=True)
    if r.returncode != 0:
        raise RuntimeError("mklink /J failed: %s" % (r.stdout + r.stderr).strip())
    return "linked"



def main():
    os.makedirs(DST, exist_ok=True)
    for name in ("atmosphere.xml", "terrain.bin", "navmesh.bin"):
        s = os.path.join(SRC, name)
        d = os.path.join(DST, name)
        if os.path.isfile(s) and not os.path.exists(d):
            shutil.copy2(s, d)
            print("copied", name)

    sc_src = os.path.join(SRC, "ShaderCache")
    sc_dst = os.path.join(DST, "ShaderCache")
    if os.path.isdir(sc_src) and not os.path.isdir(sc_dst):
        shutil.copytree(sc_src, sc_dst)
        print("copied ShaderCache")
    print("TileSets:", ensure_tilesets_link())

    tree = ET.parse(os.path.join(SRC, "scene.xscene"))
    root = tree.getroot()

    # 1) 地形规格（范围与节点数读 terrain_meta.json）
    terrain = root.find("terrain")
    terrain.set("node_dimension_x", str(NODES_X))
    terrain.set("node_dimension_y", str(NODES_Y))
    terrain.set("node_size", "%.4f" % NODE_SIZE)
    terrain.set("min_height", "%.3f" % MIN_H)
    terrain.set("max_height", "%.3f" % MAX_H)
    print("terrain %dx%d @ %.4f | min/max = %.3f / %.3f"
          % (NODES_X, NODES_Y, NODE_SIZE, MIN_H, MAX_H))

    # 2) 植被边界清零（不拷 flora.bin，避免原版树盖在 RDR2 地形上）
    fb = root.find("flora_bounding_rect")
    if fb is not None:
        fb.set("min", "0.000, 0.000")
        fb.set("max", "0.000, 0.000")

    # 3) 自动寻路 + 物理边界（物理边界必须与地形一致：0..848；原版值 -973 是给背景山实体的）
    props = {p.get("name"): p for p in root.find("environment_properties").iter("property")}
    if "nav_mesh_auto_generated_" in props:
        props["nav_mesh_auto_generated_"].set("value", "true")
    if "physics_world_min" in props:
        props["physics_world_min"].set("value", "0.000, 0.000")
    if "physics_world_max_" in props:
        props["physics_world_max_"].set("value", "%.3f, %.3f" % (MAP_W, MAP_H))
    print("physics world:", props["physics_world_min"].get("value"),
          "->", props["physics_world_max_"].get("value"))

    # 4) 全局水位：骑砍大地图只有一个海平面。地形侧已按 B 方案处理（陆地垫到 ≥0.5 m、
    #    低洼水体挖到 −2 m），所以这里**开水**，水位 = 0 米 = 0 场景单位。
    wp = root.find("environment_properties/water_properties")
    if wp is not None:
        for p in wp.iter("property"):
            if p.get("name") == "water_exists":
                p.set("value", "true")
            elif p.get("name") == "water_level":
                p.set("value", "%.3f" % rdr2_world_meta.water_level_units())
    print("water: exists=true level=%.3f units" % rdr2_world_meta.water_level_units())

    # 5) 只留相机边界实体
    ents = root.find("entities")
    removed = 0
    for e in list(ents):
        if e.get("name") not in ("border_min", "border_max"):
            ents.remove(e)
            removed += 1
    print("removed entities:", removed)
    for e in list(ents):
        n = e.get("name")
        pos = "0.000, 0.000, 0.000" if n == "border_min" else "%.3f, %.3f, 1000.000" % (MAP_W, MAP_H)
        tr = e.find("transform")
        if tr is None:
            tr = ET.SubElement(e, "transform")
        tr.set("position", pos)
        tr.set("rotation_euler", "0.000, 0.000, 0.000")

    # 6) 追加 snow 层（复制 default 层，换名字与贴图）
    layers = terrain.find("layers")
    names = {l.get("name") for l in layers}
    if "snow" not in names:
        default = next(l for l in layers if l.get("name") == "default")
        snow = copy.deepcopy(default)
        snow.set("name", "snow")
        snow.set("physics_material", "grass")
        for tex in snow.iter("texture"):
            t = tex.get("type")
            if t == "diffusemap":
                tex.set("name", "snow_a_d")
            elif t == "displacementmap":
                tex.set("name", "")
            else:
                tex.set("name", "none")
        layers.append(snow)
        print("added snow layer")

    # 7) 预览素材：把圣丹尼斯图标（metamesh + 图集材质）拷进本模块，并把图标摆到地形上
    for pack in ("rdr2_city_atlas.tpac", "rdr2_map_icons.tpac"):
        s = os.path.join(MODULES, "RD2", "AssetPackages", pack)
        d = os.path.join(MODULE, "AssetPackages", pack)
        if os.path.isfile(s):
            os.makedirs(os.path.dirname(d), exist_ok=True)
            shutil.copy2(s, d)
            print("copied pack", pack)

    import gen_rdr2_world as W
    from gen_rdr2_map_scene import load_height, terrain_z
    world, _ = W.load_world()
    row = next(r for r in world if r["code"] == "sdn")
    z = terrain_z(load_height(), row["u"], row["v"]) + 0.05
    ents.append(ET.fromstring(
        '<game_entity name="rdr2_sdn_icon" old_prefab_name="" mobility="1">'
        '<transform position="%.1f, %.1f, %.3f" rotation_euler="0.000, 0.000, 0.000"/>'
        '<components><meta_mesh_component name="rdr2_m_sdn"/></components></game_entity>'
        % (row["u"], row["v"], z)))
    print("placed rdr2_sdn_icon at (%.1f, %.1f, %.3f)" % (row["u"], row["v"], z))

    out = os.path.join(DST, "scene.xscene")
    tree.write(out, encoding="utf-8", xml_declaration=True)
    print("wrote", out, os.path.getsize(out), "bytes")

    # 7) 导入说明（生成物，改文本 = 改本脚本顶部 README 串）
    readme_path = os.path.join(MODULE, "README_导入步骤.md")
    io.open(readme_path, "w", encoding="utf-8", newline="\n").write(README)
    print("wrote", readme_path)
    # 同一份说明也放到地形产物目录（旧版脚本在那里留过一份，避免两份说法不一致）
    io.open(os.path.join(TERRAIN_DIR, "README_导入步骤.md"), "w",
            encoding="utf-8", newline="\n").write(README)
    print("wrote", os.path.join(TERRAIN_DIR, "README_导入步骤.md"))


if __name__ == "__main__":
    main()
