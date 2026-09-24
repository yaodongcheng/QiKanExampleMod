# -*- coding: utf-8 -*-
"""FBX 体检：看【绑定姿势】是否是站姿，以及骨盆逐帧走向。

为什么要它 —— 区分两种长得像的病（2026-09-24 立，被处决/伏击那 14 条逼出来的）：
  · **TRF 位移轨病**（§15.3b）：FBX 是好的，只是导出时把「绝对」当「增量」写 ⇒ 首帧 ≈ 0.79~0.86
    且**整批数值一样**（就是骨架静止高度）。
  · **FBX 绑定姿势病**（本脚本查的）：重定向时把**某一帧的姿势烘成了 rest** ⇒ 导出的增量
    天生带一个常数偏移（实测 +0.71~+0.83），**每条数值还不一样**、查不出那个整齐的常数。
    判据：**首帧 − 绑定姿势 ≠ 0**（好件是 0.0000）。根因在 FBX，改 TRF 治不好。

用法
    blender -b --factory-startup --python check_fbx_bindpose.py -- <a.fbx> <b.fbx> ...

判读
    `绑定姿势骨盆世界z` ≈ 0.85 = 站姿（正常，来自官方 human_lod_4）
    明显偏低（0.0~0.15）= **躺/跪姿被烘成了 rest** ⇒ 该 FBX 须按官方骨架重做静止姿势后再导出 TRF
"""
import os
import sys

import bpy

paths = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
for p in paths:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if not os.path.isfile(p):
        print("!! 缺文件 %s" % p)
        continue
    bpy.ops.import_scene.fbx(filepath=p)
    arm = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
    if arm is None:
        print("!! 无骨架 %s" % p)
        continue
    e = arm.rotation_euler if arm.rotation_mode != "QUATERNION" else arm.rotation_quaternion.to_euler()
    import math
    deg = [math.degrees(v) for v in e]
    # 骨盆世界坐标：首 / 中 / 末帧
    act = arm.animation_data.action if arm.animation_data else None
    fr = act.frame_range if act else (0, 0)
    zs = []
    for f in (int(fr[0]), int((fr[0] + fr[1]) / 2), int(fr[1])):
        bpy.context.scene.frame_set(f)
        bpy.context.view_layer.update()
        pb = arm.pose.bones.get("pelvis")
        zs.append((f, round((arm.matrix_world @ pb.head).z, 4) if pb else None))
    print("%-46s 对象旋转 XYZ=(%.2f, %.2f, %.2f)°  缩放=%.4f  骨盆世界z 首/中/末=%s"
          % (os.path.basename(p), deg[0], deg[1], deg[2], arm.scale.x, zs))
    # 绑定姿势（静止）的骨盆世界 z —— 用来判「FBX 静姿是否 = 官方骨架静姿」
    bz = (arm.matrix_world @ arm.data.bones["pelvis"].head_local).z if "pelvis" in arm.data.bones else None
    print("%-46s   ↑ 绑定姿势骨盆世界z=%.4f   首帧−绑定=%s"
          % ("", bz, ("%+.4f" % (zs[0][1] - bz)) if (bz is not None and zs[0][1] is not None) else "?"))
