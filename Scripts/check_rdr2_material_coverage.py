"""体检：FBX 里用到的材质名，编辑器工程里是不是都有对应 _mtl.tpac。

用途：批量导网格之前先跑，避免导完发现一堆 "Unable to find material for mesh"。
口径与 export_rdr2_bld_fbx.py 完全一致（同一份 rdr2_tex_manifest.json + 同一套大小写归一）。

用法：
    python Scripts/check_rdr2_material_coverage.py --mtl-dir <Assets/materials/rdr2_all>
"""
import argparse
import glob
import hashlib
import io
import json
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

SHORT_LIMIT = 55


def short_name(name):
    """必须与 export_rdr2_fbx_tile.py / gen_rdr2_mat_aliases.py 完全一致。"""
    if len(name) <= SHORT_LIMIT:
        return name
    return name[:SHORT_LIMIT - 9] + "_" + hashlib.md5(name.encode("utf-8")).hexdigest()[:8]

MANIFEST = (r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
            r"\Debug\RD2构造相关\rdr2_tex_manifest.json")
DEFAULT_MTL_DIR = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
                   r"\Mount & Blade II Bannerlord\Modules\RDR2\Assets\materials\rdr2_all")


def expected_materials():
    d = json.load(io.open(MANIFEST, encoding="utf-8"))
    mats = d["materials"]
    exp = set()
    tex_by_lower = {}
    for name, e in mats.items():
        if e.get("alb"):
            stem = os.path.splitext(e["alb"])[0]
            tex_by_lower[stem.lower()] = stem
            exp.add(short_name(stem))
    groups = {}
    for name, e in mats.items():
        if e.get("flat"):
            groups.setdefault(name.lower(), []).append(name)
    for variants in groups.values():
        variants.sort(key=lambda n: (-mats[n].get("uses", 0), n))
        canon = variants[0]
        # 与贴图名只差大小写的 → FBX 用贴图拼写，材质由贴图材质提供
        t = tex_by_lower.get(canon.lower())
        exp.add(short_name(t if t else canon))
    return exp


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mtl-dir", default=DEFAULT_MTL_DIR)
    ap.add_argument("--manifest", default=MANIFEST)
    args = ap.parse_args()

    exp = expected_materials()
    got = {os.path.basename(p)[:-len("_mtl.tpac")]
           for p in glob.glob(os.path.join(args.mtl_dir, "*_mtl.tpac"))}
    missing = sorted(exp - got)
    extra = sorted(got - exp)
    print("需要材质 %d 个 · 已有 %d 个 · 缺 %d 个 · 多 %d 个"
          % (len(exp), len(got), len(missing), len(extra)))
    for m in missing[:30]:
        print("  缺 %s" % m)
    for m in extra[:10]:
        print("  多 %s" % m)
    return 0 if not missing else 1


if __name__ == "__main__":
    sys.exit(main())
