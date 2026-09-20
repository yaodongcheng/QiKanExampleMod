# -*- coding: utf-8 -*-
"""阴魔斩 —— 三段式技能特效 spec（对标三张参考图：蓄力 / 发射 / 运动）

跑法：
    python Scripts/gen_particle_effect.py Debug/offline/particle_demo/yinmo_spec.py \
        -o Debug/offline/particle_demo/yinmo_slash.xml

【为什么是三个 effect 而不是一个】
引擎里一个 effect 只有一个发射原点（挂在骨骼或飞行物上）。而这三段的原点不同：
    蓄力 → 手（骨骼挂点）      爆开 → 施法者中心      残迹 → 跟着飞行物走
所以真实工程里就是三个 effect，由代码在时间线上依次 Spawn。
（引擎虽有 activation_delay，但那只能延迟同一个原点的 emitter，跨原点做不到。）

【🔴 尺度纪律 —— 2026-09-18 实机截图踩坑后立】
场景参照物 = **人形 1.8 米**。所有 particle_size 都按「在人身上看起来多大」定：
    能量核心 ≈ 头大 (0.35m)   暗晕 ≈ 两人宽 (1.5m)   尾烟团 ≈ 半人高 (0.7m)
    爆开球壳半径 ≈ 两人高 (3.7m)  余烬/碎片 = 厘米级
**踩过的坑**：初版 smoke_puffs 尺寸算到 3.17 米（比人还高），叠加乘法混合后
糊成一坨死黑，整个画面只剩一团黑烟。改尺寸前先问「放在 1.8 米的人旁边是多大」。

【🔴 混合模式 —— 决定成败的第二条】
material 不只是贴图，**它同时决定混合模式**（2026-09-18 全量实测 41 个 prt_shd_* 材质）：
    add_modulate_combined × 28  →  prt_shd_smoke_1（发光）
    modulate              ×  9  →  prt_shd_haze_1 （乘法压暗 = 能出黑烟！）
    add_alpha             ×  4  →  prt_shd_steam_1（半透明）
⚠️ 压暗类是**乘法叠加**：多颗粒子重叠会连乘 → 迅速饱和成纯黑。
   所以压暗类的 alpha 峰值要压在 0.4 左右（发光类可以到 0.9）。这是踩出来的。
⚠️ 相机背景影响观感：压暗类元素在**明亮背景**（白天战场）下才看得见。
"""

# 场景参照：人形高 1.8m（预览器里的人形剪影）
EFFECTS = [

    # =====================================================================
    # 阶段 1 · 蓄力 —— 手上一颗能量球，外围暗晕、身后暗丝
    # =====================================================================
    dict(
        name="lwn_yinmo_charge",
        guid="{A1B2C3D4-0001-4E5F-9A0B-1C2D3E4F5061}",
        emitters=[

            # 能量核心：头那么大的一颗亮球（0.12 的粒子 × 300/s × 0.25s ≈ 0.35m 球）
            dict(name="core",
                 material="prt_shd_smoke_1",
                 emission_rate=(420, 0), particle_life=(0.28, 0.08),
                 emit_sphere_radius=0.10, emit_volume_type="sphere",
                 damping=3.0, gravity="0.000, 0.000, 0.000",
                 velocity=(0.35, 0.35),
                 particle_size=(0.210, 0.070), size_curve=(1.0, 0.35, 0.100),
                 emissive_multiplier=4.6, diffuse_multiplier=1.0,
                 max_alive_particle_count=500,
                 color=[(0.0, "1.000, 0.960, 0.760"), (1.0, "1.000, 0.520, 0.150")],
                 alpha=[(0.0, 0.0), (0.10, 1.0), (0.65, 0.55), (1.0, 0.0)]),

            # 橙红外壳：包着核心，最大到 ~0.8m
            dict(name="shell_orange",
                 material="prt_shd_smoke_1",
                 emission_rate=(200, 0), particle_life=(0.45, 0.15),
                 emit_sphere_radius=0.22, emit_volume_type="sphere",
                 damping=2.0, gravity="0.000, 0.000, 0.000",
                 velocity=(0.55, 0.45),
                 particle_size=(0.180, 0.060), size_curve=(0.55, 1.60, 0.180),
                 emissive_multiplier=1.6,
                 max_alive_particle_count=500,
                 color=[(0.0, "1.000, 0.420, 0.110"), (1.0, "0.780, 0.130, 0.040")],
                 alpha=[(0.0, 0.0), (0.15, 0.42), (0.60, 0.20), (1.0, 0.0)]),

            # 🔴 暗晕 —— modulate 材质，压暗背景（参考图里球外那圈暗色），直径约 1.5m
            #    alpha 峰值压在 0.42：乘法叠加会连乘，超过就糊成纯黑
            dict(name="dark_halo",
                 material="prt_shd_haze_1",
                 emission_rate=(72, 0), particle_life=(1.30, 0.45),
                 emit_sphere_radius=0.40, emit_volume_type="sphere",
                 damping=0.5, gravity="0.000, 0.000, 0.000",
                 velocity=(0.45, 0.40),
                 particle_size=(0.165, 0.060), size_curve=(0.50, 2.10, 0.165),
                 diffuse_multiplier=0.45, emissive_multiplier=0.0,
                 max_alive_particle_count=400,
                 color=[(0.0, "0.420, 0.130, 0.170"), (1.0, "0.180, 0.070, 0.120")],
                 alpha=[(0.0, 0.0), (0.20, 0.26), (0.65, 0.14), (1.0, 0.0)]),

            # 暗色丝线 —— turn_to_velocity_side 让面片顺着速度方向 = 丝带状
            dict(name="tendrils",
                 material="prt_shd_haze_1",
                 billboard_type="turn_to_velocity_side",
                 emission_rate=(50, 0), particle_life=(1.60, 0.55),
                 emit_sphere_radius=0.30, emit_volume_type="sphere",
                 damping=0.35, gravity="0.000, 0.000, 0.000",
                 velocity=(1.10, 0.70),
                 particle_size=(0.050, 0.020), size_curve=(1.0, 0.20, 0.015),
                 diffuse_multiplier=0.50, emissive_multiplier=0.0,
                 max_alive_particle_count=400,
                 color=[(0.0, "0.300, 0.100, 0.180"), (1.0, "0.120, 0.050, 0.100")],
                 alpha=[(0.0, 0.0), (0.12, 0.32), (0.70, 0.14), (1.0, 0.0)]),
        ]),

    # =====================================================================
    # 阶段 2 · 爆开 —— 巨型球壳扩张（半径 ~3.7m）+ 黑烟爆发 + 碎片
    # =====================================================================
    dict(
        name="lwn_yinmo_burst",
        guid="{A1B2C3D4-0002-4E5F-9A0B-1C2D3E4F5062}",
        emitters=[

            # 球壳本体：小球体积 + 高速外扩 + 阻尼 → 粒子停在某个半径上形成壳
            #   壳半径 ≈ 初速 / 阻尼 = 5.5 / 1.5 ≈ 3.7m（约两人高）
            dict(name="shell_expand",
                 material="prt_shd_smoke_1",
                 emission_rate=(1100, 0), particle_life=(0.60, 0.18),
                 emit_sphere_radius=0.22, emit_volume_type="sphere",
                 damping=1.5, gravity="0.000, 0.000, 0.000",
                 velocity=(5.50, 1.60),
                 particle_size=(0.300, 0.100), size_curve=(0.35, 2.40, 0.550),
                 emissive_multiplier=1.3,
                 max_alive_particle_count=1400,
                 flags=dict(order_by_distance=True),
                 color=[(0.0, "1.000, 0.620, 0.580"),
                        (0.55, "0.900, 0.220, 0.280"),
                        (1.0, "0.520, 0.080, 0.140")],
                 alpha=[(0.0, 0.0), (0.08, 0.55), (0.50, 0.24), (1.0, 0.0)]),

            # 经线亮纹（近似）—— 球面上的纵向亮线，引擎里其实是 shader，这里用细亮粒子凑
            dict(name="shell_filaments",
                 material="prt_shd_smoke_1",
                 emission_rate=(520, 0), particle_life=(0.55, 0.15),
                 emit_sphere_radius=0.18, emit_volume_type="sphere",
                 damping=1.3, gravity="0.000, 0.000, 0.000",
                 velocity=(5.20, 1.30),
                 particle_size=(0.070, 0.030), size_curve=(1.0, 0.40, 0.025),
                 emissive_multiplier=3.4,
                 max_alive_particle_count=800,
                 color=[(0.0, "1.000, 0.930, 0.900"), (1.0, "1.000, 0.380, 0.420")],
                 alpha=[(0.0, 0.0), (0.06, 0.95), (0.60, 0.35), (1.0, 0.0)]),

            # 🔴 黑烟爆发 —— modulate；alpha 峰值 0.45（乘法连乘会饱和）
            dict(name="black_smoke",
                 material="prt_shd_haze_1",
                 emission_rate=(110, 0), particle_life=(1.90, 0.65),
                 emit_sphere_radius=0.55, emit_volume_type="sphere",
                 damping=0.8, gravity="0.000, 0.000, 0.000",
                 velocity=(1.60, 1.00),
                 particle_size=(0.300, 0.120), size_curve=(0.50, 2.80, 0.450),
                 diffuse_multiplier=0.35, emissive_multiplier=0.0,
                 max_alive_particle_count=500,
                 flags=dict(order_by_distance=True),
                 color=[(0.0, "0.260, 0.110, 0.150"), (1.0, "0.090, 0.040, 0.070")],
                 alpha=[(0.0, 0.0), (0.10, 0.45), (0.55, 0.22), (1.0, 0.0)]),

            # 碎片 —— 厘米级、暗、受重力
            dict(name="debris",
                 material="prt_shd_haze_1",
                 emission_rate=(110, 0), particle_life=(1.50, 0.50),
                 emit_sphere_radius=0.30, emit_volume_type="sphere",
                 damping=0.15, gravity="0.000, 0.000, -2.600",
                 velocity=(3.00, 1.60),
                 particle_size=(0.045, 0.025), size_curve=(1.0, 1.0, 0.0),
                 diffuse_multiplier=0.55, emissive_multiplier=0.0,
                 max_alive_particle_count=400,
                 color=[(0.0, "0.160, 0.100, 0.130"), (1.0, "0.080, 0.050, 0.070")],
                 alpha=[(0.0, 0.0), (0.05, 0.75), (0.85, 0.70), (1.0, 0.0)]),
        ]),

    # =====================================================================
    # 阶段 3 · 残迹 —— 月牙弧 + 余烬 + 黑烟团 + 淡紫细丝
    # =====================================================================
    dict(
        name="lwn_yinmo_trail",
        guid="{A1B2C3D4-0003-4E5F-9A0B-1C2D3E4F5063}",
        emitters=[

            # 月牙弧 —— 丝带朝向速度方向，厚约 10cm 的一层刃光
            dict(name="crescent",
                 material="prt_shd_smoke_1",
                 billboard_type="turn_to_velocity_side",
                 emission_rate=(300, 0), particle_life=(0.85, 0.25),
                 emit_sphere_radius=0.16, emit_volume_type="sphere",
                 damping=0.30, gravity="0.000, 0.000, 0.000",
                 velocity=(0.35, 0.35), inherit_emitter_velocity=0.94,
                 particle_size=(0.140, 0.050), size_curve=(1.0, 0.15, 0.035),
                 emissive_multiplier=2.6,
                 max_alive_particle_count=700,
                 color=[(0.0, "1.000, 0.400, 0.400"),
                        (0.60, "0.950, 0.180, 0.300"),
                        (1.0, "0.700, 0.090, 0.200")],
                 alpha=[(0.0, 0.0), (0.10, 0.88), (0.60, 0.40), (1.0, 0.0)]),

            # 弧端亮点 —— 弧尖那颗亮珠（约拳头大）
            dict(name="arc_tip",
                 material="prt_shd_smoke_1",
                 emission_rate=(170, 0), particle_life=(0.22, 0.07),
                 emit_sphere_radius=0.07, emit_volume_type="sphere",
                 damping=2.4, gravity="0.000, 0.000, 0.000",
                 velocity=(0.12, 0.12), inherit_emitter_velocity=1.00,
                 particle_size=(0.090, 0.030), size_curve=(1.0, 0.50, 0.030),
                 emissive_multiplier=4.0,
                 max_alive_particle_count=300,
                 color=[(0.0, "1.000, 0.900, 0.760"), (1.0, "1.000, 0.400, 0.300")],
                 alpha=[(0.0, 0.0), (0.12, 1.0), (0.70, 0.40), (1.0, 0.0)]),

            # 余烬光点 —— 厘米级、飘散、微下坠、长命
            dict(name="embers",
                 material="prt_shd_smoke_1",
                 emission_rate=(34, 0), particle_life=(2.40, 0.90),
                 emit_sphere_radius=0.35, emit_volume_type="sphere",
                 damping=0.16, gravity="0.000, 0.000, 0.000",
                 velocity=(0.75, 0.75), inherit_emitter_velocity=0.80,
                 particle_size=(0.045, 0.025), size_curve=(1.0, 0.10, 0.012),
                 emissive_multiplier=3.6,
                 max_alive_particle_count=500,
                 color=[(0.0, "1.000, 0.480, 0.640"), (1.0, "0.480, 0.120, 0.320")],
                 alpha=[(0.0, 0.0), (0.25, 1.0), (0.70, 0.50), (1.0, 0.0)]),

            # 🔴 黑烟团 —— modulate；每团约半人高（0.7m），alpha 峰值 0.38
            dict(name="smoke_puffs",
                 material="prt_shd_haze_1",
                 emission_rate=(30, 0), particle_life=(2.30, 0.85),
                 emit_sphere_radius=0.45, emit_volume_type="sphere",
                 damping=0.20, gravity="0.000, 0.000, 0.000",
                 velocity=(0.60, 0.55), inherit_emitter_velocity=0.86,
                 particle_size=(0.160, 0.070), size_curve=(0.50, 2.60, 0.220),
                 diffuse_multiplier=0.35, emissive_multiplier=0.0,
                 max_alive_particle_count=400,
                 flags=dict(order_by_distance=True),
                 color=[(0.0, "0.240, 0.100, 0.140"), (1.0, "0.080, 0.035, 0.065")],
                 alpha=[(0.0, 0.0), (0.12, 0.38), (0.55, 0.20), (1.0, 0.0)]),

            # 淡紫细丝 —— 极细、低透明、快速掠过
            dict(name="thin_filaments",
                 material="prt_shd_smoke_1",
                 billboard_type="turn_to_velocity_side",
                 emission_rate=(40, 0), particle_life=(2.60, 1.00),
                 emit_sphere_radius=0.25, emit_volume_type="sphere",
                 damping=0.10, gravity="0.000, 0.000, 0.000",
                 velocity=(1.60, 0.70), inherit_emitter_velocity=0.90,
                 particle_size=(0.035, 0.015), size_curve=(1.0, 0.30, 0.010),
                 emissive_multiplier=1.4,
                 max_alive_particle_count=400,
                 color=[(0.0, "0.620, 0.460, 0.820"), (1.0, "0.300, 0.200, 0.480")],
                 alpha=[(0.0, 0.0), (0.15, 0.40), (0.70, 0.18), (1.0, 0.0)]),
        ]),
]
