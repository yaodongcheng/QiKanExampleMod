"""RDR2 全图建筑模型 → FBX（ModKit 批量导入用）。

产出目录（默认）：
    RDR2/AssetSources/ImortReady/rdr2_bld_fbx/<模型名>.fbx      一模型一个（不是一实例一个）

做法：扫 43 个据点的预览瓦片（export/previews/<据点>/tiles/*.gltf），
每瓦片跑一次 Blender（Scripts/export_rdr2_fbx_tile.py），把还没导过的模型各导一个 FBX。
· **优先级 = sdn 先**（其余按字母序），这样圣丹尼斯先出齐。
· **断点续跑**：已存在的 FBX 自动跳过，重跑只补缺。
· 导出后模型是「本地坐标、底面中心 = 原点、Z-up」，实例摆位仍来自瓦片 JSON。

用法：
    python Scripts/export_rdr2_bld_fbx.py --only sdn          # 先看一座城
    python Scripts/export_rdr2_bld_fbx.py                     # 全图
    python Scripts/export_rdr2_bld_fbx.py --limit 40          # 只导 40 个模型（试跑）
"""
import argparse
import glob
import io
import json
import os
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
DEFAULT_IMAGES = r"D:\BrainMaker\大镖客2解包分析\export\previews"
DEFAULT_OUT = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
               r"\Mount & Blade II Bannerlord\Modules\RDR2\AssetSources\ImortReady\rdr2_bld_fbx")
DEFAULT_BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
TILE_SCRIPT = os.path.join(ROOT, "Scripts", "export_rdr2_fbx_tile.py")
TEX_MANIFEST = os.path.join(ROOT, "Debug", "RD2构造相关", "rdr2_tex_manifest.json")
WORK_DIR = os.path.join(ROOT, "Debug", "offline", "_rdr2_fbx_work")


def tile_names(gltf_path):
    with io.open(gltf_path, encoding="utf-8") as f:
        j = json.load(f)
    return {n["name"] for n in j.get("nodes", []) if "mesh" in n and n.get("name")}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--images", default=DEFAULT_IMAGES)
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--blender", default=DEFAULT_BLENDER)
    ap.add_argument("--only", default=None, help="只跑这个据点代码（如 sdn）")
    ap.add_argument("--limit", type=int, default=0, help="总共只导这么多模型（试跑）")
    ap.add_argument("--force", action="store_true", help="忽略已存在的 FBX，全部重出（改过材质名映射时用）")
    args = ap.parse_args()

    os.makedirs(args.out, exist_ok=True)
    os.makedirs(WORK_DIR, exist_ok=True)

    # 材质大小写规范化表（贴图阶段的 alias：原拼写 → 规范拼写）
    matmap = {}
    if os.path.isfile(TEX_MANIFEST):
        with io.open(TEX_MANIFEST, encoding="utf-8") as f:
            mf = json.load(f)
        for variant, canon in mf.get("alias", {}).items():
            matmap[os.path.splitext(variant)[0]] = os.path.splitext(canon)[0]
        # 无贴图材质（纯色占位）也要按大小写归一，否则生成材质时文件互相覆盖
        flat_groups = {}
        for name, e in mf.get("materials", {}).items():
            if not e.get("flat"):
                continue
            flat_groups.setdefault(name.lower(), []).append(name)
        flat_alias = 0
        for variants in flat_groups.values():
            if len(variants) < 2:
                continue
            variants.sort(key=lambda n: (-mf["materials"][n].get("uses", 0), n))
            for v in variants[1:]:
                matmap[v] = variants[0]
                flat_alias += 1
        # 无贴图材质名与某张贴图名只差大小写 → 统一用贴图的拼写
        # （Windows 只能存一个 `<名>_mtl.tpac`，否则贴图材质的文件被占位材质顶掉）
        tex_by_lower = {}
        for name, e in mf.get("materials", {}).items():
            if e.get("alb"):
                stem = os.path.splitext(e["alb"])[0]
                tex_by_lower[stem.lower()] = stem
        flat_to_tex = 0
        for name, e in mf.get("materials", {}).items():
            if not e.get("flat"):
                continue
            t = tex_by_lower.get(name.lower())
            if t and name != t:
                matmap[name] = t
                flat_to_tex += 1
        print("材质大小写映射 %d 条（无贴图材质变体 %d 条 / 与贴图撞名改拼写 %d 条，来自 %s）"
              % (len(matmap), flat_alias, flat_to_tex, os.path.basename(TEX_MANIFEST)))
    matmap_path = os.path.join(WORK_DIR, "matmap.json")
    with io.open(matmap_path, "w", encoding="utf-8") as f:
        json.dump(matmap, f, ensure_ascii=False)

    codes = sorted(d for d in os.listdir(args.images)
                   if os.path.isdir(os.path.join(args.images, d, "tiles")))
    if args.only:
        want = [c.strip() for c in args.only.split(",") if c.strip()]
        codes = [c for c in codes if c in want]
    codes.sort(key=lambda c: (c != "sdn", c))          # sdn 优先

    tiles = []
    for code in codes:
        for f in sorted(glob.glob(os.path.join(args.images, code, "tiles", "*.gltf"))):
            tiles.append((code, f))
    print("瓦片 %d 个，据点 %d 个" % (len(tiles), len(codes)))

    done = set() if args.force else {
        os.path.splitext(os.path.basename(p))[0]
        for p in glob.glob(os.path.join(args.out, "*.fbx"))}
    print("已存在 FBX %d 个（%s）"
          % (len(glob.glob(os.path.join(args.out, "*.fbx"))),
             "强制全部重出" if args.force else "断点续跑会跳过"))

    t0 = time.time()
    initial = len(done)
    misses = []
    for idx, (code, gltf) in enumerate(tiles, 1):
        total_new = len(done) - initial
        if args.limit and total_new >= args.limit:
            break
        names = sorted(tile_names(gltf) - done)
        if args.limit:
            names = names[:max(0, args.limit - total_new)]
        if not names:
            continue
        names_path = os.path.join(WORK_DIR, "names.json")
        with io.open(names_path, "w", encoding="utf-8") as f:
            json.dump(names, f, ensure_ascii=False)
        cmd = [args.blender, "-b", "-P", TILE_SCRIPT, "--",
               gltf, names_path, args.out, matmap_path]
        r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
        out_lines = (r.stdout or "").splitlines()
        ok = 0
        for line in out_lines:
            if line.startswith("OK "):
                ok += 1
            elif line.startswith("MISS "):
                misses.append((code, line[5:].strip()))
            elif line.startswith("TILE_DONE"):
                pass
        if r.returncode != 0:
            print("  [blender 退出码 %d] %s" % (r.returncode, (r.stderr or "")[-400:]))
        # 以磁盘为准
        for n in names:
            if os.path.isfile(os.path.join(args.out, n + ".fbx")):
                done.add(n)
        total = len(done)
        el = time.time() - t0
        print("[%d/%d] %-10s %-22s 本瓦片 %d 个 · 累计 %d 个 · 用时 %.1f 分"
              % (idx, len(tiles), code, os.path.basename(gltf), ok, total, el / 60), flush=True)
        if args.limit and total - initial >= args.limit:
            break

    print("完成：%d 个 FBX → %s" % (len(done), args.out))
    if misses:
        print("未找到（%d）：%s" % (len(misses), misses[:10]))


if __name__ == "__main__":
    main()
