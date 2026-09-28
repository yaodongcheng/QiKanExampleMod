#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""vid2pose_multi.py —— **多人**视频 → 每人各自的 3D 关键点序列 + 跨镜身份跟踪。

== 为什么需要它 ==
  `vid2pose.py` 走的是 `mp.solutions.pose`（单人），它内部**只取置信度最高的那一个人**，
  画面里第二个人直接消失。而 MediaPipe 官方支持多人的 `PoseLandmarker` 任务 API 需要一个
  `.task` 权重包 —— 本机 pypi / huggingface / raw.githubusercontent **全不通**，拉不到，
  所以不能靠"升级 API"解决。

  ⇒ 本脚本用**已有的单人体检模型**（pose_landmark_full.tflite + pose_detection.tflite）
     在**顶下（top-down）**框架里跑多人，等价于复现官方 num_poses>1 的内部做法：
       ① 在整帧上跑一次单人 BlazePose -> 得到一个人；
       ② 把这个人的身体区域**抹掉**（用周围背景色填充）；
       ③ 再跑一次 -> 得到下一个人；重复到检不出为止。
     每次调用都是 static_image_mode=True（独立检测，不会被上一帧的跟踪锁死）。

== 三个能力的落点（对应本次进阶研究的三个问题）==
  Q1 多人 -> 分别映射到不同骨骼
      · 多人检测（上面那套抹除迭代）
      · 逐帧多目标跟踪（IoU + 归一化质心距离 + 匈牙利匹配）-> 每人一条连续轨迹
      · 每人的 world 关键点各自独立（BlazePose 的 world 本来就是"该人髋中心"局部系）
        => 直接喂 pipeline/rigs/pose_mediapipe/retarget.py 就得到**各自独立的 28 骨动画**
      · --export 会为每个身份写出 input/source/pose_mediapipe/<clip>__<id>.json

  Q2 换镜 -> 前后镜头的人对应谁
      · 硬切检测（灰度帧间差 + HSV 直方图相关性）
      · 镜头切了就把所有轨迹"封存"，新镜头里重新建轨迹，再用**外观签名**跨镜重识别
      · 外观签名 = 躯干/腿部 ROI 的加权 HSV 直方图 + 体型比例（肩宽/髋宽、躯干/腿长）
      · 进/出场：某条轨迹中途消失 -> lost -> 超时未回 -> exited；新轨迹匹配不上任何老身份 -> 新身份

  Q3 道具 -> 见 prop_extract.py（本脚本负责把"人的位置"和"镜头边界"给出去）

== 用法 ==
    python pipeline/tools/vid2pose_multi.py --in input/source/multi_person/multishot2.mp4 \
        --out input/source/multi_person/multishot2_multi.json \
        --overlay output/verify/multi_person/multishot2_multi_overlay.png \
        --export --fps 24 --max-people 4
"""
import argparse
import json
import math
import os
import sys

import numpy as np

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

LM_NAMES = [
    "nose", "left_eye_inner", "left_eye", "left_eye_outer",
    "right_eye_inner", "right_eye", "right_eye_outer",
    "left_ear", "right_ear", "mouth_left", "mouth_right",
    "left_shoulder", "right_shoulder", "left_elbow", "right_elbow",
    "left_wrist", "right_wrist", "left_pinky", "right_pinky",
    "left_index", "right_index", "left_thumb", "right_thumb",
    "left_hip", "right_hip", "left_knee", "right_knee",
    "left_ankle", "right_ankle", "left_heel", "right_heel",
    "left_foot_index", "right_foot_index",
]
CORE = [11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28]
EDGES = [(11, 12), (11, 13), (13, 15), (12, 14), (14, 16),
         (11, 23), (12, 24), (23, 24), (23, 25), (25, 27),
         (24, 26), (26, 28), (27, 29), (29, 31), (28, 30), (30, 32),
         (11, 7), (12, 8), (7, 0), (8, 0)]
FACE_LM = [0, 2, 5, 9, 10, 7, 8]
ID_COLORS = [(60, 60, 240), (60, 200, 60), (240, 160, 40), (40, 220, 240),
             (200, 60, 200), (240, 240, 60), (140, 100, 255), (100, 255, 160)]


def imread_utf8(p):
    import cv2
    buf = np.fromfile(p, dtype=np.uint8)
    return cv2.imdecode(buf, cv2.IMREAD_COLOR)


def imwrite_utf8(p, img):
    import cv2
    ext = "." + p.rsplit(".", 1)[-1].lower()
    ok, buf = cv2.imencode(ext, img)
    if not ok:
        raise SystemExit("imencode 失败：%s" % p)
    buf.tofile(p)
    return True


def lm_px(norm, i, w, h):
    return int(round(norm[i, 0] * w)), int(round(norm[i, 1] * h))


def core_score(norm):
    return float(np.mean([norm[i, 3] for i in CORE]))


def person_bbox(norm, h, w, pad=0.06):
    vis = norm[:, 3] > 0.3
    if vis.sum() < 4:
        return None
    pts = norm[vis][:, :2]
    x0, y0 = pts.min(axis=0)
    x1, y1 = pts.max(axis=0)
    ph = max(1e-3, y1 - y0)
    x0 -= pad * ph; x1 += pad * ph; y0 -= pad * ph; y1 += pad * ph
    return [float(np.clip(x0, 0, 1)), float(np.clip(y0, 0, 1)),
            float(np.clip(x1, 0, 1)), float(np.clip(y1, 0, 1))]


def person_mask(norm, h, w, grow=1.0):
    import cv2
    m = np.zeros((h, w), np.uint8)
    vis = norm[:, 3] > 0.3
    ph = 0.35
    if vis.sum() > 4:
        pts = norm[vis][:, :2]
        ph = float(np.clip(pts[:, 1].max() - pts[:, 1].min(), 0.1, 1.0))
    th = max(4, int(grow * 0.10 * ph * h))
    for (a, b) in EDGES:
        if norm[a, 3] > 0.3 and norm[b, 3] > 0.3:
            cv2.line(m, lm_px(norm, a, w, h), lm_px(norm, b, w, h), 255, th)
    if min(norm[i, 3] for i in (11, 12, 23, 24)) > 0.3:
        poly = np.array([lm_px(norm, i, w, h) for i in (11, 12, 24, 23)], np.int32)
        cv2.fillConvexPoly(m, poly, 255)
    if norm[0, 3] > 0.3:
        cv2.circle(m, lm_px(norm, 0, w, h), max(6, int(0.045 * ph * h * grow)), 255, -1)
    return m


def _rect_minus_mask(frame, mask, bbox, h, w, pad=0.18):
    x0 = int(max(0, (bbox[0] - pad) * w)); x1 = int(min(w, (bbox[2] + pad) * w))
    y0 = int(max(0, (bbox[1] - pad) * h)); y1 = int(min(h, (bbox[3] + pad) * h))
    sub = frame[y0:y1, x0:x1]
    subm = mask[y0:y1, x0:x1]
    if sub.size == 0:
        return np.array([128, 128, 128], np.float32)
    outside = sub[subm == 0]
    if outside.size < 30:
        outside = frame.reshape(-1, 3)
    return np.median(outside.reshape(-1, 3), axis=0)
