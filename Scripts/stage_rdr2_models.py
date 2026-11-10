#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 RDR2 全世界的模型 TRF / 贴图 分装进 ImortReady（**全局模型库，与子场景无关**）。

为什么全局：模型是跨场景复用的（同一栋房子在 3 个镇出现），按场景切会重复导入；
贴图同理。**56 个场景的摆放表全部扫一遍，按模型名去重**。

输出（Modules/RDR2/AssetSources/ImortReady/rdr2_models_pending/）—— 只含**尚未导入编辑器**的模型：
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
OUT = os.path.join(MOD, "AssetSources", "ImortReady", "rdr2_models_pending")

IMPORT_CLASSES = [
    ("bldg", "建筑"), ("part", "建筑部件"), ("prop", "道具"), ("junk", "杂物堆"),
    ("veh", "载具"), ("struct", "桥梁结构"), ("rail", "铁路"), ("rail_decal", "铁路贴花"),
    ("road", "道路"), ("decal", "地面贴花"), ("veg", "植被"), ("rock", "岩石地形"),
    ("stone", "石块"), ("ruin", "废墟"), ("bldg_decal", "建筑贴花"),
    # 2026-11-10 用户裁定：室内贴花 / 轨道贴花 也是游戏里看得见的，一起导
    ("int_decal", "室内贴花"), ("track_decal", "轨道贴花"),
    # 2026-11-10 用户裁定：子场景活动区外圈的远景件要导，放进同一个 main_map_lod 目录
    ("lod", "远景碎片（子场景外圈）"),
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
    ap.add_argument("--out", default="",
                    help="暂存根目录（默认 ImortReady\\rdr2_models_pending = 只含未导入的模型）")
    ap.add_argument("--include-imported", action="store_true",
                    help="连编辑器里已导过的也装（默认跳过，避免重复导入撞同名）")
    ap.add_argument("--rebuild-matmap", action="store_true",
                    help="只按现有批次目录里的 models.csv 重建 matmap.csv（不拷贝任何文件）")
    args = ap.parse_args()

    global OUT
    if args.out:
        OUT = os.path.abspath(args.out)

    want = [c for c, _ in IMPORT_CLASSES]
    if args.classes:
        want = [c.strip() for c in args.classes.split(",") if c.strip()]
    zh = dict(IMPORT_CLASSES)

    # 🔴 2026-11-10：已导过的资产跳过 —— 按名字扫编辑器里的 *_geo.tpac。
    # 目的：全量重装时只装没导过的，已导的原地不动（不重导、不撞同名、不白费昨晚的活）。
    have_assets = set()
    if not args.include_imported:
        for sub in ("models", "decal_models"):
            base = os.path.join(MOD, "Assets", sub)
            for root, _dirs, files in os.walk(base):
                for f in files:
                    if f.endswith("_geo.tpac"):
                        have_assets.add(f[:-len("_geo.tpac")])
        print("编辑器里已有网格资产 %d 个（本次自动跳过）" % len(have_assets))

    # 🔴 2026-11-10：hash→名字 原来用 model_manifest.csv（只有 10,893 行，只覆盖
    # 44,728 个模型里的 7,678 个）⇒ 解析不到的摆放被静默丢掉（bldg_decal 316 只进 62、
    # watertile 323 只进 17、track_decal 6 进 0）。改用全量库存表（44,728 行，全覆盖）。
    hash2name = {(r.get("model_hash") or "").lower().replace("0x", ""): r.get("name", "")
                 for r in load(os.path.join(DELIV, "manifests", "asset_inventory_models.csv"))}
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

    # 室内专用模型清单：2026-11-10 用户裁定「interior 分类保留」——
    # 名单里的模型一律归 interior，**覆盖**场景多数类（hash 表修好后它们本来会被并进 part）。
    interior_names = set()
    ip = os.path.join(DELIV, "manifests", "interior_only_models.txt")
    if os.path.isfile(ip):
        interior_names = {s.strip() for s in
                          io.open(ip, encoding="utf-8").read().replace("\n", ",").split(",")
                          if s.strip()}

    byname = collections.defaultdict(list)
    forced = 0
    for n, c in cls_cnt.items():
        k = c.most_common(1)[0][0]
        if "interior" in want and n in interior_names and k != "interior":
            k = "interior"
            forced += 1
        byname[k].append(n)
    if "interior" in want and interior_names:
        extra = [n for n in interior_names if n not in cls_cnt]
        byname["interior"].extend(extra)
        print("室内名单 %d 个 → 归 interior（覆盖场景多数类 %d 个，无摆放补入 %d 个）"
              % (len(interior_names), forced, len(extra)))

    os.makedirs(OUT, exist_ok=True)
    # 目标目录映射（用户 2026-11-10 裁定）：子场景远景并进 main_map_lod，不另开目录；
    # 贴花 5 类进 Assets\decal_models\<类>（02 文档 §0.4 口径）；其余进 Assets\models\<类>。
    DEST = {"lod": "models\\main_map_lod",
            "decal": "decal_models\\decal", "road": "decal_models\\road",
            "bldg_decal": "decal_models\\bldg_decal", "rail_decal": "decal_models\\rail_decal",
            "track_decal": "decal_models\\track_decal", "int_decal": "decal_models\\int_decal"}
    batch_rows = []
    summary, all_mats = [], set()
    for c in want:
        names = [n for n in sorted(byname.get(c, [])) if n not in have_assets]
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
            batch_rows.append([sub, DEST.get(c, "models\\" + c), "mesh", len(chunk)])
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

    # 批次清单：每行「暂存目录 → 编辑器目标目录 → 类型 → 个数」，导入脚本照它逐批跑
    if batch_rows:
        out_csv = os.path.join(OUT, "batches.csv")
        if not args.dry_run:
            with io.open(out_csv, "w", encoding="utf-8-sig", newline="") as fh:
                w = csv.writer(fh)
                w.writerow(["batch_dir", "dest_assets_rel", "kind", "count"])
                w.writerows(batch_rows)
        print("批次清单 -> %s（%d 批%s）" % (out_csv, len(batch_rows), "" if not args.dry_run else "，dry-run 未落盘"))

    # ---- 贴图 / matmap ----
    tex = set()
    for m in all_mats:
        for t in mats.get(m, ("", "", "")):
            if t:
                tex.add(t)
    # 🔴 2026-11-10：有些贴图不在 textures_export.csv 索引里，但 PNG 其实趴在
    # `deliverable/textures/{alb,nm,ma}/` 池子里（索引只收了"导入子集"用到的）。
    # 先按索引取；取不到就按名字去池子里捞，仍取不到才算"缺源"。
    POOL = {r: os.path.join(DELIV, "textures", r) for r in ("alb", "nm", "ma")}

    def resolve_tex(t):
        src = texdest.get(t)
        if src and os.path.isfile(src):
            return texrole.get(t, ""), src
        for role in ("alb", "nm", "ma"):
            p = os.path.join(POOL[role], t + ".png")
            if os.path.isfile(p):
                return role, p
        return None, None

    resolved, missing_tex = {}, []
    for t in tex:
        role, src = resolve_tex(t)
        if src:
            resolved[t] = (role, src)
        else:
            missing_tex.append(t)

    # 已导过的贴图不再复制（全量重装时只补缺的）
    have_tex = set()
    if not args.include_imported:
        for root, _dirs, files in os.walk(os.path.join(MOD, "Assets", "textures")):
            for f in files:
                if f.endswith("_tex.tpac"):
                    have_tex.add(f[:-len("_tex.tpac")])
    todo_tex = {t: v for t, v in resolved.items() if t not in have_tex}
    # 贴图按**角色**分三目录（用户要求 2026-11-09）：漫反射 / 法线 / 高光(ma)
    ROLE_DIR = {"alb": "albedo", "nm": "normal", "ma": "specular"}
    if not args.no_textures:
        td = os.path.join(OUT, "textures")
        if not args.dry_run:
            for sub in ROLE_DIR.values():
                os.makedirs(os.path.join(td, sub), exist_ok=True)
            for t in sorted(todo_tex):
                role, src = todo_tex[t]
                sub = ROLE_DIR.get(role, "")
                if not sub:
                    sub = "normal" if t.endswith("_nm") else ("specular" if t.endswith("_ma") else "albedo")
                dst = os.path.join(td, sub, t + ".png")
                if not os.path.isfile(dst):
                    shutil.copy2(src, dst)
    print("贴图 需要 %d 张 ｜ 已导 %d ｜ 待导 %d ｜ 缺源 %d"
          % (len(resolved) + len(missing_tex), len(resolved) - len(todo_tex), len(todo_tex), len(missing_tex)))

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
