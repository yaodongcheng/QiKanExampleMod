#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""root_z_from_image.py —— 把**倒地下沉**从视频里估出来，写进 TRF 的根骨位置轨 Z。

== 为什么需要它 ==
  本仓的 pose_mediapipe 链路**只搬关节角度、不做全局位移**（`world` 关键点天生是"双髋中心"局部系）。
  这在跑步/挥拳这类"原地动作"上没问题，但**倒地被它直接吃掉**：
  受击者最后一帧明明趴在地上，解出来的动画却是"身体在髋高（≈0.9 m）水平躺着"——**悬在半空**。
  （实测本 case：受击者髋中点从 y=0.493 掉到 0.842、人体像素高从 0.72 塌到 0.08，是真的躺平了。）

  ⇒ 需要一条**单目估高的补丁**：用画面里"髋中点离地高度"反推髋的真实高度，再整体下沉根骨。
     骑砍2 的 AnimationClip 有 `displacement` 槽，但它**只吃水平分量（原版 Z 恒为 0）**
     （见 pipeline/common/trf_root_travel.py），所以下沉**不能**填那儿，只能写进**根骨位置轨** ——
     这也正是本仓 `_ground_trf`（压脚底）走的那条路。

== 怎么估（自标定，不需要相机参数）==
      ground   = 全片所有可见关键点的最大 y（归一化）= 最低点；人倒地时身体贴地 ⇒ 该值就是地面线
      h(f)     = ground − 髋中点y(f)          髋中点离地高度（归一化）
      scale    = stand_h / h(0)               米 / 归一化单位（用"站立时髋高 stand_h"自标定）
      z(f)     = h(f) × scale                 ⇒ dz(f) = z(f) − z(0)，加到根骨位置轨 Z 上
  自标定的好处：不需要知道相机焦距/距离，误差只来自透视（侧视下可接受）。

== 幂等 ==
  在 <trf> 旁写 sidecar `<trf>.rootz.json` 记录这次加的 dz 曲线；重跑时**先减掉上次的再加新的**，
  所以反复跑不会叠加。副作用也很清楚：删掉 sidecar 就等于放弃幂等。

用法
    python pipeline/tools/root_z_from_image.py \
        --pose input/source/dianxue_pair/dianxue_multi__S0_T1_s0.json \
        --trf  output/trf/pose_dianxue_victim.trf --stand-h 0.90
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
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "common"))


def main():
    ap = argparse.ArgumentParser(description="从画面估倒地下沉 -> 写进 TRF 根骨位置轨 Z")
    ap.add_argument("--pose", required=True, help="该身份的 pose.json")
    ap.add_argument("--trf", required=True, help="要改的 .trf（就地改，旁边写 sidecar）")
    ap.add_argument("--stand-h", type=float, default=0.90, help="站立时髋中点离地高度（米）")
    ap.add_argument("--min-hip-drop", type=float, default=0.15,
                    help="髋最高到最低的落差小于该值就不动（防把正常动作误压下去）")
    ap.add_argument("--apply", action="store_true", help="真的写盘（默认只算+打印）")
    a = ap.parse_args()

    from trf_compose import read_trf, write_trf

    P = json.load(open(a.pose, encoding="utf-8"))

    # 🔴 手 K 编排的数据会**直接给出**每帧该下沉多少米（root_z_m）——那就不必从画面估，
    #    直接用（更准，且没有单目估高的透视误差）。
    if P.get("root_z_m"):
        dz = np.asarray(P["root_z_m"], np.float32)
        T = len(dz)
        drop = float(dz[0] - dz.min())
        print("=" * 70)
        print("  身份    : %s（%d 帧）" % (P.get("identity", "?"), T))
        print("  来源    : pose.json 自带的 root_z_m（手 K 编排数据，跳过单目估计）")
        print("  下沉    : 首帧 %.3f m  最低 %.3f m  落差 %.3f m" % (dz[0], dz.min(), drop))
        # 手 K 编排给的 root_z_m 是**显式数据**，不做"倒地/非倒地"的猜测护栏
        # （虚步下沉 0.055 m 是故意的，不该被 0.15 的护栏拦掉）
        if drop < 0.02:
            print("  结论    : 落差 < 0.02 ⇒ 无实质下沉，不动 TRF")
            return
        if drop < a.min_hip_drop:
            print("  提示    : 落差 %.3f < %.2f，但数据是显式 root_z_m ⇒ 照常写入" % (drop, a.min_hip_drop))
        from trf_compose import read_trf, write_trf
        t = read_trf(a.trf)
        n = len(t.root_pos)
        dz_r = np.interp(np.linspace(0, 1, n), np.linspace(0, 1, T), dz)
        side = a.trf + ".rootz.json"
        prev = None
        if os.path.isfile(side):
            prev = np.asarray(json.load(open(side, encoding="utf-8"))["dz"], np.float32)
            if len(prev) != n:
                prev = None
        t.root_pos = [(f, (v[0], v[1],
                           float(v[2]) - (float(prev[i]) if prev is not None else 0.0) + float(dz_r[i])))
                      for i, (f, v) in enumerate(t.root_pos)]
        if not a.apply:
            print("  干跑    : 加 --apply 才写盘")
            print("=" * 70)
            return
        write_trf(t, a.trf)
        json.dump({"dz": [float(x) for x in dz_r], "source": "pose.json:root_z_m",
                   "note": "root_z_from_image 写入，重跑会先减掉这条"},
                  open(side, "w", encoding="utf-8"), ensure_ascii=False)
        print("  写盘    : %s" % a.trf)
        print("=" * 70)
        print("DONE")
        return

    norm = np.asarray(P["norm"], np.float32)          # [T,33,4]
    T = norm.shape[0]
    vis = norm[:, :, 3] > 0.3
    # 地面线 = 全片最低点
    lows = [float(norm[t][vis[t]][:, 1].max()) for t in range(T) if vis[t].sum() >= 4]
    ground = float(np.percentile(lows, 98))
    hip = (norm[:, 23, 1] + norm[:, 24, 1]) / 2.0
    h = ground - hip                                   # 归一化高度
    h0 = float(h[0])
    if h0 <= 1e-6:
        raise SystemExit("首帧髋高异常（%.4f），先看叠加图确认关键点" % h0)
    scale = a.stand_h / h0
    z = h * scale
    dz = z - z[0]
    drop = float(z.max() - z.min())
    print("=" * 70)
    print("  身份    : %s（%d 帧）" % (P.get("identity", "?"), T))
    print("  地面线  : y=%.4f（归一化）  首帧髋高 h0=%.4f  ⇒ scale=%.3f m/unit" %
          (ground, h0, scale))
    print("  髋高度  : 首帧 %.3f m  最低 %.3f m  落差 %.3f m" % (z[0], float(z.min()), drop))
    if drop < a.min_hip_drop:
        print("  结论    : 落差 < --min-hip-drop(%.2f) ⇒ 这不是倒地动作，不动 TRF" % a.min_hip_drop)
        return
    print("  结论    : 判定为倒地 ⇒ 需要把根骨沿世界 Z 下沉最多 %.3f m" % (-drop))

    t = read_trf(a.trf)
    n = len(t.root_pos)
    # 重采样 dz 到 TRF 的帧数（retarget 可能把 24fps 重采样成 30fps）
    src = np.linspace(0.0, 1.0, T)
    dst = np.linspace(0.0, 1.0, n)
    dz_r = np.interp(dst, src, dz)
    # 幂等：先减掉上次写进去的那条曲线
    side = a.trf + ".rootz.json"
    prev = None
    if os.path.isfile(side):
        prev = np.asarray(json.load(open(side, encoding="utf-8"))["dz"], np.float32)
        if len(prev) != n:
            prev = None
    new = []
    for i, (f, v) in enumerate(t.root_pos):
        zz = float(v[2]) - (float(prev[i]) if prev is not None else 0.0) + float(dz_r[i])
        new.append((f, (v[0], v[1], zz)))
    print("  TRF     : %s（%d 帧位置轨；%s）" %
          (os.path.basename(a.trf), n, "已减掉上次的补偿" if prev is not None else "首次"))
    if not a.apply:
        print("  干跑    : 加 --apply 才写盘")
        print("=" * 70)
        return
    t.root_pos = new
    write_trf(t, a.trf)
    json.dump({"dz": [float(x) for x in dz_r], "stand_h": a.stand_h,
               "scale": scale, "ground": ground, "note": "root_z_from_image 写入，重跑会先减掉这条"},
              open(side, "w", encoding="utf-8"), ensure_ascii=False)
    print("  写盘    : %s  (+ sidecar %s)" % (a.trf, os.path.basename(side)))
    print("=" * 70)
    print("DONE")


if __name__ == "__main__":
    main()
