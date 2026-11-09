# -*- coding: utf-8 -*-
"""给圣丹尼斯 LOD 碎片批量生成【三槽】编辑器材质资产（_mtl.tpac）。

三槽 = DiffuseMap(0) / NormalMap(2) / SpecularMap(4)。
配方：克隆一份 3 槽参考材质（mats_stage 里之前验证通过的那份），
按 material_textures.csv 把三个贴图 GUID 换掉（RawMeta 字节级同步替换）。

输出：Modules/RDR2/Assets/materials/lod/sdn_test/<材质名>_mtl.tpac
旧的 _mtl.tpac 先移到 Debug 备份目录（不删除）。
"""
import csv
import io
import json
import os
import shutil
import subprocess

DELIV = r"D:\BrainMaker\大镖客2解包分析\deliverable"
MOD = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\RDR2"
LWN = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
TOOL = os.path.join(LWN, "tools", "tpactool", "TpacToolCLI", "bin", "Release", "net9.0", "tpaccli.exe")
STAGE = os.path.join(LWN, "Debug", "RD2构造相关", "sdn_mat_stage")
TEX_SRC = os.path.join(MOD, "Assets", "textures", "lod", "sdn_test")
IMORT = os.path.join(MOD, "AssetSources", "ImortReady", "rdr2_lod_fragments", "textures_sdn")
MAT_OUT = os.path.join(MOD, "Assets", "materials", "lod", "sdn_test")
REF = os.path.join(LWN, "Debug", "RD2构造相关", "mats_stage", "nbx_wall_brick_scan_dirtlight_ab_mtl.tpac")
OLD = os.path.join(LWN, "Debug", "RD2构造相关", "sdn_mat_old")


def run(args):
    p = subprocess.run([TOOL] + args, capture_output=True, text=True)
    out = (p.stdout or "") + (p.stderr or "")
    print(" ".join(args[:3]), "->", p.returncode)
    if p.returncode != 0:
        print(out)
        raise SystemExit("tpaccli failed")
    return out


def main():
    g = json.load(io.open(os.path.join(DELIV, "manifests", "prefabs", "sdn.json"), encoding="utf-8"))
    models = {f["name"] for f in g["fragments"]}
    mats = {}
    for r in csv.DictReader(io.open(os.path.join(DELIV, "manifests", "model_materials.csv"),
                                    encoding="utf-8-sig")):
        if r["model"] in models:
            mats[r["material"]] = (r["albedo"], r["normal"], r["ma"])

    tex_have = {f[:-len("_tex.tpac")].lower() for f in os.listdir(TEX_SRC) if f.endswith("_tex.tpac")}
    print("materials=%d imported_tex=%d" % (len(mats), len(tex_have)))

    # 备份旧的 _mtl.tpac（不删除）
    os.makedirs(OLD, exist_ok=True)
    for d in (os.path.join(MOD, "Assets", "materials", "lod"), MAT_OUT):
        if os.path.isdir(d):
            for f in os.listdir(d):
                if f.endswith("_mtl.tpac"):
                    src = os.path.join(d, f)
                    dst = os.path.join(OLD, f)
                    if os.path.exists(dst):
                        os.remove(dst)
                    shutil.move(src, dst)
    print("old materials moved ->", OLD)

    # 🔴 中性贴图（平法线/白）必须由用户在 ModKit 中导入；本脚本不往 Assets 写 _tex.tpac。
    from PIL import Image
    os.makedirs(IMORT, exist_ok=True)
    for png, color in (("rdr2_flat_normal.png", (128, 128, 255)),
                       ("rdr2_white.png", (255, 255, 255))):
        p = os.path.join(IMORT, png)
        if not os.path.isfile(p):
            Image.new("RGB", (32, 32), color).save(p)
    missing_tex = [t for t in ("rdr2_flat_normal_tex.tpac", "rdr2_white_tex.tpac")
                   if not os.path.isfile(os.path.join(TEX_SRC, t))]
    if missing_tex:
        raise SystemExit(
            "缺少中性贴图资产: %s\n请先在 ModKit 的 Resource Browser 导入（名字保持一致）：\n  %s\n  %s"
            % (", ".join(missing_tex),
               os.path.join(IMORT, "rdr2_flat_normal.png"),
               os.path.join(IMORT, "rdr2_white.png")))

    # 把所有 _tex.tpac（含平法线/白）复制到 staging，供 genmats3 读 GUID
    tex_stage = os.path.join(STAGE, "tex3")
    if os.path.isdir(tex_stage):
        shutil.rmtree(tex_stage)
    os.makedirs(tex_stage)
    for f in os.listdir(TEX_SRC):
        if f.endswith("_tex.tpac"):
            shutil.copy2(os.path.join(TEX_SRC, f), os.path.join(tex_stage, f))
    print("staged %d _tex.tpac" % len(os.listdir(tex_stage)))

    # 🔴 先导贴图、再生成材质：缺任何 albedo 就直接失败，禁止静默用白色顶替
    bad = [m for m, (a, _, _) in mats.items()
           if m != "no_tex" and (not a or a.lower() not in tex_have)]
    if bad:
        raise SystemExit("以下材质的 albedo 贴图未导入（先在 ModKit 导贴图）：\n  "
                         + "\n  ".join(sorted(bad)))

    # matmap：material,albedo,normal,ma
    matmap = os.path.join(STAGE, "matmap.csv")
    with io.open(matmap, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("material,albedo,normal,ma\n")
        for m, (a, n, ma) in sorted(mats.items()):
            if not a or a.lower() not in tex_have:
                a = "rdr2_white"
            if not n or n.lower() not in tex_have:
                n = ""
            if not ma or ma.lower() not in tex_have:
                ma = ""
            fh.write("%s,%s,%s,%s\n" % (m, a, n, ma))
        fh.write("no_tex,rdr2_white,,\n")

    os.makedirs(MAT_OUT, exist_ok=True)
    run(["editormeta", "--name", "dummy", "--out", os.path.join(MAT_OUT, "dummy"),
         "--genmats3", tex_stage, "--mtlref", REF, "--matmap", matmap])
    files = [f for f in os.listdir(MAT_OUT) if f.endswith("_mtl.tpac")]
    print("generated %d materials -> %s" % (len(files), MAT_OUT))


if __name__ == "__main__":
    main()
