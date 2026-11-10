# -*- coding: utf-8 -*-
"""预检：待导目录里**每一个 TRF** 引用的材质，是否都已在 RDR2 工程的 Assets\\materials 里。

与 check_rdr2_mesh_materials.py 的区别：那是查"已导入网格"，这是查"还没导的 TRF"，
用来在开 ModKit 之前回答"导这 20,998 个网格还会不会因为缺材质弹窗"。

用法：
    python Scripts\\check_rdr2_pending_materials.py
    python Scripts\\check_rdr2_pending_materials.py --out missing.csv
"""
import argparse
import collections
import csv
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

MODULE = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2"
PENDING = os.path.join(MODULE, "AssetSources", "ImortReady", "rdr2_models_pending")

# 一行子网格声明：<模型>.<序号或lodN.序号> <标志> <材质名>
SUB_RE = re.compile(r'^(\S+)\.(\d+)\s+\d+\s+(\S+)\s*$', re.M)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pending", default=PENDING)
    ap.add_argument("--out", default="")
    args = ap.parse_args()

    mats = set()
    base = os.path.join(MODULE, "Assets", "materials")
    for root, _d, files in os.walk(base):
        for f in files:
            if f.endswith("_mtl.tpac"):
                mats.add(f[:-len("_mtl.tpac")])
    print("现有材质资产: %d" % len(mats))

    trfs = []
    for b in sorted(os.listdir(args.pending)):
        d = os.path.join(args.pending, b)
        if not os.path.isdir(d):
            continue
        for f in os.listdir(d):
            if f.endswith(".trf"):
                trfs.append((b, os.path.join(d, f)))
    print("待导 TRF: %d 个" % len(trfs))

    used = collections.Counter()
    bad = collections.defaultdict(set)
    scan = 0
    for batch, path in trfs:
        scan += 1
        try:
            txt = io.open(path, encoding="utf-8", errors="replace").read()
        except Exception:
            continue
        names = {m.group(3) for m in SUB_RE.finditer(txt)}
        for n in names:
            used[n] += 1
            if n not in mats:
                bad[n].add(os.path.basename(path))
        if scan % 2000 == 0:
            print("  ... %d/%d" % (scan, len(trfs)), flush=True)

    print("")
    print("TRF 引用的不同材质: %d" % len(used))
    print("其中**工程里没有**的: %d" % len(bad))
    for n in sorted(bad)[:30]:
        print("   缺 %-46s (被 %d 个 TRF 引用)" % (n, len(bad[n])))
    if args.out and bad:
        with io.open(args.out, "w", encoding="utf-8-sig", newline="") as f:
            w = csv.writer(f)
            w.writerow(["material", "trf_count", "sample_trf"])
            for n in sorted(bad):
                w.writerow([n, len(bad[n]), sorted(bad[n])[0]])
        print("清单 ->", args.out)
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
