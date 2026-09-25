#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_ring_sprite_tex.py — 「电罩外轮廓」那张**环形贴图**（给粒子用，不是给网格）
================================================================================
    python tools/armor-pipeline/scripts/gen_ring_sprite_tex.py

## 为什么是"一张环形贴图"（2026-09-25 绕了一大圈才回到这条）

UE `NS_Lightning_Barrier` 那个"完整一圈外轮廓"，用的就是 **环形贴图**：
发射器 `NE_Circle` = `M_Ring`（180×250 cm）、`NE_Circle001_1` = `M_Ring`、`NewNiagaraEmitter4` = `MI_Ring_Glow`。
**轮廓是画在图里的**，配 `Billboard type = 3d`（正对相机）就是一个永远完整的圈；
再用 **`Quad scale` 非等比**压成蛋形 ⇒ 蛋壳轮廓 ✓

🔴 **两条走错的弯路（别再走）**：
① **烘进球壳网格的贴图里** ✗ —— 轮廓是**随视角变**的（临边发光），球面展开图上根本烘不出来；
② **`Use Normal Vector as Alpha`** ✗ —— 我一度以为它是"临边发光"，挖到 `generated_definitions.rsh:29`
   才发现 `view_vector` 是**世界空间的「像素→相机」方向**、跟法线无关 ⇒ 那个字段实际是
   **"按相机俯仰淡出"**（平视 alpha=1、俯视 alpha=0，编辑器相机是俯视的所以整个片会消失）。

## 贴图规格

    512 × 512 · 8bit RGB · 纯黑底（配 `Alpha Blend Mode = Add`：黑=不发光=隐形）
    圈画在**正方形**里 ⇒ 蛋形交给材质的 `Quad scale`（X 0.75 / Y 1.0）去压，贴图本身保持通用
"""
import os
import subprocess
import sys

import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = os.path.dirname(HERE)
REPO = os.path.dirname(os.path.dirname(TOOL))
OUTDIR = os.path.join(TOOL, "out")
PNG_FOR_EDITOR = os.path.join(os.path.dirname(TOOL), "face-pipeline", "scripts", "png_for_editor.py")

NAME = "lwn_prt_ring_d.png"
STAGE_DIR = os.path.join(REPO, "..", "TaikouAnim", "AssetSources", "ImortReady", "lwn_prt_ring")
MIRROR_DIR = os.path.join(REPO, "..", "TaikouAnim", "AssetSources", "materials", "lwn_prt_ring")
REGISTERED_MARK = os.path.join(REPO, "..", "TaikouAnim", "Assets", "materials", "lwn_prt_ring",
                               NAME.replace(".png", "_tex.tpac"))

SIZE = 512
R_OUT = 0.880       # 圈的中心线半径（归一化，1.0 = 贴图半宽）
W_CORE = 0.012      # 亮核半宽
W_MID = 0.035       # 中间层
W_GLOW = 0.095      # 外圈辉光
G_CORE, G_MID, G_GLOW = 1.00, 0.42, 0.13
FILL = 0.030        # 圈内极淡的底（让罩子有点"体积感"；0 = 只有一圈线）
C_CORE = np.array([255.0, 255.0, 255.0])
C_MID = np.array([190.0, 240.0, 255.0])
C_GLOW = np.array([88.0, 203.0, 255.0])


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    print("=== 电罩外轮廓 · 环形贴图 ===")
    n = SIZE
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float32)
    # 归一化到**半宽**（中心 0，边缘 ±1）—— 用 1 - x/SIZE 那套更平滑
    cx = cy = (n - 1) / 2.0
    nx = (xx - cx) / (n * 0.5)
    ny = (yy - cy) / (n * 0.5)
    r = np.sqrt(nx * nx + ny * ny)
    d = r - R_OUT                                   # 到圈中心线的距离（带符号）
    acc = np.zeros((n, n, 3), dtype=np.float32)
    for w, g, c in ((W_CORE, G_CORE, C_CORE), (W_MID, G_MID, C_MID), (W_GLOW, G_GLOW, C_GLOW)):
        acc += (np.exp(-(d / w) ** 2) * g)[:, :, None] * c[None, None, :]
    # 圈内的极淡底（外圈之外不要 —— 那会露出方边）
    inside = np.clip((R_OUT - r) / 0.25, 0.0, 1.0) * FILL
    acc += inside[:, :, None] * C_MID[None, None, :]
    rgb = np.clip(acc, 0.0, 255.0).astype(np.uint8)
    im = Image.fromarray(rgb, "RGB").filter(ImageFilter.GaussianBlur(0.6))
    print(f"    圈中心线半径 {R_OUT} · 核 {W_CORE}/中间 {W_MID}/辉光 {W_GLOW} · 峰值 {acc.max():.0f}")

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
    print("  ① Import 这张贴图")
    print("  ② 新建材质 `lwn_prt_ring`（照 `lwn_prt_lightning` 同款配方：")
    print("     shader = particle_shading · blend = add_modulate_combined · Diffuse1 = 本图）")
    print("  ③ 粒子 `glow` 发射器：Material = `lwn_prt_ring` · Billboard = 3d · Type = none")
    print("     Particle size = 2.0 · Quad scale X/Y = 0.75 / 1.0 · 速度/重力/阻尼全 0")
    print("     🔴 `Use Normal Vector as Alpha` **不要勾**（那是按相机俯仰淡出，不是临边发光）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
