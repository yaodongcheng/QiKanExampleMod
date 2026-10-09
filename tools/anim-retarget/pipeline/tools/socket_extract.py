#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""socket_extract.py —— 源骨架「挂接点(socket)」的提取 · 目标侧映射 · 配对动作的挂接偏移推导。

【为什么需要】
源骨架 FBX 里除了 ARMATURE，还有一批 **Null/EMPTY 挂接点**（Guajiedian01/02、Ride、LHand、
Back、Yao、Hit…）。重定向只重建 28 骨 ARMATURE ⇒ **这些 socket 会被整批丢掉**。
要保留"挂接"语义，就得把它们抽出来、映射到目标骨、以 **sidecar(JSON)** 交给引擎/查看器。

【三种模式】
1) --skeleton <fbx>                     列出该骨架所有 socket（名字 / 父骨 / 相对父骨局部变换）
2) --skeleton <fbx> --to-target <map.json> --scale k
                                        把 socket 映射到目标骨名并按 k 缩放偏移（产出目标侧 sidecar）
3) --pair-a <a.fbx> --pair-b <b.fbx> --bone-a <骨> --bone-b <骨>
                                        从【配对动作】反推刚性挂接偏移（A 的挂接点 <- B 的挂接点）

【为什么模式 3 是重点】
本工程的 Guajiedian02 / Ride 在 FBX 里是 **占位值**（parent 挂到根模板骨 Bone001/03 ⇒ 恒在世界原点），
且动画 FBX 里根本没有 socket 节点。所以它们没法直接读。
但配对动作里两人是刚性挂接的 ⇒ **逐帧相对变换是常量** ⇒ 取中位数即可反推出挂接偏移，
并同时给出 std 作为"是否真的挂住"的判据。

用法
    blender -b --factory-startup --python pipeline/tools/socket_extract.py -- --skeleton <fbx> [--out j.json]
    blender -b --factory-startup --python pipeline/tools/socket_extract.py -- \
        --pair-a <a.fbx> --pair-b <b.fbx> --bone-a "Bip001 Spine2" --bone-b "Bip001 Pelvis" [--out j.json]
"""
import bpy, sys, os, json, statistics, math
from mathutils import Vector, Matrix

def args():
    a = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    o = {}; i = 0
    while i < len(a):
        if a[i].startswith("--"): o[a[i][2:]] = a[i+1] if i+1 < len(a) else ""; i += 2
        else: i += 1
    return o
A = args()

def list_sockets(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
    out = []
    for o in bpy.data.objects:
        if o.type != 'EMPTY': continue
        pb = o.parent_bone if o.parent_type == 'BONE' else None
        entry = {"name": o.name, "parent_type": o.parent_type, "parent_bone": pb,
                 "world_T": [round(v, 6) for v in o.matrix_world.translation]}
        if pb and arm and pb in arm.pose.bones:
            rel = (arm.matrix_world @ arm.pose.bones[pb].matrix).inverted() @ o.matrix_world
            entry["local_T"] = [round(v, 6) for v in rel.translation]
        out.append(entry)
    return out

def derive_pair(pa, pb_, ba, bb):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    bpy.ops.import_scene.fbx(filepath=pa)
    arma = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=pb_)
    armb = next(o for o in bpy.data.objects if o.type == 'ARMATURE' and o not in before)
    acta = arma.animation_data.action; actb = armb.animation_data.action
    f0 = max(int(acta.frame_range[0]), int(actb.frame_range[0]))
    f1 = min(int(acta.frame_range[1]), int(actb.frame_range[1]))
    offs = []
    for f in range(f0, f1+1):
        sc.frame_set(f); bpy.context.view_layer.update()
        Ma = arma.matrix_world @ arma.pose.bones[ba].matrix
        Mb = armb.matrix_world @ armb.pose.bones[bb].matrix
        offs.append(Ma.inverted() @ Mb)
    n = len(offs)
    cen = sum((o.translation for o in offs), Vector((0,0,0))) / n
    dists = [(o.translation - cen).length for o in offs]
    q0 = offs[0].to_quaternion()
    angs = [math.degrees(2*math.acos(min(1.0, abs(q0.dot(o.to_quaternion()))))) for o in offs]
    # 位置用中位数（更抗噪），旋转用中位四元数（取最接近均值的那个样本）
    med = sorted(offs, key=lambda o: (o.translation - cen).length)[len(offs)//2]
    return {"mode": "derive_pair", "pair_a": os.path.basename(pa), "pair_b": os.path.basename(pb_),
            "bone_a": ba, "bone_b": bb, "frames": [f0, f1], "n": n,
            "offset_local_median": [round(v, 5) for v in med.translation],
            "offset_mean": [round(v, 5) for v in cen],
            "offset_std": round(statistics.pstdev(dists), 5),
            "rot_max_dev_deg": round(max(angs), 2), "rot_median_dev_deg": round(statistics.median(angs), 2),
            "verdict": "RIGID(刚性挂接)" if statistics.pstdev(dists) < 0.03 else "LOOSE(只是靠近)"}

if A.get("skeleton"):
    res = {"mode": "skeleton", "file": os.path.basename(A["skeleton"]), "sockets": list_sockets(A["skeleton"])}
    # 目标侧映射
    if A.get("to-target"):
        mp = json.load(open(A["to-target"], encoding="utf-8"))["bone_map"]
        k = float(A.get("scale", "1.0"))
        for s in res["sockets"]:
            tb = mp.get(s.get("parent_bone"))
            s["target_bone"] = tb
            if tb and s.get("local_T"):
                s["target_offset_local"] = [round(v*k, 6) for v in s["local_T"]]
elif A.get("pair-a"):
    res = derive_pair(A["pair-a"], A["pair-b"], A["bone-a"], A["bone-b"])
else:
    print("need --skeleton or --pair-a"); sys.exit(2)

txt = json.dumps(res, ensure_ascii=False, indent=2)
if A.get("out"):
    os.makedirs(os.path.dirname(os.path.abspath(A["out"])), exist_ok=True)
    open(A["out"], "w", encoding="utf-8").write(txt + "\n")
    print("WROTE", A["out"])
print(txt)
