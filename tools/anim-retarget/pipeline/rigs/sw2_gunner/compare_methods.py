# -*- coding: utf-8 -*-
"""汇总对比多种重定向方案的"肢段方向误差"（与骨骼轴向约定无关）。"""
import json, math, os, sys, glob

def ang(a, b):
    d = sum(x*y for x, y in zip(a, b))
    return math.degrees(math.acos(max(-1.0, min(1.0, d))))

GROUPS = {
    "躯干": ["spine", "spine2", "head"],
    "左臂": ["l_clavicle", "l_upperarm_twist", "l_foretwist", "l_hand"],
    "右臂": ["r_clavicle", "r_upperarm_twist", "r_foretwist", "r_hand"],
    "左腿": ["l_thigh", "l_calf", "l_foot", "l_toe0"],
    "右腿": ["r_thigh", "r_calf", "r_foot", "r_toe0"],
}
LABELS = {
    "mine_align": "自研脚本 align 模式",
    "mine_delta": "自研脚本 delta 模式",
    "bac": "BoneAnimCopy 插件",
    "biosculpt": "BioSculpt Retargeter 插件",
}
ORDER = ["biosculpt", "bac", "mine_delta", "mine_align"]

def load(p):
    d = json.load(open(p, encoding="utf-8"))
    seg = d["meta"]["seg_bones"]; per = {b: [] for b in seg}
    for row in d["rows"]:
        for b in seg:
            sd = row.get(b, {}).get("src_dir"); td = row.get(b, {}).get("tgt_dir")
            if sd and td: per[b].append(ang(sd, td))
    return per

def stats(per):
    allv = [v for b in per for v in per[b]]
    if not allv: return None
    return dict(mean=sum(allv)/len(allv),
                rms=math.sqrt(sum(v*v for v in allv)/len(allv)), mx=max(allv),
                n=len(allv))

here = os.path.dirname(os.path.abspath(__file__))
files = {os.path.basename(p)[len("measure_"):-5]: p
         for p in glob.glob(os.path.join(here, "out", "measure_*.json"))}
res = {}
for k in ORDER:
    if k in files: res[k] = (load(files[k]), stats(load(files[k])))

print("=" * 96)
print("肢段方向误差（源→目标的肢体朝向夹角，单位度；越小越像源）")
print("=" * 96)
print("%-26s %8s %8s %8s | %s" % ("方案", "mean", "RMS", "max", "   ".join("%-6s" % g for g in GROUPS)))
for k in ORDER:
    if k not in res: continue
    per, st = res[k]
    segline = []
    for g, bs in GROUPS.items():
        v = [x for b in bs for x in per.get(b, [])]
        segline.append("%-6.2f" % (sum(v)/len(v)) if v else "  -   ")
    print("%-26s %8.2f %8.2f %8.2f | %s" % (LABELS.get(k, k), st["mean"], st["rms"], st["mx"], "   ".join(segline)))

print()
print("=" * 96)
print("逐骨 mean 误差（度）")
print("=" * 96)
bones = sorted({b for k in res for b in res[k][0]})
print("%-22s %s" % ("bone", "  ".join("%-12s" % LABELS.get(k, k)[:12] for k in ORDER if k in res)))
for b in bones:
    cells = []
    for k in ORDER:
        if k not in res: continue
        v = res[k][0].get(b, [])
        cells.append("%-12.2f" % (sum(v)/len(v)) if v else "%-12s" % "-")
    print("%-22s %s" % (b, "  ".join(cells)))

# 输出 markdown 表供 HTML 使用
md = []
md.append("| 方案 | 全局 mean | RMS | max | 躯干 | 左臂 | 右臂 | 左腿 | 右腿 |")
md.append("|---|---|---|---|---|---|---|---|---|")
for k in ORDER:
    if k not in res: continue
    per, st = res[k]
    g = []
    for gg, bs in GROUPS.items():
        v = [x for b in bs for x in per.get(b, [])]
        g.append("%.2f" % (sum(v)/len(v)) if v else "-")
    md.append("| %s | %.2f° | %.2f° | %.2f° | %s° | %s° | %s° | %s° | %s° |" %
              (LABELS.get(k, k), st["mean"], st["rms"], st["mx"], *g))
open(os.path.join(here, "out", "compare.md"), "w", encoding="utf-8").write("\n".join(md))
print("\n[saved] out/compare.md")
