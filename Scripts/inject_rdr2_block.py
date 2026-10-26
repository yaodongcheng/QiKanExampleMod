"""把圣丹尼斯一块街区（默认 200×200 m）的实例注入场景 xscene。

坐标口径（都验证过，别改）：
  · 场景范围：世界中心 (2554.5, -1150.5)、1500×1500 m  ⇒  scene_x = wx - 1804.5
                                                          scene_y = -400.5 - wy
  · 高度：**取场景地形高度图**（`terrain_sdn/rdr2_sdn_height_1537.png`），不取实例自带的 z——
    地形生成时做过岸线平滑，自带 z 会和地面差几米（实测：样板楼自带 31.85，实际落位 22.81）。
  · 朝向：RDR2 是 X东/Y北，骑砍场景 Y+ 朝南（镜像）⇒ **rotation_z = -yaw**。
  · 网格名：查 `rdr2_mesh_map.json`（`<名>_lod` 这类会被编辑器改名，必须映射）。

用法：
    python Scripts/inject_rdr2_block.py [--center 2825,-1225] [--half 100] [--scene <xscene>]
"""
import argparse
import io
import json
import math
import os
import shutil
import sys
import time

import numpy as np
from PIL import Image

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
MODULE = (r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12"
          r"\Mount & Blade II Bannerlord\Modules\RDR2")
TILES = r"D:\BrainMaker\大镖客2解包分析\export\previews\sdn\tiles"
HEIGHT = os.path.join(ROOT, r"Debug\RD2构造相关\terrain_sdn\rdr2_sdn_height_1537.png")
MESHMAP = os.path.join(ROOT, r"Debug\RD2构造相关\rdr2_mesh_map.json")
SCENE = os.path.join(MODULE, r"SceneObj\rdr2_sanit_denis\scene.xscene")

# 🔴 场景范围一律从 terrain_sdn_meta.json 推导，**禁止手抄 1804.5 / 1500 / 25.41**
#    （总纲 §8.0「坐标体系」是唯一口径）
META_SDN = os.path.join(ROOT, "Debug/RD2构造相关/terrain_sdn/terrain_sdn_meta.json")
_meta = json.load(io.open(META_SDN, encoding="utf-8"))
SIZE = float(_meta["size_m"])
WX0 = float(_meta["center_m"][0]) - SIZE / 2.0     # 场景西边界对应的世界 x
WY1 = float(_meta["center_m"][1]) + SIZE / 2.0     # 场景北边界对应的世界 y
ZSPAN = float(_meta["z_max_m"]) - float(_meta["z_min_m"])


def load_height():
    a = np.array(Image.open(HEIGHT), dtype=np.float32)
    return a / 65535.0 * ZSPAN


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--center", default="2825,-1225", help="世界坐标 x,y")
    ap.add_argument("--half", type=float, default=100.0, help="半边长（米）")
    ap.add_argument("--scene", default=SCENE)
    ap.add_argument("--tag", default="block")
    ap.add_argument("--dry", action="store_true")
    args = ap.parse_args()

    cx, cy = [float(v) for v in args.center.split(",")]
    hmap = load_height()
    meshmap = json.load(io.open(MESHMAP, encoding="utf-8"))

    items, missing = [], set()
    for fn in sorted(os.listdir(TILES)):
        if not fn.endswith(".gltf"):
            continue
        # 懒加载：只读需要的 JSON
        with io.open(os.path.join(TILES, fn), encoding="utf-8") as f:
            j = json.load(f)
        for n in j.get("nodes", []):
            t = n.get("translation")
            nm = n.get("name")
            if "mesh" not in n or not nm or not t:
                continue
            if abs(t[0] - cx) > args.half or abs(t[1] - cy) > args.half:
                continue
            mesh = meshmap.get(nm)
            if not mesh:
                missing.add(nm)
                continue
            q = n.get("rotation") or [0, 0, 0, 1]
            yaw = math.degrees(2 * math.atan2(q[2], q[3]))
            s = n.get("scale") or [1, 1, 1]
            items.append((nm, mesh, t[0], t[1], yaw, s[0]))

    frag = []
    for i, (nm, mesh, wx, wy, yaw, sc) in enumerate(items):
        sx = wx - WX0
        sy = WY1 - wy
        hx = min(hmap.shape[1] - 1, max(0, int(round(sx / SIZE * (hmap.shape[1] - 1)))))
        hy = min(hmap.shape[0] - 1, max(0, int(round(sy / SIZE * (hmap.shape[0] - 1)))))
        sz = float(hmap[hy, hx])
        rz = -math.radians(yaw)
        scale = '' if abs(sc - 1.0) < 1e-3 else ' scale="%.4f, %.4f, %.4f"' % (sc, sc, sc)
        frag.append(
            '<game_entity name="%s_%04d" old_prefab_name="" mobility="1">'
            '<transform position="%.3f, %.3f, %.3f" rotation_euler="0.000, 0.000, %.4f"%s/>'
            '<components><meta_mesh_component name="%s"/></components></game_entity>'
            % (nm, i, sx, sy, sz, rz, scale, mesh))

    print("街区 中心(%g,%g) 半径 %g m ⇒ 实例 %d · 唯一模型 %d · 缺网格 %d"
          % (cx, cy, args.half, len(items), len({x[1] for x in items}), len(missing)))
    if missing:
        print("  缺网格的模型:", sorted(missing)[:8])
    if args.dry:
        return

    src = io.open(args.scene, encoding="utf-8", errors="replace").read()
    backup = os.path.join(ROOT, r"Debug\offline", "_scene_%s_%s.xscene"
                          % (args.tag, time.strftime("%H%M%S")))
    os.makedirs(os.path.dirname(backup), exist_ok=True)
    shutil.copy2(args.scene, backup)
    if "</entities>" not in src:
        print("场景里没有 </entities>，中止")
        return
    out = src.replace("</entities>", "".join(frag) + "</entities>", 1)
    io.open(args.scene, "w", encoding="utf-8", newline="\n").write(out)
    print("已注入 %d 个实体 → %s（备份 %s）" % (len(frag), args.scene, backup))


if __name__ == "__main__":
    main()
