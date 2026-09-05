# -*- coding: utf-8 -*-
"""
flora.bin 结构探针（第 1 步：只读解析，改文件名/版本兼容问题）

背景：骑砍2 大地图植被 = SceneObj/<map>/flora.bin（"FLR2" 魔数）。
已观察：u32@4 = 剩余字节数；u32@8 = 记录数；每条记录 = [u32 名字长][名字][payload?]。
本脚本：暴力搜 payload 定长，验证记录流精确覆盖文件尾，然后 dump 首条 payload 语义。

用法:
    python flora_probe.py
"""
import struct
import re
from collections import Counter

PATHS = [
    r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\SandBox\SceneObj\Main_map\flora.bin",
    r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\Shokuho\SceneObj\Main_map\flora.bin",
]

NAME_RE = re.compile(rb"^[a-z_0-9]+$")


def probe(path):
    d = open(path, "rb").read()
    print("=" * 70)
    print("FILE:", path)
    print("size =", len(d), "magic =", d[:4])
    assert d[:4] == b"FLR2"

    rest = struct.unpack("<I", d[4:8])[0]
    n = struct.unpack("<I", d[8:12])[0]
    print("u32@4 = %d  (len(d)-8 = %d)" % (rest, len(d) - 8))
    print("u32@8 = %d  (记录数?)" % n)

    # ---------- 假说搜索: 记录 = u32 len + name + payload(定长) ----------
    hits = []
    for payload in range(50, 140):
        off = 12
        ok = True
        for i in range(n):
            if off + 4 > len(d):
                ok = False
                break
            ln = struct.unpack("<I", d[off:off + 4])[0]
            if not (1 <= ln <= 64):
                ok = False
                break
            name = d[off + 4:off + 4 + ln]
            if not NAME_RE.match(name):
                ok = False
                break
            off += 4 + ln + payload
        if ok and off == len(d):
            hits.append(payload)
        elif ok and off < len(d) and len(d) - off < 64 and hits:
            pass
    print("payload 定长命中: %s" % hits)
    if not hits:
        print("!! 定长假说不中，尝试不带对齐变体/payload 变化…")
        return None

    payload = hits[0]
    # ---------- 全量验证 + 统计 ----------
    off = 12
    names = []
    for i in range(n):
        ln = struct.unpack("<I", d[off:off + 4])[0]
        name = d[off + 4:off + 4 + ln]
        names.append(name.decode())
        off += 4 + ln + payload
    assert off == len(d)
    print("全量验证通过: N=%d, 记录步长=%d" % (n, 4 + payload))
    print("树种分布 (top): %s" % Counter(names).most_common(15))

    # ---------- 首条记录 payload dump ----------
    off = 12
    ln = struct.unpack("<I", d[off:off + 4])[0]
    name = d[off + 4:off + 4 + ln]
    payload_ofs = off + 4 + ln
    pd = d[payload_ofs:payload_ofs + payload]
    print("-" * 70)
    print("首条记录: len=%d name=%s payload_ofs=%d payload_len=%d" % (ln, name, payload_ofs, payload))
    print("payload hex:")
    for i in range(0, len(pd), 16):
        chunk = pd[i:i + 16]
        hexs = " ".join("%02X" % b for b in chunk)
        ascii_ = "".join(chr(b) if 32 <= b < 127 else "." for b in chunk)
        print("  %04X: %-48s %s" % (i, hexs, ascii_))
    print("payload float32 (little):")
    nf = len(pd) // 4
    for i in range(nf):
        v = struct.unpack("<f", pd[i * 4:i * 4 + 4])[0]
        print("  f[%2d] @%2d = %+9.5f" % (i, i * 4, v))
    return dict(path=path, n=n, payload=payload, names=Counter(names))


if __name__ == "__main__":
    for p in PATHS:
        try:
            r = probe(p)
        except Exception as e:
            print("!! %s 解析异常: %s" % (p, e))
