#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_lightning_arc_tex.py — 引导电弧网格 `lwn_lightning_arc` 的「翻页闪电」图集
================================================================================
    python tools/armor-pipeline/scripts/gen_lightning_arc_tex.py

## 为什么要做这张图（2026-09-25 用户裁定）

M9 卡的是「网格材质透明」：贴图是**黑底 + 白闪电**，黑底会在游戏里画出一个黑方块。
换了好几个 `Alpha Blend Mode` 都不对劲 —— 因为**根本不需要透明**：

    `Alpha Blend Mode = Add`（纯加法）= 背景 + 贴图。**黑 = 加 0 = 不发光 = 隐形** ✓

黑底加法的代价是「只能加亮、不能压暗」，而闪电本来就是自发光 ⇒ 正合适。
（同款配方 = 飞行法阵 `lwn_flight_sigil`，**已实机验证**；见
 `Knowledge/骑砍2网格贴图动画_引擎能力与实现.md` §五「最小配方」+ §六 坑 9/10。）

## 顺带把「闪电在闪」做出来 = 翻页（flipbook）

网格要动起来，材质层有两条路（同一份 shader 源码实证）：
  · **翻页** `use_animated_texture_coords` —— 贴图必须是 **N 列 × M 行图集**，shader 自己
    `uv *= 1/列行` 再 `+= 格偏移`（缩放+偏移，**不平移**）⇒ 每格内容铺满整个网格、逐帧硬切。
    参数 = `Vector Argument 1 = (列, 行, 帧/秒, 总帧数)`。
  · 漂移 `use_texture_sweep` —— 平移 UV，会把贴图里的东西一起漂走，且**与翻页互斥**。
⇒ 闪电要的是**形状跳变**，所以走翻页。

🔴 **三个效果抢同一个 `Vector Argument 1`**（翻页 / 漂移 / 自发光 `self_illumination`）——
    只能开一个。本图配**翻页**，材质里必须**取消** `self_illumination` 与 `use_texture_sweep`。

## 图集规格（与网格 UV 口径配套，改之前先读 `build_lightning_arc.py`）

网格 UV：**u = 宽度方向、v = 长度方向**（`build_lightning_arc.py` 的 `arc_geometry()`）。
🔴 **图像上方 = 网格原点 = 起点端（施法者）**、**下方 = 远端（命中点）** —— 引擎按 v=0 读图像第一行。
   （我第一版注释把这头写反了，2026-09-25 对着实拍修正；若哪天发现反了，把变体里的
    `source_at` 改成 `"bottom"` 重跑即可。）
网格实测 UV 满 [0,1] ⇒ **换贴图不用动网格**。

    图集      1024 × 2048
    栅格      4 列 × 4 行 = **16 帧**
    每格      256 × 512（与旧单帧图同比例，粗细观感不变）
    帧率      18 帧/秒（循环一轮 ≈ 0.89 s）
    Vector1   (4, 4, 18, 16)

## 🔴 画的是「一束」不是「一根」（2026-09-25 用户裁定）

「疾光电影」的观感 = **许多根细闪电捆成一束**，整束逐帧重新噼啪 —— 不是一根长闪电在抖。
所以每格画 **9 根**：

  · 9 根**共用一条缓和的 S 形骨架**（都从起点端收敛出发，朝远端略微散开成扇形）
  · 每根**自己的高频抖动相位不同** ⇒ 同一帧里根根错开，像一捆乱窜的电丝
  · 逐帧把**全部 9 根的相位重掷** ⇒ 每帧都是新的一束 ⇒ 看着就是「一直在噼啪」
  · 加法累积（重叠处更亮）⇒ 束心自然烧成白热、边缘转青 —— 与参考图一致

**近端收、远端散**：起点端（图像下方）散幅小、远端（图像上方）散幅大，即参考图里的扇形。

## 底色 = 纯黑（硬要求）

加法混合下 **黑 = 不发光**；铺白/灰底会被当 albedo 受光，渲染成「木地板」（实拍过）。
本脚本每格背景恒为纯黑，且**不做任何整图亮度抬升**。

## 交付链路（🔴 2026-09-25 用户两次纠正后定稿）

    **分两种情况，脚本自己判**（判据 = 工程里有没有这个资产）：

      ① 资产**已注册**（`Assets/meshes/lightning_arc/lwn_lightning_arc_d_tex.tpac` 已存在）
         ⇒ **直接覆盖镜像目录**，编辑器会自动同步（**这正是镜像布局的用途** —— "替换已注册资产的内容"）
            `Modules/TaikouAnim/AssetSources/meshes/lightning_arc/lwn_lightning_arc_d.png`
         ⇒ 用户**不用**再手动 Import

      ② 资产**从没导入过** ⇒ 放「待导入」目录，交用户 Import：
            `Modules/TaikouAnim/AssetSources/ImortReady/lwn_lightning_arc/lwn_lightning_arc_d.png`

    🔴 **两条纪律**：
      · **待导入的"新资产"绝不许放进镜像**（镜像与 `Assets/` 同目录同名 ⇒ 自动同步生成一个、
        用户再 Import 又生成一个 = 撞车）—— 这就是①与②必须分开的原因。
      · 覆盖镜像**必须在 ModKit 开着时做**（编辑器是文件监视，开之前的改动不补拉，CLAUDE.md 铁律 31），
        且 `Copy-Item` **不改时间戳** ⇒ 覆盖后要显式刷 `LastWriteTime`（脚本里已做）。
      落点目录名现状拼写是 **`ImortReady`**（少一个 p，用户既有名字，别改名）。

    用户侧流程（①的情况省掉 Import）：材质里开 `use_animated_texture_coords` + 填 Vector1 + `Add` 混合
                → Compile Shader → Save → Publish。
"""
import os
import shutil
import subprocess
import sys

import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = os.path.dirname(HERE)                                   # tools/armor-pipeline
REPO = os.path.dirname(os.path.dirname(TOOL))                  # 仓库根（LivingWorldNpcs）
OUTDIR = os.path.join(TOOL, "out")
PNG_FOR_EDITOR = os.path.join(os.path.dirname(TOOL), "face-pipeline", "scripts", "png_for_editor.py")

# ──────────────────── 变体：两张图共用一套画法 ────────────────────
# "1" = 首版（**已注册、正在用**）—— ⚠️ 跑它 = 覆盖正在用的资产，没事别跑
# "2" = 「源端能量集中 / 远端发散」版（2026-09-25 用户要求：新图 + 新材质）
#       与首版的差别：起点端**更紧更亮**、远端**散得更开更暗**，整条有一个「能量沿程耗散」的渐变
VARIANTS = {
    "1": dict(name="lwn_lightning_arc_d.png", asset_dir="lightning_arc", stage="lwn_lightning_arc",
              spread_near=0.070, spread_far=0.360, jit_amp=0.260,
              core_slot=0.26, core_jit=0.55, long_falloff=1.00, src_gain=1.00,
              n_core=5, n_outer=7, outer_gain=0.82, outer_width=0.95,
              source_at="top"),
    # v2 = 「源端集中 / 远端发散」。🔴 2026-09-25 第二版：用户反馈首版 v2 **还是聚焦在中间**
    #      （亮芯被钉在中轴全长不放、外圈又暗又稀）⇒ 核心跟着扇形一起放 + 外圈加密提亮 + 远端少压暗
    "2": dict(name="lwn_lightning_arc2_d.png", asset_dir="lightning_arc", stage="lightning_arc2",
              spread_near=0.018, spread_far=0.500, jit_amp=0.230,
              core_slot=0.45, core_jit=0.60, long_falloff=0.62, src_gain=1.25,
              n_core=3, n_outer=10, outer_gain=0.95, outer_width=1.05,
              source_at="top"),
}
VARIANT = "2"
V = VARIANTS[VARIANT]
# 🔴 源端放哪一头：**"top" = 图像上方**（当前）。引擎按 v=0 读图像第一行 ⇒ 网格原点那一端
#    （= 施法者）显示的是**图的顶部**。若实机发现反了（源端跑到了命中点那头）⇒ 改成 "bottom"，重跑。
SOURCE_AT = V["source_at"]

NAME = V["name"]
# 🔴 编辑器把资产放在**镜像里 FBX 所在的那个目录**（实测：arc2 的 FBX 放进
#    `AssetSources/meshes/lightning_arc/` 后，工程里生成的是 `Assets/meshes/lightning_arc/lwn_lightning_arc2_*`，
#    **不会**另开 `lightning_arc2/` 文件夹）⇒ 两个变体共用同一个 asset_dir。
ASSET_DIR = V["asset_dir"]       # 镜像 / 工程里的资产目录
STAGE_SUB = V["stage"]           # ImortReady 下的待导入子目录
MIRROR = os.path.join(REPO, "..", "TaikouAnim", "AssetSources", "meshes", ASSET_DIR, NAME)
STAGE_DIR = os.path.join(REPO, "..", "TaikouAnim", "AssetSources", "ImortReady", STAGE_SUB)
STAGE = os.path.join(STAGE_DIR, NAME)
# 「资产是否已注册」的判据 = 编辑器工程里有没有它的 tpac
REGISTERED_MARK = os.path.join(REPO, "..", "TaikouAnim", "Assets", "meshes", ASSET_DIR,
                               NAME.replace(".png", "_tex.tpac"))

# ─────────────────────────── 图集规格 ───────────────────────────
COLS, ROWS = 4, 4            # 栅格（列 × 行）
CELL_W, CELL_H = 256, 512    # 每格像素（与旧单帧图同比例）
FPS = 18.0                   # 帧率（帧/秒）—— 循环一轮 = 16/18 ≈ 0.89 s
SEED = 20260925              # 固定种子 ⇒ 可复跑（换个人跑出同一张图）

# ─────────────────────────── 画法参数 ───────────────────────────
N_CORE = V["n_core"]         # 核心束：几根稍微靠中（**但也跟着扇形放**，否则全长一条直芯 = "还是聚焦中间"）
N_OUTER = V["n_outer"]       # 外圈电丝：均匀铺满扇形（根数越多，扇面越"连续"而不是几根毛边）
N_STRANDS = N_CORE + N_OUTER
OUTER_GAIN = V["outer_gain"]     # 外圈亮度倍率（相对核心）—— 太小 = 外圈看不见 = 只剩中间一柱
OUTER_WIDTH = V["outer_width"]   # 外圈粗细倍率（相对核心）
# 骨架：所有根共用的一条缓和 S 形（只给整束一点整体走向；**别给大**，大了就成波浪面条）
BASE_AMP = 0.060             # 骨架横向摆幅（× 格宽）
BASE_FREQ = 1.20             # 骨架弯折次数
# 扇形散幅：源端（图像**上方** = 网格原点）收、远端（图像**下方**）散
SPREAD_NEAR = V["spread_near"]   # 起点端散幅（× 格宽）—— 小 = 源端一束亮芯
SPREAD_FAR = V["spread_far"]     # 远端散幅（× 格宽）—— 大 = 远端散成一片
CORE_SLOT = V["core_slot"]       # 核心束的站位范围（× 扇形）—— 小 = 挤在中轴
CORE_JIT = V["core_jit"]         # 核心束的抖动倍率（相对外圈）—— 小 = 束住不散
# 长轴渐变：能量沿程耗散 —— 源端亮、远端暗（加法下 = 源端更"实"）
LONG_FALLOFF = V["long_falloff"] # 远端亮度（× 源端）—— 1.0 = 不衰减
SRC_GAIN = V["src_gain"]         # 源端整体增益（>1 = 源端更炽）
# 逐帧抖动：每根一条独立折线、每帧重掷 —— 这部分就是「在闪」
# 🔴 幅度是按**格宽**算的，而网格是「12 cm 宽 × 1 m 长」的窄带 ⇒ 格宽方向的摆幅**要往大给**
#    （2026-09-25 用户反馈「抖动不够夸张」：±0.15 落到实物上只有 ±1.8 cm，看着就是一根直线）
JIT_SEGS = 128               # 折线段数（2 的幂；越多越细碎）
JIT_AMP = 0.260              # 单根抖动的总幅度（× 格宽）
JIT_ROUGH = 0.80             # 分形粗糙度：每细分一级幅度乘它
                             # 🔴 别给太小（0.5x 那档出来是「一捆光滑电线」）—— 闪电的细碎度靠它
# 水平两端淡出：防「某根亮芯正好落在格边」被切成一条硬竖线
# （加法下 0 = 不发光；只影响最外侧这几个百分点，束心不受影响）
EDGE_FADE = 0.06
                             # 🔴 别给太小（0.5x 那档出来是「一捆光滑电线」）—— 闪电的细碎度靠它
# 粗细：每根都是「内核 + 中间层 + 外圈辉光」三层高斯剖面
CORE_W = 0.0052              # 白热内核半径（× 格宽）
MID_W = 0.0140               # 中间层（青白）
GLOW_W = 0.0420              # 外圈辉光（青）
CORE_GAIN = 0.85             # 内核增益
MID_GAIN = 0.30
GLOW_GAIN = 0.085
# 每根的强弱/粗细微差（避免 9 根一模一样）
GAIN_JITTER = (0.55, 1.00)   # 单根亮度倍率区间
WIDTH_JITTER = (0.80, 1.30)  # 单根粗细倍率区间
# 配色：白核 → 青白 → 青（青与 `lwn_prt_lightning_cyan_d` 一致 (88,203,255)）
C_CORE = np.array([255.0, 255.0, 255.0])
C_MID = np.array([190.0, 240.0, 255.0])
C_GLOW = np.array([88.0, 203.0, 255.0])


def base_skeleton(y_norm, phase):
    """全束共用的骨架横向偏移（归一化）。phase 固定 ⇒ 每一帧骨架都一样。"""
    return BASE_AMP * np.sin(2 * np.pi * BASE_FREQ * y_norm + phase)


def fractal_path(rng, amp, rough=JIT_ROUGH, segs=JIT_SEGS):
    """分形中点位移 —— 生成一条两端锚定、中间犬牙交错的折线。

    这是画闪电的标准做法：从「一条直线」出发反复对半插点，每个新点的横向偏移
    取左右邻居均值 + 一个随机量，随机量每细分一级乘 `rough`。
    ⇒ 大尺度上有整体走向、小尺度上是硬拐角（**不是正弦那种圆滑波浪**）。

    返回 (segs+1,) 的横向偏移（各自 ±amp 量级）。
    """
    n = segs
    v = np.zeros(n + 1, dtype=np.float64)
    step = n
    scale = amp
    while step > 1:
        half = step // 2
        idx = np.arange(half, n, step)
        v[idx] = 0.5 * (v[idx - half] + v[idx + half]) + rng.uniform(-1.0, 1.0, idx.size) * scale
        step = half
        scale *= rough
    return v


def strand_offsets(y_norm, rng, segs=JIT_SEGS):
    """单根的高频折线抖动，采样到每一行（归一化）。每根、每帧都不同。"""
    v = fractal_path(rng, JIT_AMP, segs=segs)
    ys_knot = np.linspace(0.0, 1.0, segs + 1)
    return np.interp(y_norm, ys_knot, v)


def stamp(xs, cx, w, gain, color, acc):
    """把一条「沿 x 的高斯剖面」按行累加进 acc（加法式累积，最后一次性 clamp）。

    xs   : (W,)   像素 x 坐标
    cx   : (H,)   该剖面在每一行的中心（像素）
    w    : 半径（像素）
    acc  : (H, W, 3) 累积场
    """
    d = (xs[None, :] - cx[:, None]) / max(w, 1e-6)
    p = np.exp(-d * d) * gain
    acc += p[:, :, None] * color[None, None, :]


def draw_beam(cell_w, cell_h, rng, skel_phase):
    """画一格「一束闪电」，返回 (cell_h, cell_w, 3) 的加法亮度场。"""
    acc = np.zeros((cell_h, cell_w, 3), dtype=np.float32)
    ys = np.arange(cell_h, dtype=np.float32)
    y_norm = ys / (cell_h - 1.0)
    xs = np.arange(cell_w, dtype=np.float32)

    # 骨架 + 扇形基线（近端收 → 远端散）
    skel = base_skeleton(y_norm, skel_phase)
    fan = SPREAD_NEAR + (SPREAD_FAR - SPREAD_NEAR) * y_norm

    for i in range(N_STRANDS):
        is_core = i < N_CORE
        if is_core:
            # 核心束：挤在中轴附近 ⇒ 几根叠在一起把中间烧成实心白
            slot = rng.uniform(-CORE_SLOT, CORE_SLOT)
            jit_mul = CORE_JIT
        else:
            # 外圈电丝：均匀铺满扇形（各自微扰，免得等距得像梳子）
            k = i - N_CORE
            slot = -1.0 + 2.0 * (k + 0.5) / N_OUTER + rng.uniform(-0.15, 0.15)
            jit_mul = 1.0
        # 🔴 抖动强度随「扇形宽度」一起长：起点端（v=0）只给 35% ⇒ 束心收紧、接得上施法者；
        #    远端给满 ⇒ 散成一撮乱窜的电丝（参考图就是这个样子）
        jit_scale = jit_mul * (0.30 + 0.70 * y_norm)
        cx_norm = 0.5 + skel + slot * fan + strand_offsets(y_norm, rng) * jit_scale
        cx = cx_norm * cell_w

        # 外圈那几根细一点、暗一点（它们负责"毛边"，不是主体）
        g = rng.uniform(*GAIN_JITTER) * (1.00 if is_core else OUTER_GAIN)
        wj = rng.uniform(*WIDTH_JITTER) * (1.00 if is_core else OUTER_WIDTH)
        stamp(xs, cx, CORE_W * cell_w * wj, CORE_GAIN * g, C_CORE, acc)
        stamp(xs, cx, MID_W * cell_w * wj, MID_GAIN * g, C_MID, acc)
        stamp(xs, cx, GLOW_W * cell_w * wj, GLOW_GAIN * g, C_GLOW, acc)

    # 长轴渐变：能量沿程耗散 —— 源端亮、远端暗（加法下 = 源端更"实"）
    # y_norm=0 = 图像第一行；SOURCE_AT="top" 时它就是源端
    lg = SRC_GAIN * (LONG_FALLOFF + (1.0 - LONG_FALLOFF) * (1.0 - y_norm))
    acc *= lg[:, None, None]
    # 水平两端淡出（见 EDGE_FADE 的说明）—— 防亮芯压在格边被切成硬竖线
    xf = np.clip(np.minimum(xs, (cell_w - 1.0) - xs) / max(1.0, EDGE_FADE * cell_w), 0.0, 1.0)
    acc *= xf[None, :, None]
    if SOURCE_AT == "bottom":
        acc = acc[::-1, :, :]     # 整格上下翻转（几何与渐变一起翻）
    return acc


def build_atlas():
    """产出 4×4 = 16 帧的闪电图集（纯黑底）。"""
    aw, ah = CELL_W * COLS, CELL_H * ROWS
    atlas = np.zeros((ah, aw, 3), dtype=np.float32)
    rng = np.random.default_rng(SEED)
    skel_phase = np.random.default_rng(SEED + 1).uniform(0.0, 2.0 * np.pi)
    for r in range(ROWS):
        for c in range(COLS):
            cell = draw_beam(CELL_W, CELL_H, rng, skel_phase)
            y0, x0 = r * CELL_H, c * CELL_W
            atlas[y0:y0 + CELL_H, x0:x0 + CELL_W] = cell
            print(f"    格 [{c},{r}]  峰值 {cell.max():6.2f}  均值 {cell.mean():5.2f}")
    rgb = np.clip(atlas, 0.0, 255.0)
    # 只做一次极轻的模糊，抹掉高频正弦叠加可能留下的硬边（不改变形状）
    im = Image.fromarray(rgb.astype(np.uint8), "RGB").filter(ImageFilter.GaussianBlur(0.5))
    return im


def preview_sheet(im):
    """把图集按 1:1 拼一张预览（带格子分隔线），给人眼过一遍 16 格。"""
    a = np.asarray(im).copy()
    for c in range(1, COLS):
        a[:, c * CELL_W - 1:c * CELL_W + 1] = (255, 60, 60)
    for r in range(1, ROWS):
        a[r * CELL_H - 1:r * CELL_H + 1, :] = (255, 60, 60)
    return Image.fromarray(a, "RGB")


def main():
    # 🔴 Windows 控制台默认 GBK，遇到 `⇒`/`🔴` 这类字符会 UnicodeEncodeError 直接崩
    #    （拷文件在 print 之前 ⇒ 崩了也说不清到底做没做）。统一转 UTF-8。
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    print("=== 引导电弧网格 · 翻页闪电图集 ===")
    print(f"    图集 {CELL_W * COLS}×{CELL_H * ROWS} · 栅格 {COLS}×{ROWS} = {COLS * ROWS} 帧 · "
          f"{FPS:.0f} 帧/秒（一轮 {COLS * ROWS / FPS:.2f} s）· 每格 {N_STRANDS} 根")
    im = build_atlas()

    os.makedirs(OUTDIR, exist_ok=True)
    sheet = os.path.join(OUTDIR, "_preview_" + NAME)
    preview_sheet(im).save(sheet)
    print(f"    预览（带格线）-> {sheet}")

    raw = os.path.join(OUTDIR, "_raw_" + NAME)
    im.save(raw)
    staged = os.path.join(OUTDIR, NAME)
    subprocess.run([sys.executable, PNG_FOR_EDITOR, raw, staged], check=True)
    os.remove(raw)
    print(f"    产出 {staged}")

    if os.path.isfile(REGISTERED_MARK):
        # ① 已注册 ⇒ 覆盖镜像（编辑器自动同步）。🔴 copy2 保留源文件时间戳 ⇒ 必须显式刷新，
        #    否则监视器看不到改动（CLAUDE.md 铁律 31「蹭 mtime 必须显式写」）。
        shutil.copy2(staged, MIRROR)
        os.utime(MIRROR, None)
        print(f"    资产已注册 ⇒ 覆盖镜像 {MIRROR}（mtime 已刷新，等编辑器重编）")
        print(f"    判据 = {REGISTERED_MARK} 的 mtime 变新")
        print("    用户不用手动 Import。")
    else:
        os.makedirs(STAGE_DIR, exist_ok=True)
        shutil.copy2(staged, STAGE)
        print(f"    资产未注册 ⇒ 放入待导入目录 {STAGE}")
        print("    用户操作：在 ModKit 里 Import 这张贴图。")

    print("\n材质设置（改过就不用再动）：")
    print("    Material Shader Flags: [x] use_animated_texture_coords   [ ] use_texture_sweep   [ ] self_illumination")
    print(f"    Vector Arguments -> Vector 1 = ({COLS}, {ROWS}, {FPS:.0f}, {COLS * ROWS})")
    print("    Transparency -> Alpha Blend Mode = Add      (黑底加法 = 天然隐形，不需要透明)")
    print("    改完 Compile Shader -> Save -> Publish")
    print("\n🔴 摆幅提示：贴图横向摆幅的物理天花板 = 网格宽度（基准 0.12 m）。")
    print("    要抖得夸张就把粗细倍率拉大：custom.spell scale 8（=96 cm 宽）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
