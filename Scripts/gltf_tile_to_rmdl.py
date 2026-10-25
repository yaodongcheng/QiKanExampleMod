#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""glTF 瓦片里的一个 mesh → RMDL1（.mdl），给 tpaccli meshmake / meshbatch 用。

背景：`export/previews/sdn/tiles/*.gltf` 是带**完整变换 + albedo/法线/ORM** 的圣丹尼斯瓦片，
但 tpaccli 只吃 RMDL1，所以先转。

用法：
  python gltf_tile_to_rmdl.py <tile.gltf> <mesh_index> <out.mdl> [--sub N]
    --sub N   只导出第 N 个 primitive（默认 -1 = 全部，各成一个子网格）

RMDL1 布局（见解包交接 README §5.3）：
  0    "RMDL1\\0\\0\\0"
  8    uint32 nsub
  12   float32[6] bbox(minx,miny,minz,maxx,maxy,maxz)
  36   nsub × 28B: texId(=材质序号),vCount,iCount,posOff,nrmOff,uvOff,idxOff
  之后 按偏移取数据
"""

import json
import os
import struct
import sys

import numpy as np


def _read_accessor(doc, bin_data, idx):
    acc = doc["accessors"][idx]
    bv = doc["bufferViews"][acc["bufferView"]]
    comp = acc["componentType"]
    typ = acc["type"]
    n = acc["count"]
    ncomp = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[typ]
    dtype = {5126: "<f4", 5123: "<u2", 5125: "<u4", 5121: "<u1"}[comp]
    itemsize = np.dtype(dtype).itemsize
    base = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = bv.get("byteStride") or (itemsize * ncomp)
    out = np.empty((n, ncomp), dtype=dtype)
    for i in range(n):
        off = base + i * stride
        out[i] = np.frombuffer(bin_data, dtype=dtype, count=ncomp, offset=off)
    return out.reshape(n, ncomp)


def main():
    if len(sys.argv) < 4:
        sys.exit(__doc__)
    gltf_path, mesh_index, out_path = sys.argv[1], int(sys.argv[2]), sys.argv[3]
    only = -1
    if "--sub" in sys.argv:
        only = int(sys.argv[sys.argv.index("--sub") + 1])

    doc = json.load(open(gltf_path, encoding="utf-8"))
    base_dir = os.path.dirname(os.path.abspath(gltf_path))
    buf = doc["buffers"][0]
    bin_data = open(os.path.join(base_dir, buf["uri"]), "rb").read()

    mesh = doc["meshes"][mesh_index]
    prims = mesh["primitives"] if only < 0 else [mesh["primitives"][only]]

    subs = []
    for p in prims:
        pos = _read_accessor(doc, bin_data, p["attributes"]["POSITION"]).astype("<f4")
        nrm = _read_accessor(doc, bin_data, p["attributes"]["NORMAL"]).astype("<f4")
        uv = _read_accessor(doc, bin_data, p["attributes"]["TEXCOORD_0"]).astype("<f4")
        idx = _read_accessor(doc, bin_data, p["indices"]).astype("<i4").reshape(-1)
        subs.append((pos, nrm, uv, idx, p.get("material", -1)))

    allpos = np.concatenate([x[0] for x in subs])
    bbox = ([float(allpos[:, i].min()) for i in range(3)]
            + [float(allpos[:, i].max()) for i in range(3)])

    header = b"RMDL1\0\0\0" + struct.pack("<i", len(subs)) + struct.pack("<6f", *bbox)
    data_off = len(header) + 28 * len(subs)
    table = b""
    blobs = []
    off = data_off
    for pos, nrm, uv, idx, mat in subs:
        pb, nb, ub, ib_ = pos.tobytes(), nrm.tobytes(), uv.tobytes(), idx.tobytes()
        table += struct.pack("<7i", mat, len(pos), len(idx), off,
                             off + len(pb), off + len(pb) + len(nb),
                             off + len(pb) + len(nb) + len(ub))
        blobs += [pb, nb, ub, ib_]
        off += len(pb) + len(nb) + len(ub) + len(ib_)

    with open(out_path, "wb") as f:
        f.write(header + table + b"".join(blobs))

    tot_v = sum(len(x[0]) for x in subs)
    tot_t = sum(len(x[3]) // 3 for x in subs)
    print("wrote %s | mesh=%s subs=%d verts=%d tris=%d bbox=%s"
          % (out_path, mesh.get("name"), len(subs), tot_v, tot_t,
             [round(v, 2) for v in bbox]))


if __name__ == "__main__":
    main()
