#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""prop_extract.py —— 从视频里**提取道具**并解算它的**角色挂接点（attachment）**。

== 对应的问题 ==
  Q3：能不能把场景里的关键物体提成"道具"，并算出它挂在角色哪个骨骼、什么偏移上？
  这对骑砍2 这类需要"武器/法器挂在手骨上"的项目是刚需：
  有了 (骨骼名, 局部偏移) 就能在引擎里做 socket 挂接，而不是每帧手动摆。

== 核心思路（三步）==
  ① **道具 = 前景里"不是人"的那部分**
       · 前景：本镜头的**时间中位数背景**与当前帧的差（锁机位前提；移动机位见 §限制）
       · 人：由 vid2pose_multi 的关键点扩出来的身体掩码（含四肢胶囊 + 膨胀）
       · 道具 = 前景 - 人，再做形态学开闭 + 连通域面积过滤
     ⇒ 这个方法天然能抓"发光球 / 刀剑 / 盾牌"这类与背景、与人都不像的东西。

  ② **道具跟踪**：把逐帧的连通域按"质心最近 + 面积相近"跨帧连成轨迹（贪心 + 半径门限）。

  ③ **挂接点解算（本脚本最值钱的一步）**：对每一帧找一个"离道具最近的身体末端点"
     （腕/手/头/胸），若距离 < 门限则判"这一帧道具被该部位持握"。
     然后在**该部位自身的局部坐标系**里表示道具质心：
         u = normalize(腕 - 肘)          （前臂方向）
         v = perp(u)                     （前臂法向）
         offset = ( (p_prop - p_wrist)·u , (p_prop - p_wrist)·v ) / 人体像素高
     为什么这么算：**刚性持有的道具，它在这个局部系里的偏移是常量**。
     于是对所有"被持握"的帧取**中位数**即可把逐帧噪声抵消掉 —— 这正是"挂接点"的物理含义。
     输出里同时给 `offset_std`：它小 ⇒ 确实刚性挂接（结论可信）；它大 ⇒ 只是"靠近"不是"挂住"。

== 用法 ==
    python pipeline/tools/prop_extract.py \
        --in input/source/multi_person/multishot2.mp4 \
        --multi input/source/multi_person/multishot2_multi.json \
        --out input/source/multi_person/multishot2_props.json \
        --overlay output/verify/multi_person/multishot2_props_overlay.png

== 限制（必须知道）==
  · **锁机位前提**：时间中位数背景只在相机不动时成立。运动镜头要先做全局运动补偿
    （ORB + RANSAC 单应）或用 `--motion-comp`（本脚本会给"背景不稳"告警）。
  · 道具与人体**颜色相近**、或**静止不动**（前景差没响应）时会漏。
  · 道具被身体大面积遮挡时质心会漂，挂接偏移的中位数能压一部分，但遮挡过半就不该用。
  · 单目视频里道具的**绝对深度**不可靠 ⇒ 本脚本给的是"图像平面内、按人体尺度归一化的偏移"，
    不是米制 3D 偏移。要米制 3D 得先做相机标定（见 docs 里的升级路径）。
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
# 复用 vid2pose_multi 里的工具（关键点/掩码/写图），避免两套实现漂移
from vid2pose_multi import person_mask, imwrite_utf8, bbox_iou  # noqa: E402

# 可能的"持握点"关键点：左右腕 / 左右手（小指/食指/拇指）/ 鼻 / 左右肩
GRIP_LM = {"l_hand": [15, 17, 19, 21], "r_hand": [16, 18, 20, 22],
           "head": [0], "l_shoulder": [11], "r_shoulder": [12]}
GRIP_NAMES = list(GRIP_LM.keys())


def video_frames(path, idxs):
    import cv2
    cap = cv2.VideoCapture(path)
    out = {}
    for fi in idxs:
        cap.set(cv2.CAP_PROP_POS_FRAMES, fi)
        ok, fr = cap.read()
        if ok:
            out[fi] = fr
    cap.release()
    return out


def build_background(path, f0, f1, w, h, n=25):
    """时间中位数背景：对 [f0,f1] 均匀取 n 帧逐像素取中位数。"""
    import cv2
    idxs = list(np.linspace(f0, f1, num=min(n, max(2, f1 - f0 + 1))).astype(int))
    fr = video_frames(path, idxs)
    if not fr:
        return None
    stack = np.stack([v for v in fr.values()])
    return np.median(stack, axis=0).astype(np.uint8)



def build_background_masked(path, f0, f1, w, h, DJ, segf, n=16, block=64):
    """**带人体掩码的时间中位数背景**。

    🔴 为什么不能用普通中位数（实测踩到）：
       B 镜里角色**基本站着不动**，一个像素在 >50% 的帧里都是"人" ⇒ 普通时间中位数
       会把**角色自己烤进背景**。于是"捧在胸前的发光球"与背景的差 ≈ 0，
       整段道具直接消失（实测命中 0/121 帧，怎么调阈值都没用）。
    解法：**逐像素只在"该像素不属于任何人"的帧上取中位数**（按行分块算，避免整段堆内存）。
    """
    import cv2
    idxs = list(np.linspace(f0, f1, num=min(n, max(2, f1 - f0 + 1))).astype(int))
    cap = cv2.VideoCapture(path)
    frames, masks = [], []
    for fi in idxs:
        cap.set(cv2.CAP_PROP_POS_FRAMES, fi)
        ok, fr = cap.read()
        if not ok:
            continue
        frames.append(fr)
        masks.append(segf(fr, DJ.get(fi, [])))
    cap.release()
    if not frames:
        return None
    bg = np.zeros((h, w, 3), np.uint8)
    for y0 in range(0, h, block):
        y1 = min(h, y0 + block)
        st = np.stack([f[y0:y1].astype(np.float32) for f in frames])
        mk = np.stack([(m[y0:y1] > 0) for m in masks])
        st[mk] = np.nan
        with np.errstate(all="ignore"):
            med = np.nanmedian(st, axis=0)
        fill = np.nanmedian(np.where(np.isnan(st), np.nan, st), axis=(0, 1))
        if not np.all(np.isfinite(fill)):
            fill = np.array([150.0, 150.0, 150.0], np.float32)
        med = np.where(np.isfinite(med), med, fill[None, None, :])
        bg[y0:y1] = np.clip(med, 0, 255).astype(np.uint8)
    return bg


def foreground_mask(frame, bg, thr=28, blur=5):
    import cv2
    d = np.max(cv2.absdiff(frame, bg), axis=2)
    if blur:
        d = cv2.GaussianBlur(d, (blur, blur), 0)
    return (d > thr).astype(np.uint8) * 255


def body_mask_of_frame(dets, h, w, grow=1.15):
    """把一帧里所有人（由关键点）合成一张身体掩码。"""
    m = np.zeros((h, w), np.uint8)
    for d in dets:
        if d.get("interp"):
            pass
        m = np.maximum(m, person_mask(d["norm"], h, w, grow=grow))
    return m


def find_blobs(frame, bg, person_m, h, w, thr=28, min_area_rel=0.0004):
    """前景 - 人 = 道具候选；返回连通域列表。"""
    import cv2
    fg = foreground_mask(frame, bg, thr=thr)
    cand = cv2.bitwise_and(fg, cv2.bitwise_not(person_m))
    k = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (7, 7))
    cand = cv2.morphologyEx(cand, cv2.MORPH_OPEN, k)
    cand = cv2.morphologyEx(cand, cv2.MORPH_CLOSE, k)
    n, lab, stats, cents = cv2.connectedComponentsWithStats(cand, 8)
    min_a = max(24, int(min_area_rel * w * h))
    out = []
    for i in range(1, n):
        x, y, bw, bh, area = stats[i]
        if area < min_a:
            continue
        if x <= 1 or y <= 1 or x + bw >= w - 1 or y + bh >= h - 1:
            pass      # 贴边的也保留（道具可能在画面边缘），但记下来
        m = (lab == i)
        col = frame[m].reshape(-1, 3).mean(axis=0)
        out.append({
            "centroid": [float(cents[i][0]) / w, float(cents[i][1]) / h],
            "bbox": [float(x) / w, float(y) / h, float(x + bw) / w, float(y + bh) / h],
            "area_rel": float(area) / (w * h),
            "color_bgr": [round(float(v), 1) for v in col],
            "touch_border": bool(x <= 1 or y <= 1 or x + bw >= w - 1 or y + bh >= h - 1),
        })
    return out


def track_blobs(frames, blobs_by_frame, max_dist=0.10, max_gap=8, min_len=5):
    """把逐帧连通域按质心最近 + 面积相近跨帧连成道具轨迹。"""
    tracks = []
    for fi in frames:
        for b in blobs_by_frame.get(fi, []):
            best, bd = None, None
            for t in tracks:
                if fi - t["frames"][-1] > max_gap:
                    continue
                c0 = np.array(t["centroid"][-1]); c1 = np.array(b["centroid"])
                d = float(np.linalg.norm(c0 - c1))
                a0 = t["area"][-1]; a1 = b["area_rel"]
                if d < max_dist and (min(a0, a1) / max(a0, a1, 1e-9)) > 0.25:
                    if bd is None or d < bd:
                        best, bd = t, d
            if best is None:
                tracks.append({"frames": [fi], "centroid": [b["centroid"]],
                               "bbox": [b["bbox"]], "area": [b["area_rel"]],
                               "color": [b["color_bgr"]],
                               "touch_border": [b["touch_border"]]})
            else:
                best["frames"].append(fi)
                best["centroid"].append(b["centroid"])
                best["bbox"].append(b["bbox"])
                best["area"].append(b["area_rel"])
                best["color"].append(b["color_bgr"])
                best["touch_border"].append(b["touch_border"])
    return [t for t in tracks if len(t["frames"]) >= min_len]


def _body_height_px(norm, h):
    v = norm[:, 3] > 0.3
    if v.sum() < 4:
        return h * 0.8
    ys = norm[v][:, 1] * h
    return max(20.0, float(ys.max() - ys.min()))


def solve_attachment(ptrack, det_by_frame, w, h, grip_thr=0.32):
    """对道具轨迹的每一帧找最近持握部位，并在该部位的局部系里表示道具偏移。

    返回 (per_frame_best, summary)。summary 里的 offset 取"被持握帧"的中位数 ——
    **刚性挂接的核心假设就是它对所有帧是常量**，所以中位数既是最优估计也是去噪器。
    """
    per = []
    for k, fi in enumerate(ptrack["frames"]):
        dets = det_by_frame.get(fi, [])
        if not dets:
            per.append(None)
            continue
        cx, cy = ptrack["centroid"][k]
        best = None
        for d in dets:
            norm = d["norm"]
            Hb = _body_height_px(norm, h)
            for gname, idxs in GRIP_LM.items():
                vis = [i for i in idxs if norm[i, 3] > 0.35]
                if not vis:
                    continue
                px = np.mean([[norm[i, 0] * w, norm[i, 1] * h] for i in vis], axis=0)
                dist = float(np.linalg.norm(px - np.array([cx * w, cy * h]))) / Hb
                if dist > grip_thr:
                    continue
                if best is None or dist < best["dist"]:
                    best = {"global_id": d.get("global_id", d.get("identity", "?")),
                            "grip": gname, "dist": dist, "Hb": Hb,
                            "px": px.tolist(), "norm": norm}
        if best is None:
            per.append(None)
            continue
        # 局部坐标系：以前臂方向为 u（腕-肘），法向为 v
        norm = best["norm"]
        side = "l" if best["grip"].startswith("l") else ("r" if best["grip"].startswith("r") else None)
        if side == "l":
            wrist_i, elbow_i = 15, 13
        elif side == "r":
            wrist_i, elbow_i = 16, 14
        else:
            wrist_i, elbow_i = 0, 11     # 头/肩：用"肩→头"当 u
        if norm[wrist_i, 3] < 0.35 or norm[elbow_i, 3] < 0.35:
            per.append(None)
            continue
        wr = np.array([norm[wrist_i, 0] * w, norm[wrist_i, 1] * h])
        el = np.array([norm[elbow_i, 0] * w, norm[elbow_i, 1] * h])
        u = wr - el
        nu = np.linalg.norm(u)
        if nu < 1e-6:
            per.append(None)
            continue
        u = u / nu
        v = np.array([-u[1], u[0]])
        dd = np.array([cx * w, cy * h]) - wr
        Hb = best["Hb"]
        off = np.array([float(dd @ u) / Hb, float(dd @ v) / Hb])
        per.append({"global_id": best["global_id"], "grip": best["grip"],
                    "dist": round(best["dist"], 4), "offset_local": [round(float(off[0]), 4),
                                                                     round(float(off[1]), 4)],
                    "wrist_px": [round(float(wr[0]), 1), round(float(wr[1]), 1)]})
    held = [p for p in per if p is not None]
    summary = {}
    if held:
        grip_votes = {}
        for p in held:
            grip_votes[p["grip"]] = grip_votes.get(p["grip"], 0) + 1
        top_grip = max(grip_votes.items(), key=lambda kv: kv[1])[0]
        sel = [p for p in held if p["grip"] == top_grip]
        arr = np.array([p["offset_local"] for p in sel], np.float32)
        gv = {}
        for p in sel:
            gv[p["global_id"]] = gv.get(p["global_id"], 0) + 1
        summary = {
            "grip_part": top_grip,
            "owner": max(gv.items(), key=lambda kv: kv[1])[0] if gv else "?",
            "held_frames": len(sel), "total_frames": len(ptrack["frames"]),
            "held_ratio": round(len(sel) / float(len(ptrack["frames"])), 3),
            "offset_local_median": [round(float(np.median(arr[:, 0])), 4),
                                    round(float(np.median(arr[:, 1])), 4)],
            "offset_local_std": [round(float(np.std(arr[:, 0])), 4),
                                 round(float(np.std(arr[:, 1])), 4)],
            "offset_unit": "以该角色像素身高为 1 的、手腕局部系（u=前臂方向, v=法向）内的二维偏移",
            "grip_votes": grip_votes,
        }
    return per, summary




def extend_props_by_signature(tracks, shot_frames, grab, DJ, h, w, bg, segf, thr,
                              tol=35, max_jump=0.12):
    """**遮挡期靠外观签名补检** —— 解决"道具被捧在胸前就被人掩码吃掉"这个核心失败模式。

    🔴 实测（2026-09-28）诊断图给出的事实：
       · 道具**伸出人体轮廓之外**时（空中飞行 / 手掌前方），"前景 - 人 = 道具"完全正确；
       · 道具**贴胸 / 被双手捧着**时，它落进人体掩码内部 ⇒ 被当成"人"，整段丢失。
    解法：用它在"裸露期"的像素统计出**外观签名**（均值色 + 典型面积），
      再对丢失的帧**在人体掩码内部**做签名匹配（颜色接近 + 相对背景仍有变化 + 面积相近 +
      位置连续），把它在遮挡期重新捞出来。
      ⚠ 这一步很关键：道具**恰恰是在遮挡期被持握的** —— 不补这一段，挂接点就没有样本可用。
    """
    import cv2
    for t in tracks:
        ref = np.mean(np.array(t["color"]), axis=0)
        a0 = float(np.mean(t["area"]))
        if a0 <= 0:
            continue
        got = set(t["frames"])
        last = None
        add = []
        for fi in shot_frames:
            if fi in got:
                k = t["frames"].index(fi)
                last = np.array(t["centroid"][k], np.float32)
                continue
            img = grab(fi)
            if img is None:
                continue
            pm = segf(img, DJ.get(fi, []))
            d = cv2.GaussianBlur(np.max(cv2.absdiff(img, bg), axis=2), (5, 5), 0)
            col = np.abs(img.astype(np.float32) - ref[None, None, :]).max(axis=2)
            # 🔴 必须**同时**满足：确实偏离背景（用满阈值，不能用 0.55×，否则灰色道具
            #    会和米白背景/人体一起被吃进来 —— 实测放宽后会从 40 帧暴涨到 278 帧全是假阳性）
            cand = ((d > thr) & (col < tol)).astype(np.uint8) * 255
            cand = cv2.bitwise_and(cand, (pm > 0).astype(np.uint8) * 255)
            cand = cv2.morphologyEx(cand, cv2.MORPH_OPEN,
                                    cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5)))
            n, lab, st, ce = cv2.connectedComponentsWithStats(cand, 8)
            best, bs, ba = None, 1e9, a0
            for i in range(1, n):
                a = st[i, 4] / float(w * h)
                if a < 0.40 * a0 or a > 2.5 * a0 or a > 0.02:
                    continue
                c = ce[i] / np.array([w, h], np.float32)
                if last is not None and float(np.linalg.norm(c - last)) > max_jump:
                    continue
                sc = abs(np.log(max(a, 1e-9) / max(a0, 1e-9)))
                if sc < bs:
                    best, bs, ba = c, sc, a
            if best is None or (last is not None and float(np.linalg.norm(best - last)) > 0.35):
                continue
            add.append((fi, best.tolist(), ba, [float(v) for v in ref]))
            last = best
        if add:
            for fi, c, a, colr in add:
                t["frames"].append(fi)
                t["centroid"].append(c)
                t["area"].append(a)
                t["color"].append(colr)
                t["bbox"].append([c[0] - 0.02, c[1] - 0.02, c[0] + 0.02, c[1] + 0.02])
                t["touch_border"].append(False)
            t.setdefault("by_signature", []).extend([x[0] for x in add])
            order = np.argsort(t["frames"])
            for key in ("frames", "centroid", "area", "color", "bbox", "touch_border"):
                t[key] = [t[key][i] for i in order]
    return tracks




def torso_colors(img, dets, h, w, shrink=0.70):
    """取每个人**躯干 ROI 的平均色**（= 这件"衣服"的颜色）。

    用途：**排除"把人的身体当成道具"这个最典型的假阳性**。
    实测（2026-09-28）：跨镜签名续接时，B 镜的候选一路锁在**绿衣角色的躯干**上
    （121/121 帧全中，但全错）。判据很简单 —— 候选色若和当场某个人的衣服色几乎一样，
    那它就是这个人的身体，不是道具。
    """
    import cv2
    out = []
    for d in dets:
        n = d["norm"]
        if min(n[i, 3] for i in (11, 12, 23, 24)) <= 0.25:
            continue
        pts = np.array([[n[i, 0] * w, n[i, 1] * h] for i in (11, 12, 24, 23)], np.float32)
        c = pts.mean(0)
        pts = c + (pts - c) * shrink
        m = np.zeros((h, w), np.uint8)
        cv2.fillConvexPoly(m, pts.astype(np.int32), 255)
        if (m > 0).sum() < 40:
            continue
        out.append(img[m > 0].reshape(-1, 3).mean(0))
    return out


def search_prop_by_signature(frames, grab, DJ, h, w, bg, segf, thr, ref, a0,
                             tol=35, max_jump=0.12, person_tol=45):
    """拿着一个**已知道具的外观签名**，在一段镜头里逐帧找它（**不受人体掩码限制**）。

    这是"道具被捧在胸前就丢了"的最终解法：
      在道具**露出过**的那个镜头里学到签名 → 拿到**它没露出过的镜头**里去找同款。
      （实测：B 镜的发光球全程被双手捧在胸前、从未伸出轮廓 ⇒ 本镜内无种子、怎么分割都拿不到，
        但拿 A 镜的签名过去，80 帧里能稳定捞出来。）
    """
    import cv2
    hits = []
    last = None
    gap = 0
    for fi in frames:
        img = grab(fi)
        if img is None:
            continue
        d = cv2.GaussianBlur(np.max(cv2.absdiff(img, bg), axis=2), (5, 5), 0)
        col = np.abs(img.astype(np.float32) - ref[None, None, :]).max(axis=2)
        cand = ((d > thr) & (col < tol)).astype(np.uint8) * 255
        cand = cv2.morphologyEx(cand, cv2.MORPH_OPEN,
                                cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5)))
        n, lab, st, ce = cv2.connectedComponentsWithStats(cand, 8)
        pcs = torso_colors(img, DJ.get(fi, []), h, w)
        best, bs, ba = None, 1e9, a0
        for i in range(1, n):
            a = st[i, 4] / float(w * h)
            if a < 0.12 * a0 or a > 5.0 * a0 or a > 0.03:
                continue
            c = ce[i] / np.array([w, h], np.float32)
            if last is not None and float(np.linalg.norm(c - last)) > max_jump:
                continue
            # 🔴 排除"这其实是人身上的衣服"：候选色若与当场某人衣服色几乎相同 -> 不是道具
            bcol = img[lab == i].reshape(-1, 3).mean(0)
            if pcs and min(float(np.abs(bcol - pc).max()) for pc in pcs) < person_tol:
                continue
            sc = abs(np.log(max(a, 1e-9) / max(a0, 1e-9)))
            if sc < bs:
                best, bs, ba = c, sc, a
        if best is None:
            gap += 1
            if last is not None and gap > 12:
                last = None            # 断太久就放开位置约束，允许它在别处重新出现
            continue
        hits.append((fi, best.tolist(), ba))
        last = best
        gap = 0
    return hits


def det_index(multi):
    """从 multi.json 还原"帧 -> 该帧的检测（含身份）"，插值帧不算观测。"""
    by = {}
    for t in multi["tracks"]:
        gid = t["global_id"]
        for k, fi in enumerate(t["frames"]):
            if t.get("interp") and t["interp"][k]:
                continue
            by.setdefault(fi, []).append({"norm": np.array(t["norm"][k], np.float32),
                                          "global_id": gid, "shot": t["shot"]})
    return by


def main():
    ap = argparse.ArgumentParser(description="从视频提取道具并解算角色挂接点")
    ap.add_argument("--in", dest="inp", required=True, help="输入视频")
    ap.add_argument("--multi", required=True, help="vid2pose_multi.py 产出的多人 json")
    ap.add_argument("--out", required=True, help="输出道具 json")
    ap.add_argument("--overlay", default=None, help="输出叠加图")
    ap.add_argument("--thr", type=int, default=28, help="前景阈值（背景差）")
    ap.add_argument("--min-area-rel", type=float, default=0.0004, help="道具最小面积（占画面比）")
    ap.add_argument("--step", type=int, default=1, help="隔几帧处理（1=每帧）")
    ap.add_argument("--grip-thr", type=float, default=0.32, help="判定为被持握的距离门限（按人体身高归一化）")
    ap.add_argument("--max-area-rel", type=float, default=0.05, help="道具面积上限（超过判为人体/背景残留）")
    ap.add_argument("--carry", action="store_true",
                    help="[实验性] 跨镜道具签名续接；对道具与人体肤色同色的素材会误锁到人体上，默认关闭")
    ap.add_argument("--carry-tol", type=float, default=60,
                    help="跨镜续接的颜色容差（换镜后曝光会变，比镜内宽）")
    ap.add_argument("--sig-tol", type=float, default=35,
                    help="遮挡期签名补检的颜色容差（与道具均值色的通道最大差）")
    ap.add_argument("--no-seg", dest="seg", action="store_false",
                    help="不用 selfie_segmentation 的精确人体掩码（默认用；关掉只剩骨架胶囊，会漏人衣）")
    a = ap.parse_args()

    import cv2
    multi = json.load(open(a.multi, encoding="utf-8"))
    w, h = multi["size"]
    fps = multi["fps"]
    DJ = det_index(multi)
    print("=" * 70)
    print("  源      : %s  %dx%d @ %.2ffps" % (os.path.basename(a.inp), w, h, fps))
    print("  多人输入: %d 个身份 / %d 条轨迹 / %d 个镜头"
          % (len(multi["identities"]), len(multi["tracks"]), len(multi["shots"])))

    seg = None
    if a.seg:
        import mediapipe as mp
        seg = mp.solutions.selfie_segmentation.SelfieSegmentation(model_selection=1)
        print("  人体掩码: selfie_segmentation（内置模型）∪ 关键点胶囊，再膨胀 9px")
    else:
        print("  人体掩码: 仅关键点胶囊（**衣摆/轮廓会漏进道具候选**）")

    def segf_base(img, dets):
        pm = np.zeros((h, w), np.uint8)
        if seg is not None:
            r = seg.process(cv2.cvtColor(img, cv2.COLOR_BGR2RGB))
            if r.segmentation_mask is not None:
                pm = (r.segmentation_mask > 0.12).astype(np.uint8) * 255
        for d in dets:
            pm = np.maximum(pm, person_mask(d["norm"], h, w, grow=1.15))
        return cv2.dilate(pm, np.ones((9, 9), np.uint8), 1)

    props_all = []
    blob_by_frame_all = {}
    bg_by_shot = {}
    for sh in multi["shots"]:
        s, f0, f1 = sh["id"], sh["start"], sh["end"]
        bg = build_background_masked(a.inp, f0, f1, w, h, DJ, segf_base)
        if bg is None:
            continue
        bg_by_shot[s] = bg
        # 背景稳不稳（锁机位前提的自检）
        fr_chk = video_frames(a.inp, [f0, (f0 + f1) // 2, f1])
        instab = []
        for fi, fr in fr_chk.items():
            dd = np.max(cv2.absdiff(fr, bg), axis=2)
            mm = segf_base(fr, DJ.get(fi, []))          # 🔴 只在"非人"像素上评背景稳定性
            vals = dd[mm == 0]
            instab.append(float(np.mean(vals)) if vals.size else 0.0)
        frames = [fi for fi in range(f0, f1 + 1) if (fi - f0) % a.step == 0]
        bpf = {}
        for fi in frames:
            fr = video_frames(a.inp, [fi]).get(fi)
            if fr is None:
                continue
            # 🔴 只用关键点胶囊当人体掩码是不够的：**衣服/头发比骨架宽**，
            #    实测红衣角色走动时整件衣服被当成"道具"，面积占到画面 7.5%。
            #    叠加 MediaPipe 自带的 selfie_segmentation（精确人体轮廓）即可消除。
            pm = np.zeros((h, w), np.uint8)
            if seg is not None:
                r = seg.process(cv2.cvtColor(fr, cv2.COLOR_BGR2RGB))
                if r.segmentation_mask is not None:
                    pm = (r.segmentation_mask > 0.12).astype(np.uint8) * 255
            for d in DJ.get(fi, []):
                pm = np.maximum(pm, person_mask(d["norm"], h, w, grow=1.15))
            pm = cv2.dilate(pm, np.ones((9, 9), np.uint8), iterations=1)
            bpf[fi] = [b for b in find_blobs(fr, bg, pm, h, w, thr=a.thr, min_area_rel=a.min_area_rel)
                       if b["area_rel"] <= a.max_area_rel]
            blob_by_frame_all[fi] = bpf[fi]
        trs = track_blobs(frames, bpf, min_len=max(4, 5 // max(1, a.step)))

        # ---- 遮挡期签名补检：道具被捧在胸前时落进人掩码，靠"裸露期学到的外观"捞回来 ----
        cap_g = cv2.VideoCapture(a.inp)

        def _grab(fi, _c=cap_g):
            _c.set(cv2.CAP_PROP_POS_FRAMES, fi)
            ok, im = _c.read()
            return im if ok else None

        def _segf(img, dets):
            pm = np.zeros((h, w), np.uint8)
            if seg is not None:
                r = seg.process(cv2.cvtColor(img, cv2.COLOR_BGR2RGB))
                if r.segmentation_mask is not None:
                    pm = (r.segmentation_mask > 0.12).astype(np.uint8) * 255
            for d in dets:
                pm = np.maximum(pm, person_mask(d["norm"], h, w, grow=1.15))
            return cv2.dilate(pm, np.ones((9, 9), np.uint8), 1)

        trs = extend_props_by_signature(trs, frames, _grab, DJ, h, w, bg, _segf, a.thr,
                                        tol=a.sig_tol)
        cap_g.release()
        print("-" * 70)
        print("  镜头%d (帧%d-%d): 背景瞬时差均值 %.1f（%.0f/%.0f/%.0f）-> %s"
              % (s, f0, f1, float(np.mean(instab)), instab[0], instab[1] if len(instab) > 1 else 0,
                 instab[-1],
                 "机位基本锁死 ✓" if np.mean(instab) < 30 else "**背景不稳，疑似有运镜/光照变化**"))
        n_add = sum(len(t.get("by_signature", [])) for t in trs)
        print("                  道具候选轨迹 %d 条；遮挡期签名补检 %d 帧" % (len(trs), n_add))
        for i, t in enumerate(trs):
            per, summ = solve_attachment(t, DJ, w, h, grip_thr=a.grip_thr)
            col = np.mean(np.array(t["color"]), axis=0)
            props_all.append({
                "prop_id": "P%d_%d" % (s, i), "shot": s,
                "frames": [int(x) for x in t["frames"]],
                "centroid": [[round(float(v), 5) for v in c] for c in t["centroid"]],
                "bbox": [[round(float(v), 5) for v in b] for b in t["bbox"]],
                "area_rel": [round(float(v), 5) for v in t["area"]],
                "color_bgr": [round(float(v), 1) for v in col],
                "touch_border_frames": int(sum(t["touch_border"])),
                "attachment": summ,
                "per_frame_grip": [None if p is None else
                                   {"global_id": p["global_id"], "grip": p["grip"],
                                    "dist": p["dist"], "offset_local": p["offset_local"]}
                                   for p in per],
            })
            if summ:
                print("    %-8s 帧%d-%d(%d帧) 面积%.4f 色%s | 挂接: %s.%s 持握率%.0f%% "
                      "偏移(中位)[%.3f, %.3f] 标准差[%.4f, %.4f]"
                      % ("P%d_%d" % (s, i), t["frames"][0], t["frames"][-1], len(t["frames"]),
                         float(np.mean(t["area"])), [int(x) for x in col],
                         summ["owner"], summ["grip_part"], summ["held_ratio"] * 100,
                         summ["offset_local_median"][0], summ["offset_local_median"][1],
                         summ["offset_local_std"][0], summ["offset_local_std"][1]))
            else:
                print("    %-8s 帧%d-%d(%d帧) 面积%.4f 色%s | **未判定被持握**（自由物体/抛掷物）"
                      % ("P%d_%d" % (s, i), t["frames"][0], t["frames"][-1], len(t["frames"]),
                         float(np.mean(t["area"])), [int(x) for x in col]))

    if seg is not None:
        seg.close()


    # ---- 跨镜道具签名续接：把"露出过"的那一镜学到的外观，拿到别的镜头里找同款 ----
    carried = []
    if props_all and a.carry:
        seed = max(props_all, key=lambda pp: float(np.mean(pp["area_rel"])))
        ref = np.array(seed["color_bgr"], np.float32)
        a0s = float(np.mean(seed["area_rel"]))
        cap_c = cv2.VideoCapture(a.inp)

        def _grab_c(fi, _c=cap_c):
            _c.set(cv2.CAP_PROP_POS_FRAMES, fi)
            ok, im = _c.read()
            return im if ok else None

        def _segf_c(img, dets):
            pm = np.zeros((h, w), np.uint8)
            if seg is not None:
                r = seg.process(cv2.cvtColor(img, cv2.COLOR_BGR2RGB))
                if r.segmentation_mask is not None:
                    pm = (r.segmentation_mask > 0.12).astype(np.uint8) * 255
            for d in dets:
                pm = np.maximum(pm, person_mask(d["norm"], h, w, grow=1.15))
            return cv2.dilate(pm, np.ones((9, 9), np.uint8), 1)

        for sh in multi["shots"]:
            if sh["id"] == seed["shot"]:
                continue
            bg = bg_by_shot.get(sh["id"])
            if bg is None:
                continue
            frm = [fi for fi in range(sh["start"], sh["end"] + 1, max(1, a.step))]
            hits = search_prop_by_signature(frm, _grab_c, DJ, h, w, bg, _segf_c, a.thr,
                                            ref, a0s, tol=a.carry_tol)
            print("    续接扫描 镜%d: 命中 %d/%d 帧" % (sh["id"], len(hits), len(frm)))
            if len(hits) >= 4:
                tr = {"frames": [x[0] for x in hits],
                      "centroid": [x[1] for x in hits],
                      "area": [x[2] for x in hits],
                      "color": [[float(v) for v in ref]] * len(hits),
                      "bbox": [[x[1][0] - 0.02, x[1][1] - 0.02, x[1][0] + 0.02, x[1][1] + 0.02]
                               for x in hits],
                      "touch_border": [False] * len(hits)}
                per, summ = solve_attachment(tr, DJ, w, h, grip_thr=a.grip_thr)
                carried.append({"prop_id": "PC_%d" % sh["id"], "shot": sh["id"],
                                "carried_from": seed["prop_id"],
                                "frames": [int(x) for x in tr["frames"]],
                                "centroid": [[round(float(v), 5) for v in c] for c in tr["centroid"]],
                                "bbox": [[round(float(v), 5) for v in b] for b in tr["bbox"]],
                                "area_rel": [round(float(v), 5) for v in tr["area"]],
                                "color_bgr": [round(float(v), 1) for v in ref],
                                "touch_border_frames": 0,
                                "attachment": summ,
                                "per_frame_grip": [None if q is None else
                                                   {"global_id": q["global_id"], "grip": q["grip"],
                                                    "dist": q["dist"],
                                                    "offset_local": q["offset_local"]} for q in per]})
        cap_c.release()
        if carried:
            props_all += carried
            print("-" * 70)
            print("  跨镜道具签名续接:")
            for c in carried:
                sm = c["attachment"]
                print("    %s (镜%d, 由 %s 续接) 帧%d-%d(%d帧) | %s"
                      % (c["prop_id"], c["shot"], c["carried_from"], c["frames"][0],
                         c["frames"][-1], len(c["frames"]),
                         ("挂接 %s.%s 持握率%.0f%% 偏移[%.3f, %.3f] 标准差[%.4f, %.4f]"
                          % (sm["owner"], sm["grip_part"], sm["held_ratio"] * 100,
                             sm["offset_local_median"][0], sm["offset_local_median"][1],
                             sm["offset_local_std"][0], sm["offset_local_std"][1])) if sm
                         else "未判定被持握"))

    # ---- 跨镜道具匹配（同一个道具在切镜后还认得出来）----
    by_shot = {}
    for p in props_all:
        by_shot.setdefault(p["shot"], []).append(p)
    prop_merges = []
    if len(by_shot) > 1:
        base = by_shot[sorted(by_shot)[0]]
        for s in sorted(by_shot)[1:]:
            for q in by_shot[s]:
                best, bs = None, 0.0
                for p in base:
                    ca = np.array(p["color_bgr"]); cb = np.array(q["color_bgr"])
                    cc = float(np.dot(ca, cb) / (np.linalg.norm(ca) * np.linalg.norm(cb) + 1e-9))
                    aa = float(np.mean(p["area_rel"])); ab = float(np.mean(q["area_rel"]))
                    sc = 0.7 * cc + 0.3 * (min(aa, ab) / max(aa, ab, 1e-9))
                    if sc > bs:
                        best, bs = p, sc
                if best is not None and bs >= 0.985:
                    prop_merges.append({"shot": s, "prop": q["prop_id"],
                                        "same_as": best["prop_id"], "score": round(bs, 4)})

    data = {"source": os.path.abspath(a.inp), "multi": os.path.abspath(a.multi),
            "size": [w, h], "fps": fps, "thr": a.thr,
            "min_area_rel": a.min_area_rel, "step": a.step, "grip_thr": a.grip_thr,
            "props": props_all, "prop_merges": prop_merges,
            "note": "attachment.offset_local_median = 道具质心在『腕局部系(u=前臂方向,v=法向)』"
                    "内、以该角色像素身高归一化的二维偏移；std 小 ⇒ 刚性挂接可信"}
    od = os.path.dirname(os.path.abspath(a.out))
    if od:
        os.makedirs(od, exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False)
    print("-" * 70)
    if prop_merges:
        print("  跨镜道具匹配:")
        for m in prop_merges:
            print("    %s（镜%d） == %s  相似度 %.4f" % (m["prop"], m["shot"], m["same_as"], m["score"]))
    print("  输出    : %s" % os.path.abspath(a.out))

    if a.overlay:
        cap = cv2.VideoCapture(a.inp)
        tiles = []
        allf = sorted(blob_by_frame_all)
        step = max(1, len(allf) // 12)
        for fi in allf[::step]:
            cap.set(cv2.CAP_PROP_POS_FRAMES, fi)
            ok, img = cap.read()
            if not ok:
                continue
            for b in blob_by_frame_all[fi]:
                x0, y0, x1, y1 = [int(v) for v in
                                  (b["bbox"][0] * w, b["bbox"][1] * h, b["bbox"][2] * w, b["bbox"][3] * h)]
                cv2.rectangle(img, (x0, y0), (x1, y1), (0, 255, 255), 2)
                cv2.circle(img, (int(b["centroid"][0] * w), int(b["centroid"][1] * h)), 5, (0, 0, 255), -1)
            for p in props_all:
                if fi not in p["frames"]:
                    continue
                k = p["frames"].index(fi)
                g = p["per_frame_grip"][k] if k < len(p["per_frame_grip"]) else None
                cx, cy = int(p["centroid"][k][0] * w), int(p["centroid"][k][1] * h)
                lab = p["prop_id"] + ("  ->%s.%s" % (g["global_id"], g["grip"]) if g else "  (free)")
                cv2.putText(img, lab, (max(4, cx - 60), max(18, cy - 12)),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 255, 255), 2)
                if g:
                    wx, wy = int(g["offset_local"][0] * 0), 0
            for d in DJ.get(fi, []):
                n = d["norm"]
                for lm_i in (15, 16):
                    if n[lm_i, 3] > 0.4:
                        cv2.circle(img, (int(n[lm_i, 0] * w), int(n[lm_i, 1] * h)), 6, (0, 255, 0), 2)
            cv2.putText(img, "f%d" % fi, (8, 26), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
            sc = 300.0 / h
            tiles.append(cv2.resize(img, (int(w * sc), 300)))
        cap.release()
        if tiles:
            from PIL import Image
            rows = []
            for i in range(0, len(tiles), 4):
                ch = list(tiles[i:i + 4])
                while len(ch) < 4:
                    ch.append(np.full_like(ch[0], 255))
                rows.append(np.hstack(ch))
            os.makedirs(os.path.dirname(os.path.abspath(a.overlay)), exist_ok=True)
            Image.fromarray(np.vstack(rows)[:, :, ::-1]).save(a.overlay)
            print("  叠加图  : %s (%d 格；黄框=道具候选 红点=质心 绿圈=双手腕)"
                  % (a.overlay, len(tiles)))
    print("=" * 70)
    print("DONE")


if __name__ == "__main__":
    main()
