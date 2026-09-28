#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
build_rasengan.py — 螺旋丸的两个网格：**中心球** + **4 片旋涡叶**
============================================================================
    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python build_rasengan.py

【这一版要做什么】照用户给的实机参考图（中心青蓝小球 + 四片白色旋涡叶）：
    ① `lwn_rasengan_core`  —— 正球，**半径 0.5（直径 1.0 m 基准）**
    ② `lwn_rasengan_blade` —— **4 片**旋涡叶，同一网格里出全（各差 90°），在 z=0 水平面上

【基准尺寸口径】（跟 `build_lightning_arc.py` 的"长 1.0 m"、`build_sphere_shell.py` 的"直径 1.0 m"同一套）
    代码/prefab 按基准缩放：`scale 1.0` = 球直径 1 m、叶片外径 2.1 m。
    参考图里叶片外伸约是球半径的 2 倍，所以 `BLADE_R1 = 1.05`（≈ 0.5 × 2.1）。

【为什么叶片是"一条螺旋带"而不是一朵花瓣】
    `blade_half_width(t)` 决定沿半径的形状：内端窄 → 三成处最宽 → 外端收尖；
    `BLADE_SWIRL` 决定**从内到外旋过多少度**（= 卷得多厉害）。两个旋钮分开调，
    改形状不用动参数化代码。

【UV 约定】（贴图脚本 `gen_rasengan_parts_tex.py` 按这个画，两边必须一致）
    球：**等距柱状**（u = 经度、v = 纬度），同 `build_sphere_shell.py`
    叶片：u = **沿带长**（0 = 内端、1 = 外端）· v = **横向**（0 / 1 = 两条边、0.5 = 中轴）
          ⇒ 贴图可以做成"沿长度渐隐 + 横向边缘柔化"

【三条沿用/新立的硬约定】
    · **两个绕序都出**（里外都画）⇒ 薄片从两侧看都不消失，也不依赖材质勾 Two Sided
    · **FBX 对象名 = 材质名 = 网格名**（铁律 32）—— 名字错 = 编辑器里那块是白板
    · 🔴🔴 **双绕序 × 平滑着色 = 法线抵消**（2026-09-27 实机撞出来的）：
      双绕序的网格里**同一个顶点同时属于"朝外面"和"朝里面"两个副本**，
      一旦开平滑，Blender 把这两套相反的法线**平均成 ≈ 0** ⇒ 顶点法线退化成乱数
      ⇒ 编辑器里那颗球变成**青蓝底 + 深蓝迷彩斑**（实测症状）。
      **规矩**：闭合体（球）= **单绕序 + 法线朝外 + 平滑**；
                薄片（叶片）= **双绕序 + 平面着色**（薄片本来就平，平面着色永远正确）。
      顺带纠正一条误判：球看着"一圈圈棱"**不是**平面着色造成的（老件 `lwn_lightning_shell`
      从没设过平滑、导入回来仍是 512/512 全平滑）—— 那是**分段太少**，见 `CORE_SEGS` 旁的注释。

产出（写 `tools/particle-pipeline/out/`，并自动拷进 `AssetSources/ImortReady/<名>/`）：
    lwn_rasengan_core.fbx · lwn_rasengan_blade.fbx
配套贴图见 `gen_rasengan_parts_tex.py`（另一条命令，不依赖 Blender）。
"""
import math
import os
import re
import shutil

import bpy

# ─────────────────────────── 参数 ───────────────────────────
NAME_CORE = "lwn_rasengan_core"
NAME_BLADE = "lwn_rasengan_blade"

# 中心球
CORE_R = 0.5          # 半径（m）—— 基准 = 直径 1.0 m，别改
# 🔴 分段是**棱面**的唯一旋钮（实测 2026-09-27）：Blender 导出的 FBX 本来就是平滑法线
#    （老件 `lwn_lightning_shell` 从没设过平滑、导入回来仍是 512/512 全平滑），
#    所以"球看着是一圈圈多面体"的真因是**每面夹角太大**——32×16 时每面约 11°，
#    近景一眼看穿。球是纯色发光件、又没有贴图纹理掩护，必须给足：
#    64×32 ⇒ 每面 5.6°，2304 面（双绕序后 4608），对单个视觉焦点球完全划算。
CORE_SEGS = 64        # 经度分段
CORE_RINGS = 32       # 纬度分段

# 叶片（4 片旋涡叶）
BLADE_COUNT = 4       # 片数
BLADE_R0 = 0.12       # 内端半径（m）—— 留一点空，别插进球里
BLADE_R1 = 1.05       # 外端半径（m）
BLADE_SWIRL = 72.0    # 从内到外**旋过的角度**（度）—— 越大越卷
BLADE_STEPS = 26      # 沿带长的分段（越大越圆滑）
W_IN = 0.075          # 内端弧长半宽（m）—— 参考图里内端就不细，留够宽度才饱满
W_MID = 0.260         # 最宽处弧长半宽（m）
W_TIP = 0.012         # 外端弧长半宽（m）
W_PEAK = 0.18         # 最宽处所在的 t（0 = 内端、1 = 外端）

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


# ─────────────────────────── 中心球 ───────────────────────────
def sphere_mesh(segs=CORE_SEGS, rings=CORE_RINGS):
    """正球（**等距柱状 UV**），照抄 `build_sphere_shell.py` 的两条细节：
       ① 接缝列复制一份（u 从 0.97 绕回 0 会把最后一列贴图反向压扁）
       ② 极点每格一个独立顶点（共用一个顶点会形成扇状挤压）
    """
    verts, faces, uvs = [], [], []
    z_top, z_bot = CORE_R, -CORE_R
    north = []
    for i in range(segs):
        north.append(len(verts))
        verts.append((0.0, 0.0, z_top))
        uvs.append(((i + 0.5) / segs, 1.0))
    band_start = len(verts)
    for j in range(1, rings):
        uu = math.cos(math.pi * j / rings)
        z = CORE_R * uu
        r = CORE_R * math.sqrt(max(0.0, 1.0 - uu * uu))
        for i in range(segs + 1):
            th = 2.0 * math.pi * i / segs
            verts.append((r * math.cos(th), r * math.sin(th), z))
            uvs.append((i / segs, 1.0 - j / rings))
    south = []
    for i in range(segs):
        south.append(len(verts))
        verts.append((0.0, 0.0, z_bot))
        uvs.append(((i + 0.5) / segs, 0.0))

    rows = rings - 1

    def idx(row, col):
        return band_start + row * (segs + 1) + col

    for i in range(segs):
        faces.append((north[i], idx(0, i), idx(0, i + 1)))
    for j in range(rows - 1):
        for i in range(segs):
            faces.append((idx(j, i), idx(j, i + 1), idx(j + 1, i + 1), idx(j + 1, i)))
    for i in range(segs):
        faces.append((south[i], idx(rows - 1, i + 1), idx(rows - 1, i)))
    return verts, faces, uvs


# ─────────────────────────── 4 片旋涡叶 ───────────────────────────
def blade_half_width(t):
    """沿半径的半宽（**弧长**，米）：内端窄 → W_PEAK 处最宽 → 外端收尖。
    在 (0,0) 处会退化成零宽 —— 这里内端给 W_IN 保底，避免自交出的烂面。"""
    if t <= W_PEAK:
        k = t / max(W_PEAK, 1e-6)
        return W_IN + (W_MID - W_IN) * math.sin(k * math.pi * 0.5)
    k = (t - W_PEAK) / max(1.0 - W_PEAK, 1e-6)
    return W_MID * ((1.0 - k) ** 1.15) + W_TIP * k


def blade_mesh():
    """4 片旋涡叶，**一次出全**（各差 90°），平铺在 z=0 平面上。
    UV：u = 沿带长（内→外），v = 横向（0 / 1 两条边，0.5 是中轴）。"""
    verts, faces, uvs = [], [], []
    for b in range(BLADE_COUNT):
        base = 2.0 * math.pi * b / BLADE_COUNT
        ring_start = len(verts)
        for i in range(BLADE_STEPS + 1):
            t = i / BLADE_STEPS
            r = BLADE_R0 + (BLADE_R1 - BLADE_R0) * t
            th = base + math.radians(BLADE_SWIRL) * t
            dth = blade_half_width(t) / max(r, 1e-4)      # 弧长 → 角度
            for sgn in (-1.0, 1.0):
                a = th + sgn * dth
                verts.append((r * math.cos(a), r * math.sin(a), 0.0))
                uvs.append((t, 0.5 + 0.5 * sgn))
        for i in range(BLADE_STEPS):
            b0 = ring_start + i * 2
            faces.append((b0, b0 + 1, b0 + 3, b0 + 2))
    return verts, faces, uvs


# ─────────────────────────── 导出 ───────────────────────────
def orient_outward(verts, faces, center=(0.0, 0.0, 0.0)):
    """单绕序网格专用：逐面算 Newell 法线，**不朝外就翻转该面的顶点顺序**。
    球心在原点 ⇒ 面心指向原点的反方向就是"外"。返回 (faces, 被翻正的面数)。"""
    out, flipped = [], 0
    for f in faces:
        n = [0.0, 0.0, 0.0]
        for i in range(len(f)):
            a = verts[f[i]]
            b = verts[f[(i + 1) % len(f)]]
            n[0] += (a[1] - b[1]) * (a[2] + b[2])
            n[1] += (a[2] - b[2]) * (a[0] + b[0])
            n[2] += (a[0] - b[0]) * (a[1] + b[1])
        inv = 1.0 / len(f)
        cx = sum(verts[i][0] for i in f) * inv - center[0]
        cy = sum(verts[i][1] for i in f) * inv - center[1]
        cz = sum(verts[i][2] for i in f) * inv - center[2]
        if n[0] * cx + n[1] * cy + n[2] * cz < 0.0:
            f = tuple(reversed(f))
            flipped += 1
        out.append(tuple(f))
    return out, flipped


def new_object(name, verts, faces, mat_name, uvs=None, smooth=False):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    # 🔴 **平滑着色只对单绕序网格开** —— 见文件头「双绕序 × 平滑 = 法线抵消」那条：
    #    双绕序的网格里，同一个顶点同时属于"朝外面"和"朝里面"两个副本，
    #    平滑会把两者的法线**平均掉**（≈ 0）⇒ 着色退化成迷彩状乱纹。
    if smooth:
        for p in me.polygons:
            p.use_smooth = True
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


def export(name, verts, faces, uvs, double_sided=True, smooth=False, lod_levels=LOD_LEVELS):
    wipe()
    if double_sided:
        # 薄片件（叶片）：里外各一份面 ⇒ 从两侧看都不消失，**但不能开平滑**（法线会抵消）
        use_faces = list(faces) + [tuple(reversed(f)) for f in faces]
        note = f"面 {len(use_faces)}（双绕序）· 平面着色"
    else:
        # 闭合体（球）：单绕序 + 法线朝外 + 平滑 ⇒ 才是光滑的圆
        use_faces, flipped = orient_outward(verts, faces)
        note = f"面 {len(use_faces)}（单绕序，{flipped} 面已翻正朝外）· 平滑着色"
    ob = new_object(name, verts, use_faces, name, uvs, smooth=smooth)
    shared_mat = ob.data.materials[0] if len(ob.data.materials) else None
    for lv in range(1, lod_levels):
        lod_ob = new_object(f"{name}.lod{lv}", verts, use_faces, name, uvs, smooth=smooth)
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
          f"{note}  UV 层 {len(ob.data.uv_layers)}  贴图引用: {tex if tex else '无 ✓'}")
    return path


def stage(fbx, name):
    # 🔴 目录名 = **完整资产名**（不去 `lwn_` 前缀）—— 与 `gen_rasengan_parts_tex.py`
    #    的落点口径一致，这样一个资产（网格 + 贴图）在 ImortReady 里躺在同一个目录，
    #    用户一次 Import 就够。
    stage_dir = os.path.join(STAGE_ROOT, name)
    os.makedirs(stage_dir, exist_ok=True)
    dst = os.path.join(stage_dir, name + ".fbx")
    shutil.copy2(fbx, dst)
    print(f"    已放入待导入目录 {dst}")


def main():
    os.makedirs(OUTDIR, exist_ok=True)
    print("    === 螺旋丸 · 中心球 + 4 片旋涡叶 ===")

    print(f"    [1/2] 中心球：半径 {CORE_R} m（直径 {CORE_R*2} m 基准）· "
          f"{CORE_SEGS} 经 × {CORE_RINGS} 纬 · 等距柱状 UV · 单绕序+平滑")
    v, f, uv = sphere_mesh()
    stage(export(NAME_CORE, v, f, uv, double_sided=False, smooth=True), NAME_CORE)

    print(f"    [2/2] 叶片：{BLADE_COUNT} 片 · 内端 r={BLADE_R0} → 外端 r={BLADE_R1} m · "
          f"旋过 {BLADE_SWIRL}° · 最宽 {W_MID*2} m（t={W_PEAK} 处）· 双绕序+平面着色")
    v, f, uv = blade_mesh()
    stage(export(NAME_BLADE, v, f, uv, double_sided=True, smooth=False), NAME_BLADE)

    print()
    print("=" * 72)
    print("接下来在 ModKit 里（用户操作）：")
    print("  ① Import 上面两个 FBX ⇒ 各自建同名网格 + 同名材质")
    print("  ② Import 配套贴图（`gen_rasengan_parts_tex.py` 生成，与本脚本无关）")
    print("  ③ 材质：Shader = pbr_translucent · Alpha Blend Mode = **Add**（黑底天然隐形）")
    print("     · 球那张若想更亮，另开 `self_illumination`（注意它和翻页抢 Vector1，本件不翻页，不冲突）")
    print("  ④ 场景里：建空的旋转节点 → 把球 / 叶片挂进去 → Save As Prefab")
    print("=" * 72)


if __name__ == "__main__":
    main()
