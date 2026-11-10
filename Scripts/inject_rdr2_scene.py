# -*- coding: utf-8 -*-
"""把场景的 placement 摆位清单注入 xscene，实体按【类型】命名分组。

坐标口径（2026-11-10 实测定标，别改）：
  · 场景矩形 = meta 的 rect_m = [x0, y0, x1, y1]；size_m = [x1-x0, y1-y0]
  · placement 的 loc 是 **blend 世界坐标**，blend 原点 = (x0 + size_x/2, y0 + size_y/2 + 64)
    —— 那 64 m 是地形相对 blend 的北裁偏移，用 156 个贴地件网格搜索 + 锚点模型双重验证
       （中位残差 0.51~0.56 m，明显优于"矩形中心"的 2.33 m）
  · 场景坐标（米，1:1）：sx = world_x - x0 ; sy = world_y - y0（Y+ 朝北）
  · 高度：采样 `rdr2_sdn_height_3329x3841.png`（行 0 = 北），双线性；不取实例自带 z
  · 朝向：quat 的排列是 **(w, x, y, z)**（Blender to_quaternion 的顺序，2026-11-10 用 AABB 反验：
      该公式 AABB 中位误差 0.000、96.7% 命中；换成 (x,y,z,w) 只有 62.8%）
      ⇒ yaw = 2*atan2(q[3], q[0])；rotation_z = +yaw（场景 Y+ 朝北，无镜像）
  · 高度：**默认直接沿用 blend 的 z**（`--z-mode blend`，2026-11-10 用户裁定）——
      RDR2 大量建筑/码头桩基本来就埋在
      地面里，贴地会把这些关系抹平；`--z-mode snap` 是旧办法（地形贴地 + 扣模型局部
      min_z，缓存 trf_bbox.json），保留仅作对照
  · 实体名 = **metamesh 名**（同名实例加 _2、_3…）；
      类别用两样编辑器元数据表达：`<tags><tag name="类"/></tags>`（列表搜索框 t 前缀）
      + `<edit_mode_data><selection_sets><set name="类"/></selection_sets>`（选择集分组）
      —— 不用 scene level，那是游戏内可见性开关，会误伤实机

用法：
    python Scripts/inject_rdr2_scene.py --id sdn --dry
    python Scripts/inject_rdr2_scene.py --id sdn --min-size 2 --yaw-mode xyzw
"""
import argparse
import csv
import io
import json
import math
import os
import re
import shutil
import sys
import time

import numpy as np
from PIL import Image

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
MODULE = r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\RDR2"
DATA = r"D:\BrainMaker\大镖客2解包分析"
MANIFEST = os.path.join(DATA, r"deliverable\manifests\model_manifest.csv")

SKIP_CLS = {"terrain_cell", "terrain_lod", "underground", "watertile",
            "placeholder", "lod", "layout", "int_proxy"}
SUFFIX_RE = re.compile(r"\.\d{3}$")     # Blender 复制体 p_crate01x.001

# 场景 id -> (meta json, 高度图, 摆位 jsonl, xscene, blend 原点 y 偏移)
SCENES = {
    "sdn": (
        os.path.join(ROOT, r"Debug\RD2构造相关\terrain_sdn\terrain_sdn_meta.json"),
        os.path.join(ROOT, r"Debug\RD2构造相关\terrain_sdn\rdr2_sdn_height_3329x3841.png"),
        os.path.join(DATA, r"deliverable\blender\scenes\placement_sdn.jsonl"),
        os.path.join(MODULE, r"SceneObj\rdr2_sanit_denis\scene.xscene"),
        64.0,
    ),
}


def load_imported_meshes():
    """→ (资产名集合, {去 .NNN 后缀的基名: 真资产名})。

    有些资产导入时名字带 Blender 复制体后缀（如 `p_cal_fence02x.008`），
    而摆位表里写的是 `p_cal_fence02x` —— 必须靠基名索引救回来。
    """
    have = set()
    base2asset = {}
    for sub in ("models", "decal_models"):
        base = os.path.join(MODULE, "Assets", sub)
        if not os.path.isdir(base):
            continue
        for root, _, files in os.walk(base):
            for fn in files:
                if not fn.endswith("_geo.tpac"):
                    continue
                n = fn[:-len("_geo.tpac")]
                low = n.lower()
                have.add(low)
                b = SUFFIX_RE.sub("", low)
                if b != low:
                    base2asset.setdefault(b, n)
    return have, base2asset


def load_manifest():
    out = {}
    with io.open(MANIFEST, encoding="utf-8-sig") as f:
        for r in csv.DictReader(f):
            try:
                out[r["hash"].lower()] = (
                    r["name"], int(r["tris"]),
                    max(float(r["size_x"]), float(r["size_y"]), float(r["size_z"])),
                    (float(r["size_x"]), float(r["size_y"])))
            except Exception:
                pass
    return out


BBOX_CACHE = os.path.join(ROOT, r"Debug\RD2构造相关\trf_bbox.json")
MODEL_ROOTS = [os.path.join(DATA, r"deliverable\models"),
               os.path.join(DATA, r"deliverable\models_extra")]


def trf_min_z(name, cache):
    """模型局部坐标的 min_z（TRF 里就是这套坐标）。缓存到 trf_bbox.json。"""
    if name in cache:
        return cache[name]
    path = None
    for r in MODEL_ROOTS:
        p = os.path.join(r, name + ".trf")
        if os.path.exists(p):
            path = p
            break
    mnz = 0.0
    if path:
        lines = io.open(path, encoding="utf-8", errors="replace").read().splitlines()
        n = int(lines[1].split()[1])
        i = 2
        mnz = 1e18
        for _ in range(n):
            i += 1
            vc = int(lines[i]); i += 1
            for _k in range(vc):
                mnz = min(mnz, float(lines[i].split()[2])); i += 1
            i += 1
            fvf = int(lines[i]); i += 1 + fvf * 3
            mk = int(lines[i]); i += 1
            for _m in range(mk):
                i += 1
                c = int(lines[i]); i += 1 + c
            fc = int(lines[i]); i += 1 + fc
        if mnz > 1e17:
            mnz = 0.0
    cache[name] = mnz
    return mnz


def yaw_of(q, mode):
    if mode == "none":
        return 0.0
    if mode == "wxyz":
        return math.degrees(2 * math.atan2(q[3], q[0]))
    return math.degrees(2 * math.atan2(q[2], q[3]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--id", default="sdn")
    ap.add_argument("--scene", default=None)
    ap.add_argument("--min-size", type=float, default=0.0,
                    help="模型最大边小于这个米数就不摆（默认 0 = 全摆，和 blend 对齐）")
    ap.add_argument("--yaw-mode", choices=["wxyz", "xyzw", "none"], default="wxyz")
    ap.add_argument("--yaw-offset", type=float, default=0.0)
    ap.add_argument("--z-mode", choices=["blend", "snap"], default="blend",
                    help="blend = 直接用摆位表的 z（默认，保留桩基埋地）；snap = 按地形贴地")
    ap.add_argument("--z-offset", type=float, default=0.0, help="整场高度偏移（米）")
    ap.add_argument("--dry", action="store_true")
    ap.add_argument("--exclude", default="veg,lod_out",
                    help="不注入的类，逗号分隔（默认 veg=用户明确不要植被；lod_out=大地图远景碎片，场景里会和建筑重复）")
    ap.add_argument("--replace", action="store_true", default=True,
                    help="写之前先删掉本脚本上次注入的实体（按类名前缀识别）")
    a = ap.parse_args()

    meta_p, height_p, place_p, scene_p, y_off = SCENES[a.id]
    if a.scene:
        scene_p = a.scene

    meta = json.load(io.open(meta_p, encoding="utf-8"))
    x0, y0, x1, y1 = meta["rect_m"]
    size_x, size_y = meta["size_m"]
    zmin, zmax = meta["z_min_m"], meta["z_max_m"]
    kx, ky = x0 + size_x / 2.0, y0 + size_y / 2.0 + y_off

    a_img = np.array(Image.open(height_p), dtype=np.float32)
    hmap = zmin + a_img / 65535.0 * (zmax - zmin)
    H, W = hmap.shape

    man = load_manifest()
    have, base2asset = load_imported_meshes()
    bbox_cache = {}
    if os.path.exists(BBOX_CACHE):
        try:
            bbox_cache = json.load(io.open(BBOX_CACHE, encoding="utf-8"))
        except Exception:
            bbox_cache = {}

    def ground(sx, sy):
        fx = sx / size_x * (W - 1)
        fy = (1.0 - sy / size_y) * (H - 1)
        ix = min(max(int(fx), 0), W - 2)
        iy = min(max(int(fy), 0), H - 2)
        tx, ty = fx - ix, fy - iy
        return float(hmap[iy, ix] * (1 - tx) * (1 - ty) + hmap[iy, ix + 1] * tx * (1 - ty)
                     + hmap[iy + 1, ix] * (1 - tx) * ty + hmap[iy + 1, ix + 1] * tx * ty)

    exclude = set(x.strip() for x in (a.exclude or "").split(",") if x.strip())
    items = []
    skipped = {"small": 0, "no_man": 0, "not_imported": 0, "outside": 0, "excluded": 0}
    with io.open(place_p, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line:
                continue
            d = json.loads(line)
            cls = d.get("cls") or "unknown"
            if cls in SKIP_CLS:
                continue
            if cls in exclude:
                skipped["excluded"] += 1
                continue
            h = (d.get("model_hash") or d.get("hash") or "").lower()
            ent = man.get(h)
            nm = d.get("n") or ""
            if ent and ent[0].lower() in have:
                name, tris_n, sz, (mx, my) = ent
            else:
                # 回退链：清单名 / 摆位名 / 各自去 .NNN 后缀 / 资产侧带后缀的基名索引
                cands = []
                if ent:
                    cands.append(ent[0])
                if nm:
                    cands.append(nm)
                cands += [SUFFIX_RE.sub("", c) for c in list(cands)]
                name = next((c for c in cands if c and c.lower() in have), None)
                if name is None:
                    name = next((base2asset[c.lower()] for c in cands
                                 if c and c.lower() in base2asset), None)
                if name is None:
                    if ent:
                        skipped["not_imported"] += 1
                    else:
                        skipped["no_man"] += 1
                    continue
                tris_n = ent[1] if ent else 0
                if ent:
                    sz, (mx, my) = ent[2], (ent[2], ent[2])
                else:
                    sz, (mx, my) = 0.0, (0.0, 0.0)
                if sz == 0.0:
                    # 清单里没有尺寸时用 1 m 兜底（尺寸过滤对它不生效）
                    sz = max(1.0, a.min_size)
            if sz < a.min_size:
                skipped["small"] += 1
                continue
            lx, ly, lz = float(d["loc"][0]), float(d["loc"][1]), float(d["loc"][2])
            wx, wy = lx + kx, ly + ky
            sx, sy = wx - x0, wy - y0
            if not (0 <= sx <= size_x and 0 <= sy <= size_y):
                skipped["outside"] += 1
                continue
            q = d.get("quat") or [0, 0, 0, 1]
            yaw = yaw_of(q, a.yaw_mode) + a.yaw_offset
            if a.z_mode == "blend":
                # 直接沿用 blend 的 z（2026-11-10 用户裁定）：RDR2 大量建筑桩基埋进地面，
                # 贴地反而会把这些关系抹掉
                z = lz + a.z_offset
            else:
                # 老办法：按地形贴地 + 扣模型局部枢轴
                hs = [ground(sx, sy)]
                for dx, dy in ((mx / 2, my / 2), (mx / 2, -my / 2), (-mx / 2, my / 2), (-mx / 2, -my / 2)):
                    hs.append(ground(sx + dx, sy + dy))
                z = min(hs) - trf_min_z(name, bbox_cache) + a.z_offset
            items.append((cls, name, sx, sy, z, math.radians(yaw), tris_n))

    per = {}
    for it in items:
        c = per.setdefault(it[0], [0, 0])
        c[0] += 1
        c[1] += it[6]
    try:
        io.open(BBOX_CACHE, "w", encoding="utf-8").write(json.dumps(bbox_cache))
    except Exception as e:
        print("bbox 缓存写入失败: %s" % e)
    print("注入实体 = %d   面数 = %.1fM   跳过: 小件 %d / 无清单 %d / 未导入 %d / 出界 %d / 排除 %d"
          % (len(items), sum(it[6] for it in items) / 1e6,
             skipped["small"], skipped["no_man"], skipped["not_imported"],
             skipped["outside"], skipped["excluded"]))
    for c, (n, t) in sorted(per.items(), key=lambda kv: -kv[1][1]):
        print("   %-12s %6d 个  %7.2fM 面" % (c, n, t / 1e6))
    if a.dry:
        return

    src = io.open(scene_p, encoding="utf-8", errors="replace").read()
    if "</entities>" not in src:
        print("场景没有 </entities>，中止")
        return

    # 幂等：删掉上次注入的实体（我们注入的都带 meta_mesh_component，原生实体没有）
    def strip_injected(s):
        out, i, n = [], 0, 0
        while True:
            j = s.find("<game_entity", i)
            if j < 0:
                out.append(s[i:])
                break
            out.append(s[i:j])
            k = s.find("</game_entity>", j)
            if k < 0:
                out.append(s[j:])
                break
            k += len("</game_entity>")
            block = s[j:k]
            # `far` 标签的实体是远景层（inject_rdr2_far.py 写的），重铺城市时不能删
            if "<meta_mesh_component" in block and '<tag name="far"/>' not in block:
                n += 1
            else:
                out.append(block)
            i = k
        return "".join(out), n

    src, removed = strip_injected(src)

    frag = []
    used = {}
    names = []
    for cls, name, sx, sy, sz, rz, tris_n in items:
        k = used.get(name, 0) + 1
        used[name] = k
        ent_name = name if k == 1 else "%s_%d" % (name, k)
        names.append(ent_name)
        frag.append('<game_entity name="%s" old_prefab_name="" mobility="1">'
                    '<tags><tag name="%s"/></tags>'
                    '<transform position="%.3f, %.3f, %.3f" rotation_euler="0.000, 0.000, %.4f"/>'
                    '<edit_mode_data><selection_sets><set name="%s"/></selection_sets></edit_mode_data>'
                    '<components><meta_mesh_component name="%s"/></components></game_entity>'
                    % (ent_name, cls, sx, sy, sz, rz, cls, name))

    backup_dir = os.path.join(ROOT, r"Debug\offline")
    os.makedirs(backup_dir, exist_ok=True)
    bak = os.path.join(backup_dir, "_scene_%s_%s.xscene" % (a.id, time.strftime("%Y%m%d_%H%M%S")))
    shutil.copy2(scene_p, bak)

    out = src.replace("</entities>", "".join(frag) + "</entities>", 1)
    io.open(scene_p, "w", encoding="utf-8", newline="\n").write(out)

    man_csv = os.path.join(ROOT, r"Debug\RD2构造相关\scene_%s_entities.csv" % a.id)
    with io.open(man_csv, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(["entity", "cls", "model", "x", "y", "z", "yaw_deg", "tris"])
        for (cls, name, sx, sy, sz, rz, tris_n), ent_name in zip(items, names):
            w.writerow([ent_name, cls, name,
                        "%.2f" % sx, "%.2f" % sy, "%.2f" % sz,
                        "%.1f" % math.degrees(rz), tris_n])

    print("已写入 %d 个实体 → %s" % (len(frag), scene_p))
    print("删掉旧注入 %d 个；备份 %s" % (removed, bak))
    print("清单 %s" % man_csv)


if __name__ == "__main__":
    main()
