#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Taikou 据点 XML 生成器（六剧本，脚本生成——用户 2026-09-10 裁定「每个剧本的 xml 由脚本生成」）

🔴 生成物（铁律 22）：改内容 = 改本脚本或上游 `Settlements.csv`，然后重跑；禁止手改产出。

输入
----
  `Knowledge/太阁5/骑砍2织丰角色ID对应/csv/Settlements.csv`
    （id / TK5Type / Name_All / MOD_X / MOD_Y / {Name,Owner,Clan,Soldiers}_{1554..1598}）

产出（内容包 ModuleData）
------------------------
  settlements.xml            ← 基线（1560 开局；沿用现有 SubModule 注册，不破坏现行入口）
  settlements_{1554,1568,1575,1582,1598}.xml   ← 其余五剧本（名字差异；归属待 T4 接真实数据）

三类据点 → 组件（对齐骑砍制式，模板取自官方 SandBox settlements.xml）
  城 town_tk*    → <Town is_castle="false" level="3">  + town_complex（center/arena/tavern/lordshall）+ 3 CommonAreas
  町 village_tk* → <Village bound=最近城>              + village_complex（village_center）+ 3 CommonAreas
  里/砦 castle_tk* → <Town is_castle="true" level="1"> + castle_complex（center/lordshall/prison）
  内部场景/网格 = 官方 empire_* 占位（日式内部场景后置换，与「京」现状同款）

🔴 临时口径（用户裁定）：**所有据点 owner = Faction.clan_oda**（先看城池位置，不做真实归属）。
   → 接真实归属时：改本脚本读 CSV 的 Owner_/Clan_ 列（数据已备好），重跑即可。

Usage
-----
  python Scripts/gen_taikou_settlements_xml.py            # 重跑产出
  python Scripts/gen_taikou_settlements_xml.py --check    # 只校验磁盘产物是否最新（不一致 exit 1）
  python Scripts/gen_taikou_settlements_xml.py --module PATH
"""
import argparse
import csv
import io
import os
import re
import sys
import xml.etree.ElementTree as ET

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

TEMP_OWNER = "Faction.clan_oda"          # 🔴 临时：全体同一主人（用户裁定）
# ⚠️ 各剧本的家族段互斥（spclans.xml ↔ spclans_1582.xml），临时主人必须是**该剧本已定义**的家族：
#    1560 套有 clan_oda；1582 套只有 clan_g / player_faction → 1582 用 clan_g
TEMP_OWNER_BY_ERA = {"1582": "Faction.clan_g"}
CULTURE = "Culture.ikoku"
# 🔴 排除：4 个「外国港」（釜山/宁波/吕宋/那霸）——太阁地图左上角的装饰港口，
#    mod 的真实地理日本图上没有对应陆地 → 落海里会破坏导航/可点性。
#    数据仍保留在 Settlements.csv（列齐全），只是不进世界。
EXCLUDE_IDS = {"village_tk242", "village_tk243", "village_tk244", "village_tk245"}

BASELINE_ERA = "1560"                    # 基线文件 settlements.xml 用哪一代
ERAS = ["1554", "1560", "1568", "1575", "1582", "1598"]

# 官方帝国系占位资源（内部场景 + 背景网格；日式资源后置换）
MESH_TOWN = ("menu_empire_4", "wait_empire_town")
MESH_CASTLE = ("menu_empire_1", "wait_empire_town")
MESH_VILLAGE = ("gui_bg_village_empire", "wait_empire_village", "gui_bg_castle_empire")
VILLAGE_SCENES = ["empire_village_%03d" % i for i in (1, 2, 3, 4, 5, 6, 7, 8)]
VILLAGE_TYPES = ["VillageType.silk_plant", "VillageType.vineyard", "VillageType.wheat_farm",
                 "VillageType.fishing", "VillageType.iron_mine", "VillageType.horse_ranch"]
TOWN_LOCATIONS = [("center", "empire_town_g", 4), ("arena", "arena_empire_a", 1),
                  ("tavern", "empire_house_c_tavern_a", 1)]
CASTLE_LOCATIONS = [("center", "empire_siege_001", 4),
                    ("lordshall", "empire_castle_keep_a_l1_interior", 1),
                    ("prison", "empire_dungeon_a", 1)]
COMMON_AREAS = {"town": [("Backstreet", "TAIKOU_sett_area_backstreet", "Backstreet"),
                         ("Clearing", "TAIKOU_sett_area_clearing", "Clearing"),
                         ("Waterfront", "TAIKOU_sett_area_waterfront", "Waterfront")],
                "village": [("Pasture", "TAIKOU_sett_area_pasture", "Pasture"),
                            ("Thicket", "TAIKOU_sett_area_thicket", "Thicket"),
                            ("Bog", "TAIKOU_sett_area_bog", "Bog")]}


def registry_mb2_path():
    """读注册表 MB2_PATH（铁律 19：环境变量以注册表为准）"""
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


def read_csv(path):
    rows = list(csv.DictReader(io.open(path, encoding="utf-8-sig")))
    assert len(rows) == 274, "Settlements.csv 应 274 行，实际 %d" % len(rows)
    return [r for r in rows if r["id"] not in EXCLUDE_IDS]


def era_name_key(row, era):
    """该据点在该剧本用的本地化键：官方名（不随年代变）用基础键；改名过的用 _<era> 键"""
    base = row["Name_All"].split("|")[0]
    nm = row["Name_" + era]
    return ("TAIKOU_sett_" + row["id"], base) if nm == base else ("TAIKOU_sett_%s_%s" % (row["id"], era), nm)


def nearest_town(row, towns):
    """町 → 最近的城（世界米距离平方）"""
    x, y = float(row["MOD_X"]), float(row["MOD_Y"])
    return min(towns, key=lambda t: (float(t["MOD_X"]) - x) ** 2 + (float(t["MOD_Y"]) - y) ** 2)


def suffix(sid):
    """town_tk105 → tk105（组件 id 用）"""
    for p in ("town_", "village_", "castle_"):
        if sid.startswith(p):
            return sid[len(p):]
    return sid


def build_settlement(row, era, parent_id, seq):
    temp_owner = TEMP_OWNER_BY_ERA.get(era, TEMP_OWNER)
    key, fallback = era_name_key(row, era)
    s = ET.Element("Settlement", {
        "id": row["id"],
        "name": "{=%s}%s" % (key, fallback),
        "owner": temp_owner,
        "posX": row["MOD_X"], "posY": row["MOD_Y"],
        "culture": CULTURE,
    })
    comps = ET.SubElement(s, "Components")
    kind = row["TK5Type"]
    if kind == "城":
        ET.SubElement(comps, "Town", {
            "id": "town_comp_" + suffix(row["id"]), "is_castle": "false", "level": "3",
            "background_crop_position": "0.0", "background_mesh": MESH_TOWN[0],
            "wait_mesh": MESH_TOWN[1], "gate_rotation": "0.208",
            "max_prosperity": "70", "prosperity": "5100"})
        locs = ET.SubElement(s, "Locations", {"complex_template": "LocationComplexTemplate.town_complex"})
        loc_spec, areas = TOWN_LOCATIONS, COMMON_AREAS["town"]
    elif kind in ("里", "砦"):
        ET.SubElement(comps, "Town", {
            "id": "castle_comp_" + suffix(row["id"]), "is_castle": "true", "level": "1",
            "background_crop_position": "0.0", "background_mesh": MESH_CASTLE[0],
            "wait_mesh": MESH_CASTLE[1], "gate_rotation": "0.908", "prosperity": "420"})
        locs = ET.SubElement(s, "Locations", {"complex_template": "LocationComplexTemplate.castle_complex"})
        loc_spec, areas = CASTLE_LOCATIONS, None
    else:                                                     # 町 → Village
        ET.SubElement(comps, "Village", {
            "id": "village_comp_" + suffix(row["id"]),
            "village_type": VILLAGE_TYPES[seq % len(VILLAGE_TYPES)],
            "hearth": "200", "gate_rotation": "0.008",
            "bound": "Settlement." + parent_id,
            "background_crop_position": "0.0", "background_mesh": MESH_VILLAGE[0],
            "wait_mesh": MESH_VILLAGE[1], "castle_background_mesh": MESH_VILLAGE[2]})
        locs = ET.SubElement(s, "Locations", {"complex_template": "LocationComplexTemplate.village_complex"})
        loc_spec, areas = [("village_center", VILLAGE_SCENES[seq % len(VILLAGE_SCENES)], 1)], COMMON_AREAS["village"]
    for lid, scene, n in loc_spec:
        attrs = {"id": lid, "scene_name": scene}
        for i in range(1, n):
            attrs["scene_name_%d" % i] = scene
        ET.SubElement(locs, "Location", attrs)
    if areas:
        ca = ET.SubElement(s, "CommonAreas")
        for typ, akey, afb in areas:
            ET.SubElement(ca, "Area", {"type": typ, "name": "{=%s}%s" % (akey, afb)})
    return s


def build_file(rows, era):
    root = ET.Element("Settlements")
    root.append(ET.Comment(
        " Taikou 据点总表（%s 剧本）——生成物（铁律 22）：由 Scripts/gen_taikou_settlements_xml.py "
        "从 csv/Settlements.csv 生成，禁止手改。临时口径：owner 全体 = %s。 " % (era, TEMP_OWNER)))
    towns = [r for r in rows if r["TK5Type"] == "城"]
    for seq, row in enumerate(rows):
        parent = nearest_town(row, towns)["id"] if row["TK5Type"] == "町" else ""
        root.append(build_settlement(row, era, parent, seq))
    # 退休据点（引擎硬编码 Settlement.Find("retirement_retreat")，缺 = 每小时 tick NRE；雷 31）
    root.append(ET.Comment(" 服务性据点：官方 RetirementCampaignBehavior 硬编码查找（雷 31） "))
    ret = ET.SubElement(root, "Settlement", {
        "id": "retirement_retreat", "name": "{=TAIKOU_sett_retreat}The Retreat",
        "posX": "1090.0", "posY": "500.0", "culture": CULTURE})
    comps = ET.SubElement(ret, "Components")
    ET.SubElement(comps, "RetirementSettlementComponent", {
        "id": "retirement_component", "map_icon": "bandit_hideout_b",
        "background_crop_position": "0.0", "background_mesh": "gui_bg_village_battania",
        "wait_mesh": "retirement_wait"})
    locs = ET.SubElement(ret, "Locations", {"complex_template": "LocationComplexTemplate.retreat_complex"})
    ET.SubElement(locs, "Location", {"id": "retirement_retreat", "scene_name": "scn_retirement",
                                     "max_prosperity": "100"})
    return root


def serialize(root):
    ET.indent(root, space="  ")
    return ('<?xml version="1.0" encoding="utf-8"?>\n'
            + ET.tostring(root, encoding="unicode") + "\n")


def registered_paths(module):
    """读 SubModule.xml：哪些 settlements* 段已注册 → 只写已注册的，避免产出孤儿数据文件
    （孤儿 = ModuleData 有文件但无段加载 → 运行时不存在的静默失效；check_module_registration 会报）"""
    p = os.path.join(module, "SubModule.xml")
    if not os.path.exists(p):
        return None
    reg = set()
    for m in re.finditer(r'<XmlName\s+id="Settlements"\s+path="([^"]+)"', io.open(p, encoding="utf-8").read()):
        reg.add(m.group(1))
    return reg


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="只校验产物是否最新")
    ap.add_argument("--module", default=None, help="内容包路径（默认按 MB2_PATH 推 Taikou）")
    ap.add_argument("--csv", default=None)
    args = ap.parse_args()

    here = os.path.dirname(os.path.abspath(__file__))
    proj = os.path.dirname(here)
    csv_path = args.csv or os.path.join(
        proj, "Knowledge", "太阁5", "骑砍2织丰角色ID对应", "csv", "Settlements.csv")
    module = args.module or os.path.join(registry_mb2_path(), "Modules", "Taikou")
    md = os.path.join(module, "ModuleData")
    if not os.path.isdir(md):
        print("找不到内容包 ModuleData：%s" % md, file=sys.stderr)
        return 2

    rows = read_csv(csv_path)
    reg = registered_paths(module)
    stale, written, skipped = [], [], []
    for era in ERAS:
        fn = "settlements.xml" if era == BASELINE_ERA else "settlements_%s.xml" % era
        path = os.path.join(md, fn)
        if reg is not None and fn.split(".")[0] not in reg:
            skipped.append("%s(%s)" % (fn, era))
            continue
        text = serialize(build_file(rows, era))
        old = io.open(path, encoding="utf-8-sig").read() if os.path.exists(path) else None
        if old == text:
            continue
        if args.check:
            stale.append(fn)
        else:
            io.open(path, "w", encoding="utf-8").write(text)
            written.append(fn)
    if args.check:
        if stale:
            print("产物与生成器不一致（需重跑）：%s" % ", ".join(stale))
            return 1
        print("OK：已注册剧本的据点 XML 均为最新")
        return 0
    print("写出：%s" % (", ".join(written) if written else "（无变化）"))
    if skipped:
        print("跳过（SubModule 未注册该段 → 先加 GameType 再落盘）：%s" % ", ".join(skipped))
    print("据点 %d 个（城 %d / 町 %d / 里砦 %d）+ 退休据点；临时主人 %s"
          % (len(rows), sum(r["TK5Type"] == "城" for r in rows),
             sum(r["TK5Type"] == "町" for r in rows),
             sum(r["TK5Type"] in ("里", "砦") for r in rows), TEMP_OWNER))
    return 0


if __name__ == "__main__":
    sys.exit(main())
