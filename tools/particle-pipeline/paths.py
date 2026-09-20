#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""paths.py —— 两根定位。本工具链**唯一允许出现绝对路径的地方**。

    TOOL = 代码根（在游戏仓库里，进 git）    <repo>/tools/particle-pipeline/
    DATA = 数据根（D 盘，体量大不进 git）     D:/BrainMaker/骑砍2粒子特效复刻/

两者靠 **junction** 连成一份（见 tools/particle-pipeline/README.md §1）：
    D:\\BrainMaker\\骑砍2粒子特效复刻\\pipeline  ->  TOOL/pipeline   （junction）
    D:\\BrainMaker\\骑砍2粒子特效复刻\\preview   ->  TOOL/preview    （junction）

所以每个脚本都有两个入口，两边都必须能跑：
    cd D:/BrainMaker/骑砍2粒子特效复刻 && python pipeline/t3d_parse.py   ← 数据侧入口
    python <repo>/tools/particle-pipeline/pipeline/t3d_parse.py          ← 仓库侧入口

🔴 关键实现细节：用 `realpath` 而不是 `abspath`。
   `realpath` 会**穿透 junction** 拿到真实位置（2026-09-20 实测：
   `os.path.realpath("<junction>/a.txt")` 返回的是被指向的真实路径）；
   `abspath` 只会原样保留 junction 路径，于是「从 D 盘入口跑」时
   TOOL 会解析成 D 盘那个假目录，找不到 gen_particle_effect.py。

数据根可用环境变量 `BM_PARTICLE_ROOT` 覆盖（默认即上面那个 D 盘路径）；
换台机器/换盘符时只改这一处，或设这个环境变量，不用改任何脚本。
"""
import os

# 代码根：本文件就在 <TOOL>/paths.py，往上一级即 TOOL
TOOL = os.path.dirname(os.path.realpath(__file__))

# 数据根：D 盘那个「来料加工坊」，所有 output/ 产物与 288MB T3D、441MB 贴图都在那边
DATA = os.environ.get("BM_PARTICLE_ROOT") or r"D:/BrainMaker/骑砍2粒子特效复刻"

# 生成器：spec -> 骑砍 particle XML（格式权威，被 ue2bannerlord.py 当子进程调用）
GEN = os.path.join(TOOL, "gen_particle_effect.py")


def out(*parts):
    """数据根下 output/ 里的路径：out("xml") -> D:/…/output/xml"""
    return os.path.join(DATA, "output", *parts)


# UE 源贴图目录（262 张 VFX PNG，441MB，umodel 从 UE 工程导出）
#   —— 预览器 `--tex-dir` 的默认值；换 UE 工程/换目录改这一行，或命令行显式传 --tex-dir
TEX_DIR = out("tex", "FlexibleCombatSystem", "VFX", "Textures")
