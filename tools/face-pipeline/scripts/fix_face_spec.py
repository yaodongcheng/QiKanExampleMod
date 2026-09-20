# -*- coding: utf-8 -*-
"""fix_face_spec.py —— 把自建头的脸 `_s` 从「金属度口径」改成「脸部口径」

【为什么】
  脸部着色器把 `_s` 当【高光】用，不是金属度（源码实证）：
      forward_face_functions.rsh → calculate_specular_face
          specularity.x *= g_specular_coef     // R = 反射强度
          specularity.y *= g_gloss_coef        // G = 光泽
      Face.rsh → compute_specular_face_lighting
          reflectivity = specularity_info.x * 5.0f
          roughness    = max(0.05, 1.0 - specularity_info.y)
      forward_face_functions.rsh → calculate_ao_face_forward
          occ = specular_sample.z              // B = 环境光遮蔽

  实测中位：
      参照（xxFemale 原版脸）  R 93  G 104  B 194   ← 有皮肤油光 + 一点遮蔽
      我们（make_head_textures 合成的）R  0  G  27  B 255
  ⇒ 我们的 R=0 → 反射强度 0 → **脸完全没有高光，是一块死哑光板**；
    旁边的身体有正常皮肤光泽 ⇒ 一眼两种材质（这是"生硬的两张皮肤"的第二半）。
  🔴 `make_head_textures.py` 头注释里「与 Native body_female_a 同款」是错的：
    body_female_a 是【标准着色器】的金属度口径，脸不是。同一条链上的
    蒂法 / 萨菲罗斯 / 亨利 / 战无2×28 全中。

【做法】不改几何、不改 UV —— 只重映射通道数值：
  G（光泽） = 把现有 G（= 255−粗糙度，UV 天然对齐）线性映射到参照的 G 区间
  R（反射）= 用同一个归一化量映射到参照的 R 区间（R 与 G 在皮肤上高度相关）
  B（遮蔽）= 有源 RMA 就用它的 B 通道（真 AO）；没有就用参照中位常数

【用法】
  python fix_face_spec.py --src-spec <现有_s.png> --ref-spec <参照_s.png> --out <出_s.png>
      [--rma <源RMA.png>]        # 可选：拿真实 AO 填 B（亨利有 m_head_henry_RMA.png）

  铁律 22：本脚本是生成器，改效果改这里。
"""
import argparse
import sys

import numpy as np
from PIL import Image


def band(a, lo=10, hi=90, ch=0):
    v = a[..., ch].reshape(-1)
    v = v[v > 1]
    if len(v) == 0:
        return 0.0, 1.0
    return float(np.percentile(v, lo)), float(np.percentile(v, hi))


def rescale(x, f0, f1, t0, t1):
    t = np.clip((x - f0) / max(1e-6, f1 - f0), 0.0, 1.0)
    return t0 + t * (t1 - t0)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src-spec", required=True, help="现有 _s（R=金属度 / G=255−粗糙度 / B=AO或常数）")
    ap.add_argument("--ref-spec", required=True, help="参照 _s（原版/xxFemale 的脸部高光图，只取统计区间）")
    ap.add_argument("--out", required=True)
    ap.add_argument("--rma", default=None, help="可选：源 RMA 图，用它的 B 通道当 AO")
    ap.add_argument("--out-preview", default=None)
    a = ap.parse_args()

    src = np.asarray(Image.open(a.src_spec).convert("RGB")).astype(np.float32)
    ref = np.asarray(Image.open(a.ref_spec).convert("RGB")).astype(np.float32)
    H, W = src.shape[:2]

    rR = band(ref, 10, 90, 0)
    rG = band(ref, 10, 90, 1)
    rB = float(np.median(ref[..., 2][ref[..., 2] > 1]))
    print("参照区间： R %.0f~%.0f   G %.0f~%.0f   B 中位 %.0f" % (rR[0], rR[1], rG[0], rG[1], rB))

    # 现有 G 就是「光泽」的量（= 255−粗糙度），只是数值区间不对
    gloss = src[..., 1]
    sG = band(src, 10, 90, 1)
    print("现有 G 区间： %.0f~%.0f  →  重映射到参照 %.0f~%.0f" % (sG[0], sG[1], rG[0], rG[1]))

    t = np.clip((gloss - sG[0]) / max(1e-6, sG[1] - sG[0]), 0, 1)   # 0~1 归一
    outG = rG[0] + t * (rG[1] - rG[0])
    outR = rR[0] + t * (rR[1] - rR[0])

    if a.rma:
        rma = Image.open(a.rma).convert("RGB").resize((W, H), Image.LANCZOS)
        outB = np.asarray(rma).astype(np.float32)[..., 2]
        print("B（AO）取自源 RMA 的 B 通道：中位 %.0f   区间 %.0f~%.0f"
              % (np.median(outB), np.percentile(outB, 5), np.percentile(outB, 95)))
    else:
        outB = np.full((H, W), rB, dtype=np.float32)
        print("B（AO）用参照中位常数 %.0f（源没有 AO 信息）" % rB)

    res = np.clip(np.dstack([outR, outG, outB]), 0, 255).astype(np.uint8)
    Image.fromarray(res).save(a.out)
    m = np.median(res.reshape(-1, 3), axis=0)
    print("🔴 改后 _s 通道中位： R %.0f  G %.0f  B %.0f   （参照 R 93 G 104 B 194）" % tuple(m))
    print("已写出 %s" % a.out)

    if a.out_preview:
        c = Image.new("RGB", (W // 2 * 3, H // 4), (18, 18, 18))
        for i in range(3):
            c.paste(Image.fromarray(res).split()[i].convert("RGB").resize((W // 2, H // 4)),
                    (i * W // 2, 0))
        c.save(a.out_preview)
        print("通道预览 %s" % a.out_preview)
    return 0


if __name__ == "__main__":
    sys.exit(main())
