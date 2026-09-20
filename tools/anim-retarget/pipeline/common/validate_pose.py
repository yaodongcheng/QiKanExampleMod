#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""数值校验：对比 源动画 与 重定向后目标骨架 的每骨「世界空间姿态旋转增量」差异。
差异越小 = 越忠实还原源的世界空间动作（这正是重定向目标）。
用法: blender --background --python validate_pose.py -- <source.fbx> <target_fbx> <target2_fbx_optional> <frame>
"""
import bpy, sys
from mathutils import Matrix


# --- 路径基准：自动向上查找含 pipeline/ 与 input/ 的项目根 ---
import os as _os
def _project_root(p):
    for _ in range(6):
        if _os.path.isdir(_os.path.join(p, "pipeline")) and _os.path.isdir(_os.path.join(p, "input")):
            return p
        p = _os.path.dirname(p)
    return _os.path.dirname(p)
PROJECT_ROOT = _project_root(_os.path.dirname(_os.path.abspath(__file__)))

def arg(i):
    a=sys.argv
    if "--" not in a: return None
    b=a[a.index("--")+1:]
    return b[i] if i<len(b) else None

SRC=arg(0); TGT=arg(1); TGT2=arg(2); FRAME=int(arg(3) or "1")
MAP=os.path.join(PROJECT_ROOT, "pipeline/rigs/ue_mannequin/map.json")
import json
cfg=json.load(open(MAP,encoding="utf-8")); mapping=cfg["bone_map"]

def rot3(m): return m.to_3x3().normalized()
def swap(n):
    if n.startswith("l_"): return "r_"+n[2:]
    if n.startswith("r_"): return "l_"+n[2:]
    if n.endswith("_l"): return n[:-2]+"_r"
    if n.endswith("_r"): return n[:-2]+"_l"
    return None

def load_target(fbx):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx)
    return next((o for o in bpy.data.objects if o.type=='ARMATURE'),None)

def deltas(arm):
    """返回 {tgt_bone: worldDelta 3x3}，右侧 tgt 用源左侧骨(镜像) 的约定在主体里处理。"""
    d={}
    world_rot_delta={}
    for s,t in mapping.items():
        if t not in arm.data.bones: continue
        # 实际用的源骨：右侧 tgt 用 swap(s)
        sn = swap(s) if t.startswith("r_") else s
        if sn not in arm.data.bones: continue
        src_rest=rot3(arm.matrix_world @ arm.data.bones[sn].matrix_local)
        src_pose=rot3(arm.matrix_world @ arm.pose.bones[sn].matrix)
        D=src_pose @ src_rest.inverted()
        world_rot_delta[t]=D
    return world_rot_delta

# 源（预计算所有数据为普通矩阵拷贝，避免引用被后续 read_factory_settings 清掉）
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
src=next((o for o in bpy.data.objects if o.type=='ARMATURE'),None)
src.animation_data.action = bpy.data.actions[0]
if hasattr(src.animation_data.action,"slots") and src.animation_data.action_slot is None:
    try: src.animation_data.action_slot=src.animation_data.action.slots[0]
    except: pass
bpy.context.scene.frame_set(FRAME); bpy.context.view_layer.update()
M=Matrix(((-1,0,0),(0,1,0),(0,0,1)))
srce=dict()
src_mw = src.matrix_world.copy()
SRC_PELVIS_REST = rot3(src_mw @ src.data.bones["pelvis"].matrix_local)
src_delta={}
for s,t in mapping.items():
    sn = swap(s) if t.startswith("r_") else s
    if sn not in src.data.bones: continue
    rest=rot3(src_mw @ src.data.bones[sn].matrix_local)
    pose=rot3(src_mw @ src.pose.bones[sn].matrix)
    D=pose @ rest.inverted()
    if t.startswith("r_"): D=M @ D @ M     # 目标右侧用镜像后的源左侧增量
    src_delta[t]=D.copy()

def load_and_score(fbx,label):
    arm=load_target(fbx)
    t_pelvis = rot3(arm.matrix_world @ arm.data.bones["pelvis"].matrix_local)
    R_align = t_pelvis @ SRC_PELVIS_REST.inverted()
    if arm.animation_data and arm.animation_data.action is None and bpy.data.actions:
        arm.animation_data.action=bpy.data.actions[0]
        try:
            if hasattr(arm.animation_data.action,"slots") and arm.animation_data.action_slot is None:
                arm.animation_data.action_slot=arm.animation_data.action.slots[0]
        except: pass
    bpy.context.scene.frame_set(FRAME); bpy.context.view_layer.update()
    worst=0.0; worst_bone=None; maxdiff=0.0; maxbone=None; tot=0.0; n=0
    rows=[]
    for s,t in mapping.items():
        if t not in arm.data.bones: continue
        t_rest=rot3(arm.matrix_world @ arm.data.bones[t].matrix_local)
        t_pose=rot3(arm.matrix_world @ arm.pose.bones[t].matrix)
        Dt=t_pose @ t_rest.inverted()
        # 与源对齐：D_src_against_target = R_align @ src_delta[t] @ R_align^-1
        Ds = R_align @ src_delta[t] @ R_align.inverted()
        q=(Ds.inverted() @ Dt).to_quaternion()
        ang=abs(q.angle)  # 弧度
        if ang>3.1415: ang=6.2831-ang
        deg=ang*57.2958
        rows.append((t,deg))
        tot+=deg*deg; n+=1
        if deg>maxdiff: maxdiff=deg; maxbone=t
    import math
    rms=math.sqrt(tot/n) if n else 0
    print("[%s] over %d bones: RMS=%.1f deg, worst=%s=%.1f deg" % (label,n,rms,maxbone,maxdiff))
    for t,d in sorted(rows,key=lambda x:-x[1])[:6]:
        print("    %-16s %.1f" % (t,d))

load_and_score(TGT,"NEW(world-delta)")
if TGT2:
    load_and_score(TGT2,"OLD(basis-copy)")
print("VALIDATE DONE")
