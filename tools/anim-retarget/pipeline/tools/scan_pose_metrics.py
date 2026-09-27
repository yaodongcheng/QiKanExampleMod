#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""纯 Python FK 扫描：遍历 TRF，量每帧"右手/左手相对头顶的高度"等指标，给"举手过头"素材排序。

为什么不用 Blender：后台模式下 view_layer.update() 对 pose bone 的世界矩阵不可靠（实测出 head_z=0），
且逐帧拉 depsgraph 很慢。改为"Blender 一次性导 skel.json + 纯 Python FK"，快且确定。

口径（与 pipeline/common/trf_to_fbx.py 对偶）
    pose_matrix[bone]      = pose_matrix[parent] @ rest_local[bone] @ basis[bone]
    basis                  = T(location) @ R(quat)     ;  quat(Blender) = rest_q⁻¹ ∘ q_trf
    根骨 location          = rest3⁻¹ ∘ p               ;  TRF 是"相对静止的纯增量"
    world                  = armature_world @ pose_matrix
用法
    python scan_pose_metrics.py --skel-json skel.json --trfdir output/trf --out scan.json [--stride 2]
"""
import os, sys, json, math, argparse
import numpy as np

def q2m(q):  # (x,y,z,w) -> 3x3
    x,y,z,w = q
    n = math.sqrt(x*x+y*y+z*z+w*w) or 1.0
    x,y,z,w = x/n,y/n,z/n,w/n
    return np.array([
        [1-2*(y*y+z*z), 2*(x*y-z*w),   2*(x*z+y*w)],
        [2*(x*y+z*w),   1-2*(x*x+z*z), 2*(y*z-x*w)],
        [2*(x*z-y*w),   2*(y*z+x*w),   1-2*(x*x+y*y)]], dtype=float)

def qmul(a,b):
    ax,ay,az,aw=a; bx,by,bz,bw=b
    return (aw*bx+ax*bw+ay*bz-az*by, aw*by-ax*bz+ay*bw+az*bx,
            aw*bz+ax*by-ay*bx+az*bw, aw*bw-ax*bx-ay*by-az*bz)

def qconj(q): return (-q[0],-q[1],-q[2],q[3])

def qnorm(q):
    n=math.sqrt(sum(c*c for c in q)) or 1.0
    return (q[0]/n,q[1]/n,q[2]/n,q[3]/n)

def read_trf(path):
    with open(path, encoding="utf-8") as f:
        L=[ln.rstrip("\r\n") for ln in f]
    name=L[2].split()[0]; nb=int(L[3]); i=4; rots=[]
    for _ in range(nb):
        n=int(L[i]); i+=1; fr=[]
        for _k in range(n):
            p=L[i].split(); i+=1
            fr.append((int(p[0]),(float(p[1]),float(p[2]),float(p[3]),float(p[4]))))
        rots.append(fr)
    np_=int(L[i]); i+=1; pos=[]
    for _k in range(np_):
        p=L[i].split(); i+=1
        pos.append((int(p[0]),(float(p[1]),float(p[2]),float(p[3]))))
    return name, rots, pos

def build(skel):
    bones=skel["bones"]
    rl=[np.array(b["rest_local"],dtype=float) for b in bones]
    rq=[qnorm(tuple(np.array(b["rest_local"],dtype=float)[:3,:3].reshape(-1))) for b in bones]  # placeholder
    # 从 rest_local 的旋转部分取四元数（用矩阵→四元数）
    def m2q(M):
        t=M[0,0]+M[1,1]+M[2,2]
        if t>0:
            s=math.sqrt(t+1)*2; w=s/4; x=(M[2,1]-M[1,2])/s; y=(M[0,2]-M[2,0])/s; z=(M[1,0]-M[0,1])/s
        elif M[0,0]>M[1,1] and M[0,0]>M[2,2]:
            s=math.sqrt(1+M[0,0]-M[1,1]-M[2,2])*2; w=(M[2,1]-M[1,2])/s; x=s/4; y=(M[0,1]+M[1,0])/s; z=(M[0,2]+M[2,0])/s
        elif M[1,1]>M[2,2]:
            s=math.sqrt(1+M[1,1]-M[0,0]-M[2,2])*2; w=(M[0,2]-M[2,0])/s; x=(M[0,1]+M[1,0])/s; y=s/4; z=(M[1,2]+M[2,1])/s
        else:
            s=math.sqrt(1+M[2,2]-M[0,0]-M[1,1])*2; w=(M[1,0]-M[0,1])/s; x=(M[0,2]+M[2,0])/s; y=(M[1,2]+M[2,1])/s; z=s/4
        return qnorm((x,y,z,w))
    rq=[m2q(b["rest_local"] if False else np.array(b["rest_local"],dtype=float)[:3,:3]) for b in bones]
    invq=[qconj(q) for q in rq]
    return bones, rl, invq

def fk(skel, rl, invq, quats, root_loc, names, want):
    """quats: dict name->(x,y,z,w) TRF 绝对局部. 返回 {name: (head_xyz, tail_xyz)}"""
    bones=skel["bones"]; W=np.array(skel["armature_world"],dtype=float)
    P=[None]*len(bones)
    out={}
    for i,b in enumerate(bones):
        q=quats.get(b["name"])
        if q is None:
            B=np.eye(4)
        else:
            B=np.eye(4); B[:3,:3]=q2m(qmul(invq[i], q))
        if b["name"]==names[0] and root_loc is not None:   # root translation
            B[:3,3]=root_loc
        par=b["parent"]
        P[i]= (P[par] @ rl[i] @ B) if par>=0 else (rl[i] @ B)
    for n in want:
        i=[k for k,bb in enumerate(bones) if bb["name"]==n][0]
        head=W @ (P[i] @ np.array([0,0,0,1.0]))
        tail=W @ (P[i] @ np.array([0,bones[i]["length"],0,1.0]))
        out[n]=(head[:3],tail[:3])
    return out

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument("--skel-json",required=True); ap.add_argument("--trfdir",required=True)
    ap.add_argument("--out",required=True); ap.add_argument("--stride",type=int,default=2)
    ap.add_argument("--root",default="pelvis"); ap.add_argument("--filter",default="")
    a=ap.parse_args()
    skel=json.load(open(a.skel_json,encoding="utf-8"))
    bones, rl, invq = build(skel)
    root3=np.array([b for b in bones if b["name"]==a.root][0]["rest_local"],dtype=float)[:3,:3] if any(b["name"]==a.root for b in bones) else np.eye(3)
    rootinv=np.linalg.inv(root3)
    WANT=["r_hand","l_hand","head","pelvis","r_toe0","l_toe0","r_foretwist","l_foretwist"]
    files=sorted(f for f in os.listdir(a.trfdir) if f.endswith(".trf") and a.filter in f)
    res=[]
    for fn in files:
        try: _n,rots,pos=read_trf(os.path.join(a.trfdir,fn))
        except Exception as e: print("  skip",fn,e); continue
        if len(rots)!=len(bones): print("  mismatch",fn); continue
        L=len(rots[0]); best=None
        for fi in range(0,L,a.stride):
            quats={bones[bi]["name"]: rots[bi][fi][1] for bi in range(len(bones))}
            rloc = rootinv @ np.array(pos[fi][1]) if (pos and fi<len(pos)) else None
            p=fk(skel,rl,invq,quats,rloc,[a.root],WANT)
            hr=p["r_hand"][1]; hl=p["l_hand"][1]; hd=p["head"][1]
            rec=dict(frame=rots[0][fi][0],
                     rHand_above=float(hr[2]-hd[2]), lHand_above=float(hl[2]-hd[2]),
                     rHand_z=float(hr[2]), head_z=float(hd[2]),
                     rHand_x=float(hr[0]), rHand_y=float(hr[1]),
                     foot_z=float(min(p["r_toe0"][0][2],p["l_toe0"][0][2])),
                     pelvis_z=float(p["pelvis"][0][2]))
            score=rec["rHand_above"]
            if best is None or score>best["rHand_above"]: best=rec
        if best is None: continue
        best["clip"]=fn[:-4]; best["frames"]=L
        # 也记录"最后一段的平均抬手高度"（用于过渡终点的选择）
        res.append(best)
    res.sort(key=lambda r:-r["rHand_above"])
    json.dump(res,open(a.out,"w",encoding="utf-8"),ensure_ascii=False,indent=1)
    print("=== TOP 30 右手过头顶（米）===")
    for r in res[:30]:
        print("  %-42s rHand_above=%6.3f  lHand_above=%6.3f  head_z=%.2f pelvis_z=%.2f foot_z=%.2f f=%d"
              %(r["clip"],r["rHand_above"],r["lHand_above"],r["head_z"],r["pelvis_z"],r["foot_z"],r["frame"]))
    print("Done ->",a.out)
if __name__=="__main__": main()
