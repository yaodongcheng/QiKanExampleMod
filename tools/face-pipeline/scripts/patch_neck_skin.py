# -*- coding: utf-8 -*-
"""patch_neck_skin.py —— 自建头的「脖子/领口段」皮肤色调校正（只改贴图）

【为什么需要】
  自建头（亨利/蒂法/萨菲罗斯/战无2×28）的脖子段贴图色调跟身体、脸都对不上：
    亨利脖子 亮度185 ｜ 骑砍身体底色 165~140 ｜ 亨利脸 129~160
  引擎里 头 和 身体 是**同一个肤色乘子**（`tex_col.rgb *= g_mesh_factor_color`，
  见 Shaders/Sources/forward_face_functions.rsh 的 calculate_albedo_face），
  所以「头身一致」的数学条件就是 **两张贴图底色同族**。
  实测铁证：织丰的 head_male_basemesh_d (189,120,87) 与 body_male_basemesh_d (195,129,96)
  底色几乎相同 —— 这就是原版做法。
  我们的头是「源模型烘死的成品肤色」，脖子那块又偏亮偏黄 → 实机「生硬的两张皮肤」。

【做什么】
  1) 用头网格（带 UV）光栅化出「颈/领口段」面片覆盖的 UV 像素 → 掩膜
     （只选 3 顶点 z 都低于 --neck-z 的面，所以不会碰到同区域里的嘴/牙小岛）
  2) 掩膜内做**频率分离**：
       · 低频（整块色调）→ 拉到 --target-* 指定的目标底色
       · 高频（毛孔）    → 原样保留
  3) 掩膜羽化后合成回原图

【不改什么】几何 / UV / 材质 / 形状键 —— 只是贴图数据

【用法】
  python patch_neck_skin.py --obj <头.obj> --src <头_d.png> --out <出.png> \
      --neck-z 1.596 --target-brightness 158 --out-mask <可选：掩膜预览.png>
  target 两个旋钮：
      --target-brightness  目标底色亮度（身体同族：原版女身165 / 织丰男身140）
      --target-ratio       目标色的 R:G:B 比例（默认 1:0.70:0.62 = 原版女身 (214,148,132) 归一）

  铁律 22：本脚本是**生成器**，改效果改这里，禁止手改产物。
"""
import argparse
import io
import os
import sys

try:
    import numpy as np
    from PIL import Image, ImageFilter
except ImportError:
    sys.exit("需要 numpy + Pillow")

# UV → 像素：v=0 对应贴图顶行（与 render_head.py 同一约定，实测过）
def uv_to_px(u, v, W, H):
    return u * (W - 1), v * (H - 1)


def load_obj(path):
    """返回 verts[N,3], uvs[M,2], tris[K,3,2]（每面 3 组 (v_idx, vt_idx)）"""
    verts, uvs, tris = [], [], []
    for line in io.open(path, "r", encoding="utf-8", errors="replace"):
        if line.startswith("v "):
            a = line.split()
            verts.append((float(a[1]), float(a[2]), float(a[3])))
        elif line.startswith("vt "):
            a = line.split()
            uvs.append((float(a[1]), float(a[2])))
        elif line.startswith("f "):
            parts = line[2:].split()
            if len(parts) < 3:
                continue
            idx = []
            for p in parts[:3]:
                s = p.split("/")
                vi = int(s[0]) - 1
                ti = int(s[1]) - 1 if len(s) > 1 and s[1] else vi
                idx.append((vi, ti))
            tris.append(idx)
    return (np.asarray(verts, dtype=np.float64),
            np.asarray(uvs, dtype=np.float64),
            tris)


def rasterize(verts, uvs, tris, W, H, zmax, dilate=6):
    """把「3 顶点 z 都 < zmax」的面片光栅化进 UV 空间。
    返回 (mask, zmap, 选中面数, z 范围)。zmap = 每像素的重心插值高度（未覆盖处为 nan）。
    为什么按 z 而不是按 UV 矩形：萨菲罗斯的领口 UV 不连续（z↔v 相关 −0.55），
    按矩形刷会连带刷到脸上（§22.5 的老结论，本脚本沿用同一套光栅化）。"""
    mask = np.zeros((H, W), dtype=bool)
    zmap = np.full((H, W), np.nan, dtype=np.float32)
    sel = 0
    zs = []
    for tri in tris:
        vi = [t[0] for t in tri]
        z = verts[vi, 2]
        if not (z[0] < zmax and z[1] < zmax and z[2] < zmax):
            continue
        sel += 1
        zs.extend(z.tolist())
        ti = [t[1] for t in tri]
        uv = uvs[ti]
        x = uv[:, 0] * (W - 1)
        y = uv[:, 1] * (H - 1)
        x0 = max(0, int(np.floor(x.min())))
        x1 = min(W - 1, int(np.ceil(x.max())))
        y0 = max(0, int(np.floor(y.min())))
        y1 = min(H - 1, int(np.ceil(y.max())))
        if x1 < x0 or y1 < y0:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        d = ((y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2]))
        if abs(d) < 1e-12:
            continue
        l0 = ((y[1] - y[2]) * (gx - x[2]) + (x[2] - x[1]) * (gy - y[2])) / d
        l1 = ((y[2] - y[0]) * (gx - x[2]) + (x[0] - x[2]) * (gy - y[2])) / d
        l2 = 1.0 - l0 - l1
        inside = (l0 >= -1e-6) & (l1 >= -1e-6) & (l2 >= -1e-6)
        if inside.any():
            sub = zmap[y0:y1 + 1, x0:x1 + 1]
            zz = (l0 * z[0] + l1 * z[1] + l2 * z[2]).astype(np.float32)
            sub[inside] = zz[inside]
            mask[y0:y1 + 1, x0:x1 + 1] |= inside
    if sel == 0:
        return mask, zmap, 0, None
    if dilate > 0:
        # 掩膜膨胀后，把 zmap 的空洞用邻域最近值填掉（否则膨胀出来的边没有 z）
        m = Image.fromarray((mask * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(dilate * 2 + 1))
        dm = np.asarray(m) > 127
        hole = dm & ~mask
        if hole.any():
            # 用 8bit 归一化图做模糊（PIL 的 GaussianBlur 不吃 float32 模式）
            z0f = float(np.nanmin(zmap)) if np.isfinite(zmap).any() else 0.0
            z1f = float(np.nanmax(zmap)) if np.isfinite(zmap).any() else 1.0
            span = max(1e-6, z1f - z0f)
            norm = np.clip(np.nan_to_num((zmap - z0f) / span, nan=0.0), 0, 1)
            filled = np.asarray(
                Image.fromarray((norm * 255).astype(np.uint8))
                .filter(ImageFilter.GaussianBlur(dilate))).astype(np.float32) / 255.0
            zmap[hole] = (filled[hole] * span + z0f).astype(np.float32)
        mask = dm
    return mask, zmap, sel, (min(zs), max(zs))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--obj", required=True, help="头网格 OBJ（带 UV）")
    ap.add_argument("--src", required=True, help="当前头贴图 _d.png")
    ap.add_argument("--out", required=True, help="输出 png")
    ap.add_argument("--neck-z", type=float, default=None,
                    help="颈段上界 z（3 顶点都低于它才算颈段）。缺省 = 包围盒下沿 + 42%%")
    ap.add_argument("--target-brightness", type=float, default=158.0,
                    help="目标底色亮度。参照：原版女身165 / 织丰男身140 / 原版女头174")
    ap.add_argument("--target-ratio", default="1,0.70,0.62",
                    help="目标色 R:G:B 比例。默认 = 原版女身 (214,148,132) 归一")
    ap.add_argument("--flatten", type=float, default=0.35,
                    help="低频压平强度 0~1（0=只挪整体亮度，1=完全压成常数色）")
    ap.add_argument("--blur", type=float, default=24.0, help="频率分离的低频模糊半径（像素）")
    ap.add_argument("--feather", type=float, default=10.0, help="掩膜羽化半径（像素）")
    ap.add_argument("--strength", type=float, default=1.0, help="整体效果强度 0~1（做对比档用）")
    ap.add_argument("--out-mask", default=None, help="可选：把掩膜叠加到原图存一张预览")
    # ── 彩条诊断模式（一次进游戏定方向 + 定幅度，见 §22.4 色带法）──────────────
    ap.add_argument("--bands", default=None,
                    help="诊断模式：'0.78,0.88,1.00,1.11,1.22,1.35' —— 按高度切成 N 条，"
                         "每条施加一个【均匀 RGB 倍数】（色相不变）。从下往上数第 1 条 = 列表第 1 个")
    ap.add_argument("--band-z0", type=float, default=None, help="彩条最低高度（缺省 = 网格最低点）")
    ap.add_argument("--band-z1", type=float, default=None, help="彩条最高高度（缺省 = --neck-z）")
    ap.add_argument("--band-sep", type=int, default=3, help="彩条之间的黑色分隔线宽度（像素）")
    a = ap.parse_args()

    verts, uvs, tris = load_obj(a.obj)
    img = Image.open(a.src).convert("RGB")
    W, H = img.size
    src = np.asarray(img).astype(np.float32)

    zlo, zhi = verts[:, 2].min(), verts[:, 2].max()
    neck_z = a.neck_z if a.neck_z is not None else zlo + (zhi - zlo) * 0.42

    # ══════════ 彩条诊断模式 ══════════
    if a.bands:
        gains = [float(x) for x in a.bands.split(",")]
        bz0 = a.band_z0 if a.band_z0 is not None else zlo
        bz1 = a.band_z1 if a.band_z1 is not None else neck_z
        # 取所有 z 低于 bz1 的面（彩条要盖满整段）
        # 🔴 彩条模式 dilate=0：膨胀区的 z 是模糊填出来的，会画出锯齿状分隔线
        mask, zmap, sel, zr = rasterize(verts, uvs, tris, W, H, bz1, dilate=0)
        print("彩条模式：%d 条，z %.4f→%.4f（每条约 %.1f mm）" %
              (len(gains), bz0, bz1, 1000 * (bz1 - bz0) / len(gains)))
        print("选中面片 %d 个（z %.4f~%.4f），覆盖 %d 像素" % (sel, zr[0], zr[1], mask.sum()))
        n = len(gains)
        idx = np.clip(((zmap - bz0) / max(1e-6, (bz1 - bz0)) * n).astype(np.int32), 0, n - 1)
        band_gain = np.zeros((H, W, 3), dtype=np.float32)
        for k, g in enumerate(gains):
            band_gain[idx == k] = g
        out = src * np.where(mask[:, :, None], band_gain, 1.0)
        # 分隔线：把每条的上边界涂黑，方便数条数
        if a.band_sep > 0:
            edge = np.zeros((H, W), dtype=bool)
            for k in range(1, n):
                zb = bz0 + (bz1 - bz0) * k / n
                e = mask & (np.abs(zmap - zb) < (bz1 - bz0) / n * 0.06)
                edge |= e
            if edge.any():
                e = np.asarray(Image.fromarray((edge * 255).astype(np.uint8))
                               .filter(ImageFilter.MaxFilter(a.band_sep * 2 + 1))) > 127
                out[e] = out[e] * 0.25
        res = np.clip(np.where(mask[:, :, None], out, src), 0, 255).astype(np.uint8)
        Image.fromarray(res).save(a.out)
        print("从下往上：%s" % "  ".join("第%d条 ×%.2f" % (k + 1, g) for k, g in enumerate(gains)))
        print("已写出 %s" % a.out)
        return 0

    mask, zmap, sel, zr = rasterize(verts, uvs, tris, W, H, neck_z)
    print("网格 z %.4f~%.4f   颈段上界 z=%.4f" % (zlo, zhi, neck_z))
    print("选中面片 %d 个（z %.4f~%.4f），掩膜覆盖 %d 像素（%.2f%%）"
          % (sel, zr[0], zr[1], mask.sum(), 100.0 * mask.sum() / (W * H)))
    if sel == 0:
        sys.exit("!! 没选中任何面片，检查 --neck-z")

    # 目标色
    ratio = np.array([float(t) for t in a.target_ratio.split(",")], dtype=np.float32)
    ratio = ratio / ratio.mean()
    target = ratio * (a.target_brightness * 3.0 / ratio.sum())

    # 频段分离
    lo = np.asarray(Image.fromarray(src.astype(np.uint8))
                    .filter(ImageFilter.GaussianBlur(a.blur))).astype(np.float32)
    lo_safe = np.maximum(lo, 4.0)

    # ① 挪整体亮度：把区域低频均值搬到目标色
    cur_mean = src[mask].mean(axis=0) if mask.any() else src.reshape(-1, 3).mean(axis=0)
    gain = target / np.maximum(cur_mean, 1.0)
    out = src * gain[None, None, :]

    # ② 压平低频起伏：out × (target / 局部低频)
    if a.flatten > 0:
        lo2 = np.asarray(Image.fromarray(np.clip(out, 0, 255).astype(np.uint8))
                         .filter(ImageFilter.GaussianBlur(a.blur))).astype(np.float32)
        ratio_map = np.clip(target[None, None, :] / np.maximum(lo2, 4.0), 0.5, 2.0)
        flat = out * ratio_map
        out = out * (1.0 - a.flatten) + flat * a.flatten

    print("区域当前底色 %s（亮度 %.0f） → 目标 %s（亮度 %.0f）"
          % (np.round(cur_mean).astype(int), cur_mean.mean(),
             np.round(target).astype(int), target.mean()))

    # ③ 羽化合成
    soft = np.asarray(Image.fromarray((mask * 255).astype(np.uint8))
                      .filter(ImageFilter.GaussianBlur(a.feather))).astype(np.float32) / 255.0
    w = np.clip(soft * a.strength, 0.0, 1.0)[:, :, None]
    res = src * (1.0 - w) + out * w
    res = np.clip(res, 0, 255).astype(np.uint8)
    Image.fromarray(res).save(a.out)
    print("已写出 %s" % a.out)

    if a.out_mask:
        prev = src.copy()
        prev[mask] = prev[mask] * 0.4 + np.array([255, 0, 0]) * 0.6
        Image.fromarray(np.clip(prev, 0, 255).astype(np.uint8)).save(a.out_mask)
        print("掩膜预览 %s" % a.out_mask)
    return 0


if __name__ == "__main__":
    sys.exit(main())
