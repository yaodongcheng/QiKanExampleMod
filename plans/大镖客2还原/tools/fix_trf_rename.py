# -*- coding: utf-8 -*-
"""Re-do the TRF rename safely: only rewrite true part headers
(<name>.<slot> 0 <material>).  Then verify geometry equals source."""
import io, os, re
import numpy as np

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
GEN = os.path.join(RDR2, "out", "lod_import", "trf")
SRC = os.path.join(RDR2, "out", "lod_trf")
NAME = "new_com_02_bank"
PAIRS = [("lodtest_bank_orig.trf", "1_原版LOD0"),
         ("lodtest_bank_r33.trf", "2_减面33_按部件"),
         ("lodtest_bank_r50.trf", "3_减面50_均匀")]

# part header = first token is exactly  <NAME>.<digits>
HDR = re.compile(r"^(%s)\.(\d+) 0 (\S+)$" % re.escape(NAME))


def rename(src, dst, newname):
    n = 0
    out = []
    for ln in io.open(src, encoding="utf-8", errors="replace"):
        s = ln.rstrip("\n")
        m = HDR.match(s)
        if m:
            s = "%s.%s 0 %s" % (newname, m.group(2), m.group(3))
            n += 1
        out.append(s)
    io.open(dst, "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")
    return n


def parse(path):
    lines = io.open(path, encoding="utf-8", errors="replace").read().split("\n")
    i, n, parts = 0, len(lines), []
    while i < n:
        s = lines[i].strip()
        if not s:
            i += 1; continue
        if s.startswith("rfver") or s.startswith("mesh ") or s == "end":
            i += 1; continue
        tok = s.split()
        if len(tok) != 3 or tok[1] != "0":
            raise ValueError("%s line %d unexpected: %r" % (os.path.basename(path), i+1, s[:70]))
        pname, mat = tok[0], tok[2]; i += 1
        npos = int(lines[i].strip()); i += 1
        pos = np.fromstring(" ".join(lines[i:i+npos]), sep=" ").reshape(-1, 3)
        i += npos + 1
        nvert = int(lines[i].strip()); i += 1
        blk = np.fromstring(" ".join(lines[i:i+nvert*3]), sep=" ").reshape(nvert, 9)
        i += nvert*3
        pidx = blk[:, 0].astype(np.int64); nrm = blk[:, 2:5]; uv = blk[:, 5:7]
        bone = int(lines[i].strip()); i += 1
        if bone > 0:
            i += 1; w = int(lines[i].strip()); i += 1; i += w
        ntri = int(lines[i].strip()); i += 1
        tri = np.fromstring(" ".join(lines[i:i+ntri]), sep=" ").astype(np.int64).reshape(-1, 3)
        i += ntri
        parts.append(dict(name=pname, mat=mat, pos=pos, pidx=pidx, nrm=nrm, uv=uv, tri=tri))
    return parts


os.makedirs(GEN, exist_ok=True)
for gen_name, src_sub in PAIRS:
    newname = gen_name[:-4]
    src = os.path.join(SRC, src_sub, NAME + ".trf")
    dst = os.path.join(GEN, gen_name)
    n = rename(src, dst, newname)
    g = parse(dst)
    s = parse(src)
    ok = len(g) == len(s)
    for a, b in zip(g, s):
        if not (np.array_equal(a["pos"], b["pos"]) and np.array_equal(a["tri"], b["tri"])
                and np.array_equal(a["uv"], b["uv"]) and a["mat"] == b["mat"]):
            ok = False
    faces = sum(len(p["tri"]) for p in g)
    print("%-26s renamed_headers=%2d parts=%2d faces=%6d  geometry %s  %.2f MB"
          % (gen_name, n, len(g), faces,
             "IDENTICAL to source" if ok else "*** MISMATCH ***",
             os.path.getsize(dst) / 1024 / 1024))
