# -*- coding: utf-8 -*-
"""make_sw2_textures.py —— 战无2 角色源图集 → 骑砍2 头部要的 5 张贴图。

和 `face-pipeline/scripts/make_head_textures.py` 的区别（**战无2 不能用那个**）：
  · 战无2 是**一张全身图集**（512×1024），没有独立的 normal / 金属度 / 粗糙度图
  · 因此：脸/眼/嘴 三张 diffuse **都是这同一张图集**（脸/眼/嘴各取自己那块 UV 区域），
          `_n` / `_s` 用**纯色**（没有细节可给）
  · 这不是偷懒，是信长那版实测出来的既定配方（见 Knowledge/战国无双换装工程.md §7.10）：
    "三张 diffuse 逐像素完全相同（md5 一致）——正常：战无2 原模型就是一件模型一张图集"

纯色值直接照抄信长那版（已实机通过）：
    `_n` = (128, 128, 255)   完全平的法线
    `_s` = (0, 77, 255)      R=金属度0 / G=光泽度77(即粗糙度178) / B=AO 255

用法（系统 python，不需要 Blender）:
    python make_sw2_textures.py --atlas <源图集.png> --out <输出目录> --name head_yukimura_a
    python make_sw2_textures.py ... --long-edge 1024     # 想省体积时缩一半

产出 5 张（资源名即文件名，进 AssetSources）:
    <name>_d.png  <name>_eye_d.png  <name>_mouth_d.png   ← 图集
    <name>_n.png  <name>_s.png                            ← 纯色
全部经 `png_for_editor.py` 规范化（8bit RGB、无附加块）——Blender 直出的 PNG 会被编辑器清掉。
"""
import argparse
import os
import sys

try:
    from PIL import Image
except ImportError:
    sys.exit("需要 Pillow：pip install pillow")

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "face-pipeline", "scripts"))
try:
    import png_for_editor            # 复用它的格式规范化（含回读校验）
except Exception as e:
    sys.exit("找不到 png_for_editor.py（应在 tools/face-pipeline/scripts/）：%s" % e)

N_FLAT = (128, 128, 255)
S_FLAT = (0, 77, 255)


def save_rgb(arr, path):
    Image.fromarray(arr, "RGB").save(path, format="PNG", optimize=False)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--atlas", required=True, help="源图集 PNG（web/textures/<角色>.png）")
    ap.add_argument("--out", required=True, help="输出目录")
    ap.add_argument("--name", required=True, help="资源名前缀，如 head_yukimura_a")
    ap.add_argument("--long-edge", type=int, default=2048,
                    help="输出长边（默认 2048，与已实机通过的信长那版一致；源图是 512x1024）")
    ap.add_argument("--kind", default="head", choices=["head", "weapon"],
                    help="head = 5 张（脸/眼/嘴三 diffuse + _n/_s）；weapon = 3 张（_d + _n/_s）")
    ap.add_argument("--no-upscale", action="store_true",
                    help="只缩不放 —— 武器贴图很小（实测 128×32 ~ 256×512，多在 256×64），"
                         "放大到 2048 只是插值变糊 + 体积涨 20 倍（28 张 25.4MB vs 1MB），细节一点不增")
    a = ap.parse_args()

    if not os.path.isfile(a.atlas):
        sys.exit("FAIL: 找不到源图集 %s" % a.atlas)
    os.makedirs(a.out, exist_ok=True)

    im = Image.open(a.atlas).convert("RGB")
    w, h = im.size
    if w == 0 or h == 0:
        sys.exit("FAIL: 源图集尺寸非法 %s" % (im.size,))
    scale = a.long_edge / float(max(w, h))
    if a.no_upscale:
        scale = min(scale, 1.0)          # 只缩不放（武器贴图小，放大 = 白涨体积）
    nw, nh = max(1, int(round(w * scale))), max(1, int(round(h * scale)))
    if (nw, nh) != (w, h):
        im = im.resize((nw, nh), Image.LANCZOS)
    arr = __import__("numpy").asarray(im)

    # 三张 diffuse：同一张图集（战无2 的脸/眼/嘴本来就在同一张图上）
    # 武器只要一张（`_d`）——武器没有独立的眼/嘴 UV 区
    suffixes = ("_d",) if a.kind == "weapon" else ("_d", "_eye_d", "_mouth_d")
    tmp = []
    for suffix in suffixes:
        p = os.path.join(a.out, a.name + suffix + ".png")
        save_rgb(arr, p)
        tmp.append(p)
    # 两张纯色
    n_flat = __import__("numpy").tile(
        __import__("numpy").array(N_FLAT, dtype="uint8"), (nh, nw, 1))
    s_flat = __import__("numpy").tile(
        __import__("numpy").array(S_FLAT, dtype="uint8"), (nh, nw, 1))
    for suffix, flat in (("_n", n_flat), ("_s", s_flat)):
        p = os.path.join(a.out, a.name + suffix + ".png")
        save_rgb(flat, p)
        tmp.append(p)

    print("生成 %d 张贴图 @ %dx%d：%s" % (len(tmp), nw, nh, a.out))
    # 过一遍编辑器格式关（8bit RGB / 无附加块），就地重写
    for p in tmp:
        png_for_editor.convert(p, p)
    print("\n下一步：把这几张图连同头部 FBX 一起放进 AssetSources，进 ModKit 导入")
    return 0


if __name__ == "__main__":
    sys.exit(main())
