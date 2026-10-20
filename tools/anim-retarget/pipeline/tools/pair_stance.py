#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""pair_stance.py —— 量双人动作的「首帧相对站位」，输出 PairActions.csv 的 Stance_* 值。

两种模式
    --mode root     入口 clip 的根骨首帧**自带**站位偏移（扛人这类：被扛者首帧 = 她站他旁边）。
                    直接读 defender 入口 clip 的 root_pos[0]；attacker 首帧应为 0。
                    这是目前**能自动量**的那一类（`biped_233_beikangqi_lift` 实测 -0.735/0.0846）。

    --mode contact  首帧根位移为 0 的成对 clip（处决这类：两条各自从原点起）。
                    正确做法 = 两人骨架叠同一原点做 FK，逐帧量「攻击方手 ↔ 受击方全身」最近距离并全帧求和
                    （见 `tools/anim-retarget/项目总纲.md` §0.14 §十三）。**本脚本暂未实现 FK**；
                    目前只打印候选与实机校准步骤，不硬编数字。

口径
    · +X = 角色右手侧 · +Y = 角色正前 · 偏航 = atan2(x, y)（度），0 = 同向，180 = 面对面。
    · stance 的方向 = **defender 相对 attacker**（写在 attacker 的本地系里）。
    · 只读 TRF 的根骨轨；旋转轨不动。

用法（工作目录 = 数据根 D:\\BrainMaker\\骑砍2动画重定向）
    python pipeline/tools/pair_stance.py --mode root \
        --attacker output/trf/biped_230_kangqi_lift.trf \
        --defender output/trf/biped_233_beikangqi_lift.trf --id grapple_carry

    python pipeline/tools/pair_stance.py --mode contact --id execution02
"""
import argparse
import math
import os
import sys

_COMMON = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "common")
sys.path.insert(0, os.path.abspath(_COMMON))
from trf_compose import read_trf  # noqa: E402


def first_root(t):
    return t.root_pos[0][1] if t.root_pos else (0.0, 0.0, 0.0)


def quat_yaw_deg(q):
    """根骨首帧四元数 → 偏航角（度）。四元数布局 = (x, y, z, w)。"""
    x, y, z, w = q
    return math.degrees(math.atan2(2.0 * (w * z + x * y), 1.0 - 2.0 * (y * y + z * z)))


def first_yaw(t):
    return quat_yaw_deg(t.bones[0][0][1]) if t.bones and t.bones[0] else 0.0


def norm180(a):
    while a > 180.0:
        a -= 360.0
    while a < -180.0:
        a += 360.0
    return a


def mode_root(args):
    atk = read_trf(args.attacker)
    dfd = read_trf(args.defender)
    af = first_root(atk)
    df = first_root(dfd)
    ay = first_yaw(atk)
    dy = first_yaw(dfd)

    right = df[0] - af[0]
    forward = df[1] - af[1]

    # 🔴 root 四元数**不是**角色朝向（它含骨架静止姿势的局部轴；实测扛人这对算出来 -119°，
    #    而项目总纲记录的实际朝向差是 0°）。这里只当 sanity 提示，**不写进 Stance_Direction**。
    quat_delta = norm180(dy - ay)

    print("attacker '%s' first=(%.4f, %.4f, %.4f) rootQuatYaw=%.2f deg" % (atk.name, af[0], af[1], af[2], ay))
    print("defender '%s' first=(%.4f, %.4f, %.4f) rootQuatYaw=%.2f deg" % (dfd.name, df[0], df[1], df[2], dy))
    print("root-quat delta=%.2f deg（**不是**角色朝向，只当 sanity 提示）" % quat_delta)
    print("")
    print("Stance_Right=%.6f Stance_Forward=%.6f Stance_Direction=0.00" % (right, forward))
    print("Stance_Verified=True Stance_Source=root   (id=%s)" % (args.id or "<id>"))
    print("")
    print("检查：attacker 首帧水平应≈0（它是参考系）；defender 首帧就是站姿偏移。")
    print("⚠ Stance_Direction 默认 0（同向）：root 模式量不了朝向；起手若面对面/横驮，请按实机改成 180 / ±90。")
    if abs(af[0]) > 0.02 or abs(af[1]) > 0.02:
        print("⚠ attacker 首帧水平非 0 —— 这条不是纯参考系；先把 attacker 过一遍 `trf_edit.py inplace`。")
    if args.emit_csv:
        print("")
        print("把上面 Stance_* 五个值写进 PairActions.csv 里 id=%s 那一行。" % (args.id or "<id>"))


def mode_contact(args):
    print("contact mode: 本脚手架**未实现 skeleton FK**，不能自动给处决类的首帧站位。")
    print("")
    print("正确做法（项目总纲 §0.14 §十三）：")
    print("  1) 两人骨架叠同一原点做 FK，逐帧量「攻击方两只手 ↔ 受击方全身」最近距离，全帧求和；")
    print("  2) 候选成对生成（trf_edit.py flipside --mode x / yaw180），谁小谁对；")
    print("  3) **别只挑首末帧**：首末帧是左右差异最小的两帧（实测首帧 0.16 vs 0.17，全帧 13.88 vs 6.48）。")
    print("")
    print("眼下最快的实机校准：")
    print("  custom.pair stance %s        (打印目标点/距离；看是否合理)" % (args.id or "<id>"))
    print("  custom.pair start %s force   (强制起播；看接触帧)" % (args.id or "<id>"))
    print("  custom.pair commit %s        (打印最终相对位置)" % (args.id or "<id>"))
    print("把校准结果手写进 PairActions.csv，并把 Stance_Verified 改成 True。")


def main():
    ap = argparse.ArgumentParser(description="量双人动作的首帧相对站位")
    ap.add_argument("--mode", choices=("root", "contact"), default="root")
    ap.add_argument("--attacker", help="attacker 侧入口 TRF（mode=root 必填）")
    ap.add_argument("--defender", help="defender 侧入口 TRF（mode=root 必填）")
    ap.add_argument("--id", help="PairActions.csv 的 ID（只用于打印）")
    ap.add_argument("--emit-csv", action="store_true", help="打印可直接抄进 CSV 的 Stance_* 值")
    args = ap.parse_args()

    if args.mode == "root":
        if not args.attacker or not args.defender:
            ap.error("mode=root 需要 --attacker 和 --defender")
        mode_root(args)
    else:
        mode_contact(args)


if __name__ == "__main__":
    main()
