#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""3ds Max Biped (Bip001) -> 骑砍2 重定向。

与 ue_mannequin/retarget.py 的唯一结构性差异（也是本源的关键坑）：
    源资产的【绑定姿势】在动画文件里是【倒地姿势】——
    实测 230_daodibeibang.fbx 的 Bip001 根骨默认位姿 = 躺地（世界高 0.19 m），
    而 230_ske.fbx 的绑定姿势 = 站姿（世界高 1.3626 m）。
    若沿用"同一条 armature 的 matrix_local 当 rest"的旧逻辑，
    rest 与 pose 都是倒地 => 相对增量≈0 => 目标角色会一直站着（倒地姿势被约掉）。
    修法：rest(静姿) 取自 --skeleton（站姿骨架），pose 取自动画文件，两者世界系一致。
"""
import bpy, sys, os, json, math, argparse
from mathutils import Euler

import os as _os
def _project_root(p):
    for _ in range(6):
        if _os.path.isdir(_os.path.join(p, "pipeline")) and _os.path.isdir(_os.path.join(p, "input")):
            return p
        p = _os.path.dirname(p)
    return _os.path.dirname(p)
PROJECT_ROOT = _project_root(_os.path.dirname(_os.path.abspath(__file__)))
ROOT = PROJECT_ROOT
MAP  = json.load(open(os.path.join(PROJECT_ROOT, "pipeline/rigs/biped/map.json"), encoding="utf-8"))
PAIRS = MAP["bone_map"]
TGT_FBX = os.path.join(ROOT, "input", "target", "bannerlord", "human_lod_4.fbx")
SRC_ROOT = os.path.join(ROOT, "input", "source", "biped")
OUTDIR = os.path.join(ROOT, "output/fbx/biped")

def _src_paths(clip):
    """按 clip 前缀自动选角色目录与站姿骨架：230_* -> 男(230)，233_* -> 女(233)。"""
    tag = "233" if clip.startswith("233") else "230"
    d = os.path.join(SRC_ROOT, tag)
    return d, os.path.join(d, tag + "_ske.fbx")


def parse():
    a = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--clip", default="230_daodibeibang")
    ap.add_argument("--skeleton", default=None, help="站姿骨架 FBX（缺省按 clip 前缀自动选 230/233）")
    ap.add_argument("--flip", default="90")
    ap.add_argument("--pose", default="align")
    ap.add_argument("--animdir", default=None, help="源动画目录（缺省按 clip 前缀自动选 230/233）")
    ap.add_argument("--name", default=None)
    ap.add_argument("--outdir", default=None)
    ap.add_argument("--no_trf", default="false")
    ap.add_argument("--pelvis", default="ground", choices=["ground", "none", "src"])
    ap.add_argument("--root_basis", default=None)
    ap.add_argument("--yaw", default=None)
    ap.add_argument("--auto_yaw", default=None)
    ns = ap.parse_args(a)
    if not ns.name: ns.name = ns.clip
    if not ns.outdir: ns.outdir = OUTDIR
    _d, _sk = _src_paths(ns.clip)
    if not ns.animdir: ns.animdir = _d
    if not ns.skeleton: ns.skeleton = _sk
    ns.no_trf = str(ns.no_trf).lower() in ("1", "true", "yes")
    return ns
args = parse()
def log(m): print("[biped] %s" % m, flush=True)
def rot3(m): return m.to_3x3().normalized()

# TRF 直写用的公式（与 pipeline/common/fbx_to_trf.py 逐字一致）
def rest_local(pb):
    if pb.parent:
        return pb.parent.bone.matrix_local.inverted() @ pb.bone.matrix_local
    return pb.bone.matrix_local.copy()
def _abs_local(pb):
    if pb.parent:
        return pb.parent.matrix.inverted() @ pb.matrix
    return pb.matrix.copy()
def write_trf(name, rots, pos, out_trf):
    NL = "\n"
    contents = ["rfver 4" + NL, "skeleton_anim 1" + NL, " ".join([name, "1"]) + NL,
                " " + str(len(rots)) + NL]
    for bone in rots:
        contents.append(" " + str(len(bone)) + NL)
        for (t, x, y, z, w) in bone:
            contents.append(" %d %s %s %s %s" % (t, format(x, ".6f"), format(y, ".6f"),
                                                 format(z, ".6f"), format(w, ".6f")) + NL)
    contents.append(" " + str(len(pos)) + NL)
    for p in pos:
        contents.append(" %d %s %s %s" % (p[0], format(p[1], ".6f"),
                                          format(p[2], ".6f"), format(p[3], ".6f")) + NL)
    contents.append("end")
    os.makedirs(os.path.dirname(os.path.abspath(out_trf)), exist_ok=True)
    with open(out_trf, "w", encoding="utf-8") as f:
        f.writelines(contents)

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene

# ---------- 1) rest 骨架（站姿）----------
if not os.path.isfile(args.skeleton):
    log("!! 找不到骨架 FBX: %s" % args.skeleton); raise SystemExit(2)
log("rest 骨架: %s" % args.skeleton)
bpy.ops.import_scene.fbx(filepath=args.skeleton)
srest = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
log("  rest armature=%s bones=%d" % (srest.name, len(srest.data.bones)))

# ---------- 2) 动画（pose；其绑定姿势 = 倒地，仅取 pose）----------
_src_fbx = os.path.join(args.animdir, args.clip + ".fbx")
if not os.path.isfile(_src_fbx):
    import glob as _g
    _cand = _g.glob(os.path.join(args.animdir, "**", args.clip + ".fbx"), recursive=True)
    _src_fbx = _cand[0] if _cand else None
if _src_fbx is None:
    log("!! 找不到动画 FBX: %s" % args.clip); raise SystemExit(2)
log("动画 FBX: %s" % _src_fbx)
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=_src_fbx)
sanim = next(o for o in bpy.data.objects if o.type == 'ARMATURE' and o not in before)
SRC_FPS = sc.render.fps
sact = sanim.animation_data.action if sanim.animation_data else None
if sact is None:
    log("!! 动画文件里没有 action"); raise SystemExit(2)
fs, fe = int(round(sact.frame_range[0])), int(round(sact.frame_range[1]))
log("源 %s 帧 %d..%d = %.3f s @ %d fps" % (args.clip, fs, fe, (fe-fs)/SRC_FPS, SRC_FPS))

# ---------- 3) 目标（骑砍2 骨架）----------
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=TGT_FBX)
tgt = next(o for o in bpy.data.objects if o.type == 'ARMATURE' and o not in before)
sc.render.fps = SRC_FPS
log("导入目标后 场景 fps 已还原为 %d" % sc.render.fps)

PAIRS = {s: t for s, t in PAIRS.items() if s in sanim.pose.bones and s in srest.pose.bones and t in tgt.data.bones}
log("有效映射 %d 骨" % len(PAIRS))
F = Euler((0.0, 0.0, math.radians(float(args.flip))), 'XYZ').to_matrix()
log("帧变换 F = Rot(Z,%s deg)" % args.flip)

tgt_rest_w = {t: rot3(tgt.matrix_world @ tgt.data.bones[t].matrix_local) for t in PAIRS.values()}


def mapped_kids(arm, nm, is_src):
    kid = []; st = [c for c in arm.data.bones[nm].children]
    while st:
        c = st.pop(0)
        if (c.name in PAIRS) if is_src else (c.name in PAIRS.values()): kid.append(c.name)
        else: st.extend(c.children)
    return kid
def sub_has(arm, nm, want):
    st = [arm.data.bones[nm]]
    while st:
        b = st.pop()
        if b.name == want: return True
        st.extend(b.children)
    return False
def out_dir(arm, nm, kid):
    v = (arm.matrix_world @ arm.data.bones[kid].matrix_local.translation) - \
        (arm.matrix_world @ arm.data.bones[nm].matrix_local.translation)
    return v if v.length > 1e-6 else None
# 静姿对齐修正：用【rest 骨架】(站姿) 与目标的静姿方向算（不是动画里那个倒地静姿）
A_align = {}
for s, t in PAIRS.items():
    ks = mapped_kids(srest, s, True); kt = mapped_kids(tgt, t, False)
    if not ks or not kt: continue
    if len(ks) == 1 and len(kt) == 1: cs, ct = ks[0], kt[0]
    else:
        cs = next((k for k in ks if sub_has(srest, k, "head")), None)
        ct = next((k for k in kt if sub_has(tgt, k, "head")), None)
        if not cs or not ct: continue
    vs = out_dir(srest, s, cs); vt = out_dir(tgt, t, ct)
    if vs is None or vt is None: continue
    A_align[t] = ((F @ vs).normalized()).rotation_difference(vt.normalized())
log("静姿对齐修正覆盖 %d 骨" % len(A_align))

if tgt.animation_data: tgt.animation_data_clear()
tgt.animation_data_create()
newact = bpy.data.actions.new("RT_" + args.clip)
tgt.animation_data.action = newact
try:
    if hasattr(newact, "slots"):
        newact.slots.new(id_type='OBJECT', name=tgt.name); tgt.animation_data.action_slot = newact.slots[0]
except Exception as e: log("slot %s" % e)

order = []
def walk(b):
    order.append(b.name)
    for c in b.children: walk(c)
for b in tgt.data.bones:
    if b.parent is None: walk(b)

TGT_FPS = 30
SRC_STEP = float(SRC_FPS) / TGT_FPS
N_OUT = int(round((fe - fs) / SRC_STEP)) + 1
log("resample: src %d fps / %d frames -> out %d fps / %d frames" % (SRC_FPS, fe - fs + 1, TGT_FPS, N_OUT))

# 采样：pose 取自动画 armature(sanim)，rest 取自站姿 armature(srest)
for _i in range(N_OUT):
    _of = 1 + _i
    _sf = int(round(fs + _i * SRC_STEP))
    sc.frame_set(_sf); bpy.context.view_layer.update()
    W = {}
    for s, t in PAIRS.items():
        sp = rot3(sanim.matrix_world @ sanim.pose.bones[s].matrix)          # 当帧世界姿态
        rp = rot3(srest.matrix_world @ srest.data.bones[s].matrix_local)    # 站姿静姿（世界）
        R = F @ (sp @ rp.inverted()) @ F.transposed()
        A = A_align.get(t)
        W[t] = (R @ A.inverted().to_matrix()) @ tgt_rest_w[t] if A is not None else (R @ tgt_rest_w[t])
    for t in order:
        if t not in W: continue
        pb = tgt.pose.bones[t]
        cur = tgt.matrix_world @ pb.matrix
        nw = W[t].to_4x4(); nw.translation = cur.translation
        pb.matrix = tgt.matrix_world.inverted() @ nw
        bpy.context.view_layer.update()
        pb.rotation_mode = 'QUATERNION'
        pb.keyframe_insert(data_path="rotation_quaternion", frame=_of)

gb = [b for b in ("l_toe0", "r_toe0", "l_foot", "r_foot") if b in tgt.pose.bones]

if args.pelvis in ("ground", "src"):
    PB = tgt.pose.bones["pelvis"]
    rest_head = PB.bone.matrix_local.translation.copy()
    for _i in range(N_OUT):
        _of = 1 + _i
        sc.frame_set(_of); bpy.context.view_layer.update()
        m = PB.matrix.copy()
        m.translation = rest_head
        PB.matrix = m; bpy.context.view_layer.update()
        mz = min((tgt.matrix_world @ tgt.pose.bones[b].head).z for b in gb)
        m = PB.matrix.copy(); m.translation.z -= mz
        PB.matrix = m; bpy.context.view_layer.update()
        PB.keyframe_insert(data_path="location", frame=_of)
    log("骨盆位移轨完成（--pelvis %s 贴地）" % args.pelvis)
else:
    log("跳过骨盆位移轨（--pelvis %s）" % args.pelvis)

# ---------- TRF 采样（直写，不经 FBX）----------
# 为什么直写：Blender 5.2 的 FBX 导出器 (a) 会把【导出时当前帧的 pose】当成 bind pose 写出去
#   （实测 OUTPUT FBX 绑定骨盆 z 0.9145 -> 0.1286 = 躺姿）；(b) 会丢掉【根骨 pelvis 的
#   Lcl Translation 动画】（导出后该曲线恒 = 静止值 0.9145）。fbx_to_trf 于是按躺姿算
#   "相对静止姿势的增量" => 位移轨恒 0 => 目标角色悬空。
#   这里直接从【场景】按官方静止姿势 (bone.matrix_local) 采样 TRF —— 旋转=绝对局部变换、
#   平移=相对静止姿势的增量，与 common/fbx_to_trf.py 的公式一致。
TRF_ROTS = None; TRF_POS = None
if not args.no_trf:
    TRF_ROTS = [[] for _ in range(len(tgt.pose.bones))]
    TRF_POS = []
    for _f in range(1, N_OUT + 1):
        sc.frame_set(_f); bpy.context.view_layer.update()
        for _i2, _pb in enumerate(tgt.pose.bones):
            _q = _abs_local(_pb).to_quaternion().normalized()
            TRF_ROTS[_i2].append((_f, _q.x, _q.y, _q.z, _q.w))
        _rp = tgt.pose.bones["pelvis"]
        _loc = rest_local(_rp).to_3x3() @ _rp.location
        TRF_POS.append((_f, _loc.x, _loc.y, _loc.z))
    log("TRF 采样完成（直写）：%d 骨 x %d 帧，根骨=pelvis，首帧位置=(%.4f,%.4f,%.4f)"
        % (len(tgt.pose.bones), N_OUT, TRF_POS[0][1], TRF_POS[0][2], TRF_POS[0][3]))

# ---------- 导出 ModKit 规格（28 骨 / 只骨架 / Z-up / cm / 根名 human_skeleton_notused）----------
keep = set([tgt])
for c in tgt.children: keep.add(c)
for o in list(bpy.data.objects):
    if o not in keep:
        try:
            if o.type == 'ARMATURE' and o.animation_data: o.animation_data_clear()
        except Exception: pass
        bpy.data.objects.remove(o, do_unlink=True)
log("清理后保留对象: %s" % [o.name for o in bpy.data.objects])

tgt.name = "human_skeleton_notused"; tgt.data.name = "human_skeleton_notused"
sc.unit_settings.system = 'METRIC'; sc.unit_settings.scale_length = 1.0
sc.render.fps = TGT_FPS
sc.frame_start, sc.frame_end = 1, N_OUT
try:
    sc.frame_set(1)
    for _pb in tgt.pose.bones:
        _pb.matrix_basis.identity()
    tgt.update_tag()
    bpy.context.view_layer.update()
    log("导出前已清零 pose（保护 rest）")
except Exception as _e:
    log("清 pose 告警: %s" % _e)

os.makedirs(args.outdir, exist_ok=True)
out = os.path.join(args.outdir, args.name + ".fbx")
bpy.ops.export_scene.fbx(
    filepath=out, use_selection=False,
    object_types={'ARMATURE'},
    add_leaf_bones=False,
    axis_up='Z', axis_forward='-Y',
    primary_bone_axis='Y', secondary_bone_axis='X',
    apply_unit_scale=True, global_scale=1.0,
    bake_anim=True, bake_anim_use_all_bones=True,
    bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0,
    bake_anim_simplify_factor=0.0, path_mode='AUTO')
log("导出(ModKit 规格): %s" % out)

if not args.no_trf and TRF_ROTS is not None:
    trf_out = os.path.join(args.outdir, args.name + ".trf")
    write_trf(args.name, TRF_ROTS, TRF_POS, trf_out)
    _f0 = TRF_POS[0]
    log("TRF_NAME: 动画名 = '%s'（骨架对象名 human_skeleton_notused，两者解耦）" % args.name)
    log("CHECK_POS: 位置轨首帧 = (%.6f, %.6f, %.6f)  判定线: 首帧应 ≈ 0（纯增量）"
        % (_f0[1], _f0[2], _f0[3]))
    _last = TRF_POS[-1]
    log("CHECK_TRAVEL: 位置轨末帧 = (%.4f, %.4f, %.4f)（相对静止姿势的净增量）"
        % (_last[1], _last[2], _last[3]))
    log("导出 TRF: %s %s" % (trf_out, "OK" if os.path.isfile(trf_out) else "!! 未生成"))
log("DONE")
