#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""author_dianxue_pair.py —— **手 K 关键帧**编排成对交互「花式点穴 → 应声倒地」。

== 为什么不再靠生成 ==
  同一段"点穴"提示词生成了 4 版，实测：
    v1 两头废时间 / v2 动作被拍没 / v3 全挤在前 0.5s / v4 手在肩上一按 2.7 秒然后突然扑地。
  **四拍编排（绕手备势 → 并指双击 → 瞬间定格 → 直挺挺前倒）一个都没出现。**
  ⇒ 生成模型可以给你"一个动作"，但给不了"一套编排"。编排这件事必须自己写。

== 思路：不直接写骨骼，而是**合成关键点**，复用现成解算链路 ==
  本项目已经有一条成熟的 pose 解算链（`pipeline/rigs/pose_mediapipe/retarget.py`：把 33 个
  3D 关键点"瞄准"成骑砍2 28 骨）。所以这里只要**造出 33 个关键点的逐帧轨迹**，
  下游（解算 / TRF / 倒地根位移 / GLB / 查看器）**一行都不用改**。

== 坐标系（本文件内部一律用它）==
  **骨架系**：X = 角色右手侧　Y = 角色正前方　Z = 上　原点 = 双髋中点，米制，站立站姿。
  写文件前转成 MediaPipe world：`mp = (-X, -Z, -Y)`（就是 retarget 里那个固定旋转的逆）。

== 编排（24fps，共 96 帧 = 4.0 秒）==
  攻击者： 绕手备势(划圈) → 蓄势 → **双击×2** → 收势造型 → 保持造型
  受击者： 站立 → **被点中瞬间绷直定格** → 定住不动 → **绕脚踝刚性前倒** → 躺平
  （"刚性"= 全身关键点整体一起转，不做任何关节弯曲 ⇒ 看上去就是一块木板拍下去）

⚠️ 已知限制：骑砍2 每只手只有 **1 根静态手指骨**，所以"剑指"手型做不出来。
   "花式"只能靠**手臂/手腕的编排**（绕圈、双击、收势）来传达。

用法
    python pipeline/tools/author_dianxue_pair.py --outdir input/source/dianxue_author --name dianxue_auth
    # 然后照常解算：
    python pipeline/run_retarget.py --rig pose_mediapipe --clip dianxue_auth__atk --name dx_atk \
        --animdir input/source/dianxue_author --pelvis ground
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

LM_NAMES = ["nose", "left_eye_inner", "left_eye", "left_eye_outer", "right_eye_inner", "right_eye",
            "right_eye_outer", "left_ear", "right_ear", "mouth_left", "mouth_right",
            "left_shoulder", "right_shoulder", "left_elbow", "right_elbow", "left_wrist", "right_wrist",
            "left_pinky", "right_pinky", "left_index", "right_index", "left_thumb", "right_thumb",
            "left_hip", "right_hip", "left_knee", "right_knee", "left_ankle", "right_ankle",
            "left_heel", "right_heel", "left_foot_index", "right_foot_index"]
CORE = [11, 12, 13, 14, 15, 16, 23, 24, 25, 26, 27, 28]

# 骨架系 -> MediaPipe（R = R^T，自逆）
R = np.array([[-1.0, 0, 0], [0, 0, -1.0], [0, -1.0, 0]])


def a2m(P):
    return np.asarray(P, np.float64) @ R.T


# 站立静止姿势（骨架系，米，原点=双髋中点）—— 手写的解剖学近似，够用
REST = {
    0: (0.00, 0.09, 0.72),
    1: (-0.015, 0.080, 0.755), 2: (-0.035, 0.080, 0.755), 3: (-0.055, 0.075, 0.750),
    4: (0.015, 0.080, 0.755), 5: (0.035, 0.080, 0.755), 6: (0.055, 0.075, 0.750),
    7: (-0.075, 0.0, 0.740), 8: (0.075, 0.0, 0.740),
    9: (-0.025, 0.085, 0.690), 10: (0.025, 0.085, 0.690),
    11: (-0.17, 0.0, 0.470), 12: (0.17, 0.0, 0.470),
    13: (-0.19, 0.01, 0.180), 14: (0.19, 0.01, 0.180),
    15: (-0.20, 0.02, -0.120), 16: (0.20, 0.02, -0.120),
    17: (-0.235, 0.02, -0.210), 18: (0.235, 0.02, -0.210),
    19: (-0.195, 0.04, -0.220), 20: (0.195, 0.04, -0.220),
    21: (-0.160, 0.01, -0.170), 22: (0.160, 0.01, -0.170),
    23: (-0.09, 0.0, 0.0), 24: (0.09, 0.0, 0.0),
    25: (-0.09, 0.0, -0.45), 26: (0.09, 0.0, -0.45),
    27: (-0.09, 0.0, -0.88), 28: (0.09, 0.0, -0.88),
    29: (-0.09, -0.06, -0.93), 30: (0.09, -0.06, -0.93),
    31: (-0.09, 0.12, -0.95), 32: (0.09, 0.12, -0.95),
}


def rest_pose():
    return np.array([REST[i] for i in range(33)], np.float64)


def ik2(S, W, L1, L2, pole):
    """两骨 IK：给定肩 S、腕目标 W、上/前臂长度与"肘往哪边拐"的 pole，求肘位置。"""
    d = W - S
    dist = float(np.linalg.norm(d))
    if dist < 1e-6:
        return S + np.array([0.0, 0.0, -L1])
    dist = float(np.clip(dist, abs(L1 - L2) + 1e-4, L1 + L2 - 1e-4))
    dn = d / np.linalg.norm(d)
    a = (L1 * L1 - L2 * L2 + dist * dist) / (2.0 * dist)
    h = math.sqrt(max(0.0, L1 * L1 - a * a))
    p = pole - dn * float(np.dot(pole, dn))
    if np.linalg.norm(p) < 1e-6:
        p = np.cross(dn, np.array([0.0, 0.0, 1.0]))
        if np.linalg.norm(p) < 1e-6:
            p = np.array([1.0, 0.0, 0.0])
    p = p / np.linalg.norm(p)
    return S + a * dn + h * p


def set_arm(P, side, wrist, pole=None, clamp=True):
    """把某一侧的整条手臂（肘/腕/手三点）摆到指定腕位置。side: 'r' | 'l'。"""
    if side == "r":
        sh, el, wr, pk, ix, th = 12, 14, 16, 18, 20, 22
        dflt_pole = np.array([0.75, -0.25, -1.0])
    else:
        sh, el, wr, pk, ix, th = 11, 13, 15, 17, 19, 21
        dflt_pole = np.array([-0.75, -0.25, -1.0])
    S = P[sh].copy()
    L1 = float(np.linalg.norm(np.array(REST[el]) - np.array(REST[sh])))
    L2 = float(np.linalg.norm(np.array(REST[wr]) - np.array(REST[el])))
    W = np.asarray(wrist, np.float64)
    if clamp:                      # 够不到就缩到可达球面（解算是只看方向的，这样更干净）
        v = W - S
        n = float(np.linalg.norm(v))
        if n > (L1 + L2) * 0.985:
            W = S + v / n * (L1 + L2) * 0.985
    E = ik2(S, W, L1, L2, dflt_pole if pole is None else pole)
    P[el] = E
    P[wr] = W
    fwd = W - E
    fwd = fwd / max(np.linalg.norm(fwd), 1e-6)
    sd = np.cross(fwd, np.array([0.0, 0.0, 1.0]))
    if np.linalg.norm(sd) < 1e-6:
        sd = np.array([1.0, 0.0, 0.0])
    sd = sd / np.linalg.norm(sd)
    P[pk] = W + fwd * 0.105 + sd * 0.022
    P[ix] = W + fwd * 0.105 - sd * 0.022
    P[th] = W + fwd * 0.030 + sd * 0.052
    return P


def rigid_about(P, pivot, axis, deg):
    """把**所有**关键点整体绕某轴刚性旋转（不做任何关节弯曲）—— 这就是"木板式倒地"。"""
    t = math.radians(deg)
    k = np.asarray(axis, np.float64)
    k = k / np.linalg.norm(k)
    c, s = math.cos(t), math.sin(t)
    Q = P - np.asarray(pivot, np.float64)
    kx = np.array([[0, -k[2], k[1]], [k[2], 0, -k[0]], [-k[1], k[0], 0]])
    Rot = c * np.eye(3) + s * kx + (1 - c) * np.outer(k, k)
    return Q @ Rot.T + np.asarray(pivot, np.float64)


def bend_upper(P, yaw_deg=0.0, pitch_deg=0.0):
    """上半身（0~22 号点：头/躯干/双臂）绕双髋中点整体扭转 + 前倾。

    🔴 这是"身法"的来源。实测教训：只动手臂、躯干不跟，观感就是"抬手指了个方向"，
    完全不像武打；躯干跟着画圈，"以身为轴"的感觉才出得来（上一版只给 ±14°，视觉上几乎看不出）。
    """
    idx = list(range(0, 23))
    Q = P[idx].copy()
    ty = math.radians(yaw_deg)
    cy, sy = math.cos(ty), math.sin(ty)
    Ry = np.array([[cy, -sy, 0], [sy, cy, 0], [0, 0, 1.0]])
    tx = math.radians(pitch_deg)
    cx_, sx_ = math.cos(tx), math.sin(tx)
    Rx = np.array([[1.0, 0, 0], [0, cx_, -sx_], [0, sx_, cx_]])
    P[idx] = Q @ (Rx @ Ry).T
    return P


def lerp(a, b, u):
    return a + (b - a) * float(u)


def seg(f, a, b):
    """f 在 [a,b] 内映射到 0~1（外推钳位）。"""
    if b <= a:
        return 1.0 if f >= b else 0.0
    return float(np.clip((f - a) / float(b - a), 0.0, 1.0))


def ease_io(u):
    return u * u * (3 - 2 * u)


def ease_out(u):
    return 1 - (1 - u) ** 3


# ─────────────────────────── 编排 ───────────────────────────
# 🔴 这一版是**照着《武林外传》白展堂「葵花点穴手」的剧照拆出来的**（参考图见
#    input/source/dianxue_ref/）。拆解结论（和"凭感觉编"差别很大）：
#      ① 剑指：食指+中指并拢伸出、其余收进掌心   ← 骑砍2 只有 1 根静态手指骨，**做不了**，
#         但**手臂的走势**必须照剑指来（直臂、指尖朝前）
#      ② 出手那条手臂**完全伸直、肘锁死**，肩到指尖一条干净直线
#      ③ 高度**齐肩到齐胸，不举过头**
#      ④ 躯干 **3/4 侧身 + 拧腰**，不是正面平推
#      ⑤ **空着的手低放/略向后** —— 不要和出手的手对称张开
#      ⑥ 脚下**轻弓步/虚步**，双脚不并排，重心偏后脚
#      ⑦ 整体是「**定格亮相**」：contact 帧要 **HOLD 住**，速度感来自前后摇，不是接触帧本身
#    ⭐ 最后一条最关键：**"花式"不是靠绕圈，是靠"出手后定住给人看"**。
W_REST = np.array([0.20, 0.02, -0.12])
W_L_REST = np.array([-0.20, 0.02, -0.12])

W_WIND = np.array([0.24, -0.10, -0.02])     # 蓄势：右手收到腰侧偏后
W_L_WIND = np.array([-0.26, 0.10, 0.04])    # 蓄势：左手抬到腹前
W_STRIKE = np.array([-0.07, 1.07, 0.47])    # 出手：目标设在**可达距离之外** -> IK 拉成直臂；
                                            # 方向抬到让手落在【肩高】（原来落在齐胸，参考剧照要求齐肩）
W_L_STRIKE = np.array([-0.30, -0.22, -0.14])  # 出手/亮相：左手**低放后引**（非对称！）
W_RECOVER = np.array([0.16, 0.14, -0.02])

# 出手那一拍的时间线
F_WIND0, F_WIND1 = 0, 10       # 起手
F_LOW0, F_LOW1 = 10, 18        # 蓄势低身
F_HIT0, F_HIT1 = 18, 24        # 快速出手
F_SNAP = 22                    # 「点」的那一下：先过冲 2 帧再回弹
F_HOLD1 = 44                   # ★ 定格亮相结束（HOLD 0.83 秒）
F_BACK1 = 56                   # 收手结束


def set_stance(P, k):
    """虚步站位：前(右)脚前探、后(左)脚后蹬、双膝微屈、重心偏后。
    k=0 并步站立；k=1 完全虚步。参考图里脚下是"轻弓步"，**双脚不能并排**。"""
    if k <= 0:
        return P
    d = 0.30 * k
    for idx, dy, dz in ((26, 0.85, -0.02), (28, 1.00, 0.0), (30, 0.96, 0.0), (32, 1.12, 0.0)):
        P[idx] = P[idx] + np.array([0.02 * k, d * dy, dz])          # 右腿：膝/踝/跟/趾 前移
    for idx, dy in ((25, -0.40), (27, -0.62), (29, -0.62), (31, -0.70)):
        P[idx] = P[idx] + np.array([-0.02 * k, d * dy, 0.0])        # 左腿：后蹬
    return P


def attacker_pose(f, N):
    P = rest_pose()
    tw, pit, stance = 0.0, 0.0, 0.0
    if f < F_WIND1:                              # 起手：手抬到腰侧
        k = ease_io(seg(f, F_WIND0, F_WIND1))
        W = lerp(W_REST, W_WIND, k)
        WL = lerp(W_L_REST, W_L_WIND, k)
        tw, stance = -8.0 * k, 0.25 * k
    elif f < F_LOW1:                             # 蓄势低身（拧腰、下沉、开步）
        k = ease_io(seg(f, F_LOW0, F_LOW1))
        W = lerp(W_WIND, W_WIND + np.array([0.0, -0.06, 0.0]), k)
        WL = W_L_WIND
        tw, pit, stance = lerp(-8.0, -26.0, k), 6.0 * k, lerp(0.25, 0.85, k)
    elif f < F_HIT1:                             # ★ 快速出手（一条直线）
        k = ease_out(seg(f, F_HIT0, F_HIT1))
        W = lerp(W_WIND, W_STRIKE, k)
        WL = lerp(W_L_WIND, W_L_STRIKE, k)
        tw, pit, stance = lerp(-26.0, 34.0, k), lerp(6.0, 14.0, k), 1.0
        # 「点」的那一下：接触瞬间**过冲**（再往前下 4cm）2 帧，然后回弹到定格位。
        # 参考剧照拆出来的经验：没有这个回弹，动作读起来像"推掌"而不是"点穴"。
        if F_SNAP <= f < F_SNAP + 2:
            W = W + np.array([0.0, 0.045, -0.045])
    elif f < F_SNAP + 4:                          # 回弹落位
        W = W_STRIKE.copy() + np.array([0.0, 0.045, -0.045]) * (1.0 - ease_io(seg(f, F_SNAP + 2, F_SNAP + 4)))
        WL = W_L_STRIKE.copy()
        tw, pit, stance = 34.0, 14.0, 1.0
    elif f < F_HOLD1:                            # ★★ 定格亮相：**完全不动**（"花式"就在这里）
        W, WL = W_STRIKE.copy(), W_L_STRIKE.copy()
        tw, pit, stance = 34.0, 14.0, 1.0
    elif f < F_BACK1:                            # 收手
        k = ease_io(seg(f, F_HOLD1, F_BACK1))
        W = lerp(W_STRIKE, W_RECOVER, k)
        WL = lerp(W_L_STRIKE, W_L_REST, k)
        tw, pit, stance = lerp(34.0, 0.0, k), lerp(14.0, 0.0, k), lerp(1.0, 0.0, k)
    else:                                        # 站定
        W, WL = W_RECOVER.copy(), W_L_REST.copy()
        tw, pit, stance = 0.0, 0.0, 0.0
    set_stance(P, stance)
    P = bend_upper(P, yaw_deg=tw, pitch_deg=pit)
    # 出手手臂：肘朝外上方（不是贴身）；pole 只在不完全伸直时才起作用
    _pole = np.array([1.0, -0.15, 0.55]) if f >= F_HIT0 else np.array([0.75, -0.30, -0.85])
    set_arm(P, "r", W, pole=_pole)
    set_arm(P, "l", WL)
    # 虚步要下沉一点（写在根位移里）
    root_z = -0.055 * stance
    return P, root_z


# ─────────────────────────── 编排：受击者 ───────────────────────────
ANKLE_PIVOT = np.array([0.0, 0.0, -0.88])
LEAN_FREEZE = -9.0
LEAN_END = 90.0
F_V_STILL = 24      # 站立到这一帧
F_V_STIFF = 28      # 瞬间绷直
F_V_HOLD = 40       # 定格到这一帧
F_V_FALL = 62       # 倒到地面


def victim_pose(f, N):
    P = rest_pose()
    if f < F_V_STILL:
        th, k = 0.0, 0.0
    elif f < F_V_STIFF:                          # 被点中瞬间：绷直
        th = lerp(0.0, LEAN_FREEZE, ease_out(seg(f, F_V_STILL, F_V_STIFF)))
        k = ease_out(seg(f, F_V_STILL, F_V_STIFF))
    elif f < F_V_HOLD:                           # 僵直定格
        th, k = LEAN_FREEZE, 1.0
    elif f < F_V_FALL:                           # 木板式前倒（重力加速）
        th = lerp(LEAN_FREEZE, LEAN_END, seg(f, F_V_HOLD, F_V_FALL) ** 1.8)
        k = 1.0
    else:
        th, k = LEAN_END, 1.0
    if k > 0:                                    # 被点中时双臂微微一炸
        set_arm(P, "r", lerp(np.array([0.20, 0.02, -0.12]), np.array([0.34, 0.10, -0.14]), k))
        set_arm(P, "l", lerp(np.array([-0.20, 0.02, -0.12]), np.array([-0.34, 0.10, -0.14]), k))
    P = rigid_about(P, ANKLE_PIVOT, np.array([1.0, 0, 0]), th)
    root_z = 0.88 * (math.cos(math.radians(th)) - 1.0)
    return P, root_z


def dump_pose(path, kind, pts_arm, root_z, fps):
    """把逐帧骨架系关键点写成下游认识的 pose.json（world=MediaPipe，另附 root_z_m）。"""
    T = len(pts_arm)
    world = np.stack([a2m(P) for P in pts_arm])                 # [T,33,3]
    # 粗略图像投影（仅供下游占位；真正的下沉走 root_z_m，不靠它估）
    X, Y, Z = world[:, :, 0], world[:, :, 1], world[:, :, 2]
    nx = np.clip(0.5 - X * 0.62, 0, 1)
    ny = np.clip(0.42 + (-Z) * 0.44, 0, 1)
    norm = np.zeros((T, 33, 4), np.float32)
    norm[:, :, 0], norm[:, :, 1], norm[:, :, 3] = nx, ny, 1.0
    data = {
        "source": "author_dianxue_pair.py（手 K 关键帧编排）", "kind": "author",
        "fps": float(fps), "frames": int(T), "size": [1280, 720],
        "landmark_names": LM_NAMES, "core_indices": CORE,
        "valid": [True] * T, "interp": [False] * T,
        "min_vis_per_frame": [1.0] * T,
        "facing": {"median_yaw_deg": 0.0, "yaw_range_deg": [0.0, 0.0], "max_frame_jump_deg": 0.0,
                   "n_jumps_gt60": 0, "face_vis_mean": 1.0, "likely_front_facing": True,
                   "note": "手 K 编排：角色一律面朝骨架 +Y（= 朝向镜头）"},
        "front_conf": [1.0] * T,
        "root_z_m": [round(float(v), 5) for v in root_z],
        "world": np.round(world, 6).tolist(),
        "norm": np.round(norm, 6).tolist(),
        "axis_note": "由 author_dianxue_pair 合成；root_z_m = 该帧根骨应下沉的米数（倒地下沉）",
    }
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    json.dump(data, open(path, "w", encoding="utf-8"), ensure_ascii=False)


def main():
    ap = argparse.ArgumentParser(description="手 K 关键帧：花式点穴（绕手→双击→定格→木板倒）")
    ap.add_argument("--outdir", required=True)
    ap.add_argument("--name", default="dianxue_auth")
    ap.add_argument("--frames", type=int, default=96, help="总帧数（24fps；96 = 4.0 秒）")
    ap.add_argument("--fps", type=float, default=24.0)
    a = ap.parse_args()

    N = a.frames
    A_pts, A_rz = [], []
    B_pts, B_rz = [], []
    for f in range(N):
        pa, rza = attacker_pose(f, N)
        pb, rzb = victim_pose(f, N)
        A_pts.append(pa)
        A_rz.append(rza)
        B_pts.append(pb)
        B_rz.append(rzb)

    dump_pose(os.path.join(a.outdir, a.name + "__atk.json"), "atk", A_pts, A_rz, a.fps)
    dump_pose(os.path.join(a.outdir, a.name + "__vic.json"), "vic", B_pts, B_rz, a.fps)

    def report(tag, pts, rz):
        sp = [float(np.linalg.norm(P.max(0) - P.min(0))) for P in pts]
        print("  %-10s 帧数%d  人体跨度 首%.2f 中位%.2f 末%.2f   根骨下沉 末%.3f m"
              % (tag, len(pts), sp[0], float(np.median(sp)), sp[-1], rz[-1]))
    print("=" * 70)
    print("  编排: 攻击者 绕手备势(6-26帧) → 蓄势 → 双击(37/46帧) → 收势造型(56帧后)")
    print("        受击者 站立 → 绷直定格(36-40) → 定住(40-48) → 木板式前倒(48-70) → 躺平")
    report("攻击者", A_pts, A_rz)
    report("受击者", B_pts, B_rz)
    print("  输出: %s" % os.path.abspath(a.outdir))
    print("  下一步: python pipeline/run_retarget.py --rig pose_mediapipe --clip %s__atk \\" % a.name)
    print("          --name <名字> --animdir %s --pelvis ground" % a.outdir)
    print("=" * 70)
    print("DONE")


if __name__ == "__main__":
    main()
