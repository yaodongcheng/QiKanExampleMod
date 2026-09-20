# -*- coding: utf-8 -*-
"""
ue_export_slim.py  (v1 - 精确清单导出)
用途：按「精简清单」精确导出 AnimSequence -> FBX，不做子串匹配，避免多带。
清单来源：BM_SLIM_LIST (JSON，map: {"<asset package path>": {...}})，默认 anim_inventory/export_slim_list.json
输出目录：BM_OUT_DIR，默认 .../UEAnims/exported_slim
日志：<OUT_DIR>/export_trace_slim.log
"""
import unreal
import os
import json


# --- 路径基准：自动向上查找含 pipeline/ 与 input/ 的项目根 ---
import os as _os
def _project_root(p):
    for _ in range(6):
        if _os.path.isdir(_os.path.join(p, "pipeline")) and _os.path.isdir(_os.path.join(p, "input")):
            return p
        p = _os.path.dirname(p)
    return _os.path.dirname(p)
PROJECT_ROOT = _project_root(_os.path.dirname(_os.path.abspath(__file__)))

ROOT_DEFAULT = os.path.join(PROJECT_ROOT, "input/source/ue_mannequin")
TRACE = None

def get_env(n, d=""):
    v = os.environ.get(n)
    return v if v else d

def get_out_dir():
    return get_env("BM_OUT_DIR", os.path.join(ROOT_DEFAULT, "exported_slim"))

def get_list_path():
    return get_env("BM_SLIM_LIST", os.path.join(ROOT_DEFAULT, "anim_inventory", "export_slim_list.json"))

def T(msg):
    global TRACE
    try:
        if TRACE is None:
            d = get_out_dir()
            os.makedirs(d, exist_ok=True)
            TRACE = open(os.path.join(d, "export_trace_slim.log"), "w", encoding="utf-8")
        TRACE.write(str(msg) + "\n")
        TRACE.flush()
    except Exception:
        pass
    try:
        unreal.log(str(msg))
    except Exception:
        pass

def pkg_of(a):
    for attr in ("package_name", "object_path", "package_name_str"):
        if hasattr(a, attr):
            try:
                return str(getattr(a, attr))
            except Exception:
                pass
    return str(a)

def cls_of(a):
    for attr in ("asset_class", "asset_class_name", "asset_class_path"):
        if hasattr(a, attr):
            try:
                return str(getattr(a, attr))
            except Exception:
                pass
    return ""

def export_one(path, out_dir):
    try:
        asset = unreal.load_asset(path)
        if asset is None:
            T("  load_asset 失败: " + path)
            return False
        name = path.split("/")[-1]
        out_fbx = os.path.join(out_dir, name + ".fbx")
        task = unreal.AssetExportTask()
        task.set_editor_property("object", asset)
        task.set_editor_property("exporter", unreal.AnimSequenceExporterFBX())
        task.set_editor_property("filename", out_fbx)
        opt = unreal.AnimSeqExportOption()
        for attr, val in (("export_transforms", True),
                          ("export_morph_targets", False),
                          ("export_material_curves", False),
                          ("export_attribute_curves", False),
                          ("evaluate_all_skeletal_mesh_components", True),
                          ("record_in_world_space", False)):
            try:
                opt.set_editor_property(attr, val)
            except Exception:
                pass
        task.set_editor_property("options", opt)
        ok = unreal.Exporter.run_asset_export_task(task)
        if ok:
            T("  OK %s" % name)
            return True
        T("  导出返回False: " + path)
        return False
    except Exception as e:
        T("  异常 %s : %s" % (path, str(e)))
        return False

def main():
    out_dir = get_out_dir()
    os.makedirs(out_dir, exist_ok=True)
    lp = get_list_path()
    targets = json.load(open(lp, encoding="utf-8"))
    tset = set(targets.keys())
    T("=== 精确清单导出 开始 ===")
    T("引擎: %s" % unreal.SystemLibrary.get_engine_version())
    T("清单: %s (%d 条)" % (lp, len(tset)))
    T("输出: %s" % out_dir)

    reg = unreal.AssetRegistryHelpers.get_asset_registry()
    all_assets = reg.get_all_assets()
    T("registry 资产总数: %d" % len(all_assets))

    anim_pkgs = {}
    for a in all_assets:
        if "AnimSequence" not in cls_of(a):
            continue
        p = pkg_of(a)
        # 去掉可能的 ".AssetName" 尾巴，只留包路径
        if "." in p.split("/")[-1]:
            p = p.rsplit(".", 1)[0]
        anim_pkgs.setdefault(p, 0)
        anim_pkgs[p] += 1
    T("AnimSequence 包数: %d" % len(anim_pkgs))

    hit = [p for p in tset if p in anim_pkgs]
    miss = sorted(tset - set(hit))
    T("精确命中 %d / %d" % (len(hit), len(tset)))

    # 未命中的做模糊定位（同名不同目录 / 命名差异），只报告不改写
    if miss:
        T("--- 未精确命中 %d 条，尝试尾部同名模糊定位 ---" % len(miss))
        byname = {}
        for p in anim_pkgs:
            byname.setdefault(p.split("/")[-1], []).append(p)
        recovered = []
        for p in miss:
            n = p.split("/")[-1]
            cand = byname.get(n, [])
            if len(cand) == 1:
                T("  模糊命中 %s -> %s" % (p, cand[0]))
                recovered.append(cand[0])
            elif len(cand) > 1:
                T("  同名歧义 %s -> %s" % (p, cand))
            else:
                T("  找不到 %s" % p)
        hit = hit + recovered

    ok = 0
    fail = 0
    for i, p in enumerate(sorted(hit), 1):
        T("[%3d/%d] %s" % (i, len(hit), p))
        if export_one(p, out_dir):
            ok += 1
        else:
            fail += 1
    T("=== 完成: 成功 %d / 失败 %d / 清单 %d ===" % (ok, fail, len(tset)))

main()
