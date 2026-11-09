# -*- coding: utf-8 -*-
"""生成占位方块 TRF（给没有 LOD 碎片组的据点用，如 twi / abe）。

单位立方体：x/y 居中 ±0.5，z 从 0 到 1（底面在原点，方便 prefab 里按
(size_w, size_d, max_h) 缩放后直接坐地）。材质 = `no_tex`（模块里已有）。

输出：
  Modules/RDR2/AssetSources/ImortReady/rdr2_placeholder/rdr2_placeholder_cube.trf
导入后资产名 = `rdr2_placeholder_cube`（TRF 导入会去掉末尾 _lod，这里本来就没有）。
"""
import io
import os

OUT = (r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\RDR2"
       r"\AssetSources\ImortReady\rdr2_placeholder")
NAME = "rdr2_placeholder_cube"
MAT = "no_tex"
COLOR = 4294967295


def main():
    os.makedirs(OUT, exist_ok=True)
    # 每面 4 个顶点（独立法线），(p0..p3, normal)
    faces = [
        ([(-.5, -.5, 1), (.5, -.5, 1), (.5, .5, 1), (-.5, .5, 1)], (0, 0, 1)),      # +Z
        ([(-.5, -.5, 0), (-.5, .5, 0), (.5, .5, 0), (.5, -.5, 0)], (0, 0, -1)),     # -Z
        ([(-.5, -.5, 0), (.5, -.5, 0), (.5, -.5, 1), (-.5, -.5, 1)], (0, -1, 0)),   # -Y
        ([(.5, .5, 0), (-.5, .5, 0), (-.5, .5, 1), (.5, .5, 1)], (0, 1, 0)),        # +Y
        ([(-.5, .5, 0), (-.5, -.5, 0), (-.5, -.5, 1), (-.5, .5, 1)], (-1, 0, 0)),   # -X
        ([(.5, -.5, 0), (.5, .5, 0), (.5, .5, 1), (.5, -.5, 1)], (1, 0, 0)),        # +X
    ]
    uvs = [(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)]
    verts, nrms, uvl, tris = [], [], [], []
    for fi, (quad, n) in enumerate(faces):
        base = fi * 4
        for k, p in enumerate(quad):
            verts.append(p); nrms.append(n); uvl.append(uvs[k])
        tris.append((base + 0, base + 1, base + 2))
        tris.append((base + 0, base + 2, base + 3))

    n = len(verts)
    out = ["rfver 4", "mesh 1", "%s 0 %s" % (NAME, MAT), str(n)]
    out += ["%.6f %.6f %.6f" % v for v in verts]
    out += ["0", str(n)]
    for i in range(n):
        out.append("%d %d %.6f %.6f %.6f" % (i, COLOR, nrms[i][0], nrms[i][1], nrms[i][2]))
        out.append("%.6f %.6f" % uvl[i])
        out.append("0.000000 0.000000")
    out += ["0", str(len(tris))]
    out += ["%d %d %d" % t for t in tris]
    out.append("end")

    path = os.path.join(OUT, NAME + ".trf")
    with io.open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(out) + "\n")
    print("wrote", path, "%.1f KB  verts=%d tris=%d" % (os.path.getsize(path) / 1024, n, len(tris)))


if __name__ == "__main__":
    main()
