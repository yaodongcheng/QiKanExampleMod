# -*- coding: utf-8 -*-
"""transfer_face_spec.py —— 把参照头的「脸部高光图」按 3D 最近点搬到目标头上

【为什么】
  脸部着色器把 `_s` 的通道当【高光】用，不是金属度：
      forward_face_functions.rsh → calculate_specular_face
          specularity.x *= g_specular_coef     // R = 反射强度
          specularity.y *= g_gloss_coef        // G = 光泽
      Face.rsh → compute_specular_face_lighting
          reflectivity = specularity_info.x * 5.0f
          roughness    = max(0.05, 1.0 - specularity_info.y)
      calculate_ao_face_forward: occ = specular_sample.z   // B = 环境光遮蔽

  实测对照（中位）：
      参照（xxFemale / 原版）  R 93   G 104   B 194   ← 有皮肤油光 + 一点遮蔽
      我们（make_head_textures 合成） R  0   G  27   B 255   ← 反射强度 0 = 死哑光
  ⇒ 身子有正常皮肤光泽，脸是块哑光板 = 一眼两种材质。
  （🔴 头文件注释里「与 Native body_female_a 同款」是错的：body 是标准着色器的金属度口径，
    脸不是。同一条链上的蒂法/萨菲罗斯/战无2×28 全中。）

【做法】
  两个头都是人头，解剖结构对应 → 用 **3D 最近点**建立对应：
    目标头每个纹素 →（重心插值）得到 3D 位置 → KDTree 找参照头最近顶点 → 取其 UV → 采参照 _s
  这是本工程既有手法（transfer_channels.py 同源思路）。

【用法】
  python transfer_face_spec.py --obj <目标头.obj> --ref-obj <参照头.obj> \
      --ref-spec <参照头_s.png> --out <输出_s.png> [--gain-r 1.0] [--gain-g 1.0]

  铁律 22：本脚本是生成器，改效果改这里。
"""
import argparse
import io
import sys

import numpy as np
from PIL import Image, ImageFilter

try:
    from scipy.spatial import cKDTree
except ImportError:
    sys.exit("需要 scipy：pip install scipy")


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


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--obj", required=True, help="目标头 OBJ（带 UV）")
    ap.add_argument("--ref-obj", required=True, help="参照头 OBJ（要与目标头同空间、同性别朝向）")
    ap.add_argument("--ref-spec", required=True, help="参照头的 _s 贴图")
    ap.add_argument("--out", required=True, help="输出的 _s 贴图（尺寸 = 目标头现用 _s 的尺寸）")
    ap.add_argument("--size", type=int, default=2048)
    ap.add_argument("--gain-r", type=float, default=1.0, help="R(反射强度)总增益")
    ap.add_argument("--gain-g", type=float, default=1.0, help="G(光泽)总增益")
    ap.add_argument("--smooth", type=float, default=1.5,
                    help="最近点映射会有块状感，最后做一次小半径模糊（像素）")
    a = ap.parse_args()

    R = 512  # 内部栅格分辨率（映射本身是低频的，不用满分辨率）
    V, T, F = load_obj(a.obj)
    RV, RT, RF = load_obj(a.ref_obj)
    ref = Image.open(a.ref_spec).convert("RGB")
    RW, RH = ref.size
    refA = np.asarray(ref).astype(np.float32)

    tree = cKDTree(RV)
    # 参照头「顶点索引 → UV」查表（一个顶点可能有多套 UV，取第一个用到的）
    refv_uv = np.zeros((len(RV), 2), dtype=np.float32)
    have = np.zeros(len(RV), dtype=bool)
    for f in RF:
        for vi, ti in f:
            if not have[vi]:
                refv_uv[vi] = RT[ti]
                have[vi] = True
    if not have.any():
        sys.exit("!! 参照头没有 UV")
    # 逐三角形在 UV 上光栅化，重心插值出 3D 位置 → 最近参照顶点 → 参照 UV
    uvbuf = np.zeros((R, R, 2), dtype=np.float32)
    cov = np.zeros((R, R), dtype=bool)
    for f in F:
        vi = [t[0] for t in f]
        P3 = V[vi]
        uv = T[[t[1] for t in f]]
        x = uv[:, 0] * (R - 1)
        y = uv[:, 1] * (R - 1)
        x0 = max(0, int(x.min())); x1 = min(R - 1, int(x.max()) + 1)
        y0 = max(0, int(y.min())); y1 = min(R - 1, int(y.max()) + 1)
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
        if not ins.any():
            continue
        pos = (l0[..., None] * P3[0] + l1[..., None] * P3[1] + l2[..., None] * P3[2])
        _, nbr = tree.query(pos[ins], k=1)
        uvbuf[y0:y1, x0:x1][ins] = refv_uv[nbr]
        cov[y0:y1, x0:x1] |= ins

    # 采样参照 _s
    px = np.clip((uvbuf[..., 0] * (RW - 1)).astype(np.int32), 0, RW - 1)
    py = np.clip((uvbuf[..., 1] * (RH - 1)).astype(np.int32), 0, RH - 1)
    out_small = refA[py, px]

    # 🔴 最近点映射天生是「一格一格的多边形」（每个参照顶点管一大片 UV）。
    #    在小图上做【带权模糊】消掉块边：blur(v·m)/blur(m)，避免把没覆盖区的 0 拖进来。
    r_small = max(4.0, a.smooth * R / float(a.size))
    num = np.dstack([np.asarray(Image.fromarray(np.clip(out_small[..., c], 0, 255).astype(np.uint8))
                                .filter(ImageFilter.GaussianBlur(r_small))).astype(np.float32)
                     for c in range(3)])
    den = np.asarray(Image.fromarray((cov * 255).astype(np.uint8))
                     .filter(ImageFilter.GaussianBlur(r_small))).astype(np.float32) / 255.0
    filled = num / np.maximum(den, 0.05)[..., None]
    cov_f = np.clip(den * 1.25, 0, 1)

    im = Image.fromarray(np.clip(filled, 0, 255).astype(np.uint8))
    im = im.resize((a.size, a.size), Image.LANCZOS)
    if a.smooth > 0:
        im = im.filter(ImageFilter.GaussianBlur(a.smooth * 0.5))
    fill = np.asarray(im).astype(np.float32)
    m = np.asarray(Image.fromarray((cov_f * 255).astype(np.uint8))
                   .resize((a.size, a.size), Image.LANCZOS)).astype(np.float32) / 255.0
    m = np.clip(m, 0, 1)[..., None]
    skin = np.array([93.0, 104.0, 194.0], dtype=np.float32)   # 参照中位
    res = fill * m + skin[None, None, :] * (1 - m)
    res[..., 0] *= a.gain_r
    res[..., 1] *= a.gain_g
    Image.fromarray(np.clip(res, 0, 255).astype(np.uint8)).save(a.out)
    print("参照 %s %s → 目标 %s" % (a.ref_spec, ref.size, a.out))
    print("通道中位：R %.0f  G %.0f  B %.0f" % tuple(np.median(res.reshape(-1, 3), axis=0)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
