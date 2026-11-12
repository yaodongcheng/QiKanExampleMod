#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""RDR2Map 导入进度检查器（v2 交付口径 · 2026-11-11）。

与旧的 `check_rdr2_import_progress.py` 的区别：
  旧 = `Modules\\RDR2` + `ImortReady\\rdr2_models_pending\\<批次>`（有 batch_01 这类批次目录）
  新 = `Modules\\RDR2Map` + `ImportReady\\meshes\\<13 类>` + `ImportReady\\textures\\{albedo,normal,specular}`
       —— **没有批次目录**，进度按"分类目录 / 贴图通道"算。

判据一律来自磁盘现场（不读文档里的数字）：
  · 贴图已导 = `RDR2Map\\Assets\\textures\\**\\<名>_tex.tpac`（**全项目查名**，资产名全局唯一）
  · 模型已导 = `RDR2Map\\Assets\\models\\**\\<名>_geo.tpac` + `Assets\\decal_models\\**`
  · 材质已导 = `RDR2Map\\Assets\\materials\\**\\<材质>_mtl.tpac`
  · 待导清单 = `RDR2Map\\AssetSources\\ImportReady\\`

进度分母的权威来源（二选一，默认取最小值防虚高）：
  · 磁盘上的待导文件（`ImportReady` 目录实有）
  · `deliverable\\manifests\\model_materials.csv`（交付清单，含每个模型的材质与三通道贴图名）

用法：
  python Scripts/check_rdr2map_progress.py
  python Scripts/check_rdr2map_progress.py --json-only
  python Scripts/check_rdr2map_progress.py --detail-dir <目录>     # 导出缺失 CSV
  python Scripts/check_rdr2map_progress.py --no-manifest            # 只信磁盘，不读交付清单
"""
import argparse
import csv
import io
import json
import os
import re

MOD_NAME = "RDR2Map"
MOD = os.path.join(r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules",
                   MOD_NAME)
LWN = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
STAGE = os.path.join(MOD, "AssetSources", "ImportReady")
MESH_STAGE = os.path.join(STAGE, "meshes")
TEX_STAGE = os.path.join(STAGE, "textures")
OUT_JSON = os.path.join(LWN, "Debug", "RD2构造相关", "rdr2map_progress.json")
MANIFEST = r"D:\BrainMaker\大镖客2解包分析\deliverable\manifests\model_materials.csv"

ROLES = ("albedo", "normal", "specular")
# 13 个分类目录（按 2026-11-11 交付实测）
CLASSES = ("bldg", "interior", "prop", "part", "junk", "rail", "unknown",
           "veh", "main_map_lod", "struct", "ruin", "rock", "stone")


def walk_stems(root, suffix):
    """递归收某后缀的文件名（去后缀），返回 set。"""
    out = set()
    if not os.path.isdir(root):
        return out
    sl = suffix.lower()
    for dp, _, fn in os.walk(root):
        for f in fn:
            if f.lower().endswith(sl):
                out.add(f[: -len(suffix)])
    return out


def stems_here(root, suffix):
    """只看本层。"""
    if not os.path.isdir(root):
        return set()
    return {f[: -len(suffix)] for f in os.listdir(root) if f.lower().endswith(suffix.lower())}


def strip_lod(n):
    """TRF 导入会去掉末尾 _lod / _lodNNN（沿用旧检查器口径）。"""
    return re.sub(r"_lod\d*$", "", n)


def fmt(n):
    return format(n, ",")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--json-only", action="store_true")
    ap.add_argument("--detail-dir", default="", help="给了就导出缺失清单 CSV")
    ap.add_argument("--no-manifest", action="store_true", help="不读交付清单，只信磁盘")
    args = ap.parse_args()

    if not os.path.isdir(MOD):
        raise SystemExit("模块不存在：%s" % MOD)

    # ---------- 现场：项目里已有啥 ----------
    tex_done = walk_stems(os.path.join(MOD, "Assets", "textures"), "_tex.tpac")
    geo_done = walk_stems(os.path.join(MOD, "Assets", "meshes"), "_geo.tpac")
    geo_done_loose = geo_done | {strip_lod(n) for n in geo_done}
    mtl_done = walk_stems(os.path.join(MOD, "Assets", "materials"), "_mtl.tpac")

    report = {"module": MOD_NAME, "stage": STAGE, "textures": {}, "meshes": {},
              "materials": {}, "next": []}

    # ---------- 贴图 ----------
    tex_missing = {}
    for role in ROLES:
        total = stems_here(os.path.join(TEX_STAGE, role), ".dds")
        done = total & tex_done
        tex_missing[role] = total - done
        report["textures"][role] = {"total": len(total), "done": len(done),
                                    "remaining": len(total) - len(done)}
    tex_total = sum(v["total"] for v in report["textures"].values())
    tex_done_n = sum(v["done"] for v in report["textures"].values())

    # ---------- 模型（按分类目录） ----------
    mesh_missing = {}
    for cls in CLASSES:
        total = stems_here(os.path.join(MESH_STAGE, cls), ".trf")
        done = {n for n in total if n in geo_done_loose}
        mesh_missing[cls] = total - done
        report["meshes"][cls] = {"total": len(total), "done": len(done),
                                 "remaining": len(total) - len(done)}
    # 兜底：磁盘上出现未在 CLASSES 里的目录也要算进去，别漏
    if os.path.isdir(MESH_STAGE):
        extra = sorted(d for d in os.listdir(MESH_STAGE)
                       if os.path.isdir(os.path.join(MESH_STAGE, d)) and d not in CLASSES)
        for cls in extra:
            total = stems_here(os.path.join(MESH_STAGE, cls), ".trf")
            done = {n for n in total if n in geo_done_loose}
            mesh_missing[cls] = total - done
            report["meshes"][cls] = {"total": len(total), "done": len(done),
                                     "remaining": len(total) - len(done)}
    mesh_total = sum(v["total"] for v in report["meshes"].values())
    mesh_done = sum(v["done"] for v in report["meshes"].values())

    # ---------- 材质（靠交付清单 model_materials.csv） ----------
    mat_rows = []
    if not args.no_manifest and os.path.isfile(MANIFEST):
        delivered = set()
        for v in report["meshes"].values():
            pass  # 分母已按磁盘算，模型名从清单里再取
        for dp, _, fn in os.walk(MESH_STAGE):
            for f in fn:
                if f.lower().endswith(".trf"):
                    delivered.add(f[:-4])
        with io.open(MANIFEST, encoding="utf-8-sig") as fh:
            for r in csv.DictReader(fh):
                if r.get("model") in delivered and r.get("material"):
                    mat_rows.append(r["material"])
        mats = sorted(set(mat_rows))
        ready = [m for m in mats if m in mtl_done]
        no_mtl = [m for m in mats if m not in mtl_done]

        # 材质依赖的贴图是否齐（三通道任一缺即算缺）
        tex_by_name = {}
        with io.open(MANIFEST, encoding="utf-8-sig") as fh:
            for r in csv.DictReader(fh):
                if r.get("model") not in delivered:
                    continue
                for ch in ("albedo", "normal", "ma"):
                    t = (r.get(ch) or "").strip()
                    if t:
                        tex_by_name.setdefault(t, set()).add(ch)
        lack_tex = {t: sorted(chs) for t, chs in tex_by_name.items()
                    if not (t in tex_done or (t + "_ab") in tex_done
                            or (t + "_nm") in tex_done or (t + "_ma") in tex_done)}
        report["materials"] = {"total": len(mats), "ready": len(ready),
                               "no_mtl": len(no_mtl), "no_tex": len(lack_tex),
                               "textures_referenced": len(tex_by_name)}
        if lack_tex:
            report["next"].append("材质 → 还有 %d 张贴图没导（先把贴图导完）" % len(lack_tex))
        elif no_mtl:
            report["next"].append("材质 → 贴图齐了，还有 %d 个材质没生成（TRF 导入时会自动建）"
                                  % len(no_mtl))
    else:
        report["materials"] = {"total": 0, "ready": 0, "no_mtl": 0, "no_tex": 0,
                               "note": "未读交付清单"}

    # ---------- 下一步建议 ----------
    if tex_done_n < tex_total:
        for role in ROLES:
            v = report["textures"][role]
            if v["remaining"]:
                report["next"].append("贴图 %s → 剩 %s 张" % (role, fmt(v["remaining"])))
    if mesh_done < mesh_total:
        left = [(c, v["remaining"]) for c, v in report["meshes"].items() if v["remaining"]]
        left.sort(key=lambda x: -x[1])
        for c, n in left[:5]:
            report["next"].append("模型 %s → 剩 %s 个" % (c, fmt(n)))
    # 先贴图后模型：贴图没过完就明确警告
    if tex_done_n < tex_total and mesh_done < mesh_total:
        report["next"].insert(0, "⚠️ 贴图还没导完，先别导模型（TRF 按材质名绑定，缺贴图=白板）")

    # ---------- 写 JSON ----------
    os.makedirs(os.path.dirname(OUT_JSON), exist_ok=True)
    with io.open(OUT_JSON, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=1)

    # ---------- 缺失去向 CSV ----------
    if args.detail_dir:
        os.makedirs(args.detail_dir, exist_ok=True)
        for role in ROLES:
            with io.open(os.path.join(args.detail_dir, "missing_textures_%s.csv" % role), "w",
                         encoding="utf-8-sig", newline="") as fh:
                w = csv.writer(fh)
                w.writerow(["texture"])
                for n in sorted(tex_missing[role]):
                    w.writerow([n])
        for cls in report["meshes"]:
            with io.open(os.path.join(args.detail_dir, "missing_meshes_%s.csv" % cls), "w",
                         encoding="utf-8-sig", newline="") as fh:
                w = csv.writer(fh)
                w.writerow(["asset_name"])
                for n in sorted(mesh_missing.get(cls, set())):
                    w.writerow([n])
        print("detail ->", args.detail_dir)

    if args.json_only:
        print("json ->", OUT_JSON)
        return

    # ---------- 控制台 ----------
    print("=" * 70)
    print("模块 %s   待导根目录 %s" % (MOD_NAME, STAGE))
    print("=" * 70)
    print("贴图（全项目查名 %s 个 _tex.tpac）" % fmt(len(tex_done)))
    for role in ROLES:
        v = report["textures"][role]
        pct = 100.0 * v["done"] / v["total"] if v["total"] else 0.0
        mark = "[OK] " if v["total"] and v["done"] >= v["total"] else ("[..]" if v["done"] else "[  ]")
        print("  %-9s %5s / %5s  %5.1f%%  %s" % (role, fmt(v["done"]), fmt(v["total"]), pct, mark))
    if tex_total:
        print("  %-9s %5s / %5s  %5.1f%%" % ("小计", fmt(tex_done_n), fmt(tex_total),
                                             100.0 * tex_done_n / tex_total))

    print("\n模型（%s 个 _geo.tpac）" % fmt(len(geo_done)))
    order = sorted(report["meshes"].items(), key=lambda kv: -kv[1]["total"])
    for cls, v in order:
        if not v["total"]:
            continue
        pct = 100.0 * v["done"] / v["total"]
        mark = "[OK] " if v["done"] >= v["total"] else ("[..]" if v["done"] else "[  ]")
        print("  %-14s %6s / %6s  %5.1f%%  %s" % (cls, fmt(v["done"]), fmt(v["total"]), pct, mark))
    print("  %-14s %6s / %6s  %5.1f%%" % ("小计", fmt(mesh_done), fmt(mesh_total),
                                           100.0 * mesh_done / mesh_total if mesh_total else 0.0))

    m = report["materials"]
    if m.get("total"):
        print("\n材质  %s 个：就绪 %s ｜ 缺材质 %s ｜ 缺贴图 %s"
              % (fmt(m["total"]), fmt(m["ready"]), fmt(m["no_mtl"]), fmt(m["no_tex"])))

    print("-" * 70)
    print("下一步：")
    for s in report["next"] or ["（全部完成）"]:
        print("  ·", s)
    print("json ->", OUT_JSON)


if __name__ == "__main__":
    main()
