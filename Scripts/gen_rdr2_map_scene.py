#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""RD2 大地图场景骨架生成器（地形占位 → 由 apply_rdr2_terrain.py 换成 RDR2 地形）

做四件事：
  1. 把原版 SandBox/SceneObj/Main_map 整目录拷到 RD2/SceneObj/Main_map（地形/植被/大气/寻路占位）；
  2. 删掉原版所有据点胶囊（campaign_icon_capsule_*）；
  3. 按 RDR2 据点表生成据点胶囊（位置 = 地图单位 u/v，**高度按 RDR2 高度图贴地**）；
  4. 圣丹尼斯用我们自己的图标 prefab（`rdr2_p_sdn`），其余先用原版帝国图标占位。

地形替换（RDR2 真实地形）是下一步 ModKit 手工导入的事，本脚本不动 terrain.bin。
生成物禁止手改；改内容 = 改本脚本 / 改数据源，然后重跑。
"""

import io
import copy
import os
import shutil
import sys
import xml.etree.ElementTree as ET

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import gen_rdr2_world as W
import rdr2_world_meta

SRC = os.path.join(W.MODULES, "SandBox", "SceneObj", "Main_map")
DST = os.path.join(W.MODULE, "SceneObj", "Main_map")
SCENE_FILES = ("terrain.bin", "navmesh.bin", "atmosphere.xml", "flora.bin", "references.txt")

HEIGHT_PNG = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\RD2构造相关\terrain\rdr2_height_4097.png"
_META = rdr2_world_meta.load()
Z_MIN_M, Z_MAX_M = _META["z_min_m"], _META["z_max_m"]
IMPORT_SCALE = _META["import_scale"]          # 满量程 → 0..50 场景单位（ModKit 口径）

# 用自家图标 prefab 的据点（键 = settlement_id，如 town_sdn；其余用原版帝国图标占位）
ICON_PREFABS = {"town_sdn": "rdr2_p_sdn"}


def load_height():
    if not os.path.isfile(HEIGHT_PNG):
        return None
    return np.asarray(Image.open(HEIGHT_PNG))


def terrain_z(h, u, v):
    """(u,v) = 地图单位（0..map_w / 0..map_h）→ 场景 z（单位）。"""
    if h is None:
        return 3.0
    iy = int(round(v / rdr2_world_meta.map_h() * (h.shape[0] - 1)))
    ix = int(round(u / rdr2_world_meta.map_w() * (h.shape[1] - 1)))
    iy = max(0, min(h.shape[0] - 1, iy))
    ix = max(0, min(h.shape[1] - 1, ix))
    # 🔴 ModKit 的 Import Heightmap 只有 scale/offset，地形底面固定 0（整幅抬高了 −Z_MIN）。
    # 场景坐标 = 归一化值 × import_scale。
    return float(h[iy, ix]) / 65535.0 * IMPORT_SCALE


def capsule_xml(idx, row, z=3.0):
    sid = W.settlement_id(row)
    typ = W.settlement_type(row)
    tag = {"Town": "town", "Castle": "castle", "Village": "village", "Hideout": "wm_hideout"}[typ]
    if typ == "Town":
        mesh, scale, bo_scale, circle = "mi_emp_keep_1", "1.000", "2.618", "0.936"
    elif typ == "Castle":
        mesh, scale, bo_scale, circle = "mi_emp_keep_1", "0.850", "2.000", "0.800"
    elif typ == "Hideout":
        mesh, scale, bo_scale, circle = "mi_emp_keep_1", "0.500", "1.000", "0.500"
    else:
        mesh, scale, bo_scale, circle = "village_empire_1", "0.600", "1.323", "0.600"
    icon_prefab = ICON_PREFABS.get(sid)
    if icon_prefab:
        icon = ('<game_entity name="map_icon" prefab="%s"/>' % icon_prefab)
        scale = "1.000"
    else:
        icon = ('<game_entity name="map_icon" old_prefab_name="" mobility="1">\n'
                '\t\t\t\t\t\t\t<components>\n'
                '\t\t\t\t\t\t\t\t<meta_mesh_component name="%s"/>\n'
                '\t\t\t\t\t\t\t</components>\n'
                '\t\t\t\t\t\t</game_entity>' % mesh)
    return f'''
		<game_entity name="campaign_icon_capsule_{idx:03d}_{sid}" old_prefab_name="" mobility="1">
			<transform position="{row['u']:.1f}, {row['v']:.1f}, {z:.3f}" rotation_euler="0.000, 0.000, 0.000"/>
			<scripts>
				<script name="Town Entity Manager">
					<variables>
						<variable name="Override Factor Color" value="false"/>
						<variable name="Factor Color" value="1.000, 1.000, 1.000, 1.000"/>
					</variables>
				</script>
			</scripts>
			<children>
				<game_entity name="{sid}" old_prefab_name="" mobility="1">
					<tags>
						<tag name="{tag}"/>
					</tags>
					<transform rotation_euler="0.000, 0.000, 0.000" scale="{scale}, {scale}, 1.000"/>
					<children>
						{icon}
						<game_entity name="bo_town" old_prefab_name="bo_town" mobility="1">
							<tags>
								<tag name="bo_town"/>
							</tags>
							<transform position="0.000, 0.000, 0.000" rotation_euler="0.000, 0.000, 0.000" scale="{bo_scale}, {bo_scale}, {bo_scale}"/>
							<physics shape="bo_sphere_collider">
								<body_flags>
									<body_flag name="only_collide_with_raycast"/>
								</body_flags>
							</physics>
						</game_entity>
						<game_entity name="gate_position" old_prefab_name="gate_position" mobility="1">
							<tags>
								<tag name="main_map_city_gate"/>
							</tags>
							<transform position="0.000, -2.000, 0.000" rotation_euler="0.000, 0.000, 0.000"/>
						</game_entity>
						<game_entity name="town_circle_decal" old_prefab_name="town_circle_decal" mobility="1">
							<tags>
								<tag name="map_settlement_circle"/>
							</tags>
							<transform position="0.000, 0.000, 2.600" rotation_euler="0.000, 0.000, 0.000" scale="{circle}, {circle}, 1.151"/>
							<components>
								<decal_component material="decal_city_circle_a" argument="1.000, 1.000, 0.000, 0.000"/>
							</components>
						</game_entity>
					</children>
				</game_entity>
			</children>
		</game_entity>
'''


def main():
    os.makedirs(DST, exist_ok=True)
    for name in SCENE_FILES:
        s = os.path.join(SRC, name)
        d = os.path.join(DST, name)
        if os.path.isfile(s) and not os.path.exists(d):
            shutil.copy2(s, d)
            print("copied", name)

    src_scene = os.path.join(SRC, "scene.xscene")
    tree = ET.parse(src_scene)
    root = tree.getroot()
    ents = root.find("entities")
    if ents is None:
        raise RuntimeError("no <entities> in source scene")

    removed = 0
    for e in list(ents):
        n = e.get("name") or ""
        if n.startswith("campaign_icon_capsule_"):
            ents.remove(e)
            removed += 1
    print("removed vanilla settlement capsules:", removed)

    # 相机边界 = 地形 node_dimension × node_size（范围读 terrain_meta.json，不写死）
    _max_pos = "%.3f, %.3f, 1000.000" % (rdr2_world_meta.map_w(), rdr2_world_meta.map_h())
    for e in list(ents):
        n = e.get("name")
        if n in ("border_min", "border_max"):
            pos = "0.000, 0.000, 0.000" if n == "border_min" else _max_pos
            tr = e.find("transform")
            if tr is None:
                tr = ET.SubElement(e, "transform")
            tr.set("position", pos)
            tr.set("rotation_euler", "0.000, 0.000, 0.000")
            print("border", n, "->", pos)

    # 全局水位：大地图只有一个海平面，地形侧已按 B 方案处理（陆地垫到 ≥0.5 米、低洼水体挖到 −2 米）
    wp = root.find("environment_properties/water_properties")
    if wp is not None:
        for p in wp.iter("property"):
            if p.get("name") == "water_exists":
                p.set("value", "true")
            elif p.get("name") == "water_level":
                p.set("value", "%.3f" % rdr2_world_meta.water_level_units())
        print("water: exists=true level=%.3f units" % rdr2_world_meta.water_level_units())

    world, _ = W.load_world()
    hm = load_height()
    if hm is None:
        print("WARN: heightmap not found, settlements will sit at z=3.0")
    for i, row in enumerate(world):
        z = terrain_z(hm, row["u"], row["v"]) + 0.05
        frag = capsule_xml(i, row, z)
        ents.append(ET.fromstring(frag))
    print("added RD2 settlement capsules:", len(world))

    # 保持 scene 根节点名不变（引擎按 Main_map 找）
    out = os.path.join(DST, "scene.xscene")

    # 已经烘过 RDR2 地形（apply_rdr2_terrain.py 留了标记）：把现有 <terrain> 原样带过来，
    # 别用原版卡拉迪亚地形覆盖掉（重跑本脚本只为重排据点实体）
    marker = os.path.join(DST, ".rdr2_terrain_baked")
    if os.path.isfile(marker) and os.path.isfile(out):
        baked = ET.parse(out).getroot().find("terrain")
        if baked is not None:
            cur = root.find("terrain")
            idx = list(root).index(cur)
            root.remove(cur)
            root.insert(idx, copy.deepcopy(baked))
            print("kept baked RDR2 terrain (marker present)")

    tree.write(out, encoding="utf-8", xml_declaration=True)
    print("wrote", out, os.path.getsize(out), "bytes")


if __name__ == "__main__":
    main()
