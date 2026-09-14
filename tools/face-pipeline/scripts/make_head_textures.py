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
#       --mouth Clive_Mouth_D.jpg --eye "Eye_D (1).jpg" [--face-size 4096] [--normal-size 2048]
#
# 说明：
#   · --face-m / --face-r 是合成 _s 用的【输入】，不是输出槽位（引擎没有 _m/_r 槽）
#   · 源图尺寸与目标不一致时按 LANCZOS 缩放；长宽比差异会被拉到正方形（源贴图是 8201x8192
#     这类非 2 次幂，差 0.01% 量级，UV 偏移 <0.5px，可忽略）
#   · 只写 _d/_n/_s/mouth_d/eye_d 这 5 张——男头是 3 件（脸/眼/嘴），没有睫毛/眉毛/眼影件
import argparse
import os
import sys

try:
    from PIL import Image, ImageOps
except ImportError:
    sys.exit("需要 Pillow：pip install pillow")


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
    a = ap.parse_args()

    os.makedirs(a.out, exist_ok=True)
    print("源: %s\n目标: %s\n前缀: %s" % (a.src, a.out, a.name))

    # 1) 脸 diffuse
    save(square(load(a.src, a.face_d), a.face_size), a.out, "%s_d.png" % a.name)
    # 2) 脸 normal
    save(square(load(a.src, a.face_n), a.normal_size), a.out, "%s_n.png" % a.name)
    # 3) 脸 _s = R 金属度 / G 1−粗糙度 / B AO(255)
    m = square(load(a.src, a.face_m), a.normal_size, "L")
    r = square(load(a.src, a.face_r), a.normal_size, "L")
    s = Image.merge("RGB", (m, ImageOps.invert(r), Image.new("L", (a.normal_size, a.normal_size), 255)))
    save(s, a.out, "%s_s.png" % a.name)
    # 4) 嘴 / 5) 眼
    save(square(load(a.src, a.mouth), a.small_size), a.out, "%s_mouth_d.png" % a.name)
    save(square(load(a.src, a.eye), a.small_size), a.out, "%s_eye_d.png" % a.name)

    print("完成。下一步：编辑器里按材质名接槽（脸 d/n/s、眼 _eye_d、嘴 _mouth_d），")
    print("        Publish 后跑 tools/face-pipeline/scripts/install_pack.py")
    return 0


if __name__ == "__main__":
    sys.exit(main())
