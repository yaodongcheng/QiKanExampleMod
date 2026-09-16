# -*- coding: utf-8 -*-
"""make_sw2_textures.py —— 战无2 角色源图集 → 骑砍2 头部要的 5 张贴图。

和 `face-pipeline/scripts/make_head_textures.py` 的区别（**战无2 不能用那个**）：
  · 战无2 是**一张全身图集**（512×1024），没有独立的 normal / 金属度 / 粗糙度图
  · 因此：脸/眼/嘴 三张 diffuse **都是这同一张图集**（脸/眼/嘴各取自己那块 UV 区域）
  · 这不是偷懒，是信长那版实测出来的既定配方（见 Knowledge/战国无双换装工程.md §7.10）：
    "三张 diffuse 逐像素完全相同（md5 一致）——正常：战无2 原模型就是一件模型一张图集"

两条来源路线（`--src-dir` 有没有给，二选一）：

  A. **旧路线**（不给 `--src-dir`）：读原版图集，`_s` 写纯色。
     `_n` = (128, 128, 255)   完全平的法线
     `_s` = (0, 77, 255)      R=金属度0 / G=光泽度77(即粗糙度178) / B=AO 255
     纯色值直接照抄信长那版（已实机通过）。

  B. 🔴 **升级路线**（给了 `--src-dir`，2026-09-16 加）：读源工程超分后的
     `<键>_d.png`（超分 + 质感增强）与 `<键>_n.png`（真法线）。
     价值：旧路线 `_n` 是**纯色平法线**——脸上一点表面质感都没有；
     且旧 `_d` 实测清晰度 71.9，与"纯插值放大"**完全相同**（零真实细节）。
     升级图实测 2456。
     **本轮只给脸和武器用真法线**：
       · 脸：`_n` 原来就是纯色 → 换上真法线 = 从无到有
       · 武器：同上（武器也没有法线管线，原来也是纯色）
       · **甲不用**（甲走 `build_textures.py`，不经过本脚本）：甲有**自己的**法线管线，
         从漫反射提高频，会避开图集里**画上去的光影**（画师的阴影不是几何）；
         源工程的法线不区分这个。
     **`_s` 一律仍写纯色**：源工程的 `_mr` 是 R=AO/G=粗糙/B=金属，
     与骑砍的 R=金属/G=光泽/B=AO **通道顺序相反**，且它的金属度**刻意置 0**
     （见源工程 README §7.5.2）——那个 `_s` 我们不用，仍走旧路线的纯色值。

用法（系统 python，不需要 Blender）:
    # A. 旧路线
    python make_sw2_textures.py --atlas <源图集.png> --out <输出目录> --name head_yukimura_a
    python make_sw2_textures.py ... --long-edge 1024     # 想省体积时缩一半
    # B. 升级路线（脸）
    python make_sw2_textures.py --src-dir <tex_batch目录> --key L00_yukimura \
        --out <输出目录> --name head_yukimura_a
    # B. 升级路线（武器，--no-upscale 保留源图尺寸 = 比旧路线清晰 2 倍）
    python make_sw2_textures.py --src-dir <tex_batch目录> --key w_yukimura0 \
        --out <输出目录> --name taikou_yukimura_weapon_a --kind weapon --no-upscale

产出 5 张（资源名即文件名，进 AssetSources）:
    <name>_d.png  <name>_eye_d.png  <name>_mouth_d.png   ← 图集
    <name>_n.png  <name>_s.png                            ← 法线 + 高光
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


def resize_to(im, nw, nh):
    """按目标尺寸缩（相等则原样返回），LANCZOS。"""
    if (nw, nh) != im.size:
        im = im.resize((nw, nh), Image.LANCZOS)
    return im


def fit_target(im, long_edge, no_upscale):
    """把图按「长边 = long_edge，只缩不放（可选）」定到目标尺寸。"""
    w, h = im.size
    if w == 0 or h == 0:
        sys.exit("FAIL: 源图尺寸非法 %s" % (im.size,))
    scale = long_edge / float(max(w, h))
    if no_upscale:
        scale = min(scale, 1.0)          # 只缩不放（武器贴图小，放大 = 白涨体积）
    return resize_to(im, max(1, int(round(w * scale))), max(1, int(round(h * scale))))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--atlas", help="A 路线：源图集 PNG（web/textures/<角色>.png）")
    ap.add_argument("--src-dir", help="B 路线：升级贴图目录（work/tex_batch），"
                                      "里面是 <键>_d.png / <键>_n.png")
    ap.add_argument("--key", help="B 路线：源角色键（如 L00_yukimura / w_yukimura0）")
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

    use_upgrade = bool(a.src_dir)
    if use_upgrade:
        if not a.key:
            sys.exit("FAIL: 给了 --src-dir 就必须给 --key（源角色键）")
        d_path = os.path.join(a.src_dir, a.key + "_d.png")
        n_path = os.path.join(a.src_dir, a.key + "_n.png")
    else:
        if not a.atlas:
            sys.exit("FAIL: 没给 --atlas（A 路线）也没给 --src-dir（B 路线）")
        d_path, n_path = a.atlas, None

    if not os.path.isfile(d_path):
        sys.exit("FAIL: 找不到漫反射源图 %s" % d_path)
    os.makedirs(a.out, exist_ok=True)

    # ---- diffuse：定到目标尺寸 ----
    im = fit_target(Image.open(d_path).convert("RGB"), a.long_edge, a.no_upscale)
    nw, nh = im.size
    arr = __import__("numpy").asarray(im)

    tmp = []
    # 三张 diffuse：同一张图（战无2 的脸/眼/嘴本来就在同一张图上）
    # 武器只要一张（`_d`）——武器没有独立的眼/嘴 UV 区
    suffixes = ("_d",) if a.kind == "weapon" else ("_d", "_eye_d", "_mouth_d")
    for suffix in suffixes:
        p = os.path.join(a.out, a.name + suffix + ".png")
        save_rgb(arr, p)
        tmp.append(p)

    # ---- 法线 ----
    if use_upgrade and os.path.isfile(n_path):
        # B 路线：真法线。定到与 diffuse 同一尺寸（图上 UV 一致，尺寸必须逐一对应）
        n_im = resize_to(Image.open(n_path).convert("RGB"), nw, nh)
        p = os.path.join(a.out, a.name + "_n.png")
        n_im.save(p, format="PNG", optimize=False)
        tmp.append(p)
        src_note = "真法线"
    else:
        # A 路线（或 B 路线缺法线图）：纯色平法线
        n_flat = __import__("numpy").tile(
            __import__("numpy").array(N_FLAT, dtype="uint8"), (nh, nw, 1))
        p = os.path.join(a.out, a.name + "_n.png")
        save_rgb(n_flat, p)
        tmp.append(p)
        src_note = "平法线（%s）" % ("源缺 _n" if use_upgrade else "A 路线")

    # ---- 高光：两条路线都写纯色 ----
    # 源工程的 _mr 是 R=AO/G=粗糙/B=金属，与骑砍的 R=金属/G=光泽/B=AO 顺序相反，
    # 且金属度刻意置 0 → 不用它，仍走照抄信长那版的纯色值。
    s_flat = __import__("numpy").tile(
        __import__("numpy").array(S_FLAT, dtype="uint8"), (nh, nw, 1))
    p = os.path.join(a.out, a.name + "_s.png")
    save_rgb(s_flat, p)
    tmp.append(p)

    print("生成 %d 张贴图 @ %dx%d（漫反射=%s，%s）：%s" % (
        len(tmp), nw, nh, "升级图" if use_upgrade else "原版图集", src_note, a.out))
    # 过一遍编辑器格式关（8bit RGB / 无附加块），就地重写
    for p in tmp:
        png_for_editor.convert(p, p)
    print("\n下一步：把这几张图连同头部 FBX 一起放进 AssetSources，进 ModKit 导入")
    return 0


if __name__ == "__main__":
    sys.exit(main())
