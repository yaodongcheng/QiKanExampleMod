#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""RDR2Map 材质批量生成（v2 口径 · 2026-11-11）。

对应旧流程的第二步「材质构建」，但目标模块是 RDR2Map、材质表是 matmap_v2.csv。

原理（沿用旧 `gen_rdr2_sdn_materials3.py` 的经验）：
  tpaccli editormeta --genmats3 需要**贴图的 _tex.tpac 实物**来读 GUID，
  所以先把 `Assets\\textures\\**\\*_tex.tpac` 全部复制进一个扁平 staging 目录，
  再喂 `--genmats3 <staging> --mtlref <3槽参考材质> --matmap <matmap.csv>`。

前置条件（缺一不可）：
  1. 贴图已导入 `RDR2Map\\Assets\\textures\\`（先跑 check_rdr2map_progress.py 看是否 23,434/23,434）
  2. `rdr2_white` 与 `rdr2_flat_normal` 两张中性贴图已在 Assets 里
  3. `matmap_v2.csv` 里每个 albedo 都能在 Assets 里找到

用法：
  python Scripts/gen_rdr2map_materials.py --dry-run     # 只体检，不生成
  python Scripts/gen_rdr2map_materials.py               # 真跑
  python Scripts/gen_rdr2map_materials.py --limit 50    # 先试 50 条
"""
import argparse
import csv
import io
import os
import shutil
import subprocess
import sys

LWN = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
MOD = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2Map"
TOOL = os.path.join(LWN, "tools", "tpactool", "TpacToolCLI", "bin", "Release", "net9.0", "tpaccli.exe")
MATMAP = r"D:\BrainMaker\大镖客2解包分析\deliverable\compress\matmap_v2.csv"
REF = os.path.join(LWN, "Debug", "RD2构造相关", "mats_stage",
                   "nbx_wall_brick_scan_dirtlight_ab_mtl.tpac")
REF_FALLBACK = os.path.join(MOD.replace("RDR2Map", "RDR2"), "Assets", "materials", "base",
                            "nbx_wall_brick_scan_dirtlight_ab_mtl.tpac")
MAT_OUT = os.path.join(MOD, "Assets", "materials", "v2")
STAGE_DIR = os.path.join(os.environ["TEMP"], "rdr2_slim", "mat_stage_tex")
NEEDED_HELPERS = ("rdr2_white", "rdr2_flat_normal")


def walk_tpac(root):
    """{小写名: 全路径}，名去掉 _tex.tpac 后缀。"""
    out = {}
    if not os.path.isdir(root):
        return out
    for dp, _, fn in os.walk(root):
        for f in fn:
            if f.lower().endswith("_tex.tpac"):
                out[f[: -len("_tex.tpac")].lower()] = os.path.join(dp, f)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--matmap", default=MATMAP)
    ap.add_argument("--out", default=MAT_OUT)
    args = ap.parse_args()

    for p, what in ((TOOL, "tpaccli"), (args.matmap, "matmap")):
        if not os.path.isfile(p):
            raise SystemExit("缺 %s：%s" % (what, p))
    ref = REF if os.path.isfile(REF) else REF_FALLBACK
    if not os.path.isfile(ref):
        raise SystemExit("缺 3 槽参考材质：%s" % REF)
    print("参考材质：%s" % ref)

    tex = walk_tpac(os.path.join(MOD, "Assets", "textures"))
    print("Assets 里的 _tex.tpac：%d 个" % len(tex))

    rows = list(csv.DictReader(io.open(args.matmap, encoding="utf-8-sig")))
    print("matmap 材质：%d 条" % len(rows))

    # ---- 体检 1：中性贴图 ----
    miss_helper = [h for h in NEEDED_HELPERS if h not in tex]
    if miss_helper:
        raise SystemExit("缺中性贴图（先在 ModKit 里导入）：%s" % ", ".join(miss_helper))

    # ---- 体检 2：每条材质的 albedo 必须存在 ----
    bad = [r["material"] for r in rows
           if r.get("albedo") and r["albedo"].lower() not in tex]
    if bad:
        print("\n!! 以下 %d 个材质的 albedo 贴图未导入：" % len(bad))
        for b in bad[:30]:
            print("   " + b)
        raise SystemExit("贴图没导完，禁止生成材质（会得到白板材质）")

    # ---- 体检 3：normal / ma 缺的允许留空，只统计 ----
    n_no_n = sum(1 for r in rows if not (r.get("normal") or "").strip())
    n_no_m = sum(1 for r in rows if not (r.get("ma") or "").strip())
    print("normal 留空 %d 条 ｜ ma 留空 %d 条（genmats3 允许，用默认值）" % (n_no_n, n_no_m))

    if args.limit:
        rows = rows[: args.limit]
        print("--limit 生效：只做 %d 条" % len(rows))

    if args.dry_run:
        print("\n[dry-run] 体检通过，未生成。")
        return 0

    # ---- staging：把所有 _tex.tpac 扁平复制，供 genmats3 读 GUID ----
    if os.path.isdir(STAGE_DIR):
        shutil.rmtree(STAGE_DIR)
    os.makedirs(STAGE_DIR)
    for name, path in tex.items():
        shutil.copy2(path, os.path.join(STAGE_DIR, os.path.basename(path)))
    print("staged %d 个 _tex.tpac -> %s" % (len(os.listdir(STAGE_DIR)), STAGE_DIR))

    # ---- 生成本次使用的 matmap（可能被 --limit 截过）----
    use_map = os.path.join(STAGE_DIR, "_matmap_use.csv")
    with io.open(use_map, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("material,albedo,normal,ma\n")
        for r in rows:
            fh.write("%s,%s,%s,%s\n" % (r["material"], r.get("albedo", ""),
                                        r.get("normal", ""), r.get("ma", "")))

    # ---- 生成 ----
    os.makedirs(args.out, exist_ok=True)
    cmd = [TOOL, "editormeta", "--name", "dummy", "--out", os.path.join(args.out, "dummy"),
           "--genmats3", STAGE_DIR, "--mtlref", ref, "--matmap", use_map]
    print("跑：" + " ".join(cmd[:5]) + " ...")
    p = subprocess.run(cmd, capture_output=True, text=True)
    out = (p.stdout or "") + (p.stderr or "")
    print(out[-2000:] if out else "(无输出)")
    if p.returncode != 0:
        raise SystemExit("tpaccli 失败，返回码 %d" % p.returncode)

    got = [f for f in os.listdir(args.out) if f.endswith("_mtl.tpac")]
    print("\n生成材质 %d 个 -> %s" % (len(got), args.out))
    print("期望 %d 个" % len(rows))
    if len(got) < len(rows):
        print("⚠️ 少于期望，检查上面 tpaccli 的输出")
    return 0


if __name__ == "__main__":
    sys.exit(main())
