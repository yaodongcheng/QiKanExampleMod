# -*- coding: utf-8 -*-
"""配准探针 2：定 TK5 MapLand_j.TR5 的朝向与海陆取值。"""
import numpy as np

TK5 = r"E:\taikou5\Taikou5.Green.Edition-ALI213\Taikou5\MapLand_j.TR5"
raw = np.frombuffer(open(TK5, "rb").read(), dtype=np.uint8)
print("字节数:", raw.size, " 550*448 =", 550 * 448, " 448*550 =", 448 * 550)
vals, cnts = np.unique(raw, return_counts=True)
print("全量取值分布:")
for v, c in zip(vals.tolist(), cnts.tolist()):
    print("   0x%02X (%3d): %7d  %5.2f%%" % (v, v, c, 100 * c / raw.size))


def ascii_map(mask, w=104, h=38):
    H, W = mask.shape
    out = []
    for j in range(h):
        row = ""
        for i in range(w):
            blk = mask[j * H // h:(j + 1) * H // h, i * W // w:(i + 1) * W // w]
            f = blk.mean()
            row += "#" if f > 0.6 else ("+" if f > 0.3 else ".")
        out.append(row)
    return out


for name, arr in (("550x448", raw.reshape(448, 550)), ("448x550(转置)", raw.reshape(550, 448).T)):
    for seaval in (0xFE, 0xFF):
        land = arr != seaval
        print("\n===== %s  sea=0x%02X  (陆地 %.1f%%) =====" % (name, seaval, 100 * land.mean()))
        for line in ascii_map(land):
            print(line)
