#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""RMDL1 → OBJ（只为预览/Blender 复核用；顶点在前，Blender 才吃得下）。

用法：python rmdl_to_obj.py <in.mdl> <out.obj> [--scale 1.0] [--flip-y]
"""
import struct
import sys

import numpy as np


def main():
    src, dst = sys.argv[1], sys.argv[2]
    scale = 1.0
    flip_y = False
    if "--scale" in sys.argv:
        scale = float(sys.argv[sys.argv.index("--scale") + 1])
    if "--flip-y" in sys.argv:
        flip_y = True

    d = open(src, "rb").read()
    assert d[:5] == b"RMDL1", "not RMDL1"
    nsub = struct.unpack_from("<i", d, 8)[0]
    subs = []
    for s in range(nsub):
        o = 12 + 24 + s * 28
        texId, vCount, iCount, posOff, nrmOff, uvOff, idxOff = struct.unpack_from("<7i", d, o)
        pos = np.frombuffer(d, dtype="<f4", count=vCount * 3, offset=posOff).reshape(-1, 3).astype(np.float64)
        nrm = np.frombuffer(d, dtype="<f4", count=vCount * 3, offset=nrmOff).reshape(-1, 3).astype(np.float64)
        uv = np.frombuffer(d, dtype="<f4", count=vCount * 2, offset=uvOff).reshape(-1, 2).astype(np.float64)
        idx = np.frombuffer(d, dtype="<i4", count=iCount, offset=idxOff).reshape(-1, 3)
        subs.append((pos, nrm, uv, idx))

    with open(dst, "w", encoding="utf-8", newline="\n") as f:
        f.write("# converted from %s\n" % src)
        base = 1
        for si, (pos, nrm, uv, idx) in enumerate(subs):
            p = pos.copy()
            n = nrm.copy()
            if flip_y:
                p[:, 1] *= -1.0
                n[:, 1] *= -1.0
            p *= scale
            f.write("o mesh_%d\n" % si)
            for a, b, c in p:
                f.write("v %.6f %.6f %.6f\n" % (a, b, c))
            for a, b in uv:
                f.write("vt %.6f %.6f\n" % (a, b))
            for a, b, c in n:
                f.write("vn %.6f %.6f %.6f\n" % (a, b, c))
            for tri in idx:
                i0, i1, i2 = (tri + base).tolist()
                f.write("f %d/%d/%d %d/%d/%d %d/%d/%d\n"
                        % (i0, i0, i0, i1, i1, i1, i2, i2, i2))
            base += len(pos)
    print("wrote", dst)


if __name__ == "__main__":
    main()
