#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""从项目自有素材拼出「螺旋手里剑手势」的三阶段动作（start / loop / end）。

══ 思路（为什么不是"逐骨线性插值"）══════════════════════════════════════
参考图给的是一个**目标姿态**。要"自然地"走到它，靠插关节角度是做不到的 ——
肩胛带的旋转、脊柱的配合、重心的转移都不在插值里。所以本脚本全程只搬**真实动作**：

  · 起手 / 收招：`ue_BarrierSpell`（FCS 屏障族出手，蒙太奇 M_MagicAttack_RH_Up）
      实测它的**首帧逐骨 == MagicIdle（施法待机）**（差 0.0°），末帧也几乎一致（max 6.8°）
      ⇒ 它本身就是一条"从 idle 起、回到 idle"的真实动作，天然满足三阶段的两端要求。
      本次只用 FK 客观量了一件事：**右手举过头顶最高的那一帧**（= 目标姿态帧）。

  · 循环段：不做"定格不动"，而是把 `ue_MagicIdle`（施法待机的真实呼吸）的
      **相对运动** `M(t) ∘ M(0)⁻¹` 当作姿态层，用 sin² 窗叠到峰值姿态上（见 common/trf_edit.py）。
      窗在两端压到 0 ⇒ 首末帧逐骨严格等于峰值姿态 ⇒ 与 start 末帧 / end 首帧**零跳变**，
      自身也完美闭环；膝盖以下不打层 ⇒ 脚不滑。

══ 产出 ══════════════════════════════════════════════════════════════
  output/trf/ue_SpiralHand_Start.trf   起手（idle → 手势），真 mocap 切段
  output/trf/ue_SpiralHand_Loop.trf    循环（手势保持 + 真实呼吸），完美闭环
  output/trf/ue_SpiralHand_End.trf     收招（手势 → idle），真 mocap 切段

用法
  python pipeline/tools/build_spiral_hand.py [--dry] [--peak 40] [--loop-len 60] [--loop-scale 0.9]
"""
import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, os.path.join(ROOT, "pipeline", "common"))
sys.path.insert(0, HERE)

from trf_edit import (cut, hold_at, layer, index_of, frames_of,          # noqa: E402
                     rel_layer, slerp_tail, concat, renumber)
from trf_compose import read_trf, write_trf, qangle_deg                   # noqa: E402
from scan_pose_metrics import build, fk                                   # noqa: E402
import numpy as np                                                        # noqa: E402

BASE = "ue_BarrierSpell"      # 真 mocap：idle → 右手上举 → idle
BREATH = "ue_MagicIdle"       # 真呼吸：循环段的姿态层来源
CAST_SRC = "ue_ProjectileSpell"  # 真 mocap：投掷手势（FCS M_Projectile）—— cast 分支的运动来源
OUTS = {"start": "ue_SpiralHand_Start", "loop": "ue_SpiralHand_Loop",
        "end": "ue_SpiralHand_End", "cast": "ue_SpiralHand_Cast"}


def hand_above_head(skel, rl, invq, names, rots, fi):
    """第 fi 帧：右手（指尖）比头顶高多少（米）。"""
    quats = {names[bi]: rots[bi][fi][1] for bi in range(len(names))}
    p = fk(skel, rl, invq, quats, None, [names[0]], ["r_hand", "l_hand", "head", "pelvis", "r_toe0", "l_toe0"])
    return (float(p["r_hand"][1][2] - p["head"][1][2]),
            float(p["l_hand"][1][2] - p["head"][1][2]),
            float(min(p["r_toe0"][0][2], p["l_toe0"][0][2])),
            float(p["pelvis"][0][2]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--trfdir", default=os.path.join(ROOT, "output", "trf"), help="输入 TRF 目录")
    ap.add_argument("--outdir", default=None, help="输出目录（缺省 = --trfdir）")
    ap.add_argument("--skel-json", default=os.path.join(ROOT, "output", "verify", "bannerlord_skel.json"))
    ap.add_argument("--skel-fbx", default=os.path.join(ROOT, "input/target/bannerlord/human_lod_4.fbx"))
    ap.add_argument("--peak", type=int, default=0, help="0 = 自动按【右手过顶最高】选帧")
    ap.add_argument("--peak-lo", type=int, default=20, help="自动选峰值的搜索下界（帧）")
    ap.add_argument("--peak-hi", type=int, default=60, help="自动选峰值的搜索上界（帧）")
    ap.add_argument("--start-f0", type=int, default=0, help="0 = 用 BarrierSpell 的首帧")
    ap.add_argument("--end-f1", type=int, default=0, help="0 = 用 BarrierSpell 的末帧")
    ap.add_argument("--loop-len", type=int, default=60, help="循环段帧数（30fps 下 60 帧 = 2.0s）")
    ap.add_argument("--loop-scale", type=float, default=0.9, help="呼吸层幅度缩放")
    ap.add_argument("--cast", action="store_true", default=True,
                    help="额外产出 cast 分支（= end 的第二个出口：把手里的东西丢出去）")
    ap.add_argument("--no-cast", dest="cast", action="store_false")
    ap.add_argument("--cast-src", default=CAST_SRC, help="cast 分支的运动来源 TRF 名")
    ap.add_argument("--cast-f0", type=int, default=18, help="源里的起帧（锚点，该帧相对量=0）")
    ap.add_argument("--cast-f1", type=int, default=46, help="源里的止帧")
    ap.add_argument("--cast-tail", type=int, default=8, help="最后 N 帧 slerp 落回 idle")
    ap.add_argument("--cast-mask-legs", action="store_true", default=True,
                    help="cast 只搬上半身（膝盖以下保持保持姿态，防脚滑）")
    ap.add_argument("--cast-no-mask-legs", dest="cast_mask_legs", action="store_false")
    ap.add_argument("--cast-mode", choices=["hybrid", "rel"], default="hybrid",
                    help="hybrid = 短过渡 + 源动作【原样播放】(推荐，投掷是真 mocap)；"
                         "rel = 把源的【相对运动】搬到手势姿态上（实测会把手的位置轨迹搞乱，仅留对照）")
    ap.add_argument("--cast-hold", type=int, default=3, help="hybrid：开头保持手势的帧数")
    ap.add_argument("--cast-blend", type=int, default=5, help="hybrid：过渡到源起帧的帧数（slerp）")
    ap.add_argument("--cast-join", type=int, default=16, help="hybrid：接入源动作的帧（引臂帧）")
    ap.add_argument("--cast-end", type=int, default=60, help="hybrid：源动作播到哪帧为止")
    ap.add_argument("--cast-scale", type=float, default=1.0, help="cast 层整体幅度")
    ap.add_argument("--cast-root-scale", type=float, default=0.0,
                    help="cast 的 pelvis 相对旋转（0 = 摘掉，防整个人翻掉；见 trf_edit.rel_layer）")
    ap.add_argument("--ground", type=float, default=None,
                    help="落地补偿：把整段沿世界 -Z 平移这么多米（脚底 ≈0）。"
                         "⚠️ 源件自带本项目已知的【导出浮空】bug（脚底 ~+0.11 m，见 README §0.5），"
                         "默认**不做**补偿 —— 因为同族的 MagicIdle/施法集都一样浮空，"
                         "单独把这三条压到地面反而会和 idle 对不上。要压就传本参数。")
    ap.add_argument("--dry", action="store_true")
    a = ap.parse_args()
    if not a.outdir:
        a.outdir = a.trfdir
    os.makedirs(a.outdir, exist_ok=True)

    base = read_trf(os.path.join(a.trfdir, BASE + ".trf"))
    breath = read_trf(os.path.join(a.trfdir, BREATH + ".trf"))
    fs = frames_of(base); L = len(fs)
    f0 = a.start_f0 or fs[0]
    f1 = a.end_f1 or fs[-1]

    # ---- 选峰值帧：右手过顶最高 ----
    skel = json.load(open(a.skel_json, encoding="utf-8"))
    bones, rl, invq = build(skel)
    names = [b["name"] for b in bones]
    if len(names) != base.bone_count:
        sys.exit("骨架 %d 骨 / TRF %d 骨 不符" % (len(names), base.bone_count))
    best_f, best_v = None, None
    for fi in range(L):
        fr = fs[fi]
        if not (a.peak_lo <= fr <= a.peak_hi):
            continue
        v = hand_above_head(skel, rl, invq, names, base.bones, fi)
        if best_v is None or v[0] > best_v[0]:
            best_f, best_v = fr, v
    peak = a.peak or best_f
    ip = index_of(base, peak)
    pv = hand_above_head(skel, rl, invq, names, base.bones, ip)
    print("★ 目标姿态帧 = %d   右手过顶 %+.3f m ｜ 左手过顶 %+.3f m ｜ 脚底 %.3f ｜ 骨盆高 %.3f"
          % (peak, pv[0], pv[1], pv[2], pv[3]))
    print("  起手段 = 帧 %d..%d（%d 帧）  收招段 = 帧 %d..%d（%d 帧）"
          % (f0, peak, peak - f0 + 1, peak, f1, f1 - peak + 1))

    if a.dry:
        print("--dry：不落盘"); return

    # ---- 三段 ----
    t_start = cut(base, f0, peak); t_start.name = OUTS["start"]
    t_end = cut(base, peak, f1);   t_end.name = OUTS["end"]
    t_loop = layer(base, peak, breath, a.loop_len, scale=a.loop_scale,
                   start=1, mask_legs=True, bones=names)
    t_loop.name = OUTS["loop"]

    # ---- 可选：落地补偿（沿世界 -Z 平移；写进根骨的"纯增量"位置轨）----
    if a.ground:
        _t_all = [t_start, t_loop, t_end] + ([t_cast] if t_cast is not None else [])
        # 🔴 TRF 位置轨存的就已经是【世界（骨架）空间】的纯增量 p = rest_rot @ loc
        #    （见 docs/TRF规范.md / trf_to_fbx.py：还原时 loc = rest3⁻¹ ∘ p）。
        #    所以落地补偿直接在世界 Z 上减，**不要再过一次 rest3⁻¹**（过两次会平移错轴）。
        dl = np.array([0.0, 0.0, -abs(a.ground)])
        for t in _t_all:
            t.root_pos = [(f, tuple(np.array(p) + dl)) for (f, p) in t.root_pos]
        print("落地补偿：沿世界 Z 平移 %.3f m（写进根骨位置轨）" % -abs(a.ground))

    t_cast = None
    if a.cast:
        proj = read_trf(os.path.join(a.trfdir, a.cast_src + ".trf"))
        if a.cast_mode == "rel":
            t_cast = rel_layer(base, peak, proj, a.cast_f0, a.cast_f1,
                               start=1, mask_legs=a.cast_mask_legs, bones=names,
                               root_scale=a.cast_root_scale, scale=a.cast_scale)
            t_cast = slerp_tail(t_cast, base, f1, a.cast_tail)
        else:
            # hybrid：① 保持手势 N 帧 → ② slerp 过渡到【源引臂帧】→ ③ 源动作从引臂帧**原样播完**
            #   好处：投掷本体 100% 是真 mocap（手的位置轨迹就是源里的那条），
            #         过渡段只有 `cast-blend` 帧（默认 5 帧 = 0.17s），且"从高举落回引臂"本就是自然的蓄力动作。
            seg_hold = hold_at(base, peak, a.cast_hold, start=1)
            seg_blend = slerp_tail(hold_at(base, peak, max(2, a.cast_blend), start=1),
                                   proj, a.cast_join, max(2, a.cast_blend))
            seg_throw = cut(proj, a.cast_join, a.cast_end)
            t_cast = concat([seg_hold, seg_blend, seg_throw])
            # 末段再压一次，保证末帧**严格等于 idle**（源末帧与 BarrierSpell 末帧本就有几度差）
            t_cast = slerp_tail(t_cast, base, f1, a.cast_tail)
        t_cast.name = OUTS["cast"]

    outs = [(t_start, OUTS["start"]), (t_loop, OUTS["loop"]), (t_end, OUTS["end"])]
    if t_cast is not None:
        outs.append((t_cast, OUTS["cast"]))
    for t, nm in outs:
        p = os.path.join(a.outdir, nm + ".trf")
        write_trf(t, p)
        print("  写出 %-38s %3d 帧  %d..%d" % (os.path.basename(p), len(t.bones[0]),
                                              t.bones[0][0][0], t.bones[0][-1][0]))

    # ---- 接缝自检（逐骨角度）----
    def seam(A, ia, B, ib, lbl):
        worst = max((qangle_deg(A.bones[k][ia][1], B.bones[k][ib][1]), names[k]) for k in range(A.bone_count))
        print("  接缝 %-34s max %6.2f°  (%s)" % (lbl, worst[0], worst[1]))
    nL = len(t_loop.bones[0])
    seam(t_start, len(t_start.bones[0]) - 1, t_loop, 0, "start末 ↔ loop首")
    seam(t_loop, nL - 1, t_loop, 0, "loop末 ↔ loop首（闭环）")
    seam(t_loop, 0, t_end, 0, "loop首 ↔ end首")
    seam(t_end, len(t_end.bones[0]) - 1, base, L - 1, "end末 ↔ BarrierSpell末（=idle）")
    if t_cast is not None:
        seam(t_cast, 0, t_loop, 0, "cast首 ↔ loop首（= 手势保持姿态）")
        seam(t_cast, len(t_cast.bones[0]) - 1, base, L - 1, "cast末 ↔ BarrierSpell末（=idle）")

    # ---- 逐段体检：右手过顶 / 脚底高度 ----
    root_i0 = names.index("pelvis")
    R3r = np.array(bones[root_i0]["rest_local"], dtype=float)[:3, :3]
    R3ri = np.linalg.inv(R3r)

    def audit(t, nm):
        vals = []
        for fi in range(len(t.bones[0])):
            quats = {names[bi]: t.bones[bi][fi][1] for bi in range(t.bone_count)}
            rloc = R3ri @ np.array(t.root_pos[fi][1]) if t.root_pos else None
            p = fk(skel, rl, invq, quats, rloc, ["pelvis"],
                   ["r_hand", "l_hand", "head", "pelvis", "r_toe0", "l_toe0"])
            vals.append((p["r_hand"][1][2] - p["head"][1][2], min(p["r_toe0"][0][2], p["l_toe0"][0][2])))
        print("  %-24s 右手过顶 %+.3f..%+.3f m ｜ 脚底(toe) %+.3f..%+.3f m"
              % (nm, min(v[0] for v in vals), max(v[0] for v in vals),
                 min(v[1] for v in vals), max(v[1] for v in vals)))
    print("体检：")
    for t, nm in outs:
        audit(t, nm)
    print("DONE")


if __name__ == "__main__":
    main()
