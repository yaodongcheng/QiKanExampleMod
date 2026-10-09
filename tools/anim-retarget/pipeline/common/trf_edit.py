#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""trf_edit.py —— TRF 剪辑手术刀：切段 / 定格 / 重采样 / **窗口化姿态层**。

为什么需要
    合成一条"从 idle → 某个姿态 → 回 idle"的三阶段动作时，最忌讳的是**逐骨线性插值**：
    关节不会同步地线性转，插出来的中间帧一定僵、一定不像人。
    正确做法有两条路，本脚本给的是第 ② 条：
      ① 直接搬一段**真实动作**（起手/收招各自用真 mocap 切段）—— 见 `cut`
      ② 把一段**真实动作的"相对运动"**（`M(t) ∘ M(0)⁻¹`）当作"姿态层"叠到目标姿态上 —— 见 `layer`
         · 相对运动是**真人的运动学**（肩胛/脊柱/腕的配合都在里面），不是插值出来的
         · 用 sin² 窗把层在两端压到 0 ⇒ **首末帧逐骨等于目标姿态**，跨 clip 拼接零跳变、自身完美闭环
         · 膝盖以下默认不打层 ⇒ 脚不会滑

口径（与 pipeline/common/trf_compose.py 完全一致）
    旋转：TRF 存**绝对局部变换**；层的合成就一句四元数乘法  result(t) = base(t) ∘ delta(t)
    平移：TRF 存**相对静止的纯增量**（只有根骨有）；层默认不动它

用法
    # ① 切段（真 mocap 起手/收招）
    python trf_edit.py cut  --in ue_BarrierSpell.trf --f0 2 --f1 40 --out spiral_hand_start.trf

    # ② 定格（把某一帧的姿态撑成 N 帧，用来做"姿态格"或 loop 基座）
    python trf_edit.py hold --in ue_BarrierSpell.trf --frame 40 --len 60 --out peak60.trf

    # ③ 窗口化姿态层（loop = 峰值姿态 ∘ 施法待机的相对呼吸）
    python trf_edit.py layer --peak ue_BarrierSpell.trf --frame 40 \
        --motion ue_MagicIdle.trf --len 60 --scale 0.85 --out spiral_hand_loop.trf
"""
import argparse
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from trf_compose import (Trf, read_trf, write_trf, qmul, qconj, qnormalize,
                         qslerp_identity, qangle_deg)  # noqa: E402

# 默认"不打层"的骨骼（按骑砍2 28 骨序的名字）—— 膝盖以下 + 骨盆：动它们脚会滑
LEG_BONES = {"pelvis", "l_thigh", "l_calf", "l_foot", "l_toe0",
             "r_thigh", "r_calf", "r_foot", "r_toe0"}


def qslerp(a, b, s):
    """四元数球面插值（走短弧）。"""
    a = qnormalize(a); b = qnormalize(b)
    d = sum(x * y for x, y in zip(a, b))
    if d < 0.0:
        b = tuple(-x for x in b); d = -d
    if d > 0.9995:
        return qnormalize(tuple(a[i] + s * (b[i] - a[i]) for i in range(4)))
    th0 = math.acos(max(-1.0, min(1.0, d)))
    th1 = th0 * s
    s0 = math.sin(th0 - th1) / math.sin(th0)
    s1 = math.sin(th1) / math.sin(th0)
    return qnormalize(tuple(a[i] * s0 + b[i] * s1 for i in range(4)))


def frames_of(t, b=0):
    return [f for f, _q in t.bones[b]]


def index_of(t, frame, b=0):
    for i, (f, _q) in enumerate(t.bones[b]):
        if f == frame:
            return i
    raise KeyError("TRF 里没有帧 %d（范围 %d~%d）" % (frame, t.bones[b][0][0], t.bones[b][-1][0]))


# ─────────────────────────────── ① 切段 ───────────────────────────────

def cut(t, f0, f1):
    """保留 [f0, f1] 的帧（闭区间）。"""
    r = Trf(); r.name = t.name
    for bone in t.bones:
        sel = [(f, q) for (f, q) in bone if f0 <= f <= f1]
        if not sel:
            raise ValueError("切段 [%d,%d] 在该骨上没取到帧" % (f0, f1))
        r.bones.append(sel)
    r.root_pos = [(f, p) for (f, p) in t.root_pos if f0 <= f <= f1]
    return r


# ─────────────────────────────── ② 定格 ───────────────────────────────

def hold_at(t, frame, n, start=1):
    """把 t 在 `frame` 处的姿态撑成 n 帧（帧号 start..start+n-1）。"""
    i = index_of(t, frame)
    r = Trf(); r.name = t.name
    for bone in t.bones:
        q = bone[i][1]
        r.bones.append([(start + k, q) for k in range(n)])
    p = t.root_pos[i][1] if i < len(t.root_pos) else (0.0, 0.0, 0.0)
    r.root_pos = [(start + k, p) for k in range(n)]
    return r


# ─────────────────────────────── 重采样 ───────────────────────────────

def resample(t, n, start=1):
    """按【帧号线性时间轴】把 t 重采样成 n 帧（奇数端点保留）。"""
    fs = frames_of(t)
    L = len(fs)
    r = Trf(); r.name = t.name
    for bone in t.bones:
        out = []
        for k in range(n):
            x = (L - 1) * (k / float(n - 1)) if n > 1 else 0.0
            i0 = int(math.floor(x)); i1 = min(i0 + 1, L - 1)
            s = x - i0
            q = bone[i0][1] if i0 == i1 else qslerp(bone[i0][1], bone[i1][1], s)
            out.append((start + k, q))
        r.bones.append(out)
    if t.root_pos:
        out = []
        for k in range(n):
            x = (L - 1) * (k / float(n - 1)) if n > 1 else 0.0
            i0 = int(math.floor(x)); i1 = min(i0 + 1, L - 1)
            s = x - i0
            p0, p1 = t.root_pos[i0][1], t.root_pos[i1][1]
            out.append((start + k, tuple(p0[j] + s * (p1[j] - p0[j]) for j in range(3))))
        r.root_pos = out
    return r


# ─────────────────── ③b 相对运动层（起点锚定，无窗）───────────────────

def rel_layer(peak, peak_frame, motion, f0, f1, start=1, mask_legs=False, bones=None,
              root_scale=1.0, scale=1.0, root_index=0):
    """result(t) = peak(peak_frame) ∘ [ motion(f) ∘ motion(f0)⁻¹ ]，f = f0..f1（逐帧）。

    与 `layer` 的区别：**不做窗口** —— 相对运动以 motion(f0) 为锚点（该帧相对量天然为 0），
    所以**首帧逐骨严格等于 peak**，可以直接接在 hold / loop 后面；
    末帧落在 peak ∘ rel(f1)（不是 idle）。

    root_scale：根骨（默认 0 号 = pelvis）那道相对旋转缩放多少。1 = 原样；**0 = 摘掉**。
       🔴 整身动作（投掷/冲刺）的 pelvis 相对旋转能到 90°+，直接乘进目标姿态会让**整个人翻掉**
       （实测：头动幅 0.94 m、角色空中翻滚）。只搬上半身时必须 `root_scale=0` + `mask_legs=True`。
    scale：整条层的幅度缩放（1 = 原样）。
    ⚠️ 结果好不好看**必须渲染看**，不要只盯数字下结论。
    """
    i_peak = index_of(peak, peak_frame)
    i0 = index_of(motion, f0)
    i1 = index_of(motion, f1)
    span = i1 - i0 + 1
    r = Trf(); r.name = peak.name
    for bi, bone in enumerate(peak.bones):
        q_peak = bone[i_peak][1]
        if mask_legs:
            skip = (bones[bi] in LEG_BONES) if (bones and bi < len(bones)) else (bi <= 8)
        else:
            skip = False
        if skip:
            r.bones.append([(start + k, q_peak) for k in range(span)])
            continue
        q0 = qnormalize(motion.bones[bi][i0][1])
        out = []
        for k in range(span):
            rel = qmul(qnormalize(motion.bones[bi][i0 + k][1]), qconj(q0))
            if scale < 0.999:
                rel = qslerp_identity(rel, max(0.0, scale))
            if bi == root_index and root_scale < 0.999:
                # 🔴 根骨（pelvis）那道相对旋转**必须能摘掉**：把它乘进 hold 姿态 = 整个人被转掉，
                #    实测不加这个开关会直接翻跟头（头动幅 0.94 m）。同 trf_compose.py 的 --root-scale。
                rel = qslerp_identity(rel, max(0.0, root_scale))
            out.append((start + k, qnormalize(qmul(q_peak, rel))))
        r.bones.append(out)
    p0 = peak.root_pos[i_peak][1] if i_peak < len(peak.root_pos) else (0.0, 0.0, 0.0)
    r.root_pos = [(start + k, p0) for k in range(span)]
    return r


def renumber(t, start=1):
    """把帧号顺排成 start..start+N-1（拼接前必做：TRF 帧号直接映射到时间，留空档=时间被拉伸）。"""
    r = Trf(); r.name = t.name
    for bone in t.bones:
        r.bones.append([(start + k, q) for k, (_f, q) in enumerate(bone)])
    r.root_pos = [(start + k, p) for k, (_f, p) in enumerate(t.root_pos)]
    return r


def reverse(t):
    """倒放：把每根骨的旋转帧与根骨位移帧**整体反转**，再顺排帧号。
    语义正确性：TRF 存的是【绝对局部旋转】，倒放 = 按时间反序播放同一批绝对姿态，无需取逆。"""
    r = Trf(); r.name = t.name
    for bone in t.bones:
        r.bones.append(list(reversed(bone)))
    r.root_pos = list(reversed(t.root_pos))
    return renumber(r)


def concat(parts):
    """把若干段首尾相接（自动顺排帧号）。各段骨数必须一致。"""
    r = Trf(); r.name = parts[0].name
    r.bones = [[] for _ in parts[0].bones]
    r.root_pos = []
    f = 1
    for t in parts:
        rr = renumber(t, f)
        for bi, bone in enumerate(rr.bones):
            r.bones[bi].extend(bone)
        r.root_pos.extend(rr.root_pos)
        f += len(rr.bones[0])
    return r


def slerp_tail(t, target, target_frame, n):
    """把 t 的**最后 n 帧**逐骨 slerp 到 target 在 target_frame 的姿态（末帧严格等于目标）。

    用途：让"搬过来的相对运动"最后**落回 idle** —— 否则末帧停在 peak∘rel 上，接不回 idle。
    """
    if n < 2:
        return t
    it = index_of(target, target_frame)
    L = len(t.bones[0])
    n = min(n, L)
    r = Trf(); r.name = t.name
    for bi, bone in enumerate(t.bones):
        q_t = target.bones[bi][it][1]
        out = []
        for k in range(L):
            if k < L - n:
                out.append(bone[k])
            else:
                s = (k - (L - n)) / float(n - 1)
                out.append((bone[k][0], qslerp(bone[k][1], q_t, s)))
        r.bones.append(out)
    if t.root_pos:
        pt = target.root_pos[it][1] if it < len(target.root_pos) else (0.0, 0.0, 0.0)
        out = []
        for k in range(L):
            if k < L - n:
                out.append(t.root_pos[k])
            else:
                s = (k - (L - n)) / float(n - 1)
                p = t.root_pos[k][1]
                out.append((t.root_pos[k][0], tuple(p[j] + s * (pt[j] - p[j]) for j in range(3))))
        r.root_pos = out
    return r


# ─────────────────────────── ③ 窗口化姿态层 ───────────────────────────

def window(n, kind="sin2"):
    """两端为 0 的窗（sin²：中点 =1）。"""
    if n == 1:
        return [0.0]
    out = []
    for k in range(n):
        u = k / float(n - 1)
        out.append(math.sin(math.pi * u) ** 2 if kind == "sin2" else (1.0 - math.cos(2 * math.pi * u)) / 2.0)
    return out


def layer(peak, peak_frame, motion, n, scale=1.0, start=1, mask_legs=True,
          bones=None, win="sin2", label=""):
    """result(t) = peak(peak_frame) ∘ slerp(I, [M(t) ∘ M(0)⁻¹], scale·w(t))

    · motion 先重采样到 n 帧；相对运动 rel(t) = M(t) ∘ M(0)⁻¹ —— 这是**真实运动**，不是插值
    · w(t) = sin² 窗 ⇒ t=0 与 t=n-1 处 rel 权重为 0 ⇒ 首末帧逐骨 **严格等于** peak
    · mask_legs=True：骨盆与膝盖以下不打层（防脚滑、防整体位移被牵动）
    """
    i_peak = index_of(peak, peak_frame)
    M = resample(motion, n, start=start)
    w = window(n, win)
    names = bones
    r = Trf(); r.name = peak.name
    for bi, bone in enumerate(peak.bones):
        q_peak = bone[i_peak][1]
        # 是否给这根骨打层：mask_legs 时跳过"骨盆 + 膝盖以下"
        if names is not None:
            skip = names[bi] in LEG_BONES
        else:
            skip = bi <= 8              # 骑砍2 28 骨序：0 骨盆 / 1~4 左腿 / 5~8 右腿
        apply = not (mask_legs and skip)
        out = []
        for k in range(n):
            if apply:
                rel = qmul(qnormalize(M.bones[bi][k][1]), qconj(qnormalize(motion.bones[bi][0][1])))
                q = qmul(q_peak, qslerp_identity(rel, scale * w[k]))
            else:
                q = q_peak
            out.append((start + k, qnormalize(q)))
        r.bones.append(out)
    # 位移：保持峰值那条（默认不动，防"整体走位"）
    p = peak.root_pos[i_peak][1] if i_peak < len(peak.root_pos) else (0.0, 0.0, 0.0)
    r.root_pos = [(start + k, p) for k in range(n)]
    return r


# ─────────────────────────────── CLI ───────────────────────────────

def _main():
    ap = argparse.ArgumentParser(description="TRF 剪辑手术刀")
    sub = ap.add_subparsers(dest="op", required=True)

    c = sub.add_parser("cut"); c.add_argument("--in", dest="inp", required=True)
    c.add_argument("--f0", type=int, required=True); c.add_argument("--f1", type=int, required=True)
    c.add_argument("--out", required=True); c.add_argument("--name")

    h = sub.add_parser("hold"); h.add_argument("--in", dest="inp", required=True)
    h.add_argument("--frame", type=int, required=True); h.add_argument("--len", type=int, required=True)
    h.add_argument("--out", required=True); h.add_argument("--name")

    rv = sub.add_parser("reverse"); rv.add_argument("--in", dest="inp", required=True)
    rv.add_argument("--out", required=True); rv.add_argument("--name")

    l = sub.add_parser("layer"); l.add_argument("--peak", required=True)
    l.add_argument("--frame", type=int, required=True); l.add_argument("--motion", required=True)
    l.add_argument("--len", type=int, required=True); l.add_argument("--scale", type=float, default=1.0)
    l.add_argument("--no-mask-legs", action="store_true")
    l.add_argument("--out", required=True); l.add_argument("--name")

    a = ap.parse_args()
    if a.op == "cut":
        t = cut(read_trf(a.inp), a.f0, a.f1)
    elif a.op == "hold":
        t = hold_at(read_trf(a.inp), a.frame, a.len)
    elif a.op == "reverse":
        t = reverse(read_trf(a.inp))
    else:
        t = layer(read_trf(a.peak), a.frame, read_trf(a.motion), a.len,
                  scale=a.scale, mask_legs=not a.no_mask_legs)
    t.name = a.name or os.path.splitext(os.path.basename(a.out))[0]
    write_trf(t, a.out)
    print("写出 %s（动画名=%s，%d 帧，范围 %d..%d）"
          % (a.out, t.name, len(t.bones[0]), t.bones[0][0][0], t.bones[0][-1][0]))


if __name__ == "__main__":
    _main()
