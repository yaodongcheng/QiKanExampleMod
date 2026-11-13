# -*- coding: utf-8 -*-
"""截断坏 LOD：对细碎模型只保留前 3 档（LOD0~LOD2），其余档丢弃。

判定（每个材质取 LOD0）：面数 / 连通块数 < 40  ⇒  视为细构件（马车轮、杆件、箱棱）
  这类模型的 LOD3+ 已被证实会散架，直接丢弃。

用法：
  python trim_lod.py <inroot> <outroot>
"""
import io
import os
import re
import sys
from collections import defaultdict

KEEP = 3
FRAG_RATIO = 40.0


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
        i += 1 + npos
        if i < len(lines) and lines[i].strip() == "0":
            i += 1
        nvert = int(lines[i].strip())
        i += 1 + nvert * 3
        if i < len(lines) and lines[i].strip() == "0":
            i += 1
        ntri = int(lines[i].strip())
        i += 1 + ntri
        parts.append((mat, start, i, npos, ntri))
    return parts


def components(npos, pos_lines, tri_block):
    parent = list(range(npos))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[ra] = rb

    for ln in tri_block:
        t = ln.split()
        if len(t) >= 3:
            try:
                a, b, c = int(t[0]), int(t[1]), int(t[2])
                union(a, b); union(b, c)
            except ValueError:
                pass
    return len({find(x) for x in range(npos)})


def main():
    inroot, outroot = sys.argv[1], sys.argv[2]
    tot = trimmed = 0
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
            if not parts:
                io.open(dst, "w", encoding="utf-8", newline="\n").write(txt)
                continue
            # 每材质 LOD0 的碎片化程度
            seen = defaultdict(int)
            frag = []
            for mat, s, e, npos, ntri in parts:
                if seen[mat] > 0:
                    seen[mat] += 1
                    continue
                seen[mat] += 1
                # 三角形块在部件末尾 ntri 行
                tri_lines = lines[e - ntri:e]
                ncomp = components(npos, None, tri_lines)
                if ncomp > 0:
                    frag.append(ntri / float(ncomp))
            thin = bool(frag) and (sum(frag) / len(frag)) < FRAG_RATIO

            if thin:
                out = ["rfver 4"]
                keep_parts = []
                seen2 = defaultdict(int)
                for mat, s, e, npos, ntri in parts:
                    if seen2[mat] < KEEP:
                        keep_parts.append((mat, s, e))
                    seen2[mat] += 1
                out.append("mesh %d" % len(keep_parts))
                for mat, s, e in keep_parts:
                    out.extend(lines[s:e])
                out.append("end")
                io.open(dst, "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")
                trimmed += 1
            else:
                io.open(dst, "w", encoding="utf-8", newline="\n").write(txt)
            tot += 1
            s_in += os.path.getsize(src)
            s_out += os.path.getsize(dst)
    print("  文件 %d   截断 %d (%.1f%%)" % (tot, trimmed, 100.0 * trimmed / max(1, tot)))
    print("  %.2f GB → %.2f GB   比例 %.1f%%"
          % (s_in / 2 ** 30, s_out / 2 ** 30, 100.0 * s_out / max(1, s_in)))


if __name__ == "__main__":
    main()
