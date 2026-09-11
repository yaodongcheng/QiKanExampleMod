# -*- coding: utf-8 -*-
"""配准探针 5：MapLand_j.TR5 按 u16/格 解（275x448）。"""
import numpy as np

TK5 = r"E:\taikou5\Taikou5.Green.Edition-ALI213\Taikou5\MapLand_j.TR5"
raw = open(TK5, "rb").read()
u16 = np.frombuffer(raw, dtype="<u2")
vals, cnts = np.unique(u16, return_counts=True)
order = np.argsort(cnts)[::-1]
print("u16 取值 Top15:", [(int(vals[i]), int(cnts[i])) for i in order[:15]])
print("不同取值个数:", vals.size)

g = u16.reshape(448, 275)
sea_val = int(vals[order[0]])
print("最常见值 = 0x%04X (视为海)" % sea_val)
for j in range(0, 448, 11):
    row = ""
    for i in range(0, 275, 3):
        blk = g[j:j + 11, i:i + 3]
        row += "#" if (blk == sea_val).mean() > 0.5 else "."
    print("  %3d %s" % (j, row))
