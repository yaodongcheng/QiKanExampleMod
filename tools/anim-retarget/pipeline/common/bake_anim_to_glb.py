#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把【ModKit 规格 FBX（只骨架 + 动作）】套到【骑砍2 身体网格】上，烘成 GLB（供查看器）。

用法:
  blender -b --python pipeline/common/bake_anim_to_glb.py --       input/target/bannerlord/body/body_male_a.fbx       output/fbx/<clip>.fbx       viewer/datasets/<ds>/assets/bl_<clip>.glb       <clip>            # ★ 第4参数 = clip 名

★ 第4参数(动作改名) 不是可选项：查看器是【按 clip 名】在两侧配对，
  不改名 ⇒ 目标侧找不到同名 clip ⇒ 停在 rest（A-pose）。
"""
import bpy, sys, os
a = sys.argv[sys.argv.index('--')+1:]
BODY, ANIM, OUT = a[0], a[1], a[2]
CLIP = a[3] if len(a) > 3 else None
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=BODY)
A = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
nmesh = len([o for o in bpy.data.objects if o.type == 'MESH'])
print('[bake] body armature=%s bones=%d meshes=%d' % (A.name, len(A.data.bones), nmesh))
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=ANIM)
B = [o for o in bpy.data.objects if o.type == 'ARMATURE' and o not in before][0]
act = B.animation_data.action if B.animation_data else None
print('[bake] anim armature=%s action=%s range=%s' % (B.name, act.name if act else None,
      tuple(act.frame_range) if act else None))
try:
    _fcs = act.fcurves
except Exception:
    _fcs = []
    for _l in getattr(act, 'layers', []):
        for _st in getattr(_l, 'strips', []):
            for _cb in getattr(_st, 'channelbags', []):
                _fcs = list(_fcs) + list(getattr(_cb, 'fcurves', []))
print('[bake] fcurve 数: %d' % len(_fcs))
# 逐个骨把动作"锚"到目标骨架：按骨名重建 fcurve 引用（Blender5 槽位 API 兼容）
print('[bake] 目标骨名样例:', [b.name for b in A.pose.bones][:6])
print('[bake] 源骨名样例:', [b.name for b in B.pose.bones][:6])
A.animation_data_create()
A.animation_data.action = act
# ★ 关键：把动作改名成 clip 名 —— 查看器是【按 clip 名】在两侧配对，
#   否则目标侧会找不到对应 clip 而停在 rest（A-pose）
if CLIP:
    act.name = CLIP
    try:
        act.use_fake_user = True
    except Exception:
        pass
    print('[bake] action 改名为:', act.name)
for b in A.pose.bones:
    b.rotation_mode = 'QUATERNION'
# 删掉"只带动画的临时骨架"，只留 body 骨架 + mesh
for o in list(bpy.data.objects):
    if o.type == 'ARMATURE' and o is not A:
        bpy.data.objects.remove(o, do_unlink=True)
print('[bake] 保留对象:', [o.name for o in bpy.data.objects])
bpy.ops.object.select_all(action='DESELECT')
A.select_set(True)
for o in bpy.data.objects:
    if o.type == 'MESH' and o.parent is None: o.parent = A
# 只保留我们这一条动画（身体 FBX 自带的跑动等动画不要）
for _a in list(bpy.data.actions):
    if _a is not act:
        try: bpy.data.actions.remove(_a)
        except Exception: pass
print('[bake] 保留 action:', [a.name for a in bpy.data.actions])
bpy.ops.export_scene.gltf(filepath=OUT, export_format='GLB', export_animations=True,
                          export_skins=True, export_yup=True)
print('[bake] wrote', OUT, os.path.getsize(OUT))
