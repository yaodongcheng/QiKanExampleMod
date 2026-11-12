# -*- coding: utf-8 -*-
"""TPAC index-only parser: sum storage/actual bytes per asset type + segment type.
No pixel/mesh data is loaded, so this is fast and read-only."""
import io, os, struct, sys, collections, glob

PACK_DIR = (sys.argv[1] if len(sys.argv) > 1 else
            r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\Publish\RDR2\AssetPackages")

ASSET_TYPES = {
    "a08f8b97-197c-4bea-b95b-53846cae834e": "Metamesh",
    "c974cbcb-5f1c-49f6-9a32-2b5b6c92c2e8": "Texture",
    "1db01393-6902-4f19-83ba-b37a39830717": "Material",
    "c635a3d5-eabb-45dd-883e-aa57e4196113": "Skeleton",
    "506509c8-e563-4ca4-b166-a53b92e913a7": "AnimationClip",
}
SEG_TYPES = {
    "bb1df897-584f-4770-abf2-663fe449f247": "VertexStreamData",
    "3d41985f-24d2-4fc1-82e4-6a6e0da3f4e2": "MeshEditData",
    "644030f6-8a42-4c86-b935-9b9daa9391c2": "EditmodeMiscData",
    "70ee4e2c-79e4-4b2d-8d54-d53ecd2a559c": "TexturePixelData",
    "7c475be2-7c8d-431d-b0e1-53218d047fb9": "TextureImportSettingsData",
    "8ab981a7-6ba0-4908-9606-91ad341d19a9": "TextureSourceInfoData",
    "af41613e-bd4d-46ec-8284-debb56c042dc": "ClothMappingData",
    "b77ddd3c-afe9-449b-9fe5-99de8b4060c1": "MorphDefinitionData",
}
STORAGE = {0: "Raw", 1: "LZ4HC"}

GB = 1024.0 ** 3


def guid_str(b):
    d1 = int.from_bytes(b[0:4], 'little')
    d2 = int.from_bytes(b[4:6], 'little')
    d3 = int.from_bytes(b[6:8], 'little')
    d4 = b[8:16].hex()
    return "%08x-%04x-%04x-%s-%s" % (d1, d2, d3, d4[0:4], d4[4:16])


def parse(path):
    with open(path, 'rb') as f:
        buf = f.read()
    o = 0
    magic, ver = struct.unpack_from('<II', buf, o); o += 8
    if magic != 0x43415054:
        raise ValueError('not tpac')
    o += 16                                   # package guid
    nres, dataoff, _res = struct.unpack_from('<III', buf, o); o += 12
    segs = []
    for _ in range(nres):
        atype = guid_str(buf[o:o+16]); o += 16
        o += 16                               # asset guid
        if ver > 1:
            o += 4                            # asset version
        nlen = struct.unpack_from('<i', buf, o)[0]; o += 4
        name = buf[o:o+nlen].decode('utf-8', 'replace'); o += nlen
        meta = struct.unpack_from('<Q', buf, o)[0]; o += 8
        o += meta
        o += 8                                # checksum
        nseg = struct.unpack_from('<i', buf, o)[0]; o += 4
        for _ in range(nseg):
            so, act, sto = struct.unpack_from('<QQQ', buf, o); o += 24
            o += 16                           # seg guid
            stype = guid_str(buf[o:o+16]); o += 16
            o += 8 + 4 + 1                    # unknown ulong, uint, format byte
            segs.append((atype, stype, act, sto))
        ndep = struct.unpack_from('<i', buf, o)[0]; o += 4
        o += ndep * 48
    return ver, nres, segs, len(buf)


def main():
    files = sorted(glob.glob(os.path.join(PACK_DIR, '*.tpac')))
    print("packs: %d" % len(files))
    by_seg = collections.Counter()       # storage size
    by_seg_act = collections.Counter()
    by_seg_cnt = collections.Counter()
    by_asset = collections.Counter()
    fmt_bytes = collections.Counter()
    total_disk = 0
    total_index = 0
    vers = set()
    for fp in files:
        try:
            ver, nres, segs, fsize = parse(fp)
        except Exception as e:
            print("  FAIL %s: %r" % (os.path.basename(fp), e))
            continue
        vers.add(ver)
        total_disk += fsize
        for atype, stype, act, sto in segs:
            an = ASSET_TYPES.get(atype, atype)
            sn = SEG_TYPES.get(stype, stype)
            by_seg[(an, sn)] += sto
            by_seg_act[(an, sn)] += act
            by_seg_cnt[(an, sn)] += 1
            by_asset[an] += sto
    print("tpac versions: %s" % vers)
    print("sum of pack file sizes = %.3f GB" % (total_disk / GB))
    print()
    print("=== disk bytes by asset type ===")
    for k, v in by_asset.most_common():
        print("  %-12s %8.2f GB" % (k, v / GB))
    print()
    print("=== disk bytes by (asset, segment) ===")
    for (an, sn), v in sorted(by_seg.items(), key=lambda x: -x[1])[:20]:
        print("  %-11s %-26s %8.3f GB  (%8.3f GB raw)  n=%d"
              % (an, sn, v / GB, by_seg_act[(an, sn)] / GB, by_seg_cnt[(an, sn)]))


if __name__ == '__main__':
    main()
