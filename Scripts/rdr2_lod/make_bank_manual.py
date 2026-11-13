# -*- coding: utf-8 -*-
"""给 bank 生成手工导入测试件：v → 1-v，保留原版 .lodN. 部件命名。

输出：AssetSources\\_manual_test\\new_com_02_bank_uvfix.trf
"""
import io
import os
import re

MAP = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
       r"\Mount & Blade II Bannerlord\Modules\RDR2Map")
SRC = os.path.join(MAP, "AssetSources", "_meshes_orig", "bldg", "new_com_02_bank.trf")
OUTDIR = os.path.join(MAP, "AssetSources", "_manual_test")
NAME = "new_com_02_bank_uvfix"


def main():
    os.makedirs(OUTDIR, exist_ok=True)
    L = io.open(SRC, encoding="utf-8", errors="replace").read().split("\n")
    print("源: %d 行  %s / %s" % (len(L), L[0].strip(), L[1].strip()))

    levels = {}
    i = 2
    npart = 0
    while i < len(L):
        m = re.match(r"^(\S+?)(?:\.lod(\d+))?\.(\d+) 0 (\S+)$", L[i].strip())
        if not m:
            i += 1
            continue
        lvl = int(m.group(2) or 0)
        levels[lvl] = levels.get(lvl, 0) + 1
        npart += 1
        i += 1
        npos = int(L[i].strip())
        i += 1 + npos
        if L[i].strip() == "0":
            i += 1
        nvert = int(L[i].strip())
        i += 1
        for k in range(nvert):
            b = i + k * 3 + 1
            t = L[b].split()
            if len(t) >= 2:
                try:
                    t[1] = "%.6f" % (1.0 - float(t[1]))
                    L[b] = " ".join(t)
                except ValueError:
                    pass
        i += nvert * 3
        if L[i].strip() == "0":
            i += 1
        ntri = int(L[i].strip())
        i += 1 + ntri

    print("部件 %d 个，各档部件数: %s" % (npart, dict(sorted(levels.items()))))

    text = "\n".join(L)
    text = re.sub(r"(?m)^new_com_02_bank(\.lod\d+)?\.(\d+) 0 ",
                  lambda m: "%s%s.%s 0 " % (NAME, m.group(1) or "", m.group(2)), text)
    dst = os.path.join(OUTDIR, NAME + ".trf")
    io.open(dst, "w", encoding="utf-8", newline="\n").write(text)
    print("输出: %s  %d B" % (dst, os.path.getsize(dst)))

    # 校验命名
    bad = 0
    for ln in text.split("\n"):
        if re.match(r"^\S+ 0 \S+$", ln) and " 0 " in ln and ".lod" not in ln and not re.match(r"^\S+\.\d+ 0 ", ln):
            bad += 1
    print("命名检查：非标准行 %d" % bad)


if __name__ == "__main__":
    main()
