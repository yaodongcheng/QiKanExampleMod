# make_armor_atlas.py —— 按图集方案把各片的源贴图拼成一张（配 build_armor.py --atlas-plan）
#
# 分工（两边必须读同一份 plan JSON，否则 UV 和贴图对不上）：
#   · `build_armor.py --atlas-plan`        → 把每片的 UV 缩进对应格子
#   · **本脚本**                            → 把每片的源贴图拼进同一张图集
#
# 🔴 v 轴方向：Blender 的 UV `v=0` 在**下边**，而图像的 row 0 在**上边**。
#    所以格子 `[col, row]`（row 从下往上数）落到像素上是
#    `y = H - (row+1)*cell`。搞反 = 上下颠倒（画面上看得出，但很容易顺手写错）。
#
# 产出（每件一套三张，过 `png_for_editor.py` 之后即可进编辑器工程源）：
#   <piece>_d.png   漫反射
#   <piece>_n.png   法线
#   <piece>_s.png   骑砍 _s 打包（R 金属 / G 255−粗糙 / B AO）
#
# 用法（系统 python，需要 Pillow）:
#   python make_armor_atlas.py --plan <atlas_plan.json> --piece kcd_henry_body_a \
#          --maps <KCD 源贴图目录> --out <输出目录>
#   python make_armor_atlas.py --plan ... --all --maps ... --out ...     # plan 里所有件
import argparse
import io
import json
import os
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

from PIL import Image
import numpy as np

Image.MAX_IMAGE_PIXELS = None

CH = {"d": "COLOR", "n": "NORM", "rma": "RMA"}


def load_scaled(path, cell):
    if not os.path.isfile(path):
        return None
    im = Image.open(path).convert("RGB")
    if im.size != (cell, cell):
        im = im.resize((cell, cell), Image.LANCZOS)
    return im


def build_piece(plan, piece, maps_dir, out_dir):
    spec = plan["pieces"][piece]
    cols, rows = spec["grid"]
    cell = int(spec["cell"])
    W, H = cols * cell, rows * cell
    print("\n== %s == 图集 %dx%d（%dx%d 格 · 每格 %d）" % (piece, W, H, cols, rows, cell))

    canvases = {k: Image.new("RGB", (W, H), (0, 0, 0)) for k in ("d", "n", "rma")}
    used = 0
    for mat, ent in spec["map"].items():
        cx, cy = ent["cell"]
        tex = ent["tex"]
        # 🔴 v 轴：格子 row 从下往上数 → 像素 y 要翻过来
        px, py = cx * cell, H - (cy + 1) * cell
        got = []
        if tex == "__black":
            # 空占位槽（源件 Empty_col.png 实测是纯黑）—— 不加载，画布本来就是黑的
            got.append("填黑")
        else:
            for key, suffix in CH.items():
                p = os.path.join(maps_dir, "%s_%s.png" % (tex, suffix))
                im = load_scaled(p, cell)
                if im is None:
                    got.append("%s缺" % suffix)
                    continue
                canvases[key].paste(im, (px, py))
        used += 1
        print("   格[%d,%d] %-46s <- %-30s %s" % (cx, cy, mat[:46], tex, " ".join(got) or "✓"))

    os.makedirs(out_dir, exist_ok=True)
    out = {}
    for key in ("d", "n"):
        p = os.path.join(out_dir, "%s_%s.png" % (piece, key))
        canvases[key].save(p)
        out[key] = p
    # _s = R 金属度 / G 255−粗糙度 / B AO（KCD 的 RMA 是 R 粗糙 / G 金属 / B AO，实测见 split_rma.py）
    a = np.asarray(canvases["rma"])
    R, G, B = a[:, :, 0], a[:, :, 1], a[:, :, 2]
    g_inv = (255 - R.astype(np.int16)).clip(0, 255).astype(np.uint8)
    p = os.path.join(out_dir, "%s_s.png" % piece)
    Image.fromarray(np.dstack([G, g_inv, B])).save(p)
    out["s"] = p
    for k, v in out.items():
        print("   %s -> %s" % (k, v))
    print("   用了 %d/%d 个格子" % (used, cols * rows))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--plan", required=True)
    ap.add_argument("--piece")
    ap.add_argument("--all", action="store_true")
    ap.add_argument("--maps", required=True, help="KCD 源贴图目录（*_COLOR/_NORM/_RMA.png）")
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    plan = json.load(io.open(a.plan, encoding="utf-8"))
    pieces = list(plan["pieces"]) if a.all else [a.piece]
    if not pieces or pieces == [None]:
        sys.exit("!! 给 --piece <名> 或 --all")
    for pc in pieces:
        if pc not in plan["pieces"]:
            sys.exit("!! plan 里没有 %r（有：%s）" % (pc, ", ".join(plan["pieces"])))
        build_piece(plan, pc, a.maps, a.out)
    print("\n[RMA] 完。**下一步过 png_for_editor.py** 再进编辑器工程源。")


main()
