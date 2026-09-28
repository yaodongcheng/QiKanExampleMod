#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""make_multi_compare.py —— **逐身份**的「源（裁到这个人）vs 他/她自己的解算动画」对照图。

== 为什么不能用现成的 make_compare_sheet.py ==
  那张图的上行是**整帧源视频**。多人场景下整帧里站着好几个人，
  上行根本分不清"下半这套骨架是在跟谁比" —— 实测拿它去问视觉模型，
  得到的回答是"两张图的上行一模一样，无法证实这套动画来自红衣服那个"。
  ⇒ 多人验证必须**把源裁到该身份自己的 bbox**，做到"一人一图、图里只有他"。

== 布局 ==
     上行 = 源视频第 t 帧，**裁到该身份 bbox**（外扩 25%），只留这一个人
     下行 = 该身份 TRF 烘出的 3D 动画，同一时间点、正面机位
  于是"上行那个人 = 下行这套骨架"是**唯一的**读法，不再有歧义。

用法
    python pipeline/tools/make_multi_compare.py \
        --multi input/source/multi_person/multishot2_multi.json \
        --src   input/source/multi_person/multishot2.mp4 \
        --clip  multishot2_multi__S0_T0_s0 --name mp_blue_shotA \
        --out   output/verify/multi_person/mp_blue_shotA_逐身份对照.png
"""
import argparse
import json
import os
import sys

import numpy as np

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))


def _root(p):
    for _ in range(6):
        if os.path.isdir(os.path.join(p, "pipeline")) and os.path.isdir(os.path.join(p, "input")):
            return p
        p = os.path.dirname(p)
    return os.path.dirname(p)


ROOT = _root(HERE)


def imwrite_utf8(p, img):
    import cv2
    ok, buf = cv2.imencode("." + p.rsplit(".", 1)[-1].lower(), img)
    if not ok:
        raise SystemExit("imencode failed")
    d = os.path.dirname(os.path.abspath(p))
    if d:
        os.makedirs(d, exist_ok=True)
    buf.tofile(p)


def main():
    ap = argparse.ArgumentParser(description="逐身份「源(裁到本人) vs 本人动画」对照图")
    ap.add_argument("--multi", required=True)
    ap.add_argument("--src", required=True)
    ap.add_argument("--clip", required=True, help="导出的 pose.json 名（不含 .json）")
    ap.add_argument("--name", required=True, help="TRF/FBX 名")
    ap.add_argument("--out", required=True)
    ap.add_argument("--animdir", default=None)
    ap.add_argument("--workdir", default=None, help="渲染帧目录（默认 output/verify/_cmp_work）")
    ap.add_argument("--frames", default="0,0.2,0.4,0.6,0.8,1.0")
    ap.add_argument("--size", default="240x320")
    a = ap.parse_args()

    import cv2
    from PIL import Image, ImageDraw

    animdir = a.animdir or os.path.join(ROOT, "input", "source", "multi_person")
    workdir = a.workdir or os.path.join(ROOT, "output", "verify", "_cmp_work")
    P = json.load(open(os.path.join(animdir, a.clip + ".json"), encoding="utf-8"))
    M = json.load(open(a.multi, encoding="utf-8"))
    gid = P["identity"]
    shot = P["shot"]
    w, h = M["size"]

    # 找到该身份在该镜的轨迹，用来取每帧 bbox
    tr = None
    for t in M["tracks"]:
        if t["global_id"] == gid and t["shot"] == shot:
            tr = t
            break
    if tr is None:
        sys.exit("multi.json 里找不到 %s@镜%d" % (gid, shot))

    fr = [float(x) for x in a.frames.split(",")]
    CW, CH = [int(x) for x in a.size.lower().split("x")]
    n = int(P["frames"])
    f0 = int((P.get("shot_frame_range") or [0])[0])

    cap = cv2.VideoCapture(a.src)
    src_tiles, anim_tiles = [], []
    for frac in fr:
        k = int(round((n - 1) * frac))
        k = max(0, min(len(tr["bbox"]) - 1, k))
        vf = f0 + k
        cap.set(cv2.CAP_PROP_POS_FRAMES, vf)
        ok, img = cap.read()
        if not ok:
            continue
        bb = tr["bbox"][k]
        px, py = 0.25, 0.25
        bw = bb[2] - bb[0]
        bh = bb[3] - bb[1]
        x0 = int(max(0, (bb[0] - px * bw) * w))
        y0 = int(max(0, (bb[1] - py * bh) * h))
        x1 = int(min(w, (bb[2] + px * bw) * w))
        y1 = int(min(h, (bb[3] + py * bh) * h))
        crop = img[y0:y1, x0:x1]
        if crop.size == 0:
            crop = img
        src_tiles.append(cv2.resize(crop, (CW, CH), interpolation=cv2.INTER_LINEAR))
        ap_ = os.path.join(workdir, "frames", "%s@%02d_f.png" % (a.clip, int(round(frac * 100))))
        if os.path.isfile(ap_):
            aimg = cv2.imdecode(np.fromfile(ap_, dtype=np.uint8), cv2.IMREAD_COLOR)
            anim_tiles.append(cv2.resize(aimg, (CW, CH), interpolation=cv2.INTER_AREA))
        else:
            anim_tiles.append(np.full((CH, CW, 3), 240, np.uint8))
    cap.release()

    m = min(len(src_tiles), len(anim_tiles))
    src_tiles, anim_tiles = src_tiles[:m], anim_tiles[:m]
    if m == 0:
        sys.exit("一帧都没取到")

    W = 20 + m * CW
    H = 118 + 2 * (CH + 20)
    im = Image.new("RGB", (W, H), (248, 248, 250))
    d = ImageDraw.Draw(im)
    d.text((12, 10), "逐身份对照 · %s（%s · 镜%d）" % (a.name, gid, shot), fill=(20, 20, 20))
    d.text((12, 30), "上行 = 源视频里【裁到这个人自己】的画面    下行 = 这个人自己的骑砍2 28 骨解算动画",
           fill=(110, 110, 110))
    d.text((12, 48), "读法唯一：上行那个人 = 下行这套骨架。多人场景必须这么比，整帧对比分不清是在跟谁比。",
           fill=(110, 110, 110))
    fc = P.get("facing", {})
    d.text((12, 66), "源朝向：中位 yaw %+.1f°｜逐帧最大跳变 %.1f°｜面部可见度 %.2f"
           % (fc.get("median_yaw_deg", 0), fc.get("max_frame_jump_deg", 0), fc.get("face_vis_mean", 0)),
           fill=(90, 90, 90))
    d.text((12, 84), "注意：目标骨架手臂是解剖学长度，MediaPipe 骨段被压缩 ⇒ 手伸出去时「伸得更远」，属固有比例差",
           fill=(170, 110, 60))
    y = 104
    d.text((12, y), "源（裁到本人）", fill=(60, 60, 60))
    y += 16
    for i, t in enumerate(src_tiles):
        im.paste(Image.fromarray(t[:, :, ::-1]), (20 + i * CW, y))
    y += CH + 20
    d.text((12, y - 16), "解算动画（同一时间点 · 正面机位）", fill=(60, 60, 60))
    for i, t in enumerate(anim_tiles):
        im.paste(Image.fromarray(t[:, :, ::-1]), (20 + i * CW, y))
    im.save(a.out)
    print("  逐身份对照图: %s（%s，%d 格）" % (os.path.abspath(a.out), a.name, m))
    print("DONE")


if __name__ == "__main__":
    main()
