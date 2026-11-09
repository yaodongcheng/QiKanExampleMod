# -*- coding: utf-8 -*-
"""单独整理圣丹尼斯（sdn）的 LOD 远景碎片，放一个独立批次。

输出（Modules/RDR2/AssetSources/ImortReady/rdr2_lod_fragments/）：
  trf_batch_sdn/            184 个 TRF
  textures_sdn/             114 张 PNG
  import_sdn_models.csv
  import_sdn_textures.csv
  sdn_fragments.csv         185 个碎片：frag_id/name/sub_group/pos_local/rot/scale/贴图
  missing_sdn.csv           1 个缺网格
"""
import csv
import io
import json
import os
import shutil
from collections import defaultdict, Counter

RDR2 = r"D:\BrainMaker\大镖客2解包分析\deliverable"
MOD = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\RDR2"
OUT = os.path.join(MOD, "AssetSources", "ImortReady", "rdr2_lod_fragments")
GID = "sdn"


def load(path):
    return list(csv.DictReader(io.open(path, encoding="utf-8-sig")))


def main():
    g = json.load(io.open(os.path.join(RDR2, "manifests", "prefabs", GID + ".json"), encoding="utf-8"))
    models = {}
    for fn in ("models_export.csv", "models_export_extra.csv"):
        for r in load(os.path.join(RDR2, "manifests", fn)):
            models.setdefault(r["name"], r)
    mats = defaultdict(list)
    for r in load(os.path.join(RDR2, "manifests", "model_materials.csv")):
        mats[r["model"]].append(r)
    texdest = {r["name"]: r["dest"] for r in load(os.path.join(RDR2, "manifests", "textures_export.csv"))}

    frags = g["fragments"]
    names = sorted({f["name"] for f in frags})
    present, missing = [], []
    for n in names:
        r = models.get(n)
        p = os.path.join(RDR2, "models", r["trf"]) if r else None
        (present if (p and os.path.isfile(p)) else missing).append(n)
    tex = sorted({t for n in present for m in mats.get(n, [])
                  for t in (m.get("albedo"), m.get("normal"), m.get("ma")) if t})

    tdir = os.path.join(OUT, "trf_batch_sdn")
    xdir = os.path.join(OUT, "textures_sdn")
    for d in (tdir, xdir):
        if os.path.isdir(d):
            shutil.rmtree(d)
        os.makedirs(d)

    for n in present:
        shutil.copy2(os.path.join(RDR2, "models", models[n]["trf"]),
                     os.path.join(tdir, os.path.basename(models[n]["trf"])))
    for t in tex:
        shutil.copy2(texdest[t], os.path.join(xdir, t + ".png"))

    with io.open(os.path.join(OUT, "import_sdn_models.csv"), "w", encoding="utf-8-sig", newline="") as fh:
        w = csv.writer(fh); w.writerow(["asset_name", "source_trf", "bytes"])
        for n in present:
            p = os.path.join(RDR2, "models", models[n]["trf"])
            w.writerow([n, p, os.path.getsize(p)])
    with io.open(os.path.join(OUT, "import_sdn_textures.csv"), "w", encoding="utf-8-sig", newline="") as fh:
        w = csv.writer(fh); w.writerow(["asset_name", "source", "bytes"])
        for t in tex:
            w.writerow([t, texdest[t], os.path.getsize(texdest[t])])

    with io.open(os.path.join(OUT, "sdn_fragments.csv"), "w", encoding="utf-8-sig", newline="") as fh:
        w = csv.writer(fh)
        w.writerow(["frag_id", "name", "sub_group", "pos_local_x", "pos_local_y", "pos_local_z",
                    "qx", "qy", "qz", "qw", "sxy", "sz", "trf_present", "tex_albedo", "tex_normal", "tex_ma"])
        for f in frags:
            n = f["name"]
            m = mats.get(n, [{}])
            al = "|".join(sorted({x.get("albedo", "") for x in m if x.get("albedo")}))
            no = "|".join(sorted({x.get("normal", "") for x in m if x.get("normal")}))
            ma = "|".join(sorted({x.get("ma", "") for x in m if x.get("ma")}))
            r = f.get("rot", [0, 0, 0, 1]); s = f.get("scale", [1, 1])
            p = f.get("pos_local", [0, 0, 0])
            w.writerow([f["frag_id"], n, f.get("sub_group", ""), p[0], p[1], p[2],
                        r[0], r[1], r[2], r[3], s[0], s[1],
                        "1" if n in present else "0", al, no, ma])

    with io.open(os.path.join(OUT, "missing_sdn.csv"), "w", encoding="utf-8-sig", newline="") as fh:
        w = csv.writer(fh); w.writerow(["model_name", "frag_id"])
        for f in frags:
            if f["name"] in missing:
                w.writerow([f["name"], f["frag_id"]])

    print("sdn fragments=%d models=%d present=%d missing=%d textures=%d"
          % (len(frags), len(names), len(present), len(missing), len(tex)))
    print("sub_group", dict(Counter(f["sub_group"] for f in frags)))
    print("trf %.1f MB, tex %.1f MB" %
          (sum(os.path.getsize(os.path.join(tdir, x)) for x in os.listdir(tdir)) / 1024 / 1024,
           sum(os.path.getsize(os.path.join(xdir, x)) for x in os.listdir(xdir)) / 1024 / 1024))
    print("out ->", tdir, "|", xdir)


if __name__ == "__main__":
    main()
