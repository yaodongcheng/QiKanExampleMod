import unreal, os

# --- 路径基准：自动向上查找含 pipeline/ 与 input/ 的项目根 ---
import os as _os
def _project_root(p):
    for _ in range(6):
        if _os.path.isdir(_os.path.join(p, "pipeline")) and _os.path.isdir(_os.path.join(p, "input")):
            return p
        p = _os.path.dirname(p)
    return _os.path.dirname(p)
PROJECT_ROOT = _project_root(_os.path.dirname(_os.path.abspath(__file__)))

def T(m):
    try: unreal.log(str(m))
    except: pass
d=os.path.join(PROJECT_ROOT, "input/source/ue_mannequin/rig")
os.makedirs(d, exist_ok=True)
mesh_path="/Game/Mannequin/Character/Mesh/SK_Mannequin"
mesh=unreal.load_asset(mesh_path)
T("mesh=%s" % (mesh is not None))
if mesh:
    task=unreal.AssetExportTask()
    task.set_editor_property("object", mesh)
    task.set_editor_property("exporter", unreal.SkeletalMeshExporterFBX())
    task.set_editor_property("filename", os.path.join(d,"Mannequin_src.fbx"))
    task.set_editor_property("automated", True)
    opt=unreal.FbxExportOption()
    for a,v in (("export_animations",True),("bake_anim",True),
                ("anim_use_default_framerate",False),("anim_frame_rate",30),
                ("export_morph_targets",False),("export_materials",False)):
        try: opt.set_editor_property(a,v)
        except Exception as e: T("  opts %s: %s"%(a,str(e)[:40]))
    task.set_editor_property("options", opt)
    try:
        r=unreal.Exporter.run_asset_export_task(task)
        T("export result=%s" % r)
    except Exception as e:
        T("export err: %s" % str(e))
T("DONE")
