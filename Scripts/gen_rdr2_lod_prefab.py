#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 prefabs/<组>.json 转成 Bannerlord prefab XML（远景碎片）。

用法：
  python gen_rdr2_lod_prefab.py                      # 默认只做 sdn（兼容旧行为）
  python gen_rdr2_lod_prefab.py --all                # 全部 56 组 + 2 个占位据点
  python gen_rdr2_lod_prefab.py --groups sdn,val     # 指定组
  python gen_rdr2_lod_prefab.py --all --root-at-anchor
        # 根节点直接放地图坐标（场景里实体放 0,0,0）；
        # 默认不这样，沿用 sdn 口径：根 0，场景实体放锚点。

输出：
  Modules/RDR2/Prefabs/rdr2_lod_prefabs.xml    每组两份 prefab：
      rdr2_<组>_lod       大地图缩略（内部 map_scale 容器 1:14.x）
      rdr2_<组>_lod_1to1  1:1 场景用（不缩放）
  Modules/RDR2/AssetSources/ImortReady/rdr2_lod_fragments/prefab_anchors.csv
      摆位表：group_id / 中英文名 / group_type / anchor_world / 地图坐标 /
              碎片数 / 已导入数 / 已知缺件数 / placeholder

占位据点（2026-11-09 用户裁定）：
  `twi` 双子岩、`abe` 阿伯丁猪场 在碎片 JSON 里没有 LOD 组 ⇒ 先用**原版村庄地图缩略**
  占位（`map_village_a`，原版 `map_icons.xml` 的 mesh_scale = 0.45），**不需要导入任何资产**。
  以后替换成对应建筑群落的 LOD6 版本。

子节点口径（与既有 sdn 一致）：
- 位置 = pos_local（相对据点锚点，米）
- 旋转 = 四元数 (x,y,z,w) → 引擎 Mat3.ApplyEulerAngles 口径
- 缩放 = (sxy, sxy, sz)
- 世界 → 地图单位：x 768/10768，y 576/8024，z 50/725.278
"""
import argparse
import csv
import glob
import io
import json
import math
import os
import re

DELIV = r"D:\BrainMaker\大镖客2解包分析\deliverable"
MOD = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\RDR2"
LWN = r"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
OUT = os.path.join(MOD, "Prefabs", "rdr2_lod_prefabs.xml")
IMPORT_DIR = os.path.join(MOD, "AssetSources", "ImortReady", "rdr2_lod_fragments")
ANCHOR_CSV = os.path.join(IMPORT_DIR, "prefab_anchors.csv")
PREFAB_DIR = os.path.join(DELIV, "manifests", "prefabs")
HEIGHTMAP = os.path.join(LWN, "Debug", "RD2构造相关", "terrain", "rdr2_height_4097.png")
SETTLE_CSV = os.path.join(DELIV, "manifests", "settlements_world_v2.csv")
SCALE_CSV = os.path.join(DELIV, "manifests", "settlement_scale.csv")

# 没有 LOD 碎片组、先用原版村庄地图缩略占位的据点（2026-11-09 用户裁定）
PLACEHOLDER_CODES = ("twi", "abe")
# 原版 Native/ModuleData/map_icons.xml：map_icon_village_a -> mesh_name="map_village_a", mesh_scale="0.45"
PLACEHOLDER_MESH = "map_village_a"
PLACEHOLDER_SCALE = 0.45

# 世界 → 地图单位（x 768/10768，y 576/8024，z 50/725.278）
SX = 768.0 / 10768.0
SY = 576.0 / 8024.0
SZ = 50.0 / (690.3469848632812 - (-34.93079376220703))


def metamesh_name(model):
    r"""资产名 = 模型名（2026-11-12 起统一用 _far 后缀）。

    ⚠️ 历史：模型名曾用 `_lod` / `_lodNN` 后缀，而 ModKit 导入时会把末尾的
    `_lod\d*` **剥掉**再注册 MetaMesh 名 —— 于是 135 段铁路全部注册成同一个
    名字 `jklm_1114_b_rai`，互相覆盖只剩 1 段（实测确认）。
    现在全量改名为 `_far` / `_farNN`，引擎不剥，名字一一对应，此处不再做转换。
    """
    return model


def imported_assets(module_root=None, assets_subdir="models"):
    """扫描 <模块>\\Assets\\<子目录> 下所有 *_geo.tpac → MetaMesh 资产名集合。

    2026-11-12 起模型名统一改 `_far` 后缀（引擎会剥 `_lodNN`，见 00-总纲.md），
    所以 `metamesh_name()` 现在是恒等；文件名即资产名。

    默认扫旧模块 `RDR2\\Assets\\models`；导入新模块时传
    `module_root=...\\RDR2Map, assets_subdir="meshes"`。
    """
    root = os.path.join(module_root or MOD, "Assets", assets_subdir)
    assets = set()
    if not os.path.isdir(root):
        return assets
    for dirpath, _dirs, files in os.walk(root):
        for fn in files:
            if not fn.endswith("_geo.tpac"):
                continue
            n = fn[:-len("_geo.tpac")]
            assets.add(metamesh_name(n))
    return assets


def known_missing():
    """交付里本来就没有网格的模型（允许缺件，不算回归）。"""
    out = set()
    for fn in ("missing_models.csv", "missing_sdn.csv"):
        p = os.path.join(IMPORT_DIR, fn)
        if not os.path.isfile(p):
            continue
        with io.open(p, encoding="utf-8-sig") as fh:
            for r in csv.DictReader(fh):
                n = (r.get("model_name") or "").strip()
                if n:
                    out.add(n)
    return out


def load_csv_rows(path):
    out = {}
    with io.open(path, encoding="utf-8-sig") as fh:
        for r in csv.DictReader(fh):
            out[r["code"]] = r
    return out


def heightmap_z(mx, my):
    """地图坐标 → 地形高度（与 terrain 生成口径一致：行 0 = 北）。"""
    from PIL import Image
    im = Image.open(HEIGHTMAP)
    W, H = im.size
    px = int(round(mx / 768.0 * (W - 1)))
    py = int(round((1.0 - my / 576.0) * (H - 1)))
    px = max(0, min(W - 1, px)); py = max(0, min(H - 1, py))
    return float(im.getpixel((px, py))) / 65535.0 * 50.0


def quat_to_euler(qx, qy, qz, qw):
    # 标准四元数旋转矩阵（列向量 = local 基在世界中的像）
    xx, yy, zz = qx * qx, qy * qy, qz * qz
    xy, xz, yz = qx * qy, qx * qz, qy * qz
    wx, wy, wz = qw * qx, qw * qy, qw * qz
    s = (1 - 2 * (yy + zz), 2 * (xy + wz), 2 * (xz - wy))       # side (X)
    f = (2 * (xy - wz), 1 - 2 * (xx + zz), 2 * (yz + wx))       # forward (Y)
    u = (2 * (xz + wy), 2 * (yz - wx), 1 - 2 * (xx + yy))       # up (Z)
    ex = math.asin(max(-1.0, min(1.0, f[2])))
    ey = math.atan2(-s[2], u[2])
    ez = math.atan2(-f[0], f[1])
    return ex, ey, ez


def fmt(v):
    return "%.3f" % v


def make_entity_namer(prefix):
    """实体名 = <prefix>_<模型名>；同 prefab 内重名追加 _02/_03…"""
    used = set()

    def pick(base):
        safe = re.sub(r"[^0-9A-Za-z_]", "_", base or "frag")
        cand = "%s_%s" % (prefix, safe)
        n = cand
        k = 2
        while n in used:
            n = "%s_%02d" % (cand, k)
            k += 1
        used.add(n)
        return n

    return pick


def frag_xml(f, asset_name, entity_name):
    p = f["pos_local"]
    r = f.get("rot", [0, 0, 0, 1])
    s = f.get("scale", [1, 1])
    ex, ey, ez = quat_to_euler(r[0], r[1], r[2], r[3])
    return (
        '\t\t\t<game_entity name="%s" mobility="1">\n'
        '\t\t\t\t<transform position="%s, %s, %s" rotation_euler="%s, %s, %s" scale="%s, %s, %s"/>\n'
        '\t\t\t\t<components>\n'
        '\t\t\t\t\t<meta_mesh_component name="%s"/>\n'
        '\t\t\t\t</components>\n'
        '\t\t\t</game_entity>\n'
        % (entity_name, fmt(p[0]), fmt(p[1]), fmt(p[2]),
           fmt(ex), fmt(ey), fmt(ez), fmt(s[0]), fmt(s[0]), fmt(s[1]), asset_name)
    )


def resolve_asset(f, have, by_hash):
    """碎片 → 实际导入的 MetaMesh 资产名。

    碎片里用原型名（`adl_barn_slod`），但模型库里有一部分按 hash 命名
    （`e_a47ae07c`）⇒ 名字查不到时按 `hash` 回退。返回 None = 真缺。
    """
    a = metamesh_name(f["name"])
    if a in have:
        return a
    r = by_hash.get((f.get("hash") or "").lower())
    if r:
        a2 = metamesh_name(r["name"])
        if a2 in have:
            return a2
    return None


def placeholder_body(code):
    """原版村庄地图缩略：它本身就是地图尺度的资产，直接引用，不套 map_scale。"""
    return (
        '\t\t\t<game_entity name="%s_placeholder" mobility="1">\n'
        '\t\t\t\t<transform position="0.000, 0.000, 0.000" rotation_euler="0.000, 0.000, 0.000" '
        'scale="%s, %s, %s"/>\n'
        '\t\t\t\t<components>\n'
        '\t\t\t\t\t<meta_mesh_component name="%s"/>\n'
        '\t\t\t\t</components>\n'
        '\t\t\t</game_entity>\n'
        % (code, fmt(PLACEHOLDER_SCALE), fmt(PLACEHOLDER_SCALE), fmt(PLACEHOLDER_SCALE), PLACEHOLDER_MESH)
    )


def anchor_map(g):
    """anchor_world → 地图坐标 (x, y, z)。"""
    ax, ay, az = g["anchor_world"]
    return ((ax + 6844.0) / 10768.0 * 768.0,
            (ay + 4800.0) / 8024.0 * 576.0,
            (az + 34.93079376220703) / (690.3469848632812 - (-34.93079376220703)) * 50.0)


def prefab_xml(gid, body, root_pos, map_scale=True, body_1to1=None):
    """一组 → 两段 prefab（地图 + 1:1）。

    `map_scale=False` 用于占位：原版村庄地图缩略本身就是地图尺度，不能再套 1:14 容器。
    """
    if body_1to1 is None:
        body_1to1 = body
    rx, ry, rz = root_pos
    scale = "%s, %s, %s" % (fmt(SX), fmt(SY), fmt(SZ))
    if map_scale:
        map_children = (
            '\t\t\t<game_entity name="map_scale" mobility="1">\n'
            '\t\t\t\t<transform position="0.000, 0.000, 0.000" rotation_euler="0.000, 0.000, 0.000" '
            'scale="%s"/>\n'
            '\t\t\t\t<children>\n%s\t\t\t\t</children>\n'
            '\t\t\t</game_entity>\n' % (scale, body)
        )
    else:
        map_children = body
    return (
        '\t<game_entity name="rdr2_%s_lod" mobility="1">\n'
        '\t\t<transform position="%s, %s, %s" rotation_euler="0.000, 0.000, 0.000"/>\n'
        '\t\t<children>\n%s\t\t</children>\n'
        '\t</game_entity>\n'
        '\t<game_entity name="rdr2_%s_lod_1to1" mobility="1">\n'
        '\t\t<transform position="0.000, 0.000, 0.000" rotation_euler="0.000, 0.000, 0.000"/>\n'
        '\t\t<children>\n%s\t\t</children>\n'
        '\t</game_entity>\n'
        % (gid, fmt(rx), fmt(ry), fmt(rz), map_children, gid, body_1to1)
    )


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--all", action="store_true", help="全部 56 组 + 2 个占位据点")
    ap.add_argument("--groups", default="", help="逗号分隔的组 id（默认 sdn）")
    ap.add_argument("--root-at-anchor", action="store_true",
                    help="地图 prefab 的根节点直接放锚点（场景实体放 0,0,0）")
    ap.add_argument("--out", default=OUT)
    ap.add_argument("--module-root", default="",
                    help="扫哪个模块的 Assets 判「已导入」（默认旧模块 RDR2）")
    ap.add_argument("--assets-subdir", default="models",
                    help="Assets 下的子目录名：旧模块 models / 新模块 meshes")
    ap.add_argument("--anchors-out", default="",
                    help="摆位表输出路径（默认与 --out 同目录）")
    a = ap.parse_args()

    if a.all:
        gids = [os.path.splitext(os.path.basename(p))[0]
                for p in sorted(glob.glob(os.path.join(PREFAB_DIR, "*.json")))]
        gids += [c for c in PLACEHOLDER_CODES if c not in gids]
    elif a.groups:
        gids = [x.strip() for x in a.groups.split(",") if x.strip()]
    else:
        gids = ["sdn"]

    have = imported_assets(a.module_root or None, a.assets_subdir)
    missing_known = known_missing()
    print("已导入 MetaMesh: %d 个；已知缺件: %d 个" % (len(have), len(missing_known)))

    # 碎片原型名 → manifest 模型名（hash 回退）：模型库里有一部分是 e_<hash>
    by_hash = {}
    for fn in ("models_export.csv", "models_export_extra.csv"):
        p = os.path.join(DELIV, "manifests", fn)
        if not os.path.isfile(p):
            continue
        with io.open(p, encoding="utf-8-sig") as fh:
            for r in csv.DictReader(fh):
                by_hash.setdefault(r["model_hash"].lower(), r)

    settle = load_csv_rows(SETTLE_CSV) if any(g in PLACEHOLDER_CODES for g in gids) else {}
    scale = load_csv_rows(SCALE_CSV) if any(g in PLACEHOLDER_CODES for g in gids) else {}

    chunks = []
    rows = []
    total_keep = total_skip = 0
    for gid in gids:
        if gid in PLACEHOLDER_CODES:
            s = settle.get(gid); sc = scale.get(gid)
            if not s or not sc:
                raise SystemExit("占位据点缺数据: %s（settlements_world_v2 / settlement_scale）" % gid)
            wx, wy = float(s["world_x"]), float(s["world_y"])
            mx = (wx + 6844.0) / 10768.0 * 768.0
            my = (wy + 4800.0) / 8024.0 * 576.0
            mz = heightmap_z(mx, my)
            root = (mx, my, mz) if a.root_at_anchor else (0.0, 0.0, 0.0)
            ph = placeholder_body(gid)
            ph1 = ph.replace("abe_placeholder", "abe_placeholder_1to1").replace("twi_placeholder", "twi_placeholder_1to1")
            chunks.append(prefab_xml(gid, ph, root, map_scale=False, body_1to1=ph1))
            rows.append({
                "group_id": gid, "group_type": s.get("btype", ""),
                "name_en": s.get("name_en", ""), "name_zh": s.get("name_zh", ""),
                "scene_name": "rdr2_" + gid,
                "anchor_world_x": "%.2f" % wx, "anchor_world_y": "%.2f" % wy,
                "anchor_world_z": "",
                "map_x": "%.3f" % mx, "map_y": "%.3f" % my, "map_z": "%.3f" % mz,
                "frags_total": 0, "frags_kept": 1, "frags_known_missing": 0,
                "placeholder": 1, "placeholder_mesh": PLACEHOLDER_MESH,
            })
            print("  %-8s PLACEHOLDER %s x%.2f  map=(%.1f, %.1f, %.1f)  (footprint %.0fx%.0f m)"
                  % (gid, PLACEHOLDER_MESH, PLACEHOLDER_SCALE, mx, my, mz,
                     float(sc["size_w"]), float(sc["size_d"])))
            continue

        p = os.path.join(PREFAB_DIR, gid + ".json")
        if not os.path.isfile(p):
            raise SystemExit("找不到组 JSON: %s" % p)
        g = json.load(io.open(p, encoding="utf-8"))
        frags = g.get("fragments", [])
        keep = []
        skipped = []
        for f in frags:
            asset = resolve_asset(f, have, by_hash)
            if asset:
                keep.append((f, asset))
            else:
                skipped.append(f)
        unexpected = [f for f in skipped if f["name"] not in missing_known]
        if unexpected:
            print("UNEXPECTED missing MetaMesh in group %s (not in known missing list):" % gid)
            for f in unexpected:
                print("  ", f["frag_id"], f["name"], "->", metamesh_name(f["name"]))
            raise SystemExit("FAIL: unexpected missing meshes; import TRF first or fix name mapping")

        total_keep += len(keep)
        total_skip += len(skipped)
        pick_map = make_entity_namer("rdr2_%s_lod" % gid)
        pick_one = make_entity_namer("rdr2_%s_lod_1to1" % gid)
        body = "".join(frag_xml(f, asset, pick_map(asset)) for f, asset in keep)
        body_1to1 = "".join(frag_xml(f, asset, pick_one(asset)) for f, asset in keep)
        mp = anchor_map(g)
        root = mp if a.root_at_anchor else (0.0, 0.0, 0.0)
        chunks.append(prefab_xml(gid, body, root, body_1to1=body_1to1))
        rows.append({
            "group_id": gid, "group_type": g.get("group_type", ""),
            "name_en": g.get("name_en", ""), "name_zh": g.get("name_zh", ""),
            "scene_name": g.get("scene_name", ""),
            "anchor_world_x": "%.2f" % g["anchor_world"][0],
            "anchor_world_y": "%.2f" % g["anchor_world"][1],
            "anchor_world_z": "%.2f" % g["anchor_world"][2],
            "map_x": "%.3f" % mp[0], "map_y": "%.3f" % mp[1], "map_z": "%.3f" % mp[2],
            "frags_total": len(frags), "frags_kept": len(keep), "frags_known_missing": len(skipped),
            "placeholder": 0, "placeholder_mesh": "",
        })
        print("  %-8s frags=%-3d kept=%-3d known_missing=%-2d map=(%.1f, %.1f, %.1f)"
              % (gid, len(frags), len(keep), len(skipped), mp[0], mp[1], mp[2]))

    xml = (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        "<!-- RDR2 LOD 远景碎片 prefab（%d 组，2026-11-09 自动生成；twi/abe 为方块占位） -->\n"
        "<prefabs>\n%s</prefabs>\n" % (len(gids), "".join(chunks))
    )
    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    with io.open(a.out, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(xml)
    print("wrote %s  %.1f KB  groups=%d frags_kept=%d known_missing=%d"
          % (a.out, os.path.getsize(a.out) / 1024, len(gids), total_keep, total_skip))

    if a.all and not a.root_at_anchor:
        # anchors 跟 prefab 放一起，别写回旧模块 RDR2（2026-11-12 改：
        # 老模块只读不写；以前这里固定写 RDR2\AssetSources\ImortReady 下）
        anchors = a.anchors_out or os.path.join(
            os.path.dirname(a.out), "rdr2_lod_prefab_anchors.csv")
        os.makedirs(os.path.dirname(anchors), exist_ok=True)
        with io.open(anchors, "w", encoding="utf-8-sig", newline="") as fh:
            w = csv.DictWriter(fh, fieldnames=list(rows[0].keys()))
            w.writeheader()
            w.writerows(rows)
        print("wrote", anchors)


if __name__ == "__main__":
    main()
