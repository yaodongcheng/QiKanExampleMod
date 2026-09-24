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


# ---------------------------------------------------------------- UE 侧 T3D 数据源（2026-09-24 合并）
# 🔴 导出与解析都归 **tools/ue-dissect/**（唯一 UE 底座）：
#    · 导出：tools/ue-dissect/export_t3d.py（全量，超集）
#    · 解析底座：tools/ue-dissect/t3d_tools.py（两条管线共用）
# 本管线的 pipeline/export_t3d_all.py 已停止维护，仅为历史记录保留。
REPO = os.path.dirname(os.path.dirname(TOOL))                    # <repo>（realpath 已穿透 junction）
UE_DISSECT_TOOL = os.path.join(REPO, "tools", "ue-dissect")
UE_DISSECT_DUMP = os.environ.get("BM_UE_DISSECT_DUMP") or os.path.join(
    REPO, "Debug", "offline", "fcs_dump", "out")


def t3d_src():
    """T3D 数据源：优先用 ue-dissect 的全量导出（含 Cascade，是超集），
    没有就退回本管线自己的 out("t3d")。显式指定：BM_T3D_SRC 或命令行第一参数。

    ⚠️ 2026-09-24 起 `output/t3d/` 已删（合并到 ue-dissect），所以两条都不在会**显式报错**，
    而不是静默返回一个不存在的目录（否则症状是"解析出 0 个文件"，很难查）。"""
    for cand in (os.environ.get("BM_T3D_SRC"),
                 os.path.join(UE_DISSECT_DUMP, "t3d", "vfx"),
                 out("t3d")):
        if cand and os.path.isdir(cand) and any(f.endswith(".t3d") for f in os.listdir(cand)):
            return cand
    raise SystemExit(
        "找不到 T3D 数据源。依次试过：\n"
        "  1) BM_T3D_SRC 环境变量        = %s\n"
        "  2) ue-dissect 的 dump          = %s\n"
        "  3) 本管线自己的 output/t3d     = %s\n"
        "修法：跑 tools/ue-dissect/export_t3d.py（UE 无头导出），或设 BM_T3D_SRC 指向已有的 T3D 目录。"
        % (os.environ.get("BM_T3D_SRC") or "（未设）",
           os.path.join(UE_DISSECT_DUMP, "t3d", "vfx"), out("t3d")))


def asset_id_map():
    """ue-dissect 的资产清单 → {资产名: 旧扁平 id}，用来保持 parsed JSON 文件名稳定。
    旧 id 形态：FlexibleCombatSystem__VFX__<路径>__<类名>（粒子管线历史约定）。"""
    import json
    p = os.path.join(UE_DISSECT_DUMP, "01_inventory.json")
    m = {}
    if not os.path.isfile(p):
        return m
    try:
        for r in json.load(open(p, encoding="utf-8")):
            path, name, cls = r.get("path", ""), r.get("name"), r.get("class")
            if path.startswith("/Game/") and cls in ("NiagaraSystem", "ParticleSystem", "NiagaraEmitter"):
                m.setdefault(name, path[len("/Game/"):].replace("/", "__") + "__" + cls)
    except Exception:
        pass
    return m


# UE 源贴图目录（262 张 VFX PNG，441MB，umodel 从 UE 工程导出）
#   —— 预览器 `--tex-dir` 的默认值；换 UE 工程/换目录改这一行，或命令行显式传 --tex-dir
TEX_DIR = out("tex", "FlexibleCombatSystem", "VFX", "Textures")
