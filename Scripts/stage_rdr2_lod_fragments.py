# -*- coding: utf-8 -*-
"""把 56 组 LOD 远景碎片需要的 TRF / 贴图整理到 ImortReady，供 ModKit 分批导入。

输入：
  deliverable/manifests/prefabs/*.json      56 组碎片
  deliverable/manifests/models_export*.csv  模型 → trf
  deliverable/manifests/model_materials.csv 模型 → 材质 → 三贴图
  deliverable/manifests/textures_export.csv 贴图名 → 源文件

输出（Modules/RDR2/AssetSources/ImortReady/rdr2_lod_fragments/）：
  trf_batch_01..NN/*.trf     每批 ≤ MESH_PER_BATCH 个
  textures/*.png             736 张
  import_trf_batch_XX.csv    批次清单
  import_textures.csv        贴图清单
  missing_models.csv         有碎片但交付里没有网格的模型（已知 817 缺口的一部分）
  README.md                  导入顺序
"""
import csv
import io
import json
import os
import shutil
import glob
from collections import defaultdict, OrderedDict

RDR2 = r"D:\BrainMaker\大镖客2解包分析\deliverable"
MOD = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\RDR2"
OUT = os.path.join(MOD, "AssetSources", "ImortReady", "rdr2_lod_fragments")
MESH_PER_BATCH = 560


def load_csv(path):
    return list(csv.DictReader(io.open(path, encoding="utf-8-sig")))


def main():
    # ---- 1. 56 组 prefab 的碎片 ----
    groups = OrderedDict()
    frag_groups = defaultdict(set)
    for p in sorted(glob.glob(os.path.join(RDR2, "manifests", "prefabs", "*.json"))):
        g = json.load(io.open(p, encoding="utf-8"))
        gid = g["group_id"]
        groups[gid] = g
        for f in g["fragments"]:
            frag_groups[f["name"]].add(gid)

    # ---- 2. 模型 → trf ----
    models = {}
    for fn in ("models_export.csv", "models_export_extra.csv"):
        for r in load_csv(os.path.join(RDR2, "manifests", fn)):
            models.setdefault(r["name"], r)

    # ---- 3. 模型 → 材质 → 贴图 ----
    mats = defaultdict(list)
    for r in load_csv(os.path.join(RDR2, "manifests", "model_materials.csv")):
        mats[r["model"]].append(r)

    # ---- 4. 贴图名 → 源文件 ----
    texdest = {}
    for r in load_csv(os.path.join(RDR2, "manifests", "textures_export.csv")):
        texdest[r["name"]] = r["dest"]

    names = sorted(frag_groups)
    present, missing = [], []
    for n in names:
        r = models.get(n)
        p = os.path.join(RDR2, "models", r["trf"]) if r else None
        (present if (p and os.path.isfile(p)) else missing).append(n)

    tex_names = sorted({t for n in present for m in mats.get(n, [])
                        for t in (m.get("albedo"), m.get("normal"), m.get("ma")) if t})
    tex_present = [t for t in tex_names if t in texdest and os.path.isfile(texdest[t])]
    tex_missing = [t for t in tex_names if t not in tex_present]

    print("groups=%d fragments=%d unique_models=%d trf_present=%d missing=%d"
          % (len(groups), sum(len(g["fragments"]) for g in groups.values()),
             len(names), len(present), len(missing)))
    print("textures=%d present=%d missing=%d" % (len(tex_names), len(tex_present), len(tex_missing)))

    # ---- 5. 清理旧产物（只清本脚本自己的目录）----
    if os.path.isdir(OUT):
        for d in glob.glob(os.path.join(OUT, "trf_batch_*")):
            shutil.rmtree(d)
        os.makedirs(os.path.join(OUT, "textures"), exist_ok=True)
    os.makedirs(OUT, exist_ok=True)

    # ---- 6. 分批拷 TRF ----
    batches = [present[i:i + MESH_PER_BATCH] for i in range(0, len(present), MESH_PER_BATCH)]
    for bi, batch in enumerate(batches, 1):
        bdir = os.path.join(OUT, "trf_batch_%02d" % bi)
        os.makedirs(bdir, exist_ok=True)
        rows = []
        for n in batch:
            src = os.path.join(RDR2, "models", models[n]["trf"])
            dst = os.path.join(bdir, os.path.basename(src))
            if not os.path.isfile(dst):
                shutil.copy2(src, dst)
            rows.append([n, src, os.path.getsize(dst), "|".join(sorted(frag_groups[n]))])
        with io.open(os.path.join(OUT, "import_trf_batch_%02d.csv" % bi), "w",
                     encoding="utf-8-sig", newline="") as fh:
            w = csv.writer(fh)
            w.writerow(["asset_name", "source_trf", "bytes", "groups"])
            w.writerows(rows)
        print("batch %02d: %d trf -> %s" % (bi, len(rows), bdir))

    # ---- 7. 拷贴图 ----
    tdir = os.path.join(OUT, "textures")
    os.makedirs(tdir, exist_ok=True)
    trows = []
    for t in tex_present:
        src = texdest[t]
        dst = os.path.join(tdir, t + ".png")
        if not os.path.isfile(dst):
            shutil.copy2(src, dst)
        trows.append([t, src, os.path.getsize(dst)])
    with io.open(os.path.join(OUT, "import_textures.csv"), "w",
                 encoding="utf-8-sig", newline="") as fh:
        w = csv.writer(fh)
        w.writerow(["asset_name", "source", "bytes"])
        w.writerows(trows)
    print("textures: %d -> %s" % (len(trows), tdir))

    # ---- 8. 缺网格清单 ----
    with io.open(os.path.join(OUT, "missing_models.csv"), "w",
                 encoding="utf-8-sig", newline="") as fh:
        w = csv.writer(fh)
        w.writerow(["model_name", "groups", "fragments"])
        for n in missing:
            w.writerow([n, "|".join(sorted(frag_groups[n])),
                        sum(1 for g in groups.values()
                            for f in g["fragments"] if f["name"] == n)])
    print("missing -> missing_models.csv (%d)" % len(missing))

    # ---- 9. README ----
    txt = """# LOD 远景碎片导入包（56 组 prefab）

## 内容
- `trf_batch_01..%02d/`：TRF 模型（多档 LOD），每批 ≤ %d 个
- `textures/`：%d 张贴图（alb/nm/ma）
- `import_trf_batch_XX.csv`：每批的资产名/源文件/组
- `import_textures.csv`：贴图清单
- `missing_models.csv`：%d 个模型交付里本来就没网格（先跳过）

## 导入顺序（ModKit）
1. **先导贴图**：Resource Browser → Import → 选 `textures\\*.png`（一次导完 %d 张）
2. **再逐批导 TRF**：一次一个 `trf_batch_XX` 目录，导完重启 ModKit（单次会话上限 ~600 网格）
3. 每批导完在 `Assets` 里确认资产数量对得上（见对应 CSV 行数）

## 注意
- TRF 资产名 = 文件名去掉 `.trf`；prefab 生成时按这个名字引用。
- 贴图资产名 = 文件名去掉 `.png`；材质按 `model_materials.csv` 的三贴图名解析。
- `missing_models.csv` 里的模型没有网格，对应碎片先不摆（不要重新解包）。
""" % (len(batches), MESH_PER_BATCH, len(trows), len(missing), len(trows))
    with io.open(os.path.join(OUT, "README.md"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write(txt)
    print("README ->", os.path.join(OUT, "README.md"))
    print("total size: %.1f MB" % (sum(os.path.getsize(os.path.join(dp, f))
          for dp, _, fs in os.walk(OUT) for f in fs) / 1024 / 1024))


if __name__ == "__main__":
    main()
