# -*- coding: utf-8 -*-
"""Audit v2: resolve每个材质的 ab/nm/ma 贴图，并核对 DDS 目录覆盖情况。"""
import io, os, re

RDR2 = r"D:\BrainMaker\大镖客2解包分析"
TEX = os.path.join(RDR2, "deliverable", "textures")
ALB, NMD, MAD = os.path.join(TEX, "alb"), os.path.join(TEX, "nm"), os.path.join(TEX, "ma")
DDS = os.path.join(RDR2, "deliverable", "compress", "textures", "100_原分辨率")
PNGD = os.path.join(RDR2, "deliverable", "compress", "textures", "100_原分辨率_png")
TRF = os.path.join(RDR2, "deliverable", "compress", "trf", "lodtest_bank_r33_l07.trf")

HDR = re.compile(r"^(\S+\.\d+) 0 (\S+)$")


def slot_materials(path):
    out = {}
    for ln in io.open(path, encoding="utf-8", errors="replace"):
        m = HDR.match(ln.rstrip("\n"))
        if m:
            k = int(re.search(r"\.(\d+)$", m.group(1)).group(1))
            out[k] = m.group(2)
    return dict(sorted(out.items()))


def ls(d):
    return sorted(os.listdir(d)) if os.path.isdir(d) else []


def stems_of(mat):
    """候选贴图词干，从最具体到最泛"""
    out = [mat]
    if mat.endswith("_ab"):
        out.append(mat[:-3])
    # 拼接名：A_ab + B_al -> 取前半段再剥 _ab
    if mat.count("_ab") >= 1 and mat.count("_al") >= 1:
        head = mat.split("_al")[0]
        if head.endswith("_ab"):
            out.append(head)
            out.append(head[:-3])
    if mat.endswith("_al"):
        out.append(mat[:-3])
    seen, res = set(), []
    for x in out:
        if x and x not in seen:
            seen.add(x); res.append(x)
    return res


def pick(files, stems, suffix, allow_exact_only=False):
    """1) <stem><suffix>.png 精确  2) <stem>.png（拼接名用）  3) 前缀/包含匹配"""
    s = set(files)
    for st in stems:
        f = st + suffix + ".png"
        if f in s:
            return f, "精确"
    for st in stems:
        f = st + ".png"
        if f in s:
            return f, "整体名"
    best = None
    for fn in files:
        if not fn.endswith(".png"):
            continue
        base = fn[:-4]
        for st in stems:
            if base and (st.startswith(base) or base.startswith(st)):
                if best is None or len(base) > len(best[0]):
                    best = (base, fn)
    if best:
        return best[1], "前缀"
    return None, None


mats = slot_materials(TRF)
alb, nm, ma = ls(ALB), ls(NMD), ls(MAD)
dds, pngd = set(ls(DDS)), set(ls(PNGD))

print("%-3s %-52s | %-26s %-26s %-26s" % ("槽", "TRF 材质名", "albedo", "normal", "material"))
print("-" * 140)
rows = []
for k, mat in mats.items():
    st = stems_of(mat)
    fa, ka = pick(alb, st, "_ab")
    fn, kn = pick(nm, st, "_nm")
    fm, km = pick(ma, st, "_ma")
    rows.append((k, mat, fa, ka, fn, kn, fm, km))
    def cell(f, kind):
        if not f:
            return "缺失"
        base = f[:-4]
        ind = "✓DDS" if (base + ".dds") in dds else ("PNG" if f in pngd else "✗未打包")
        return "%s %s %s" % (kind, base[:17], ind)
    print("%-3d %-52s | %-26s %-26s %-26s" % (k, mat[:52], cell(fa, ka), cell(fn, kn), cell(fm, km)))

print("\n=== 汇总 ===")
full = [r for r in rows if r[2] and r[4] and r[6]]
no_nm = [r for r in rows if r[2] and not r[4]]
no_ma = [r for r in rows if r[2] and not r[6]]
print("  三类图齐全: %d / %d" % (len(full), len(rows)))
for r in rows:
    if not r[2]:
        print("  ✗ 连 albedo 都没有: 槽 %d %s" % (r[0], r[1]))
    elif not r[4] or not r[6]:
        print("  △ 槽 %2d %-46s  nm=%s  ma=%s" % (r[0], r[1][:46], bool(r[4]), bool(r[6])))

print("\n=== 源目录里到底有没有 nm/ma ===")
for k, mat, fa, ka, fn, kn, fm, km in rows:
    if fa and (not fn or not fm):
        base = fa[:-4]
        # 在 nm/ma 目录里找任何包含 base 主干的文件
        probe = base.split("_ab")[0]
        cand_n = [x for x in nm if probe in x]
        cand_m = [x for x in ma if probe in x]
        print("  槽 %2d probe=%-40s nm目录含此串:%d   ma目录含此串:%d"
              % (k, probe[:40], len(cand_n), len(cand_m)))
        for x in (cand_n[:2] + cand_m[:2]):
            print("        %s" % x)
