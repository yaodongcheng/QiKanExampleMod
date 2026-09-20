# -*- coding: utf-8 -*-
"""
probe_vfx_api.py -- 探针：摸清 UE4.27 Python 对 Niagara / Cascade 暴露了哪些可读属性。
用法：
  UE4Editor.exe <uproject> -run=pythonscript -script="probe_vfx_api.py" -unattended -nosplash -nullrhi -stdout
输出：<OUT>/vfx_api_probe.log + stdout
"""
import unreal, os, json, traceback

OUT = os.environ.get("BM_PART_OUT", r"D:/BrainMaker/骑砍2粒子特效复刻/output/probe")
os.makedirs(OUT, exist_ok=True)
LOG = os.path.join(OUT, "vfx_api_probe.log")
_f = open(LOG, "w", encoding="utf-8")

def L(msg):
    _f.write(str(msg) + "\n"); _f.flush()
    try: unreal.log(str(msg))
    except Exception: pass

def pub(o, limit=400):
    """列出对象的公开属性名（无下划线开头），过滤掉 _ 开头的 C++ 内部名。"""
    out = []
    try:
        for n in dir(o):
            if n.startswith("_"): continue
            out.append(n)
    except Exception as e:
        out.append("<dir err %s>" % e)
    return out[:limit]

def safe(fn, *a, **k):
    try:
        return fn(*a, **k)
    except Exception as e:
        return "<ERR %s: %s>" % (type(e).__name__, e)

def desc(v, n=200):
    s = repr(v)
    return s[:n]

def section(t):
    L(""); L("=" * 78); L("## " + t); L("=" * 78)

def main():
    section("ENGINE")
    L("engine_version = %s" % safe(unreal.SystemLibrary.get_engine_version))

    section("ASSET REGISTRY 摸底")
    reg = unreal.AssetRegistryHelpers.get_asset_registry()
    L("scan: %s" % safe(reg.scanpaths_synchronous, ["/Game"], True, True))
    all_a = safe(reg.get_all_assets)
    L("all_assets count = %s" % (len(all_a) if isinstance(all_a, list) else all_a))
    try:
        seen = {}
        for a in (all_a or []):
            try: c = str(a.asset_class)
            except Exception: c = "?"
            seen[c] = seen.get(c, 0) + 1
        L("class distribution top30:")
        for k, v in sorted(seen.items(), key=lambda z: -z[1])[:30]:
            L("   %-46s %d" % (k, v))
    except Exception as e:
        L("dist err %s" % e)

    # ---------------- Niagara ----------------
    section("NIAGARA: NiagaraSystem?")
    L("has unreal.NiagaraSystem = %s" % hasattr(unreal, "NiagaraSystem"))
    L("Niagara-related names in unreal module:")
    try:
        ns = [n for n in dir(unreal) if "Niagara" in n]
        L("   count=%d" % len(ns))
        for n in ns[:200]:
            L("   - " + n)
    except Exception as e:
        L("err %s" % e)

    need = unreal.load_asset("/Game/FlexibleCombatSystem/VFX/MagicVFX/Projectiles/NS_Fireball")
    L("")
    L("load NS_Fireball -> %s" % desc(need))
    if need:
        L("class = %s" % type(need))
        L("props = %s" % pub(need, 200))
        handles = safe(need.get_editor_property, "emitter_handles")
        L("emitter_handles = %s" % desc(handles, 600))
        if isinstance(handles, (list, tuple)) or hasattr(handles, "__len__"):
            for i, h in enumerate(handles):
                L("  -- handle[%d] type=%s" % (i, type(h)))
                L("     props = %s" % pub(h, 120))
                L("     name  = %s" % desc(safe(h.get_editor_property, "name")))
                L("     enabled = %s" % desc(safe(h.get_editor_property, "is_enabled")))
                for acc in ("get_instance", "get_emitter_data", "get_emitter", "get_id", "get_name"):
                    if hasattr(h, acc):
                        L("     %s() -> %s" % (acc, desc(safe(getattr(h, acc)), 400)))
                inst = safe(h.get_instance) if hasattr(h, "get_instance") else None
                if inst and not isinstance(inst, str):
                    L("     INSTANCE props: %s" % pub(inst, 200))
                    for pr in ("renderer_properties", "graph", "emitter_spawn_script",
                               "emitter_update_script", "spawn_script", "update_script",
                               "sim_target", "enabled", "name"):
                        L("       %s = %s" % (pr, desc(safe(inst.get_editor_property, pr), 300)))
                    rps = safe(inst.get_editor_property, "renderer_properties")
                    if hasattr(rps, "__len__"):
                        for j, rp in enumerate(rps):
                            L("       renderer[%d] type=%s" % (j, type(rp)))
                            L("         props = %s" % pub(rp, 160))
                            for rpr in ("material", "materials", "mesh", "alignment",
                                        "screen_alignment", "sub_image_size", "facing_mode",
                                        "sort_mode", "pivot_offset", "renderer_visibility"):
                                L("         .%s = %s" % (rpr, desc(safe(rp.get_editor_property, rpr), 240)))
                    for sc in ("emitter_spawn_script", "emitter_update_script",
                               "spawn_script", "update_script"):
                        s = safe(inst.get_editor_property, sc)
                        if s and not isinstance(s, str):
                            L("       SCRIPT %s type=%s props=%s" % (sc, type(s), pub(s, 160)))

    # ---------------- Cascade ----------------
    section("CASCADE: ParticleSystem")
    L("has unreal.ParticleSystem = %s" % hasattr(unreal, "ParticleSystem"))
    cas = unreal.load_asset("/Game/FlexibleCombatSystem/VFX/MeleeVFX/P_BlockImpact")
    L("load P_BlockImpact -> %s" % desc(cas))
    if cas:
        L("class = %s" % type(cas))
        L("props = %s" % pub(cas, 200))
        for pr in ("emitters", "max_draw_distance", "update_time_fps", "use_fixed_relative_time",
                   "seconds_before_inactive", "b_use_emitter_loop", "emitter_delay",
                   "warmup_time", "warmup_tick_delta"):
            L("  .%s = %s" % (pr, desc(safe(cas.get_editor_property, pr), 300)))
        ems = safe(cas.get_editor_property, "emitters")
        if hasattr(ems, "__len__"):
            L("emitter count = %d" % len(ems))
            for i, em in enumerate(ems):
                if i > 1: break
                L("  EMITTER[%d] type=%s" % (i, type(em)))
                L("    props = %s" % pub(em, 200))
                L("    .name = %s" % desc(safe(em.get_editor_property, "emitter_name")))
                lods = safe(em.get_editor_property, "lod_levels")
                if hasattr(lods, "__len__"):
                    L("    lod_levels = %d" % len(lods))
                    for li, lod in enumerate(lods):
                        if li > 0: break
                        L("      LOD[%d] type=%s props=%s" % (li, type(lod), pub(lod, 160)))
                        mods = safe(lod.get_editor_property, "modules")
                        if hasattr(mods, "__len__"):
                            L("      modules = %d" % len(mods))
                            for mi, m in enumerate(mods):
                                if mi > 6: break
                                L("        MOD[%d] type=%s props=%s" % (mi, type(m), pub(m, 80)))
                    tdd = safe(em.get_editor_property, "type_data")
                    if tdd and not isinstance(tdd, str):
                        L("    type_data props = %s" % pub(tdd, 160))
                for pr2 in ("material", "required", "sprite", "initial_velocity",
                            "emitter_duration", "emitter_loops"):
                    L("    .%s = %s" % (pr2, desc(safe(em.get_editor_property, pr2), 200)))

    section("DONE")
    _f.close()
    print("PROBE DONE -> " + LOG)

try:
    main()
except Exception:
    L("FATAL:\n" + traceback.format_exc())
    _f.close()
    raise
