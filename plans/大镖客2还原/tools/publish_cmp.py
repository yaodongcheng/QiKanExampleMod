# -*- coding: utf-8 -*-
"""精确到 MB 的分段对比：三个 publish 测试包的 ME / VS / Texture。"""
import os
import struct
import sys

P = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\Publish"
DIRS = ["RDR2Map-tex100-lodbase", "RDR2Map-tex25-lodbase", "RDR2Map-tex25-lod5"]
SEG = {
    "bb1df897-584f-4770-abf2-663fe449f247": "VertexStreamData",
    "3d41985f-24d2-4fc1-82e4-6a6e0da3f4e2": "MeshEditData",
    "644030f6-8a42-4c86-b935-9b9daa9391c2": "EditmodeMiscData",
    "70ee4e2c-79e4-4b2d-8d54-d53ecd2a559c": "TexturePixelData",
}
ASSET = {"a08f8b97-197c-4bea-b95b-53846cae834e": "Metamesh",
         "c974cbcb-5f1c-49f6-9a32-2b5b6c92c2e8": "Texture",
         "1db01393-6902-4f19-83ba-b37a39830717": "Material"}


def g(b):
    d1 = int.from_bytes(b[0:4], 'little'); d2 = int.from_bytes(b[4:6], 'little')
    d3 = int.from_bytes(b[6:8], 'little'); d4 = b[8:16].hex()
    return "%08x-%04x-%04x-%s-%s" % (d1, d2, d3, d4[0:4], d4[4:16])


def scan(path):
    with open(path, 'rb') as f:
        buf = f.read()
    o = 0
    magic, ver = struct.unpack_from('<II', buf, o); o += 8
    o += 16
    nres, _, _ = struct.unpack_from('<III', buf, o); o += 12
    out = []
    for _ in range(nres):
        atype = g(buf[o:o + 16]); o += 16
        o += 16
        if ver > 1:
            o += 4
        nlen = struct.unpack_from('<i', buf, o)[0]; o += 4
        name = buf[o:o + nlen].decode('utf-8', 'replace'); o += nlen
        meta = struct.unpack_from('<Q', buf, o)[0]; o += 8
        o += meta; o += 8
        nseg = struct.unpack_from('<i', buf, o)[0]; o += 4
        segs = []
        for _ in range(nseg):
            so, act, sto = struct.unpack_from('<QQQ', buf, o); o += 24
            o += 16
            stype = g(buf[o:o + 16]); o += 16
            o += 13
            segs.append((SEG.get(stype, stype[:8]), act, sto))
        ndep = struct.unpack_from('<i', buf, o)[0]; o += 4
        o += ndep * 48
        out.append((ASSET.get(atype, atype[:8]), name, segs))
    return out


print("%-22s %8s" % ("包", "文件MB"))
tot = {}
for d in DIRS:
    ap = os.path.join(P, d, "AssetPackages")
    files = [os.path.join(ap, f) for f in os.listdir(ap) if f.lower().endswith(".tpac")]
    fb = sum(os.path.getsize(f) for f in files)
    tot[d] = dict(file=fb, seg={}, n={})
    for f in files:
        for atype, name, segs in scan(f):
            for st, act, sto in segs:
                k = (atype, st)
                tot[d]["seg"][k] = tot[d]["seg"].get(k, 0) + sto
                tot[d]["n"][k] = tot[d]["n"].get(k, 0) + 1
    print("%-22s %8.2f" % (d, fb / 1024 ** 2))

print("\n=== 分段（磁盘 MB / 未压缩 MB / 段数）===")
keys = set()
for d in DIRS:
    keys |= set(tot[d]["seg"].keys())
print("%-22s %-20s %9s %9s %5s" % ("包", "段", "磁盘", "未压缩", "n"))
for d in DIRS:
    for k in sorted(keys):
        if k in tot[d]["seg"]:
            # 需要 raw；上面没存 raw，重扫一遍太贵 -> 从文件大小与段类型估
            print("%-22s %-20s %9.3f %9s %5d"
                  % (d, "%s/%s" % k, tot[d]["seg"][k] / 1024 ** 2, "-", tot[d]["n"][k]))
    print()

print("=== 差值 ===")
for a, b, lab in ((DIRS[0], DIRS[1], "贴图 100% -> 25%（同 lodbase）"),
                  (DIRS[1], DIRS[2], "LOD base(7档) -> 5档（同 tex25）"),
                  (DIRS[0], DIRS[2], "两者同时")):
    fa = tot[a]["file"] / 1024 ** 2
    fb = tot[b]["file"] / 1024 ** 2
    print("  %-38s %8.2f -> %8.2f MB   %+8.2f MB  (%+.1f%%)"
          % (lab, fa, fb, fb - fa, 100.0 * (fb - fa) / fa))
