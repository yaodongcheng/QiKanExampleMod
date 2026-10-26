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
_Y_SOUTH = float(_meta["center_m"][1]) - SIZE / 2.0   # 场景南边界对应的世界 y


def load_height():
    a = np.array(Image.open(HEIGHT), dtype=np.float32)
    return a / 65535.0 * ZSPAN


def load_modelsize():
    """模型最大边（米），用于剔除 <2 m 的家具/碎屑。"""
    import csv
    out = {}
    with io.open(r"D:\BrainMaker\大镖客2解包分析\deliverable\handoff\model_manifest.csv",
                 encoding="utf-8-sig") as f:
        for r in csv.DictReader(f):
            try:
                out[r["name"]] = max(float(r["size_x"]), float(r["size_y"]), float(r["size_z"]))
            except Exception:
                pass
    return out


def load_modelsize_xy():
    """模型 x/y 尺寸（米），用于按足迹取最低地面。"""
    import csv
    out = {}
    with io.open(r"D:\BrainMaker\大镖客2解包分析\deliverable\handoff\model_manifest.csv",
                 encoding="utf-8-sig") as f:
        for r in csv.DictReader(f):
            try:
                out[r["name"]] = (float(r["size_x"]), float(r["size_y"]))
            except Exception:
                pass
    return out


def load_contour():
    """源高程（4 m/px）：判"贴地"用。返回 (meta, array)。"""
    meta = json.load(io.open(r"D:\BrainMaker\大镖客2解包分析\deliverable\heightmap\continent_height_meta.json",
                             encoding="utf-8"))
    hm = meta["heightmap"]
    a = np.array(Image.open(os.path.join(r"D:\BrainMaker\大镖客2解包分析\deliverable\heightmap",
                                         hm["file_16bit"])), dtype=np.float32)
    a = hm["zmin"] + a / 65535.0 * (hm["zmax"] - hm["zmin"])
    return hm, a


def cont_z(hm, a, wx, wy):
    ix = int(round((wx - hm["x0"]) / hm["res_m"]))
    iy = int(round((hm["y1"] - wy) / hm["res_m"]))
    iy = max(0, min(a.shape[0] - 1, iy)); ix = max(0, min(a.shape[1] - 1, ix))
    return float(a[iy, ix])


WATER_Z = float(_meta["water_surface_m"]) - float(_meta["z_min_m"])   # 水面（场景单位）


def sample_height(hmap, sx, sy, y_north=True):
    """双线性采样（原来用最近邻，实测整片地面会差半米级）。"""
    H, W = hmap.shape
    fx = sx / SIZE * (W - 1)
    fy = ((SIZE - sy) / SIZE if y_north else (sy / SIZE)) * (H - 1)
    ix, iy = int(fx), int(fy)
    ix = max(0, min(W - 2, ix)); iy = max(0, min(H - 2, iy))
    tx, ty = fx - ix, fy - iy
    return float(hmap[iy, ix] * (1 - tx) * (1 - ty) + hmap[iy, ix + 1] * tx * (1 - ty)
                 + hmap[iy + 1, ix] * (1 - tx) * ty + hmap[iy + 1, ix + 1] * tx * ty)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--center", default="2825,-1225", help="世界坐标 x,y")
    ap.add_argument("--half", type=float, default=100.0, help="半边长（米）")
    ap.add_argument("--scene", default=SCENE)
    ap.add_argument("--tag", default="block")
    ap.add_argument("--dry", action="store_true")
    ap.add_argument("--prefix", default="", help="实体名前缀（区分多组对照）")
    ap.add_argument("--sink-per-meter", type=float, default=0.0,
                    help="每米足迹额外下沉多少米（大平板在坡上会悬空，用它压住）")
    ap.add_argument("--z-offset", type=float, default=0.0,
                    help="全局高度偏移（米），整片抬高/压低")
    ap.add_argument("--boat-sink", type=float, default=2.5,
                    help="船（new_boats*）在水面上的吃水深度（米，默认 2.5）")
    ap.add_argument("--no-water-clamp", action="store_true",
                    help="不把水面以下的实例抬到水面（默认会抬）")
    ap.add_argument("--y-north", action="store_true", default=True,
                    help="场景 Y+ 朝北（实机确认，默认开）；关掉则按旧的 Y+ 朝南")
    ap.add_argument("--min-size", type=float, default=2.0,
                    help="最大边小于这个米数的实例不摆（家具/钉子/碎屑）")
    ap.add_argument("--max-z-diff", type=float, default=None,
                    help="只摆与地形高度差 <= 这个米数的实例（剔掉楼上部件/屋顶壳）")
    args = ap.parse_args()

    cx, cy = [float(v) for v in args.center.split(",")]
    hmap = load_height()
    meshmap = json.load(io.open(MESHMAP, encoding="utf-8"))
    msize = load_modelsize()
    msize2 = load_modelsize_xy()
    chm, carr = load_contour() if args.max_z_diff is not None else (None, None)

    items, missing = [], set()
    small = elevated = 0
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
            sz = msize.get(nm)
            if sz is not None and sz < args.min_size:
                small += 1
                continue
            if args.max_z_diff is not None and abs(t[2] - cont_z(chm, carr, t[0], t[1])) > args.max_z_diff:
                elevated += 1
                continue
            q = n.get("rotation") or [0, 0, 0, 1]
            yaw = math.degrees(2 * math.atan2(q[2], q[3]))
            s = n.get("scale") or [1, 1, 1]
            items.append((nm, mesh, t[0], t[1], yaw, s[0]))

    frag = []
    for i, (nm, mesh, wx, wy, yaw, sc) in enumerate(items):
        sx = wx - WX0
        # 场景 Y+ 朝北（2026-10-26 实机确认）：南边界 = 世界 y 最小处
        sy = (wy - _Y_SOUTH) if args.y_north else (WY1 - wy)
        cz = sample_height(hmap, sx, sy, args.y_north)
        if cz < WATER_Z - 0.05:
            # 水面区：船沉一截吃水，其余（码头/桩）贴水面
            sz = (WATER_Z - args.boat_sink) if nm.startswith("new_boats") else WATER_Z
        else:
            # 陆地：按"足迹四角 + 中心"取最低地面 ⇒ 长条构件不再一端悬空
            hx, hy = msize2.get(nm, (0.0, 0.0))
            hs = [cz]
            for ddx, ddy in ((hx / 2, hy / 2), (hx / 2, -hy / 2), (-hx / 2, hy / 2), (-hx / 2, -hy / 2)):
                hs.append(sample_height(hmap, sx + ddx, sy + ddy, args.y_north))
            sz = min(hs)
            if args.sink_per_meter:
                sz -= args.sink_per_meter * max(hx, hy)      # 大件多压一点
            if not args.no_water_clamp:
                sz = max(sz, WATER_Z)
        sz += args.z_offset
        rz = (math.radians(yaw) if args.y_north else -math.radians(yaw))
        scale = '' if abs(sc - 1.0) < 1e-3 else ' scale="%.4f, %.4f, %.4f"' % (sc, sc, sc)
        frag.append(
            '<game_entity name="%s%s_%04d" old_prefab_name="" mobility="1">'
            '<transform position="%.3f, %.3f, %.3f" rotation_euler="0.000, 0.000, %.4f"%s/>'
            '<components><meta_mesh_component name="%s"/></components></game_entity>'
            % (args.prefix, nm, i, sx, sy, sz, rz, scale, mesh))

    print("街区 中心(%g,%g) 半径 %g m ⇒ 实例 %d（剔除 <%.1f m 的 %d 个、离地 >%s 的 %d 个）· 唯一模型 %d · 缺网格 %d"
          % (cx, cy, args.half, len(items), args.min_size, small,
             args.max_z_diff if args.max_z_diff is not None else "-", elevated,
             len({x[1] for x in items}), len(missing)))
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
