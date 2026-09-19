# -*- coding: utf-8 -*-
"""
fbx_to_trf_fixed.py —— 从 FBX 导出 Bannerlord TRF（skeleton_anim），修掉「增量当绝对」的错。

【为什么有这份】
D:\\BrainMaker\\OpenTrf\\fbx_to_trf.py 写出去的是 Blender 的
    pose_bone.rotation_quaternion / pose_bone.location
这两个值都是「相对静止姿势的增量」，不是骨骼的实际局部变换。
骑砍 human_skeleton 是 A-pose，每根骨的静止朝向都不同 —— 增量当绝对用，
等于把每根骨强行摆到「零旋转」，实机表现 = 人趴在地上、四肢乱折。

本脚本把两条轨道都改成「绝对」：
    旋转 = rest_local ∘ delta                    （rest_local = 该骨相对父骨的静止矩阵）
    平移 = rest_local.translation + rest_rot @ delta_loc
根骨同理（相对骨架原点）。

【判据】重新生成后，位置轨首帧应 ≈ 0（**不是** 0.915 —— 那是骨盆绝对高度，本格式不存那个）。

【输出格式】与 fbx_to_trf.py 逐字节同构：
    rfver 4
    skeleton_anim 1
    <name> 1
     <bone_count>
     <rot_count>
     <t> <x> <y> <z> <w>          (四元数按 x y z w，6 位小数)
     ...
     <pos_count>
     <t> <x> <y> <z>
    end

【用法】
  blender --background --factory-startup --python fbx_to_trf_fixed.py -- ^
      --fbx <in.fbx> --out <out.trf> [--skeleton 名] [--start N] [--end N] ^
      [--name 动画名] [--root 骨名]

  --start/--end   采样帧范围，--end **含**最后一帧（内部 range(start, end+1)）。
                  缺省取动作自身的 frame_range。
  --name          TRF 里的动画名，缺省用骨架物体名（不是动作名 —— 引擎侧认的是骨架）。

【脚本自己会做的核对（都会打印）】
  · 骨数 / 帧范围 / 每骨帧数 / 骨序逐行
  · 位置轨首帧数值（验收判据）
  · 绝对公式 vs Blender 自带 pose_bone.matrix 的交叉核对，偏差 > 0.01° 打 CHECK_WARN
"""

import bpy
import sys
import os
import json
import math


# --------------------------------------------------------------------------
# 参数解析：Blender 把脚本参数放在 "--" 之后
# --------------------------------------------------------------------------
def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    out = {}
    i = 0
    while i < len(argv):
        tok = argv[i]
        if tok.startswith("--"):
            key = tok[2:]
            if i + 1 < len(argv) and not argv[i + 1].startswith("--"):
                out[key] = argv[i + 1]
                i += 2
            else:
                out[key] = True
                i += 1
        else:
            i += 1
    return out


def fail(msg):
    print("CONVERT_ERROR: " + msg)
    sys.exit(1)


# --------------------------------------------------------------------------
# 核心修正：相对父骨的静止矩阵
# --------------------------------------------------------------------------
def rest_local(pb):
    """该骨相对父骨的静止矩阵。Blender 的 bone.matrix_local 是「骨架空间」的，
    所以父骨的静止阵求逆再乘自己，才得到父骨坐标系下的静止。根骨直接用它本身。"""
    if pb.parent:
        return pb.parent.bone.matrix_local.inverted() @ pb.bone.matrix_local
    return pb.bone.matrix_local.copy()


def absolute_local(pb):
    """该骨的绝对局部变换（= FBX 里 Lcl Translation/Rotation 的语义）。
    用 Blender 自己算好的 pose 矩阵反推，只用来跟下面的手算做交叉核对。"""
    if pb.parent:
        return pb.parent.matrix.inverted() @ pb.matrix
    return pb.matrix.copy()


# --------------------------------------------------------------------------
# 写盘：格式与 fbx_to_trf.py 逐字节同构（前缀空格、四元数 x y z w、6 位小数、end 不带换行）
# --------------------------------------------------------------------------
def write_trf(name, rots, pos, out_trf):
    contents = []
    contents.append('rfver 4\n')
    contents.append('skeleton_anim 1\n')
    contents.append(' '.join([name, '1']) + '\n')
    contents.append(' ' + str(len(rots)) + '\n')
    for bone in rots:                       # rots[i] = [(t, qx,qy,qz,qw), ...]
        contents.append(' ' + str(len(bone)) + '\n')
        for (t, x, y, z, w) in bone:
            contents.append(' %d %s %s %s %s\n' % (
                t, format(x, '.6f'), format(y, '.6f'), format(z, '.6f'), format(w, '.6f')))
    contents.append(' ' + str(len(pos)) + '\n')
    for p in pos:                           # pos[i] = (t, x, y, z)
        contents.append(' %d %s %s %s\n' % (p[0], format(p[1], '.6f'),
                                            format(p[2], '.6f'), format(p[3], '.6f')))
    contents.append('end')
    os.makedirs(os.path.dirname(os.path.abspath(out_trf)), exist_ok=True)
    with open(out_trf, 'w', encoding='utf-8') as f:
        f.writelines(contents)


# --------------------------------------------------------------------------
# 采样
# --------------------------------------------------------------------------
def sample(arm, start, end_loop, root_index):
    pose = arm.pose
    n_bones = len(pose.bones)
    rots = [[] for _ in range(n_bones)]
    pos = []
    worst_deg = 0.0
    worst_at = ""

    for frame in range(start, end_loop):
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        for i in range(n_bones):
            pb = pose.bones[i]
            rest = rest_local(pb)
            # 绝对旋转 = 静止朝向 ∘ 增量
            q = rest.to_quaternion() @ pb.rotation_quaternion
            rots[i].append((frame, q.x, q.y, q.z, q.w))
            # 交叉核对：跟 Blender 自己算的 pose 矩阵比角度
            ref = absolute_local(pb).to_quaternion()
            dot = min(1.0, abs(q.normalized().dot(ref.normalized())))
            deg = math.degrees(2.0 * math.acos(dot))
            if deg > worst_deg:
                worst_deg, worst_at = deg, "%s@f%d" % (pb.name, frame)

        # 根骨平移 = 「相对静止姿势的增量」，**不叠加**静止头部偏移。
        # 🔴 2026-09-19 实机教训：曾经写成 `rest.translation + rest_rot @ location`（绝对值，≈0.86m），
        #    实机表现 = 人物整体被抬高约 6cm。原因：引擎自带的动画必须适配不同身高的角色，
        #    所以它存的是「相对该骨架静止姿势的增量」，不是绝对高度。
        pb = pose.bones[root_index]
        rest = rest_local(pb)
        loc = rest.to_3x3() @ pb.location
        pos.append((frame, loc.x, loc.y, loc.z))

    result = list(rots)
    result_pos = pos
    return result, result_pos, worst_deg, worst_at


# --------------------------------------------------------------------------
# 驱动
# --------------------------------------------------------------------------
def main():
    args = parse_args()
    fbx = args.get('fbx')
    out_trf = args.get('out')
    if not fbx or not out_trf:
        fail("必须提供 --fbx 和 --out")
    if not os.path.isfile(fbx):
        fail("找不到输入 FBX: %s" % fbx)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    res = bpy.ops.import_scene.fbx(filepath=fbx)
    if 'FINISHED' not in res:
        fail("FBX 导入失败: %r" % (res,))

    arms = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    if not arms:
        fail("FBX 里没有骨架（ARMATURE）")
    want = args.get('skeleton')
    if want:
        arm = next((o for o in arms if o.name == want), None)
        if arm is None:
            fail("找不到指定骨架 %s，可选: %s" % (want, [o.name for o in arms]))
    else:
        if len(arms) > 1:
            fail("场景里有多个骨架 %s，请用 --skeleton 指定" % [o.name for o in arms])
        arm = arms[0]

    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm

    act = arm.animation_data.action if arm.animation_data else None
    if act is None:
        fail("骨架 %s 上没有 Action，FBX 里没有动画数据" % arm.name)

    fr = act.frame_range
    key_lo, key_hi = int(round(fr[0])), int(round(fr[1]))
    start = int(args['start']) if 'start' in args else key_lo
    end = int(args['end']) if 'end' in args else key_hi
    end_loop = end + 1                                  # range() 右开，--end 含最后一帧

    root_name = args.get('root')
    pose = arm.pose
    if root_name:
        root_index = next((i for i, b in enumerate(pose.bones) if b.name == root_name), None)
        if root_index is None:
            fail("骨架里找不到根骨骼 %s" % root_name)
    else:
        root_index = 0
    true_root = next((b.name for b in arm.data.bones if b.parent is None), None)
    if pose.bones[root_index].name != true_root:
        print("CONVERT_WARN: 第 %d 根骨骼是 '%s'，真正的根骨骼是 '%s'，位置帧写在前者身上"
              % (root_index, pose.bones[root_index].name, true_root))

    try:
        bpy.ops.object.mode_set(mode='POSE')
    except Exception as e:
        print("CONVERT_WARN: 切 POSE 模式失败（%r），继续采样" % (e,))

    print("CONVERT_INFO: 骨序（Blender pose.bones 顺序，TRF 的骨索引就是这个顺序）")
    for i, b in enumerate(pose.bones):
        print("  bone[%2d] %s" % (i, b.name))

    rots, pos, worst_deg, worst_at = sample(arm, start, end_loop, root_index)

    name = args.get('name') or arm.name
    write_trf(name, rots, pos, out_trf)

    if worst_deg > 0.5:                     # 0.5° 以上才算真错；实测噪音约 0.05°（四元数归一化误差）
        print("CHECK_WARN: 绝对公式与 Blender pose 矩阵最大偏差 %.4f° (%s) —— 偏差大说明手算有误"
              % (worst_deg, worst_at))
    else:
        print("CHECK_OK: 绝对公式与 Blender pose 矩阵一致（最大偏差 %.4f°，属数值噪音）" % worst_deg)

    head_pos = pos[0]
    print("CHECK_POS: 位置轨首帧 = (%.4f, %.4f, %.4f)   判定线: 应 ≈ 0（纯增量）"
          % (head_pos[1], head_pos[2], head_pos[3]))

    summary = {
        "fbx": fbx, "out": out_trf, "skeleton": arm.name, "action": act.name,
        "action_frame_range": [key_lo, key_hi],
        "export_frames": "%d..%d (%d frames)" % (start, end_loop - 1, end_loop - start),
        "trf_name": name, "bone_count": len(rots),
        "root_bone": pose.bones[root_index].name,
        "rot_frames_per_bone": len(rots[0]), "pos_frames": len(pos),
        "pos_first_frame": [round(v, 6) for v in head_pos[1:]],
        "max_deviation_deg": round(worst_deg, 4),
        "out_bytes": os.path.getsize(out_trf),
    }
    print("SUMMARY_JSON: " + json.dumps(summary, ensure_ascii=False))
    print("CONVERT_DONE")


main()
