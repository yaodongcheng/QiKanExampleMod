# -*- coding: utf-8 -*-
"""Final texture packaging: 大小写无关 + 共用贴图 + 拼接名，输出 31 张贴图。"""
import io, os, re, shutil, subprocess
from PIL import Image

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
TEX = os.path.join(RDR2, "deliverable", "textures")
ALB, NMD, MAD = (os.path.join(TEX, x) for x in ("alb", "nm", "ma"))
OUT = os.path.join(RDR2, "deliverable", "compress", "textures")
NVC = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\TextureConverter\NTool\nvcompress.exe"
TRF = os.path.join(RDR2, "deliverable", "compress", "trf", "lodtest_bank_r33_l07.trf")
HDR = re.compile(r"^(\S+\.\d+) 0 (\S+)$")
MOD = re.compile(r"(_flat|_01a|_a|a|_b|_\d+|_high|_lowres)*$")


def slot_materials(path):
    out = {}
    for ln in io.open(path, encoding="utf-8", errors="replace"):
        m = HDR.match(ln.rstrip("\n"))
        if m:
            out[int(re.search(r"\.(\d+)$", m.group(1)).group(1))] = m.group(2)
    return dict(sorted(out.items()))


def load(d):
    return [(fn[:-4].lower(), fn) for fn in sorted(os.listdir(d))
            if fn.lower().endswith(".png")]


def variants(mat):
    n = mat.lower()
    vs = [n]
    for suf in ("_ab", "_al", "_nm", "_ma"):
        if n.endswith(suf):
            vs.append(n[:-len(suf)])
    parts = re.split(r"_ab(?=[a-z])", n)
    if len(parts) > 1:
        vs.append(parts[0])
    out = []
    for v in vs:
        if v and v not in out:
            out.append(v)
    return out


def match(files, mat, kind):
    cand = variants(mat)
    suf = "_" + kind
    scored = []
    for base, real in files:
        core = base[:-len(suf)] if base.endswith(suf) else base
        for v in cand:
            if base == v or core == v:
                scored.append((5, len(base), real, "精确"))
            elif core.startswith(v) and MOD.fullmatch(core[len(v):]):
                scored.append((4, len(core), real, "变体后缀"))
            elif v.startswith(core) and len(core) >= 12:
                scored.append((3, len(core), real, "共用"))
    if not scored:
        return None, None
    scored.sort(key=lambda x: (-x[0], -x[1]))
    return scored[0][2], scored[0][3]


def has_alpha(p):
    im = Image.open(p)
    if im.mode not in ("RGBA", "LA", "PA"):
        return False
    return im.convert("RGBA").getchannel("A").getextrema()[0] < 250


def run(args):
    r = subprocess.run([NVC] + args, capture_output=True, text=True)
    if r.returncode != 0:
        print("   FAIL %s  %s" % (args[-1], (r.stderr or r.stdout)[:160]))
        return False
    return True


mats = slot_materials(TRF)
alb, nm, ma = load(ALB), load(NMD), load(MAD)
alb = [(b, f) for b, f in alb if not b.endswith(("_nm", "_ma"))]
nm = [(b, f) for b, f in nm if b.endswith("_nm")]
ma = [(b, f) for b, f in ma if b.endswith("_ma")]

plan = []
todo = {}
for k, mat in mats.items():
    fa, ka = match(alb, mat, "ab")
    fn, kn = match(nm, mat, "nm")
    fm, km = match(ma, mat, "ma")
    plan.append((k, mat, fa, ka, fn, kn, fm, km))
    for src, d in ((fa, ALB), (fn, NMD), (fm, MAD)):
        if src:
            todo[src] = os.path.join(d, src)

print("材质 %d 个，贴图 %d 张" % (len(plan), len(todo)))
print("%-3s %-50s %-26s %-26s %-26s" % ("槽", "材质名", "albedo", "normal", "material"))
for k, mat, fa, ka, fn, kn, fm, km in plan:
    c = lambda f, hh: ("%s:%s" % (hh, f[:20])) if f else "缺"
    print("%-3d %-50s %-26s %-26s %-26s" % (k, mat[:50], c(fa, ka), c(fn, kn), c(fm, km)))

for scale, tag in ((1.0, "100_原分辨率"), (0.5, "50_半分辨率"), (0.25, "25_四分之一")):
    d = os.path.join(OUT, tag)
    if os.path.isdir(d):
        shutil.rmtree(d)
    os.makedirs(d)
    tot = 0
    for name, src in sorted(todo.items()):
        base = name[:-4]
        low = name.lower()
        kind = "nm" if low.endswith("_nm.png") else ("ma" if low.endswith("_ma.png") else "ab")
        im = Image.open(src)
        im = im.convert("RGBA" if has_alpha(src) else "RGB")
        if scale != 1.0:
            im = im.resize((max(4, int(round(im.width * scale))),
                            max(4, int(round(im.height * scale)))), Image.LANCZOS)
        tmp = os.path.join(d, base + ".__tmp.png")
        im.save(tmp)
        out = os.path.join(d, base + ".dds")
        if kind == "nm":
            args = ["-bc5", tmp, out]
        elif has_alpha(src):
            args = ["-bc3", "-alpha", "-alpha_dithering", tmp, out]
        else:
            args = ["-bc1", tmp, out]
        if run(args):
            tot += os.path.getsize(out)
        os.remove(tmp)
    print("  [%s] %d 个 DDS  %.2f MiB" % (tag, len(todo), tot / 1024 / 1024))

pd = os.path.join(OUT, "100_原分辨率_png")
if os.path.isdir(pd):
    shutil.rmtree(pd)
os.makedirs(pd)
for name, src in sorted(todo.items()):
    shutil.copy2(src, os.path.join(pd, name))
print("  [PNG 兜底] %d 个  %.2f MiB"
      % (len(todo), sum(os.path.getsize(os.path.join(pd, n)) for n in todo) / 1024 / 1024))

csvp = os.path.join(RDR2, "deliverable", "compress", "matmap.csv")
with io.open(csvp, "w", encoding="utf-8-sig", newline="") as f:
    f.write("slot,material,albedo,albedo_match,normal,normal_match,ma,ma_match\n")
    for k, mat, fa, ka, fn, kn, fm, km in plan:
        g = lambda x: x[:-4] if x else ""
        f.write("%d,%s,%s,%s,%s,%s,%s,%s\n"
                % (k, mat, g(fa), ka or "", g(fn), kn or "", g(fm), km or ""))
print("matmap.csv 已重写")
