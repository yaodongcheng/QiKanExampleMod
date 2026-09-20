#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""健壮姿态渲染：FBX(骨架+网格+动作) 指定帧 -> 3/4正面/侧面 两张 PNG。
用 TRACK_TO 相机自动对准网格中心。--rest 渲染 rest pose 基准。
用法: blender --background --python render_pose.py -- <fbx> <frame> <out_prefix> [rest]
"""
import bpy, sys, math
from mathutils import Vector

def arg(i):
    a=sys.argv
    if "--" not in a: return None
    b=a[a.index("--")+1:]
    return b[i] if i<len(b) else None

fbx=arg(0); frame=int(arg(1) or "1"); out_pref=arg(2) or "render_out"
rest=(arg(3) or "").lower() in ("rest","1","true")
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
arm=next((o for o in bpy.data.objects if o.type=='ARMATURE'),None)
if not arm: print("NO ARMATURE"); sys.exit(1)
meshes=[o for o in bpy.data.objects if o.type=='MESH']
if not rest:
    if arm.animation_data and arm.animation_data.action is None and bpy.data.actions:
        arm.animation_data.action=bpy.data.actions[0]
        try:
            if hasattr(arm.animation_data.action,"slots") and arm.animation_data.action_slot is None:
                arm.animation_data.action_slot=arm.animation_data.action.slots[0]
        except Exception as e: print("slot warn",e)
    bpy.context.scene.frame_set(frame)
else:
    if arm.animation_data: arm.animation_data_clear()
    bpy.context.scene.frame_set(1)
bpy.context.view_layer.update()

# 网格中心/尺寸（绑定态 bbox 即可，稳定）
m=meshes[0] if meshes else None
if m:
    bb=[m.matrix_world @ Vector(c) for c in m.bound_box]
    minv=Vector((min(p.x for p in bb),min(p.y for p in bb),min(p.z for p in bb)))
    maxv=Vector((max(p.x for p in bb),max(p.y for p in bb),max(p.z for p in bb)))
else:
    minv=Vector((-0.6,-0.6,0));maxv=Vector((0.6,0.6,1.9))
center=(minv+maxv)/2
maxdim=max(maxv.x-minv.x, maxv.y-minv.y, maxv.z-minv.z)

sc=bpy.context.scene
cam_d=bpy.data.cameras.new("Cam"); cam_d.type='ORTHO'; cam_d.ortho_scale=maxdim*1.35
cam=bpy.data.objects.new("Cam",cam_d); sc.collection.objects.link(cam); sc.camera=cam
tgt=bpy.data.objects.new("Target",None); sc.collection.objects.link(tgt); tgt.location=center
con=cam.constraints.new('TRACK_TO'); con.target=tgt
con.track_axis='TRACK_NEGATIVE_Z'; con.up_axis='UP_Y'
lamp=bpy.data.lights.new("Sun",'SUN'); lamp.energy=3.0
lo=bpy.data.objects.new("Sun",lamp); sc.collection.objects.link(lo)
lo.rotation_euler=(math.radians(50),0,math.radians(30))
sc.render.engine='BLENDER_WORKBENCH'
sc.display.shading.light='STUDIO'; sc.display.shading.color_type='MATERIAL'
sc.render.resolution_x=440; sc.render.resolution_y=600
D=maxdim*3.0
def shoot(loc,name):
    cam.location=loc
    sc.render.filepath=out_pref+"_"+name+".png"
    bpy.ops.render.render(write_still=True)
    print("WROTE",sc.render.filepath)
# 3/4 正面：+Y 偏 +X
shoot((center.x+D*0.7, center.y+D, center.z+D*0.15), "q34")
# 侧面：+X
shoot((center.x+D, center.y, center.z), "side")
print("RENDER DONE")
