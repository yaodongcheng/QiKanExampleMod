# -*- coding: utf-8 -*-
"""验证剥离结果：保留段必须逐字节一致，删除段确实消失，其他字段不变。"""
import hashlib
import os
import struct

SEG = {"bb1df897-584f-4770-abf2-663fe449f247": "VS",
       "3d41985f-24d2-4fc1-82e4-6a6e0da3f4e2": "MED",
       "644030f6-8a42-4c86-b935-9b9daa9391c2": "MISC",
       "70ee4e2c-79e4-4b2d-8d54-d53ecd2a559c": "TEXPIX"}
P = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\Publish"
T = os.path.join(os.environ.get("TEMP", ""), "rdr2_slim", "stripped")


def g(b):
    d1 = int.from_bytes(b[0:4], 'little'); d2 = int.from_bytes(b[4:6], 'little')
    d3 = int.from_bytes(b[6:8], 'little'); d4 = b[8:16].hex()
    return "%08x-%04x-%04x-%s-%s" % (d1, d2, d3, d4[0:4], d4[4:16])


def scan(path):
    with open(path, 'rb') as f:
        buf = f.read()
    o = 0
    magic, ver = struct.unpack_from('<II', buf, o); o += 8
    assert magic == 0x43415054
    o += 16
    nres, doff, reserve = struct.unpack_from('<III', buf, o); o += 12
    out = {}
    for _ in range(nres):
        atype = g(buf[o:o + 16]); o += 16
        o += 16
        if ver > 1:
            o += 4
        nlen = struct.unpack_from('<i', buf, o)[0]; o += 4
        name = buf[o:o + nlen].decode('utf-8', 'replace'); o += nlen
        meta = struct.unpack_from('<Q', buf, o)[0]; o += 8
        o += meta + 8
        nseg = struct.unpack_from('<i', buf, o)[0]; o += 4
        for _ in range(nseg):
            so, act, sto = struct.unpack_from('<QQQ', buf, o)
            stype = g(buf[o + 40:o + 56])
            o += 69
            kind = SEG.get(stype, stype[:8])
            payload = buf[so:so + sto]
            out[(name, kind)] = (act, sto, hashlib.sha256(payload).hexdigest()[:16])
        ndep = struct.unpack_from('<i', buf, o)[0]; o += 4
        o += ndep * 48
    return out, len(buf), nres


for n in ["RDR2Map-tex25-lod5", "RDR2Map-tex25-lodbase", "RDR2Map-tex100-lodbase"]:
    src = os.path.join(P, n, "AssetPackages", "pack0.tpac")
    dst = os.path.join(T, n + ".tpac")
    a, la, na = scan(src)
    b, lb, nb = scan(dst)
    print("=== %s ===" % n)
    print("  大小 %.3f -> %.3f MB (-%.1f%%)   资源数 %d -> %d"
          % (la / 1024 ** 2, lb / 1024 ** 2, 100.0 * (la - lb) / la, na, nb))
    kept_a = {k: v for k, v in a.items() if k[1] in ("VS", "TEXPIX")}
    bad = [k for k in kept_a if k not in b]
    diff = [k for k in kept_a if k in b and b[k] != kept_a[k]]
    extra = [k for k in b if k not in kept_a]
    print("  保留段应有 %d，实际 %d；缺失 %d，内容不一致 %d，多余 %d"
          % (len(kept_a), len(b), len(bad), len(diff), len(extra)))
    if diff[:3]:
        for k in diff[:3]:
            print("     差异: %s %s  %s -> %s" % (k[0], k[1], kept_a[k], b[k]))
    print("  MED 残留: %d" % sum(1 for k in b if k[1] == "MED"))
    print("  结论: %s" % ("通过" if not bad and not diff and not extra else "有问题"))
    print()
