# split_rma.py —— KCD 的 `*_RMA.png` → 骑砍2 要的 `_s.png`（+ 拆出单独的金属度/粗糙度图）
#
# 为什么需要它
# ------------
# KCD 是 CryEngine 制式：**粗糙度 / 金属度 / AO 三通道打包成一张 RMA**。
# 骑砍2 的 `_s` 也是打包图，但**通道顺序不同**：
#
#     来源             R          G            B
#     KCD RMA          粗糙度      金属度        AO
#     骑砍 _s          金属度      255−粗糙度    AO
#
# 直接拿 RMA 当 `_s` 用 = 金属度和粗糙度对调 → 金属感全乱（这是本工程开工前点名的风险之一）。
#
# 🔴 通道顺序是**实测**出来的，不是照名字猜的（2026-09-19）：
#     · 皮肤（头 / 身体）  G = 0.000      —— 皮肤不导电 ⇒ G = 金属度
#     · 布（兜帽）         G = 0.002
#     · 钢板（胸甲）       G = 0.461
#     · 剑                 G = 0.763
#     · 锁子甲（铁）       G = 0.991
#     · B 各件均 ≈ 1.0，头的 0.749（头发/眼窝有遮蔽）⇒ B = AO
#     · R 抛光剑 0.36 < 粗糙皮肤 0.86 ⇒ R = 粗糙度
#
# 产出
# ----
#   <out>/<name>_metallic.png    灰图（= RMA 的 G 通道）—— 喂 make_head_textures.py --face-m
#   <out>/<name>_roughness.png   灰图（= RMA 的 R 通道）—— 喂 make_head_textures.py --face-r
#   <out>/<name>_s.png           骑砍 _s 打包图（R 金属 / G 255−粗糙 / B AO）
#
# 用法（系统 python，需要 Pillow）
#   python split_rma.py --rma <in_RMA.png> --out <目录> --name <裸名>
#   python split_rma.py --dir <maps 目录> --out <目录> --name sw  # 批量：把 *_RMA.png 全转
#
# ⚠️ 产出的 PNG **还要过一遍 png_for_editor.py** 才能进编辑器工程源（8bit RGB、无附加块）。
import argparse
import os
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

try:
    from PIL import Image
except ImportError:
    sys.exit("!! 需要 Pillow：pip install Pillow")

Image.MAX_IMAGE_PIXELS = None


def chan_to_gray(a, idx):
    """取一个通道，铺成 RGB 灰图（三通道同值）"""
    c = a[:, :, idx]
    return Image.merge("RGB", [Image.fromarray(c)] * 3)


def convert(rma_path, out_dir, name):
    im = Image.open(rma_path).convert("RGB")
    import numpy as np
    a = np.asarray(im)
    R, G, B = a[:, :, 0], a[:, :, 1], a[:, :, 2]

    os.makedirs(out_dir, exist_ok=True)
    p_m = os.path.join(out_dir, name + "_metallic.png")
    p_r = os.path.join(out_dir, name + "_roughness.png")
    p_s = os.path.join(out_dir, name + "_s.png")

    chan_to_gray(a, 1).save(p_m)
    chan_to_gray(a, 0).save(p_r)

    # 骑砍 _s：R = 金属度(=RMA.G) / G = 255−粗糙度(=255−RMA.R) / B = AO(=RMA.B)
    g_inv = (255 - R.astype(np.int16)).clip(0, 255).astype(np.uint8)
    s = np.dstack([G, g_inv, B])
    Image.fromarray(s).save(p_s)

    print("  %-44s -> %s_s.png (%dx%d)" % (os.path.basename(rma_path), name, im.width, im.height))
    return p_m, p_r, p_s


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rma", help="单张 RMA png")
    ap.add_argument("--dir", help="批量：这个目录下所有 *_RMA.png")
    ap.add_argument("--out", required=True, help="输出目录")
    ap.add_argument("--name", help="单张模式的裸名；批量模式省略 = 用文件名去掉 _RMA")
    a = ap.parse_args()
    if bool(a.rma) == bool(a.dir):
        sys.exit("!! --rma 和 --dir 必须给且只给一个")

    if a.rma:
        if not a.name:
            sys.exit("!! 单张模式必须给 --name")
        convert(a.rma, a.out, a.name)
        return
    n = 0
    for f in sorted(os.listdir(a.dir)):
        if not f.lower().endswith("_rma.png"):
            continue
        nm = a.name if a.name and n == 0 and len(os.listdir(a.dir)) == 1 else f[:-len("_RMA.png")]
        convert(os.path.join(a.dir, f), a.out, nm)
        n += 1
    print("[RMA] 共 %d 张" % n)


main()
