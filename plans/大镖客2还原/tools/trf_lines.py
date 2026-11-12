# -*- coding: utf-8 -*-
"""Account for every line of a TRF: prove there is no editor-data section."""
import io, os, re, sys
import numpy as np

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
T = os.path.join(RDR2, "deliverable", "compress", "trf")
FILES = ["lodtest_bank_orig.trf", "lodtest_bank_r33.trf", "lodtest_bank_r33_l07.trf"]

for fn in FILES:
    p = os.path.join(T, fn)
    lines = io.open(p, encoding="utf-8", errors="replace").read().split("\n")
    n = len(lines)
    i = 0
    cat = {"rfver": 0, "mesh 头": 0, "材质名": 0, "位置数组长度": 0, "位置": 0,
           "分段常量0": 0, "顶点数": 0, "顶点FVF": 0, "顶点UV": 0, "顶点UV2空位": 0,
           "骨骼数": 0, "三角形数": 0, "三角形": 0, "end": 0, "空行": 0}
    mats, bone_sum, parts = set(), 0, 0
    other = []
    while i < n:
        s = lines[i].strip()
        if not s:
            cat["空行"] += 1; i += 1; continue
        if s.startswith("rfver"):
            cat["rfver"] += 1; i += 1; continue
        if s.startswith("mesh "):
            cat["mesh 头"] += 1; i += 1; continue
        if s == "end":
            cat["end"] += 1; i += 1; continue
        tok = s.split()
        if len(tok) == 3 and tok[1] == "0":
            cat["材质名"] += 1; mats.add(tok[2]); parts += 1; i += 1
            npos = int(lines[i].strip()); cat["位置数组长度"] += 1; i += 1
            cat["位置"] += npos; i += npos
            if lines[i].strip() != "0":
                other.append((i + 1, lines[i][:50])); 
            cat["分段常量0"] += 1; i += 1
            nv = int(lines[i].strip()); cat["顶点数"] += 1; i += 1
            cat["顶点FVF"] += nv; cat["顶点UV"] += nv; cat["顶点UV2空位"] += nv
            i += nv * 3
            b = int(lines[i].strip()); cat["骨骼数"] += 1; bone_sum += b; i += 1
            if b > 0:
                i += 1; w = int(lines[i].strip()); i += 1; i += w
            nt = int(lines[i].strip()); cat["三角形数"] += 1; i += 1
            cat["三角形"] += nt; i += nt
            continue
        other.append((i + 1, lines[i][:60])); i += 1

    total = sum(cat.values())
    print("\n== %s ==" % fn)
    for k, v in cat.items():
        if v:
            print("   %-14s %8d" % (k, v))
    print("   %-14s %8d   (文件总行数 %d)" % ("合计", total, n))
    print("   材质数 %d, 部件数 %d, 骨骼段总数 %d" % (len(mats), parts, bone_sum))
    print("   未归类行: %d  %s" % (len(other), other[:3]))
    print("   结论: %s" % ("全部行可归类到网格数据，无编辑器数据段"
                           if not other and total == n else "有未解释内容"))
