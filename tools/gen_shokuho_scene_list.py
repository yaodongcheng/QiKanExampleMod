# -*- coding: utf-8 -*-
"""
生成织丰据点子场景清单（生成物，禁止手改）。
输入：Modules/Shokuho/ModuleData/settlements.xml + duel/port/temple_location_settlements.xml
输出：Knowledge/织丰据点子场景清单.md + Knowledge/织丰据点子场景清单.csv
用法：python tools/gen_shokuho_scene_list.py
"""
import csv
import glob
import os
import re
import sys
import xml.etree.ElementTree as ET
from collections import Counter, defaultdict

BASE = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules"
SH_MODDATA = os.path.join(BASE, "Shokuho", "ModuleData")
OUT_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Knowledge")
OUT_MD = os.path.join(OUT_DIR, "织丰据点子场景清单.md")
OUT_CSV = os.path.join(OUT_DIR, "织丰据点子场景清单.csv")

# 字典加载目录（按优先级）：织丰中文包 → 官方原生中文包 → 织丰模块英文源
CN_DIRS = [
    r"h:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Shokuho_CNs\ModuleData\Languages\CNs",
    os.path.join(BASE, "Native", "ModuleData", "Languages", "CNs"),
    os.path.join(BASE, "SandBox", "ModuleData", "Languages", "CNs"),
]
EN_DIRS = [
    os.path.join(r"h:\SteamLibrary\steamapps\common\MB2_Version\MB2_1.2.12\Mount & Blade II Bannerlord\Modules\Shokuho", "ModuleData", "Languages"),
]

# 地点 id → 中文（列头用；技术 id 无本地化键，人工映射）
LOC_CN = {
    "center": "中心", "arena": "竞技场", "tavern": "酒馆", "lordshall": "领主大厅",
    "prison": "地牢", "alley": "小巷", "practice_arena": "训练场",
    "house_1": "民居1", "house_2": "民居2", "house_3": "民居3",
    "village_center": "村庄中心", "duel_location_id": "决斗场", "port_location_id": "港口",
    "temple_location_id": "寺庙", "postbattle_location_id": "战后场景",
    "village_supply": "补给点", "retirement_retreat": "退休地",
}


def load_dicts(dirs):
    """解析 <string id key text val> 词典；utf-8/utf-16 自动（ET 按声明解析）。"""
    d = {}
    for dd in dirs:
        if not os.path.isdir(dd):
            continue
        for f in glob.glob(os.path.join(dd, "*.xml")):
            try:
                tree = ET.parse(f)
            except Exception:
                continue
            for s in tree.getroot().iter("string"):
                k, v = s.get("id"), s.get("text")
                if k and v and k not in d:
                    d[k] = v.strip()
    return d


CN_DICT = load_dicts(CN_DIRS)
EN_DICT = load_dicts(EN_DIRS)


def clean_name(name: str) -> str:
    """去掉 {=xxx} key，取 fallback 显示名。"""
    if not name:
        return "", ""
    m = re.match(r"^\{=([^}]*)\}(.*)$", name)
    if not m:
        return "", name.strip()
    key, en = m.group(1), m.group(2).strip()
    return key, en


def zh_of(key: str) -> str:
    """key → 中文（优先中文包，其次模块英文源兜底无意义则空）。"""
    if key:
        return CN_DICT.get(key, "")
    return ""


FILES = [
    ("settlements.xml", None),
    ("duel_location_settlements.xml", "duel"),
    ("port_location_settlements.xml", "port"),
    ("temple_location_settlements.xml", "temple"),
]


def scene_of(loc_el) -> str:
    """取场景名；三档不同则记为 1:a/2:b/3:c。"""
    s1 = loc_el.get("scene_name_1", "") or loc_el.get("scene_name", "")
    s2 = loc_el.get("scene_name_2", "")
    s3 = loc_el.get("scene_name_3", "")
    if s2 and s3 and s2 != s1:
        return "1:%s/2:%s/3:%s" % (s1, s2, s3)
    return s1 or (s2 or "")


def main():
    rows = []  # dict: id,key,name,zh,type,culture,complex,loc:{id:scene}
    for fname, tag in FILES:
        path = os.path.join(SH_MODDATA, fname)
        if not os.path.exists(path):
            print("MISSING:", path)
            continue
        tree = ET.parse(path)
        root = tree.getroot()
        for sel in root.iter("Settlement"):
            sid = sel.get("id", "")
            nkey, name = clean_name(sel.get("name", ""))
            zh = zh_of(nkey)
            culture = sel.get("culture", "")
            # 类型判定（组件；组件在 <Components> 包裹下）
            comp = None
            comp_nodes = sel.find("Components")
            for c in (comp_nodes if comp_nodes is not None else sel):
                if c.tag in ("Town", "Village", "DuelLocationComponent", "PortLocation", "TempleLocationComponent"):
                    comp = c.tag
                    break
            is_castle = bool(comp == "Town" and comp_nodes is not None
                             and comp_nodes.find("Town") is not None
                             and comp_nodes.find("Town").get("is_castle") == "true")
            if comp == "Village":
                ltype = "village"
            elif comp == "Town":
                ltype = "castle" if is_castle else "town"
            else:
                ltype = comp or "?"
            locations = {}
            for lo in sel.iter("Location"):
                locations[lo.get("id", "")] = scene_of(lo)
            complex_t = ""
            for lo in sel.iter("Locations"):
                complex_t = lo.get("complex_template", "")
            rows.append({"id": sid, "name": name, "zh": zh, "type": ltype, "culture": culture,
                         "complex": complex_t, "loc": locations, "tag": tag})

    # ---- 统计 ----
    types = Counter(r["type"] for r in rows)
    scene_counter = Counter()
    scene_at_loc = defaultdict(set)   # 场景 → 出现的地点列（推导用途）
    for r in rows:
        for lid, s in r["loc"].items():
            base = s.split("/")[0].split(":")[0] if s else s
            scene_counter[base] += 1
            if base:
                scene_at_loc[base].add(lid)

    # ---- CSV（宽表，值内无半角逗号，多值用 |）----
    loc_ids = sorted({lid for r in rows for lid in r["loc"]})
    cn_keys = [("settlement_id", "据点 id"), ("name", "名称(英)"), ("zh", "名称(中)"),
               ("type", "类型"), ("culture", "文化"), ("complex", "地点模板")]
    with open(OUT_CSV, "w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f)
        w.writerow([k for k, _ in cn_keys] + ["%s(%s)" % (l, LOC_CN.get(l, "")) for l in loc_ids])
        for r in rows:
            w.writerow([r["id"], r["name"], r["zh"], r["type"], r["culture"], r["complex"]]
                       + [r["loc"].get(l, "") for l in loc_ids])
    # 校验：值内半角逗号会裂列（铁律 24）
    for r in rows:
        for v in [r["id"], r["name"], r["zh"], r["type"], r["culture"], r["complex"]]:
            if "," in v:
                print("!! 值含半角逗号（需处理）：", v)
            if "|" in v:
                print("!! 值含竖线（需处理）：", v)

    # ---- MD ----    # noqa: E265
    lines = []
    lines.append("# 织丰据点子场景清单（生成物，禁止手改）\n")
    lines.append("> 生成脚本：`tools/gen_shokuho_scene_list.py` ｜ 数据源：`Modules/Shokuho/ModuleData/`"
                 "（settlements.xml + duel/port/temple_location_settlements.xml）\n")
    lines.append("> 中文名：按 string key 查织丰中文包（Shokuho_CNs/CNs）；查不到保留英文。"
                 "场景名规则：`scene_name` 三档繁荣度（1/2/3），`1:x/2:y/3:z` 表示三档不同。"
                 "地点属性（indoor/进入权限）只看模板 `location_complex_templates.xml`。\n")
    lines.append("## 0. 总览\n")
    lines.append("- 据点总数：%d（城镇 %d / 城堡 %d / 村庄 %d / 特殊 %d）"
                 % (len(rows), types["town"], types["castle"], types["village"], sum(v for k, v in types.items() if k not in ("town", "castle", "village"))))
    lines.append("- 用到的场景（次数）" + " ｜ ".join("%s×%d" % (k, v) for k, v in scene_counter.most_common()) + "\n")
    lines.append("### 场景用途对照（按场景出现的地点列自动推导）\n")
    lines.append("| 场景 | 次数 | 出现在地点列 |")
    lines.append("|---|---|---|")
    for sc in sorted(scene_at_loc, key=lambda s: -scene_counter[s]):
        locs = "、".join("%s(%s)" % (l, LOC_CN.get(l, l)) for l in sorted(scene_at_loc[sc]))
        lines.append("| %s | %d | %s |" % (sc, scene_counter[sc], locs))
    lines.append("\n## 1. 城镇（town_complex）\n")
    lines.append("| 据点 | 名称 | center | arena | tavern | lordshall | prison | house_1 | house_2 | house_3 | alley |")
    lines.append("|---|------|-------|-------|--------|-----------|--------|---------|---------|---------|-------|")
    for r in sorted(rows, key=lambda r: r["id"]):
        if r["type"] != "town":
            continue
        l = r["loc"]
        disp = "%s(%s)" % (r["zh"], r["name"]) if r["zh"] else (r["name"] or r["id"])
        lines.append("| %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |" % (
            r["id"], disp, l.get("center", ""), l.get("arena", ""), l.get("tavern", ""),
            l.get("lordshall", ""), l.get("prison", ""), l.get("house_1", ""), l.get("house_2", ""),
            l.get("house_3", ""), l.get("alley", "")))
    lines.append("\n## 2. 城堡（castle_complex）\n")
    lines.append("| 据点 | 名称 | center | lordshall | prison |")
    lines.append("|---|------|-------|-----------|--------|")
    for r in sorted(rows, key=lambda r: r["id"]):
        if r["type"] != "castle":
            continue
        l = r["loc"]
        disp = "%s(%s)" % (r["zh"], r["name"]) if r["zh"] else (r["name"] or r["id"])
        lines.append("| %s | %s | %s | %s | %s |" % (r["id"], disp, l.get("center", ""), l.get("lordshall", ""), l.get("prison", "")))
    lines.append("\n## 3. 村庄（village_complex，仅 village_center 一个地点，按场景分组）\n")
    by_scene = defaultdict(list)
    for r in rows:
        if r["type"] == "village":
            by_scene[r["loc"].get("village_center", "(none)")].append(r["id"])
    lines.append("| village_center 场景 | 数量 | 据点 id |")
    lines.append("|---|---|---|")
    for sc in sorted(by_scene):
        ids = by_scene[sc]
        lines.append("| %s | %d | %s |" % (sc, len(ids), ", ".join(ids)))
    lines.append("\n## 4. 特殊地点 settlement（决斗/港口/寺庙）\n")
    lines.append("| 据点 | 名称 | 类型 | 文化 | 地点场景 |")
    lines.append("|---|------|------|------|---------|")
    for r in sorted(rows, key=lambda r: r["id"]):
        if r["type"] not in ("town", "castle", "village"):
            l = r["loc"]
            scene = " ｜ ".join("%s:%s" % (k, v) for k, v in l.items())
            disp = "%s(%s)" % (r["zh"], r["name"]) if r["zh"] else (r["name"] or r["id"])
            lines.append("| %s | %s | %s | %s | %s |" % (r["id"], disp, r["type"], r["culture"], scene or "(无 Locations)"))
    with open(OUT_MD, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("OK  rows=%d  ->  %s" % (len(rows), OUT_MD))
    print("         ->  %s  (loc columns=%d)" % (OUT_CSV, len(loc_ids)))


if __name__ == "__main__":
    sys.exit(main())
