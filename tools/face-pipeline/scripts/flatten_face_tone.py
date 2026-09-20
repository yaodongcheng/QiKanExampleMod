# -*- coding: utf-8 -*-
"""flatten_face_tone.py —— 把自建头的脸贴图从「照片」压成「平底图」

【原理（实测钉死的）】
  引擎对【头和身体用同一个肤色乘子】：
      Shaders/Sources/forward_face_functions.rsh → calculate_albedo_face
          tex_col.rgb *= g_mesh_factor_color.rgb
  所以「头身一致」的硬条件 = 两张贴图底色同族。实测各家脸/身贴图的【低频色调跨度】：

      xxFemale 头 18 ｜ 织丰男头 21 ｜ 织丰男身 11 ｜ 原版女身 19 ｜ 原版女头 45
      亨利      113        ← 差 5~10 倍

  ⇒ 人家的脸贴图【整张脸只有一个色调】，明暗全交给引擎的乘子 + 光照；
    我们的是一张照片，自带阴影/血色/明暗 → 乘一遍 = 一张更暗的照片 = 跟身体不是一张皮。
    脖子只是这个病【最显眼】的地方（紧挨着身体，有直接对照物）。

【做法：频率分离】
  低频（色调场）  → 压到目标底色，只保留 --keep-low 比例的原有起伏
  中频（眉/唇/眼窝/胡茬的边缘）→ 原样保留
  高频（毛孔/皮纹）           → 原样保留

【不碰】
  · 头皮（z 高于 --z-scalp）：被头发盖住，压平反而会变白
  · 极暗区（--dark-cut 以下）：口腔内衬 / 深阴影
  · 网格 UV 没用到的纹素

【用法】
  python flatten_face_tone.py --obj <头.obj> --src <头_d.png> --out <出.png>
      [--target-rgb "210,157,140"] [--keep-low 0.20] [--blur 60]
      [--z-scalp 1.73] [--dark-cut 55] [--out-preview <预览.png>]

  铁律 22：本脚本是生成器，改效果改这里，禁止手改产物。
"""
import argparse
import io
import sys

import numpy as np
from PIL import Image, ImageFilter

# 目标底色默认值 = 原版女身代理 (214,148,132) 与原版女头 (206,167,148) 的同族折中
DEFAULT_TARGET = "210,157,140"


def load_obj(path):
    V, T, F = [], [], []
    for line in io.open(path, "r", encoding="utf-8", errors="replace"):
        if line.startswith("v "):
            a = line.split()
            V.append((float(a[1]), float(a[2]), float(a[3])))
        elif line.startswith("vt "):
            a = line.split()
            T.append((float(a[1]), float(a[2])))
        elif line.startswith("f "):
            q = line[2:].split()
            if len(q) >= 3:
                row = []
                for x in q[:3]:
                    s = x.split("/")
                    vi = int(s[0]) - 1
                    ti = int(s[1]) - 1 if len(s) > 1 and s[1] else vi
                    row.append((vi, ti))
                F.append(row)
    return np.asarray(V), np.asarray(T), F


def rasterize_z(V, T, F, W, H):
    """逐三角形在 UV 上光栅化，重心插值出每像素对应的高度 z。返回 (mask, zmap)。"""
    mask = np.zeros((H, W), dtype=bool)
    zmap = np.full((H, W), np.nan, dtype=np.float32)
    for f in F:
        vi = [t[0] for t in f]
        z = V[vi, 2]
        uv = T[[t[1] for t in f]]
        x = uv[:, 0] * (W - 1)
        y = uv[:, 1] * (H - 1)
        x0 = max(0, int(x.min())); x1 = min(W - 1, int(x.max()) + 1)
        y0 = max(0, int(y.min())); y1 = min(H - 1, int(y.max()) + 1)
        if x1 <= x0 or y1 <= y0:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1), np.arange(y0, y1))
        d = ((y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2]))
        if abs(d) < 1e-12:
            continue
        l0 = ((y[1] - y[2]) * (gx - x[2]) + (x[2] - x[1]) * (gy - y[2])) / d
        l1 = ((y[2] - y[0]) * (gx - x[2]) + (x[0] - x[2]) * (gy - y[2])) / d
        l2 = 1.0 - l0 - l1
        ins = (l0 >= -1e-6) & (l1 >= -1e-6) & (l2 >= -1e-6)
        if ins.any():
            sub = zmap[y0:y1, x0:x1]
            zz = (l0 * z[0] + l1 * z[1] + l2 * z[2]).astype(np.float32)
            sub[ins] = zz[ins]
            mask[y0:y1, x0:x1] |= ins
    return mask, zmap


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--obj", required=True, help="头网格 OBJ（带 UV）")
    ap.add_argument("--src", required=True, help="当前头贴图 _d.png")
    ap.add_argument("--out", required=True)
    ap.add_argument("--target-rgb", default=DEFAULT_TARGET,
                    help="目标底色 'R,G,B'。默认 = 原版头身同族折中")
    ap.add_argument("--keep-low", type=float, default=0.20,
                    help="保留多少原有的低频起伏 0~1。0=完全压平，0.2≈跨度降到原来的 1/5")
    ap.add_argument("--blur", type=float, default=60.0, help="低频模糊半径（像素，2048 贴图用 60）")
    ap.add_argument("--z-scalp", type=float, default=None,
                    help="高于此 z 不处理（头皮被头发盖住）。缺省 = 包围盒下沿 + 78%%")
    ap.add_argument("--dark-cut", type=float, default=55.0, help="亮度低于此不处理（口腔/深阴影）")
    ap.add_argument("--feather", type=float, default=6.0, help="掩膜羽化（像素）")
    ap.add_argument("--strength", type=float, default=1.0, help="总强度 0~1")
    ap.add_argument("--out-preview", default=None, help="可选：把处理范围涂红存预览")
    a = ap.parse_args()

    V, T, F = load_obj(a.obj)
    img = Image.open(a.src).convert("RGB")
    W, H = img.size
    src = np.asarray(img).astype(np.float32)

    cov, zmap = rasterize_z(V, T, F, W, H)
    zlo, zhi = V[:, 2].min(), V[:, 2].max()
    z_scalp = a.z_scalp if a.z_scalp is not None else zlo + (zhi - zlo) * 0.78

    lum = src.mean(axis=2)
    # 软掩膜：网格用到 且 不是极暗 且 z 在头皮线以下
    soft = np.zeros((H, W), dtype=np.float32)
    soft[cov] = 1.0
    soft[lum < a.dark_cut] = 0.0
    zz = np.nan_to_num(zmap, nan=0.0)
    soft[(cov) & (zz > z_scalp)] = 0.0
    soft = np.asarray(Image.fromarray((soft * 255).astype(np.uint8))
                      .filter(ImageFilter.GaussianBlur(a.feather))).astype(np.float32) / 255.0
    soft *= a.strength

    use = soft > 0.02
    if not use.any():
        sys.exit("!! 掩膜为空，检查 --dark-cut / --z-scalp")

    target = np.array([float(x) for x in a.target_rgb.split(",")], dtype=np.float32)

    lo = np.asarray(Image.fromarray(src.astype(np.uint8))
                    .filter(ImageFilter.GaussianBlur(a.blur))).astype(np.float32)
    lo_mean = lo[use].reshape(-1, 3).mean(axis=0)

    # 目标低频场 = 目标色 + keep_low ×（原低频 − 原低频均值）
    new_lo = target[None, None, :] + a.keep_low * (lo - lo_mean[None, None, :])
    new_lo = np.maximum(new_lo, 4.0)
    out = src * (new_lo / np.maximum(lo, 4.0))

    res = src * (1.0 - soft[:, :, None]) + out * soft[:, :, None]
    res = np.clip(res, 0, 255).astype(np.uint8)
    Image.fromarray(res).save(a.out)

    def field_span(im_arr, m):
        g = im_arr.mean(axis=2)
        l = np.asarray(Image.fromarray(np.clip(g, 0, 255).astype(np.uint8))
                       .filter(ImageFilter.GaussianBlur(a.blur))).astype(np.float32)
        v = l[m]
        return np.percentile(v, 90) - np.percentile(v, 10)

    print("处理范围 %.1f%% 纹素   头皮线 z=%.3f" % (100.0 * use.sum() / (W * H), z_scalp))
    print("原低频均值 RGB%s（亮度 %.0f）  →  目标 RGB%s"
          % (np.round(lo_mean).astype(int), lo_mean.mean(), np.round(target).astype(int)))
    print("🔴 低频色调跨度：改前 %.0f  →  改后 %.0f   （参照：xxFemale 18 / 原版女头 45）"
          % (field_span(src, use), field_span(res.astype(np.float32), use)))
    print("已写出 %s" % a.out)

    if a.out_preview:
        prev = src.copy()
        prev[use] = prev[use] * 0.45 + np.array([255, 0, 0]) * 0.55
        Image.fromarray(np.clip(prev, 0, 255).astype(np.uint8)).save(a.out_preview)
        print("范围预览 %s" % a.out_preview)
    return 0


if __name__ == "__main__":
    sys.exit(main())
