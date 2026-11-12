# -*- coding: utf-8 -*-
"""Assemble an import-ready package: 3 LOD0 TRF variants + texture sets as DDS.

  python prep_import.py
"""
import io, os, re, shutil, subprocess, sys
from PIL import Image

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
DEL = os.path.join(RDR2, "deliverable")
ALB = os.path.join(DEL, "textures", "alb")
NMD = os.path.join(DEL, "textures", "nm")
MAD = os.path.join(DEL, "textures", "ma")
SRC_TRF = os.path.join(RDR2, "out", "lod_trf")
OUT = os.path.join(RDR2, "out", "lod_import")
NVC = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\TextureConverter\NTool\nvcompress.exe"
NAME = "new_com_02_bank"
VARIANTS = [("1_原版LOD0", "lodtest_bank_orig"),
            ("2_减面33_按部件", "lodtest_bank_r33"),
            ("3_减面50_均匀", "lodtest_bank_r50")]


# ---------------------------------------------------------------- textures
def slist(d):
    return os.listdir(d) if os.path.isdir(d) else []


_alb = slist(ALB); _nm = slist(NMD); _ma = slist(MAD)
_alb_set = set(_alb); _nm_set = set(_nm); _ma_set = set(_ma)


def resolve_in(files, matname, suffix):
    """exact <matname><suffix>.png first, then longest file-progressively-matching"""
    f = matname + suffix + ".png"
    if f in files:
        return f
    best = None
    for fn in files:
        if not fn.endswith(suffix + ".png"):
            continue
        stem = fn[:-len(suffix + ".png")]      # e.g. nbx_x_ab  -> nbx_x
        if matname.startswith(stem) and (best is None or len(stem) > len(best[0])):
            best = (stem, fn)
    return best[1] if best else None


def materials_from_trf(path):
    mats = []
    pat = re.compile(r"^(\S+) 0 (\S+)$")
    for ln in io.open(path, encoding="utf-8", errors="replace"):
        m = pat.match(ln.rstrip("\n"))
        if m:
            mats.append(m.group(2))
    return mats


def has_alpha(p):
    im = Image.open(p)
    if im.mode not in ("RGBA", "LA", "PA"):
        return False
    a = im.convert("RGBA").getchannel("A")
    return a.getextrema()[0] < 250


def run_nvc(args):
    r = subprocess.run([NVC] + args, capture_output=True, text=True)
    if r.returncode != 0:
        print("    FAIL %s\n%s" % (args, (r.stderr or r.stdout)[:300]))
        return False
    return True


def build_texture_set(scale, tag, files):
    """files: list of (src_png, out_basename, kind)  kind in ab/nm/ma"""
    d = os.path.join(OUT, "textures", tag)
    os.makedirs(d, exist_ok=True)
    tot_png = tot_dds = 0
    for src, base, kind in files:
        dst_png = os.path.join(d, base + ".png")
        im = Image.open(src).convert("RGB" if not has_alpha(src) else "RGBA")
        if scale != 1.0:
            w = max(4, int(round(im.width * scale)))
            h = max(4, int(round(im.height * scale)))
            im = im.resize((w, h), Image.LANCZOS)
        im.save(dst_png)
        args = []
        if kind == "nm":
            args = ["-bc5", dst_png, os.path.join(d, base + ".dds")]
        elif has_alpha(src):
            args = ["-bc3", "-alpha", "-alpha_dithering", dst_png, os.path.join(d, base + ".dds")]
        else:
            args = ["-bc1", dst_png, os.path.join(d, base + ".dds")]
        tot_png += os.path.getsize(dst_png)
        if run_nvc(args):
            dds = os.path.join(d, base + ".dds")
            tot_dds += os.path.getsize(dds)
        os.remove(dst_png)                     # keep only dds in the output
    print("  [%s] %d files -> dds total %.3f MB (png was %.3f MB)"
          % (tag, len(files), tot_dds / 1024 / 1024, tot_png / 1024 / 1024))
    return tot_dds


# ---------------------------------------------------------------- trf rename
def rename_trf(src, dst, newname):
    out = []
    pat = re.compile(r"^([^\s]+) 0 (\S+)$")
    for ln in io.open(src, encoding="utf-8", errors="replace"):
        s = ln.rstrip("\n")
        m = pat.match(s)
        if m and s != "rfver 4":
            rest = m.group(1).split(".")
            rest[0] = newname
            s = ".".join(rest) + " 0 " + m.group(2)
        out.append(s)
    io.open(dst, "w", encoding="utf-8", newline="\n").write("\n".join(out) + "\n")


def main():
    trf0 = os.path.join(SRC_TRF, "1_原版LOD0", NAME + ".trf")
    mats = materials_from_trf(trf0)
    print("materials = %d" % len(mats))

    files = []
    seen = set()
    for m in mats:
        for kind, d, suf, fileset in (("ab", ALB, "_ab", _alb_set),
                                      ("nm", NMD, "_nm", _nm_set),
                                      ("ma", MAD, "_ma", _ma_set)):
            fn = resolve_in(fileset, m, suf)
            if not fn:
                continue
            base = fn[:-4]
            if base in seen:
                continue
            seen.add(base)
            files.append((os.path.join(d, fn), base, kind))
    print("resolved texture files = %d" % len(files))
    for src, base, kind in files:
        print("   %-4s %-56s %s" % (kind, base[:56], "%dx%d" % Image.open(src).size))

    print("\n== building texture sets ==")
    sizes = {}
    for scale, tag in ((1.0, "100_原分辨率"), (0.5, "50_半分辨率"), (0.25, "25_四分之一")):
        sizes[tag] = build_texture_set(scale, tag, files)

    print("\n== assembling package ==")
    os.makedirs(OUT, exist_ok=True)
    for sub, newname in VARIANTS:
        d = os.path.join(OUT, "trf")
        os.makedirs(d, exist_ok=True)
        src = os.path.join(SRC_TRF, sub, NAME + ".trf")
        dst = os.path.join(d, newname + ".trf")
        rename_trf(src, dst, newname)
        print("  %-34s %.2f MB" % (os.path.basename(dst), os.path.getsize(dst) / 1024 / 1024))

    print("\ntexture set totals:")
    for k, v in sizes.items():
        print("  %-16s %.3f MB" % (k, v / 1024 / 1024))
    print("\nOUT = %s" % OUT)


main()
