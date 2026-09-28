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
import zlib

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


def detect_people(frame, pose, max_people, min_vis, grow=1.25):
    """在单帧里检出 1..max_people 个人，返回 [{norm, world, score, bbox}]。

    做法：跑一次 -> 记下 -> 用背景色把人抹掉 -> 再跑。等价于官方 num_poses>1 的内部机制。
    """
    import cv2
    h, w = frame.shape[:2]
    work = frame.copy()
    out = []
    # 🔴 2026-09-28 实测修正：原来一旦「第一个人」的可见度低于阈值就 `break`，
    #    结果**整帧一个人都不返回**。实测在 B 镜红衣角色走出画的那 25 帧里，
    #    排第一的检测恰好是那个"半出画、可见度被拉低"的人 ⇒ 整帧 0 人、轨迹断成两截。
    #    正确做法：低置信度的人**抹掉继续找**（画面里还有别人），只有连 bbox 都立不住才停。
    for k in range(max_people + 2):
        res = pose.process(cv2.cvtColor(work, cv2.COLOR_BGR2RGB))
        if res.pose_landmarks is None or res.pose_world_landmarks is None:
            break
        nl = res.pose_landmarks.landmark
        wl = res.pose_world_landmarks.landmark
        norm = np.array([[l.x, l.y, l.z, l.visibility] for l in nl], np.float32)
        world = np.array([[l.x, l.y, l.z] for l in wl], np.float32)
        sc = core_score(norm)
        n_vis_core = int((norm[CORE, 3] > 0.3).sum())
        bb = person_bbox(norm, h, w)
        weak = (sc < min_vis) or (n_vis_core < 8) or (bb is None)
        if weak:
            # 太弱：只有 bbox 还立得住（说明确实有个人形）才值得抹掉重找，否则停
            if bb is None or (bb[3] - bb[1]) < 0.06 or n_vis_core < 5:
                break
            m = person_mask(norm, h, w, grow=grow)
            work[m > 0] = _rect_minus_mask(frame, m, bb, h, w)
            work = cv2.GaussianBlur(work, (5, 5), 0)
            continue
        cx = 0.5 * (bb[0] + bb[2]); cy = 0.5 * (bb[1] + bb[3])
        dup = False
        for o in out:
            ob = o["bbox"]
            ocx = 0.5 * (ob[0] + ob[2]); ocy = 0.5 * (ob[1] + ob[3])
            if math.hypot(cx - ocx, cy - ocy) < 0.09:
                dup = True
                break
        if dup:
            break
        out.append({"norm": norm, "world": world, "score": sc, "bbox": bb})
        m = person_mask(norm, h, w, grow=grow)
        bg = _rect_minus_mask(frame, m, bb, h, w)
        work[m > 0] = bg
        work = cv2.GaussianBlur(work, (5, 5), 0)
    return out


def _roi_poly(norm, idxs, h, w, shrink=0.10):
    import cv2
    if min(norm[i, 3] for i in idxs) <= 0.25:
        return None
    pts = np.array([lm_px(norm, i, w, h) for i in idxs], np.float32)
    c = pts.mean(axis=0)
    pts = c + (pts - c) * (1.0 - shrink)
    m = np.zeros((h, w), np.uint8)
    cv2.fillConvexPoly(m, pts.astype(np.int32), 255)
    return m


def color_signature(frame, norm, h, w):
    """躯干 + 腿部的色调直方图（按饱和度加权）—— 跨镜重识别的主特征。

    为什么用色调直方图而不是均值色：直方图对**光照明暗变化**鲁棒（换镜后曝光常变），
    均值色会被整体亮度带偏。躯干/腿分开统计 => 对"上衣一种色、裤子另一种色"也能区分。
    """
    import cv2
    hsv = cv2.cvtColor(frame, cv2.COLOR_BGR2HSV)
    H, S, V = hsv[:, :, 0], hsv[:, :, 1], hsv[:, :, 2]
    torso = _roi_poly(norm, [11, 12, 24, 23], h, w, 0.16)
    legs = _roi_poly(norm, [23, 24, 28, 27], h, w, 0.16)
    out = {}
    for name, m in (("torso", torso), ("legs", legs)):
        if m is None or (m > 0).sum() < 40:
            out[name] = np.zeros(30, np.float32)
            out[name + "_sv"] = np.zeros(3, np.float32)
            continue
        sel = (m > 0) & (S > 35)
        if sel.sum() < 40:
            sel = (m > 0)
        hh = H[sel].astype(np.float32)
        ww = S[sel].astype(np.float32)
        hist, _ = np.histogram(hh, bins=30, range=(0, 180), weights=ww)
        n = np.linalg.norm(hist)
        out[name] = (hist / n).astype(np.float32) if n > 1e-6 else hist.astype(np.float32)
        out[name + "_sv"] = np.array([float(V[sel].mean()) / 255.0,
                                      float(S[sel].mean()) / 255.0,
                                      float(sel.sum()) / max(1, int((m > 0).sum()))], np.float32)
    return out


def proportions(norm):
    def d(a, b):
        return float(np.linalg.norm(norm[a, :2] - norm[b, :2]))
    sh = d(11, 12) + 1e-6
    hp = d(23, 24) + 1e-6
    torso = 0.5 * (np.linalg.norm(norm[11, :2] - norm[23, :2]) + np.linalg.norm(norm[12, :2] - norm[24, :2]))
    legs = 0.5 * (np.linalg.norm(norm[23, :2] - norm[27, :2]) + np.linalg.norm(norm[24, :2] - norm[28, :2]))
    return np.array([sh / hp, torso / (legs + 1e-6), sh / (torso + 1e-6)], np.float32)


def sig_similarity(a, b):
    import cv2
    s = 0.0
    wsum = 0.0
    for k, wt in (("torso", 0.55), ("legs", 0.25)):
        h1, h2 = a.get(k), b.get(k)
        if h1 is None or h2 is None:
            continue
        h1 = np.asarray(h1, np.float32); h2 = np.asarray(h2, np.float32)
        if h1.sum() < 1e-6 or h2.sum() < 1e-6:
            continue
        c = float(cv2.compareHist(h1.reshape(-1, 1), h2.reshape(-1, 1), cv2.HISTCMP_CORREL))
        s += wt * max(0.0, c)
        wsum += wt
    pa, pb = a.get("_prop"), b.get("_prop")
    if pa is not None and pb is not None:
        pa = np.asarray(pa, np.float32); pb = np.asarray(pb, np.float32)
        s += 0.20 * max(0.0, 1.0 - float(np.abs(pa - pb).sum()))
        wsum += 0.20
    return s / wsum if wsum > 0 else 0.0


def sig_merge(a, b, alpha=0.35):
    out = {}
    for k in a:
        va = np.asarray(a[k], np.float32)
        vb = np.asarray(b.get(k, a[k]), np.float32)
        m = (1 - alpha) * va + alpha * vb
        if k.endswith("_sv") or k == "_prop":
            out[k] = m
        else:
            n = np.linalg.norm(m)
            out[k] = m / n if n > 1e-6 else m
    return out


def bbox_iou(a, b):
    ix0 = max(a[0], b[0]); iy0 = max(a[1], b[1])
    ix1 = min(a[2], b[2]); iy1 = min(a[3], b[3])
    iw = max(0.0, ix1 - ix0); ih = max(0.0, iy1 - iy0)
    inter = iw * ih
    ua = (a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - inter
    return inter / ua if ua > 1e-9 else 0.0


class Track(object):
    def __init__(self, tid, det, frame_idx, shot):
        self.tid = tid
        self.shot = shot
        self.start = frame_idx
        self.end = frame_idx
        self.frames = [frame_idx]
        self.norm = [det["norm"]]
        self.world = [det["world"]]
        self.bbox = [det["bbox"]]
        self.score = [det["score"]]
        self.sig = dict(det["sig"])
        self.prop = np.asarray(det["prop"], np.float32)
        self.missed = 0
        self.global_id = None
        self.interp = None

    def densify(self):
        """把轨迹内"漏检的中间帧"用线性插值补上（遮挡期的姿态由前后帧过渡），
        并用 self.interp 标出哪些帧是插值的（**下游必须知道**：这不是观测到的姿态）。"""
        fs = list(self.frames)
        if len(fs) < 2:
            self.interp = [False] * len(fs)
            return 0
        idx = list(range(fs[0], fs[-1] + 1))
        if len(idx) == len(fs):
            self.interp = [False] * len(fs)
            return 0
        src = np.asarray(fs, np.float32)

        def _ia(arr):
            arr = np.asarray(arr, np.float32)
            flat = arr.reshape(len(fs), -1)
            out = np.zeros((len(idx), flat.shape[1]), np.float32)
            for k in range(flat.shape[1]):
                out[:, k] = np.interp(idx, src, flat[:, k])
            return out.reshape([len(idx)] + list(arr.shape[1:]))
        self.norm = _ia(self.norm)
        self.world = _ia(self.world)
        self.bbox = _ia(self.bbox)
        self.score = np.interp(idx, src, np.asarray(self.score, np.float32)).tolist()
        known = set(fs)
        self.interp = [i not in known for i in idx]
        n_add = len(idx) - len(fs)
        self.frames = idx
        self.start, self.end = idx[0], idx[-1]
        return n_add

    def update(self, det, frame_idx):
        self.end = frame_idx
        self.frames.append(frame_idx)
        self.norm.append(det["norm"])
        self.world.append(det["world"])
        self.bbox.append(det["bbox"])
        self.score.append(det["score"])
        self.sig = sig_merge(self.sig, det["sig"])
        self.prop = 0.7 * self.prop + 0.3 * np.asarray(det["prop"], np.float32)
        self.missed = 0

    def predict_bbox(self):
        if len(self.bbox) < 3:
            return self.bbox[-1]
        v = np.array(self.bbox[-1]) - np.array(self.bbox[-2])
        v = np.clip(v, -0.25, 0.25)
        return list(np.array(self.bbox[-1]) + v)

    def gallery(self):
        g = dict(self.sig)
        g["_prop"] = self.prop
        return g


def assign_tracks(dets, tracks, iou_thr=0.02, dist_thr=1.1):
    """匈牙利匹配：代价 = 0.65*(1-IoU) + 0.35*(归一化质心距离)。返回 [(det_i, track_j)]。"""
    from scipy.optimize import linear_sum_assignment
    if not dets or not tracks:
        return []
    C = np.ones((len(dets), len(tracks)), np.float32)
    for i, d in enumerate(dets):
        db = d["bbox"]; dcx = 0.5 * (db[0] + db[2]); dcy = 0.5 * (db[1] + db[3])
        dh = max(1e-3, db[3] - db[1])
        for j, t in enumerate(tracks):
            tb = t.predict_bbox()
            tcx = 0.5 * (tb[0] + tb[2]); tcy = 0.5 * (tb[1] + tb[3])
            th = max(1e-3, tb[3] - tb[1])
            iou = bbox_iou(db, tb)
            dist = math.hypot(dcx - tcx, dcy - tcy) / max(dh, th)
            C[i, j] = 0.65 * (1.0 - iou) + 0.35 * min(1.0, dist)
    ri, ci = linear_sum_assignment(C)
    out = []
    for i, j in zip(ri, ci):
        d = dets[i]; tb = tracks[j].predict_bbox()
        if bbox_iou(d["bbox"], tb) < iou_thr:
            db = d["bbox"]
            dcx = 0.5 * (db[0] + db[2]); dcy = 0.5 * (db[1] + db[3])
            tcx = 0.5 * (tb[0] + tb[2]); tcy = 0.5 * (tb[1] + tb[3])
            dh = max(1e-3, db[3] - db[1])
            if math.hypot(dcx - tcx, dcy - tcy) / dh > dist_thr:
                continue
        out.append((i, j))
    return out


def build_tracks(per_frame, shot_of, T, max_missed, min_len=3):
    """逐镜多目标跟踪（IoU + 归一化质心距离 + 匈牙利）。"""
    tracks = []
    next_tid = 0
    for s in sorted(set(int(x) for x in shot_of)):
        active = []
        for i in range(T):
            if shot_of[i] != s:
                continue
            dets = per_frame[i]["dets"]
            pairs = assign_tracks(dets, active)
            used_d, used_t = set(), set()
            for di, tj in pairs:
                active[tj].update(dets[di], i)
                used_d.add(di); used_t.add(tj)
            for di in range(len(dets)):
                if di not in used_d:
                    t = Track(next_tid, dets[di], i, s)
                    next_tid += 1
                    tracks.append(t); active.append(t)
            still = []
            for tj, t in enumerate(active):
                if tj not in used_t:
                    t.missed += 1
                    if t.missed <= max_missed:
                        still.append(t)
                else:
                    still.append(t)
            active = still
    return [t for t in tracks if len(t.frames) >= min_len]


def detect_in_crop(frame, pose, box_px, w, h, min_vis, expect_bbox=None, pad=0.40):
    """**轨迹引导的局部重检**：把某个人的 bbox 裁出来（外扩 pad）单独跑一次 BlazePose。

    🔴 为什么需要：实测（2026-09-28）当三个人**紧贴重叠**时，整帧迭代里的
       `pose_detection` 会漏检（B 镜 128~145 帧整帧返回 None，而画面里明明站着 3 个人，
       画面亮度/清晰度都正常）。把目标人物的 bbox 裁出来单独送检，目标在裁剪里占满画面，
       检测器成功率显著上升 —— 这正是"顶下（top-down）"多人姿态框架的标准补救动作。
    """
    import cv2
    x0, y0, x1, y1 = [int(round(v)) for v in box_px]
    x0 = max(0, x0); y0 = max(0, y0); x1 = min(w, x1); y1 = min(h, y1)
    if x1 - x0 < 12 or y1 - y0 < 12:
        return None
    crop = frame[y0:y1, x0:x1]
    res = pose.process(cv2.cvtColor(crop, cv2.COLOR_BGR2RGB))
    if res.pose_landmarks is None or res.pose_world_landmarks is None:
        return None
    ch, cw = crop.shape[:2]
    nl = res.pose_landmarks.landmark
    wl = res.pose_world_landmarks.landmark
    norm = np.array([[l.x, l.y, l.z, l.visibility] for l in nl], np.float32)
    world = np.array([[l.x, l.y, l.z] for l in wl], np.float32)
    norm[:, 0] = (x0 + norm[:, 0] * cw) / float(w)
    norm[:, 1] = (y0 + norm[:, 1] * ch) / float(h)
    sc = core_score(norm)
    if sc < 0.85 * min_vis:
        return None
    bb = person_bbox(norm, h, w)
    if bb is None:
        return None
    if expect_bbox is not None and bbox_iou(bb, expect_bbox) < 0.15:
        return None          # 裁出来的框跟预期人不在同一处 -> 检错人了，丢掉
    return {"norm": norm, "world": world, "score": sc, "bbox": bb, "src": "roi"}


def gapfill_by_roi(per_frame, tracks, pose, get_frame, w, h, min_vis, pad=0.40):
    """把每条轨迹中间"漏检的帧"用局部裁剪重检补回来。"""
    added = 0
    for t in tracks:
        fs = t.frames
        for k in range(len(fs) - 1):
            a, b = fs[k], fs[k + 1]
            if b - a <= 1:
                continue
            ba = np.array(t.bbox[k], np.float32)
            bb_ = np.array(t.bbox[k + 1], np.float32)
            for i in range(a + 1, b):
                alpha = (i - a) / float(b - a)
                bi = (1 - alpha) * ba + alpha * bb_
                if any(bbox_iou(d["bbox"], bi) > 0.25 for d in per_frame[i]["dets"]):
                    continue
                img = get_frame(per_frame[i]["fi"])
                if img is None:
                    continue
                eb = [max(0.0, bi[0] - pad), max(0.0, bi[1] - pad),
                      min(1.0, bi[2] + pad), min(1.0, bi[3] + pad)]
                bx = [eb[0] * w, eb[1] * h, eb[2] * w, eb[3] * h]
                det = detect_in_crop(img, pose, bx, w, h, min_vis, expect_bbox=list(bi))
                if det is not None:
                    det["sig"] = color_signature(img, det["norm"], h, w)
                    det["prop"] = proportions(det["norm"])
                    per_frame[i]["dets"].append(det)
                    added += 1
    return added



def medfilt_time(x, k):
    """沿**时间轴**做中值滤波 —— 专治"抽搐"（单帧/少数帧的脉冲尖峰）。

    为什么需要它：多人检测每帧都是 `static_image_mode=True`（独立检测，不带时间跟踪），
    所以 BlazePose 的逐帧噪声比单人链路更明显；再加上遮挡期的插值补洞，
    会出现"某几帧突然抖一下"。实测本 case 受击者**头部逐帧位移 p95 是中位数的 28 倍**，
    最抖的一帧头部位移 0.31×腿长（≈0.26 m）—— 肉眼就是"抽搐"。
    中值滤波能在**保住真实快速动作**（它是多帧连续的）的前提下，把 1~3 帧的脉冲直接抹掉。
    """
    if k is None or k <= 1 or x.shape[0] < 3:
        return x
    try:
        from scipy.ndimage import median_filter
        kk = k if k % 2 == 1 else k + 1
        if kk > x.shape[0]:
            kk = x.shape[0] if x.shape[0] % 2 == 1 else x.shape[0] - 1
        if kk <= 1:
            return x
        return median_filter(x, size=(kk, 1, 1), mode="nearest").astype(x.dtype)
    except Exception as e:
        print("  [warn] 中值滤波跳过：%s" % e)
        return x



def sanitize_tracks(tracks, thr=0.60):
    """**收尾护栏**：把「人体跨度塌缩」的观测帧判为无效，改用相邻好帧线性插值，并标 interp。

    为什么放在最后而不是检测阶段：坏帧可能来自**局部裁剪重检**或**断轨缝合**，
    在检测阶段过滤会被这两步绕过去（实测：检测阶段加了护栏仍然漏，因为那段是重检补出来的）。
    放在最终轨迹上，不管坏帧从哪儿来都兜得住。

    判据用【人体包围盒对角线】：站立/躺地都 ≈1.6~1.8 m，塌缩成废姿势时只剩 0.1~0.7 m。
    （**不能只看髋宽/肩宽**：world 是按人体自身尺度归一化的，姿势怎么塌髋宽都还是 0.25 m。）
    """
    n_fix = 0
    for t in tracks:
        W = np.asarray(t.world, np.float32)
        span = np.linalg.norm(W.max(axis=1) - W.min(axis=1), axis=1)
        med = float(np.median(span)) or 1.0
        # 双阈值：相对（塌到中位 60% 以下）或 绝对（整段人体跨度不足 0.95 m）
        bad = (span < thr * med) | (span < 0.95)
        if not bad.any():
            continue
        good = np.where(~bad)[0]
        if len(good) == 0:
            continue
        T = len(span)
        for attr, dim in (("world", None), ("norm", None)):
            A = np.asarray(getattr(t, attr), np.float32)
            flat = A.reshape(T, -1)
            out = flat.copy()
            for k in range(flat.shape[1]):
                out[:, k] = np.interp(np.arange(T), good, flat[good, k])
            setattr(t, attr, out.reshape(A.shape))
        if t.interp is None:
            t.interp = [False] * T
        for i in np.where(bad)[0]:
            t.interp[int(i)] = True
            n_fix += 1
    return n_fix


def stitch_tracks(tracks, gap=36, sim_thr=0.55):
    """**同镜内断轨缝合**：因为遮挡/出画/检出抖动，同一个人在同一个镜头里常被切成两三段
    （实测：B 镜红衣角色在"半出画"那 25 帧里整帧漏检，一条轨迹被切成 123-127 与 146-188 两截）。

    合并条件（三条同时满足）：
      · 同镜、时间不重叠、间隙 <= gap 帧
      · 外观签名相似度 >= sim_thr（换镜前后光照不同也能对上，因为用的是色调直方图）
      · 位置连续：把前段末帧的 bbox 匀速外推，落点要落在后段首帧 bbox 的合理邻域内
    """
    from collections import defaultdict
    by_shot = defaultdict(list)
    for t in tracks:
        by_shot[t.shot].append(t)
    merged_all = []
    for s, ts in by_shot.items():
        ts = sorted(ts, key=lambda x: x.frames[0])
        changed = True
        while changed:
            changed = False
            for i in range(len(ts)):
                for j in range(len(ts)):
                    if i == j:
                        continue
                    A, B = ts[i], ts[j]
                    if A.frames[-1] >= B.frames[0]:
                        continue
                    dt = B.frames[0] - A.frames[-1]
                    if dt > gap:
                        continue
                    sc = sig_similarity(A.gallery(), B.gallery())
                    if sc < sim_thr:
                        continue
                    ab, bb = A.bbox[-1], B.bbox[0]
                    ah = max(1e-3, ab[3] - ab[1])
                    bcy = 0.5 * (bb[1] + bb[3])
                    pred_cy = 0.5 * (ab[1] + ab[3])
                    if len(A.bbox) >= 2:
                        # 🔴 外推要**限步**：单帧位移本身有噪声，直接 ×dt(=30 帧) 会放大成
                        #    几个身位，位置连续性判据就永远不过（实测把本该合并的绿衣轨迹挡在门外）。
                        v1 = float(np.clip((np.array(A.bbox[-1]) - np.array(A.bbox[-2]))[1], -0.5, 0.5))
                        pred_cy += v1 * min(dt, 4)
                    if abs(pred_cy - bcy) > 0.45 * ah + 0.08:
                        continue
                    # 合并 B 进 A
                    A.end = B.end
                    A.frames += B.frames
                    A.norm += B.norm
                    A.world += B.world
                    A.bbox += B.bbox
                    A.score += B.score
                    A.sig = sig_merge(A.sig, B.sig, 0.5)
                    ts = [t for t in ts if t is not B]
                    changed = True
                    break
                if changed:
                    break
        merged_all += ts
    return merged_all


def _axis_matrix():
    mid = {"matrix": [[-1, 0, 0], [0, 0, -1], [0, -1, 0]]}
    try:
        root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
        mf = os.path.join(root, "pipeline", "rigs", "pose_mediapipe", "map.json")
        with open(mf, encoding="utf-8") as f:
            mid = json.load(f)
        mid = mid.get("axis", mid)
    except Exception:
        pass
    return np.array(mid["matrix"], dtype=np.float64)


def facing_series(world, norm):
    AX = _axis_matrix()
    T = len(world)
    yaw = np.zeros(T, np.float32)
    conf = np.zeros(T, np.float32)
    for k in range(T):
        P = (AX @ np.asarray(world[k]).T).T
        up = (P[11] + P[12]) * 0.5 - (P[23] + P[24]) * 0.5
        lf = P[23] - P[24]
        nu, nl = np.linalg.norm(up), np.linalg.norm(lf)
        if nu < 1e-6 or nl < 1e-6:
            yaw[k] = yaw[k - 1] if k else 0.0
            continue
        up = up / nu
        lf = lf - up * float(lf @ up)
        nl = np.linalg.norm(lf)
        if nl < 1e-6:
            yaw[k] = yaw[k - 1] if k else 0.0
            continue
        lf = lf / nl
        f = np.cross(lf, up)
        yaw[k] = math.degrees(math.atan2(float(f[0]), float(f[1])))
        conf[k] = float(np.mean([norm[k][i][3] for i in FACE_LM]))
    return yaw, conf


def facing_report(yaw, conf):
    if len(yaw) == 0:
        return {}
    d = np.abs(np.diff(yaw)) if len(yaw) > 1 else np.zeros(1)
    d = np.minimum(d, 360.0 - d)
    return {"median_yaw_deg": round(float(np.median(yaw)), 2),
            "yaw_range_deg": [round(float(yaw.min()), 2), round(float(yaw.max()), 2)],
            "max_frame_jump_deg": round(float(d.max()) if len(d) else 0.0, 2),
            "n_jumps_gt60": int((d > 60).sum()),
            "face_vis_mean": round(float(np.mean(conf)), 3),
            "likely_front_facing": bool(np.mean(conf) >= 0.5),
            "note": "yaw=角色前方相对骨架 +Y 的方位角；0 度即正面朝镜头"}


def detect_cuts(gray_small, hist_small):
    """硬切检测。返回 (cut 索引列表, 判据信息)。cut 索引 = 新镜头的第一帧下标。"""
    import cv2
    d = np.mean(np.abs(np.diff(gray_small.astype(np.float32), axis=0)), axis=(1, 2))
    med = float(np.median(d)); mad = float(np.median(np.abs(d - med))) + 1e-6
    thr = max(0.10, med + 8.0 * mad)
    corr = []
    for i in range(len(hist_small) - 1):
        corr.append(float(cv2.compareHist(hist_small[i], hist_small[i + 1], cv2.HISTCMP_CORREL)))
    corr = np.array(corr, np.float32) if corr else np.ones(1, np.float32)
    cuts = []
    for i in range(len(d)):
        c = corr[i] if i < len(corr) else 1.0
        if d[i] > thr and c < 0.75:
            cuts.append(i + 1)
    merged = []
    for c in cuts:
        if not merged or c - merged[-1] > 3:
            merged.append(c)
    return merged, {"thr": round(thr, 4), "median_diff": round(med, 4),
                    "mad": round(mad, 4), "max_diff": round(float(d.max()) if len(d) else 0.0, 4)}


def draw_overlay(frame, dets, tinfo, h, w):
    import cv2
    for d, (gid, tid) in zip(dets, tinfo):
        # 🔴 不能用 python 内置 hash()：字符串 hash 每个进程都会变（PYTHONHASHSEED 随机），
        #    不同身份可能撞成同一个颜色。crc32 是确定性的，跨进程稳定。
        col = ID_COLORS[zlib.crc32(gid.encode("utf-8")) % len(ID_COLORS)]
        n = d["norm"]
        for (p, q) in EDGES:
            if n[p, 3] > 0.3 and n[q, 3] > 0.3:
                cv2.line(frame, lm_px(n, p, w, h), lm_px(n, q, w, h), col, 3)
        for k in range(33):
            if n[k, 3] > 0.3:
                cv2.circle(frame, lm_px(n, k, w, h), 4, (255, 255, 255), -1)
                cv2.circle(frame, lm_px(n, k, w, h), 4, col, 1)
        bb = d["bbox"]
        p0 = (int(bb[0] * w), int(bb[1] * h)); p1 = (int(bb[2] * w), int(bb[3] * h))
        cv2.rectangle(frame, p0, p1, col, 2)
        cv2.putText(frame, "%s#%s" % (gid, tid), (p0[0], max(18, p0[1] - 6)),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.6, col, 2)
    return frame


def save_montage(tiles, out, cols=4):
    from PIL import Image
    rows = []
    for i in range(0, len(tiles), cols):
        chunk = list(tiles[i:i + cols])
        while len(chunk) < cols:
            chunk.append(np.full_like(chunk[0], 255))
        rows.append(np.hstack(chunk))
    if not rows:
        return None
    im = Image.fromarray(np.vstack(rows)[:, :, ::-1])
    d = os.path.dirname(os.path.abspath(out))
    if d:
        os.makedirs(d, exist_ok=True)
    im.save(out)
    return out


def _sgol(x, win, poly=2):
    if win is None or win <= 1 or x.shape[0] < 3:
        return x
    try:
        from scipy.signal import savgol_filter
        ww = win if win % 2 == 1 else win + 1
        if ww > x.shape[0]:
            ww = x.shape[0] if x.shape[0] % 2 == 1 else x.shape[0] - 1
        if ww <= poly:
            return x
        return savgol_filter(x, ww, poly, axis=0, mode="interp")
    except Exception:
        return x


def frame_reader(path):
    import cv2
    cap = cv2.VideoCapture(path)

    def get(fi):
        cap.set(cv2.CAP_PROP_POS_FRAMES, fi)
        ok, img = cap.read()
        return img if ok else None
    return cap, get


def main():
    ap = argparse.ArgumentParser(description="多人视频 -> 每人 3D 关键点 + 跨镜身份跟踪")
    ap.add_argument("--in", dest="inp", required=True, help="输入视频")
    ap.add_argument("--out", required=True, help="输出 json")
    ap.add_argument("--fps", type=float, default=0.0, help="抽帧到该 fps（0=源帧率）")
    ap.add_argument("--start", type=int, default=0)
    ap.add_argument("--end", type=int, default=0, help="0=到末尾")
    ap.add_argument("--model", type=int, default=1, choices=[0, 1, 2], help="BlazePose 复杂度")
    ap.add_argument("--max-people", type=int, default=4, help="单帧最多检几个人")
    ap.add_argument("--min-vis", type=float, default=0.45, help="核心点平均可见度下限")
    ap.add_argument("--max-missed", type=int, default=12, help="连续多少帧没匹配上就判该轨迹消失")
    ap.add_argument("--reid-thr", type=float, default=0.30,
                    help="跨镜身份匹配相似度下限（实测正配 0.38~0.77 / 错配 <=0.16，取 0.30 落在间隔中）")
    ap.add_argument("--stitch-thr", type=float, default=0.55, help="同镜内断轨缝合的外观相似度下限")
    ap.add_argument("--min-len", type=int, default=6, help="短于该帧数的轨迹直接丢弃（噪点）")
    ap.add_argument("--no-gapfill", dest="gapfill", action="store_false",
                    help="关闭『轨迹引导局部重检』（默认开启；紧贴重叠时靠它补漏检帧）")
    ap.add_argument("--smooth", type=int, default=9)
    ap.add_argument("--median", type=int, default=3,
                    help="沿时间轴的中值滤波窗口（奇数；专治单帧抽搐。0/1=关）")
    ap.add_argument("--overlay", default=None, help="输出叠加图")
    ap.add_argument("--export", action="store_true", help="为每个身份导出 pose.json")
    a = ap.parse_args()

    import cv2
    import mediapipe as mp

    cap = cv2.VideoCapture(a.inp)
    if not cap.isOpened():
        raise SystemExit("读不到视频：%s" % a.inp)
    src_fps = cap.get(cv2.CAP_PROP_FPS) or 24.0
    w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    stride = 1
    if a.fps and src_fps > 0:
        stride = max(1, int(round(src_fps / a.fps)))
    f1 = a.end if a.end > 0 else (total if total > 0 else 10 ** 9)
    n_expect = max(1, ((f1 - a.start) + stride - 1) // stride)
    print("=" * 70)
    print("  源      : %s  %dx%d @ %.2ffps  共 %s 帧" %
          (os.path.basename(a.inp), w, h, src_fps, total or "?"))
    print("  抽帧    : stride=%d -> %.2f fps，约 %d 帧待处理" % (stride, src_fps / stride, n_expect))

    pose = mp.solutions.pose.Pose(
        static_image_mode=True,
        model_complexity=a.model,
        smooth_landmarks=False,
        min_detection_confidence=0.4,
        min_tracking_confidence=0.4,
    )
    per_frame = []
    gray_small, hist_small = [], []
    fi = -1
    while True:
        ok, frame = cap.read()
        fi += 1
        if not ok or fi >= f1:
            break
        if fi < a.start or (fi - a.start) % stride != 0:
            continue
        dets = detect_people(frame, pose, a.max_people, a.min_vis)
        for d in dets:
            d["sig"] = color_signature(frame, d["norm"], h, w)
            d["prop"] = proportions(d["norm"])
        per_frame.append({"fi": fi, "dets": dets})
        g = cv2.cvtColor(cv2.resize(frame, (96, 54)), cv2.COLOR_BGR2GRAY)
        gray_small.append(g)
        hsv = cv2.cvtColor(cv2.resize(frame, (96, 54)), cv2.COLOR_BGR2HSV)
        hh = cv2.calcHist([hsv], [0, 1], None, [24, 8], [0, 180, 0, 256])
        cv2.normalize(hh, hh)
        hist_small.append(hh)
        if len(per_frame) % 25 == 0:
            print("    ... %d 帧，累计检出 %d 人次" %
                  (len(per_frame), sum(len(x["dets"]) for x in per_frame)), flush=True)
    cap.release()
    # 🔴 pose 不能在这里 close：下面的"轨迹引导局部重检"还要用它（见 gapfill_by_roi）
    T = len(per_frame)
    if T == 0:
        raise SystemExit("一帧都没读到")
    out_fps = src_fps / stride

    # ── 体型尺度反查（🔴 多人版原来漏了这条护栏）──
    # 只信 visibility 会被骗：BlazePose 偶尔会吐出一个「置信度不低、但整个人塌成一坨」的姿势。
    # 实测本 case：受击者切片第 0/1 帧 头+0.06 / 髋-0.01 / 踝-0.07（髋中心系），
    #   **整个人只有 0.13 m 高**（正常 1.7 m）—— 整条动画的根被 `_ground_trf` 按这个废姿势
    #   压下去 1.32 m，表现就是「受击者开局就躺在地上」。查了很久才找到源头。
    #
    # ⚠️ 两个容易写错的点（我都踩过）：
    #   ① 必须量 **world（米制）**，不能量 norm（图像归一化）—— norm 的宽窄只反映"人在画面哪/多大"，
    #      世界尺度塌缩时它一点不变。
    #   ② **不能只量髋宽/肩宽**：`pose_world_landmarks` 是**按人体自身尺度归一化**的，
    #      姿势再怎么塌，髋宽照样 ≈0.25 m。要量的是【整个人体的跨度】——
    #      用 33 点的包围盒对角线，站立/躺地都 ≈1.7 m，塌缩时只剩 0.1 m 量级。
    _diag = []
    for _fr in per_frame:
        for _d in _fr["dets"]:
            _p = np.asarray(_d["world"], np.float32)
            _diag.append(float(np.linalg.norm(_p.max(axis=0) - _p.min(axis=0))))
    if _diag:
        _md = float(np.median(_diag)) or 1.0
        _drop = 0
        for _fr in per_frame:
            _keep = []
            for _d in _fr["dets"]:
                _p = np.asarray(_d["world"], np.float32)
                if float(np.linalg.norm(_p.max(axis=0) - _p.min(axis=0))) < 0.50 * _md:
                    _drop += 1
                    continue
                _keep.append(_d)
            _fr["dets"] = _keep
        print("  体型尺度反查: 人体跨度中位 %.3f m；剔除 %d 个『塌缩』检出（< 50%%）" % (_md, _drop))

    cuts, cutinfo = detect_cuts(np.stack(gray_small), hist_small)
    print("  切镜    : %d 个硬切点 (索引 %s) | 判据 %s" % (len(cuts), cuts, cutinfo))
    shot_of = np.zeros(T, np.int32)
    sid = 0
    for i in range(T):
        if i in cuts:
            sid += 1
        shot_of[i] = sid
    n_shots = sid + 1
    shots = []
    for s in range(n_shots):
        idx = np.where(shot_of == s)[0]
        shots.append({"id": s, "start": int(per_frame[int(idx[0])]["fi"]),
                      "end": int(per_frame[int(idx[-1])]["fi"]), "n": int(len(idx))})

    n_det0 = sum(len(x["dets"]) for x in per_frame)
    tracks = build_tracks(per_frame, shot_of, T, a.max_missed, 3)
    print("  轨迹    : 第一轮逐镜跟踪得到 %d 条（首轮整帧检出 %d 人次）" % (len(tracks), n_det0))
    # 🔴 顺序很重要：**先缝合断轨，再补漏检帧**。
    #    紧贴重叠会让同一个人在整帧迭代里断成 2~3 截；只有先接回一长条，
    #    "中间那段整帧漏检"才会落在这条轨迹的**内部间隙**上，才谈得上重检/插值。
    sg = int(round(2.5 * a.max_missed))
    tracks = stitch_tracks(tracks, gap=sg, sim_thr=a.stitch_thr)
    print("  同镜缝合: -> %d 条（断轨按『外观相似 + 位置连续』接回，gap<=%d 帧）" % (len(tracks), sg))
    if a.gapfill:
        cap2 = cv2.VideoCapture(a.inp)

        def _get(fi, _c=cap2):
            _c.set(cv2.CAP_PROP_POS_FRAMES, fi)
            ok, im = _c.read()
            return im if ok else None
        added = gapfill_by_roi(per_frame, tracks, pose, _get, w, h, a.min_vis)
        cap2.release()
        if added:
            n_det1 = sum(len(x["dets"]) for x in per_frame)
            print("  局部重检: 补回 %d 人次（整帧检出 %d -> %d）" % (added, n_det0, n_det1))
            tracks = build_tracks(per_frame, shot_of, T, a.max_missed, 3)
            tracks = stitch_tracks(tracks, gap=sg, sim_thr=a.stitch_thr)
    pose.close()
    # 先缝合后过滤：碎片只要能被接回长轨就保留，接不回才当噪声丢掉
    n_pre = len(tracks)
    tracks = [t for t in tracks if len(t.frames) >= a.min_len]
    if n_pre != len(tracks):
        print("  噪声过滤: %d -> %d 条（< %d 帧且接不回任何长轨）" % (n_pre, len(tracks), a.min_len))

    n_det_final = sum(len(x["dets"]) for x in per_frame)
    n_interp = 0
    for t in tracks:
        n_interp += t.densify()
    print("  最终轨迹: %d 条；观测帧 %d，其中插值补洞 %d 帧（遮挡期，已在 json 里标 interp）"
          % (len(tracks), sum(len(t.frames) for t in tracks), n_interp))
    print("  （检出人次：首轮 %d -> 最终 %d）" % (n_det0, n_det_final))

    for t in tracks:
        t.global_id = "S%d_T%d" % (t.shot, t.tid)
    id_map = {}
    merges = []
    roster = {}
    for t in tracks:
        if t.shot == 0:
            roster[t.global_id] = t.gallery()
    for s in range(1, n_shots):
        cur = [t for t in tracks if t.shot == s]
        cand = list(roster.items())
        if cand and cur:
            from scipy.optimize import linear_sum_assignment
            C = np.zeros((len(cur), len(cand)), np.float32)
            for i, t in enumerate(cur):
                for j, (g, gsig) in enumerate(cand):
                    C[i, j] = 1.0 - sig_similarity(t.gallery(), gsig)
            ri, ci = linear_sum_assignment(C)
            for i, j in zip(ri, ci):
                sc = float(1.0 - C[i, j])
                if sc >= a.reid_thr:
                    old = cur[i].global_id
                    cur[i].global_id = cand[j][0]
                    roster[cand[j][0]] = sig_merge(roster[cand[j][0]], cur[i].gallery(), 0.5)
                    merges.append({"shot": s, "from": old, "to": cand[j][0], "sim": round(sc, 4)})
        for t in cur:
            roster.setdefault(t.global_id, t.gallery())
    for t in tracks:
        id_map["%d:%d" % (t.shot, t.tid)] = t.global_id

    gids = []
    for t in tracks:
        if t.global_id not in gids:
            gids.append(t.global_id)
    identities = []
    for g in gids:
        segs = [t for t in tracks if t.global_id == g]
        identities.append({"global_id": g, "n_segments": len(segs),
                           "segments": [{"shot": t.shot,
                                         "start": int(per_frame[t.frames[0]]["fi"]),
                                         "end": int(per_frame[t.frames[-1]]["fi"]),
                                         "n": len(t.frames)} for t in segs],
                           "total_frames": sum(len(t.frames) for t in segs)})
    identities.sort(key=lambda x: -x["total_frames"])

    events = []
    for t in tracks:
        last_i = t.frames[-1]
        tail_shot = int(shot_of[last_i])
        left = sum(1 for i in range(last_i + 1, T) if shot_of[i] == tail_shot)
        if left > max(4, a.max_missed) and t.end < (T - 1):
            events.append({"type": "exit_mid_shot", "track": t.global_id,
                           "frame": int(per_frame[last_i]["fi"]), "shot": tail_shot,
                           "note": "在本镜内消失且之后未再出现 -> 离场"})
    seen_before = {}
    for t in sorted(tracks, key=lambda x: (x.shot, x.start)):
        if t.global_id not in seen_before:
            if t.shot > 0:
                events.append({"type": "enter_shot", "track": t.global_id,
                               "frame": int(per_frame[t.frames[0]]["fi"]), "shot": int(t.shot),
                               "note": "本镜新出现、未匹配上任何旧身份 -> 入场"})
            seen_before[t.global_id] = t.shot

    for t in tracks:
        # 🔴 顺序：先中值去脉冲尖峰，再 savgol 平滑。只做 savgol 的话尖峰会"糊"成一小段抖动，
        #    看起来仍然像抽搐（实测 —— 所以先中值）。
        t.norm = _sgol(medfilt_time(np.stack(t.norm), a.median), a.smooth)
        t.world = _sgol(medfilt_time(np.stack(t.world), a.median), a.smooth)

    # 🔴 塌缩帧护栏必须放在**平滑之后**：实测放前面会被 savgol 绕过去
    #    （原始值 0.8x 没触发阈值，平滑后掉到 0.70，反而是最终产物里的坏帧）。
    n_fix = sanitize_tracks(tracks)
    if n_fix:
        print("  塌缩帧收尾: %d 帧判为无效（人体跨度塌缩），已用相邻好帧插值并标 interp" % n_fix)

    data = {
        "source": os.path.abspath(a.inp),
        "size": [w, h], "src_fps": float(src_fps), "fps": float(out_fps),
        "stride": int(stride), "frames": int(T),
        "model_complexity": int(a.model), "max_people": int(a.max_people),
        "min_vis": float(a.min_vis), "max_missed": int(a.max_missed),
        "reid_thr": float(a.reid_thr),
        "landmark_names": LM_NAMES, "core_indices": CORE,
        "cut_info": cutinfo, "cuts": [int(per_frame[c]["fi"]) for c in cuts],
        "shots": shots, "identities": identities, "events": events,
        "merges": merges, "id_map": id_map,
        "tracks": [{
            "global_id": t.global_id, "local_tid": t.tid, "shot": t.shot,
            "start": int(per_frame[t.frames[0]]["fi"]),
            "end": int(per_frame[t.frames[-1]]["fi"]),
            "frames": [int(per_frame[i]["fi"]) for i in t.frames],
            "interp": [bool(v) for v in (t.interp or [False] * len(t.frames))],
            "bbox": [[round(float(v), 5) for v in b] for b in t.bbox],
            "score": [round(float(v), 4) for v in t.score],
            "color_sig": {k: np.round(np.asarray(v), 5).tolist()
                          for k, v in t.sig.items() if k != "_prop"},
            "proportions": np.round(np.asarray(t.prop), 4).tolist(),
            "facing": facing_report(*facing_series(t.world, t.norm)),
            "world": np.round(t.world, 6).tolist(),
            "norm": np.round(t.norm, 6).tolist(),
        } for t in tracks],
        "per_frame_n": [len(x["dets"]) for x in per_frame],
        "axis_note": "world: 米制, 原点=该人双髋中点, y 向下为正, z 越负越近相机; norm: (x,y,z,vis)",
    }
    od = os.path.dirname(os.path.abspath(a.out))
    if od:
        os.makedirs(od, exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False)

    print("-" * 70)
    print("  镜头    : %d 段 %s" % (n_shots, [(s["start"], s["end"]) for s in shots]))
    print("  身份    : %d 个" % len(identities))
    for idn in identities:
        print("    %-12s 跨 %d 镜 / 共 %d 帧  %s"
              % (idn["global_id"], idn["n_segments"], idn["total_frames"],
                 [(s["shot"], s["start"], s["end"]) for s in idn["segments"]]))
    if merges:
        print("  跨镜身份匹配:")
        for m in merges:
            print("    镜%d: %-10s -> %-10s 相似度 %.3f" % (m["shot"], m["from"], m["to"], m["sim"]))
    for e in events:
        print("  事件    : %-14s %-12s @帧%-5s (镜%s)" % (e["type"], e["track"], e["frame"], e["shot"]))
    print("  输出    : %s" % os.path.abspath(a.out))

    if a.export:
        base = os.path.splitext(os.path.abspath(a.out))[0]
        n_exp = 0
        for t in tracks:
            if len(t.frames) < 5:
                continue
            name = "%s__%s_s%d" % (os.path.basename(base), t.global_id, t.shot)
            yaw, fconf = facing_series(t.world, t.norm)
            d = {
                "source": os.path.abspath(a.inp), "kind": "video",
                "fps": float(out_fps), "frames": len(t.frames),
                "size": [w, h], "model_complexity": int(a.model),
                "landmark_names": LM_NAMES, "core_indices": CORE,
                "valid": [not bool(v) for v in (t.interp or [False] * len(t.frames))],
                "interp": [bool(v) for v in (t.interp or [False] * len(t.frames))],
                "min_vis_per_frame": [round(float(s), 4) for s in t.score],
                "facing": dict(facing_report(yaw, fconf),
                               yaw_deg=[round(float(x), 3) for x in yaw]),
                "front_conf": [round(float(x), 4) for x in fconf],
                "world": np.round(t.world, 6).tolist(),
                "norm": np.round(t.norm, 6).tolist(),
                "identity": t.global_id, "shot": int(t.shot),
                "shot_frame_range": [int(per_frame[t.frames[0]]["fi"]),
                                     int(per_frame[t.frames[-1]]["fi"])],
                "axis_note": data["axis_note"],
            }
            with open(os.path.join(os.path.dirname(base), name + ".json"), "w",
                      encoding="utf-8") as f:
                json.dump(d, f, ensure_ascii=False)
            n_exp += 1
        print("  已导出 %d 份每身份 pose.json（可直接 run_retarget --rig pose_mediapipe）" % n_exp)

    if a.overlay:
        cap2 = cv2.VideoCapture(a.inp)
        tiles = []
        step = max(1, T // 12)
        for i in range(0, T, step):
            fr = per_frame[i]
            cap2.set(cv2.CAP_PROP_POS_FRAMES, fr["fi"])
            ok, img = cap2.read()
            if not ok:
                continue
            tinfo = []
            for d in fr["dets"]:
                best, bt, bi = "?", -1, 0.0
                for t in tracks:
                    if t.shot != shot_of[i] or not t.frames:
                        continue
                    if not (t.frames[0] <= i <= t.frames[-1]):
                        continue
                    k = i - t.frames[0]
                    if 0 <= k < len(t.bbox):
                        v = bbox_iou(t.bbox[k], d["bbox"])
                        if v > bi:
                            best, bt, bi = t.global_id, t.tid, v
                tinfo.append((best, bt))
            img = draw_overlay(img, fr["dets"], tinfo, h, w)
            cv2.putText(img, "f%d SHOT%d" % (fr["fi"], shot_of[i]), (8, 26),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 255), 2)
            sc = 300.0 / h
            tiles.append(cv2.resize(img, (int(w * sc), 300)))
        cap2.release()
        if tiles:
            save_montage(tiles, a.overlay, cols=4)
            print("  叠加图  : %s (%d 格)" % (a.overlay, len(tiles)))
    print("=" * 70)
    print("DONE")


if __name__ == "__main__":
    main()
