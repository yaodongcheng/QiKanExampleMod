# -*- coding: utf-8 -*-
"""贴花到底有几类：目录分类 vs 清单 class 分类，是否一致、是否有嵌套。"""
import collections
import csv
import io
import os

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
M = os.path.join(RDR2, "deliverable", "manifests")
DEC = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2\AssetSources\decal_models"

print("=== decal_models 顶层目录 ===")
tops = sorted(os.listdir(DEC))
for t in tops:
    p = os.path.join(DEC, t)
    print("  %-16s %s" % (t, "目录" if os.path.isdir(p) else "文件"))

print("\n=== 每个目录的文件数/字节，以及是否有更深嵌套 ===")
deepest = 0
for t in tops:
    p = os.path.join(DEC, t)
    if not os.path.isdir(p):
        continue
    n = b = 0
    subdirs = set()
    for root, dirs, files in os.walk(p):
        rel = os.path.relpath(root, p)
        lvl = 0 if rel == "." else rel.count(os.sep) + 1
        deepest = max(deepest, lvl)
        for d in dirs:
            subdirs.add(os.path.join(rel, d) if rel != "." else d)
        for f in files:
            if f.lower().endswith(".trf"):
                n += 1
                b += os.path.getsize(os.path.join(root, f))
    print("  %-16s %5d 个  %7.1f MB  子目录:%s"
          % (t, n, b / 1024 ** 2, ("无" if not subdirs else "%d 个" % len(subdirs))))
print("  最大嵌套层数 = %d" % deepest)


def load(n):
    with io.open(os.path.join(M, n), encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


print("\n=== 清单里的 decal 相关 class（import_classes 计数）===")
cnt = collections.Counter()
for r in load("asset_import_models.csv"):
    for c in (r.get("import_classes") or "").split(";"):
        c = c.strip().lower()
        if c and ("decal" in c or c == "road"):
            cnt[c] += 1
for c, n in cnt.most_common():
    print("  %-22s %5d" % (c, n))

print("\n=== classes 列（原始分类）里的 decal 相关 ===")
cnt2 = collections.Counter()
for r in load("asset_import_models.csv"):
    for c in (r.get("classes") or "").split(";"):
        c = c.strip().lower()
        if c and ("decal" in c or c == "road"):
            cnt2[c] += 1
for c, n in cnt2.most_common(12):
    print("  %-22s %5d" % (c, n))

print("\n=== models_extra 里也有贴花吗 ===")
ex = load("models_export_extra.csv")
print("  models_export_extra 行数 = %d" % len(ex))
mm = load("model_materials.csv")
names = set()
for r in ex:
    names.add((r.get("name") or "").lower())
hit = sum(1 for r in load("asset_import_models.csv")
          if (r.get("name") or "").lower() in names and "decal" in
          ((r.get("import_classes") or "") + (r.get("classes") or "")).lower())
print("  同时出现在两份清单且是贴花的 = %d（仅供参考）" % hit)
