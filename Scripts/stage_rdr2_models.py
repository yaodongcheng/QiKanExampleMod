#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 RDR2 全世界的模型 TRF / 贴图 分装进 ImortReady（**全局模型库，与子场景无关**）。

为什么全局：模型是跨场景复用的（同一栋房子在 3 个镇出现），按场景切会重复导入；
贴图同理。**56 个场景的摆放表全部扫一遍，按模型名去重**。

输出（Modules/RDR2/AssetSources/ImortReady/rdr2_models/）：
  textures/            全世界用到的 albedo/normal/ma（去重，同名 .png）
  matmap.csv           material,albedo,normal,ma —— 喂 `tpaccli editormeta --genmats3`
  <class>/             按**多数类**归类的 TRF；>560 个自动切 <class>_01/_02…
  staging_summary.csv  每类：模型数 / 有 TRF / 缺 / 体积 / 批次
  missing_<class>.csv  缺 TRF 的模型名

口径：
  · 类名/判定 = `D:\\BrainMaker\\大镖客2解包分析\\tools\\class_rules.py`
  · 导入类 = `Scripts/export_rdr2_trf_from_scenes.py` 的 DEFAULT_CLASSES
    （bldg/part/prop/junk/veg/rock/stone/struct/ruin/veh/rail/rail_decal/road/decal/unknown）
    **去掉 lod_out**（LOD 走 `rdr2_lod_fragments` 那套，已经做完）
  · 一个模型在多个场景里当过不同类 → 取**出现次数最多**的那个类
  · 每批 ≤560 个网格（ModKit 实测，批间重启）

用法：
  python Scripts/stage_rdr2_models.py                      # 全量
  python Scripts/stage_rdr2_models.py --classes bldg,part  # 只装指定类
  python Scripts/stage_rdr2_models.py --no-textures
"""
import argparse
import collections
import csv
import glob
import io
import json
import os
import shutil

DELIV = r"D:\BrainMaker\大镖客2解包分析\deliverable"
MOD = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\RDR2"
OUT = os.path.join(MOD, "AssetSources", "ImortReady", "rdr2_models")

IMPORT_CLASSES = [
    ("bldg", "建筑"), ("part", "建筑部件"), ("prop", "道具"), ("junk", "杂物堆"),
    ("veh", "载具"), ("struct", "桥梁结构"), ("rail", "铁路"), ("rail_decal", "铁路贴花"),
    ("road", "道路"), ("decal", "地面贴花"), ("veg", "植被"), ("rock", "岩石地形"),
    ("stone", "石块"), ("ruin", "废墟"), ("watertile", "水面块"), ("bldg_decal", "建筑贴花"),
    ("unknown", "未分类"), ("interior", "室内（MLO 专用模型）"), ("misc", "无类别"),
]
BATCH = 560


def load(path):
    return list(csv.DictReader(io.open(path, encoding="utf-8-sig")))


def resolve_trf(model_row):
    """TRF 可能在 models\\ 也可能在 models_extra\\（2026-11-09 教训：只查 models\\ 会把
    bldg_decal / watertile 整类误判成"缺 TRF"）。"""
    if not model_row or not model_row.get("trf"):
        return None
    for base in ("models", "models_extra"):
        p = os.path.join(DELIV, base, model_row["trf"])
        if os.path.isfile(p):
            return p
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--classes", default="", help="逗号分隔；默认导入集")
    ap.add_argument("--scenes", default="", help="逗号分隔场景 id；默认全部")
    ap.add_argument("--batch", type=int, default=BATCH)
    ap.add_argument("--no-textures", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--rebuild-matmap", action="store_true",
                    help="只按现有批次目录里的 models.csv 重建 matmap.csv（不拷贝任何文件）")
    args = ap.parse_args()

    want = [c for c, _ in IMPORT_CLASSES]
    if args.classes:
        want = [c.strip() for c in args.classes.split(",") if c.strip()]
    zh = dict(IMPORT_CLASSES)

    hash2name = {(r.get("hash") or "").lower().replace("0x", ""): r.get("name", "")
                 for r in load(os.path.join(DELIV, "manifests", "model_manifest.csv"))}
    models = {}
    for fn in ("models_export.csv", "models_export_extra.csv"):
        p = os.path.join(DELIV, "manifests", fn)
        if os.path.isfile(p):
            for r in load(p):
                models.setdefault(r["name"], r)

    mats, mat_of = {}, collections.defaultdict(set)
    for r in load(os.path.join(DELIV, "manifests", "model_materials.csv")):
        mats.setdefault(r["material"], (r.get("albedo", ""), r.get("normal", ""), r.get("ma", "")))
        mat_of[r["model"]].add(r["material"])

    texdest, texrole = {}, {}
    for r in load(os.path.join(DELIV, "manifests", "textures_export.csv")):
        if r.get("name") and r.get("dest"):
            texdest[r["name"]] = r["dest"]
            texrole[r["name"]] = r.get("role", "")

    # ---- 只重建 matmap（分批跑 --classes 会覆盖掉别的批次，用这个补齐）----
    if args.rebuild_matmap:
        need = set()
        for b in sorted(os.listdir(OUT)):
            d = os.path.join(OUT, b)
            mp = os.path.join(d, "models.csv")
            if os.path.isdir(d) and os.path.isfile(mp):
                for r in csv.DictReader(io.open(mp, encoding="utf-8-sig")):
                    need |= mat_of.get(r["asset_name"], set())
        mpath = os.path.join(OUT, "matmap.csv")
        merged = {}
        if os.path.isfile(mpath):
            for r in csv.DictReader(io.open(mpath, encoding="utf-8")):
                merged[r["material"]] = (r["albedo"], r["normal"], r["ma"])
        for m in need:
            merged[m] = mats.get(m, ("", "", ""))
        with io.open(mpath, "w", encoding="utf-8", newline="\n") as fh:
            fh.write("material,albedo,normal,ma\n")
            for m in sorted(merged):
                fh.write("%s,%s,%s,%s\n" % ((m,) + merged[m]))
        print("rebuild matmap: %d 条（批次依赖 %d）-> %s" % (len(merged), len(need), mpath))
        return

    # ---- 扫全部场景，按模型统计"多数类" ----
    files = sorted(glob.glob(os.path.join(DELIV, "blender", "scenes", "placement_*.jsonl")))
    if args.scenes:
        keep = {s.strip() for s in args.scenes.split(",")}
        files = [p for p in files if os.path.basename(p)[len("placement_"):-len(".jsonl")] in keep]
    cls_cnt = collections.defaultdict(collections.Counter)
    unresolved = 0
    for p in files:
        for line in io.open(p, encoding="utf-8"):
            d = json.loads(line)
            n = hash2name.get((d.get("model_hash") or "").lower().replace("0x", ""))
            if n:
                cls_cnt[n][d.get("cls") or "misc"] += 1
            else:
                unresolved += 1
    print("场景 %d 个；模型 %d 个；未解析实例 %d" % (len(files), len(cls_cnt), unresolved))

    # 室内专用模型：不在 placement 里，来自 interior_only_models.txt（一行逗号分隔）
    if "interior" in want:
        ip = os.path.join(DELIV, "manifests", "interior_only_models.txt")
        if os.path.isfile(ip):
            names = {s.strip() for s in io.open(ip, encoding="utf-8").read().split(",") if s.strip()}
            for n in names:
                cls_cnt.setdefault(n, collections.Counter())["interior"] += 1
            print("室内专用模型 %d 个" % len(names))

    byname = collections.defaultdict(list)
    for n, c in cls_cnt.items():
        byname[c.most_common(1)[0][0]].append(n)

    os.makedirs(OUT, exist_ok=True)
    summary, all_mats = [], set()
    for c in want:
        names = sorted(byname.get(c, []))
        ok, miss = [], []
        for n in names:
            r = models.get(n)
            p = resolve_trf(r)
            (ok if (p and os.path.isfile(p)) else miss).append(n)
        ok.sort(key=lambda n: -int(models[n].get("faces") or 0))
        chunks = [ok[i:i + args.batch] for i in range(0, len(ok), args.batch)]
        total = 0
        for i, chunk in enumerate(chunks):
            sub = c if len(chunks) == 1 else "%s_%02d" % (c, i + 1)
            d = os.path.join(OUT, sub)
            if not args.dry_run:
                os.makedirs(d, exist_ok=True)
            for n in chunk:
                src = resolve_trf(models[n])
                if not args.dry_run:
                    shutil.copy2(src, os.path.join(d, os.path.basename(src)))
                total += os.path.getsize(src)
                all_mats |= mat_of.get(n, set())
            if not args.dry_run:
                with io.open(os.path.join(d, "models.csv"), "w",
                             encoding="utf-8-sig", newline="") as fh:
                    w = csv.writer(fh)
                    w.writerow(["asset_name", "trf", "faces"])
                    for n in chunk:
                        w.writerow([n, os.path.basename(models[n]["trf"]), models[n].get("faces", "")])
        summary.append([c, zh.get(c, c), len(names), len(ok), len(miss), round(total / 1e6, 1), len(chunks)])
        print("%-12s 模型 %5d 有TRF %5d 缺 %4d 批次 %d  %8.1f MB"
              % (c, len(names), len(ok), len(miss), len(chunks), total / 1e6))
        if miss and not args.dry_run:
            with io.open(os.path.join(OUT, "missing_%s.csv" % c), "w",
                         encoding="utf-8-sig", newline="") as fh:
                w = csv.writer(fh)
                w.writerow(["asset_name", "reason"])
                for n in miss:
                    w.writerow([n, "no_trf" if n in models else "not_in_models_export"])

    # ---- 贴图 / matmap ----
    tex = set()
    for m in all_mats:
        for t in mats.get(m, ("", "", "")):
            if t:
                tex.add(t)
    missing_tex = [t for t in tex if t not in texdest or not os.path.isfile(texdest[t])]
    # 贴图按**角色**分三目录（用户要求 2026-11-09）：漫反射 / 法线 / 高光(ma)
    ROLE_DIR = {"alb": "albedo", "nm": "normal", "ma": "specular"}
    if not args.no_textures:
        td = os.path.join(OUT, "textures")
        if not args.dry_run:
            for sub in ROLE_DIR.values():
                os.makedirs(os.path.join(td, sub), exist_ok=True)
            for t in sorted(tex):
                if t in missing_tex:
                    continue
                sub = ROLE_DIR.get(texrole.get(t, ""), "")
                if not sub:
                    sub = "normal" if t.endswith("_nm") else ("specular" if t.endswith("_ma") else "albedo")
                dst = os.path.join(td, sub, t + ".png")
                if not os.path.isfile(dst):
                    shutil.copy2(texdest[t], dst)
    print("贴图 %d 张（缺源 %d）" % (len(tex) - len(missing_tex), len(missing_tex)))

    if not args.dry_run:
        # 🔴 matmap 必须**合并写**：分批跑（--classes）时会只累到本次的材质，
        #    直接覆盖会把别的批次（如 interior）的材质冲掉（2026-11-09 实锤）。
        mpath = os.path.join(OUT, "matmap.csv")
        merged = {}
        if os.path.isfile(mpath):
            for r in csv.DictReader(io.open(mpath, encoding="utf-8")):
                merged[r["material"]] = (r["albedo"], r["normal"], r["ma"])
        for m in all_mats:
            merged[m] = mats.get(m, ("", "", ""))
        with io.open(mpath, "w", encoding="utf-8", newline="\n") as fh:
            fh.write("material,albedo,normal,ma\n")
            for m in sorted(merged):
                fh.write("%s,%s,%s,%s\n" % ((m,) + merged[m]))
        print("matmap: %d 条（本次 +%d）" % (len(merged), len(all_mats - set(merged) | all_mats)))
        with io.open(os.path.join(OUT, "staging_summary.csv"), "w",
                     encoding="utf-8-sig", newline="") as fh:
            w = csv.writer(fh)
            w.writerow(["class", "zh", "models", "with_trf", "missing", "trf_MB", "batches"])
            w.writerows(summary)
        print("done ->", OUT)


if __name__ == "__main__":
    main()
