#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把一具 Blender 骨架的静止数据（骨名/父子/rest_local 4x4/长度/世界变换）导出成 JSON。
给纯 Python 的 FK 用（免 Blender、免 depsgraph）。
用法 blender -b --python dump_skeleton.py -- --skel <FBX> --out <JSON>
"""
import os, sys, json
import bpy
from mathutils import Matrix

def _args():
    a = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
    o, i = {}, 0
    while i < len(a):
        if a[i].startswith("--"):
            o[a[i][2:]] = a[i+1] if i+1 < len(a) else ""
            i += 2
        else: i += 1
    return o
A = _args()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=A["skel"])
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
bones = list(arm.pose.bones)
idx = {b.name: i for i, b in enumerate(bones)}
out = {"armature_world": [list(r) for r in arm.matrix_world], "bones": []}
for pb in bones:
    if pb.parent:
        rl = pb.parent.bone.matrix_local.inverted() @ pb.bone.matrix_local
    else:
        rl = pb.bone.matrix_local.copy()
    out["bones"].append({
        "name": pb.name,
        "parent": idx.get(pb.parent.name, -1) if pb.parent else -1,
        "rest_local": [list(r) for r in rl],
        "length": pb.bone.length,
        "head_local": list(pb.bone.head_local),
        "tail_local": list(pb.bone.tail_local),
    })
with open(A["out"], "w", encoding="utf-8") as f:
    json.dump(out, f)
print("bones", len(bones))
for b in out["bones"]:
    print("  %-3d %-24s parent=%-3d len=%7.3f head=%s" % (len([1]), b["name"], b["parent"], b["length"],
          ["%.2f"%v for v in b["head_local"]]))
