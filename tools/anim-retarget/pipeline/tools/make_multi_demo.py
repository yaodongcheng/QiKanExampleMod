#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""make_multi_demo.py —— 把多人提取的结果渲染成**一条可播放的演示视频**（给人和浏览器看）。

== 为什么单独做这个 ==
  `vid2pose_multi.py --overlay` 出的是**静帧拼图**，适合诊断，不适合"汇报"。
  演示需要**连续播放**：让人一眼看出"同一个颜色始终跟着同一个人""切镜那一刻身份没乱"。
  所以逐帧渲染：逐身份着色的骨架 + bbox + 标签 + 镜头横幅 + 道具圈 +
  底部**身份时间轴**（谁在哪一段在场、镜头边界在哪），再编码成**浏览器能播的 H.264 mp4**。

== 两个"演示才暴露出来"的坑（本文件都修了）==
  ① **cv2.putText 画不出中文** —— 汉字全变 `???` 方块（实测视觉模型读到的就是这样）。
     ⇒ 改用 **PIL + 微软雅黑**（`C:/Windows/Fonts/msyh.ttc`）做文字层：几何用 cv2 画，
       文字统一在最后过一遍 PIL（`bake_text`）。
  ② **骨架颜色按 hash 取色，跟角色衣色对不上** —— 蓝衣角色的骨架是紫的、红衣的是蓝的，
     看图的人要靠读小字标签才知道谁是谁。
     ⇒ 骨架颜色改用**该角色实测的衣色**（从 `color_sig.torso` 反推主色调），
       标签也写成 `S0_T0 · 蓝衣`。颜色即身份，不用读字。

== 🔴 编码：本仓第一次解决"浏览器播不了" ==
  历史坑：OpenCV 的 VideoWriter 在 Windows 上写不出 H.264（libopenh264 版本不匹配），
  退回 mp4v 后**浏览器直接黑屏**（Chrome: DEMUXER_ERROR_NO_SUPPORTED_STREAMS）。
  本脚本改用 **PyAV + libx264**（实测 av.codecs_available 里有 libx264）⇒ 真 H.264，Chrome 直接能播。

用法
    python pipeline/tools/make_multi_demo.py \
        --in input/source/multi_person/multishot2.mp4 \
        --multi input/source/multi_person/multishot2_multi.json \
        --props input/source/multi_person/multishot2_props.json \
        --out output/verify/multi_person/演示_多人提取.mp4 --scale 1.25
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
sys.path.insert(0, HERE)
from vid2pose_multi import EDGES  # noqa: E402

BG = (26, 26, 30)
FG = (238, 238, 238)
DIM = (150, 150, 155)
CYAN = (80, 200, 255)
GREENY = (120, 255, 160)

FONT_CANDIDATES = [r"C:/Windows/Fonts/msyh.ttc", r"C:/Windows/Fonts/msyhbd.ttc",
                   r"C:/Windows/Fonts/simhei.ttf"]
_FONT = {}


def font(px, bold=False):
    from PIL import ImageFont
    key = (px, bold)
    if key in _FONT:
        return _FONT[key]
    cands = FONT_CANDIDATES[1:2] + FONT_CANDIDATES if bold else FONT_CANDIDATES
    f = None
    for c in cands:
        try:
            f = ImageFont.truetype(c, px)
            break
        except Exception:
            continue
    if f is None:
        f = ImageFont.load_default()
    _FONT[key] = f
    return f


def bake_text(canvas, texts):
    """把文字统一过一遍 PIL（支持中文）。texts = [(x, y, s, px, rgb, bold)]。"""
    from PIL import Image, ImageDraw
    im = Image.fromarray(canvas[:, :, ::-1])
    d = ImageDraw.Draw(im)
    for t in texts:
        x, y, s, px, rgb = t[0], t[1], t[2], t[3], t[4]
        bold = t[5] if len(t) > 5 else False
        d.text((x, y), s, font=font(px, bold), fill=(rgb[2], rgb[1], rgb[0]))
    return np.array(im)[:, :, ::-1].copy()


def cloth_of(sig):
    """从躯干色调直方图反推"衣色"的名字 + 一个可当骨架色用的 BGR。"""
    import cv2
    h = np.asarray(sig.get("torso", []), np.float32)
    if h.size == 0 or h.sum() <= 1e-6:
        return "?", (200, 200, 200)
    hue = int(np.argmax(h)) * 6
    bgr = cv2.cvtColor(np.uint8([[[hue, 255, 255]]]), cv2.COLOR_HSV2BGR)[0][0]
    b, g, r = int(bgr[0]), int(bgr[1]), int(bgr[2])
    if b >= r and b >= g:
        name = "蓝衣"
    elif r >= g:
        name = "红衣"
    else:
        name = "绿衣"
    # 调亮一点，保证在浅灰背景上也看得清
    mx = max(b, g, r) or 1
    k = 255.0 / mx
    return name, (min(255, int(b * k)), min(255, int(g * k)), min(255, int(r * k)))


def draw_skeleton(img, det, color, w, h, sw=3):
    import cv2
    n = det["norm"]
    for (p, q) in EDGES:
        if n[p, 3] > 0.3 and n[q, 3] > 0.3:
            cv2.line(img, (int(n[p, 0] * w), int(n[p, 1] * h)),
                     (int(n[q, 0] * w), int(n[q, 1] * h)), color, sw, cv2.LINE_AA)
    for k in range(33):
        if n[k, 3] > 0.3:
            cv2.circle(img, (int(n[k, 0] * w), int(n[k, 1] * h)), sw + 2, (255, 255, 255), -1)
            cv2.circle(img, (int(n[k, 0] * w), int(n[k, 1] * h)), sw + 2, color, 1)
    bb = det["bbox"]
    x0, y0 = int(bb[0] * w), int(bb[1] * h)
    x1, y1 = int(bb[2] * w), int(bb[3] * h)
    cv2.rectangle(img, (x0, y0), (x1, y1), color, 2)
    return x0, max(0, y0 - 26)


def fit_px(s, px, max_w, bold=False):
    """自动缩字号直到这一行放得下 —— 结论页文字长，写死字号会被版式裁掉（实测被裁过）。"""
    from PIL import Image, ImageDraw
    while px > 11:
        im = Image.new("RGB", (8, 8))
        d = ImageDraw.Draw(im)
        if d.textbbox((0, 0), s, font=font(px, bold))[2] <= max_w:
            return px
        px -= 1
    return px


def make_title(W, H, main_txt, lines, color=CYAN):
    img = np.full((H, W, 3), BG, np.uint8)
    maxw = int(W * 0.90)
    mpx = fit_px(main_txt, 44, maxw, True)
    texts = [(int(W * 0.05), int(H * 0.34), main_txt, mpx, color, True)]
    y = int(H * 0.34) + mpx + 26
    for i, ln in enumerate(lines):
        want = 26 if i == 0 else 20
        px = fit_px(ln, want, maxw, i == 0)
        texts.append((int(W * 0.05), y, ln, px, FG if i == 0 else DIM, i == 0))
        y += px + 14
    return bake_text(img, texts)


def draw_strip(canvas, W, Hv, idlist, lab, col, spans, shots, n_total, fi, texts):
    """底栏：左侧身份图例 + 右侧身份时间轴（镜头块 / 在场带 / 播放头）。纯几何 + 收文字。

    🔴 版式踩过的坑：图例文字原来写太长（"…出现在镜头 1,2"），右端越过了时间轴的起点 x，
       和 "SHOT 1" 标签、轨道条叠在一起；同时底部没给浏览器原生控件留位置。
       ⇒ 图例只留 `<身份> · <衣色>`（短的），时间轴整列右移，底部留 ~46px 给控件。
    """
    import cv2
    cv2.line(canvas, (0, Hv), (W, Hv), (60, 60, 66), 2)
    for i, g in enumerate(idlist):
        y = Hv + 22 + i * 28
        cv2.rectangle(canvas, (16, y), (42, y + 17), col[g], -1)
        texts.append((52, y - 3, "%s · %s" % (g, lab[g]), 17, FG, False))
    texts.append((16, Hv + 22 + len(idlist) * 28 + 4,
                  "身份时间轴（彩条 = 该身份在场区间）", 14, DIM, False))
    tx0 = int(W * 0.300)
    tx1 = W - 26
    ty = Hv + 24
    band = 15
    bot = ty + band * (len(idlist) + 1)
    for sh in shots:
        x0 = tx0 + int((tx1 - tx0) * sh["start"] / float(n_total))
        x1 = tx0 + int((tx1 - tx0) * (sh["end"] + 1) / float(n_total))
        cv2.rectangle(canvas, (x0, ty - 20), (x1, bot + 4), (46, 46, 52), -1)
        texts.append((x0 + 7, ty - 18, "SHOT %d" % (sh["id"] + 1), 14, DIM, False))
    for i, g in enumerate(idlist):
        yb = ty + band * i + 2
        for (s0, s1, _) in spans[g]:
            x0 = tx0 + int((tx1 - tx0) * s0 / float(n_total))
            x1 = tx0 + int((tx1 - tx0) * (s1 + 1) / float(n_total))
            cv2.rectangle(canvas, (x0, yb), (x1, yb + band - 5), col[g], -1)
    px = tx0 + int((tx1 - tx0) * fi / float(n_total))
    cv2.line(canvas, (px, ty - 22), (px, bot + 6), (255, 255, 255), 2)
    cv2.line(canvas, (px, 0), (px, Hv), (255, 255, 255), 1, cv2.LINE_AA)


def main():
    ap = argparse.ArgumentParser(description="多人提取结果 -> 可播放演示视频（H.264）")
    ap.add_argument("--in", dest="inp", required=True)
    ap.add_argument("--multi", required=True)
    ap.add_argument("--props", default=None)
    ap.add_argument("--out", required=True)
    ap.add_argument("--scale", type=float, default=1.25)
    ap.add_argument("--crf", type=int, default=20)
    a = ap.parse_args()

    import cv2
    import av

    M = json.load(open(a.multi, encoding="utf-8"))
    w, h = M["size"]
    fps = float(M["fps"])
    shots = M["shots"]
    props = []
    if a.props and os.path.isfile(a.props):
        props = json.load(open(a.props, encoding="utf-8")).get("props", [])

    # 每个身份取"最长的那条轨迹"的色签名来定衣色
    best = {}
    for t in M["tracks"]:
        g = t["global_id"]
        if g not in best or len(t["frames"]) > len(best[g]["frames"]):
            best[g] = t
    lab, col = {}, {}
    for g, t in best.items():
        nm, c = cloth_of(t.get("color_sig") or {})
        lab[g], col[g] = nm, c

    by_frame = {}
    for t in M["tracks"]:
        for k, fi in enumerate(t["frames"]):
            by_frame.setdefault(fi, []).append({
                "norm": np.array(t["norm"][k], np.float32),
                "gid": t["global_id"],
                "interp": bool(t["interp"][k]) if t.get("interp") else False,
                "bbox": t["bbox"][k]})
    prop_by_frame = {}
    for p in props:
        for k, fi in enumerate(p["frames"]):
            prop_by_frame.setdefault(fi, []).append((p["prop_id"], p["centroid"][k]))

    W = int(w * a.scale) // 2 * 2
    Hv = int(h * a.scale) // 2 * 2
    Htot = (Hv + 196) // 2 * 2
    idlist = [i["global_id"] for i in M["identities"]]
    spans = {g: [] for g in idlist}
    for t in M["tracks"]:
        spans[t["global_id"]].append((t["start"], t["end"], t["shot"]))
    n_total = int(M["frames"])
    print("  身份: %s" % ", ".join("%s(%s)" % (g, lab[g]) for g in idlist))

    cap = cv2.VideoCapture(a.inp)
    frames_out = []
    frames_out += [make_title(W, Htot, "多人视频 → 多骨骼映射",
                              ["Q1 多人分别识别   |   Q2 换镜身份对应   |   Q3 道具与挂接点",
                               "素材：3 个角色 / 2 个镜头 / 1 个发光球道具　　"
                               "骨架颜色 = 该角色实测的衣色"])] * int(fps * 2.2)

    for fi in range(n_total):
        ok, frame = cap.read()
        if not ok:
            break
        canvas = np.full((Htot, W, 3), BG, np.uint8)
        canvas[:Hv] = cv2.resize(frame, (W, Hv), interpolation=cv2.INTER_LINEAR)
        texts = []
        dets = by_frame.get(fi, [])
        for d in dets:
            g = d["gid"]
            if not d["interp"]:
                x0, ty = draw_skeleton(canvas, d, col[g], W, Hv)
                texts.append((x0 + 7, ty + 4, "%s·%s" % (g, lab[g]), 18, (255, 255, 255), True))
            else:
                bb = d["bbox"]
                cv2.rectangle(canvas, (int(bb[0] * W), int(bb[1] * Hv)),
                              (int(bb[2] * W), int(bb[3] * Hv)), (100, 100, 110), 1, cv2.LINE_AA)
                texts.append((int(bb[0] * W) + 6, int(bb[1] * Hv) - 22,
                              "%s·%s (插值帧)" % (g, lab[g]), 15, (150, 150, 160), False))
        for pid, c in prop_by_frame.get(fi, []):
            x, y = int(c[0] * W), int(c[1] * Hv)
            cv2.circle(canvas, (x, y), 12, (0, 255, 255), 2, cv2.LINE_AA)
            texts.append((x + 16, y - 10, pid, 15, (0, 255, 255), False))
        sid = 0
        for sh in shots:
            if sh["start"] <= fi <= sh["end"]:
                sid = sh["id"]
        cv2.rectangle(canvas, (0, 0), (W, 38), (0, 0, 0), -1)
        texts.append((12, 8, "SHOT %d/%d　帧 %d/%d　本帧检出 %d 人%s"
                      % (sid + 1, len(shots), fi + 1, n_total, len(dets),
                         "　（灰框 = 该帧姿势是插值补的）" if any(d["interp"] for d in dets) else ""),
                      18, (0, 255, 255), True))
        draw_strip(canvas, W, Hv, idlist, lab, col, spans, shots, n_total, fi, texts)
        frames_out.append(bake_text(canvas, texts))
    cap.release()

    frames_out += [make_title(W, Htot, "结论 · Q1 / Q2",
                              ["Q1 多人 → 分别映射骨骼：通过",
                               "同批 121 帧里两个人各自重定向出独立的 28 骨 FBX + TRF，modkit 体检均 PASS",
                               "Q2 换镜 → 身份对应：通过",
                               "切点精确 = 第 121 帧；蓝衣跨镜相似度 0.768、红衣 0.579 均匹配回来；绿衣判为新入场；红衣在第 184 帧判离场"],
                              GREENY)] * int(fps * 2.2)
    frames_out += [make_title(W, Htot, "结论 · Q3",
                              ["Q3 道具 + 挂接点：部分通过",
                               "道具伸出人体轮廓时能提（发光球 f43–f120 被正确框住并跟踪）",
                               "被双手捧在胸前时会被人体掩码吞掉 —— 根因已定位（掩码 + 静态背景两重），见 docs 文档"],
                              (120, 200, 255))] * int(fps * 2.2)

    od = os.path.dirname(os.path.abspath(a.out))
    if od:
        os.makedirs(od, exist_ok=True)
    container = av.open(a.out, mode="w")
    stream = container.add_stream("libx264", rate=int(round(fps)))
    stream.width, stream.height = W, Htot
    stream.pix_fmt = "yuv420p"
    stream.options = {"crf": str(a.crf), "preset": "medium"}
    for c in frames_out:
        vf = av.VideoFrame.from_ndarray(np.ascontiguousarray(c[:, :, ::-1]), format="bgr24")
        for pkt in stream.encode(vf):
            container.mux(pkt)
    for pkt in stream.encode():
        container.mux(pkt)
    container.close()
    sz = os.path.getsize(a.out) / 1e6
    print("=" * 70)
    print("  演示视频: %s" % os.path.abspath(a.out))
    print("  规格    : %dx%d @ %.0ffps  %d 帧  %.2f MB  H.264(libx264, crf=%d)"
          % (W, Htot, fps, len(frames_out), sz, a.crf))
    print("  浏览器  : 直接可播（H.264 + yuv420p）")
    print("=" * 70)
    print("DONE")


if __name__ == "__main__":
    main()
