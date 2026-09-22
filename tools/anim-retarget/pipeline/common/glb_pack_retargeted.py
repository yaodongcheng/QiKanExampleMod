#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把【已重定向好】的动画合进一个查看器 GLB —— 不重跑重定向。

两条路，**优先 TRF**：
  · `--trfdir`（✅ 推荐）：**直接读 .trf** —— 那是真正进 ModKit 的交付物，所以查看器里看到的
    = 装机所见（逐字）。不经过 FBX、不经过 Blender 的动作绑定，最硬。
  · `--fbxdir`：读 retarget.py 产出的 FBX。**只对"普通动画"可靠** ——
    2026-09-22 踩过：自己导出的 FBX（`trf_to_fbx.py` 的产物）在打包时源姿势读到的仍是基础动作，
    烘出来是错的（Blender ≥4.4 的 action slot 语义坑）。TRF 路没这个问题。

用法
    blender -b --python glb_pack_retargeted.py -- ^
        --base  <带网格的骨架 FBX> --trfdir <TRF 目录> ^
        --clips "fly_A_Flight_Dodge_A_L,fly_A_Flight_HoverMove_A=名字B" ^
        --out   <输出 GLB>

    动画名 = **源 clip 名**（默认把 `fly_` 前缀去掉；查看器靠两侧同名配对）。
    要一条动画换个名字（比如拿基础动画冒充"叠加前"），写 `TRF名=动画名`。
"""
import math
import os
import sys

import bpy
from mathutils import Quaternion, Vector


def _args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = {}
    i = 0
    while i < len(a):
        if a[i].startswith("--"):
            out[a[i][2:]] = a[i + 1] if i + 1 < len(a) else ""
            i += 2
        else:
            i += 1
    return out


A = _args()
BASE = A.get("base")
FBXDIR = A.get("fbxdir")
TRFDIR = A.get("trfdir")
OUT = A.get("out")
# --clips 支持两种写法：`名`（默认命名）、`名=动画名`（一条素材出多个名字，做"前/后对照"用）
CLIPS = []          # [(素材名, 动画名 or None)]
for item in (A.get("clips") or "").split(","):
    item = item.strip()
    if not item:
        continue
    if "=" in item:
        c, n = item.split("=", 1)
        CLIPS.append((c.strip(), n.strip()))
    else:
        CLIPS.append((item, None))
STRIP = A.get("strip", "fly_")
FPS = int(A.get("fps", "30"))
MAP = {}
for pair in (A.get("map") or "").split(","):
    if "=" in pair:
        k, v = pair.split("=", 1)
        MAP[k.strip()] = v.strip()

if not (BASE and (FBXDIR or TRFDIR) and CLIPS and OUT):
    print("!! 缺参数：--base / --clips / --out 必填，且 --trfdir 与 --fbxdir 至少给一个")
    sys.exit(2)


def log(m):
    print("[pack] %s" % m, flush=True)


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


def fcurves_of(act):
    """取动作里的全部 F-Curve —— 兼容两代 API。

    Blender ≤4.3：`act.fcurves` 直接可用。
    Blender ≥4.4（本机 5.2）：动作改成「层 → 条 → 通道包」结构，`fcurves` 由
    `layer.strips[*].channelbags[*].fcurves` 提供（**5.x 里顶层的 `act.fcurves` 已删除**）。
    """
    direct = getattr(act, "fcurves", None)
    if direct is not None:
        return list(direct)
    out = []
    for layer in getattr(act, "layers", []):
        for strip in getattr(layer, "strips", []):
            for cb in getattr(strip, "channelbags", []):
                out.extend(list(cb.fcurves))
    return out


def src_name(clip):
    """输出名 → 源 clip 名（两侧配对用）。"""
    if clip in MAP:
        return MAP[clip]
    return clip[len(STRIP):] if STRIP and clip.startswith(STRIP) else clip


bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.fps = FPS

bpy.ops.import_scene.fbx(filepath=BASE)
# 🔴 帧率必须在**导入基底之后**再设一次：FBX 导入器会按文件自带的帧率改写场景 fps，
#    不改的话关键帧会被按错的 fps 换算成秒（实测踩过：31 帧的 clip 变成 1.25s = 按 24fps 写的，
#    查看器里两边还会被"按时长比缩放"错位）。
sc.render.fps = FPS
base = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
if base.animation_data:
    base.animation_data.action = None          # 骨架自带的那条动画不要
log("基底 %s  骨 %d  fps=%d" % (os.path.basename(BASE), len(base.data.bones), FPS))

ok, fails = 0, []
for idx, (clip, name_override) in enumerate(CLIPS, 1):
    name = name_override or src_name(clip)
    try:
        # 给 base 建一条**空动作**（关键帧直接写进去）—— 空动作是"为 base 新建的"，
        # 赋给 base 走正常路径，不涉及跨骨架的 slot 迁移。
        new_act = bpy.data.actions.new("<tmp>" + clip)
        if base.animation_data is None:
            base.animation_data_create()
        base.animation_data.action = new_act
        try:
            if hasattr(new_act, "slots") and len(new_act.slots) == 0:
                new_act.slots.new(id_type='OBJECT', name=base.name)
            if hasattr(new_act, "slots") and len(new_act.slots):
                base.animation_data.action_slot = new_act.slots[0]
        except Exception as e:
            log("    槽位告警（一般无害）: %s" % e)
        pose_tgt = list(base.pose.bones)

        if TRFDIR:
            # ── 路 A（推荐）：直接读 TRF。还原口径与 trf_to_fbx.py 一致 ──
            path = os.path.join(TRFDIR, clip + ".trf")
            if not os.path.isfile(path):
                fails.append((clip, "没有这个 TRF"))
                log("  [%2d/%d] %-34s 跳过（%s 不存在）" % (idx, len(CLIPS), clip, path))
                continue
            _tn, rots, pos = read_trf(path)
            if len(rots) != len(pose_tgt):
                fails.append((clip, "骨数不符"))
                log("  [%2d/%d] %-34s 跳过（骨架 %d 骨 / TRF %d 骨）"
                    % (idx, len(CLIPS), clip, len(pose_tgt), len(rots)))
                continue
            f0 = rots[0][0][0]
            n_frames = rots[0][-1][0] - f0
            for bi, pb in enumerate(pose_tgt):
                inv = rest_local(pb).to_quaternion().inverted()
                pb.rotation_mode = 'QUATERNION'
                for (f, q) in rots[bi]:
                    pb.rotation_quaternion = inv @ Quaternion((q[3], q[0], q[1], q[2]))
                    pb.keyframe_insert(data_path="rotation_quaternion", frame=f - f0)
            root = base.pose.bones.get("pelvis") or pose_tgt[0]
            inv3 = rest_local(root).to_3x3().inverted()
            for (f, p) in pos:
                root.location = inv3 @ Vector(p)
                root.keyframe_insert(data_path="location", frame=f - f0)
        else:
            # ── 路 B：读 FBX（只对 retarget.py 的产物可靠）──
            path = os.path.join(FBXDIR, clip + ".fbx")
            if not os.path.isfile(path):
                fails.append((clip, "没有这个 FBX"))
                log("  [%2d/%d] %-34s 跳过（%s 不存在）" % (idx, len(CLIPS), clip, path))
                continue
            before = set(bpy.data.objects)
            bpy.ops.import_scene.fbx(filepath=path)
            U = next(o for o in bpy.data.objects if o.type == 'ARMATURE' and o not in before)
            act = U.animation_data.action if U.animation_data else None
            if act is None:
                fails.append((clip, "FBX 里没有动作"))
                log("  [%2d/%d] %-34s 跳过（无动作）" % (idx, len(CLIPS), clip))
                continue
            fcs = fcurves_of(act)
            if not fcs:
                fails.append((clip, "动作里没有 F-Curve"))
                log("  [%2d/%d] %-34s 跳过（动作是空的）" % (idx, len(CLIPS), clip))
                bpy.data.objects.remove(U, do_unlink=True)
                continue
            f0 = min((kp.co.x for fc in fcs for kp in fc.keyframe_points), default=0.0)
            n_frames = max((kp.co.x for fc in fcs for kp in fc.keyframe_points), default=0.0)
            pose_src = list(U.pose.bones)
            for f in range(int(f0), int(f0 + n_frames) + 1):
                sc.frame_set(f)
                bpy.context.view_layer.update()
                for i, pb_src in enumerate(pose_src):
                    if i >= len(pose_tgt):
                        break
                    pb_dst = pose_tgt[i]
                    pb_dst.rotation_mode = 'QUATERNION'
                    pb_dst.rotation_quaternion = pb_src.rotation_quaternion.copy()
                    pb_dst.location = pb_src.location.copy()
                    pb_dst.scale = pb_src.scale.copy()
                    pb_dst.keyframe_insert(data_path="rotation_quaternion", frame=f - int(f0))
                    pb_dst.keyframe_insert(data_path="location", frame=f - int(f0))
                    pb_dst.keyframe_insert(data_path="scale", frame=f - int(f0))
            bpy.data.objects.remove(U, do_unlink=True)
            try:
                bpy.data.actions.remove(act)
            except Exception:
                pass

        # 改名（命名 = 源 clip 名，查看器靠同名配对）
        old = bpy.data.actions.get(name)
        if old is not None and old is not new_act:
            old.name = name + "_dup"
        new_act.name = name
        new_act.use_fake_user = True

        # 取证：烘完后头骨的世界位置走了多少（≈0 = 这条根本没动）
        head = base.pose.bones.get("head") or pose_tgt[-1]
        probe = []
        for fr in (0, n_frames // 2, int(n_frames)):
            sc.frame_set(fr)
            bpy.context.view_layer.update()
            probe.append((base.matrix_world @ head.head).copy())
        ok += 1
        log("  [%2d/%d] %-34s → %-26s %d 帧 / %.3f s  头动幅 中%.3f m 末%.3f m"
            % (idx, len(CLIPS), clip, name, int(n_frames) + 1, (n_frames + 1) / float(FPS),
               (probe[1] - probe[0]).length, (probe[2] - probe[0]).length))
    except Exception as e:
        fails.append((clip, str(e)))
        log("  [%2d/%d] %-34s 失败: %s" % (idx, len(CLIPS), clip, e))

# 只留 base（及其子级）
keep = set([base]) | set(base.children)
for o in list(bpy.data.objects):
    if o not in keep:
        try:
            bpy.data.objects.remove(o, do_unlink=True)
        except Exception:
            pass

# 🔴 清掉没用的动作：基底骨架 FBX 自带一条动作（human_lod_4.fbx 里那条 `..._run_...`），
#    导出器在 ACTIONS 模式下会**把它也导出去**（实测多出第 13 条，查看器里就是一条垃圾 clip）。
wanted = set((n or src_name(c)) for (c, n) in CLIPS)
for a in list(bpy.data.actions):
    if a.name not in wanted:
        try:
            bpy.data.actions.remove(a)
        except Exception:
            pass

sc.frame_start = 0
sc.frame_end = int(max([a.frame_range[1] for a in bpy.data.actions] or [0])) + 1
os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB',
                          export_animations=True, export_animation_mode='ACTIONS',
                          export_skins=True, export_apply=False, use_selection=False,
                          export_frame_step=1)
log("导出 %s（%.1f MB，动画 %d 条）" % (OUT, os.path.getsize(OUT) / 1048576.0, ok))
if fails:
    log("!! 有 %d 条没进去：%s" % (len(fails), fails))
log("DONE")
