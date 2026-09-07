# -*- coding: utf-8 -*-
"""⚠️ 已废弃主链路（2026-09-07）：custom.export_heightmap 已直接产出 16bit PNG（heightmap_16bit.png），
本脚本原产物 terrain_heights.bin 不再生成——脚本保留仅供已有旧 bin 文件校验/转图使用。

历史说明：实机导出 terrain_heights.bin → 16bit 灰度 PNG（+ 与 NativeExample 对照检验）

对应 C# 侧命令：custom.export_heightmap（Debug/TerrainExportCommands.cs）
产物格式：魔数 "LWNHM1" + 头部元数据 + 逐节点原始 float32 高度（见下），
数据与编辑器「Export Heightmap」同源（同一 native 路径）。

用法:
  python raw_dump_to_png.py <terrain_heights.bin> [out_16bit.png]
  python raw_dump_to_png.py <bin> --native <NativeExample png>   # 对照原版导出（尺寸/相关性）

格式 v1 字段（全部 little-endian）:
  头部 8B 魔数 + int 版本 + int nodeDimX + int nodeDimY + float nodeSize
      + int layerCount + int layerVersion + float globalMin + float globalMax + int nodeCount
  每节点（Y 外循环、X 内循环，行列序与引擎一致）:
      int nx + int ny + int vtx + float quadLength + float nodeMin + float nodeMax
      + int dataCount + float[dataCount] heights
"""
import struct
import sys
import numpy as np
from PIL import Image

MAGIC = b"LWNHM1"
FMT_VERSION = 1


def read_bin(path):
    d = open(path, "rb").read()
    assert d[:6] == MAGIC, "魔数不对: %r" % d[:8]
    assert d[6:8] == b"\x00\x00", "魔数区非零填充: %r" % d[6:8]
    (ver, nx, ny, node_size, layer_count, layer_version,
     gmin, gmax, node_count) = struct.unpack_from("<iiifii ffi", d, 8)
    # 魔数 8 + 版本 4 + X 4 + Y 4 + size 4 + layerCount 4 + layerVersion 4 + gMin 4 + gMax 4 + nodeCount 4
    off = 8 + 24 + 12
    assert ver == FMT_VERSION, "格式版本不支持: %d" % ver
    blocks = []
    vtx_set = set()
    for _ in range(node_count):
        (bx, by, v, ql, nmin, nmax, ln) = struct.unpack_from("<iiifffi", d, off)
        off += 28
        arr = np.frombuffer(d, dtype="<f4", count=ln, offset=off).copy()
        off += 4 * ln
        blocks.append(((bx, by), v, ql, nmin, nmax, arr))
        vtx_set.add(v)
    assert off == len(d), "残留字节: %d（文件被截断或协议不匹配）" % (len(d) - off)
    return dict(ver=ver, node_dim=(nx, ny), node_size=node_size, layer_count=layer_count,
                layer_version=layer_version, gmin=gmin, gmax=gmax, node_count=node_count,
                vtx_set=vtx_set, blocks=blocks)


def assemble(meta, merge=True):
    """按节点拼总网格。
    merge=True 用「相邻节点共享边」约定：总 = nodeDim*(vtx-1)+1
    （判据：原版 16 节点 × 257 顶点 → 4097，与编辑器导出 4097x4097 对上；若实机 vtx=256，
     能对上 4096x4096 时同样成立，两约定写出来对照即知）。
    merge=False = 节点不共享边（总 = nodeDim*vtx）。
    """
    (nx, ny) = meta["node_dim"]
    blocks = sorted(meta["blocks"], key=lambda b: (b[0][1], b[0][0]))  # Y 外 X 内，与引擎存取序一致
    vtx = blocks[0][1]
    if merge:
        tw, th = nx * (vtx - 1) + 1, ny * (vtx - 1) + 1
    else:
        tw, th = nx * vtx, ny * vtx
    stride = vtx - 1 if merge else vtx
    grid = np.full((th, tw), np.nan, dtype=np.float32)
    for (bx, by), v, ql, nmin, nmax, arr in blocks:
        local = arr.reshape(v, v)
        yc, xc = by * stride, bx * stride
        if merge:
            grid[yc:yc + v, xc:xc + v] = local  # 相邻节点共享边：后写覆盖（同值）
        else:
            grid[yc:yc + v, xc:xc + v] = local
    assert not np.isnan(grid).any(), "拼图出现空洞（节点布局异常）"
    return grid, vtx


def main():
    argv = list(sys.argv[1:])
    native = None
    if "--native" in argv:
        i = argv.index("--native")
        native = argv[i + 1]
        del argv[i:i + 2]
    if len(argv) < 1:
        print("用法: python raw_dump_to_png.py <terrain_heights.bin> [out_16bit.png] [--native <png>]")
        return 1
    bin_path = argv[0]
    out_png = argv[1] if len(argv) > 1 else "heightmap_export_16bit.png"

    meta = read_bin(bin_path)
    nx, ny = meta["node_dim"]
    print("=" * 70)
    print("文件: %s" % bin_path)
    print("节点网格: %d x %d | 节点边长 %.3fm | 图层 %d v%d" % (nx, ny, meta["node_size"],
                                                          meta["layer_count"], meta["layer_version"]))
    print("全局 min/max(头): %.4f / %.4f" % (meta["gmin"], meta["gmax"]))
    print("每节点顶点数: %s" % sorted(meta["vtx_set"]))

    # 节点头 min/max 与块内实际值比对：一致 = 数组是整机坐标；不一致 = 可能是节点局部坐标
    mins = np.array([b[3] for b in meta["blocks"]])
    maxs = np.array([b[4] for b in meta["blocks"]])
    arr_min = np.array([b[5].min() for b in meta["blocks"]])
    arr_max = np.array([b[5].max() for b in meta["blocks"]])
    same = np.allclose(mins, arr_min, rtol=1e-4) and np.allclose(maxs, arr_max, rtol=1e-4)
    print("节点头 min/max 与块内实际一致: %s（不一致 = 节点数据可能为局部坐标，拼图需再核）" % same)

    grid_shared, vtx = assemble(meta, merge=True)
    grid_sep, _ = assemble(meta, merge=False)
    print("共享边约定: %d x %d | 不共享边约定: %d x %d" % (grid_shared.shape[1], grid_shared.shape[0],
                                                     grid_sep.shape[1], grid_sep.shape[0]))
    print("（原版 NativeExample 参考 = 4097x4097；用户主档 = 4096x2560 系 Dim*节点 约定，对不上时以实机为准）")

    grid = grid_shared
    lo, hi = meta["gmin"], meta["gmax"]
    if not np.isfinite(lo) or not np.isfinite(hi):
        lo, hi = grid.min(), grid.max()
    img = np.clip((grid - lo) / (hi - lo) * 65535.0, 0, 65535).astype(np.uint16)
    Image.fromarray(img).save(out_png)
    print("已写 16bit PNG: %s（按 [%0.4f, %0.4f] 归一）" % (out_png, lo, hi))
    print("网格数值: min %.4f / max %.4f / mean %.4f" % (grid.min(), grid.max(), grid.mean()))

    if native:
        ref = Image.open(native)
        rw, rh = ref.size
        print("-- native 对照: %s = %dx%d" % (native, rw, rh))
        if (rw, rh) != (grid.shape[1], grid.shape[0]):
            print("尺寸不一致（导出 %dx%d）——先定拼图约定/翻转，再做相关性" % (grid.shape[1], grid.shape[0]))
        else:
            r = np.asarray(ref).astype(np.float64).ravel()
            best = None
            for flip in (False, True):
                for rot in range(4):
                    g2 = np.rot90(np.flipud(grid) if flip else grid, rot).ravel()
                    c = np.corrcoef(r, (g2 - g2.mean()) / (g2.std() + 1e-9))[0, 1]
                    if best is None or c > best[0]:
                        best = (c, flip, rot)
            print("全翻转组合最佳: 相关性 r = %.4f（flipud=%s rot=%d°；1.0 = 同一数据）" % (best[0], best[1], best[2] * 90))
    return 0


if __name__ == "__main__":
    sys.exit(main())
