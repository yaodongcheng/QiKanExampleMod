#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Taikou 据点批量进场景（xscene 实体注入）

🔴 本脚本改**场景文件**（Main_map/scene.xscene）——运行前自动备份到 SceneObj/Backups/。

做什么
------
按 `csv/Settlements.csv` 的 MOD_X/MOD_Y，给每个据点生成一棵官方制式的实体树：
  campaign_icon_capsule_<n>   （Z=20 哨兵；带 "Town Entity Manager" 脚本 —— 官方 capsule 惯例）
    └ <据点 id>               （name 必须 = 据点 StringId；tags 按类型 town/village/castle）
        ├ bo_town / bo_castle / bo_village    （射线拾取碰撞体，官方 bo_sphere_collider）
        ├ gate_position                        （tag main_map_city_gate；城/城堡有）
        └ town_circle_decal                    （tag map_settlement_circle；黄圈=可见可点）

🔴 例外：4 个「外国港」（釜山/宁波/吕宋/那霸 = 太阁地图左上角的装饰港口）在 mod 地图上没有对应
   陆地 → **不生成实体**（落海里会破坏导航/可点性），实体集合 = 274 - 4 = 270。

用法
----
  python Scripts/place_settlements.py            # 备份 + 注入/重刷
  python Scripts/place_settlements.py --dry-run  # 只报告，不写文件
"""
import argparse
import csv
import io
import os
import re
import shutil
import sys
import time
import xml.etree.ElementTree as ET

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
try:
    from taikou_terrain import Terrain                 # Z 贴合地形（缺高度图则退回固定 20）
    _TERRAIN = Terrain()
except Exception as _e:
    _TERRAIN = None
    print("[WARN] 地形模块不可用，Z 用固定 20：%s" % _e)

FOREIGN_PORTS = {"village_tk242", "village_tk243", "village_tk244", "village_tk245"}  # 釜山/宁波/那霸/吕宋
CAPSULE_BASE = 100          # campaign_icon_capsule_101 起（避开官方编号）
T = "\t"

# 🔴 mesh 原点 ≠ 底部也 ≠ 中心 —— 实测值（tpaccli dump 出 OBJ 量顶点，2026-09-11）：
#   mi_aserai_tower_2   Z ∈ [-0.86, +1.16]（高 2.01m）→ 底面在原点下方 0.86m
#   village_empire_1    Z ∈ [-0.82, +1.00]（高 1.82m）→ 底面在原点下方 0.82m
#   据点实体 scale = (2, 2, 1) → Z 不缩放，故偏移量照用。
#   ⇒ 让「底面正好贴地」的实体 Z = 地形高度 + 该 mesh 的底面偏移 + 余量。
#   量法（换日式图标后重测一次）：tpaccli dump --packdir <MB2>/Modules/Native/AssetPackages \
#                                  --filter <mesh名> --out <dir> --format obj
MESH_BASE_OFFSET = {"mi_aserai_tower_2": 0.86, "village_empire_1": 0.82}
MESH_Z_CLEARANCE = 0.0      # 底面与地面的余量：0 = 正好接触；正 = 浮起；负 = 略埋


def registry_mb2_path():
    if sys.platform == "win32":
        import winreg
        for hive, sub in ((winreg.HKEY_CURRENT_USER, "Environment"),
                          (winreg.HKEY_LOCAL_MACHINE,
                           r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")):
            try:
                with winreg.OpenKey(hive, sub) as k:
                    v, _ = winreg.QueryValueEx(k, "MB2_PATH")
                    if v:
                        return v
            except OSError:
                continue
    return os.environ.get("MB2_PATH", "")


def tag_block(indent, tags):
    out = ["%s<tags>" % indent]
    out += ["%s%s<tag name=\"%s\"/>" % (indent, T, t) for t in tags]
    out.append("%s</tags>" % indent)
    return out


def collider(indent, name):
    """拾取碰撞体：🔴 引擎/检查器认的是 **tag** <tag name="bo_town"/>（名字只是惯例）"""
    out = ["%s<game_entity name=\"%s\" old_prefab_name=\"%s\" mobility=\"1\">" % (indent, name, name)]
    out += tag_block(indent, [name])
    out += ["%s%s<physics shape=\"bo_sphere_collider\">" % (indent, T),
            "%s%s%s<body_flags>" % (indent, T, T),
            "%s%s%s%s<body_flag name=\"only_collide_with_raycast\"/>" % (indent, T, T, T),
            "%s%s%s</body_flags>" % (indent, T, T),
            "%s%s</physics>" % (indent, T),
            "%s</game_entity>" % indent]
    return out


def circle_decal(indent):
    return ["%s<game_entity name=\"town_circle_decal\" old_prefab_name=\"town_circle_decal\" mobility=\"1\">" % indent,
            "%s%s<tags>" % (indent, T), "%s%s%s<tag name=\"map_settlement_circle\"/>" % (indent, T, T),
            "%s%s</tags>" % (indent, T),
            "%s%s<transform position=\"0.000, 0.000, 2.600\" rotation_euler=\"0.000, 0.000, 2.301\" "
            "scale=\"0.936, 0.936, 1.151\"/>" % (indent, T),
            "%s%s<components>" % (indent, T),
            "%s%s%s<decal_component material=\"decal_city_circle_a\" argument=\"1.000, 1.000, 0.000, 0.000\"/>" % (indent, T, T),
            "%s%s</components>" % (indent, T),
            "%s</game_entity>" % indent]



def capsule_block(seq, sid, kind, x, y):
    """一棵完整实体树（按类型出 tags / 子件）

    🔴 capsule 名 = campaign_icon_capsule_<n>_<据点 id>（用户 2026-09-11：带名字后缀，ModKit 里一眼认人）
    🔴 capsule 原点 = mesh 原点（用户 2026-09-11 裁定：不要偏移）—— 子实体相对偏移 = (0,0,0)。
    🔴 贴地：mesh 原点在**底面之上**（tower 0.86m / village 0.82m，见 MESH_BASE_OFFSET 实测值），
       故实体 Z = 地形高度 + 该 mesh 底面偏移 + MESH_Z_CLEARANCE（0 = 底面正好接触地面）。
    """
    n = CAPSULE_BASE + seq
    mesh = "mi_aserai_tower_2" if kind in ("城", "里", "砦") else "village_empire_1"
    ent_scale = (2.000, 2.000, 1.000) if kind in ("城", "里", "砦") else (1.000, 1.000, 1.000)
    mesh_scale = (1.000, 1.000, 1.000) if kind in ("城", "里", "砦") else (0.440, 0.440, 0.440)
    base_off = MESH_BASE_OFFSET.get(mesh, 0.0) * mesh_scale[2]      # 底面偏移随 Z 缩放
    z = 20.0 if _TERRAIN is None else _TERRAIN.height_at(x, y) + base_off + MESH_Z_CLEARANCE
    o = ["%s<game_entity name=\"campaign_icon_capsule_%d_%s\" old_prefab_name=\"\" mobility=\"1\">" % (T * 2, n, sid),
         "%s%s<transform position=\"%.3f, %.3f, %.3f\" rotation_euler=\"0.000, 0.000, 0.000\"/>" % (T * 2, T, x, y, z),
         "%s%s<scripts>" % (T * 2, T),
         "%s%s%s<script name=\"Town Entity Manager\">" % (T * 2, T, T),
         "%s%s%s%s<variables>" % (T * 2, T, T, T),
         "%s%s%s%s%s<variable name=\"Override Factor Color\" value=\"false\"/>" % (T * 2, T, T, T, T),
         "%s%s%s%s%s<variable name=\"Factor Color\" value=\"1.000, 1.000, 1.000, 1.000\"/>" % (T * 2, T, T, T, T),
         "%s%s%s%s</variables>" % (T * 2, T, T, T),
         "%s%s%s</script>" % (T * 2, T, T),
         "%s%s</scripts>" % (T * 2, T),
         "%s%s<children>" % (T * 2, T),
         "%s%s%s<game_entity name=\"%s\" old_prefab_name=\"\" mobility=\"1\">" % (T * 2, T, T, sid)]
    tags = ["town"] if kind == "城" else (["castle"] if kind in ("里", "砦") else ["village"])
    o += tag_block(T * 3, tags)
    o.append("%s%s%s<transform position=\"0.000, 0.000, 0.000\" rotation_euler=\"0.000, 0.000, 0.000\" "
             "scale=\"%.3f, %.3f, %.3f\"/>" % ((T * 2, T, T) + ent_scale))
    o += ["%s%s%s<children>" % (T * 2, T, T),
          # 网格挂子实体（照官方村的做法：实体 scale ~1 + 图标子件 scale 0.44）；
          # 城/城堡 = 实体 (2,2,1) × 图标 (1,1,1) = 与已验证的「京」同款外观
          "%s%s%s%s<game_entity name=\"map_icon\" old_prefab_name=\"\" mobility=\"1\">" % (T * 2, T, T, T),
          "%s%s%s%s%s<transform position=\"0.000, 0.000, 0.000\" rotation_euler=\"0.000, 0.000, 0.000\" "
          "scale=\"%.3f, %.3f, %.3f\"/>" % ((T * 2, T, T, T, T) + mesh_scale),
          "%s%s%s%s%s<components>" % (T * 2, T, T, T, T),
          "%s%s%s%s%s%s<meta_mesh_component name=\"%s\"/>" % (T * 2, T, T, T, T, T, mesh),
          "%s%s%s%s%s</components>" % (T * 2, T, T, T, T),
          "%s%s%s%s</game_entity>" % (T * 2, T, T, T)]
    # 城与里/砦 同为 Town 组件（is_castle 区分）→ 引擎/检查器统一认 tag bo_town（官方城堡也是这棵树）
    o += collider(T * 4, "bo_town" if kind in ("城", "里", "砦") else "bo_village")
    if kind in ("城", "里", "砦"):
        o += ["%s<game_entity name=\"gate_position\" old_prefab_name=\"gate_position\" mobility=\"1\">" % (T * 4)]
        o += tag_block(T * 4, ["main_map_city_gate"])
        o.append("%s%s<transform position=\"0.000, -2.000, 0.000\" rotation_euler=\"0.000, 0.000, 0.561\"/>" % (T * 4, T))
        o.append("%s</game_entity>" % (T * 4))
    o += circle_decal(T * 4)
    o += ["%s%s%s</children>" % (T * 2, T, T),
          "%s%s%s</game_entity>" % (T * 2, T, T),
          "%s%s</children>" % (T * 2, T),
          "%s</game_entity>" % (T * 2)]
    return "\n".join(o)


def strip_previous(text):
    """删掉上次生成的 capsule 块（名字 = campaign_icon_capsule_<范围号>[_<据点id>]）与旧的手摆 town_kyoto 块"""
    removed = 0
    for name in ["campaign_icon_capsule_%d" % (CAPSULE_BASE + i) for i in range(1, 400)]:
        pat = re.compile(r"\n\t\t<game_entity name=\"%s(?:_[A-Za-z0-9_]+)?\" old_prefab_name=\"\".*?\n\t\t</game_entity>"
                         % re.escape(name), re.S)
        text, k = pat.subn("", text)
        removed += k
    # 旧京胶囊（含 town_kyoto 子实体）：整块删掉——它已被 town_tk105 / village_tk212 取代
    pat = re.compile(r"\n\t\t<game_entity name=\"campaign_icon_capsule_1\" old_prefab_name=\"\".*?\n\t\t</game_entity>",
                     re.S)
    text, k = pat.subn("", text)
    return text, removed + k


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--module", default=None)
    ap.add_argument("--csv", default=None)
    args = ap.parse_args()

    here = os.path.dirname(os.path.abspath(__file__))
    proj = os.path.dirname(here)
    csv_path = args.csv or os.path.join(
        proj, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "Settlements.csv")
    module = args.module or os.path.join(registry_mb2_path(), "Modules", "Taikou")
    scene = os.path.join(module, "SceneObj", "Main_map", "scene.xscene")
    if not os.path.exists(scene):
        print("找不到场景：%s" % scene, file=sys.stderr)
        return 2

    rows = [r for r in csv.DictReader(io.open(csv_path, encoding="utf-8-sig"))
            if r["id"] not in FOREIGN_PORTS]
    text = io.open(scene, encoding="utf-8-sig").read()
    text, removed = strip_previous(text)

    blocks = [capsule_block(i, r["id"], r["TK5Type"], float(r["MOD_X"]), float(r["MOD_Y"]))
              for i, r in enumerate(rows, start=1)]
    anchor = "\n\t\t<game_entity name=\"border_min\""
    if anchor not in text:
        print("找不到插入锚点 border_min", file=sys.stderr)
        return 3
    text = text.replace(anchor, "\n" + "\n".join(blocks) + anchor, 1)

    try:
        ET.fromstring(text)
    except ET.ParseError as e:
        print("注入后 XML parse 失败：%s" % e, file=sys.stderr)
        return 4

    print("据点实体 %d 个（跳过外国港 %d 个：%s）"
          % (len(rows), len(FOREIGN_PORTS), ", ".join(sorted(FOREIGN_PORTS))))
    print("清理旧块 %d 个（含旧京胶囊）" % removed)
    if args.dry_run:
        print("--dry-run：未写文件")
        return 0

    stamp = time.strftime("%Y%m%d_%H%M%S")
    bak = os.path.join(module, "SceneObj", "Backups", "Main_map_" + stamp, "scene.xscene")
    os.makedirs(os.path.dirname(bak), exist_ok=True)
    shutil.copy2(scene, bak)
    io.open(scene, "w", encoding="utf-8").write(text)
    print("已备份 → %s" % bak)
    print("已写入 → %s（%d 字节）" % (scene, len(text.encode("utf-8"))))
    return 0


if __name__ == "__main__":
    sys.exit(main())
