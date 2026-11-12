# -*- coding: utf-8 -*-
"""逐网格算"朝上比例"和"平面度"，筛出可地面化的贴花子集。

输出：
  - 按朝上比例分桶：网格数 / 字节 / 顶点
  - 达标子集（>=0.95 朝上）的平面度分布（能否压成一张平面）
"""
import collections
import io
import os
import numpy as np

DEC = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2\AssetSources\decal_models"


def parse(path):
    with io.open(path, encoding="utf-8", errors="replace") as f:
        lines = f.read().split("\n")
    i, n = 0, len(lines)
    parts = []
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
        pos = np.fromstring(" ".join(lines[i:i + npos]), sep=" ").reshape(-1, 3)
        i += npos + 1
        nv = int(lines[i].strip()); i += 1
        blk = np.fromstring(" ".join(lines[i:i + nv * 3]), sep=" ")
        if blk.size != nv * 9:
            break
        blk = blk.reshape(nv, 9)
        pidx = blk[:, 0].astype(np.int64)
        nrm = blk[:, 2:5]
        i += nv * 3
        b = int(lines[i].strip()); i += 1
        if b > 0:
            i += 1; w = int(lines[i].strip()); i += 1; i += w
        nt = int(lines[i].strip()); i += 1
        tri = np.fromstring(" ".join(lines[i:i + nt]), sep=" ").astype(np.int64).reshape(-1, 3)
        i += nt
        parts.append((pos, pidx, nrm, tri))
    return parts


rows = []
files = []
for root, dirs, fs in os.walk(DEC):
    for f in fs:
        if f.lower().endswith(".trf"):
            files.append((os.path.basename(root), os.path.join(root, f)))
print("扫描 %d 个文件..." % len(files))

for idx, (cat, p) in enumerate(files):
    if idx % 500 == 0:
        print("  %d / %d" % (idx, len(files)))
    try:
        parts = parse(p)
    except Exception:
        continue
    vtot = up9 = 0
    zs = []
    xy = []
    fcount = 0
    for pos, pidx, nrm, tri in parts:
        if len(nrm) == 0:
            continue
        vtot += len(nrm)
        up9 += int((nrm[:, 2] > 0.9).sum())
        V = pos[pidx]
        zs.append(V[:, 2])
        xy.append(V[:, :2])
        fcount += len(tri)
    if vtot == 0:
        continue
    z = np.concatenate(zs); q = np.concatenate(xy)
    zr = float(z.max() - z.min())
    xyext = float(np.linalg.norm(q.max(axis=0) - q.min(axis=0)))
    rows.append(dict(cat=cat, path=p, up=up9 / float(vtot), verts=vtot, faces=fcount,
                     bytes=os.path.getsize(p), zr=zr, xyext=xyext,
                     flat=(zr / xyext) if xyext > 1e-6 else 0.0))
print("解析成功 %d 个" % len(rows))

tot_b = sum(r["bytes"] for r in rows)
tot_v = sum(r["verts"] for r in rows)
tot_f = sum(r["faces"] for r in rows)
print("\n全部: %d 个 / %.0f MB / %s 顶点 / %s 面"
      % (len(rows), tot_b / 1024 ** 2, format(tot_v, ","), format(tot_f, ",")))

print("\n=== 按朝上比例分桶 ===")
bks = [("0-30%", 0, .30), ("30-60%", .30, .60), ("60-85%", .60, .85),
       ("85-95%", .85, .95), (">=95%", .95, 1.01)]
print("%-10s %6s %10s %7s %12s %10s" % ("朝上比例", "网格", "MB", "字节占比", "顶点", "平均面数"))
for lab, lo, hi in bks:
    sub = [r for r in rows if lo <= r["up"] < hi]
    if not sub:
        continue
    b = sum(r["bytes"] for r in sub)
    v = sum(r["verts"] for r in sub)
    f = sum(r["faces"] for r in sub)
    print("%-10s %6d %10.1f %6.1f%% %12s %10.0f"
          % (lab, len(sub), b / 1024 ** 2, 100.0 * b / tot_b, format(v, ","), f / len(sub)))

elig = [r for r in rows if r["up"] >= 0.95]
print("\n=== 达标子集（朝上 >=95%%）: %d 个 / %.1f MB ===" % (len(elig), sum(r["bytes"] for r in elig) / 1024 ** 2))
byc = collections.Counter()
byb = collections.Counter()
for r in elig:
    byc[r["cat"]] += 1
    byb[r["cat"]] += r["bytes"]
for c in sorted(byc, key=lambda x: -byb[x]):
    print("  %-14s %5d 个  %8.1f MB" % (c, byc[c], byb[c] / 1024 ** 2))

print("\n=== 达标子集的平面度（z落差 / xy尺寸）===")
print("  越小越接近一张平面，越容易压平成 quad")
pb = [("<=0.01", 0, .01), ("0.01-0.05", .01, .05), ("0.05-0.2", .05, .2),
      ("0.2-0.5", .2, .5), (">0.5", .5, 99)]
for lab, lo, hi in pb:
    sub = [r for r in elig if lo <= r["flat"] < hi]
    print("  %-12s %5d 个  %8.1f MB  %s 顶点"
          % (lab, len(sub), sum(r["bytes"] for r in sub) / 1024 ** 2,
             format(sum(r["verts"] for r in sub), ",")))

print("\n=== 达标子集里最大的 10 个 ===")
for r in sorted(elig, key=lambda x: -x["bytes"])[:10]:
    print("  %-44s %7.2f MB  %6s 顶点  z落差/xysize=%.3f"
          % (os.path.basename(r["path"])[:44], r["bytes"] / 1024 ** 2,
             format(r["verts"], ","), r["flat"]))
