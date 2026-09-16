# -*- coding: utf-8 -*-
"""check_regression.py —— 【回归闸门】用织田信长当基准，卡住批量管线。

为什么要有它：信长的头是本工程**唯一有"手工正确答案"的角色** ——
    `D:\\BrainMaker\\战国无双2资产解包分析\\work\\out\\build_head_L02.txt` 是他当年手工切的构建日志，
    里面有精确的落位数字。批量管线每改一处（挑件方式 / 切嘴 / 清碎片 / 剔非头部…），
    都必须拿他对照一遍。

2026-09-14 教训：把 `keep_head_only` 的半径系数从 3.0 收到 2.0 时，**把他的头发削掉了 349/387 个顶点**
（因为头发绑的头骨 bone_11 不在面部骨族里），当时只靠批量输出才看见 —— 没有闸门就会溜过去。

**改任何 build_head.py 的几何步骤之后，先跑这个。**
用法: python tools/sw2-pipeline/check_regression.py [--rebuild]
退出码 0 = 通过；1 = 与基准偏离
"""
import argparse
import io
import os
import re
import subprocess
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
KEY = "L02_nobunaga"
BUILD = os.path.join(REPO, "Debug", "offline", "sw2_build", KEY)
PROBE = os.path.join(REPO, "tools", "face-pipeline", "scripts", "fbx_probe.py")

# 基准 = 手工版的构建日志（允许的偏差见注释）
GOLDEN = {
    "mouth_z": (172.376, 0.05),        # 源空间嘴中心 z
    "scale": (0.01144, 0.0008),        # 标定缩放（手工 0.01144；放宽到 ±7%）
    "bbox_x": ((-0.1321, 0.1302), 0.006),
    # 🔴 2026-09-16 更新：加 `--neck-fill`（补脖子下摆）之后，头底从 1.4644 降到 1.4146
    #    —— 这是**有意为之**：原版头的脖子底就在 1.4144，补下摆是为了让脖子伸进领口里
    #    （见 build_head.py 的 fill_neck_to_rim）。旧值留给"回退到不补"时对照。
    "bbox_z": ((1.4146, 1.9502), 0.015),
    #    （旧值 1.4644 留给"回退到不补下摆"时对照，不要作为键加进来——checker 会去取值）
    "eye_z": (1.6839, 0.001),
    "mouth_target_z": (1.6044, 0.001),
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rebuild", action="store_true", help="先重跑一遍 L02 再检查")
    a = ap.parse_args()

    if a.rebuild:
        r = subprocess.run([sys.executable, os.path.join(HERE, "build_heads.py"), "--only", KEY],
                           cwd=REPO)
        if r.returncode != 0:
            print("FAIL: 重跑 %s 就没通过" % KEY)
            return 1

    logp = os.path.join(BUILD, KEY + ".log")
    v2 = [f for f in os.listdir(BUILD) if f.endswith("_v2.fbx")] if os.path.isdir(BUILD) else []
    if not v2:
        print("FAIL: 找不到 %s 的产物，先跑 build_heads.py --only %s" % (KEY, KEY))
        return 1
    log = io.open(logp, encoding="utf-8", errors="replace").read()
    r = subprocess.run([sys.executable, PROBE, os.path.join(BUILD, v2[0])], capture_output=True)
    probe = (r.stdout + r.stderr).decode("latin1")

    def g(pat, txt, cast=float):
        m = re.search(pat, txt)
        return cast(m.group(1)) if m else None

    got = {
        "mouth_z": g(r"嘴中心 z=([\d.]+)", log),
        "scale": g(r"→ 缩放 ([\d.]+)", log),
        "eye_z": g(r"眼球 \(-?[\d.]+, [\d.]+, ([\d.]+)\)", log),
        "mouth_target_z": g(r"嘴\s+\(-?[\d.]+, [\d.]+, ([\d.]+)\)", log),
    }
    mb = re.search(r"\.0.{0,4}Geometry\] verts=(\d+)\s+x\[([-\d.]+),([-\d.]+)\] y\[[-\d.]+,([-\d.]+)\] z\[([-\d.]+),([-\d.]+)\]", probe)
    if mb:
        got["bbox_x"] = (float(mb.group(2)), float(mb.group(3)))
        got["bbox_z"] = (float(mb.group(5)), float(mb.group(6)))
        got["face_verts"] = int(mb.group(1))
    for ln in log.split("\n"):
        if "-> head_nobunaga_a.0" in ln:
            m = re.search(r"面 (\d+)", ln)
            if m:
                got["face_faces"] = int(m.group(1))

    bad = []
    print("=" * 72)
    print("信长回归闸门（基准 = work/out/build_head_L02.txt 的手工版）")
    print("=" * 72)
    for k, (want, tol) in GOLDEN.items():
        v = got.get(k)
        if v is None:
            print("  %-16s ❌ 取不到值（管线输出格式变了？）" % k)
            bad.append(k)
            continue
        if isinstance(want, tuple):
            ok = all(abs(v[i] - want[i]) <= tol for i in range(2))
            dd = max(abs(v[i] - want[i]) for i in range(2))
        else:
            ok = abs(v - want) <= tol
            dd = abs(v - want)
        print("  %-16s %-7s 现值 %-22s 基准 %-22s 差 %.4f（容差 %.4f）"
              % (k, "✅" if ok else "❌", str(v), str(want), dd, tol))
        if not ok:
            bad.append(k)

    # 额外硬指标：成品的面数范围（放宽容差，盯"突变"而不是盯绝对值）。
    # 🔴 2026-09-16 基准 816 → 929，两笔增量都核实过是**有意为之**：
    #    ① 9-15 通用步「薄片补背面」（脸壳复制+翻面）→ 917
    #    ② 9-16 通用步「补脖子下摆」（--neck-fill）→ 929
    #    判据：这两步都是**通用**的（所有头一起变），不是某个角色跑偏；
    #    若哪天只有一个人偏离这条，那才是真回归。
    FF_BASE, FF_TOL = 929, 70
    ff = got.get("face_faces")
    if ff is not None:
        ok = abs(ff - FF_BASE) <= FF_TOL
        print("  %-16s %-7s 现值 %-22s 基准 %-22s" % ("face_faces", "✅" if ok else "❌", ff,
              "%d±%d" % (FF_BASE, FF_TOL)))
        if not ok:
            bad.append("face_faces")

    print("=" * 72)
    if bad:
        print("❌ 回归：%s 偏离基准 —— 改几何步骤前先看 build_head.py 的注释，别硬调系数" % bad)
    else:
        print("✅ 全部通过")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
