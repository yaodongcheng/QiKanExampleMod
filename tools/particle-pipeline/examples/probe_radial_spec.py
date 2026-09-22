# -*- coding: utf-8 -*-
"""探针 spec —— 「引擎到底认不认 55 个以外的粒子参数」

【要判什么】
用户质疑：「一般粒子特效都能做引力/球体内运动，骑砍2 难道不支持？」
静态证据停在这一步（2026-09-22 核实）：
  ✅ TaleWorlds.Native.dll 的参数名字符串池里**确实有**
       `radial_velocity` / `radial_emission_velocity` / `radial_rotation_speed` /
       `emit_sphere_radius_inner` / `emit_box_size` / `emit_disc_radius` / `warmup_time` …
     其中 `radial_velocity` 就夹在 `emit_velocity_y` 与 `activation_delay` 之间。
  ✅ XML **确实存在「55 个以外」的写入路径**：`Native/Prefabs/*.xml` 的 `<emitter_overrides>` 里
     写了 `emit_disc_radius`(5 处) / `emit_box_size`(5) / `decal_material`(77) / `camera_fadeout_far_coef`(1)；
     `emit_volume_type` 还有一种原版 moduleData 里没见过的取值 **`disc`**（3 处）。
  ❌ 但是：`particle_systems_*.xml` 的 emitter 侧，原版 491 个活体 emitter **清一色 (21 flag, 55 param)**
     —— 从没写过第 56 个参数。所以「`radial_velocity` 写进 particle_systems 的 emitter 会不会被读」
     **静态证据到此为止，必须实测**。

【怎么判】把本文件生成的 XML 注册进模块（见 README §2.4），在编辑器里放两个 Particle 组件
（或代码 spawn 两个），对比：
    lwn_probe_radial_in    ← 写了 radial_velocity = -1.60
    lwn_probe_radial_out   ← 写了 radial_velocity = +1.60
    lwn_probe_radial_off   ← 对照组，不写这个参数
判据：
  · 若 in 明显往中心收、out 明显往外飞 ⇒ **引擎认这个参数** → 「真·引力/向心」可用，把
    `radial_velocity` 提到正式 spec 里，替掉现在的「壳层塌缩」近似。
  · 若三个一模一样（烟就停在球壳上不动）⇒ 该字段不接受 XML 写入 → 回到壳层塌缩，
    或用 C# 每帧驱动一个挂粒子的 Entity 去做真·向心（本项目代码侧能做）。

【⚠️ 别用预览页判】preview/make_preview.py 只实现了 55 个参数 → 它对这三个 effect
只会渲染出「一团不动的烟」。这不是引擎的答案。

跑法：
    python tools/particle-pipeline/gen_particle_effect.py examples/probe_radial_spec.py \
        -o out/probe/probe_radial.xml
"""

_BASE = dict(
    material="prt_shd_haze_1",
    emission_rate=(120, 0), particle_life=(1.20, 0.20),
    emit_sphere_radius=0.90, emit_volume_type="sphere",
    damping=0.0, gravity="0.000, 0.000, 0.000",
    velocity=(0.00, 0.00),
    particle_size=(0.180, 0.050), size_curve=(1.0, 0.85, 0.30),
    diffuse_multiplier=0.40, emissive_multiplier=0.0,
    max_alive_particle_count=400,
    flags=dict(order_by_distance=True),
    color=[(0.0, "0.260, 0.100, 0.150"), (1.0, "0.110, 0.040, 0.080")],
    alpha=[(0.0, 0.0), (0.12, 0.26), (0.70, 0.12), (1.0, 0.0)],
)

def _eff(name, guid, extra=None):
    em = dict(_BASE, name="smoke")
    if extra:
        em["extra_params"] = extra
    return dict(name=name, guid=guid, emitters=[em])

EFFECTS = [
    # A · 负径向速度：若引擎认，烟应明显**朝心收**
    _eff("lwn_probe_radial_in",  "{A1B2C3D4-9001-4E5F-9A0B-1C2D3E4F5901}",
         extra={"radial_velocity": -1.60}),
    # B · 正径向速度：应**往外飞**（用来确认「读到了、而且方向对」）
    _eff("lwn_probe_radial_out", "{A1B2C3D4-9002-4E5F-9A0B-1C2D3E4F5902}",
         extra={"radial_velocity": 1.60}),
    # C · 对照组：不写这个参数（= 现在正式 spec 的行为）
    _eff("lwn_probe_radial_off", "{A1B2C3D4-9003-4E5F-9A0B-1C2D3E4F5903}"),
]
