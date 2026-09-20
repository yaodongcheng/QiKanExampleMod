#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""用 kumopult/blender_BoneAnimCopy 插件把 SW2 铁炮兵 p006 重定向到骑砍2骨架。
   原理：在 owner(骑砍) 骨上加 COPY_ROTATION(WORLD)+常量旋转偏移约束，再 nla.bake。
   关键：建立映射时两边都必须在 rest，插件才会算出正确的"静止姿态差"偏移。
用法: blender -b --python drive_boneanimcopy.py -- --pose ... --name ...
"""
import bpy, json, os, sys, math, argparse

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
         action="p006", outdir=os.path.join(HERE, "..", "..", "..", "..", "output", "verify"), name="bac", fps=30,
         ortho="false")
def parse():
    a = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    for k, v in D.items(): ap.add_argument("--"+k, default=v)
    return ap.parse_args(a)
def log(m): print("[BAC] %s" % m, flush=True)

args = parse()
cfg = json.load(open(args.map, encoding="utf-8"))
pairs = cfg["bone_map"]

bpy.ops.wm.read_factory_settings(use_empty=True)
import addon_utils
for _m in ("blender_BoneAnimCopy", "biosculpt_retargeter"):
    try: addon_utils.enable(_m, default_set=False)
    except Exception as _e: log("enable %s err %s" % (_m, _e))
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


# --- 关键：先让两边都处于 rest 姿态，插件才能算出正确的静止差偏移 ---
if src.animation_data: src.animation_data.action = None
for pb in list(src.pose.bones) + list(tgt.pose.bones):
    pb.location = (0,0,0); pb.rotation_quaternion = (1,0,0,0); pb.rotation_euler = (0,0,0); pb.scale=(1,1,1)
sc.frame_set(0); bpy.context.view_layer.update()

sc.kumopult_bac_owner = tgt
st = tgt.data.kumopult_bac
st.ortho_offset = str(args.ortho).lower() in ("1","true","yes")
st.calc_offset = True
st.preview = True
st.selected_target = src
sc.frame_set(0); bpy.context.view_layer.update()

for i in range(len(st.mappings)-1, -1, -1): st.mappings.remove(i)
for sb, tb in pairs.items():
    if sb in src.pose.bones and tb in tgt.data.bones:
        st.add_mapping(tb, sb)        # owner=骑砍骨, target=源骨
log("映射 %d 条" % len(st.mappings))
for m in st.mappings:
    off = tuple(round(math.degrees(x), 1) for x in m.offset)
    log("   %-22s <- %-10s rotoffs=%s offset=%s" % (m.owner, m.target, m.has_rotoffs, off))

# --- 挂上源动作 ---
act = bpy.data.actions.get(args.action)
if act is None:
    log("!! 找不到动作 %s" % args.action); raise SystemExit
if src.animation_data is None: src.animation_data_create()
src.animation_data.action = act
try:
    if hasattr(act, "slots") and len(act.slots): src.animation_data.action_slot = act.slots[0]
except Exception as e: log("slot %s" % e)
fs, fe = int(act.frame_range[0]), int(act.frame_range[1])
sc.frame_start, sc.frame_end = fs, fe
log("源动作 %s 帧 %d..%d" % (args.action, fs, fe))

r = bpy.ops.kumopult_bac.bake()
log("bake -> %s" % (r,))
own = sc.kumopult_bac_owner
log("烘焙后 action: %s" % (own.animation_data.action.name if own.animation_data and own.animation_data.action else None))

# 贴地：把脚最低点拉到 0（与其它方案同基准）
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
for o in list(bpy.data.objects):
    if o not in set([own]) | set(own.children):
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
