#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
build_sphere_shell.py — 施法框架「电罩」那层**球壳**网格
============================================================================
    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python build_sphere_shell.py

用途（2026-09-25 用户裁定）：复刻 FCS `NS_Lightning_Barrier` 时，**细长电弧粒子做不到**
（骑砍粒子只能出"点状电花"，拉长要靠 `turn_to_velocity_side` + skew，而那需要速度，
壳上的电丝不该有速度）⇒ 补一层**球壳网格 + 翻页电纹贴图**当本体，粒子只当点缀。

🔴 基准尺寸 = **半径 0.5（直径 1.0 m）** —— 跟 `build_lightning_arc.py` 的"长 1.0 m"同一个口径：
   代码把实体缩放到想要的实际尺寸（`scale 1.5` = 直径 1.5 m，正好配上半径 0.75 的粒子壳）。

产出（写 `tools/armor-pipeline/out/`，并自动拷进 `AssetSources/ImortReady/<子目录>/`）：
    lwn_lightning_shell.fbx   UV 球（等距柱状 UV）

🔴 **为什么必须是等距柱状 UV（equirect）**：贴图要按球面展开画（u = 经度、v = 纬度），
   翻页图集里每一格都是一张"球面展开图"，电弧才会均匀铺满球面而不是挤在赤道。
   极点用**每格一个独立极点顶点**处理（u 取该扇形中点），避免极点处整张贴图被挤成一点。

🔴 **两个绕序都出**（跟 `build_lightning_arc.py` 同款）⇒ 球**里外都画**：
   电罩要靠"透过壳看到背面"才有那一圈电丝的层次感；也省掉"材质必须勾 Two Sided"的依赖。
"""
import math
import os
import re
import shutil

import bpy

# ─────────────────────────── 参数 ───────────────────────────
NAME = "lwn_lightning_shell"
# 🔴 **不是正球，是蛋形椭球**（2026-09-25 用户裁定："人家的护盾类似一个鸡蛋壳"）。
#    UE 侧数据也印证：`SphereLocation.Non Uniform Scale = (1.5, 1.5, 2.0)` ⇒ **高是宽的 1.33 倍**。
#    比例**烘进网格**（不靠代码缩放）⇒ `scale` 就是整体倍率，`scale 2.0` = 1.5 m 宽 × 2.0 m 高。
HEIGHT = 1.0      # 基准高（m）—— 代码按这个基准缩放，别改
WIDTH = 0.75      # 基准最宽处（m）⇒ 宽:高 = 0.75（UE 是 1.5:2.0 = 0.75 ✓ 同一个比例）
EGG_K = 0.14      # 蛋形不对称量：>0 = **上宽下尖**、<0 = 上尖下宽、0 = 对称椭球
                  #   0.14 是"看得出是蛋、但不夸张"的量；想要更尖就往 0.25 给
SEGS = 32         # 经度分段（横向）—— 影响轮廓圆滑度
RINGS = 16        # 纬度分段（纵向）
LOD_LEVELS = 1

OUTDIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "out")
_TOOL = os.path.dirname(OUTDIR)
_REPO = os.path.dirname(os.path.dirname(_TOOL))
STAGE_ROOT = os.path.join(_REPO, "..", "TaikouAnim", "AssetSources", "ImortReady")

FBX_KW = dict(
    use_selection=False, object_types={'MESH'}, global_scale=1.0,
    apply_unit_scale=False, apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=False,
    use_mesh_modifiers=False, add_leaf_bones=False,
    primary_bone_axis='Y', secondary_bone_axis='X',
    axis_forward='Y', axis_up='Z',
    bake_anim=False, path_mode='COPY', embed_textures=False, use_custom_props=False,
)


def wipe():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def egg_profile(u):
    """给定归一化高度 u ∈ [-1(南极), +1(北极)]，返回 (z, 水平半径系数)。

    包络 = 椭圆 `sqrt(1-u²)`；蛋形 = 再乘一个线性不对称量 `(1 + EGG_K·u)`
    ⇒ 最宽处从赤道**上移**（EGG_K>0），下半更收 ⇒ 上宽下尖的蛋。
    """
    e = math.sqrt(max(0.0, 1.0 - u * u))
    m = 1.0 + EGG_K * u
    return (HEIGHT * 0.5) * u, (WIDTH * 0.5) * e * m


def uv_sphere(segs=SEGS, rings=RINGS):
    """蛋形壳（**等距柱状 UV**）。函数名保留 `uv_sphere` 免得改调用点；真实形状见 `egg_profile()`。

    🔴 两个细节决定成败：
      ① **接缝列要复制一份**（横向做 segs+1 列，最后一列空间位置等于第一列但 u=1.0）
         —— 不复制的话最后一个四边形会把 u 从 0.97 绕回 0，整张贴图在那一列被反向压扁。
      ② **极点每格一个独立顶点**（u 取该扇形中点）—— 一个极点共用会形成扇状挤压。
    """
    verts, faces, uvs = [], [], []
    z_top, _ = egg_profile(1.0)
    z_bot, _ = egg_profile(-1.0)
    # ── 北极：每格一个独立顶点 ──
    north = []
    for i in range(segs):
        north.append(len(verts))
        verts.append((0.0, 0.0, z_top))
        uvs.append(((i + 0.5) / segs, 1.0))
    # ── 中间纬度带（含接缝复制列）──
    band_start = len(verts)
    for j in range(1, rings):
        uu = math.cos(math.pi * j / rings)
        z, r = egg_profile(uu)
        for i in range(segs + 1):
            th = 2.0 * math.pi * i / segs
            verts.append((r * math.cos(th), r * math.sin(th), z))
            uvs.append((i / segs, 1.0 - j / rings))
    # ── 南极 ──
    south = []
    for i in range(segs):
        south.append(len(verts))
        verts.append((0.0, 0.0, z_bot))
        uvs.append(((i + 0.5) / segs, 0.0))

    rows = rings - 1                                   # 中间纬度带的行数
    def idx(row, col):                                 # col ∈ [0, segs]
        return band_start + row * (segs + 1) + col

    for i in range(segs):                              # 北极扇形
        faces.append((north[i], idx(0, i), idx(0, i + 1)))
    for j in range(rows - 1):                          # 中间四边形
        for i in range(segs):
            faces.append((idx(j, i), idx(j, i + 1), idx(j + 1, i + 1), idx(j + 1, i)))
    for i in range(segs):                              # 南极扇形
        faces.append((south[i], idx(rows - 1, i + 1), idx(rows - 1, i)))
    return verts, faces, uvs


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


def export(name, verts, faces, uvs, lod_levels=LOD_LEVELS):
    wipe()
    # 🔴 两个绕序都出 ⇒ 里外都画（见文件头）
    doubled = list(faces) + [tuple(reversed(f)) for f in faces]
    ob = new_object(name, verts, doubled, name, uvs)
    shared_mat = ob.data.materials[0] if len(ob.data.materials) else None
    for lv in range(1, lod_levels):
        lod_ob = new_object(f"{name}.lod{lv}", verts, doubled, name, uvs)
        if shared_mat is not None:
            lod_ob.data.materials.clear()
            lod_ob.data.materials.append(shared_mat)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    path = os.path.join(OUTDIR, name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, **FBX_KW)
    blob = open(path, "rb").read()
    tex = sorted(set(m.decode("latin1") for m in
                     re.findall(rb"[ -~]{4,120}\.(?:png|tga|dds|jpg|jpeg)", blob)))
    print(f"   -> {path}  ({os.path.getsize(path)} bytes)  顶点 {len(verts)}  "
          f"面 {len(doubled)}（含双绕序）  UV 层 {len(ob.data.uv_layers)}  "
          f"贴图引用: {tex if tex else '无 ✓'}")
    return path


def main():
    os.makedirs(OUTDIR, exist_ok=True)
    print("    === 施法框架 · 电罩蛋形壳生成 ===")
    print(f"    高 {HEIGHT} m × 最宽 {WIDTH} m（宽高比 {WIDTH / HEIGHT:.2f}）· 蛋形量 {EGG_K:+.2f}"
          f"（>0 = 上宽下尖）· {SEGS} 经 × {RINGS} 纬 · 等距柱状 UV")
    v, f, uv = uv_sphere()
    fbx = export(NAME, v, f, uv)

    sub = NAME[4:] if NAME.startswith("lwn_") else NAME
    stage_dir = os.path.join(STAGE_ROOT, sub)
    os.makedirs(stage_dir, exist_ok=True)
    dst = os.path.join(stage_dir, NAME + ".fbx")
    shutil.copy2(fbx, dst)
    print(f"    已放入待导入目录 {dst}")
    print("\n下一步（**用户操作**）：")
    print("  ① 在 ModKit 里 Import 上面的 FBX ⇒ 自动建网格 + 同名材质")
    print("  ② Import 配套的翻页电纹贴图（`lwn_lightning_shell_d.png`，等生成）")
    print("  ③ 材质配方同 `lwn_lightning_arc2`：Shader pbr_translucent · ☑ use_animated_texture_coords")
    print("     · Vector1 = (列, 行, 帧/秒, 总帧数) · Alpha Blend Mode = Add · Vector2 全 0")


if __name__ == "__main__":
    main()
