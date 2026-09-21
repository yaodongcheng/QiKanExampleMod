#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
build_spell_mesh.py — 法印施法体系的法术网格（阴魔斩：月牙 + 能量核）
============================================================================
用途：Blender 无头跑出两个静态网格 FBX，交给 ModKit（TaikouAnim 中转沙箱）导入。

    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python build_spell_mesh.py

产出（写 `tools/armor-pipeline/out/`）：
    lwn_yinmo_crescent.fbx   月牙弧 + 弧心的能量核（**合并成一件** —— 一发导弹只能挂一个
                             flying_mesh，而"核会作为月牙的中心点一起发射出去"，所以必须同一个网格）
    lwn_yinmo_core.fbx       只有能量核（蓄力时挂在手心那个球复用这一件）

尺寸依据（`Knowledge/骑砍2粒子系统.md` §九，从 `阴魔斩.mp4` 用角色身高当尺子量出来的）：
    弧半径 1.6~2.2 m · 整体跨度 3~4 m · 弧带厚 0.6~0.85 m（含辉光）
    本脚本取 R=1.95 / 弧角 155° → 弦长(跨度) = 2·R·sin(77.5°) ≈ 3.81 m ✓ 落在实测区间

🔴 朝向怎么定的（**这是本文件最要紧的一段，改之前先读**）：
    ① 原版飞行网格实测（`tpaccli dump` 出来的 `bolt_bl_a` / `bolt_bl_flying`）：
       **长轴 = Z**，且 `bolt_bl_a` 的原点在尾端、尖端在 z=+0.4785 ⇒ **+Z = 飞行方向**。
       （量出来的 0.4785 m 与物品的 `weapon_length="47"` 完全吻合 ⇒ dump 不缩放，单位就是米。）
    ② 所以**月牙要躺在自己的 XY 平面上**（法线 = Z = 飞行方向）⇒ 飞行时月牙面**正对前方**、
       侧看是一条线 —— 这才读得出"斩击波"。这与项目里 `lwn_flight_sigil` 的作图习惯同源
       （那个法阵也是躺在自己 XY 平面、法线 +Z）。
    ③ `HORN_UP` 是**唯一没实证的自由度**（弧的"犄角"朝上还是朝下，取决于引擎对 FBX 的轴重映射，
       本项目没实证过）。**实机看一眼，错了就翻这个开关重跑**——不用改别的。

⚠️ 本脚本产出的是 FBX **源**，不是交付。交付还要走：开 ModKit → 分发进
   `TaikouAnim/AssetSources/meshes/<名>/` → 编辑器重编 + Publish → 产物 tpac **改名**拷进内容包
   （铁律 31：分发必须在 ModKit 开着的时候做，否则文件监视抓不到）。
"""
import math
import os
import sys

import bpy

# ─────────────────────────── 参数（要改的都在这里） ───────────────────────────
NAME_CRESCENT = "lwn_yinmo_crescent"
NAME_CORE = "lwn_yinmo_core"

R = 1.95          # 弧半径（m）—— 实测 1.6~2.2
ARC_DEG = 155.0   # 弧角（度）—— 与 R 一起决定跨度：弦长 = 2R·sin(ARC/2) ≈ 3.81 m
BAND_BELLY = 0.85  # 弧腹（正中）处的径向带厚（m）—— 🔴 **必须 ≥ 核直径 0.30** 才"包得住"核
BAND_TIP = 0.18    # 其余弧段的径向带厚（m）—— 决定整体是"薄月牙"还是"胖月牙"
BULGE_POW = 3.0    # 鼓包集中度：1=整条一起粗 ｜ 3=弧腹平滑隆起（当前）｜ 7=只鼓正中间一小块（会成"驼峰"，试过）
DEPTH_BELLY = 0.50 # 弧腹处的刃厚（m）—— 与核同量级，核才"嵌"在刃里而不是穿在签子上
DEPTH_TIP = 0.03   # 其余弧段的刃厚（m）—— 薄，读得出"刃"
                   # 🔴 刃厚要比核**直径小一点**：核 ⌀0.72 vs 刃厚 0.50 ⇒ 两面各鼓出 0.11 m。
                   #    正面看是"月牙弧腹正中嵌着一颗球"，侧面看球从刃面微微鼓出来。
                   # 🔴 刃厚要比核**直径小**：径向"包裹"是让带子够宽（BAND_BELLY ≫ 核 ⌀0.30），
                   #    厚度方向则要**让核露出来** —— 核 ⌀0.30 vs 刃厚 0.20 ⇒ 两面各鼓出 0.05 m，
                   #    正面看就是"月牙弧腹正中嵌着一颗球"。刃厚若 ≥ 核直径，核会整个埋进刃里看不见。
CORE_R = 0.36     # 能量核半径（m）—— 🔴 **比弧腹中间宽度稍小**（用户 2026-09-21 裁定）：
                  #    ⌀0.72 vs 中间带宽 0.85 ⇒ 径向只剩 0.065 m 一圈边，读作"带子刚好含住球"。
                  #    ⚠️ 别再按录像里"手心蓄力球 0.27~0.31 m"取值 —— 那是**手上**那点白热盘，
                  #       飞出去当中心球时视觉上要撑满月牙的腹，太小就成了颗小珠子（实测踩过）。
CORE_POS = "belly"  # 核放哪：'belly' = 月牙弧腹的正中（被带子包住）｜'center' = 弧的曲率中心（圆的圆心）
HORN_UP = True    # 🔴 犄角朝上？实测不确定的自由度，错了翻这里
SEG_ARC = 96      # 弧向分段
SEG_BAND = 20     # 径向分段
TAPER_MIN = 0.02  # 犄角端的最小收束比例（别收成 0 = 退化面）

OUTDIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "out")

# 导出规格：与 tools/armor-pipeline/scripts/build_armor.py 逐字一致（那套已被编辑器验证过）
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
    # UV：没有 UV 的网格进编辑器会缺一层数据，以后要贴图也得重导一次 —— 这里顺手带上
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


def taper(u):
    """犄角收尖用的包络：u=0/1 收到 TAPER_MIN，中途接近 1。"""
    return max(TAPER_MIN, math.sin(math.pi * u) ** 0.35)


def band_half(u):
    """径向**半**带厚 = 细基带 + 弧腹鼓包（**相加**，不用 max）。
    🔴 用户裁定（2026-09-21）：**月牙的"中间"要能包住能量核** —— 是弧腹局部鼓，不是整条都加粗。
    ⚠️ 别改回 `max(基带, 鼓包)`：两条幂曲线取 max 会在交叉点留下 **C1 断点**，
       渲染出来就是弧腹两侧各一道折痕（第一版实拍过）。相加则全程光滑。"""
    s = math.sin(math.pi * u)
    return (BAND_TIP / 2.0) * taper(u) + ((BAND_BELLY - BAND_TIP) / 2.0) * (s ** BULGE_POW)


def depth_half(u):
    """刃的**半**厚（沿 Z）= 细刃 + 弧腹加厚，同样相加。整体比核薄 —— 让核在正面**露出来**。"""
    s = math.sin(math.pi * u)
    return (DEPTH_TIP / 2.0) * taper(u) + ((DEPTH_BELLY - DEPTH_TIP) / 2.0) * (s ** BULGE_POW)


def crescent_geometry():
    """月牙 = 弧带。躺在 **XY 平面**（法线 = Z）、沿 Z 双向鼓出、径向羽化成刃。
    返回 (顶点, 面, UV)：UV 的 u 沿弧、v 跨带 —— 将来贴"刃口亮、刃背暗"的渐变正好用它。"""
    mid = math.degrees(arc_mid_angle())        # 弧腹所在角度：270°=−Y（腹在下、犄角朝上）
    a0 = math.radians(mid - ARC_DEG / 2.0)
    a1 = math.radians(mid + ARC_DEG / 2.0)

    verts, faces, uvs = [], [], []
    # 上下两片壳（z>0 / z<0）；两片在 v=0 与 v=1 处 z 都收到 0 ⇒ 自然闭合
    for sheet in (+1.0, -1.0):
        base = len(verts)
        for iu in range(SEG_ARC + 1):
            u = iu / SEG_ARC
            a = a0 + (a1 - a0) * u
            hb, hd = band_half(u), depth_half(u)
            for iv in range(SEG_BAND + 1):
                v = iv / SEG_BAND
                s = 2.0 * v - 1.0                                  # −1（内缘）→ +1（外缘）
                r = R + s * hb
                lens = math.sqrt(max(0.0, 1.0 - s * s))            # 横截面透镜形：两边收成刃
                z = sheet * hd * lens
                verts.append((math.cos(a) * r, math.sin(a) * r, z))
                # 🔴 u 压到 [0, 0.5]：贴图是**左右半图集**（左半=跨带渐变、右半=核的径向渐变），
                #    月牙只许取左半，右半留给核（见 gen_spell_textures.py 顶部示意）
                uvs.append((u * 0.5, v))
        for iu in range(SEG_ARC):
            for iv in range(SEG_BAND):
                v0 = base + iu * (SEG_BAND + 1) + iv
                v1 = v0 + 1
                v2 = v0 + (SEG_BAND + 1) + 1
                v3 = v0 + (SEG_BAND + 1)
                faces.append((v0, v1, v2, v3) if sheet > 0 else (v3, v2, v1, v0))
    return verts, faces, uvs


def arc_mid_angle():
    """弧腹（弧的中点）所在的角度：270° = −Y（腹在下、犄角朝上）。"""
    return math.radians(270.0 if HORN_UP else 90.0)


def core_center():
    """能量核的球心。
    'belly'  = 弧腹的带子正中（= 月牙形体的正中心，会被带子包住）—— 用户指定的做法
    'center' = 弧的曲率中心（圆的圆心，离刃面一整个 R 远）—— 早期版本，作对照保留"""
    a = arc_mid_angle()
    if CORE_POS == "center":
        return (0.0, 0.0, 0.0)
    return (math.cos(a) * R, math.sin(a) * R, 0.0)


def uv_sphere_geometry(radius, center=(0.0, 0.0, 0.0), seg=32, ring=16):
    """UV 球（能量核）。手搓而不用 bpy.ops —— 无头模式下 ops 依赖上下文，容易炸。
    🔴 UV 是**平面投影**（沿 Z 投到 XY 的圆盘），不是球面 UV：
       贴图 `lwn_yinmo_core_d` 是一张**径向渐变**（中心白热 → 边缘暗紫黑），
       平面投影才能让"球心的白热"落在贴图中心、正反面各看到同一个亮心。
       球面 UV 做不到这件事（极点会拖成长条）。"""
    cx, cy, cz = center
    verts, faces, uvs = [], [], []
    for i in range(1, ring):
        phi = math.pi * i / ring
        for j in range(seg):
            th = 2.0 * math.pi * j / seg
            x = radius * math.sin(phi) * math.cos(th)
            y = radius * math.sin(phi) * math.sin(th)
            z = radius * math.cos(phi)
            verts.append((cx + x, cy + y, cz + z))
            # 平面投影，压到图集的**右上四分之一方块**（u∈[0.5,1] × v∈[0.5,1]）——
            # 图集左半是月牙的跨带渐变，右上是核的径向圆盘；圆心/半径与
            # gen_spell_textures.py 里的 (768,256)/256 严格对应，改一边必须改另一边
            uvs.append((0.75 + 0.25 * (x / radius), 0.75 + 0.25 * (y / radius)))
    top = len(verts); verts.append((cx, cy, cz + radius)); uvs.append((0.75, 0.75))
    bot = len(verts); verts.append((cx, cy, cz - radius)); uvs.append((0.75, 0.75))
    for i in range(ring - 2):
        for j in range(seg):
            a = i * seg + j
            b = i * seg + (j + 1) % seg
            c = (i + 1) * seg + (j + 1) % seg
            d = (i + 1) * seg + j
            faces.append((a, b, c, d))
    for j in range(seg):
        faces.append((top, (j + 1) % seg, j))
        faces.append((bot, (ring - 2) * seg + j, (ring - 2) * seg + (j + 1) % seg))
    return verts, faces, uvs


def export(name, verts, faces, uvs):
    wipe()
    ob = new_object(name, verts, faces, name, uvs)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    path = os.path.join(OUTDIR, name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, **FBX_KW)
    # 自检：FBX 里不许留贴图引用（照 build_armor.py 的做派——编辑器导入时找不到图会崩）
    blob = open(path, "rb").read()
    import re
    tex = sorted(set(m.decode("latin1") for m in
                     re.findall(rb"[ -~]{4,120}\.(?:png|tga|dds|jpg|jpeg)", blob)))
    print(f"   -> {path}  ({os.path.getsize(path)} bytes)  "
          f"UV 层 {len(ob.data.uv_layers)}  贴图引用: {tex if tex else '无 ✓'}")
    return path


def main():
    os.makedirs(OUTDIR, exist_ok=True)
    print(f"    === 法印施法体系 · 法术网格生成 ===")
    print(f"    弧半径 R={R}  弧角={ARC_DEG}°  跨度≈{2 * R * math.sin(math.radians(ARC_DEG) / 2):.2f} m")
    print(f"    带厚 腹{BAND_BELLY}/余{BAND_TIP}  刃厚 腹{DEPTH_BELLY}/余{DEPTH_TIP}"
          f"  核心球 ⌀{CORE_R * 2:.2f} m  犄角朝{'上' if HORN_UP else '下'}")
    cc = core_center()
    print(f"    核位置 {CORE_POS} = ({cc[0]:+.2f}, {cc[1]:+.2f}, {cc[2]:+.2f})")
    print(f"    包裹余量（径向往两边各）: {band_half(0.5) - CORE_R:+.3f} m"
          f"   ┃ 核沿 Z 露出的量: {CORE_R - depth_half(0.5):+.3f} m（正=露出来，负=埋进刃里）")
    print(f"    输出目录 {OUTDIR}\n")

    # ① 月牙 + 弧腹核（合并：一发导弹只能挂一个 flying_mesh）
    cv, cf, cuv = crescent_geometry()
    # 🔴 把弧腹（核）挪到网格原点：导弹的**原点 = 命中点**，核是玩家眼里这发法术的"正中"，
    #    所以原点必须落在核上。否则法术看着打在月牙正中、实际在 R 米开外命中（月牙会整体偏出去）。
    cv = [(x - cc[0], y - cc[1], z - cc[2]) for (x, y, z) in cv]
    kv, kf, kuv = uv_sphere_geometry(CORE_R, (0.0, 0.0, 0.0))
    off = len(cv)
    export(NAME_CRESCENT, cv + kv, cf + [tuple(i + off for i in f) for f in kf], cuv + kuv)
    print(f"       月牙 {len(cf)} 面 + 核 {len(kf)} 面")

    # ② 只有核（蓄力时挂手心复用）
    export(NAME_CORE, kv, kf, kuv)
    print(f"\nDONE —— 两个 FBX 已在 {OUTDIR}")


if __name__ == "__main__":
    main()
