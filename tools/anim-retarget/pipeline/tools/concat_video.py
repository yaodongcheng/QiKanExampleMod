#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""concat_video.py —— 把多段视频拼成一条，并**同时**产出两种编码。

══ 为什么必须产两份（2026-09-28 踩的坑）══════════════════════════════════
  OpenCV 的 VideoWriter 在 Windows 上**写不出 H.264**（libopenh264 缺失，日志会报
  "Failed to load OpenH264 library"）。所以 mp4 只能退回 `mp4v`（MPEG-4 Part 2）。
  而 **浏览器只认 H.264 / VP8 / VP9 / AV1** —— 实测 Chrome 打开 mp4v 直接报
  `DEMUXER_ERROR_NO_SUPPORTED_STREAMS`，画面全黑（用户当场发现）。

  ⇒ 本脚本一次产两个：
     · `<out>.mp4`   —— mp4v，**给下游工具读**（OpenCV 系：vid2pose / 抽帧 等，读得没问题）
     · `<out>.webm`  —— VP9，**给浏览器播**（放进 HTML / 查看器页面）
  两个文件内容一致，只是编码不同。**别再把 mp4v 的 mp4 塞进网页。**

用法
    python pipeline/tools/concat_video.py --in seg1.mp4 seg2.mp4 seg3.mp4 --out full.mp4 --fps 30
    # 只出 mp4（不需要网页播放时）：加 --no-webm
"""
import argparse
import os
import sys

import cv2
import numpy as np

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass


def fourcc_is(codec):
    return cv2.VideoWriter_fourcc(*codec)


def main():
    ap = argparse.ArgumentParser(description="多段视频拼接（同时产出浏览器可播的 webm）")
    ap.add_argument("--in", dest="ins", nargs="+", required=True, help="输入分段（按顺序）")
    ap.add_argument("--out", required=True, help="输出 mp4 路径（webm 同名 .webm）")
    ap.add_argument("--fps", type=float, default=30.0, help="输出帧率（源帧率不同会重采样）")
    ap.add_argument("--no-webm", action="store_true", help="不产 webm")
    ap.add_argument("--no-mp4", action="store_true", help="不产 mp4（只产 webm）")
    a = ap.parse_args()

    caps = []
    W = H = None
    for p in a.ins:
        if not os.path.isfile(p):
            sys.exit("找不到输入：%s" % p)
        c = cv2.VideoCapture(p)
        w = int(c.get(cv2.CAP_PROP_FRAME_WIDTH)); h = int(c.get(cv2.CAP_PROP_FRAME_HEIGHT))
        fps = c.get(cv2.CAP_PROP_FPS) or a.fps
        n = int(c.get(cv2.CAP_PROP_FRAME_COUNT))
        caps.append((p, c, w, h, fps, n))
        W = max(W or 0, w); H = max(H or 0, h)
        print("  %-28s %dx%d @%.1ffps  %d 帧" % (os.path.basename(p), w, h, fps, n))

    out_mp4 = os.path.abspath(a.out)
    out_webm = os.path.splitext(out_mp4)[0] + ".webm"
    os.makedirs(os.path.dirname(out_mp4), exist_ok=True)
    vw_m = None if a.no_mp4 else cv2.VideoWriter(out_mp4, fourcc_is("mp4v"), a.fps, (W, H))
    vw_w = None if a.no_webm else cv2.VideoWriter(out_webm, fourcc_is("VP90"), a.fps, (W, H))
    if not a.no_mp4 and not vw_m.isOpened():
        sys.exit("mp4 VideoWriter 打不开")
    if not a.no_webm and not vw_w.isOpened():
        print("  [warn] webm(VP9) VideoWriter 打不开，跳过 webm"); vw_w = None

    total = 0
    for p, c, w, h, fps, n in caps:
        rep = max(1, int(round(a.fps / max(fps, 1e-6))))
        while True:
            ok, fr = c.read()
            if not ok:
                break
            if (w, h) != (W, H):
                fr = cv2.resize(fr, (W, H))
            for _ in range(rep):
                if vw_m: vw_m.write(fr)
                if vw_w: vw_w.write(fr)
                total += 1
        c.release()
    for v in (vw_m, vw_w):
        if v:
            v.release()

    print("  拼接完成：%dx%d  %d 帧  %.2f 秒" % (W, H, total, total / a.fps))
    if not a.no_mp4:
        print("  下游用（mp4v，浏览器播不了）：%s  (%.1f MB)" % (out_mp4, os.path.getsize(out_mp4) / 1048576))
    if vw_w:
        print("  网页用（VP9，浏览器可播）：%s  (%.1f MB)" % (out_webm, os.path.getsize(out_webm) / 1048576))
    print("DONE")


if __name__ == "__main__":
    main()
