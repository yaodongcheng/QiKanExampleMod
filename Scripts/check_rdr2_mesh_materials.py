# -*- coding: utf-8 -*-
"""体检：RDR2 编辑器工程里，**已导入网格引用的材质是否都存在**。

为什么需要：编辑器加载工程时，任何一个网格的材质找不到都会弹一个
`RGL CONTENT WARNING` 模态框 —— 几十个就卡死在 loading。等它弹完再修太慢，
所以这里离线预先查一遍。

判据（与编辑器实际行为对齐）：
  1. 每个 `Assets\\models|decal_models\\<目录>\\<名>_geo.tpac` 去找导入时的镜像源
     `AssetSources\\models\\<目录>\\<名>.trf`（编辑器导入时自动写这份），
     解析其中**所有子网格**的材质名（TRF 里材质声明散落在顶点数据中间，必须全文扫）；
  2. 每个材质名必须在 `Assets\\materials\\**\\<材质>_mtl.tpac` 里存在；
     缺 = 编辑器会弹窗（ERROR）；
  3. 材质文件 mtime 比网格新 = 当初导入时材质还不存在、绑定已烘坏（WARN，同样需要删掉重导）。

用法：
    python Scripts\\check_rdr2_mesh_materials.py                # 用默认模块路径
    python Scripts\\check_rdr2_mesh_materials.py --module "H:\\...\\Modules\\RDR2"
    python Scripts\\check_rdr2_mesh_materials.py --fix-list out.csv   # 导出需重导的清单
"""
import argparse
import csv
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

DEFAULT_MODULE = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2"
DELIV = r"D:\BrainMaker\大镖客2解包分析\deliverable"

# TRF 里一行子网格声明：<模型>.<序号> <标志> <材质名>，例如 j_x.1 0 gri_trestles_wood_02_ab
SUB_RE = re.compile(r'^(\S+)\.(\d+)\s+\d+\s+(\S+)\s*$', re.M)


def materials_of(module):
    out = {}
    base = os.path.join(module, "Assets", "materials")
    for root, _d, files in os.walk(base):
        for f in files:
            if f.endswith("_mtl.tpac"):
                out[f[:-len("_mtl.tpac")]] = os.path.join(root, f)
    return out


def meshes_of(module):
    """→ {网格名: (geo 路径, 类目录)}"""
    out = {}
    for sub in ("models", "decal_models"):
        base = os.path.join(module, "Assets", sub)
        for root, _d, files in os.walk(base):
            for f in files:
                if f.endswith("_geo.tpac"):
                    out[f[:-len("_geo.tpac")]] = (os.path.join(root, f), os.path.basename(root))
    return out


def find_trf(module, name, cls_dir):
    """优先 AssetSources 镜像（导入时写的那份），其次交付包。"""
    p = os.path.join(module, "AssetSources", "models", cls_dir, name + ".trf")
    if os.path.isfile(p):
        return p
    for d in ("decal_models", "models"):
        p = os.path.join(module, "AssetSources", d, cls_dir, name + ".trf")
        if os.path.isfile(p):
            return p
    for d in ("models", "models_extra"):
        p = os.path.join(DELIV, d, name + ".trf")
        if os.path.isfile(p):
            return p
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", default=DEFAULT_MODULE)
    ap.add_argument("--fix-list", default="", help="把需要重导的网格导出成 CSV")
    ap.add_argument("--verbose", action="store_true")
    args = ap.parse_args()

    mats = materials_of(args.module)
    mesh = meshes_of(args.module)
    print("材质资产 %d 个 ｜ 已导入网格 %d 个" % (len(mats), len(mesh)))

    missing = []      # (网格, 缺的材质)
    stale = []        # (网格, 材质比网格新)
    no_trf = []
    for i, (name, (geo, cls_dir)) in enumerate(sorted(mesh.items()), 1):
        trf = find_trf(args.module, name, cls_dir)
        if not trf:
            no_trf.append(name)
            continue
        try:
            txt = io.open(trf, encoding="utf-8", errors="replace").read()
        except Exception as e:
            no_trf.append("%s (%r)" % (name, e))
            continue
        used = {m.group(3) for m in SUB_RE.finditer(txt)}
        if not used:
            continue
        lack = sorted(m for m in used if m not in mats)
        if lack:
            missing.append((name, lack))
            continue
        geo_mt = os.path.getmtime(geo)
        newest = max(mats[m] and os.path.getmtime(mats[m]) for m in used)
        if newest > geo_mt + 1:
            stale.append((name,))
        if args.verbose and i % 1000 == 0:
            print("  ... %d/%d" % (i, len(mesh)), flush=True)

    print("")
    print("缺材质（编辑器必弹窗） : %d 个网格" % len(missing))
    for n, lack in missing[:20]:
        print("   %-40s 缺 %s" % (n, ", ".join(lack[:3])))
    print("材质比网格新（绑定已烘坏，同样要重导）: %d 个网格" % len(stale))
    for (n,) in stale[:20]:
        print("   %s" % n)
    if no_trf:
        print("找不到镜像/交付 TRF（无法判定）: %d 个，例：%s" % (len(no_trf), no_trf[:3]))

    bad = [n for n, _ in missing] + [n for (n,) in stale]
    if args.fix_list:
        with io.open(args.fix_list, "w", encoding="utf-8-sig", newline="") as f:
            w = csv.writer(f)
            w.writerow(["mesh", "reason"])
            for n, _ in missing:
                w.writerow([n, "missing_material"])
            for (n,) in stale:
                w.writerow([n, "material_newer"])
        print("需重导清单 -> %s" % args.fix_list)

    if bad:
        print("\n结论：%d 个网格需要「删掉 geo + 重导」。" % len(bad))
        return 1
    print("\n结论：全部网格的材质都在，进编辑器不会再因缺材质弹窗。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
