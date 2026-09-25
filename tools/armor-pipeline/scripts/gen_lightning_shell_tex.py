#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_lightning_shell_tex.py — 电罩球壳 `lwn_lightning_shell` 的「翻页电纹」图集
================================================================================
    python tools/armor-pipeline/scripts/gen_lightning_shell_tex.py

## 这是什么

`build_sphere_shell.py` 出的那个球壳（等距柱状 UV）要贴一张**球面展开的电弧图**，
并且要**会动** ⇒ 走翻页（`use_animated_texture_coords`）：每格 = 一张"球面上电弧乱窜"的展开图。

🔴 **为什么粒子做不到、必须上网格**：UE 那边靠 **Ribbon/Beam** 画 15 条细长弧；
   骑砍粒子只能出"点状电花"，要拉长得给速度（`turn_to_velocity_side` + skew），
   可壳上的电丝**不该有速度**（一有速度就变成往外喷的火花 —— 实拍过）。
   网格 + 翻页是唯一能画出"一长条弧"的路子。

## 图集规格

    图集      2048 × 1024
    栅格      4 列 × 4 行 = **16 帧**（每格 512 × 256，2:1 = 等距柱状的正确比例）
    帧率      20 帧/秒（一轮 ≈ 0.8 s）
    Vector1   (4, 4, 20, 16)

## 画法（与球面几何对齐，别乱来）

球壳 UV（`build_sphere_shell.py` 的 `uv_sphere()`）：
    u = atan2(y, x) / 2π                     → 经度
    v = 1 − acos(z / r) / π                  → **v=1 是北极、v=0 是南极**
⇒ 画的时候**在 3D 球面上采样路径**（两点之间的大圆 + 垂直抖动），再投影到 (u, v)，
   这样电弧的粗细/走向才跟球面对得上。**不要在像素空间画直线** —— 那在球面上是歪的。

- **u 要绕接**：跨过 u=0/1 的电弧必须**无缝绕过去**（画的时候按环绕取像素差）。
- 两端**收笔**（抖动在 t=0/1 处归零）⇒ 电弧像"贴着球面的一段"，不是乱线。
- 底色**纯黑**（配 `Alpha Blend Mode = Add`：黑 = 不发光 = 隐形）。

## 交付（两条路，脚本自己判）

    ① 资产已注册 → 覆盖镜像（编辑器自动同步）
    ② 没导入过   → 放 `AssetSources/ImortReady/lightning_shell/`，交用户 Import   ← 首次走这条
"""
import math
import os
import shutil
import subprocess
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = os.path.dirname(HERE)
REPO = os.path.dirname(os.path.dirname(TOOL))
OUTDIR = os.path.join(TOOL, "out")
PNG_FOR_EDITOR = os.path.join(os.path.dirname(TOOL), "face-pipeline", "scripts", "png_for_editor.py")

NAME = "lwn_lightning_shell_d.png"
ASSET_DIR = "lightning_shell"
STAGE_SUB = "lightning_shell"
MIRROR = os.path.join(REPO, "..", "TaikouAnim", "AssetSources", "meshes", ASSET_DIR, NAME)
STAGE_DIR = os.path.join(REPO, "..", "TaikouAnim", "AssetSources", "ImortReady", STAGE_SUB)
REGISTERED_MARK = os.path.join(REPO, "..", "TaikouAnim", "Assets", "meshes", ASSET_DIR,
                               NAME.replace(".png", "_tex.tpac"))

# ─────────────────────────── 模式 ───────────────────────────
# 🔴 **"ring"（当前）= 静态赤道带** —— 用户 2026-09-25 裁定：**壳是"容器"，电弧是"内容"**。
#    壳的作用只是给出那个**静态的形状**（UE 预览里那个椭圆环），电丝交给粒子层。
#    ⇒ 静态 ⇒ 材质**不需要** `use_animated_texture_coords`，Vector Argument 1 **空出来**了
#      （想要自发光就可以改勾 `self_illumination`）。
#    ⇒ 一条赤道带从球外看：近侧与远侧投到同一圈 ⇒ **就是一个环** ✓
# "arcs" = 旧的翻页电纹版（球面 12 条电弧乱窜，4×4 图集）—— 保留可复跑，随时能换回去
MODE = "ring"

# ─────────────────────────── 规格 ───────────────────────────
COLS, ROWS = 4, 4          # 仅 "arcs" 模式用
CELL_W, CELL_H = 512, 256  # 2:1 = 等距柱状
FPS = 20.0
SEED = 20260925

# ── "ring" 模式参数（静态）──
RING_W, RING_H = 1024, 512      # 单帧静态图（等距柱状 2:1）
RING_V = 0.50                   # 带子中心（v: 1=北极 / 0=南极）⇒ 0.50 = 赤道
RING_SIGMA = 0.055              # 带子半宽（v 方向）—— 越大越粗
RING_GAIN = 0.85                # 带子亮度
RING_LOBES = 3                  # 沿经度方向的明暗波瓣数（0 = 完全均匀；给几瓣免得像塑料圈）
RING_LOBE_AMP = 0.22            # 波瓣起伏幅度（占带子亮度的比例）
FILL = 0.045                    # 整球淡底（让罩子有"体积感"；0 = 只有一条带）
FILL_POLE_FADE = 0.55           # 淡底往两极衰减到原来的多少（两极完全不亮 = 0）

# ─────────────────────────── 画法 ───────────────────────────
N_ARCS = 12                # 每帧几条弧（UE 是 15 条 ribbon）
ARC_LEN_MIN, ARC_LEN_MAX = 0.16, 0.62   # 弧长（占大圆的比例）——有长有短才自然
ARC_STEPS = 48             # 每条弧的采样点（别给太多：**抖动频率一高，弧线会变成"毛虫"**）
JIT_AMP = 0.105            # 垂直抖动幅度（弧度制比例）—— 越大越犬牙
JIT_ROUGH = 0.66           # 分形粗糙度（同上：给太高 = 梳齿状）
# 粗细：三层高斯剖面（内核 / 中间层 / 外圈辉光），单位 = 格宽的比例
CORE_W = 0.0042
MID_W = 0.0120
GLOW_W = 0.0220            # 🔴 别给大：这是"紧贴电弧的一圈光晕"，给到 0.04+ 整张画布会泛白
CORE_GAIN = 0.95
MID_GAIN = 0.32
GLOW_GAIN = 0.085
POLE_LIMIT = 0.88          # 弧线端点被挡在 |z| ≤ 0.88（约 ±62° 纬度）—— 防极点处经度跳变
GAIN_RANGE = (0.55, 1.00)  # 每条弧的亮度倍率
# 配色：与粒子那层 `lwn_prt_lightning`（青白）一致，免得壳是金、粒子是青两块打架
C_CORE = np.array([255.0, 255.0, 255.0])
C_MID = np.array([190.0, 240.0, 255.0])
C_GLOW = np.array([88.0, 203.0, 255.0])


def rand_unit(rng):
    while True:
        v = rng.normal(size=3)
        n = float(np.linalg.norm(v))
        if n > 1e-6:
            return v / n


def slerp(a, b, t):
    d = float(np.clip(np.dot(a, b), -1.0, 1.0))
    th = math.acos(d)
    if th < 1e-5:
        return a.copy()
    s = math.sin(th)
    return (math.sin((1.0 - t) * th) * a + math.sin(t * th) * b) / s


def fractal(n, rng, amp, rough=JIT_ROUGH):
    """分形中点位移（同 `gen_lightning_arc_tex.py`）—— 闪电是折线硬拐，不是正弦。"""
    v = np.zeros(n + 1)
    step, scale = n, amp
    while step > 1:
        half = step // 2
        idx = np.arange(half, n, step)
        v[idx] = 0.5 * (v[idx - half] + v[idx + half]) + rng.uniform(-1, 1, idx.size) * scale
        step, scale = half, scale * rough
    return v


def arc_path(rng, tries=10):
    """一条贴在球面上的弧：两点之间的大圆 + 垂直分形抖动（两端收笔）。

    🔴 **极区用"弃样重掷"，不要"夹平"**（2026-09-25 实拍）：夹 `z` 会把路径压到同一条纬线上，
       画出来是一圈**梳齿状横带**。改成整条弧越界就重掷，弧线就保持自然。
    """
    for _ in range(tries):
        a = rand_unit(rng)
        b = rand_unit(rng)
        t_end = rng.uniform(ARC_LEN_MIN, ARC_LEN_MAX) * 2.0      # 2.0 = 半个大圆
        b = slerp(a, b, min(1.0, t_end))
        dis = fractal(ARC_STEPS, rng, JIT_AMP)
        pts, ok = [], True
        for i in range(ARC_STEPS + 1):
            t = i / ARC_STEPS
            p = slerp(a, b, t)
            tang = slerp(a, b, min(1.0, t + 1e-3)) - slerp(a, b, max(0.0, t - 1e-3))
            perp = np.cross(p, tang)
            npn = float(np.linalg.norm(perp))
            if npn < 1e-6:
                perp = np.cross(p, np.array([0.0, 0.0, 1.0]))
                npn = float(np.linalg.norm(perp)) or 1.0
            perp /= npn
            off = float(dis[min(ARC_STEPS, int(round(t * ARC_STEPS)))]) * math.sin(math.pi * t)
            q = p + perp * off
            q /= max(1e-6, float(np.linalg.norm(q)))
            if abs(q[2]) > POLE_LIMIT:
                ok = False
                break
            pts.append(q)
        if ok:
            return pts
    return pts if pts else [a]


def project(p):
    """3D 单位向量 → 等距柱状 (u, v)，口径与 build_sphere_shell.py 的 uv_sphere() 一致。"""
    u = (math.atan2(float(p[1]), float(p[0])) / (2.0 * math.pi)) % 1.0
    v = 1.0 - math.acos(float(np.clip(p[2], -1.0, 1.0))) / math.pi
    return u, v


def draw_frame(canvas, rng):
    """一帧：N 条弧。

    🔴 **用距离场画，不是逐个点叠高斯**（2026-09-25 踩过）：
       沿路径 96 个点反复叠高斯 ⇒ 整张画布被涂满、峰值冲到 12000+（严重过曝）。
       正确做法 = ① 把路径画成 **1px 线** ② 算「每个像素到最近线的距离」
       ③ 按距离**套一次**三层剖面 ⇒ 不累积，峰值 = 设定的增益。
    """
    from scipy import ndimage
    from PIL import ImageDraw
    h, w = canvas.shape[:2]
    for _ in range(N_ARCS):
        pts = arc_path(rng)
        uv = [project(p) for p in pts]
        uu = np.array([u for u, _ in uv])
        vv = np.array([v for _, v in uv])
        # 🔴 经度**先解绕**（把 ±1 的跨越折成连续值）：贴图 u 是环形的，跨接缝的弧
        #    如果照原样画，会从 0.98 一路倒着拉回 0.02 = 一条横贯全图的错线。
        du = np.diff(uu)
        du = (du + 0.5) % 1.0 - 0.5
        u_un = np.concatenate([[uu[0]], uu[0] + np.cumsum(du)])
        py = (1.0 - vv) * (h - 1)
        # 🔴 解绕后仍跳变 > 0.25 ⇒ 那是**极点**造成的真断点（经度本身就无意义）⇒ 必须断成两段画
        breaks = np.where(np.abs(du) > 0.25)[0] + 1
        segs, start = [], 0
        for b in list(breaks) + [len(u_un)]:
            if b - start >= 2:
                segs.append((start, b))
            start = b
        img = Image.new("L", (w * 3, h), 0)
        dr = ImageDraw.Draw(img)
        for s, e in segs:
            xs, ys = (u_un[s:e] * w), py[s:e]
            if xs.max() - xs.min() > 1.5 * w:      # 那段横跨超过一圈 = 垃圾段，丢掉
                continue
            for shift in (0.0, w, 2.0 * w):
                dr.line(list(zip((xs + shift).tolist(), ys.tolist())),
                        fill=255, width=1, joint="curve")
        mask = np.asarray(img)[:, w:2 * w] > 0
        dist = ndimage.distance_transform_edt(~mask)
        # ② 按距离套一次三层剖面（**逐弧单独做** ⇒ 不累积、峰值可控，且每条弧亮度不同）
        g = rng.uniform(*GAIN_RANGE)
        for sigma, gain, color in ((CORE_W * w, CORE_GAIN * g, C_CORE),
                                   (MID_W * w, MID_GAIN * g, C_MID),
                                   (GLOW_W * w, GLOW_GAIN * g, C_GLOW)):
            p = np.exp(-(dist / max(1e-6, sigma)) ** 2) * gain
            canvas += p[:, :, None] * color[None, None, :]
    return canvas


def build_atlas():
    aw, ah = CELL_W * COLS, CELL_H * ROWS
    atlas = np.zeros((ah, aw, 3), dtype=np.float32)
    rng = np.random.default_rng(SEED)
    for r in range(ROWS):
        for c in range(COLS):
            cell = np.zeros((CELL_H, CELL_W, 3), dtype=np.float32)
            draw_frame(cell, rng)
            atlas[r * CELL_H:(r + 1) * CELL_H, c * CELL_W:(c + 1) * CELL_W] = cell
            print(f"    格 [{c},{r}]  峰值 {cell.max():6.1f}  非黑占比 {(cell.max(2) > 8).mean() * 100:4.1f}%")
    rgb = np.clip(atlas, 0.0, 255.0).astype(np.uint8)
    return Image.fromarray(rgb, "RGB")


def preview_sheet(im):
    a = np.asarray(im).copy()
    for c in range(1, COLS):
        a[:, c * CELL_W - 1:c * CELL_W + 1] = (255, 60, 60)
    for r in range(1, ROWS):
        a[r * CELL_H - 1:r * CELL_H + 1, :] = (255, 60, 60)
    return Image.fromarray(a, "RGB")


def build_ring():
    """静态赤道带 —— 壳的"形状"就是它，不含任何电弧。

    等距柱状布局：**行 = v（1=北极 在图像顶行，0=南极 在底行）**，列 = 经度 u。
    赤道带 ⇒ 图像正中间一条横带；从球外看，近侧与远侧投到同一圈 = **一个椭圆环** ✓
    """
    h, w = RING_H, RING_W
    v = (1.0 - (np.arange(h) / (h - 1.0)))[:, None]        # 图像顶行 v=1（北极）
    u = (np.arange(w) / w)[None, :]
    # 赤道带（v 方向高斯）× 沿经度的明暗波瓣（免得像个塑料圈）
    band = np.exp(-((v - RING_V) / RING_SIGMA) ** 2) * RING_GAIN
    if RING_LOBES > 0:
        band = band * (1.0 - RING_LOBE_AMP * (0.5 - 0.5 * np.cos(2 * np.pi * RING_LOBES * u)))
    # 整球淡底（两极稍暗）
    fill = FILL * (FILL_POLE_FADE + (1.0 - FILL_POLE_FADE) * (1.0 - np.abs(2.0 * v - 1.0)))
    a = np.clip(band + fill, 0.0, 1.0)
    rgb = (a[:, :, None] * np.array([200.0, 240.0, 255.0])[None, None, :] / 255.0 * 255.0)
    print(f"    静态赤道带 {w}×{h} · 带心 v={RING_V} σ={RING_SIGMA} 亮度 {RING_GAIN:.2f} · "
          f"淡底 {FILL:.3f} · 峰值 {rgb.max():.0f}")
    return Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8), "RGB")


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    print(f"=== 电罩球壳贴图（模式 {MODE}）===")
    if MODE == "ring":
        im = build_ring()
        os.makedirs(OUTDIR, exist_ok=True)
        raw = os.path.join(OUTDIR, "_raw_" + NAME)
        im.save(raw)
        staged = os.path.join(OUTDIR, NAME)
        subprocess.run([sys.executable, PNG_FOR_EDITOR, raw, staged], check=True)
        os.remove(raw)
        print(f"    产出 {staged}")
        _deliver(staged)
        print("\n材质 `lwn_lightning_shell` 配方（**静态，不需要翻页**）：")
        print("    Shader pbr_translucent · Textures Diffuse1 = 本图")
        print("    ☐ use_animated_texture_coords / ☐ use_texture_sweep / ☐ self_illumination")
        print("      ⇒ 🔴 静态图**不占** Vector Argument 1：想要自发光就勾 `self_illumination`（发光图放 Diffuse 2）")
        print("    Vector Arguments 全 0 · Transparency → Alpha Blend Mode = Add")
        return 0
    return _main_arcs()


def _deliver(staged):
    """交付：已注册 ⇒ 覆盖镜像；没导入过 ⇒ 放待导入目录。"""
    if os.path.isfile(REGISTERED_MARK):
        shutil.copy2(staged, MIRROR)
        os.utime(MIRROR, None)
        print(f"    资产已注册 ⇒ 覆盖镜像 {MIRROR}（mtime 已刷新）")
        print(f"    判据 = {REGISTERED_MARK} 的 mtime 变新")
    else:
        os.makedirs(STAGE_DIR, exist_ok=True)
        shutil.copy2(staged, os.path.join(STAGE_DIR, NAME))
        print(f"    资产未注册 ⇒ 放入待导入目录 {STAGE_DIR}")


def _main_arcs():
    print(f"    图集 {CELL_W * COLS}×{CELL_H * ROWS} · {COLS}×{ROWS} = {COLS * ROWS} 帧 · "
          f"{FPS:.0f} 帧/秒（一轮 {COLS * ROWS / FPS:.2f} s）· 每帧 {N_ARCS} 条弧")
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
    _deliver(staged)
    print("\n材质 `lwn_lightning_shell`（Import 网格时自动建的）配方：")
    print("    Shader pbr_translucent · Textures Diffuse1 = 本图")
    print("    ☑ use_animated_texture_coords（☐ 另两个）· Vector1 = "
          f"({COLS}, {ROWS}, {FPS:.0f}, {COLS * ROWS}) · Vector2 全 0")
    print("    Transparency → Alpha Blend Mode = Add")
    return 0


if __name__ == "__main__":
    sys.exit(main())
