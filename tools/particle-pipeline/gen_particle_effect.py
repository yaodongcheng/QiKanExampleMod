#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""骑砍 2 粒子特效生成器 —— 紧凑 spec → 完整 particle_systems XML。

【为什么要生成器】
引擎的粒子格式是**定长**的：每个 emitter 固定 21 个 flag + 55 个 parameter
（2026-09-18 对 Native/ModuleData/particle_systems_*.xml 全量统计实证：
 basic 16 个 emitter 全是 (21,55)，general 57 个全是 (21,55)，无一例外）。
所以手写一个 emitter 就是 ~76 行样板。做一套三段式技能 = 一千行噪音。

生成器把「要改的那几个值」和「格式样板」分开：spec 里只写要改的，
其余从 DEFAULT_PARAMS 里原样带出。

【DEFAULT_PARAMS 的来源】
逐字取自 Native 1.2.12 的 `particle_systems_basic.xml` → `prt_hit_dust`
（原版最简单的一次性爆发效果）。**顺序也是原版顺序**，不要重排——
保持同序才能让 diff 只显示真正改动的行。

【用法】
    python tools/particle-pipeline/gen_particle_effect.py <spec.py> -o <输出.xml>

    （原来在 Scripts/ 下，2026-09-20 随粒子工具链搬进 tools/particle-pipeline/；
      调用方 ue2bannerlord.py 走 paths.GEN 定位，不写死路径。）

spec.py 里定义 EFFECTS = [ {...}, ... ]，见同目录示例或本文件末尾的 SPEC 说明。

【纪律（铁律 22）】
生成物**禁止手改**。要改效果 = 改 spec → 重跑本脚本。
"""

import argparse
import importlib.util
import io
import os
import sys

# ---------------------------------------------------------------------------
# 原版默认模板（逐字取自 Native 1.2.12 / particle_systems_basic.xml / prt_hit_dust）
# 每项: (参数名, 类型, 默认值)
#   类型 const : 默认值 = 字符串（原样写进 value=）
#   类型 rand  : 默认值 = (base, bias)
#   类型 curve : 默认值 = dict(base, bias, curve_name, mult, default, keys)
#   类型 color : 默认值 = dict(color=[(t,v)…], alpha=[(t,v)…])
# ---------------------------------------------------------------------------
DEFAULT_FLAGS = [
    ("emit_while_moving", "false"),
    ("dont_emit_while_moving", "false"),
    ("emit_at_once", "false"),
    ("local_emit_dir", "true"),
    ("loop_sprite", "false"),
    ("uses_sprite_animation", "false"),
    ("scale_with_respect_to_emitter_velocity", "false"),
    ("skew_with_respect_to_particle_velocity", "false"),
    ("spherical_normals", "true"),
    ("emit_on_terrain", "false"),
    ("select_random_sample_mesh", "false"),
    ("fixed_billboard_direction", "false"),
    ("order_by_distance", "false"),
    ("select_random_sprite", "true"),
    ("create_decal_on_collision", "false"),
    ("create_decal_only_once", "false"),
    ("randomize_collision_decal_rotation", "false"),
    ("permanent_collision_decals", "false"),
    ("collide_with_objects", "false"),
    ("use_color_from_terrain", "false"),
    ("enable_collision", "false"),
]

DEFAULT_PARAMS = [
    ("emitter_life", "const", "0.000"),
    ("activation_delay", "const", "0.000"),
    ("skew_with_particle_velocity_coef", "const", "10.000"),
    ("skew_with_particle_velocity_limit", "const", "0.000"),
    ("scale_with_emitter_velocity_coef", "const", "0.000"),
    ("inherit_emitter_velocity", "const", "0.000"),
    ("emit_volume", "const", "0.000, 0.000, 0.000"),
    ("emit_sphere_radius", "const", "1.000"),
    ("gravity", "const", "0.000, 0.000, -1.000"),
    ("fixed_billboard_direction", "const", "0.000, 0.000, 1.000"),
    ("emission_speed_limit", "const", "0.000"),
    ("decal_min_scale", "const", "1.000, 1.000"),
    ("decal_max_scale", "const", "1.000, 1.000"),
    ("skinned_decal_start_index", "const", "-1"),
    ("skinned_decal_end_index", "const", "-1"),
    ("max_alive_particle_count", "const", "0"),
    ("quad_scale", "const", "1.000, 1.000"),
    ("quad_bias", "const", "0.000, 0.000"),
    ("texture_sprite_count", "const", "2, 2"),
    ("texture_sprite_frame_count", "const", "1"),
    ("texture_sprite_frame_rate", "const", "1.000"),
    ("fixed_particle_initial_speed", "const", "0.000"),
    ("fadeout_distance", "const", "1.000"),
    ("fadeout_coef", "const", "0.000"),
    ("camera_fadeout_coef", "const", "0.000"),
    ("backlight_multiplier", "const", "0.000"),
    ("diffuse_multiplier", "const", "1.000"),
    ("emissive_multiplier", "const", "1.000"),
    ("heatmap_multiplier", "const", "1.000"),
    ("emission_turbulence_interval", "const", "340282346638528859811704183484516925440.000"),
    ("emission_turbulence_strength", "const", "0.000"),
    ("collision_damping", "const", "0.000"),
    ("collision_angular_damping", "const", "0.000"),
    ("cone_emit_angle", "const", "0.000"),
    ("particle_size_curve_op", "const", "add"),
    ("emit_volume_type", "const", "box"),
    ("billboard_type", "const", "3d"),
    ("collision_behaviour", "const", "normal"),
    ("emission_velocity_model", "const", "random_velocity_components"),
    ("initial_rotation", "rand", ("0.000", "180.000")),
    ("damping", "rand", ("0.733", "0.000")),
    ("turbulence_strength", "rand", ("0.000", "0.000")),
    ("angular_damping", "rand", ("0.599", "0.000")),
    ("particle_life", "rand", ("1.300", "0.400")),
    ("emission_rate", "rand", ("5.000", "0.000")),
    ("cone_emit_velocity", "curve", dict(
        base="1.000", bias="0.000", curve_name="emitter_life",
        mult="1.000", default="1.000",
        keys=[("0.000", "1.000", "0.200, 0.000"), ("1.000", "1.000", "-0.200, 0.000")])),
    ("emit_velocity_x", "rand", ("1.000", "1.000")),
    ("emit_velocity_y", "rand", ("0.000", "1.000")),
    ("emit_velocity_z", "rand", ("1.000", "1.000")),
    ("emit_rotation_speed", "rand", ("0.000", "171.887")),
    ("wind_effect", "curve", dict(
        base="1.000", bias="0.000", curve_name="particle_life",
        mult="1.000", default="1.000",
        keys=[("0.000", "1.000", "0.200, 0.000"), ("1.000", "1.000", "-0.200, 0.000")])),
    ("particle_size", "curve", dict(
        base="0.250", bias="0.250", curve_name="particle_life",
        mult="0.300", default="1.000",
        keys=[("0.000", "1.000", "0.193, 0.000"), ("1.000", "0.667", "-0.193, 0.000")])),
    ("material", "const", "prt_shd_smoke_1"),
    ("sample_mesh", "const", ""),
    ("particle_color", "color", dict(
        color=[("0.100", "0.307, 0.255, 0.220"), ("0.900", "0.349, 0.284, 0.231")],
        alpha=[("0.000", "0.000"), ("0.300", "0.392"), ("0.700", "0.392"), ("1.000", "0.000")])),
]

# 数字格式：原版一律三位小数；保留字面量（如 "add" / "3d"）原样透传
def _num(v):
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, float)):
        return "%.3f" % v
    return str(v)


# 整型参数 —— 这些**不能**写成小数（原版是 `value="0"`）。
# 引擎若按 int 解析该属性，`"500.000"` 会直接失败。宁保守勿激进。
INT_PARAMS = {
    "max_alive_particle_count",
    "skinned_decal_start_index",
    "skinned_decal_end_index",
    "texture_sprite_frame_count",
}


def _fmt(key, v):
    if key in INT_PARAMS:
        return "%d" % int(round(float(v)))
    return _num(v)


def _pairs(seq):
    return [( _num(t), v if isinstance(v, str) else _num(v)) for t, v in seq]


class Emitter(object):
    """一个 emitter 的最终参数集：默认模板 + spec 覆盖。"""

    def __init__(self, spec):
        self.name = spec["name"]
        self.index = spec.get("_index_", 0)
        self.flags = dict(DEFAULT_FLAGS)
        self.params = {}
        for n, t, d in DEFAULT_PARAMS:
            self.params[n] = (t, d)

        # 1) flag 覆盖
        for k, v in (spec.get("flags") or {}).items():
            if k not in self.flags:
                raise KeyError("未知 flag: %s" % k)
            self.flags[k] = "true" if v else "false"

        # 2) 参数覆盖 —— spec 的键名直接就是 XML 参数名
        handled = set()
        for key, val in spec.items():
            if key in ("name", "_index_", "flags", "size_curve", "velocity", "color", "alpha",
                       "extra_params"):
                continue
            if key not in self.params:
                raise KeyError("emitter '%s' 未知参数: %s" % (self.name, key))
            t, _ = self.params[key]
            if t == "const":
                self.params[key] = ("const", _fmt(key, val))
            elif t == "rand":
                if isinstance(val, (tuple, list)) and len(val) == 2:
                    self.params[key] = ("rand", (_num(val[0]), _num(val[1])))
                else:
                    self.params[key] = ("rand", (_num(val), "0.000"))
            elif t == "curve":
                d = dict(self.params[key][1])
                if isinstance(val, dict):
                    d.update({k: _num(v) for k, v in val.items() if k != "keys"})
                    if "keys" in val:
                        d["keys"] = _pairs(val["keys"])
                elif isinstance(val, (tuple, list)) and len(val) == 2:
                    # (base, bias) —— 曲线参数同样支持 base±bias 随机
                    d["base"], d["bias"] = _num(val[0]), _num(val[1])
                else:
                    d["base"] = _num(val)
                self.params[key] = ("curve", d)
            handled.add(key)

        # 3) size_curve 简写：(起点值, 终点值, curve_multiplier)
        if "size_curve" in spec:
            k0, k1, mult = spec["size_curve"][:3]
            d = dict(self.params["particle_size"][1])
            d["mult"] = _num(mult)
            d["keys"] = [(  "0.000", _num(k0), "0.200, 0.000"),
                         (  "1.000", _num(k1), "-0.200, 0.000")]
            self.params["particle_size"] = ("curve", d)

        # 3b) velocity 简写：各向同性初速（base, bias）→ 同时写 x/y/z
        if "velocity" in spec:
            b, bias = (list(spec["velocity"]) + [0])[:2]
            for ax in "xyz":
                self.params["emit_velocity_" + ax] = ("rand", (_num(b), _num(bias)))

        # 3c) extra_params  「55 个以外的参数」逃生口（2026-09-22 加）
        #   背景：引擎参数名字符串池里有一批**原版 XML 从没写过**的字段
        #   （radial_velocity / radial_emission_velocity / radial_rotation_speed /
        #     emit_sphere_radius_inner / emit_box_size / emit_disc_radius / warmup_time ...）
        #   证据 = TaleWorlds.Native.dll 字符串池，以及 Native/Prefabs/*.xml 真的写过
        #     emit_disc_radius / emit_box_size / decal_material / camera_fadeout_far_coef。
        #   写在这里的参数**追加**在标准 55 个之后；默认不写 => 输出仍是原版 21/55 定长。
        #   注意：预览器只实现了 55 个 => 这类参数在预览里一定「没反应」，只能进游戏判。
        self.extra = list((spec.get("extra_params") or {}).items())

        # 4) 颜色 / 透明度曲线
        if "color" in spec or "alpha" in spec:
            d = dict(self.params["particle_color"][1])
            if "color" in spec:
                d["color"] = _pairs(spec["color"])
            if "alpha" in spec:
                d["alpha"] = _pairs(spec["alpha"])
            self.params["particle_color"] = ("color", d)

    # -- 输出 ---------------------------------------------------------------
    def xml(self, tabs="\t\t\t"):
        L = []
        a = L.append
        a('%s<emitter' % tabs)
        a('%s\tname="%s"' % (tabs, self.name))
        a('%s\t_index_="%d">' % (tabs, self.index))
        a('%s\t<flags>' % tabs)
        for n, _ in DEFAULT_FLAGS:
            a('%s\t\t<flag' % tabs)
            a('%s\t\t\tname="%s"' % (tabs, n))
            a('%s\t\t\tvalue="%s" />' % (tabs, self.flags[n]))
        a('%s\t</flags>' % tabs)
        a('%s\t<parameters>' % tabs)
        for n, _, _ in DEFAULT_PARAMS:
            t, d = self.params[n]
            if t == "const":
                a('%s\t\t<parameter' % tabs)
                a('%s\t\t\tname="%s"' % (tabs, n))
                a('%s\t\t\tvalue="%s" />' % (tabs, d))
            elif t == "rand":
                a('%s\t\t<parameter' % tabs)
                a('%s\t\t\tname="%s"' % (tabs, n))
                a('%s\t\t\tbase="%s"' % (tabs, d[0]))
                a('%s\t\t\tbias="%s" />' % (tabs, d[1]))
            elif t == "curve":
                a('%s\t\t<parameter' % tabs)
                a('%s\t\t\tname="%s"' % (tabs, n))
                a('%s\t\t\tbase="%s"' % (tabs, d["base"]))
                a('%s\t\t\tbias="%s">' % (tabs, d["bias"]))
                a('%s\t\t\t<curve' % tabs)
                a('%s\t\t\t\tname="%s"' % (tabs, d["curve_name"]))
                a('%s\t\t\t\tversion="1"' % tabs)
                a('%s\t\t\t\tdefault="%s"' % (tabs, d["default"]))
                a('%s\t\t\t\tcurve_multiplier="%s">' % (tabs, d["mult"]))
                a('%s\t\t\t\t<keys>' % tabs)
                for kt, kv, ktan in d["keys"]:
                    a('%s\t\t\t\t\t<key' % tabs)
                    a('%s\t\t\t\t\t\ttime="%s"' % (tabs, kt))
                    a('%s\t\t\t\t\t\tvalue="%s"' % (tabs, kv))
                    a('%s\t\t\t\t\t\ttangent="%s" />' % (tabs, ktan))
                a('%s\t\t\t\t</keys>' % tabs)
                a('%s\t\t\t</curve>' % tabs)
                a('%s\t\t</parameter>' % tabs)
            elif t == "color":
                a('%s\t\t<parameter' % tabs)
                a('%s\t\t\tname="%s">' % (tabs, n))
                a('%s\t\t\t<color>' % tabs)
                a('%s\t\t\t\t<keys>' % tabs)
                for kt, kv in d["color"]:
                    a('%s\t\t\t\t\t<key' % tabs)
                    a('%s\t\t\t\t\t\ttime="%s"' % (tabs, kt))
                    a('%s\t\t\t\t\t\tvalue="%s" />' % (tabs, kv))
                a('%s\t\t\t\t</keys>' % tabs)
                a('%s\t\t\t</color>' % tabs)
                a('%s\t\t\t<alpha>' % tabs)
                a('%s\t\t\t\t<keys>' % tabs)
                for kt, kv in d["alpha"]:
                    a('%s\t\t\t\t\t<key' % tabs)
                    a('%s\t\t\t\t\t\ttime="%s"' % (tabs, kt))
                    a('%s\t\t\t\t\t\tvalue="%s" />' % (tabs, kv))
                a('%s\t\t\t\t</keys>' % tabs)
                a('%s\t\t\t</alpha>' % tabs)
                a('%s\t\t</parameter>' % tabs)
        for k, v in self.extra:
            a('%s\t\t<parameter' % tabs)
            a('%s\t\t\tname="%s"' % (tabs, k))
            a('%s\t\t\tvalue="%s" />' % (tabs, _num(v)))
        a('%s\t</parameters>' % tabs)
        a('%s</emitter>' % tabs)
        return "\n".join(L)


def build(spec):
    # 🔴 形状**逐字对齐原版**（Native/ModuleData/particle_systems_*.xml）：**带 UTF-8 BOM**、
    #    **根节点前不放任何注释**、禁止物警告一律放**根节点之后**（XML 允许根元素后带注释）。
    #    为什么：这份 XML 的最后一个消费者是**编辑器**（运行时读的是它发布出来的粒子包），
    #    而原版 7 份粒子文件清一色是「BOM + 根节点前零注释」—— 少一个变量是一个（2026-09-24 改）。
    out = ['<?xml version="1.0" encoding="utf-8"?>']
    out.append('<particle_effects>')
    for eff in spec:
        out.append('\t<effect')
        out.append('\t\tname="%s"' % eff["name"])
        out.append('\t\tguid="%s">' % eff["guid"])
        out.append('\t\t<emitters>')
        for i, es in enumerate(eff["emitters"]):
            es = dict(es)
            es["_index_"] = i
            out.append(Emitter(es).xml())
        out.append('\t\t</emitters>')
        out.append('\t</effect>')
    out.append('</particle_effects>')
    out.append('<!-- 生成物 —— 禁止手改（铁律 22）。改效果请改 spec 并重跑：')
    out.append('     python tools/particle-pipeline/gen_particle_effect.py <spec.py> -o <输出.xml> -->')
    return "\n".join(out) + "\n"


def main():
    ap = argparse.ArgumentParser(description="骑砍 2 粒子特效生成器")
    ap.add_argument("spec", help="spec.py（定义 EFFECTS = [...]）")
    ap.add_argument("-o", "--out", required=True, help="输出 XML 路径")
    args = ap.parse_args()

    name = os.path.splitext(os.path.basename(args.spec))[0]
    s = importlib.util.spec_from_file_location(name, args.spec)
    mod = importlib.util.module_from_spec(s)
    s.loader.exec_module(mod)
    if not getattr(mod, "EFFECTS", None):
        print("spec 里没有 EFFECTS", file=sys.stderr)
        return 1

    xml = build(mod.EFFECTS)

    # 自检 —— 这类「spec 值没被正确展开」的 bug 必须由生成器自己抓，不能靠人眼。
    # 踩过：particle_size=(0.24,0.07) 走错分支 → 输出 base="(0.24, 0.07)"。
    for bad in ("(", ")", "[", "]", "'", "None"):
        if bad in xml:
            print("自检失败：输出含可疑串 %r —— spec 里有值没被正确展开" % bad, file=sys.stderr)
            for i, line in enumerate(xml.split("\n"), 1):
                if bad in line:
                    print("  第 %d 行: %s" % (i, line.strip()), file=sys.stderr)
                    break
            return 1

    # 🔴 `utf-8-sig` = 写 UTF-8 BOM（对齐原版粒子文件；理由见 build() 头部）
    io.open(args.out, "w", encoding="utf-8-sig", newline="\n").write(xml)

    n_em = sum(len(e["emitters"]) for e in mod.EFFECTS)
    print("写出 %s" % args.out)
    print("  %d 个 effect / %d 个 emitter / %d 行 / %d 字节"
          % (len(mod.EFFECTS), n_em, xml.count("\n") + 1, len(xml.encode("utf-8"))))
    return 0


if __name__ == "__main__":
    sys.exit(main())
