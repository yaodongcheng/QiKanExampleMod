# -*- coding: utf-8 -*-
"""TRF 左右镜像（X -> -X）。本骨架左右约定：desired_world = Mx @ R_src @ Dz,  Dz=diag(1,1,-1)
   （实测：对全部 L/R 配对误差 0.00~0.06°；而 Mx@R@Mx 恰好差 180°）"""
import os, sys, json, math, argparse
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _adjust_upper_body import fk_full, m2q, read_trf, write_trf
from scan_pose_metrics import build, qmul, qconj, qnorm, q2m

ROOT = r"D:/BrainMaker/骑砍2动画重定向"
Mx = np.diag([-1.0, 1.0, 1.0])
Dz = np.diag([1.0, 1.0, -1.0])
# keep_rest 骨（map.json）：左右 rest 不对称，不参与镜像，原样保留
KEEP_STATIC = {"l_upperarm_twist1", "r_upperarm_twist1", "l_foretwist1", "r_foretwist1", "l_finger0", "r_finger0"}


def mirror_name(n):
    if n.startswith("l_"): return "r_" + n[2:]
    if n.startswith("r_"): return "l_" + n[2:]
    return n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="inp", required=True); ap.add_argument("--out", required=True)
    ap.add_argument("--name", default=None)
    a = ap.parse_args()
    skel = json.load(open(os.path.join(ROOT, "output/verify/bannerlord_skel.json"), encoding="utf-8"))
    bones, rl, invq = build(skel)
    names = [b["name"] for b in bones]
    rest_q = [m2q(np.array(b["rest_local"], float)[:3, :3]) for b in bones]
    invq = [qconj(q) for q in rest_q]
    idx = {n: i for i, n in enumerate(names)}
    W = np.array(skel["armature_world"])
    root3 = np.array(bones[0]["rest_local"], float)[:3, :3]
    rootinv = np.linalg.inv(root3)
    nm, rots, pos = read_trf(a.inp if os.path.isabs(a.inp) else os.path.join(ROOT, a.inp))
    L = len(rots[0])
    quats = {n: [] for n in names}
    out_pos = []
    for fi in range(L):
        src_q = {names[i]: rots[i][fi][1] for i in range(len(bones))}
        rloc = rootinv @ np.array(pos[fi][1]) if (pos and fi < len(pos)) else None
        _P, wrot_src = fk_full(skel, bones, rl, invq, src_q, rloc)
        P_new = [None] * len(bones)
        for i, b in enumerate(bones):
            if b["name"] in KEEP_STATIC:
                q = src_q[b["name"]]
                Bm = np.eye(4); Bm[:3, :3] = q2m(qmul(invq[i], q))
                P_new[i] = rl[i] @ Bm if b["parent"] < 0 else P_new[b["parent"]] @ rl[i] @ Bm
                if b["parent"] < 0 and rloc is not None:
                    P_new[i][:3, 3] = rl[i][:3, :3] @ (rootinv @ Mx @ np.array(pos[fi][1])) + rl[i][:3, 3]
                quats[b["name"]].append((rots[i][fi][0], q))
                continue
            Rws = wrot_src[idx[mirror_name(b["name"])]]
            desired = Mx @ Rws @ Dz
            par = b["parent"]
            Mframe = (W @ rl[i])[:3, :3] if par < 0 else (W @ P_new[par] @ rl[i])[:3, :3]
            q = qnorm(qmul(rest_q[i], m2q(np.linalg.inv(Mframe) @ desired)))
            Bm = np.eye(4); Bm[:3, :3] = q2m(qmul(invq[i], q))
            if par < 0:
                if rloc is not None:
                    Bm[:3, 3] = (rootinv @ Mx @ np.array(pos[fi][1]))
                P_new[i] = rl[i] @ Bm
            else:
                P_new[i] = P_new[par] @ rl[i] @ Bm
            quats[b["name"]].append((rots[i][fi][0], q))
        if rloc is not None:
            out_pos.append((pos[fi][0], tuple(Mx @ np.array(pos[fi][1]))))
    outp = a.out if os.path.isabs(a.out) else os.path.join(ROOT, a.out)
    write_trf(outp, a.name or os.path.basename(outp)[:-4], len(bones), quats, bones, out_pos if out_pos else pos)
    print("-> %s (镜像完成, %d 帧)" % (outp, L))


if __name__ == "__main__":
    main()
