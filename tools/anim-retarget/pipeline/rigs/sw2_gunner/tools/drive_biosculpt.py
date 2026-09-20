#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""用 BioSculpt Retargeter 插件把 SW2 铁炮兵 p006 重定向到骑砍2骨架。
   流程：1 建代理骨架 -> 2 对齐 Roll -> 3 约束代理跟随源 -> 4 空物体桥接到目标 -> 5 烘焙+清理
用法: blender -b --python drive_biosculpt.py -- --name biosculpt_x [--hip pelvis|--hip '']
"""
import bpy, json, os, sys, argparse, addon_utils

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
         action="p006", outdir=os.path.join(HERE, "..", "..", "..", "..", "output", "verify"), name="biosculpt",
         fps=30, hip="pelvis", legik="false")
def parse():
    a = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    for k, v in D.items(): ap.add_argument("--"+k, default=v)
    return ap.parse_args(a)
def log(m): print("[BS] %s" % m, flush=True)

args = parse()
cfg = json.load(open(args.map, encoding="utf-8"))
pairs = cfg["bone_map"]

bpy.ops.wm.read_factory_settings(use_empty=True)
addon_utils.enable("biosculpt_retargeter", default_set=False)
addon_utils.enable("blender_BoneAnimCopy", default_set=False)
sc = bpy.context.scene
sc.render.fps = args.fps
bpy.ops.import_scene.gltf(filepath=args.src)
src = [o for o in bpy.data.objects if o.type=='ARMATURE'][0]
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=args.tgt)
tgt = [o for o in bpy.data.objects if o.type=='ARMATURE' and o not in before][-1]
log("源=%s 目标=%s" % (src.name, tgt.name))

# 骑砍2 的 FBX 自带 24fps，导入后会改掉场景 fps；不重置回 30 会让导出时长膨胀 25%
sc.render.fps = args.fps


act = bpy.data.actions.get(args.action)
if src.animation_data is None: src.animation_data_create()
src.animation_data.action = act
try:
    if hasattr(act, "slots") and len(act.slots): src.animation_data.action_slot = act.slots[0]
except Exception as e: log("slot %s" % e)
fs, fe = int(act.frame_range[0]), int(act.frame_range[1])
sc.frame_start, sc.frame_end = fs, fe
log("源动作 %s 帧 %d..%d" % (args.action, fs, fe))

p = sc.biosculpt_props
p.locale = "zh_CN"
p.source_armature = src
p.target_armature = tgt
p.use_auto_mirror = False
p.use_hip_offset = False
p.use_leg_ik = str(args.legik).lower() in ("1","true","yes")
p.hip_tgt_bone = args.hip
p.hip_src_bone = "bone_0"
p.root_src_bone = "bone_0"
p.bone_mapping_list.clear()
for sb, tb in pairs.items():
    if sb in src.pose.bones and tb in tgt.data.bones:
        it = p.bone_mapping_list.add(); it.source_bone = sb; it.target_bone = tb
log("映射 %d 条" % len(p.bone_mapping_list))

for i in range(5):
    try:
        r = bpy.ops.biosculpt.step_by_step_retarget()
    except Exception as e:
        log("step %d 异常: %s" % (i+1, e)); break
    log("step %d -> %s (state=%s)" % (i+1, r, p.retarget_step))
    if 'CANCELLED' in r: break

own = tgt
a2 = own.animation_data.action if own.animation_data else None
log("烘焙后 action: %s" % (a2.name if a2 else None))

# 贴地（与其它方案同基准）
gb = [b for b in ("l_toe0","r_toe0","l_foot","r_foot") if b in own.pose.bones]
if gb:
    for f in range(fs, fe+1):
        sc.frame_set(f); bpy.context.view_layer.update()
        minz = min((own.matrix_world @ own.pose.bones[b].head).z for b in gb)
        pb = own.pose.bones["pelvis"]
        m = pb.matrix.copy(); m.translation.z -= minz
        pb.matrix = m
        bpy.context.view_layer.update()
        pb.keyframe_insert(data_path="location", frame=f)
    log("贴地完成")

os.makedirs(args.outdir, exist_ok=True)
out = os.path.join(args.outdir, args.name + ".fbx")
keep = set([own]) | set(own.children)
for o in list(bpy.data.objects):
    if o not in keep:
        try: bpy.data.objects.remove(o, do_unlink=True)
        except Exception: pass
bpy.ops.export_scene.fbx(filepath=out, use_selection=False, apply_unit_scale=True,
    add_leaf_bones=False,
    axis_up='Z', axis_forward='-Y',   # 不加这个会凭空多出 5 根 *_end 叶骨（28->33），ModKit 直接报骨骼数不一致
    bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_all_actions=False,
    bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
    bake_anim_step=1.0, object_types={'ARMATURE'})
log("导出: %s" % out)
log("DONE")
