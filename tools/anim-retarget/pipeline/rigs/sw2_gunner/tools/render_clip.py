# -*- coding: utf-8 -*-
"""渲染一段动画的正面/侧面帧图。
用法:
 blender -b --python render_clip.py -- --kind gltf|fbx --path X [--action p006]
        --face -Y|+Y --frames 0,5,10 --out DIR --prefix src [--h 1.75]
说明: 源(SW2 glTF) 直接播会因"根位移=髋空间增量"整体下沉, 故逐帧把脚最低点拉到 z=0。
"""
import bpy, sys, os, math, argparse
from mathutils import Vector

def parse():
    a = sys.argv[sys.argv.index("--")+1:]
    ap = argparse.ArgumentParser()
    ap.add_argument("--kind", default="gltf"); ap.add_argument("--path", required=True)
    ap.add_argument("--action", default=""); ap.add_argument("--face", default="-Y")
    ap.add_argument("--frames", default="0,5,10"); ap.add_argument("--out", default="out/frames")
    ap.add_argument("--prefix", default="clip"); ap.add_argument("--h", type=float, default=1.75)
    ap.add_argument("--fps", type=int, default=30)
    ap.add_argument("--views", default="front,side")
    ap.add_argument("--side_sign", default="auto")   # auto / 1 / -1  强制侧面相机所在 X 侧
    ap.add_argument("--res", default="480,640")
    return ap.parse_args(a)

def main():
    A = parse()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = A.fps
    if A.kind == "gltf":
        bpy.ops.import_scene.gltf(filepath=A.path)
    else:
        bpy.ops.import_scene.fbx(filepath=A.path)
    arm = [o for o in bpy.data.objects if o.type=='ARMATURE'][-1]
    meshes = [o for o in bpy.data.objects if o.type=='MESH' and o.parent==arm]
    if not meshes: meshes = [o for o in bpy.data.objects if o.type=='MESH']
    mesh = meshes[0]
    if A.action:
        act = bpy.data.actions.get(A.action)
        if arm.animation_data is None: arm.animation_data_create()
        arm.animation_data.action = act
        try:
            if hasattr(act,"slots") and len(act.slots): arm.animation_data.action_slot = act.slots[0]
        except Exception as e: print("slot", e)
    print("[render] arm=%s mesh=%s action=%s" % (arm.name, mesh.name, A.action))

    # 目标高度归一
    sc.frame_set(int(A.frames.split(",")[0])); bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    eo = mesh.evaluated_get(dg)
    bb = [eo.matrix_world @ Vector(c) for c in eo.bound_box]
    h = max(p.z for p in bb) - min(p.z for p in bb)
    s = A.h / h
    arm.scale = (s,s,s)   # mesh 系 arm 子级, 只缩放父级
    bpy.context.view_layer.update()
    print("[render] 归一化 scale=%.4f (原高 %.3f)" % (s, h))
    return A, arm, mesh, sc

A, arm, mesh, sc = main()

def char_frame(f):
    sc.frame_set(f); bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    eo = mesh.evaluated_get(dg)
    bb = [eo.matrix_world @ Vector(c) for c in eo.bound_box]
    return Vector((min(p.x for p in bb), min(p.y for p in bb), min(p.z for p in bb))), \
           Vector((max(p.x for p in bb), max(p.y for p in bb), max(p.z for p in bb)))

# ---- 逐帧贴地偏移(整体对象位移): 先算好每帧脚最低点, 渲染时再施加 ----
FR = [int(x) for x in A.frames.split(",")]
off = {}
for f in FR:
    lo, hi = char_frame(f)
    off[f] = -lo.z
print("[render] 逐帧贴地偏移(cm/单位):", {k: round(v,2) for k,v in off.items()})

# ---- 相机 / 灯光 ----
sc.frame_set(FR[0]); bpy.context.view_layer.update()
arm.location.z = off[FR[0]]
bpy.context.view_layer.update()
lo, hi = char_frame(FR[0])
ct = (lo + hi) * 0.5
side_dir = Vector((0,-1,0)) if A.face == "-Y" else Vector((0,1,0))
cd = bpy.data.cameras.new("Cam"); cd.type='ORTHO'
W, H = [int(x) for x in A.res.split(",")]
sc.render.resolution_x = W; sc.render.resolution_y = H
cd.ortho_scale = max(hi.z-lo.z, 0.1) * 1.35
cam = bpy.data.objects.new("Cam", cd); sc.collection.objects.link(cam); sc.camera = cam
tgt_obj = bpy.data.objects.new("T", None); sc.collection.objects.link(tgt_obj)
tgt_obj.location = Vector((ct.x, ct.y, (lo.z+hi.z)*0.5))
tc = cam.constraints.new('TRACK_TO'); tc.target = tgt_obj
tc.track_axis='TRACK_NEGATIVE_Z'; tc.up_axis='UP_Y'
sun = bpy.data.lights.new("S",'SUN'); sun.energy=4.0
so = bpy.data.objects.new("S", sun); sc.collection.objects.link(so)
so.rotation_euler=(math.radians(55), 0, math.radians(35))
w = bpy.data.worlds.new("W"); sc.world = w
try:
    w.use_nodes=True; bgn = w.node_tree.nodes.get("Background")
    if bgn: bgn.inputs[0].default_value=(0.16,0.17,0.20,1)
except Exception: pass
sc.render.engine='BLENDER_WORKBENCH'
sc.display.shading.light='STUDIO'
sc.display.shading.color_type='OBJECT'
A.out = os.path.abspath(A.out)
os.makedirs(A.out, exist_ok=True)

D = 3.5
for f in FR:
    sc.frame_set(f)
    arm.location.z = off[f]
    bpy.context.view_layer.update()
    for view in A.views.split(","):
        if view == "front":
            cam.location = Vector((tgt_obj.location.x, tgt_obj.location.y + side_dir.y*D, tgt_obj.location.z))
        else:
            # 侧面视角必须按角色自身朝向选相机侧，否则朝向相反的两套骨架
            # 会被拍成一左一右（源面朝 -Y / 骑砍面朝 +Y，二者本就相差 180°）。
            #  +X 相机看 -X：屏幕右 = +Y  -> 面朝 -Y 的角色屏幕朝左
            #  -X 相机看 +X：屏幕右 = -Y  -> 面朝 +Y 的角色屏幕朝左
            _ss = str(getattr(A, "side_sign", "auto")).lower()
            sx = (1.0 if A.face == "-Y" else -1.0) if _ss in ("auto", "") else float(_ss)
            cam.location = Vector((tgt_obj.location.x + sx*D, tgt_obj.location.y, tgt_obj.location.z))
        p = os.path.join(A.out, "%s_%s_f%03d.png" % (A.prefix, view, f))
        sc.render.filepath = p
        bpy.ops.render.render(write_still=True)
        print("[render] ->", p, "exists" if os.path.exists(p) else "MISSING!!")
print("[render] DONE")
