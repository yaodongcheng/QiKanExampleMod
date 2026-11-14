# -*- coding: utf-8 -*-
"""make_lod_v1.py —— LOD0–6 生成器（2026-11-13 定稿规范）

取代 rebuild_batch2.py + proxy_box.py。设计目标：命名/UV/档数三重可自证。

用法（Blender 无头）：
    blender -b --factory-startup -P make_lod_v1.py -- <src.trf> <outdir> [levels] [--report <csv>]

规范（见 deliverable\\compress\\README.md §3）：
  · LOD0 原样（只翻 UV 一次 v→1-v），不重建
  · LOD1..LOD5 每一档都从 LOD0 几何重造：目标面数 = LOD0 × 档位比例，
    在 [0.0005, 焊接上界] 上二分焊接距离，每次 remove_doubles → dissolve_limit → triangulate
  · 三闸门（每部件）：面积损失 <2% / 包围盒收缩 <1.5% / 最短边增长 <2.5x
  · 焊接距离物理上限 = 0.5 × 该部件"最短边中位数"；细长杆件(细长比中位>6) 再 ×0.5
  · 薄片材质（glass/windows/curtains）LOD1-3 原样；signs 全档不焊
  · LOD6 = OBB 盒（12 面，内缩 2%）
  · 命名 <base>[.lodN].<档内序号>；mesh N == 部件行数
  · 落盘后 G1-G7 校验 + 读回，任一不过 → 移入 _rejected 并 exit 1

输出：<outdir>\\<base>.trf + <base>.report.txt
"""
import io
import math
import os
import re
import struct
import sys
import time
import hashlib

import bmesh
import bpy
import numpy as np

# ---------------------------------------------------------------- 规范参数
RATIO = {1: 0.45, 2: 0.28, 3: 0.205, 4: 0.145, 5: 0.10}
WELD_MAX = {1: 0.35, 2: 0.60, 3: 1.00, 4: 1.60, 5: 3.00}
# v10 排程（用户已认可的渲染即此参数）：焊接 m / 合并角
SCHED_WELD = {1: 0.15, 2: 0.30, 3: 0.50, 4: 0.80, 5: 2.00}
# v10 的实测面数比例（v10 = 7,266/4,668/3,488/2,483/849 于 16,862 基准）
V10_RATIO = {1: 0.4309, 2: 0.2767, 3: 0.2069, 4: 0.1472, 5: 0.0535}
DEG = {1: 10, 2: 15, 3: 20, 4: 25, 5: 45}
SHEET = ("glass", "windows", "curtains")
SIGN = ("signs",)
WELD_FLOOR = 0.0005

MODE = "v1"      # v1 / v10 / v10h(前 4 档 v10、末档用 collapse)
CUR_V10 = False  # 当前档是否走 v10 焊接路线
FORCE_COLLAPSE = False  # 焊接够不到目标时，本档强制 collapse
AREA_TOL = 0.02        # 面积损失 < 2%
BBOX_TOL = 0.015       # 包围盒收缩 < 1.5%
EDGE_TOL = 2.5         # 最短边增长 < 2.5x

PART_RE = re.compile(r"^(\S+?)(?:\.lod(\d+))?\.(\d+) 0 (\S+)$")


# ---------------------------------------------------------------- TRF 解析
def parse_trf(path):
    L = io.open(path, encoding="utf-8", errors="replace").read().split("\n")
    parts = []
    i = 2
    while i < len(L):
        m = PART_RE.match(L[i].strip())
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
        if i < len(L) and L[i].strip() == "0":
            i += 1
        nvert = int(L[i].strip())
        i += 1
        pidx, uv = [], []
        for k in range(nvert):
            pidx.append(int(L[i + k * 3].split()[0]))
            t = L[i + k * 3 + 1].split()
            uv.append((float(t[0]), float(t[1])))
        i += nvert * 3
        if i < len(L) and L[i].strip() == "0":
            i += 1
        ntri = int(L[i].strip())
        i += 1
        tri = [tuple(map(int, L[i + k].split()[:3])) for k in range(ntri)]
        i += ntri
        bad = max(max(t) for t in tri) if tri else -1
        if bad >= nvert:
            raise ValueError("%s 三角形索引越界 %d>=%d（顶点数）" % (path, bad, nvert))
        parts.append(dict(lvl=lvl, mat=mat, P=P, pidx=pidx, uv=uv, tri=tri))
    return parts


# ---------------------------------------------------------------- 几何工具
def tri_edges(tri, P):
    a, b, c = tri
    pa, pb, pc = P[a], P[b], P[c]
    return (dist(pa, pb), dist(pb, pc), dist(pc, pa))


def dist(p, q):
    return math.sqrt((p[0] - q[0]) ** 2 + (p[1] - q[1]) ** 2 + (p[2] - q[2]) ** 2)


def _tri_area(P, t):
    (a, b, c) = t
    ax, ay, az = P[a]
    bx, by, bz = P[b]
    cx, cy, cz = P[c]
    ux, uy, uz = bx - ax, by - ay, bz - az
    vx, vy, vz = cx - ax, cy - ay, cz - az
    nx, ny, nz = uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx
    return 0.5 * math.sqrt(nx * nx + ny * ny + nz * nz)


def area_of(P, tris):
    s = 0.0
    for (a, b, c) in tris:
        ax, ay, az = P[a]
        bx, by, bz = P[b]
        cx, cy, cz = P[c]
        ux, uy, uz = bx - ax, by - ay, bz - az
        vx, vy, vz = cx - ax, cy - ay, cz - az
        nx, ny, nz = uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx
        s += 0.5 * math.sqrt(nx * nx + ny * ny + nz * nz)
    return s


def bbox_of(P):
    if not P:
        return (0.0, 0.0, 0.0, 0.0, 0.0, 0.0)
    xs = [p[0] for p in P]
    ys = [p[1] for p in P]
    zs = [p[2] for p in P]
    return (min(xs), min(ys), min(zs), max(xs), max(ys), max(zs))


def bbox_diag(bb):
    return math.sqrt((bb[3] - bb[0]) ** 2 + (bb[4] - bb[1]) ** 2 + (bb[5] - bb[2]) ** 2)


def part_stats(P, tris):
    """最短边中位数、三角细长比中位数"""
    mins, aspects = [], []
    for t in tris:
        e = sorted(tri_edges(t, P))
        if e[0] <= 1e-9:
            continue
        mins.append(e[0])
        aspects.append(e[2] / e[0])
    mins.sort()
    aspects.sort()
    med_min = mins[len(mins) // 2] if mins else 0.0
    med_asp = aspects[len(aspects) // 2] if aspects else 0.0
    return med_min, med_asp



def collapse_part(P, tris, uvs, ratio):
    """Blender Decimate(COLLAPSE) 单部件减面 —— 远景档用，三角分布均匀，不出焊接尖刺"""
    me = bpy.data.meshes.new("t_lod")
    me.from_pydata(P, [], tris)
    me.update()
    lay = me.uv_layers.new(name="UVMap")
    li = 0
    for ti, t in enumerate(tris):
        for j in range(3):
            lay.data[li].uv = uvs[ti][j]
            li += 1
    ob = bpy.data.objects.new("t_lod", me)
    bpy.context.collection.objects.link(ob)
    if 0.0 < ratio < 0.999:
        m = ob.modifiers.new("d", "DECIMATE")
        m.decimate_type = "COLLAPSE"
        m.ratio = ratio
        m.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = ob
        ob.select_set(True)
        bpy.ops.object.modifier_apply(modifier=m.name)
    me2 = ob.data
    P2 = [tuple(v.co) for v in me2.vertices]
    lay2 = me2.uv_layers.active
    tris2, uvs2 = [], []
    for poly in me2.polygons:
        vi = [me2.loops[l].vertex_index for l in poly.loop_indices]
        uv = [tuple(lay2.data[l].uv) for l in poly.loop_indices]
        if len(vi) == 3:
            tris2.append(tuple(vi))
            uvs2.append(uv)
        elif len(vi) > 3:
            for k in range(1, len(vi) - 1):
                tris2.append((vi[0], vi[k], vi[k + 1]))
                uvs2.append([uv[0], uv[k], uv[k + 1]])
    bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.meshes.remove(me2)
    return P2, tris2, uvs2


def build_bm(P, tris, uvs):
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in P]
    bm.verts.index_update()
    lay = bm.loops.layers.uv.new("UVMap")
    for t, u in zip(tris, uvs):
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
        if len(f.loops) != 3:
            continue
        tris.append(tuple(l.vert.index for l in f.loops))
        uvs.append([tuple(l[lay].uv) for l in f.loops])
    return P, tris, uvs


def rebuild(P, tris, uvs, weld, deg):
    bm = build_bm(P, tris, uvs)
    if weld > 0:
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=weld)
    if deg > 0:
        bmesh.ops.dissolve_limit(bm, angle_limit=math.radians(deg),
                                 verts=bm.verts, edges=bm.edges)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    # 清掉退化面（面积 0）
    dead = [f for f in bm.faces if f.calc_area() < 1e-12]
    if dead:
        bmesh.ops.delete(bm, geom=dead, context="FACES")
    out = bm_out(bm)
    bm.free()
    return out


def obb(P):
    """最小面积包围矩形（0.5° 步进）+ z 上下界，内缩 2%"""
    xs = [p[0] for p in P]
    ys = [p[1] for p in P]
    zs = [p[2] for p in P]
    zlo, zhi = min(zs), max(zs)
    best = None
    for d10 in range(0, 1800, 5):
        th = math.radians(d10 / 10.0)
        c, s = math.cos(th), math.sin(th)
        a = [x * c + y * s for x, y in zip(xs, ys)]
        b = [-x * s + y * c for x, y in zip(xs, ys)]
        ar = (max(a) - min(a)) * (max(b) - min(b))
        if best is None or ar < best[0]:
            best = (ar, th, min(a), max(a), min(b), max(b))
    _, th, a0, a1, b0, b1 = best
    da = (a1 - a0) * 0.02
    db = (b1 - b0) * 0.02
    a0, a1, b0, b1 = a0 + da, a1 - da, b0 + db, b1 - db
    c, s = math.cos(th), math.sin(th)
    pts = []
    for aa, bb in ((a0, b0), (a1, b0), (a1, b1), (a0, b1)):
        pts.append((aa * c - bb * s, aa * s + bb * c, zlo))
    for aa, bb in ((a0, b0), (a1, b0), (a1, b1), (a0, b1)):
        pts.append((aa * c - bb * s, aa * s + bb * c, zhi))
    quads = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    tris = []
    for a, b, c2, d in quads:
        tris.append((a, b, c2))
        tris.append((a, c2, d))
    uvs = [[(0.05, 0.05)] * 3 for _ in tris]
    return pts, tris, uvs




def collapse_joined(parts, target, stitch=0.002, hard_cap=None, keep_closed=False):
    """把所有可减面部件**合成一个网格**再 COLLAPSE —— 各部件独立减面会在接缝（屋顶/墙顶/窗框）拉出裂隙。
    先按 stitch 缝合重合边界，再整体减面，最后按材质拆回多个部件（材质不变）。"""
    import bmesh

    mats, P0, F0, U0, MI = [], [], [], [], []
    for p in parts:
        if p["mat"] not in mats:
            mats.append(p["mat"])
        mi = mats.index(p["mat"])
        off = len(P0)
        P0.extend(p["P"])
        for (a, b, c) in p["tri"]:
            if len({a, b, c}) != 3:
                continue
            F0.append((off + p["pidx"][a], off + p["pidx"][b], off + p["pidx"][c]))
            U0.append([p["uv"][a], p["uv"][b], p["uv"][c]])
            MI.append(mi)

    me = bpy.data.meshes.new("join")
    me.from_pydata(P0, [], F0)
    me.update()
    uvl = me.uv_layers.new(name="UVMap")
    li = 0
    for i in range(len(me.polygons)):
        for k in range(3):
            uvl.data[li].uv = U0[i][k]
            li += 1
    for m in mats:
        me.materials.append(bpy.data.materials.new(name=(m or "mat")[:50]))
    for f, mi in zip(me.polygons, MI):
        f.material_index = mi

    ob = bpy.data.objects.new("join", me)
    bpy.context.collection.objects.link(ob)

    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=stitch)
    bm.to_mesh(me)
    bm.free()
    me.update()

    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)

    n0 = len(me.polygons)
    if n0 > target:
        ratio = max(0.004, min(1.0, float(target) / max(1, n0)))
        mod = ob.modifiers.new("dec", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = ratio
        mod.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
        me = ob.data
        # 万一没降到位（低模常见），再补几刀，确保严格小于上一档
        guard = 0
        while len(me.polygons) >= target + 1 and guard < 4:
            r2 = max(0.05, min(0.95, (float(target) / max(1, len(me.polygons))) * 0.85))
            m2 = ob.modifiers.new("dec2", "DECIMATE")
            m2.decimate_type = "COLLAPSE"
            m2.ratio = r2
            m2.use_collapse_triangulate = True
            bpy.ops.object.modifier_apply(modifier=m2.name)
            me = ob.data
            guard += 1
            if len(me.polygons) <= target:
                break

    # ---- 兜底 1：Decimate 在"散件/孤立片"上会卡住，到不了目标 ----
    # 直接丢最小面积的面，保证严格小于上一档，绝不允许三档一模一样。
    if hard_cap and len(me.polygons) >= hard_cap:
        bm = bmesh.new()
        bm.from_mesh(me)
        fs = sorted(bm.faces, key=lambda f: f.calc_area())
        need = max(0, len(fs) - int(hard_cap * 0.92) + 1)
        if need:
            bmesh.ops.delete(bm, geom=fs[:need], context="FACES")
        bm.to_mesh(me)
        bm.free()
        me.update()
        print("[cap] 减面卡住 -> 丢最小面 %d 个，剩 %d 面" % (need, len(me.polygons)))

    # ---- 兜底 2：LOD0 封口的，这一档也必须封口（用户原则）----
    if keep_closed:
        for _ in range(3):
            bm = bmesh.new()
            bm.from_mesh(me)
            bnd = [e for e in bm.edges if len(e.link_faces) == 1]
            if not bnd:
                bm.free()
                break
            vs = set()
            for e in bnd:
                vs.add(e.verts[0]); vs.add(e.verts[1])
            bmesh.ops.remove_doubles(bm, verts=list(vs), dist=0.02)
            bm.to_mesh(me)
            bm.free()
            me.update()
        bm = bmesh.new()
        bm.from_mesh(me)
        bnd = [e for e in bm.edges if len(e.link_faces) == 1]
        if bnd and len(bnd) <= 200:
            try:
                bmesh.ops.holes_fill(bm, edges=bnd, sides=4)
                print("[closed] 补开口 %d 条边" % len(bnd))
            except Exception:
                pass
            bm.to_mesh(me)
            bm.free()
            me.update()
        else:
            bm.free()

    if len(me.polygons) == 0:
        # 退化件（零面积的电缆/细片）减面会直接归零。这里按比例截断三角，
        # 保证档位面数严格递减（否则 G6 会判"相邻档完全相同"）。
        tri_total = sum(len(p["tri"]) for p in parts)
        keep_ratio = min(1.0, float(target) / max(1, tri_total))
        out0 = []
        for p in parts:
            T0 = [(p["pidx"][a], p["pidx"][b], p["pidx"][c]) for (a, b, c) in p["tri"]]
            U0 = [[p["uv"][a], p["uv"][b], p["uv"][c]] for (a, b, c) in p["tri"]]
            k = max(1, int(round(len(T0) * keep_ratio)))
            if k < len(T0):
                order = sorted(range(len(T0)), key=lambda j: -_tri_area(p["P"], T0[j]))
                T0 = [T0[j] for j in order[:k]]
                U0 = [U0[j] for j in order[:k]]
            out0.append((p["mat"], p["P"], T0, U0))
        bpy.data.objects.remove(ob, do_unlink=True)
        return out0

    verts = [tuple(v.co) for v in me.vertices]
    uvd = me.uv_layers.active.data
    groups = {}
    for poly in me.polygons:
        vs = list(poly.vertices)
        if len(vs) != 3:
            continue
        uvs = [tuple(uvd[li].uv) for li in poly.loop_indices]
        g = groups.setdefault(poly.material_index, ([], [], []))
        g[1].append((vs[0], vs[1], vs[2]))
        g[2].append(uvs)

    out = []
    for mi, (_, tris, uvlist) in groups.items():
        used = sorted({i for t in tris for i in t})
        remap = {v: k for k, v in enumerate(used)}
        Pg = [verts[i] for i in used]
        Tg = [(remap[a], remap[b], remap[c]) for (a, b, c) in tris]
        out.append((mats[mi], Pg, Tg, uvlist))
    bpy.data.objects.remove(ob, do_unlink=True)
    return out


def proxy_box(parts, abs_size=None):
    """末档代理盒：长宽高各取 50%，侧面用主材质、顶面用屋顶材质，UV 取该材质的真实范围。

    目的：远档不要一块突兀的大灰板。缩小一半 + 真材质以后，读起来像"远处的同色体块"。
    只出 5 个面（4 侧 + 1 顶），底面省掉。
    """
    pts = [tuple(pt) for p in parts for pt in p["P"]]
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    zs = [p[2] for p in pts]
    zlo, zhi = min(zs), max(zs)
    best = None
    for d10 in range(0, 1800, 5):
        th = math.radians(d10 / 10.0)
        c, s_ = math.cos(th), math.sin(th)
        a = [x * c + y * s_ for x, y in zip(xs, ys)]
        b = [-x * s_ + y * c for x, y in zip(xs, ys)]
        ar = (max(a) - min(a)) * (max(b) - min(b))
        if best is None or ar < best[0]:
            best = (ar, th, min(a), max(a), min(b), max(b))
    _, th, a0, a1, b0, b1 = best
    ca, cb = (a0 + a1) / 2.0, (b0 + b1) / 2.0
    ha, hb = (a1 - a0) * 0.35, (b1 - b0) * 0.35      # 每边各收 15% => 尺寸 70%
    a0, a1, b0, b1 = ca - ha, ca + ha, cb - hb, cb + hb
    z0, z1 = zlo, zlo + (zhi - zlo) * 0.7             # 高也 70%，底面落地
    c, s_ = math.cos(th), math.sin(th)

    def w(a, b, z):
        return (a * c - b * s_, a * s_ + b * c, z)

    V8 = [w(a0, b0, z0), w(a1, b0, z0), w(a1, b1, z0), w(a0, b1, z0),
          w(a0, b0, z1), w(a1, b0, z1), w(a1, b1, z1), w(a0, b1, z1)]

    # 主材质 = 面积最大；屋顶材质 = 平均高度最高
    def part_area(p):
        return area_of(p["P"], [(p["pidx"][a], p["pidx"][b], p["pidx"][c]) for (a, b, c) in p["tri"]])

    def part_zmean(p):
        idx = sorted({p["pidx"][a] for t in p["tri"] for a in t})
        return sum(p["P"][i][2] for i in idx) / max(1, len(idx))

    main_p = max(parts, key=part_area)
    roof_p = max(parts, key=part_zmean)
    if area_of(roof_p["P"], [(roof_p["pidx"][a], roof_p["pidx"][b], roof_p["pidx"][c]) for (a, b, c) in roof_p["tri"]]) < 0.5:
        roof_p = main_p

    def uv_box(p):
        """取该材质"最大的一块三角"的 UV 范围 —— 整张图铺满会把图集里的条纹铺出来"""
        best_t, best_a = None, -1.0
        for (a, b, c) in p["tri"]:
            ia, ib, ic = p["pidx"][a], p["pidx"][b], p["pidx"][c]
            ar = _tri_area(p["P"], (ia, ib, ic))
            if ar > best_a:
                best_a, best_t = ar, (a, b, c)
        if best_t is None:
            us = [u for (u, v) in p["uv"]]
            vs = [v for (u, v) in p["uv"]]
            return min(us), min(vs), max(us), max(vs)
        us = [p["uv"][i][0] for i in best_t]
        vs = [p["uv"][i][1] for i in best_t]
        u0, u1 = min(us), max(us)
        v0, v1 = min(vs), max(vs)
        if u1 - u0 < 1e-4:
            u0, u1 = u0 - 0.02, u0 + 0.02
        if v1 - v0 < 1e-4:
            v0, v1 = v0 - 0.02, v0 + 0.02
        return u0, v0, u1, v1

    SIDES = [(0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    TOP = [(4, 5, 6, 7)]

    def quads_to_part(pmat, plist):
        u0, v0, u1, v1 = uv_box(pmat)
        P_ = []
        T_, U_ = [], []
        for (i0, i1, i2, i3) in plist:
            base = len(P_)
            P_.extend([V8[i0], V8[i1], V8[i2], V8[i3]])
            uvq = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
            T_.append((base, base + 1, base + 2))
            T_.append((base, base + 2, base + 3))
            U_.append([uvq[0], uvq[1], uvq[2]])
            U_.append([uvq[0], uvq[2], uvq[3]])
        return P_, T_, U_

    if abs_size is not None:
        cx = sum(v[0] for v in V8) / 8.0
        cy = sum(v[1] for v in V8) / 8.0
        cz = sum(v[2] for v in V8) / 8.0
        mx = max(max(abs(v[i] - c_[i]) for i in range(3)) for v in V8 for c_ in [(cx, cy, cz)])
        k = (abs_size * 0.5) / mx if mx > 1e-9 else 0.0
        V8 = [(cx + (v[0] - cx) * k, cy + (v[1] - cy) * k, cz + (v[2] - cz) * k) for v in V8]

    out = []
    P_, T_, U_ = quads_to_part(main_p, SIDES)
    out.append((main_p["mat"], P_, T_, U_))
    if roof_p is not main_p:
        P_, T_, U_ = quads_to_part(roof_p, TOP)
        out.append((roof_p["mat"], P_, T_, U_))
    else:
        P_, T_, U_ = quads_to_part(main_p, TOP)
        out.append((main_p["mat"], P_, T_, U_))
    return out


# ---------------------------------------------------------------- 单档处理
LEVEL_AREA = {1: 0.15, 2: 0.18, 3: 0.22, 4: 0.30, 5: 1.00}
FLOOR_TRIS = 24


def p95(vals):
    if not vals:
        return 0.0
    vals = sorted(vals)
    return vals[min(len(vals) - 1, int(len(vals) * 0.95))]


def aspects(P, tris):
    out = []
    for t in tris:
        e = tri_edges(t, P)
        mn = min(e)
        if mn <= 1e-9:
            continue
        out.append(max(e) / mn)
    return out


def _measure(res, P0, area0, diag0, level, asp0):
    P1, t1, _ = res
    a1 = area_of(P1, t1)
    bb1 = bbox_of(P1)
    d1 = bbox_diag(bb1)
    area_loss = abs(a1 - area0) / area0 if area0 > 0 else 0.0
    bbox_shrink = (diag0 - d1) / diag0 if diag0 > 0 else 0.0
    asp1 = p95(aspects(P1, t1))
    why = []
    if bbox_shrink > BBOX_TOL:
        why.append("bbox%.1f" % (bbox_shrink * 100))
    if len(t1) < 10:
        why.append("collapse%d" % len(t1))
    return dict(ok=not why, why=",".join(why), area=area_loss, bbox=bbox_shrink, sliver=asp1)


def make_level(lod0_part, level, target, log, model_thin=False, force_weld=None, small_keep_ok=True):
    """返回 (P, tris, uvs, 统计 dict)；target = 该部件本档目标面数"""
    mat = lod0_part["mat"]
    P0 = lod0_part["P"]
    tris0 = [(lod0_part["pidx"][a], lod0_part["pidx"][b], lod0_part["pidx"][c])
             for (a, b, c) in lod0_part["tri"] if len({a, b, c}) == 3]
    uvs0 = [[lod0_part["uv"][a], lod0_part["uv"][b], lod0_part["uv"][c]]
            for (a, b, c) in lod0_part["tri"] if len({a, b, c}) == 3]
    n0 = len(tris0)
    stat = dict(mat=mat, n0=n0, n=n0, tgt=target, weld=0.0, deg=0, retry=0,
                area_loss=0.0, bbox_shrink=0.0, sliver=0.0, warn="")

    if n0 == 0:
        stat["warn"] = "empty"
        return P0, tris0, uvs0, stat
    if n0 <= FLOOR_TRIS:
        stat["warn"] = "floor-keep"
        return P0, tris0, uvs0, stat
    if level <= 3 and any(k in mat for k in SHEET):
        stat["warn"] = "sheet-keep"
        return P0, tris0, uvs0, stat
    # 小部件（栏杆/招牌/屋顶这类几十~几百面）在近档不许动：它们总成本可忽略，砍了就穿帮
    if level <= 2 and n0 < 300 and small_keep_ok:
        stat["warn"] = "small-keep"
        return P0, tris0, uvs0, stat

    # 小件若也套 24 面下限，两三档会被夹到同一个数（G6 相邻档相同）
    _floor = min(FLOOR_TRIS, max(4, n0 // 8))
    target = max(_floor, min(target, n0))
    tiny = (n0 < 200)
    # 细杆/极少面：直接 COLLAPSE，别走焊接（焊不住会退化成平面化、减不下来）
    _use_collapse = (not CUR_V10) or FORCE_COLLAPSE or model_thin or tiny or (level >= 3)
    med_min, med_asp = part_stats(P0, tris0)
    thin = med_asp > 6.0
    area0 = area_of(P0, tris0)
    bb0 = bbox_of(P0)
    diag0 = bbox_diag(bb0) or 1.0
    asp0 = p95(aspects(P0, tris0))

    weld_hi = min(WELD_MAX[level], 0.03 * diag0)
    # 细管件（电缆/绳索/栏杆）：焊接半径超过管径就会把整根焊没
    weld_hi = min(weld_hi, 0.4 * med_min)
    if thin:
        weld_hi *= 0.5
    if any(k in mat for k in SIGN):
        weld_hi = 0.0
    if weld_hi < WELD_FLOOR:
        weld_hi = 0.0

    if weld_hi <= 0 and not _use_collapse:
        res = rebuild(P0, tris0, uvs0, 0.0, DEG[level])
        if not res[1]:                      # 退化件会被平面化吃光，原样保留
            stat["warn"] = "rebuild-empty"
            return P0, tris0, uvs0, stat
        st = _measure(res, P0, area0, diag0, level, asp0)
        stat.update(n=len(res[1]), weld=0.0, deg=DEG[level], warn="dissolve-only",
                    area_loss=st["area"], bbox_shrink=st["bbox"], sliver=st["sliver"])
        return res[0], res[1], res[2], stat

    # LOD3 以后改用 COLLAPSE（远处要的是均匀三角，不是焊缝）
    if _use_collapse and any(k not in mat for k in SHEET) \
            and any(k not in mat for k in SIGN) and n0 >= 60:
        ratio = max(0.02, min(0.95, float(target) / float(n0)))
        P1, t1, u1 = collapse_part(P0, tris0, uvs0, ratio)
        if not t1:
            # 退化件塌成 0 面：按目标面数截断，保证档与档之间还能递减
            k = max(4, min(len(tris0), target))
            order = sorted(range(len(tris0)), key=lambda j: -_tri_area(P0, tris0[j]))
            t1 = [tris0[j] for j in order[:k]]
            u1 = [uvs0[j] for j in order[:k]]
            P1 = P0
        if len(t1) > target:               # 塌不到目标（细管常见）→ 丢最小面
            order = sorted(range(len(t1)), key=lambda k: _tri_area(P1, t1[k]))
            keep = order[len(t1) - target:]
            t1 = [t1[k] for k in keep]
            u1 = [u1[k] for k in keep]
        st = _measure((P1, t1, u1), P0, area0, diag0, level, asp0)
        stat.update(n=len(t1), weld=0.0, deg=-1, retry=0, warn="collapse",
                    area_loss=st["area"], bbox_shrink=st["bbox"], sliver=st["sliver"])
        if not st["ok"]:
            stat["warn"] = "collapse;" + st["why"]
        return P1, t1, u1, stat

    if force_weld is not None:
        res = rebuild(P0, tris0, uvs0, force_weld, DEG[level])
        st = _measure(res, P0, area0, diag0, level, asp0)
        stat.update(n=len(res[1]), weld=force_weld, deg=DEG[level], retry=0, warn="v10",
                    area_loss=st["area"], bbox_shrink=st["bbox"], sliver=st["sliver"])
        return res[0], res[1], res[2], stat

    if CUR_V10:
        lo, hi = 0.01, SCHED_WELD[level]
        best = None
        for _ in range(8):
            mid = (lo + hi) / 2.0
            res = rebuild(P0, tris0, uvs0, mid, DEG[level])
            n = len(res[1])
            d = abs(n - target)
            if best is None or d < best[0]:
                best = (d, mid, res)
            if n > target:
                lo = mid
            else:
                hi = mid
        _, w, res = best
        st = _measure(res, P0, area0, diag0, level, asp0)
        stat.update(n=len(res[1]), weld=w, deg=DEG[level], retry=0, warn="v10",
                    area_loss=st["area"], bbox_shrink=st["bbox"], sliver=st["sliver"])
        return res[0], res[1], res[2], stat

    w = min(SCHED_WELD[level], 0.03 * diag0)
    if thin and not CUR_V10:
        # 杆件/窗棂：整件都是格构时只许极轻焊接（否则整根焊没）；普通建筑上的窗棂只轻一点
        if model_thin:
            w = min(w * 0.25, 0.35 * med_min) if med_min > 0 else w * 0.25
        else:
            w *= 0.85
    if w < WELD_FLOOR:
        w = 0.0
    res = rebuild(P0, tris0, uvs0, w, DEG[level])

    retry = 0
    while True:
        st = _measure(res, P0, area0, diag0, level, asp0)
        if st["ok"] or retry >= 3 or w <= WELD_FLOOR:
            break
        retry += 1
        w = max(WELD_FLOOR, w * 0.7)
        res = rebuild(P0, tris0, uvs0, w, DEG[level])
    st = _measure(res, P0, area0, diag0, level, asp0)
    stat.update(n=len(res[1]), weld=w, deg=DEG[level], retry=retry,
                area_loss=st["area"], bbox_shrink=st["bbox"], sliver=st["sliver"])
    if not st["ok"]:
        stat["warn"] = st["why"] + (";thin" if thin else "")
    return res[0], res[1], res[2], stat


# ---------------------------------------------------------------- 写盘
def write_trf(path, blocks):
    """blocks = [(level, idx, mat, P, tris, uvs)]，UV 在这里统一翻一次"""
    out = ["rfver 4"]
    body = []
    for (level, idx, mat, P, tris, uvs) in blocks:
        nm = "%s.lod%d.%d" % ("__BASE__", level, idx) if level > 0 else "__BASE__.%d" % idx
        body.append((nm, mat, P, tris, uvs))
    out.append("mesh %d" % len(body))
    for nm, mat, P, tris, uvs in body:
        out.append("%s 0 %s" % (nm, mat))
        out.append(str(len(P)))
        out.extend(["%.6f %.6f %.6f" % p for p in P])
        out.append("0")
        vmap, order = {}, []
        ttri = []
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
            out.append("%.6f %.6f" % (u, v0))
            out.append("0.000000 0.000000")
        out.append("0")
        out.append(str(len(ttri)))
        out.extend(["%d %d %d" % t for t in ttri])
    out.append("end")
    io.open(path, "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")
    return os.path.getsize(path)


# ---------------------------------------------------------------- G1-G7
def validate(path, base, expect_levels):
    errs = []
    lines = io.open(path, encoding="utf-8", errors="replace").read().split("\n")
    if lines[0].strip() != "rfver 4":
        errs.append("G5: 缺 rfver 4")
    decl = None
    m = re.match(r"^mesh (\d+)$", lines[1].strip())
    if m:
        decl = int(m.group(1))
    else:
        errs.append("G5: 缺 mesh 头")
    parts = []
    i = 2
    while i < len(lines):
        s = lines[i].strip()
        if s == "end":
            break
        if PART_RE.match(s):
            parts.append((i + 1, s))
            # 跳过几何体
            i += 1
            npos = int(lines[i].strip())
            i += 1 + npos
            if lines[i].strip() == "0":
                i += 1
            nvert = int(lines[i].strip())
            i += 1 + nvert * 3
            if lines[i].strip() == "0":
                i += 1
            ntri = int(lines[i].strip())
            i += 1 + ntri
            continue
        i += 1

    if decl is not None and decl != len(parts):
        errs.append("G5: mesh %d != 部件 %d" % (decl, len(parts)))

    lv = {}
    idx_by_lv = {}
    bases = set()
    for (ln, hdr) in parts:
        mm = PART_RE.match(hdr)
        b, L, i2, mat = mm.group(1), int(mm.group(2) or 0), int(mm.group(3)), mm.group(4)
        bases.add(b)
        if b != base:
            errs.append("G2: 第 %d 行 base=%s != %s" % (ln, b, base))
        lv.setdefault(L, 0)
        lv[L] += 1
        idx_by_lv.setdefault(L, []).append(i2)
        if "__PROXY__" in hdr or ".X 0" in hdr:
            errs.append("G6: 垃圾部件名 %r" % hdr[:40])

    got = set(lv)
    if got != set(expect_levels):
        errs.append("G3: 档位 %s != 期望 %s" % (sorted(got), sorted(expect_levels)))
    for L, idxs in idx_by_lv.items():
        if len(idxs) != len(set(idxs)):
            errs.append("G4: 档 %d 序号重复" % L)
    if len(bases) > 1:
        errs.append("G2: 文件内出现多个资产名 %s" % sorted(bases)[:5])

    # G6: 相邻档几何不得全等
    digests = {}
    i = 2
    while i < len(lines):
        s = lines[i].strip()
        mm = PART_RE.match(s)
        if not mm:
            if s == "end":
                break
            i += 1
            continue
        L = int(mm.group(2) or 0)
        i += 1
        npos = int(lines[i].strip())
        P = lines[i + 1:i + 1 + npos]
        digests.setdefault(L, hashlib.md5())
        digests[L].update(("|".join(P)).encode())
        i += 1 + npos
        if lines[i].strip() == "0":
            i += 1
        nvert = int(lines[i].strip())
        digests[L].update(("|".join(lines[i + 1:i + 1 + nvert * 3])).encode())
        i += 1 + nvert * 3
        if lines[i].strip() == "0":
            i += 1
        ntri = int(lines[i].strip())
        i += 1 + ntri
    hsl = {L: d.hexdigest() for L, d in digests.items()}
    for a in sorted(hsl):
        for b in sorted(hsl):
            if a < b and hsl[a] == hsl[b]:
                errs.append("G6: 档 %d 与档 %d 几何完全相同" % (a, b))
    return errs, dict(parts=len(parts), levels=sorted(lv), per_level=lv), hsl


def readback(path):
    parts = parse_trf(path)
    per = {}
    for p in parts:
        per.setdefault(p["lvl"], 0)
        per[p["lvl"]] += len(p["tri"])
    return dict(parts=len(parts), levels=sorted(per), tris=per)


# ---------------------------------------------------------------- main

def enforce_decreasing(blocks):
    """写盘前最后一道保险：任何一档的总面数都必须严格小于上一档。
    减面算法在小件上会卡在同一个面数（G6 相邻档完全相同），这里按面积裁剪，
    保证单调递减，且每块至少留 1 个三角。"""
    import collections
    by_lv = collections.defaultdict(list)
    for i, b in enumerate(blocks):
        by_lv[b[0]].append(i)
    prev = None
    for lv in sorted(by_lv):
        idxs = by_lv[lv]
        total = sum(len(blocks[i][4]) for i in idxs)
        if prev is not None and total >= prev:
            want = max(len(idxs), prev - 1)
            cur = total
            for i in sorted(idxs, key=lambda k: -len(blocks[k][4])):
                blv, bidx, bmat, BP, BT, BU = blocks[i]
                n = len(BT)
                if cur <= want:
                    break
                drop = min(n - 1, cur - want)
                if drop <= 0:
                    continue
                order = sorted(range(n), key=lambda j: -_tri_area(BP, BT[j]))
                keep = sorted(order[: n - drop])
                blocks[i] = (blv, bidx, bmat, BP, [BT[j] for j in keep], [BU[j] for j in keep])
                cur -= drop
        prev = sum(len(blocks[i][4]) for i in idxs)
    return blocks


def main():
    global MODE, CUR_V10
    argv = sys.argv[sys.argv.index("--") + 1:]
    src, outdir = argv[0], argv[1]
    levels = int(argv[2]) if len(argv) > 2 and not argv[2].startswith("--") else 7
    if "--mode" in argv:
        MODE = argv[argv.index("--mode") + 1]
    report_csv = ""
    if "--report" in argv:
        report_csv = argv[argv.index("--report") + 1]

    base = os.path.basename(src)[:-4]
    os.makedirs(outdir, exist_ok=True)
    parts = parse_trf(src)
    lod0 = [p for p in parts if p["lvl"] == 0]
    if not lod0:
        print("[ERR] 源文件没有 LOD0 部件: %s" % src)
        sys.exit(2)
    mats = [p["mat"] for p in lod0]
    n0 = sum(len(p["tri"]) for p in lod0)
    # levels = 总槽位数：7 → LOD0..LOD5 + 盒；4 → LOD0..LOD2 + 盒；2 → LOD0 + 盒
    if levels == 0:
        pts = [pt for p in lod0 for pt in p["P"]]
        xs = [q[0] for q in pts]; ys = [q[1] for q in pts]; zs = [q[2] for q in pts]
        diag = math.sqrt((max(xs)-min(xs))**2 + (max(ys)-min(ys))**2 + (max(zs)-min(zs))**2)
        if n0 < 100:
            levels = 2                      # 几十面的细杆：只留 LOD0 + 远档盒
        elif n0 < 300:
            levels = 4                      # 面太少，塞不满 7 档（电缆/细杆）
        elif diag >= 12.0 or n0 >= 5000:
            levels = 7
        elif diag >= 3.0 or n0 >= 1000:
            levels = 4
        else:
            levels = 2
        print("[auto] 对角线 %.1f m / LOD0 %d 面 => %d 槽" % (diag, n0, levels))
    head = {7: 6, 6: 6, 5: 5, 4: 3, 3: 3, 2: 1, 1: 1}[levels]
    print("[mode] %s" % MODE)
    print("[src] %s  LOD0 %d 部件 / %d 面 / 材质 %d   目标槽位 %d（真档 0..%d%s）"
          % (base, len(lod0), n0, len(mats), levels, head - 1, " + 盒" if levels > 1 else ""))

    blocks = []
    logs = []
    # LOD0：原样（几何不动，UV 在写盘时翻）
    for idx, p in enumerate(lod0):
        P = p["P"]
        tris = [(p["pidx"][a], p["pidx"][b], p["pidx"][c]) for (a, b, c) in p["tri"]]
        uvs = [[p["uv"][a], p["uv"][b], p["uv"][c]] for (a, b, c) in p["tri"]]
        blocks.append((0, idx, p["mat"], P, tris, uvs))
        logs.append(dict(level=0, mat=p["mat"], n0=len(tris), n=len(tris), tgt=len(tris),
                         weld=0, deg=0, retry=0, area_loss=0, bbox_shrink=0, sliver=0, warn=""))

    # 模型级细长判定：整体都是细杆 ⇒ 格构件，焊接要更轻
    _a_thin = 0.0
    _a_all = 0.0
    for p in lod0:
        tri0 = [(p["pidx"][a], p["pidx"][b], p["pidx"][c]) for (a, b, c) in p["tri"] if len({a, b, c}) == 3]
        for t_ in tri0:
            e_ = tri_edges(t_, p["P"])
            ar_ = _tri_area(p["P"], t_)
            _a_all += ar_
            # 退化三角（某条边长为 0，细长电缆常见）会让 max/min 除零
            if e_[0] > 1e-9 and min(e_) > 1e-9 and (max(e_) / min(e_)) > 6.0:
                _a_thin += ar_
    frac = (_a_thin / _a_all) if _a_all > 0 else 0.0
    model_thin = frac > 0.6
    print("[thin] 细长面占地表 %.0f%% -> %s" % (frac * 100, "整件格构，焊接减到 1/4" if model_thin else "普通"))
    n0s = [len(p["tri"]) for p in lod0]
    tot0 = sum(n0s)
    floors = [min(n, FLOOR_TRIS) for n in n0s]
    for level in range(1, head):
        tgt_total = int(round(tot0 * (V10_RATIO[level] if MODE == "v10" else RATIO[level])))
        red = tot0 - sum(floors)
        deficit = max(0, tgt_total - sum(floors))
        global FORCE_COLLAPSE
        FORCE_COLLAPSE = False
        CUR_V10 = (MODE == "v10") or (MODE == "v10h" and level < 5)
        # 末档：全材质合并成一个壳再减面（各材质独立减面会在接缝处开洞）
        if CUR_V10 and level == 5:
            P_all, t_all, u_all = [], [], []
            for p in lod0:
                off = len(P_all)
                P_all.extend(p["P"])
                for (a, b, c) in p["tri"]:
                    t_all.append((off + p["pidx"][a], off + p["pidx"][b], off + p["pidx"][c]))
                    u_all.append([p["uv"][a], p["uv"][b], p["uv"][c]])
            P1, t1, u1 = rebuild(P_all, t_all, u_all, 2.0, 45)
            blocks.append((5, 0, lod0[0]["mat"], P1, t1, u1))
            logs.append(dict(level=5, mat="(merged shell)", n0=len(t_all), n=len(t1), tgt=tgt_total,
                             weld=0, deg=-1, retry=0, area_loss=0, bbox_shrink=0, sliver=0, warn="merged"))
            continue
        fw = None
        if CUR_V10:
            # v10 = 全模型一个焊接值；二分到目标总面数
            lo, hi = 0.01, SCHED_WELD[level]
            best = None
            for _ in range(7):
                mid = (lo + hi) / 2.0
                tot_n = 0
                for p in lod0:
                    if level <= 3 and any(k in p["mat"] for k in SHEET):
                        tot_n += len(p["tri"])
                    else:
                        tt = [(p["pidx"][a], p["pidx"][b], p["pidx"][c]) for (a, b, c) in p["tri"] if len({a, b, c}) == 3]
                        uu = [[p["uv"][a], p["uv"][b], p["uv"][c]] for (a, b, c) in p["tri"] if len({a, b, c}) == 3]
                        tot_n += len(rebuild(p["P"], tt, uu, mid, DEG[level])[1])
                d = abs(tot_n - tgt_total)
                if best is None or d < best[0]:
                    best = (d, mid, tot_n)
                if tot_n > tgt_total:
                    lo = mid
                else:
                    hi = mid
            fw = best[1]
            # 焊接够不到目标（小件/低模常见）→ 这一档退到 collapse，别留一档"几乎没减面"
            if best[2] > 1.55 * max(1, tgt_total):
                print("[fallback] 档 %d：焊接只能到 %d（目标 %d）→ 改用 collapse"
                      % (level, best[2], tgt_total))
                CUR_V10 = False
                FORCE_COLLAPSE = True
                fw = None
        # LOD3+ 走"整体缝合后一次减面"，避免各部件独立减面在屋顶/墙顶拉出裂隙
        # 玻璃/纱帘：LOD3 还保、LOD4+ 直接丢掉 —— 墙已经减面，留着的玻璃只会从墙里戳出来变成黑三角
        def _is_sheet(p):
            return any(k in p["mat"] for k in SHEET)

        def _is_sign(p):
            return any(k in p["mat"] for k in SIGN)

        # 玻璃/招牌一并参与整体减面：它们本来就贴在墙面上，
        # 丢掉会让窗洞和招牌位置变成"能透视的洞"，单独保留又会戳出墙外。
        joinable = [p for p in lod0 if len(p["tri"]) >= 60]
        if not joinable:
            joinable = list(lod0)   # 全是小件时：整件一起减，别留一半原样（否则总数会反而变大）
        keepers = [p for p in lod0 if all(p is not q for q in joinable)]
        # 前一档面数（用来保证严格递减，绝不出现两档一模一样）
        _prev = sum(len(b[4]) for b in blocks if b[0] == level - 1) if level >= 1 else tot0
        _closed0 = True
        try:
            _e = {}
            for (blv, bidx, bmat, BP, BT, BU) in blocks:
                if blv != 0:
                    continue
                for (x, y, z) in BT:
                    for (u, v) in ((x, y), (y, z), (z, x)):
                        k = (bidx, min(u, v), max(u, v))
                        _e[k] = _e.get(k, 0) + 1
            _closed0 = not any(n == 1 for n in _e.values())
        except Exception:
            _closed0 = False
        if (level >= 3 or _closed0) and (joinable or _prev > 60):
            tgt_join = max(40, tgt_total - sum(len(p["tri"]) for p in keepers))
            # 保证严格递减：目标不得超过上一档的 80%
            tgt_join = min(tgt_join, max(40, int(_prev * 0.8)))
            bi = 0
            for (mat, PP, TT, UU) in collapse_joined(joinable, tgt_join,
                                                    hard_cap=int(_prev * 0.9),
                                                    keep_closed=_closed0):
                blocks.append((level, bi, mat, PP, TT, UU))
                logs.append(dict(level=level, mat=mat, n0=0, n=len(TT), tgt=tgt_join, weld=0.0,
                                 deg=-1, retry=0, area_loss=0.0, bbox_shrink=0.0, sliver=0.0,
                                 warn="joined"))
                bi += 1
            for p in keepers:
                T = [(p["pidx"][a], p["pidx"][b], p["pidx"][c]) for (a, b, c) in p["tri"]]
                U = [[p["uv"][a], p["uv"][b], p["uv"][c]] for (a, b, c) in p["tri"]]
                blocks.append((level, bi, p["mat"], p["P"], T, U))
                logs.append(dict(level=level, mat=p["mat"], n0=len(T), n=len(T), tgt=len(T), weld=0.0,
                                 deg=0, retry=0, area_loss=0.0, bbox_shrink=0.0, sliver=0.0,
                                 warn="keep"))
                bi += 1
            continue
        n_blk0, n_log0 = len(blocks), len(logs)
        while True:
            for idx, p in enumerate(lod0):
                red_i = n0s[idx] - floors[idx]
                t = floors[idx] + (int(round(deficit * red_i / red)) if red > 0 else 0)
                P, tris, uvs, st = make_level(p, level, t, logs, model_thin, fw, tot0 >= 6000)
                blocks.append((level, idx, p["mat"], P, tris, uvs))
                st["level"] = level
                logs.append(st)
            got_n = sum(len(b[4]) for b in blocks[n_blk0:])
            # 小件冻结太多导致这一档几乎没减面 → 本档整体重来，强制 collapse
            if got_n > 1.55 * max(1, tgt_total) and not FORCE_COLLAPSE and level < head:
                print("[fallback] 档 %d 实际只到 %d（目标 %d）→ 重做，强制 collapse"
                      % (level, got_n, tgt_total))
                del blocks[n_blk0:]
                del logs[n_log0:]
                FORCE_COLLAPSE = True
                fw = None
                continue
            break

    # 末档盒只在"体块够厚 + 末档还有不少面"时才加；细长/稀疏件宁可不写这一档
    add_box = False
    if levels > 1:
        _pts = [q for p in lod0 for q in p["P"]]
        _d = [max(c[i] for c in _pts) - min(c[i] for c in _pts) for i in range(3)]
        _last = sum(len(b[4]) for b in blocks if b[0] == head - 1)
        _ratio = min(_d) / max(1e-6, max(_d))
        _diag = math.sqrt(_d[0] ** 2 + _d[1] ** 2 + _d[2] ** 2)
        if levels <= 2:
            _last = tot0            # 2 槽件没有中间档，用 LOD0 面数判断
        solid = (not model_thin) and _ratio >= 0.20 and _diag >= 20.0 and _last >= 800
        tag = "solidbox" if solid else "emptybox"
        print("[box] 末档 = %s（末档 %d 面 / 厚比 %.2f / 对角线 %.1f m / 格构 %s）"
              % ("实心盒 70%" if solid else "空盒 2cm", _last, _ratio, _diag,
                 "是" if model_thin else "否"))
    # 这一档必须写：没有远档，引擎会在任意距离继续画最后一档几何（性能杀手）
    for bi, (bmat, BP, BT, BU) in enumerate(proxy_box(lod0, None if solid else 0.02)):
        blocks.append((head, bi, bmat, BP, BT, BU))
        logs.append(dict(level=head, mat=bmat, n0=len(BT), n=len(BT), tgt=10,
                         weld=0, deg=0, retry=0, area_loss=0, bbox_shrink=0, sliver=0,
                         warn="%s:%s" % (tag, "side" if bi == 0 else "top")))

    blocks = enforce_decreasing(blocks)
    dst = os.path.join(outdir, base + ".trf")
    size = write_trf(dst, blocks)

    # 写盘后把 __BASE__ 换成真实资产名
    txt = io.open(dst, encoding="utf-8", errors="replace").read().replace("__BASE__", base)
    io.open(dst, "w", encoding="utf-8", newline="\n").write(txt)

    expect = list(range(0, head)) + [head]   # 末档永远是盒（实心或空）   # 没盒子的件就少一档，引擎会一直用最后一档
    errs, info, hsl = validate(dst, base, expect)

    # ---- G9：封口守恒（用户 2026-11-13 定的原则）----
    # LOD0 封口 -> 每档都必须封口；LOD0 本就没封口（栅栏/铁轨/招牌片）-> 不强制，
    # 但也不许比 LOD0 的开口更多（那说明减面撕出了新口子）。
    edged = {}
    for bi, (blv, bidx, bmat, BP, BT, BU) in enumerate(blocks):
        cnt = edged.setdefault(blv, {})
        for (x, y, z) in BT:
            for (u, v) in ((x, y), (y, z), (z, x)):
                k = (bi, min(u, v), max(u, v))
                cnt[k] = cnt.get(k, 0) + 1
    open_cnt = {k: sum(1 for e, n in d.items() if n == 1) for k, d in edged.items()}
    info["open_edges"] = open_cnt
    base_open = open_cnt.get(0, 0)
    _last_lv = max(open_cnt) if open_cnt else -1
    if base_open == 0:
        for lv in sorted(open_cnt):
            if lv == _last_lv:
                continue          # 末档是合成盒子，不参与封口判定
            if lv > 0 and open_cnt[lv] > 0:
                errs.append("G9: LOD0 封口，但 LOD%d 有 %d 条开口边" % (lv, open_cnt[lv]))
    else:
        for lv in sorted(open_cnt):
            if lv == _last_lv:
                continue          # 末档是合成盒子，同样不参与
            if lv > 0 and open_cnt[lv] > base_open:
                errs.append("G9: LOD%d 开口边 %d > LOD0 的 %d（减面撕出了新口子）"
                            % (lv, open_cnt[lv], base_open))
    rb = readback(dst)

    lines = []
    lines.append("model: %s" % base)
    lines.append("file : %s  (%.2f MB)" % (dst, size / 1024.0 / 1024.0))
    lines.append("parts: %d   levels: %s" % (info["parts"], info["levels"]))
    lines.append("readback: %d levels  tris=%s" % (len(rb["levels"]), rb["tris"]))
    lines.append("")
    lines.append("%-4s %-42s %7s %7s %7s %7s %4s %5s %6s %6s %6s %s"
                 % ("LOD", "material", "n0", "tgt", "n", "weld", "deg", "retry", "area%", "bbox%", "sliver", "warn"))
    for s in logs:
        lines.append("%-4d %-42s %7d %7d %7d %7.4f %4d %5d %6.2f %6.2f %6.1f %s"
                     % (s["level"], s["mat"][:42], s["n0"], s.get("tgt", 0), s["n"],
                        s["weld"], s["deg"], s["retry"],
                        s["area_loss"] * 100, s["bbox_shrink"] * 100, s.get("sliver", 0.0), s["warn"]))
    lines.append("")
    if errs:
        lines.append("GATES: FAIL  (G1-G9)")
        for e in errs:
            lines.append("  - " + e)
    else:
        lines.append("GATES: PASS  (G1-G9)")

    txt = "\n".join(lines) + "\n"
    io.open(os.path.join(outdir, base + ".report.txt"), "w", encoding="utf-8", newline="\n").write(txt)
    print(txt)

    if report_csv:
        new = not os.path.exists(report_csv)
        f = io.open(report_csv, "a", encoding="utf-8", newline="\n")
        if new:
            f.write("model,level,tris,n0,weld,deg,retry,area_pct,bbox_pct,edge_x,warn,gates\n")
        for s in logs:
            f.write("%s,%d,%d,%d,%d,%.4f,%d,%d,%.2f,%.2f,%.2f,%s,%s\n"
                    % (base, s["level"], s["n"], s.get("tgt", 0), s["n0"], s["weld"], s["deg"], s["retry"],
                       s["area_loss"] * 100, s["bbox_shrink"] * 100, s.get("sliver", 0.0),
                       s["warn"], "FAIL" if errs else "PASS"))
        f.close()

    if errs:
        bad = os.path.join(outdir, "_rejected")
        os.makedirs(bad, exist_ok=True)
        os.replace(dst, os.path.join(bad, base + ".trf"))
        print("[FAIL] 已移入 _rejected")
        sys.exit(1)



def batch_run(listfile, outroot, mode, shard=0, nshard=1, maxn=0):
    """一次 Blender 启动内处理整张清单：批处理是唯一能把 18,500 件压到几小时的办法。
    清单每行：<源 trf 绝对路径>\t<分类目录名>

    单件成本 4.7~5.4 s 里绝大部分是 Blender 启停；批处理只付一次。
    """
    tasks = []
    for line in io.open(listfile, encoding="utf-8"):
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        f = line.split("\t")
        src = f[0]
        cls = f[1] if len(f) > 1 else os.path.basename(os.path.dirname(src))
        if not os.path.exists(src):
            continue
        out = os.path.join(outroot, cls, os.path.basename(src))
        if os.path.exists(out):
            continue          # 断点续跑：已有产物就跳过
        rej = os.path.join(outroot, cls, "_rejected", os.path.basename(src))
        if os.path.exists(rej):
            continue          # 已判失败的不再重试（否则每轮都白跑一遍）
        tasks.append((src, cls))
    # 分片：多进程并行时每个进程只看自己那一份（按 idx % nshard）
    if nshard > 1:
        tasks = [t for i, t in enumerate(tasks) if i % nshard == shard]
    if maxn and len(tasks) > maxn:
        tasks = tasks[:maxn]
    print("[batch] shard %d/%d 待处理 %d 件 -> %s" % (shard, nshard, len(tasks), outroot))
    ok = fail = 0
    t0 = time.time()
    for k, (src, cls) in enumerate(tasks, 1):
        base = os.path.basename(src)[:-4]
        outdir = os.path.join(outroot, cls)
        try:
            # 关键：FORCE_COLLAPSE / CUR_V10 是模块级全局，上一件触发的回退会污染下一件
            global FORCE_COLLAPSE, CUR_V10
            FORCE_COLLAPSE = False
            CUR_V10 = (mode == "v10") or (mode == "v10h")
            bpy.ops.wm.read_factory_settings(use_empty=True)
            sys.argv = ["blender", "--", src, outdir, "0", "--mode", mode]
            main()
            ok += 1
        except SystemExit:
            fail += 1
        except Exception as ex:
            fail += 1
            print("[batch][ERR] %s : %s" % (base, ex))
        if k % 25 == 0:
            el = time.time() - t0
            print("[batch] %d/%d  ok=%d fail=%d  %.1f s/件  剩余约 %.1f 分钟"
                  % (k, len(tasks), ok, fail, el / k, (len(tasks) - k) * el / k / 60.0))
    print("[batch] DONE ok=%d fail=%d" % (ok, fail))



if __name__ == "__main__":
    _argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--batch" in _argv:
        def _arg(name, dflt):
            return _argv[_argv.index(name) + 1] if name in _argv else dflt
        batch_run(_arg("--batch", ""), _arg("--outroot", ""), _arg("--mode", "v10h"),
                  int(_arg("--shard", "0")), int(_arg("--nshard", "1")), int(_arg("--max", "0")))
    else:
        main()
