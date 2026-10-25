#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把烘焙好的图标（RMDL + 图集）装成游戏资产：克隆材质 → 换图集 → 出网格包 → 装两个模块 → 重生成场景。

用法：
  python Scripts/install_rdr2_city_icon.py \
      --mdl   Debug/RD2构造相关/icon_blend/rdr2_m_sdn.mdl \
      --atlas Debug/RD2构造相关/icon_blend/atlas_d.png \
      --name  rdr2_m_sdn
"""
import argparse
import json
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
MODULES = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules"
LWN = os.path.join(MODULES, "LivingWorldNpcs")
TPACCLI = os.path.join(LWN, "tools", "tpactool", "TpacToolCLI", "bin", "Release", "net9.0", "tpaccli.exe")
# 直接指原目录（铁律 39：禁止硬链接造影子文件；tpaccli 扫全目录约 1 分钟）
PACKDIR = os.path.join(MODULES, "Native", "AssetPackages")
WORK = os.path.join(LWN, "Debug", "RD2构造相关", "icon_install")

TEMPLATE_SRC = "mi_emp_keep_1"
TEMPLATE_DST = "rdr2_m_city"
TEX_NAME = "rdr2_citymap_icon_empire_city_01_d"


def run(cmd):
    print(">>", os.path.basename(cmd[0]), *cmd[1:])
    if subprocess.run(cmd).returncode != 0:
        sys.exit("FAILED: %s" % cmd[0])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mdl", required=True)
    ap.add_argument("--atlas", required=True)
    ap.add_argument("--name", default="rdr2_m_sdn")
    args = ap.parse_args()

    os.makedirs(WORK, exist_ok=True)
    clone = os.path.join(WORK, "rdr2_city_atlas.tpac")
    if os.path.isfile(clone):
        os.remove(clone)

    run([TPACCLI, "assetclone", "--packdir", PACKDIR, "--src", TEMPLATE_SRC,
         "--newname", TEMPLATE_DST, "--srctype", "metamesh",
         "--depprefix", "rdr2_city", "--out", WORK, "--packname", "rdr2_city_atlas.tpac"])

    atlas = os.path.abspath(args.atlas)
    from PIL import Image
    w, h = Image.open(atlas).size
    manifest = {"packs": [{"packName": "rdr2_city_atlas",
                           "textures": [{"name": TEX_NAME, "png": atlas.replace("\\", "/"),
                                         "width": w, "height": h}]}]}
    mpath = os.path.join(WORK, "atlas_manifest.json")
    json.dump(manifest, open(mpath, "w", encoding="utf-8"), indent=2)

    run([TPACCLI, "texreplace", "--packdir", WORK, "--filter", "rdr2_city_atlas",
         "--mapping", mpath, "--out", WORK])

    run([TPACCLI, "meshmake", "--packdir", WORK, "--src", TEMPLATE_DST,
         "--mdl", os.path.abspath(args.mdl), "--newname", args.name,
         "--lods", "0,1,2,3", "--out", WORK, "--packname", "rdr2_map_icons.tpac"])

    for mod in ("RD2", "RD2Map"):
        dst = os.path.join(MODULES, mod, "AssetPackages")
        os.makedirs(dst, exist_ok=True)
        for pack in ("rdr2_city_atlas.tpac", "rdr2_map_icons.tpac"):
            shutil.copy2(os.path.join(WORK, pack), os.path.join(dst, pack))
            print("copied", pack, "->", mod)

    run([sys.executable, os.path.join(HERE, "gen_rdr2_map_scene.py")])
    run([sys.executable, os.path.join(HERE, "gen_rdr2_modkit_map.py")])
    print("installed, mesh name =", args.name)


if __name__ == "__main__":
    main()
