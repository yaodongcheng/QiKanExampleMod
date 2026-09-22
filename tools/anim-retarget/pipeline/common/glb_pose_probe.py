#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""GLB 姿势探针：直接读 GLB 的节点层级 + 关键帧，算指定时刻「骨盆 / 头 / 脚」的世界坐标。

纯 python（不需要 Blender、不需要浏览器）—— 用来判定"某条动画在某时刻到底是什么姿势"，
以及两侧的**时长**（顺带查出帧率写错这类问题：30fps 的 61 帧 = 2.03s，写成 24fps 就变 2.54s）。

用法：
  python glb_pose_probe.py <a.glb> [<b.glb> ...] [--clip 名字] [--t 0.5]
"""
import array
import json
import math
import os
import struct
import sys

# Windows 控制台默认 GBK，中文/箭头会炸 —— 能改就改成 UTF-8
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass


def load_glb(path):
    b = open(path, "rb").read()
    ln = struct.unpack("<I", b[12:16])[0]
    j = json.loads(b[20:20 + ln].decode("utf-8"))
    off = 20 + ln
    blen = struct.unpack("<I", b[off:off + 4])[0]
    return j, b[off + 8:off + 8 + blen]


CT = {5126: ("f", 4), 5123: ("H", 2), 5125: ("I", 4), 5121: ("B", 1)}
NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def acc(j, bin_, i):
    a = j["accessors"][i]
    v = j["bufferViews"][a["bufferView"]]
    fmt, sz = CT[a["componentType"]]
    n = a["count"] * NC[a["type"]]
    o = v.get("byteOffset", 0) + a.get("byteOffset", 0)
    return array.array(fmt, bin_[o:o + n * sz]), a["count"], NC[a["type"]]


# ── 极简 4x4 矩阵（行主序 list[16]）──
def m_id():
    return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]


def m_mul(a, b):
    o = [0.0] * 16
    for r in range(4):
        for c in range(4):
            o[r * 4 + c] = sum(a[r * 4 + k] * b[k * 4 + c] for k in range(4))
    return o


def m_from_trs(t, q, s):
    x, y, z, w = q
    xx, yy, zz = x * x, y * y, z * z
    xy, xz, yz = x * y, x * z, y * z
    wx, wy, wz = w * x, w * y, w * z
    r = [
        (1 - 2 * (yy + zz)) * s[0], 2 * (xy - wz) * s[1], 2 * (xz + wy) * s[2], 0,
        2 * (xy + wz) * s[0], (1 - 2 * (xx + zz)) * s[1], 2 * (yz - wx) * s[2], 0,
        2 * (xz - wy) * s[0], 2 * (yz + wx) * s[1], (1 - 2 * (xx + yy)) * s[2], 0,
        t[0], t[1], t[2], 1,
    ]
    return r


def m_pos(m):
    return (m[12], m[13], m[14])


def q_slerp(a, b, u):
    d = sum(x * y for x, y in zip(a, b))
    if d < 0:
        b = [-x for x in b]
        d = -d
    if d > 0.9995:
        return [a[i] + u * (b[i] - a[i]) for i in range(4)]
    th = math.acos(max(-1.0, min(1.0, d)))
    s = math.sin(th)
    w1, w2 = math.sin((1 - u) * th) / s, math.sin(u * th) / s
    return [a[i] * w1 + b[i] * w2 for i in range(4)]


def sample(j, bin_, an, path, t):
    """把整条动画在时刻 t 采样成 {node: {'translation'/'rotation'/'scale': v}}。"""
    out = {}
    for ch in an["channels"]:
        if ch["target"]["path"] != path:
            continue
        ni = ch["target"].get("node")
        s = an["samplers"][ch["sampler"]]
        times, n, _ = acc(j, bin_, s["input"])
        vals, _, nc = acc(j, bin_, s["output"])
        if n == 0:
            continue
        if t <= times[0]:
            v = list(vals[0:nc])
        elif t >= times[n - 1]:
            v = list(vals[(n - 1) * nc:n * nc])
        else:
            k = 0
            while k < n - 1 and times[k + 1] < t:
                k += 1
            u = (t - times[k]) / max(1e-9, times[k + 1] - times[k])
            a = list(vals[k * nc:(k + 1) * nc])
            b = list(vals[(k + 1) * nc:(k + 2) * nc])
            if path == "rotation":
                v = q_slerp(a, b, u)
            else:
                v = [a[i] + u * (b[i] - a[i]) for i in range(nc)]
        out.setdefault(ni, {})[path] = v
    return out


def world_matrices(j, t, overrides):
    nodes = j["nodes"]
    parent = {}
    for i, nd in enumerate(nodes):
        for c in nd.get("children", []):
            parent[c] = i
    cache = {}

    def local(i):
        nd = nodes[i]
        # 🔴 glTF 的节点要么写 TRS、要么写 `matrix`（列主序）。Blender 导出器常把**静态节点**
        #    （含骨架根那一下 Y-up 转换）写成 matrix —— 漏掉它，整套骨架就还在 Z-up 里，
        #    "竖直"会比错轴（本探针第一版就是这么把"直立"读成"横躺"的）。
        if "matrix" in nd:
            cm = nd["matrix"]          # 列主序
            return [cm[0], cm[4], cm[8], cm[12],
                    cm[1], cm[5], cm[9], cm[13],
                    cm[2], cm[6], cm[10], cm[14],
                    cm[3], cm[7], cm[11], cm[15]]
        t_ = list(nd.get("translation", [0, 0, 0]))
        q_ = list(nd.get("rotation", [0, 0, 0, 1]))
        s_ = list(nd.get("scale", [1, 1, 1]))
        ov = overrides.get(i)
        if ov:
            t_ = ov.get("translation", t_)
            q_ = ov.get("rotation", q_)
            s_ = ov.get("scale", s_)
        return m_from_trs(t_, q_, s_)

    def world(i):
        if i in cache:
            return cache[i]
        m = local(i)
        p = parent.get(i)
        if p is not None:
            m = m_mul(world(p), m)
        cache[i] = m
        return m

    return world, nodes


def report(path, clip_filter, t_req):
    j, bin_ = load_glb(path)
    nodes = j["nodes"]
    name_of = {i: n.get("name", "#%d" % i) for i, n in enumerate(nodes)}
    print("\n%s" % os.path.basename(path))
    for an in j.get("animations", []):
        if clip_filter and an["name"] != clip_filter:
            continue
        dur = 0.0
        for ch in an["channels"]:
            times, n, _ = acc(j, bin_, an["samplers"][ch["sampler"]]["input"])
            if n:
                dur = max(dur, times[n - 1])
        t = min(t_req, dur)
        ov = sample(j, bin_, an, "translation", t)
        for k, v in sample(j, bin_, an, "rotation", t).items():
            ov.setdefault(k, {}).update(v)
        for k, v in sample(j, bin_, an, "scale", t).items():
            ov.setdefault(k, {}).update(v)
        world, _ = world_matrices(j, t, ov)
        pos = {}
        for i, n in enumerate(nodes):
            nm = n.get("name", "")
            if nm in ("pelvis", "head", "l_foot", "r_foot", "spine2"):
                pos[nm] = m_pos(world(i))
        pv, hd = pos.get("pelvis"), pos.get("head")
        if pv and hd:
            d = [hd[k] - pv[k] for k in range(3)]
            L = math.sqrt(sum(x * x for x in d))
            axis = [x / L for x in d] if L > 1e-9 else [0, 0, 0]
            up = axis[1]  # glTF 是 Y-up：+Y = 竖直
            kind = "直立" if abs(up) > 0.75 else ("趴/横躺" if abs(up) < 0.4 else "半躺")
            feet = [pos[k] for k in ("l_foot", "r_foot") if k in pos]
            fz = min(f[1] for f in feet) if feet else float("nan")
            print("  %-34s 时长 %5.2fs（%4.0f 帧@30）  骨盆(%.2f,%.2f,%.2f) 头(%.2f,%.2f,%.2f)"
                  % (an["name"], dur, dur * 30, pv[0], pv[1], pv[2], hd[0], hd[1], hd[2]))
            print("      身体轴(骨盆→头) 竖直分量 %+.2f  ⇒ %s   最低脚 y=%.2f（t=%.2f）"
                  % (up, kind, fz, t))


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    clip = None
    t_req = 0.5
    for a in sys.argv[1:]:
        if a.startswith("--clip"): clip = a.split("=", 1)[1] if "=" in a else None
        if a.startswith("--t"): t_req = float(a.split("=", 1)[1]) if "=" in a else 0.5
    if not args:
        print(__doc__); sys.exit(1)
    for p in args:
        report(p, clip, t_req)
