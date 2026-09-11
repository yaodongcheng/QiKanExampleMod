# -*- coding: utf-8 -*-
"""配准探针 3：扫描 MapLand_j.TR5 起始偏移，找到真正的 550x448 网格。"""
import numpy as np
from scipy import ndimage

TK5 = r"E:\taikou5\Taikou5.Green.Edition-ALI213\Taikou5\MapLand_j.TR5"
raw = np.frombuffer(open(TK5, "rb").read(), dtype=np.uint8)
N = raw.size

cands = []
for off in range(0, 1200):
    for shape in ((448, 550), (550, 448)):
        need = shape[0] * shape[1]
        if off + need > N:
            continue
        g = raw[off:off + need].reshape(shape)
        sea = g == 0xFE
        sf = sea.mean()
        if not (0.25 < sf < 0.50):
            continue
        lab, n = ndimage.label(~sea)
        sizes = np.sort(ndimage.sum(~sea, lab, range(1, n + 1)))[::-1]
        big = int((sizes > 800).sum())
        # 日本：海=1 大块；陆=4 大岛 (北海道/本州/四国/九州)
        if big == 4 and int((sizes > 20).sum()) < 400:
            cands.append((off, shape, sf, big, sizes[:8].astype(int).tolist()))

for c in cands[:10]:
    print("off=%d shape=%s sea=%.3f big=%d sizes=%s" % c)


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


if cands:
    off, shape, _, _, _ = cands[0]
    g = raw[off:off + shape[0] * shape[1]].reshape(shape)
    print("\n===== 最佳候选 off=%d shape=%s（海 = . ）=====" % (off, shape))
    for line in ascii_map(g != 0xFE):
        print(line)
