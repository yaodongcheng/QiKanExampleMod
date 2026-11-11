#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
RDR2 碰撞体（Physics Shape）生成器 —— 第一版：体素化 + 贪心盒子合并
================================================================================

背景 / 为什么这么干（2026-11-11 实测定标，别再猜）：
  · 原版建筑碰撞体**不是**视觉网格减面来的，是**美术手搓的低模块**：
    全库 7,847 个物理体，顶点中位 **32**，79% 在 100 顶点以下；
    `bo_empire_village_house_a` = 220 顶点 / 339 面，而它的视觉 LOD0 = 13,305 面（2.5%）。
    这些体的面 **99% 是四边面** —— 减面只会出三角面，所以它们是手搭的方块/楔块。
  · 单个物理体体积：中位 4.8 KB，整城推估 25~30 MB（对比网格资产 14 GB）。
  ⇒ 我们照同样的预算自动生成即可，不需要跟原建筑严丝合缝。

本脚本做什么：
  源 TRF（视觉网格，局部坐标）→ 体素占位 → 贪心合并成盒子 → 写 `bo_<名字>.trf`

  输出位置默认 `Modules/RDR2/AssetSources/ImortReady/rdr2_collision/<类>/bo_<名字>.trf`
  （铁律 36：新资产只放 ImortReady，交用户在编辑器里 Import；**不要**写进 `Assets/`）

导入之后：
  1. 编辑器里应出现一个 **Physics Shape** 资产（名字 `bo_<名字>`）—— 这一步先验证 TRF 能不能出物理体
  2. 场景 XML 里给实体加 `<physics shape="bo_<名字>" mass="1.000"/>`

TRF 格式（rfver 4，2026-11-11 逐行验证过）：
    rfver 4
    mesh <N>
      <mesh_name> <flag> <material>
      <vertex_count>
      x y z  × vertex_count
      0
      <fvf_count>
      (vertex_index color nx ny nz) / (u v) / (u2 v2)  × fvf_count
      <morph_key_count>            # 恒 0
      <face_count>
      i j k  × face_count          # 索引指向 fvf
    end

用法：
    python Scripts/gen_rdr2_collision_bodies.py --model new_pie_train_43_2
    python Scripts/gen_rdr2_collision_bodies.py --model p_lampfactory02x --voxel 0.25
    python Scripts/gen_rdr2_collision_bodies.py --model A --model B --out D:/tmp/bo
    python Scripts/gen_rdr2_collision_bodies.py --list names.txt --cls prop
"""
import argparse
import io
import math
import os
import sys

import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_SRC = [
    r"D:\BrainMaker\大镖客2解包分析\deliverable\models",
    r"D:\BrainMaker\大镖客2解包分析\deliverable\models_extra",
]
DEFAULT_OUT = os.path.join(ROOT, "..", "RDR2", "AssetSources", "ImortReady", "rdr2_collision")

# 目标预算（对齐原版实测：普通建筑 200~900 面 ⇒ 每个盒子 12 面 ⇒ 约 50~80 个盒子）
DEFAULT_MAX_GRID = 16      # 体素网格最长边格数（16 时 hotel 得 636 面，与原版 674 面同档）
DEFAULT_DILATE = 1         # 占位膨胀次数（补三角形之间的缝）
DEFAULT_MAX_BOXES = 4000   # 超过就警告（盒数 × 12 = 三角形数）


# ──────────────────────────────────────────────────────────────── TRF 读
def read_trf(path):
    """→ (positions Nx3 float, tris Mx3 int, materials list, mesh_names list)"""
    with io.open(path, encoding="utf-8", errors="replace") as f:
        L = f.read().splitlines()
    if not L or not L[0].startswith("rfver"):
        raise ValueError("not a trf: " + path)

    n_mesh = int(L[1].split()[1])
    i = 2
    pos_all, tri_all = [], []
    materials, names = [], []
    off = 0
    for _ in range(n_mesh):
        parts = L[i].split(maxsplit=2)
        i += 1
        name = parts[0] if parts else "?"
        mat = parts[2] if len(parts) > 2 else "default"
        names.append(name)
        materials.append(mat)

        vc = int(L[i]); i += 1
        vs = np.array(" ".join(L[i:i + vc]).split(), dtype=np.float64).reshape(vc, 3)
        i += vc
        i += 1                      # flag（恒 0）

        fvf = int(L[i]); i += 1
        fvf_to_pos = np.empty(fvf, dtype=np.int64)
        for k in range(fvf):
            fvf_to_pos[k] = int(L[k * 3 + i].split()[0])
        i += fvf * 3

        mk = int(L[i]); i += 1
        if mk:
            raise ValueError("morph keys not supported (mesh '%s' has %d)" % (name, mk))

        fc = int(L[i]); i += 1
        tri = np.fromiter(
            (int(x) for k in range(fc) for x in L[i + k].split()),
            dtype=np.int64, count=fc * 3).reshape(fc, 3)
        i += fc

        pos_all.append(vs)
        tri_all.append(fvf_to_pos[tri] + off)      # fvf 索引 → 全局顶点索引
        off += vc

    if not pos_all:
        raise ValueError("empty trf: " + path)
    return np.vstack(pos_all), np.vstack(tri_all), materials, names


# ──────────────────────────────────────────────────────── 体素占位 / 合并
def voxelize(pos, tris, max_grid=DEFAULT_MAX_GRID, voxel=None, dilate=DEFAULT_DILATE):
    """顶点 + 面心 + 三条边中点 → 占位格 → 膨胀。返回 (occ, origin, cell)。"""
    lo = pos.min(axis=0)
    hi = pos.max(axis=0)
    size = np.maximum(hi - lo, 1e-3)
    if voxel is None:
        voxel = float(max(size) / max_grid)
        voxel = min(max(voxel, 0.15), 3.00)     # 15cm ~ 3m（大物件允许更粗）

    origin = lo - voxel
    dims = np.ceil(size / voxel).astype(int) + 3
    grid = np.zeros(tuple(dims), dtype=bool)

    tri_pos = pos[tris]                                     # M×3×3
    cent = tri_pos.mean(axis=1)
    edges = np.concatenate([
        (tri_pos[:, 0] + tri_pos[:, 1]) * 0.5,
        (tri_pos[:, 1] + tri_pos[:, 2]) * 0.5,
        (tri_pos[:, 2] + tri_pos[:, 0]) * 0.5,
    ], axis=0)

    pts = np.vstack([pos, cent, edges])                     # 稠密点云代替精确体素化
    idx = np.floor((pts - origin) / voxel).astype(np.int64)
    np.clip(idx, 0, dims - 1, out=idx)
    grid[idx[:, 0], idx[:, 1], idx[:, 2]] = True

    for _ in range(dilate):
        g = grid.copy()
        g[1:, :, :] |= grid[:-1, :, :]
        g[:-1, :, :] |= grid[1:, :, :]
        g[:, 1:, :] |= grid[:, :-1, :]
        g[:, :-1, :] |= grid[:, 1:, :]
        g[:, :, 1:] |= grid[:, :, :-1]
        g[:, :, :-1] |= grid[:, :, 1:]
        grid = g

    return grid, origin, voxel


def greedy_boxes(occ):
    """标准贪心盒合并：先在 x 扩，再在 y、z 扩整块。返回 [(i0,i1,j0,j1,k0,k1)] 含端点。"""
    used = np.zeros_like(occ)
    boxes = []
    nx, ny, nz = occ.shape
    for i, j, k in np.argwhere(occ):
        if used[i, j, k]:
            continue

        i2 = i
        while i2 + 1 < nx and occ[i2 + 1, j, k] and not used[i2 + 1, j, k]:
            i2 += 1

        j2 = j
        while j2 + 1 < ny and occ[i:i2 + 1, j2 + 1, k].all() and not used[i:i2 + 1, j2 + 1, k].any():
            j2 += 1

        k2 = k
        while k2 + 1 < nz and occ[i:i2 + 1, j:j2 + 1, k2 + 1].all() and not used[i:i2 + 1, j:j2 + 1, k2 + 1].any():
            k2 += 1

        used[i:i2 + 1, j:j2 + 1, k:k2 + 1] = True
        boxes.append((i, i2, j, j2, k, k2))
    return boxes


# ────────────────────────────────────────────────────────────────── 写 TRF
def boxes_to_mesh(boxes, origin, cell, lo=None, hi=None):
    """盒子 → (verts Nx3, tris Mx3)，顶点按坐标取整去重。

    lo/hi = 源网格包围盒：把膨胀（dilate）撑出来的外圈切掉，
    否则物理体会比模型大 1~3 个体素（实测 train 大出 8.5 m），玩家会撞到空气。
    切完变成零厚度的盒子直接丢弃。
    """
    vmap = {}
    verts = []
    tris = []

    def vid(p):
        key = (round(p[0], 4), round(p[1], 4), round(p[2], 4))
        got = vmap.get(key)
        if got is None:
            got = len(verts)
            vmap[key] = got
            verts.append(p)
        return got

    eps = 1e-4
    for (i0, i1, j0, j1, k0, k1) in boxes:
        x0, x1 = origin[0] + i0 * cell, origin[0] + (i1 + 1) * cell
        y0, y1 = origin[1] + j0 * cell, origin[1] + (j1 + 1) * cell
        z0, z1 = origin[2] + k0 * cell, origin[2] + (k1 + 1) * cell
        if lo is not None:
            x0, y0, z0 = max(x0, lo[0]), max(y0, lo[1]), max(z0, lo[2])
            x1, y1, z1 = min(x1, hi[0]), min(y1, hi[1]), min(z1, hi[2])
            if x1 - x0 <= eps or y1 - y0 <= eps or z1 - z0 <= eps:
                continue
        c = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
             (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
        q = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]
        for a, b, cc, d in q:
            ia, ib, ic, id_ = vid(c[a]), vid(c[b]), vid(c[cc]), vid(c[d])
            tris.append((ia, ib, ic))
            tris.append((ia, ic, id_))

    return np.array(verts, dtype=np.float64), np.array(tris, dtype=np.int64)


def write_trf(path, name, material, verts, tris):
    out = ["rfver 4", "mesh 1", "%s 0 %s" % (name, material), str(len(verts))]
    out.extend("%.5f %.5f %.5f" % (v[0], v[1], v[2]) for v in verts)
    out.append("0")
    out.append(str(len(verts)))                       # fvf 与顶点 1:1
    for k in range(len(verts)):
        out.append("%d 4294967295 0.0 0.0 1.0" % k)   # 法线随便给 +Z（物理体不用）
        out.append("0.0 0.0")
        out.append("0.0 0.0")
    out.append("0")                                   # morph keys
    out.append(str(len(tris)))
    out.extend("%d %d %d" % (t[0], t[1], t[2]) for t in tris)
    out.append("end")

    os.makedirs(os.path.dirname(path), exist_ok=True)
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(out) + "\n")


# ─────────────────────────────────────────────────────────────────── 主流程
def find_trf(name, src_dirs):
    for d in src_dirs:
        for root, _, files in os.walk(d):
            if name + ".trf" in files:
                return os.path.join(root, name + ".trf")
    return None


def build_one(name, src_dirs, out_dir, cls, voxel, dilate, max_boxes, max_grid=DEFAULT_MAX_GRID, quiet=False):
    src = find_trf(name, src_dirs)
    if src is None:
        print("[bo] 找不到源：%s.trf" % name)
        return None

    pos, tris, mats, mesh_names = read_trf(src)
    occ, origin, cell = voxelize(pos, tris, max_grid=max_grid, voxel=voxel, dilate=dilate)
    boxes = greedy_boxes(occ)
    verts, out_tris = boxes_to_mesh(boxes, origin, cell, pos.min(axis=0), pos.max(axis=0))

    mat = mats[0] if mats else "default"
    out_path = os.path.join(out_dir, cls, "bo_%s.trf" % name) if cls else os.path.join(out_dir, "bo_%s.trf" % name)
    write_trf(out_path, "bo_" + name, mat, verts, out_tris)

    size = pos.max(axis=0) - pos.min(axis=0)
    q = (pos.max(axis=0) - pos.min(axis=0)).round(1)
    tri_per_m2 = out_tris.shape[0]
    if not quiet:
        print("[bo] %-34s 源 %6d 顶点 / %7d 面  盒子 %5d → %6d 顶点 / %6d 面  "
              "网格 %s  体素 %.2fm  尺寸 %.1f×%.1f×%.1f"
              % (name, len(pos), len(tris), len(boxes), len(verts), len(out_tris),
                 "x".join(str(x) for x in occ.shape), cell, q[0], q[1], q[2]))
        if len(boxes) > max_boxes:
            print("      [warn] 盒子数超过预算 %d —— 调大 --voxel 或减小 --max-grid" % max_boxes)
    return {
        "name": name, "src": src, "out": out_path,
        "src_verts": int(len(pos)), "src_faces": int(len(tris)),
        "boxes": len(boxes), "verts": int(len(verts)), "tris": int(len(out_tris)),
        "voxel": cell, "grid": [int(x) for x in occ.shape],
        "size": [float(x) for x in size], "material": mat,
    }


def main():
    ap = argparse.ArgumentParser(description="RDR2 物理体（bo_）生成器 —— 体素 + 盒子合并")
    ap.add_argument("--model", action="append", default=[], help="模型名（可多次给），对应 <模型名>.trf")
    ap.add_argument("--list", help="模型名清单文件（每行一个）")
    ap.add_argument("--src", action="append", default=[], help="源 TRF 搜索目录（默认 deliverable/models 与 models_extra）")
    ap.add_argument("--out", default=DEFAULT_OUT, help="输出根目录（默认 AssetSources/ImortReady/rdr2_collision）")
    ap.add_argument("--cls", default="", help="子目录（bldg/prop/...，默认不加）")
    ap.add_argument("--voxel", type=float, default=None, help="体素边长（米）。默认 = 最长边/96，钳 0.2~1.5")
    ap.add_argument("--dilate", type=int, default=DEFAULT_DILATE, help="占位膨胀次数（默认 1）")
    ap.add_argument("--max-grid", type=int, default=DEFAULT_MAX_GRID, help="网格最长边上限（默认 96）")
    ap.add_argument("--max-boxes", type=int, default=DEFAULT_MAX_BOXES, help="盒子数告警阈值")
    a = ap.parse_args()

    names = list(a.model)
    if a.list:
        with io.open(a.list, encoding="utf-8") as f:
            names += [ln.strip() for ln in f if ln.strip() and not ln.startswith("#")]
    if not names:
        ap.print_help()
        return 2

    src = a.src or DEFAULT_SRC
    ok = 0
    total_boxes = total_tris = 0
    for n in names:
        r = build_one(n, src, a.out, a.cls, a.voxel, a.dilate, a.max_boxes, a.max_grid)
        if r:
            ok += 1
            total_boxes += r["boxes"]
            total_tris += r["tris"]

    print("[bo] 完成 %d/%d，盒子合计 %d，三角形合计 %d" % (ok, len(names), total_boxes, total_tris))
    return 0 if ok == len(names) else 1


if __name__ == "__main__":
    sys.exit(main())
