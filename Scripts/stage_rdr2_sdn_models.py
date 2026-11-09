#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""按分类把圣丹尼斯（sdn）的基础模型 TRF / 贴图 分装进 ImortReady，供 ModKit 逐类导入。

输出（Modules/RDR2/AssetSources/ImortReady/rdr2_sdn_models/）：
  textures/            该场景所有类用到的 albedo/normal/ma（同名 .png，去重）
  matmap.csv           material,albedo,normal,ma —— 喂 tpaccli editormeta --genmats3
  <class>/             该类 TRF + models.csv（超过 560 个自动切 <class>_01/_02…）
  staging_summary.csv  每类：模型数 / TRF 体积 / 缺 TRF 数 / 批次目录

口径（2026-11-09）：
  · 类名与判定 = `D:\\BrainMaker\\大镖客2解包分析\\tools\\class_rules.py`（唯一真身）。
  · **lod / lod_out / terrain_cell / int_* / layout / placeholder / underground / unknown 不导**
    —— lod 那一批已经在 `ImortReady/rdr2_lod_fragments` 做完了。
  · 模型名解析：placement 的 model_hash → model_manifest.csv(hash→name) → models_export.csv(trf)。
  · 每批 ≤560 个网格（ModKit 实测值，批间要重启编辑器）。

用法：
  python Scripts/stage_rdr2_sdn_models.py                 # 全量
  python Scripts/stage_rdr2_sdn_models.py --classes bldg  # 只装一个类
  python Scripts/stage_rdr2_sdn_models.py --no-textures   # 跳过贴图拷贝（已经导过时）
"""
import argparse
import csv
import io
import json
import os
import shutil
import sys

DELIV = r"D:\BrainMaker\大镖客2解包分析\deliverable"
MOD = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\RDR2"
OUT = os.path.join(MOD, "AssetSources", "ImortReady", "rdr2_sdn_models")
PLACEMENT = os.path.join(DELIV, "blender", "scenes", "placement_sdn.jsonl")

# 顺序 = 建议导入顺序（先立骨架，再填细节）
CLASSES = [
    ("bldg", "建筑"),
    ("part", "建筑部件"),
    ("prop", "道具"),
    ("junk", "杂物堆"),
    ("veh", "载具"),
    ("struct", "桥梁结构"),
    ("rail", "铁路"),
    ("road", "道路"),
    ("decal", "地面贴花"),
    ("veg", "植被"),
    ("rock", "岩石地形"),
    ("stone", "石块"),
    ("ruin", "废墟"),
    ("watertile", "水面块"),
    ("bldg_decal", "建筑贴花"),
]
BATCH = 560


def load(path):
    return list(csv.DictReader(io.open(path, encoding="utf-8-sig")))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--classes", default="", help="逗号分隔；默认全部")
    ap.add_argument("--batch", type=int, default=BATCH)
    ap.add_argument("--no-textures", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    want = [c for c, _ in CLASSES]
    if args.classes:
        want = [c.strip() for c in args.classes.split(",") if c.strip()]
    zh = dict(CLASSES)

    # ---- 清单 ----
    hash2name = {}
    for r in load(os.path.join(DELIV, "manifests", "model_manifest.csv")):
        hash2name[(r.get("hash") or "").lower().replace("0x", "")] = r.get("name", "")

    models = {}
    for fn in ("models_export.csv", "models_export_extra.csv"):
        p = os.path.join(DELIV, "manifests", fn)
        if os.path.isfile(p):
            for r in load(p):
                models.setdefault(r["name"], r)

    mats = {}
    for r in load(os.path.join(DELIV, "manifests", "model_materials.csv")):
        mats.setdefault(r["material"], (r.get("albedo", ""), r.get("normal", ""), r.get("ma", "")))
    mat_of_model = {}
    for r in load(os.path.join(DELIV, "manifests", "model_materials.csv")):
        mat_of_model.setdefault(r["model"], set()).add(r["material"])

    texdest = {}
    for r in load(os.path.join(DELIV, "manifests", "textures_export.csv")):
        if r.get("name") and r.get("dest"):
            texdest[r["name"]] = r["dest"]

    need = {c: set() for c in want}
    inst = {c: 0 for c in want}
    with io.open(PLACEMENT, encoding="utf-8") as f:
        for line in f:
            d = json.loads(line)
            c = d.get("cls")
            if c not in need:
                continue
            inst[c] += 1
            n = hash2name.get((d.get("model_hash") or "").lower().replace("0x", ""))
            if n:
                need[c].add(n)

    os.makedirs(OUT, exist_ok=True)
    summary = []
    all_mats = set()

    for c in want:
        names = sorted(need[c])
        ok, miss = [], []
        for n in names:
            r = models.get(n)
            p = os.path.join(DELIV, "models", r["trf"]) if (r and r.get("trf")) else None
            (ok if (p and os.path.isfile(p)) else miss).append(n)
        # 面数多的先导（大件先立骨架）
        ok.sort(key=lambda n: -int(models[n].get("faces") or 0))
        chunks = [ok[i:i + args.batch] for i in range(0, len(ok), args.batch)]
        print("%-12s 模型 %5d  有TRF %5d  缺 %4d  批次 %d" % (c, len(names), len(ok), len(miss), len(chunks)))
        total_bytes = 0
        for i, chunk in enumerate(chunks):
            sub = c if len(chunks) == 1 else "%s_%02d" % (c, i + 1)
            d = os.path.join(OUT, sub)
            if not args.dry_run:
                os.makedirs(d, exist_ok=True)
            rows = []
            for n in chunk:
                src = os.path.join(DELIV, "models", models[n]["trf"])
                dst = os.path.join(d, os.path.basename(src))
                if not args.dry_run:
                    shutil.copy2(src, dst)
                total_bytes += os.path.getsize(src)
                rows.append([n, os.path.basename(src), models[n].get("faces", ""), inst[c]])
                all_mats |= mat_of_model.get(n, set())
        # models.csv
        if chunks and not args.dry_run:
            for i, chunk in enumerate(chunks):
                sub = c if len(chunks) == 1 else "%s_%02d" % (c, i + 1)
                with io.open(os.path.join(OUT, sub, "models.csv"), "w",
                             encoding="utf-8-sig", newline="") as fh:
                    w = csv.writer(fh)
                    w.writerow(["asset_name", "trf", "faces", "instances_in_scene"])
                    for n in chunk:
                        w.writerow([n, os.path.basename(models[n]["trf"]),
                                    models[n].get("faces", ""), inst[c]])
        summary.append([c, zh.get(c, c), len(names), len(ok), len(miss),
                        round(total_bytes / 1e6, 1), len(chunks)])
        if miss:
            with io.open(os.path.join(OUT, "missing_%s.csv" % c), "w",
                         encoding="utf-8-sig", newline="") as fh:
                w = csv.writer(fh)
                w.writerow(["asset_name", "reason"])
                for n in miss:
                    w.writerow([n, "no_trf" if n in models else "not_in_models_export"])

    # ---- 贴图 + matmap ----
    tex = set()
    for m in sorted(all_mats):
        a, n, ma = mats.get(m, ("", "", ""))
        for t in (a, n, ma):
            if t:
                tex.add(t)
    if not args.no_textures:
        td = os.path.join(OUT, "textures")
        if not args.dry_run:
            os.makedirs(td, exist_ok=True)
        missing = [t for t in tex if t not in texdest or not os.path.isfile(texdest[t])]
        for t in sorted(tex):
            if t in missing:
                continue
            if not args.dry_run:
                shutil.copy2(texdest[t], os.path.join(td, t + ".png"))
        print("贴图 %d 张（缺源 %d）" % (len(tex) - len(missing), len(missing)))
        if missing and not args.dry_run:
            with io.open(os.path.join(OUT, "missing_textures.csv"), "w",
                         encoding="utf-8-sig", newline="") as fh:
                w = csv.writer(fh)
                w.writerow(["texture"])
                for t in sorted(missing):
                    w.writerow([t])

    if not args.dry_run:
        with io.open(os.path.join(OUT, "matmap.csv"), "w", encoding="utf-8", newline="\n") as fh:
            fh.write("material,albedo,normal,ma\n")
            for m in sorted(all_mats):
                a, n, ma = mats.get(m, ("", "", ""))
                fh.write("%s,%s,%s,%s\n" % (m, a, n, ma))
        with io.open(os.path.join(OUT, "staging_summary.csv"), "w",
                     encoding="utf-8-sig", newline="") as fh:
            w = csv.writer(fh)
            w.writerow(["class", "zh", "models", "with_trf", "missing", "trf_MB", "batches"])
            w.writerows(summary)
        print("done ->", OUT)


if __name__ == "__main__":
    main()
