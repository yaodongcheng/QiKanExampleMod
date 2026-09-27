#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""重刷「飞行·合成」查看器第③格（合成后的动画）+ 底部条目清单。

查看器 datasets/ue_fly_comp 三格：①源 pose(UE小白人) ②重定向后 pose(骑砍2) ③合成后的动画。
①② 直接用 output/glb/ue_flight/ 里那两份 130 段 GLB（同名 clip），**不用重烘**。
只有 ③ 需要本脚本：把 output/trf 里的合成件直读烘成 comp.glb；同时写 clips.json 收窄底栏。

用法（在 D:/BrainMaker/骑砍2动画重定向 下）：
    python pipeline/common/build_fly_comp_viewer.py          # 用下面 PAIRS 的表
    python pipeline/common/build_fly_comp_viewer.py --dry    # 只看会跑什么

PAIRS 里第 1 项是合成件名（output/trf/<名>.trf），不存在会**跳过并打印** —— 合成完跑一次即可。
PAIRS 同时被 compose_fly.py 复用（同一张表，别在两处各写一份）。
"""
import io
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
BLENDER = r"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe"
# 🔴 基底必须和第②格（output/glb/ue_flight/bannerlord_flight.glb）同一个，否则两格的"人"不一样：
#    human_lod_4.fbx = 整身低模(236 顶点，含头) ｜ body_male_a.fbx = 精细体但【无头】(1586 顶点)。
BASE_FBX = os.path.join(ROOT, "input/target/bannerlord/human_lod_4.fbx")
TRFDIR = os.path.join(ROOT, "output/trf")
OUT_GLB = os.path.join(ROOT, "viewer/datasets/ue_fly_comp/assets/comp.glb")

HM = "fly_A_Flight_HoverMove_A"
FM = "fly_A_Flight_FastMove_A"
# 🔴 命名口径（2026-09-27 用户裁定）：**合成件叫 Add<方向>，不许叫 Lean/Pitch** ——
#    Lean / Pitch 是"观感"，来源族会串（HoverMove 的 _L_Add 和 HoverLean 的 _L_Add 都被叫成过 Lean）。
#    合成件 = 基础动画名 + "_Add" + 方向：
#      fly_A_Flight_HoverMove_A_L_Add -> fly_A_Flight_HoverMove_A_AddL
#      fly_A_FM_A_Lean_L              -> fly_A_Flight_FastMove_A_AddL
# (合成件名, 查看器里的 clip 名 = 源 Add 名, 基础动画名, 根骨(root)增量缩放)
# 🔴 root-scale 一律 1.0（2026-09-27 修完乘法顺序后定）：修好后，合成件在参照帧上
#    **逐骨等于那条 Add 本身**（实测 ≤0.18°，只剩 6 位小数舍入）—— 这才是加性动画该有的样子。
#    旧口径 0（摘掉根骨那道量）已经作废：它会把 Add 的大头（pelvis 0~90°）丢掉，
#    合成结果跟那张 Add 完全不是一回事（用户当场看图指出"不符合预期"）。
PAIRS = [
    (HM + "_AddL", "A_Flight_HoverMove_A_L_Add", HM, 1.0),
    (HM + "_AddR", "A_Flight_HoverMove_A_R_Add", HM, 1.0),
    (HM + "_AddF", "A_Flight_HoverMove_A_F_Add", HM, 1.0),
    (HM + "_AddB", "A_Flight_HoverMove_A_B_Add", HM, 1.0),
    (HM + "_AddD", "A_Flight_HoverMove_A_D_Add", HM, 1.0),
    (HM + "_AddU", "A_Flight_HoverMove_A_U_Add", HM, 1.0),
    ("fly_A_Flight_HoverLean_A_AddL", "A_Flight_HoverLean_A_L_Add", HM, 1.0),
    ("fly_A_Flight_HoverLean_A_AddR", "A_Flight_HoverLean_A_R_Add", HM, 1.0),
    (FM + "_AddL", "A_FM_A_Lean_L", FM, 1.0),
    (FM + "_AddR", "A_FM_A_Lean_R", FM, 1.0),
    (FM + "_AddU", "A_FM_A_Pose_U", FM, 1.0),
    (FM + "_AddD", "A_FM_A_Pose_D", FM, 1.0),
]


def write_clips_manifest(pairs):
    """底部条目清单：①②两格引用的 GLB 各有 130 条，不收窄底栏就会列出全部 130 条。"""
    dsdir = os.path.dirname(os.path.dirname(OUT_GLB))
    clips_json = os.path.join(dsdir, "clips.json")
    ds_json = os.path.join(dsdir, "dataset.json")
    man = {"clips": {}}
    for comp, clip, base, _r in pairs:
        man["clips"][clip] = {"prio": 0, "weapon": "", "func": "合成",
                              "desc": "%s = %s + fly_%s" % (comp, base, clip)}
    io.open(clips_json, "w", encoding="utf-8", newline="\n").write(
        json.dumps(man, ensure_ascii=False, indent=2) + "\n")
    if os.path.isfile(ds_json):
        cfg = json.load(io.open(ds_json, encoding="utf-8"))
        if cfg.get("clipsFile") != "clips.json":
            cfg["clipsFile"] = "clips.json"
            io.open(ds_json, "w", encoding="utf-8", newline="\n").write(
                json.dumps(cfg, ensure_ascii=False, indent=2) + "\n")
    return clips_json, len(man["clips"])


def build(dry=False):
    ok = [x for x in PAIRS if os.path.isfile(os.path.join(TRFDIR, x[0] + ".trf"))]
    miss = [x for x in PAIRS if not os.path.isfile(os.path.join(TRFDIR, x[0] + ".trf"))]
    if miss:
        print("!! 这些合成件在 output/trf 里没有，会跳过：")
        for c, _n, _b in miss:
            print("   ", c)
    if not ok:
        print("没有可烘的 —— 先合成。")
        return 1
    clips = ",".join("%s=%s" % (c, n) for c, n, _b, _r in ok)
    cmd = [BLENDER, "-b", "--factory-startup", "--python",
           os.path.join(HERE, "glb_pack_retargeted.py"), "--",
           "--base", BASE_FBX, "--trfdir", TRFDIR, "--clips", clips,
           "--out", OUT_GLB, "--fps", "30"]
    print("烘 %d 条 -> %s" % (len(ok), OUT_GLB))
    if not dry:
        p, n = write_clips_manifest(ok)
        print("底部条目清单 -> %s（%d 条）" % (p, n))
    if dry:
        print(" ".join(cmd))
        return 0
    return subprocess.run(cmd).returncode


if __name__ == "__main__":
    sys.exit(build(dry="--dry" in sys.argv))
