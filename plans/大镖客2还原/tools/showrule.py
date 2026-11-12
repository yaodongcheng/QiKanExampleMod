# -*- coding: utf-8 -*-
"""Show per-part face counts for the three TRF variants -> proves which rule each used."""
import io, os, re
import numpy as np

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
GEN = os.path.join(RDR2, "out", "lod_import", "trf")
HDR = re.compile(r"^(\S+)\.(\d+) 0 (\S+)$")


def parts(path):
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
            raise ValueError("unexpected %r" % s[:60])
        name, mat = tok[0], tok[2]; i += 1
        npos = int(lines[i].strip()); i += 1
        i += npos + 1
        nvert = int(lines[i].strip()); i += 1
        i += nvert * 3
        bone = int(lines[i].strip()); i += 1
        if bone > 0:
            i += 1; w = int(lines[i].strip()); i += 1; i += w
        ntri = int(lines[i].strip()); i += 1
        i += ntri
        out.append((int(re.search(r"\.(\d+)$", name).group(1)), mat, nvert, ntri))
    return sorted(out)


files = {}
for fn in ("lodtest_bank_orig.trf", "lodtest_bank_r33.trf", "lodtest_bank_r50.trf"):
    files[fn] = {s: (v, t) for s, m, v, t in parts(os.path.join(GEN, fn))}

o = files["lodtest_bank_orig.trf"]
a = files["lodtest_bank_r33.trf"]
b = files["lodtest_bank_r50.trf"]

print("%-3s %-46s %8s %8s %8s %7s %7s" %
      ("slot", "material", "orig F", "r33 F", "r50 F", "r33%", "r50%"))
tot = [0, 0, 0]
for s in sorted(o):
    mat = [m for ss, m, v, t in parts(os.path.join(GEN, "lodtest_bank_orig.trf")) if ss == s][0]
    f0 = o[s][1]; f1 = a[s][1]; f2 = b[s][1]
    tot[0] += f0; tot[1] += f1; tot[2] += f2
    print("%-3d %-46s %8d %8d %8d %6.0f%% %6.0f%%"
          % (s, mat[:46], f0, f1, f2, 100.0*f1/f0, 100.0*f2/f0))
print("%-3s %-46s %8d %8d %8d %6.0f%% %6.0f%%"
      % ("", "TOTAL", tot[0], tot[1], tot[2],
         100.0*tot[1]/tot[0], 100.0*tot[2]/tot[0]))
