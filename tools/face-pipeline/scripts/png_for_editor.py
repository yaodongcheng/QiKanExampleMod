# png_for_editor.py —— 把任意 PNG 转成【ModKit 编辑器工程能接受的格式】
#
# 🔴 为什么需要它（2026-09-14 实测踩坑）：
#   Blender 保存的 PNG 是 **RGBA + 带 sRGB/gAMA/cHRM/eXIf/pHYs 附加块**；
#   而工程里原有的源图（编辑器一直正常读取的那些）是 **8bit RGB、只有 IHDR/IDAT/IEND**。
#   实测把 Blender 输出的图直接丢进 `AssetSources/` 再 Import → **编辑器把这张源图和它的
#   编译产物一起清掉了**（`head_sephiroth_a_d.png` + `Assets/sephiroth/head_sephiroth_a_d_tex.tpac`
#   双双消失，整个模块/两个客户端都无残留）。
#   ⇒ 进工程源的贴图一律先过本脚本。
#
# 用法（系统 python，不需要 Blender）：
#   python png_for_editor.py <输入.png> [输出.png]      # 省略输出 = 原地覆盖
#
# 保证：
#   ① 8bit RGB（丢掉 alpha；alpha 非全 255 时会警告，因为那是"丢了内容"而非"丢了冗余"）
#   ② 不写 sRGB/gAMA/cHRM/eXIf/pHYs 等附加块（只留 IHDR/IDAT/IEND）
#   ③ RGB 三通道逐像素零改动（不做任何色彩空间换算）
import os
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

import numpy as np
from PIL import Image


def convert(src, dst):
    im = Image.open(src)
    a = np.asarray(im)
    if a.ndim == 3 and a.shape[2] == 4:
        alpha = a[:, :, 3]
        if alpha.min() != 255:
            print("[!] alpha 不是全 255（min=%d）—— 丢 alpha 会丢内容，请确认这是你要的"
                  % alpha.min())
        rgb = a[:, :, :3]
    elif a.ndim == 3:
        rgb = a[:, :, :3]
    else:
        raise SystemExit("FAIL: 灰度图不支持（本管线只有 RGB 贴图）")

    Image.fromarray(rgb, "RGB").save(dst, format="PNG", optimize=False)

    # 回读校验
    d = open(dst, "rb").read()
    assert d[:8] == b"\x89PNG\r\n\x1a\n", "输出不是 PNG"
    chunks, i = [], 8
    while i < len(d) - 8:
        ln = int.from_bytes(d[i:i + 4], "big")
        nm = d[i + 4:i + 8].decode("latin1")
        chunks.append(nm)
        i += 12 + ln
        if nm == "IEND":
            break
    extra = [c for c in chunks if c not in ("IHDR", "IDAT", "IEND")]
    back = np.asarray(Image.open(dst))
    same = np.array_equal(a[:, :, :3], back)

    print("IN  %s  %dx%d mode=%s" % (src, im.size[0], im.size[1], im.mode))
    print("OUT %s  %dx%d 位深=%d 颜色类型=%d 附加块=%s 大小=%d B"
          % (dst, back.shape[1], back.shape[0], d[24], d[25],
             extra if extra else "无", len(d)))
    print("颜色零损失校验：%s" % ("通过" if same else "❌ 有差异"))
    if extra:
        raise SystemExit("FAIL: 输出仍带附加块 %s —— 编辑器可能不接受" % extra)
    if not same:
        raise SystemExit("FAIL: 像素有改动，本脚本不该改颜色")


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__ or "用法：python png_for_editor.py <输入.png> [输出.png]")
    src = sys.argv[1]
    dst = sys.argv[2] if len(sys.argv) > 2 else src
    if not os.path.exists(src):
        raise SystemExit("FAIL: 找不到 %s" % src)
    convert(src, dst)


if __name__ == "__main__":
    main()
