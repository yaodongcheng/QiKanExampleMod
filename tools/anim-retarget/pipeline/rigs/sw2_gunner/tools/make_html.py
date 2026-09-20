# -*- coding: utf-8 -*-
"""生成重定向核对页 index.html（数值自算 + 相对路径引用对比图）。"""
import json, math, os, glob

def ang(a, b):
    d = sum(x*y for x, y in zip(a, b))
    return math.degrees(math.acos(max(-1.0, min(1.0, d))))

GROUPS = {"躯干": ["spine","spine2","head"],
          "左臂": ["l_clavicle","l_upperarm_twist","l_foretwist","l_hand"],
          "右臂": ["r_clavicle","r_upperarm_twist","r_foretwist","r_hand"],
          "左腿": ["l_thigh","l_calf","l_foot","l_toe0"],
          "右腿": ["r_thigh","r_calf","r_foot","r_toe0"]}
ORDER = ["mine_align","mine_delta","bac","bac_ortho","biosculpt"]
LABEL = {"mine_align":"自研脚本 · align 模式 ★","mine_delta":"自研脚本 · delta 模式",
         "bac":"BoneAnimCopy 插件（自动偏移）","bac_ortho":"BoneAnimCopy 插件（正交偏移）",
         "biosculpt":"BioSculpt Retargeter 插件"}
NOTE = {"mine_align":"世界增量 + 按肢段方向做逐骨静止对齐（本次最佳）",
        "mine_delta":"只做世界空间旋转增量，保留目标自身 rest 站姿差",
        "bac":"COPY_ROTATION(WORLD) + 插件自动算出的常量旋转偏移",
        "bac_ortho":"同上，但偏移被近似到 90° 的倍数（插件默认）",
        "biosculpt":"建代理骨架→对齐Roll→约束→空物体桥接→烘焙"}

here = os.path.dirname(os.path.abspath(__file__))
res = {}
for p in glob.glob(os.path.join(here, "out", "measure_*.json")):
    k = os.path.basename(p)[len("measure_"):-5]
    d = json.load(open(p, encoding="utf-8")); seg = d["meta"]["seg_bones"]
    per = {b: [] for b in seg}
    for row in d["rows"]:
        for b in seg:
            sd = row.get(b, {}).get("src_dir"); td = row.get(b, {}).get("tgt_dir")
            if sd and td: per[b].append(ang(sd, td))
    allv = [v for b in seg for v in per[b]]
    res[k] = dict(per=per, mean=sum(allv)/len(allv),
                  rms=math.sqrt(sum(v*v for v in allv)/len(allv)), mx=max(allv), n=len(allv))

def cells(k):
    if k not in res: return ["-"]*5
    out = []
    for g, bs in GROUPS.items():
        v = [x for b in bs for x in res[k]["per"].get(b, [])]
        out.append("%.2f" % (sum(v)/len(v)) if v else "-")
    return out

rows = ""
for k in ORDER:
    if k not in res: continue
    r = res[k]; c = cells(k)
    cls = ' class="best"' if k == "mine_align" else ''
    rows += ("<tr%s><td><b>%s</b><div class=n>%s</div></td><td class=v>%.2f</td><td>%.2f</td>"
             "<td>%.2f</td><td>%s</td><td>%s</td><td>%s</td><td>%s</td><td>%s</td></tr>\n"
             % (cls, LABEL[k], NOTE[k], r["mean"], r["rms"], r["mx"], c[0], c[1], c[2], c[3], c[4]))

bones = sorted({b for k in res for b in res[k]["per"]})
brow = ""
for b in bones:
    tds = ""
    for k in ORDER:
        if k not in res: continue
        v = res[k]["per"].get(b, [])
        tds += "<td>%.2f</td>" % (sum(v)/len(v)) if v else "<td>-</td>"
    brow += "<tr><td>%s</td>%s</tr>\n" % (b, tds)

rest = {}
for p in glob.glob(os.path.join(here, "out", "measure_*.json")):
    d = json.load(open(p, encoding="utf-8"))
    if d.get("rest_dirs"): rest = d["rest_dirs"]; break
rrow = ""
for b, v in sorted(rest.items(), key=lambda kv: -kv[1].get("rest_gap_deg", 0)):
    rrow += "<tr><td>%s</td><td>%.2f°</td></tr>\n" % (b, v.get("rest_gap_deg", 0))

# --- 朝向一致性证据（用 align 结果算：脚尖方向 + 躯干朝向）---
facing = ""
mp = os.path.join(here, "out", "measure_mine_align.json")
if os.path.exists(mp):
    d = json.load(open(mp, encoding="utf-8"))
    for b in ("l_toe0", "r_toe0", "head"):
        v = [ang(r[b]["src_dir"], r[b]["tgt_dir"]) for r in d["rows"]
             if b in r and r[b].get("src_dir")]
        if not v: continue
        sd = d["rows"][0][b]["src_dir"]; td = d["rows"][0][b]["tgt_dir"]
        label = {"l_toe0": "左脚 脚踝→脚尖", "r_toe0": "右脚 脚踝→脚尖", "head": "胸→头（躯干朝向）"}[b]
        same = "逐位相同" if all(abs(a-b2) < 1e-6 for a, b2 in zip(sd, td)) else "接近"
        facing += ("<tr><td>%s</td><td>(%.2f, %.2f, %.2f)</td><td>(%.2f, %.2f, %.2f)</td>"
                   "<td>%s</td><td>%.2f</td><td>%.2f</td></tr>\n"
                   % (label, sd[0], sd[1], sd[2], td[0], td[1], td[2], same,
                      sum(v)/len(v), max(v)))

HEAD = "".join("<th>%s</th>" % LABEL[k].split("（")[0].replace("插件","").strip()
               for k in ORDER if k in res)

tpl = open(os.path.join(here, "_tpl.html"), encoding="utf-8").read()
html = (tpl.replace("__ROWS__", rows).replace("__BROWS__", brow)
           .replace("__RROWS__", rrow).replace("__HEAD__", HEAD).replace("__FACING__", facing))
out = os.path.join(here, "index.html")
open(out, "w", encoding="utf-8").write(html)
print("saved %s (%d bytes)" % (out, len(html)))
