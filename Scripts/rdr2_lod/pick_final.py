# -*- coding: utf-8 -*-
"""从 _lodfix2 里筛出最终集：问题类别/过小物件回退原版，其余用重建版。

回退条件（用原版 TRF）：
  · 类别 ∈ rail / struct / rock（细杆薄壳，焊接会焊断）
  · 模型对角线 < 2.5 m（小道具，焊接距离相对过大）

输出 _lodfix3\（镜像类别目录），并统计总量。
"""
import io
import os
import re
import shutil
import sys

MAP = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
       r"\Mount & Blade II Bannerlord\Modules\RDR2Map")
SRC = os.path.join(MAP, "AssetSources", "ImportReady", "meshes")
V2 = os.path.join(MAP, "AssetSources", "_lodfix2")
OUT = os.path.join(MAP, "AssetSources", "_lodfix3")

THIN_CLASS = ("rail", "struct", "rock")
MIN_DIAG = 2.5


def diag_of(path, limit=400000):
    """只读前若干字节，取 LOD0 位置块算对角线。"""
    try:
        with io.open(path, encoding="utf-8", errors="replace") as f:
            lines = []
            for i, ln in enumerate(f):
                lines.append(ln.rstrip("\n"))
                if i > 4000:
                    break
        i = 2
        while i < len(lines):
            if not re.match(r"^\S+\.\d+ 0 \S+$", lines[i].strip()):
                i += 1
                continue
            i += 1
            npos = int(lines[i].strip())
            i += 1
            xs, ys, zs = [], [], []
            for k in range(npos):
                p = lines[i + k].split()
                xs.append(float(p[0])); ys.append(float(p[1])); zs.append(float(p[2]))
            if not xs:
                return 0.0
            dx = max(xs) - min(xs); dy = max(ys) - min(ys); dz = max(zs) - min(zs)
            return (dx * dx + dy * dy + dz * dz) ** 0.5
    except Exception:
        return 0.0
    return 0.0


def main():
    tot = kept = back = 0
    s_out = 0
    s_src = 0
    by_cls = {}
    for dirpath, _, names in os.walk(SRC):
        cls = os.path.basename(dirpath)
        if cls.lower() == "main_map_lod":
            continue
        for n in names:
            if not n.lower().endswith(".trf"):
                continue
            src = os.path.join(dirpath, n)
            rel = os.path.relpath(src, SRC)
            dst = os.path.join(OUT, rel)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            v2 = os.path.join(V2, rel)
            use_orig = False
            if cls in THIN_CLASS:
                use_orig = True
            elif not os.path.isfile(v2):
                use_orig = True
            else:
                d = diag_of(src)
                if d < MIN_DIAG:
                    use_orig = True
            pick = src if use_orig else v2
            shutil.copyfile(pick, dst)
            sz = os.path.getsize(dst)
            s_out += sz
            s_src += os.path.getsize(src)
            tot += 1
            if use_orig:
                back += 1
            else:
                kept += 1
            c = by_cls.setdefault(cls, [0, 0, 0])
            c[0] += 1
            c[1] += sz
            c[2] += 1 if use_orig else 0

    print("  文件 %d 个   重建版 %d   回退原版 %d (%.1f%%)"
          % (tot, kept, back, 100.0 * back / max(1, tot)))
    print("  原版合计 %.2f GB → 最终 %.2f GB   比例 %.1f%%"
          % (s_src / 2 ** 30, s_out / 2 ** 30, 100.0 * s_out / max(1, s_src)))
    print()
    print("  %-10s %6s %9s %8s %8s" % ("类别", "个数", "最终GB", "占原版", "回退数"))
    for c in sorted(by_cls, key=lambda x: -by_cls[x][1]):
        n, sz, b = by_cls[c]
        print("  %-10s %6d %9.2f %8s %8d" % (c, n, sz / 2 ** 30, "-", b))


if __name__ == "__main__":
    main()
