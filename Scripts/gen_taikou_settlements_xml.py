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
CULTURE = "Culture.ikoku"                # 兜底：服务性据点等**非日本据点**（普通据点一律走下面的映射）
# 🔴 据点文化 = 太阁5 的「地」→ 地域文化（2026-09-12 用户裁定）。
#    数据源 = `Settlements.csv` 的 `Chi` 列（由 `Scripts/import_settlement_kuni_chi.py` 从
#    `太阁日志/据点日志.md` 回填）。10 个「地」里 9 个日本地域 ↔ Taikou 9 个地域文化；
#    「海外」4 町（釜山/宁波/那霸/吕宋）在 EXCLUDE_IDS 里、不进世界，故不参与映射。
CHI2CULT = {
    "九州": "saikai", "四国": "nankai", "中部": "sanyo", "近畿": "kinai",
    "东北": "ou", "关东": "kanto", "东海": "tokai", "北陆": "hokuriku", "甲信": "tosan",
}
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
# 🔴 村型 id 必须 ∈ 引擎 1.2.12 `DefaultVillageTypes.RegisterAll()` 的 22 个 id（反编译实测，2026-09-12）：
#   wheat_farm / europe_horse_ranch / steppe_horse_ranch / desert_horse_ranch / battanian_horse_ranch /
#   sturgian_horse_ranch / vlandian_horse_ranch / lumberjack / clay_mine / salt_mine / iron_mine / fisherman /
#   cattle_farm / sheep_farm / swine_farm / vineyard / flax_plant / date_farm / olive_trees / silk_plant /
#   silver_mine / trapper
# 写错 id 不报错：`RegisterPresumedObject` 会造**推测桩村型**，其产出表为 null → 建世界初次产出时
# `CalculateDailyProductionAmount` 里 `item.IsMountable` 裸解引用 → NRE（雷 108 实测：原写 fishing / horse_ranch 两个都不存在）。
# 另：每个村型的 XML 产出物（`AddProductions` 按字符串 id 查）必须在世界物品集里，否则同样塞 null —— 见
# `Scripts/check_village_types_and_items.py`。
VILLAGE_TYPES = ["VillageType.silk_plant", "VillageType.vineyard", "VillageType.wheat_farm",
                 "VillageType.fisherman", "VillageType.iron_mine", "VillageType.europe_horse_ranch"]
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
    """读据点表 —— 🔴 **必须走 csv_dual（两行表头取第 2 行英文键）**。

    2026-09-12 修：本函数原先裸用 `csv.DictReader`（按**第 1 行中文标签**取键）。
    0.15 「CSV 两行表头」迁移时**漏了本脚本** → 中文标签行被当成表头、英文键行被当成一条数据
    → 行数 275 ≠ 274，断言当场崩（也就是说：这次迁移之后本生成器**一直是坏的**，只是没人重跑）。
    纪律：本仓读 `csv/` 下的表一律用 `csv_dual.read_table/dict_rows`，禁止裸写 DictReader。
    """
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from csv_dual import read_table
    _cn, _en, raw = read_table(path, head=2)
    cols = _en
    rows = [{k: (r[i] if i < len(r) else "").strip() for i, k in enumerate(cols)}
            for r in raw if any((x or "").strip() for x in r)]
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


def chi_to_culture(row):
    """据点文化 =「地」→ 地域文化。缺 Chi 列或该「地」无映射 → 硬错误（不静默落到 ikoku）。"""
    chi = (row.get("Chi") or "").strip()
    if chi not in CHI2CULT:
        raise SystemExit("[FATAL] 据点 %s 的「地」=%r 无文化映射"
                         "（先跑 Scripts/import_settlement_kuni_chi.py 回填 Chi 列）" % (row.get("id"), chi))
    return CHI2CULT[chi]


def build_settlement(row, era, parent_id, seq, owner_clan):
    key, fallback = era_name_key(row, era)
    s = ET.Element("Settlement", {
        "id": row["id"],
        "name": "{=%s}%s" % (key, fallback),
        # 🔴 2026-09-12 接真实归属：`owner` = 该代 `Clan_<年>`（XML 里 owner 收的是**家族**，
        #    不是城主本人——骑砍的 Settlement.OwnerClan 是 Clan）。无主据点由调用方按
        #    「继承最近有主据点（町 = 其 bound 城）的家族」传入，保证链不断。
        "owner": "Faction." + owner_clan,
        "posX": row["MOD_X"], "posY": row["MOD_Y"],
        "culture": "Culture." + chi_to_culture(row),
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


def owner_of(row, era, rows, towns):
    """该据点在 `era` 年的 owner 家族 id。

    规则（2026-09-12 接真实归属）：
      ① 有 `Clan_<年>` → 直接用（实测有主据点的家族 100% 在该代家族表里，0 例外）
      ② 无主（66 个町 + 墨俣城）→ **继承最近有主据点**的家族：
         町 = 它 bound 的那座城（骑砍惯例：村归其所属城）；其余 = 欧氏距离最近的有主据点。
         为什么必须给一个家族：骑砍的 `Settlement.OwnerClan` 是**非空**语义，缺 = 一串 NRE。
    """
    cl = (row.get("Clan_" + era) or "").strip()
    if cl:
        return cl
    if row["TK5Type"] == "町":
        cand = nearest_town(row, towns)
    else:
        cand = nearest_with_owner(row, rows, era)
    if cand is None:
        return "player_faction"                  # 极兜底（理论上到不了）
    cl = (cand.get("Clan_" + era) or "").strip()
    return cl or "player_faction"


def nearest_with_owner(row, rows, era):
    """欧氏距离最近、且该代有 Clan 的据点（同代、逐格确定性）。"""
    x, y = float(row["MOD_X"]), float(row["MOD_Y"])
    best, bd = None, None
    for r in rows:
        if r["id"] == row["id"] or not (r.get("Clan_" + era) or "").strip():
            continue
        d = (float(r["MOD_X"]) - x) ** 2 + (float(r["MOD_Y"]) - y) ** 2
        if bd is None or d < bd or (d == bd and r["id"] < best["id"]):
            best, bd = r, d
    return best


def build_file(rows, era):
    root = ET.Element("Settlements")
    root.append(ET.Comment(
        " Taikou 据点总表（%s 剧本）——生成物（铁律 22）：由 Scripts/gen_taikou_settlements_xml.py "
        "从 csv/Settlements.csv 生成，禁止手改。归属 = 该代 Clan_<年>；无主据点继承最近有主据点 "
        "（町 = 其所属城）的家族。 " % era))
    towns = [r for r in rows if r["TK5Type"] == "城"]
    for seq, row in enumerate(rows):
        parent = nearest_town(row, towns)["id"] if row["TK5Type"] == "町" else ""
        root.append(build_settlement(row, era, parent, seq, owner_of(row, era, rows, towns)))
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
