# -*- coding: utf-8 -*-
"""flora.bin 生成器（v1：层图判据模式）——链路见 Knowledge/骑砍2战役地形制作管线.md 三·十一

功能：按"森林层图（mask）判据 + 种群参数"撒点 → 直写 FLR2 植被存档。

v1 关键区别（2026-09-06 用户指正）：层图 = NativeExample 导出的逐层材质图
  terrain_materialmal_layerN.png（4097×4097 16bit 灰度，与高度图同格，官方 Import/Export 规格）。
  实测授权：pine 树 97.7% 落 layer2 植被区（flora_forest 层）、阔叶树 94.7% 落 layer7（forest2 层）。
  → 判据 = 这两张层图本身（值 = 该层权重，>0 即该处属此层）。

生成流程：
  1. 读参照 flora.bin（如 OnlyTerrian）→ 按组（pine/leaf）取树种混比、尺寸/朝向样本池
  2. 判据 = 层图值概率面（4097² → 848² 降采样；权重越大越可能种树）
  3. 按概率面采样 N 棵（默认 = 参照同组棵数），z = 参照池 (x,y) 最近邻（贴地近似）
  4. 写 FLR2（FLR2 + u32 len-8 + u32 N + [u32 名字长][名字][84B payload 21×f32]）
  5. 对照报告：棵数/混比/bbox/scale/与参照栅格密度相关

用法:
  python make_flora.py --src <参照 flora.bin> --lmap-pine <层图.png> --lmap-leaf <层图.png>
                       [--out out.bin] [--seed N] [--ratio 1.0]
"""
import argparse
import os
import struct
import math

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None

GRID_M = 848.0        # 世界尺寸（m）；层图 4097px ↔ 848m（4096+1 边界）
LEAF_PREFIXES = ("worldmap_tree_acacia", "worldmap_tree_beech", "worldmap_tree_high")


def read_flr2(path):
    d = open(path, "rb").read()
    assert d[:4] == b"FLR2", "bad magic: %r" % d[:4]
    n = struct.unpack("<I", d[8:12])[0]
    p = 12
    recs = []
    for _ in range(n):
        ln = struct.unpack("<I", d[p:p + 4])[0]
        name = d[p + 4:p + 4 + ln].decode("ascii")
        pay = struct.unpack("<21f", d[p + 4 + ln:p + 4 + ln + 84])
        recs.append((name, pay))
        p += 4 + ln + 84
    assert p == len(d), "trailing bytes: %d vs %d" % (p, len(d))
    return recs


def write_flr2(path, recs):
    buf = bytearray()
    for name, pay in recs:
        nb = name.encode("ascii")
        buf += struct.pack("<I", len(nb)) + nb + struct.pack("<21f", *pay)
    out = b"FLR2" + struct.pack("<II", len(buf) + 4, len(recs)) + bytes(buf)
    open(path, "wb").write(out)


def load_mask(path, w=848):
    """层图 → (w,w) 概率面 [0,1]，与世界坐标同向（y 翻转：图 y=0 顶 ↔ 世界 y=848）"""
    a = np.asarray(Image.open(path).convert("L")).astype(np.float32) / 255.0
    a = np.asarray(Image.fromarray((a * 255).astype(np.uint8)).resize((w, w), Image.BOX),
                   np.float32) / 255.0
    return a[::-1]        # 翻转 y：图顶(y=0) = 世界 y=848（采样验证：命中率 y_flip=True 最优）


def sample_from_mask(mask, n, rng):
    p = mask.ravel().astype(np.float64).clip(0, None)
    s = p.sum()
    if s <= 0:
        raise ValueError("mask 全零——判据图无植被区")
    idx = rng.choice(p.size, size=n, replace=True, p=p / s)
    gy, gx = np.divmod(idx, mask.shape[0])
    jx = (rng.random(n) - 0.5) * (GRID_M / mask.shape[0])
    jy = (rng.random(n) - 0.5) * (GRID_M / mask.shape[0])
    return gx * (GRID_M / mask.shape[0]) + jx, gy * (GRID_M / mask.shape[0]) + jy


def nearest_z(pool_xy, pool_z, pts):
    from scipy.spatial import cKDTree
    _, ii = cKDTree(pool_xy).query(pts, k=1)
    return np.asarray(pool_z)[ii]


def mask_density(pts, w=424):
    dens = np.zeros((w, w), np.float64)
    xi = np.clip((pts[0] / GRID_M * w).astype(int), 0, w - 1)
    yi = np.clip((pts[1] / GRID_M * w).astype(int), 0, w - 1)
    np.add.at(dens, (yi, xi), 1.0)
    return dens


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", required=True, help="参照 flora.bin（取树种混比/尺寸/朝向样本池）")
    ap.add_argument("--lmap-pine", required=True, help="松林层图（layer2 flora_forest 权重图）")
    ap.add_argument("--lmap-leaf", required=True, help="阔叶层图（layer7 forest2 权重图）")
    ap.add_argument("--out", default="flora_generated.bin")
    ap.add_argument("--seed", type=int, default=20260906)
    ap.add_argument("--ratio", type=float, default=1.0, help="棵数倍率（1.0 = 与参照同组棵数）")
    a = ap.parse_args()

    rng = np.random.default_rng(a.seed)
    recs = read_flr2(a.src)
    print("参照: %s  总实例 %d" % (os.path.basename(a.src), len(recs)))

    out = []
    for gname, lmap in (("pine", a.lmap_pine), ("leaf", a.lmap_leaf)):
        grp = [(n, p) for n, p in recs
               if (n.startswith("map_pine") if gname == "pine"
                   else n.startswith(LEAF_PREFIXES))]
        if not grp:
            print("  [%s] 参照无此组，跳过" % gname)
            continue
        names = [n for n, _ in grp]
        pays = [p for _, p in grp]
        uniq, cnts = np.unique(np.array(names), return_counts=True)
        order = np.argsort(-cnts)
        pword = cnts[order] / len(names)
        pnames = uniq[order]
        xs = np.array([p[13] for p in pays]); ys = np.array([p[14] for p in pays])
        zs = np.array([p[15] for p in pays])
        mask = load_mask(lmap)
        n = int(round(len(grp) * a.ratio))
        x, y = sample_from_mask(mask, n, rng)
        z = nearest_z(np.stack([xs, ys], 1), zs, np.stack([x, y], 1))
        pick = rng.choice(len(pnames), size=n, p=pword)
        for i in range(n):
            nm = pnames[pick[i]]
            tmpl = int(rng.integers(0, len(pays)))
            pay = list(pays[tmpl])
            pay[13], pay[14], pay[15] = float(x[i]), float(y[i]), float(z[i])
            out.append((nm, pay))
        # 参照栅格密度 vs 判据图相关（验证「层图唯一等于树区」）
        d_ref = mask_density((xs, ys))
        d_gen = mask_density((x, y)) / max(1.0, n / d_ref.sum()) if n else d_ref
        corr = float(np.corrcoef(d_ref.ravel(), d_gen.ravel())[0, 1])
        print("  [%s] 参照 %d 棵 → 生成 %d 棵；判据图非零=%.1f%% ；密度相关 r=%.3f" %
              (gname, len(grp), n, 100 * (mask > 0.01).mean(), corr))

    write_flr2(a.out, out)
    print("产物: %s  %d 棵 (%.1f KB)" % (a.out, len(out), os.path.getsize(a.out) / 1024))
    from collections import Counter
    print("树种混比(top6): %s" % dict(Counter(n for n, _ in out).most_common(6)))
    xs2 = np.array([p[13] for _, p in out]); ys2 = np.array([p[14] for _, p in out])
    ss2 = np.array([p[11] for _, p in out])
    print("bbox: x[%.0f,%.0f] y[%.0f,%.0f] scale均值=%.3f±%.3f" %
          (xs2.min(), xs2.max(), ys2.min(), ys2.max(), ss2.mean(), ss2.std()))


if __name__ == "__main__":
    main()
