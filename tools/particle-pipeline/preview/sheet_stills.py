# -*- coding: utf-8 -*-
"""sheet_stills.py —— 一次渲染多个时刻，拼成一张 3×3 分镜图（自检闭环用）

为什么需要它：`render_still.py` 一次只出一张图，而"这个特效跟参考像不像"必须**连着看几帧**
（形态怎么长、怎么散、颜色怎么变）。一帧一读图太慢、也太费上下文；拼成一张 = 一次读图看 9 格。

跑法（示例：把 0.00→2.00s 每 0.25s 拼成一张，跟参考的 seq/B_11-13s.png 逐格对）：
    python preview/sheet_stills.py --xml out/yinmo_slash.xml --view examples/yinmo.view.json \
        --times 0,0.25,0.5,0.75,1.0,1.25,1.5,1.75,2.0 --out out/sheet_ours.png

产出：一张 PNG，每格左下角标注时刻。参考分镜在
      `D:\\BrainMaker\\骑砍2粒子特效复刻\\output\\yinmo_ref\\seq\\`（B_11-13s / C_13-15s 是阴魔斩的两段）。
"""
import argparse
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
RENDER = os.path.join(HERE, "render_still.py")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--xml", required=True)
    ap.add_argument("--view", default=None)
    ap.add_argument("--times", required=True, help="逗号分隔的时刻，如 0,0.25,0.5")
    ap.add_argument("--out", required=True, help="拼好的 PNG 路径")
    ap.add_argument("--cols", type=int, default=3)
    ap.add_argument("--cell", default="640x360", help="每格尺寸 WxH")
    args = ap.parse_args()

    from PIL import Image, ImageDraw

    cw, ch = (int(x) for x in args.cell.lower().split("x"))
    times = [float(x) for x in args.times.split(",") if x.strip() != ""]
    tmpdir = os.path.join(os.path.dirname(os.path.abspath(args.out)), "_sheet_tmp")
    os.makedirs(tmpdir, exist_ok=True)

    frames = []
    for t in times:
        one = os.path.join(tmpdir, "t%07.3f.png" % t)
        cmd = [sys.executable, RENDER, "--xml", args.xml, "--t", str(t), "--out", one]
        if args.view:
            cmd += ["--view", args.view]
        r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
        if r.returncode != 0 or not os.path.exists(one):
            print("[WARN] t=%.3f 渲染失败：%s" % (t, (r.stdout or "")[-300:] + (r.stderr or "")[-300:]))
            continue
        info = (r.stdout or "").strip().splitlines()
        frames.append((t, one, info[-1] if info else ""))
        print("  t=%.3f OK" % t)

    if not frames:
        print("[STOP] 一帧都没渲染出来")
        return 1

    rows = (len(frames) + args.cols - 1) // args.cols
    sheet = Image.new("RGB", (cw * args.cols, ch * rows), (18, 18, 22))
    d = ImageDraw.Draw(sheet)
    for i, (t, path, _info) in enumerate(frames):
        im = Image.open(path).convert("RGB").resize((cw, ch), Image.LANCZOS)
        r, c = divmod(i, args.cols)
        sheet.paste(im, (c * cw, r * ch))
        # 时刻标签（黑底黄字，跟参考分镜的标法一致）
        d.rectangle([c * cw, r * ch, c * cw + 92, r * ch + 22], fill=(0, 0, 0))
        d.text((c * cw + 6, r * ch + 6), "%.2fs" % t, fill=(255, 255, 0))
    sheet.save(args.out)
    print("拼好 -> %s  (%d 格 / %d 列)" % (args.out, len(frames), args.cols))
    return 0


if __name__ == "__main__":
    sys.exit(main())
