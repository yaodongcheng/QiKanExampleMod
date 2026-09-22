#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""TRF 定格化：把一条 TRF 的**每一帧都换成第 0 帧**（长度/帧号不变）。

用途：查看器里做「姿态 vs 动画」对照 —— 姿态格要的是**静态一帧**，但若真只留 1 帧，
查看器会按"时长比"去拉伸另一侧（两格时长不一样就会错位）。定格成同样的长度最省事，
观感就是"这一格不动"。

用法
    python trf_hold.py --indir output/trf --outdir output/trf_hold \
        --pairs "fly_A_Flight_HoverMove_A=fly_hold_base_hover,fly_A_Flight_HoverMove_A_LeanR=fly_hold_leanR"
"""
import io
import os
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass


def _args():
    a = sys.argv[1:]
    out, i = {}, 0
    while i < len(a):
        if a[i].startswith("--"):
            out[a[i][2:]] = a[i + 1] if i + 1 < len(a) else ""
            i += 2
        else:
            i += 1
    return out


def read_trf(path):
    with io.open(path, encoding="utf-8") as f:
        L = [ln.rstrip("\r\n") for ln in f]
    assert L[0].startswith("rfver"), "不是 TRF: %s" % L[0]
    name = L[2].split()[0]
    nb = int(L[3])
    i = 4
    rots = []
    for _ in range(nb):
        n = int(L[i]); i += 1
        fr = []
        for _k in range(n):
            p = L[i].split(); i += 1
            fr.append((int(p[0]), p[1:5]))
        rots.append(fr)
    np_ = int(L[i]); i += 1
    pos = []
    for _k in range(np_):
        p = L[i].split(); i += 1
        pos.append((int(p[0]), p[1:4]))
    return name, rots, pos


def write_trf(name, rots, pos, path):
    out = ["rfver 4", "skeleton_anim 1", "%s 1" % name, " %d" % len(rots)]
    for bone in rots:
        out.append(" %d" % len(bone))
        for (f, v) in bone:
            out.append(" %d %s" % (f, " ".join(v)))
    out.append(" %d" % len(pos))
    for (f, v) in pos:
        out.append(" %d %s" % (f, " ".join(v)))
    out.append("end")
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(out))


A = _args()
INDIR = A.get("indir", ".")
OUTDIR = A.get("outdir", ".")
PAIRS = [p.strip() for p in (A.get("pairs") or "").split(",") if p.strip()]
LIKE = {}
for pair in (A.get("like") or "").split(","):
    if "=" in pair:
        k, v = pair.split("=", 1)
        LIKE[k.strip()] = v.strip()
if not PAIRS:
    print(__doc__)
    sys.exit(1)

for pair in PAIRS:
    src, dst = (pair.split("=", 1) + [None])[:2] if "=" in pair else (pair, pair)
    src = src.strip()
    dst = (dst or src).strip()
    sp = os.path.join(INDIR, src + ".trf")
    if not os.path.isfile(sp):
        print("  跳过（没有 %s）" % sp)
        continue
    _n, rots, pos = read_trf(sp)
    # 帧号取自 --like 指定的参考动画（缺省=自己）——这样"定格格"和"动画格"长度一致
    ref_name = LIKE.get(dst, dst)
    rp = os.path.join(INDIR, ref_name + ".trf")
    if os.path.isfile(rp):
        _rn, rrots, rpos = read_trf(rp)
        frames = [f for (f, _v) in rrots[0]]
        pframes = [f for (f, _v) in rpos] or frames
    else:
        frames = [f for (f, _v) in rots[0]]
        pframes = [f for (f, _v) in pos]
    held = [[(f, bone[0][1]) for f in frames] for bone in rots]
    p0 = pos[0][1]
    held_pos = [(f, p0) for f in pframes]
    write_trf(dst, held, held_pos, os.path.join(OUTDIR, dst + ".trf"))
    print("  %-40s → %-34s %d 帧（参考 %s）"
          % (src, dst, len(frames), ref_name if ref_name != dst else "自己"))
print("DONE")
