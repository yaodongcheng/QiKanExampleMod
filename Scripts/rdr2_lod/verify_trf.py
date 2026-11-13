# -*- coding: utf-8 -*-
"""交付前验收：TRF 的 LOD 命名 + UV 是否正确

用法： python verify_trf.py <新.trf> <源.trf> [游戏内验过的对照.trf]
输出： 屏幕报告 + 同目录 <名字>.check.txt
"""
import io
import os
import re
import sys

RE = re.compile(r"^(\S+?)(?:\.lod(\d+))?\.(\d+)$")


def parse(path):
    """返回 parts: dict(lvl, idx, mat, base, P, pidx, uv, tri, hdr)"""
    L = io.open(path, encoding="utf-8", errors="replace").read().split("\n")
    if not L[0].strip().startswith("rfver"):
        raise SystemExit("首行不是 rfver: %r" % L[0][:40])
    parts, i = [], 2
    while i < len(L) and L[i].strip() and L[i].strip() != "end":
        hdr = L[i].strip()
        head, mat = hdr.split(" 0 ", 1)
        m = RE.match(head)
        if not m:
            raise SystemExit("部件名不合法: %r" % hdr[:60])
        base, lv, idx = m.group(1), int(m.group(2) or 0), int(m.group(3))
        i += 1
        npos = int(L[i].strip()); i += 1
        P = [tuple(map(float, L[i + k].split()[:3])) for k in range(npos)]; i += npos
        if L[i].strip() == "0":
            i += 1
        nv = int(L[i].strip()); i += 1
        pidx, uv = [], []
        for k in range(nv):
            pidx.append(int(L[i + k * 3].split()[0]))
            t = L[i + k * 3 + 1].split()
            uv.append((float(t[0]), float(t[1])))
        i += nv * 3
        if L[i].strip() == "0":
            i += 1
        nt = int(L[i].strip()); i += 1
        tri = [tuple(map(int, L[i + k].split()[:3])) for k in range(nt)]; i += nt
        parts.append(dict(lvl=lv, idx=idx, mat=mat, base=base, P=P, pidx=pidx, uv=uv, tri=tri, hdr=hdr))
    return parts


def main():
    new_p, src_p = sys.argv[1], sys.argv[2]
    ref_p = sys.argv[3] if len(sys.argv) > 3 and os.path.exists(sys.argv[3]) else None
    out = []

    def say(s=""):
        print(s)
        out.append(s)

    A = parse(new_p)
    S = parse(src_p)
    base = os.path.basename(new_p)[:-4]

    say("=" * 74)
    say("TRF 验收：%s.trf" % base)
    say("=" * 74)

    # ---------- 1. 命名 ----------
    say("\n【1】LOD 命名")
    lv_count = {}
    for p in A:
        lv_count[p["lvl"]] = lv_count.get(p["lvl"], 0) + 1
    levels = sorted(lv_count)
    say("  档位 = %s   每档部件数 = %s" % (levels, [lv_count[l] for l in levels]))
    bad = [p for p in A if p["base"] != base]
    say("  资产名一致性：%s" % ("PASS（全部 = %s）" % base if not bad else "FAIL %d 个" % len(bad)))
    bad2 = [p for p in A if (p["lvl"] == 0) != (".lod" not in p["hdr"].split(" 0 ")[0])]
    say("  LOD0 无 .lodN. / LOD≥1 必有 .lodN.：%s" % ("PASS" if not bad2 else "FAIL %d 个" % len(bad2)))
    dup = len(A) != len({(p["lvl"], p["idx"]) for p in A})
    say("  同档序号唯一：%s" % ("FAIL" if dup else "PASS"))
    say("  `_lod` 后缀检查：%s" % ("PASS（引擎不会剥掉名字）" if "_lod" not in base else "FAIL 名字里含 _lod"))
    say("\n  每档第一行原文（直接来自文件第 3 行起）：")
    for l in levels:
        first = next(p for p in A if p["lvl"] == l)
        say("    LOD%d  %s" % (l, first["hdr"]))
    say("\n  最后一档最后一行原文：")
    say("    %s" % A[-1]["hdr"])

    # ---------- 2. UV ----------
    say("\n【2】UV 与几何（按三角形顶点多重集比对，不受顶点顺序/去重影响）")

    def ms(parts):
        d = {}
        for p in parts:
            for (x, y, z) in p["tri"]:
                for i in (x, y, z):
                    pi = p["pidx"][i]
                    u, v = p["uv"][i]
                    q = (round(p["P"][pi][0], 4), round(p["P"][pi][1], 4), round(p["P"][pi][2], 4),
                         round(u, 5), round(v, 5))
                    d.setdefault(p["mat"], []).append(q)
        for k in d:
            d[k].sort()
        return d

    s0 = [p for p in S if p["lvl"] == 0]
    a0 = [p for p in A if p["lvl"] == 0]
    ms_s, ms_a = ms(s0), ms(a0)
    say("  材质数：源 %d / 新 %d" % (len(ms_s), len(ms_a)))
    allok = True
    nvert_s = sum(len(p["uv"]) for p in s0)
    nvert_a = sum(len(p["uv"]) for p in a0)
    say("  顶点数：源 %d / 新 %d（差 %d，生成器会合并重复顶点）" % (nvert_s, nvert_a, nvert_s - nvert_a))
    for mat in sorted(ms_s):
        src = ms_s[mat]
        dst = ms_a.get(mat, [])
        # 新的 V 应 = 1 - 源 V
        flipped = [(x, y, z, u, round(1.0 - v, 5)) for (x, y, z, u, v) in src]
        flipped.sort()
        if len(src) != len(dst):
            say("    %-44s 顶点数 %d vs %d  ⚠️" % (mat[:44], len(src), len(dst)))
            allok = False
            continue
        du = max((abs(x[0] - y[0]) + abs(x[1] - y[1]) + abs(x[2] - y[2]) for x, y in zip(flipped, dst)), default=0)
        duv = max((max(abs(x[3] - y[3]), abs(x[4] - y[4])) for x, y in zip(flipped, dst)), default=0)
        same = duv <= 3e-5 and du <= 1e-3
        say("    %-44s %d 顶点  位置最大偏差 %.1e  UV 最大偏差 %.1e  %s"
            % (mat[:44], len(src), du, duv, "一致 ✓" if same else "❌ 不一致"))
        allok = allok and same
    say("  ⇒ UV 结论：%s" % ("PASS（U 不变、V 恰好翻一次、位置不变）" if allok else "FAIL，见上表"))

    # ---------- 4. 结构 ----------
    say("\n【4】结构自检")
    say("  部件总数 mesh 头 = %s" % io.open(new_p, encoding="utf-8", errors="replace").read().split("\n")[1])
    say("  实际部件数 = %d" % len(A))
    tri = {l: sum(len(p["tri"]) for p in A if p["lvl"] == l) for l in levels}
    say("  每档面数 = %s" % tri)
    say("  三角索引范围合法：%s" % ("PASS" if all(0 <= i < len(p["uv"]) for p in A for t in p["tri"] for i in t) else "FAIL"))
    say("  每档面数严格递减：%s" % ("PASS" if all(tri[levels[k]] > tri[levels[k + 1]] for k in range(len(levels) - 1)) else "FAIL"))

    txt = "\n".join(out)
    dst = os.path.splitext(new_p)[0] + ".check.txt"
    io.open(dst, "w", encoding="utf-8", newline="\n").write(txt + "\n")
    print("\n[report] %s" % dst)


if __name__ == "__main__":
    main()
