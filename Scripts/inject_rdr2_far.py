# -*- coding: utf-8 -*-
r"""把「打散版远景碎片」摆进指定 Mission 场景的活动区之外（P 外远景层）。

数据源：`deliverable/manifests/lod_fragments.csv`
  · 每行 = 一块碎片，带**世界坐标** wx/wy/wz + 四元数 qx/qy/qz/qw + 缩放 sxy/sz
  · 与场景 far_rect 相交的组才取；中心落在活动区 P 内的跳过（P 内由 HD 本体负责）
网格：`Assets\models\main_map_lod\<名>_geo.tpac`（打散版碎片，已导入 2,212 个）

坐标（与 inject_rdr2_scene.py 同一套）：scene_x = wx − rect_x0 ; scene_y = wy − rect_y0 ; scene_z = wz
朝向：yaw = 2·atan2(qz, qw)（世界系 → 场景系，Y 都朝北，无镜像）

实体带 `far` 标签 + `far` 选择集，重复执行会先删掉上次摆的（按标签识别）。

用法：
  python Scripts/inject_rdr2_far.py --id sdn --dry
  python Scripts/inject_rdr2_far.py --id sdn
"""
import argparse
import csv
import io
import json
import math
import os
import re
import shutil
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
MODULE = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2"
DATA = r"D:\BrainMaker\大镖客2解包分析"
FRAG = os.path.join(DATA, r"deliverable\manifests\lod_fragments.csv")
GROUPS = os.path.join(DATA, r"deliverable\manifests\lod_groups.json")

SCENES = {
    "sdn": (
        os.path.join(DATA, r"deliverable\blender\scenes\scene_report_sdn.json"),
        os.path.join(ROOT, r"Debug\RD2构造相关\terrain_sdn\terrain_sdn_meta.json"),
        os.path.join(MODULE, r"SceneObj\rdr2_sanit_denis\scene.xscene"),
    ),
}


def strip_tagged(src, tag):
    out, i, n = [], 0, 0
    while True:
        j = src.find("<game_entity", i)
        if j < 0:
            out.append(src[i:])
            break
        out.append(src[i:j])
        k = src.find("</game_entity>", j)
        if k < 0:
            out.append(src[j:])
            break
        k += len("</game_entity>")
        block = src[j:k]
        if ('<tag name="%s"/>' % tag) in block:
            n += 1
        else:
            out.append(block)
        i = k
    return "".join(out), n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--id", default="sdn")
    ap.add_argument("--dry", action="store_true")
    ap.add_argument("--include-p", action="store_true", help="连活动区内的碎片也摆（默认跳过）")
    a = ap.parse_args()

    rep_p, meta_p, scene_p = SCENES[a.id]
    rep = json.load(io.open(rep_p, encoding="utf-8"))
    x0, y0, x1, y1 = rep["far_rect"]
    ax0, ay0, ax1, ay1 = rep["activity_rect"]
    meta = json.load(io.open(meta_p, encoding="utf-8"))
    tx0, ty0 = meta["rect_m"][0], meta["rect_m"][1]

    groups = json.load(io.open(GROUPS, encoding="utf-8"))["groups"]
    ids = set()
    for g in groups:
        bb = g.get("bbox_world") or []
        if len(bb) < 5 or not g.get("count"):
            continue
        if bb[3] < x0 or bb[0] > x1 or bb[4] < y0 or bb[1] > y1:
            continue
        ids.add(g["group_id"])

    # 🔴 资产要在**整个 Assets 里**找，不能只看 main_map_lod：
    #    `new_area_slod4__split_sdn` 实际在 `Assets\models\sdn_test\`；
    #    且引擎导入时会把文件名末尾 `_lod/_lodNNN` **去掉**再注册 MetaMesh
    #    （例：`sis_floodlights_lod_geo.tpac` → metamesh `sis_floodlights`），
    #    所以比对和执行都要用"去 _lod 后的名字"。
    have = set()
    for sub in ("models", "decal_models"):
        b = os.path.join(MODULE, "Assets", sub)
        if not os.path.isdir(b):
            continue
        for root, _, fns in os.walk(b):
            for fn in fns:
                if fn.endswith("_geo.tpac"):
                    have.add(re.sub(r"_lod\d*$", "", fn[:-9], flags=re.I).lower())
    rows = list(csv.DictReader(io.open(FRAG, encoding="utf-8-sig")))
    pick, miss, in_p, out_far = [], [], 0, 0
    for r in rows:
        if r["group_id"] not in ids:
            continue
        wx, wy, wz = float(r["wx"]), float(r["wy"]), float(r["wz"])
        if not (x0 <= wx <= x1 and y0 <= wy <= y1):
            out_far += 1
            continue
        # 🔴 判"在不在活动区"必须用 **bb**，不能用 wx/wy：
        #    wx/wy 是源对象原点，拆分块的几何可能离原点几百米
        #    （`new_area_slod4__split_sis` 原点在 P 内，bb 却在 x 3097..3415 = 完全在 P 外）
        bx0, bx1 = float(r["bb_min_x"]), float(r["bb_max_x"])
        by0, by1 = float(r["bb_min_y"]), float(r["bb_max_y"])
        bb_in_p = not (bx1 < ax0 or bx0 > ax1 or by1 < ay0 or by0 > ay1)
        if (not a.include_p) and bb_in_p:
            in_p += 1
            continue
        # 引擎注册的 MetaMesh 名 = 去掉末尾 `_lod/_lodNNN`，所以两边都要剥
        mesh_name = re.sub(r"_lod\d*$", "", r["name"], flags=re.I)
        if mesh_name.lower() not in have:
            miss.append(r["name"])
            continue
        pick.append((r, mesh_name))

    tris = sum(int(r["tris"]) for r, _m in pick if r["tris"].isdigit())
    print("组 %d 个 · 候选碎片 %d · 会摆 %d 件 / %.2fM 面（跳过：区内 %d、框外 %d、缺网格 %d）"
          % (len(ids), len(rows), len(pick), tris / 1e6, in_p, out_far, len(miss)))
    if miss:
        print("  缺网格例:", miss[:6])
    if a.dry:
        return

    src = io.open(scene_p, encoding="utf-8", errors="replace").read()
    if "</entities>" not in src:
        print("场景没有 </entities>，中止")
        return
    src, removed = strip_tagged(src, "far")

    frag, used = [], {}
    for r, name in pick:
        k = used.get(name, 0) + 1
        used[name] = k
        ent = name if k == 1 else "%s_%d" % (name, k)
        sx = float(r["wx"]) - tx0
        sy = float(r["wy"]) - ty0
        sz = float(r["wz"])
        qz, qw = float(r["qz"]), float(r["qw"])
        rz = math.radians(math.degrees(2 * math.atan2(qz, qw)))
        sxy, sz2 = float(r["sxy"]), float(r["sz"])
        sc = "" if abs(sxy - 1) < 1e-3 and abs(sz2 - 1) < 1e-3 else \
            ' scale="%.4f, %.4f, %.4f"' % (sxy, sxy, sz2)
        frag.append('<game_entity name="%s" old_prefab_name="" mobility="1">'
                    '<tags><tag name="far"/></tags>'
                    '<transform position="%.3f, %.3f, %.3f" rotation_euler="0.000, 0.000, %.4f"%s/>'
                    '<edit_mode_data><selection_sets><set name="far"/></selection_sets></edit_mode_data>'
                    '<components><meta_mesh_component name="%s"/></components></game_entity>'
                    % (ent, sx, sy, sz, rz, sc, name))

    bak = os.path.join(ROOT, r"Debug\offline", "_scene_%s_far_%s.xscene"
                       % (a.id, time.strftime("%Y%m%d_%H%M%S")))
    shutil.copy2(scene_p, bak)
    io.open(scene_p, "w", encoding="utf-8", newline="\n").write(
        src.replace("</entities>", "".join(frag) + "</entities>", 1))
    print("已写入 %d 个远景实体（删掉旧 %d 个）→ %s" % (len(frag), removed, scene_p))
    print("备份 %s" % bak)


if __name__ == "__main__":
    main()
