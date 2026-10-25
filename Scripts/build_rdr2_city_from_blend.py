#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""从 continent.blend 抽圣丹尼斯 → 减面 → 图集 UV → OBJ → RMDL（大地图缩略模型）。

为什么走 Blender：rnd 里的模型、材质、贴图、位置都在 `continent.blend` 里现成摆好了，
而且物体名就是模型哈希（e_5138eda2 ↔ assets/lib_native/index.json），尺寸/贴图都能对上。

流程：
  1. 统计裁切区内每个模型的主贴图 → 选 47 张最常用的做 2048² 图集（第 48 格是兜底色）
  2. 写 {物体名: 图集格} 映射
  3. Blender 无头：选件 → 逐件减面到总预算 → UV 重映射进图集格 → 合并 → 导出 OBJ
  4. OBJ → RMDL1（乘 1/29，Z 乘夸张系数）
  5. 渲染预览图

用法：
  python Scripts/build_rdr2_city_from_blend.py [--budget 250000] [--vexag 1.5]
"""

import argparse
import csv
import json
import os
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from build_progress import Progress  # noqa: E402

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
MODULES = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules"
LWN = os.path.join(MODULES, "LivingWorldNpcs")
BLEND = os.path.join(RDR2, "deliverable", "blender", "continent", "continent.blend")
OUT = os.path.join(LWN, "Debug", "RD2构造相关", "icon_blend")
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"

CROP = (2330.0, 2900.0, -1450.0, -880.0)
ATLAS_SIZE = 4096
COLS, ROWS = 32, 32   # 128²/格 = 1024 格（城市用到 740 种贴图，必须全覆盖）
TILE = ATLAS_SIZE // COLS
PAD = 3
SCALE = 1.0 / 29.0


def load_manifest():
    man = {}
    p = os.path.join(RDR2, "deliverable/handoff/model_manifest.csv")
    for r in csv.DictReader(open(p, encoding="utf-8-sig")):
        man[int(r["hash"], 16)] = r
    return man


JUNK = ("cablemesh", "_rds_", "rds_", "road", "_glue", "glue_", "_dec_", "decal",
        "_vfx", "lightdummy", "_lh", "_ipl")


def load_lib():
    idx = json.load(open(os.path.join(RDR2, "assets/lib_native/index.json"), encoding="utf-8"))
    return {int(k, 16): v for k, v in idx["models"].items()}


def load_tiles():
    rows = []
    tdir = os.path.join(RDR2, "export/world/continent/tiles")
    x0, x1, y0, y1 = CROP
    for name in sorted(os.listdir(tdir)):
        if not name.endswith(".json"):
            continue
        for row in json.load(open(os.path.join(tdir, name), encoding="utf-8")):
            h, x, y, z, qx, qy, qz, qw, sxy, sz, cls = row
            if x0 <= x <= x1 and y0 <= y <= y1:
                rows.append((h & 0xffffffff, cls))
    return rows


_tex_cache = {}


def tex_img(tex_id):
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


def build_atlas(lib):
    """按实例频次选 47 张贴图做图集；返回 (tilemap, atlas_path)。"""
    inst = load_tiles()
    tex_of = {}
    for h, _cls in inst:
        v = lib.get(h)
        if not v:
            continue
        subs = v.get("s") or []
        if not subs:
            continue
        tex_of[h] = max(subs, key=lambda s: s[2])[0]

    usage = {}
    for h, _cls in inst:
        t = tex_of.get(h)
        if t is not None and t >= 0:
            usage[t] = usage.get(t, 0) + 1
    top = [t for t, _ in sorted(usage.items(), key=lambda kv: -kv[1])][:COLS * ROWS - 1]

    atlas = Image.new("RGB", (ATLAS_SIZE, ATLAS_SIZE), (110, 104, 96))
    dr = ImageDraw.Draw(atlas)
    tile_of_tex = {}
    for i, t in enumerate(top):
        col, img = tex_img(t)
        cx = (i % COLS) * TILE
        cy = (i // COLS) * TILE
        if img is not None:
            atlas.paste(img.resize((TILE - 2 * PAD, TILE - 2 * PAD), Image.LANCZOS),
                        (cx + PAD, cy + PAD))
            tile_of_tex[t] = i
        else:
            dr.rectangle([cx, cy, cx + TILE - 1, cy + TILE - 1],
                         fill=tuple(int(v) for v in (col or (110, 104, 96))))
            tile_of_tex[t] = i
    os.makedirs(OUT, exist_ok=True)
    atlas_path = os.path.join(OUT, "atlas_d.png")
    atlas.save(atlas_path)

    fallback = COLS * ROWS - 1
    fx = (fallback % COLS) * TILE
    fy = (fallback // COLS) * TILE
    dr.rectangle([fx, fy, fx + TILE - 1, fy + TILE - 1], fill=(120, 112, 100))
    atlas.save(atlas_path)

    tilemap = {}
    for h in {h for h, _ in inst}:
        t = tex_of.get(h)
        tilemap["e_%08x" % h] = tile_of_tex.get(t, fallback) if t is not None and t >= 0 else fallback
    print("atlas tiles used:", len(tile_of_tex), "objects mapped:", len(tilemap))
    return tilemap, atlas_path


def obj_to_rmdl(obj_path, mdl_path, scale, vexag):
    verts = []
    uvs = []
    faces = []
    for line in open(obj_path, encoding="utf-8", errors="ignore"):
        if line.startswith("v "):
            parts = line.split()
            verts.append((float(parts[1]), float(parts[2]), float(parts[3])))
        elif line.startswith("vt "):
            parts = line.split()
            uvs.append((float(parts[1]), float(parts[2])))
        elif line.startswith("f "):
            parts = line.split()[1:]
            idx = []
            for p in parts:
                seg = p.split("/")
                vi = int(seg[0]) - 1
                ti = int(seg[1]) - 1 if len(seg) > 1 and seg[1] else -1
                idx.append((vi, ti))
            if len(idx) == 3:
                faces.append(idx)
            elif len(idx) > 3:
                for i in range(1, len(idx) - 1):
                    faces.append([idx[0], idx[i], idx[i + 1]])
    print("obj verts", len(verts), "uvs", len(uvs), "faces", len(faces))

    vmap = {}
    P, U, I = [], [], []
    for tri in faces:
        out = []
        for vi, ti in tri:
            key = (vi, ti)
            k = vmap.get(key)
            if k is None:
                k = len(P)
                vmap[key] = k
                x, y, z = verts[vi]
                P.append((x * scale, y * scale, z * scale * vexag))
                if 0 <= ti < len(uvs):
                    U.append(uvs[ti])
                else:
                    U.append((0.0, 0.0))
            out.append(k)
        I.append(tuple(out))

    pa = np.asarray(P, dtype="<f4")
    # 法线：由面法线平均（引擎那边 meshmake 只吃位置/法线/UV）
    na = np.zeros((len(P), 3), dtype="<f4")
    for tri in I:
        a, b, c = (np.asarray(P[i], dtype=np.float64) for i in tri)
        n = np.cross(b - a, c - a)
        ln = np.linalg.norm(n)
        if ln > 1e-9:
            na[tri[0]] += n / ln
            na[tri[1]] += n / ln
            na[tri[2]] += n / ln
    ln = np.linalg.norm(na, axis=1, keepdims=True)
    ln[ln < 1e-9] = 1.0
    na = (na / ln).astype("<f4")
    ua = np.asarray(U, dtype="<f4")
    ia = np.asarray(I, dtype="<i4").reshape(-1)
    mn, mx = pa.min(0), pa.max(0)

    off = 12 + 24 + 28
    posOff, nrmOff = off, off + pa.nbytes
    uvOff = nrmOff + na.nbytes
    idxOff = uvOff + ua.nbytes
    buf = bytearray()
    buf += b"RMDL1\0\0\0"
    buf += np.asarray([1], dtype="<i4").tobytes()
    buf += np.asarray(list(mn) + list(mx), dtype="<f4").tobytes()
    buf += np.asarray([-1, len(P), len(I) * 3, posOff, nrmOff, uvOff, idxOff], dtype="<i4").tobytes()
    buf += pa.tobytes() + na.tobytes() + ua.tobytes() + ia.tobytes()
    open(mdl_path, "wb").write(bytes(buf))
    print("wrote", mdl_path, os.path.getsize(mdl_path), "bytes",
          "bbox", list(np.round(mn, 2)), list(np.round(mx, 2)))



def meshbin_to_rmdl(bin_path, mdl_path, scale, vexag):
    """Blender 导出的二进制网格 → RMDL1（全程 numpy，不做逐面 Python 循环）。"""
    data = io.open(bin_path, "rb").read() if False else open(bin_path, "rb").read()
    assert data[:5] == b"MESH1", "not MESH1"
    n, m = np.frombuffer(data, dtype="<i4", count=2, offset=8)
    off = 16
    co = np.frombuffer(data, dtype="<f4", count=int(n) * 3, offset=off).reshape(-1, 3).astype(np.float32)
    off += int(n) * 12
    vidx = np.frombuffer(data, dtype="<i4", count=int(m), offset=off).astype(np.int64)
    off += int(m) * 4
    uv = np.frombuffer(data, dtype="<f4", count=int(m) * 2, offset=off).reshape(-1, 2).astype(np.float32)
    # 稳健基面：取 2% 分位数当"地面"（个别埋地/超高的离群件不能带偏整座城），
    # 再把高度钳到 0~60 米（地图尺度上看不出，但能保证图标高度合理）。
    base = float(np.percentile(co[:, 2], 2))
    co[:, 2] = np.clip(co[:, 2] - base, 0.0, 60.0)
    co = co * np.array([scale, scale, scale * vexag], dtype=np.float32)

    # 顶点去重：键 = (原始顶点号, UV 量化到 1e-4)
    key = np.empty((int(m), 3), dtype=np.int64)
    key[:, 0] = vidx
    key[:, 1] = np.round(uv[:, 0].astype(np.float64) * 10000).astype(np.int64)
    key[:, 2] = np.round(uv[:, 1].astype(np.float64) * 10000).astype(np.int64)
    uniq, inv = np.unique(key, axis=0, return_inverse=True)
    new_vidx = uniq[:, 0]
    new_uv = (uniq[:, 1:] / 10000.0).astype(np.float32)
    faces = inv.reshape(-1, 3).astype(np.int32)
    # 丢掉退化面
    ok = (faces[:, 0] != faces[:, 1]) & (faces[:, 1] != faces[:, 2]) & (faces[:, 0] != faces[:, 2])
    faces = faces[ok]

    P = co[new_vidx]
    U = new_uv
    # 法线：面法线累加到顶点
    a, b, c = P[faces[:, 0]], P[faces[:, 1]], P[faces[:, 2]]
    fn = np.cross(b - a, c - a)
    N = np.zeros_like(P)
    for i in range(3):
        np.add.at(N, faces[:, i], fn)
    ln = np.linalg.norm(N, axis=1, keepdims=True)
    ln[ln < 1e-9] = 1.0
    N = (N / ln).astype(np.float32)

    print("meshbin: verts", int(n), "loops", int(m), "-> uniq", len(P), "tris", len(faces))
    if len(faces) > 6_000_000:
        print("WARN: 面数仍然偏高（预算 30 万）")

    mn, mx = P.min(0), P.max(0)
    pos_a, nrm_a, uv_a = P.astype("<f4"), N.astype("<f4"), U.astype("<f4")
    idx_a = faces.astype("<i4").reshape(-1)
    off2 = 12 + 24 + 28
    posOff, nrmOff = off2, off2 + pos_a.nbytes
    uvOff = nrmOff + nrm_a.nbytes
    idxOff = uvOff + uv_a.nbytes
    buf = bytearray()
    buf += b"RMDL1\0\0\0"
    buf += np.asarray([1], dtype="<i4").tobytes()
    buf += np.asarray(list(mn) + list(mx), dtype="<f4").tobytes()
    buf += np.asarray([-1, len(P), len(faces) * 3, posOff, nrmOff, uvOff, idxOff], dtype="<i4").tobytes()
    buf += pos_a.tobytes() + nrm_a.tobytes() + uv_a.tobytes() + idx_a.tobytes()
    open(mdl_path, "wb").write(bytes(buf))
    print("wrote", mdl_path, os.path.getsize(mdl_path), "bytes",
          "bbox", [round(float(v), 2) for v in list(mn) + list(mx)])

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--budget", type=int, default=250000)
    ap.add_argument("--vexag", type=float, default=1.5)
    ap.add_argument("--skip-blender", action="store_true")
    args = ap.parse_args()

    os.makedirs(OUT, exist_ok=True)
    prog = Progress(os.path.join(OUT, "progress.html"), "圣丹尼斯 · 大地图图标（真实模型版）")
    prog.update("读模型库 + 统计贴图", extra="进度页：%s" % os.path.join(OUT, "progress.html"))
    lib = load_lib()
    man = load_manifest()
    tilemap, atlas_path = build_atlas(lib)
    tilemap_path = os.path.join(OUT, "hash_tile.json")
    # 图集每格的平均色（0..1），给 Blender 侧"按材质贴图颜色找最近格"用
    _sm = Image.open(atlas_path).resize((COLS, ROWS), Image.BOX).convert("RGB")
    _cols = [[float(v) / 255.0 for v in _sm.getpixel((i % COLS, i // COLS))]
             for i in range(COLS * ROWS)]
    open(os.path.join(OUT, "tile_colors.json"), "w", encoding="utf-8").write(json.dumps(_cols))
    json.dump(tilemap, open(tilemap_path, "w", encoding="utf-8"))

    # 垃圾件按“模型身份”剔（电线/路面/地面/贴花）——不按尺寸一刀切，否则会误伤 60~100 米的大地标
    excl = set()
    for h, _cls in load_tiles():
        nm = (man.get(h) or {}).get("name", "")
        if nm and any(k in nm for k in JUNK):
            excl.add("e_%08x" % h)
    print("excluded objects (by name):", len(excl))

    obj_path = os.path.join(OUT, "sdn_raw.meshbin")
    cfg_path = os.path.join(OUT, "cfg.json")
    cfg = {
        "crop": list(CROP),
        "budget_tris": args.budget,
        "min_verts": 40,
        "max_footprint": 120.0,
        "min_footprint": 1.2,
        "min_height": 2.5,
        "exclude_objects": sorted(excl),
        "tilemap": tilemap_path.replace("\\", "/"),
        "atlas_grid": [COLS, ROWS],
        "atlas_pad_px": PAD,
        "atlas_size": ATLAS_SIZE,
        "atlas_png": atlas_path.replace("\\", "/"),
        "tile_colors": os.path.join(OUT, "tile_colors.json").replace("\\", "/"),
        "out_obj": obj_path.replace("\\", "/"),
    }
    json.dump(cfg, open(cfg_path, "w", encoding="utf-8"), indent=2)

    if not args.skip_blender:
        script = os.path.join(LWN, "Debug", "RD2构造相关", "blender", "build_blend_icon.py")
        prog.update("Blender：打开 continent.blend（8.4 GB，约 1 分钟）", extra="这一步没有任何输出是正常的")
        proc = subprocess.Popen([BLENDER, "-b", BLEND, "--python", script, "--", cfg_path],
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                text=True, encoding="utf-8", errors="replace", bufsize=1)
        for line in proc.stdout:
            line = line.rstrip()
            if not line:
                continue
            print(line, flush=True)
            if "SELECT {" in line:
                prog.update("选件完成", extra=line[line.index("{"):][:160])
            elif "scene trimmed" in line:
                prog.update("场景瘦身完成", extra=line)
            elif "decimate+uv done" in line:
                prog.update("UV 重映射进图集完成", extra=line)
            elif line.startswith("cells:"):
                n = line.split("cells:")[1].split()[0]
                prog.update("分块减面", done=0, total=int(n), extra=line)
            elif line.startswith("cell "):
                parts = line.split()
                try:
                    done_i = int(parts[1])
                    tot_i = int(parts[3])
                    prog.update("分块减面", done=done_i, total=tot_i,
                                extra=" ".join(parts[4:]))
                except (ValueError, IndexError):
                    prog.log(line)
            elif line.startswith("EXPORTED"):
                prog.update("导出 OBJ 完成", extra=line)
            elif line.startswith("processed groups"):
                parts = line.split()
                try:
                    prog.update("按网格组减面 + UV 重映射", done=int(parts[2]), total=int(parts[4]),
                                extra="objects " + parts[6])
                except (ValueError, IndexError):
                    prog.log(line)
            elif line.startswith("processed "):
                parts = line.split()
                try:
                    prog.update("逐件减面 + UV 重映射", done=int(parts[1]), total=int(parts[3]),
                                extra=" ".join(parts[4:]))
                except (ValueError, IndexError):
                    prog.log(line)
            elif line.startswith(("joined verts", "total_tris", "decimate+uv")):
                prog.log(line)
        proc.wait()
        if proc.returncode != 0:
            prog.update("❌ Blender 失败", extra="见 build.log")
            sys.exit("blender failed (see build.log)")

    prog.update("生成 RMDL（缩放 1/29、高度 ×%.1f）" % args.vexag)
    mdl_path = os.path.join(OUT, "rdr2_m_sdn.mdl")
    meshbin_to_rmdl(obj_path, mdl_path, SCALE, args.vexag)

    prog.update("渲染预览")
    # 预览渲染（Blender，用图集材质）—— 预览直接读最终 RMDL，保证"看到的就是要发的"
    prev_obj = os.path.join(OUT, "sdn_preview.obj")
    subprocess.run([sys.executable, os.path.join(HERE, "rmdl_to_obj.py"), mdl_path, prev_obj])
    render = os.path.join(LWN, "Debug", "RD2构造相关", "blender", "render_icon.py")
    out_png = os.path.join(OUT, "preview_render.png")
    subprocess.run([BLENDER, "-b", "--factory-startup", "--python", render, "--",
                    prev_obj, atlas_path, out_png])
    prog.update("✅ 完成", done=1, total=1, extra=out_png)
    print("preview:", out_png)


if __name__ == "__main__":
    main()
