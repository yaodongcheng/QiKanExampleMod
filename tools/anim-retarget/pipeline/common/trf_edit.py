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

    # ④ 姿态过渡桥（A 姿态 → B 姿态的缓动 slerp，首末帧逐骨严格等于两端）
    python trf_edit.py blend --a 230_binded_cry.trf --fa 180 --b 230_daodibeibang.trf --fb 1 \
        --len 36 --ease smoothstep --out cry180_to_daodi.trf
    #     ⚠️ blend 只做旋转/位移插值，中段会穿地；随后必须跑
    #     `pipeline/tools/trf_ground_clamp.py` 做逐帧贴地钳制。

    # ⑤ 多段拼接（自动顺排帧号；用来拼 "cry[..180] + 过渡 + daodi[2..]" 验接缝）
    python trf_edit.py concat --in a.trf,b.trf,c.trf --out chain.trf

    # ⑥ 去等待：按【运动弧长】重采样（变化小的段被压缩，两端归零的"死段"不再占帧）
    python trf_edit.py retime --in cry180_to_daodi.trf --len 24 --ease 0.30 --out cry180_to_daodi_fast.trf
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


def motion_arc(t):
    """逐帧"运动量"与累积弧长（旋转最大转角 度 + 根位移 米×100 的合成度量）。"""
    L = len(t.bones[0])
    m = []
    for k in range(1, L):
        ang = max(qangle_deg(t.bones[b][k - 1][1], t.bones[b][k][1]) for b in range(t.bone_count))
        dp = math.dist(t.root_pos[k - 1][1], t.root_pos[k][1]) if k < len(t.root_pos) else 0.0
        m.append(ang + dp * 100.0)
    S = [0.0]
    for x in m:
        S.append(S[-1] + x)
    return m, S


def retime(t, n, ease=0.0):
    """按【运动弧长】把 t 重采样成 n 帧 —— 变化快的段留帧多、变化慢的段被压缩。

    ⇒ 去掉"等待感"（两端/保持段几乎没有位移却占了很多帧）。
    `ease`：0 = 匀速（最不留等待）；1 = 与 smoothstep 同形的缓动；中间值 = 二者插值。
    """
    L = len(t.bones[0])
    m, S = motion_arc(t)
    tot = S[-1]
    if tot <= 1e-9 or n < 2:
        return resample(t, n)
    r = Trf(); r.name = t.name

    def warp(u):
        return (1.0 - ease) * u + ease * (u * u * (3.0 - 2.0 * u))

    idx = []
    j = 1
    for i in range(n):
        u = i / float(n - 1)
        target = warp(u) * tot
        while j < L - 1 and S[j] < target:
            j += 1
        seg = S[j] - S[j - 1]
        s = 0.0 if seg <= 1e-9 else (target - S[j - 1]) / seg
        idx.append((j - 1, j, max(0.0, min(1.0, s))))
    for b in range(t.bone_count):
        out = []
        for i, (a, c, s) in enumerate(idx):
            out.append((1 + i, qslerp(t.bones[b][a][1], t.bones[b][c][1], s)))
        r.bones.append(out)
    if t.root_pos:
        out = []
        for i, (a, c, s) in enumerate(idx):
            pa = t.root_pos[a][1]; pb = t.root_pos[c][1]
            out.append((1 + i, tuple(pa[d] + s * (pb[d] - pa[d]) for d in range(3))))
        r.root_pos = out
    return r


def parse_frame_spec(spec):
    """解析 "1-3,5,7-9" → 帧号集合。"""
    out = set()
    for part in str(spec).split(","):
        part = part.strip()
        if not part:
            continue
        if "-" in part:
            a, b = part.split("-", 1)
            out.update(range(int(a), int(b) + 1))
        else:
            out.add(int(part))
    return out


def pick(t, keep):
    """只保留 keep 里的帧号（原顺序），再顺排 1..N。

    用途：**抽帧** —— 把某段"变化不大/嫌长"的区间按间隔留下几帧、其余丢掉，
    该段播放速度随之变快（帧数少了但仍是 30fps 时间轴）。
    """
    ks = set(keep)
    r = Trf(); r.name = t.name
    for bone in t.bones:
        sel = [(f, q) for (f, q) in bone if f in ks]
        if not sel:
            raise ValueError("pick：该骨一帧都没留下")
        r.bones.append(sel)
    r.root_pos = [(f, p) for (f, p) in t.root_pos if f in ks]
    return renumber(r)


def inplace(t, keep_z=True):
    """把根骨位移的**水平分量归零** ⇒ 角色原地（"原地版"）。

    用途（🔴 双人挂接硬规矩）：**扛人者必须是参考系 = 原地**，只有被扛者带"相对位移"。
    扛人者那条若留着"靠近"的水平位移，实机里他会自己滑一段，与引擎/挂接冲突。
    `keep_z=True` 只清水平 —— 竖直（蹲/起的高度）要保留，那是姿态的一部分。
    """
    r = Trf(); r.name = t.name
    for bone in t.bones:
        r.bones.append(list(bone))
    r.root_pos = [(f, (0.0, 0.0, (p[2] if keep_z else 0.0))) for (f, p) in t.root_pos]
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


# ─────────────────────── ④ 姿态过渡桥（A 姿态 → B 姿态） ───────────────────────

def _ease(name):
    """返回 [0,1]→[0,1] 的缓动函数（供 blend 用）。"""
    if name in ("linear", "lin"):
        return lambda u: u
    if name in ("smoothstep", "smooth"):
        return lambda u: u * u * (3.0 - 2.0 * u)
    if name == "smootherstep":
        return lambda u: u * u * u * (u * (6.0 * u - 15.0) + 10.0)
    if name == "ease_out":
        return lambda u: 1.0 - (1.0 - u) ** 2
    if name == "ease_out3":
        return lambda u: 1.0 - (1.0 - u) ** 3
    if name == "ease_in":
        return lambda u: u * u
    if name == "ease_in3":
        return lambda u: u ** 3
    raise ValueError("未知缓动: " + name)


def blend(ta, fa, tb, fb, n, ease="smoothstep", leg_lag=0.0, root_arc=0.0,
          phase=None):
    """从 ta 的 fa 姿态逐骨 slerp 到 tb 的 fb 姿态，输出 n 帧（1..n）的过渡桥。

    · 旋转：四元数 slerp（短弧），**不做欧拉 lerp**（欧拉 lerp 会让关节路径扭曲/翻转）
    · 根骨位移：按同一缓动线性插值；`root_arc` 额外叠一条 sin 弧，模拟重心起伏（正=中段抬高）
    · `leg_lag`：腿骨(索引 1..8)比躯干晚 `leg_lag` 比例启动 → "上身先倒、腿跟上"的错峰
    · `phase`：可选 [(bone_index, delay)] 覆盖单骨启动延迟
    """
    ia = index_of(ta, fa)
    ib = index_of(tb, fb)
    E = _ease(ease)
    delay = {}
    if phase:
        for b, d in phase:
            delay[b] = d
    r = Trf(); r.name = ta.name
    for bi, bone in enumerate(ta.bones):
        qa = bone[ia][1]
        qb = tb.bones[bi][ib][1]
        lag = delay.get(bi, leg_lag if 1 <= bi <= 8 else 0.0)
        lag = max(0.0, min(0.95, lag))
        out = []
        for k in range(n):
            u = k / float(n - 1) if n > 1 else 1.0
            uu = 0.0 if u <= lag else (u - lag) / (1.0 - lag)
            out.append((1 + k, qslerp(qa, qb, E(uu))))
        r.bones.append(out)
    pa = ta.root_pos[ia][1] if ia < len(ta.root_pos) else (0.0, 0.0, 0.0)
    pb = tb.root_pos[ib][1] if ib < len(tb.root_pos) else (0.0, 0.0, 0.0)
    out = []
    for k in range(n):
        u = k / float(n - 1) if n > 1 else 1.0
        s = E(u)
        z = pa[2] + (pb[2] - pa[2]) * s + root_arc * math.sin(math.pi * u)
        out.append((1 + k, (pa[0] + (pb[0] - pa[0]) * s,
                            pa[1] + (pb[1] - pa[1]) * s, z)))
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

    bl = sub.add_parser("blend", help="A 姿态 → B 姿态的缓动过渡桥")
    bl.add_argument("--a", dest="a", required=True, help="起始 TRF")
    bl.add_argument("--fa", type=int, required=True, help="起始帧")
    bl.add_argument("--b", dest="b", required=True, help="目标 TRF")
    bl.add_argument("--fb", type=int, required=True, help="目标帧")
    bl.add_argument("--len", type=int, required=True, help="输出帧数")
    bl.add_argument("--ease", default="smoothstep",
                    help="linear|smoothstep|smootherstep|ease_out|ease_out3|ease_in|ease_in3")
    bl.add_argument("--leg-lag", type=float, default=0.0, help="腿比躯干晚启动的比例 0~0.9")
    bl.add_argument("--root-arc", type=float, default=0.0, help="根骨中段额外抬降(米)")
    bl.add_argument("--out", required=True); bl.add_argument("--name")

    cc = sub.add_parser("concat", help="多段首尾相接（自动顺排帧号）")
    cc.add_argument("--in", dest="inp", required=True, help="逗号分隔的多个 TRF")
    cc.add_argument("--out", required=True); cc.add_argument("--name")

    rt = sub.add_parser("retime", help="按运动弧长重采样（去掉不动的等待帧）")
    rt.add_argument("--in", dest="inp", required=True)
    rt.add_argument("--len", type=int, required=True, help="输出帧数")
    rt.add_argument("--ease", type=float, default=0.0, help="0=匀速(最紧凑) 1=smoothstep 缓动")
    rt.add_argument("--out", required=True); rt.add_argument("--name")

    pk = sub.add_parser("pick", help="按帧号白名单裁剪（抽帧：留几帧丢几帧）")
    pk.add_argument("--in", dest="inp", required=True)
    pk.add_argument("--keep", required=True, help='帧号，如 "1-3,5,7-9"')
    pk.add_argument("--out", required=True); pk.add_argument("--name")

    ip = sub.add_parser("inplace", help="根骨水平位移归零 → 原地版（扛人者=参考系用）")
    ip.add_argument("--in", dest="inp", required=True)
    ip.add_argument("--zero-z", action="store_true", help="连竖直也归零（默认只清水平）")
    ip.add_argument("--out", required=True); ip.add_argument("--name")

    a = ap.parse_args()
    if a.op == "cut":
        t = cut(read_trf(a.inp), a.f0, a.f1)
    elif a.op == "hold":
        t = hold_at(read_trf(a.inp), a.frame, a.len)
    elif a.op == "reverse":
        t = reverse(read_trf(a.inp))
    elif a.op == "blend":
        t = blend(read_trf(a.a), a.fa, read_trf(a.b), a.fb, a.len,
                  ease=a.ease, leg_lag=a.leg_lag, root_arc=a.root_arc)
    elif a.op == "concat":
        parts = [read_trf(p) for p in a.inp.split(",")]
        t = concat(parts)
    elif a.op == "retime":
        t = retime(read_trf(a.inp), a.len, ease=a.ease)
    elif a.op == "pick":
        t = pick(read_trf(a.inp), parse_frame_spec(a.keep))
    elif a.op == "inplace":
        t = inplace(read_trf(a.inp), keep_z=not a.zero_z)
    else:
        t = layer(read_trf(a.peak), a.frame, read_trf(a.motion), a.len,
                  scale=a.scale, mask_legs=not a.no_mask_legs)
    t.name = a.name or os.path.splitext(os.path.basename(a.out))[0]
    write_trf(t, a.out)
    print("写出 %s（动画名=%s，%d 帧，范围 %d..%d）"
          % (a.out, t.name, len(t.bones[0]), t.bones[0][0][0], t.bones[0][-1][0]))


if __name__ == "__main__":
    _main()
