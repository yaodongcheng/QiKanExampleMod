# -*- coding: utf-8 -*-
"""Prove whether identical LOD levels are duplicated or shared inside the tpac."""
import os, struct, glob, collections

PACK = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\Publish\RDR2\AssetPackages"
VS = "bb1df897-584f-4770-abf2-663fe449f247"
MED = "3d41985f-24d2-4fc1-82e4-6a6e0da3f4e2"
METAMESH = "a08f8b97-197c-4bea-b95b-53846cae834e"
WANT = "new_com_02_bank"


def guid_str(b):
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
        atype = guid_str(buf[o:o+16]); o += 16
        o += 16
        if ver > 1: o += 4
        nlen = struct.unpack_from('<i', buf, o)[0]; o += 4
        name = buf[o:o+nlen].decode('utf-8', 'replace'); o += nlen
        meta = struct.unpack_from('<Q', buf, o)[0]; o += 8
        o += meta; o += 8
        nseg = struct.unpack_from('<i', buf, o)[0]; o += 4
        segs = []
        for _ in range(nseg):
            so, act, sto = struct.unpack_from('<QQQ', buf, o); o += 24
            owner = guid_str(buf[o:o+16]); o += 16
            stype = guid_str(buf[o:o+16]); o += 16
            o += 13
            segs.append((stype, so, act, sto, owner))
        ndep = struct.unpack_from('<i', buf, o)[0]; o += 4
        o += ndep * 48
        if atype == METAMESH and WANT in name:
            out.append((name, [s for s in segs if s[0] == VS]))
    return out


print("model                        #VS   offsets(unique?)  total bytes")
for fp in sorted(glob.glob(os.path.join(PACK, '*.tpac'))):
    try:
        got = scan(fp)
    except Exception:
        continue
    for name, vs in got:
        if not vs:
            continue
        offsets = [v[1] for v in vs]
        sizes = [v[3] for v in vs]
        print("%-28s %3d   distinct offsets=%d/%d   sum=%.3f MB"
              % (name, len(vs), len(set(offsets)), len(offsets), sum(sizes)/1024/1024))
        # aggregate by identical size to spot duplicated levels
        by_size = collections.Counter(sizes)
        dup = {s: c for s, c in by_size.items() if c > 1 and s > 10000}
        if dup:
            for s, c in dup.items():
                print("      %d segments share the same size %.2f MB  -> %s"
                      % (c, s/1024/1024,
                         "DUPLICATED (separate offsets)" if len(set(offsets)) == len(offsets) else "shared?"))

# whole-pack sanity: sum of all segment bytes vs file size
tot_seg = 0
tot_file = 0
for fp in sorted(glob.glob(os.path.join(PACK, '*.tpac'))):
    tot_file += os.path.getsize(fp)
print("\nsum of file sizes: %.3f GB" % (tot_file/1024**3))
