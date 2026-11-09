# -*- coding: utf-8 -*-
"""给圣丹尼斯 LOD 碎片批量生成编辑器材质资产（_mtl.tpac）。

原理（项目既定配方，见 plans/大镖客2还原/00-总纲.md §18.3 / §18.6）：
  1. 参考材质（一个只有 albedo 槽的模板，shader 328d3572…）
  2. 把 albedo 贴图的 `_tex.tpac` 放一个目录
  3. tpaccli editormeta --genmats 克隆参考材质 → 每个贴图一个 _mtl.tpac（名字=贴图名）

输出：Modules/RDR2/Assets/materials/lod/sdn_test/<材质名>_mtl.tpac
"""
import csv
import io
import json
import os
import re
import shutil
import subprocess
from collections import defaultdict

DELIV = r"D:\BrainMaker\大镖客2解包分析\deliverable"
MOD = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\RDR2"
LWN = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
TOOL = os.path.join(LWN, "tools", "tpactool", "TpacToolCLI", "bin", "Release", "net9.0", "tpaccli.exe")
STAGE = os.path.join(LWN, "Debug", "RD2构造相关", "sdn_mat_stage")
TEX_SRC = os.path.join(MOD, "Assets", "textures", "lod", "sdn_test")
MAT_OUT = os.path.join(MOD, "Assets", "materials", "lod", "sdn_test")
SHADER = "328d3572-5e9e-4183-b49e-f451a19213d0"
FLAGS = "use_specular,do_not_use_vertex_color_as_occlusion"


def run(args):
    p = subprocess.run([TOOL] + args, capture_output=True, text=True)
    out = (p.stdout or "") + (p.stderr or "")
    print(" ".join(args[:4]), "->", p.returncode)
    if p.returncode != 0:
        print(out)
        raise SystemExit("tpaccli failed")
    return out


def main():
    g = json.load(io.open(os.path.join(DELIV, "manifests", "prefabs", "sdn.json"), encoding="utf-8"))
    models = {f["name"] for f in g["fragments"]}
    material_albedo = {}
    for r in csv.DictReader(io.open(os.path.join(DELIV, "manifests", "model_materials.csv"),
                                    encoding="utf-8-sig")):
        if r["model"] in models:
            material_albedo[r["material"]] = r["albedo"]

    # 已导入的 _tex.tpac：贴图名 -> 文件
    tex_files = {}
    for fn in os.listdir(TEX_SRC):
        if fn.endswith("_tex.tpac"):
            tex_files[fn[:-len("_tex.tpac")]] = os.path.join(TEX_SRC, fn)

    albedo_names = sorted({a for a in material_albedo.values() if a and a in tex_files})
    missing = sorted(m for m, a in material_albedo.items() if not a or a not in tex_files)
    print("materials=%d albedo_tex=%d missing=%s" % (len(material_albedo), len(albedo_names), missing))

    os.makedirs(STAGE, exist_ok=True)
    tex_stage = os.path.join(STAGE, "tex")
    if os.path.isdir(tex_stage):
        shutil.rmtree(tex_stage)
    os.makedirs(tex_stage)
    for a in albedo_names:
        shutil.copy2(tex_files[a], os.path.join(tex_stage, a + "_tex.tpac"))
    print("staged %d albedo _tex.tpac -> %s" % (len(albedo_names), tex_stage))

    # 1x1 白色贴图（给 no_tex 用），先造它的 _tex.tpac
    from PIL import Image
    white_png = os.path.join(STAGE, "rdr2_white.png")
    Image.new("RGB", (32, 32), (255, 255, 255)).save(white_png)
    out = run(["editormeta", "--tex", "--name", "rdr2_white", "--png", white_png,
               "--out", os.path.join(tex_stage, "rdr2_white_tex.tpac")])
    m = re.search(r"guid=([0-9a-fA-F-]{36})", out)
    white_guid = m.group(1) if m else None
    print("white tex guid", white_guid)

    # 参考材质：单槽（只有 albedo），shader 与之前测试一致
    ref = os.path.join(STAGE, "rdr2_ref_mtl.tpac")
    run(["editormeta", "--mat", "--name", "rdr2_ref", "--texguid", white_guid,
         "--shader", SHADER, "--flags", FLAGS,
         "--out", ref])

    # 批量生成材质
    os.makedirs(MAT_OUT, exist_ok=True)
    run(["editormeta", "--name", "dummy", "--out", MAT_OUT, "--genmats", tex_stage,
         "--mtlref", ref])

    # no_tex 材质（没有 albedo 的那个）
    run(["editormeta", "--mat", "--name", "no_tex", "--texguid", white_guid,
         "--shader", SHADER, "--flags", FLAGS,
         "--out", os.path.join(MAT_OUT, "no_tex_mtl.tpac")])

    files = [f for f in os.listdir(MAT_OUT) if f.endswith("_mtl.tpac")]
    print("generated %d materials -> %s" % (len(files), MAT_OUT))


if __name__ == "__main__":
    main()
