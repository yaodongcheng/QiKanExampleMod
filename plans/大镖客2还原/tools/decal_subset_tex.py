# -*- coding: utf-8 -*-
"""达标（朝上>=95%）子集的贴图成本 + 减面潜力试算。"""
import collections
import csv
import io
import os

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
M = os.path.join(RDR2, "deliverable", "manifests")
DEC = r"H:\SteamLibrary\apps\common\x"  # placeholder
DEC = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2\AssetSources\decal_models"
TEXDIRS = {k: os.path.join(RDR2, "deliverable", "textures", k) for k in ("alb", "nm", "ma")}


def load(n):
    with io.open(os.path.join(M, n), encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


# 复用前一步算出的达标名单：重新快速算一遍朝上比例（只读法线）
import numpy as np
HDR = None


def up_ratio(path):
    with io.open(path, encoding="utf-8", errors="replace") as f:
        lines = f.read().split("\n")
    i, n = 0, len(lines)
    vtot = up9 = 0
    while i < n:
        s = lines[i].strip()
        if not s:
            i += 1; continue
        if s.startswith("rfver") or s.startswith("mesh ") or s == "end":
            i += 1; continue
        tok = s.split()
        if len(tok) != 3 or tok[1] != "0":
            break
        i += 1
        npos = int(lines[i].strip()); i += 1
        i += npos + 1
        nv = int(lines[i].strip()); i += 1
        blk = np.fromstring(" ".join(lines[i:i + nv * 3]), sep=" ")
        if blk.size != nv * 9:
            break
        blk = blk.reshape(nv, 9)
        vtot += nv
        up9 += int((blk[:, 4] > 0.9).sum())
        i += nv * 3
        b = int(lines[i].strip()); i += 1
        if b > 0:
            i += 1; w = int(lines[i].strip()); i += 1; i += w
        nt = int(lines[i].strip()); i += 1
        i += nt
    return (up9 / float(vtot)) if vtot else 0.0


files = []
for root, dirs, fs in os.walk(DEC):
    for f in fs:
        if f.lower().endswith(".trf"):
            files.append(os.path.join(root, f))
print("复算 %d 个文件的朝上比例..." % len(files))
elig = []
for p in files:
    try:
        if up_ratio(p) >= 0.95:
            elig.append(p)
    except Exception:
        pass
print("达标 %d 个, %.1f MB" % (len(elig), sum(os.path.getsize(p) for p in elig) / 1024 ** 2))

# 材质
mm = load("model_materials.csv")
mats_of = collections.defaultdict(set)
for r in mm:
    mats_of[(r.get("model") or "").lower()].add((r.get("material") or "").lower())
mats = set()
for p in elig:
    n = os.path.splitext(os.path.basename(p))[0].lower()
    if mats_of.get(n):
        mats |= mats_of[n]
print("涉及材质 %d 个" % len(mats))

mt = {(r["material"] or "").lower(): ((r.get("albedo") or "").lower(),
                                      (r.get("normal") or "").lower(),
                                      (r.get("ma") or "").lower())
      for r in load("material_textures.csv")}
want = set()
for m in mats:
    t = mt.get(m)
    if not t:
        continue
    for k, v in zip(("alb", "nm", "ma"), t):
        if v:
            want.add((k, v))

tot = 0
byk = collections.Counter()
cnt = collections.Counter()
for k, v in want:
    p = os.path.join(TEXDIRS[k], v if v.endswith(".png") else v + ".png")
    if os.path.isfile(p):
        s = os.path.getsize(p)
        tot += s
        byk[k] += s
        cnt[k] += 1
print("\n=== 达标子集贴图 ===")
print("  去重 %d 张（alb %d / nm %d / ma %d）" % (len(want), cnt["alb"], cnt["nm"], cnt["ma"]))
print("  PNG 合计 %.1f MB (alb %.1f / nm %.1f / ma %.1f)"
      % (tot / 1024 ** 2, byk["alb"] / 1024 ** 2, byk["nm"] / 1024 ** 2, byk["ma"] / 1024 ** 2))

# 减面潜力：这些网格顶点密度极高（面/顶点 ≈ 1）
tv = 0
tf = 0
for p in elig:
    with io.open(p, encoding="utf-8", errors="replace") as f:
        txt = f.read()
    # 只统计 mesh 头里的 ntri 与 nvert
    lines = txt.split("\n")
    i, n = 0, len(lines)
    while i < n:
        s = lines[i].strip()
        if not s:
            i += 1; continue
        if s.startswith("rfver") or s.startswith("mesh ") or s == "end":
            i += 1; continue
        tok = s.split()
        if len(tok) != 3 or tok[1] != "0":
            break
        i += 1
        npos = int(lines[i].strip()); i += 1
        i += npos + 1
        nv = int(lines[i].strip()); i += 1; tv += nv
        i += nv * 3
        b = int(lines[i].strip()); i += 1
        if b > 0:
            i += 1; w = int(lines[i].strip()); i += 1; i += w
        nt = int(lines[i].strip()); i += 1; tf += nt
        i += nt
print("\n=== 达标子集几何 ===")
print("  顶点 %s / 面 %s" % (format(tv, ","), format(tf, ",")))
eb = sum(os.path.getsize(p) for p in elig)
print("  TRF %.1f MB，按 48B/顶点 + 6B/面 折合顶点流 %.1f MB"
      % (eb / 1024 ** 2, (tv * 48 + tf * 6) / 1024 ** 2))
print("  按 103B/顶点 + 12B/面 折合编辑器数据 %.1f MB" % ((tv * 103 + tf * 12) / 1024 ** 2))
print("\n  若减面到 10%%（这类平铺贴花视觉几乎无损）：")
print("    顶点 ≈ %s  顶点流 %.1f MB  编辑器 %.1f MB"
      % (format(int(tv * 0.25), ","), (tv * 0.25 * 48 + tf * 0.1 * 6) / 1024 ** 2,
         (tv * 0.25 * 103 + tf * 0.1 * 12) / 1024 ** 2))
