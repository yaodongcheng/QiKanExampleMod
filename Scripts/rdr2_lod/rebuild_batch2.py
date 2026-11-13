# -*- coding: utf-8 -*-
"""批量重建 TRF 的 LOD（v10 逻辑），输出到新目录并统计体积。

用法：
  blender -b --factory-startup -P rebuild_batch.py -- <inroot> <outroot> <logcsv> [limit]

规则：
  · LOD0 原样不动
  · L1~4：焊接距离 = 对角线 × {0.0025,0.005,0.013,0.020}，角度 {10,15,25,30}
    薄片材质（glass/windows/curtains/signs）不重建，沿用 LOD0 几何
  · 超出公共档数的最后一档：全材质合并成单网格，焊接 = 对角线×0.033，角度 45
  · 写盘共享顶点（同 (位置,UV) 只写一条）
"""
import io
import math
import os
import re
import sys
import time

import bpy
import bmesh
import numpy as np

WELD_F = {1: 0.0025, 2: 0.005, 3: 0.013, 4: 0.020}
WELD_CAP = 0.60          # 绝对上限（米）
THIN_CLASS = ("rail", "struct", "rock")
DEG = {1: 10, 2: 15, 3: 25, 4: 30, 5: 45}
THIN = ("glass", "windows", "curtains")


def parse(path):
    L = io.open(path, encoding="utf-8", errors="replace").read().split("\n")
    parts = []
    i = 2
    while i < len(L):
        m = re.match(r"^(\S+)\.(\d+) 0 (\S+)$", L[i].strip())
        if not m:
            i += 1
            continue
        mat = m.group(3)
        i += 1
        npos = int(L[i].strip())
        i += 1
        P = [tuple(map(float, L[i + k].split()[:3])) for k in range(npos)]
        i += npos
        if i < len(L) and L[i].strip() == "0":
            i += 1
        nvert = int(L[i].strip())
        i += 1
        pidx, uv = [], []
        for k in range(nvert):
            pidx.append(int(L[i + k * 3].split()[0]))
            u = L[i + k * 3 + 1].split()
            uv.append((float(u[0]), float(u[1])))
        i += nvert * 3
        if i < len(L) and L[i].strip() == "0":
            i += 1
        ntri = int(L[i].strip())
        i += 1
        tri = [tuple(map(int, L[i + k].split()[:3])) for k in range(ntri)]
        i += ntri
        parts.append(dict(mat=mat, P=P, pidx=pidx, uv=uv, tri=tri))
    return parts


def bm_from(P, tris_uv):
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in P]
    bm.verts.index_update()
    lay = bm.loops.layers.uv.new("UVMap")
    for (t, u) in tris_uv:
        try:
            f = bm.faces.new([vs[t[0]], vs[t[1]], vs[t[2]]])
            for j in range(3):
                f.loops[j][lay].uv = u[j]
        except (ValueError, IndexError):
            pass
    return bm


def bm_out(bm):
    bm.verts.index_update()
    P = [tuple(v.co) for v in bm.verts]
    lay = bm.loops.layers.uv.active
    tris, uvs = [], []
    for f in bm.faces:
        tris.append(tuple(l.vert.index for l in f.loops))
        uvs.append([tuple(l[lay].uv) for l in f.loops])
    return P, tris, uvs


def rebuild(P, tris, uvs, weld, deg):
    bm = bm_from(P, list(zip(tris, uvs)))
    if weld > 0:
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=weld)
    bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(deg),
                             verts=bm.verts, edges=bm.edges)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    out = bm_out(bm)
    bm.free()
    return out


def write_trf(path, blocks, name):
    out = ["rfver 4", "mesh %d" % len(blocks)]
    for k, (mat, P, tris, uvs) in enumerate(blocks):
        out.append("%s.%d 0 %s" % (name, k, mat))
        out.append(str(len(P)))
        out.extend(["%.6f %.6f %.6f" % p for p in P])
        out.append("0")
        vmap, order, ttri = {}, [], []
        for ti, t in enumerate(tris):
            idx = []
            for j, pi in enumerate(t):
                u, v = uvs[ti][j]
                key = (pi, round(u, 6), round(v, 6))
                if key not in vmap:
                    vmap[key] = len(order)
                    order.append(key)
                idx.append(vmap[key])
            ttri.append(tuple(idx))
        out.append(str(len(order)))
        for (pi, u, v) in order:
            out.append("%d 4294967295 0.000000 0.000000 1.000000" % pi)
            out.append("%.6f %.6f" % (u, v))
            out.append("0.000000 0.000000")
        out.append("0")
        out.append(str(len(ttri)))
        out.extend(["%d %d %d" % t for t in ttri])
    out.append("end")
    io.open(path, "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")
    return os.path.getsize(path)


def process(src, dst, name, cls=""):
    parts = parse(src)
    order, by = [], {}
    for p in parts:
        if p["mat"] not in by:
            by[p["mat"]] = []
            order.append(p["mat"])
        by[p["mat"]].append(p)
    nlev = max(len(v) for v in by.values())
    if nlev <= 1:
        # 单档：只做共享顶点写盘
        blocks = []
        for mat in order:
            p = by[mat][0]
            tris = [(p["pidx"][a], p["pidx"][b], p["pidx"][c]) for (a, b, c) in p["tri"]]
            uvs = [[p["uv"][a], p["uv"][b], p["uv"][c]] for (a, b, c) in p["tri"]]
            blocks.append((mat, p["P"], tris, uvs))
        return write_trf(dst, blocks, name), nlev

    from collections import Counter
    COMMON = Counter(len(v) for v in by.values()).most_common(1)[0][0]
    A = np.array([p for m in order for p in by[m][0]["P"]])
    diag = float(np.linalg.norm(A.max(0) - A.min(0))) or 1.0

    blocks = []
    merged = None
    for mat in order:
        lst = by[mat]
        base = lst[0]
        b_tris = [(base["pidx"][a], base["pidx"][b], base["pidx"][c]) for (a, b, c) in base["tri"]]
        b_uvs = [[base["uv"][a], base["uv"][b], base["uv"][c]] for (a, b, c) in base["tri"]]
        thin = any(k in mat for k in THIN)
        for level, p in enumerate(lst):
            if level >= COMMON:
                continue
            if level == 0 or thin:
                blocks.append((mat, base["P"], b_tris, b_uvs))
            else:
                f = 0.5 if cls in THIN_CLASS else 1.0
                w = min(diag * WELD_F.get(level, 0.005) * f, WELD_CAP * f)
                P, tris, uvs = rebuild(base["P"], b_tris, b_uvs, w, DEG[level])
                blocks.append((mat, P, tris, uvs))
        big = (diag > 8.0 and len(b_tris) > 3000)
        if len(lst) > COMMON and big:
            if merged is None:
                flat_tris, flat_uvs = [], []
                for m2 in order:
                    b = by[m2][0]
                    for (a, bb, c) in b["tri"]:
                        flat_tris.append((b["pidx"][a], b["pidx"][bb], b["pidx"][c]))
                        flat_uvs.append([b["uv"][a], b["uv"][bb], b["uv"][c]])
                merged = rebuild(A.tolist(), flat_tris, flat_uvs,
                                 diag * 0.033, DEG[5])
            blocks.append((order[0], merged[0], merged[1], merged[2]))
        elif len(lst) > COMMON:
            # 小模型：末档沿用上一档几何
            last = blocks[-1]
            blocks.append((last[0], last[1], last[2], last[3]))
    return write_trf(dst, blocks, name), nlev


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    inroot, outroot, logcsv = argv[0], argv[1], argv[2]
    limit = int(argv[3]) if len(argv) > 3 else 10 ** 9

    files = []
    for dirpath, _, names in os.walk(inroot):
        if os.path.basename(dirpath).lower() == "main_map_lod":
            continue
        for n in names:
            if n.lower().endswith(".trf"):
                files.append(os.path.join(dirpath, n))
    files.sort()
    files = files[:limit]
    print("[batch] %d 个文件" % len(files))

    log = io.open(logcsv, "a", encoding="utf-8")
    if log.tell() == 0:
        log.write("file,class,in_bytes,out_bytes,levels,seconds,status\n")

    t0 = time.time()
    ok = fail = 0
    tot_in = tot_out = 0
    for k, src in enumerate(files):
        rel = os.path.relpath(src, inroot)
        cls = rel.split(os.sep)[0]
        dst = os.path.join(outroot, rel)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        name = os.path.basename(src)[:-4]
        s0 = time.time()
        try:
            in_sz = os.path.getsize(src)
            out_sz, nlev = process(src, dst, name, cls)
            if out_sz > in_sz:
                import shutil
                shutil.copyfile(src, dst)
                out_sz = in_sz
            ok += 1
            tot_in += in_sz
            tot_out += out_sz
            log.write("%s,%s,%d,%d,%d,%.2f,ok\n" % (rel, cls, in_sz, out_sz, nlev, time.time() - s0))
        except Exception as e:
            fail += 1
            log.write("%s,%s,0,0,0,%.2f,ERR:%s\n" % (rel, cls, time.time() - s0, str(e)[:80]))
        if (k + 1) % 20 == 0:
            log.flush()
            el = time.time() - t0
            rate = (k + 1) / el
            eta = (len(files) - k - 1) / rate / 60.0
            print("[%d/%d] ok=%d fail=%d  %.2f 文件/秒  ETA %.1f 分钟  累计 %.1f→%.1f MB"
                  % (k + 1, len(files), ok, fail, rate, eta, tot_in / 1e6, tot_out / 1e6))
    log.flush()
    log.close()
    print("[done] ok=%d fail=%d  用时 %.1f 分钟" % (ok, fail, (time.time() - t0) / 60.0))
    print("[size] 输入 %d B (%.2f GB) → 输出 %d B (%.2f GB)  比例 %.1f%%"
          % (tot_in, tot_in / 1073741824.0, tot_out, tot_out / 1073741824.0,
             100.0 * tot_out / max(1, tot_in)))


if __name__ == "__main__":
    main()

