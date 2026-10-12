# -*- coding: utf-8 -*-
"""调整 TRF 上半身朝向：把 spine 链整体"直起"、把头颈摆正；只改 pitch，保留 yaw，不动骨盆/腿。

口径与 pipeline/common/trf_compose.py / scan_pose_metrics.py 一致：
    pose_matrix[i] = pose_matrix[par] @ rest_local[i] @ basis[i]
    basis_rot      = rest_q[i]⁻¹ ∘ q_trf[i]     (Blender 约定)
    world_rot[i]   = (armature_world @ pose_matrix[i])[:3,:3]
"""
import os, sys, json, math, argparse
import numpy as np
# 2026-10-10 收编入库：原来写死 D 盘路径找同目录的 scan_pose_metrics，改成按【本文件所在目录】找
# （与 pipeline/tools/mirror_trf.py 同一写法）—— 仓库侧的 pipeline/ 与 D 盘是 junction，同一份文件。
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from scan_pose_metrics import build, read_trf, q2m, qmul, qconj, qnorm

ROOT = r"D:/BrainMaker/骑砍2动画重定向"
Yv = np.array([0.0, 1.0, 0.0])   # 骨轴 = 局部 +Y
LEG_BONES = ["pelvis", "l_thigh", "l_calf", "l_foot", "l_toe0",
             "r_thigh", "r_calf", "r_foot", "r_toe0"]


def m2q(M):
    t = M[0, 0] + M[1, 1] + M[2, 2]
    if t > 0:
        s = math.sqrt(t + 1) * 2; w = s / 4; x = (M[2, 1] - M[1, 2]) / s; y = (M[0, 2] - M[2, 0]) / s; z = (M[1, 0] - M[0, 1]) / s
    elif M[0, 0] > M[1, 1] and M[0, 0] > M[2, 2]:
        s = math.sqrt(1 + M[0, 0] - M[1, 1] - M[2, 2]) * 2; w = (M[2, 1] - M[1, 2]) / s; x = s / 4; y = (M[0, 1] + M[1, 0]) / s; z = (M[0, 2] + M[2, 0]) / s
    elif M[1, 1] > M[2, 2]:
        s = math.sqrt(1 + M[1, 1] - M[0, 0] - M[2, 2]) * 2; w = (M[0, 2] - M[2, 0]) / s; x = (M[0, 1] + M[1, 0]) / s; y = s / 4; z = (M[1, 2] + M[2, 1]) / s
    else:
        s = math.sqrt(1 + M[2, 2] - M[0, 0] - M[1, 1]) * 2; w = (M[1, 0] - M[0, 1]) / s; x = (M[0, 2] + M[2, 0]) / s; y = (M[1, 2] + M[2, 1]) / s; z = s / 4
    return qnorm((x, y, z, w))


def rodrigues(axis, deg):
    a = np.array(axis, float); a = a / (np.linalg.norm(a) or 1.0)
    th = math.radians(deg); c = math.cos(th); s = math.sin(th)
    x, y, z = a
    K = np.array([[0, -z, y], [z, 0, -x], [-y, x, 0]])
    return np.eye(3) * c + s * K + (1 - c) * np.outer(a, a)


def pitch_deg(v):
    h = math.hypot(v[0], v[1])
    return math.degrees(math.atan2(v[2], h))


def fk_full(skel, bones, rl, invq, quats, rloc):
    W = np.array(skel["armature_world"], float)
    P = [None] * len(bones)
    for i, b in enumerate(bones):
        q = quats.get(b["name"])
        B = np.eye(4)
        if q is not None:
            B[:3, :3] = q2m(qmul(invq[i], q))
        par = b["parent"]
        if par < 0:
            if rloc is not None:
                B[:3, 3] = rloc
            P[i] = rl[i] @ B
        else:
            P[i] = P[par] @ rl[i] @ B
    world_rot = [(W @ P[i])[:3, :3] for i in range(len(bones))]
    return P, world_rot


def write_trf(path, name, nb, quats, bones, pos):
    with open(path, "w", encoding="utf-8") as f:
        f.write("rfver 4\nskeleton_anim 1\n%s 1\n %d\n" % (name, nb))
        L = len(quats[bones[0]["name"]])
        for b in bones:
            f.write(" %d\n" % L)
            for fr, q in quats[b["name"]]:
                f.write(" %d %.6f %.6f %.6f %.6f\n" % (fr, q[0], q[1], q[2], q[3]))
        f.write(" %d\n" % len(pos))
        for fr, p in pos:
            f.write(" %d %.6f %.6f %.6f\n" % (fr, p[0], p[1], p[2]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="inp", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--torso-pitch", type=float, default=8.0, help="spine2 目标俯仰角(度, +=朝前上/直立)")
    ap.add_argument("--head-pitch", type=float, default=-3.0, help="head 目标俯仰角(度)")
    ap.add_argument("--torso-bone", default="spine")
    ap.add_argument("--head-bone", default="neck")
    ap.add_argument("--stand-legs", action="store_true", help="骨盆+双腿复位到静止直立姿")
    ap.add_argument("--root-zero", action="store_true", help="根骨位移归零(站回静止高度)")
    a = ap.parse_args()

    skel = json.load(open(os.path.join(ROOT, "output/verify/bannerlord_skel.json"), encoding="utf-8"))
    bones, rl, invq = build(skel)
    rest_q = [m2q(np.array(b["rest_local"], float)[:3, :3]) for b in bones]
    invq = [qconj(q) for q in rest_q]
    idx = {b["name"]: i for i, b in enumerate(bones)}
    root3 = np.array(bones[0]["rest_local"], float)[:3, :3]
    rootinv = np.linalg.inv(root3)
    W = np.array(skel["armature_world"], float)

    name, rots, pos = read_trf(a.inp if os.path.isabs(a.inp) else os.path.join(ROOT, a.inp))
    L = len(rots[0])
    # 组装 quats: name -> [(frame, q)]
    quats = {bones[i]["name"]: [(rots[i][fi][0], rots[i][fi][1]) for fi in range(L)] for i in range(len(bones))}

    ti = idx[a.torso_bone]; hi = idx[a.head_bone]
    print("== %s : torso@%s -> %+.1f°, head@%s -> %+.1f° ==" % (name, a.torso_bone, a.torso_pitch, a.head_bone, a.head_pitch))
    for fi in range(L):
        qt = {bones[i]["name"]: quats[bones[i]["name"]][fi][1] for i in range(len(bones))}
        rloc = rootinv @ np.array(pos[fi][1]) if (pos and fi < len(pos)) else None
        # 0) 先把骨盆+双腿复位到静止直立姿（骨轴 basis=I ⇒ q_trf = rest_q）
        if a.stand_legs:
            for nm in LEG_BONES:
                qt[nm] = rest_q[idx[nm]]
        if a.root_zero:
            rloc = np.zeros(3)
        P, wrot = fk_full(skel, bones, rl, invq, qt, rloc)
        b0 = pitch_deg(wrot[idx["spine2"]] @ Yv); h0 = pitch_deg(wrot[idx["head"]] @ Yv)

        # 1) torso: rotate spine 链，使 spine2 pitch -> target
        v = wrot[idx["spine2"]] @ Yv
        ax = np.cross([0, 0, 1.0], v)
        if np.linalg.norm(ax) < 1e-6:
            ax = np.array([1.0, 0, 0])
        R = rodrigues(ax, b0 - a.torso_pitch)
        des = R @ wrot[ti]
        # local q for bone ti
        par = bones[ti]["parent"]
        M = (W @ P[par] @ rl[ti])[:3, :3] if par >= 0 else (W @ rl[ti])[:3, :3]
        qt[bones[ti]["name"]] = qnorm(qmul(rest_q[ti], m2q(np.linalg.inv(M) @ des)))
        P, wrot = fk_full(skel, bones, rl, invq, qt, rloc)

        # 2) head: rotate neck 链，使 head pitch -> target
        v2 = wrot[idx["head"]] @ Yv
        b1 = pitch_deg(v2)
        ax2 = np.cross([0, 0, 1.0], v2)
        if np.linalg.norm(ax2) < 1e-6:
            ax2 = np.array([1.0, 0, 0])
        R2 = rodrigues(ax2, b1 - a.head_pitch)
        des2 = R2 @ wrot[hi]
        par2 = bones[hi]["parent"]
        M2 = (W @ P[par2] @ rl[hi])[:3, :3] if par2 >= 0 else (W @ rl[hi])[:3, :3]
        qt[bones[hi]["name"]] = qnorm(qmul(rest_q[hi], m2q(np.linalg.inv(M2) @ des2)))
        P, wrot = fk_full(skel, bones, rl, invq, qt, rloc)

        t1 = pitch_deg(wrot[idx["spine2"]] @ Yv); h1 = pitch_deg(wrot[idx["head"]] @ Yv)
        print("  f%-3d spine2 %+6.1f -> %+6.1f | head %+6.1f -> %+6.1f" % (rots[0][fi][0], b0, t1, h0, h1))
        for i in range(len(bones)):
            quats[bones[i]["name"]][fi] = (quats[bones[i]["name"]][fi][0], qt[bones[i]["name"]])

    if a.root_zero:
        pos = [(fr, (0.0, 0.0, 0.0)) for fr, _p in pos]
    outp = a.out if os.path.isabs(a.out) else os.path.join(ROOT, a.out)
    write_trf(outp, name, len(bones), quats, bones, pos)
    print("-> ", outp)


if __name__ == "__main__":
    main()
