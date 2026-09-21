#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_spell_textures.py — 阴魔斩网格的漫反射贴图（月牙 / 能量核）
============================================================================
    python Scripts/../../tools/armor-pipeline/scripts/gen_spell_textures.py
（系统 python 即可，不需要 Blender；跑完自动过一遍 `png_for_editor.py`）

两张图都**不是拍脑袋的配色**，色标来自 `Knowledge/骑砍2粒子系统.md` §九
（用角色身高当尺子、逐帧掩膜取均值/p95，从 `阴魔斩.mp4` 量的）：

    白热核   (246,150,135) / p95 (255,194,185)   → 白热偏粉
    绯红亮盘 (241,142,143) / p95 (255,164,167)   → 亮绯红（月牙亮边同色）
    黑烟     暗紫红 (≈0.20,0.09,0.13) → 近黑

🔴 贴图与 UV 的对应（`build_spell_mesh.py` 里铺的）：
    月牙  u = 沿弧（0..1，与贴图无关）  v = **跨带**（0=内缘/凹面 → 1=外缘/凸面）
          ⇒ 本图做成**竖直渐变**：底部(v=0)白热 → 顶部(v=1)暗紫黑。
             这正是原版录像里"凹面白热、往外红橙、最外裹黑烟"的读法。
    核    UV 是**平面投影**（沿 Z 投到 XY 上的圆盘）⇒ 本图做成**径向渐变**：
             中心白热 → 0.4r 绯红 → 0.85r 暗红 → 边缘近黑（= 原版那圈"暗紫晕圈"的起点）。
             火焰与外晕由粒子承担（spec 里的 crimson_disk / dark_halo），网格只给底色与亮心。

⚠️ 生成物纪律（铁律 22）：本文件是生成器，**图是产物，禁手改** —— 要调色改下面的色标重跑。
⚠️ 进工程源前必须过 `png_for_editor.py`（8bit RGB、无附加块）—— 本脚本自动调用。
"""
import math
import os
import subprocess
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = os.path.dirname(HERE)                       # tools/armor-pipeline
OUTDIR = os.path.join(TOOL, "out")
PNG_FOR_EDITOR = os.path.join(os.path.dirname(TOOL), "face-pipeline", "scripts", "png_for_editor.py")

SIZE = 1024
NAME_CRESCENT = "lwn_yinmo_crescent_d.png"
NAME_CORE = "lwn_yinmo_core_d.png"

# 🔴 底图来自 UE 原版 VFX 贴图库（`tools/particle-pipeline` 从 FlexibleCombatSystem 导出的 262 张）。
#    为什么不用纯渐变：**原版月牙的观感 90% 来自湍流火焰贴图**，光滑渐变怎么调都差一个量级
#    （并排对照过，见 out/_compare/_SIDE_BY_SIDE.png）。
#    这两个文件属于"另一个工程的资产"，不进本仓库 —— 路径变了改这里（或设 BM_UE_TEX 环境变量）。
UE_TEX_DIR = os.environ.get(
    "BM_UE_TEX",
    r"D:\BrainMaker\骑砍2粒子特效复刻\output\tex\FlexibleCombatSystem\VFX\Textures")
UE_FIRE = "T_Noise_Fire.png"          # 橙红底 + 黄白亮脉 + 焦黑块 ← 月牙/核的火焰本体
UE_NOISE = "T_FireNoiseTile_02.png"   # 黑白扰动（做花瓣边、亮脉起伏）

# ── 色标（R,G,B 0-255）：(位置, 颜色) —— 位置 0..1 沿对应轴 ──
# 月牙：位置 = v（0 内缘 → 1 外缘）
CRESCENT_RAMP = [
    (0.00, (255, 236, 224)),   # 内缘刃口：白热
    (0.14, (255, 194, 185)),   # 白热 p95（录像实测）
    (0.30, (241, 142, 143)),   # 绯红亮盘（录像实测）
    (0.52, (176,  52,  58)),
    (0.74, ( 95,  24,  32)),
    (1.00, ( 42,  13,  20)),   # 外缘：近黑（黑烟侧）
]
# 核的两级结构（原版最显眼的结构特征，见 out/_compare/orig_core_burst.png）：
#   白热心（边缘花瓣状，不是正圆） → **一圈独立暗环** → 外接火焰
WHITE_HOT = (255, 246, 240)
DARK_RING = (86, 20, 26)


def lerp_ramp(ramp, t):
    t = min(1.0, max(0.0, t))
    for i in range(len(ramp) - 1):
        p0, c0 = ramp[i]
        p1, c1 = ramp[i + 1]
        if t <= p1:
            k = 0.0 if p1 == p0 else (t - p0) / (p1 - p0)
            return tuple(int(round(c0[j] + (c1[j] - c0[j]) * k)) for j in range(3))
    return ramp[-1][1]


def load_ue(name, size):
    """读一张 UE 贴图并缩到 size×size（灰度化留给调用方按需做）。"""
    p = os.path.join(UE_TEX_DIR, name)
    if not os.path.isfile(p):
        print(f"[FATAL] 找不到 UE 贴图 {p}\n        改 UE_TEX_DIR 或设环境变量 BM_UE_TEX 指到 Textures 目录")
        raise SystemExit(2)
    return Image.open(p).convert("RGB").resize((size, size), Image.LANCZOS)


def make_atlas():
    """一张图集（`build_spell_mesh.py` 的 UV 按这个铺）：

        ┌───────────────┬───────────────┐
        │  左半 u 0~0.5 │  右上 u .5~1  │
        │  月牙跨带      │  v .5~1       │
        │  火焰 × 内热外冷│  核：白心+暗环 │
        ├───────────────┤               │
        │   （左半整高） │               │
        └───────────────┴───────────────┘

    🔴 **为什么要拼图集**：月牙与核是**同一个网格**（一发导弹只能挂一个 flying_mesh），
       而 ModKit 的贴图是按 `<网格名>_d.png` 自动配的 —— **一个网格只能认一张图**。
    🔴 **为什么用 UE 火焰贴图而不是渐变**：原版的烧灼质感（亮脉/焦黑/絮边）全部来自贴图，
       渐变只能给出"干净的塑料红"。对照见 out/_compare/_SIDE_BY_SIDE.png。"""
    half = SIZE // 2
    fire = load_ue(UE_FIRE, half)                       # 512² 火焰底
    noise = load_ue(UE_NOISE, half).convert("L")        # 512² 扰动

    im = Image.new("RGB", (SIZE, SIZE), (0, 0, 0))
    fpx, npx, px = fire.load(), noise.load(), im.load()

    # ── 左半：月牙 ── 火焰 × 跨带渐变（v=0 内缘白热 → v=1 外缘焦黑），沿弧方向把火焰铺开
    for y in range(SIZE):
        v = 1.0 - y / (SIZE - 1)                        # 图像顶行 = v=1
        g = lerp_ramp(CRESCENT_RAMP, v)
        for x in range(SIZE):
            if x >= half:
                break
            fr, fg, fb = fpx[x % half, y % half]
            # 火焰的亮度当"温度"，再乘跨带渐变 → 内缘白热、外缘焦黑、中间带亮脉
            t = (0.35 * fr + 0.5 * fg + 0.15 * fb) / 255.0
            k = t * (0.35 + 0.75 * (1.0 - v))
            px[x, y] = tuple(min(255, int(g[i] * (0.45 + 1.15 * k))) for i in range(3))

    # ── 右上：核 ── 白热心（花瓣状边） + 一圈**独立暗环** + 外接火焰
    cx_px, cy_px, rad_px = 0.75 * SIZE, 0.25 * SIZE, 0.25 * SIZE
    for y in range(SIZE // 2):
        dy = (y - cy_px) / rad_px
        for x in range(half, SIZE):
            dx = (x - cx_px) / rad_px
            r = (dx * dx + dy * dy) ** 0.5
            ang = math.atan2(dy, dx)
            # 花瓣边：用扰动图沿角度采样 → 白心的边界不是正圆
            ni = int(((ang + math.pi) / (2 * math.pi)) * (half - 1))
            nj = int(((r * 2.2) % 1.0) * (half - 1))
            wob = (npx[ni, nj] / 255.0 - 0.5) * 0.16
            core_r = 0.60 + wob                          # 白心半径（带扰动）
            ring_r = core_r + 0.20                       # 暗环外沿
            fr, fg, fb = fpx[x % half, y % half]
            fire_c = (fr, fg, fb)
            if r < core_r:
                # 白热核：色标里的白热，按火焰亮度微微起伏（不要死平）
                t = 0.75 + 0.25 * ((0.35 * fr + 0.5 * fg + 0.15 * fb) / 255.0)
                c = tuple(min(255, int(WHITE_HOT[i] * t)) for i in range(3))
            elif r < ring_r:
                # 🔴 那圈**独立暗环** —— 原版最显眼的结构特征（甜甜圈感），
                #    不是"中心白→边缘暗"的连续渐变（第一版就是这么做的，对照后改掉）
                k = 0.75 + 0.25 * ((0.35 * fr + 0.5 * fg + 0.15 * fb) / 255.0)
                c = tuple(int(DARK_RING[i] * k) for i in range(3))
            else:
                fade = max(0.0, 1.0 - (r - ring_r) / max(0.001, 1.15 - ring_r))
                c = tuple(int(fire_c[i] * (0.35 + 0.65 * fade)) for i in range(3))
            px[x, y] = c
    return im


def save(im, name):
    raw = os.path.join(OUTDIR, "_raw_" + name)
    im.save(raw)
    # 进工程源前过格式闸门（8bit RGB / 无附加块）——省得编辑器把源图连产物一起删
    subprocess.run([sys.executable, PNG_FOR_EDITOR, raw, os.path.join(OUTDIR, name)], check=True)
    os.remove(raw)
    print(f"   -> {os.path.join(OUTDIR, name)}")


def main():
    os.makedirs(OUTDIR, exist_ok=True)
    if not os.path.isfile(PNG_FOR_EDITOR):
        print(f"[FATAL] 找不到 {PNG_FOR_EDITOR}")
        return 2
    print("=== 阴魔斩网格贴图（色标 = 录像实测值，§九） ===")
    atlas = make_atlas()
    # 两个网格各自认自己名字的 _d.png，内容**同一张图集** —— 各自都能正确取样
    save(atlas, NAME_CRESCENT)
    save(atlas, NAME_CORE)
    print("DONE")
    return 0


if __name__ == "__main__":
    sys.exit(main())
