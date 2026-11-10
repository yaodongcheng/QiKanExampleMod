#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""导入进度检查器（实时算，不读文档里的数字）。

判据一律来自磁盘现场：
  · 贴图已导 = `Assets\\textures\\**\\<名>_tex.tpac`（**全项目查名**，资产名全局唯一、与目录无关）
  · 模型已导 = `Assets\\models\\**\\<名>_geo.tpac` + `Assets\\decal_models\\**\\<名>_geo.tpac`
  · 材质已导 = `Assets\\materials\\**\\<材质>_mtl.tpac`
  · 待导清单 = `AssetSources\\ImortReady\\rdr2_models\\` 下的批次目录（贴图 / 模型 / 贴花）

输出：
  · 控制台：每个角色 / 每个批次的 已导 / 总数 / 剩余，以及**下一批该导哪个**
  · `Debug\\RD2构造相关\\import_progress.json`：机器可读全量快照

用法：
  python Scripts/check_rdr2_import_progress.py
  python Scripts/check_rdr2_import_progress.py --json-only
"""
import argparse
import csv
import io
import json
import os
import re

MOD = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2"
LWN = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
STAGE = os.path.join(MOD, "AssetSources", "ImortReady", "rdr2_models_pending")
TEX_STAGE = os.path.join(STAGE, "textures")
OUT_JSON = os.path.join(LWN, "Debug", "RD2构造相关", "import_progress.json")

ROLES = [
    ("albedo", ["albedo", "_todo_albedo", "_already_imported_lod"]),
    # 2026-11-10：新暂存目录用 textures\normal / textures\specular（旧的是 _todo_*）
    ("normal", ["normal", "_todo_normal"]),
    ("specular", ["specular", "_todo_specular"]),
]
MODEL_CLASSES = ("bldg", "part", "prop", "junk", "veh", "veg", "rock", "rail", "rail_decal",
                 "struct", "ruin", "stone", "unknown", "misc", "interior")


def walk_files(root, suffix):
    out = set()
    if not os.path.isdir(root):
        return out
    for dp, _, fn in os.walk(root):
        for f in fn:
            if f.endswith(suffix):
                out.add(f[: -len(suffix)])
    return out


def png_names(path, recursive=False):
    """目录里的 PNG 名（默认只看本层；recursive=True 连子目录一起收）。"""
    if not os.path.isdir(path):
        return set()
    if recursive:
        return walk_files(path, ".png")
    return {f[:-4] for f in os.listdir(path) if f.endswith(".png")}


def batch_dirs(parent):
    return sorted(d for d in os.listdir(parent)
                  if os.path.isdir(os.path.join(parent, d)) and not d.startswith("_")) \
        if os.path.isdir(parent) else []


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--json-only", action="store_true")
    ap.add_argument("--detail-dir", default="", help="给了就导出缺失清单 CSV（贴图/模型/材质）")
    args = ap.parse_args()

    # ---- 现场：项目里已存在的资产名 ----
    tex_done = walk_files(os.path.join(MOD, "Assets", "textures"), "_tex.tpac")
    geo_done = walk_files(os.path.join(MOD, "Assets", "models"), "_geo.tpac") \
        | walk_files(os.path.join(MOD, "Assets", "decal_models"), "_geo.tpac")
    mtl_done = walk_files(os.path.join(MOD, "Assets", "materials"), "_mtl.tpac")
    # TRF 导入会去掉末尾 _lod / _lodNNN（见交接文档），比对时两种都试
    geo_done_stripped = {re.sub(r"_lod\d*$", "", n) for n in geo_done}

    report = {"textures": {}, "models": {}, "materials": {}, "next": []}

    # ---- 贴图 ----
    # staging 被清理（导完删了）时不再逐角色统计，直接报"已完成"
    tex_staging_gone = not os.path.isdir(TEX_STAGE)
    if tex_staging_gone:
        for role, _ in ROLES:
            report["textures"][role] = {"total": 0, "done": 0, "remaining": 0,
                                        "batches": [], "note": "staging 已清理"}
    else:
        for role, folders in ROLES:
            total, done, batches = set(), set(), []
            for sub in folders:
                p = os.path.join(TEX_STAGE, sub)
                names = png_names(p, recursive=True)
                total |= names
                done |= (names & tex_done)
                for b in batch_dirs(p):
                    bn = png_names(os.path.join(p, b))
                    total |= bn
                    bd = len(bn & tex_done)
                    batches.append({"batch": "%s\\%s" % (sub, b), "total": len(bn), "done": bd})
            rem = len(total) - len(done)
            report["textures"][role] = {"total": len(total), "done": len(done), "remaining": rem,
                                        "batches": batches}
            pend = [b for b in batches if b["done"] < b["total"]]
            if pend:
                report["next"].append("贴图 %s → %s（剩 %d 张）"
                                      % (role, pend[0]["batch"], pend[0]["total"] - pend[0]["done"]))

    # ---- 模型 / 贴花（批次目录 = 带 models.csv 的目录）----
    # 🔴 同一批模型会同时存在于 原批次 / _retryNN / _mesh_pending 等多个目录里，
    #    逐目录相加会把总数放大 2~4 倍。这里按**模型名全局去重**，只算第一次出现的目录。
    model_batches, seen_models = [], set()
    for b in sorted(os.listdir(STAGE)):
        d = os.path.join(STAGE, b)
        if not os.path.isdir(d):
            continue
        mp = os.path.join(d, "models.csv")
        if not os.path.isfile(mp):
            continue
        names = [r["asset_name"] for r in csv.DictReader(io.open(mp, encoding="utf-8-sig"))]
        uniq = [n for n in names if n not in seen_models]
        seen_models.update(uniq)
        dn = sum(1 for n in uniq if n in geo_done or re.sub(r"_lod\d*$", "", n) in geo_done_stripped)
        model_batches.append({"batch": b, "total": len(uniq), "done": dn,
                              "dup_skipped": len(names) - len(uniq)})
    report["models"]["unique_total"] = sum(x["total"] for x in model_batches)
    report["models"]["unique_done"] = sum(x["done"] for x in model_batches)
    report["models"]["batches"] = model_batches
    pend = [b for b in model_batches if b["done"] < b["total"]]
    if pend:
        report["next"].append("模型 → %s（剩 %d 个）" % (pend[0]["batch"], pend[0]["total"] - pend[0]["done"]))

    # ---- 材质（真门槛：材质文件 + 它依赖的三张贴图都得在）----
    matmap = os.path.join(STAGE, "matmap.csv")
    missing_tex_by_mat = {}
    if os.path.isfile(matmap):
        rows = list(csv.DictReader(io.open(matmap, encoding="utf-8")))
        ready, no_mtl, no_tex = 0, 0, 0
        for r in rows:
            m = r["material"]
            texs = [t for t in (r.get("albedo", ""), r.get("normal", ""), r.get("ma", "")) if t]
            lack = [t for t in texs if t not in tex_done]
            if lack:
                no_tex += 1
                missing_tex_by_mat[m] = lack
            elif m not in mtl_done:
                no_mtl += 1
            else:
                ready += 1
        report["materials"] = {"total": len(rows), "ready": ready,
                               "no_mtl": no_mtl, "no_tex": no_tex}
        if no_tex:
            report["next"].append("材质 → 还有 %d 条缺贴图（先把贴图导完）" % no_tex)
        elif no_mtl:
            report["next"].append("材质 → 贴图齐了，跑一次 genmats3（缺 %d 条材质）" % no_mtl)

    # ---- 输出 ----
    os.makedirs(os.path.dirname(OUT_JSON), exist_ok=True)
    with io.open(OUT_JSON, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=1)

    if args.detail_dir:
        os.makedirs(args.detail_dir, exist_ok=True)
        # 贴图：逐角色缺失
        for role, folders in ROLES:
            total, done = set(), set()
            for sub in folders:
                names = png_names(os.path.join(TEX_STAGE, sub), recursive=True)
                total |= names
                done |= (names & tex_done)
            with io.open(os.path.join(args.detail_dir, "missing_textures_%s.csv" % role), "w",
                         encoding="utf-8-sig", newline="") as fh:
                w = csv.writer(fh)
                w.writerow(["texture"])
                for n in sorted(total - done):
                    w.writerow([n])
        # 模型：逐批次缺失
        for b in model_batches:
            d = os.path.join(STAGE, b["batch"])
            names = [r["asset_name"] for r in csv.DictReader(io.open(os.path.join(d, "models.csv"),
                                                                    encoding="utf-8-sig"))]
            miss = [n for n in names
                    if n not in geo_done and re.sub(r"_lod\d*$", "", n) not in geo_done_stripped]
            with io.open(os.path.join(args.detail_dir, "missing_models_%s.csv" % b["batch"]), "w",
                         encoding="utf-8-sig", newline="") as fh:
                w = csv.writer(fh)
                w.writerow(["asset_name"])
                for n in miss:
                    w.writerow([n])
        # 材质：缺贴图 / 缺材质
        if os.path.isfile(matmap):
            with io.open(os.path.join(args.detail_dir, "missing_materials.csv"), "w",
                         encoding="utf-8-sig", newline="") as fh:
                w = csv.writer(fh)
                w.writerow(["material", "reason", "missing_textures"])
                for m, lack in sorted(missing_tex_by_mat.items()):
                    w.writerow([m, "no_texture", "|".join(lack)])
                rows = list(csv.DictReader(io.open(matmap, encoding="utf-8")))
                for r in rows:
                    mm = r["material"]
                    texs = [t for t in (r.get("albedo", ""), r.get("normal", ""), r.get("ma", "")) if t]
                    if not [t for t in texs if t not in tex_done] and mm not in mtl_done:
                        w.writerow([mm, "no_material", ""])
        print("detail ->", args.detail_dir)

    if args.json_only:
        print("json ->", OUT_JSON)
        return

    print("=" * 64)
    print("贴图（全项目查名）")
    for role, v in report["textures"].items():
        print("  %-9s 已导 %5d / %5d   剩 %5d" % (role, v["done"], v["total"], v["remaining"]))
        if v["total"] == 0:
            # ImortReady\rdr2_models\textures\ 已被清理（贴图导完后人删的）：
            # 分母来自那些切片目录，删了就变 0 —— 这里直接报项目里的实有数量。
            n_asset = len(walk_files(os.path.join(MOD, "Assets", "textures"), "_tex.tpac"))
            print("      [OK] 源批次目录已清理（已全部导完），项目内 _tex.tpac 合计 %d 个" % n_asset)
        if v["batches"]:
            btot = sum(b["total"] for b in v["batches"])
            bdone = sum(b["done"] for b in v["batches"])
            print("      批次小计 %5d / %5d   剩 %5d     （另有 %d 张已导但不在任何批次：名册/lod）"
                  % (bdone, btot, btot - bdone, v["done"] - bdone))
        for b in v["batches"]:
            mark = "[OK] " if b["done"] >= b["total"] else ("[..]" if b["done"] else "[  ]")
            print("      %-28s %4d / %4d  %s" % (b["batch"], b["done"], b["total"], mark))
    m = report["materials"]
    if m:
        print("材质  %d 条：就绪 %d ｜ 缺材质 %d ｜ 缺贴图 %d"
              % (m["total"], m["ready"], m["no_mtl"], m["no_tex"]))
    print("模型（按模型名全局去重）  %d / %d"
          % (report["models"]["unique_done"], report["models"]["unique_total"]))
    for b in model_batches:
        if b["total"] == 0:
            continue
        mark = "[OK] " if b["done"] >= b["total"] else ("[..]" if b["done"] else "[  ]")
        dup = ("  (重复跳过 %d)" % b["dup_skipped"]) if b.get("dup_skipped") else ""
        print("  %-14s %4d / %4d  %s%s" % (b["batch"], b["done"], b["total"], mark, dup))
    print("-" * 64)
    print("下一步：")
    for s in report["next"] or ["（全部完成）"]:
        print("  ·", s)
    print("json ->", OUT_JSON)


if __name__ == "__main__":
    main()
