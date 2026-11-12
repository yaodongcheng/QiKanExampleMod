# -*- coding: utf-8 -*-
"""Three different size units for the same geometry: TRF text vs tpac raw vs tpac on disk."""
import io, os, re
import numpy as np

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
T = os.path.join(RDR2, "deliverable", "compress", "trf")
FILES = [("lodtest_bank_orig.trf", "原版 单档 LOD0"),
         ("lodtest_bank_r33.trf", "r33 单档 LOD0"),
         ("lodtest_bank_r33_l07.trf", "r33 + 7 档链")]

VS_V, ME_V, IDX_F, ME_F = 48.1, 103.0, 6.0, 12.0
LZ_VS, LZ_ME = 0.51, 0.46      # measured LZ4 ratios


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
        nvert = int(lines[i].strip()); i += 1
        i += nvert * 3
        bone = int(lines[i].strip()); i += 1
        if bone > 0:
            i += 1; w = int(lines[i].strip()); i += 1; i += w
        ntri = int(lines[i].strip()); i += 1
        i += ntri
        out.append((nvert, ntri))
    return out


def lod_of(name):
    return 0


print("%-22s %8s %8s %10s %10s %10s %10s" %
      ("文件", "面数", "顶点", "TRF文本", "tpac原始", "tpac磁盘", "文本/磁盘"))
for fn, label in FILES:
    p = os.path.join(T, fn)
    allp = parse(p)
    V = sum(v for v, f in allp)
    F = sum(f for v, f in allp)
    text = os.path.getsize(p) / 1024 / 1024
    vs_raw = (V * VS_V + F * IDX_F) / 1024 / 1024
    me_raw = (V * ME_V + F * ME_F) / 1024 / 1024
    raw = vs_raw + me_raw
    disk = vs_raw * LZ_VS + me_raw * LZ_ME
    print("%-22s %8d %8d %9.2fM %9.2fM %9.2fM %9.2f"
          % (label, F, V, text, raw, disk, text / disk))
    print("   其中 顶点流 %.2fM(原始) / 编辑器 %.2fM(原始)" % (vs_raw, me_raw))

print("\n注：M = MiB（1024²）。TRF 文本是 ASCII，tpac 是二进制 + LZ4。")
