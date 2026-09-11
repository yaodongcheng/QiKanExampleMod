# -*- coding: utf-8 -*-
"""ElementTree 版差异比对：现场景（ModKit 保存过，缺省属性被省略） vs 当前生成器产出。"""
import io
import os
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
LIVE = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord"
        r"\Modules\Taikou\SceneObj\Main_map\scene.xscene")
TMP = os.path.join(ROOT, "output", "_tmp_module")


def parse_capsules(path):
    """返回 {sid: (x, y, z, mesh)}；缺省 position = 0"""
    root = ET.parse(path).getroot()
    out = {}
    for e in root.iter("game_entity"):
        nm = e.get("name") or ""
        if not nm.startswith("campaign_icon_capsule_"):
            continue
        sid = nm.split("_", 4)[-1].split("_", 1)[1] if nm.count("_") >= 4 else nm
        sid = nm[len("campaign_icon_capsule_"):].split("_", 1)[1]          # 去掉编号
        tr = e.find("./transform")
        x = y = z = 0.0
        if tr is not None and tr.get("position"):
            x, y, z = (float(v) for v in tr.get("position").split(","))
        mesh = ""
        for mm in e.iter("meta_mesh_component"):
            mesh = mm.get("name")
        out[sid] = (x, y, z, mesh)
    return out


live = parse_capsules(LIVE)
os.makedirs(os.path.join(TMP, "SceneObj", "Main_map"), exist_ok=True)
shutil.copy2(LIVE, os.path.join(TMP, "SceneObj", "Main_map", "scene.xscene"))
subprocess.run([sys.executable, os.path.join(ROOT, "Scripts", "place_settlements.py"), "--module", TMP],
               check=True, capture_output=True)
gen = parse_capsules(os.path.join(TMP, "SceneObj", "Main_map", "scene.xscene"))

print("据点实体：现场景 %d 个，生成器 %d 个" % (len(live), len(gen)))
diff = []
for sid in sorted(set(live) | set(gen)):
    a, b = live.get(sid), gen.get(sid)
    if a is None or b is None:
        diff.append((sid, a, b, "仅在一边"))
    elif max(abs(a[i] - b[i]) for i in range(3)) > 0.05:
        diff.append((sid, a, b, "位置不同"))
print("与生成器产出的差异：%d 个" % len(diff))
for sid, a, b, why in diff:
    print("   %-14s 现场景 %s   生成器 %s  (%s)" % (sid, a, b, why))
