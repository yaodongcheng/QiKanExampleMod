# -*- coding: utf-8 -*-
"""Audit v4: albedo includes _al files; both-direction prefix allowed (共用贴图)."""
import io, os, re

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
TEX = os.path.join(RDR2, "deliverable", "textures")
ALB, NMD, MAD = (os.path.join(TEX, x) for x in ("alb", "nm", "ma"))
TRF = os.path.join(RDR2, "deliverable", "compress", "trf", "lodtest_bank_r33_l07.trf")
HDR = re.compile(r"^(\S+\.\d+) 0 (\S+)$")


def slot_materials(path):
    out = {}
    for ln in io.open(path, encoding="utf-8", errors="replace"):
        m = HDR.match(ln.rstrip("\n"))
        if m:
            out[int(re.search(r"\.(\d+)$", m.group(1)).group(1))] = m.group(2)
    return dict(sorted(out.items()))


def load(d):
    out = []
    for fn in os.listdir(d) if os.path.isdir(d) else []:
        if fn.lower().endswith(".png"):
            out.append((fn[:-4].lower(), fn))
    return out


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


MOD = re.compile(r"(_flat|_01a|_a|a|_b|_\d+|_high|_lowres)*$")


def match(files, mat, kind):
    cand = variants(mat)
    suf = "_" + kind          # 比较时剥掉贴图类型后缀，否则共用贴图匹配不到
    scored = []
    for base, real in files:
        core = base[:-len(suf)] if base.endswith(suf) else base
        for v in cand:
            if base == v:
                scored.append((5, len(base), real, "精确"))
            elif core == v:
                scored.append((5, len(base), real, "精确"))
            elif core.startswith(v):
                tail = core[len(v):]
                if MOD.fullmatch(tail):
                    scored.append((4, len(core), real, "变体后缀"))
                else:
                    scored.append((1, len(core), real, "前缀近似"))
            elif v.startswith(core) and len(core) >= 12:
                scored.append((3, len(core), real, "共用"))
            elif base.startswith(v):
                scored.append((1, len(base), real, "带后缀近似"))
    if not scored:
        return None, None
    scored.sort(key=lambda x: (-x[0], -x[1]))
    return scored[0][2], scored[0][3]


mats = slot_materials(TRF)
alb, nm, ma = load(ALB), load(NMD), load(MAD)

# 只保留对应后缀的文件，避免 ab 匹配到 nm
alb = [(b, f) for b, f in alb if not b.endswith(("_nm", "_ma"))]
nm = [(b, f) for b, f in nm if b.endswith("_nm")]
ma = [(b, f) for b, f in ma if b.endswith("_ma")]

print("%-3s %-50s | %-28s %-28s %-28s" % ("槽", "材质名", "albedo", "normal", "material"))
print("-" * 145)
plan = []
for k, mat in mats.items():
    fa, ka = match(alb, mat, "ab")
    fn, kn = match(nm, mat, "nm")
    fm, km = match(ma, mat, "ma")
    plan.append((k, mat, fa, ka, fn, kn, fm, km))
    c = lambda f, hh: ("%s %s" % (hh, f[:23])) if f else "缺失"
    print("%-3d %-50s | %-28s %-28s %-28s" % (k, mat[:50], c(fa, ka), c(fn, kn), c(fm, km)))

full = sum(1 for r in plan if r[2] and r[4] and r[6])
print("\n三类齐全 %d/%d" % (full, len(plan)))
print("\n仍缺的：")
for k, mat, fa, ka, fn, kn, fm, km in plan:
    if not (fa and fn and fm):
        print("  槽 %2d %-46s ab=%s nm=%s ma=%s"
              % (k, mat[:46], bool(fa), bool(fn), bool(fm)))

print("\n需要打包的贴图 %d 个：" % len({r[2] for r in plan if r[2]} | {r[4] for r in plan if r[4]} | {r[6] for r in plan if r[6]}))
for f in sorted({r[2] for r in plan if r[2]} | {r[4] for r in plan if r[4]} | {r[6] for r in plan if r[6]}):
    print("   " + f)
