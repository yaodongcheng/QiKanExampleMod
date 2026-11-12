# -*- coding: utf-8 -*-
"""删除 tpac 里指定类型的段并重排数据区（试作：剥 MeshEditData / EditmodeMiscData）。

  python tpac_strip.py <src.tpac> <dst.tpac> [MED,MISC]

原理：索引里每个段记录 (offset, actualSize, storageSize, ownerGuid, typeGuid, 13B)；
数据区段与段紧密排列。删段后按原顺序复制保留段的字节，重算 offset 与 dataOffset。
不动压缩格式、不重算 actualSize。
"""
import os
import struct
import sys

SEG = {"bb1df897-584f-4770-abf2-663fe449f247": "VS",
       "3d41985f-24d2-4fc1-82e4-6a6e0da3f4e2": "MED",
       "644030f6-8a42-4c86-b935-9b9daa9391c2": "MISC",
       "70ee4e2c-79e4-4b2d-8d54-d53ecd2a559c": "TEXPIX"}
DROP_DEFAULT = {"MED", "MISC"}


def g(b):
    d1 = int.from_bytes(b[0:4], 'little'); d2 = int.from_bytes(b[4:6], 'little')
    d3 = int.from_bytes(b[6:8], 'little'); d4 = b[8:16].hex()
    return "%08x-%04x-%04x-%s-%s" % (d1, d2, d3, d4[0:4], d4[4:16])


def strip(src, dst, drop):
    with open(src, 'rb') as f:
        buf = f.read()

    o = 0
    magic, ver = struct.unpack_from('<II', buf, o); o += 8
    assert magic == 0x43415054, "not a tpac"
    hdr_pkg = buf[o:o + 16]; o += 16
    nres, data_off, reserve = struct.unpack_from('<III', buf, o); o += 12
    hdr_end = o

    resources = []
    for _ in range(nres):
        rstart = o
        atype = g(buf[o:o + 16]); o += 16
        aguid = g(buf[o:o + 16]); o += 16
        if ver > 1:
            o += 4
        nlen = struct.unpack_from('<i', buf, o)[0]; o += 4
        o += nlen
        meta = struct.unpack_from('<Q', buf, o)[0]; o += 8
        o += meta + 8                          # metadata + checksum
        prefix_end = o                         # 到 nseg 字段为止
        nseg = struct.unpack_from('<i', buf, o)[0]; o += 4
        segs = []
        for _ in range(nseg):
            so, act, sto = struct.unpack_from('<QQQ', buf, o)
            rec = buf[o:o + 69]
            stype = g(buf[o + 40:o + 56])
            o += 69
            segs.append(dict(kind=SEG.get(stype, stype[:8]), rec=rec,
                             off=so, actual=act, storage=sto))
        ndep = struct.unpack_from('<i', buf, o)[0]; o += 4
        deps = buf[o:o + ndep * 48]; o += ndep * 48
        resources.append(dict(atype=atype, aguid=aguid, name_start=rstart,
                              prefix=buf[rstart:prefix_end], segs=segs, deps=deps, ndep=ndep))

    # 真正的数据起点 = 最小段偏移（也等于索引解析结束位置）；头部那个 data 字段只是
    # "索引结束 - 36" 的历史约定，三个原始包都这么写，这里照样沿用。
    data_start = min((s["off"] for r in resources for s in r["segs"]), default=o)
    assert data_start == o, "index end %d != first segment offset %d" % (o, data_start)
    data_prefix = b""

    # 组装新数据区
    new_data = bytearray(data_prefix)
    base = 0                      # 段偏移是绝对文件偏移，稍后统一回填
    kept = dropped = 0
    drop_bytes = 0
    for r in resources:
        r["new_segs"] = []
        for s in r["segs"]:
            if s["kind"] in drop:
                dropped += 1
                drop_bytes += s["storage"]
                continue
            s["payload"] = buf[s["off"]:s["off"] + s["storage"]]
            r["new_segs"].append(s)
            kept += 1

    # 组装新索引
    idx = bytearray()
    for r in resources:
        idx += r["prefix"]
        idx += struct.pack('<i', len(r["new_segs"]))
        for s in r["new_segs"]:
            rec = bytearray(s["rec"])
            struct.pack_into('<Q', rec, 0, 0)     # 偏移稍后回填
            s["rec_new_pos"] = (len(idx), s)
            idx += bytes(rec)
        idx += struct.pack('<i', r["ndep"]) + r["deps"]

    new_index_end = hdr_end + len(idx)
    # 回填绝对偏移，并把段数据紧跟索引排布
    cursor = new_index_end
    data = bytearray()
    for r in resources:
        for s in r["new_segs"]:
            struct.pack_into('<Q', idx, s["rec_new_pos"][0], cursor)
            data += s["payload"]
            cursor += len(s["payload"])

    out = bytearray()
    out += struct.pack('<II', 0x43415054, ver)
    out += hdr_pkg
    # 该字段沿用"索引结束 - 36"的约定（三个原始包都如此）
    out += struct.pack('<III', nres, new_index_end - 36, reserve)
    out += idx
    assert len(out) == new_index_end, "%d != %d" % (len(out), new_index_end)
    out += data

    os.makedirs(os.path.dirname(dst) or ".", exist_ok=True)
    with open(dst, 'wb') as f:
        f.write(out)

    print("src %-46s %9.3f MB" % (os.path.basename(src), len(buf) / 1024 ** 2))
    print("dst %-46s %9.3f MB" % (os.path.basename(dst), len(out) / 1024 ** 2))
    print("  资源 %d，段 %d -> %d（删除 %d 个，%.3f MB 数据）"
          % (nres, kept + dropped, kept, dropped, drop_bytes / 1024 ** 2))
    print("  索引 %d -> %d 字节；dataOffset 字段 %d -> %d"
          % (data_start - hdr_end, len(idx), data_off, new_index_end - 36))
    return len(buf), len(out)


if __name__ == "__main__":
    src = sys.argv[1]
    dst = sys.argv[2]
    drop = set(sys.argv[3].split(",")) if len(sys.argv) > 3 else DROP_DEFAULT
    print("drop = %s" % sorted(drop))
    a, b = strip(src, dst, drop)
    print("  => 省 %.3f MB (%.1f%%)" % ((a - b) / 1024 ** 2, 100.0 * (a - b) / a))
