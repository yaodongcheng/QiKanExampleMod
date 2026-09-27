#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""重刷「飞行·合成」查看器第③格（合成后的动画）。

查看器 datasets/ue_fly_comp 三格：①源 pose(UE小白人) ②重定向后 pose(骑砍2) ③合成后的动画。
①② 直接用 output/glb/ue_flight/ 里那两份 130 段 GLB（同名 clip），**不用重烘**。
只有 ③ 需要本脚本：把 output/trf 里的合成件直读烘成 comp.glb。

用法（在 D:/BrainMaker/骑砍2动画重定向 下）：
    python pipeline/common/build_fly_comp_viewer.py          # 用下面 PAIRS 的表
    python pipeline/common/build_fly_comp_viewer.py --dry    # 只看会跑什么

PAIRS 里第 1 项是合成件名（output/trf/<名>.trf），不存在会**跳过并打印** —— 合成完跑一次即可。
PAIRS 同时被 compose_fly.py 复用（同一张表，别在两处各写一份）。
"""
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
BLENDER = r"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe"
BASE_FBX = os.path.join(ROOT, "input/target/bannerlord/body/body_male_a.fbx")
TRFDIR = os.path.join(ROOT, "output/trf")
OUT_GLB = os.path.join(ROOT, "viewer/datasets/ue_fly_comp/assets/comp.glb")

HM = "fly_A_Flight_HoverMove_A"
FM = "fly_A_Flight_FastMove_A"
# (合成件名, 查看器里的 clip 名 = 源 Add 名, 基础动画名)
PAIRS = [
    (HM + "_LeanL",     "A_Flight_HoverMove_A_L_Add", HM),
    (HM + "_LeanR",     "A_Flight_HoverMove_A_R_Add", HM),
    (HM + "_PitchF",    "A_Flight_HoverMove_A_F_Add", HM),
    (HM + "_PitchB",    "A_Flight_HoverMove_A_B_Add", HM),
    (HM + "_PitchD",    "A_Flight_HoverMove_A_D_Add", HM),
    (HM + "_PitchU",    "A_Flight_HoverMove_A_U_Add", HM),
    (HM + "_HoverLeanL", "A_Flight_HoverLean_A_L_Add", HM),
    (HM + "_HoverLeanR", "A_Flight_HoverLean_A_R_Add", HM),
    (FM + "_LeanL",     "A_FM_A_Lean_L", FM),
    (FM + "_LeanR",     "A_FM_A_Lean_R", FM),
    (FM + "_PitchU",    "A_FM_A_Pose_U", FM),
    (FM + "_PitchD",    "A_FM_A_Pose_D", FM),
]


def build(dry=False):
    pairs, miss = [], []
    for comp, clip, _base in PAIRS:
        (pairs if os.path.isfile(os.path.join(TRFDIR, comp + ".trf")) else miss).append((comp, clip))
    if miss:
        print("!! 这些合成件在 output/trf 里没有，会跳过：")
        for m in miss:
            print("   ", m[0])
    if not pairs:
        print("没有可烘的 —— 先合成。")
        return 1
    clips = ",".join("%s=%s" % (c, n) for c, n in pairs)
    cmd = [BLENDER, "-b", "--factory-startup", "--python",
           os.path.join(HERE, "glb_pack_retargeted.py"), "--",
           "--base", BASE_FBX, "--trfdir", TRFDIR, "--clips", clips,
           "--out", OUT_GLB, "--fps", "30"]
    print("烘 %d 条 -> %s" % (len(pairs), OUT_GLB))
    if dry:
        print(" ".join(cmd))
        return 0
    return subprocess.run(cmd).returncode


if __name__ == "__main__":
    sys.exit(build(dry="--dry" in sys.argv))
