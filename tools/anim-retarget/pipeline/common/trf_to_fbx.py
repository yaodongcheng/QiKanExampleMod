#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""TRF → FBX（把 .trf 还原成一具骨架上的关键帧动作）—— 预览 / 检查用。

为什么需要
    查看器只吃 GLB，而**真正进包的交付物是 TRF**（ModKit 导的就是它）。要看"合成后到底是什么样"
    （`trf_compose.py` 的产物）就得有一条 TRF → 可视化的路。本脚本补这一环：
    TRF + 骨架 FBX → 带动画的 FBX，再交给 `glb_pack_retargeted.py` 合进查看器 GLB。

还原口径（与 `fbx_to_trf.py` 的写入逐条对偶，见那边 §sample）
    · 旋转：TRF 存的是**绝对局部变换**（`rest ∘ 增量`）⇒ 还原 `增量 = rest⁻¹ ∘ q_trf`
    · 平移：TRF 存的是**相对静止的纯增量**，写在**根骨**上（`rest_3x3 ∘ location`）
      ⇒ 还原 `location = rest_3x3⁻¹ ∘ p`，**不加 rest.translation**（加了就是那个"抬高 6cm"的旧 bug）

用法
    blender -b --python trf_to_fbx.py -- \
        --skel <骨架 FBX> --trfdir <TRF 目录> --clips a,b,c --outdir <输出目录> [--root pelvis]
"""
import os
import sys

import bpy
from mathutils import Quaternion, Vector


def _args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out, i = {}, 0
    while i < len(a):
        if a[i].startswith("--"):
            out[a[i][2:]] = a[i + 1] if i + 1 < len(a) else ""
            i += 2
        else:
            i += 1
    return out


A = _args()
SKEL = A.get("skel")
TRFDIR = A.get("trfdir")
OUTDIR = A.get("outdir")
ROOT = A.get("root", "pelvis")
FPS = int(A.get("fps", "30"))
CLIPS = [c.strip() for c in (A.get("clips") or "").split(",") if c.strip()]

if not (SKEL and TRFDIR and OUTDIR and CLIPS):
    print("!! 缺参数：--skel / --trfdir / --clips / --outdir 都必填")
    sys.exit(2)


def log(m):
    print("[trf2fbx] %s" % m, flush=True)


def read_trf(path):
    """返回 (name, rots[骨][(frame,(x,y,z,w))], pos[(frame,(x,y,z))])。"""
    with open(path, encoding="utf-8") as f:
        L = [ln.rstrip("\r\n") for ln in f]
    assert L[0].startswith("rfver"), "不是 TRF: %s" % L[0]
    name = L[2].split()[0]
    nb = int(L[3])
    i = 4
    rots = []
    for _ in range(nb):
        n = int(L[i]); i += 1
        fr = []
        for _k in range(n):
            p = L[i].split(); i += 1
            fr.append((int(p[0]), (float(p[1]), float(p[2]), float(p[3]), float(p[4]))))
        rots.append(fr)
    np_ = int(L[i]); i += 1
    pos = []
    for _k in range(np_):
        p = L[i].split(); i += 1
        pos.append((int(p[0]), (float(p[1]), float(p[2]), float(p[3]))))
    return name, rots, pos


def rest_local(pb):
    """该骨相对父骨的静止矩阵（与 fbx_to_trf.py 同一口径）。"""
    if pb.parent:
        return pb.parent.bone.matrix_local.inverted() @ pb.bone.matrix_local
    return pb.bone.matrix_local.copy()


ok = 0
for idx, clip in enumerate(CLIPS, 1):
    trf_path = os.path.join(TRFDIR, clip + ".trf")
    if not os.path.isfile(trf_path):
        log("  [%2d/%d] %-34s 跳过（没有 %s）" % (idx, len(CLIPS), clip, trf_path))
        continue
    try:
        name, rots, pos = read_trf(trf_path)
        bpy.ops.wm.read_factory_settings(use_empty=True)
        sc = bpy.context.scene
        sc.render.fps = FPS
        bpy.ops.import_scene.fbx(filepath=SKEL)
        arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
        if arm.animation_data:
            arm.animation_data.action = None
        act = bpy.data.actions.new(name)
        act.use_fake_user = True
        if arm.animation_data is None:
            arm.animation_data_create()
        arm.animation_data.action = act
        try:
            if hasattr(act, "slots") and len(act.slots) == 0:
                act.slots.new(id_type='OBJECT', name=arm.name)
            if hasattr(act, "slots") and len(act.slots):
                arm.animation_data.action_slot = act.slots[0]
        except Exception as e:
            log("    槽位告警（一般无害）: %s" % e)

        bones = list(arm.pose.bones)
        if len(bones) != len(rots):
            log("  [%2d/%d] %-34s 骨数不符（骨架 %d / TRF %d）—— 骨架选错了？"
                % (idx, len(CLIPS), clip, len(bones), len(rots)))
            continue

        # ① 旋转：TRF 的绝对局部 → Blender 的增量（rest⁻¹ ∘ q）
        #    🔴 四元数顺序：TRF 存 (x,y,z,w)，Blender 的 Quaternion 构造是 (w,x,y,z) —— 别直接喂。
        for bi, pb in enumerate(bones):
            rest_q = rest_local(pb).to_quaternion()
            pb.rotation_mode = 'QUATERNION'
            inv = rest_q.inverted()
            for (f, q) in rots[bi]:
                pb.rotation_quaternion = inv @ Quaternion((q[3], q[0], q[1], q[2]))
                pb.keyframe_insert(data_path="rotation_quaternion", frame=f)

        # ② 平移：只写在根骨上；TRF 里是"相对静止的纯增量"，不加 rest.translation
        root = arm.pose.bones.get(ROOT) or bones[0]
        rest3 = rest_local(root).to_3x3()
        inv3 = rest3.inverted()
        for (f, p) in pos:
            root.location = inv3 @ Vector(p)
            root.keyframe_insert(data_path="location", frame=f)

        os.makedirs(OUTDIR, exist_ok=True)
        out = os.path.join(OUTDIR, clip + ".fbx")
        # 🔴 场景帧范围必须设成**这条 clip 的真实范围**：导出器带 `force_startend_keying`，
        #    会用场景的 frame_start/end 给每根骨补首尾键 —— 不设就是 Blender 默认的 1..250，
        #    整段被拉到 250 帧（实测踩过：合成后的 clip 变成 250 帧，姿态在后半段僵着）。
        all_frames = [f for fr in rots for (f, _q) in fr] + [f for (f, _p) in pos]
        sc.frame_start = int(min(all_frames))
        sc.frame_end = int(max(all_frames))
        bpy.ops.export_scene.fbx(
            filepath=out, use_selection=False, object_types={'ARMATURE'},
            add_leaf_bones=False, axis_up='Z', axis_forward='-Y',
            primary_bone_axis='Y', secondary_bone_axis='X',
            apply_unit_scale=True, global_scale=1.0,
            bake_anim=True, bake_anim_use_all_bones=True,
            bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
            bake_anim_force_startend_keying=True, bake_anim_step=1.0,
            bake_anim_simplify_factor=0.0, path_mode='AUTO')
        ok += 1
        log("  [%2d/%d] %-34s → %s（%d 骨 / %d 帧）"
            % (idx, len(CLIPS), clip, clip + ".fbx", len(rots), len(rots[0])))
    except Exception as e:
        log("  [%2d/%d] %-34s 失败: %s" % (idx, len(CLIPS), clip, e))

log("完成 %d/%d 段" % (ok, len(CLIPS)))
log("DONE")
