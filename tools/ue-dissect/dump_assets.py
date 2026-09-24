# -*- coding: utf-8 -*-
"""
FCS (Flexible Combat System, UE4.27) 全量资产导出 —— 在 UE4Editor-Cmd 的 PythonScript 里跑。
用法（PowerShell）:
  UE4Editor-Cmd.exe <uproject> -run=PythonScript -script="exec(open(r'<本文件>', encoding='utf-8-sig').read())" -unattended -nosplash -nullrhi -nopause -stdout -log
产出：<OUT>/ 下按阶段分文件（每资产一个 json，逐资产落盘，中途崩溃不丢已得）。
"""
import unreal, json, os, traceback, math

from paths import DUMP_ROOT as OUT
LOG = os.path.join(OUT, "_progress.log")


def log(msg):
    try:
        with open(LOG, "a", encoding="utf-8") as f:
            f.write(str(msg) + "\n")
    except Exception:
        pass


def mkdir(p):
    try:
        os.makedirs(p)
    except Exception:
        pass


def wjson(rel, obj):
    p = os.path.join(OUT, rel)
    mkdir(os.path.dirname(p))
    try:
        with open(p, "w", encoding="utf-8") as f:
            json.dump(obj, f, indent=1, ensure_ascii=False, default=str)
        return True
    except Exception as e:
        log("WJSON_FAIL %s %r" % (rel, e))
        return False


# ---------------------------------------------------------------- 通用反射序列化
MAXDEPTH = 6
ARRCAP = 64
SKIP_ATTR = set("""outer world level package class meta_data  property_linker
                    garbage_collection_frame  script_bytecode""".split())


def _f(v):
    if isinstance(v, float):
        if math.isnan(v) or math.isinf(v):
            return str(v)
    return v


def is_reflected(obj):
    return isinstance(obj, (unreal.Object, unreal.StructBase))


def ser(obj, depth=0, seen=None):
    """把一个反射对象/值序列化成 JSON 友好结构。"""
    if seen is None:
        seen = set()
    if depth > MAXDEPTH:
        return "<depth>"
    if obj is None or isinstance(obj, (bool, int, str)):
        return obj
    if isinstance(obj, float):
        return _f(obj)
    if isinstance(obj, (unreal.Name, unreal.Text, unreal.StringLibrary.__class__)):
        return str(obj)
    try:
        if isinstance(obj, unreal.EnumBase):
            return "%s::%s" % (type(obj).__name__, str(obj))
    except Exception:
        pass
    try:
        if isinstance(obj, unreal.Class):
            return "<class %s>" % obj.get_name()
    except Exception:
        pass
    try:
        if isinstance(obj, unreal.Object):
            try:
                return "<obj %s : %s>" % (obj.get_path_name(), obj.get_class().get_name())
            except Exception:
                return "<obj ?>"
    except Exception:
        pass
    if isinstance(obj, (list, tuple, set)):
        out = []
        for i, x in enumerate(obj):
            if i >= ARRCAP:
                out.append("<... %d more>" % (len(obj) - ARRCAP))
                break
            out.append(ser(x, depth + 1, seen))
        return out
    if isinstance(obj, dict):
        out = {}
        for i, (k, v) in enumerate(obj.items()):
            if i >= ARRCAP:
                break
            out[str(k)] = ser(v, depth + 1, seen)
        return out
    if isinstance(obj, unreal.StructBase):
        key = id(obj)
        if key in seen:
            return "<cycle>"
        seen.add(key)
        out = {"__struct__": type(obj).__name__}
        try:
            names = [n for n in dir(obj) if not n.startswith("_")]
        except Exception:
            names = []
        for n in names:
            if n in SKIP_ATTR:
                continue
            try:
                v = getattr(obj, n)
            except Exception:
                try:
                    v = obj.get_editor_property(n)
                except Exception:
                    continue
            if callable(v):
                continue
            out[n] = ser(v, depth + 1, seen)
        seen.discard(key)
        return out
    if is_reflected(obj):
        key = id(obj)
        if key in seen:
            return "<cycle>"
        seen.add(key)
        out = {"__class__": type(obj).__name__}
        try:
            names = [n for n in dir(obj) if not n.startswith("_")]
        except Exception:
            names = []
        for n in names:
            if n in SKIP_ATTR:
                continue
            v = None
            got = False
            try:
                v = obj.get_editor_property(n)
                got = True
            except Exception:
                try:
                    v = getattr(obj, n)
                    got = True
                except Exception:
                    got = False
            if not got or callable(v):
                continue
            out[n] = ser(v, depth + 1, seen)
        seen.discard(key)
        return out
    return str(obj)


def gp(obj, *names):
    """按多个候选名取属性（编辑器属性名 / python 蛇形名）。"""
    for n in names:
        for accessor in (lambda o, k: o.get_editor_property(k), lambda o, k: getattr(o, k)):
            try:
                v = accessor(obj, n)
                if v is not None:
                    return v
            except Exception:
                continue
    return None


def load(path):
    """注意：本工程没启用 EditorScriptingUtilities 插件，unreal.EditorAssetLibrary 不存在。"""
    try:
        a = unreal.load_asset(path)
        if a is not None:
            return a
    except Exception as e:
        log("LOAD_FAIL1 %s %r" % (path, e))
    try:
        a = unreal.load_asset(None, path)
        if a is not None:
            return a
    except Exception as e:
        log("LOAD_FAIL %s %r" % (path, e))
    return None


# ---------------------------------------------------------------- 清单
def phase_inventory():
    reg = unreal.AssetRegistryHelpers.get_asset_registry()
    assets = reg.get_assets_by_path("/Game", recursive=True)
    rows = []
    for a in assets:
        try:
            rows.append({
                "path": str(a.package_name),
                "name": str(a.asset_name),
                "class": str(a.asset_class),
            })
        except Exception as e:
            rows.append({"err": repr(e)})
    wjson("01_inventory.json", rows)
    log("PHASE inventory %d" % len(rows))
    # 依赖图（只对 FCS 目录下的资产，量大）
    deps = {}
    for r in rows:
        p = r.get("path")
        if not p or "/Game/FlexibleCombatSystem" not in p:
            continue
        try:
            d = reg.get_dependencies(p)
            deps[p] = ser(d, 0)
        except Exception as e:
            deps[p] = {"err": repr(e)}
    wjson("01_deps.json", deps)
    log("PHASE deps %d" % len(deps))
    return rows


# ---------------------------------------------------------------- 枚举 / 结构体
def phase_enums(rows):
    out = {}
    for r in rows:
        if r.get("class") != "UserDefinedEnum":
            continue
        a = load(r["path"])
        if a is None:
            continue
        d = {"path": r["path"]}
        d["enumerators"] = ser(gp(a, "enumerators", "Enumerators"))
        d["display_name_map"] = ser(gp(a, "display_name_map", "DisplayNameMap"))
        d["descriptions"] = ser(gp(a, "enumerator_descriptions", "EnumeratorDescriptions"))
        d["cooked"] = ser(gp(a, "cooked_display_names", "CookedDisplayNameMap"))
        out[r["name"]] = d
    wjson("02_enums.json", out)
    log("PHASE enums %d" % len(out))


def phase_structs(rows):
    out = {}
    for r in rows:
        if r.get("class") != "UserDefinedStruct":
            continue
        a = load(r["path"])
        if a is None:
            continue
        d = {"path": r["path"]}
        for k in ("struct_flags", "status", "guid", "editor_data", "display_name",
                  "references", "properties", "child_properties", "super_struct"):
            v = gp(a, k)
            if v is not None:
                d[k] = ser(v, 1)
        out[r["name"]] = d
    wjson("02_structs.json", out)
    log("PHASE structs %d" % len(out))


# ---------------------------------------------------------------- 数据表（全行）
def phase_datatables(rows):
    stats = {}
    for r in rows:
        if r.get("class") != "DataTable":
            continue
        a = load(r["path"])
        if a is None:
            stats[r["name"]] = "LOAD_FAIL"
            continue
        d = {"path": r["path"]}
        rs = gp(a, "row_struct", "RowStruct")
        d["row_struct"] = rs.get_name() if rs else None
        try:
            d["row_names"] = [str(x) for x in unreal.DataTableFunctionLibrary.get_data_table_row_names(a)]
        except Exception as e:
            d["row_names"] = []
            d["row_names_err"] = repr(e)
        # 方式一：CSV 导出
        csv = os.path.join(OUT, "03_datatables", r["name"] + ".csv")
        mkdir(os.path.dirname(csv))
        ok = False
        try:
            task = unreal.AssetExportTask()
            task.set_editor_property("object", a)
            task.set_editor_property("filename", csv)
            task.set_editor_property("automated", True)
            task.set_editor_property("prompt", False)
            task.set_editor_property("replace_identical", True)
            ok = bool(unreal.Exporter.run_asset_export_task(task))
        except Exception as e:
            d["csv_err"] = repr(e)
        d["csv_ok"] = ok
        if not ok:
            # 方式二：JSON 导出
            try:
                task = unreal.AssetExportTask()
                task.set_editor_property("object", a)
                task.set_editor_property("filename", os.path.join(OUT, "03_datatables", r["name"] + ".json"))
                task.set_editor_property("automated", True)
                task.set_editor_property("prompt", False)
                task.set_editor_property("replace_identical", True)
                d["json_ok"] = bool(unreal.Exporter.run_asset_export_task(task))
            except Exception as e:
                d["json_err"] = repr(e)
        wjson("03_datatables/_meta/%s.json" % r["name"], d)
        stats[r["name"]] = {"rows": len(d.get("row_names") or []), "csv": ok, "struct": d["row_struct"]}
    wjson("03_datatables/_stats.json", stats)
    log("PHASE datatables %d" % len(stats))


# ---------------------------------------------------------------- 蓝图
def bp_var_dump(bp):
    vs = gp(bp, "new_variables", "NewVariables")
    out = []
    if vs is None:
        return None
    try:
        for v in vs:
            out.append({
                "name": str(gp(v, "var_name", "VarName") or ""),
                "type": ser(gp(v, "var_type", "VarType"), 1),
                "default": str(gp(v, "default_value", "DefaultValue") or ""),
                "category": ser(gp(v, "category", "Category")),
                "tooltip": ser(gp(v, "tool_tip", "ToolTip") or gp(v, "meta_data_array", "MetaDataArray")),
                "flags": ser(gp(v, "property_flags", "PropertyFlags")),
                "replication": ser(gp(v, "rep_notify_func", "RepNotifyFunc")),
            })
    except Exception as e:
        return {"err": repr(e)}
    return out


def bp_graph_names(bp):
    out = {}
    for key in ("function_graphs", "FunctionGraphs", "ubergraph_pages", "UbergraphPages",
                "macro_graphs", "MacroGraphs"):
        g = gp(bp, key)
        if g is None:
            continue
        try:
            out[key] = [str(x.get_name()) for x in g]
        except Exception as e:
            out[key] = "ERR " + repr(e)
    return out


def comp_tree(root, depth=0):
    if root is None or depth > 4:
        return None
    d = {"class": None, "name": None}
    try:
        d["class"] = root.get_class().get_name()
        d["name"] = root.get_name()
    except Exception:
        pass
    for k in ("relative_location", "relative_rotation", "relative_scale3d", "tags",
              "component_tags", "attach_socket_name", "is_editor_only", "mobility",
              "sphere_radius", "capsule_radius", "capsule_half_height", "box_extent",
              "static_mesh", "skeletal_mesh", "sprite", "material", "generate_overlap_events",
              "collision_profile_name", "collision_enabled", "simulate_physics",
              "auto_activate", "anim_class", "physics_asset"):
        v = gp(root, k)
        if v is not None:
            d[k] = ser(v, 1)
    kids = []
    try:
        for i in range(root.get_num_children()):
            c = comp_tree(root.get_child(i), depth + 1)
            if c:
                kids.append(c)
    except Exception:
        pass
    if kids:
        d["children"] = kids
    return d


def phase_blueprints(rows):
    idx = {}
    for r in rows:
        if r.get("class") not in ("Blueprint", "WidgetBlueprint", "AnimBlueprint"):
            continue
        a = load(r["path"])
        if a is None:
            continue
        d = {"path": r["path"], "kind": r["class"]}
        pc = gp(a, "parent_class", "ParentClass")
        d["parent_class"] = pc.get_name() if pc else None
        gc = None
        try:
            gc = a.generated_class()
            d["generated_class"] = gc.get_name()
        except Exception as e:
            d["generated_class_err"] = repr(e)
        d["blueprint_type"] = ser(gp(a, "blueprint_type", "BlueprintType"))
        d["variables"] = bp_var_dump(a)
        d["graphs"] = bp_graph_names(a)
        # 函数名清单（来自生成类反射）
        if gc is not None:
            try:
                fns = []
                for n in dir(gc):
                    if n.startswith("_"):
                        continue
                    try:
                        v = getattr(gc, n)
                    except Exception:
                        continue
                    if callable(v):
                        fns.append(n)
                d["functions"] = sorted(set(fns))
            except Exception as e:
                d["functions_err"] = repr(e)
            # CDO 默认值
            try:
                cdo = unreal.get_default_object(gc)
                d["cdo"] = ser(cdo, 0)
            except Exception as e:
                d["cdo_err"] = repr(e)
        idx[r["name"]] = d
    wjson("04_blueprints.json", idx)
    log("PHASE blueprints %d" % len(idx))


# ---------------------------------------------------------------- 动画
def phase_anims(rows):
    idx = {}
    for r in rows:
        if r.get("class") not in ("AnimMontage", "AnimSequence", "AnimComposite", "BlendSpace", "BlendSpace1D", "AimOffsetBlendSpace"):
            continue
        a = load(r["path"])
        if a is None:
            continue
        d = {"path": r["path"], "kind": r["class"]}
        d["length"] = ser(gp(a, "sequence_length", "SequenceLength", "play_length"))
        d["rate_scale"] = ser(gp(a, "rate_scale", "RateScale"))
        d["root_motion"] = ser(gp(a, "enable_root_motion", "bEnableRootMotion"))
        d["notifies"] = ser(gp(a, "notifies", "Notifies"), 2)
        d["notify_tracks"] = ser(gp(a, "notify_tracks", "NotifyTracks"), 2)
        d["sections"] = ser(gp(a, "composite_sections", "CompositeSections"), 2)
        d["slot_tracks"] = ser(gp(a, "slot_anim_tracks", "SlotAnimTracks"), 2)
        d["curves"] = ser(gp(a, "raw_curve_data", "RawCurveData"), 1)
        d["sync_group"] = ser(gp(a, "sync_group", "SyncGroup"))
        d["additive"] = ser(gp(a, "additive_anim_type", "AdditiveAnimType"))
        idx[r["name"]] = d
    wjson("05_animations.json", idx)
    log("PHASE anims %d" % len(idx))


# ---------------------------------------------------------------- 控件（UI）
def widget_dump(w, depth=0):
    if w is None or depth > 8:
        return None
    d = {}
    try:
        d["class"] = w.get_class().get_name()
        d["name"] = w.get_name()
        d["display"] = str(gp(w, "display_label", "DisplayLabel") or "")
    except Exception:
        pass
    for k in ("visibility", "is_variable", "slot", "render_opacity", "render_transform",
              "tool_tip_text", "b_is_enabled", "is_enabled"):
        v = gp(w, k)
        if v is not None:
            d[k] = ser(v, 1)
    # 文本/样式类信息（TextBlock 等）
    for k in ("text", "font", "color_and_opacity", "brush", "style", "percent",
              "checked_state", "selected_option", "min_desired_width"):
        try:
            v = w.get_editor_property(k)
            d["p_" + k] = ser(v, 1)
        except Exception:
            pass
    kids = []
    try:
        n = w.get_children_count()
        for i in range(n):
            c = widget_dump(w.get_child_at(i), depth + 1)
            if c:
                kids.append(c)
    except Exception:
        # 非 Panel 控件
        pass
    if kids:
        d["children"] = kids
    return d


def phase_widgets(rows):
    idx = {}
    for r in rows:
        if r.get("class") != "WidgetBlueprint":
            continue
        a = load(r["path"])
        if a is None:
            continue
        d = {"path": r["path"]}
        wt = gp(a, "widget_tree", "WidgetTree")
        if wt is not None:
            root = gp(wt, "root_widget", "RootWidget")
            d["root"] = widget_dump(root)
            try:
                d["all_widget_names"] = [str(x.get_name()) for x in gp(wt, "all_widgets", "AllWidgets") or []]
            except Exception:
                pass
        anims = gp(a, "animations", "Animations")
        if anims is not None:
            al = []
            try:
                for an in anims:
                    al.append({"name": str(gp(an, "get_name") or an.get_name()),
                               "bindings": ser(gp(an, "animation_bindings", "AnimationBindings"), 2),
                               "length": ser(gp(an, "display_label", "DisplayLabel"))})
            except Exception as e:
                al = [{"err": repr(e)}]
            d["animations"] = al
        idx[r["name"]] = d
    wjson("06_widgets.json", idx)
    log("PHASE widgets %d" % len(idx))


# ---------------------------------------------------------------- 粒子 / 声音 / 网格 / 相机
def phase_vfx(rows):
    idx = {}
    for r in rows:
        if r.get("class") not in ("NiagaraSystem", "NiagaraEmitter", "NiagaraScript", "ParticleSystem"):
            continue
        a = load(r["path"])
        if a is None:
            continue
        d = {"path": r["path"], "kind": r["class"]}
        for k in ("exposed_parameters", "emitter_handles", "warmup_time", "warmup_tick_count",
                  "system_spawn_constant", "b_fixed_bounds", "fixed_bounds", "effect_type",
                  "template", "emitter_name", "sim_target", "b_local_space",
                  "emitter_renderers", "renderer_properties", "graph_source", "script_usage",
                  "last_compile_status", "unique_id"):
            v = gp(a, k)
            if v is not None:
                d[k] = ser(v, 2)
        idx[r["name"]] = d
    wjson("07_vfx.json", idx)
    log("PHASE vfx %d" % len(idx))


def phase_sounds(rows):
    idx = {}
    for r in rows:
        if r.get("class") not in ("SoundCue", "SoundWave", "SoundClass", "SoundMix",
                                  "SoundAttenuation", "MetaSoundSource", "ReverbEffect"):
            continue
        a = load(r["path"])
        if a is None:
            continue
        idx[r["name"]] = {"path": r["path"], "kind": r["class"], "dump": ser(a, 0)}
    wjson("08_sounds.json", idx)
    log("PHASE sounds %d" % len(idx))


def phase_meshes(rows):
    idx = {}
    for r in rows:
        if r.get("class") not in ("SkeletalMesh", "StaticMesh"):
            continue
        a = load(r["path"])
        if a is None:
            continue
        d = {"path": r["path"], "kind": r["class"]}
        d["sockets"] = ser(gp(a, "sockets", "Sockets"), 2)
        d["materials"] = ser(gp(a, "materials", "Materials"), 2)
        d["physics_asset"] = ser(gp(a, "physics_asset", "PhysicsAsset"))
        d["skeleton"] = ser(gp(a, "skeleton", "Skeleton"))
        d["bounds"] = ser(gp(a, "extended_bounds", "ExtendedBounds"))
        idx[r["name"]] = d
    wjson("09_meshes.json", idx)
    log("PHASE meshes %d" % len(idx))


def phase_materials(rows):
    idx = {}
    for r in rows:
        if r.get("class") not in ("Material", "MaterialInstanceConstant", "MaterialInstance",
                                  "MaterialFunction", "MaterialParameterCollection"):
            continue
        a = load(r["path"])
        if a is None:
            continue
        d = {"path": r["path"], "kind": r["class"]}
        for k in ("parent", "blend_mode", "shading_model", "two_sided", "scalar_parameter_values",
                  "vector_parameter_values", "texture_parameter_values", "static_parameters",
                  "used_with_niagara_sprites", "used_with_niagara_ribbons", "used_with_skeletal_mesh",
                  "used_with_particle_sprites", "used_with_static_lighting"):
            v = gp(a, k)
            if v is not None:
                d[k] = ser(v, 2)
        idx[r["name"]] = d
    wjson("10_materials.json", idx)
    log("PHASE materials %d" % len(idx))


def phase_misc(rows):
    idx = {}
    for r in rows:
        if r.get("class") not in ("CameraShake", "MatineeCameraShake", "CameraShakeBase",
                                  "CurveFloat", "CurveVector", "CurveLinearColor", "CurveTable",
                                  "DataAsset", "PrimaryDataAsset", "UserDefinedStruct",
                                  "PhysicsAsset", "Skeleton", "BlueprintGeneratedClass",
                                  "AnimBlueprintGeneratedClass", "NiagaraParameterCollection"):
            continue
        a = load(r["path"])
        if a is None:
            continue
        idx[r["name"]] = {"path": r["path"], "kind": r["class"], "dump": ser(a, 0)}
    wjson("11_misc.json", idx)
    log("PHASE misc %d" % len(idx))


# ---------------------------------------------------------------- 主
def main():
    mkdir(OUT)
    log("=== START ===")
    rows = []
    for name, fn in (("inventory", phase_inventory),):
        try:
            rows = fn()
        except Exception:
            log("PHASE_ERR %s\n%s" % (name, traceback.format_exc()))
    if not rows:
        try:
            rows = json.load(open(os.path.join(OUT, "01_inventory.json"), encoding="utf-8"))
        except Exception:
            rows = []
    only = os.environ.get("FCS_PHASES", "")
    phases = [
        ("enums", lambda: phase_enums(rows)),
        ("structs", lambda: phase_structs(rows)),
        ("datatables", lambda: phase_datatables(rows)),
        ("blueprints", lambda: phase_blueprints(rows)),
        ("anims", lambda: phase_anims(rows)),
        ("widgets", lambda: phase_widgets(rows)),
        ("vfx", lambda: phase_vfx(rows)),
        ("sounds", lambda: phase_sounds(rows)),
        ("meshes", lambda: phase_meshes(rows)),
        ("materials", lambda: phase_materials(rows)),
        ("misc", lambda: phase_misc(rows)),
    ]
    for name, fn in phases:
        if only and name not in only.split(","):
            continue
        try:
            fn()
        except Exception:
            log("PHASE_ERR %s\n%s" % (name, traceback.format_exc()))
    log("=== DONE ===")
    print("FCS_DUMP_DONE")


main()
