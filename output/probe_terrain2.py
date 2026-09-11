# -*- coding: utf-8 -*-
"""探针 v2：terrain.bin 里的 PNG 全扫出来按尺寸/模式认出高度图，验已知点。"""
import io
import numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS = None

BIN = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou\SceneObj\Main_map\terrain.bin"
d = open(BIN, "rb").read()

offs = []
i = d.find(b"\x89PNG")
while i != -1:
    offs.append(i)
    i = d.find(b"\x89PNG", i + 4)
print("PNG 数:", len(offs))
best = None
for k, o in enumerate(offs):
    end = offs[k + 1] if k + 1 < len(offs) else len(d)
    try:
        im = Image.open(io.BytesIO(d[o:end]))
        a = np.asarray(im)
        print("  #%d @%d  %s %s  值域 %s..%s" % (k, o, im.size, im.mode, a.min(), a.max()))
        if im.size == (4096, 2560) or im.size == (4097, 4097):
            best = (o, im, a)
    except Exception as e:
        print("  #%d @%d  解析失败 %s" % (k, o, e))

if best:
    o, im, a = best
    print("\n判定高度图 = @%d %s %s" % (o, im.size, im.mode))
    MIN_H, MAX_H = 0.0, 19.136
    f = a.astype(np.float32) / (65535.0 if a.dtype == np.uint16 else 255.0)
    hw = f * (MAX_H - MIN_H) + MIN_H
    H, W = hw.shape
    print("高度(米): %.2f .. %.2f   低于水面(1.65)占 %.1f%%" % (hw.min(), hw.max(), 100 * (hw < 1.65).mean()))

    def height_at(x, y):
        px = int(round(x / 2048.0 * (W - 1)))
        py = int(round((1.0 - y / 1280.0) * (H - 1)))
        return float(hw[min(max(py, 0), H - 1), min(max(px, 0), W - 1)])

    for nm, (x, y) in (("京(969,436)", (969, 436)), ("江户(1441,394)", (1441, 394)),
                       ("松前(1789,1031)", (1789, 1031)), ("博多(356,447)", (356, 447)),
                       ("鹿儿岛(265,213)", (265, 213)), ("富士附近(1200,560)", (1200, 560)),
                       ("远海(300,1100)", (300, 1100)), ("远海(1900,200)", (1900, 200))):
        print("  %-20s 高度 %.2f m" % (nm, height_at(x, y)))
