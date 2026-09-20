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

reg = unreal.AssetRegistryHelpers.get_asset_registry()
all_a = reg.get_all_assets()
lines = []
cmu = 0
for a in all_a:
    try:
        cls = str(a.asset_class)
    except Exception:
        cls = ""
    if "AnimSequence" not in cls:
        continue
    pkg = str(a.package_name)
    if "CMU" in pkg or "cmu" in pkg:
        cmu += 1
    lines.append(pkg)
out = "\n".join(sorted(lines))
d = os.path.join(PROJECT_ROOT, "input/source/ue_mannequin/clips_basic")
os.makedirs(d, exist_ok=True)
with open(os.path.join(d, "all_anim_list.txt"), "w", encoding="utf-8") as f:
    f.write(out)
print("TOTAL anim:", len(lines), "CMU-liked:", cmu)
