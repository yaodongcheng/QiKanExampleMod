#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""check_trf_axis.py —— 交付前拦"朝向偏 90°"坏件（本项目的血泪判据）。

用法：
    python pipeline/tools/check_trf_axis.py  <候选.trf>  --ref <基准.trf>

判据（来自 2026-10-10 那次事故的实测）：
  · "整套动画摆错朝向" 只会改【根骨 #0】的旋转 → 其余骨应【逐字相同】。
  · 所以：
      1) 除 #0 外，任何骨的旋转/位移若与基准有差异 → 报警（不是单纯朝向问题）。
      2) 根骨 #0 的角差 = 整体朝向偏差（候选相对基准转了多少度）。
      3) 帧数不一致 → 也报警（同一条动画不该变帧数）。
"""
import sys, os, math

def read_trf(path):
    L = open(path, encoding='utf-8').read().splitlines()
    assert L[0].startswith("rfver"), "不是 TRF: " + path
    nb = int(L[3]); i = 4
    rots = []
    for _ in range(nb):
        n = int(L[i]); i += 1
        fr = []
        for _k in range(n):
            p = L[i].split(); i += 1
            fr.append((int(p[0]), (float(p[1]), float(p[2]), float(p[3]), float(p[4]))))
        rots.append(fr)
    npos = int(L[i]); i += 1
    pos = []
    for _k in range(npos):
        p = L[i].split(); i += 1
        pos.append((int(p[0]), (float(p[1]), float(p[2]), float(p[3]))))
    return rots, pos

def qang(a, b):
    """两四元数夹角(度)"""
    d = abs(sum(x * y for x, y in zip(a, b)))
    d = max(-1.0, min(1.0, d))
    return math.degrees(2.0 * math.acos(d))

def main():
    if len(sys.argv) < 4:
        print(__doc__); return 2
    cand = sys.argv[1]
    ref = sys.argv[sys.argv.index("--ref") + 1]
    cr, cp = read_trf(cand)
    rr, rp = read_trf(ref)
    print("候选: %s  (%d 骨, 根位移 %d 帧)" % (os.path.basename(cand), len(cr), len(cp)))
    print("基准: %s  (%d 骨, 根位移 %d 帧)" % (os.path.basename(ref), len(rr), len(rp)))
    bad = False
    if len(cr) != len(rr):
        print("  [X] 骨数不一致 %d vs %d" % (len(cr), len(rr))); return 1
    worst = (0.0, -1)
    for b in range(len(cr)):
        n = min(len(cr[b]), len(rr[b]))
        m = 0.0
        for k in range(n):
            m = max(m, qang(cr[b][k][1], rr[b][k][1]))
        if m > worst[0]: worst = (m, b)
        if b != 0 and m > 0.5:      # 除根骨外不应有差异
            print("  [X] 骨 #%d 与基准差 %.2f°（除根骨外应逐字相同）" % (b, m)); bad = True
    if len(cp) != len(rp):
        print("  [X] 根位移帧数不一致 %d vs %d" % (len(cp), len(rp))); bad = True
    root = max((qang(cr[0][k][1], rr[0][k][1]) for k in range(min(len(cr[0]), len(rr[0])))), default=0.0)
    print("  · 除根骨外最大差异 = %.2f°（应在 0.5° 内）" % (worst[0] if worst[1] != 0 else 0.0))
    print("  · 根骨(#0) 朝向偏差 = %.1f°  ← 这个数就是「整体转向了多少」" % root)
    if abs(root) > 45:
        print("  [X] 朝向偏差 > 45° —— 典型的「朝向偏 90°」坏件！"); bad = True
    else:
        print("  [OK] 朝向正常（±45° 内）")
    return 1 if bad else 0

if __name__ == "__main__":
    sys.exit(main())
