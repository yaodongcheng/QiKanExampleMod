# -*- coding: utf-8 -*-
"""
terrain.bin 段结构探针（只读解析）
格式观察：魔数 "ZGR6RTRN" + u32 version=2 + 分段 [4B 名字][payload...]
已知段名: MIDX(索引) HGHT(高度, PNG流) NRML(法线) WGHT(权重/材质splat, PNG流) PHYM(物理材质)
用法: python terrain_probe.py <terrain.bin路径>
"""
import struct
import sys
import io

CHUNK_NAMES = (b"MIDX", b"HGHT", b"NRML", b"WGHT", b"PHYM")


def parse_png_header(data):
    """给 PNG 字节流返回 (w,h,bit_depth,color_type)"""
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    w, h = struct.unpack(">II", data[16:24])
    bit_depth = data[24]
    color_type = data[25]
    return w, h, bit_depth, color_type


def probe(path):
    d = open(path, "rb").read()
    print("=" * 70)
    print("FILE:", path)
    print("size =", len(d))
    assert d[:8] == b"ZGR6RTRN", "magic mismatch: %r" % d[:8]
    ver = struct.unpack("<I", d[8:12])[0]
    print("magic ZGR6RTRN ok, version =", ver)

    # 找出各段名出现位置
    pos = {}
    for name in CHUNK_NAMES:
        idx = d.find(name)
        while idx != -1:
            # 名字后面通常有长度或数据; 记录第一次出现
            if name not in pos:
                pos[name] = idx
            idx = d.find(name, idx + 1)
    print("段名首次出现位置: ", {k.decode(): v for k, v in sorted(pos.items(), key=lambda kv: kv[1])})

    # 每个段: 名(4B) 后 u32 长度? 验证: name 后 4B 读长度, 若 name+4+len 落在下一段名或文件尾则命中
    keys = sorted(pos.items(), key=lambda kv: kv[1])
    for i, (name, off) in enumerate(keys):
        end = keys[i + 1][1] if i + 1 < len(keys) else len(d)
        after = d[off + 4:off + 8]
        ln = struct.unpack("<I", after)[0]
        print("-" * 60)
        print("chunk %s @%d: u32-after-name = %d (到下一段距离 = %d)" % (name.decode(), off, ln, end - off))
        # 数据从 off+8 或 off+4 开始试 PNG
        for hdr_shift in (4, 8):
            cand = d[off + hdr_shift:off + hdr_shift + 40]
            p = parse_png_header(cand)
            if p:
                print("   [PNG 命中] 偏移 +%d 起, 尺寸 %dx%d, bit_depth=%d color_type=%d" % (hdr_shift, p[0], p[1], p[2], p[3]))
        # if chunk 数据是 PNG 分解: 打印 IHDR 前后的 hex 上下文
        ctx = d[off:off + 64]
        print("   hex:", " ".join("%02X" % b for b in ctx))


if __name__ == "__main__":
    paths = sys.argv[1:] or [r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\SandBox\SceneObj\Main_map\terrain.bin"]
    for p in paths:
        try:
            probe(p)
        except Exception as e:
            import traceback
            traceback.print_exc()
