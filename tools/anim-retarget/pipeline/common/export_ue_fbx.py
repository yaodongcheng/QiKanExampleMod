# -*- coding: utf-8 -*-
"""
ue_export_fbx.py  (v3 - 带文件日志追踪)
用途：在 UE5 编辑器命令环境下，把项目内 AnimSequence 资产批量导出为 FBX。
用法：
    UnrealEditor.exe <uproject> -run=pythonscript -script=".../ue_export_fbx.py" ...
日志写入 <OUT_DIR>/export_trace.log，避免 stdout 被引擎过滤。
"""
import unreal
import os


# --- 路径基准：自动向上查找含 pipeline/ 与 input/ 的项目根 ---
import os as _os
def _project_root(p):
    for _ in range(6):
        if _os.path.isdir(_os.path.join(p, "pipeline")) and _os.path.isdir(_os.path.join(p, "input")):
            return p
        p = _os.path.dirname(p)
    return _os.path.dirname(p)
PROJECT_ROOT = _project_root(_os.path.dirname(_os.path.abspath(__file__)))

TRACE = None

def T(msg):
    """写 trace 日志文件 + unreal.log 双通道。"""
    global TRACE
    try:
        if TRACE is None:
            d = get_out_dir()
            os.makedirs(d, exist_ok=True)
            TRACE = open(os.path.join(d, "export_trace.log"), "w", encoding="utf-8")
        TRACE.write(str(msg) + "\n")
        TRACE.flush()
    except Exception:
        pass
    try:
        unreal.log(str(msg))
    except Exception:
        pass

def get_env(name, default=""):
    v = os.environ.get(name)
    return v if v is not None else default

def get_out_dir():
    return get_env("BM_OUT_DIR", os.path.join(PROJECT_ROOT, "input/source/ue_mannequin/clips_basic"))

def get_filter():
    return get_env("BM_ANIM_FILTER", "")

def get_max_n():
    try:
        return int(get_env("BM_MAX_N", "0") or "0")
    except Exception:
        return 0

def get_pkg_name(a):
    """跨版本取包名。"""
    for attr in ("package_name", "object_path", "package_name_str"):
        if hasattr(a, attr):
            v = getattr(a, attr)
            try:
                return str(v)
            except Exception:
                return str(v)
    return str(a)

def get_class_name(a):
    """跨版本取类名。
    UE4: asset_class 是字符串（如 'AnimSequence'）
    UE5: asset_class 是 TopLevelAssetPath（需取 .asset_name，str() 会带 /Script/Engine. 前缀）
    """
    for attr in ("asset_class", "asset_class_name", "asset_class_path"):
        if hasattr(a, attr):
            try:
                v = getattr(a, attr)
                n = getattr(v, "asset_name", None)      # UE5 TopLevelAssetPath
                if n:
                    return str(n)
                sv = str(v)
                if sv and sv != "None":
                    # 兜底：/Script/Engine.AnimSequence -> AnimSequence
                    return sv.rsplit(".", 1)[-1] if "." in sv else sv
            except Exception:
                pass
    return ""

def list_anim_assets():
    reg = unreal.AssetRegistryHelpers.get_asset_registry()
    # !!! UE5（尤其 5.3）在 -run=pythonscript 命令行下，AssetRegistry 可能尚未完成扫描，
    #     get_all_assets() 会返回 0 —— 实测 mySekiro(UE5.3) 扫到 0 个 AnimSequence。
    #     先同步扫描 /Game 并等待完成。
    try:
        reg.scan_paths_synchronous(["/Game"], True, True)
        reg.wait_for_completion()
        T("AssetRegistry 同步扫描完成")
    except Exception as e:
        T("AssetRegistry 扫描告警: %s" % e)
    out = []
    all_assets = reg.get_all_assets()
    T("get_all_assets 总数: %d" % len(all_assets))
    try:
        if all_assets:
            _a = all_assets[0]
            T("诊断-首资产 raw.asset_class=%r  get_class_name=%r" % (getattr(_a, "asset_class", None), get_class_name(_a)))
            _seen = {}
            for _x in all_assets[:400]:
                _k = get_class_name(_x)
                _seen[_k] = _seen.get(_k, 0) + 1
            T("诊断-前400个类名分布: %s" % sorted(_seen.items(), key=lambda z: -z[1])[:6])
    except Exception as _e:
        T("诊断失败: %s" % _e)
    for a in all_assets:
        cls = get_class_name(a)
        if "AnimSequence" in cls:
            out.append(a)
    return out

def export_one(asset_path_str, out_dir):
    try:
        asset = unreal.load_asset(asset_path_str)
        if asset is None:
            T("load_asset 失败: " + asset_path_str)
            return False, asset_path_str
        # !!! 保留【完整相对路径】作为输出路径，彻底避免同名覆盖：
        #     - GhostSamurai 同一资产名在 Inplace/Root/Sample 下各一份（69 条曾只剩 23）
        #     - mySekiro 工程深目录下大量同名（2874 条曾只剩 779）
        #     用 /Game/ 之后的部分原样建子目录。
        _rel = asset_path_str
        for _pre in ("/Game/", "/game/"):
            if _rel.startswith(_pre):
                _rel = _rel[len(_pre):]
                break
        _rel = _rel.replace("\\", "/").replace("/", os.sep)
        out_fbx = os.path.join(out_dir, _rel + ".fbx")
        _od = os.path.dirname(out_fbx)
        if _od:
            os.makedirs(_od, exist_ok=True)
        export_task = unreal.AssetExportTask()
        export_task.set_editor_property("object", asset)
        # UE5.0 动画序列专用导出器
        export_task.set_editor_property("exporter", unreal.AnimSequenceExporterFBX())
        export_task.set_editor_property("filename", out_fbx)
        # 动画序列导出选项（属性名 options，不是 export_options）
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
        export_task.set_editor_property("options", opt)
        result = unreal.Exporter.run_asset_export_task(export_task)
        if result:
            T("OK 导出: %s -> %s" % (asset_path_str, out_fbx))
            return True, out_fbx
        else:
            T("导出失败(返回False): " + asset_path_str)
            return False, asset_path_str
    except Exception as e:
        T("导出异常 %s: %s" % (asset_path_str, str(e)))
        return False, asset_path_str

def main():
    out_dir = get_out_dir()
    os.makedirs(out_dir, exist_ok=True)
    flt = get_filter()
    max_n = get_max_n()
    T("=== UE FBX Export 开始 ===")
    T("输出目录: " + out_dir)
    T("过滤器: '%s'  (空=全部)   MAX_N=%s" % (flt, max_n))
    T("引擎版本: %s" % unreal.SystemLibrary.get_engine_version())
    assets = list_anim_assets()
    T("扫描到 AnimSequence 资产: %d" % len(assets))
    if not assets:
        T("!! 未扫描到动画")
        return
    ok = 0; fail = 0; skipped = 0
    for a in assets:
        asset_path_str = get_pkg_name(a)
        if flt and not any(t.strip() in asset_path_str for t in flt.split(";")):
            skipped += 1
            continue
        if max_n and ok >= max_n:
            break
        success, info = export_one(asset_path_str, out_dir)
        if success:
            ok += 1
        else:
            fail += 1
    T("=== 完成: 成功 %d, 失败 %d, 跳过 %d ===" % (ok, fail, skipped))

main()
