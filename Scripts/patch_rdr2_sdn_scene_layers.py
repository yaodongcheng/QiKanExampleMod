#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""给 rdr2_sanit_denis 场景配好四个地形层 + 全局水（离线改 scene.xscene）。

背景：
  · 用户在 ModKit 里建了四个层（grass / water / mud / rock），但只是**起了名字**，
    贴图还是模板的 desert_a，节点层掩码也还是 1（只渲染第 0 层）。
  · 本脚本做四件事：
      ① `<layers>` 整段重写成四个层（grass / water / mud / rock），贴图取原版实证可用的组合；
      ② 每个节点的 layer_is_used_mask_* 改成 15（4 层全开）；weightmap_levels 0..3 = 8；
      ③ `<water_properties>` 改成圣丹尼斯的全局水（水位 40.5 m / 泥色河水）；
      ④ 写前备份一次 scene.xscene.layers_bak。

口径依据：
  · 层贴图组合抄自原版场景（battania_castle_a 的 grass/rock、battania_town_b 的 mud、
    battle_terrain_biome_048 的 water）——diffuse/normal/specular/displacement 成对存在。
  · 水位 40.5 m、水材质 wat_naval_light_brown_river_g：见
    `export/world/water_level_map_2lv.npy` 与原版 aserai_village_*_navalraid 场景。

用法：
  python Scripts/patch_rdr2_sdn_scene_layers.py
  python Scripts/patch_rdr2_sdn_scene_layers.py --scene "<别的 scene.xscene>" --dry-run
"""
import argparse
import io
import os
import re
import shutil
import sys

DEFAULT_SCENE = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
                 r"\Mount & Blade II Bannerlord\Modules\RDR2\SceneObj\rdr2_sanit_denis\scene.xscene")

# 层定义：名字 / 缩放 / 物理材质 / (diffuse, normal, specular, displacement)
LAYERS = [
    ("grass", "4.000, 4.000", "grass",
     ("ground_grass_b_d", "ground_grass_b_n", "ground_grass_b_s", "ground_grass_b_h")),
    ("water", "5.000, 5.000", "water",
     ("water_ground", "none", "gloss_s", "none")),
    ("mud", "8.000, 8.000", "mud",
     ("ground_mud_a_d", "ground_mud_a_n", "none", "ground_mud_a_h")),
    ("rock", "15.000, 15.000", "stone",
     ("rock_cliff_b_d", "rock_cliff_b_n", "rock_cliff_b_s", "rock_cliff_b_h")),
]

WATER = {
    "water_level": "40.500",
    "water_strength": "0.600",
    "water_wind_dependency": "1.000",
    "water_material": "wat_river_clean_g",
    "water_shallow_color": "1.000, 1.000, 1.000",
    "water_deep_color": "1.000, 1.000, 1.000",
    "water_exists": "true",
    "place_water_probe": "true",
}

FLAGS = """
					<layer_flags>
						<flag name="use_parallax" value="false"/>
						<flag name="use_displacement_mapping" value="false"/>
						<flag name="use_randomized_normalmap" value="false"/>
						<flag name="use_transparency_of_diffuse_alpha" value="false"/>
						<flag name="use_transparency_of_ground_slope" value="false"/>
						<flag name="use_dxt5_normal" value="false"/>
						<flag name="use_six_side_uv_mapping" value="false"/>
					</layer_flags>
					<meshes/>"""


def layer_xml(name, scale, phys, tex):
    diffuse, normal, specular, disp = tex
    summer = (
        '\t\t\t<layer name="%s">\n'
        '\t\t\t\t<summer is_enabled="true" name="%s" is_flora_layer="false" is_mesh_blend_layer="false" '
        'pitch_roll_yaw="0.000, 0.000, 0.000" scale="%s" shear="0.000, 0.000" position_offset="0.000, 0.000" '
        'physics_material="%s" detail_level_adjustment="0" elevation_amount="0.000" parallax_amount="0.000" '
        'ground_slope_scale="1.000" bigdetailmap_mode="0" bigdetailmap_weight="0.000" '
        'albedo_factor_color="1.000, 1.000, 1.000" albedo_factor_mode="0" smooth_blend_amount="1.000" '
        'bigdetailmap_scale_x="0.080" bigdetailmap_scale_y="0.080" bigdetailmap_bias_x="0.080" bigdetailmap_bias_y="0.080">\n'
        '\t\t\t\t\t<textures>\n'
        '\t\t\t\t\t\t<texture type="diffusemap" name="%s"/>\n'
        '\t\t\t\t\t\t<texture type="areamap" name="none"/>\n'
        '\t\t\t\t\t\t<texture type="normalmap" name="%s"/>\n'
        '\t\t\t\t\t\t<texture type="specularmap" name="%s"/>\n'
        '\t\t\t\t\t\t<texture type="splattingmap" name="none"/>\n'
        '\t\t\t\t\t\t<texture type="displacementmap" name="%s"/>\n'
        '\t\t\t\t\t</textures>%s\n'
        '\t\t\t\t</summer>\n'
    ) % (name, name, scale, phys, diffuse, normal, specular, disp, FLAGS)
    thin = "".join(
        '\t\t\t\t<%s is_enabled="true" name="%s" is_flora_layer="false" is_mesh_blend_layer="false">\n'
        '\t\t\t\t\t<meshes/>\n'
        '\t\t\t\t</%s>\n' % (s, name, s) for s in ("fall", "winter", "spring"))
    return summer + thin + "\t\t\t</layer>"


def extract_base_layer(backup_path, name="desert_a"):
    """从备份里原样抠出第 0 层（默认地形材质）。

    🔴 第 0 层是 shader 的兜底层（`terrain_layer_index == 0` 恒定 weight=1.0），
       删掉它整片地表会渲染成黑色 —— 所以永远保留，四个新层排在它后面。
    """
    if not os.path.isfile(backup_path):
        return None
    t = io.open(backup_path, encoding="utf-8", errors="replace").read()
    m = re.search(r'[ \t]*<layer name="%s">.*?</layer>' % re.escape(name), t, re.S)
    return m.group(0).strip("\n") if m else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--scene", default=DEFAULT_SCENE)
    ap.add_argument("--base-layer", default="desert_a", help="第 0 层（默认地形材质）名字")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    path = args.scene
    if not os.path.isfile(path):
        sys.exit("scene not found: " + path)
    t = io.open(path, encoding="utf-8", errors="replace").read()
    orig = t

    # ① 层表整段替换（第 0 层保留原样，其后接四个新层）
    i = t.find("<layers")
    j = t.find("</layers>")
    if i < 0 or j < 0:
        sys.exit("no <layers> block")
    j += len("</layers>")
    blocks = []
    base = args.base_layer if args.base_layer else None
    if base:
        blk = extract_base_layer(path + ".layers_bak", base)
        if blk is None:
            sys.exit("base layer '%s' not found in %s.layers_bak -- refuse to drop layer 0"
                     % (base, path))
        blocks.append(blk)
        print("layer 0 kept -> %s" % base)
    blocks += [layer_xml(*L) for L in LAYERS]
    t = t[:i] + "<layers version=\"1\">\n" + "\n".join(blocks) + "\n\t\t</layers>" + t[j:]
    print("layers -> %s" % ", ".join(([base] if base else []) + [L[0] for L in LAYERS]))

    # ② 节点层掩码 + 权重层
    nlayers = len(LAYERS) + (1 if base else 0)
    mask = (1 << nlayers) - 1
    t, n1 = re.subn(r'layer_is_used_mask_(summer|fall|winter|spring)="\d+"',
                    lambda m: 'layer_is_used_mask_%s="%d"' % (m.group(1), mask), t)
    def wm(m):
        idx = int(m.group(1))
        val = 8 if idx < nlayers else 0
        return '<variable layer="%d" value="%d"/>' % (idx, val)
    t, n2 = re.subn(r'<variable layer="(\d+)" value="\d+"/>', wm, t)
    print("node masks -> %d 处改成 %d；weightmap 变量 %d 个" % (n1, mask, n2))

    # ③ 全局水
    def w(m):
        k = m.group(1)
        return '<property name="%s" value="%s"/>' % (k, WATER[k]) if k in WATER else m.group(0)
    t, n3 = re.subn(r'<property name="([a-z_]+)" value="[^"]*"/>', w, t)
    print("water props -> %d 行（其中改 %d 项）" % (n3, len(WATER)))

    if t == orig:
        print("no change")
        return
    if args.dry_run:
        print("dry-run, not written")
        return

    bak = path + ".layers_bak"
    if not os.path.isfile(bak):
        shutil.copy2(path, bak)
        print("backup ->", bak)
    with io.open(path, "w", encoding="utf-8", newline="") as f:
        f.write(t)
    print("written", path)


if __name__ == "__main__":
    main()
