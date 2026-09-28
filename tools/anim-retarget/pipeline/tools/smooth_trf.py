#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""smooth_trf.py —— 在**成品 TRF 上**做一次时间轴平滑，压掉"重定向后还抽搐"的残余抖动。

== 为什么要单独有这一步 ==
  抖动有两个来源，前两个已经在前置环节治掉了：
    ① 源关键点抖（BlazePose 逐帧噪声）→ 用 `vid2pose_multi --median/--smooth` 治；
    ② 24fps 源 → 30fps 输出的【就近取样】让 106 帧里 21 帧重复（走走停停）
       → 已在 `rigs/pose_mediapipe/retarget.py` 改成**相邻帧 slerp** 治掉。
  但即使都做完，输出仍可能有残余高频抖动（实测：逐帧转动方向翻转率 中位 0.36~0.40）。
  **在成品上再平滑一次**是最直接、最不影响姿态幅度的收口手段。

== 做法与两个坑 ==
  · 对每根骨的**四元数四分量**分别做 Savitzky-Golay（保趋势、去高频），再重新归一化。
  · 🔴 坑 1：**必须先做符号连续化**。四元数 q 与 −q 表示同一姿态，序列里一旦跳号，
    直接平滑会造出"翻过去"的假动作。做法：逐个与前一帧点积，负则整帧取反。
  · 🔴 坑 2：窗口别开到接近动作本身的时长。实测倒地那一下只有 ~14 帧（30fps），
    窗口 9 是安全上限；开到 15 会把"倒"糊掉。

用法
    python pipeline/tools/smooth_trf.py --trf output/trf/xxx.trf --window 9 --apply
"""
import argparse
import os
import sys

import numpy as np

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "common"))


def sign_continuous(q):
    """四元数符号连续化：与前一帧点积为负就整帧取反（同一姿态的两种表示）。"""
    q = np.array(q, np.float64).copy()
    for i in range(1, len(q)):
        if float(np.dot(q[i - 1], q[i])) < 0:
            q[i] = -q[i]
    return q


def sgol(x, win, poly=2):
    from scipy.signal import savgol_filter
    n = x.shape[0]
    w = win if win % 2 == 1 else win + 1
    if w > n:
        w = n if n % 2 == 1 else n - 1
    if w <= poly + 1:
        return x
    return savgol_filter(x, w, poly, axis=0, mode="interp")


def main():
    ap = argparse.ArgumentParser(description="TRF 成品平滑（压残余抖动）")
    ap.add_argument("--trf", required=True)
    ap.add_argument("--window", type=int, default=9, help="Savitzky-Golay 窗口（帧，奇数）")
    ap.add_argument("--poly", type=int, default=2)
    ap.add_argument("--root-pos", action="store_true", help="连根骨骼位置轨一起平滑")
    ap.add_argument("--apply", action="store_true")
    a = ap.parse_args()

    from trf_compose import read_trf, write_trf, qangle_deg

    t = read_trf(a.trf)
    nb = len(t.bones)
    nf = len(t.bones[0])
    before = []
    for bi in range(nb):
        q = [c[1] for c in t.bones[bi]]
        d = np.array([qangle_deg(q[i - 1], q[i]) for i in range(1, len(q))], np.float32)
        if d.mean() < 0.05:
            before.append(np.nan); continue
        s = np.sign(np.diff(d))
        before.append(float(np.mean(s[1:] * s[:-1] < 0)) if len(s) > 3 else 0.0)
    b = np.array([x for x in before if not np.isnan(x)])

    for bi in range(nb):
        fr = [c[0] for c in t.bones[bi]]
        q = sign_continuous([c[1] for c in t.bones[bi]])
        q = sgol(q, a.window, a.poly)
        q = q / np.maximum(np.linalg.norm(q, axis=1, keepdims=True), 1e-9)
        t.bones[bi] = [(fr[i], tuple(float(v) for v in q[i])) for i in range(len(fr))]
    if a.root_pos:
        fr = [c[0] for c in t.root_pos]
        p = sgol(np.array([c[1] for c in t.root_pos], np.float64), a.window, a.poly)
        t.root_pos = [(fr[i], tuple(float(v) for v in p[i])) for i in range(len(fr))]

    after = []
    for bi in range(nb):
        q = [c[1] for c in t.bones[bi]]
        d = np.array([qangle_deg(q[i - 1], q[i]) for i in range(1, len(q))], np.float32)
        if d.mean() < 0.05:
            continue
        s = np.sign(np.diff(d))
        after.append(float(np.mean(s[1:] * s[:-1] < 0)) if len(s) > 3 else 0.0)
    c = np.array(after)

    print("=" * 66)
    print("  平滑 %s  窗口 %d  帧数 %d" % (os.path.basename(a.trf), a.window, nf))
    print("  转动方向翻转率（越低越顺）: 中位 %.2f -> %.2f   最高 %.2f -> %.2f"
          % (np.median(b), np.median(c), b.max(), c.max()))
    if not a.apply:
        print("  干跑：加 --apply 才写盘")
        print("=" * 66)
        return
    write_trf(t, a.trf)
    print("  已写盘: %s" % a.trf)
    print("=" * 66)
    print("DONE")


if __name__ == "__main__":
    main()
