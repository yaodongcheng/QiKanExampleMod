#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_spell_textures.py — 阴魔斩网格的贴图（月牙 / 能量核，**各一张全 UV**）
============================================================================
    python tools/armor-pipeline/scripts/gen_spell_textures.py
（系统 python 即可，不需要 Blender；跑完自动过一遍 `png_for_editor.py`）

🔴 **2026-09-22 改版：拆网格后不再共用分区图集。** 旧版是一张图上下左右分区
（左半月牙 / 右上核），因为"一个网格只能认一张 `_d.png`"。现在月牙与核是**两个独立网格**
（见 `build_spell_mesh.py` 头部），各拿一张全 UV 图 ⇒ 两条限制同时消失：
    · 各自一套材质（月牙可以漂移、核可以不动）
    · 各自的 UV 满 [0,1]，**没有分区要躲**

色标来自 `Knowledge/骑砍2粒子系统.md` §九（用角色身高当尺子从 `阴魔斩.mp4` 逐帧量的）：
    白热核 (246,150,135)/p95 (255,194,185) · 绯红亮盘 (241,142,143) · 黑烟 暗紫红 (≈0.20,0.09,0.13)

两张图都是**黑底 + 加法就绪**（配 `Alpha Blend Mode = Add Alpha`：黑 = 不发光 = 隐形）。

⚠️ 生成物纪律（铁律 22）：图是产物，**禁手改** —— 要调色改下面的参数重跑。
"""
import math
import os
import subprocess
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = os.path.dirname(HERE)                       # tools/armor-pipeline
OUTDIR = os.path.join(TOOL, "out")
PNG_FOR_EDITOR = os.path.join(os.path.dirname(TOOL), "face-pipeline", "scripts", "png_for_editor.py")

# UE 原版 VFX 贴图库（粒子工具链导出的 262 张；路径变了改这里或设 BM_UE_TEX）
UE_TEX_DIR = os.environ.get(
    "BM_UE_TEX",
    r"D:\BrainMaker\骑砍2粒子特效复刻\output\tex\FlexibleCombatSystem\VFX\Textures")
UE_FIRE = "T_Noise_Fire.png"          # 橙红底 + 黄白亮脉 + 焦黑块；实测**可平铺**
UE_NOISE = "T_FireNoiseTile_02.png"   # 黑白扰动（做花瓣边）

SIZE = 1024
NAME_CRESCENT = "lwn_yinmo_crescent_d.png"
NAME_CORE = "lwn_yinmo_core_d.png"

# 🔴 2026-09-22 实机修正：原版最热的色是**鲑红**（实测 (246,150,135) / p95 (255,194,185)），
#    **不是白**。第一版推成纯白 (255,246,240) → 加法叠上去是一片白雾，被亮背景（草地）一衬啥也看不出
#    （实机症状："能看到在流动，但看不到血红"）。而且贴图本身的饱和度要**更红**：
#    游戏里的 bloom 会自然把它洗白，所以底图留白 = 两头都丢。
WHITE_HOT = (255, 170, 132)   # 白热偏橙（给内缘用，不是纯白）
SAT_B = 0.72                  # 蓝通道压暗比值 → 提高饱和度（加法叠在亮背景上会被冲淡，底图必须更红）
SAT_G = 0.90
DARK_RING = (86, 20, 26)

# ── 月牙参数 ──
# 🔴 u（= 沿弧）方向必须**可平铺**：材质要用 use_texture_sweep **只漂 u**，把火顺着刃口送出去。
#    v（= 跨带）方向**不漂**，所以"内缘白热 → 外缘暗"的渐变烘在 v 上会**稳稳停住**。
CR_ENV_POW = 1.10    # 跨带亮度包络指数（越大越集中在内缘）
CR_HOT_W = 0.40      # 内缘最多往"白热偏橙"偏多少（**不再拉满** —— 拉满就是白雾）
CR_GAIN = 1.60       # 整体增益（加法叠亮背景会被冲淡，要比第一版更冲）
CR_BASE = 0.25       # 无火处的底亮度（加法下"有底"才有实体感；实机反馈太透 → 从 0.05 提到 0.25）
CR_TCONTRAST = 1.15  # 火亮度对比度（>1 压暗中间调；实机反馈太透 → 从 1.45 降到 1.15，让更多中间调参与）

# ── 核参数 ──
CO_CORE_R = 0.55     # 白心半径（归一化，1.0 = 贴图半宽）
CO_RING_W = 0.12     # 暗环宽度（🔴 加法下"暗环" = 不加光 → 读作一条暗缝，原版最显眼的结构特征）
CO_GAIN = 1.30


def load_ue(name, size):
    p = os.path.join(UE_TEX_DIR, name)
    if not os.path.isfile(p):
        print(f"[FATAL] 找不到 UE 贴图 {p}\n        改 UE_TEX_DIR 或设 BM_UE_TEX 指到 Textures 目录")
        raise SystemExit(2)
    return np.asarray(Image.open(p).convert("RGB").resize((size, size), Image.LANCZOS), dtype=np.float32)


def luma(a):
    return (0.35 * a[:, :, 0] + 0.5 * a[:, :, 1] + 0.15 * a[:, :, 2]) / 255.0


def save(rgb, name):
    os.makedirs(OUTDIR, exist_ok=True)
    raw = os.path.join(OUTDIR, "_raw_" + name)
    Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB").save(raw)
    subprocess.run([sys.executable, PNG_FOR_EDITOR, raw, os.path.join(OUTDIR, name)], check=True)
    os.remove(raw)
    print(f"   -> {os.path.join(OUTDIR, name)}")


def build_crescent():
    """月牙：**火噪声（u 可平铺）× 跨带渐变（v：内缘白热 → 外缘暗）**。
    配合 `use_texture_sweep` + `Vector1 = (速度, 0, 0, 0)` ⇒ 火沿刃口流动、热刃口不动。"""
    fire = load_ue(UE_FIRE, SIZE)                  # 缩放保持可平铺（边到边）
    t = luma(fire)[:, :, None]
    # 🔴 行→UV 的对应：**图像底行 = UV 的 v=0 = 月牙的内缘**（网格那边 v=0 就是内缘）。
    #    `rowf` = 行比例：顶行 0 → 底行 1。所以"内缘最热" = 热在**底行** = 用 rowf 本身，
    #    ⚠️ 别再写成 `1 - rowf` —— 那样热会跑到外缘去（第一版就是这么错的，渲染出来内缘是暗的）。
    rowf = np.linspace(0.0, 1.0, SIZE)[:, None, None]
    env = np.power(rowf, CR_ENV_POW)
    hot = np.power(rowf, 2.0) * CR_HOT_W * (0.45 + 0.55 * t)
    col = fire * (1.0 - hot) + np.array(WHITE_HOT, dtype=np.float32)[None, None, :] * hot
    col[:, :, 1] *= SAT_G                       # 提饱和：压绿、压蓝 → 红更红
    col[:, :, 2] *= SAT_B
    # 🔴 亮度的"底"必须接近 0：加法混合下**只有亮的地方才存在** —— 底给大了整条带都发光 = 实心块
    tt = np.power(np.clip(t, 0.0, 1.0), CR_TCONTRAST)
    k = env * (CR_BASE + 1.75 * tt) * CR_GAIN
    print(f"    月牙：{UE_FIRE} × 跨带渐变（内缘白热偏橙 → 外缘暗）· u 可平铺"
          f" · 底 {CR_BASE} · 对比 {CR_TCONTRAST} · 增益 {CR_GAIN}")
    return col * k


def build_core():
    """核：径向 —— **白热心（花瓣边）→ 暗环（加法下是暗缝）→ 外圈火 → 全黑**。
    静态（不漂）：径向图案一漂就偏心，和飞行法阵的"圆形遮罩被漂走"是同一个坑。"""
    fire = load_ue(UE_FIRE, SIZE)
    noise = load_ue(UE_NOISE, SIZE)
    t2 = luma(fire)                       # (S,S)   ← 🔴 掩码一律保持 2D
    t3 = t2[:, :, None]                   # (S,S,1) ← 只在**最后相乘**时才升轴
    yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32)
    h = (SIZE - 1) / 2.0
    nx, ny = (xx - h) / h, (yy - h) / h
    r = np.sqrt(nx * nx + ny * ny)        # (S,S)
    ang = np.arctan2(ny, nx)
    # 花瓣边：用扰动图沿角度采样 → 白心边界不是正圆
    ni = np.clip(((ang + math.pi) / (2 * math.pi) * (SIZE - 1)).astype(np.int32), 0, SIZE - 1)
    nj = np.clip(((r * 2.2) % 1.0 * (SIZE - 1)).astype(np.int32), 0, SIZE - 1)
    wob = (luma(noise)[nj, ni] - 0.5) * 0.14        # (S,S)
    core_r = CO_CORE_R + wob                        # (S,S)
    ring_r = core_r + CO_RING_W                     # (S,S)

    white3 = np.broadcast_to(np.array(WHITE_HOT, dtype=np.float32), (SIZE, SIZE, 3))
    ring3 = np.broadcast_to(np.array(DARK_RING, dtype=np.float32), (SIZE, SIZE, 3))
    # ⚠️ ring_r 是**数组**（core_r 含花瓣扰动）⇒ 必须用 np.maximum 而不是内置 max（后者对数组求真值会报错）
    fade = np.clip(1.0 - (r - ring_r) / np.maximum(1e-6, 1.10 - ring_r), 0.0, 1.0)   # (S,S)
    outer = fire * fade[:, :, None] * (0.35 + 1.30 * t3)
    inner_k = (0.90 + 0.45 * t2) * (1.0 - 0.30 * np.clip(r / np.maximum(core_r, 1e-6), 0, 1) ** 3)

    out = np.where((r < core_r)[:, :, None], white3 * inner_k[:, :, None],
          np.where((r < ring_r)[:, :, None], ring3 * 0.045, outer * CO_GAIN))
    out[:, :, 1] *= SAT_G                       # 同样提饱和（理由见 WHITE_HOT 那段注释）
    out[:, :, 2] *= SAT_B
    print(f"    核：{UE_FIRE} 径向 —— 白心 r<{CO_CORE_R}（花瓣边）→ 暗环 +{CO_RING_W} → 外圈火 → 黑")
    return out


def main():
    print("=== 阴魔斩网格贴图（月牙 / 核，各一张全 UV） ===")
    save(build_crescent(), NAME_CRESCENT)
    save(build_core(), NAME_CORE)
    print("DONE")
    return 0


if __name__ == "__main__":
    sys.exit(main())
