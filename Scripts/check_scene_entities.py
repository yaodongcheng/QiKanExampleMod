#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Scene-entity checker (自定义世界「地图场景必备实体」离线体检 — 雷 28/34/37/42/53 族防线)
========================================================================
为什么需要它：地图场景是**最容易静默出错**的地方——缺实体不报错、不崩溃，只是"玩法悄悄没了"：
  · 缺 border_min/border_max → 相机被钳进引擎兜底盒 900×900（1.2.12 静默空气墙 / 1.5.x 进图崩）
  · 缺 CampaignMapSiegePrefabEntityCache → MapScreen.OnInitialize NRE（引擎硬查询无守卫）
  · 缺 bo_town（只碰射线碰撞体）→ 黄圈不显示 / 点击无反应 / 进不了城，且探针全绿极具迷惑性
  · 据点没有同名场景实体 → 不进距离缓存 → 全局最大据点距恒 0 → 家宅永远选不中（雷 53）
本脚本把这一族检查全部提前到离线（替代运行期 [MapBorder] 诊断日志）。

规则：
  1. 场景文件存在（缺 = 配置错）
  2. border_min / border_max 两份实体在位，且取值与 `<terrain>` 规格自洽
     （期望 max = node_dimension_x×node_size, node_dimension_y×node_size；min = 0,0）
  3. 引擎硬查询的 5 个地图脚本实体在位（按 `<script name=...>` 全场景匹配）
  4. navmesh 可用：`navmesh.bin` 存在 或 `nav_mesh_auto_generated_=true`
  5. settlements.xml 每个据点 → 场景里存在同名 game_entity（缺 = 雷 53 家族）
  6. 城镇（含 `<Town>` 组件）实体子树里必须有交互链三件套（缺 = 雷 37）：
     bo_town / map_settlement_circle / main_map_city_gate
     banner_pos（map_banner_placeholder）缺失只提示（纯视觉，不影响玩法）

严重级：ERROR = exit 1 阻断（必须修）；WARN = 只提示。

Usage:
  python Scripts/check_scene_entities.py [--module PATH] [--official-root PATH] [--scene NAME]
  --official-root 缺省 = 读注册表 MB2_PATH（铁律 19：环境变量以注册表为准）
Exit: 0 无 ERROR / 1 有 ERROR / 2 fatal。
"""
import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# GBK 控制台下 ✓ 等符号会崩——统一 UTF-8 输出（py3.7+）
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

if sys.platform == "win32":
    import winreg


def registry_mb2_path():
    """读注册表 MB2_PATH（User 级优先，再 Machine）——铁律 19。"""
    for hive, sub in ((winreg.HKEY_CURRENT_USER, "Environment"),
                      (winreg.HKEY_LOCAL_MACHINE,
                       r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment")):
        try:
            with winreg.OpenKey(hive, sub) as k:
                val, _ = winreg.QueryValueEx(k, "MB2_PATH")
                if val:
                    return val
        except OSError:
            continue
    return None


# 引擎硬查询（无 null 守卫）的地图脚本实体——缺 = 进图崩或玩法链断
REQUIRED_SCRIPTS = [
    "CampaignMapSiegePrefabEntityCache",   # MapScreen.OnInitialize 硬查询，缺 = NRE（雷 28）
    "MapColorGradeManager",
    "SettlementPositionScript",            # 距离缓存读写者（雷 53 同族）
    "Town Scene Manager",
    "SceneLeveler",
]

# 城镇交互链三件套（缺任一 = 黄圈/点击/进城断链，雷 37）+ 旗帜占位（纯视觉，只提示）
REQUIRED_TOWN_TAGS = {
    "bo_town": "拾取碰撞体（only_collide_with_raycast）——缺 = 黄圈不显示/点不动/进不了城",
    "map_settlement_circle": "黄圈贴花——缺 = 无视觉圈",
    "main_map_city_gate": "城门定位——缺 = 门相关逻辑失效",
}
OPTIONAL_TOWN_TAGS = {
    "map_banner_placeholder": "旗帜占位（纯视觉，不影响玩法）",
}

# 「需要有地图实体」的据点组件——官方实测（493 据点）：Town 120 / Village 273 / Hideout 99 **全部**有实体；
# 唯一没有实体的是只带 RetirementSettlementComponent 的 retirement_retreat（官方 Main_map 里同样 0 命中）
# ⇒ 服务性据点不在地图上是**官方设计**，不是缺陷。判定按「组件里有没有这几类」。
MAP_COMPONENT_TAGS = ("Town", "Village", "Castle", "Hideout")


def parse_vec3(text):
    if not text:
        return None
    try:
        parts = [float(x) for x in text.replace(",", " ").split()]
        return parts if len(parts) >= 2 else None
    except ValueError:
        return None


def walk_entities(node):
    """所有 game_entity（含 children 里的嵌套）。"""
    for e in node.findall("game_entity"):
        yield e
        children = e.find("children")
        if children is not None:
            yield from walk_entities(children)


def entity_tags(e):
    tags = e.find("tags")
    return {t.get("name") for t in tags.findall("tag")} if tags is not None else set()


def scene_scripts(root):
    return {s.get("name") for s in root.iter("script")}


def main():
    ap = argparse.ArgumentParser(description="Content-pack map-scene entity checker")
    ap.add_argument("--module",
                    default=r"H:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Taikou")
    ap.add_argument("--official-root", default=None, help="游戏根（缺省=注册表 MB2_PATH）")
    ap.add_argument("--scene", default="Main_map", help="场景目录名（默认 Main_map = 战役大地图）")
    args = ap.parse_args()

    mod_path = Path(args.module)
    if not mod_path.is_dir():
        print(f"[FATAL] module not found: {mod_path}")
        return 2

    scene_dir = mod_path / "SceneObj" / args.scene
    scene_file = scene_dir / "scene.xscene"
    print(f"Module : {mod_path}")
    print(f"Scene  : {scene_file}")

    errors, warns = [], []

    if not scene_file.is_file():
        print(f"[FATAL] scene.xscene not found: {scene_file}")
        return 2

    try:
        root = ET.parse(str(scene_file)).getroot()
    except Exception as e:
        print(f"[FATAL] scene.xscene 解析失败: {e}")
        return 2

    entities_node = root.find("entities")
    if entities_node is None:
        print("[FATAL] 场景无 <entities> 节点")
        return 2

    all_entities = list(walk_entities(entities_node))
    by_name = {}
    for e in all_entities:
        by_name.setdefault(e.get("name"), []).append(e)
    print(f"实体总数: {len(all_entities)}\n")

    # ── 1. border_min / border_max（雷 34）──
    terrain = root.find("terrain")
    dims = None
    if terrain is not None:
        try:
            dims = (float(terrain.get("node_dimension_x")) * float(terrain.get("node_size")),
                    float(terrain.get("node_dimension_y")) * float(terrain.get("node_size")))
        except (TypeError, ValueError):
            dims = None

    print("== 相机边界实体（缺 = 空气墙 900×900 / 1.5.x 进图崩，雷 34） ==")
    for name, expect in (("border_min", (0.0, 0.0)), ("border_max", dims)):
        ents = by_name.get(name)
        if not ents:
            errors.append(f"缺实体 {name}")
            print(f"  [ERROR] 缺 {name}")
            continue
        tf = ents[0].find("transform")
        pos = parse_vec3(tf.get("position")) if tf is not None else None
        if pos is None:
            warns.append(f"{name} 无 transform/position")
            print(f"  [WARN]  {name} 没有 transform position")
            continue
        if expect and (abs(pos[0] - expect[0]) > 1.0 or abs(pos[1] - expect[1]) > 1.0):
            errors.append(f"{name} 取值与地形规格不符")
            print(f"  [ERROR] {name}=({pos[0]:.1f},{pos[1]:.1f}) 与地形规格 ({expect[0]:.1f},{expect[1]:.1f}) 不符"
                  f" —— 地形 = node_dimension×node_size")
        else:
            print(f"  [ OK ] {name}=({pos[0]:.1f},{pos[1]:.1f})")
    if dims is None:
        warns.append("terrain 节点缺失或规格不可读——border_max 无法对账")

    # ── 2. 引擎硬查询脚本实体（雷 28）──
    print("\n== 引擎硬查询的地图脚本实体（缺 = 进图崩/玩法链断，雷 28） ==")
    present = scene_scripts(root)
    for s in REQUIRED_SCRIPTS:
        if s in present:
            print(f"  [ OK ] {s}")
        else:
            errors.append(f"缺脚本实体 {s}")
            print(f"  [ERROR] 缺 {s}")

    # ── 3. navmesh ──
    print("\n== navmesh（缺 = native AccessViolation） ==")
    nav_bin = scene_dir / "navmesh.bin"
    auto = False
    env = root.find("environment_properties")
    if env is not None:
        for p in env.findall("property"):
            if p.get("name") == "nav_mesh_auto_generated_":
                auto = (p.get("value") or "").lower() == "true"
    if nav_bin.is_file():
        print(f"  [ OK ] navmesh.bin（{nav_bin.stat().st_size:,} bytes）")
    elif auto:
        print("  [ OK ] 无 navmesh.bin，但 nav_mesh_auto_generated_=true（引擎自动生成）")
    else:
        errors.append("无 navmesh.bin 且未标 nav_mesh_auto_generated_")
        print("  [ERROR] 既无 navmesh.bin 也没标 auto_generated")

    # ── 4. 据点实体（雷 53 / 雷 37）──
    sett_file = mod_path / "ModuleData" / "settlements.xml"
    print("\n== 据点实体 + 城镇交互链（缺 = 家宅选不中/点不动城，雷 53 & 37） ==")
    if not sett_file.is_file():
        # 🔴 这里**必须报错，不能跳过**（2026-09-11 用户裁定）。理由：
        #   基线文件名 settlements.xml 是本仓库的**约定**（BASELINE_ERA=1560 走无后缀名），
        #   约定写在生成器里、检查器却当固定名用——一旦约定漂移（换基线年份 / 有人给基线加
        #   `_1560` 后缀），本检查会**静默跳过**，看起来仍然全绿，而据点实体这块根本没人查。
        #   改成硬报错：文件不在 = 约定漂移 = 要先对齐生成器的 BASELINE_ERA 与 SubModule 的 path。
        #   （另注：官方地图编辑器也按固定名读写 settlements.xml，见
        #    Knowledge/bannerlordmodding_lt/editor/editor.md——基线名不是说改就能改的。）
        errors.append(f"settlements.xml 不存在（{sett_file}）——据点实体检查无法进行。"
                      f"本仓库约定「基线时代（BASELINE_ERA=1560）用无后缀名 settlements.xml」，"
                      f"改名/换基线必须同步改本检查；改名还会让官方地图编辑器保存地图时崩。")
        print(f"  [ERROR] settlements.xml 不存在：{sett_file}")
        print("          约定：基线时代走无后缀名（生成器 BASELINE_ERA）；改名要同步本检查 + SubModule path")
    else:
        try:
            sroot = ET.parse(str(sett_file)).getroot()
        except Exception as e:
            errors.append(f"settlements.xml 解析失败: {e}")
            print(f"  [ERROR] settlements.xml 解析失败: {e}")
            sroot = None
        for s in (sroot.findall("Settlement") if sroot is not None else []):
            sid = s.get("id") or "<无 id>"
            comps = s.find("Components")
            comp_kinds = [c.tag for c in comps] if comps is not None else []
            is_town = "Town" in comp_kinds
            needs_map_entity = any(k in comp_kinds for k in MAP_COMPONENT_TAGS)
            ents = by_name.get(sid)
            if not ents:
                if not needs_map_entity:
                    # 服务性据点（如 retirement_retreat）——官方同样没有地图实体，属设计
                    print(f"  [INFO] 据点 {sid} 无地图实体，组件={comp_kinds or '（无）'}——"
                          f"非地图据点，官方亦如此（退休据点等），跳过")
                    continue
                errors.append(f"据点 {sid} 无同名场景实体")
                print(f"  [ERROR] 据点 {sid}（组件={comp_kinds}）在场景里没有同名实体")
                print(f"          → 引擎生成距离缓存时会跳过它（`LoadSettlementData` 找不到实体即 continue）"
                      f"→ 据点对变少 → 全局最大据点距可能恒 0 → 家宅选不中（雷 53）")
                continue
            tags = set()
            for e in ents:
                tags |= entity_tags(e)
                children = e.find("children")
                if children is not None:
                    for sub in walk_entities(children):
                        tags |= entity_tags(sub)
            if not is_town:
                print(f"  [ OK ] 据点 {sid}（非城镇，仅查实体存在）")
                continue
            missing = [t for t in REQUIRED_TOWN_TAGS if t not in tags]
            for t in REQUIRED_TOWN_TAGS:
                if t in tags:
                    print(f"  [ OK ] {sid} :: {t}")
            for t in missing:
                errors.append(f"城镇 {sid} 缺 {t}")
                print(f"  [ERROR] {sid} 缺 {t} —— {REQUIRED_TOWN_TAGS[t]}")
            for t, why in OPTIONAL_TOWN_TAGS.items():
                if t not in tags:
                    warns.append(f"城镇 {sid} 缺 {t}")
                    print(f"  [WARN]  {sid} 缺 {t} —— {why}")

    # ── 汇总 ──
    print(f"\nSummary: errors={len(errors)} warnings={len(warns)}")
    if errors:
        for e in errors:
            print(f"  [ERROR] {e}")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
