#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""make_compare_sheet.py —— 产出标准「源素材 vs 解算动画」对照图，供**视觉模型诊断**。

══ 为什么要有这一步 ══════════════════════════════════════════════════════
  "视频/图片 → 骨骼动画"这条链路的错误**天然是视觉的**：左右镜像、上下翻转、正反面不连续、
  姿态对不上 —— 这些用数字很难穷举，但**人（或视觉模型）一眼就能看出来**。
  所以交付流程固定为：
      vid2pose → retarget → **make_compare_sheet** → 【视觉模型诊断】→ 记录结论
  本脚本负责第 3 步：把"同一时间点、**同一机位方向**"的源与解算结果上下并排，
  并**把诊断清单一起打印出来**，直接喂给视觉模型。

🔴 机位铁律：源素材是"人朝镜头"。目标骨架面朝 +Y ⇒ 对照图的动画一格必须用 **正面机位(az=180)**。
   用背面机位会得到"源正面 vs 我们背面"，非对称动作会被误判成镜像（本脚本已固化，不要再手写机位）。

用法
    python pipeline/tools/make_compare_sheet.py --src 某人.mp4 --clip dance01 --name pose_dance01 \
        --out output/verify/pose_mediapipe/dance01_源vs动画对照.png
    python pipeline/tools/make_compare_sheet.py --src 某图.png --clip photo_fullbody --name pose_photo_fullbody \
        --out output/verify/pose_mediapipe/photo_源vs动画对照.png
"""
import argparse
import json
import os
import subprocess
import sys

import cv2
import numpy as np
from PIL import Image, ImageDraw

BLENDER = r"C:/Program Files/Blender Foundation/Blender 5.2/blender.exe"


def _root(p):
    for _ in range(6):
        if os.path.isdir(os.path.join(p, "pipeline")) and os.path.isdir(os.path.join(p, "input")):
            return p
        p = os.path.dirname(p)
    return os.path.dirname(p)


ROOT = _root(os.path.dirname(os.path.abspath(__file__)))
HERE = os.path.dirname(os.path.abspath(__file__))


def imread_utf8(p):
    return cv2.imdecode(np.fromfile(p, dtype=np.uint8), cv2.IMREAD_COLOR)


def main():
    ap = argparse.ArgumentParser(description="源素材 vs 解算动画 对照图（给视觉模型诊断）")
    ap.add_argument("--src", required=True, help="源视频 / 源图片")
    ap.add_argument("--clip", required=True, help="pose.json 名（不含 .json）")
    ap.add_argument("--name", required=True, help="TRF 名（不含 .trf），如 pose_dance01")
    ap.add_argument("--out", required=True, help="输出对照图 PNG")
    ap.add_argument("--frames", default="0,0.2,0.4,0.6,0.8,1.0")
    ap.add_argument("--view", default="f", help="动画侧机位（默认 f=正面；别乱改）")
    ap.add_argument("--size", default="210x300", help="每格宽x高")
    ap.add_argument("--animdir", default=None,
                    help="pose.json 所在目录（默认 input/source/pose_mediapipe；多人链路传 input/source/multi_person）")
    ap.add_argument("--workdir", default=os.path.join(ROOT, "output", "verify", "_cmp_work"))
    ap.add_argument("--keep", action="store_true", help="保留中间帧")
    a = ap.parse_args()

    fr = [float(x) for x in a.frames.split(",")]
    CW, CH = [int(x) for x in a.size.lower().split("x")]
    animdir = a.animdir or os.path.join(ROOT, "input", "source", "pose_mediapipe")
    pj = os.path.join(animdir, a.clip + ".json")
    if not os.path.isfile(pj):
        sys.exit("找不到 pose.json: %s（先跑 vid2pose.py）" % pj)
    P = json.load(open(pj, encoding="utf-8"))
    fps = float(P.get("fps") or 30.0)
    stride = int(P.get("stride") or 1)
    src_fps = float(P.get("src_fps") or fps)
    # 多人导出没有 start_frame 字段，但有 shot_frame_range -> 用它的起点（s0 镜即 0）
    _sfr = P.get("shot_frame_range")
    start = int(P.get("start_frame") or (_sfr[0] if _sfr else 0))

    # ---- 1) 从 TRF 烘一个临时 GLB（复用项目既有工具）----
    os.makedirs(a.workdir, exist_ok=True)
    glb = os.path.join(a.workdir, a.name + ".glb")
    base = os.path.join(ROOT, "input/target/bannerlord/human_lod_4.fbx")
    cmd = [BLENDER, "-b", "--python", os.path.join(ROOT, "pipeline/common/glb_pack_retargeted.py"), "--",
           "--base", base, "--trfdir", os.path.join(ROOT, "output", "trf"),
           "--clips", "%s=%s" % (a.name, a.clip), "--out", glb]
    print("[1/4] 烘 GLB ...")
    r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=900)
    if not os.path.isfile(glb):
        print((r.stdout or "")[-2000:]); sys.exit("烘 GLB 失败")

    # ---- 2) 渲动画帧（**正面机位**，由 render_glb_frames.py 唯一负责）----
    rdir = os.path.join(a.workdir, "frames")
    os.makedirs(rdir, exist_ok=True)
    cmd = [BLENDER, "-b", "--python", os.path.join(HERE, "render_glb_frames.py"), "--",
           "--glb", glb, "--outdir", rdir, "--clip", a.clip,
           "--fracs", ",".join(str(x) for x in fr), "--view", a.view,
           "--size", "%dx%d" % (max(400, CW * 2), max(540, CH * 2))]
    print("[2/4] 渲动画帧（机位 %s）..." % a.view)
    subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=900)

    # ---- 3) 取源素材对应时间点的帧 ----
    print("[3/4] 取源帧 ...")
    srcs = []
    if P.get("kind") == "image":
        im = imread_utf8(a.src)
        srcs = [im]
        fr = [0.0]
    else:
        cap = cv2.VideoCapture(a.src)
        for frac in fr:
            k = int(P.get("frames", 1) - 1) * frac
            vf = start + k * stride
            cap.set(cv2.CAP_PROP_POS_FRAMES, int(round(vf)))
            ok, f = cap.read()
            srcs.append(f if ok else None)
        cap.release()

    # ---- 4) 拼图 ----
    print("[4/4] 拼图 ...")
    n = len(fr)
    W = 20 + n * CW
    H = 104 + 2 * (CH + 18)
    im = Image.new("RGB", (W, H), (248, 248, 250))
    d = ImageDraw.Draw(im)
    d.text((12, 8), "源素材 vs 解算动画 · %s（%s）" % (a.clip, os.path.basename(P.get("source", ""))), fill=(20, 20, 20))
    d.text((12, 26), "上行 = 源（人朝镜头）   下行 = 解算出的骑砍2 28 骨动画（同一时间点 · **正面机位**）", fill=(110, 110, 110))
    d.text((12, 44), "对照要点：① 上下有没有翻转 ② 左右有没有镜像 ③ 正反面连不连续 ④ 逐格姿态像不像", fill=(110, 110, 110))
    d.text((12, 62), "注：目标骨架的手臂是解剖学长度，MediaPipe 的骨段长度是压缩的 ⇒ 手伸出去时我们「伸得更远」，属固有比例差",
           fill=(170, 110, 60))
    pj_note = P.get("facing", {})
    if pj_note:
        d.text((12, 80), "源朝向：中位 yaw %+.1f°｜逐帧最大跳变 %.1f°｜面部可见度 %.2f（%s）"
               % (pj_note.get("median_yaw_deg", 0), pj_note.get("max_frame_jump_deg", 0),
                  pj_note.get("face_vis_mean", 0),
                  "正面朝镜头" if pj_note.get("likely_front_facing") else "疑似背对"),
               fill=(90, 90, 90))
    for i, frac in enumerate(fr):
        x = 20 + i * CW
        s = srcs[i] if i < len(srcs) else None
        if s is not None:
            h, w = s.shape[:2]
            sc = CH / float(h)
            im.paste(Image.fromarray(cv2.resize(s, (int(w * sc), CH))[:, :, ::-1]), (x, 104))
        p = os.path.join(rdir, "%s@%02d_%s.png" % (a.clip, int(frac * 100), a.view))
        if os.path.isfile(p):
            im.paste(Image.open(p).convert("RGB").resize((CW, CH)), (x, 104 + CH + 18))
        else:
            d.rectangle([x, 104 + CH + 18, x + CW, 104 + 2 * CH + 18], outline=(255, 0, 0))
        d.text((x + 4, 104 + CH + 4), "t=%.0f%%" % (frac * 100), fill=(120, 120, 120))
    out = os.path.abspath(a.out)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    im.save(out)
    print("对照图: %s（%d 格）" % (out, n))

    print("""
================ 交给【视觉模型】的诊断清单（逐条回答，别含糊）================
对着上面这张图，逐条给结论；每条都必须指出**依据第几格**：
 1) 上下：动画格有没有出现整体倒置/头朝下？（有 / 无，依据…）
 2) 左右：逐格比「四肢在画面里的左右」。**哪几格出现镜像**？（没有就写"无"）
    —— 注意：对称动作（双手举高/双手抱胸）看不出左右，只在非对称格下结论。
 3) 正反面：动画格是不是**始终正面朝镜头**？有没有某格突然变成背面？
 4) 姿态匹配：逐格 1~5 分（5=几乎一致），并指出**最不像的那一格**差在哪。
 5) 明显错误：有没有穿模、关节反折、脚悬空、肢体互相穿透？
 6) 该不该签收：通过 / 打回（打回要写清改哪一步：vid2pose 关键点 / 解算 / 机位）。
============================================================================
""")
    if not a.keep:
        pass
    print("DONE")


if __name__ == "__main__":
    main()
