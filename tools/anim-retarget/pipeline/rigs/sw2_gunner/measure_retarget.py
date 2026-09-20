#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""对任意"重定向结果 FBX"算同一口径的肢段方向误差（与骨骼轴向约定无关）。
输出 JSON: {meta:{...}, rows:[{f, <tgt_bone>:{src_dir:[..], tgt_dir:[..]}}]}
用法: blender -b --python measure_retarget.py -- --cand out/x.fbx --name X
"""
import bpy, json, os, sys, argparse, math
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
         action="p006", cand="", name="cand", outdir=os.path.join(HERE, "..", "..", "..", "output", "verify"), fps=30)
def parse():
    a = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    for k, v in D.items(): ap.add_argument("--"+k, default=v)
    return ap.parse_args(a)
def log(m): print("[measure] %s" % m, flush=True)

args = parse()
cfg = json.load(open(args.map, encoding="utf-8"))
pairs = cfg["bone_map"]              # src -> tgt
T2S = {t: s for s, t in pairs.items()}

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene; sc.render.fps = args.fps
bpy.ops.import_scene.gltf(filepath=args.src)
src = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
act = bpy.data.actions.get(args.action)
if src.animation_data is None: src.animation_data_create()
src.animation_data.action = act
try:
    if hasattr(act, "slots") and len(act.slots): src.animation_data.action_slot = act.slots[0]
except Exception: pass
fs, fe = int(act.frame_range[0]), int(act.frame_range[1])

before = set(bpy.data.objects)
before_acts = set(a.name for a in bpy.data.actions)
bpy.ops.import_scene.fbx(filepath=(args.cand or args.tgt))
cand = [o for o in bpy.data.objects if o.type=='ARMATURE' and o not in before][-1]
cand.animation_data_clear()
new_acts = [a for a in bpy.data.actions if a.name not in before_acts]
cand_act = new_acts[0] if new_acts else None
if cand_act is None: log("!! 未找到结果动作"); raise SystemExit
if cand.animation_data is None: cand.animation_data_create()
cand.animation_data.action = cand_act
try:
    if hasattr(cand_act, "slots") and len(cand_act.slots): cand.animation_data.action_slot = cand_act.slots[0]
except Exception: pass
log("源=%s 候选=%s action=%s" % (src.name, cand.name, cand_act.name))

F = Euler((0.0, 0.0, math.radians(180)), 'XYZ').to_matrix()

def mapped_parent(arm, nm, is_src):
    b = arm.data.bones.get(nm)
    while b and b.parent:
        b = b.parent
        if (b.name in pairs) if is_src else (b.name in pairs.values()):
            return b.name
    return None
PAR_S, PAR_T = {}, {}
for s, t in pairs.items():
    if s not in src.data.bones or t not in cand.data.bones: continue
    ps = mapped_parent(src, s, True); pt = mapped_parent(cand, t, False)
    if ps: PAR_S[s] = ps
    if pt: PAR_T[t] = pt

def sdirm(arm, nm, pmap, pose=True, Fm=None):
    p = pmap.get(nm)
    if not p: return None
    if pose:
        a = arm.matrix_world @ arm.pose.bones[nm].head
        b = arm.matrix_world @ arm.pose.bones[p].head
    else:
        a = arm.matrix_world @ arm.data.bones[nm].matrix_local.translation
        b = arm.matrix_world @ arm.data.bones[p].matrix_local.translation
    v = a - b
    if v.length < 1e-9: return None
    v = v.normalized()
    if Fm is not None: v = Fm @ v
    return v

rows = []
for f in range(fs, fe+1):
    sc.frame_set(f); bpy.context.view_layer.update()
    row = {"f": f}
    for s, t in pairs.items():
        if s not in PAR_S or t not in PAR_T: continue
        vs = sdirm(src, s, PAR_S, True, F); vt = sdirm(cand, t, PAR_T, True, None)
        if vs and vt:
            row[t] = {"src_dir": [round(x,6) for x in vs], "tgt_dir": [round(x,6) for x in vt]}
    rows.append(row)

rest = {}
for s, t in pairs.items():
    if s not in PAR_S or t not in PAR_T: continue
    vs = sdirm(src, s, PAR_S, False, F); vt = sdirm(cand, t, PAR_T, False, None)
    if vs and vt:
        c = max(-1.0, min(1.0, vs.dot(vt)))
        rest[t] = {"src": [round(x,6) for x in vs], "tgt": [round(x,6) for x in vt],
                   "rest_gap_deg": round(math.degrees(math.acos(c)), 2)}

os.makedirs(args.outdir, exist_ok=True)
out = os.path.join(args.outdir, "measure_%s.json" % args.name)
json.dump({"meta": {"name": args.name, "cand": args.cand, "frames": [fs, fe],
                    "seg_bones": sorted(set(PAR_T.keys()))},
           "rest_dirs": rest, "rows": rows},
          open(out, "w", encoding="utf-8"), ensure_ascii=False)
log("写入 %s (%d 帧)" % (out, len(rows)))
