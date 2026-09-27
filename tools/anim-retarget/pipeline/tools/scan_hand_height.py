#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""客观扫描：遍历一批 TRF，用 FK 求【右手世界高度 - 头顶高度】的逐帧极值，给"举手/过头"素材排序。

用法
    blender -b --python scan_hand_height.py -- --skel <骨架FBX> --trfdir <TRF目录> \
        --out <结果JSON> [--stride 3] [--filter 子串]

口径（与 trf_to_fbx.py 完全一致）
    q_trf = rest_q ∘ q_pose  ⇒  pose.rotation_quaternion = rest_q⁻¹ ∘ q_trf
    根骨平移：location = rest3⁻¹ ∘ p（纯增量，不加 rest.translation）
"""
import os, sys, json
import bpy
from mathutils import Quaternion, Vector

def _args():
    a = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    o, i = {}, 0
    while i < len(a):
        if a[i].startswith("--"):
            o[a[i][2:]] = a[i+1] if i+1 < len(a) else ""
            i += 2
        else:
            i += 1
    return o

A = _args()
SKEL, TRFDIR, OUT = A["skel"], A["trfdir"], A["out"]
STRIDE = int(A.get("stride", "3"))
FILT = A.get("filter", "")
ROOT = A.get("root", "pelvis")

def read_trf(path):
    with open(path, encoding="utf-8") as f:
        L = [ln.rstrip("\r\n") for ln in f]
    name = L[2].split()[0]
    nb = int(L[3]); i = 4; rots = []
    for _ in range(nb):
        n = int(L[i]); i += 1; fr = []
        for _k in range(n):
            p = L[i].split(); i += 1
            fr.append((int(p[0]), (float(p[1]), float(p[2]), float(p[3]), float(p[4]))))
        rots.append(fr)
    np_ = int(L[i]); i += 1; pos = []
    for _k in range(np_):
        p = L[i].split(); i += 1
        pos.append((int(p[0]), (float(p[1]), float(p[2]), float(p[3]))))
    return name, rots, pos

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SKEL)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
if arm.animation_data:
    arm.animation_data.action = None
bones = list(arm.pose.bones)
names = [b.name for b in bones]
def rest_local(pb):
    if pb.parent:
        return pb.parent.bone.matrix_local.inverted() @ pb.bone.matrix_local
    return pb.bone.matrix_local.copy()
restq = [rest_local(pb).to_quaternion() for pb in bones]
invq  = [q.inverted() for q in restq]
root_i = names.index(ROOT) if ROOT in names else 0
rest3 = rest_local(bones[root_i]).to_3x3()
inv3 = rest3.inverted()
IDX = {n: i for i, n in enumerate(names)}
HAND_R = IDX.get('r_hand'); HAND_L = IDX.get('l_hand')
HEAD = IDX.get('head'); PELV = IDX.get('pelvis'); TOE_R = IDX.get('r_toe0'); TOE_L = IDX.get('l_toe0')

files = sorted(f for f in os.listdir(TRFDIR) if f.endswith('.trf'))
if FILT:
    files = [f for f in files if FILT in f]

res = []
for fn in files:
    clip = fn[:-4]
    try:
        _n, rots, pos = read_trf(os.path.join(TRFDIR, fn))
    except Exception as e:
        print("  skip", fn, e); continue
    if len(rots) != len(bones):
        print("  bones mismatch", clip); continue
    L = len(rots[0])
    best = None
    for fi in range(0, L, STRIDE):
        for bi, pb in enumerate(bones):
            f, q = rots[bi][fi]
            pb.rotation_mode = 'QUATERNION'
            pb.rotation_quaternion = invq[bi] @ Quaternion((q[3], q[0], q[1], q[2]))
        if pos:
            f, p = pos[fi]
            bones[root_i].location = inv3 @ Vector(p)
        bpy.context.view_layer.update()
        W = arm.matrix_world
        hr = W @ bones[HAND_R].tail if HAND_R is not None else Vector((0,0,0))
        hl = W @ bones[HAND_L].tail if HAND_L is not None else Vector((0,0,0))
        hd = W @ bones[HEAD].tail if HEAD is not None else Vector((0,0,0))
        pv = W @ bones[PELV].head if PELV is not None else Vector((0,0,0))
        tr = W @ bones[TOE_R].head if TOE_R is not None else Vector((0,0,0))
        tl = W @ bones[TOE_L].head if TOE_L is not None else Vector((0,0,0))
        # 手过头顶多少（世界 Z，cm 制 → 转米除以 100? 骨架单位 cm，保持原样判断相对量）
        above = hr.z - hd.z
        rec = dict(frame=rots[0][fi][0], handR_z=hr.z, head_z=hd.z, above=above,
                   handL_z=hl.z, handL_above=hl.z - hd.z, pelvis_z=pv.z,
                   foot_z=min(tr.z, tl.z), handR_x=hr.x, handR_y=hr.y, head_x=hd.x, head_y=hd.y)
        if best is None or rec["above"] > best["above"]:
            best = rec
    if best is None: continue
    best["clip"] = clip
    best["frames"] = L
    res.append(best)
    print("  %-42s above=%8.1f (frame %d)" % (clip, best["above"], best["frame"]))

res.sort(key=lambda r: -r["above"])
with open(OUT, "w", encoding="utf-8") as f:
    json.dump(res, f, ensure_ascii=False, indent=1)
print("\n=== TOP 25 右手过头顶 ===")
for r in res[:25]:
    print("  %-42s above=%7.1f cm  handR=(%.0f,%.0f,%.0f) head_z=%.0f pelvis_z=%.0f foot_z=%.1f frame=%d"
          % (r["clip"], r["above"], r["handR_x"], r["handR_y"], r["handR_z"], r["head_z"], r["pelvis_z"], r["foot_z"], r["frame"]))
print("\nDone ->", OUT)
