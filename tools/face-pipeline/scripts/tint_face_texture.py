# -*- coding: utf-8 -*-
"""tint_face_texture.py —— 把自建头的脸贴图往「引擎可染色的淡底图」方向调。

## 为什么需要
肤色滑杆是**逐通道乘法**（`g_mesh_factor_color`，见 Shaders/Sources/forward_face_functions.rsh
的 `calculate_albedo_face`）。原版/xxFemale 的脸贴图是**很淡的底图**（肤色由引擎染上去）；
我们的脸贴图是源模型的**成品肤色**（已经带一层颜色）→ 再乘一次就过头：
白档脸发黄、黑档脸变"黑人"、蓝通道被压两遍。

参考基准 = `xxFemaleHead`（一个能正常工作的替换头 mod）的 `head_female_x*_d.png`
（实测皮肤中位 ≈ (221,182,160)、亮度 186；我们的亨利 ≈ (139,88,65)、亮度 97）。

## 用法
    python tint_face_texture.py --src <脸_d.png> --out <输出.png> --ref <xxFemale_d.png> --level 0.5

`--level` = 朝参考基准走多远（0 = 不动，1 = 完全对齐参考）。
**别一上来给 1.0** —— 实测 1.0 会洗过头（脸发灰白），0.5 是更好的起点。

曲线：逐通道 `x*g`，超过 K 的部分做指数滚降（保住高光，不削顶）。
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

K = 0.72          # 高光滚降起点
TARGET_PCT = 50   # 用"皮肤中位亮度"定标


def skin_median(a):
    """取亮度中位附近的像素均值当"皮肤本色"（比全图均值稳）"""
    lum = a.mean(2)
    v = np.percentile(lum, TARGET_PCT)
    m = np.abs(lum - v) <= 3
    return a[m].mean(0)


def apply_curve(a, gain, s):
    o = np.zeros_like(a)
    for c in range(3):
        v = a[..., c] / 255.0 * gain[c] * s
        lo = v <= K
        o[..., c] = np.where(lo, v, K + (1 - K) * (1 - np.exp(-(v - K) / (1 - K)))) * 255.0
    return o


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--ref", required=True, help="参照贴图（xxFemale 的 head_female_x*_d.png）")
    ap.add_argument("--level", type=float, default=0.5, help="朝参照走多远，0~1（默认 0.5）")
    a = ap.parse_args()

    src = Image.open(a.src).convert("RGB")
    ref = Image.open(a.ref).convert("RGB")
    A = np.asarray(src).astype(float)

    cur = skin_median(A)
    tgt = skin_median(np.asarray(ref).astype(float))
    full = tgt / np.maximum(cur, 1e-6)
    gain = full ** a.level                      # 几何插值：level=0.5 时取平方根
    print("原皮肤中位 = (%.1f,%.1f,%.1f)  亮度 %.1f" % (*cur, cur.mean()))
    print("参照中位   = (%.1f,%.1f,%.1f)  亮度 %.1f" % (*tgt, tgt.mean()))
    print("满档增益   = (%.3f,%.3f,%.3f)" % tuple(full))
    print("本次增益   = (%.3f,%.3f,%.3f)   level=%.2f" % (*gain, a.level))

    s = 1.0
    for _ in range(6):                          # 滚降会吃掉一点，迭代补回
        got = skin_median(apply_curve(A, gain, s))
        want = cur + (tgt - cur) * a.level
        s *= float(np.mean(want / np.maximum(got, 1e-6)))
    O = np.clip(apply_curve(A, gain, s), 0, 255)
    got = skin_median(O)
    print("调后中位   = (%.1f,%.1f,%.1f)  亮度 %.1f   收尾 s=%.3f" % (*got, got.mean(), s))
    print("纯白占比   = %.2f%%" % ((O >= 254.5).mean() * 100))

    Image.fromarray(O.astype(np.uint8)).save(a.out)
    print("EXPORTED -> " + a.out)


if __name__ == "__main__":
    main()
