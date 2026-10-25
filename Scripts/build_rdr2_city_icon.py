#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""圣丹尼斯大地图图标：一条命令从原始解包数据跑到内容包（可重复跑）。

链路（每一步都是幂等的）：
  1. gen_rdr2_city_icon.py        实例 → 体块网格 RMDL1 + 图集 PNG + 清单
  2. tpaccli assetclone          克隆原版地图图标资产（metamesh + 材质 + 贴图）→ 我们的命名
  3. tpaccli texreplace          把图集塞进克隆出来的贴图
  4. tpaccli meshmake --lods 0,1,2,3   几何换进克隆外壳，出 4 档 LOD
  5. 拷两个包进 RD2 / RD2Map 的 AssetPackages
  6. 重跑两个场景生成器          把 prefab 挂到 sdn 据点 / 摆进沙箱预览

前置：直接读 Modules/Native/AssetPackages（不复制、不建硬链接，铁律 39）——需要 map_icons / map_icon_parts /
materials / _shared / pack_4 / core / core_game）。缺了按 gen 脚本顶部注释重建。
"""

import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

MODULES = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules"
LWN = os.path.join(MODULES, "LivingWorldNpcs")
ICON = os.path.join(LWN, "Debug", "RD2构造相关", "icon_sdn")
BUILD = os.path.join(ICON, "build")
# 直接指原目录（铁律 39：禁止硬链接造影子文件；tpaccli 扫全目录约 1 分钟）
PACKDIR = os.path.join(MODULES, "Native", "AssetPackages")
TPACCLI = os.path.join(LWN, "tools", "tpactool", "TpacToolCLI", "bin", "Release", "net9.0", "tpaccli.exe")

CODE = "sdn"
TEMPLATE_SRC = "mi_emp_keep_1"
TEMPLATE_DST = "rdr2_m_city"


def run(cmd):
    print(">>", " ".join(os.path.basename(c) if i == 0 else c for i, c in enumerate(cmd)))
    r = subprocess.run(cmd)
    if r.returncode != 0:
        sys.exit("FAILED: %s" % cmd[0])


def main():
    if not os.path.isdir(PACKDIR):
        sys.exit("missing %s -- 见 Scripts/gen_rdr2_city_icon.py 顶部说明" % PACKDIR)

    run([sys.executable, os.path.join(HERE, "gen_rdr2_city_icon.py"), "--code", CODE])

    os.makedirs(BUILD, exist_ok=True)
    clone = os.path.join(BUILD, "rdr2_city_atlas.tpac")
    if os.path.isfile(clone):
        os.remove(clone)

    run([TPACCLI, "assetclone", "--packdir", PACKDIR, "--src", TEMPLATE_SRC,
         "--newname", TEMPLATE_DST, "--srctype", "metamesh",
         "--depprefix", "rdr2_city", "--out", BUILD, "--packname", "rdr2_city_atlas.tpac"])

    run([TPACCLI, "texreplace", "--packdir", BUILD, "--filter", "rdr2_city_atlas",
         "--mapping", os.path.join(ICON, "atlas_manifest.json"), "--out", BUILD])

    run([TPACCLI, "meshmake", "--packdir", BUILD, "--src", TEMPLATE_DST,
         "--mdl", os.path.join(ICON, "rdr2_m_%s.mdl" % CODE),
         "--newname", "rdr2_m_%s" % CODE, "--lods", "0,1,2,3",
         "--out", BUILD, "--packname", "rdr2_map_icons.tpac"])

    for mod in ("RD2", "RD2Map"):
        dst = os.path.join(MODULES, mod, "AssetPackages")
        os.makedirs(dst, exist_ok=True)
        for pack in ("rdr2_city_atlas.tpac", "rdr2_map_icons.tpac"):
            shutil.copy2(os.path.join(BUILD, pack), os.path.join(dst, pack))
            print("copied", pack, "->", mod)

    run([sys.executable, os.path.join(HERE, "gen_rdr2_map_scene.py")])
    run([sys.executable, os.path.join(HERE, "gen_rdr2_modkit_map.py")])
    print("done")


if __name__ == "__main__":
    main()
