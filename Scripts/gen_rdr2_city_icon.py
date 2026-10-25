#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""圣丹尼斯大地图图标生成器（RDR2 据点图标试点）。

为什么不是"每栋房子减面"：
  圣丹尼斯的建筑是模块化拼的，单件小、数量上万，而且**约一半大件在模型库里没有网格**
  （解包侧只导了部分 archetype）。原版大地图的据点本来也是"体块拼出来的示意城"
  （10~15 场景单位宽、300~560 个件）。所以这里做体块化：一个据点 = 一个网格 + 一个材质。

两种取形方式（--mode）：
  buildings（默认）逐实例出盒子：有 bbox 的按真实尺寸/朝向，没有的按尺寸档给默认体块
  voxel            撒网格出盒子：先把实例撒到 N 米网格，再逐格出版块（更连续，但会糊掉街道）

输入：D:\\BrainMaker\\大镖客2解包分析\\（tiles / lib_native / heightmap/water_mask.png）
输出：Debug/RD2构造相关/icon_<code>/
  rdr2_m_<code>.mdl      RMDL1（给 tpaccli meshmake）
  atlas_d.png            2048² 图集（给 texreplace）
  atlas_manifest.json    texreplace 清单
  preview_top.png / preview_iso.png   离线自查图
  stats.json             规模/面数统计

生成物禁止手改；改参数/配色 = 改本脚本再重跑。
"""

import argparse
import csv
import io
import json
import math
import os

import numpy as np
from PIL import Image, ImageDraw

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
LWN = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
OUT_ROOT = os.path.join(LWN, "Debug", "RD2构造相关")

CROPS = {
    "sdn": (2350.0, 2870.0, -1420.0, -900.0),
}

EXCLUDE = ("cablemesh", "_rds_", "rds_", "road", "_glue", "glue_", "_dec_", "decal",
           "_vfx", "lightdummy", "_lh", "_ipl", "water_", "_water")

ATLAS_SIZE = 2048
ATLAS_COLS, ATLAS_ROWS = 8, 6
TILE = ATLAS_SIZE // ATLAS_COLS
PAD = 3


def load_lib():
    idx = json.load(io.open(os.path.join(RDR2, "assets/lib_native/index.json"), encoding="utf-8"))
    return {int(k, 16): v for k, v in idx["models"].items()}


def load_manifest():
    man = {}
    p = os.path.join(RDR2, "deliverable/handoff/model_manifest.csv")
    for r in csv.DictReader(io.open(p, encoding="utf-8-sig")):
        man[int(r["hash"], 16)] = r
    return man


def load_instances(x0, x1, y0, y1):
    rows = []
    tdir = os.path.join(RDR2, "export/world/continent/tiles")
    for name in sorted(os.listdir(tdir)):
        if not name.endswith(".json"):
            continue
        for row in json.load(io.open(os.path.join(tdir, name), encoding="utf-8")):
            h, x, y, z, qx, qy, qz, qw, sxy, sz, cls = row
            if x0 <= x <= x1 and y0 <= y <= y1:
                rows.append(dict(h=h & 0xffffffff, x=x, y=y, z=z, qx=qx, qy=qy, qz=qz, qw=qw,
                                 sxy=sxy or 1.0, sz=sz or 1.0, cls=cls))
    return rows


def load_water():
    p = os.path.join(RDR2, "deliverable/heightmap/water_mask.png")
    return np.asarray(Image.open(p).convert("L")) if os.path.isfile(p) else None


def on_water(wm, wx, wy):
    if wm is None:
        return False
    col = int((wx + 6844.0) / 4.0)
    row = int((3224.0 - wy) / 4.0)
    if row < 0 or col < 0 or row >= wm.shape[0] or col >= wm.shape[1]:
        return False
    return wm[row, col] > 128


def yaw_of(qx, qy, qz, qw):
    return math.atan2(2.0 * (qw * qz + qx * qy), 1.0 - 2.0 * (qy * qy + qz * qz))


_tex_cache = {}


def tex_avg_and_img(tex_id):
    if tex_id in _tex_cache:
        return _tex_cache[tex_id]
    p = os.path.join(RDR2, "assets/lib_native/tex", "%s.png" % tex_id)
    if not os.path.isfile(p):
        _tex_cache[tex_id] = (None, None)
        return (None, None)
    im = Image.open(p).convert("RGBA")
    a = np.asarray(im, dtype=np.float32)
    alpha = a[..., 3:4] / 255.0
    rgb = a[..., :3]
    w = float(alpha.sum())
    avg = (rgb * alpha).reshape(-1, 3).sum(0) / w if w > 1 else rgb.reshape(-1, 3).mean(0)
    bg = Image.new("RGBA", im.size, tuple(int(v) for v in avg) + (255,))
    flat = Image.alpha_composite(bg, im).convert("RGB")
    res = (tuple(int(v) for v in avg), flat)
    _tex_cache[tex_id] = res
    return res


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--code", default="sdn")
    ap.add_argument("--mode", default="buildings", choices=("buildings", "voxel"))
    ap.add_argument("--scale", type=float, default=1.0 / 29.0, help="场景单位/米")
    ap.add_argument("--vexag", type=float, default=1.6, help="高度夸张倍数")
    ap.add_argument("--cell", type=float, default=10.0, help="voxel 模式格边长（米）")
    ap.add_argument("--default-height", type=float, default=9.0)
    ap.add_argument("--max-height", type=float, default=40.0)
    ap.add_argument("--max-footprint", type=float, default=55.0)
    ap.add_argument("--clusters", type=int, default=ATLAS_COLS * ATLAS_ROWS)
    args = ap.parse_args()

    x0, x1, y0, y1 = CROPS[args.code]
    outdir = os.path.join(OUT_ROOT, "icon_%s" % args.code)
    os.makedirs(outdir, exist_ok=True)

    lib = load_lib()
    man = load_manifest()
    wm = load_water()
    inst = load_instances(x0, x1, y0, y1)
    print("instances in crop:", len(inst))

    # ── 1) 过滤 + 出体块 ─────────────────────────────────────────────
    dropped = dict(exclude=0, water=0, flat=0, small=0, huge=0)
    boxes = []
    cells = {}
    cell = args.cell

    for r in inst:
        m = lib.get(r["h"])
        mm = man.get(r["h"])
        nm = mm["name"] if mm else ""
        if nm and any(k in nm for k in EXCLUDE):
            dropped["exclude"] += 1
            continue
        if on_water(wm, r["x"], r["y"]):
            dropped["water"] += 1
            continue
        w = d = hgt = None
        tex = -1
        if m:
            b = m["b"]
            w = max(0.4, (b[3] - b[0]) * r["sxy"])
            d = max(0.4, (b[4] - b[1]) * r["sxy"])
            hgt = max(0.4, (b[5] - b[2]) * r["sz"])
            subs = m.get("s") or []
            if subs:
                tex = max(subs, key=lambda s: s[2])[0]
        if hgt is not None and hgt < 2.0:
            dropped["flat"] += 1
            continue
        if hgt is not None and max(w, d) > 4 * hgt and hgt < 6:
            dropped["flat"] += 1
            continue
        if w is not None and max(w, d) > args.max_footprint:
            dropped["huge"] += 1
            continue
        if not m and r["cls"] < 2:
            dropped["small"] += 1
            continue

        cols = []
        if tex is not None and tex >= 0:
            c, img = tex_avg_and_img(tex)
            if c is not None:
                cols.append(c)
            else:
                tex = -1

        if args.mode == "buildings":
            if not m:
                w = d = 14.0 if r["cls"] >= 3 else 8.0
                hgt = args.default_height if r["cls"] >= 3 else args.default_height * 0.7
            boxes.append(dict(cx=r["x"], cy=r["y"], z=r["z"], w=w, d=d,
                              h=min(hgt, args.max_height), ang=yaw_of(r["qx"], r["qy"], r["qz"], r["qw"]),
                              cols=cols, tex=tex, n=1))
        else:
            ix = int((r["x"] - x0) / cell)
            iy = int((y1 - r["y"]) / cell)
            c = cells.setdefault((ix, iy), dict(n=0, h=0.0, hz=0.0, cols=[], tex=-1))
            c["n"] += 1
            c["hz"] = max(c["hz"], r["z"])
            if m and hgt:
                c["h"] = max(c["h"], hgt)
            if cols:
                c["cols"].extend(cols)
            if tex is not None and tex >= 0 and c["tex"] < 0:
                c["tex"] = tex

    if args.mode == "voxel":
        cells = {k: v for k, v in cells.items() if v["n"] >= 2 or v["h"] > 0}
        keys = sorted(cells)
        for k in keys:
            c = cells[k]
            c["avg"] = tuple(np.mean(np.asarray(c["cols"], dtype=np.float32), axis=0)) if c["cols"] else None
        cols_known = [cells[k]["avg"] for k in keys if cells[k]["avg"] is not None]
        dcol = tuple(np.mean(np.asarray(cols_known, dtype=np.float32), axis=0)) if cols_known else (120, 110, 100)
        filled = {k: cells[k]["avg"] is not None for k in keys}
        for _ in range(4):
            todo = [k for k in keys if not filled[k]]
            if not todo:
                break
            new = {}
            for k in todo:
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        nk = (k[0] + dx, k[1] + dy)
                        if nk in cells and filled.get(nk):
                            new[k] = cells[nk]["avg"]
                            break
                    if k in new:
                        break
            for k, v in new.items():
                cells[k]["avg"] = v
                filled[k] = True
        for k in keys:
            if not filled[k]:
                cells[k]["avg"] = dcol
            cells[k]["h"] = min(cells[k]["h"] or args.default_height, args.max_height)

        def nb(k):
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    nk = (k[0] + dx, k[1] + dy)
                    if nk in cells:
                        yield nk

        for _ in range(2):
            upd = {k: float(np.median([cells[j]["h"] for j in nb(k)])) for k in keys}
            for k, v in upd.items():
                cells[k]["h"] = v
        upd = {k: tuple(np.mean(np.stack([np.asarray(cells[j]["avg"], dtype=np.float32) for j in nb(k)]), axis=0))
               for k in keys}
        for k, v in upd.items():
            cells[k]["avg"] = v

        for k in keys:
            c = cells[k]
            wx0 = x0 + k[0] * cell
            wy1 = y1 - k[1] * cell
            boxes.append(dict(cx=wx0 + cell * 0.5, cy=wy1 - cell * 0.5, z=c["hz"],
                              w=cell * 0.96, d=cell * 0.96, h=c["h"], ang=0.0,
                              cols=[c["avg"]], tex=c["tex"], n=c["n"]))

    print("boxes:", len(boxes), "dropped:", dropped)
    if not boxes:
        raise SystemExit("no boxes generated")

    # ── 2) 颜色聚类 → 图集 ───────────────────────────────────────────
    default_col = (122, 112, 100)
    for b in boxes:
        if not b["cols"]:
            b["cols"] = [default_col]
        b["avg"] = tuple(np.mean(np.asarray(b["cols"], dtype=np.float32), axis=0))
    colors = np.asarray([b["avg"] for b in boxes], dtype=np.float32)

    K = args.clusters
    luma = colors @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
    order = np.argsort(luma)
    cents = colors[order[np.linspace(0, len(order) - 1, K).astype(int)]].copy()
    for _ in range(18):
        d = ((colors[:, None, :] - cents[None, :, :]) ** 2).sum(2)
        lab = d.argmin(1)
        for k in range(K):
            sel = colors[lab == k]
            if len(sel):
                cents[k] = sel.mean(0)
    for b, c in zip(boxes, lab):
        b["cluster"] = int(c)
    print("clusters used:", len(set(int(c) for c in lab)))

    atlas = Image.new("RGB", (ATLAS_SIZE, ATLAS_SIZE), (110, 104, 96))
    dr = ImageDraw.Draw(atlas)
    members = {}
    for i, b in enumerate(boxes):
        members.setdefault(b["cluster"], []).append(i)
    for cid in range(K):
        cx = (cid % ATLAS_COLS) * TILE
        cy = (cid // ATLAS_COLS) * TILE
        if cy >= ATLAS_SIZE:
            continue
        best, bestd = None, 1e9
        for i in members.get(cid, []):
            tex = boxes[i]["tex"]
            if tex is None or tex < 0:
                continue
            col, img = tex_avg_and_img(tex)
            if img is None:
                continue
            dd = float(((np.asarray(col, dtype=np.float32) - cents[cid]) ** 2).sum())
            if dd < bestd:
                bestd, best = dd, img
        if best is not None:
            atlas.paste(best.resize((TILE - 2 * PAD, TILE - 2 * PAD), Image.LANCZOS),
                        (cx + PAD, cy + PAD))
        else:
            dr.rectangle([cx, cy, cx + TILE - 1, cy + TILE - 1], fill=tuple(int(v) for v in cents[cid]))
    atlas.save(os.path.join(outdir, "atlas_d.png"))
    print("atlas saved")

    # ── 3) 几何 ─────────────────────────────────────────────────────
    s, vx = args.scale, args.vexag
    zref = min(b["z"] for b in boxes)
    cxw, cyw = (x0 + x1) * 0.5, (y0 + y1) * 0.5

    pos, nrm, uv, idx = [], [], [], []

    def to_local(wx, wy, wz):
        # RDR2 X 东 / Y 北 / Z 上 → 场景 X 东 / Y 南 / Z 上（Y 取反）
        return ((wx - cxw) * s, -(wy - cyw) * s, (wz - zref) * s * vx + 0.02)

    def add_quad(quad, u0, v0, u1, v1):
        base = len(pos)
        for p in quad:
            pos.append(p)
        q = [np.asarray(p, dtype=np.float64) for p in quad]
        n = np.cross(q[1] - q[0], q[2] - q[0])
        ln = np.linalg.norm(n)
        n = n / ln if ln > 1e-9 else np.array([0.0, 0.0, 1.0])
        for _ in range(4):
            nrm.append(tuple(float(v) for v in n))
        uv.extend([(u0, v0), (u1, v0), (u1, v1), (u0, v1)])
        idx.extend([(base, base + 1, base + 2), (base, base + 2, base + 3)])

    for b in boxes:
        ca, sa = math.cos(b["ang"]), math.sin(b["ang"])
        hw, hd = b["w"] * 0.5, b["d"] * 0.5
        corners = []
        for dx, dy in ((-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd)):
            corners.append((b["cx"] + dx * ca - dy * sa, b["cy"] + dx * sa + dy * ca))
        bx = [to_local(wx, wy, b["z"]) for wx, wy in corners]
        tz = b["h"] * s * vx
        tx = [(p[0], p[1], p[2] + tz) for p in bx]
        center = tuple(np.mean(np.asarray(bx + tx, dtype=np.float32), axis=0))

        cid = b["cluster"]
        u0 = (cid % ATLAS_COLS) * TILE / ATLAS_SIZE + 2.0 / ATLAS_SIZE
        v0 = (cid // ATLAS_COLS) * TILE / ATLAS_SIZE + 2.0 / ATLAS_SIZE
        u1 = (cid % ATLAS_COLS + 1) * TILE / ATLAS_SIZE - 2.0 / ATLAS_SIZE
        v1 = (cid // ATLAS_COLS + 1) * TILE / ATLAS_SIZE - 2.0 / ATLAS_SIZE

        quads = [
            (bx[0], bx[1], tx[1], tx[0]),
            (bx[1], bx[2], tx[2], tx[1]),
            (bx[2], bx[3], tx[3], tx[2]),
            (bx[3], bx[0], tx[0], tx[3]),
            (tx[0], tx[1], tx[2], tx[3]),
        ]
        for quad in quads:
            q = [np.asarray(p, dtype=np.float64) for p in quad]
            n = np.cross(q[1] - q[0], q[2] - q[0])
            ln = np.linalg.norm(n)
            n = n / ln if ln > 1e-9 else np.array([0.0, 0.0, 1.0])
            fc = (q[0] + q[1] + q[2] + q[3]) / 4.0
            if float(np.dot(n, fc - np.asarray(center))) < 0:
                quad = tuple(reversed(quad))
            add_quad(quad, u0, v0, u1, v1)

    V, T = len(pos), len(idx)
    print("verts", V, "tris", T)

    pos_a = np.asarray(pos, dtype="<f4")
    nrm_a = np.asarray(nrm, dtype="<f4")
    uv_a = np.asarray(uv, dtype="<f4")
    idx_a = np.asarray(idx, dtype="<i4").reshape(-1)
    mn, mx = pos_a.min(0), pos_a.max(0)

    off = 12 + 24 + 28
    posOff, nrmOff = off, off + pos_a.nbytes
    uvOff = nrmOff + nrm_a.nbytes
    idxOff = uvOff + uv_a.nbytes
    buf = io.BytesIO()
    buf.write(b"RMDL1\0\0\0")
    buf.write(np.asarray([1], dtype="<i4").tobytes())
    buf.write(np.asarray(list(mn) + list(mx), dtype="<f4").tobytes())
    buf.write(np.asarray([-1, V, T * 3, posOff, nrmOff, uvOff, idxOff], dtype="<i4").tobytes())
    buf.write(pos_a.tobytes())
    buf.write(nrm_a.tobytes())
    buf.write(uv_a.tobytes())
    buf.write(idx_a.tobytes())
    mdl_path = os.path.join(outdir, "rdr2_m_%s.mdl" % args.code)
    open(mdl_path, "wb").write(buf.getvalue())
    print("wrote", mdl_path, os.path.getsize(mdl_path), "bytes")

    manifest = {"packs": [{
        "packName": "rdr2_city_atlas",
        "textures": [{
            "name": "rdr2_citymap_icon_empire_city_01_d",
            "png": os.path.join(outdir, "atlas_d.png").replace("\\", "/"),
            "width": ATLAS_SIZE,
            "height": ATLAS_SIZE,
        }],
    }]}
    io.open(os.path.join(outdir, "atlas_manifest.json"), "w", encoding="utf-8").write(
        json.dumps(manifest, indent=2))

    stats = {
        "code": args.code, "mode": args.mode,
        "crop_world_m": [x0, x1, y0, y1], "cell_m": cell,
        "scale_units_per_m": s, "vexag": vx,
        "icon_span_units": [(x1 - x0) * s, (y1 - y0) * s],
        "height_units": float(mx[2] - mn[2]),
        "boxes": len(boxes), "dropped": dropped,
        "verts": V, "tris": T,
        "clusters_used": len(set(int(c) for c in lab)),
        "bbox_local": [float(v) for v in list(mn) + list(mx)],
    }
    io.open(os.path.join(outdir, "stats.json"), "w", encoding="utf-8").write(
        json.dumps(stats, indent=2, ensure_ascii=False))
    print(json.dumps(stats, indent=2, ensure_ascii=False))

    # ── 4) 预览 ─────────────────────────────────────────────────────
    span_x, span_y = (x1 - x0) * s, (y1 - y0) * s
    PW = 900
    sc = PW / span_x
    top = Image.new("RGB", (PW + 20, int(span_y * sc) + 20), (24, 26, 30))
    d2 = ImageDraw.Draw(top)
    for b in sorted(boxes, key=lambda b: b["z"]):
        ca, sa = math.cos(b["ang"]), math.sin(b["ang"])
        hw, hd = b["w"] * 0.5, b["d"] * 0.5
        pts = []
        for dx, dy in ((-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd)):
            lx, ly, _ = to_local(b["cx"] + dx * ca - dy * sa, b["cy"] + dx * sa + dy * ca, b["z"])
            pts.append((10 + (lx - mn[0]) * sc, 10 + (ly - mn[1]) * sc))
        col = tuple(int(v) for v in np.asarray(b["avg"]) * (0.45 + 0.55 * min(1.0, b["h"] / 22.0)))
        d2.polygon(pts, fill=col)
    top.save(os.path.join(outdir, "preview_top.png"))

    W2, H2 = 1000, 700
    iso = Image.new("RGB", (W2, H2), (26, 28, 32))
    d3 = ImageDraw.Draw(iso)
    ang = math.radians(35)
    ca, sa = math.cos(ang), math.sin(ang)
    sc_iso = 34.0

    def iso_pt(x, y, z):
        return (W2 * 0.5 + (x * ca - y * sa) * sc_iso,
                H2 * 0.70 - (x * sa + y * ca) * sc_iso - z * sc_iso * 1.2)

    for b in sorted(boxes, key=lambda b: -(b["cy"] * ca - b["cx"] * sa)):
        ca2, sa2 = math.cos(b["ang"]), math.sin(b["ang"])
        hw, hd = b["w"] * 0.5, b["d"] * 0.5
        bx = []
        for dx, dy in ((-hw, -hd), (hw, -hd), (hw, hd), (-hw, hd)):
            lx, ly, _ = to_local(b["cx"] + dx * ca2 - dy * sa2, b["cy"] + dx * sa2 + dy * ca2, b["z"])
            bx.append((lx, ly))
        tz = b["h"] * s * vx
        base = [iso_pt(p[0], p[1], 0.0) for p in bx]
        topq = [iso_pt(p[0], p[1], tz) for p in bx]
        shade = 0.45 + 0.55 * min(1.0, b["h"] / 20.0)
        col = tuple(int(min(255, v * shade)) for v in b["avg"])
        d3.polygon(topq, fill=col)
        d3.polygon([base[0], base[1], topq[1], topq[0]], fill=tuple(int(v * 0.68) for v in col))
        d3.polygon([base[1], base[2], topq[2], topq[1]], fill=tuple(int(v * 0.52) for v in col))
    iso.save(os.path.join(outdir, "preview_iso.png"))
    print("previews saved")


if __name__ == "__main__":
    main()
