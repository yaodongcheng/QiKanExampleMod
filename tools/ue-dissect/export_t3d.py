# -*- coding: utf-8 -*-
"""FCS 全量 T3D 导出（引擎自带 ObjectExporterT3D）—— 这是本次拆解的证据基座。

为什么用 T3D：UE Python 在 commandlet 里读不到蓝图变量/节点/通知（属性非 Edit 标志），
而 T3D 文本导出会把「带标签属性」全部写出来：节点 FunctionReference / Pin 默认值 / 连线、
CDO 全默认值、动画通知时间、控件树、材质参数…… 等于不用第三方工具就能拿到全量语义。

产出：out/t3d/<类别>/<资产名>.t3d  +  out/_t3d_manifest.json（成功/失败/字节数）
另做数据表取值（字段全名 → 逐格 get_data_table_column_as_string）→ out/13_dtcells.json
"""
import unreal, os, io, json

from paths import DUMP_ROOT as OUT
T3D = os.path.join(OUT, "t3d")
MAN = {}
LOG = os.path.join(OUT, "_t3d.log")


def log(s):
    with io.open(LOG, "a", encoding="utf-8") as f:
        f.write(str(s) + "\n")


def mk_exporter():
    try:
        return unreal.new_object(unreal.ObjectExporterT3D)
    except Exception as e:
        log("EXPORTER_FAIL %r" % e)
        return None


def t3d(obj, rel):
    path = os.path.join(T3D, rel)
    try:
        os.makedirs(os.path.dirname(path))
    except Exception:
        pass
    e = mk_exporter()
    if e is None or obj is None:
        return None
    t = unreal.AssetExportTask()
    try:
        t.set_editor_property("object", obj)
        t.set_editor_property("exporter", e)
        t.set_editor_property("filename", path)
        t.set_editor_property("automated", True)
        t.set_editor_property("prompt", False)
        t.set_editor_property("replace_identical", True)
        ok = bool(unreal.Exporter.run_asset_export_task(t))
    except Exception as ex:
        log("T3D_FAIL %s %r" % (rel, ex))
        return None
    if not ok:
        log("T3D_FALSE %s" % rel)
        return None
    try:
        return os.path.getsize(path)
    except Exception:
        return None


def load(path):
    try:
        return unreal.load_asset(path)
    except Exception as e:
        log("LOAD_FAIL %s %r" % (path, e))
        return None


def per(rows, classes, subdir, cdo=False, kind_field="class"):
    n = 0
    for r in rows:
        if r.get(kind_field) not in classes:
            continue
        a = load(r["path"])
        if a is None:
            MAN[r["name"]] = {"path": r["path"], "err": "load"}
            continue
        size = t3d(a, os.path.join(subdir, r["name"] + ".t3d"))
        rec = {"path": r["path"], "type": r.get("class"), "bytes": size}
        if cdo:
            gc = None
            try:
                gc = unreal.load_class(None, r["path"] + "." + r["name"] + "_C")
            except Exception as e:
                rec["cdo_err"] = repr(e)
            if gc is not None:
                try:
                    cdoobj = unreal.get_default_object(gc)
                    rec["cdo_bytes"] = t3d(cdoobj, os.path.join("cdo", r["name"] + "_CDO.t3d"))
                except Exception as e:
                    rec["cdo_err"] = repr(e)
        MAN[r["name"]] = rec
        n += 1
    log("PER %s %d" % (subdir, n))
    return n


def main():
    os.makedirs(T3D, exist_ok=True)
    rows = json.load(io.open(os.path.join(OUT, "01_inventory.json"), encoding="utf-8"))
    log("=== T3D START %d assets ===" % len(rows))

    per(rows, ("Blueprint", "WidgetBlueprint", "AnimBlueprint"), "bp", cdo=True)
    per(rows, ("DataTable",), "dt")
    per(rows, ("AnimMontage", "AnimSequence", "AnimComposite", "BlendSpace", "BlendSpace1D", "AimOffsetBlendSpace"), "anim")
    per(rows, ("UserDefinedStruct",), "struct")
    per(rows, ("UserDefinedEnum",), "enum")
    per(rows, ("NiagaraSystem", "NiagaraEmitter", "ParticleSystem"), "vfx")
    per(rows, ("SoundCue", "SoundWave", "SoundClass", "SoundAttenuation", "SoundMix", "MetaSoundSource"), "sound")
    per(rows, ("Material", "MaterialInstanceConstant", "MaterialInstance", "MaterialFunction", "MaterialParameterCollection"), "mat")
    per(rows, ("BehaviorTree", "BlackboardData", "EnvQuery", "DataAsset", "PrimaryDataAsset", "CurveFloat",
               "CurveVector", "CurveLinearColor", "CurveTable", "CameraShake", "MatineeCameraShake",
               "SkeletalMesh", "StaticMesh", "PhysicsAsset", "Skeleton", "Texture2D", "Font",
               "SlateBrushAsset", "ForceFeedbackEffect", "DialogueVoice", "DialogueWave"), "misc")
    per(rows, ("World", "Level"), "level")

    json.dump(MAN, io.open(os.path.join(OUT, "_t3d_manifest.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)

    # ---- 数据表取值 ----
    cols = json.load(io.open(os.path.join(OUT, "12_dt_columns.json"), encoding="utf-8"))
    RES = {}
    for tname, info in cols["tables"].items():
        dt = load(info["pkg"])
        if dt is None:
            continue
        try:
            rn = [str(x) for x in unreal.DataTableFunctionLibrary.get_data_table_row_names(dt)]
        except Exception as e:
            rn = []
            log("ROWNAME_FAIL %s %r" % (tname, e))
        cells = {}
        for full in info["full"]:
            got = None
            for key in (full, unreal.Name(full)):
                try:
                    res = unreal.DataTableFunctionLibrary.get_data_table_column_as_string(dt, key)
                    vals = [str(x) for x in res]
                    if any(v.strip() for v in vals):
                        got = vals
                        break
                except Exception:
                    pass
            if got:
                cells[full] = got
        RES[tname] = {"path": info["pkg"], "rows": rn, "cells": cells}
        log("DTCELLS %s rows=%d colhit=%d/%d" % (tname, len(rn), len(cells), len(info["full"])))
    json.dump(RES, io.open(os.path.join(OUT, "13_dtcells.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)
    log("=== T3D DONE ===")
    print("T3D_ALL_DONE")


main()
