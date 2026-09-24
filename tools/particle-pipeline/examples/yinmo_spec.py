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

================================================================================
🔴 2026-09-24 晚 第 2 轮：照参考帧分镜（output/yinmo_ref/seq/B_11-13s、C_13-15s）返工
================================================================================
诊断（把我们的分镜与参考分镜逐格比，见 out/sheet_ours_*.png）：
    **我们的效果整体"太白、太干净、黑烟规模太小"** —— 三段全被白球主导，
    而参考的身份特征是「**黑烟为主体 + 红发光点缀**」。逐条改：
  ① 白热核缩到"只是个核"（尺寸 0.15→0.085、速率 420→240、emissive 5.0→2.0）
     —— 参考里白核只占亮盘的 1/2 直径，不是整颗球。
  ② 绯红盘加大加亮（速率 260→420、尺寸 0.36→0.40、emissive 1.8→2.8）→ 让"红"成为主体。
  ③ 蓄力段补 **远场暗丝**（新 emitter `dark_far_filaments`）：参考 11.00–11.75s 有一圈
     **细长暗色丝**从球心向外辐射数米 —— 这是原来完全没有的元素。
  ④ 爆开段把"亮壳"压下去（速率 1100→750、emissive 1.4→0.7），把**黑烟抬上来**
     （速率 110→300、速度 1.6→2.2、尺寸上限 ~1.9 m）—— 参考 12.25–12.50s 的画面主体是黑烟。
  ⑤ 残迹段黑烟成规模（smoke_puffs 速率 70→170、尺寸 0.30→0.42、曲线顶到 3.0m），
     月牙亮带加厚（0.32→0.52，落在实测 0.6–0.85m 的下沿）。
"""

# 场景参照：人形高 1.8m（预览器里的人形剪影）
EFFECTS = [

    # =====================================================================
    # 阶段 1 · 蓄力（1.50s）—— 掌上一颗白热红球，四周黑气**向核塌缩**（2026-09-22 重写）
    #   实测：亮盘 0.30m / 暗晕 0.52m / 球心离指尖 0.37m / 满蓄 1.5s
    # =====================================================================
    dict(
        name="lwn_yinmo_charge",
        guid="{A1B2C3D4-0001-4E5F-9A0B-1C2D3E4F5061}",
        emitters=[

            # 白热内核：**只是核**（2026-09-24 缩）：实测白核 ⌀0.13m，占亮盘不到一半。
            #   原来 0.15m×420/秒 把整颗球糊成白色 —— 现在 0.085m × 240/秒。
            dict(name="core_whitehot",
                 material="prt_shd_glow",
                 emission_rate=(240, 0), particle_life=(0.26, 0.07),
                 emit_sphere_radius=0.04, emit_volume_type="sphere",
                 damping=3.0, gravity="0.000, 0.000, 0.000",
                 velocity=(0.30, 0.30),
                 particle_size=(0.085, 0.030), size_curve=(1.0, 0.45, 0.160),
                 emissive_multiplier=2.0, diffuse_multiplier=1.0,
                 max_alive_particle_count=300,
                 color=[(0.0, "1.000, 0.800, 0.700"), (1.0, "1.000, 0.420, 0.280")],
                 alpha=[(0.0, 0.0), (0.10, 0.92), (0.60, 0.45), (1.0, 0.0)]),

            # 绯红亮盘：包着白热核的那层饱和红（实测 ≈1.4 倍头大 ≈ 0.45m 盘面）
            #   2026-09-24：加大加亮 —— 参考里"红"才是主体，白只是芯
            dict(name="crimson_disk",
                 material="prt_shd_glow",
                 emission_rate=(420, 0), particle_life=(0.50, 0.14),
                 emit_sphere_radius=0.10, emit_volume_type="sphere",
                 damping=2.0, gravity="0.000, 0.000, 0.000",
                 velocity=(0.45, 0.35),
                 particle_size=(0.400, 0.090), size_curve=(0.55, 1.50, 0.180),
                 emissive_multiplier=2.8,
                 max_alive_particle_count=500,
                 color=[(0.0, "0.980, 0.550, 0.520"), (1.0, "0.620, 0.100, 0.120")],
                 alpha=[(0.0, 0.0), (0.15, 0.66), (0.60, 0.30), (1.0, 0.0)]),

            # =================================================================
            # 🔴 黑气向核聚拢（2026-09-22 新增）—— 引擎没有「吸引子」，做不出真·径向内飞
            #   实证：Native 全部 particle XML 里 emission_velocity_model **只有一种值**
            #        `random_velocity_components`（492/492）→ 没有 inward / attract 速度模型。
            #   所以「聚拢」用可实现的近似：**壳层塌缩 + 雾团自缩**
            #     ① 不用 emit_at_once（语义不明），改用 emitter_life 0.10s × 高频 emission_rate
            #        ≈ 一整层一次性发完——时长×速率，行为可预测；
            #     ② 每层发在一个固定半径的球壳上（1.30m / 0.55m），用 activation_delay 错开；
            #     ③ particle_size 的 size_curve 让每团从 1.0 缩到 0.2
            #        → 视觉上就是「一圈黑气往核里收」。
            #   ⚠️ 浓度铁律：modulate 是**乘法叠加**，一团叠一团会连乘到纯黑（第一版 90 颗 0.85m 粒子
            #      把白热核整个吃掉）→ 壳上的粒子数 × 粒子尺寸必须小，alpha 峰值压在 0.2 以下。
            #   实测依据：11.10–11.90s 蓄力期，核周围流场的光学流**径向分量为负**
            #        （朝心占比 0.59→0.71，mean_rad −0.02→−0.22 px/帧）；
            #        释放瞬间翻转（12.05s：+2.55 px/帧、朝心占比 0.24）⇒「蓄力吸、释放喷」。
            #   取证图：output/yinmo_ref/flow/flow_11.90s.png（蓝箭头 = 朝心）
            # =================================================================

            # 平滑暗晕：视频里紧贴亮盘的那圈暗色是**连续渐变**的（⌀0.52m ≈ 亮盘 1.7 倍，
            #   已实测）。200 颗小烟团叠出来是"一团团烟"，不是暗晕 ⇒ 改用**单颗大面片**承担
            #   （prt_shd_haze_1 是 modulate，一颗 0.52m 的面片正好是一层平滑暗环）
            #   2026-09-24：加大到 0.62m 并提 alpha —— 参考里这圈暗晕很显眼
            dict(name="dark_halo_flat",
                 material="prt_shd_haze_1",
                 emission_rate=(8, 0), particle_life=(1.20, 0.30),
                 emit_sphere_radius=0.06, emit_volume_type="sphere",
                 damping=0.5, gravity="0.000, 0.000, 0.000",
                 velocity=(0.15, 0.10),
                 particle_size=(0.620, 0.070), size_curve=(1.0, 1.05, 1.0),
                 diffuse_multiplier=0.45, emissive_multiplier=0.0,
                 max_alive_particle_count=10,
                 color=[(0.0, "0.300, 0.090, 0.140"), (1.0, "0.120, 0.040, 0.080")],
                 alpha=[(0.0, 0.0), (0.18, 0.30), (0.70, 0.16), (1.0, 0.0)]),

            # 外层收拢雾：0.45m 壳上一圈细颗粒，各自往小瘪（范围按实测收）
            dict(name="dark_shell_outer",
                 material="prt_shd_haze_1",
                 emitter_life=0.10, activation_delay=0.00,
                 emission_rate=(120, 0), particle_life=(0.90, 0.20),
                 emit_sphere_radius=0.45, emit_volume_type="sphere",
                 damping=0.0, gravity="0.000, 0.000, 0.000",
                 velocity=(0.10, 0.10),
                 particle_size=(0.160, 0.050), size_curve=(1.0, 0.25, 0.18),
                 diffuse_multiplier=0.40, emissive_multiplier=0.0,
                 max_alive_particle_count=400,
                 flags=dict(order_by_distance=True),
                 color=[(0.0, "0.240, 0.090, 0.140"), (1.0, "0.100, 0.040, 0.070")],
                 alpha=[(0.0, 0.0), (0.12, 0.10), (0.65, 0.05), (1.0, 0.0)]),

            # 内层收拢雾：0.22m 壳，延后 0.35s 发 —— 接住外层的「塌缩接力」
            dict(name="dark_shell_inner",
                 material="prt_shd_haze_1",
                 emitter_life=0.10, activation_delay=0.35,
                 emission_rate=(80, 0), particle_life=(0.85, 0.20),
                 emit_sphere_radius=0.22, emit_volume_type="sphere",
                 damping=0.0, gravity="0.000, 0.000, 0.000",
                 velocity=(0.08, 0.08),
                 particle_size=(0.140, 0.040), size_curve=(1.0, 0.22, 0.18),
                 diffuse_multiplier=0.42, emissive_multiplier=0.0,
                 max_alive_particle_count=400,
                 flags=dict(order_by_distance=True),
                 color=[(0.0, "0.260, 0.100, 0.150"), (1.0, "0.110, 0.040, 0.080")],
                 alpha=[(0.0, 0.0), (0.15, 0.12), (0.65, 0.06), (1.0, 0.0)]),

            # 暗气流条：细长、顺着速度铺开
            #   skew_with_respect_to_particle_velocity 是原版**最常用**的 flag（467 次）——拉条就靠它
            #   2026-09-24：加粗加长加亮（原来太细看不见）
            dict(name="dark_inflow_streaks",
                 material="prt_shd_haze_1",
                 billboard_type="turn_to_velocity_side",
                 emission_rate=(90, 0), particle_life=(0.55, 0.20),
                 emit_sphere_radius=0.90, emit_volume_type="sphere",
                 damping=0.25, gravity="0.000, 0.000, 0.000",
                 velocity=(0.55, 0.45),
                 particle_size=(0.075, 0.030), size_curve=(1.0, 0.25, 0.030),
                 skew_with_particle_velocity_coef=16.000, skew_with_particle_velocity_limit=0.500,
                 diffuse_multiplier=0.38, emissive_multiplier=0.0,
                 max_alive_particle_count=600,
                 flags=dict(skew_with_respect_to_particle_velocity=True),
                 color=[(0.0, "0.280, 0.090, 0.140"), (1.0, "0.110, 0.040, 0.070")],
                 alpha=[(0.0, 0.0), (0.14, 0.22), (0.70, 0.08), (1.0, 0.0)]),

            # 🔴 远场暗丝（2026-09-24 新增）—— 参考 11.00–11.75s 球心向外辐射**数米长**的
            #   细暗丝（原来完全没有这个元素）。做法：大发射球壳（1.5m）+ 高速外飞 + 高 skew
            #   拉成细长条；命长（0.9s）、极淡（alpha 0.14）⇒ 只在近黑背景上隐约可见。
            dict(name="dark_far_filaments",
                 material="prt_shd_haze_1",
                 billboard_type="turn_to_velocity_side",
                 emission_rate=(60, 0), particle_life=(0.90, 0.30),
                 emit_sphere_radius=1.50, emit_volume_type="sphere",
                 damping=0.12, gravity="0.000, 0.000, 0.000",
                 velocity=(1.20, 0.40),
                 particle_size=(0.045, 0.020), size_curve=(1.0, 0.60, 0.050),
                 skew_with_particle_velocity_coef=18.000, skew_with_particle_velocity_limit=0.800,
                 diffuse_multiplier=0.40, emissive_multiplier=0.0,
                 max_alive_particle_count=300,
                 flags=dict(order_by_distance=True, skew_with_respect_to_particle_velocity=True),
                 color=[(0.0, "0.260, 0.090, 0.140"), (1.0, "0.100, 0.040, 0.070")],
                 alpha=[(0.0, 0.0), (0.12, 0.14), (0.75, 0.05), (1.0, 0.0)]),
        ]),

    # =====================================================================
    # 阶段 2 · 斩击爆开 —— 圆盘状球壳扩张（实测弧半径 1.6–2.2m）+ 黑烟 + 碎屑
    #   2026-09-24：参考 12.25–12.50s 的画面主体是**黑烟**，亮壳只是边缘 ⇒ 压亮壳、抬黑烟
    # =====================================================================
    dict(
        name="lwn_yinmo_burst",
        guid="{A1B2C3D4-0002-4E5F-9A0B-1C2D3E4F5062}",
        emitters=[

            # 球壳本体：小球体积 + 高速外扩 + 阻尼 → 粒子停在某个半径上形成壳
            #   壳半径 ≈ 初速 / 阻尼 = 3.0 / 1.5 = **2.0m**（落在实测 1.6–2.2m 区间内）
            #   2026-09-24：速率 1100→600、emissive 1.4→0.45、alpha 峰值 0.55→0.26
            #   —— 实测 1.50–1.60s 原来是一颗白球，参考同刻是"红核 + 黑烟"
            dict(name="wave_shell",
                 material="prt_shd_glow",
                 emission_rate=(600, 0), particle_life=(0.60, 0.18),
                 emit_sphere_radius=0.22, emit_volume_type="sphere",
                 damping=1.5, gravity="0.000, 0.000, 0.000",
                 velocity=(3.00, 0.90),
                 particle_size=(0.300, 0.090), size_curve=(0.35, 2.20, 0.550),
                 emissive_multiplier=0.45,
                 max_alive_particle_count=1000,
                 flags=dict(order_by_distance=True),
                 color=[(0.0, "0.980, 0.600, 0.560"),
                        (0.55, "0.900, 0.200, 0.260"),
                        (1.0, "0.480, 0.070, 0.130")],
                 alpha=[(0.0, 0.0), (0.08, 0.26), (0.50, 0.13), (1.0, 0.0)]),

            # 亮边 / 经线亮纹 —— 造成"弧带 0.6–0.85m 厚"的读数主要靠它
            dict(name="wave_edge",
                 material="prt_shd_glow",
                 emission_rate=(380, 0), particle_life=(0.55, 0.15),
                 emit_sphere_radius=0.18, emit_volume_type="sphere",
                 damping=1.3, gravity="0.000, 0.000, 0.000",
                 velocity=(2.90, 0.80),
                 particle_size=(0.070, 0.028), size_curve=(1.0, 0.40, 0.025),
                 emissive_multiplier=2.0,
                 max_alive_particle_count=700,
                 color=[(0.0, "1.000, 0.700, 0.640"), (1.0, "1.000, 0.320, 0.360")],
                 alpha=[(0.0, 0.0), (0.06, 0.62), (0.60, 0.24), (1.0, 0.0)]),

            # 🔴 黑烟爆发 —— modulate；尺寸上限 ≈ 0.70×3.0 ≈ 2.1m
            #    alpha 峰值 0.55（乘法连乘会饱和，别再加）
            #    2026-09-24：速率 300→340、速度 →2.6、阻尼 0.65→0.45（铺得更开）、尺寸 →0.70
            dict(name="dark_smoke_burst",
                 material="prt_shd_haze_1",
                 emission_rate=(340, 0), particle_life=(2.30, 0.80),
                 emit_sphere_radius=0.55, emit_volume_type="sphere",
                 damping=0.45, gravity="0.000, 0.000, 0.000",
                 velocity=(2.60, 1.10),
                 particle_size=(0.700, 0.220), size_curve=(0.50, 3.00, 0.450),
                 diffuse_multiplier=0.35, emissive_multiplier=0.0,
                 max_alive_particle_count=900,
                 flags=dict(order_by_distance=True),
                 color=[(0.0, "0.220, 0.090, 0.130"), (1.0, "0.070, 0.030, 0.050")],
                 alpha=[(0.0, 0.0), (0.10, 0.55), (0.55, 0.28), (1.0, 0.0)]),

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
    #   2026-09-24：黑烟成规模 + 亮带加厚（原来细得像根线）
    # =====================================================================
    dict(
        name="lwn_yinmo_trail",
        guid="{A1B2C3D4-0003-4E5F-9A0B-1C2D3E4F5063}",
        emitters=[

            # 🔴 月牙亮边 —— 决定"这是月牙"的主元素：丝带朝向速度方向，一层刃光
            #    骑砍没有 mesh / ribbon emitter，月牙只能这样"用速度铺出来的带"近似
            #    2026-09-24：厚度 0.32→0.52（实测带厚 0.6–0.85m 的下沿）、emissive 3.0→2.2
            dict(name="crescent_edge",
                 material="prt_shd_glow",
                 billboard_type="turn_to_velocity_side",
                 emission_rate=(520, 0), particle_life=(0.85, 0.25),
                 emit_sphere_radius=0.16, emit_volume_type="sphere",
                 damping=0.30, gravity="0.000, 0.000, 0.000",
                 velocity=(0.35, 0.35), inherit_emitter_velocity=0.94,
                 particle_size=(0.520, 0.140), size_curve=(1.0, 0.16, 0.040),
                 emissive_multiplier=2.2,
                 max_alive_particle_count=900,
                 color=[(0.0, "1.000, 0.620, 0.550"),
                        (0.60, "0.950, 0.120, 0.200"),
                        (1.0, "0.620, 0.060, 0.140")],
                 alpha=[(0.0, 0.0), (0.10, 0.90), (0.60, 0.40), (1.0, 0.0)]),

            # 弧尖亮珠 —— 弧上那颗白热核（实测 ⌀0.20–0.25m，是整条弧最亮的一点）
            dict(name="core_bead",
                 material="prt_shd_glow",
                 emission_rate=(170, 0), particle_life=(0.22, 0.07),
                 emit_sphere_radius=0.055, emit_volume_type="sphere",
                 damping=2.4, gravity="0.000, 0.000, 0.000",
                 velocity=(0.12, 0.12), inherit_emitter_velocity=1.00,
                 particle_size=(0.175, 0.045), size_curve=(1.0, 0.50, 0.030),
                 emissive_multiplier=3.0,
                 max_alive_particle_count=300,
                 color=[(0.0, "1.000, 0.920, 0.800"), (1.0, "1.000, 0.400, 0.300")],
                 alpha=[(0.0, 0.0), (0.12, 1.0), (0.70, 0.40), (1.0, 0.0)]),

            # 余烬光点 —— 厘米级、飘散、微下坠、长命
            dict(name="embers",
                 material="prt_shd_glow",
                 emission_rate=(44, 0), particle_life=(2.40, 0.90),
                 emit_sphere_radius=0.35, emit_volume_type="sphere",
                 damping=0.16, gravity="0.000, 0.000, 0.000",
                 velocity=(0.75, 0.75), inherit_emitter_velocity=0.80,
                 particle_size=(0.050, 0.028), size_curve=(1.0, 0.10, 0.012),
                 emissive_multiplier=2.6,
                 max_alive_particle_count=500,
                 color=[(0.0, "1.000, 0.620, 0.780"), (1.0, "0.480, 0.120, 0.320")],
                 alpha=[(0.0, 0.0), (0.25, 1.0), (0.70, 0.50), (1.0, 0.0)]),

            # 🔴 黑烟团 —— modulate；每团最大 ≈ 0.42×3.0 ≈ 1.26m，alpha 峰值 0.50
            #    2026-09-24：速率 70→220、尺寸 0.30→0.42、曲线顶 2.4→3.0、命 2.3→2.6s
            #    —— 参考残迹段是"一整条黑色烟带"，原来那点量根本铺不出来
            dict(name="smoke_puffs",
                 material="prt_shd_haze_1",
                 emission_rate=(220, 0), particle_life=(2.60, 0.90),
                 emit_sphere_radius=0.45, emit_volume_type="sphere",
                 damping=0.20, gravity="0.000, 0.000, 0.000",
                 velocity=(0.75, 0.60), inherit_emitter_velocity=0.86,
                 particle_size=(0.420, 0.150), size_curve=(0.45, 3.00, 0.250),
                 diffuse_multiplier=0.35, emissive_multiplier=0.0,
                 max_alive_particle_count=900,
                 flags=dict(order_by_distance=True),
                 color=[(0.0, "0.200, 0.080, 0.120"), (1.0, "0.060, 0.030, 0.050")],
                 alpha=[(0.0, 0.0), (0.12, 0.50), (0.55, 0.26), (1.0, 0.0)]),

            # 🔴 品红飘带 —— 视频里活得最久的元素（1.5–2.6s、2–4m 长的细弧线，2–3 根成组）
            #    material 换 prt_shd_trail（T_Trail / T_Ribbon 贴图）才是"飘带"而非"烟丝"
            dict(name="magenta_ribbons",
                 material="prt_shd_trail",
                 billboard_type="turn_to_velocity_side",
                 emission_rate=(64, 0), particle_life=(3.00, 1.10),
                 emit_sphere_radius=0.22, emit_volume_type="sphere",
                 damping=0.10, gravity="0.000, 0.000, 0.000",
                 velocity=(2.20, 0.60), inherit_emitter_velocity=0.90,
                 particle_size=(0.055, 0.020), size_curve=(1.0, 0.35, 0.010),
                 emissive_multiplier=1.9,
                 max_alive_particle_count=500,
                 color=[(0.0, "0.950, 0.660, 0.860"), (1.0, "0.600, 0.220, 0.500")],
                 alpha=[(0.0, 0.0), (0.15, 0.58), (0.70, 0.30), (1.0, 0.0)]),
        ]),
]
