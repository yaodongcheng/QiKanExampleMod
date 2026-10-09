#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_cloud_ring_tex.py — 「中间空的云层」那张**云环贴图**（给粒子用，不是给网格）
================================================================================
    python tools/particle-pipeline/scripts/gen_cloud_ring_tex.py

🔴 **为什么在本目录**（2026-09-28 用户裁定）：这是**粒子特效**的贴图生成器 ⇒ 归
   `tools/particle-pipeline/`（粒子特效管线），与 `gen_particle_effect.py` 平级。
   ❌ 我第一版放进了 `tools/armor-pipeline/scripts/`（**盔甲的管线**）—— 错因是"看邻居在哪"，
      而没问"这条管线是不是它的域"。别再按邻居放。

## 要它干嘛（2026-09-28）

飞行特效 `NS_Flight_HighSpeed_Start` / `_Wave` 的观感是**一圈向外扩的云环**。
🔴 用户在 UE 里看穿了它的做法：**材质不是环形贴图，而是拿一张云贴图 + 对中间部分做不透明度运算**
（挖洞）。

**骑砍的粒子材质是固定的（贴图 + flag），做不了程序化运算** ⇒ 等效办法有两条：
  ① **给一张"已经带洞"的贴图** ← **本脚本产出这张**
  ② `disc` 发射器 × N 颗排出环（用户落地已验证，但那是"一圈颗粒"不是"一整个环"）

走 ① 的理由：**结构同构** —— UE 那边是 **1 颗、20 m**，我们也是"1 颗粒子 + 一张图"。

## 贴图规格

    512 × 512 · 8bit RGB · 纯黑底（配加法类混合：黑 = 不发光 = 隐形）
    中间**真空**（半径 0.6 以内基本全黑）· 环身是**云絮**（fbm 噪声浓淡 + 轮廓扰动）

## 和 `gen_ring_sprite_tex.py` 的区别（那张是电罩的"干净圈线"）

| | 电罩那个 | 这个 |
|---|---|---|
| 形状 | 细圈线（3 层高斯叠出来的亮核） | **宽环带**（云团，中间空） |
| 细节 | 干净、均匀 | **fbm 噪声**：浓淡不匀 + 轮廓毛糙（云） |
| 用途 | 蛋壳轮廓 | **破空云环**（冲刺起步 / 冲刺持续） |
"""
import os
import subprocess
import sys

import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))          # tools/particle-pipeline/scripts
TOOL = os.path.dirname(HERE)                                # tools/particle-pipeline
REPO = os.path.dirname(os.path.dirname(TOOL))               # 仓库根（MODULES\LivingWorldNpcs）
OUTDIR = os.path.join(TOOL, "out")                          # 与本目录其它 gen_*_tex 同一口径
PNG_FOR_EDITOR = os.path.join(os.path.dirname(TOOL), "face-pipeline", "scripts", "png_for_editor.py")

NAME = "lwn_prt_cloud_ring_d.png"
ASSET = "lwn_prt_cloud_ring"
STAGE_DIR = os.path.join(REPO, "..", "LwnAnim", "AssetSources", "ImortReady", ASSET)
MIRROR_DIR = os.path.join(REPO, "..", "LwnAnim", "AssetSources", "materials", ASSET)
REGISTERED_MARK = os.path.join(REPO, "..", "LwnAnim", "Assets", "materials", ASSET,
                               NAME.replace(".png", "_tex.tpac"))

SIZE = 512
SEED = 20260928

# ── 环的骨架（半径都是"归一化到半宽"，1.0 = 贴图半宽）──
# 🔴 下面这组值是**对比过 4 个变体挑出来的（B 版）**（2026-09-28，对比图脚本
#    `Debug/offline/_cloud_ring_variants.py`）：细一些、有云絮结构、中间的洞清楚。
#    另三版的毛病：太糊（奶雾）/ 太碎（断成块）/ 太实（像个甜甜圈）。
R_MID = 0.620        # 环带中心线半径 —— 0.62 ⇒ 中间留出很大的洞
W_BAND = 0.150       # 环带宽（高斯半宽）
WARP = 0.130         # 轮廓扰动幅度（云边毛糙的关键；0 = 正圆）
FADE_IN = 0.14       # 内边淡出宽度（让洞的边缘是"渐渐没有"，不是刀切）
FADE_OUT = 0.10      # 外边淡出宽度

# ── 噪声 ──
OCT_DENSITY = 5      # 浓淡噪声的层数
FREQ_DENSITY = 3     # 基础频率（越小 = 云团越大块）
OCT_FINE = 3         # 细絮层数
FREQ_FINE = 11       # 细絮频率（越大 = 越碎）
GAIN_DENSITY = 0.95  # 浓淡对比（大 = 明暗更分明）
GAIN_FINE = 0.70     # 细絮强度（大 = 更碎更毛）


def value_noise(n, freq, rng):
    """值噪声：随机格点 + smoothstep 双线性插值（不引第三方库）。"""
    freq = max(2, int(freq))
    g = rng.random((freq + 1, freq + 1)).astype(np.float32)
    ys = np.linspace(0.0, freq, n, endpoint=False)
    xs = np.linspace(0.0, freq, n, endpoint=False)
    y0 = np.floor(ys).astype(np.int32)
    x0 = np.floor(xs).astype(np.int32)
    fy = (ys - y0)[:, None]
    fx = (xs - x0)[None, :]
    fy = fy * fy * (3.0 - 2.0 * fy)
    fx = fx * fx * (3.0 - 2.0 * fx)
    v00 = g[np.ix_(y0, x0)]
    v01 = g[np.ix_(y0, x0 + 1)]
    v10 = g[np.ix_(y0 + 1, x0)]
    v11 = g[np.ix_(y0 + 1, x0 + 1)]
    return (v00 * (1.0 - fx) + v01 * fx) * (1.0 - fy) + (v10 * (1.0 - fx) + v11 * fx) * fy


def fbm(n, base_freq, octaves, rng, gain=0.5):
    total = np.zeros((n, n), dtype=np.float32)
    amp, norm, f = 1.0, 0.0, float(base_freq)
    for _ in range(octaves):
        total += value_noise(n, f, rng) * amp
        norm += amp
        amp *= gain
        f *= 2.0
    return total / norm


def smoothstep(x, a, b):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    print("=== 破空云环 · 中间空的云层贴图 ===")
    n = SIZE
    rng = np.random.default_rng(SEED)

    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    c = (n - 1) / 2.0
    nx = (xx - c) / (n * 0.5)
    ny = (yy - c) / (n * 0.5)
    r = np.sqrt(nx * nx + ny * ny)

    # 轮廓扰动：让环的半径随噪声起伏 ⇒ 云边毛糙、不是正圆
    n_warp = fbm(n, FREQ_DENSITY, OCT_DENSITY, rng) - 0.5
    r_w = r + n_warp * WARP * 2.0

    # 环带主体（高斯）+ 内外淡出（洞的边缘要"渐渐没有"）
    band = np.exp(-((r_w - R_MID) / W_BAND) ** 2)
    band *= smoothstep(r, R_MID - W_BAND * 1.6, R_MID - W_BAND * 1.6 + FADE_IN)
    band *= 1.0 - smoothstep(r, R_MID + W_BAND * 1.4, R_MID + W_BAND * 1.4 + FADE_OUT)

    # 浓淡（大块）+ 细絮（碎）—— 两层叠出"云"的观感
    dens = fbm(n, FREQ_DENSITY, OCT_DENSITY, rng)
    fine = fbm(n, FREQ_FINE, OCT_FINE, rng)
    density = band * (0.30 + GAIN_DENSITY * dens) * (1.0 - GAIN_FINE + GAIN_FINE * 2.0 * fine)

    # 贴图边缘必须是纯黑（不然方边会露出来）
    density *= 1.0 - smoothstep(r, 0.96, 1.06)
    density = np.clip(density, 0.0, 1.0)

    rgb = np.clip(density * 255.0, 0.0, 255.0).astype(np.uint8)
    rgb3 = np.repeat(rgb[:, :, None], 3, axis=2)
    im = Image.fromarray(rgb3, "RGB").filter(ImageFilter.GaussianBlur(0.7))

    arr = np.asarray(im).astype(np.float32)
    cx = arr[n // 2, n // 2].mean()
    print(f"    环带中心半径 {R_MID} · 半宽 {W_BAND} · 轮廓扰动 {WARP} · 峰值 {arr.max():.0f}")
    print(f"    正中心亮度 {cx:.1f}（应接近 0 —— 中间是洞）")

    os.makedirs(OUTDIR, exist_ok=True)
    raw = os.path.join(OUTDIR, "_raw_" + NAME)
    im.save(raw)
    staged = os.path.join(OUTDIR, NAME)
    subprocess.run([sys.executable, PNG_FOR_EDITOR, raw, staged], check=True)
    os.remove(raw)
    print(f"    产出 {staged}")

    if os.path.isfile(REGISTERED_MARK):
        os.makedirs(MIRROR_DIR, exist_ok=True)
        import shutil
        shutil.copy2(staged, os.path.join(MIRROR_DIR, NAME))
        os.utime(os.path.join(MIRROR_DIR, NAME), None)
        print(f"    资产已注册 ⇒ 覆盖镜像 {MIRROR_DIR}")
    else:
        os.makedirs(STAGE_DIR, exist_ok=True)
        import shutil
        shutil.copy2(staged, os.path.join(STAGE_DIR, NAME))
        print(f"    资产未注册 ⇒ 放入待导入目录 {STAGE_DIR}")

    print("\n用户操作：")
    print(f"  ① Import 这张贴图（名字 `{ASSET}`）")
    print(f"  ② 新建材质 `{ASSET}` —— 照原版 `prt_shd_water_foam_circular` 的配方，只换贴图：")
    print("     shader      = particle_shading")
    print("     blend       = add_modulate_combined      （Alpha Blend Mode 里选 `Add Modulate Combined`）")
    print("     shaderFlags = use_sunlight")
    print("     flags       = dont_draw_to_gbuffer, no_modify_depth_buffer, needs_forward_rendering")
    print(f"     tex[0]      = 本图（{NAME}）")
    print(f"  ③ 粒子发射器：Material = `{ASSET}` · Billboard type = 3d · Texture sprite Type = none · count 1/1")
    return 0


if __name__ == "__main__":
    sys.exit(main())
