# -*- coding: utf-8 -*-
"""细碎模型：保留 LOD0~2，末尾追加「定向包围盒」作为远景档。

判定：每材质 LOD0 的 面数/连通块数 < 40 ⇒ 细构件（杆件、车轮、箱棱）。
盒子：用 LOD0 顶点做 0.5° 步进的旋转卡尺，取面积最小的朝向，
      沿两轴内缩 2%（略小于实体，避免穿模），Z 取实际上下界。
其余模型原样复制。

用法：
  python proxy_box.py <inroot> <outroot>
"""
import io
import math
import os
import re
import sys
from collections import defaultdict

KEEP = 3
FRAG_RATIO = 40.0
INSET_F = 0.02


def parse_parts(lines):
    parts = []
    i = 2
    while i < len(lines):
        if not re.match(r"^\S+\.\d+ 0 \S+$", lines[i].strip()):
            i += 1
            continue
        start = i
        mat = lines[i].strip().split()[-1]
        i += 1
        try:
            npos = int(lines[i].strip())
        except ValueError:
            break
        i += 1
        pos = []
        for k in range(npos):
            t = lines[i + k].split()
            pos.append((float(t[0]), float(t[1]), float(t[2])))
        i += npos
        if i < len(lines) and lines[i].strip() == "0":
            i += 1
        nvert = int(lines[i].strip())
        i += 1 + nvert * 3
        if i < len(lines) and lines[i].strip() == "0":
            i += 1
        ntri = int(lines[i].strip())
        i += 1 + ntri
        parts.append((mat, start, i, npos, ntri, pos))
    return parts


def n_components(npos, tri_lines):
    parent = list(range(npos))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    for ln in tri_lines:
        t = ln.split()
        if len(t) >= 3:
            try:
                a, b, c = int(t[0]), int(t[1]), int(t[2])
            except ValueError:
                continue
            ra, rb, rc = find(a), find(b), find(c)
            if ra != rb:
                parent[ra] = rb
            ra, rc = find(a), find(c)
            if ra != rc:
                parent[ra] = rc
    return len({find(x) for x in range(npos)})


def obb(pos):
    """返回 8 个角点 + 朝向角度。"""
    xs = [p[0] for p in pos]; ys = [p[1] for p in pos]; zs = [p[2] for p in pos]
    zlo, zhi = min(zs), max(zs)
    best = None
    for deg10 in range(0, 1800, 5):
        th = math.radians(deg10 / 10.0)
        c, s = math.cos(th), math.sin(th)
        a = [x * c + y * s for x, y in zip(xs, ys)]
        b = [-x * s + y * c for x, y in zip(xs, ys)]
        area = (max(a) - min(a)) * (max(b) - min(b))
        if best is None or area < best[0]:
            best = (area, th, min(a), max(a), min(b), max(b))
    _, th, a0, a1, b0, b1 = best
    da = (a1 - a0) * INSET_F
    db = (b1 - b0) * INSET_F
    a0 += da; a1 -= da; b0 += db; b1 -= db
    c, s = math.cos(th), math.sin(th)
    pts = []
    for aa, bb, z in ((a0, b0, zlo), (a1, b0, zlo), (a1, b1, zlo), (a0, b1, zlo),
                      (a0, b0, zhi), (a1, b0, zhi), (a1, b1, zhi), (a0, b1, zhi)):
        pts.append((aa * c - bb * s, aa * s + bb * c, z))
    return pts


QUADS = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]


def box_part(mat, pts):
    tris = []
    for a, b, c, d in QUADS:
        tris.append((a, b, c))
        tris.append((a, c, d))
    out = ["%s 0 %s" % ("__PROXY__", mat)]
    out.append(str(len(pts)))
    out.extend(["%.6f %.6f %.6f" % p for p in pts])
    out.append("0")
    out.append(str(len(tris) * 3))
    for (a, b, c) in tris:
        for k in (a, b, c):
            out.append("%d 4294967295 0.000000 0.000000 1.000000" % k)
            out.append("0.050000 0.050000")
            out.append("0.000000 0.000000")
    out.append("0")
    out.append(str(len(tris)))
    out.extend(["%d %d %d" % ((i * 3), (i * 3 + 1), (i * 3 + 2)) for i in range(len(tris))])
    return out


def main():
    inroot, outroot = sys.argv[1], sys.argv[2]
    tot = proxied = 0
    s_in = s_out = 0
    for dirpath, _, names in os.walk(inroot):
        rel = os.path.relpath(dirpath, inroot)
        for n in names:
            if not n.lower().endswith(".trf"):
                continue
            src = os.path.join(dirpath, n)
            dst = os.path.join(outroot, rel, n)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            txt = io.open(src, encoding="utf-8", errors="replace").read()
            lines = txt.split("\n")
            parts = parse_parts(lines)
            base = os.path.basename(n)[:-4]
            if not parts:
                io.open(dst, "w", encoding="utf-8", newline="\n").write(txt)
                tot += 1
                s_in += os.path.getsize(src); s_out += os.path.getsize(dst)
                continue
            seen = defaultdict(int)
            frag = []
            big_mat = None
            big_ntri = -1
            for mat, s, e, npos, ntri, pos in parts:
                if seen[mat] == 0:
                    tri_lines = lines[e - ntri:e]
                    nc = n_components(npos, tri_lines)
                    if nc > 0:
                        frag.append(ntri / float(nc))
                    if ntri > big_ntri:
                        big_ntri = ntri
                        big_mat = mat
                        big_pos = pos
                seen[mat] += 1
            thin = bool(frag) and (sum(frag) / len(frag)) < FRAG_RATIO

            if thin:
                out = ["rfver 4"]
                keep = []
                seen2 = defaultdict(int)
                for mat, s, e, npos, ntri, pos in parts:
                    if seen2[mat] < KEEP:
                        keep.append((mat, s, e))
                    seen2[mat] += 1
                pts = obb(big_pos)
                bp = box_part(big_mat, pts)
                out.append("mesh %d" % (len(keep) + 1))
                for mat, s, e in keep:
                    out.extend(lines[s:e])
                out.extend(bp)
                out.append("end")
                body = "\n".join(out) + "\n"
                body = re.sub(r"(?m)^(\S+)\.\d+ 0 ", lambda m: "%s.%s 0 " % (base, "X"), body)
                io.open(dst, "w", encoding="utf-8", newline="\n").write(body)
                proxied += 1
            else:
                io.open(dst, "w", encoding="utf-8", newline="\n").write(txt)
            tot += 1
            s_in += os.path.getsize(src); s_out += os.path.getsize(dst)
    print("  文件 %d   加代理盒 %d (%.1f%%)" % (tot, proxied, 100.0 * proxied / max(1, tot)))
    print("  %.2f GB → %.2f GB   比例 %.1f%%"
          % (s_in / 2 ** 30, s_out / 2 ** 30, 100.0 * s_out / max(1, s_in)))


if __name__ == "__main__":
    main()
