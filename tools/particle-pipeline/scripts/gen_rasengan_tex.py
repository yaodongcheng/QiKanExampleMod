#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_rasengan_tex.py — 螺旋丸气流的「蓝白烟」粒子贴图（拿原版 smoke_d 的形状改造）
====================================================================================
    python tools/particle-pipeline/scripts/gen_rasengan_tex.py
（系统 python 即可；跑完自动过一遍 `png_for_editor.py`，并把待导入件放进 ImortReady）

【为什么要这张图】
`lwn_manual_rasengan_air` 现在挂的是压暗类材质（`prt_shd_haze_1` 一类）⇒ 粒子是**黑的**。
而这个 build 里**粒子颜色的 RGB 色条不可编辑**（见 Knowledge/骑砍2粒子系统.md §12.9 定案）
⇒ 「黑 → 蓝白」只能靠**换材质 / 换贴图**，调色那条路是堵死的。

【为什么不能直接改原版 smoke_d 的 RGB】
原版 `smoke_d`（512×512，2×2 图集）是 **RGB 全白 + 形状全在 alpha 通道** 的贴图。
而 ModKit 只吃 **8bit RGB（无 alpha）** 的源图（`png_for_editor.py` 头部写明了它为什么丢 alpha：
带 alpha 的源图会被编辑器连源图带编译产物一起清掉）
⇒ 直接拿原图染个色丢进去，**形状会连着 alpha 一起丢**，粒子会变成一块块方块。

【本脚本的做法】
把 `smoke_d` 的 **alpha 通道当作亮度**搬进 RGB，再映射成「亮部偏白、暗部偏蓝」的色阶：
    t = alpha / 255
    R = t × (R0 + (1−R0)·t)      ← 暗部红分量压得低 ⇒ 偏蓝
    G = t × (G0 + (1−G0)·t)      ← 保留青
    B = t                        ← 蓝全量
⇒ t=1 是纯白、t=0.5 是青蓝、越暗越偏蓝。
配 **`Alpha Blend Mode = Add`（纯加法）**：黑 = 加 0 = 天然隐形，形状完全由这张图承担。

【产物】
    tools/particle-pipeline/out/lwn_prt_rasengan_air_d.png           管线产物（2×2 图集）
    tools/particle-pipeline/out/_preview_lwn_prt_rasengan_air_d.png  预览（黑底 + 白底并排）
    Modules/LwnAnim/AssetSources/ImortReady/lwn_prt_rasengan_air/   待用户 Import

【导入后要在编辑器里做的】见脚本末尾打印的后续步骤（建材质 / 选混合 / 切图集）。
⚠️ 生成物纪律（铁律 22）：图是产物，**禁手改** —— 要调色改下面的常量重跑。
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

# ---------------------------------------------------------------------------
# 配置
# ---------------------------------------------------------------------------
# 色阶端点（0~1）：t=1 时恒为纯白，这里是"暗部那一端"的 RGB 残留比例
R0, G0, B0 = 0.30, 0.78, 1.00      # 想更蓝就压低 R0；想更青就抬 G0
GAIN = 1.00                         # 整体亮度倍率（加法混合下不够亮靠材质的 Diffuse multiplier 提）

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
SRC = os.path.join(ROOT, "tools", "particle-pipeline", "out", "mattex_all", "text0", "smoke_d.png")
OUT = os.path.join(ROOT, "tools", "particle-pipeline", "out")
# 🔴 待导入目录在**正牌模块** `Modules/LwnAnim/`（与 LivingWorldNpcs 平级）——
#    不是 `LivingWorldNpcs/Modules/LwnAnim`（那是另一份独立副本，编辑器不读它）。
#    口径与 `build_rasengan.py` / `build_sphere_shell.py` 的 STAGE_ROOT 一致。
IMR = os.path.join(os.path.dirname(ROOT), "LwnAnim", "AssetSources", "ImortReady", "lwn_prt_rasengan_air")
NAME = "lwn_prt_rasengan_air_d"


def build_color(rgba):
    """RGBA(uint8) → 蓝白 RGB(uint8)：alpha 当亮度，黑底 + 蓝白烟"""
    t = rgba[:, :, 3].astype(np.float32) / 255.0
    r = t * (R0 + (1.0 - R0) * t)
    g = t * (G0 + (1.0 - G0) * t)
    b = t * (B0 + (1.0 - B0) * t)
    rgb = np.stack([r, g, b], axis=-1) * GAIN
    return (np.clip(rgb, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8)


def make_preview(rgb):
    """黑底（= Add 混合下的真实观感）与白底并排，方便肉眼判形状/颜色"""
    h, w, _ = rgb.shape
    canvas = np.zeros((h, w * 2 + 8, 3), dtype=np.uint8)
    canvas[:, :w] = rgb                                   # 黑底：加法下就是这样叠在画面上的
    canvas[:, w + 8:] = 255 - ((255 - rgb.astype(np.int16)) // 2).astype(np.uint8)  # 白底：半透明近似
    return canvas


def main():
    if not os.path.isfile(SRC):
        print("[STOP] 找不到原版贴图：%s" % SRC)
        print("       它由 particle-pipeline 的材质普查脚本导出（tools/particle-pipeline/out/mattex_all/）。")
        return 1

    im = Image.open(SRC)
    a = np.asarray(im)
    if a.ndim != 3 or a.shape[2] != 4:
        print("[STOP] 源图不是 RGBA（shape=%s）—— 本脚本按「形状在 alpha」的前提写的" % (a.shape,))
        return 1

    alpha = a[:, :, 3]
    print("源图 %s  %dx%d  alpha: min=%d max=%d mean=%.1f"
          % (os.path.basename(SRC), im.size[0], im.size[1], alpha.min(), alpha.max(), alpha.mean()))

    rgb = build_color(a)
    os.makedirs(OUT, exist_ok=True)
    main_png = os.path.join(OUT, NAME + ".png")
    Image.fromarray(rgb, "RGB").save(main_png)

    prev_png = os.path.join(OUT, "_preview_" + NAME + ".png")
    Image.fromarray(make_preview(rgb), "RGB").save(prev_png)
    print("产物  %s  (%.0f KB)" % (main_png, os.path.getsize(main_png) / 1024.0))
    print("预览  %s" % prev_png)

    # ---- 过 png_for_editor（8bit RGB、只留 IHDR/IDAT/IEND）----
    conv = os.path.join(ROOT, "tools", "face-pipeline", "scripts", "png_for_editor.py")
    r = subprocess.run([sys.executable, conv, main_png], capture_output=True, text=True, encoding="utf-8", errors="replace")
    print("[png_for_editor] rc=%d %s" % (r.returncode, (r.stdout or "").strip()))

    # ---- 放进待导入目录（用户 Import，铁律 36：不碰镜像目录）----
    os.makedirs(IMR, exist_ok=True)
    dst = os.path.join(IMR, NAME + ".png")
    shutil.copy2(main_png, dst)
    print("待导入 %s" % dst)

    print()
    print("=" * 72)
    print("接下来在 ModKit 里（用户操作）：")
    print("  1. Import 上面那张贴图  →  资源名 %s" % NAME)
    print("  2. 建材质（右键 Create > Material，或克隆原版 prt_shd_smoke_1）：")
    print("       Shader            particle_shading")
    print("       Diffuse 槽        %s" % NAME)
    print("       Alpha Blend Mode  Add          ← 纯加法；黑底才天然隐形")
    print("  3. 粒子编辑器里 Material 栏选这个新材质")
    print("  4. Texture sprite：Type = select_random，Sprite count = 2, 2（图集是 2×2，别选 none）")
    print("  5. 亮度不够 → 调 Diffuse multiplier（原版火焰给到 1000，烟雾通常 1~30）")
    print("=" * 72)
    return 0


if __name__ == "__main__":
    sys.exit(main())
