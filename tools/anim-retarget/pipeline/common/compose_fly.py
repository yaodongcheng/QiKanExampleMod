#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""飞行动画「一键合成 + 刷查看器」——从 3 个 input 到查看器里能看到，一条命令。

3 个 input（就是 trf_compose.py 的三个参数）：
    ① 基础 pose   = --ref  那条动画的第 0 帧（默认 = --base，同一份）
    ② add pose    = --add  增量动画
    ③ 基础动画    = --base
    公式：结果(t) = 基础(t) ∘ (增量 ∘ 参照⁻¹)

用法（在 D:/BrainMaker/骑砍2动画重定向 下）：
    python pipeline/common/compose_fly.py                 # 合成 PAIRS 里 12 条 + 刷查看器
    python pipeline/common/compose_fly.py --dry           # 只打印命令
    python pipeline/common/compose_fly.py --no-viewer     # 只合成不刷查看器
    python pipeline/common/compose_fly.py --root-scale 0.3
    python pipeline/common/compose_fly.py --diff          # 每条都跑 --check（不写文件）

合成完：viewer/启动查看器.bat → 下拉选「【飞行·合成】…」→ 底部按 Add 名切换 → 看三格。
表在 build_fly_comp_viewer.PAIRS（唯一一份，改表只改那里）。
"""
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
from build_fly_comp_viewer import PAIRS, TRFDIR  # noqa: E402

COMPOSE = os.path.join(HERE, "trf_compose.py")
PY = sys.executable or "python"


def main():
    dry = "--dry" in sys.argv
    no_viewer = "--no-viewer" in sys.argv
    diff = "--diff" in sys.argv
    rs = "0"
    if "--root-scale" in sys.argv:
        rs = sys.argv[sys.argv.index("--root-scale") + 1]

    ok, bad = 0, []
    for comp, clip, base in PAIRS:
        add = "fly_" + clip
        b = os.path.join(TRFDIR, base + ".trf")
        a = os.path.join(TRFDIR, add + ".trf")
        if not (os.path.isfile(b) and os.path.isfile(a)):
            bad.append((comp, "缺输入: %s / %s" % (os.path.basename(b), os.path.basename(a))))
            continue
        cmd = [PY, COMPOSE, "--base", b, "--add", a, "--ref", b, "--root-scale", rs]
        if diff:
            cmd += ["--check"]
        else:
            cmd += ["--out", os.path.join(TRFDIR, comp + ".trf")]
        print("── %s   ← %s" % (comp, add))
        if dry:
            print("   " + " ".join(cmd)); ok += 1; continue
        r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
        tail = [l for l in (r.stdout or "").splitlines() if "最大偏差" in l or "已写出" in l]
        if r.returncode == 0:
            ok += 1
            for l in tail: print("   " + l.strip())
        else:
            bad.append((comp, (r.stdout or "")[-200:] + (r.stderr or "")[-200:]))

    print("\n合成完成：成功 %d / 失败或缺料 %d" % (ok, len(bad)))
    for c, why in bad: print("   ✗ %s  %s" % (c, why))
    if ok and not dry and not no_viewer and not diff:
        print("\n-- 刷查看器第③格 --")
        subprocess.run([PY, os.path.join(HERE, "build_fly_comp_viewer.py")])
        print("\n看：viewer/启动查看器.bat → 数据集选「【飞行·合成】…」→ 底部切 Add")
    return 0 if not bad else 1


if __name__ == "__main__":
    sys.exit(main())
