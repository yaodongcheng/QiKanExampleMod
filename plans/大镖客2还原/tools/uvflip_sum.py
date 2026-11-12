# -*- coding: utf-8 -*-
"""Summary across several models: is LOD0's V un-flipped while LOD1+ are flipped?"""
import io, os, re
import numpy as np
from scipy.spatial import cKDTree

MODELS = r"D:\BrainMaker\大镖客2解包分析\deliverable\models"
NAMES = ["new_com_02_bank", "new_gar_05_house_a10", "coo_01_church_wall",
         "val_bank", "bla_tailor_main_shop", "pro_house002"]


def parse(path):
    lines = io.open(path, encoding="utf-8", errors="replace").read().split("\n")
    i, n, parts = 0, len(lines), []
    while i < n:
        s = lines[i].strip()
        if not s or s == "end":
            i += 1; continue
        tok = s.split()
        if len(tok) != 3 or tok[1] != "0":
            i += 1; continue
        pname, mat = tok[0], tok[2]; i += 1
        npos = int(lines[i].strip()); i += 1
        pos = np.fromstring(" ".join(lines[i:i+npos]), sep=" ").reshape(-1, 3)
        i += npos + 1
        nvert = int(lines[i].strip()); i += 1
        blk = np.fromstring(" ".join(lines[i:i+nvert*3]), sep=" ").reshape(nvert, 9)
        i += nvert*3
        pidx = blk[:, 0].astype(np.int64); uv = blk[:, 5:7]
        bone = int(lines[i].strip()); i += 1
        if bone > 0:
            i += 1; w = int(lines[i].strip()); i += 1; i += w
        ntri = int(lines[i].strip()); i += 1
        i += ntri
        parts.append(dict(name=pname, mat=mat, pos=pos, pidx=pidx, uv=uv))
    return parts


def lod_of(s):
    m = re.search(r"\.lod(\d+)\.", s)
    return int(m.group(1)) if m else 0


def slot_of(s):
    return int(re.search(r"\.(\d+)$", s).group(1))


print("%-28s %6s %6s %8s %8s %8s" % ("model", "slots", "tested", "FLIPPED", "same", "other"))
for NAME in NAMES:
    p = os.path.join(MODELS, NAME + ".trf")
    if not os.path.isfile(p):
        print("%-28s  MISSING" % NAME)
        continue
    by = {}
    for pt in parse(p):
        by.setdefault((slot_of(pt["name"]), pt["mat"]), {})[lod_of(pt["name"])] = pt
    n_slot = len(by)
    tested = flipped = same = other = 0
    for key in sorted(by):
        d = by[key]
        if 0 not in d:
            continue
        A = d[0]["pos"][d[0]["pidx"]]; UA = d[0]["uv"]
        t = cKDTree(A)
        for lod in sorted(k for k in d if k > 0):
            B = d[lod]["pos"][d[lod]["pidx"]]; UB = d[lod]["uv"]
            dist, idx = t.query(B, k=1)
            m = dist < 1e-4
            if m.sum() < 10:
                continue
            ua = UA[idx[m]]; ub = UB[m]
            raw_v = float(np.median(np.abs(ua[:, 1] - ub[:, 1])))
            fl_v = float(np.median(np.abs((1.0 - ua[:, 1]) - ub[:, 1])))
            tested += 1
            if fl_v < 1e-3 < raw_v:
                flipped += 1
            elif raw_v < 1e-3:
                same += 1
            else:
                other += 1
    print("%-28s %6d %6d %8d %8d %8d" % (NAME, n_slot, tested, flipped, same, other))
