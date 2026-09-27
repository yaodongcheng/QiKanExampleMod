#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_rasengan_parts_tex.py — 螺旋丸「中心球 + 4 片叶」的材质贴图
============================================================================
    python tools/armor-pipeline/scripts/gen_rasengan_parts_tex.py
（系统 python 即可；跑完自动过一遍 `png_for_editor.py`，并把待导入件放进 ImortReady）

【两张图，各自配一个网格】（网格由 `build_rasengan.py` 出，UV 口径见那边）
    ① `lwn_rasengan_core_d.png`  —— 中心球：**青蓝发光**
       UV = 等距柱状（u 经度 / v 纬度）。球面是均匀发光体，所以图上只要**接近纯色**
       （赤道略亮、两极略沉一点，别做径向渐变 —— 等距柱状 UV 下径向渐变会在两极挤成一团）。
    ② `lwn_rasengan_blade_d.png` —— 叶片：**内亮外淡的白光叶片**
       UV：u = 沿带长（0 内端 → 1 外端）· v = 横向（0 / 1 是两条边）
       ⇒ 图上是「横向一条带」：**沿 x（= u = 长度）由亮到灭**、**沿 y（= v = 横向）中间亮两边柔**

【为什么一律黑底】
    两张都配 **`Alpha Blend Mode = Add`（纯加法）**：黑 = 加 0 = 天然隐形，
    形状完全由这张图承担。这也是这个工程里所有网格发光件的标准口径。

【和其它件的关系】
    `gen_rasengan_tex.py` 生成的是**粒子**用的烟贴图（`lwn_prt_rasengan_air_d`），
    与本脚本的**网格**贴图是两回事，别混。

⚠️ 生成物纪律（铁律 22）：图是产物，**禁手改** —— 要调色/调渐隐改下面的常量重跑。
"""
import os
import shutil
import subprocess
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

import numpy as np
from PIL import Image

# ─────────────────────────── 参数 ───────────────────────────
SIZE = 512

# ① 中心球：青蓝发光（RGB，0~255）
CORE_COLOR = (118, 214, 255)    # 主体青蓝
CORE_EQUATOR_GAIN = 1.00        # 赤道亮度（球是均匀发光体，别过大）
CORE_POLE_GAIN = 0.86           # 两极收一点，免得等距柱状 UV 下极点过曝

# ② 叶片：白偏青，沿长度（x=u）由亮到灭，横向（y=v）中间亮两边柔
BLADE_COLOR = (232, 248, 255)   # 近白微青
BLADE_INNER_GAIN = 1.00         # x=0（内端，贴着球）的亮度
BLADE_OUTER_GAIN = 0.06         # x=1（外端）的亮度 —— 收尖处基本熄灭
BLADE_FALLOFF_POW = 0.85        # 沿长度的衰减曲线指数（越小、中段越亮）
BLADE_EDGE_SOFT = 0.55          # 横向边缘柔化强度（0 = 硬边、1 = 很柔）

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
OUT = os.path.join(ROOT, "tools", "armor-pipeline", "out")
# 🔴 待导入目录在**正牌模块** `Modules/TaikouAnim/`（与 LivingWorldNpcs 平级）——
#    不是 `LivingWorldNpcs/Modules/TaikouAnim`（那是另一份独立副本，编辑器不读它）。
#    口径与 `build_rasengan.py` 的 STAGE_ROOT 一致。
IMR_ROOT = os.path.join(os.path.dirname(ROOT), "TaikouAnim", "AssetSources", "ImortReady")
CONV = os.path.join(ROOT, "tools", "face-pipeline", "scripts", "png_for_editor.py")


def save_and_stage(name, rgb):
    """存图 → 过 png_for_editor → 进 ImortReady/<名>/"""
    os.makedirs(OUT, exist_ok=True)
    p = os.path.join(OUT, name + ".png")
    Image.fromarray(rgb.astype(np.uint8), "RGB").save(p)
    print("  产物 %s  (%.0f KB)" % (p, os.path.getsize(p) / 1024.0))

    prev = os.path.join(OUT, "_preview_" + name + ".png")
    Image.fromarray(rgb.astype(np.uint8), "RGB").save(prev)   # 黑底图本身就是预览（加法下即所见图）

    r = subprocess.run([sys.executable, CONV, p], capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    tail = [ln for ln in (r.stdout or "").strip().splitlines() if ln.strip()]
    print("  [png_for_editor] %s" % (tail[-2] if len(tail) >= 2 else (tail[-1] if tail else "?")))

    d = os.path.join(IMR_ROOT, name[:-2] if name.endswith("_d") else name)
    os.makedirs(d, exist_ok=True)
    shutil.copy2(p, os.path.join(d, name + ".png"))
    print("  待导入 %s" % os.path.join(d, name + ".png"))


def build_core():
    """中心球：等距柱状 UV ⇒ 横向（u）与纵向（v）都平滑，赤道亮、两极略沉"""
    v = np.linspace(0.0, 1.0, SIZE)[:, None]                   # 行 = v = 纬度
    gain = CORE_POLE_GAIN + (CORE_EQUATOR_GAIN - CORE_POLE_GAIN) * np.sin(np.pi * v)
    img = np.zeros((SIZE, SIZE, 3), dtype=np.float32)
    for c in range(3):
        img[:, :, c] = CORE_COLOR[c] * gain
    return img


def build_blade():
    """叶片：x = 沿带长（内→外）、y = 横向（0/1 两条边）"""
    x = np.linspace(0.0, 1.0, SIZE)[None, :]                   # 列 = u = 长度
    y = np.linspace(0.0, 1.0, SIZE)[:, None]                   # 行 = v = 横向

    along = BLADE_OUTER_GAIN + (BLADE_INNER_GAIN - BLADE_OUTER_GAIN) * ((1.0 - x) ** BLADE_FALLOFF_POW)
    # 横向：sin(π·v) 两端为 0、中轴为 1；开方让中间那团更宽（更像一束光而不是一根线）
    across = np.sin(np.pi * y) ** 0.45
    across = 1.0 - BLADE_EDGE_SOFT * (1.0 - across)

    g = along * across
    img = np.zeros((SIZE, SIZE, 3), dtype=np.float32)
    for c in range(3):
        img[:, :, c] = BLADE_COLOR[c] * g
    return img


def main():
    print("    === 螺旋丸 · 网格贴图（中心球 + 叶片）===")
    print("  [1/2] 中心球：青蓝 %s · 赤道 %.2f / 两极 %.2f"
          % (CORE_COLOR, CORE_EQUATOR_GAIN, CORE_POLE_GAIN))
    save_and_stage("lwn_rasengan_core_d", build_core())
    print("  [2/2] 叶片：内端 %.2f → 外端 %.2f · 边缘柔化 %.2f"
          % (BLADE_INNER_GAIN, BLADE_OUTER_GAIN, BLADE_EDGE_SOFT))
    save_and_stage("lwn_rasengan_blade_d", build_blade())

    print()
    print("=" * 72)
    print("接下来在 ModKit 里（用户操作）：")
    print("  ① Import 这两张贴图（连同 build_rasengan.py 出的两个 FBX）")
    print("  ② 材质（FBX 导入时会各建一个同名材质）：")
    print("       Shader            pbr_translucent")
    print("       Diffuse 槽        对应的 _d 贴图")
    print("       Alpha Blend Mode  Add          ← 纯加法；黑底天然隐形")
    print("     （想更亮可再开 self_illumination —— 本件不翻页，不跟它抢 Vector1）")
    print("  ③ 场景里：建空旋转节点 → 球 / 叶片挂进去 → Save As Prefab")
    print("=" * 72)


if __name__ == "__main__":
    sys.exit(main())
