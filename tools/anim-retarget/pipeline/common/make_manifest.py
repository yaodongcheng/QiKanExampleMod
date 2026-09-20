#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""T3：把《骑砍2动作精简清单.csv》转成查看器用的 manifest.json
   输出：web_slim/assets/manifest.json
   mode: ground(=逐帧贴地/原地) / src(=复制源骨盆位移, 供跳跃·翻越·倒地等离地类使用)
"""
import csv, json, os, sys, datetime

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))          # 骑砍2动画重定向/
CSV_PATH = os.path.join(ROOT, "input", "inventory", "骑砍2动作精简清单.csv")
OUT = os.path.join(ROOT, "viewer", "datasets", "ue_slim", "manifest.json")

# 需要"跟随源骨盆位移"（离地 / 大位移）的动作功能；其余一律用 ground
SRC_SLOTS = {
    "闪避位移特技", "跳跃翻越", "受击死亡倒地", "A10_攀爬/潜行", "A5_跳跃/腾空",
    "A6_闪避/翻滚", "A8_起身/站起", "D2_死亡/倒地", "特殊技", "坠落",
}

def main():
    with open(CSV_PATH, encoding="utf-8-sig") as f:
        rows = list(csv.DictReader(f))
    clips = {}
    for r in rows:
        name = (r.get("动作名") or "").strip()
        if not name:
            continue
        slot = (r.get("动作功能") or "").strip()
        clips[name] = {
            "prio":   (r.get("优先级") or "").strip(),
            "weapon": (r.get("武器/来源") or r.get("武器来源") or "").strip(),
            "func":   slot,
            "pack":   (r.get("来源包") or "").strip(),
            "desc":   (r.get("语义描述") or "").strip(),
            "path":   (r.get("资产路径") or "").strip(),
            "mode":   "src" if slot in SRC_SLOTS else "ground",
        }
    def uniq(key, order=None):
        vals = sorted({c[key] for c in clips.values() if c[key]})
        if order:
            vals.sort(key=lambda v: (order(v), v))
        return vals
    is_code = lambda v: (v[:1] in "ABCD" and len(v) > 1 and v[1].isdigit())
    man = {
        "meta": {
            "total": len(clips),
            "source_csv": os.path.relpath(CSV_PATH, ROOT).replace("\\", "/"),
            "generated": datetime.datetime.now().strftime("%Y-%m-%d %H:%M"),
            "note": "mode=ground 为逐帧贴地烘焙；mode=src 为复制源骨盆位移烘焙（跳跃/翻越/倒地等）",
        },
        "P0": sum(1 for c in clips.values() if c["prio"] == "P0"),
        "P1": sum(1 for c in clips.values() if c["prio"] == "P1"),
        "groups": {
            "prio":   uniq("prio"),
            "weapon": uniq("weapon"),
            # 标准动作码(A1_行走…)排前面，中文语义类排后面
            "func":   uniq("func", order=lambda v: 0 if is_code(v) else 1),
            "pack":   uniq("pack"),
            "mode":   uniq("mode"),
        },
        "clips": clips,
    }
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(man, f, ensure_ascii=False, indent=1)
    nm = {c["mode"] for c in clips.values()}
    cnt = {}
    for c in clips.values():
        cnt[c["mode"]] = cnt.get(c["mode"], 0) + 1
    print("总 %d 段  P0=%d P1=%d  ground=%d src=%d" %
          (len(clips), man["P0"], man["P1"], cnt.get("ground", 0), cnt.get("src", 0)))
    print("武器来源:", man["groups"]["weapon"])
    print("动作功能 %d 类" % len(man["groups"]["func"]))
    print("写出:", OUT)

main()
