# -*- coding: utf-8 -*-
"""肢段方向校验（与骨骼轴向约定无关，直接反映"肢体朝向是否像"）：
   对每根骨算 src_dir(已换到目标帧) 与 tgt_dir 的夹角(度)。越小越像。
"""
import json, math, os

def ang(a, b):
    d = sum(x*y for x, y in zip(a, b))
    return math.degrees(math.acos(max(-1.0, min(1.0, d))))

def show(path, label):
    d = json.load(open(path, encoding="utf-8"))
    seg = d["meta"]["seg_bones"]
    per = {b: [] for b in seg}
    for row in d["rows"]:
        for b in seg:
            sd = row.get(b, {}).get("src_dir"); td = row.get(b, {}).get("tgt_dir")
            if sd and td: per[b].append(ang(sd, td))
    allv = [v for b in seg for v in per[b]]
    if not allv: print("  (无数据)"); return None
    rms = math.sqrt(sum(v*v for v in allv)/len(allv))
    print("%-10s 全局 mean=%6.2f°  RMS=%6.2f°  max=%6.2f°" % (label, sum(allv)/len(allv), rms, max(allv)))
    groups = {"躯干": ["spine","spine2","head"], "左臂": ["l_clavicle","l_upperarm_twist","l_foretwist","l_hand"],
              "右臂": ["r_clavicle","r_upperarm_twist","r_foretwist","r_hand"],
              "左腿": ["l_thigh","l_calf","l_foot","l_toe0"], "右腿": ["r_thigh","r_calf","r_foot","r_toe0"]}
    for g, bs in groups.items():
        v = [x for b in bs for x in per.get(b, [])]
        if v: print("   %-4s mean=%6.2f° max=%6.2f°" % (g, sum(v)/len(v), max(v)))
    return rms

if __name__ == "__main__":
    here = os.path.dirname(os.path.abspath(__file__)); out = os.path.join(here, "out")
    base = json.load(open(os.path.join(out,"dump_align.json"), encoding="utf-8"))
    print("### rest 基线：源静止站姿 vs 骑砍静止站姿 的肢段方向差（纯增量法无法消除的常数偏差）")
    gaps = base.get("rest_dirs", {})
    gv = [v["rest_gap_deg"] for v in gaps.values()]
    if gv:
        print("   全局 mean=%.2f°  max=%.2f°" % (sum(gv)/len(gv), max(gv)))
        for k, v in sorted(gaps.items(), key=lambda kv: -kv[1]["rest_gap_deg"]):
            print("   %-22s %6.2f°" % (k, v["rest_gap_deg"]))
    print()
    print("### 三种姿态传递模式的肢段方向误差（越小越像源）")
    show(os.path.join(out,"dump_align.json"),    "align")
    show(os.path.join(out,"dump_delta.json"),    "delta")
    show(os.path.join(out,"dump_absolute.json"), "absolute")
