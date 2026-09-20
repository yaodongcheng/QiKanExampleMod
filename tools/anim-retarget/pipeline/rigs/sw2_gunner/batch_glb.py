#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""批量把 SW2 铁炮兵的全部动画重定向到骑砍2骨架，导出为单个 GLB（供 three.js 查看器使用）。
   公式与 retarget_sw2_to_bannerlord.py 的 align 模式一致。
用法: blender -b --python batch_retarget_glb.py -- --out web/assets/bannerlord_from_sw2.glb [--max 0]
"""
import bpy, sys, os, json, argparse, math, time
from mathutils import Euler

# --- 路径基准：自动向上查找含 pipeline/ 与 input/ 的项目根 ---
import os as _os
def _project_root(p):
    for _ in range(6):
        if _os.path.isdir(_os.path.join(p, "pipeline")) and _os.path.isdir(_os.path.join(p, "input")):
            return p
        p = _os.path.dirname(p)
    return _os.path.dirname(p)
PROJECT_ROOT = _project_root(_os.path.dirname(_os.path.abspath(__file__)))

HERE = os.path.dirname(os.path.abspath(__file__))
D = dict(src=os.path.join(PROJECT_ROOT, "input/source/sw2_gunner/L256_GUNNER_anim.gltf"),
         tgt=os.path.join(PROJECT_ROOT, "input/target/bannerlord/human_lod_4.fbx"),
         map=os.path.join(PROJECT_ROOT, "pipeline", "rigs", "sw2_gunner", "map.json"),
         out=os.path.join(PROJECT_ROOT, "viewer", "datasets", "sw2_gunner", "assets", "bannerlord_from_sw2.glb"),
         fps=30, max=0, only="")
def parse():
    a = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    for k, v in D.items(): ap.add_argument("--"+k, default=v)
    return ap.parse_args(a)
def log(m): print("[batch] %s" % m, flush=True)

args = parse()
try: args.max = int(args.max)
except Exception: args.max = 0
try: args.fps = int(args.fps)
except Exception: args.fps = 30
cfg = json.load(open(args.map, encoding="utf-8"))
mapping = cfg["bone_map"]
src_root = cfg["meta"]["root_bone_source"]
src_root_rot = cfg["meta"].get("root_rotation_source", src_root)
tgt_root = cfg["meta"]["root_bone_target"]
PREFIX = "RT_"

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.fps = args.fps
bpy.ops.import_scene.gltf(filepath=args.src)
src = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
src_acts = [a for a in bpy.data.actions]
log("源动作 %d 段" % len(src_acts))

before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=args.tgt)
tgt = [o for o in bpy.data.objects if o.type=='ARMATURE' and o not in before][-1]
log("目标 %s bones=%d" % (tgt.name, len(tgt.data.bones)))
# !!! 骑砍2 的 FBX 自带 24fps，导入后会把场景 fps 改回 24；
#     不重置回 30 的话，导出时帧->秒 会按 24 换算，整段动画慢 25%（1.25x）。
sc.render.fps = args.fps
log("重置场景 fps = %d（FBX 导入后会被改成 %d）" % (sc.render.fps, 24))
mapping = {s:t for s,t in mapping.items() if s in src.pose.bones and t in tgt.data.bones}
log("有效映射 %d" % len(mapping))

F = Euler((0.0,0.0,math.radians(180)),'XYZ').to_matrix()
def rot3(m): return m.to_3x3().normalized()
tgt_rest_w = {t: rot3(tgt.matrix_world @ tgt.data.bones[t].matrix_local) for t in mapping.values()}
src_rest_w = {s: rot3(src.matrix_world @ src.data.bones[s].matrix_local) for s in mapping}
tgt_order = []
def _walk(b):
    tgt_order.append(b.name)
    for c in b.children: _walk(c)
for b in tgt.data.bones:
    if b.parent is None: _walk(b)

# --- 静止对齐修正 A（与单条脚本 align 模式一致）---
def mapped_kids(arm, nm, is_src):
    kid=[]; stack=[c for c in arm.data.bones[nm].children]
    while stack:
        c=stack.pop(0)
        if (c.name in mapping) if is_src else (c.name in mapping.values()): kid.append(c.name)
        else: stack.extend(c.children)
    return kid
def sub_has(arm, nm, want):
    stack=[arm.data.bones[nm]]
    while stack:
        b=stack.pop()
        if b.name==want: return True
        stack.extend(b.children)
    return False
def out_dir(arm, nm, kid):
    v=(arm.matrix_world @ arm.data.bones[kid].matrix_local.translation) - \
      (arm.matrix_world @ arm.data.bones[nm].matrix_local.translation)
    return v if v.length>1e-6 else None
A_align={}
for s,t in mapping.items():
    ks=mapped_kids(src,s,True); kt=mapped_kids(tgt,t,False)
    if not ks or not kt: continue
    if len(ks)==1 and len(kt)==1: cs,ct=ks[0],kt[0]
    else:
        cs=next((k for k in ks if sub_has(src,k,"bone_11")),None)
        ct=next((k for k in kt if sub_has(tgt,k,"head")),None)
        if not cs or not ct: continue
    vs=out_dir(src,s,cs); vt=out_dir(tgt,t,ct)
    if vs is None or vt is None: continue
    A_align[t]=((F@vs).normalized()).rotation_difference(vt.normalized())
log("静止对齐修正覆盖 %d 骨" % len(A_align))

def bake_one(act, name):
    """把源动作 act 以 align 模式烘焙成目标上的新动作 name"""
    if src.animation_data is None: src.animation_data_create()
    src.animation_data.action = act
    try:
        if hasattr(act,"slots") and len(act.slots): src.animation_data.action_slot = act.slots[0]
    except Exception: pass
    # 源 glTF 的关键帧可能落在非整数帧上（如 14.5 帧），int() 会截断尾巴；
    # 起始用 round、结束用 ceil，保证整段时长被完整覆盖。
    fs = int(round(act.frame_range[0])); fe = int(math.ceil(act.frame_range[1]))
    for a in list(bpy.data.actions):
        if a.name.startswith(PREFIX) and a is not None:
            pass
    newact = bpy.data.actions.new(PREFIX + name)
    if tgt.animation_data is None: tgt.animation_data_create()
    tgt.animation_data.action = newact
    try:
        if hasattr(newact,"slots"):
            newact.slots.new(id_type='OBJECT', name=tgt.name)
            tgt.animation_data.action_slot = newact.slots[0]
    except Exception as e: log("  slot warn %s" % e)
    for f in range(fs, fe+1):
        sc.frame_set(f); bpy.context.view_layer.update()
        W={}
        for s,t in mapping.items():
            s_use = src_root_rot if (t==tgt_root and s==src_root and src_root_rot in src.pose.bones) else s
            sp = rot3(src.matrix_world @ src.pose.bones[s_use].matrix)
            rp = rot3(src.matrix_world @ src.data.bones[s_use].matrix_local)
            R = F @ (sp @ rp.inverted()) @ F.transposed()
            A = A_align.get(t)
            W[t] = (R @ A.inverted().to_matrix()) @ tgt_rest_w[t] if A is not None else (R @ tgt_rest_w[t])
        for t in tgt_order:
            if t not in W: continue
            pb = tgt.pose.bones[t]
            cur = tgt.matrix_world @ pb.matrix
            new = W[t].to_4x4(); new.translation = cur.translation
            pb.matrix = tgt.matrix_world.inverted() @ new
            bpy.context.view_layer.update()
            pb.rotation_mode='QUATERNION'
            pb.keyframe_insert(data_path="rotation_quaternion", frame=f)
    gb=[b for b in ("l_toe0","r_toe0","l_foot","r_foot") if b in tgt.pose.bones]
    for f in range(fs, fe+1):
        sc.frame_set(f); bpy.context.view_layer.update()
        minz=min((tgt.matrix_world @ tgt.pose.bones[b].head).z for b in gb)
        pb=tgt.pose.bones[tgt_root]
        m=pb.matrix.copy(); m.translation.z-=minz
        pb.matrix=m; bpy.context.view_layer.update()
        pb.keyframe_insert(data_path="location", frame=f)
    try: newact.use_fake_user=True
    except Exception: pass
    return fs, fe

# --- 逐段烘焙 ---
only = [x for x in args.only.split(",") if x] if args.only else None
targets = [a for a in src_acts if (only is None or a.name in only)]
if args.max: targets = targets[:args.max]
log("将烘焙 %d 段" % len(targets))
manifest=[]
t0=time.time()
for i,a in enumerate(targets,1):
    try:
        fs,fe = bake_one(a, a.name)
        manifest.append({"name": a.name, "dur": round((fe-fs)/args.fps, 3), "frames": [fs,fe]})
        log("  [%2d/%d] %-24s 帧 %d..%d  (累计 %.0fs)" % (i, len(targets), a.name, fs, fe, time.time()-t0))
    except Exception as e:
        log("  [%2d/%d] %s 失败: %s" % (i, len(targets), a.name, e))
log("烘焙完成，用时 %.0fs" % (time.time()-t0))

# --- 清掉源对象与源动作，只留目标 ---
if src.animation_data: src.animation_data_clear()
keep = set([tgt]) | set(tgt.children)
for o in list(bpy.data.objects):
    if o not in keep: bpy.data.objects.remove(o, do_unlink=True)
for a in list(bpy.data.actions):
    if not a.name.startswith(PREFIX):
        try: bpy.data.actions.remove(a)
        except Exception: pass
log("导出前 action: %d 个" % len(bpy.data.actions))
log("保留对象: %s" % [o.name for o in bpy.data.objects])

os.makedirs(os.path.dirname(args.out), exist_ok=True)
try:
    bpy.ops.export_scene.gltf(filepath=args.out, export_format='GLB',
        export_animations=True, export_animation_mode='ACTIONS',
        export_skins=True, export_apply=False, use_selection=False,
        export_frame_step=1, export_bake_animation=True,
        export_optimize_animation_size=False)
except TypeError as e:
    log("部分导出参数不被支持，改用精简参数: %s" % e)
    bpy.ops.export_scene.gltf(filepath=args.out, export_format='GLB',
        export_animations=True, export_animation_mode='ACTIONS', use_selection=False)
log("导出: %s (%.1f MB)" % (args.out, os.path.getsize(args.out)/1048576))
json.dump({"animations": manifest}, open(os.path.join(os.path.dirname(args.out),"bannerlord_anims.json"),"w",encoding="utf-8"), ensure_ascii=False, indent=1)
log("DONE")
