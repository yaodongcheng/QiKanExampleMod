# make_head_textures.py —— 从角色源素材生成骑砍2 头部要的 5 张贴图（资源名即文件名）
#
# 为什么需要它：头部材质要的贴图不是源模型的原图，是【按引擎槽位重新合成】的：
#   · 脸  _d = 源 diffuse（缩到引擎尺寸）
#   · 脸  _n = 源 normal
#   · 脸  _s = 🔴【合成】R=金属度(G) / G=255−粗糙度(R) / B=AO(恒 255)
#              —— 与官方 pbr_metallic 的 Specular 定义一致（Native body_female_a 同款）
#   · 嘴  _mouth_d / 眼 _eye_d = 各自的 diffuse
# 这张表以前是手工做的，重跑不了；本脚本把它固化。
#
# 用法（系统 python，不需要 Blender）：
#   python make_head_textures.py --src <源素材目录> --out <输出目录> --name head_sephiroth_a \
#       --face-d Body.D.jpg --face-n Body.N.jpg --face-m Body.M.jpg --face-r Body.R.jpg \
#       --mouth Clive_Mouth_D.jpg --eye "Eye_D (1).jpg" [--face-size 4096] [--normal-size 2048] \
#       [--small-size 512] [--eye-size 1024]     # 嘴/眼可分别定尺寸（源里嘴与脸共用图集时必须分开给）
#
# 说明：
#   · --face-m / --face-r 是合成 _s 用的【输入】，不是输出槽位（引擎没有 _m/_r 槽）
#   · 源图尺寸与目标不一致时按 LANCZOS 缩放；长宽比差异会被拉到正方形（源贴图是 8201x8192
#     这类非 2 次幂，差 0.01% 量级，UV 偏移 <0.5px，可忽略）
#   · 只写 _d/_n/_s/mouth_d/eye_d 这 5 张——男头是 3 件（脸/眼/嘴），没有睫毛/眉毛/眼影件
#   · `--tint-level`（2026-09-21 加）：脸 _d 可选「洗底」——源模型的成品肤色再被引擎的肤色
#     乘子乘一遍 = 白档发黄、黑档变黑人（机制见 plans/rules/wheels.d/assets.md §17.3）。
#     ⚠️ 档位是**逐脸定**的，别全局一个数；0 = 不动（默认，行为不变）。
import argparse
import os
import sys

try:
    from PIL import Image, ImageOps
except ImportError:
    sys.exit("需要 Pillow：pip install pillow")
import numpy as np

# 洗底的公式只有一份实现（tint_face_texture.py）—— 这里 import 复用，别抄第二份
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import tint_face_texture as _tint


def tint(im, ref_path, level):
    """把脸 _d 朝参照（原版/xxFemale 那种「淡底图」）洗一档。"""
    A = np.asarray(im.convert("RGB")).astype(float)
    R = np.asarray(Image.open(ref_path).convert("RGB")).astype(float)
    cur, tgt = _tint.skin_median(A), _tint.skin_median(R)
    gain = (tgt / np.maximum(cur, 1e-6)) ** level
    s = 1.0
    for _ in range(6):                       # 高光滚降会吃掉一点，迭代补回（同 tint_face_texture）
        got = _tint.skin_median(_tint.apply_curve(A, gain, s))
        s *= float(np.mean((cur + (tgt - cur) * level) / np.maximum(got, 1e-6)))
    O = np.clip(_tint.apply_curve(A, gain, s), 0, 255)
    print("  洗底 level=%.2f：亮度 %.1f → %.1f（参照 %.1f）"
          % (level, cur.mean(), _tint.skin_median(O).mean(), tgt.mean()))
    return Image.fromarray(O.astype(np.uint8))


def load(src_dir, name):
    p = os.path.join(src_dir, name)
    if not os.path.exists(p):
        sys.exit("找不到源图：%s" % p)
    return Image.open(p)


def square(im, size, mode="RGB"):
    if im.size != (size, size):
        im = im.resize((size, size), Image.LANCZOS)
    return im.convert(mode)


def save(im, out_dir, name):
    p = os.path.join(out_dir, name)
    im.save(p)
    print("  %-34s %-6s %s  %s" % (name, im.mode, im.size, "%.1f MB" % (os.path.getsize(p) / 1048576.0)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", required=True, help="源素材目录")
    ap.add_argument("--out", required=True, help="输出目录（模块的 AssetSources\\<角色>）")
    ap.add_argument("--name", required=True, help="资源名前缀，如 head_sephiroth_a")
    ap.add_argument("--face-d", required=True)
    ap.add_argument("--face-n", required=True)
    ap.add_argument("--face-m", required=True, help="金属度图（合成 _s 的 R 通道）")
    ap.add_argument("--face-r", required=True, help="粗糙度图（合成 _s 的 G 通道，取反）")
    ap.add_argument("--mouth", required=True)
    ap.add_argument("--eye", required=True)
    ap.add_argument("--face-size", type=int, default=4096, help="脸 _d 边长（默认 4096）")
    ap.add_argument("--normal-size", type=int, default=2048, help="脸 _n 与 _s 边长（默认 2048）")
    ap.add_argument("--small-size", type=int, default=512, help="嘴/眼贴图边长（默认 512）")
    # 🔴 `--eye-size`（2026-09-19 加）：眼部单独定尺寸，默认 = --small-size（默认行为不变）。
    #    为什么需要：有些源模型（KCD 亨利）的**嘴件和脸共用同一张图集** —— 嘴的 UV 铺在整张
    #    脸图集上，贴图给 512 等于把 2048 的脸图缩到 1/4，牙齿直接糊掉；而眼球有自己的一张
    #    独立图（1024²），给 2048 只是白白放大。两者该给不同尺寸。
    ap.add_argument("--eye-size", type=int, default=None, help="眼贴图边长（默认 = --small-size）")
    # 🔴 洗底（2026-09-21 加）：脸 _d 的肤色底太"成品" → 引擎再乘一遍肤色 = 整体偏暗偏黄。
    #    档位 0~1（0=不动）。参照固定用 xxFemale 的 `head_female_x*_d.png`（= 能正常工作的淡底图）。
    ap.add_argument("--tint-level", type=float, default=0.0, help="脸 _d 洗底档位 0~1（默认 0=不动）")
    ap.add_argument("--tint-ref", default=None,
                    help="洗底参照贴图；--tint-level > 0 时必填（xxFemale 的 head_female_x*_d.png）")
    a = ap.parse_args()

    os.makedirs(a.out, exist_ok=True)
    print("源: %s\n目标: %s\n前缀: %s" % (a.src, a.out, a.name))

    # 1) 脸 diffuse（可选洗底；档位逐脸定，见文件头 --tint-level）
    face_d = square(load(a.src, a.face_d), a.face_size)
    if a.tint_level > 0:
        if not a.tint_ref:
            sys.exit("--tint-level > 0 时必须同时给 --tint-ref")
        face_d = tint(face_d, a.tint_ref, a.tint_level)
    save(face_d, a.out, "%s_d.png" % a.name)
    # 2) 脸 normal
    save(square(load(a.src, a.face_n), a.normal_size), a.out, "%s_n.png" % a.name)
    # 3) 脸 _s = R 金属度 / G 1−粗糙度 / B AO(255)
    m = square(load(a.src, a.face_m), a.normal_size, "L")
    r = square(load(a.src, a.face_r), a.normal_size, "L")
    s = Image.merge("RGB", (m, ImageOps.invert(r), Image.new("L", (a.normal_size, a.normal_size), 255)))
    save(s, a.out, "%s_s.png" % a.name)
    # 4) 嘴 / 5) 眼
    save(square(load(a.src, a.mouth), a.small_size), a.out, "%s_mouth_d.png" % a.name)
    save(square(load(a.src, a.eye), a.eye_size or a.small_size), a.out, "%s_eye_d.png" % a.name)

    print("完成。下一步：编辑器里按材质名接槽（脸 d/n/s、眼 _eye_d、嘴 _mouth_d），")
    print("        Publish 后跑 tools/face-pipeline/scripts/install_pack.py")
    return 0


if __name__ == "__main__":
    sys.exit(main())
