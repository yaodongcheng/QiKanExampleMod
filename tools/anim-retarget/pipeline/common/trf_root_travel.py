"""TRF 根骨位置轨 → ModKit clip 的 displacement 字段该填什么。

背景：骑砍2 的 AnimationClip 有一个 `displacement` 用法槽（ModKit 里在 clip_usage_data 折叠区，
不在 Flags 列表里），字段 = (X, Y, Z) 向量 + endProgress。它的口径（原版 337 条带位移的 clip 实测）：

    · 单位【米】，纯水平 —— 原版 333 条里 Z 分量【恒为 0】
    · X = 横向（+X = 角色右手侧）   Y = 前后（+Y = 角色正前方）
      依据：原版 `stagger_left_lvl2` = (-1,0,0) / `stagger_right_lvl2` = (+1,0,0)
            `strike_knock_back_chest_back`（背后被击→往前飞）= (0,+1.7,0)
            `strike_knock_back_chest_front`（正面被击→往后飞）= (0,-1.7,0)
    · endProgress = 位移"走完"的时刻，占整条 clip 的比例（原版实测 0.4~1.0）
    · 原版量级 1.0~1.8 m（死人倒地的距离）

本脚本读 TRF 最后那段（根骨位置轨，纯增量、单位米），输出：
    ① 净位移向量（首帧→末帧）
    ② 把 Z 归零后的 (X, Y, 0) —— 这就是该填进 displacement 的值
    ③ 位移走完的进度点（供 endProgress 用）
    ④ 逐帧轨迹，供肉眼核对方向有没有中途拐弯（引擎只认直线，拐弯 = 模型对不上）
"""

import argparse
import math
import os
import sys


def parse_trf_positions(path):
    """返回 [(frame, x, y, z), ...]，即 TRF 末尾那段根骨位置轨。"""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        lines = [ln.rstrip("\r\n") for ln in fh]

    # 末段结构： ... <位移帧数> \n <帧> <x> <y> <z> \n ... \n end
    end_idx = None
    for i in range(len(lines) - 1, -1, -1):
        if lines[i].strip() == "end":
            end_idx = i
            break
    if end_idx is None:
        raise SystemExit("TRF 末尾没有 'end' —— 文件不完整？")

    # 从 end 往回走，收集 4 段式数据行，直到撞上"只有一个整数"的计数行
    rows = []
    i = end_idx - 1
    while i >= 0:
        parts = lines[i].split()
        if len(parts) == 4 and _is_int(parts[0]):
            rows.append(parts)
            i -= 1
        else:
            break
    if i < 0 or len(lines[i].split()) != 1:
        raise SystemExit(f"位置轨的计数行没找到（撞到：{lines[i]!r}）")
    declared = int(lines[i].split()[0])
    rows.reverse()
    if declared != len(rows):
        print(f"  ⚠ 计数行声明 {declared} 帧，实际读到 {len(rows)} 帧", file=sys.stderr)
    return [(int(p[0]), float(p[1]), float(p[2]), float(p[3])) for p in rows]


def _is_int(s):
    try:
        int(s)
        return True
    except ValueError:
        return False


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--trf", required=True)
    ap.add_argument("--label", default=None, help="打印用的名字，缺省取文件名")
    args = ap.parse_args()

    label = args.label or os.path.basename(args.trf)
    rows = parse_trf_positions(args.trf)
    if len(rows) < 2:
        raise SystemExit("位置轨只有 %d 帧，做不了差分" % len(rows))

    f0, x0, y0, z0 = rows[0]
    f1, x1, y1, z1 = rows[-1]
    nx, ny, nz = x1 - x0, y1 - y0, z1 - z0
    horiz = math.hypot(nx, ny)
    total = math.sqrt(nx * nx + ny * ny + nz * nz)

    print(f"== {label} ==")
    print(f"  帧数        {len(rows)}   帧号 {f0} .. {f1}")
    print(f"  首帧位置    ({x0:.6f}, {y0:.6f}, {z0:.6f})   ← 应≈0（纯增量语义）")
    print(f"  末帧位置    ({x1:.6f}, {y1:.6f}, {z1:.6f})")
    print(f"  净位移      ({nx:.6f}, {ny:.6f}, {nz:.6f})")
    print(f"  水平模长    {horiz:.4f} m     含竖直 {total:.4f} m     竖直分量 {nz:+.4f} m")
    print()

    # ④ 轨迹是不是直线：算每帧到"首→末"直线的横向偏离
    max_dev = 0.0
    axis_len = horiz if horiz > 1e-9 else 1.0
    for _f, x, y, _z in rows:
        dx, dy = x - x0, y - y0
        dev = abs(dx * ny - dy * nx) / axis_len
        max_dev = max(max_dev, dev)
    print(f"  轨迹直线度  离首末连线最远 {max_dev:.4f} m"
          f"   {'（直线，引擎单向量模型成立）' if max_dev < 0.05 * max(horiz, 1e-9) + 0.02 else '🔴 明显拐弯，单向量表达不了'}")

    # ③ 走完的进度点：第一次达到净位移 95% 的那一帧
    prog95 = None
    for k, (_f, x, y, _z) in enumerate(rows):
        if math.hypot(x - x0, y - y0) >= 0.95 * horiz:
            prog95 = k / (len(rows) - 1)
            break
    print(f"  位移走完    第 {prog95 * (len(rows) - 1):.0f}/{len(rows) - 1} 帧达 95%"
          f"  ⇒ endProgress ≈ {prog95:.4f}" if prog95 is not None else "  位移从未达 95%")
    print()

    print("  ── 填进 ModKit 的值（Z 按原版惯例归零）──")
    print(f"     X = {nx:.4f}      Y = {ny:.4f}      Z = 0")
    print(f"     endProgress = {prog95:.2f}" if prog95 is not None else "     endProgress = 1.00")
    print()

    print("  ── 逐帧轨迹（每 10 帧抽一个）──")
    step = max(1, len(rows) // 10)
    for k in range(0, len(rows), step):
        _f, x, y, _z = rows[k]
        print(f"     [{k:3d}] ({x:+.4f}, {y:+.4f})")


if __name__ == "__main__":
    main()
