# -*- coding: utf-8 -*-
"""probe_v3.py -- T3D 文本导出实验：能否把 Niagara/Cascade 的完整图 dump 成文本。"""
import unreal, os, traceback

OUT = os.environ.get("BM_PART_OUT", r"D:/BrainMaker/骑砍2粒子特效复刻/output/probe")
os.makedirs(OUT, exist_ok=True)
_f = open(os.path.join(OUT, "vfx_api_probe3.log"), "w", encoding="utf-8")
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

TARGETS = [
    ("niagara", "/Game/FlexibleCombatSystem/VFX/MagicVFX/Projectiles/NS_Fireball"),
    ("cascade", "/Game/FlexibleCombatSystem/VFX/MeleeVFX/P_BlockImpact"),
]

def introspect_exporters():
    S("导出器类 API")
    for n in ("Exporter","ObjectExporterT3D","PolysExporterT3D","TextBufferExporterTXT",
              "VectorFieldExporter","SequenceExporterT3D"):
        c = getattr(unreal, n, None)
        L("%-24s exists=%s" % (n, c is not None))
        if c is not None:
            L("   dir = %s" % props(c, 60))

def try_export(tag, path, exp, ext):
    a = safe(unreal.load_asset, path)
    if isinstance(a,str):
        L("  [%s/%s] load fail %s" % (tag, ext, d(a,150))); return
    fn = os.path.join(OUT, "%s__%s.%s" % (tag, type(exp).__name__, ext))
    L("  [%s/%s] asset=%s exporter=%s" % (tag, ext, type(a).__name__, type(exp).__name__))
    try:
        task = unreal.AssetExportTask()
        task.set_editor_property("object", a)
        task.set_editor_property("exporter", exp)
        task.set_editor_property("filename", fn)
        task.set_editor_property("automated_export", True)
        task.set_editor_property("replace_identical", True)
        task.set_editor_property("prompt", False)
        r = safe(unreal.Exporter.run_asset_export_task, task)
        ex = os.path.exists(fn)
        L("     run -> %s | exists=%s size=%s" % (d(r,150), ex, os.path.getsize(fn) if ex else -1))
        if ex and os.path.getsize(fn) > 0:
            head = open(fn, "r", encoding="utf-8", errors="replace").read(1200)
            L("     HEAD >>>\n" + head.replace("\n", "\n     | "))
            L("     <<< HEAD END")
    except Exception as e:
        L("     EXC %s %s" % (type(e).__name__, e))

def run_t3d():
    S("T3D 导出尝试")
    makers = [
        ("ObjectExporterT3D", lambda: unreal.ObjectExporterT3D()),
        ("PolysExporterT3D", lambda: unreal.PolysExporterT3D()),
        ("TextBufferExporterTXT", lambda: unreal.TextBufferExporterTXT()),
    ]
    for mk_name, mk in makers:
        exp = safe(mk)
        L("")
        L("### 构造 %s -> %s" % (mk_name, d(exp,150)))
        if isinstance(exp,str): continue
        for tag,path in TARGETS:
            ext = "t3d" if "T3D" in mk_name else "txt"
            try_export(tag, path, exp, ext)

def run_export_to_string():
    S("直接调用 exporter 的 export_to_string / export_text")
    a = safe(unreal.load_asset, TARGETS[0][1])
    L("asset = %s" % d(a,120))
    if isinstance(a,str): return
    exp = safe(lambda: unreal.ObjectExporterT3D())
    if isinstance(exp,str): return
    for m in ("export_to_string","export_text","export_to_file","export_to_output_device"):
        if hasattr(exp, m):
            L("  has %s" % m)
            for args in ((), (a,)):
                r = safe(getattr(exp,m), *args)
                L("    %s%r -> %s" % (m, args, d(r, 900)))

def main():
    S("ENGINE"); L(unreal.SystemLibrary.get_engine_version())
    try: introspect_exporters()
    except Exception: L("intro FATAL\n"+traceback.format_exc())
    try: run_t3d()
    except Exception: L("t3d FATAL\n"+traceback.format_exc())
    try: run_export_to_string()
    except Exception: L("str FATAL\n"+traceback.format_exc())
    S("DONE"); _f.close(); print("PROBE3 DONE")

try: main()
except Exception:
    L("FATAL\n"+traceback.format_exc()); _f.close(); raise
