#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""中断恢复：把某个批次里**还没导入的**内容抽成一个新批次（原批次只留已导入的）。

为什么需要：导入中断/崩溃后必须杀进程重试，但如果对原目录**全选重导**，
已导入的会全部撞同名（白等一轮，还可能触发"同名已存在"报错）。
所以恢复流程固定为：
    ① 杀进程 → ② 跑本脚本生成 `<批次>_retryNN` → ③ 只导这个新目录

判定（与 check_rdr2_import_progress.py 同源，全项目查名）：
  · 贴图已导 = `Assets\\textures\\**\\<名>_tex.tpac`
  · 模型已导 = `Assets\\models\\**\\<名>_geo.tpac` + `Assets\\decal_models\\**\\<名>_geo.tpac`

用法：
  python Scripts/repack_pending_batch.py --target "_todo_albedo\\batch_03"
  python Scripts/repack_pending_batch.py --target "part_02"
  python Scripts/repack_pending_batch.py --all            # 所有批次都检查一遍
  python Scripts/repack_pending_batch.py --target ... --dry-run
"""
import argparse
import csv
import io
import os
import re
import shutil

MOD = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2"
STAGE = os.path.join(MOD, "AssetSources", "ImortReady", "rdr2_models")
TEX_STAGE = os.path.join(STAGE, "textures")


def walk_names(root, suffix):
    out = set()
    if os.path.isdir(root):
        for dp, _, fn in os.walk(root):
            for f in fn:
                if f.endswith(suffix):
                    out.add(f[: -len(suffix)])
    return out


def tex_done():
    return walk_names(os.path.join(MOD, "Assets", "textures"), "_tex.tpac")


def model_done():
    return walk_names(os.path.join(MOD, "Assets", "models"), "_geo.tpac") \
        | walk_names(os.path.join(MOD, "Assets", "decal_models"), "_geo.tpac")


def next_retry_name(path):
    base = os.path.basename(path)
    parent = os.path.dirname(path)
    for i in range(1, 100):
        cand = os.path.join(parent, "%s_retry%02d" % (base, i))
        if not os.path.isdir(cand):
            return cand
    raise SystemExit("too many retries: " + path)


def repack_texture_batch(path, done, dry):
    files = [f for f in os.listdir(path) if f.endswith(".png")]
    pending = [f for f in files if f[:-4] not in done]
    total, name = len(files), os.path.basename(path)
    if not pending:
        print("  %-28s %4d/%4d  全部已导，无需重打包" % (name, total, total))
        return 0
    if len(pending) == total:
        print("  %-28s 尚未开始，直接导原批次" % name)
        return 0
    dst = next_retry_name(path)
    print("  %-28s 已导 %4d / 共 %4d → 新建 %s（%d 张）"
          % (name, total - len(pending), total, os.path.basename(dst), len(pending)))
    if dry:
        return len(pending)
    os.makedirs(dst, exist_ok=True)
    for f in pending:
        shutil.move(os.path.join(path, f), os.path.join(dst, f))
    return len(pending)


def repack_model_batch(path, done, done_stripped, dry):
    mp = os.path.join(path, "models.csv")
    if not os.path.isfile(mp):
        return 0
    rows = list(csv.DictReader(io.open(mp, encoding="utf-8-sig")))
    pending = [r for r in rows
               if r["asset_name"] not in done
               and re.sub(r"_lod\d*$", "", r["asset_name"]) not in done_stripped]
    name = os.path.basename(path)
    if not pending:
        print("  %-28s %4d/%4d  全部已导，无需重打包" % (name, len(rows), len(rows)))
        return 0
    if len(pending) == len(rows):
        print("  %-28s 尚未开始，直接导原批次" % name)
        return 0
    dst = next_retry_name(path)
    print("  %-28s 已导 %4d / 共 %4d → 新建 %s（%d 个）"
          % (name, len(rows) - len(pending), len(rows), os.path.basename(dst), len(pending)))
    if dry:
        return len(pending)
    os.makedirs(dst, exist_ok=True)
    for r in pending:
        f = r["trf"]
        src = os.path.join(path, f)
        if os.path.isfile(src):
            shutil.move(src, os.path.join(dst, f))
    with io.open(os.path.join(dst, "models.csv"), "w", encoding="utf-8-sig", newline="") as fh:
        w = csv.DictWriter(fh, fieldnames=rows[0].keys())
        w.writeheader()
        for r in pending:
            w.writerow(r)
    return len(pending)


def repack_one(path, tdone, mdone, dry):
    if not os.path.isdir(path):
        raise SystemExit("batch not found: " + path)
    if os.path.isfile(os.path.join(path, "models.csv")):
        return repack_model_batch(path, mdone, {re.sub(r"_lod\d*$", "", n) for n in mdone}, dry)
    return repack_texture_batch(path, tdone, dry)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--target", default="", help='批次目录，如 "_todo_albedo\\batch_03" 或 "part_02"')
    ap.add_argument("--all", action="store_true", help="检查全部批次")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    tdone, mdone = tex_done(), model_done()
    print("项目内已有：贴图 %d / 模型 %d" % (len(tdone), len(mdone)))

    if args.target:
        t = args.target
        cand = [os.path.join(TEX_STAGE, t), os.path.join(STAGE, t)]
        path = next((p for p in cand if os.path.isdir(p)), None)
        if not path:
            raise SystemExit("not found under textures/ or rdr2_models/: " + t)
        repack_one(path, tdone, mdone, args.dry_run)
        return

    if not args.all:
        ap.error("need --target or --all")

    # 贴图批次（_todo_* 下的 batch_*）
    for role in ("_todo_albedo", "_todo_normal", "_todo_specular"):
        d = os.path.join(TEX_STAGE, role)
        if not os.path.isdir(d):
            continue
        print("[%s]" % role)
        for b in sorted(os.listdir(d)):
            p = os.path.join(d, b)
            if os.path.isdir(p) and not re.search(r"_retry\d+$", b):
                repack_one(p, tdone, mdone, args.dry_run)
    # 模型批次
    print("[models]")
    for b in sorted(os.listdir(STAGE)):
        p = os.path.join(STAGE, b)
        if os.path.isdir(p) and os.path.isfile(os.path.join(p, "models.csv")) \
                and not re.search(r"_retry\d+$", b):
            repack_one(p, tdone, mdone, args.dry_run)


if __name__ == "__main__":
    main()
