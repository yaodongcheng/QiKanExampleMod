"""RDR2 全图贴图 → 待导入目录（ModKit 批量 Import 用）。

产出（默认落 RDR2/AssetSources/ImortReady/）：
    rdr2_tex_albedo/   漫反射（材质槽 0）
    rdr2_tex_normal/   法线  （材质槽 2）
    rdr2_tex_orm/      金属/粗糙/AO（材质槽 4，暂不导）
    rdr2_tex_albedo/rdr2_flat_<hex>.png   无贴图材质的纯色替代（自动生成）

规则：
  · 名字 = 材质名（逐字节相同，已核对 3023/3023）。**一律不改名**。
  · 同名多份（不同据点导出的预览图）取**分辨率最高**的那份；已核对差异只是缩放，RMS 0~0.5。
  · Windows 大小写不敏感：同名仅大小写不同的（104 组）只留一个拼写，映射写进 manifest。
  · 输出统一 8bit RGB/RGBA、无附加块（PIL 重存；编辑器只认这种，见 plans/rules「ModKit 贴图 PNG 格式」）。

用法：
    python Scripts/prep_rdr2_textures.py                 # 全量
    python Scripts/prep_rdr2_textures.py --limit 30      # 先跑 30 张看格式
    python Scripts/prep_rdr2_textures.py --scale 0.5     # 512→256（体积 1/4，近景变糊）
"""
import argparse
import collections
import io
import json
import os
import sys

from PIL import Image

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

DEFAULT_IMAGES = r"D:\BrainMaker\大镖客2解包分析\export\previews"
DEFAULT_OUT = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
               r"\Mount & Blade II Bannerlord\Modules\RDR2\AssetSources\ImortReady")
DEFAULT_MANIFEST = (r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord"
                    r"\Modules\LivingWorldNpcs\Debug\RD2构造相关\rdr2_tex_manifest.json")


def scan(images_root):
    """扫所有据点预览瓦片，得到 材质 → {alb,nm,mr} 与 文件 → 据点列表。"""
    mats = collections.defaultdict(lambda: {"alb": None, "nm": None, "mr": None,
                                            "bc": None, "uses": 0})
    files = collections.defaultdict(set)          # 文件名 → {据点代码}
    settlements = sorted(d for d in os.listdir(images_root)
                         if os.path.isdir(os.path.join(images_root, d, "tiles")))
    for code in settlements:
        for fn in sorted(os.listdir(os.path.join(images_root, code, "tiles"))):
            if not fn.endswith(".gltf"):
                continue
            with io.open(os.path.join(images_root, code, "tiles", fn), encoding="utf-8") as f:
                j = json.load(f)
            imgs = j.get("images", [])
            texs = j.get("textures", [])

            def uri(ti):
                if ti is None:
                    return None
                src = texs[ti].get("source") if ti < len(texs) else None
                if src is None:
                    return None
                return os.path.basename(imgs[src].get("uri", "")) or None

            for m in j.get("materials", []):
                name = m.get("name")
                if not name:
                    continue
                pbr = m.get("pbrMetallicRoughness", {})
                a = uri(pbr.get("baseColorTexture", {}).get("index"))
                n = uri(m.get("normalTexture", {}).get("index"))
                s = uri(pbr.get("metallicRoughnessTexture", {}).get("index"))
                e = mats[name]
                e["uses"] += 1
                if a:
                    e["alb"] = a
                    files[a].add(code)
                if n:
                    e["nm"] = n
                    files[n].add(code)
                if s:
                    e["mr"] = s
                    files[s].add(code)
                bc = pbr.get("baseColorFactor")
                if bc:
                    e["bc"] = tuple(round(float(x), 3) for x in bc)
    return mats, files, settlements


def pick_best(images_root, fn, codes):
    """同名多份 → 取文件最大（= 分辨率最高）的那份。已核对内容只是缩放差异。"""
    best = None
    for code in sorted(codes):
        p = os.path.join(images_root, code, "textures", fn)
        if not os.path.isfile(p):
            continue
        sz = os.path.getsize(p)
        if best is None or sz > best[0]:
            best = (sz, p, code)
    return best


def canonical_map(files, mats):
    """小写名 → 规范拼写。规则：用得多的拼写胜出，平手取 sdn，再按字典序。"""
    weight = collections.Counter()
    for name, e in mats.items():
        for k in ("alb", "nm", "mr"):
            if e[k]:
                weight[e[k]] += e["uses"]
                weight[e[k]] += 1000 if "sdn" in files.get(e[k], ()) else 0
    groups = collections.defaultdict(list)
    for fn in files:
        groups[fn.lower()].append(fn)
    canon, alias = {}, {}
    for low, variants in groups.items():
        variants.sort(key=lambda x: (-weight[x], "sdn" not in files.get(x, ()), x))
        canon[low] = variants[0]
        for v in variants[1:]:
            alias[v] = variants[0]
    return canon, alias


def write_png(src, dst, scale):
    im = Image.open(src)
    if im.mode not in ("RGB", "RGBA"):
        im = im.convert("RGBA" if "A" in im.getbands() else "RGB")
    if scale != 1.0:
        w = max(4, int(round(im.size[0] * scale)))
        h = max(4, int(round(im.size[1] * scale)))
        im = im.resize((w, h), Image.LANCZOS)
    im.save(dst, format="PNG", optimize=False)
    return im.size, im.mode


def write_flat(dst, rgb):
    """纯色 8×8：给没有 albedo 贴图的材质当占位（按 baseColorFactor 上色）。"""
    col = tuple(max(0, min(255, int(round(c * 255)))) for c in rgb[:3])
    Image.new("RGB", (8, 8), col).save(dst, format="PNG", optimize=False)
    return col


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--images", default=DEFAULT_IMAGES)
    ap.add_argument("--out", default=DEFAULT_OUT)
    ap.add_argument("--manifest", default=DEFAULT_MANIFEST)
    ap.add_argument("--scale", type=float, default=1.0)
    ap.add_argument("--limit", type=int, default=0)
    args = ap.parse_args()

    print("扫描瓦片 …")
    mats, files, settlements = scan(args.images)
    canon, alias = canonical_map(files, mats)
    has_alb = {e["alb"] for e in mats.values() if e["alb"]}
    print("据点 %d 个 · 材质 %d 个 · 有漫反射的材质 %d 个 · 贴图文件 %d 个"
          % (len(settlements), len(mats), len(has_alb), len(files)))
    print("大小写冲突合并 %d 个拼写" % len(alias))

    dirs = {k: os.path.join(args.out, d) for k, d in
            (("alb", "rdr2_tex_albedo"), ("nm", "rdr2_tex_normal"), ("mr", "rdr2_tex_orm"))}
    for d in dirs.values():
        os.makedirs(d, exist_ok=True)

    # 每类要写的文件 = 规范名（去掉被别名顶掉的拼写）
    wanted = {"alb": set(), "nm": set(), "mr": set()}
    for e in mats.values():
        for k in wanted:
            if e[k]:
                wanted[k].add(canon[e[k].lower()])

    report = {"images": args.images, "out": args.out, "scale": args.scale,
              "settlements": settlements, "alias": alias, "categories": {},
              "materials": {}}
    for key in ("alb", "nm", "mr"):
        names = sorted(wanted[key])
        if args.limit:
            names = names[:args.limit]
        done = 0
        bytes_out = 0
        for i, fn in enumerate(names, 1):
            best = pick_best(args.images, fn, files[fn])
            if best is None:
                print("  [缺失] %s" % fn)
                continue
            dst = os.path.join(dirs[key], os.path.splitext(fn)[0] + ".png")
            size, mode = write_png(best[1], dst, args.scale)
            bytes_out += os.path.getsize(dst)
            done += 1
            if i % 200 == 0 or i == len(names):
                print("  %s: %d/%d  最近 %s %dx%d %s"
                      % (key, i, len(names), fn, size[0], size[1], mode))
        report["categories"][key] = {"count": done, "bytes": bytes_out,
                                     "dir": dirs[key]}
        print("%s 完成：%d 张，%.1f MB → %s" % (key, done, bytes_out / 1e6, dirs[key]))

    # 无贴图材质 → 纯色占位贴图
    flat_colors = collections.Counter()
    for name, e in mats.items():
        if e["alb"]:
            continue
        flat_colors[e["bc"] or (1.0, 1.0, 1.0)] += 1
    flat_map = {}
    for rgb, _n in flat_colors.most_common():
        hexs = "%02x%02x%02x" % tuple(max(0, min(255, int(round(c * 255)))) for c in rgb[:3])
        tname = "rdr2_flat_" + hexs
        if tname in flat_map.values():
            continue
        write_flat(os.path.join(dirs["alb"], tname + ".png"), rgb)
        flat_map[rgb] = tname
    print("纯色占位贴图 %d 张（覆盖 %d 个无贴图材质）"
          % (len(flat_map), sum(flat_colors.values())))

    # 材质 → 贴图（生成材质时直接读它，不再解析 glTF）
    for name, e in mats.items():
        def cn(x):
            return os.path.splitext(canon[x.lower()])[0] if x else None
        report["materials"][name] = {
            "alb": cn(e["alb"]) if e["alb"] else flat_map[e["bc"] or (1.0, 1.0, 1.0)],
            "nm": cn(e["nm"]),
            "mr": cn(e["mr"]),
            "uses": e["uses"],
            "flat": not bool(e["alb"]),
        }
    os.makedirs(os.path.dirname(args.manifest), exist_ok=True)
    with io.open(args.manifest, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=1)
    print("manifest → %s" % args.manifest)


if __name__ == "__main__":
    main()
