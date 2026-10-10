#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""trf_ground_clamp.py —— 给一条 TRF 做【逐帧贴地钳制】（防穿地 / 防浮空）。

为什么需要
    `trf_edit.py blend` 生成的"姿态过渡桥"是**逐骨 slerp + 根骨线性插值**：
    姿态都能到 A/B 两个端点，但**中间帧的最低骨点会扎进地面**（实测 cry180→daodi
    中段脚/膝探到 -0.19 m）。逐帧贴地把根骨 z 抬到"最低骨点=目标高度"，动作就贴地了。

口径（与 check_glb_feet.py / glb_pack_retargeted.py 完全一致）
    · "最低骨点" = min over pose bones of world Z of pb.head（**只用骨头 head，不用 tail**）
    · TRF 根骨位移 p 就是**世界(骨架空间)偏移**（消费端 `root.location = rest_rot⁻¹ @ p`
      ⇒ 世界偏移 = p）⇒ 加世界 dz 就是 p.z += dz
    · 钳制量 lift(f) = max(0, target(f) − minZ(f))，再乘一个两端归零的窗 W(u)：
      保证**首末帧姿态一字不改**（跨 clip 拼接不留缝），中段才抬。

用法
    blender -b --factory-startup --python trf_ground_clamp.py -- ^
        <in.trf> <out.trf> [--base human_lod_4.fbx] [--target 0.0] [--window 0.15] [--name X]
"""
import os
import sys

import bpy
from mathutils import Quaternion, Vector

A = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def opt(k, d=None):
    return A[A.index(k) + 1] if k in A else d


pos_args = [x for i, x in enumerate(A) if not x.startswith("--") and (i == 0 or not A[i - 1].startswith("--"))]
IN = pos_args[0]
OUT = pos_args[1]
BASE = opt("--base", "input/target/bannerlord/human_lod_4.fbx")
TARGET = float(opt("--target", "0.0"))
WIN = float(opt("--window", "0.15"))
NAME = opt("--name", None)


def read_trf(p):
    L = [l.rstrip("\r\n") for l in open(p, encoding="utf-8")]
    nb = int(L[3]); i = 4; rots = []
    for _ in range(nb):
        n = int(L[i]); i += 1; fr = []
        for _k in range(n):
            q = L[i].split(); i += 1
            fr.append((int(q[0]), (float(q[1]), float(q[2]), float(q[3]), float(q[4]))))
        rots.append(fr)
    np_ = int(L[i]); i += 1; pos = []
    for _k in range(np_):
        q = L[i].split(); i += 1
        pos.append((int(q[0]), (float(q[1]), float(q[2]), float(q[3]))))
    return rots, pos


def write_trf(name, rots, pos, path):
    out = ["rfver 4", "skeleton_anim 1", "%s 1" % name, " %d" % len(rots)]
    for fr in rots:
        out.append(" %d" % len(fr))
        for f, q in fr:
            out.append(" %d %.6f %.6f %.6f %.6f" % (f, q[0], q[1], q[2], q[3]))
    out.append(" %d" % len(pos))
    for f, p in pos:
        out.append(" %d %.6f %.6f %.6f" % (f, p[0], p[1], p[2]))
    out.append("end")
    d = os.path.dirname(os.path.abspath(path))
    if d and not os.path.isdir(d):
        os.makedirs(d)
    open(path, "w", encoding="utf-8", newline="\r\n").write("\n".join(out) + "\n")


def rest_local(pb):
    if pb.parent:
        return pb.parent.bone.matrix_local.inverted() @ pb.bone.matrix_local
    return pb.bone.matrix_local.copy()


def smoothstep(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3.0 - 2.0 * x)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=BASE)
base = next(o for o in bpy.data.objects if o.type == "ARMATURE")
if base.animation_data:
    base.animation_data.action = None
rots, pos = read_trf(IN)
f0 = rots[0][0][0]
act = bpy.data.actions.new("<clamp>")
if base.animation_data is None:
    base.animation_data_create()
base.animation_data.action = act
try:
    if hasattr(act, "slots") and len(act.slots) == 0:
        act.slots.new(id_type='OBJECT', name=base.name)
    if hasattr(act, "slots") and len(act.slots):
        base.animation_data.action_slot = act.slots[0]
except Exception:
    pass
pose = list(base.pose.bones)
for bi, pb in enumerate(pose):
    inv = rest_local(pb).to_quaternion().inverted()
    pb.rotation_mode = 'QUATERNION'
    for (f, q) in rots[bi]:
        pb.rotation_quaternion = inv @ Quaternion((q[3], q[0], q[1], q[2]))
        pb.keyframe_insert("rotation_quaternion", frame=f - f0)
root = base.pose.bones.get("pelvis") or pose[0]
inv3 = rest_local(root).to_3x3().inverted()
for (f, p) in pos:
    root.location = inv3 @ Vector(p)
    root.keyframe_insert("location", frame=f - f0)

sc = bpy.context.scene
L = pos[-1][0] - f0
lifts = [0.0] * (L + 1)
for k, (f, p) in enumerate(pos):
    sc.frame_set(f - f0)
    bpy.context.view_layer.update()
    minz = min((base.matrix_world @ pb.head).z for pb in pose)
    need = max(0.0, TARGET - minz)
    u = k / float(L) if L else 0.0
    w = smoothstep(u / WIN) * smoothstep((1.0 - u) / WIN)
    lifts[k] = need * w

tp, maxlift = [], 0.0
for k, (f, p) in enumerate(pos):
    dz = lifts[k]
    maxlift = max(maxlift, dz)
    tp.append((f, (p[0], p[1], p[2] + dz)))

nm = NAME or os.path.splitext(os.path.basename(OUT))[0]
write_trf(nm, rots, tp, OUT)
print("[clamp] 写出 %s（%d 帧，最大抬升 %.4f m，目标最低点 %.4f）" % (OUT, len(tp), maxlift, TARGET))
