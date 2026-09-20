# -*- coding: utf-8 -*-
"""probe_v2.py -- 第二轮：Cascade 完整数值 + Niagara T3D 导出实验 + 通用反射探测。"""
import unreal, os, traceback

OUT = os.environ.get("BM_PART_OUT", r"D:/BrainMaker/骑砍2粒子特效复刻/output/probe")
os.makedirs(OUT, exist_ok=True)
_f = open(os.path.join(OUT, "vfx_api_probe2.log"), "w", encoding="utf-8")
def L(m):
    _f.write(str(m)+"\n"); _f.flush()
    try: unreal.log(str(m))
    except Exception: pass
def S(t=""):
    L(""); L("="*78); L("## "+t); L("="*78)
def safe(fn,*a,**k):
    try: return fn(*a,**k)
    except Exception as e: return "<ERR %s: %s>" % (type(e).__name__, e)
def d(v,n=300):
    return repr(v)[:n]
def props(o,lim=300):
    try: return [x for x in dir(o) if not x.startswith("_")][:lim]
    except Exception as e: return ["<err %s>"%e]

def walk_module(m, depth, idx):
    t = type(m)
    tn = t.__name__ if hasattr(t,"__name__") else str(t)
    L("%sMOD[%d] %s" % ("  "*depth, idx, tn))
    for p in ("emitter_duration","emitter_loops","rate","rate_scale","spawn_rate",
              "start_size","start_color","start_location","start_velocity","max_particles",
              "lifetime","lifetime_range","size","alpha","color","velocity","acceleration",
              "gravity","damping","material","mesh","alignment","velocity_scale",
              "is_enabled","enabled","use_global_spawn_rate","use_legacy_spawn_rate"):
        r = safe(m.get_editor_property,p)
        if not isinstance(r,str):
            L("%s   .%-22s = %s" % ("  "*depth,p,d(r,260)))

def cascade():
    S("CASCADE ParticleSystem 深挖")
    L("has unreal.ParticleSystem = %s" % hasattr(unreal,"ParticleSystem"))
    for np_ in ["/Game/FlexibleCombatSystem/VFX/MeleeVFX/P_BlockImpact",
                "/Game/FlexibleCombatSystem/VFX/RangedVFX/BowBuff/P_BowBuffFire"]:
        a = safe(unreal.load_asset, np_)
        L("")
        L("### load %s -> %s" % (np_, d(a,120)))
        if isinstance(a,str): continue
        L("class=%s" % type(a))
        L("dir=%s" % props(a,200))
        for pr in ("emitters","max_draw_distance","update_time_fps","warmup_time",
                   "seconds_before_inactive","delay","delay_low","b_use_emitter_loop",
                   "use_delay_range","b_use_delay_range"):
            L("  sys.%-24s = %s" % (pr, d(safe(a.get_editor_property,pr),200)))
        ems = safe(a.get_editor_property,"emitters")
        if isinstance(ems,str): continue
        L("  emitters len = %s" % (len(ems) if hasattr(ems,"__len__") else ems))
        try: ems_list = list(ems)
        except Exception as e: L("  list err %s"%e); continue
        for i,em in enumerate(ems_list[:2]):
            L("  EMITTER[%d] class=%s dir=%s" % (i,type(em),props(em,160)))
            for pr in ("emitter_name","emitter_duration","emitter_loops","material",
                       "b_disabled","sprite_animation","type_data","lod_levels",
                       "use_max_draw_distance","initial_velocity","required",
                       "low_frequency_enable","medium_frequency_enable"):
                L("    .%-24s = %s" % (pr, d(safe(em.get_editor_property,pr),200)))
            lods = safe(em.get_editor_property,"lod_levels")
            if isinstance(lods,str): continue
            try: lods_list=list(lods)
            except Exception as e: L("    lod list err %s"%e); continue
            L("    lod_levels = %d" % len(lods_list))
            for li,lod in enumerate(lods_list[:1]):
                L("      LOD[%d] class=%s dir=%s" % (li,type(lod),props(lod,140)))
                for pr in ("modules","type_data","enabled","spawn_rate","peak_active_particles",
                           "max_active_particles","emitter_duration","emitter_delay"):
                    L("        .%-22s = %s" % (pr,d(safe(lod.get_editor_property,pr),200)))
                mods = safe(lod.get_editor_property,"modules")
                if isinstance(mods,str): continue
                try: mods_list=list(mods)
                except Exception as e: L("        mod list err %s"%e); continue
                L("        modules = %d" % len(mods_list))
                for mi,m in enumerate(mods_list):
                    walk_module(m, 5, mi)
            td = safe(em.get_editor_property,"type_data")
            if not isinstance(td,str) and td is not None:
                L("    TYPE_DATA class=%s dir=%s" % (type(td),props(td,140)))
                for pr in ("material","mesh","alignment","sub_image_size","sub_images_horizontal",
                           "sub_images_vertical","screen_alignment","opacity_source_material",
                           "render_mode","blend_mode","macros","b_use_material_style"):
                    L("      .%-26s = %s" % (pr,d(safe(td.get_editor_property,pr),220)))
                mat = safe(td.get_editor_property,"material")
                if not isinstance(mat,str) and mat is not None:
                    L("      MATERIAL class=%s dir=%s" % (type(mat),props(mat,120)))
                    for pr in ("blend_mode","blend_mode_name","shading_model","two_sided","base_color"):
                        L("        mat.%-20s = %s" % (pr,d(safe(mat.get_editor_property,pr),200)))

def t3d():
    S("T3D / 文本导出实验")
    L("Exporter* classes in unreal module:")
    try:
        L("  " + ", ".join([n for n in dir(unreal) if "xporter" in n]))
    except Exception as e: L("  err %s"%e)
    L("has unreal.new_object = %s" % hasattr(unreal,"new_object"))
    for clsname in ("/Script/UnrealEd.ExporterT3D", "/Script/UnrealEd.Exporter",
                    "/Script/Engine.Exporter"):
        c = safe(unreal.load_class, None, clsname)
        L("load_class %s -> %s" % (clsname, d(c,120)))
    targets = [
        ("niagara", "/Game/FlexibleCombatSystem/VFX/MagicVFX/Projectiles/NS_Fireball"),
        ("cascade", "/Game/FlexibleCombatSystem/VFX/MeleeVFX/P_BlockImpact"),
    ]
    t3d_cls = safe(unreal.load_class, None, "/Script/UnrealEd.ExporterT3D")
    for tag,path in targets:
        a = safe(unreal.load_asset, path)
        if isinstance(a,str): L("%s load fail"%tag); continue
        L("")
        L("-- %s %s" % (tag,path))
        if not hasattr(unreal,"new_object") or isinstance(t3d_cls,str):
            L("   new_object / ExporterT3D 不可用，跳过"); continue
        exp = safe(unreal.new_object, t3d_cls)
        L("   new_object -> %s" % d(exp,120))
        if isinstance(exp,str): continue
        fn = os.path.join(OUT, "t3d_%s.t3d" % tag)
        try:
            task = unreal.AssetExportTask()
            task.set_editor_property("object", a)
            task.set_editor_property("exporter", exp)
            task.set_editor_property("filename", fn)
            task.set_editor_property("automated_export", True)
            task.set_editor_property("replace_identical", True)
            task.set_editor_property("prompt", False)
            r = safe(unreal.Exporter.run_asset_export_task, task)
            L("   run_asset_export_task -> %s" % d(r,120))
            ex = os.path.exists(fn)
            L("   file exists = %s size=%s" % (ex, (os.path.getsize(fn) if ex else -1)))
        except Exception as e:
            L("   err %s %s" % (type(e).__name__, e))

def tags():
    S("AssetRegistry 标签 / 元数据")
    eal = getattr(unreal,"EditorAssetLibrary",None)
    L("has EditorAssetLibrary = %s" % (eal is not None))
    if eal:
        L("  dir = %s" % props(eal,120))
        for p in ["/Game/FlexibleCombatSystem/VFX/MagicVFX/Projectiles/NS_Fireball",
                  "/Game/FlexibleCombatSystem/VFX/MeleeVFX/P_BlockImpact"]:
            ad = safe(eal.find_asset_data, p)
            L("  find_asset_data(%s) -> %s" % (p, d(ad,200)))
            if not isinstance(ad,str) and ad is not None:
                tg = safe(ad.get_editor_property,"tags_and_values")
                L("     tags = %s" % d(tg,800))
    for n in dir(unreal):
        if "Metadata" in n or "PropertyAccess" in n or "PropertyUtil" in n:
            L("  reflex-hint: %s" % n)

def main():
    S("ENGINE"); L(unreal.SystemLibrary.get_engine_version())
    try: cascade()
    except Exception: L("cascade FATAL\n"+traceback.format_exc())
    try: t3d()
    except Exception: L("t3d FATAL\n"+traceback.format_exc())
    try: tags()
    except Exception: L("tags FATAL\n"+traceback.format_exc())
    S("DONE"); _f.close(); print("PROBE2 DONE")

try: main()
except Exception:
    L("FATAL\n"+traceback.format_exc()); _f.close(); raise
