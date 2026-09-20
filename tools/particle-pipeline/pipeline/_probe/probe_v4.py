# -*- coding: utf-8 -*-
"""probe_v4.py -- 修正 AssetExportTask 属性名，重试 T3D 导出。"""
import unreal, os, traceback
OUT = r"D:/BrainMaker/骑砍2粒子特效复刻/output/probe"
os.makedirs(OUT, exist_ok=True)
_f = open(os.path.join(OUT, "vfx_api_probe4.log"), "w", encoding="utf-8")
def L(m):
    _f.write(str(m)+"\n"); _f.flush()
    try: unreal.log(str(m))
    except Exception: pass
def safe(fn,*a,**k):
    try: return fn(*a,**k)
    except Exception as e: return "<ERR %s: %s>" % (type(e).__name__, e)
def d(v,n=400): return repr(v)[:n]
def props(o,lim=200):
    try: return [x for x in dir(o) if not x.startswith("_")][:lim]
    except Exception as e: return ["<err %s>"%e]

L("AssetExportTask dir = %s" % props(unreal.AssetExportTask))
t = safe(unreal.AssetExportTask)
L("new task -> %s" % d(t,200))
if not isinstance(t,str):
    L("task dir = %s" % props(t))

TARGETS = [
    ("niagara", "/Game/FlexibleCombatSystem/VFX/MagicVFX/Projectiles/NS_Fireball"),
    ("cascade", "/Game/FlexibleCombatSystem/VFX/MeleeVFX/P_BlockImpact"),
]
EXPORTERS = [("ObjectExporterT3D", lambda: unreal.ObjectExporterT3D()),
             ("TextBufferExporterTXT", lambda: unreal.TextBufferExporterTXT()),
             ("VectorFieldExporter", lambda: unreal.VectorFieldExporter())]

# 只设置确实存在的属性
def build_task(a, exp, fn):
    task = unreal.AssetExportTask()
    task.set_editor_property("object", a)
    task.set_editor_property("exporter", exp)
    task.set_editor_property("filename", fn)
    for p, v in (("prompt", False), ("replace_identical", True),
                 ("automated_export", True), ("show_dialog", False),
                 ("use_file_archive", False), ("write_empty_files", False)):
        try: task.set_editor_property(p, v)
        except Exception: pass
    return task

for ename, mk in EXPORTERS:
    exp = safe(mk)
    L("")
    L("### %s -> %s" % (ename, d(exp,120)))
    if isinstance(exp,str): continue
    L("    supported_class = %s" % d(safe(exp.get_editor_property,"supported_class"),200))
    L("    format_extension = %s" % d(safe(exp.get_editor_property,"format_extension"),100))
    L("    format_description = %s" % d(safe(exp.get_editor_property,"format_description"),100))
    for tag,path in TARGETS:
        a = safe(unreal.load_asset, path)
        if isinstance(a,str):
            L("  [%s] load fail" % tag); continue
        ext = safe(exp.get_editor_property,"format_extension")
        ext = ext if isinstance(ext,str) and ext else "txt"
        fn = os.path.join(OUT, "t3d_%s_%s.%s" % (tag, ename, ext))
        try:
            task = build_task(a, exp, fn)
            r = safe(unreal.Exporter.run_asset_export_task, task)
            ex = os.path.exists(fn)
            L("  [%s] run=%s exists=%s size=%s" % (tag, d(r,120), ex,
              os.path.getsize(fn) if ex else -1))
            if ex and os.path.getsize(fn) > 0:
                head = open(fn,"r",encoding="utf-8",errors="replace").read(2000)
                L("     HEAD>>>")
                for ln in head.splitlines()[:40]:
                    L("     | "+ln[:180])
                L("     <<<")
        except Exception as e:
            L("  [%s] EXC %s %s" % (tag, type(e).__name__, e))

# 也试试 exporter.export_task(asset, filename)
L("")
L("### exporter.export_task(asset, filename) 直调")
exp = safe(lambda: unreal.ObjectExporterT3D())
if not isinstance(exp,str):
    for tag,path in TARGETS:
        a = safe(unreal.load_asset, path)
        fn = os.path.join(OUT, "direct_%s.t3d" % tag)
        r = safe(exp.export_task, a, fn)
        ex = os.path.exists(fn)
        L("  [%s] -> %s exists=%s size=%s" % (tag, d(r,150), ex, os.path.getsize(fn) if ex else -1))
        r2 = safe(exp.script_run_asset_export_task, a, fn)
        L("  [%s] script_run -> %s" % (tag, d(r2,150)))
    L("  text prop = %s" % d(safe(exp.get_editor_property,"text"), 600))

_f.close(); print("PROBE4 DONE")
