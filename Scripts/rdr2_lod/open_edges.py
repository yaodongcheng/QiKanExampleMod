# -*- coding: utf-8 -*-
"""统计 TRF 每档的开口边（只被 1 个三角形用到的边）

用法： python open_edges.py <源.trf> [新.trf]
结论：LOD0 开口边 = 0 → 该模型是"封口"的，LOD1+ 也必须封口；
      否则（栅栏/铁轨/招牌这类单面片）不强求。
"""
import io
import re
import sys
from collections import Counter

RE = re.compile(r"^(\S+?)(?:\.lod(\d+))?\.(\d+) 0 (\S+)$")


def parse(path):
    L = io.open(path, encoding="utf-8", errors="replace").read().split("\n")
    parts, i = [], 2
    while i < len(L) and L[i].strip() and L[i].strip() != "end":
        m = RE.match(L[i].strip())
        if not m:
            i += 1
            continue
        lvl = int(m.group(2) or 0)
        i += 1
        n = int(L[i].strip()); i += 1
        P = [tuple(map(float, L[i + k].split()[:3])) for k in range(n)]; i += n
        if L[i].strip() == "0":
            i += 1
        nv = int(L[i].strip()); i += 1
        pidx = [int(L[i + k * 3].split()[0]) for k in range(nv)]
        i += nv * 3
        if L[i].strip() == "0":
            i += 1
        nt = int(L[i].strip()); i += 1
        tri = [tuple(map(int, L[i + k].split()[:3])) for k in range(nt)]; i += nt
        parts.append((lvl, P, pidx, tri))
    return parts


def stat(parts, only_lod0=False):
    by = {}
    for (lvl, P, pidx, tri) in parts:
        if only_lod0 and lvl != 0:
            continue
        for (a, b, c) in tri:
            for (u, v) in ((a, b), (b, c), (c, a)):
                e = tuple(sorted((pidx[u], pidx[v])))
                by.setdefault(lvl, Counter())[e] += 1
    out = {}
    for lvl, cnt in by.items():
        out[lvl] = dict(
            edges=len(cnt),
            open_edges=sum(1 for e, c in cnt.items() if c == 1),
            nonman=sum(1 for e, c in cnt.items() if c > 2),
            tris=sum(cnt.values()) // 3,
        )
    return out


def main():
    src = sys.argv[1]
    new = sys.argv[2] if len(sys.argv) > 2 else None
    s = stat(parse(src), only_lod0=True)
    l0 = s.get(0, {})
    closed0 = l0.get("open_edges", 0) == 0
    print("源件 LOD0：边 %d，开口边 %d，非流形边 %d  -> %s"
          % (l0.get("edges", 0), l0.get("open_edges", 0), l0.get("nonman", 0),
             "封口（LOD1+ 必须也封口）" if closed0 else "未封口（不强制）"))
    if not new:
        return
    n = stat(parse(new))
    print("")
    print("%-6s %8s %10s %10s  %s" % ("档", "三角", "边", "开口边", "判定"))
    ok = True
    for lvl in sorted(n):
        d = n[lvl]
        verdict = "封闭" if d["open_edges"] == 0 else "未封口"
        must = closed0 and lvl > 0
        if must and d["open_edges"] > 0:
            verdict += "  << 违反原则"
            ok = False
        print("%-6s %8d %10d %10d  %s" % ("LOD%d" % lvl, d["tris"], d["edges"], d["open_edges"], verdict))
    print("")
    print("结论：%s" % ("PASS" if ok else "FAIL（LOD0 封口，但上面有档没封口）"))


if __name__ == "__main__":
    main()
