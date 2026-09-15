# -*- coding: utf-8 -*-
"""check_partition.py —— 归属覆盖率审计：**每个碎片必须恰好属于一个部位**。

原则（2026-09-15 用户裁定）
--------------------------
源模型是**一整块**，要切成 脸 / 眼 / 头发 / 兜 / 武器 / 甲。这四者之间
**不允许有共用的面**；反过来，**任何碎片没有归属 = 漏了内容**（不是"这块不要"）。

所以这个脚本逐【碎片】(连通域) 核对，只报两类异常：

    漏（0 个归属） —— 这块几何在四条管线里谁都没拿：做出来的角色身上会少一块，
                      或者更糟：像政宗 sub4 那样，被 `head` 判据误判后两边都不要了。
    重（≥2 个归属）—— 同一块几何进了两个资产：穿上的时候同一处叠两层。

判据模型（= 四条管线**实际**的行为，改了管线要同步改这里）
----------------------------------------------------------
    · 脸/眼/武器件      → 整件归它自己
    · 头发件            → 绑头骨的碎片归「头」（并进脸壳），其余归「甲」
    · 兜件              → 绑头骨的碎片归「兜」，其余归「甲」
    · `helmet_whole` 列  → **整块**归「兜」（纯兜件但绑的不是头骨，如谦信的长布垂带）
    · 甲件（普查认出）   → 不绑头骨的碎片归「甲」；**绑头骨的碎片没人要 = 漏**
       （甲侧 `--drop-head-idx` 剔掉它，而只有兜件里的那些才会被兜接走）
    · `build_armors.FORCE_ARMOR` 补的件 → 归「甲」（人确认是甲、普查判「待看」的）
    · 驱动件（无 UV）/ 普查没认出的件 → 从不加载，**天然无人要**

🔴 已知会让本检查误报的根因（见 2026-09-15 政宗 sub4）：
   `frag_dominant` 用「全片权重求和」判主骨，对**大面积多骨混合的碎条**会选错骨
   —— 一条手臂上的碎条被判成 head → 甲剔掉它、兜不接 → 漏。

用法：
    blender -b --python tools/sw2-pipeline/check_partition.py -- L11_masamune
    blender -b --python tools/sw2-pipeline/check_partition.py -- --all
    blender -b --python tools/sw2-pipeline/check_partition.py -- L11_masamune --csv out.csv

退出码：0 = 全绿；1 = 有漏/重。
"""
import bpy
import csv
import io
import os
import re
import sys

A = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

import inspect
import io_scene_fbx.import_fbx as mod

_s = inspect.getsource(mod)
_bad = "assert len(full_weights) >= num_shapes_assigned_to_channel"
if _bad in _s:
    exec(compile(_s.replace(_bad, "pass"), mod.__file__, "exec"), mod.__dict__)

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
from parts_table import TABLE  # noqa: E402
try:
    from build_armors import FORCE_ARMOR  # noqa: E402  （人确认是甲、普查没认出的件）
except Exception:
    FORCE_ARMOR = {}

SRC = r"D:\BrainMaker\战国无双2资产解包分析\export\fbx"
CENSUS = os.path.join(REPO, "Debug", "offline", "sw2_census")

# 🔴 必须与 build_armor.HEAD_SW / part_census.bone_group 保持一致（改了要三处同改）
HEAD_BONES = {"bone_10", "bone_11"} | {"bone_%d" % i for i in range(46, 63)}


def parse_submesh(name):
    m = re.search(r"submesh_(\d+)", name or "")
    return int(m.group(1)) if m else -1


def frag_dominant(ob, verts):
    """一片碎片的主骨：全片权重求和后最大（与 part_census.frag_dominant 同一判据）。"""
    tot = {}
    for vi in verts:
        for g in ob.data.vertices[vi].groups:
            nm = ob.vertex_groups[g.group].name
            tot[nm] = tot.get(nm, 0.0) + g.weight
    if not tot:
        return None
    return max(tot.items(), key=lambda kv: kv[1])[0]


def islands(me, min_verts=1):
    import bmesh
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


def read_census(key):
    p = os.path.join(CENSUS, key + "_census.csv")
    if not os.path.isfile(p):
        return None
    rows = list(csv.DictReader(io.open(p, encoding="utf-8")))
    by_idx = {}
    for r in rows:
        try:
            by_idx[int(r["idx"])] = r
        except (TypeError, ValueError):
            continue
    return by_idx


def idx2name(key):
    """idx → 【精确网格名】。

    🔴 一律按名字对位，**不要按子网格号**：`submesh_2_noesis_meshnode_0002` 与
       `..._0002.001` 会解析出同一个号（实测秀吉 sub2 同时是脸件和武器件），
       按号对位会凭空造出「一张脸同时属于脸和武器」这种假重复。
    """
    p = os.path.join(REPO, "Debug", "offline", "sw2_parts", key + "_parts.csv")
    if not os.path.isfile(p):
        return {}
    out = {}
    for r in csv.DictReader(io.open(p, encoding="utf-8-sig")):
        try:
            out[int(r["idx"])] = (r.get("name") or "").strip()
        except (TypeError, ValueError):
            continue
    return out


def names_of(key, idxs, i2n):
    """挑件表的 idx 列 → 精确网格名集合。"""
    return {i2n[i] for i in idxs if i in i2n}


def audit(key):
    r = TABLE[key]
    cen = read_census(key)
    if not cen:
        return None, "没有普查 CSV"
    i2n = idx2name(key)
    if not i2n:
        return None, "没有零件表（先跑 run_identify.py）"
    face_n = names_of(key, set(r.get("face") or []), i2n)
    eye_n = names_of(key, set(r.get("eye") or []), i2n)
    hair_n = names_of(key, set(r.get("hair") or []), i2n)
    helm_n = names_of(key, set(r.get("helmet") or []), i2n)
    # `helmet_whole` 列 = 整块归兜的纯兜件（不过骨判据，见 build_armor 的 `--helmet-whole`）
    helm_whole_n = names_of(key, set(r.get("helmet_whole") or []), i2n)
    weap_n = names_of(key, set(r.get("weapons") or []), i2n)
    armor_n, driver_n = set(), set()
    # ⚠️ FORCE_ARMOR 存的是【子网格号】，不是 idx —— 别拿它去 i2n（idx→名字）里查（踩过）。
    _sub2name = {}
    for _i, _n in i2n.items():
        _m = re.search(r"submesh_(\d+)", _n or "")
        if _m:
            _sub2name.setdefault(int(_m.group(1)), _n)
    for _sub in FORCE_ARMOR.get(key, []):
        if _sub in _sub2name:
            armor_n.add(_sub2name[_sub])
    for idx, x in cen.items():
        nm = i2n.get(idx)
        if not nm:
            continue
        v = x["verdict"]
        if v.startswith("武器"):
            weap_n.add(nm)
        elif v.startswith("驱动件"):
            driver_n.add(nm)
        elif v.startswith("甲件") or v.startswith("内衬着物"):
            armor_n.add(nm)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(SRC, key + ".fbx"),
                             automatic_bone_orientation=False)
    rows, orphan, dup = [], [], []
    for ob in [o for o in bpy.data.objects if o.type == "MESH"]:
        nm = ob.name
        if nm in driver_n:
            continue
        for comp in islands(ob.data):
            d = frag_dominant(ob, comp)
            is_head = d in HEAD_BONES
            own = []
            if nm in face_n:
                own.append("脸")
            if nm in eye_n:
                own.append("眼")
            if nm in weap_n:
                own.append("武器")
            if nm in hair_n:
                own.append("头" if is_head else ("甲" if nm in armor_n else None))
            if nm in helm_whole_n:
                own.append("兜")                      # 整块要，不看骨
            elif nm in helm_n:
                own.append("兜" if is_head else ("甲" if nm in armor_n else None))
            elif nm in armor_n and nm not in (face_n | eye_n | weap_n):
                own.append("甲" if not is_head else None)
            own = [o for o in own if o]
            rows.append((nm, len(comp), d, "|".join(own) or "（无）"))
            if not own:
                orphan.append((nm, len(comp), d))
            elif len(set(own)) > 1:
                dup.append((nm, len(comp), d, own))
    return dict(rows=rows, orphan=orphan, dup=dup,
                sets=dict(face=face_n, eye=eye_n, hair=hair_n, helm=helm_n,
                          weap=weap_n, armor=armor_n, driver=driver_n)), ""


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass
    # `--csv <path>` 的值不以 `--` 开头，会被当成角色 key（实测 KeyError）—— 显式跳过
    _skip = {A.index("--csv") + 1} if "--csv" in A else set()
    keys = (sorted(TABLE) if "--all" in A
            else [a for i, a in enumerate(A) if not a.startswith("--") and i not in _skip])
    csv_out = A[A.index("--csv") + 1] if "--csv" in A else None
    all_rows, bad = [], 0
    for key in keys:
        res, why = audit(key)
        if not res:
            print("%-16s ⏭  %s" % (key, why))
            continue
        n_o, n_d = len(res["orphan"]), len(res["dup"])
        flag = "❌" if (n_o or n_d) else "✅"
        print("%s %-16s %-10s 碎片 %-4d 漏 %-3d 重 %-3d"
              % (flag, key, TABLE[key]["cn"], len(res["rows"]), n_o, n_d))
        for nm, n, d in sorted(res["orphan"], key=lambda t: -t[1])[:6]:
            print("      漏  %-40s %4d 顶点  主骨=%s" % (nm.split("_noesis_")[-1], n, d))
        for nm, n, d, own in sorted(res["dup"], key=lambda t: -t[1])[:6]:
            print("      重  %-40s %4d 顶点  %s  主骨=%s" % (nm.split("_noesis_")[-1], n, own, d))
        if n_o or n_d:
            bad += 1
        for t in res["rows"]:
            all_rows.append((key, TABLE[key]["cn"], t[0], t[1], t[2], t[3]))
    if csv_out:
        with io.open(csv_out, "w", encoding="utf-8-sig", newline="") as fh:
            w = csv.writer(fh)
            w.writerow(["key", "cn", "mesh", "verts", "dominant_bone", "owners"])
            w.writerows(all_rows)
        print("\n归属表 -> %s（%d 行）" % (csv_out, len(all_rows)))
    print("\n有漏/重的人：%d / %d" % (bad, len(keys)))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
