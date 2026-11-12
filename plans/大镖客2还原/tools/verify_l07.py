# -*- coding: utf-8 -*-
"""Verify the final LOD0-6 TRF: integrity + LOD0 must equal the r33 file exactly."""
import io, os, re
import numpy as np

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
F = os.path.join(RDR2, "deliverable", "compress", "trf", "lodtest_bank_r33_l07.trf")
L0 = os.path.join(RDR2, "deliverable", "compress", "trf", "lodtest_bank_r33.trf")


def parse(path):
    lines = io.open(path, encoding="utf-8", errors="replace").read().split("\n")
    i, n, decl, parts = 0, len(lines), None, []
    while i < n:
        s = lines[i].strip()
        if not s:
            i += 1; continue
        if s.startswith("rfver"):
            i += 1; continue
        if s.startswith("mesh "):
            decl = int(s.split()[1]); i += 1; continue
        if s == "end":
            i += 1; continue
        tok = s.split()
        if len(tok) != 3 or tok[1] != "0":
            raise ValueError("line %d: %r" % (i + 1, s[:70]))
        name, mat = tok[0], tok[2]; i += 1
        npos = int(lines[i].strip()); i += 1
        pos = np.fromstring(" ".join(lines[i:i + npos]), sep=" ").reshape(-1, 3)
        i += npos
        if lines[i].strip() != "0":
            raise ValueError("expected lone 0 at %d" % (i + 1))
        i += 1
        nvert = int(lines[i].strip()); i += 1
        blk = np.fromstring(" ".join(lines[i:i + nvert * 3]), sep=" ").reshape(nvert, 9)
        i += nvert * 3
        pidx = blk[:, 0].astype(np.int64); nrm = blk[:, 2:5]; uv = blk[:, 5:7]
        bone = int(lines[i].strip()); i += 1
        if bone > 0:
            i += 1; w = int(lines[i].strip()); i += 1; i += w
        ntri = int(lines[i].strip()); i += 1
        tri = np.fromstring(" ".join(lines[i:i + ntri]), sep=" ").astype(np.int64).reshape(-1, 3)
        i += ntri
        parts.append(dict(name=name, mat=mat, pos=pos, pidx=pidx, nrm=nrm, uv=uv, tri=tri))
    return decl, parts


def lod_of(s):
    m = re.search(r"\.lod(\d+)\.", s)
    return int(m.group(1)) if m else 0


decl, parts = parse(F)
print("declared=%s  parsed=%d  file=%.2f MB" % (decl, len(parts), os.path.getsize(F) / 1024 / 1024))

bad_idx = bad_tri = 0
for p in parts:
    if p["pidx"].min() < 0 or p["pidx"].max() >= len(p["pos"]):
        bad_idx += 1
    if p["tri"].min() < 0 or p["tri"].max() >= len(p["pidx"]):
        bad_tri += 1
print("bad position index = %d, bad triangle index = %d" % (bad_idx, bad_tri))

by = {}
for p in parts:
    d = by.setdefault(lod_of(p["name"]), [0, 0, 0])
    d[0] += 1; d[1] += len(p["tri"]); d[2] += len(p["pidx"])
print("\n  %-5s %6s %8s %8s" % ("LOD", "parts", "faces", "verts"))
for lv in sorted(by):
    c, f, v = by[lv]
    print("  %-5d %6d %8d %8d" % (lv, c, f, v))

# names must follow <name>.<i> for LOD0 and <name>.lodN.<i> otherwise
bad_names = []
for p in parts:
    lv = lod_of(p["name"])
    exp0 = re.match(r"^lodtest_bank_r33_l07\.\d+$", p["name"])
    expn = re.match(r"^lodtest_bank_r33_l07\.lod%d\.\d+$" % lv, p["name"])
    if (lv == 0 and not exp0) or (lv > 0 and not expn):
        bad_names.append(p["name"])
print("\nmalformed part names: %d" % len(bad_names))

# LOD0 must be identical to the r33 source
_, src = parse(L0)
src = [p for p in src if len(p["tri"])]
fin0 = [p for p in parts if lod_of(p["name"]) == 0]
okp = oku = okt = 0
for a, b in zip(fin0, src):
    if np.array_equal(a["pos"], b["pos"]) and np.array_equal(a["pidx"], b["pidx"]):
        okp += 1
    if np.array_equal(a["uv"], b["uv"]):
        oku += 1
    if np.array_equal(a["tri"], b["tri"]):
        okt += 1
print("LOD0 vs r33 file: pos+pidx %d/%d, uv %d/%d, tri %d/%d"
      % (okp, len(src), oku, len(src), okt, len(src)))
print("\nverdict: %s" % ("OK" if (not bad_idx and not bad_tri and not bad_names
                                  and okp == oku == okt == len(src)) else "PROBLEM"))
