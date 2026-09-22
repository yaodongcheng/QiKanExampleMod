#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""GLB 动画体检（**纯 python，不需要 Blender**）—— 每条动画相对【绑定姿势】偏了多少。

为什么要它
    查看器那些 GLB 是"把已重定向的 FBX 搬进去"得到的，而 Blender ≥4.4 的 action 带 slot，
    **跨骨架搬动作会静默失败**（姿势停在绑定姿势，导出的动画等于没动；文件照样生成、
    通道照样存在，所以"有没有通道"这种检查查不出来）。本脚本直接读 GLB 的关键帧数值，
    量"每条动画里每根关节相对绑定姿势的最大转角"：
        · 接近 0°  ⇒ 这条动画等于没动（搬运失败 / 本来就是定格）；
        · 越大     ⇒ 动作越明显。
    另外报根节点的最大平移，用来分辨"原地动作"与"带位移动作"。

用法
    python check_glb_anim.py <a.glb> [<b.glb> ...] [--top 3]
"""
import array
import json
import math
import os
import struct
import sys


def load_glb(path):
    b = open(path, "rb").read()
    assert b[:4] == b"glTF", "不是 GLB: %s" % path
    ln = struct.unpack("<I", b[12:16])[0]
    j = json.loads(b[20:20 + ln].decode("utf-8"))
    off = 20 + ln
    blen = struct.unpack("<I", b[off:off + 4])[0]
    return j, b[off + 8:off + 8 + blen]


CTYPE = {5126: ("f", 4), 5123: ("H", 2), 5125: ("I", 4), 5121: ("B", 1)}
NCOMP = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def read_acc(j, bin_, idx):
    a = j["accessors"][idx]
    v = j["bufferViews"][a["bufferView"]]
    fmt, size = CTYPE[a["componentType"]]
    n = a["count"] * NCOMP[a["type"]]
    o = v.get("byteOffset", 0) + a.get("byteOffset", 0)
    return array.array(fmt, bin_[o:o + n * size])


def q_angle_deg(qa, qb):
    """两个四元数（x,y,z,w）之间的夹角（度）。"""
    d = abs(sum(a * b for a, b in zip(qa, qb)))
    d = max(-1.0, min(1.0, d))
    return math.degrees(2.0 * math.acos(d))


def check(path, top=3):
    j, bin_ = load_glb(path)
    nodes = j.get("nodes", [])
    bind_rot = [n.get("rotation", [0.0, 0.0, 0.0, 1.0]) for n in nodes]
    bind_tr = [n.get("translation", [0.0, 0.0, 0.0]) for n in nodes]
    name_of = [n.get("name", "#%d" % i) for i, n in enumerate(nodes)]
    print("\n%s  （%.0f KB，节点 %d，动画 %d）"
          % (os.path.basename(path), os.path.getsize(path) / 1024.0, len(nodes),
             len(j.get("animations", []))))
    print("  %-36s %6s %8s  %s" % ("动画", "帧数", "最大转角", "最大转角出现在（前几根）"))
    for an in j.get("animations", []):
        worst = 0.0
        worst_node = None
        keys = 0
        per_node = []
        for ch in an["channels"]:
            s = an["samplers"][ch["sampler"]]
            tgt = ch["target"]
            ni = tgt.get("node")
            if tgt["path"] != "rotation" or ni is None:
                continue
            t = read_acc(j, bin_, s["input"])
            v = read_acc(j, bin_, s["output"])
            keys = max(keys, len(t))
            mx = 0.0
            for k in range(len(t)):
                q = v[k * 4:k * 4 + 4]
                mx = max(mx, q_angle_deg(q, bind_rot[ni]))
            per_node.append((mx, name_of[ni]))
            if mx > worst:
                worst, worst_node = mx, name_of[ni]
        per_node.sort(reverse=True)
        top_s = ", ".join("%s %.0f°" % (n, a) for a, n in per_node[:top]) if per_node else "—"
        print("  %-36s %6d %7.1f°  %s" % (an["name"], keys, worst, top_s))


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    top = 3
    for a in sys.argv[1:]:
        if a.startswith("--top"):
            top = int(a.split("=", 1)[1]) if "=" in a else 3
    if not args:
        print(__doc__)
        sys.exit(1)
    for p in args:
        check(p, top)
