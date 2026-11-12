# -*- coding: utf-8 -*-
"""decal_models 按子目录分类统计 TRF，并映射到贴图字节。"""
import collections
import csv
import io
import os
import re

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
M = os.path.join(RDR2, "deliverable", "manifests")
DEC = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2\AssetSources\decal_models"
TEXDIRS = {k: os.path.join(RDR2, "deliverable", "textures", k) for k in ("alb", "nm", "ma")}


def load(n):
    with io.open(os.path.join(M, n), encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


# --- 收集
percat = collections.defaultdict(list)
for root, dirs, files in os.walk(DEC):
    for f in files:
        if f.lower().endswith(".trf"):
            cat = os.path.basename(root) if root != DEC else "(root)"
            percat[cat].append(os.path.join(root, f))

tot_n = sum(len(v) for v in percat.values())
tot_b = sum(os.path.getsize(p) for v in percat.values() for p in v)
print("=== 按类别 ===")
print("%-14s %6s %10s %8s %8s" % ("类别", "数量", "GB", "平均KB", "占比"))
for c in sorted(percat, key=lambda x: -sum(os.path.getsize(p) for p in percat[x])):
    v = percat[c]
    b = sum(os.path.getsize(p) for p in v)
    print("%-14s %6d %10.3f %8.0f %7.1f%%"
          % (c, len(v), b / 1024 ** 3, b / len(v) / 1024, 100.0 * b / tot_b))
print("%-14s %6d %10.3f" % ("合计", tot_n, tot_b / 1024 ** 3))

# --- 材质名：清单优先，缺失读 TRF 头
mm = load("model_materials.csv")
col = mm[0].keys()
mcol = "model" if "model" in col else list(col)[0]
mats_of = collections.defaultdict(set)
for r in mm:
    mats_of[(r.get(mcol) or "").lower()].add((r.get("material") or "").lower())

HDR = re.compile(r"^(\S+\.\d+) 0 (\S+)$")
mats = set()
miss = 0
for c, paths in percat.items():
    for p in paths:
        n = os.path.splitext(os.path.basename(p))[0].lower()
        s = mats_of.get(n)
        if s:
            mats |= s
        else:
            miss += 1
            try:
                with io.open(p, encoding="utf-8", errors="replace") as f:
                    for ln in f:
                        m = HDR.match(ln.rstrip("\n"))
                        if m:
                            mats.add(m.group(2).lower())
            except Exception:
                pass
print("\n材质总数 = %d（其中 %d 个模型的材质靠读 TRF 头补出的）" % (len(mats), miss))

# --- 材质 -> 贴图 -> 磁盘字节
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

realtot = 0
hit = 0
byk = collections.Counter()
wh = collections.Counter()
for k, v in want:
    p = os.path.join(TEXDIRS[k], v if v.endswith(".png") else v + ".png")
    if os.path.isfile(p):
        s = os.path.getsize(p)
        realtot += s
        hit += 1
        byk[k] += s
        wh[k] += 1

print("\n=== 贴图 ===")
print("  去重贴图 %d 张（alb %d / nm %d / ma %d）"
      % (len(want), wh["alb"], wh["nm"], wh["ma"]))
print("  在 deliverable\\textures 找到 %d 张" % hit)
print("  PNG 磁盘合计 %.3f GB  (alb %.2f / nm %.2f / ma %.2f)"
      % (realtot / 1024 ** 3, byk["alb"] / 1024 ** 3, byk["nm"] / 1024 ** 3, byk["ma"] / 1024 ** 3))
print("  粗估进包后（DXT+mip+LZ4）≈ %.2f GB" % (realtot * 0.55 / 1024 ** 3))
print("\n合计（网格 %.2f GB + 贴图 %.2f GB PNG）"
      % (tot_b / 1024 ** 3, realtot / 1024 ** 3))
