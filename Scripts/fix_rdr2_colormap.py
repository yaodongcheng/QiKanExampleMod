# -*- coding: utf-8 -*-
"""彩图修补：把坏瓦片区域用「同高程 / 同水系」的邻近纹理**绗缝**补掉。

原理：
  · 只从**周边同样地貌**的区域取小块（64 px），按高度图筛同高程带、按水网剔除水面；
  · 每块做**色调匹配**（均值/标准差对齐到洞边缘环带）；
  · 块之间 50% 重叠 + 线性羽化 ⇒ 没有硬边、没有色块感；
  · 不做"按高程重刷纯色"（那是以前失败的做法：出平色块 + 硬边界）。

用法：
  python Scripts/fix_rdr2_colormap.py --rect 4759,6331,5272,6844 --out <输出png> [--preview <对比png>]
                                    [--method mirror|quilt]   # 默认 mirror+quilt 混合
  坐标 = color_1m.png 的 (行0, 列0, 行1, 列1)，行 0 = 北。
"""
import argparse
import io
import os
import sys

import numpy as np
from PIL import Image

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
Image.MAX_IMAGE_PIXELS = None

BASE = r"D:\BrainMaker\大镖客2解包分析\deliverable\terrain"
COLOR = os.path.join(BASE, "color_1m.png")
HEIGHT = os.path.join(BASE, "stages", "01_plateau_plain", "height_16bit_4m.png")
HMETA = os.path.join(BASE, "stages", "01_plateau_plain", "meta.json")
WATER = os.path.join(BASE, "water_mask.png")


def load_height(h=None, w=None):
    import json
    meta = json.load(io.open(HMETA, encoding="utf-8"))
    zmin, zmax = meta["height"]["zmin"], meta["height"]["zmax"]
    a = np.asarray(Image.open(HEIGHT), dtype=np.float32)
    z = zmin + a / 65535.0 * (zmax - zmin)
    return z, meta


def quilt(a, hole, src_mask, z, water, block=64, overlap=32, seed=7, ring_rect=None):
    """把 hole 区域用 src_mask 允许的像素绗缝填满（按块 + 羽化）。"""
    rng = np.random.default_rng(seed)
    r0, c0, r1, c1 = hole
    H, W = r1 - r0, c1 - c0
    out = a.copy()
    target = out[r0:r1, c0:c1].astype(np.float32)

    # 参考色调：默认用「洞外环带」；给了 ring_rect 就用它（洞边更贴近真实过渡）
    pad = 16
    rr0, rc0, rr1, rc1 = ring_rect if ring_rect else (r0, c0, r1, c1)
    ring = np.concatenate([
        a[max(0, rr0 - pad):rr0, rc0:rc1].reshape(-1, 3), a[rr1:rr1 + pad, rc0:rc1].reshape(-1, 3),
        a[rr0:rr1, max(0, rc0 - pad):rc0].reshape(-1, 3), a[rr0:rr1, rc1:rc1 + pad].reshape(-1, 3)]).astype(np.float32)
    tgt_mu, tgt_sd = ring.mean(axis=0), ring.std(axis=0) + 1e-3

    step = block - overlap
    ys = list(range(0, H, step))
    xs = list(range(0, W, step))
    acc = np.zeros((H, W, 3), np.float32)
    wsum = np.zeros((H, W, 1), np.float32)
    yy, xx = np.mgrid[0:block, 0:block]
    wgt = np.minimum.reduce([yy, xx, block - 1 - yy, block - 1 - xx]).astype(np.float32)
    wgt = np.clip(wgt / (overlap + 1e-6), 0.02, 1.0)[..., None]

    A_ = a.astype(np.float32)
    ring_mu = ring.mean(axis=0)
    ring_sd_scalar = float(ring.std())
    # 源池：洞外 2.5 km 内、同高程带、非水、外观相近；全部在**彩图 1 m/px 网格**上算
    R = 2500
    SR = 4
    sr0, sr1 = max(0, r0 - R), min(a.shape[0], r1 + R)
    sc0, sc1 = max(0, c0 - R), min(a.shape[1], c1 + R)
    # 高程升到 1 m/px（最近邻）
    hz0, hz1 = sr0 // SR, min(z.shape[0], sr1 // SR + 1)
    wz0, wz1 = sc0 // SR, min(z.shape[1], sc1 // SR + 1)
    z1 = np.repeat(np.repeat(z[hz0:hz1, wz0:wz1], SR, axis=0), SR, axis=1)
    z1 = z1[:sr1 - sr0, :sc1 - sc0]
    rr0, rr1 = (r0 - sr0) // SR, max((r0 - sr0) // SR + 1, (r1 - sr0) // SR)
    cc0, cc1 = (c0 - sc0) // SR, max((c0 - sc0) // SR + 1, (c1 - sc0) // SR)
    ring_z = np.concatenate([z[hz0 + rr0 - 1:hz0 + rr0, wz0 + cc0:wz0 + cc1].ravel(),
                             z[hz0 + rr1:hz0 + rr1 + 1, wz0 + cc0:wz0 + cc1].ravel(),
                             z[hz0 + rr0:hz0 + rr1, wz0 + cc0 - 1:wz0 + cc0].ravel(),
                             z[hz0 + rr0:hz0 + rr1, wz0 + cc1:wz0 + cc1 + 1].ravel()])
    z_mu = float(np.nanmedian(ring_z))
    step_s = 32
    cand = []
    for yy0 in range(0, max(1, z1.shape[0] - block), step_s):
        for xx0 in range(0, max(1, z1.shape[1] - block), step_s):
            zb = z1[yy0:yy0 + block, xx0:xx0 + block]
            if zb.shape[0] < block or zb.shape[1] < block:
                continue
            # 🔴 必须排除「洞及其外扩圈」本身，否则会把坏瓦片自己再抽回来当样本
            gy, gx = sr0 + yy0, sc0 + xx0
            if not (gy + block <= r0 - 64 or gy >= r1 + 64 or gx + block <= c0 - 64 or gx >= c1 + 64):
                continue
            if abs(float(zb.mean()) - z_mu) > 4.0:
                continue
            # 外观相近：块颜色接近洞边环带、纹理强度接近（避免抽到绿植/岩纹）
            blk_c = A_[sr0 + yy0:sr0 + yy0 + block, sc0 + xx0:sc0 + xx0 + block]
            if abs(blk_c.reshape(-1, 3).mean(axis=0) - ring_mu).mean() > 10.0:
                continue
            sd_b = float(blk_c.reshape(-1, 3).std())
            if not (0.6 * ring_sd_scalar <= sd_b <= 1.8 * ring_sd_scalar):
                continue
            if water is not None:
                wb = water[sr0 + yy0:sr0 + yy0 + block, sc0 + xx0:sc0 + xx0 + block]
                if wb.size and wb.mean() > 0.02:
                    continue
            cand.append((yy0, xx0))
    cand = np.array(cand)
    print("  源池候选块 = %d（高程带 %.1f ±4 m）" % (len(cand), z_mu))
    if len(cand) == 0:
        cand = np.array([[0, 0]])

    for i, y in enumerate(ys):
        for j, x in enumerate(xs):
            y2, x2 = min(y + block, H), min(x + block, W)
            by, bx = y2 - y, x2 - x
            for _ in range(20):
                sy, sx = cand[rng.integers(len(cand))]
                py, px = sr0 + int(sy), sc0 + int(sx)
                blk = a[py:py + block, px:px + block].astype(np.float32)
                if blk.shape[0] < block or blk.shape[1] < block:
                    continue
                blk = blk[:by, :bx]
                mu = blk.reshape(-1, 3).mean(axis=0)
                blk = blk - mu + tgt_mu
                blk = np.clip(blk, 0, 255)
                break
            acc[y:y2, x:x2] += blk * wgt[:by, :bx]
            wsum[y:y2, x:x2] += wgt[:by, :bx]
    fill = np.clip(acc / np.maximum(wsum, 1e-6), 0, 255).astype(np.float32)
    orig = a[r0:r1, c0:c1].astype(np.float32)

    # ① 四边色差校正：在洞外 24 px 带量 (原图 − 填充) 的均值，再在洞内做双线性渐变补正
    band = 24
    ext = np.zeros((H + 2 * band, W + 2 * band, 3), np.float32)
    ext[band:band + H, band:band + W] = fill
    # 把已填充内容贴到扩展画布，外面留原图，量四条带上的差
    canvas = a[r0 - band:r1 + band, c0 - band:c1 + band].astype(np.float32).copy()
    filled_canvas = canvas.copy()
    filled_canvas[band:band + H, band:band + W] = fill
    d_top = (canvas[0:band, band:band + W] - filled_canvas[0:band, band:band + W]).mean(axis=(0, 1))
    d_bot = (canvas[-band:, band:band + W] - filled_canvas[-band:, band:band + W]).mean(axis=(0, 1))
    d_left = (canvas[band:band + H, 0:band] - filled_canvas[band:band + H, 0:band]).mean(axis=(0, 1))
    d_right = (canvas[band:band + H, -band:] - filled_canvas[band:band + H, -band:]).mean(axis=(0, 1))
    yy = np.linspace(0, 1, H, dtype=np.float32)[:, None, None]
    xx = np.linspace(0, 1, W, dtype=np.float32)[None, :, None]
    corr = (d_top * (1 - yy) + d_bot * yy) * 0.5 + (d_left * (1 - xx) + d_right * xx) * 0.5
    fill = np.clip(fill + corr, 0, 255)

    out[r0:r1, c0:c1] = np.clip(fill, 0, 255).astype(np.uint8)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rect", required=True, help="行0,列0,行1,列1")
    ap.add_argument("--out", required=True)
    ap.add_argument("--preview", default="")
    ap.add_argument("--pad", type=int, default=0)
    ap.add_argument("--method", choices=["mirror", "quilt"], default="mirror",
                    help="mirror=四向镜像延展（结构连续、无条纹，默认）；quilt=同高程块绗缝")
    a = ap.parse_args()
    r0, c0, r1, c1 = [int(v) for v in a.rect.split(",")]

    try:
        z, _meta = load_height(None, None)
        water = None
        if os.path.exists(WATER):
            wm = np.asarray(Image.open(WATER).convert("L"))
            if wm.shape[0] == z.shape[0] * 4:      # water 1 m/px vs height 4 m/px
                wm = wm[::4, ::4]
            water = wm > 127
    except Exception as e:
        print("读高度/水网失败（%s），改为不限高程" % e)
        z = np.zeros((1, 1), np.float32)
        water = None
        globals()["quilt_noh"] = True

    a_img = np.asarray(Image.open(COLOR).convert("RGB"))

    if a.method == "mirror":
        # 四向镜像：洞内像素按到四边的距离，混合「左右镜像带」与「上下镜像带」
        r0_, r1_, c0_, c1_ = r0, r1, c0, c1
        H, W = r1_ - r0_, c1_ - c0_
        A = a_img.astype(np.float32)
        left = A[r0_:r1_, c0_-W:c0_][:, ::-1, :]
        right = A[r0_:r1_, c1_:c1_+W][:, ::-1, :]
        top = A[r0_-H:r0_, c0_:c1_][::-1, :, :]
        bot = A[r1_:r1_+H, c0_:c1_][::-1, :, :]
        xx = np.arange(W)[None, :]; yy = np.arange(H)[:, None]
        wl = (W - xx).astype(np.float32) / W; wr = (xx + 1).astype(np.float32) / W
        wt = (H - yy).astype(np.float32) / H; wb = (yy + 1).astype(np.float32) / H
        h_mix = (left * wl[..., None] + right * wr[..., None]) / (wl + wr)[..., None]
        v_mix = (top * wt[..., None] + bot * wb[..., None]) / (wt + wb)[..., None]
        dh = np.minimum(xx + 1, W - xx).astype(np.float32)
        dv = np.minimum(yy + 1, H - yy).astype(np.float32)
        wh = 1.0 / (dh + 1.0); wv = 1.0 / (dv + 1.0)
        fill = (h_mix * wh[..., None] + v_mix * wv[..., None]) / (wh + wv)[..., None]
        out = a_img.copy()
        out[r0_:r1_, c0_:c1_] = np.clip(fill, 0, 255).astype(np.uint8)
        if a.preview:
            pr = 400
            y0 = max(0, r0_ - pr); y1 = min(a_img.shape[0], r1_ + pr)
            x0 = max(0, c0_ - pr); x1 = min(a_img.shape[1], c1_ + pr)
            comp = np.concatenate([a_img[y0:y1, x0:x1], np.full((y1 - y0, 8, 3), 255, np.uint8),
                                   out[y0:y1, x0:x1]], axis=1)
            im = Image.fromarray(comp); im.thumbnail((1200, 1200)); im.save(a.preview)
            print("对比图:", a.preview)
        Image.fromarray(out).save(a.out)
        print("已保存（mirror）:", a.out)
        return
    if z.shape[0] < 10:
        z = np.zeros((a_img.shape[0] // 4, a_img.shape[1] // 4), np.float32)
        water = None

    # 🔴 先填「洞 + 外扩 PAD」，再只在**洞外**那圈做羽化：
    #    洞内的原像素是坏内容，绝不能混回来（上一版就是混回来 → 边上留一圈暗边）
    PAD = 128
    er0, ec0 = max(0, r0 - PAD), max(0, c0 - PAD)
    er1, ec1 = min(a_img.shape[0], r1 + PAD), min(a_img.shape[1], c1 + PAD)
    filled = quilt(a_img, (er0, ec0, er1, ec1), None, z, water, ring_rect=(r0, c0, r1, c1))

    # 🔴 混合填充：边界用**四向镜像**（与原图逐像素连续），内部用**绗缝**（不复制邻近特征）
    A = a_img.astype(np.float32)
    Hh0, Ww0 = r1 - r0, c1 - c0
    left = A[r0:r1, c0-Ww0:c0][:, ::-1, :]
    right = A[r0:r1, c1:c1+Ww0][:, ::-1, :]
    top = A[r0-Hh0:r0, c0:c1][::-1, :, :]
    bot = A[r1:r1+Hh0, c0:c1][::-1, :, :]
    xx0 = np.arange(Ww0)[None, :]; yy0 = np.arange(Hh0)[:, None]
    wl = (Ww0 - xx0).astype(np.float32) / Ww0; wr = (xx0 + 1).astype(np.float32) / Ww0
    wt = (Hh0 - yy0).astype(np.float32) / Hh0; wb = (yy0 + 1).astype(np.float32) / Hh0
    h_mix = (left * wl[..., None] + right * wr[..., None]) / (wl + wr)[..., None]
    v_mix = (top * wt[..., None] + bot * wb[..., None]) / (wt + wb)[..., None]
    dh = np.minimum(xx0 + 1, Ww0 - xx0).astype(np.float32)
    dv = np.minimum(yy0 + 1, Hh0 - yy0).astype(np.float32)
    wh_ = 1.0 / (dh + 1.0); wv_ = 1.0 / (dv + 1.0)
    mirror_fill = (h_mix * wh_[..., None] + v_mix * wv_[..., None]) / (wh_ + wv_)[..., None]

    # 距洞边：0=贴边，越大越靠中心；贴边用镜像，中心用绗缝
    d_edge = np.minimum(np.minimum(xx0 + 1, Ww0 - xx0), np.minimum(yy0 + 1, Hh0 - yy0)).astype(np.float32)
    BAND = 200.0
    t_mix = np.clip(d_edge / BAND, 0, 1)
    w_mirror = (1 - (t_mix * t_mix * (3 - 2 * t_mix)))[..., None]
    quilt_part = filled[r0:r1, c0:c1].astype(np.float32)
    hole_fill = quilt_part * (1 - w_mirror) + mirror_fill * w_mirror

    # 合成：洞内 = 混合填充；洞外 PAD 带 = 填充与原图线性过渡（洞内原像素绝不参与）
    out = a_img.copy()
    Hh, Ww = er1 - er0, ec1 - ec0
    yyi, xxi = np.mgrid[0:Hh, 0:Ww]
    d_out = np.minimum.reduce([yyi - (r0 - er0), (r1 - er0) - 1 - yyi,
                               xxi - (c0 - ec0), (c1 - ec0) - 1 - xxi]).astype(np.float32)
    t = np.clip(d_out / float(PAD), 0, 1)
    band = (t * t * (3 - 2 * t))[..., None]
    inside = (yyi >= r0 - er0) & (yyi < r1 - er0) & (xxi >= c0 - ec0) & (xxi < c1 - ec0)
    al = np.where(inside[..., None], 1.0, band)
    filled2 = filled.astype(np.float32).copy()
    filled2[r0:r1, c0:c1] = hole_fill
    out[er0:er1, ec0:ec1] = np.clip(filled2[er0:er1, ec0:ec1] * al +
                                    a_img[er0:er1, ec0:ec1].astype(np.float32) * (1 - al), 0, 255).astype(np.uint8)

    if a.preview:
        pr = 400
        y0 = max(0, r0 - pr); y1 = min(a_img.shape[0], r1 + pr)
        x0 = max(0, c0 - pr); x1 = min(a_img.shape[1], c1 + pr)
        comp = np.concatenate([a_img[y0:y1, x0:x1], np.full((y1 - y0, 8, 3), 255, np.uint8),
                               out[y0:y1, x0:x1]], axis=1)
        im = Image.fromarray(comp); im.thumbnail((1200, 1200)); im.save(a.preview)
        print("对比图:", a.preview)

    Image.fromarray(out).save(a.out)
    print("已保存:", a.out)


if __name__ == "__main__":
    main()
