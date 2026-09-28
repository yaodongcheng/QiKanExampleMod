#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""check_lr_consistency.py —— 「源素材 vs 解算动画」的**左右/上下一致性**确定性判据。

══ 为什么它是"视觉诊断"的必要搭档 ══════════════════════════════════════
  视觉模型看图诊断能抓"一眼看得出的崩坏"（上下翻转、正反面突变、穿模、关节反折），
  但**判"左右镜像"不可靠**：两只手都举着、高低差很小时，"谁更高"根本看不出来。
  实测踩过：视觉模型把 t=80%（双手举高的球门柱式）判成"疑似镜像"，数值一算
  **20/20 帧左右符号全一致**，是误报。
  ⇒ 固定要求：**视觉诊断 + 本脚本，两条一起过**（同 README §0.5「缺一条假绿」的纪律）。

══ 判据口径（三条，都要过）══════════════════════════════════════════════
  ① 符号一致率：源与动画的「手/肘/膝/踝 相对骨盆、在图像平面上的左右与上下符号」一致的帧占比
       ≥ 0.90 通过。**手贴中线的帧单独标出来**（|横向|<0.15 肩宽）—— 那些帧符号是病态的，不计入分母。
  ② 无系统性翻转：若"左右符号"整体反号占比高（≥0.8 且与 ① 相反）⇒ 判**整体镜像**（严重）
  ③ 上下不得整体反号：上下一致率 < 0.9 ⇒ 判**整体上下翻转**（严重）

  投影口径：屏幕右 = −X_arm，屏幕上 = +Z_arm（骨架 +Y 朝镜头/进 glTF 后朝 −Z）。
  度量：相对骨盆、按 **源肩宽** 归一的图像平面位置向量。

用法
    python pipeline/tools/check_lr_consistency.py --clip dance01 --trf pose_dance01 [--step 3]
退出码：0 = 通过；1 = 不通过（可直接当流水线闸门）
"""
import argparse
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scan_pose_metrics import build, fk, read_trf      # noqa: E402


def _root(p):
    for _ in range(6):
        if os.path.isdir(os.path.join(p, "pipeline")) and os.path.isdir(os.path.join(p, "input")):
            return p
        p = os.path.dirname(p)
    return os.path.dirname(p)


ROOT = _root(os.path.dirname(os.path.abspath(__file__)))
# 源 landmark  <->  目标骨（use_tail: 1=用骨尾 0=用骨头）
PAIRS = [("R腕", 16, "r_hand", 1), ("L腕", 15, "l_hand", 1),
         ("R肘", 14, "r_foretwist", 0), ("L肘", 13, "l_foretwist", 0),
         ("R膝", 26, "r_calf", 0), ("L膝", 25, "l_calf", 0),
         ("R踝", 28, "r_foot", 0), ("L踝", 27, "l_foot", 0)]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--clip", required=True, help="pose.json 名")
    ap.add_argument("--trf", required=True, help="TRF 名（不含 .trf）")
    ap.add_argument("--step", type=int, default=3)
    ap.add_argument("--midline", type=float, default=0.15,
                    help="横向小于该值(单位=源肩宽)的帧视为病态，不计入符号一致率分母")
    ap.add_argument("--skel-json", default=os.path.join(ROOT, "output", "verify", "bannerlord_skel.json"))
    a = ap.parse_args()

    P = json.load(open(os.path.join(ROOT, "input/source/pose_mediapipe", a.clip + ".json"), encoding="utf-8"))
    W = np.array(P["world"])
    skel = json.load(open(a.skel_json, encoding="utf-8"))
    bones, rl, invq = build(skel)
    names = [b["name"] for b in bones]
    R3 = np.array([b for b in bones if b["name"] == "pelvis"][0]["rest_local"], dtype=float)[:3, :3]
    R3i = np.linalg.inv(R3)
    _n, rots, pos = read_trf(os.path.join(ROOT, "output", "trf", a.trf + ".trf"))
    NT = len(rots[0])

    n_ok = n_bad = n_ill = 0
    n_lr_ok = n_lr_bad = 0
    n_ud_ok = n_ud_bad = 0
    worst = None
    rows = []
    for k in range(0, NT, a.step):
        ks = min(k, len(W) - 1)
        hipc = (W[ks][23] + W[ks][24]) / 2
        sw = float(np.linalg.norm(W[ks][11] - W[ks][12])) or 1.0
        q = {names[bi]: rots[bi][k][1] for bi in range(len(names))}
        p = fk(skel, rl, invq, q, R3i @ np.array(pos[k][1]), ["pelvis"],
               ["r_hand", "l_hand", "l_foretwist", "r_foretwist", "l_calf", "r_calf", "l_foot", "r_foot", "pelvis"])
        pv = p["pelvis"][0]
        devs = []
        frame_bad = False
        for tag, li, bn, ut in PAIRS:
            s = np.array([W[ks][li][0] - hipc[0], -(W[ks][li][1] - hipc[1])]) / sw
            o = p[bn][1] if ut else p[bn][0]
            d = o - pv
            ov = np.array([-float(d[0]), float(d[2])]) / sw
            devs.append(float(np.linalg.norm(s - ov)))
            # 🔴 判据**按每个肢体单独**算：该肢体横向贴中线时它的左右符号是病态的，跳过不计；
            #    上下方向不受此限（人不会把手挂在肩上，竖直方向不病态）。
            if abs(s[0]) > a.midline:
                if np.sign(s[0]) != np.sign(ov[0]):
                    n_lr_bad += 1; frame_bad = True
                else:
                    n_lr_ok += 1
            else:
                n_ill += 1
            if np.sign(s[1]) != np.sign(ov[1]):
                n_ud_bad += 1; frame_bad = True
            else:
                n_ud_ok += 1
        e = float(np.mean(devs))
        if worst is None or e > worst[0]:
            worst = (e, k)
        rows.append((k, e, not frame_bad))

    denom = max(1, n_lr_ok + n_lr_bad)   # 只统计"不贴中线"的肢体样本
    lr_rate = n_lr_ok / float(denom)
    ud_rate = n_ud_ok / float(max(1, n_ud_ok + n_ud_bad))
    print("=" * 76)
    print("  左右/上下一致性 · %s vs %s" % (a.clip, a.trf))
    print("  采样帧 %d（step=%d）｜ 其中 %d 个【肢体样本】横向贴中线（病态，不计入左右分母）"
          % (len(rows), a.step, n_ill))
    print("  左右符号一致率 : %.3f  (%d/%d)  ← 只统计横向不贴中线的肢体" % (lr_rate, n_lr_ok, denom))
    print("  上下符号一致率 : %.3f  (%d/%d)" % (ud_rate, n_ud_ok, n_ud_ok + n_ud_bad))
    print("  平均图像平面偏差: %.3f 肩宽（最差帧 %d = %.3f；>0.6 才算真的错位）"
          % (float(np.mean([r[1] for r in rows])), worst[1], worst[0]))
    bad = [r[0] for r in rows if not r[2]]
    if bad:
        print("  符号不一致的帧: %s" % bad[:20])
    verdict = "PASS"
    if ud_rate < 0.9:
        verdict = "FAIL(上下整体翻转)"
    elif lr_rate < 0.9:
        verdict = "FAIL(左右整体镜像)"
    print("  判定: %s   [两条一起过：本脚本 + 视觉模型诊断]" % verdict)
    print("=" * 76)
    return 0 if verdict == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
