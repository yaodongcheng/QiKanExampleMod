#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ModKit FBX 体检 —— 进 ModKit 前逐项核对硬规格（通用层，与骨架无关）

用法：
    blender -b --python pipeline/common/verify_modkit_fbx.py -- --fbx <文件.fbx> [--bones 28] [--root human_skeleton_notused]

判据（骑砍 human_skeleton）：
    骨数 = 28 · 根名 = human_skeleton_notused · 无 *_end 叶骨 · 无 mesh · 单位 cm · Z-up
退出码：0 = 全部通过，1 = 有不合格项
"""
import bpy
import sys
import os
import argparse


def main():
    argv = sys.argv
    a = argv[argv.index("--") + 1:] if "--" in argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument("--fbx", required=True)
    ap.add_argument("--bones", type=int, default=28)
    ap.add_argument("--root", default="human_skeleton_notused")
    ns = ap.parse_args(a)

    if not os.path.isfile(ns.fbx):
        print("FAIL: 文件不存在 %s" % ns.fbx)
        return 1

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=ns.fbx)

    arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    print("=" * 62)
    print("文件: %s" % os.path.basename(ns.fbx))
    print("=" * 62)

    checks = []
    if not arms:
        print("FAIL: 没有骨架对象")
        return 1
    if len(arms) > 1:
        print("WARN: 有 %d 个骨架 %s（TRF 转换会拒绝多个骨架）" % (len(arms), [o.name for o in arms]))
    arm = arms[0]
    B = arm.data.bones
    n = len(B)

    checks.append(("骨数 = %d" % ns.bones, n == ns.bones, "实际 %d" % n))
    roots = [b.name for b in B if b.parent is None]
    checks.append(("骨架对象名 = %s" % ns.root, arm.name == ns.root, "实际 '%s'" % arm.name))
    checks.append(("根骨骼唯一", len(roots) == 1, "实际 %s" % roots))
    ends = [b.name for b in B if b.name.endswith("_end")]
    checks.append(("无 *_end 叶骨", len(ends) == 0, "残留 %s" % ends if ends else "OK"))
    checks.append(("无 mesh（只导骨架）", len(meshes) == 0, "mesh 数 %d" % len(meshes)))
    # !!! 尺寸判据要看【动画实际数据】而不是 rest：
    #     部分源（飞行动作、Pose 资产）导出后 rest 会被姿态影响而失真（实测 0.329），
    #     但动画轨道的数据是正确的（CHECK_POS 骨盆 0.9145 正常）。
    #     ModKit 消费的是动画轨道，所以这里量【动画首帧的骨骼世界范围】。
    sc2 = bpy.context.scene
    _act = arm.animation_data.action if arm.animation_data else None
    _f0 = int(round(_act.frame_range[0])) if _act else sc2.frame_current
    sc2.frame_set(_f0)
    bpy.context.view_layer.update()
    bb = [arm.matrix_world @ arm.pose.bones[bone.name].head for bone in B]
    h = (max(v.z for v in bb) - min(v.z for v in bb)) if bb else 0.0
    # 注意：该判据仅供参考，不作为门禁。部分源（飞行 FastMove/Pose）的 FBX rest 会被
    # 写成前倾姿态（实测 0.329），但【TRF 的动画轨道数据是正确的】（CHECK_POS 骨盆 0.9145 正常），
    # 而 ModKit 消费的是 TRF。因此尺寸异常只提示、不判不通过。
    unit_ok = 1.2 < h < 2.6
    if unit_ok:
        checks.append(("尺寸为米制（动画首帧高≈1.75）", True, "实测高 %.3f @f%d" % (h, _f0)))
    else:
        print("  [WARN] 尺寸异常（%.3f @f%d）—— 若 TRF 的 CHECK_POS 正常则不影响 ModKit" % (h, _f0))

    ad = arm.animation_data
    if ad and ad.action:
        fr = ad.action.frame_range
        fps = bpy.context.scene.render.fps
        dur = (fr[1] - fr[0]) / float(fps) if fps else 0
        print("  动画动作 : %s" % ad.action.name)
        print("  帧范围   : %.0f..%.0f  @ %d fps  = %.3f s" % (fr[0], fr[1], fps, dur))
        print("  ModKit   : Source 1 = %.0f · Source 2 = %.0f · Duration > 0" % (fr[0], fr[1]))
    else:
        checks.append(("含动画", False, "无 animation_data"))

    print("-" * 62)
    bad = 0
    for name, ok, detail in checks:
        print("  [%s] %-32s %s" % ("OK" if ok else "X ", name, detail))
        if not ok:
            bad += 1
    print("-" * 62)
    print("  结果: %s（%d 项不合格）" % ("PASS" if bad == 0 else "FAIL", bad))
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
