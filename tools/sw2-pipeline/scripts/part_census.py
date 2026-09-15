# -*- coding: utf-8 -*-
"""part_census.py —— 逐【碎片】统计零件绑在哪些骨上（甲件判定的权威数据）。

解决什么问题
------------
`identify_parts.py` 出的 `<角色>_parts.csv` 里 `top_bones` 只有**前 3 根骨**，
精度不够判甲：幸村的**内衬着物**（sub1，535 顶点，混着头皮+手+躯干）被它判成了 `head_area`，
照它挑件会把着物整块漏掉（而上臂中段本来就是着物盖的）。

本脚本按 **连通域（碎片）** 统计：每片碎片算一次主骨，再按骨的功能分组汇总到零件级。
战无2 的骨名是 `bone_N`，全 28 人共用同一套编号（头 10/11/46~62、手 18/19/26~45、
臂 12~17、脊柱 0/1/8/9、腿 2~7/24/25）。

判据（甲件 = 胴/袖/籠手/草摺/袴 + 内衬着物）
--------------------------------------------
  · 武器 = 材质名 `mat_w_*`；布料驱动件 = 没有 UV → 都排除（同 build_armor.is_junk）
  · 兜/发 = 碎片绝大多数落在头骨族 → 不是甲（头盔另走一条管线）
  · 着物 = 躯干骨为主，**同时**带头皮/手皮碎片 → 甲件，但要配 `--kimono-torso-only` + `--no-hands`
  · 混装件（头发+披风那种）= 同一块里既有头骨碎片又有躯干碎片 → 甲件，但要 `--drop-head-frags`

用法：
    blender -b --python part_census.py -- --src <源.fbx> [--csv <输出.csv>]
输出：stdout 一张表 + 可选 CSV（列：sub/verts/faces/mats/frags/spine%/head%/hand%/arm%/leg%/verdict）
"""
import bpy
import bmesh
import csv as _csv
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def get(a, k, d=None):
    return a[a.index(k) + 1] if k in a else d


A = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SRC = get(A, "--src")
OUT = get(A, "--csv")
if not SRC:
    print("FATAL: 需要 --src")
    sys.exit(1)

# TWT morph 缺 FullWeights → 导入器断言崩；内存级补丁（范本同 build_head.py）
import inspect
import io_scene_fbx.import_fbx as mod
_s = inspect.getsource(mod)
_bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
if _bad in _s:
    exec(compile(_s.replace(_bad, "pass"), mod.__file__, "exec"), mod.__dict__)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
bpy.context.view_layer.update()

# 全身高度（厘米）——所有源件都是厘米、Z 向上；取全部网格的 z 跨度当基准
_all = [o.matrix_world @ v.co for o in bpy.data.objects if o.type == "MESH" for v in o.data.vertices]
BODY_H_CM = (max(p.z for p in _all) - min(p.z for p in _all)) if _all else 165.0

STEM = os.path.splitext(os.path.basename(SRC))[0]


def bone_group(name):
    """战无2 骨名 → 功能组。"""
    m = re.fullmatch(r"bone_(\d+)", name or "")
    if not m:
        return "other"
    n = int(m.group(1))
    if n in (10, 11) or 46 <= n <= 62:
        return "head"
    if n in (18, 19) or 26 <= n <= 45:
        return "hand"
    if 12 <= n <= 17:
        return "arm"
    if n in (0, 1, 8, 9):
        return "spine"
    if n in (2, 3, 4, 5, 6, 7, 24, 25):
        return "leg"
    return "other"


def parse_submesh(name):
    m = re.search(r"submesh_(\d+)", name or "")
    return int(m.group(1)) if m else -1


def islands(me, min_verts=4):
    """连通域（碎片）列表，按顶点数降序。"""
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    seen = [False] * len(bm.verts)
    out = []
    for v in bm.verts:
        if seen[v.index]:
            continue
        stack, comp = [v], []
        seen[v.index] = True
        while stack:
            cur = stack.pop()
            comp.append(cur.index)
            for e in cur.link_edges:
                o2 = e.other_vert(cur)
                if not seen[o2.index]:
                    seen[o2.index] = True
                    stack.append(o2)
        if len(comp) >= min_verts:
            out.append(comp)
    bm.free()
    out.sort(key=len, reverse=True)
    return out


def frag_dominant(ob, verts):
    """一片碎片的主骨：全片权重求和后取最大。"""
    tot = {}
    for vi in verts:
        for g in ob.data.vertices[vi].groups:
            nm = ob.vertex_groups[g.group].name
            tot[nm] = tot.get(nm, 0.0) + g.weight
    if not tot:
        return "（无权重）"
    return max(tot.items(), key=lambda kv: kv[1])[0]


rows, lines = [], []
objs = sorted([o for o in bpy.data.objects if o.type == "MESH"], key=lambda o: o.name)
for i, ob in enumerate(objs):
    me = ob.data
    mats = "|".join(m.name for m in me.materials if m)
    has_uv = bool(me.uv_layers)
    is_weapon = any((m and m.name.lower().startswith("mat_w_")) for m in me.materials)
    if is_weapon or not has_uv:
        verdict = "武器" if is_weapon else "驱动件（无 UV）"
        rows.append(dict(idx=i, sub=parse_submesh(ob.name), verts=len(me.vertices), faces=len(me.polygons),
                         frags=0, mats=mats, spine=0, head=0, hand=0, arm=0, leg=0, verdict=verdict))
        lines.append("  %2d sub%-3s %-9s v=%-5d %s" % (i, parse_submesh(ob.name), verdict, len(me.vertices), mats))
        continue
    frs = islands(me)
    cnt = {"spine": 0, "head": 0, "hand": 0, "arm": 0, "leg": 0, "other": 0}
    detail = []
    for fr in frs:
        b = frag_dominant(ob, fr)
        cnt[bone_group(b)] += len(fr)
        detail.append((len(fr), b))
    tot = max(1, sum(cnt.values()))
    pct = {k: 100.0 * v / tot for k, v in cnt.items()}
    # 源件包围盒（厘米）：判"撒开布"用（披风/斗篷在绑定姿势下是平铺的一大片）
    ws = [ob.matrix_world @ v.co for v in me.vertices]
    w_cm = (max(w.x for w in ws) - min(w.x for w in ws)) if ws else 0.0
    h_cm = (max(w.z for w in ws) - min(w.z for w in ws)) if ws else 0.0
    # 判定
    if pct["head"] >= 70:
        verdict = "头部件（兜/发）"
    elif (len(frs) <= 6 and len(me.vertices) >= 60 and h_cm > 1
          and w_cm >= 0.5 * BODY_H_CM and pct["arm"] + pct["hand"] < 30):
        # 🔴 撒开布（披风/斗篷/阵羽织）：绑定姿势下平铺成一大片、只绑脊柱（脊柱在重定向里不旋转）
        #    → 甲会变成一顶"帐篷"（实测信长 sub0：131cm 宽 / 4 片碎片 / 100% 脊柱 → 甲宽 1.57 米）。
        #    四条判据一起用：碎片少（平铺大块）+ 顶点够多 + 宽 ≥ 半身 + **不绑手臂**
        #    （最后一条是关键：籠手/袖 在 T-pose 下也很宽，但它们绑手臂、重定向会把它们收回来）。
        verdict = "撒开布（披风/斗篷）——不收"
    elif pct["spine"] >= 30 and pct["hand"] >= 20:
        verdict = "内衬着物（甲件，配 --kimono-torso-only）+去手"
    elif pct["hand"] + pct["arm"] >= 55:
        verdict = "甲件（袖/籠手）" + (" +去手" if pct["hand"] >= 10 else "")
    elif pct["spine"] + pct["leg"] + pct["arm"] >= 55:
        verdict = "甲件" + (" +去手" if pct["hand"] >= 10 else "")
    else:
        verdict = "待看（碎片分散）"
    rows.append(dict(idx=i, sub=parse_submesh(ob.name), verts=len(me.vertices), faces=len(me.polygons),
                     frags=len(frs), mats=mats, spine=round(pct["spine"]), head=round(pct["head"]),
                     hand=round(pct["hand"]), arm=round(pct["arm"]), leg=round(pct["leg"]), verdict=verdict))
    lines.append("  %2d sub%-3s v=%-5d 碎=%-3d 脊%3d%% 头%3d%% 手%3d%% 臂%3d%% 腿%3d%%  %-30s %s"
                 % (i, parse_submesh(ob.name), len(me.vertices), len(frs), pct["spine"], pct["head"],
                    pct["hand"], pct["arm"], pct["leg"], verdict, mats))

print("=" * 108)
print("[CENSUS] %s" % STEM)
print("".join(l + "\n" for l in lines))
if OUT:
    os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
    with io.open(OUT, "w", encoding="utf-8", newline="") as fh:
        w = _csv.DictWriter(fh, fieldnames=["idx", "sub", "verts", "faces", "frags", "spine", "head",
                                            "hand", "arm", "leg", "verdict", "mats"])
        w.writeheader()
        for r in rows:
            w.writerow(r)
    print("[CENSUS] 写出 %s（%d 行）" % (OUT, len(rows)))
