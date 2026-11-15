# -*- coding: utf-8 -*-
"""用场景/调参坐标算出当前 v2 布局的俯视快照（离线，不能替代游戏内实拍）。

输出：Debug/offline/511_layout_snapshot.json + .svg
口径：Forward = normalize(Gate - Outside)（与运行时 ResolveAnchors 同源）；
      所有相对偏移按 +Fwd / +Right（Right = (fwd.y, -fwd.x)）换算。
"""
import io
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
DESIGN = os.path.join(ROOT, "ModuleData", "DesignData")
OUT = os.path.join(ROOT, "Debug", "offline")
TUNING = os.path.join(DESIGN, "StoryProbeTuning.json")
V2 = os.path.join(DESIGN, "StoryStageV2.json")
SCENE = "empire_town_m"


def norm(vx, vy):
    n = math.hypot(vx, vy) or 1.0
    return vx / n, vy / n


def add(base, fwd, right, f, r, u=0.0):
    return [base[0] + fwd[0] * f + right[0] * r,
            base[1] + fwd[1] * f + right[1] * r,
            base[2] + u]


def main():
    tuning = json.load(io.open(TUNING, encoding="utf-8"))
    lay = (tuning.get("Gate", {}).get("Layouts") or {}).get(SCENE)
    if not lay:
        raise SystemExit("no gate layout for " + SCENE)
    gate, outside = lay["Gate"], lay["Outside"]
    fwd = norm(gate[0] - outside[0], gate[1] - outside[1])
    right = (fwd[1], -fwd[0])
    outside_m = float(tuning["Gate"].get("OutsideMeters", 3.0))
    blocker = [gate[0] - fwd[0] * outside_m, gate[1] - fwd[1] * outside_m, gate[2]]

    v2 = json.load(io.open(V2, encoding="utf-8"))
    result = {"scene": SCENE, "forward": fwd, "anchors": {
        "player": outside, "gate_entity": gate, "guard_spawn": blocker}, "layouts": {}}

    for lid, layout in (v2.get("Layouts") or {}).items():
        anchors = {"player": outside, "gate_entity": gate, "gate": blocker, "guard_spawn": blocker,
                   "idle": outside, "market": outside}
        props = {}
        for p in layout.get("Props") or []:
            base = anchors.get(p.get("Anchor") or "player", outside)
            pos = add(base, fwd, right, p.get("Fwd", 0), p.get("Right", 0), p.get("Up", 0))
            props[p["Id"]] = {"pos": pos, "yaw": p.get("Yaw", 0)}
        work = {}
        for w in layout.get("WorkPoints") or []:
            prop = props.get(w.get("Prop"))
            if not prop:
                continue
            yaw = math.radians(prop["yaw"])
            pf = (math.cos(yaw), math.sin(yaw))
            pr = (pf[1], -pf[0])
            side = (w.get("Side") or "front").lower()
            dirv = {"front": pf, "back": (-pf[0], -pf[1]),
                    "left": (-pr[0], -pr[1]), "right": pr}.get(side, pf)
            off = float(w.get("Offset") or 0)
            work[w["Id"]] = {"pos": [prop["pos"][0] + dirv[0] * off,
                                     prop["pos"][1] + dirv[1] * off, prop["pos"][2]],
                             "action": w.get("Action")}
        standing = []
        for s in layout.get("Standing") or []:
            if s.get("WorkPoint") in work:
                pos = work[s["WorkPoint"]]["pos"]
                where = "work:" + s["WorkPoint"]
            elif s.get("Post"):
                post = next((p for p in layout.get("Posts") or [] if p.get("Id") == s["Post"]), None)
                base = anchors.get((post or {}).get("Anchor") or "guard_spawn", blocker)
                pos = add(base, fwd, right, (post or {}).get("Fwd", 0), (post or {}).get("Right", 0))
                where = "post:" + s["Post"]
            else:
                base = anchors.get(s.get("Anchor") or "player", outside)
                pos = add(base, fwd, right, s.get("Fwd", 0), s.get("Right", 0), s.get("Up", 0))
                where = s.get("Anchor") or "player"
            standing.append({"actor": s.get("Actor"), "where": where, "pos": pos})
        result["layouts"][lid] = {"props": props, "work": work, "standing": standing}

    os.makedirs(OUT, exist_ok=True)
    jpath = os.path.join(OUT, "511_layout_snapshot.json")
    with io.open(jpath, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(result, ensure_ascii=False, indent=2) + "\n")

    # 简易俯视图（X 右、Y 上；SVG 的 y 轴翻转）
    pts = [(outside, "player", "#2b6"), (gate, "gate", "#c33"), (blocker, "blocker", "#36c")]
    for layout in result["layouts"].values():
        for pid, p in layout["props"].items():
            pts.append((p["pos"], pid, "#999"))
        for s in layout["standing"]:
            pts.append((s["pos"], s["actor"], "#e90"))
    xs = [p[0][0] for p in pts]
    ys = [p[0][1] for p in pts]
    pad = 8
    x0, x1, y0, y1 = min(xs) - pad, max(xs) + pad, min(ys) - pad, max(ys) + pad
    k = 12

    def sx(x): return (x - x0) * k
    def sy(y): return (y1 - y) * k

    svg = ['<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d">' %
           ((x1 - x0) * k, (y1 - y0) * k),
           '<rect width="100%" height="100%" fill="#fafafa"/>',
           '<text x="8" y="18" font-size="13" fill="#333">empire_town_m layout snapshot (offline, scene/tuning derived — not in-game proof)</text>']
    for pos, name, color in pts:
        svg.append('<circle cx="%.1f" cy="%.1f" r="4" fill="%s"/>' % (sx(pos[0]), sy(pos[1]), color))
        svg.append('<text x="%.1f" y="%.1f" font-size="10" fill="#222">%s</text>' %
                   (sx(pos[0]) + 6, sy(pos[1]) - 5, name))
    svg.append('</svg>')
    spath = os.path.join(OUT, "511_layout_snapshot.svg")
    with io.open(spath, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(svg) + "\n")
    print("wrote %s and %s (points=%d)" % (jpath, spath, len(pts)))


if __name__ == "__main__":
    sys.exit(main())
