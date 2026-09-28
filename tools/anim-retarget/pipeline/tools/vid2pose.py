#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""vid2pose.py —— 视频 / 图片 → 人体 3D 关键点序列（MediaPipe BlazePose，纯本地、零下载）。

== 在整个链路里的位置 ==
    视频/图片 --[本脚本]--> pose.json --[pipeline/rigs/pose_mediapipe/retarget.py]--> FBX + TRF
                             33 点世界坐标      关键点 -> 骑砍2 28 骨的 look-at 解算

== 为什么用 BlazePose（而不是 HMR / SMPL 那一挂）==
  * mediapipe 装好即自带模型（mediapipe/modules/pose_*/**.tflite），不需要下载权重
    —— 本机 huggingface / release assets 都不通，HMR 那条路根本走不了。
  * CPU 就能实时，8G 显存的 4060Ti 完全够；torch 都不用。
  * 代价要认：它给的是 33 个关节点位置，没有体型参数、没有扭转(twist)、手指只有 1 根静态骨。
    所以本链路的产物是"关节角度可用、扭转靠插值补"的动画，不是影视级动捕。

== 关键点坐标约定（MediaPipe 原样保留，不要在这里转轴）==
  world: 33x3，米制，原点 = 双髋中点。y 向下为正（鼻 y≈-0.5，脚踝 y≈+0.4）。
         z 越负越靠近相机。这是"以髋为中心"的局部坐标 => 本身不含全局位移。
  norm : 33x4，图像归一化坐标 (x, y, z, visibility)，x/y 属于 [0,1]。全局位移只能从这里估。
  转换到别的坐标系由下游 rigs/pose_mediapipe/retarget.py 一次做掉，避免两边各转一次。

用法
    python pipeline/tools/vid2pose.py --in 某人.mp4 --out work/pose.json --start 30 --end 330 --fps 30
    python pipeline/tools/vid2pose.py --in 某图.png --out work/pose.json
    python pipeline/tools/vid2pose.py --in 某人.mp4 --out work/pose.json --overlay work/overlay.png
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


def imread_utf8(p):
    """Windows + 非 ASCII 路径：cv2.imread 直接返回 None，必须走 imdecode。"""
    import cv2
    buf = np.fromfile(p, dtype=np.uint8)
    return cv2.imdecode(buf, cv2.IMREAD_COLOR)


def open_source(path):
    import cv2
    ext = os.path.splitext(path)[1].lower()
    if ext in (".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tif", ".tiff"):
        img = imread_utf8(path)
        if img is None:
            raise SystemExit("读不到图片：%s" % path)
        h, w = img.shape[:2]
        return "image", None, img, 1.0, (w, h), 1
    cap = cv2.VideoCapture(path)
    if not cap.isOpened():
        raise SystemExit("读不到视频：%s" % path)
    fps = cap.get(cv2.CAP_PROP_FPS) or 30.0
    w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    n = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    return "video", cap, None, fps, (w, h), n


def savgol(x, win, poly=2):
    if win is None or win <= 1 or x.shape[0] < 3:
        return x
    try:
        from scipy.signal import savgol_filter
        w = win if win % 2 == 1 else win + 1
        if w > x.shape[0]:
            w = x.shape[0] if x.shape[0] % 2 == 1 else x.shape[0] - 1
        if w <= poly:
            return x
        return savgol_filter(x, w, poly, axis=0, mode="interp")
    except Exception as e:
        print("  [warn] 平滑跳过：%s" % e)
        return x


def fill_gaps(arr, valid):
    """把无效帧线性插值补上。arr 可以是 [T,K] 或 [T,33,C] 这种多维（自动展平再还原）。"""
    T = arr.shape[0]
    if valid.all():
        return arr
    shape = arr.shape
    flat = arr.reshape(T, -1)          # 🔴 world 是 [T,33,3]、norm 是 [T,33,4]，
                                       #    直接按列 np.interp 会报 "object too deep"
    idx = np.arange(T)
    out = flat.copy()
    for k in range(flat.shape[1]):
        if valid.sum() == 0:
            out[:, k] = 0.0
        else:
            out[:, k] = np.interp(idx, idx[valid], flat[valid, k])
    return out.reshape(shape)


# ─────────────────────── 正反面 / 朝向判定 ───────────────────────
# 🔴 为什么必须有这一步：MediaPipe 的 left/right 是**推断**出来的，人背对镜头时可能判反；
#    一旦左右判反，髋线方向就翻 180°，算出来的"角色前方"整段镜像 —— 而且**看不出来**
#    （姿态每个关节点都还在正确的位置上，只是左右互换）。
#    所以必须自带一个独立的正反面证据 + 连续性检查：
#      · front_conf = 面部关键点（眼/鼻/嘴/耳）的平均可见度 —— **人朝镜头时高、背对时低**，与骨骼无关
#      · yaw = 角色前方在骨架坐标系里相对 +Y 的方位角（0° = 正面朝镜头 = 骑砍2 的标准前方）
#      · 逐帧 yaw 突跳 > 60° ⇒ 极可能是正反面误判
FACE_LM = [0, 2, 5, 9, 10, 7, 8]          # 鼻 / 双眼 / 嘴角 / 双耳


def _axis_matrix():
    """取得 MP->骨架 的固定旋转；优先读 rig 的 map.json（单一事实来源），读不到用内置默认。"""
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
    """返回 (yaw_deg[T], front_conf[T])。yaw = 角色前方相对骨架 +Y 的方位角（0=正面朝镜头）。"""
    AX = _axis_matrix()
    T = len(world)
    yaw = np.zeros(T, np.float32)
    conf = np.zeros(T, np.float32)
    for k in range(T):
        P = (AX @ world[k].T).T                       # MP -> 骨架系
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
        f = np.cross(lf, up)                          # 角色前方（骨架系）
        yaw[k] = math.degrees(math.atan2(float(f[0]), float(f[1])))   # 相对 +Y
        conf[k] = float(np.mean([norm[k][i][3] for i in FACE_LM]))
    return yaw, conf


def facing_report(yaw, conf, valid, front_vis=0.5):
    """打印朝向摘要，返回 dict（也写进 JSON）。"""
    v = yaw[valid] if valid.any() else yaw
    d = np.abs(np.diff(v)) if len(v) > 1 else np.zeros(1)
    d = np.minimum(d, 360.0 - d)                      # 方位角取短弧
    jumps = [(int(i), float(x)) for i, x in enumerate(d) if x > 60.0]
    med = float(np.median(v)) if len(v) else 0.0
    front = float(np.mean(conf[valid])) if valid.any() else float(np.mean(conf))
    is_front = front >= front_vis
    rep = {"median_yaw_deg": round(med, 2),
           "yaw_range_deg": [round(float(v.min()), 2), round(float(v.max()), 2)],
           "max_frame_jump_deg": round(float(d.max()) if len(d) else 0.0, 2),
           "n_jumps_gt60": len(jumps),
           "jump_frames": [j[0] for j in jumps[:20]],
           "face_vis_mean": round(front, 3),
           "likely_front_facing": bool(is_front),
           "note": "yaw=角色前方相对骨架 +Y 的方位角；0° 即正面朝镜头（= 骑砍2 标准前方）"}
    print("  朝向    : 中位 yaw %+.1f°（范围 %+.1f~%+.1f°）｜ 逐帧最大跳变 %.1f°（>60° 的帧：%d 个）"
          % (rep["median_yaw_deg"], rep["yaw_range_deg"][0], rep["yaw_range_deg"][1],
             rep["max_frame_jump_deg"], rep["n_jumps_gt60"]))
    print("  正反面  : 面部关键点平均可见度 %.2f ⇒ %s"
          % (rep["face_vis_mean"], "人朝镜头（正面）" if is_front else "**疑似背对镜头**"))
    if rep["n_jumps_gt60"]:
        print("  [!] 朝向出现 %d 次 >60° 突跳（帧 %s）—— 大概率是 MediaPipe 左右判反，"
              "解算结果会整段镜像，务必看叠加图" % (rep["n_jumps_gt60"], rep["jump_frames"][:8]))
    if not is_front:
        print("  [!] 素材看起来是**背对镜头** —— 解出来的角色会背朝标准前方；"
              "用 run_retarget 的 `--auto-yaw`（或手工 `--yaw 180`）校正")
    return rep




def main():
    ap = argparse.ArgumentParser(description="视频/图片 -> MediaPipe BlazePose 关键点序列")
    ap.add_argument("--in", dest="inp", required=True, help="视频 / 图片路径")
    ap.add_argument("--out", required=True, help="输出 pose.json")
    ap.add_argument("--start", type=int, default=0, help="起始帧（含）")
    ap.add_argument("--end", type=int, default=0, help="结束帧（不含），0 = 到末尾")
    ap.add_argument("--fps", type=float, default=0.0, help="输出抽帧到该 fps，0 = 保持源帧率")
    ap.add_argument("--model", type=int, default=2, choices=[0, 1, 2], help="BlazePose 复杂度（2 最准）")
    ap.add_argument("--smooth", type=int, default=9, help="Savitzky-Golay 窗口（帧），0/1 = 关")
    ap.add_argument("--min-vis", type=float, default=0.5, help="核心点最低可见度，低于则整帧无效")
    ap.add_argument("--overlay", default=None, help="输出叠加骨架的自检图（PNG）")
    a = ap.parse_args()

    import cv2
    import mediapipe as mp

    kind, cap, image, src_fps, size, total = open_source(a.inp)
    stride = 1
    if a.fps and kind == "video" and src_fps > 0:
        stride = max(1, int(round(src_fps / a.fps)))
    f0 = max(0, a.start)
    f1 = a.end if a.end > 0 else (total if total > 0 else 10 ** 9)

    pose = mp.solutions.pose.Pose(
        static_image_mode=(kind == "image"),
        model_complexity=a.model,
        smooth_landmarks=True,
        min_detection_confidence=0.5,
        min_tracking_confidence=0.5,
    )

    world, norm, vis, frames_used, overlays = [], [], [], [], []
    n_expect = 1 if kind == "image" else max(1, (f1 - f0 + stride - 1) // stride)
    fi = -1
    while True:
        if kind == "image":
            frame = image
        else:
            ok, frame = cap.read()
            fi += 1
            if not ok or fi >= f1:
                break
            if fi < f0 or (fi - f0) % stride != 0:
                continue
        res = pose.process(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
        wl = res.pose_world_landmarks.landmark if res.pose_world_landmarks else None
        nl = res.pose_landmarks.landmark if res.pose_landmarks else None
        if wl is None or nl is None:
            world.append(np.zeros((33, 3), np.float32))
            norm.append(np.zeros((33, 4), np.float32))
            vis.append(0.0)
        else:
            world.append(np.array([[l.x, l.y, l.z] for l in wl], np.float32))
            norm.append(np.array([[l.x, l.y, l.z, l.visibility] for l in nl], np.float32))
            vis.append(float(min(nl[i].visibility for i in CORE)))
        frames_used.append(fi)
        # 🔴 图片模式也必须出叠加图：单帧自检恰恰最需要（实测动漫关键帧会出现
        #    "置信度 0.9+ 但关节位置是错的"，不看叠加图根本发现不了）。
        if a.overlay and (kind == "image" or len(world) % max(1, n_expect // 8) == 0):
            overlays.append((len(world) - 1, frame.copy()))
        if kind == "image":
            break
    if cap is not None:
        cap.release()
    pose.close()

    T = len(world)
    if T == 0:
        raise SystemExit("一帧都没读到（--start/--end 是否超范围？）")
    world = np.stack(world)
    norm = np.stack(norm)
    vis = np.array(vis, np.float32)
    # 🔴 体型尺度反查（2026-09-27 新增）：**只信 visibility 会被骗**。
    #    实测：UE 小白人投掷那一刻（帧 280~305），BlazePose 把两只髋判成了几乎重合、还左右互换
    #    （Lhip x=-0.02 / Rhip x=+0.03），而那一帧的 visibility 仍然 >= 0.5 ⇒ 原来的 min_vis 拦不住，
    #    结果根骨 yaw 被甩到 ±120°，整段动作在**最关键的一瞬间**翻掉。
    #    判据：髋宽 / 肩宽 相对全片中位数塌缩到 40% 以下 ⇒ 该帧不可信。
    def _pair_w(arr, i, j):
        d = arr[:, i, :] - arr[:, j, :]
        return np.linalg.norm(d, axis=1)
    hip_w = _pair_w(world, 23, 24)
    sh_w = _pair_w(world, 11, 12)
    med_hip = float(np.median(hip_w)) or 1.0
    med_sh = float(np.median(sh_w)) or 1.0
    scale_ok = (hip_w > 0.40 * med_hip) & (sh_w > 0.40 * med_sh)
    n_scale_bad = int((~scale_ok).sum())

    valid = (vis >= a.min_vis) & scale_ok
    # 🔴 一张图 / 全身被遮挡的视频里，**一个有效帧都没有**是常事（单人照里手腕常被身体挡住，
    #    min(CORE) 掉到 0.3 以下）。这时绝不能"清成 0"——那等于把姿态删了。
    #    降级为"全部当有效"并大声告警：数据留着，让下游和人去判断。
    if valid.sum() == 0:
        print("  [!] 没有帧达到 --min-vis=%.2f（最高才 %.2f）—— 降级为【全部当有效】继续，"
              "结果务必看图确认" % (a.min_vis, float(vis.max())))
        valid = np.ones_like(valid, dtype=bool)
    warn = ""
    if scale_ok.sum() < T:
        print("  [!] 体型尺度反查：%d/%d 帧的髋宽或肩宽塌缩到中位数 40%% 以下（BlazePose 崩帧），已判无效"
              % (n_scale_bad, T))
    if valid.sum() < T:
        warn = "%d/%d 帧无效（已线性插值补洞）" % (T - int(valid.sum()), T)

    world = savgol(fill_gaps(world, valid), a.smooth)
    norm = savgol(fill_gaps(norm, valid), a.smooth)

    # ---- 朝向 / 正反面（独立于骨骼的第二个证据）----
    yaw, fconf = facing_series(world, norm)
    yaw = savgol(yaw.reshape(-1, 1), max(3, a.smooth)).reshape(-1)
    frep = facing_report(yaw, fconf, valid)

    outp = os.path.abspath(a.out)
    if os.path.dirname(outp):
        os.makedirs(os.path.dirname(outp), exist_ok=True)
    out_fps = (src_fps / stride) if kind == "video" else 1.0
    data = {
        "source": os.path.abspath(a.inp),
        "kind": kind,
        "src_fps": float(src_fps),
        "fps": float(out_fps),
        "stride": int(stride),
        "start_frame": int(f0),
        "frames": int(T),
        "size": [int(size[0]), int(size[1])],
        "model_complexity": int(a.model),
        "smooth_window": int(a.smooth),
        "min_vis": float(a.min_vis),
        "landmark_names": LM_NAMES,
        "core_indices": CORE,
        "valid": [bool(v) for v in valid],
        "min_vis_per_frame": [round(float(v), 4) for v in vis],
        "facing": dict(frep, yaw_deg=[round(float(x), 3) for x in yaw]),
        "front_conf": [round(float(x), 4) for x in fconf],
        "world": np.round(world, 6).tolist(),
        "norm": np.round(norm, 6).tolist(),
        "axis_note": "world: 米制, 原点=双髋中点, y 向下为正, z 越负越近相机; norm: (x,y,z,vis)",
    }
    with open(outp, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False)

    print("=" * 68)
    print("  源      : %s (%s, %sx%s, 源 %.1ffps, 共 %s 帧)"
          % (os.path.basename(a.inp), kind, size[0], size[1], src_fps, total or "?"))
    print("  输出    : %s" % outp)
    print("  帧数    : %d (抽帧 stride=%d -> %.1f fps)" % (T, stride, out_fps))
    print("  可见度  : %d/%d 帧 >= %.2f (最低 %.2f / 平均 %.2f)"
          % (int(valid.sum()), T, a.min_vis, vis.min(), vis.mean()))
    print("  朝向    : 中位 yaw %+.1f° ｜ 逐帧最大跳变 %.1f° ｜ 面部可见度 %.2f"
          % (frep["median_yaw_deg"], frep["max_frame_jump_deg"], frep["face_vis_mean"]))
    if warn:
        print("  [!] %s" % warn)
    print("=" * 68)

    if a.overlay and overlays:
        import cv2 as _cv
        from PIL import Image
        tiles = []
        for idx, frame in overlays[:8]:
            n = norm[idx] * np.array([frame.shape[1], frame.shape[0], 1, 1], np.float32)
            col = (0, 255, 0) if valid[idx] else (0, 0, 255)
            for (p, q) in EDGES:
                if n[p, 3] > 0.3 and n[q, 3] > 0.3:
                    _cv.line(frame, (int(n[p, 0]), int(n[p, 1])), (int(n[q, 0]), int(n[q, 1])), col, 3)
            for k in range(33):
                if n[k, 3] > 0.3:
                    _cv.circle(frame, (int(n[k, 0]), int(n[k, 1])), 4, (255, 120, 0), -1)
            # 朝向箭头：髋中点 -> 该帧"角色前方"在该相机下的 2D 投影（用骨架系 fwd 的 x/y 近似）
            hc = (n[23] + n[24]) / 2.0
            fw = np.array([math.sin(math.radians(yaw[idx])), math.cos(math.radians(yaw[idx]))])
            # 骨架 +Y 朝镜头；屏幕 x 取 -X 轴、屏幕 y 取手臂到脚的方向（近似）
            tip = hc[:2] + np.array([-fw[0], -fw[1]]) * (frame.shape[0] * 0.16)
            _cv.arrowedLine(frame, (int(hc[0]), int(hc[1])), (int(tip[0]), int(tip[1])), (0, 210, 255), 4, tipLength=0.35)
            h, w = frame.shape[:2]
            sc = 300.0 / h
            tiles.append(_cv.resize(frame, (int(w * sc), 300)))
        W = sum(t.shape[1] for t in tiles)
        im = Image.new("RGB", (W, 320), (255, 255, 255))
        x = 0
        for t in tiles:
            im.paste(Image.fromarray(t[:, :, ::-1]), (x, 10))
            x += t.shape[1]
        op = os.path.abspath(a.overlay)
        if os.path.dirname(op):
            os.makedirs(os.path.dirname(op), exist_ok=True)
        im.save(op)
        print("  叠加自检: %s (%d 格, 绿=有效 红=无效)" % (op, len(tiles)))
    print("DONE")


if __name__ == "__main__":
    main()
