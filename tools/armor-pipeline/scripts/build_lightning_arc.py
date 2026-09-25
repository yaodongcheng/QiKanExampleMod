#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
build_lightning_arc.py — 施法框架「引导/射线」那条弧的网格
============================================================================
用途：Blender 无头跑出一个静态网格 FBX，交给 ModKit（TaikouAnim 中转沙箱）导入。

    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python build_lightning_arc.py

产出（写 `tools/armor-pipeline/out/`）：
    lwn_lightning_arc.fbx    一条"十字交叉双片"的弧带（**非**月牙，是直的）

🔴 形状与朝向（照 `build_spell_mesh.py` 的约定，改之前先读那段）：
    ① 长轴 = 本地 **+Z**，**原点在起点端（z=0）、远端在 z=+1.0**。
       ⇒ 代码把实体摆在起点、朝向目标、**把 Z 基向量乘上距离（米）**就是正确长度（1 m 基准）。
    ② **十字双片**（两片互成 90°）：单片从侧面看会消失（背面剔除 + 视线平行），
       十字就保证**任何水平视角至少有一片正对** ⇒ 电弧不会"转到侧面就没了"。
    ③ 每片**两种绕序都出**（共 8 个三角形）⇒ 不需要引擎开双面，正反都画。

尺寸：长 1.0 m × 宽 0.12 m —— 宽是**可见宽度**，想更粗的电弧改 `WIDTH` 重跑（不许改长度，
      长度是代码缩放的基准）。

⚠️ 本脚本产出的是 FBX **源**，不是交付。交付 = 放 `TaikouAnim/AssetSources/meshes/lwn_lightning_arc/`
   → **用户在 ModKit 里 Import**（铁律 36：新资产一律由用户导入，Claude 不许往 `Assets/` 塞东西）。
"""
import math
import os
import re
import shutil

import bpy

# ─────────────────────────── 参数 ───────────────────────────
# 要出哪几个网格。🔴 **名字 = FBX 里的对象名 = 导入时编辑器自动建的那个材质名**
#   ⇒ 想给一张新贴图配一个独立材质，就出一个**同名**的网格（贴图 `<名>_d.png`、材质 `<名>`）。
# 2026-09-25：给「源端集中 / 远端发散」那张 `lwn_lightning_arc2_d` 配 `lwn_lightning_arc2`。
BUILD = ["lwn_lightning_arc2"]
# 首版 `lwn_lightning_arc` 已注册、正在用 —— 只有真要重建它时才加回上面的列表
LENGTH = 1.0     # 沿 +Z 的长度（m）——🔴 **必须 1.0**（代码按"距离(米) × Z 基向量"拉伸）
WIDTH = 0.12     # 可见宽度（m）—— 🔴 **同时是「贴图横向摆幅」的物理天花板**：
                 #   贴图上画得再开，落到实物也只有 ±(0.5×WIDTH) 的横向余量（见 gen_lightning_arc_tex.py）
LOD_LEVELS = 1   # 自管实体路线不需要多档 LOD（场景实体不吃导弹那套距离剔除）

OUTDIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "out")
# 交付落点 = **待导入目录**（新资产一律交用户 Import —— CLAUDE.md 铁律 36）
_TOOL = os.path.dirname(OUTDIR)                                     # tools/armor-pipeline
_REPO = os.path.dirname(os.path.dirname(_TOOL))                     # 仓库根
STAGE_ROOT = os.path.join(_REPO, "..", "TaikouAnim", "AssetSources", "ImortReady")

# 导出规格：与 build_spell_mesh.py / build_armor.py 逐字一致（那套已被编辑器验证过）
FBX_KW = dict(
    use_selection=False, object_types={'MESH'}, global_scale=1.0,
    apply_unit_scale=False, apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=False,
    use_mesh_modifiers=False, add_leaf_bones=False,
    primary_bone_axis='Y', secondary_bone_axis='X',
    axis_forward='Y', axis_up='Z',
    bake_anim=False, path_mode='COPY', embed_textures=False, use_custom_props=False,
)


def wipe():
    """清空场景（每次只让一个网格进 FBX —— 资源名 = FBX 里的对象名，混装会串名）。"""
    bpy.ops.wm.read_factory_settings(use_empty=True)


def new_object(name, verts, faces, mat_name, uvs=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    if uvs is not None:
        uvl = me.uv_layers.new(name="UVMap")
        for loop in me.loops:
            uv = uvs[loop.vertex_index] if loop.vertex_index < len(uvs) else (0.0, 0.0)
            uvl.data[loop.index].uv = uv
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    mat = bpy.data.materials.new(mat_name)
    mat.use_nodes = True
    ob.data.materials.append(mat)
    return ob


def arc_geometry():
    """十字双片。返回 (顶点, 面, UV)。

    UV 口径：**u = 宽度方向、v = 长度方向**（贴图请用"一条竖着的闪电"，
    即我出的 `lwn_lightning_arc_d.png`；换别的图时注意这张图是真·单条、不是分镜图集）。
    """
    hw = WIDTH / 2.0
    verts, faces, uvs = [], [], []

    # 两片：A 片在 XZ 平面（宽度沿 X），B 片在 YZ 平面（宽度沿 Y），互成 90°
    for axis in ("x", "y"):
        base = len(verts)
        for iz in (0.0, 1.0):          # 沿长度：起点端 z=0 → 远端 z=LENGTH
            for iw in (-1.0, 1.0):     # 沿宽度：-hw → +hw
                off = iw * hw
                x = off if axis == "x" else 0.0
                y = 0.0 if axis == "x" else off
                verts.append((x, y, iz * LENGTH))
                uvs.append(((iw + 1.0) / 2.0, iz))   # u=宽、v=长
        # 四个顶点索引：0=(z0,-w) 1=(z0,+w) 2=(z1,-w) 3=(z1,+w)
        v0, v1, v2, v3 = base, base + 1, base + 2, base + 3
        # 两种绕序都出 ⇒ 正反都画（引擎不必开双面）
        faces.append((v0, v1, v3, v2))
        faces.append((v2, v3, v1, v0))
    return verts, faces, uvs


def export(name, verts, faces, uvs, lod_levels=LOD_LEVELS):
    wipe()
    ob = new_object(name, verts, faces, name, uvs)
    shared_mat = ob.data.materials[0] if len(ob.data.materials) else None
    for lv in range(1, lod_levels):
        lod_ob = new_object(f"{name}.lod{lv}", verts, faces, name, uvs)
        if shared_mat is not None:      # 共用同一份材质 datablock（铁律 32：各自 new 会被去重成 .001）
            lod_ob.data.materials.clear()
            lod_ob.data.materials.append(shared_mat)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    path = os.path.join(OUTDIR, name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, **FBX_KW)
    blob = open(path, "rb").read()
    tex = sorted(set(m.decode("latin1") for m in
                     re.findall(rb"[ -~]{4,120}\.(?:png|tga|dds|jpg|jpeg)", blob)))
    print(f"   -> {path}  ({os.path.getsize(path)} bytes)  "
          f"顶点 {len(verts)}  面 {len(faces)}  UV 层 {len(ob.data.uv_layers)}  "
          f"贴图引用: {tex if tex else '无 ✓'}")
    return path


def main():
    os.makedirs(OUTDIR, exist_ok=True)
    print("    === 施法框架 · 引导弧网格生成 ===")
    print(f"    长 {LENGTH} m（+Z，原点在起点端）× 宽 {WIDTH} m × 十字双片（{LOD_LEVELS} 档 LOD）")
    v, f, uv = arc_geometry()
    for name in BUILD:
        print(f"\n  -- {name}")
        fbx = export(name, v, f, uv)
        # 交付：拷进「待导入」目录（子目录 = 去掉 lwn_ 前缀的名字）
        sub = name[4:] if name.startswith("lwn_") else name
        stage_dir = os.path.join(STAGE_ROOT, sub)
        os.makedirs(stage_dir, exist_ok=True)
        dst = os.path.join(stage_dir, name + ".fbx")
        shutil.copy2(fbx, dst)
        print(f"     已放入待导入目录 {dst}")
    print(f"\nDONE —— FBX 在 {OUTDIR}")
    print("下一步（**用户操作**）：在 ModKit 里 Import 上面的 FBX")
    print("  🔴 导入后编辑器会**自动建一个同名材质**（例：`lwn_lightning_arc2`）——")
    print("     贴图 `AssetSources/ImortReady/<子目录>/<名>_d.png` 也要先 Import，再在材质里挂上。")
    print("     材质配方：Shader pbr_translucent · ☑ use_animated_texture_coords · Vector1=(4, 4, 18, 16)")
    print("             · Alpha Blend Mode = Add · Vector2 全 0")


if __name__ == "__main__":
    main()
