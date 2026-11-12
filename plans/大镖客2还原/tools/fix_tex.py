# -*- coding: utf-8 -*-
"""Rebuild the texture sets with CORRECT resolution for concatenated材质名.

规则：
  albedo : 优先 <材质名>.png（拼接名用整体名），否则 <stem>_ab.png
  normal : <stem>_nm.png / 前缀匹配
  material: <stem>_ma.png / 前缀匹配
  DDS 资产名 = 源文件名去掉 .png
"""
import io, os, re, shutil, subprocess
from PIL import Image

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
TEX = os.path.join(RDR2, "deliverable", "textures")
ALB, NMD, MAD = os.path.join(TEX, "alb"), os.path.join(TEX, "nm"), os.path.join(TEX, "ma")
OUT = os.path.join(RDR2, "deliverable", "compress", "textures")
NVC = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\tools\TextureConverter\NTool\nvcompress.exe"
TRF = os.path.join(RDR2, "deliverable", "compress", "trf", "lodtest_bank_r33_l07.trf")

HDR = re.compile(r"^(\S+\.\d+) 0 (\S+)$")


def slot_materials(path):
    out = {}
    for ln in io.open(path, encoding="utf-8", errors="replace"):
        m = HDR.match(ln.rstrip("\n"))
        if m:
            out[int(re.search(r"\.(\d+)$", m.group(1)).group(1))] = m.group(2)
    return dict(sorted(out.items()))


def base_stem(mat):
    """拼接名 A_ab + B_al -> A 的词干"""
    parts = re.split(r"_ab(?=[A-Za-z])", mat)
    head = parts[0]
    if len(parts) > 1:
        return head
    if mat.endswith("_ab"):
        return mat[:-3]
    return mat


def ls(d):
    return sorted(os.listdir(d)) if os.path.isdir(d) else []


def resolve(files, mat, suffix):
    s = set(files)
    # 1) 整体名（拼接材质）
    if suffix == "_ab":
        f = mat + ".png"
        if f in s:
            return f, "整体名"
    st = base_stem(mat)
    # 2) 词干 + 后缀
    f = st + suffix + ".png"
    if f in s:
        return f, "精确"
    # 3) 前缀匹配（共用贴图，如 nbx_fabric_curtains01_ma）
    best = None
    for fn in files:
        if not fn.endswith(suffix + ".png"):
            continue
        b = fn[:-len(suffix + ".png")]
        if st.startswith(b) or b.startswith(st):
            if best is None or len(b) > len(best[0]):
                best = (b, fn)
    if best:
        return best[1], "共用"
    return None, None


def has_alpha(p):
    im = Image.open(p)
    if im.mode not in ("RGBA", "LA", "PA"):
        return False
    return im.convert("RGBA").getchannel("A").getextrema()[0] < 250


def run(args):
    r = subprocess.run([NVC] + args, capture_output=True, text=True)
    if r.returncode != 0:
        print("   FAIL %s\n   %s" % (args[-1], (r.stderr or r.stdout)[:200]))
        return False
    return True


mats = slot_materials(TRF)
alb, nm, ma = ls(ALB), ls(NMD), ls(MAD)

plan = []
print("%-3s %-52s %-30s %-30s %-30s" % ("槽", "材质名", "albedo", "normal", "material"))
for k, mat in mats.items():
    fa, ka = resolve(alb, mat, "_ab")
    fn, kn = resolve(nm, mat, "_nm")
    fm, km = resolve(ma, mat, "_ma")
    plan.append((k, mat, fa, ka, fn, kn, fm, km))
    f = lambda x, kk: ("%s %s" % (kk or "缺", (x or "")[:24])) if x else "缺失"
    print("%-3d %-52s %-30s %-30s %-30s" % (k, mat[:52], f(fa, ka), f(fn, kn), f(fm, km)))

# 去重出贴图清单
todo = {}
for k, mat, fa, ka, fn, kn, fm, km in plan:
    for src, d in ((fa, ALB), (fn, NMD), (fm, MAD)):
        if src:
            todo[src] = os.path.join(d, src)
print("\n需要打包的贴图 %d 个" % len(todo))

SCALES = [(1.0, "100_原分辨率"), (0.5, "50_半分辨率"), (0.25, "25_四分之一")]
for scale, tag in SCALES:
    d = os.path.join(OUT, tag)
    if os.path.isdir(d):
        shutil.rmtree(d)
    os.makedirs(d)
    tot = 0
    for name, src in sorted(todo.items()):
        base = name[:-4]
        kind = "nm" if name.endswith("_nm.png") else ("ma" if name.endswith("_ma.png") else "ab")
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
    print("  [%s] %d 个 DDS  %.2f MB" % (tag, len(todo), tot / 1024 / 1024))

# PNG 兜底
pd = os.path.join(OUT, "100_原分辨率_png")
if os.path.isdir(pd):
    shutil.rmtree(pd)
os.makedirs(pd)
for name, src in sorted(todo.items()):
    shutil.copy2(src, os.path.join(pd, name))
print("  [PNG 兜底] %d 个" % len(todo))

# matmap
csvp = os.path.join(RDR2, "deliverable", "compress", "matmap.csv")
with io.open(csvp, "w", encoding="utf-8-sig", newline="") as f:
    f.write("slot,material,albedo,albedo_match,normal,normal_match,ma,ma_match\n")
    for k, mat, fa, ka, fn, kn, fm, km in plan:
        g = lambda x: x[:-4] if x else ""
        f.write("%d,%s,%s,%s,%s,%s,%s,%s\n"
                % (k, mat, g(fa), ka or "", g(fn), kn or "", g(fm), km or ""))
print("\nmatmap.csv 已重写（%d 行，含匹配方式）" % len(plan))
