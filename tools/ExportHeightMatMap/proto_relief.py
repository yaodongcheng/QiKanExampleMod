# -*- coding: utf-8 -*-
"""proto_relief.py — 高度图细节 v2 原型（山脊多倍频褶皱合成；实验性沙盒，正式管线在 make_heightmap.py）

目标：日本图高度细节达到 NativeExample（卡拉迪亚原版 4097² 高度图）的山脊等级。
手段：宏观形态（山域/平原/海岸线）保留原管线（h_base 亮度分档 + PEAKS + 雪帽），
      细碎褶皱改为「结构感知域扭曲 + 山脊化 fBm」，替代旧「单尺度亮度差分（blob 化）」。
用法: python proto_relief.py [out_w out_h]
"""
import os
import sys
import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage

Image.MAX_IMAGE_PIXELS = None

BASE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(BASE, "SourceMap", "japanmap_hires.png")
OUTD = os.path.join(BASE, "Output", "_probe")
os.makedirs(OUTD, exist_ok=True)

TARGET_RATIO = 2048 / 1280.0
LUM_SEA_MAX = 165.0
MASTER_W = 15840
HEIGHT_GAMMA = 1.4
SEED = 20260906          # 固定种子 → 可复现

# ---------- 复用正式管线的判据（与 make_heightmap.py 同源） ----------
def classify(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    lum = 0.299 * r + 0.587 * g + 0.114 * b
    blueish = (100 * b > 115 * r) & (100 * g > 102 * r) & (b >= 100)
    sea = blueish & (lum <= LUM_SEA_MAX)
    lowsat = (np.abs(r - g) < 45) & (np.abs(g - b) < 45)
    bluewhite = (b > r * 1.05) & (lum > 160) & ~sea
    white = (((lowsat & (lum > 155)) | bluewhite) & ~sea & (g <= b + 10))
    yellow = (r - b > 50) & (g - b > 30) & (r > 150) & (g > b * 1.1) & ~sea
    snow_dot = (lum > 168) & (np.abs(r - g) < 40) & (g <= b + 25)
    return sea, white, yellow, snow_dot


# ---------- 值噪声 / 山脊化 fBm ----------
def vnoise(rng, shape, cell, warp=None):
    """value noise：格点 cell px 的随机格 + 双线性平滑（map_coordinates 实现，快且省内存）"""
    H, W = shape
    gw, gh = max(2, int(round(W / cell)) + 2), max(2, int(round(H / cell)) + 2)
    lat = rng.random((gh, gw)).astype(np.float32)
    xs = (np.arange(W, dtype=np.float32) / cell)[None, :]
    ys = (np.arange(H, dtype=np.float32) / cell)[:, None]
    if warp is not None:
        wy, wx = warp
        xs = np.broadcast_to(xs + wx / cell, (H, W))
        ys = np.broadcast_to(ys + wy / cell, (H, W))
    else:
        xs = np.broadcast_to(xs, (H, W))
        ys = np.broadcast_to(ys, (H, W))
    return ndimage.map_coordinates(lat, [ys, xs], order=1, mode='wrap', prefilter=False)


def fbm(rng, shape, base_cell, octaves=4, gain=0.5, warp=None):
    acc = np.zeros(shape, np.float32)
    amp, tot, cell = 1.0, 0.0, base_cell
    for _ in range(octaves):
        acc += amp * vnoise(rng, shape, cell, warp)
        tot += amp
        amp *= gain
        cell *= 0.5
    return acc / tot


def ridged_fbm(rng, shape, base_cell, octaves=4, warp=None, sharp=2.0):
    """r = 1 - |2n-1| 的山脊化噪声：脊线亮、谷线暗；sharp=幂指数(越大越细锐)"""
    n = fbm(rng, shape, base_cell, octaves, 0.5, warp)
    r = 1.0 - 2.0 * np.abs(n - 0.5)
    return r ** sharp


def build(out_w, out_h):
    im = Image.open(SRC).convert("RGB")
    SRC_W, SRC_H = im.size
    a = np.asarray(im).astype(np.int16)
    sea, white, yellow, snow_dot = classify(a)

    # —— 与正式管线一致的语义掩膜（尺度标定：源图即 15840 主线尺度） ——
    sea_block = ndimage.binary_opening(sea, np.ones((5, 5)), iterations=2)
    river = sea & ~sea_block
    sea = sea_block
    labd, ndd = ndimage.label(snow_dot)
    if ndd:
        sizes_d = ndimage.sum(snow_dot, labd, range(1, ndd + 1))
        maxd = float(sizes_d.max())
        big_dot = np.zeros_like(snow_dot)
        for i in range(ndd):
            if sizes_d[i] >= 0.3 * maxd:
                big_dot |= labd == i + 1
        dot_zone = ndimage.binary_dilation(big_dot, np.ones((9, 9)), iterations=6)
        snowcap = sea & dot_zone
    else:
        snowcap = np.zeros_like(sea)
    sea = sea & ~snowcap
    white = ndimage.binary_closing(white, np.ones((5, 5)), iterations=1)
    w_open = ndimage.binary_opening(white, np.ones((5, 5)), iterations=2)
    white_big = w_open
    dist_sea = ndimage.distance_transform_edt(~sea).astype(np.float32)
    sea_final = sea & ~snowcap

    def softmask(m):
        return np.asarray(Image.fromarray((m * 255).astype(np.uint8))
                          .resize((out_w, out_h), Image.BOX), dtype=np.float32) / 255.0

    M = {
        'sea': softmask(sea_final),
        'white': softmask(white_big),
        'cap': softmask(snowcap),
        'dist': np.asarray(Image.fromarray(
            np.clip(dist_sea / 8192.0, 0, 1).astype(np.float32)).resize((out_w, out_h), Image.BOX),
            np.float32) * 8192.0,
    }
    del sea, sea_block, river, white, w_open, dist_sea
    ap = np.asarray(im.resize((out_w, out_h), Image.LANCZOS)).astype(np.float32)
    r, g, b = ap[..., 0], ap[..., 1], ap[..., 2]
    lum = 0.299 * r + 0.587 * g + 0.114 * b
    # 山块级明暗（大平滑σ：源图=200px → 输出按比例；专供 h_base 分段，过滤画法斑点/林冠/云影）
    sig_big = 200.0 * out_w / SRC_W
    lum_big = np.asarray(Image.fromarray(np.clip(lum, 0, 255).astype(np.uint8))
                         .filter(ImageFilter.GaussianBlur(sig_big))).astype(np.float32)

    # ---------- 宏观形态 v4：亮度先大平滑再去分档（关键修复） ----------
    # v3 诊断（分量实测）：h_base p50=0.46 → hm 山域 ≈0.34 平台 + 暗斑 ±0.15m 级摆动 ——
    # 「暗斑」源头 = 源图装饰性明暗斑点（勾线/林冠/云影）经 h_base 分段直接当高度；
    # 画法明暗 ≠ 海拔（装饰纹理），必须先行大平滑仅留「山块级」明暗再分段。
    h_base = np.where(
        lum_big >= 150, np.clip(0.22 - (lum_big - 150) * 0.0018, 0.10, 0.22),
        np.where(lum_big >= 110, 0.38 + (150 - lum_big) * 0.0020,
                 0.455 + np.clip(110 - lum_big, 0, 60) * 0.0015))
    softlum = np.asarray(Image.fromarray(lum.astype(np.uint8))
                         .filter(ImageFilter.GaussianBlur(25))).astype(np.float32)
    g = np.clip((168.0 - softlum) / 45.0, 0, 1)
    h_mac = h_base
    h_white = 0.05 + np.clip(M['dist'] / 600.0, 0, 1) * 0.50
    h_white = np.clip(h_white, 0.05, 0.95)
    greenw = ((g > r * 1.02) & (g >= b * 0.95)).astype(np.float32)
    w_land = np.clip(1.0 - M['sea'] * 1.5, 0, 1)
    hm = h_white * M['white'] * w_land + h_mac * greenw * w_land + h_mac * np.clip(1 - greenw - M['white'], 0, 1) * w_land
    hm = np.where((M['sea'] >= 0.5) & (M['cap'] < 0.5), 0.0, hm)
    h_cap = 0.60 + 0.30 * softmask(snow_dot)
    hm = np.maximum(hm, h_cap * M['cap'])
    PEAKS = [(10500, 7000, 320, 1.00, 0.42, 800)]
    ys, xs = np.mgrid[0:out_h, 0:out_w]
    peak_zone = np.zeros((out_h, out_w), np.float32)
    peak_cone = np.zeros((out_h, out_w), np.float32)
    for cx, cy, R, H, B, RB in PEAKS:
        px = cx * out_w / SRC_W
        py = cy * out_h / SRC_H
        sc = R * out_w / SRC_W
        d = np.sqrt((xs - px) ** 2 + (ys - py) ** 2) / sc
        cone = H * np.clip(1.0 - d, 0, 1) ** 1.2
        body = B * np.clip(1.0 - d * (R / RB), 0, 1) ** 0.8
        hm = np.maximum(hm, np.maximum(cone, body))
        peak_zone = np.maximum(peak_zone, (np.maximum(cone, body) > 0.03).astype(np.float32))
        peak_cone = np.maximum(peak_cone, (cone > 0.35 * H).astype(np.float32))
    # 宏观 gamma 压平（富士独大）：先压平再叠细节，后续 env 直接引用
    hm_m = np.power(hm, HEIGHT_GAMMA)

    # ---------- 结构场：源图山影的方向性纹脉（多尺度带通） ----------
    # 源图笔画：山脊亮笔 / 谷底暗笔 → 带通差分的正负极对应脊/谷
    def band(lum8, sig_src):
        sig = sig_src * out_w / SRC_W
        base = np.asarray(Image.fromarray(lum8).filter(ImageFilter.GaussianBlur(sig))).astype(np.float32)
        return lum - base

    lum8 = np.clip(lum, 0, 255).astype(np.uint8)
    S_fine = band(lum8, 30)    # 细笔画脊谷（~7.8px 输出）
    S_mid = band(lum8, 75)     # 中笔画（~19px）
    # 归一化 → 域扭曲场（结构对齐）
    def norm(u):
        s = float(u.std())
        return np.clip(u / (2.5 * s), -1, 1) if s > 1e-6 else u * 0

    Ws = ndimage.gaussian_filter(norm(S_mid), 6.0).astype(np.float32)      # 结构扭曲场 X

    # ---------- 崎岖度包络：高度驱动（v4：smoothstep 过渡 0.08~0.30）+ 坡增比 ----------
    slope = ndimage.gaussian_filter(np.sqrt(
        (np.gradient(hm)[1]) ** 2 + (np.gradient(hm)[0]) ** 2), 3.0)
    env = np.clip((hm_m - 0.08) / 0.22, 0, 1) ** 1.3             # 平原(≤0.08)=0 → 山域(≥0.30)=1
    env = env * (0.75 + 0.25 * np.clip(slope / 0.006, 0, 1))  # 坡增比（基线抬高：山地平台也全细节）
    env = env * np.clip(M['dist'] / 120.0, 0.1, 1.0)             # 近海平滑（滩岩海岸不皱）
    env = np.clip(env, 0, 1)

    # ---------- 结构感知山脊噪声 v6.12（半分辨软烤版） ----------
    # 方向场 = 源图笔触（画中山脊描线）梯度方向（v6.4 结论）——方向随笔画快速变化 = 有机顺纹；
    # 细节场以 1/2 分辨率生成再双线性放大 → 软润颗粒（Native 质感），且消除 1-2px 脆毛。
    gsy_, gsx_ = np.gradient(Ws)
    gg = np.sqrt(gsx_ ** 2 + gsy_ ** 2) + 1e-6
    Dx = ndimage.gaussian_filter(gsx_ / gg, 1.5)
    Dy = ndimage.gaussian_filter(gsy_ / gg, 1.5)
    rng = np.random.default_rng(SEED)
    warp_y = fbm(rng, (out_h, out_w), 26.0, 3, 0.5) - 0.5
    K = 5.0

    he, we = max(2, out_h // 2), max(2, out_w // 2)
    def _hs(arr):
        """-1..1 → 半分辨率（BILINEAR 放大/缩小共用）"""
        u8 = (np.clip(arr, -1, 1) * 127.5 + 127.5).astype(np.uint8)
        return np.asarray(Image.fromarray(u8).resize((we, he), Image.BILINEAR),
                          np.float32) / 127.5 - 1.0

    Gx_h = np.broadcast_to(np.arange(we, dtype=np.float32)[None, :], (he, we))
    Gy_h = np.broadcast_to(np.arange(he, dtype=np.float32)[:, None], (he, we))
    Dx_h, Dy_h = _hs(Dx), _hs(Dy)
    Uo = Gx_h * Dx_h + Gy_h * Dy_h                         # 笔触方向（顺纹轴）
    Vo = Gx_h * (-Dy_h) + Gy_h * Dx_h                      # 垂直笔触方向
    Ws_h, warp_h = _hs(Ws), _hs(warp_y)

    def aniso_ridged(base_cell, octv, sb, sd, s=1.0):
        """v6.13 整域山脊变换（程序化地形金标准）：先多倍频 fbm 平滑场 → 一次 (1-|2n-1|) 变换——
        |n-0.5| 过零曲线 = 连通的蜿蜒细带（排水纹），比「逐倍频独立山脊再叠加」天然连通，
        不再产生孤立碎斑。sb 小→亮丘宽；sd 大→暗沟窄"""
        acc = np.zeros((he, we), np.float32); amp, tot = 1.0, 0.0
        lat_cell = base_cell
        U = Uo / s + K * Ws_h
        V = Vo + K * warp_h
        for _ in range(octv):
            gw_lat = max(3, int(U.max() / lat_cell) + 3)
            gh_lat = max(3, int(V.max() / lat_cell) + 3)
            lat = rng.random((gh_lat, gw_lat)).astype(np.float32)
            n = ndimage.map_coordinates(lat, [V / lat_cell, U / lat_cell], order=1,
                                        mode='wrap', prefilter=False)
            acc += amp * n
            tot += amp
            amp *= 0.5
            lat_cell *= 0.5
        n = acc / tot
        r = 1.0 - 2.0 * np.abs(n - 0.5)
        return r ** sb - 1.15 * (1.0 - r) ** sd

    # 频率分配（半分辨格；全分辨率对应 ×2）：主 28(56) 中 8(16) 微 3(6)；
    # 宏脊 80(160 窗内坡度) + 山块 170(340 山块差异)
    cfg = [("main", 36.0, 5, 0.22, 1.9, 2.0, 0.80),
           ("mid", 9.0, 3, 0.30, 2.4, 1.0, 0.16),
           ("micro", 3.5, 2, 0.40, 2.8, 1.0, 0.04)]
    totw = sum(c[6] for c in cfg)
    det = np.zeros((he, we), np.float32)
    for name, base_cell, octv, shp_b, shp_d, s, wt in cfg:
        r = aniso_ridged(base_cell, octv, shp_b, shp_d, s)
        det += wt * r / totw
    meg = aniso_ridged(80.0, 4, 0.22, 2.0, 2.2)
    massif = aniso_ridged(170.0, 3, 0.20, 2.0, 1.5)
    det = det * 0.50 + 0.26 * meg + 0.24 * massif
    det = det - float(det.mean())
    det = np.clip(det, -0.36, 0.34)
    det = np.where(det > 0, det * 1.75, det * 0.95)      # 亮侧补强（Native 峰丘亮、沟深）
    # 半格 → 全格（BILINEAR 软放大；负数通道用统一映射保真）
    det = (det + 0.36) / 0.70
    det = np.asarray(Image.fromarray((np.clip(det, 0, 1) * 255).astype(np.uint8))
                     .resize((out_w, out_h), Image.BILINEAR), np.float32) / 255.0 * 0.70 - 0.36
    massif = (massif - massif.min()) / max(1e-6, massif.max() - massif.min())
    massif = np.asarray(Image.fromarray((np.clip(massif, 0, 1) * 255).astype(np.uint8))
                        .resize((out_w, out_h), Image.BILINEAR), np.float32) / 255.0
    AMP = 0.62
    AMP_M = 0.42            # 山块层振幅（窗级 std 目标 ≥0.055；Native 0.07~0.16）
    d = env * (det * AMP + (massif - massif.mean()) * AMP_M)

    # ---------- 合成：宏观 gamma 已算（hm_m），叠加细节 ----------
    hm2 = hm_m + d
    hm2 = np.where((M['sea'] >= 0.5) & (M['cap'] < 0.5) & (peak_zone < 0.5), 0.0, hm2)
    hm2 = np.clip(hm2, 0, 1)

    # ---------- 平滑 + 16bit ----------
    hm8 = Image.fromarray((hm2 * 255).astype(np.uint8)).filter(ImageFilter.MedianFilter(5))
    hm8 = hm8.filter(ImageFilter.GaussianBlur(1.5))
    hmv_img = np.asarray(hm8).astype(np.float32) / 255.0
    hmv = (hmv_img * 65535.0).astype(np.uint16)
    out16 = os.path.join(OUTD, f"hmv2_{out_w}x{out_h}_16bit.png")
    Image.fromarray(hmv).save(out16)
    print(f"[out] {out16}")

    # ---------- QA 对比图（上=分类着色 下=灰度高度 与正式管线同风格） ----------
    gw = (np.clip(hm2 / 0.55, 0, 1) * 200).astype(np.uint8)
    qa = np.dstack([gw, gw, gw])
    qa[np.where(M['sea'] >= 0.5)] = (30, 80, 150)
    rock = (M['white'] >= 0.5) & (M['dist'] > 150) & (M['dist'] <= 400)
    qa[np.where(M['white'] >= 0.5)] = (245, 245, 245)
    qa[np.where(rock)] = (160, 150, 140)
    qa[np.where(M['cap'] >= 0.5)] = (255, 250, 230)
    terr = np.stack([(hm2 * 0.9 * 255).astype(np.uint8)] * 3, -1)
    Image.fromarray(np.concatenate([qa, terr], 0)).save(os.path.join(OUTD, f"qa_v2_{out_w}x{out_h}.png"))

    # 与 Native 同区裁剪对比（alps + 中部山域）
    jc = Image.fromarray((hm2 * 255).astype(np.uint8)).crop(
        (int(9300 / SRC_W * out_w), int(5400 / SRC_H * out_h),
         int(12300 / SRC_W * out_w), int(7400 / SRC_H * out_h))).resize((1024, 640), Image.LANCZOS)
    jc.save(os.path.join(OUTD, f"v2_alps_{out_w}x{out_h}.png"))

    # ===== 组件诊断（alps 同区，四联：h_base / macro hm / env / det） =====
    if os.environ.get("PROTO_DIAG"):
        def crop_norm(arr):
            c = arr[int(5400 / SRC_H * out_h):int(7400 / SRC_H * out_h),
                    int(9300 / SRC_W * out_w):int(12300 / SRC_W * out_w)]
            c = (c - c.min()) / max(1e-6, c.max() - c.min())
            return np.asarray(Image.fromarray((c * 255).astype(np.uint8)).resize((1024, 640), Image.LANCZOS))
        diag = [("h_base", h_base), ("macro_hm", hm), ("hm_m", hm_m), ("env", env), ("det", det), ("d", d)]
        tiles = []
        for name, arr in diag:
            fmt = np.clip(np.nan_to_num(arr), 0, 1) if arr.dtype == np.float32 or arr.dtype == np.float64 else arr
            tiles.append((name, crop_norm(np.asarray(fmt)) if name in ("env", "det", "d", "h_base") else crop_norm(np.asarray(fmt))))
        imgs = []
        for name, cur in tiles:
            lab = np.zeros((24, 1024), np.uint8)
            imgs.append(Image.fromarray(np.concatenate([lab, cur], 0)))
        W = 1024
        H = sum(im.size[0] for im in imgs)
        big = Image.new("L", (W, H))
        y = 0
        for im in imgs:
            big.paste(im, (0, y)); y += im.size[0]
        big.save(os.path.join(OUTD, "diag_v3_alps.png"))
        # 数值摘要
        for name, arr in [("h_base", h_base), ("hm", hm), ("hm_m", hm_m), ("env", env), ("det", det), ("d", d)]:
            print(f"[diag] {name}: min={arr.min():.3f} p10={np.percentile(arr, 10):.3f} p50={np.percentile(arr, 50):.3f} p90={np.percentile(arr, 90):.3f} max={arr.max():.3f}")
    return out16


if __name__ == "__main__":
    sizes = [(2048, 1280)] if len(sys.argv) < 3 else [(int(sys.argv[1]), int(sys.argv[2]))]
    for w, h in sizes:
        print(f"===== {w}x{h} =====")
        build(w, h)
