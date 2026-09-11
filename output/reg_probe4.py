# -*- coding: utf-8 -*-
"""配准探针 4：直接看 MapLand_j.TR5 原始字节结构。"""
import numpy as np

TK5 = r"E:\taikou5\Taikou5.Green.Edition-ALI213\Taikou5\MapLand_j.TR5"
raw = np.frombuffer(open(TK5, "rb").read(), dtype=np.uint8)

print("前 320 字节 hex:")
for i in range(0, 320, 32):
    print("  %04X: %s" % (i, " ".join("%02X" % b for b in raw[i:i + 32])))

for off in (0, 256, 512):
    print("\n===== off=%d 起，550 宽的 ASCII（#=0xFE海  +=0x0F平原  .=其他）前 40 行 =====" % off)
    g = raw[off:off + 550 * 448].reshape(448, 550)
    for j in range(0, 448, 11):
        row = ""
        for i in range(0, 550, 5):
            blk = g[j:j + 11, i:i + 5]
            sea = (blk == 0xFE).mean()
            plain = (blk == 0x0F).mean()
            row += "#" if sea > 0.5 else ("+" if plain > 0.5 else ".")
        print("  %3d %s" % (j, row))
