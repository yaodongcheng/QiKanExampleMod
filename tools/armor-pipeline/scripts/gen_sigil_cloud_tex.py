#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_sigil_cloud_tex.py — 把飞行法阵的贴图换成「烟雾云」（`smoke_d` 四态图集）
============================================================================
    python tools/armor-pipeline/scripts/gen_sigil_cloud_tex.py

背景（2026-09-22 用户要求）：飞行载具 `<lwn_flight_sigil>` 原来贴的是一张法阵图
（六芒星+蓝符文），改成**类似 `prt_shd_smoke` 那种云粒子的观感** —— 即一块能被阳光照到的烟云。

🔴🔴 **2026-09-22 结论修正：不要抽格，要保留整张 2×2 图集**
    骑砍材质**能**做贴图动画 —— 材质勾 shader flag `USE_ANIMATED_TEXTURE_COORDS`，
    `pbr_standart_vertex_functions.rsh` 里有现成实现（引擎自己的火把闪烁就用它）：

        num_frames_x = g_mesh_vector_argument.x      // 图集列数
        num_frames_y = g_mesh_vector_argument.y      // 图集行数
        animation_speed = g_mesh_vector_argument.z   // 播放速度
        num_frames   = g_mesh_vector_argument.w      // 总帧数
        cur_frame = (g_time_var * animation_speed + 按世界位置错相) % num_frames
        tex_coord.xy *= (1/num_frames_x, 1/num_frames_y)   // 缩到一格
        tex_coord.xy += 当前帧的格偏移                      // 再挪到那一格

    ⇒ **UV 由 shader 自己缩格**，所以贴图必须是**完整图集**、网格 UV 保持 [0,1]
      （法阵网格实测就是 u[0,1] v[0,1] ✓）。抽成一格 = 只剩一态、动画无从谈起。
    参数由 C# 给：`TaleWorlds.Engine.Mesh.SetVectorArgument(列, 行, 速度, 总帧)`
      （法阵是我们自己的 `CarrierBoard.cs` 生成的，运行时设即可）。

法阵网格实测（2026-09-22，`preview_mesh.py --in lwn_flight_sigil.fbx`）：
    8 顶点 / 2 面（= 一个四边形）· 2.6 × 2.6 m · UV 满 [0,1] ⇒ **换贴图不用动网格**。

配套的材质（在 ModKit 里设，字段抄自原版 `prt_shd_smoke_1`，见
`tools/particle-pipeline/preview/mats/prt_shd_smoke_1.mat.txt`）：
    blend       = add_modulate_combined      ← 决定"烟"的观感（不是纯加也不是纯乘）
    shaderFlags = use_sunlight               ← 让烟被太阳照亮
    flags       = dont_draw_to_gbuffer, no_modify_depth_buffer, needs_forward_rendering
    tex[0]      = 本脚本产出的这张
"""
import os
import shutil
import subprocess
import sys

import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = os.path.dirname(HERE)                                    # tools/armor-pipeline
REPO = os.path.dirname(os.path.dirname(TOOL))                   # 仓库根（LivingWorldNpcs）
OUTDIR = os.path.join(TOOL, "out")
PNG_FOR_EDITOR = os.path.join(os.path.dirname(TOOL), "face-pipeline", "scripts", "png_for_editor.py")
SMOKE_D = os.path.join(TOOL.rsplit(os.sep, 1)[0], "particle-pipeline", "preview", "smoke_d_256.png")
# UE 原版 VFX 贴图库（`tools/particle-pipeline` 导出的 262 张；与 gen_spell_textures.py 同一个源）
UE_TEX_DIR = os.environ.get(
    "BM_UE_TEX",
    r"D:\BrainMaker\骑砍2粒子特效复刻\output\tex\FlexibleCombatSystem\VFX\Textures")

TARGET = os.path.join(REPO, "..", "TaikouAnim", "AssetSources", "meshes", "flight_sigil", "lwn_flight_sigil_d.png")
NAME = "lwn_flight_sigil_d.png"

MODE = "cloud4"  # 🔴 "cloud4" = **4 格圆云图集**（当前）—— 唯一能同时拿到「圆形 + 内聚外散 + 会动」：
                 #   · sweep（连续漂移）会把**烘在贴图里的圆形遮罩一起漂走** → 云飘出圆心、边缘露回方块 ✗
                 #   · 翻页是**缩放+偏移**不是平移（`tex_coord *= 1/列行; += 格偏移`）⇒ 每格里的圆形遮罩
                 #     **稳稳停在四边形正中心** ✓，4 帧切换 = 云在动 ✓
                 #   ⇒ 把「圆 + 内聚外散」烘进**每一格**，配 use_animated_texture_coords。
                 #   代价：4 帧硬切（无过渡）—— 用柔和的云团当 4 格，把"切"的感觉压到最低。
                 #   备选（若硬切仍碍眼）：网格从四边形改成**圆盘**、遮罩改烘进几何，那时才能自由用 sweep。
                 # "sweep"    = 连续 UV 漂移（`use_texture_sweep`）—— 无遮罩时用；会漂走遮罩
                 # "flipbook" = smoke_d 四态硬切（无圆形遮罩）
CELL = -1       # 仅 flipbook 模式用：-1 = 保留整张图集；0..3 = 只抽那一格
SIZE = 1024

# cloud4：云底 + 圆形遮罩的参数
C4_TEX = "T_Cloud_Noise_02.png"   # 无缝云噪声（256²，无缝 ✓）
C4_R_IN = 0.42      # 内聚半径（归一化，1.0 = 四边形外接圆）：此半径内满亮度
C4_R_OUT = 0.96     # 外散半径：到此为止衰减到 0（= 四边形**内切圆**附近，方角彻底不发光）
C4_FALL = 2.2       # 衰减曲线指数：越大边缘化得越快
C4_GAIN = 1.15      # 整体增益（补偿遮罩吃掉的平均亮度）。⚠️ 给太大（试过 1.55）中心会糊成白饼、云纹看不见

# sweep 模式的底图：必须**可平铺**（UV 会一直漂，接缝会周期性扫过画面）
# 实测接缝（`_tile_check.py` 那套判据：接缝差异 < 内部相邻差异×1.6 才算无缝）：
#   T_Inky_Smoke_Tile          接缝 2.2 / 内部 0.8  → **有缝，名字骗人，别用**
#   T_Cloud_Wisp_Chromatic_Tile 接缝 2.1 / 内部 1.7 → 无缝 ✓ 选中（1024²，絮状云）
#   T_Cloud_Noise_02            无缝 ✓ 但只有 256²（要更大的团块可换它，代价是放大发糊）
#   T_AtmosphericCloudNoise01   无缝 ✓ 2048² 但颗粒太细，2.6m 的片上读作噪点
SWEEP_TEX = "T_Cloud_Wisp_Chromatic_Tile.png"
# 亮度重映射：原图暗部 ≈34 不是 0 —— 加法混合下"不黑"= 整片泛灰。
# 把 [LO, HI] 拉满到 [0, 255]，暗部才真正"不发光"。
LO, HI = 55.0, 220.0
GAMMA = 1.0     # >1 = 更硬（云更稀）、<1 = 更柔（云更满）
FADE = 0.46     # 仅 flipbook 模式用：径向羽化起点


def build_sweep():
    """连续漂移用的云贴图：**可平铺** + 亮度重映射到「黑底白烟」（加法就绪）。
    🔴 不做径向羽化 —— 漂移时贴图会绕着圈走，羽化过的四角会在画面里周期性地扫出一道暗角。"""
    p = os.path.join(UE_TEX_DIR, SWEEP_TEX)
    if not os.path.isfile(p):
        print(f"[FATAL] 找不到 {p}\n        改 UE_TEX_DIR / SWEEP_TEX")
        raise SystemExit(2)
    a = np.asarray(Image.open(p).convert("L").resize((SIZE, SIZE), Image.LANCZOS), dtype=np.float32)
    # 亮度重映射：暗部拉到 0（加法下 = 不发光），亮部拉到 255
    a = np.clip((a - LO) / max(1.0, HI - LO), 0.0, 1.0) ** GAMMA * 255.0
    rgb = np.dstack([a, a, a]).astype(np.uint8)
    print(f"    {SWEEP_TEX} -> {SIZE}² 可平铺云（亮度重映射 [{LO:.0f},{HI:.0f}] -> [0,255]）")
    return Image.fromarray(rgb, "RGB")


def build_flipbook():
    """四态硬切（已弃用，保留以便回退）。"""
    src = Image.open(SMOKE_D).convert("RGBA")
    w, h = src.size
    cw, ch = w // 2, h // 2
    if CELL < 0:
        cell, grid = src, 2
        print(f"    smoke_d {src.size} -> 保留整张 {grid}×{grid} 图集（4 态）")
    else:
        cx, cy = (CELL % 2) * cw, (CELL // 2) * ch
        cell, grid = src.crop((cx, cy, cx + cw, cy + ch)), 1
        print(f"    smoke_d {src.size} 取第 {CELL} 格（静态云）")
    cell = cell.resize((SIZE, SIZE), Image.LANCZOS)
    im = Image.new("RGB", (SIZE, SIZE), (0, 0, 0))
    im.paste(Image.new("RGB", (SIZE, SIZE), (255, 255, 255)), (0, 0), cell.getchannel("A"))
    step = SIZE // grid
    for gy in range(grid):
        for gx in range(grid):
            radial_fade(im, (gx * step, gy * step, (gx + 1) * step, (gy + 1) * step))
    return im.filter(ImageFilter.GaussianBlur(1.2))


def build_cloud4():
    """4 格圆云图集：每格 = 云噪声（各取不同位移/朝向）**乘**一张圆形「内聚外散」遮罩。

    🔴 为什么遮罩能待在正中心：翻页只做 `uv *= 1/2` + `uv += 格偏移`（缩放+偏移，**不平移**），
       所以每格的内容被完整铺到整个四边形上 —— 遮罩画在格中央 = 铺在四边形中央 ✓
    🔴 遮罩必须画到**内切圆**（r=1.0 处才是外接圆；方角在 r=√2）：
       取 C4_R_OUT≈0.96 ⇒ 到内切圆就衰减完了，**四个方角彻底不发光** ⇒ 看不到方块边。"""
    p = os.path.join(UE_TEX_DIR, C4_TEX)
    if not os.path.isfile(p):
        print(f"[FATAL] 找不到 {p}")
        raise SystemExit(2)
    base = Image.open(p).convert("L").resize((SIZE // 2, SIZE // 2), Image.LANCZOS)
    b = np.asarray(base, dtype=np.float32)
    # 云噪声重映射到 [0,1]：暗部压到 0（加法下不发光），亮部拉满
    b = np.clip((b - 40.0) / 165.0, 0.0, 1.0)

    atlas = Image.new("RGB", (SIZE, SIZE), (0, 0, 0))
    half = SIZE // 2
    for cell in range(4):
        # 每格取云的不同位置+镜像 → 4 个形状不同但风格一致的云团（避免四格看起来一样）
        ox, oy = (cell % 2) * 61, (cell // 2) * 53
        c = np.roll(np.roll(b, oy, axis=0), ox, axis=1)
        if cell % 2:
            c = c[:, ::-1]
        if cell >= 2:
            c = c[::-1, :]
        yy, xx = np.mgrid[0:half, 0:half].astype(np.float32)
        # 归一化到**内切圆**：中心 0，内切圆边 = 1，方角 = √2
        nx = (xx - (half - 1) / 2.0) / ((half - 1) / 2.0)
        ny = (yy - (half - 1) / 2.0) / ((half - 1) / 2.0)
        r = np.sqrt(nx * nx + ny * ny)
        mask = np.clip((C4_R_OUT - r) / max(1e-6, C4_R_OUT - C4_R_IN), 0.0, 1.0) ** C4_FALL
        v = np.clip(c * mask * C4_GAIN, 0.0, 1.0) * 255.0
        cell_im = Image.fromarray(np.dstack([v, v, v]).astype(np.uint8), "RGB")
        atlas.paste(cell_im, ((cell % 2) * half, (cell // 2) * half))
    print(f"    {C4_TEX} -> 2×2 圆云图集（内聚 r<{C4_R_IN} 满亮 / 外散到 r={C4_R_OUT} 归零，方角不发光）")
    return atlas


def main():
    print(f"=== 法阵换云贴图（模式 {MODE}）===")
    if MODE == "cloud4":
        im = build_cloud4()
        print("    ⚠️ 材质侧：勾 use_animated_texture_coords（取消 use_texture_sweep / self_illumination）")
        print("       Vector Argument 1 = (2, 2, 速度, 4) —— 列,行,速度(帧/秒),总帧")
    elif MODE == "sweep":
        im = build_sweep()
        print("    ⚠️ 材质侧：勾 use_texture_sweep（不要勾 use_animated_texture_coords）、取消 self_illumination；")
        print("       Vector Argument 1 = (u漂移, v漂移, 0, 0)")
    else:
        im = build_flipbook()

    os.makedirs(OUTDIR, exist_ok=True)
    raw = os.path.join(OUTDIR, "_raw_" + NAME)
    im.save(raw)
    staged = os.path.join(OUTDIR, NAME)
    subprocess.run([sys.executable, PNG_FOR_EDITOR, raw, staged], check=True)
    os.remove(raw)
    print(f"    产出 {staged}")

    # 🔴 覆盖前先备份原图（那是法阵美术，不是随便能丢的）。
    #    ⚠️ **只在备份不存在时备份一次** —— 第一版每次运行都覆盖备份，
    #       于是第 2 次运行时把"上一次生成的云"当成了"原图"备进去，
    #       **真正的原图备份被自己冲掉了**（2026-09-22 实际发生，靠 `tpaccli dump`
    #       从已发布的 `AssetPackages/pack0.tpac` 里才捞回来 —— 编辑器工程的 `_tex.tpac`
    #       只有导入设置、没有像素，指望不上）。
    if os.path.isfile(TARGET):
        bakdir = os.path.join(OUTDIR, "_bak")
        os.makedirs(bakdir, exist_ok=True)
        bak = os.path.join(bakdir, "lwn_flight_sigil_d.法阵原图.png")
        if os.path.isfile(bak):
            print(f"    [备份已存在，不覆盖] {bak}")
        else:
            shutil.copy2(TARGET, bak)
            print(f"    已备份原图 -> {bak}")
        shutil.copy2(staged, TARGET)
        print(f"    已替换 {TARGET}")
    else:
        print(f"    [跳过替换] 目标不存在：{TARGET}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
