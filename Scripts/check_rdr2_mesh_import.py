"""查 RDR2 网格导入进度：已导入多少 / 还缺哪些 / 缺的落在哪个批次。

判据 = `Assets\\buildings\\<名>_geo.tpac` 是否存在（编辑器注册的才算数）。

用法：
    python Scripts/check_rdr2_mesh_import.py
"""
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

MODULE = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
          r"\Mount & Blade II Bannerlord\Modules\RDR2")
MASTER = os.path.join(MODULE, r"AssetSources\ImortReady\rdr2_bld_fbx")
BATCHES = os.path.join(MODULE, r"AssetSources\ImortReady\rdr2_bld_fbx_batches")
ASSETS = os.path.join(MODULE, r"Assets\buildings")
MIRROR = os.path.join(MODULE, r"AssetSources\buildings")


def names_in(d, suffix):
    if not os.path.isdir(d):
        return set()
    return {f[:-len(suffix)] for f in os.listdir(d)
            if f.endswith(suffix) and os.path.isfile(os.path.join(d, f))}


def main():
    want = names_in(MASTER, ".fbx")
    got = names_in(ASSETS, "_geo.tpac")
    mirror = names_in(MIRROR, ".fbx")
    missing = sorted(want - got)
    print("源 FBX        %5d" % len(want))
    print("已导入(geo)   %5d" % len(got))
    print("镜像副本      %5d" % len(mirror))
    print("还缺          %5d" % len(missing))
    if not missing:
        print("全部导入完成 ✅")
        return 0
    per_batch = {}
    if os.path.isdir(BATCHES):
        for b in sorted(os.listdir(BATCHES)):
            d = os.path.join(BATCHES, b)
            if os.path.isdir(d):
                per_batch[b] = names_in(d, ".fbx")
    for b, names in per_batch.items():
        left = len(names & set(missing))
        print("  %-10s 缺 %4d / %4d" % (b, left, len(names)))
    print("前 10 个缺的：", missing[:10])
    return 1


if __name__ == "__main__":
    sys.exit(main())
