# -*- coding: utf-8 -*-
"""UE 工程拆解工具链 —— 路径配置（唯一需要改的地方）。

三种改法，优先级从高到低：
  1) 环境变量：UE_PROJECT / UE_ENGINE / UE_DUMP / UE_MIRROR / UE_CONTENT_SUBDIR
  2) 直接改本文件的默认值
  3) 命令行 --out / --mirror 覆盖（支持的工具）

默认值 = 本仓库当前在拆的这套（FCS / UE4.27）。
"""
import os

# UE 工程（.uproject 全路径）—— 无头编辑器要加载它才能导出资产
UE_PROJECT = os.environ.get(
    "UE_PROJECT",
    r"D:/UEProjects/【UE5】FlexibleCombatSystem/FlexibleCombatSystem.uproject")

# UE 编辑器命令行（版本要能打开该工程；本例 UE4.27）
UE_ENGINE = os.environ.get(
    "UE_ENGINE",
    r"D:/UNREAL/UE_4.27/Engine/Binaries/Win64/UE4Editor-Cmd.exe")

# 产物根（T3D 原文 / 解析 JSON / CSV —— 离线产物，按仓库规则放 Debug/offline）
_DEFAULT_DUMP = (r"H:/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord/"
                 r"Modules/LivingWorldNpcs/Debug/offline/fcs_dump/out")
DUMP_ROOT = os.environ.get("UE_DUMP", _DEFAULT_DUMP)

# 人读详情树的落点（镜像工程目录结构）
_DEFAULT_MIRROR = (r"H:/SteamLibrary/steamapps/common/Mount & Blade II Bannerlord/"
                   r"Modules/LivingWorldNpcs/Knowledge/FCS详情解析")
MIRROR_ROOT = os.environ.get("UE_MIRROR", _DEFAULT_MIRROR)

# /Game 下要镜像/解析的子目录（资产树起点）
CONTENT_SUBDIR = os.environ.get("UE_CONTENT_SUBDIR", "FlexibleCombatSystem")

# 产物子目录（相对 DUMP_ROOT）
T3D_DIR = os.path.join(DUMP_ROOT, "t3d")          # 按资产类型分：bp/ cdo/ dt/ anim/ struct/ enum/ vfx/ mat/ misc/ level/
DIGEST_DIR = os.path.join(DUMP_ROOT, "digest")    # 结构化摘要 + PSEUDOCODE/
TABLES_DIR = os.path.join(DUMP_ROOT, "tables")    # 数据表 CSV
INVENTORY = os.path.join(DUMP_ROOT, "01_inventory.json")
DT_CELLS = os.path.join(DUMP_ROOT, "13_dtcells.json")
DT_COLUMNS = os.path.join(DUMP_ROOT, "12_dt_columns.json")


def show():
    print("UE_PROJECT     =", UE_PROJECT)
    print("UE_ENGINE      =", UE_ENGINE)
    print("DUMP_ROOT      =", DUMP_ROOT)
    print("MIRROR_ROOT    =", MIRROR_ROOT)
    print("CONTENT_SUBDIR =", CONTENT_SUBDIR)


if __name__ == "__main__":
    show()
