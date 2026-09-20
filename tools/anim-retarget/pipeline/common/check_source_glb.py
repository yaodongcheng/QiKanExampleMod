# -*- coding: utf-8 -*-
"""源 GLB 是否忠实：同骨名(UE)对同骨名，直接比肢段方向。"""
import bpy, math, sys, os, argparse

# --- 路径基准：自动向上查找含 pipeline/ 与 input/ 的项目根 ---
import os as _os
def _project_root(p):
    for _ in range(6):
        if _os.path.isdir(_os.path.join(p, "pipeline")) and _os.path.isdir(_os.path.join(p, "input")):
            return p
        p = _os.path.dirname(p)
    return _os.path.dirname(p)
PROJECT_ROOT = _project_root(_os.path.dirname(_os.path.abspath(__file__)))

ROOT = PROJECT_ROOT
PAR={"spine_01":"pelvis","spine_02":"spine_01","spine_03":"spine_02","neck_01":"spine_03","head":"neck_01",
     "clavicle_l":"spine_03","upperarm_l":"clavicle_l","lowerarm_l":"upperarm_l","hand_l":"lowerarm_l",
     "clavicle_r":"spine_03","upperarm_r":"clavicle_r","lowerarm_r":"upperarm_r","hand_r":"lowerarm_r",
     "thigh_l":"pelvis","calf_l":"thigh_l","foot_l":"calf_l","ball_l":"foot_l",
     "thigh_r":"pelvis","calf_r":"thigh_r","foot_r":"calf_r","ball_r":"foot_r",
     "upperarm_twist_01_l":"upperarm_l","lowerarm_twist_01_l":"lowerarm_l",
     "upperarm_twist_01_r":"upperarm_r","lowerarm_twist_01_r":"lowerarm_r",
     "thigh_twist_01_l":"thigh_l","calf_twist_01_l":"calf_l",
     "thigh_twist_01_r":"thigh_r","calf_twist_01_r":"calf_r"}
ap=argparse.ArgumentParser(); ap.add_argument("--clip", default="010_01")
a=ap.parse_args(sys.argv[sys.argv.index("--")+1:])
def import_src():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT,"input","source","ue_mannequin","clips_basic",a.clip+".fbx"))
    arm=next(o for o in bpy.data.objects if o.type=='ARMATURE'); fps=bpy.context.scene.render.fps
    return arm, fps, arm.animation_data.action.frame_range
def import_glb(p, act):
    bpy.ops.wm.read_factory_settings(use_empty=True); bpy.context.scene.render.fps=30
    bpy.ops.import_scene.gltf(filepath=p)
    arm=next(o for o in bpy.data.objects if o.type=='ARMATURE')
    ac=bpy.data.actions.get(act)
    if ac is None: return None,None
    if arm.animation_data is None: arm.animation_data_create()
    arm.animation_data.action=ac
    try:
        if hasattr(ac,"slots") and len(ac.slots): arm.animation_data.action_slot=ac.slots[0]
    except Exception: pass
    return arm, ac.frame_range
def dirs(arm):
    o={}
    for b,pa in PAR.items():
        if b not in arm.pose.bones or pa not in arm.pose.bones: continue
        v=(arm.matrix_world @ arm.pose.bones[b].head)-(arm.matrix_world @ arm.pose.bones[pa].head)
        if v.length>1e-9: o[b]=v.normalized()
    return o
src,fps,fr=import_src(); dur=(fr[1]-fr[0])/fps
N=21; S=[]
for i in range(N):
    f=fr[0]+dur*i/(N-1)*fps
    bpy.context.scene.frame_set(int(f), subframe=f-int(f)); bpy.context.view_layer.update(); S.append(dirs(src))
print("\n=== 源 %s  fps=%d 时长 %.3f s  可测骨 %d ==="%(a.clip,fps,dur,len(S[0])))
arm,gfr=import_glb(os.path.join(ROOT,"output/glb/ue_basic","ue_mannequin_src.glb"), a.clip)
if arm is None: print("  !! 源 GLB 无该动作"); raise SystemExit
T=[]
for i in range(N):
    f=gfr[0]+dur*i/(N-1)*30
    bpy.context.scene.frame_set(int(f), subframe=f-int(f)); bpy.context.view_layer.update(); T.append(dirs(arm))
vals=[]; per={}
for i in range(N):
    for b,v in S[i].items():
        if b not in T[i]: continue
        x=math.degrees(math.acos(max(-1,min(1,v.dot(T[i][b]))))); vals.append(x); per.setdefault(b,[]).append(x)
print("  源 GLB vs 源 FBX 相邻帧差: mean %.2f°  max %.2f°  (可测骨 %d)"%(sum(vals)/len(vals),max(vals),len(per)))
bad=sorted(per,key=lambda k:-sum(per[k])/len(per[k]))[:5]
print("  最差骨:", ", ".join("%s %.1f°"%(k,sum(per[k])/len(per[k])) for k in bad))
