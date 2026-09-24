# -*- coding: utf-8 -*-
"""离线复算「查看器眼里的脚底高度」—— 不需要浏览器。

🔴 为什么需要它：**查看器会掩盖一类病**。查看器里那些 GLB 是用 `fbx_to_glb` / `bake_glb_posetransfer`
烘的，走的是**世界姿态**（它的设计就写着"搬世界姿态则与静姿无关"）⇒ **FBX 绑定姿势被烘歪也看不出来**。
而**游戏读的是 TRF**，TRF 是按 FBX 的静止姿势存增量的 ⇒ 同一个病在游戏里会显形。
所以判「这类病」**不能靠肉眼看看器**，要用本脚本离线复算，或专门烘一份 TRF 直读的 GLB 来对照
（范本数据集：`viewer/datasets/ue_exec_trf/`）。

复刻 viewer.html 的归一化规则：
    h = 绑定姿势的骨骼世界包围盒高度（Blender 的 Z 轴 = glTF 的 Y 轴）
    s = TARGET_H / h ；模型整体上移，使【绑定姿势的脚底】落在 0
⇒ 查看器里某帧的「脚底高度」= (该帧骨骼最小世界 Z) − (绑定姿势骨骼最小世界 Z)

判读：≈0 = 踩在地上；≈+0.8 = 整体浮空。

实测基准（2026-09-24）：
  · `ue_exec_pair/assets/bl_pair7.glb`（FBX 路径）受害者 `Executed01` 首/中/末 = 0.016/0.041/−0.018 ✅
  · 同一批 clip 走 TRF 直读烘焙 = **0.893/0.255/0.799** ❌（绑定姿势被烘成躺姿）
  · 同 GLB 内的 `Executed02`（09-22 修过）= 0.068/−0.242/0.068 ✅ —— **自带对照，判读最省事**

用法：
    blender -b --factory-startup --python check_glb_feet.py -- <a.glb> [<b.glb> ...] [--clip 名字]
"""
import os
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ONLY = None
if "--clip" in argv:
    i = argv.index("--clip")
    ONLY = argv[i + 1]
    del argv[i:i + 2]
paths = argv


def bone_extent_z(arm):
    """当前帧：所有骨头的世界 Z 最小/最大值。"""
    lo, hi = 1e9, -1e9
    for pb in arm.pose.bones:
        z = (arm.matrix_world @ pb.head).z
        lo = min(lo, z); hi = max(hi, z)
    return lo, hi


for p in paths:
    if not os.path.isfile(p):
        print("!! 缺文件 %s" % p); continue
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=p)
    arm = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
    if arm is None:
        print("!! 无骨架 %s" % p); continue
    # 绑定姿势脚底（用 data bones 的静止位置）
    lo_bind = min((arm.matrix_world @ b.head_local).z for b in arm.data.bones)
    print("=" * 92)
    print("%s   骨骼 %d   绑定姿势脚底 Z=%.4f" % (os.path.basename(p), len(arm.data.bones), lo_bind))
    names = [a.name for a in bpy.data.actions]
    for name in sorted(names):
        if ONLY and ONLY not in name:
            continue
        arm.animation_data_create() if arm.animation_data is None else None
        arm.animation_data.action = bpy.data.actions[name]
        try:
            arm.animation_data.action_slot = bpy.data.actions[name].slots[0]
        except Exception:
            pass
        fr = bpy.data.actions[name].frame_range
        f0, f1 = int(fr[0]), int(fr[1])
        zs = []
        for f in (f0, int((f0 + f1) / 2), f1):
            bpy.context.scene.frame_set(f)
            bpy.context.view_layer.update()
            zs.append(round(bone_extent_z(arm)[0] - lo_bind, 4))
        print("   %-44s 帧%d..%d   脚底(首/中/末) = %s" % (name, f0, f1, zs))
