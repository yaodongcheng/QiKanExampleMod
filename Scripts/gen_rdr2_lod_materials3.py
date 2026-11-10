# -*- coding: utf-8 -*-
"""给全部 56 组 LOD 碎片生成【三槽】编辑器材质资产（_mtl.tpac）。

与 `gen_rdr2_sdn_materials3.py` 的区别：
- 模型集 = 56 组 prefabs/*.json 的并集（2,401 个）；
- **只生成缺失的材质**（已存在于 sdn_test 的 69 个跳过，不覆盖、不搬移）；
- 贴图目录自动发现（`Assets/textures/lod/` 下除 `sdn_test` 外 `_tex.tpac` 最多的那个），
  也可用 `--texdir` 指定；
- 中性贴图（平法线/白）从 `sdn_test` 复用（已导入，不重复导入）。

用法（先在 ModKit 里导入 622 张新贴图，再跑）：
  python gen_rdr2_lod_materials3.py
  python gen_rdr2_lod_materials3.py --texdir "Assets/textures/lod/fragments" --outdir ".../fragments"
"""
import argparse
import csv
import glob
import io
import json
import os
import shutil
import subprocess

DELIV = r"D:\BrainMaker\大镖客2解包分析\deliverable"
MOD = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\RDR2"
LWN = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
TOOL = os.path.join(LWN, "tools", "tpactool", "TpacToolCLI", "bin", "Release", "net9.0", "tpaccli.exe")
STAGE = os.path.join(LWN, "Debug", "RD2构造相关", "lod_mat_stage")
REF = os.path.join(LWN, "Debug", "RD2构造相关", "mats_stage", "nbx_wall_brick_scan_dirtlight_ab_mtl.tpac")
TEX_LOD = os.path.join(MOD, "Assets", "textures", "lod")
SDN_TEX = os.path.join(TEX_LOD, "sdn_test")
SDN_MAT = os.path.join(MOD, "Assets", "materials", "lod", "sdn_test")
MAT_OUT_DEFAULT = os.path.join(MOD, "Assets", "materials", "lod", "fragments")
PREFAB_DIR = os.path.join(DELIV, "manifests", "prefabs")


def run(args):
    p = subprocess.run([TOOL] + args, capture_output=True, text=True)
    out = (p.stdout or "") + (p.stderr or "")
    print(" ".join(args[:3]), "->", p.returncode)
    if p.returncode != 0:
        print(out)
        raise SystemExit("tpaccli failed")
    return out


def all_models():
    """56 组碎片 → 模型名集合。

    碎片里用的是**原型名**（如 `adl_barn_slod`），但模型库里有一部分是按 hash
    命名（`e_a47ae07c`）⇒ 名字查不到时按碎片 `hash` 回退到 manifest 的模型名，
    否则这些模型的材质会被漏掉（2026-11-09 实测 97 个）。
    """
    by_name = {}
    by_hash = {}
    for fn in ("models_export.csv", "models_export_extra.csv"):
        p = os.path.join(DELIV, "manifests", fn)
        if not os.path.isfile(p):
            continue
        with io.open(p, encoding="utf-8-sig") as fh:
            for r in csv.DictReader(fh):
                by_name.setdefault(r["name"], r)
                by_hash.setdefault(r["model_hash"].lower(), r)
    names = set()
    for p in glob.glob(os.path.join(PREFAB_DIR, "*.json")):
        g = json.load(io.open(p, encoding="utf-8"))
        for f in g.get("fragments", []):
            n = f["name"]
            if n in by_name:
                names.add(n)
            else:
                h = (f.get("hash") or "").lower()
                r = by_hash.get(h)
                if r:
                    names.add(r["name"])
    return names


def discover_texdirs():
    """Assets/textures/lod/ 下所有含 _tex.tpac 的目录（除 sdn_test，它的中性贴图单独复用）。"""
    out = []
    for d in sorted(glob.glob(os.path.join(TEX_LOD, "*"))):
        if not os.path.isdir(d) or os.path.abspath(d) == os.path.abspath(SDN_TEX):
            continue
        if glob.glob(os.path.join(d, "*_tex.tpac")):
            out.append(d)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--texdir", default="", help="已导入贴图目录（默认自动发现）")
    ap.add_argument("--outdir", default=MAT_OUT_DEFAULT)
    a = ap.parse_args()

    models = all_models()
    mats = {}
    with io.open(os.path.join(DELIV, "manifests", "model_materials.csv"), encoding="utf-8-sig") as fh:
        for r in csv.DictReader(fh):
            if r["model"] in models:
                mats[r["material"]] = (r["albedo"], r["normal"], r["ma"])
    print("models=%d materials_total=%d" % (len(models), len(mats)))

    existing = set()
    for d in (SDN_MAT, a.outdir):
        if os.path.isdir(d):
            existing |= {f[:-len("_mtl.tpac")] for f in os.listdir(d) if f.endswith("_mtl.tpac")}
    todo = sorted(m for m in mats if m not in existing)
    print("existing(sdn)=%d  to_generate=%d" % (len(existing), len(todo)))
    if not todo:
        print("nothing to do")
        return

    if a.texdir:
        texdirs = [os.path.join(MOD, x) if not os.path.isabs(x) else x
                   for x in a.texdir.split(",") if x.strip()]
    else:
        texdirs = discover_texdirs()
        if not texdirs:
            raise SystemExit("找不到贴图目录：请先把 PNG 导入 ModKit，或用 --texdir 指定")
    print("texture dirs:", texdirs)

    tex_have = set()
    for d in texdirs:
        tex_have |= {f[:-len("_tex.tpac")].lower() for f in os.listdir(d) if f.endswith("_tex.tpac")}
    # 中性贴图从 sdn_test 复用
    neutrals = ("rdr2_flat_normal", "rdr2_white")
    for n in neutrals:
        p = os.path.join(SDN_TEX, n + "_tex.tpac")
        if not os.path.isfile(p):
            raise SystemExit("缺少中性贴图资产: %s（sdn_test 里应有）" % p)

    bad = [m for m in todo for (a_, _, _) in [mats[m]]
           if a_ and a_.lower() not in tex_have]
    if bad:
        raise SystemExit("以下材质的 albedo 贴图不在 %s（先导入贴图）：\n  %s"
                         % (texdirs, "\n  ".join(sorted(set(bad))[:40])))

    # staging：所有贴图目录 + 中性贴图
    if os.path.isdir(STAGE):
        shutil.rmtree(STAGE)
    os.makedirs(STAGE)
    for d in texdirs:
        for f in os.listdir(d):
            if f.endswith("_tex.tpac"):
                shutil.copy2(os.path.join(d, f), os.path.join(STAGE, f))
    for n in neutrals:
        shutil.copy2(os.path.join(SDN_TEX, n + "_tex.tpac"), os.path.join(STAGE, n + "_tex.tpac"))
    print("staged %d _tex.tpac" % len(os.listdir(STAGE)))

    matmap = os.path.join(STAGE, "matmap.csv")
    with io.open(matmap, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("material,albedo,normal,ma\n")
        for m in todo:
            alb, nrm, ma = mats[m]
            if not alb or alb.lower() not in tex_have:
                alb = "rdr2_white"
            if not nrm or nrm.lower() not in tex_have:
                nrm = ""
            if not ma or ma.lower() not in tex_have:
                ma = ""
            fh.write("%s,%s,%s,%s\n" % (m, alb, nrm, ma))
    print("matmap ->", matmap, "rows", len(todo))

    os.makedirs(a.outdir, exist_ok=True)
    run(["editormeta", "--name", "dummy", "--out", os.path.join(a.outdir, "dummy"),
         "--genmats3", STAGE, "--mtlref", REF, "--matmap", matmap])
    made = [f for f in os.listdir(a.outdir) if f.endswith("_mtl.tpac")]
    print("generated %d materials -> %s" % (len(made), a.outdir))


if __name__ == "__main__":
    main()
