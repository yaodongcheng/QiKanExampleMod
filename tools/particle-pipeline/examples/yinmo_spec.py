# -*- coding: utf-8 -*-
"""阴魔斩 —— 三段式技能特效 spec

跑法：
    python tools/particle-pipeline/gen_particle_effect.py examples/yinmo_spec.py -o out/yinmo_slash.xml

【为什么是三个 effect 而不是一个】
引擎里一个 effect 只有一个发射原点（挂在骨骼或飞行物上）。而这三段的原点不同：
    蓄力 → 手（骨骼挂点）      斩击爆开 → 施法者中心      月牙飞行/残迹 → 跟着飞行物走
所以真实工程里就是三个 effect，由代码在时间线上依次 Spawn。
（引擎虽有 activation_delay，但那只能延迟同一个原点的 emitter，跨原点做不到。）

================================================================================
🔴 2026-09-21 依据《阴魔斩.mp4》逐秒标定（45.1s / 30fps / 1280×700 / 10 次施法 / 多机位）
================================================================================
标定方法（可复现）：
  ① 尺子 = 角色本身。UE5 小白人 ≈ 1.8 m。在 12.00s 帧量得「头顶→脚底 = 405 px」
     → **225 px/m（1 px = 4.44 mm）**。下面所有尺寸都是在这个尺子下读的，不是拍脑袋。
  ② 取色 = 逐帧做高亮红 / 品红掩膜，取最大连通块的均值与 p95（见下表）。

实测特征表：
  ┌ 蓄力球（12.00s） 白热亮盘 60–70 px = **0.27–0.31 m**；白热内核 ~30 px = 0.13 m
  │                  暗紫晕圈 ~117 px = **0.52 m**；球心悬在指尖上方 83 px = **0.37 m**
  │                  满蓄→释放 ≈ **1.5 s**（0.00s 已满蓄、1.25s 释放；11.0→12.25s 复现同样节奏）
  ├ 月牙斩击波（7.00s 侧后机位 / 43.25s 正面机位）
  │                  弧带厚 130–190 px = **0.6–0.85 m**；弧半径 ≈ 1.6–2.2 m；
  │                  整体跨度 3–4 m（≈两人高）；弧尖亮珠 ⌀ ≈ 0.20–0.25 m
  │                  释放后 ~0.2 s 出现，0.6–0.9 s 内飞离视野并缩小到约 1/3
  ├ 品红飘带（8.00s / 28.0–29.1s） 长 2–4 m、宽 0.03–0.06 m 的细长弧线，
  │                  2–3 根成组、互相交织成"环 / 8 字"，存活 **1.5–2.6 s**——比月牙活得久
  └ 颜色（RGB，掩膜均值 / p95）
        白热核    (246,150,135) / (255,194,185)      → 白热偏粉
        绯红亮盘  (241,142,143) / (255,164,167)      → 亮绯红（月牙亮边同色）
        品红飘带  (202,146,185) / (255,181,216)      → 品红近白
        黑烟      暗紫红 (≈ 0.20,0.09,0.13) → 近黑   → **是"发光红 + 乘法压暗烟"的组合**

🔴 结论：阴魔斩 = **「白热红球（发光叠加）＋ 月牙弧（发光）＋ 黑烟（乘法压暗）＋ 品红飘带（发光细带）」**
   四件套。红是加法发光、暗是乘法压暗——**两种混合模式必须同时存在**：只做加法没有"阴"的黑，
   只做乘法没有"斩"的亮。这条是整个效果的成败线。

【🔴 尺度纪律 —— 2026-09-18 实机截图踩坑后立】
场景参照物 = **人形 1.8 米**。所有 particle_size 都按「在人身上看起来多大」定：
    蓄力亮盘 0.30 m（≈ 1.4 个头的直径）  暗晕 0.52 m  烟团 0.6–1.5 m  月牙弧带 0.6–0.85 m
**踩过的坑**：初版 smoke_puffs 尺寸算到 3.17 米（比人还高），叠加乘法混合后
糊成一坨死黑，整个画面只剩一团黑烟。改尺寸前先问「放在 1.8 米的人旁边是多大」。

【🔴 混合模式 —— 决定成败的第二条】
material 不只是贴图，**它同时决定混合模式**（2026-09-18 全量实测 41 个 prt_shd_* 材质）：
    add_modulate_combined × 28  →  prt_shd_smoke_1 / prt_shd_glow / prt_shd_trail / prt_shd_sparks（发光）
    modulate              ×  9  →  prt_shd_haze_1（乘法压暗 = 能出黑烟！）
    add_alpha             ×  4  →  prt_shd_steam_1（半透明）
⚠️ 压暗类是**乘法叠加**：多颗粒子重叠会连乘 → 迅速饱和成纯黑。
   所以压暗类的 alpha 峰值要压在 0.4 左右（发光类可以到 0.9）。这是踩出来的。
⚠️ 相机背景影响观感：压暗类元素在**明亮背景**（白天战场）下才看得见。
"""

# 场景参照：人形高 1.8m（预览器里的人形剪影）
EFFECTS = [

    # =====================================================================
    # 阶段 1 · 蓄力（1.50s）—— 掌上一颗白热红球，外裹暗紫晕、射出暗射线
    #   实测：亮盘 0.30m / 暗晕 0.52m / 球心离指尖 0.37m / 满蓄 1.5s
    # =====================================================================
    dict(
        name="lwn_yinmo_charge",
        guid="{A1B2C3D4-0001-4E5F-9A0B-1C2D3E4F5061}",
        emitters=[

            # 白热内核：实测亮盘 0.27–0.31m。粒子 0.18m × 大量重叠 → 视觉球 ≈ 0.30m
            dict(name="core_whitehot",
                 material="prt_shd_glow",
                 emission_rate=(420, 0), particle_life=(0.30, 0.08),
                 emit_sphere_radius=0.05, emit_volume_type="sphere",
                 damping=3.0, gravity="0.000, 0.000, 0.000",
                 velocity=(0.30, 0.30),
                 particle_size=(0.150, 0.045), size_curve=(1.0, 0.45, 0.160),
                 emissive_multiplier=5.0, diffuse_multiplier=1.0,
                 max_alive_particle_count=400,
                 color=[(0.0, "1.000, 0.700, 0.620"), (1.0, "1.000, 0.340, 0.220")],
                 alpha=[(0.0, 0.0), (0.10, 0.92), (0.60, 0.45), (1.0, 0.0)]),

            # 绯红亮盘：包着白热核的那层饱和红（实测 ≈1.4 倍头大 ≈ 0.45m 盘面）
            dict(name="crimson_disk",
                 material="prt_shd_glow",
                 emission_rate=(260, 0), particle_life=(0.42, 0.12),
                 emit_sphere_radius=0.09, emit_volume_type="sphere",
                 damping=2.0, gravity="0.000, 0.000, 0.000",
                 velocity=(0.45, 0.35),
                 particle_size=(0.280, 0.070), size_curve=(0.50, 1.40, 0.160),
                 emissive_multiplier=1.8,
                 max_alive_particle_count=400,
                 color=[(0.0, "0.980, 0.550, 0.520"), (1.0, "0.620, 0.100, 0.120")],
                 alpha=[(0.0, 0.0), (0.15, 0.50), (0.60, 0.24), (1.0, 0.0)]),

            # 🔴 暗紫晕 —— modulate 材质，压暗背景（实测 ⌀0.52m，= 亮盘的 1.7 倍）
            #    alpha 峰值压在 0.34：乘法叠加会连乘，超过就糊成纯黑
            dict(name="dark_halo",
                 material="prt_shd_haze_1",
                 emission_rate=(72, 0), particle_life=(1.30, 0.45),
                 emit_sphere_radius=0.20, emit_volume_type="sphere",
                 damping=0.5, gravity="0.000, 0.000, 0.000",
                 velocity=(0.35, 0.30),
                 particle_size=(0.300, 0.080), size_curve=(0.50, 1.75, 0.200),
                 diffuse_multiplier=0.45, emissive_multiplier=0.0,
                 max_alive_particle_count=400,
                 color=[(0.0, "0.340, 0.100, 0.150"), (1.0, "0.130, 0.050, 0.090")],
                 alpha=[(0.0, 0.0), (0.20, 0.34), (0.65, 0.16), (1.0, 0.0)]),

            # 暗射线 —— 光球四周辐射出去的细暗纹（视频里那一圈"逆光刺"）
            #   turn_to_velocity_side 让面片顺着速度方向 = 细长线
            dict(name="shadow_rays",
                 material="prt_shd_haze_1",
                 billboard_type="turn_to_velocity_side",
                 emission_rate=(60, 0), particle_life=(0.45, 0.20),
                 emit_sphere_radius=0.10, emit_volume_type="sphere",
                 damping=0.20, gravity="0.000, 0.000, 0.000",
                 velocity=(1.60, 0.50),
                 particle_size=(0.035, 0.012), size_curve=(1.0, 0.30, 0.020),
                 diffuse_multiplier=0.35, emissive_multiplier=0.0,
                 max_alive_particle_count=400,
                 color=[(0.0, "0.300, 0.090, 0.140"), (1.0, "0.100, 0.040, 0.070")],
                 alpha=[(0.0, 0.0), (0.12, 0.30), (0.70, 0.12), (1.0, 0.0)]),
        ]),

    # =====================================================================
    # 阶段 2 · 斩击爆开 —— 圆盘状球壳扩张（实测弧半径 1.6–2.2m）+ 黑烟 + 碎屑
    # =====================================================================
    dict(
        name="lwn_yinmo_burst",
        guid="{A1B2C3D4-0002-4E5F-9A0B-1C2D3E4F5062}",
        emitters=[

            # 球壳本体：小球体积 + 高速外扩 + 阻尼 → 粒子停在某个半径上形成壳
            #   壳半径 ≈ 初速 / 阻尼 = 3.0 / 1.5 = **2.0m**（落在实测 1.6–2.2m 区间内）
            dict(name="wave_shell",
                 material="prt_shd_glow",
                 emission_rate=(1100, 0), particle_life=(0.60, 0.18),
                 emit_sphere_radius=0.22, emit_volume_type="sphere",
                 damping=1.5, gravity="0.000, 0.000, 0.000",
                 velocity=(3.00, 0.90),
                 particle_size=(0.340, 0.100), size_curve=(0.35, 2.20, 0.550),
                 emissive_multiplier=1.4,
                 max_alive_particle_count=1400,
                 flags=dict(order_by_distance=True),
                 color=[(0.0, "0.980, 0.600, 0.560"),
                        (0.55, "0.900, 0.200, 0.260"),
                        (1.0, "0.480, 0.070, 0.130")],
                 alpha=[(0.0, 0.0), (0.08, 0.55), (0.50, 0.24), (1.0, 0.0)]),

            # 亮边 / 经线亮纹 —— 造成"弧带 0.6–0.85m 厚"的读数主要靠它
            dict(name="wave_edge",
                 material="prt_shd_glow",
                 emission_rate=(520, 0), particle_life=(0.55, 0.15),
                 emit_sphere_radius=0.18, emit_volume_type="sphere",
                 damping=1.3, gravity="0.000, 0.000, 0.000",
                 velocity=(2.90, 0.80),
                 particle_size=(0.075, 0.030), size_curve=(1.0, 0.40, 0.025),
                 emissive_multiplier=4.0,
                 max_alive_particle_count=800,
                 color=[(0.0, "1.000, 0.940, 0.920"), (1.0, "1.000, 0.380, 0.420")],
                 alpha=[(0.0, 0.0), (0.06, 0.95), (0.60, 0.35), (1.0, 0.0)]),

            # 🔴 黑烟爆发 —— modulate；尺寸上限 ≈ 0.55×2.8 ≈ 1.5m（比人矮，别再放大）
            #    alpha 峰值 0.45（乘法连乘会饱和）
            dict(name="dark_smoke_burst",
                 material="prt_shd_haze_1",
                 emission_rate=(110, 0), particle_life=(1.90, 0.65),
                 emit_sphere_radius=0.55, emit_volume_type="sphere",
                 damping=0.8, gravity="0.000, 0.000, 0.000",
                 velocity=(1.60, 1.00),
                 particle_size=(0.550, 0.180), size_curve=(0.50, 2.80, 0.450),
                 diffuse_multiplier=0.35, emissive_multiplier=0.0,
                 max_alive_particle_count=500,
                 flags=dict(order_by_distance=True),
                 color=[(0.0, "0.220, 0.090, 0.130"), (1.0, "0.070, 0.030, 0.050")],
                 alpha=[(0.0, 0.0), (0.10, 0.45), (0.55, 0.22), (1.0, 0.0)]),

            # 碎屑 —— 厘米级、暗、受重力（视频里爆开后往下掉的暗点）
            dict(name="debris",
                 material="prt_shd_haze_1",
                 emission_rate=(110, 0), particle_life=(1.50, 0.50),
                 emit_sphere_radius=0.30, emit_volume_type="sphere",
                 damping=0.15, gravity="0.000, 0.000, -2.600",
                 velocity=(3.00, 1.60),
                 particle_size=(0.045, 0.025), size_curve=(1.0, 1.0, 0.0),
                 diffuse_multiplier=0.55, emissive_multiplier=0.0,
                 max_alive_particle_count=400,
                 color=[(0.0, "0.180, 0.100, 0.130"), (1.0, "0.080, 0.050, 0.070")],
                 alpha=[(0.0, 0.0), (0.05, 0.75), (0.85, 0.70), (1.0, 0.0)]),
        ]),

    # =====================================================================
    # 阶段 3 · 月牙飞行 + 残迹 —— 原点跟着飞行物走
    #   实测：弧带 0.6–0.85m 厚、亮珠 0.20–0.25m、飘带 2–4m 长且活得最久（1.5–2.6s）
    # =====================================================================
    dict(
        name="lwn_yinmo_trail",
        guid="{A1B2C3D4-0003-4E5F-9A0B-1C2D3E4F5063}",
        emitters=[

            # 🔴 月牙亮边 —— 决定"这是月牙"的主元素：丝带朝向速度方向，0.20m 宽的一层刃光
            #    骑砍没有 mesh / ribbon emitter，月牙只能这样"用速度铺出来的带"近似
            dict(name="crescent_edge",
                 material="prt_shd_glow",
                 billboard_type="turn_to_velocity_side",
                 emission_rate=(420, 0), particle_life=(0.85, 0.25),
                 emit_sphere_radius=0.16, emit_volume_type="sphere",
                 damping=0.30, gravity="0.000, 0.000, 0.000",
                 velocity=(0.35, 0.35), inherit_emitter_velocity=0.94,
                 particle_size=(0.320, 0.090), size_curve=(1.0, 0.16, 0.040),
                 emissive_multiplier=3.0,
                 max_alive_particle_count=700,
                 color=[(0.0, "1.000, 0.720, 0.680"),
                        (0.60, "0.950, 0.180, 0.280"),
                        (1.0, "0.700, 0.090, 0.200")],
                 alpha=[(0.0, 0.0), (0.10, 0.90), (0.60, 0.40), (1.0, 0.0)]),

            # 弧尖亮珠 —— 弧上那颗白热核（实测 ⌀0.20–0.25m，是整条弧最亮的一点）
            dict(name="core_bead",
                 material="prt_shd_glow",
                 emission_rate=(170, 0), particle_life=(0.22, 0.07),
                 emit_sphere_radius=0.055, emit_volume_type="sphere",
                 damping=2.4, gravity="0.000, 0.000, 0.000",
                 velocity=(0.12, 0.12), inherit_emitter_velocity=1.00,
                 particle_size=(0.200, 0.050), size_curve=(1.0, 0.50, 0.030),
                 emissive_multiplier=5.0,
                 max_alive_particle_count=300,
                 color=[(0.0, "1.000, 0.920, 0.800"), (1.0, "1.000, 0.400, 0.300")],
                 alpha=[(0.0, 0.0), (0.12, 1.0), (0.70, 0.40), (1.0, 0.0)]),

            # 余烬光点 —— 厘米级、飘散、微下坠、长命
            dict(name="embers",
                 material="prt_shd_glow",
                 emission_rate=(34, 0), particle_life=(2.40, 0.90),
                 emit_sphere_radius=0.35, emit_volume_type="sphere",
                 damping=0.16, gravity="0.000, 0.000, 0.000",
                 velocity=(0.75, 0.75), inherit_emitter_velocity=0.80,
                 particle_size=(0.050, 0.028), size_curve=(1.0, 0.10, 0.012),
                 emissive_multiplier=3.4,
                 max_alive_particle_count=500,
                 color=[(0.0, "1.000, 0.620, 0.780"), (1.0, "0.480, 0.120, 0.320")],
                 alpha=[(0.0, 0.0), (0.25, 1.0), (0.70, 0.50), (1.0, 0.0)]),

            # 🔴 黑烟团 —— modulate；每团 0.6–1.5m，alpha 峰值 0.38
            dict(name="smoke_puffs",
                 material="prt_shd_haze_1",
                 emission_rate=(70, 0), particle_life=(2.30, 0.85),
                 emit_sphere_radius=0.45, emit_volume_type="sphere",
                 damping=0.20, gravity="0.000, 0.000, 0.000",
                 velocity=(0.60, 0.55), inherit_emitter_velocity=0.86,
                 particle_size=(0.300, 0.100), size_curve=(0.50, 2.40, 0.220),
                 diffuse_multiplier=0.35, emissive_multiplier=0.0,
                 max_alive_particle_count=400,
                 flags=dict(order_by_distance=True),
                 color=[(0.0, "0.200, 0.080, 0.120"), (1.0, "0.060, 0.030, 0.050")],
                 alpha=[(0.0, 0.0), (0.12, 0.38), (0.55, 0.20), (1.0, 0.0)]),

            # 🔴 品红飘带 —— 视频里活得最久的元素（1.5–2.6s、2–4m 长的细弧线，2–3 根成组）
            #    material 换 prt_shd_trail（T_Trail / T_Ribbon 贴图）才是"飘带"而非"烟丝"
            dict(name="magenta_ribbons",
                 material="prt_shd_trail",
                 billboard_type="turn_to_velocity_side",
                 emission_rate=(46, 0), particle_life=(2.60, 1.00),
                 emit_sphere_radius=0.22, emit_volume_type="sphere",
                 damping=0.10, gravity="0.000, 0.000, 0.000",
                 velocity=(2.20, 0.60), inherit_emitter_velocity=0.90,
                 particle_size=(0.055, 0.020), size_curve=(1.0, 0.35, 0.010),
                 emissive_multiplier=1.6,
                 max_alive_particle_count=400,
                 color=[(0.0, "0.950, 0.660, 0.860"), (1.0, "0.600, 0.220, 0.500")],
                 alpha=[(0.0, 0.0), (0.15, 0.50), (0.70, 0.24), (1.0, 0.0)]),
        ]),
]
