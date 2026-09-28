#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""render_glb_frames.py —— 从查看器 GLB 里按时间比例渲染若干帧（拿去和源素材并排对照）。

为什么单独立一个脚本：**机位语义太容易搞反，必须只有一处定义**。
    🔴 骑砍2 目标骨架 **面朝 +Y**（bannerlord_target.json 的 facing:"+Y"；进 glTF 后是 -Z）。
       ⇒ 能拍到【正面】的相机必须放在 **+Y 侧**。
       本脚本的 place(az) = center + (R·sin az, −R·cos az, ...)，所以：
           az=0   → 相机在 −Y = **角色背后**   （曾经在这里翻过车：把 az=0 当"正面"用，
                                              于是拿"源的正面"去比"我们的背面"，非对称动作被看成镜像）
           az=180 → 相机在 +Y = **角色正面** ✅
       对外只暴露 f/b/s/q 四个名字，不要把 az 数字暴露给调用方。

用法
    blender -b --python render_glb_frames.py -- --glb <glb> --outdir <dir> --clip <clip名> \
        --fracs 0,0.2,0.4,0.6,0.8,1.0 --view f [--size 400x540]
"""
import os
import sys
import math

import bpy
from mathutils import Vector


def _args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    o, i = {}, 0
    while i < len(a):
        if a[i].startswith("--"):
            o[a[i][2:]] = a[i + 1] if i + 1 < len(a) else ""
            i += 2
        else:
            i += 1
    return o


A = _args()
GLB = A["glb"]
OUT = A["outdir"]
CLIP = A["clip"]
FRACS = [float(x) for x in (A.get("fracs") or "0,0.2,0.4,0.6,0.8,1.0").split(",")]
VIEWS = [v.strip() for v in (A.get("view") or "f").split(",") if v.strip()]
RES = (A.get("size") or "400x540").lower().split("x")
RW, RH = int(RES[0]), int(RES[1])

# 🔴 唯一的机位定义处：名字 -> 方位角
AZ = {"f": 180.0,    # 正面（相机在 +Y 侧）
      "b": 0.0,      # 背面（相机在 -Y 侧）
      "s": 90.0,     # 侧面（相机在 +X 侧 = 角色右侧）
      "q": 215.0}    # 3/4 正面

os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)

arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o.name != 'human_lod_4':
        bpy.data.objects.remove(o, do_unlink=True)
mesh = bpy.data.objects.get('human_lod_4')

sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'STUDIO'
try:
    sc.display.shading.color_type = 'TEXTURE'
except Exception:
    sc.display.shading.color_type = 'MATERIAL'
sc.display.shading.show_shadows = False
sc.render.resolution_x, sc.render.resolution_y = RW, RH
sc.render.image_settings.file_format = 'PNG'
sc.world = bpy.data.worlds.new("W")

REST_TOP = 1.75                      # 站立基准高度：固定取景，别让动作把包围盒拉大导致镜头乱跳
center = Vector((0, 0, REST_TOP * 0.58))
maxdim = REST_TOP * 1.15
cam_d = bpy.data.cameras.new("C"); cam_d.type = 'ORTHO'; cam_d.ortho_scale = maxdim * 1.55
cam = bpy.data.objects.new("C", cam_d); sc.collection.objects.link(cam); sc.camera = cam
tgt = bpy.data.objects.new("T", None); sc.collection.objects.link(tgt); tgt.location = center
con = cam.constraints.new('TRACK_TO'); con.target = tgt
con.track_axis = 'TRACK_NEGATIVE_Z'; con.up_axis = 'UP_Y'   # UP_Y = 相机自身 +Y 对齐世界 +Z（摆正）
bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, 0))   # 地面：方便看脚有没有踩地
R = maxdim * 3.0


def place(az_deg, el_deg=6):
    az = math.radians(az_deg); el = math.radians(el_deg)
    cam.location = center + Vector((R * math.sin(az) * math.cos(el),
                                    -R * math.cos(az) * math.cos(el),
                                    R * math.sin(el)))
    bpy.context.view_layer.update()


act = bpy.data.actions.get(CLIP)
if act is None:
    have = [a.name for a in bpy.data.actions]
    print("!! GLB 里没有 clip '%s'，现有：%s" % (CLIP, have))
    sys.exit(2)
if arm.animation_data is None:
    arm.animation_data_create()
arm.animation_data.action = act
try:
    if hasattr(act, 'slots') and len(act.slots) > 0:
        arm.animation_data.action_slot = act.slots[0]
except Exception as e:
    print("  slot warn", e)

f0, f1 = act.frame_range
for frac in FRACS:
    fr = int(round(f0 + (f1 - f0) * frac))
    sc.frame_set(fr)
    bpy.context.view_layer.update()
    for v in VIEWS:
        place(AZ.get(v, 180.0))
        p = os.path.join(OUT, "%s@%02d_%s.png" % (CLIP, int(frac * 100), v))
        sc.render.filepath = p
        bpy.ops.render.render(write_still=True)
    print("  rendered %s frac=%.2f frame=%d (%s)" % (CLIP, frac, fr, ",".join(VIEWS)))
print("DONE")
