#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""用"世界增量 + 逐骨静止对齐(align)"重做 UE5 -> 骑砍2 重定向，并修掉 fps 坑。
   用法: blender -b --python ue_align.py -- --clip 010_01 [--flip 180]
"""
import bpy, sys, os, json, math, argparse
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

ROOT = PROJECT_ROOT
MAP  = json.load(open(os.path.join(PROJECT_ROOT,"pipeline/rigs/ue_mannequin/map.json"), encoding="utf-8"))
PAIRS = MAP["bone_map"]
TGT_FBX = os.path.join(ROOT, "input", "target", "bannerlord", "human_lod_4.fbx")
OUTDIR  = os.path.join(ROOT, "output/fbx/ue_basic")

def parse():
    a = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    ap = argparse.ArgumentParser(); ap.add_argument("--clip", default="010_01")
    ap.add_argument("--flip", default="180"); ap.add_argument("--pose", default="align")
    ap.add_argument("--animdir", default=os.path.join(PROJECT_ROOT, "input/source/ue_mannequin/clips_basic"),
                    help="源 FBX 所在目录（clips_basic / clips_flight / clips_slim / clips_ghost ...）")
    ap.add_argument("--name", default=None); ap.add_argument("--outdir", default=None)
    ap.add_argument("--no_trf", default="false")
    ap.add_argument("--pelvis", default="ground", choices=["ground", "none", "src"],
                    help="ground=逐帧贴地、丢弃源水平位移（站立/地面动作）；"
                         "src=逐帧贴地【并保留源根位移】的水平分量（带位移动画，如处决/冲锋）；"
                         "none=保持源骨盆高度（飞行/离地动作）")
    ap.add_argument("--obj_rot", default="true",
                    help="是否把源【骨架对象】相对首帧的旋转增量搬进目标（转身类动作必需，默认 true）；"
                         "false = 复现 2026-09-22 之前的旧行为（对象级转身会被整段丢掉）")
    ns = ap.parse_args(a)
    if not ns.name: ns.name = ns.clip
    if not ns.outdir: ns.outdir = OUTDIR
    ns.no_trf = str(ns.no_trf).lower() in ("1", "true", "yes")
    ns.obj_rot = str(ns.obj_rot).lower() in ("1", "true", "yes")
    return ns
args = parse()
def log(m): print("[ue-align] %s" % m, flush=True)
def rot3(m): return m.to_3x3().normalized()

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
# 支持两种组织：扁平（animdir/名.fbx）与保留目录结构（animdir/**/名.fbx）
_src_fbx = os.path.join(args.animdir, args.clip + ".fbx")
if not os.path.isfile(_src_fbx):
    _hit = None
    for _r, _d, _fs in os.walk(args.animdir):
        if (args.clip + ".fbx") in _fs:
            _hit = os.path.join(_r, args.clip + ".fbx")
            break
    if _hit is None:
        import glob as _g
        _cand = _g.glob(os.path.join(args.animdir, "**", args.clip + ".fbx"), recursive=True)
        _hit = _cand[0] if _cand else None
    if _hit is None:
        log("!! 找不到源 FBX: %s（在 %s 下递归也没找到）" % (args.clip, args.animdir))
        raise SystemExit(2)
    _src_fbx = _hit
log("源 FBX: %s" % _src_fbx)
bpy.ops.import_scene.fbx(filepath=_src_fbx)
src = next(o for o in bpy.data.objects if o.type=='ARMATURE')
SRC_FPS = sc.render.fps
log("源 fps = %d （UE 导出 FBX 自带）" % SRC_FPS)
sact = src.animation_data.action
fs, fe = int(round(sact.frame_range[0])), int(round(sact.frame_range[1]))
log("源 %s 帧 %d..%d = %.3f s" % (args.clip, fs, fe, (fe-fs)/SRC_FPS))
src_keep = SRC_FPS

before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=TGT_FBX)
tgt = next(o for o in bpy.data.objects if o.type=='ARMATURE' and o not in before)
# !!! 他们漏掉的一步：导入骑砍 FBX 会把场景 fps 改掉，必须还原，否则导出时长错
sc.render.fps = src_keep
log("导入目标后 场景 fps 已还原为 %d（原会被骑砍 FBX 改成 24）" % sc.render.fps)

PAIRS = {s:t for s,t in PAIRS.items() if s in src.pose.bones and t in tgt.data.bones}
log("有效映射 %d 骨" % len(PAIRS))
F = Euler((0.0,0.0,math.radians(float(args.flip))),'XYZ').to_matrix()
log("帧变换 F = Rot(Z,%s°)" % args.flip)

tgt_rest_w = {t: rot3(tgt.matrix_world @ tgt.data.bones[t].matrix_local) for t in PAIRS.values()}

def mapped_kids(arm, nm, is_src):
    kid=[]; st=[c for c in arm.data.bones[nm].children]
    while st:
        c=st.pop(0)
        if (c.name in PAIRS) if is_src else (c.name in PAIRS.values()): kid.append(c.name)
        else: st.extend(c.children)
    return kid
def sub_has(arm, nm, want):
    st=[arm.data.bones[nm]]
    while st:
        b=st.pop()
        if b.name==want: return True
        st.extend(b.children)
    return False
def out_dir(arm, nm, kid):
    v=(arm.matrix_world @ arm.data.bones[kid].matrix_local.translation) - \
      (arm.matrix_world @ arm.data.bones[nm].matrix_local.translation)
    return v if v.length>1e-6 else None
A_align={}
for s,t in PAIRS.items():
    ks=mapped_kids(src,s,True); kt=mapped_kids(tgt,t,False)
    if not ks or not kt: continue
    if len(ks)==1 and len(kt)==1: cs,ct=ks[0],kt[0]
    else:
        cs=next((k for k in ks if sub_has(src,k,"head")),None)
        ct=next((k for k in kt if sub_has(tgt,k,"head")),None)
        if not cs or not ct: continue
    vs=out_dir(src,s,cs); vt=out_dir(tgt,t,ct)
    if vs is None or vt is None: continue
    A_align[t]=((F@vs).normalized()).rotation_difference(vt.normalized())
log("静止对齐修正覆盖 %d 骨" % len(A_align))

if tgt.animation_data: tgt.animation_data_clear()
tgt.animation_data_create()
newact = bpy.data.actions.new("RT_"+args.clip)
tgt.animation_data.action = newact
try:
    if hasattr(newact,"slots"):
        newact.slots.new(id_type='OBJECT', name=tgt.name); tgt.animation_data.action_slot = newact.slots[0]
except Exception as e: log("slot %s" % e)

order=[]
def walk(b):
    order.append(b.name)
    for c in b.children: walk(c)
for b in tgt.data.bones:
    if b.parent is None: walk(b)

# 统一到骑砍基准 30fps：源可能是 120/60/25fps，直接导出会让 ModKit 里时长差数倍
TGT_FPS = 30
SRC_STEP = float(SRC_FPS) / TGT_FPS
N_OUT = int(round((fe - fs) / SRC_STEP)) + 1
log("resample: src %d fps / %d frames -> out %d fps / %d frames" % (SRC_FPS, fe - fs + 1, TGT_FPS, N_OUT))

# 对象级旋转基准（2026-09-22 修 "转身类动作重定向丢转身"）
#   源 FBX 的"转身/朝向变化"挂在【骨架对象】上，不在骨骼上 —— 实测 GhostSamurai_Execution02(Root)：
#   对象 0° → -180°（帧 85~145 / 401），同段 pelvis 骨的世界 yaw 只动 ±5~13°。
#   而 sp 与 rp 若都乘【同一帧】matrix_world，对象级旋转会在 sp·rp⁻¹ 里被约掉 → 目标侧整段丢转身。
#   修法：静姿基准改用【首帧】对象变换 W0，让 W(t)·W0⁻¹ 这一增量进入 R。
#   首帧 W(fs)·W0⁻¹ = I ⇒ 首帧行为/站位对齐完全不变；对象不动的 clip W(t)≡W0 ⇒ 输出逐位不变。
#   与位移那套（_src_root_world() - _src_root0 → 骨盆 location 轨）对称，只是位移早就有、旋转一直缺。
sc.frame_set(fs); bpy.context.view_layer.update()
SRC_W0 = src.matrix_world.copy()
log("对象级旋转：%s（静姿基准 = 首帧对象变换；对象不动的 clip 输出不变）"
    % ("搬入骨盆旋转轨" if args.obj_rot else "不搬，复现旧行为"))

for _i in range(N_OUT):
    _of = 1 + _i
    sc.frame_set(int(round(fs + _i * SRC_STEP))); bpy.context.view_layer.update()
    W={}
    for s,t in PAIRS.items():
        sp = rot3(src.matrix_world @ src.pose.bones[s].matrix)
        rp = rot3((SRC_W0 if args.obj_rot else src.matrix_world) @ src.data.bones[s].matrix_local)
        R  = F @ (sp @ rp.inverted()) @ F.transposed()
        A  = A_align.get(t)
        W[t] = (R @ A.inverted().to_matrix()) @ tgt_rest_w[t] if A is not None else (R @ tgt_rest_w[t])
    for t in order:
        if t not in W: continue
        pb=tgt.pose.bones[t]
        cur=tgt.matrix_world @ pb.matrix
        nw=W[t].to_4x4(); nw.translation=cur.translation
        pb.matrix = tgt.matrix_world.inverted() @ nw
        bpy.context.view_layer.update()
        pb.rotation_mode='QUATERNION'
        pb.keyframe_insert(data_path="rotation_quaternion", frame=_of)

gb=[b for b in ("l_toe0","r_toe0","l_foot","r_foot") if b in tgt.pose.bones]

# ---------- 源根位移：这一批 UE FBX 把它挂在【骨架对象】上，不在 root/pelvis 骨上 ----------
# 依据：docs/README_骨骼经验.md 硬约束 23（Root 变体的根位移 = ARM_OBJ.loc）。
# 旧逻辑只做"逐帧贴地"，水平位移被整段丢掉 → 实机里动画演完角色又回到原点。
# 实测（GhostSamurai_Execution02 Root）：骨架对象 (0,0,0) -> (-0.246,-3.673,0)，
# 即 3.68 m 纯水平行程；同一 clip 的 Inplace 变体该值为 0 —— 正好是 A/B 对照。
def _src_root_world():
    return src.matrix_world.translation.copy()

sc.frame_set(fs); bpy.context.view_layer.update()
_src_root0 = _src_root_world()
sc.frame_set(fe); bpy.context.view_layer.update()
_src_delta = _src_root_world() - _src_root0
_src_h = math.hypot(_src_delta.x, _src_delta.y)
log("源根位移: 首帧 %s -> 末帧 %s   净 %s m（水平 %.4f / 竖直 %.4f）"
    % ([round(v,4) for v in _src_root0], [round(v,4) for v in (_src_root0 + _src_delta)],
       round(_src_delta.length,4), round(_src_h,4), round(_src_delta.z,4)))
if args.pelvis != "src" and _src_h > 1e-3:
    log("!! 源侧有 %.3f m 水平位移，但 --pelvis %s 会【丢弃】它 —— 要带位移请用 --pelvis src"
        % (_src_h, args.pelvis))

if args.pelvis in ("ground", "src"):
    PB = tgt.pose.bones["pelvis"]
    rest_head = PB.bone.matrix_local.translation.copy()      # 骨架空间里的静止骨盆头位置
    w2a = tgt.matrix_world.inverted().to_3x3()               # 世界 -> 骨架空间（含缩放，硬约束 11）
    trk = []
    for _i in range(N_OUT):
        _of = 1 + _i
        sc.frame_set(int(round(fs + _i * SRC_STEP))); bpy.context.view_layer.update()
        m = PB.matrix.copy()
        if args.pelvis == "src":
            # 源位移在世界空间算完，再用与旋转同一套帧变换 F 转过去（硬约束 11）
            _d = F @ (_src_root_world() - _src_root0)
            m.translation = rest_head + (w2a @ _d)
        else:
            m.translation = rest_head
        PB.matrix = m; bpy.context.view_layer.update()
        # 贴地：只动竖直分量，水平位移不受影响
        mz = min((tgt.matrix_world @ tgt.pose.bones[b].head).z for b in gb)
        m = PB.matrix.copy(); m.translation.z -= mz
        PB.matrix = m; bpy.context.view_layer.update()
        PB.keyframe_insert(data_path="location", frame=_of)
        trk.append((PB.matrix.translation - rest_head).copy())
    _tgt_delta = trk[-1] - trk[0]
    _tgt_h = math.hypot(_tgt_delta.x, _tgt_delta.y)
    log("骨盆位移轨完成（--pelvis %s）: 目标侧净 %s m（水平 %.4f / 竖直 %.4f）"
        % (args.pelvis, [round(v,4) for v in _tgt_delta], round(_tgt_h,4), round(_tgt_delta.z,4)))
    if args.pelvis == "src":
        _err = abs(_tgt_h - _src_h)
        log("CHECK_TRAVEL: 源水平 %.4f m vs 目标水平 %.4f m，差 %.4f m（相对 %.2f%%）"
            % (_src_h, _tgt_h, _err, (100.0*_err/_src_h) if _src_h > 1e-9 else 0.0))
else:
    # 飞行/离地动作：不写位移轨，保留源骨盆高度（否则会被强行拉到地面，骨架被压扁）
    log("跳过骨盆位移轨（--pelvis %s）：保留源骨盆高度" % args.pelvis)

# ---------- ModKit 规格导出（骑砍 human_skeleton 28 骨）----------
# 规格来源：reexport_for_modkit.py / 自定义战斗.md「第1步：造动画」
#   ① object_types={'ARMATURE'} 只导骨架，不把 mesh 塞进动画文件
#   ② add_leaf_bones=False      不补 *_end 叶骨（否则 28+5=33，ModKit 报骨骼数不一致）
#   ③ 根节点改名 human_skeleton_notused（让引擎忽略骨架、只吃动画）
#   ④ Z-up + ⑤ 单位 cm（unit scale 0.01）
# 清理：只保留目标骨架（UE 源 FBX 常带额外骨架如 'root'，会让 TRF 报「多个骨架」）
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
# !!! 导出前把骨骼姿势清零：实测部分源（飞行的 FastMove/Pose）会让 FBX 导出器
#     把【当前帧的姿态】当成 rest 写出去（rest 高被压到 0.329，正常应 ~1.6），
#     但动画轨道本身是对的（CHECK_POS 正常）。清 pose 不影响 action 里的关键帧。
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
# ---------- 最后一步：FBX → TRF（ModKit 用的骨骼动画容器）----------
# 语义要点（见 docs/TRF规范.md）：TRF 存【绝对局部变换】不是增量；
# 骨序=唯一契约（不存骨骼名，纯索引）；时间列=Blender 帧号（决定 ModKit 的 Source1/2）。
if not args.no_trf:
    import subprocess
    trf_script = os.path.join(PROJECT_ROOT, "pipeline/common/fbx_to_trf.py")
    if os.path.exists(trf_script):
        trf_out = os.path.join(args.outdir, args.name + ".trf")
        # 🔴 --name = TRF 第 3 行的【动画名】（= 本次输出名，如 fly_A_Flight_Idle_A）。
        #    不传会退化成骨架对象名 human_skeleton_notused → ModKit 里所有动画资源撞名。
        cmd = [bpy.app.binary_path, "--background", "--factory-startup", "--python-exit-code", "1",
               "--python", trf_script, "--", "--fbx", out, "--out", trf_out,
               "--name", args.name]
        try:
            r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=600)
            for ln in (r.stdout or "").splitlines():
                if ln.startswith(("CHECK_", "TRF_", "CONVERT_")) or "ERROR" in ln.upper():
                    log("  " + ln.strip())
            log("导出 TRF: %s %s" % (trf_out, "OK" if os.path.exists(trf_out) else "!! 未生成"))
        except Exception as e:
            log("!! TRF 导出失败: %s" % e)
    else:
        log("!! 找不到 TRF 转换脚本: %s" % trf_script)
log("导出 %s (fps=%d)" % (out, sc.render.fps))
log("DONE")
