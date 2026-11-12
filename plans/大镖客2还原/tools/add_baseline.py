# -*- coding: utf-8 -*-
"""Copy the shipped bank TRF (original LOD0-6 chain) into compress\trf as a
renamed baseline asset, then verify geometry is untouched."""
import io, os, re
import numpy as np

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
SRC = os.path.join(RDR2, "deliverable", "models", "new_com_02_bank.trf")
DST = os.path.join(RDR2, "deliverable", "compress", "trf", "lodtest_bank_base.trf")
OLD = "new_com_02_bank"
NEW = "lodtest_bank_base"

HDR = re.compile(r"^(%s)(\.lod\d+)?\.(\d+) 0 (\S+)$" % re.escape(OLD))


def rename(src, dst):
    n = 0
    out = []
    for ln in io.open(src, encoding="utf-8", errors="replace"):
        s = ln.rstrip("\n")
        m = HDR.match(s)
        if m:
            s = "%s%s.%s 0 %s" % (NEW, m.group(2) or "", m.group(3), m.group(4))
            n += 1
        out.append(s)
    io.open(dst, "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")
    return n


def parse(path):
    lines = io.open(path, encoding="utf-8", errors="replace").read().split("\n")
    i, n, out = 0, len(lines), []
    decl = None
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
            raise ValueError("%s:%d %r" % (os.path.basename(path), i + 1, s[:70]))
        name, mat = tok[0], tok[2]; i += 1
        npos = int(lines[i].strip()); i += 1
        pos = np.fromstring(" ".join(lines[i:i + npos]), sep=" ").reshape(-1, 3)
        i += npos + 1
        nv = int(lines[i].strip()); i += 1
        blk = np.fromstring(" ".join(lines[i:i + nv * 3]), sep=" ").reshape(nv, 9)
        i += nv * 3
        pidx = blk[:, 0].astype(np.int64); nrm = blk[:, 2:5]; uv = blk[:, 5:7]
        b = int(lines[i].strip()); i += 1
        if b > 0:
            i += 1; w = int(lines[i].strip()); i += 1; i += w
        nt = int(lines[i].strip()); i += 1
        tri = np.fromstring(" ".join(lines[i:i + nt]), sep=" ").astype(np.int64).reshape(-1, 3)
        i += nt
        out.append(dict(name=name, mat=mat, pos=pos, pidx=pidx, nrm=nrm, uv=uv, tri=tri))
    return decl, out


def lod_of(s):
    m = re.search(r"\.lod(\d+)\.", s)
    return int(m.group(1)) if m else 0


n = rename(SRC, DST)
ds, dp = parse(SRC)
dg, gp = parse(DST)
ok = len(dp) == len(gp)
for a, b in zip(dp, gp):
    if not (np.array_equal(a["pos"], b["pos"]) and np.array_equal(a["tri"], b["tri"])
            and np.array_equal(a["uv"], b["uv"]) and a["mat"] == b["mat"]):
        ok = False

by = {}
for p in gp:
    d = by.setdefault(lod_of(p["name"]), [0, 0, 0])
    d[0] += 1; d[1] += len(p["tri"]); d[2] += len(p["pidx"])

print("renamed headers = %d / parts = %d (declared %s)" % (n, len(gp), dg))
print("geometry identical to models source: %s" % ok)
print("file: %.2f MiB" % (os.path.getsize(DST) / 1024 / 1024))
print("\n  %-5s %6s %8s %8s" % ("LOD", "parts", "faces", "verts"))
for lv in sorted(by):
    c, f, v = by[lv]
    print("  %-5d %6d %8d %8d" % (lv, c, f, v))
print("\n  total faces = %d" % sum(x[1] for x in by.values()))
