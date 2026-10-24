#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""RD2 大地图场景骨架生成器（第一步：先用原版 Main_map 地形占位）

做三件事：
  1. 把原版 SandBox/SceneObj/Main_map 整目录拷到 RD2/SceneObj/Main_map（地形/植被/大气/寻路占位）；
  2. 删掉原版所有据点胶囊（campaign_icon_capsule_*）；
  3. 按 RDR2 据点表生成 46 个据点胶囊（位置 = 848 制 u/v）。

地形替换（RDR2 真实地形）是下一步 ModKit 手工导入的事，本脚本不动 terrain.bin。
生成物禁止手改；改内容 = 改本脚本 / 改数据源，然后重跑。
"""

import io
import os
import shutil
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import gen_rdr2_world as W

SRC = os.path.join(W.MODULES, "SandBox", "SceneObj", "Main_map")
DST = os.path.join(W.MODULE, "SceneObj", "Main_map")
SCENE_FILES = ("terrain.bin", "navmesh.bin", "atmosphere.xml", "flora.bin", "references.txt")


def capsule_xml(idx, row):
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
    return f'''
		<game_entity name="campaign_icon_capsule_{idx:03d}_{sid}" old_prefab_name="" mobility="1">
			<transform position="{row['u']:.1f}, {row['v']:.1f}, 3.000" rotation_euler="0.000, 0.000, 0.000"/>
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
						<game_entity name="map_icon" old_prefab_name="" mobility="1">
							<components>
								<meta_mesh_component name="{mesh}"/>
							</components>
						</game_entity>
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

    # 相机边界 = 地形 node_dimension × node_size（848×848；原版卡拉迪亚的值是 62..790，不适合我们）
    for e in list(ents):
        n = e.get("name")
        if n in ("border_min", "border_max"):
            pos = "0.000, 0.000, 0.000" if n == "border_min" else "848.000, 848.000, 1000.000"
            tr = e.find("transform")
            if tr is None:
                tr = ET.SubElement(e, "transform")
            tr.set("position", pos)
            tr.set("rotation_euler", "0.000, 0.000, 0.000")
            print("border", n, "->", pos)

    world, _ = W.load_world()
    for i, row in enumerate(world):
        frag = capsule_xml(i, row)
        ents.append(ET.fromstring(frag))
    print("added RD2 settlement capsules:", len(world))

    # 保持 scene 根节点名不变（引擎按 Main_map 找）
    out = os.path.join(DST, "scene.xscene")
    tree.write(out, encoding="utf-8", xml_declaration=True)
    print("wrote", out, os.path.getsize(out), "bytes")


if __name__ == "__main__":
    main()
