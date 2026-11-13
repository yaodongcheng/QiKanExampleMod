# -*- coding: utf-8 -*-
"""银行测试件 v2：平面合并+焊接 重建 LOD，并且按正确格式命名 <name>.lodN.<idx>。

LOD0 原样（仅 v 翻转）；L1~4 焊接 0.15/0.30/0.50/0.80 + 10/15/20/25°；
LOD5 = 全材质合并极简壳（2.0 m / 45°）。
"""
import io
import math
import os
import re
import sys

import bpy
import bmesh

MAP = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
       r"\Mount & Blade II Bannerlord\Modules\RDR2Map")
SRC = os.path.join(MAP, "AssetSources", "_meshes_orig", "bldg", "new_com_02_bank.trf")
OUTDIR = os.path.join(MAP, "AssetSources", "_manual_test")
NAME = "bank_uv_fix2"

SCHED = {1: (0.15, 10), 2: (0.30, 15), 3: (0.50, 20), 4: (0.80, 25)}
THIN = ("glass", "windows", "curtains")


def parse(path):
    L = io.open(path, encoding="utf-8", errors="replace").read().split("\n")
    parts = []
    i = 2
    while i < len(L):
        m = re.match(r"^(\S+?)(?:\.lod(\d+))?\.(\d+) 0 (\S+)$", L[i].strip())
        if not m:
            i += 1
            continue
        lvl = int(m.group(2) or 0)
        mat = m.group(4)
        i += 1
        npos = int(L[i].strip())
        i += 1
        P = [tuple(map(float, L[i + k].split()[:3])) for k in range(npos)]
        i += npos
        if L[i].strip() == "0":
            i += 1
        nvert = int(L[i].strip())
        i += 1
        pidx, uv = [], []
        for k in range(nvert):
            pidx.append(int(L[i + k * 3].split()[0]))
            u = L[i + k * 3 + 1].split()
            uv.append((float(u[0]), float(u[1])))
        i += nvert * 3
        if L[i].strip() == "0":
            i += 1
        ntri = int(L[i].strip())
        i += 1
        tri = [tuple(map(int, L[i + k].split()[:3])) for k in range(ntri)]
        i += ntri
        parts.append(dict(lvl=lvl, mat=mat, P=P, pidx=pidx, uv=uv, tri=tri))
    return parts


def rebuild(P, tris, uvs, weld, deg):
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in P]
    bm.verts.index_update()
    lay = bm.loops.layers.uv.new("UVMap")
    for ti, t in enumerate(tris):
        try:
            f = bm.faces.new([vs[t[0]], vs[t[1]], vs[t[2]]])
            for j in range(3):
                f.loops[j][lay].uv = uvs[ti][j]
        except (ValueError, IndexError):
            pass
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=weld)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(deg),
                             verts=bm.verts, edges=bm.edges)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bm.verts.index_update()
    P2 = [tuple(v.co) for v in bm.verts]
    lay2 = bm.loops.layers.uv.active
    tris2, uvs2 = [], []
    for f in bm.faces:
        tris2.append(tuple(l.vert.index for l in f.loops))
        uvs2.append([tuple(l[lay2].uv) for l in f.loops])
    bm.free()
    return P2, tris2, uvs2


def emit(out, mat, lvl, idx, P, tris, uvs):
    pname = NAME + (".lod%d" % lvl if lvl > 0 else "") + ".%d" % idx
    out.append("%s 0 %s" % (pname, mat))
    out.append(str(len(P)))
    out.extend(["%.6f %.6f %.6f" % p for p in P])
    out.append("0")
    vmap, order, ttri = {}, [], []
    for ti, t in enumerate(tris):
        idxs = []
        for j, pi in enumerate(t):
            u, v = uvs[ti][j]
            key = (pi, round(u, 6), round(1.0 - v, 6))
            if key not in vmap:
                vmap[key] = len(order)
                order.append(key)
            idxs.append(vmap[key])
        ttri.append(tuple(idxs))
    out.append(str(len(order)))
    for (pi, u, v0) in order:
        out.append("%d 4294967295 0.000000 0.000000 1.000000" % pi)
        out.append("%.6f %.6f" % (u, 1.0 - v0))
        out.append("0.000000 0.000000")
    out.append("0")
    out.append(str(len(ttri)))
    out.extend(["%d %d %d" % t for t in ttri])


def main():
    parts = parse(SRC)
    by, order = {}, []
    for p in parts:
        if p["mat"] not in by:
            by[p["mat"]] = {}
            order.append(p["mat"])
        by[p["mat"]][p["lvl"]] = p
    nlev = max(max(v) for v in by.values()) + 1
    print("源: %d 部件  %d 材质  %d 档" % (len(parts), len(order), nlev))

    out = ["rfver 4"]
    body = []
    merged = None
    for mat in order:
        lv = by[mat]
        base = lv[0]
        b_tris = [(base["pidx"][a], base["pidx"][b], base["pidx"][c]) for (a, b, c) in base["tri"]]
        b_uvs = [[base["uv"][a], base["uv"][b], base["uv"][c]] for (a, b, c) in base["tri"]]
        thin = any(k in mat for k in THIN)
        idx = 0
        for level in range(nlev):
            if level >= 5:
                continue
            if level == 0 or thin or level not in lv:
                P, tris, uvs = base["P"], b_tris, b_uvs
            else:
                w, d = SCHED[level]
                P, tris, uvs = rebuild(base["P"], b_tris, b_uvs, w, d)
                print("  %-30s L%d  %d → %d 面" % (mat[:30], level, len(b_tris), len(tris)))
            emit(body, mat, level, idx, P, tris, uvs)
            idx += 1
    # LOD5 合并壳
    flat_t, flat_u = [], []
    for mat in order:
        b = by[mat][0]
        for (a, bb, c) in b["tri"]:
            flat_t.append((b["pidx"][a], b["pidx"][bb], b["pidx"][c]))
            flat_u.append([b["uv"][a], b["uv"][bb], b["uv"][c]])
    P5, t5, u5 = rebuild([p for m in order for p in by[m][0]["P"]], flat_t, flat_u, 2.0, 45)
    # 合并壳用统一顶点索引：需要把每个 part 的 pidx 偏移
    # 上面直接传了拼接坐标，所以 t5 索引即全局索引，正确
    emit(body, order[0], 5, 0, P5, t5, u5)
    print("  LOD5 合并壳 %d 顶点 %d 面" % (len(P5), len(t5)))

    out.append("mesh %d" % sum(1 for ln in body if re.match(r"^\S+ 0 \S+$", ln)))
    out.extend(body)
    out.append("end")
    dst = os.path.join(OUTDIR, NAME + ".trf")
    os.makedirs(OUTDIR, exist_ok=True)
    io.open(dst, "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")
    print("[out] %s  %d B" % (dst, os.path.getsize(dst)))


if __name__ == "__main__":
    main()
