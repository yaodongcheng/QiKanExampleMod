# -*- coding: utf-8 -*-
"""定向 T3D 导出 —— 只导指定资产（大工程里挑几个看，不跑全量库存）。

背景：export_t3d.py 依赖 01_inventory.json（全工程资产清单）——在大工程（真游戏工程）上
跑全量清单又慢又没必要。本脚本直接按路径列表导出，用于「只想拆某几个蓝图/动画/曲线」的场景。

用法（在 UE 里跑，不是普通 python）：
  $env:UE_TARGETS = "/Game/A;/Game/B"        # 分号分隔；也支持换行
  $env:UE_DUMP    = "...\\Debug\\offline\\xxx_dump\\out"   # 可选，默认 paths.DUMP_ROOT

  & UnrealEditor-Cmd.exe <uproject> -run=PythonScript `
      -script="exec(open(r'<本文件>', encoding='utf-8-sig').read())" `
      -unattended -nosplash -nullrhi -nopause -stdout -log

产出：<UE_DUMP>/t3d/<资产名>.t3d · 蓝图层另出 <UE_DUMP>/t3d/cdo/<资产名>_CDO.t3d
      + <UE_DUMP>/_targeted_manifest.json（成功/失败/字节数）
"""
import unreal, os, io, json

try:
    from paths import DUMP_ROOT as _DEF
except Exception:
    _DEF = os.path.join(os.getcwd(), "out")

OUT = os.environ.get("UE_DUMP", _DEF)
T3D = os.path.join(OUT, "t3d")
LOG = os.path.join(OUT, "_targeted.log")

BP_CLASSES = ("Blueprint", "WidgetBlueprint", "AnimBlueprint")


def log(s):
    os.makedirs(OUT, exist_ok=True)
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
    os.makedirs(os.path.dirname(path), exist_ok=True)
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


def main():
    raw = os.environ.get("UE_TARGETS", "")
    targets = [p.strip() for p in raw.replace("\r", "").replace("\n", ";").split(";") if p.strip()]
    log("=== TARGETED START %d ===" % len(targets))
    man = {}
    for p in targets:
        a = load(p)
        name = p.rstrip("/").rsplit("/", 1)[-1] or p
        if a is None:
            man[name] = {"path": p, "err": "load"}
            continue
        cls = ""
        try:
            cls = a.get_class().get_name()
        except Exception:
            pass
        rec = {"path": p, "type": cls, "bytes": t3d(a, name + ".t3d")}
        if cls in BP_CLASSES:
            gc = None
            try:
                gc = unreal.load_class(None, p + "." + name + "_C")
            except Exception as e:
                rec["cdo_err"] = repr(e)
            if gc is not None:
                try:
                    cdoobj = unreal.get_default_object(gc)
                    rec["cdo_bytes"] = t3d(cdoobj, os.path.join("cdo", name + "_CDO.t3d"))
                except Exception as e:
                    rec["cdo_err"] = repr(e)
        man[name] = rec
    json.dump(man, io.open(os.path.join(OUT, "_targeted_manifest.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)
    ok = sum(1 for r in man.values() if r.get("bytes"))
    log("=== TARGETED DONE ok=%d/%d ===" % (ok, len(man)))
    print("TARGETED_DONE ok=%d/%d" % (ok, len(man)))


main()
