# -*- coding: utf-8 -*-
"""
生成场景 Tag 清单（生成物，禁止手改）。
输入：Modules/Shokuho + Modules/SandBox（+Native）的 SceneObj/*/scene.xscene
输出：Knowledge/场景Tag清单.md
用法：python tools/gen_scene_tag_list.py
"""
import glob
import os
import xml.etree.ElementTree as ET
from collections import Counter

BASE = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules"
ROOTS = [
    ("织丰 Shokuho", os.path.join(BASE, "Shokuho", "SceneObj")),
    ("原版 SandBox", os.path.join(BASE, "SandBox", "SceneObj")),
    ("原版 Native", os.path.join(BASE, "Native", "SceneObj")),
]
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Knowledge", "场景Tag清单.md")

# 代码约定的 tag 家族（引擎消费端实测收集；非穷尽）
TAG_FAMILIES = [
    ("玩家出生", "sp_player", "sp_player_conversation"),
    ("静态可交互贵族位", "sp_notable", "sp_throne", "sp_hermit", "sp_arena", "sp_tavern_wench"),
    ("守卫/兵", "sp_guard", "sp_guard_unarmed", "sp_guard_with_spear", "sp_guard_castle"),
    ("攻城驻防", "sp_defender_infantry_lords_hall", "sp_defender_archer_lords_hall"),
    ("随机平民出生", "npc_common", "npc_common_limited", "npc_idle", "npc_wait", "npc_drinker", "npc_beggar", "npc_passage"),
    ("情景人偶组（预摆布景）", "sp_notable_hangout_set", "sp_npc_argue_set", "sp_npc_argument_trio",
     "sp_notable_instructing_with_listeners", "sp_notable_giving_order", "sp_battle_set"),
    ("通道脚本", "passage"),
]

RECOGNIZED = {t for _, *ts in TAG_FAMILIES for t in ts}


def scene_tags(path):
    """解析 scene.xscene → Counter(tag)。"""
    c = Counter()
    try:
        root = ET.parse(path).getroot()
    except Exception:
        return c
    for tag in root.iter("tag"):
        n = tag.get("name", "")
        if n:
            c[n] += 1
    return c


def main():
    all_scenes = {}   # (mod, scene) -> Counter
    for mod, root in ROOTS:
        if not os.path.isdir(root):
            continue
        for x in glob.glob(os.path.join(root, "*", "scene.xscene")):
            scene = os.path.basename(os.path.dirname(x))
            all_scenes[(mod, scene)] = scene_tags(x)

    # 汇总 tag 总频
    total = Counter()
    for c in all_scenes.values():
        total.update(c)

    lines = []
    lines.append("# 场景 Tag 清单（生成物，禁止手改）\n")
    lines.append("> 生成脚本：`tools/gen_scene_tag_list.py` ｜ 数据源：`Modules/*/SceneObj/*/scene.xscene`\n")
    lines.append("> **Tag = 场景文件 `scene.xscene` 里 `game_entity` 上的 `<tag name>`**——一个 `.xscene` 一套 tag，"
                 "任何 settlement/location 调用同一场景看到的 tag 完全相同；差异化只看 location 的繁荣度/人口（往各 tag 塞多少人/谁）。\n")
    lines.append("## 0. 引擎消费的 tag 家族（分类总表）\n")
    lines.append("| 家族 | tag | 用途 |")
    lines.append("|---|---|---|")
    for fam, *ts in TAG_FAMILIES:
        for t in ts:
            lines.append("| %s | %s |（场景内出现 %d 次）|" % (fam, t, total.get(t, 0)))
    lines.append("\n## 1. 织丰场景 → tag 清单\n")
    lines.append("| 场景 | tag 数 | tag 列表（次数） |")
    lines.append("|---|---|---|")
    for (mod, scene), c in sorted(all_scenes.items()):
        if mod != "织丰 Shokuho":
            continue
        tags = " ".join("%s×%d" % (k, v) for k, v in sorted(c.items(), key=lambda x: -x[1]))
        lines.append("| %s | %d | %s |" % (scene, len(c), tags))
    lines.append("\n## 2. 原版对照（SandBox 战役场景节选：领主殿/地牢/酒馆/城镇）\n")
    lines.append("| 场景 | tag 数 | tag 列表（次数） |")
    lines.append("|---|---|---|")
    keys = {"aserai_castle_keep_a_l1_interior", "empire_castle_keep_a_l1_interior", "empire_dungeon_a",
            "empire_town_a", "empire_interior_tavern_a", "empire_village_a", "empire_hippodrome_a"}
    for (mod, scene), c in sorted(all_scenes.items()):
        if scene in keys:
            tags = " ".join("%s×%d" % (k, v) for k, v in sorted(c.items(), key=lambda x: -x[1]))
            lines.append("| %s（%s）| %d | %s |" % (scene, mod, len(c), tags))
    if "织丰 Shokuho" in {m for m, _ in all_scenes}:
        sho = c = None
        for (m, s), cc in all_scenes.items():
            if s == "sho_keep_scene":
                sho = cc
            if s == "empire_castle_keep_a_l1_interior":
                c = cc
        if sho is not None and c is not None:
            missing = sorted(set(c) - set(sho))
            extra = sorted(set(sho) - set(c))
            lines.append("\n## 3. sho_keep_scene vs 原版领主殿（empire_castle_keep_a_l1_interior）差异\n")
            lines.append("- 原版有、织丰缺：%s" % (", ".join(missing) if missing else "无（完全覆盖）"))
            lines.append("- 织丰有、原版缺：%s" % (", ".join(extra) if extra else "无"))
    with open(OUT, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("OK scenes=%d -> %s" % (len(all_scenes), OUT))


if __name__ == "__main__":
    main()
