#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
preview_mesh.py — 回读 FBX 量尺寸 + 渲染三视图（验收用，不进交付）
============================================================================
为什么要有它：网格是"视觉产物"，**没有截图通道就是盲改**。
本脚本把 `build_spell_mesh.py` 产出的 FBX 重新导入 Blender，报包围盒，
再从三个方向各渲一张 PNG —— 形状对不对、朝向对不对，看一眼就知道，不用先过一轮 ModKit。

    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python preview_mesh.py
    "…blender.exe" -b --python preview_mesh.py -- --in <fbx> --out <png 前缀>

产出：<out>_front.png（沿 −Z 看，即"迎面飞来"）/ _side.png（沿 −X 看）/ _quarter.png（斜 45°）
      + stdout 里每个对象的尺寸与包围盒
"""
import math
import os
import sys

import bpy
from mathutils import Vector


def argv_after_ddash():
    if "--" in sys.argv:
        return sys.argv[sys.argv.index("--") + 1:]
    return []


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def bbox_of(objs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for ob in objs:
        if ob.type != 'MESH':
            continue
        for c in ob.bound_box:
            w = ob.matrix_world @ Vector(c)
            lo = Vector((min(lo[i], w[i]) for i in range(3)))
            hi = Vector((max(hi[i], w[i]) for i in range(3)))
    return lo, hi


def setup_camera(lo, hi, direction, name):
    """正交相机，从 direction 方向看包围盒中心，自动取景。"""
    center = (lo + hi) * 0.5
    size = max((hi - lo).x, (hi - lo).y, (hi - lo).z)
    cam_data = bpy.data.cameras.new(name)
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = size * 1.25
    cam_data.clip_end = size * 20
    cam = bpy.data.objects.new(name, cam_data)
    bpy.context.collection.objects.link(cam)
    d = Vector(direction).normalized()
    cam.location = center + d * size * 4.0
    # 让相机 −Z 对准 center
    look = (center - cam.location).normalized()
    cam.rotation_euler = look.to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = cam
    return cam


def apply_texture_material(fbx, meshes):
    """把 `<网格名>_d.png`（FBX 旁边那张）接成**自发光材质**，预览才看得出配色。
    🔴 只在预览里这么做 —— **交付的 FBX 里不能带贴图引用**（build_armor.py 的自检就是查这个：
       编辑器导入时找不到图会连源图带产物一起删）。真材质在 ModKit 里设。"""
    stem = os.path.splitext(os.path.basename(fbx))[0]
    png = os.path.join(os.path.dirname(fbx), stem + "_d.png")
    if not os.path.isfile(png):
        print(f"    （没找到 {stem}_d.png，保持灰模）")
        return False
    img = bpy.data.images.load(png)
    mat = bpy.data.materials.new(stem + "_preview")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = 'Linear'
    emit = nt.nodes.new("ShaderNodeEmission")
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(tex.outputs["Color"], emit.inputs["Color"])
    nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
    for ob in meshes:
        ob.data.materials.clear()
        ob.data.materials.append(mat)
    print(f"    材质 {stem}_d.png → 自发光 ✓")
    return True


def pick_engine():
    """挑一个能跑自发光的渲染后端（EEVEE 在 4.2+ 改叫 _NEXT，名字逐版本变，别写死）。"""
    for name in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
        try:
            bpy.context.scene.render.engine = name
            return name
        except Exception:
            continue
    return None


def render(path, w=900, h=900):
    sc = bpy.context.scene
    eng = pick_engine()
    if eng == "CYCLES":
        sc.cycles.samples = 32
    # 背景用**中灰偏暗**：自发光色才压得住、也还看得清轮廓（纯黑会把暗部刃口吃掉）
    wd = sc.world or bpy.data.worlds.new("W")
    sc.world = wd
    wd.use_nodes = True
    bg = wd.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.13, 0.14, 0.17, 1.0)
        bg.inputs[1].default_value = 1.0
    sc.render.resolution_x = w
    sc.render.resolution_y = h
    sc.render.film_transparent = False
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    args = argv_after_ddash()
    outdir = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "out")
    targets = []
    if "--in" in args:
        fbxs = [args[args.index("--in") + 1]]
        prefix = args[args.index("--out") + 1] if "--out" in args else os.path.join(outdir, "preview")
    else:
        fbxs = [os.path.join(outdir, n + ".fbx") for n in ("lwn_yinmo_crescent", "lwn_yinmo_core")]
        prefix = os.path.join(outdir, "preview")

    print("=== 法术网格回读 ===")
    for fbx in fbxs:
        if not os.path.isfile(fbx):
            print(f"  [跳过] 不存在 {fbx}")
            continue
        clear()
        objs = import_fbx(fbx)
        meshes = [o for o in objs if o.type == 'MESH']
        lo, hi = bbox_of(objs)
        print(f"\n--- {os.path.basename(fbx)}")
        for o in meshes:
            print(f"    对象 {o.name:28} 顶点 {len(o.data.vertices):6d}  面 {len(o.data.polygons):6d}"
                  f"  尺寸 {o.dimensions.x:.3f} × {o.dimensions.y:.3f} × {o.dimensions.z:.3f} m")
        print(f"    包围盒 X[{lo.x:+.3f},{hi.x:+.3f}]  Y[{lo.y:+.3f},{hi.y:+.3f}]  Z[{lo.z:+.3f},{hi.z:+.3f}]")
        apply_texture_material(fbx, meshes)

        stem = os.path.splitext(os.path.basename(fbx))[0]
        for label, direction in (("front", (0, 0, 1)), ("side", (1, 0, 0)), ("quarter", (1, -1, 0.35))):
            for o in [x for x in bpy.data.objects if x.type == 'CAMERA']:
                bpy.data.objects.remove(o, do_unlink=True)
            setup_camera(lo, hi, direction, "cam_" + label)
            p = f"{prefix}_{stem}_{label}.png"
            render(p)
            print(f"    渲染 {p}   （{label}：{'迎面' if label == 'front' else '侧面' if label == 'side' else '斜 45°'}）")


if __name__ == "__main__":
    main()
