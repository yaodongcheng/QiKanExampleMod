# -*- coding: utf-8 -*-
"""sheet_stills.py —— 一次渲染多个时刻，拼成一张 3×3 分镜图（自检闭环用）

为什么需要它：`render_still.py` 一次只出一张图，而"这个特效跟参考像不像"必须**连着看几帧**
（形态怎么长、怎么散、颜色怎么变）。一帧一读图太慢、也太费上下文；拼成一张 = 一次读图看 9 格。

两种模式：
  ① 单个 XML × 多个时刻（看节奏：形态怎么长怎么散）
       python preview/sheet_stills.py --xml out/yinmo_slash.xml --view examples/yinmo.view.json \
           --times 0,0.25,0.5,0.75,1.0,1.25,1.5,1.75,2.0 --out out/sheet_ours.png
  ② 一批 XML × 每个一帧（看**这一批**做没做出来 / 像不像它该有的样子）
       python preview/sheet_stills.py --xmls "D:/.../output/xml/lwn_ns_*.xml" --t 1.2 \
           --out out/sheet_batch.png --per-sheet 9
     ⚠️ 一页 9 格时会按 `--per-sheet` 自动翻页出多张（`sheet_batch_01.png`、`_02`…）。

产出：PNG，每格左上角标注（单 XML 模式 = 时刻；批量模式 = **effect 名**）。
参考分镜在 `D:\\BrainMaker\\骑砍2粒子特效复刻\\output\\yinmo_ref\\seq\\`（B_11-13s / C_13-15s 是阴魔斩的两段）。
"""
import argparse
import glob
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
RENDER = os.path.join(HERE, "render_still.py")


def render_one(xml, t, out, view, extra=None):
    cmd = [sys.executable, RENDER, "--xml", xml, "--t", str(t), "--out", out]
    if view:
        cmd += ["--view", view]
    if extra:
        cmd += list(extra)
    r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    ok = r.returncode == 0 and os.path.exists(out)
    info = (r.stdout or "").strip().splitlines()
    return ok, (info[-1] if info else (r.stderr or "")[-200:])


def montage(cells, out, cols, cw, ch, tmpdir, tag_of):
    """cells = [(key, png_path)]；tag_of(key) = 左上角标签"""
    from PIL import Image, ImageDraw

    rows = (len(cells) + cols - 1) // cols
    sheet = Image.new("RGB", (cw * cols, ch * rows), (18, 18, 22))
    d = ImageDraw.Draw(sheet)
    for i, (key, path) in enumerate(cells):
        im = Image.open(path).convert("RGB").resize((cw, ch), Image.LANCZOS)
        r, c = divmod(i, cols)
        sheet.paste(im, (c * cw, r * ch))
        d.rectangle([c * cw, r * ch, c * cw + 150, r * ch + 22], fill=(0, 0, 0))
        d.text((c * cw + 6, r * ch + 6), tag_of(key)[:26], fill=(255, 255, 0))
    sheet.save(out)
    print("拼好 -> %s  (%d 格)" % (out, len(cells)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--xml", help="单个 XML（配 --times 用）")
    ap.add_argument("--xmls", help="一批 XML 的 glob（配 --t 用，每格一个 effect）")
    ap.add_argument("--view", default=None)
    ap.add_argument("--times", default="0,0.5,1.0", help="单 XML 模式的时刻表（逗号分隔）")
    ap.add_argument("--t", type=float, default=1.2, help="批量模式的渲染时刻")
    ap.add_argument("--out", required=True, help="拼好的 PNG 路径")
    ap.add_argument("--cols", type=int, default=3)
    ap.add_argument("--per-sheet", type=int, default=9, help="批量模式：一张分镜放几个（多余自动翻页）")
    ap.add_argument("--cell", default="640x360", help="每格尺寸 WxH")
    ap.add_argument("--tex-rgb", action="store_true", help="透传给 render_still：贴图自带颜色参与调色")
    args = ap.parse_args()

    cw, ch = (int(x) for x in args.cell.lower().split("x"))
    tmpdir = os.path.join(os.path.dirname(os.path.abspath(args.out)), "_sheet_tmp")
    os.makedirs(tmpdir, exist_ok=True)

    if args.xmls:
        # 允许逗号分隔多个 glob（挑几个特定 effect 看时用）
        files = sorted({f for pat in args.xmls.split(",") for f in glob.glob(pat.strip())})
        if not files:
            print("[STOP] glob 没匹配到文件：%s" % args.xmls)
            return 1
        print("批量：%d 个 XML，每个渲 t=%.2fs" % (len(files), args.t))
        cells = []
        for f in files:
            stem = os.path.splitext(os.path.basename(f))[0]
            one = os.path.join(tmpdir, "b_%s.png" % stem)
            ok, info = render_one(f, args.t, one, args.view, ["--tex-rgb"] if args.tex_rgb else None)
            if ok:
                cells.append((stem, one))
            else:
                print("  [WARN] %s 渲染失败：%s" % (stem, info))
        cols, per = args.cols, args.per_sheet
        base, ext = os.path.splitext(args.out)
        for i in range(0, len(cells), per):
            chunk = cells[i:i + per]
            out = args.out if len(cells) <= per else "%s_%02d%s" % (base, i // per + 1, ext)
            montage(chunk, out, cols, cw, ch, tmpdir, lambda k: k)
        return 0

    if not args.xml:
        print("[STOP] 要么 --xml（配 --times），要么 --xmls（配 --t）")
        return 1
    times = [float(x) for x in args.times.split(",") if x.strip() != ""]
    cells = []
    for t in times:
        one = os.path.join(tmpdir, "t%07.3f.png" % t)
        ok, info = render_one(args.xml, t, one, args.view, ["--tex-rgb"] if args.tex_rgb else None)
        if ok:
            cells.append((t, one))
            print("  t=%.3f OK" % t)
        else:
            print("  [WARN] t=%.3f 渲染失败：%s" % (t, info))
    if not cells:
        print("[STOP] 一帧都没渲染出来")
        return 1
    montage(cells, args.out, args.cols, cw, ch, tmpdir, lambda k: "%.2fs" % k)
    return 0


if __name__ == "__main__":
    sys.exit(main())
