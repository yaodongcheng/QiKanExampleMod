#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""select_frames.py —— 把视频按帧区间**切成若干段再拼起来**（去废时间 / 改节奏）。

== 为什么要它 ==
  生成模型经常把动作做成"起手就位 → **冻住等一大段** → 突然做完"。
  实测本 case：4 秒的点穴素材里，**f15~f51 整整 1.5 秒画面完全冻住**（人物一动不动），
  末尾 f69~f96 又躺尸 1.1 秒 —— 真正的"戳→僵直"只占 f12~f15 三帧，塌倒只占 f51~f69。
  这种废时间不该靠"重新生成"去赌，而应该**在时间轴上直接切掉**。

  切之前必须先验证一件事：**切点两侧的姿态要接得上**。
  本 case 实测 f18 与 f44 的姿态几乎完全相同（受击者头 y 0.126 vs 0.128、髋 0.468 vs 0.469），
  所以中间那 1 秒剪掉是**无缝**的。切点不连续就会看到"跳帧"，那还不如不切。

用法
    # 保留 0-18 帧 + 44-96 帧，拼成一条（约 3 秒）
    python pipeline/tools/select_frames.py --in in.mp4 --out out.mp4 --segments "0-18,44-96"
    # 加 --check 只看每段的起止帧画面，不写盘
"""
import argparse
import os
import sys

import numpy as np

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass


def imwrite_utf8(p, img):
    import cv2
    ok, b = cv2.imencode(".png", img)
    if not ok:
        raise SystemExit("imencode failed")
    d = os.path.dirname(os.path.abspath(p))
    if d:
        os.makedirs(d, exist_ok=True)
    b.tofile(p)


def parse_segments(spec):
    segs = []
    for part in spec.split(","):
        part = part.strip()
        if not part:
            continue
        if "-" in part:
            a, b = part.split("-", 1)
            segs.append((int(a), int(b)))
        else:
            v = int(part)
            segs.append((v, v))
    return segs


def main():
    ap = argparse.ArgumentParser(description="按帧区间切视频并拼接（去废时间）")
    ap.add_argument("--in", dest="inp", required=True)
    ap.add_argument("--out", default=None)
    ap.add_argument("--segments", required=True, help='如 "0-18,44-96"')
    ap.add_argument("--fps", type=float, default=0.0, help="0=沿用源 fps")
    ap.add_argument("--check", default=None, help="只出每段首/末帧的对照图到该 PNG，不写视频")
    a = ap.parse_args()

    import cv2
    cap = cv2.VideoCapture(a.inp)
    if not cap.isOpened():
        raise SystemExit("读不到视频：%s" % a.inp)
    src_fps = cap.get(cv2.CAP_PROP_FPS) or 24.0
    w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    n = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    fps = a.fps or src_fps
    segs = parse_segments(a.segments)

    def frame_at(i):
        cap.set(cv2.CAP_PROP_POS_FRAMES, i)
        ok, f = cap.read()
        return f if ok else None

    print("=" * 70)
    print("  源      : %s  %dx%d @ %.2ffps  %d 帧 (%.2fs)"
          % (os.path.basename(a.inp), w, h, src_fps, n, n / max(src_fps, 1)))
    keep = []
    for (s, e) in segs:
        s = max(0, s); e = min(n - 1, e)
        keep += list(range(s, e + 1))
        print("  段      : f%d-%d  (%d 帧, %.2fs)" % (s, e, e - s + 1, (e - s + 1) / fps))
    print("  合计    : %d 帧  %.2fs" % (len(keep), len(keep) / fps))

    if a.check:
        tiles = []
        for (s, e) in segs:
            for lbl, i in (("段首 f%d" % s, s), ("段末 f%d" % e, e)):
                f = frame_at(i)
                if f is None:
                    continue
                t = cv2.resize(f, (420, 241))
                cv2.putText(t, lbl, (6, 22), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 255), 2)
                tiles.append(t)
        if tiles:
            W = sum(t.shape[1] for t in tiles)
            sheet = np.full((261, W, 3), 255, np.uint8)
            x = 0
            for t in tiles:
                sheet[10:10 + t.shape[0], x:x + t.shape[1]] = t
                x += t.shape[1]
            imwrite_utf8(a.check, sheet)
            print("  切点对照: %s（每段首/末帧并排，先看这里接不接得上）" % a.check)
        cap.release()
        print("=" * 70)
        return

    if not a.out:
        raise SystemExit("要么给 --out，要么给 --check")
    # 🔴 必须出 H.264：OpenCV 在 Windows 写不出（libopenh264 版本不匹配），只能退 mp4v，
    #    而浏览器只认 H.264/VP8/VP9/AV1 —— 切片后的 mp4v 直接黑屏。用 PyAV + libx264。
    import av
    frames = [frame_at(i) for i in keep]
    cap.release()
    frames = [f for f in frames if f is not None]
    container = av.open(a.out, mode="w")
    st = container.add_stream("libx264", rate=int(round(fps)))
    st.width, st.height = w, h
    st.pix_fmt = "yuv420p"
    st.options = {"crf": "18", "preset": "medium"}
    for f in frames:
        vf = av.VideoFrame.from_ndarray(np.ascontiguousarray(f[:, :, ::-1]), format="bgr24")
        for pkt in st.encode(vf):
            container.mux(pkt)
    for pkt in st.encode():
        container.mux(pkt)
    container.close()
    print("  输出    : %s" % os.path.abspath(a.out))
    print("=" * 70)
    print("DONE")


if __name__ == "__main__":
    main()
