# -*- coding: utf-8 -*-
"""探针：从 terrain.bin 抠 HGHT 段 PNG → 世界米高度场，验几个已知点。"""
import io
import re
import sys
import numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS = None

BIN = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou\SceneObj\Main_map\terrain.bin"
d = open(BIN, "rb").read()
print("magic:", d[:8])

# 段表：8B magic + u32 ver + 若干 (4B tag + u32 count + u32 size + u32 offset)
pos = 8
sections = []
while pos + 16 <= 64:
    tag = d[pos:pos + 4]
    if not re.match(rb"^[A-Z]{4}$", tag):
        break
    cnt, size, off = (int.from_bytes(d[pos + 4 + i:pos + 8 + i], "little") for i in (0, 4, 8))
    sections.append((tag.decode(), cnt, size, off))
    pos += 16
for s in sections:
    print("  段 %s count=%d size=%d off=%d" % s)

hg = [s for s in sections if s[0] == "HGHT"][0]
# 该段起点附近找 PNG 签名
start = hg[3]
i = d.find(b"\x89PNG", max(0, start - 64), start + 4096)
print("HGHT 段 off=%d，PNG 签名 @%d" % (start, i))
png = d[i:]
im = Image.open(io.BytesIO(png))
print("高度图:", im.size, im.mode)
a = np.asarray(im).astype(np.float32)
print("值域:", a.min(), a.max())

MIN_H, MAX_H = 0.0, 19.136
W, H = im.size
hw = a / 65535.0 * (MAX_H - MIN_H) + MIN_H if a.max() > 255 else a / 255.0 * (MAX_H - MIN_H) + MIN_H
print("高度(米) 值域: %.2f .. %.2f   海(<1.65)占比 %.1f%%" % (hw.min(), hw.max(), 100 * (hw < 1.65).mean()))


def height_at(x, y):
    px = int(round(x / 2048.0 * (W - 1)))
    py = int(round((1.0 - y / 1280.0) * (H - 1)))
    return hw[min(max(py, 0), H - 1), min(max(px, 0), W - 1)]


for nm, (x, y) in (("京(969,436)", (969, 436)), ("江户(1441,394)", (1441, 394)),
                   ("松前(1789,1031)", (1789, 1031)), ("博多(356,447)", (356, 447)),
                   ("鹿儿岛(265,213)", (265, 213)), ("富士山附近(1200,560)", (1200, 560)),
                   ("海(300,1100)", (300, 1100)), ("海(1900,200)", (1900, 200))):
    print("  %-20s 高度 %.2f m" % (nm, height_at(x, y)))
