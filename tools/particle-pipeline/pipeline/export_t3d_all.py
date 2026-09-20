# -*- coding: utf-8 -*-
"""
export_t3d_all.py -- 用 UE 的 ObjectExporterT3D 把 FCS 项目里所有粒子资产导出为 T3D 文本。
T3D 是 UE 原生文本序列化格式，含：节点图(FunctionScript/PinName) + RapidIterationParameters
(命名常量 + 偏移 + ParameterData 字节) + RendererProperties(Material/Alignment/SubImageSize)
+ Cascade 的 Distribution* 全部数值。

用法:
  UE4Editor.exe <uproject> -run=pythonscript -script="export_t3d_all.py" -unattended -nosplash -nullrhi -stdout
环境变量:
  BM_T3D_OUT   输出目录 (默认 D:/BrainMaker/骑砍2粒子特效复刻/output/t3d)
  BM_T3D_PATH  扫描根   (默认 /Game/FlexibleCombatSystem/VFX)
"""
import unreal, os, traceback

# 🔴 本文件**故意不 import paths**：它是四段管线里唯一跑在 **UE 自带 python** 里的，
#    那边 sys.path 与工作目录都不可控，所以两根一律用「环境变量 + 默认值」写死在这里。
#    改数据根 = 设 BM_T3D_OUT，或改下面这一行的默认值（其余三段读 paths.py）。
OUT = os.environ.get("BM_T3D_OUT", r"D:/BrainMaker/骑砍2粒子特效复刻/output/t3d")
ROOT = os.environ.get("BM_T3D_PATH", "/Game/FlexibleCombatSystem/VFX")
os.makedirs(OUT, exist_ok=True)
_f = open(os.path.join(OUT, "_export_trace.log"), "w", encoding="utf-8")
def L(m):
    _f.write(str(m) + "\n"); _f.flush()
    try: unreal.log(str(m))
    except Exception: pass
def safe(fn, *a, **k):
    try: return fn(*a, **k)
    except Exception as e: return "<ERR %s: %s>" % (type(e).__name__, e)
def to_list(x):
    """unreal.Array / 任何可迭代 -> python list；字符串/异常一律返回 []。"""
    if isinstance(x, str): return []
    try: return list(x)
    except Exception: return []

WANT = ("NiagaraSystem", "ParticleSystem")

def list_assets():
    """注意：4.27 在 -run=pythonscript 下 AssetRegistry 启动时已扫好；
    这里【绝不能】再调 scanpaths_synchronous(ForceRescan) —— 会把注册表清空，
    随后 get_all_assets() 返回 0（本轮踩过）。仅在真的为空时轮询等待。"""
    reg = unreal.AssetRegistryHelpers.get_asset_registry()
    # !! 4.27 的 get_all_assets() 返回 unreal.Array（不是 python list），
    #    isinstance(x, list) 恒为 False —— 必须用 len()/迭代判空（本轮踩过）
    raw = safe(reg.get_all_assets)
    arr = to_list(raw)
    if len(arr) < 100:
        import time
        for _ in range(60):
            safe(reg.wait_for_completion); time.sleep(0.5)
            arr = to_list(safe(reg.get_all_assets))
            if len(arr) >= 100: break
    L("AssetRegistry 资产总数 = %d" % len(arr))
    out = []
    for a in arr:
        try:
            cls = str(a.asset_class)
            pkg = str(a.package_name)
        except Exception:
            continue
        if cls not in WANT: continue
        if ROOT not in pkg: continue
        out.append((pkg, cls))
    return sorted(out)

def export_one(pkg, cls, exp):
    a = safe(unreal.load_asset, pkg)
    if isinstance(a, str):
        return "load_fail"
    rel = pkg.replace("/Game/", "").replace("/", "__")
    fn = os.path.join(OUT, "%s__%s.t3d" % (rel, cls))
    task = unreal.AssetExportTask()
    task.set_editor_property("object", a)
    task.set_editor_property("exporter", exp)
    task.set_editor_property("filename", fn)
    for p, v in (("prompt", False), ("replace_identical", True)):
        try: task.set_editor_property(p, v)
        except Exception: pass
    r = safe(unreal.Exporter.run_asset_export_task, task)
    if os.path.exists(fn) and os.path.getsize(fn) > 0:
        return "ok:%d" % os.path.getsize(fn)
    return "fail:%s" % r

def main():
    L("=== T3D 批量导出 ===")
    L("engine = %s" % safe(unreal.SystemLibrary.get_engine_version))
    exp = safe(lambda: unreal.ObjectExporterT3D())
    if isinstance(exp, str):
        L("!! ObjectExporterT3D 不可用: %s" % exp); return
    assets = list_assets()
    L("命中粒子资产: %d" % len(assets))
    ok = 0; fail = 0; total = 0
    for i, (pkg, cls) in enumerate(assets):
        r = export_one(pkg, cls, exp)
        if r.startswith("ok"):
            ok += 1; total += int(r.split(":")[1])
        else:
            fail += 1
        L("[%3d/%3d] %-16s %-70s %s" % (i + 1, len(assets), cls, pkg, r))
    L("=== 完成 ok=%d fail=%d 总字节=%d ===" % (ok, fail, total))

try: main()
except Exception: L("FATAL\n" + traceback.format_exc())
_f.close(); print("EXPORT DONE")
