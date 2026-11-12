# -*- coding: utf-8 -*-
"""Compare all four test TRFs in one table."""
import io, os
import numpy as np

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
T = os.path.join(RDR2, "deliverable", "compress", "trf")
FILES = [("lodtest_bank_base.trf", "A 原版 LOD0-6（交付现状）"),
         ("lodtest_bank_orig.trf", "B 原版 LOD0 单档"),
         ("lodtest_bank_r33.trf", "C r33 LOD0 单档"),
         ("lodtest_bank_r33_l07.trf", "D r33 LOD0-6（新链）")]
VS_V, ME_V, IDX_F, ME_F = 48.1, 103.0, 6.0, 12.0
LZ_VS, LZ_ME = 0.51, 0.46


def parse(path):
    lines = io.open(path, encoding="utf-8", errors="replace").read().split("\n")
    i, n, out = 0, len(lines), []
    while i < n:
        s = lines[i].strip()
        if not s:
            i += 1; continue
        if s.startswith("rfver") or s.startswith("mesh ") or s == "end":
            i += 1; continue
        tok = s.split()
        if len(tok) != 3 or tok[1] != "0":
            raise ValueError("%r" % s[:60])
        i += 1
        npos = int(lines[i].strip()); i += 1
        i += npos + 1
        nv = int(lines[i].strip()); i += 1
        i += nv * 3
        b = int(lines[i].strip()); i += 1
        if b > 0:
            i += 1; w = int(lines[i].strip()); i += 1; i += w
        nt = int(lines[i].strip()); i += 1
        i += nt
        out.append((nv, nt))
    return out


print("%-28s %8s %8s %9s %9s %9s" % ("资产", "面数", "顶点", "TRF文本", "tpac原始", "tpac落盘"))
rows = []
for fn, label in FILES:
    p = os.path.join(T, fn)
    a = parse(p)
    V = sum(v for v, f in a)
    F = sum(f for v, f in a)
    text = os.path.getsize(p) / 1024 / 1024
    vs = (V * VS_V + F * IDX_F) / 1024 / 1024
    me = (V * ME_V + F * ME_F) / 1024 / 1024
    disk = vs * LZ_VS + me * LZ_ME
    rows.append((label, F, V, text, vs + me, disk))
    print("%-28s %8d %8d %8.2fM %8.2fM %8.2fM" % (label, F, V, text, vs + me, disk))

print()
b = rows[0][5]
for label, F, V, text, raw, disk in rows:
    print("  %-28s 落盘 %5.2f MiB  = 基准的 %5.1f%%   面数 %5.1f%%"
          % (label, disk, 100 * disk / b, 100.0 * F / rows[0][1]))
