# -*- coding: utf-8 -*-
"""用 Blender 口径校验查看器用的两个 GLB：源 GLB 是否忠实 / 骑砍 GLB 重定向质量。
   穷举 8 种朝向变换取最优。用法: blender -b --python glb_check.py -- --clip 010_01
"""
import bpy, sys, os, json, math, argparse
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

ROOT = PROJECT_ROOT
MAP  = json.load(open(os.path.join(ROOT,"pipeline","rigs","ue_mannequin","map.json"), encoding="utf-8"))["bone_map"]
def parse():
    a = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(); ap.add_argument("--clip", default="010_01")
    ap.add_argument("--cand", default="")
    return ap.parse_args(a)
args = parse()

def import_src():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT,"input","source","ue_mannequin","clips_basic",args.clip+".fbx"))
    arm = next(o for o in bpy.data.objects if o.type=='ARMATURE')
    fps = bpy.context.scene.render.fps
    act = arm.animation_data.action
    return arm, fps, act.frame_range

def import_glb(path, action):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 30
    bpy.ops.import_scene.gltf(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type=='ARMATURE')
    act = bpy.data.actions.get(action)
    if act is None:
        print("   !! GLB 里没有动作 %s（有 %s）"%(action, [a.name for a in bpy.data.actions][:5])); return None, None
    if arm.animation_data is None: arm.animation_data_create()
    arm.animation_data.action = act
    try:
        if hasattr(act,"slots") and len(act.slots): arm.animation_data.action_slot = act.slots[0]
    except Exception: pass
    return arm, act.frame_range

def mp(arm, name, is_src):
    b = arm.data.bones.get(name)
    while b and b.parent:
        b = b.parent
        if (b.name in MAP) if is_src else (b.name in MAP.values()): return b.name
    return None

def dirs(arm, is_src):
    out = {}
    for s,t in MAP.items():
        nm = s if is_src else t
        if nm not in arm.pose.bones: continue
        p = mp(arm, nm, is_src)
        if not p: continue
        v = (arm.matrix_world @ arm.pose.bones[nm].head) - (arm.matrix_world @ arm.pose.bones[p].head)
        if v.length > 1e-9: out[t] = v.normalized()
    return out

F = [Matrix.Rotation(math.radians(d), 3, 'Z') for d in (0,90,180,-90)]
MIR = Matrix(((-1,0,0),(0,1,0),(0,0,1)))
def best(S, T):
    bestv=None
    for i,R in enumerate(F):
        for mir in (False,True):
            vals=[]
            for k in range(len(S)):
                for b,v in S[k].items():
                    if b not in T[k]: continue
                    w = MIR @ v if mir else v.copy()
                    w = R @ w
                    vals.append(math.degrees(math.acos(max(-1,min(1,w.dot(T[k][b]))))))
            if not vals: continue
            m=sum(vals)/len(vals)
            if bestv is None or m<bestv[0]: bestv=(m,max(vals),("rotZ%+d"%((i*90)%360-180 if i==2 else [0,90,180,-90][i]))+("+镜像" if mir else ""))
    return bestv

src, fps, sfr = import_src()
dur = (sfr[1]-sfr[0])/fps
N = 21
times = [dur*i/(N-1) for i in range(N)]
S = []
for t in times:
    fr = sfr[0] + t*fps
    bpy.context.scene.frame_set(int(fr), subframe=fr-int(fr)); bpy.context.view_layer.update()
    S.append(dirs(src, True))
print("\n=== %s ===  源 %s  fps=%d  时长 %.3f s" % (args.clip, args.clip+".fbx", fps, dur))

CANDS = ([("候选", args.cand, args.clip)] if args.cand else
         [("骑砍 GLB 含扭骨", os.path.join(ROOT,"output/glb/ue_basic","bannerlord_from_ue.glb"), args.clip),
          ("骑砍 GLB(已重建,不含扭骨)", os.path.join(ROOT,"output/glb/ue_basic","bannerlord_from_ue.glb"), args.clip)])
for nm, p, act in CANDS:
    arm, fr = import_glb(p, act)
    if arm is None: continue
    T = []
    for t in times:
        f = fr[0] + t*30
        bpy.context.scene.frame_set(int(f), subframe=f-int(f)); bpy.context.view_layer.update()
        T.append(dirs(arm, False))
    b = best(S, T)
    print("  %-20s 时长 %.3f s | 最优肢段方向差 mean %5.2f°  max %6.2f°  [%s]"
          % (nm, (fr[1]-fr[0])/30.0, b[0], b[1], b[2]))
