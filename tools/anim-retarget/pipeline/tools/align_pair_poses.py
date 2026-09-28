#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""align_pair_poses.py —— 把成对动画的两份 pose.json **对齐到同一帧区间**（边缘帧复制补齐）。

== 为什么必须有 ==
  成对动画（处决/伏击/点穴）在查看器里是**两条 clip 同时播**，起点必须一致。
  但多人提取里，两人贴得很近时（比如"手已经按在对方后颈上"的开场），
  抹除迭代会分不出第二个人 ⇒ 实测受击者在**开头 5 帧整段漏检**，
  于是它的 pose.json 从 f5 才开始 —— 直接拿去重定向，两条动画就**错开 0.2 秒**，
  在查看器里表现为"攻击方已经动了、受击方还僵着"。

  补法很朴素：**用该身份自己的边缘帧复制补齐**（受击者在这 5 帧里本来就是站定不动的，
  复制首帧在视觉上完全正确）。不引入任何猜测姿态。

用法
    python pipeline/tools/align_pair_poses.py \
        --pose input/source/..._S0_T0_s0.json input/source/..._S0_T1_s0.json
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


def main():
    ap = argparse.ArgumentParser(description="把成对动画的多份 pose.json 对齐到同一帧数")
    ap.add_argument("--pose", nargs="+", required=True, help="要互相对齐的 pose.json（>=2 份）")
    ap.add_argument("--dry", action="store_true", help="只打印不改")
    a = ap.parse_args()

    docs = []
    for p in a.pose:
        d = json.load(open(p, encoding="utf-8"))
        docs.append((p, d, int(d["frames"])))
    nmax = max(n for _, _, n in docs)
    print("=" * 70)
    for p, d, n in docs:
        print("  %-46s %d 帧 (%s)" % (os.path.basename(p), n, d.get("identity", "?")))
    print("  对齐目标: %d 帧（取最长的那条，短的用**自己的边缘帧**复制补齐）" % nmax)

    for p, d, n in docs:
        if n == nmax:
            print("  %-46s 已是最长，不动" % os.path.basename(p))
            continue
        pad = nmax - n
        for key in ("world", "norm"):
            arr = np.asarray(d[key], np.float32)
            head = np.repeat(arr[:1], pad, axis=0)
            d[key] = np.round(np.concatenate([head, arr], axis=0), 6).tolist()
        for key in ("valid", "interp"):
            if key in d:
                d[key] = [bool(d[key][0])] * pad + list(d[key])
        for key in ("min_vis_per_frame", "front_conf"):
            if key in d:
                d[key] = [d[key][0]] * pad + list(d[key])
        for key in ("score",):
            if key in d:
                d[key] = [d[key][0]] * pad + list(d[key])
        if isinstance(d.get("facing"), dict) and "yaw_deg" in d["facing"]:
            y = d["facing"]["yaw_deg"]
            d["facing"]["yaw_deg"] = [y[0]] * pad + list(y)
        sfr = d.get("shot_frame_range")
        d["frames"] = nmax
        d["pair_align"] = {"padded_head_frames": pad,
                           "note": "开头缺 %d 帧，用本身份首帧复制补齐（成对动画起点必须一致）" % pad}
        print("  %-46s 补 %d 帧（复制首帧）" % (os.path.basename(p), pad))
        if not a.dry:
            json.dump(d, open(p, "w", encoding="utf-8"), ensure_ascii=False)
    print("=" * 70)
    print("DONE" + ("（干跑，未写盘）" if a.dry else ""))


if __name__ == "__main__":
    main()
