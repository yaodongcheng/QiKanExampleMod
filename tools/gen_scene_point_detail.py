# -*- coding: utf-8 -*-
"""
场景点位明细提取（生成物辅助工具）。
用法：
  python tools/gen_scene_point_detail.py <场景名>    # 例：sho_keep_scene
  python tools/gen_scene_point_detail.py --all        # 全量到 Knowledge/场景点位明细.md（可能很大）
输出：每点位一行：tag(中文意义) | prefab 模型 | 坐标 x,y,z | 朝向 yaw(度)
"""
import glob
import json
import os
import sys
import xml.etree.ElementTree as ET

BASE = r"h:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules"
ROOTS = [
    ("织丰 Shokuho", os.path.join(BASE, "Shokuho", "SceneObj")),
    ("原版 SandBoxCore", os.path.join(BASE, "SandBoxCore", "SceneObj")),
    ("原版 SandBox", os.path.join(BASE, "SandBox", "SceneObj")),
]
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Knowledge", "场景点位明细.md")

# tag → 中文意义（与场景Tag清单.md 的分类一致）
TAG_CN = {
    "sp_player": "玩家出生", "sp_player_conversation": "玩家对话位",
    "sp_notable": "贵族位", "sp_throne": "王座", "sp_hermit": "隐士", "sp_arena": "竞技竞技位",
    "sp_tavern_wench": "酒馆女侍",
    "sp_guard": "守卫", "sp_guard_unarmed": "守卫(徒手)", "sp_guard_with_spear": "守卫(长矛)",
    "sp_guard_castle": "城门守卫", "sp_prison_guard": "监狱守卫", "sp_notable_instructing_with_listeners": "贵族训话组",
    "sp_defender_infantry_lords_hall": "领主殿步兵(攻城)", "sp_defender_archer_lords_hall": "领主殿弓手(攻城)",
    "npc_common": "平民位", "npc_common_limited": "平民位(限)", "npc_idle": "站姿(闲)", "npc_wait": "等待位",
    "npc_drinker": "酒客位", "npc_beggar": "乞丐位", "npc_passage": "通道口位",
    "sp_notable_hangout_set": "贵族闲聊组", "sp_npc_argue_set": "争吵组", "sp_npc_argument_trio": "三人争论组",
    "sp_notable_giving_order": "下达命令组", "sp_battle_set": "战斗布景",
    "gambler_npc": "赌徒NPC位", "gambler_player": "玩家赌位", "reserved": "保留位",
    "musician": "乐师位", "alternative": "备用位", "spawnpoint_cleaner": "清理出生点",
    "passage": "通道", "gate": "城门", "inner_gate": "内门", "plank": "攻城木板",
    "defender": "守军位", "sho_archer_enforcer": "弓手弩手(织丰攻城)", "tournament_fight": "比武位",
    "sp_arena_spectator": "竞技场观众位", "tournament_practice": "比武练习位", "small": "小型位",
    "large": "大型位", "medium": "中型位", "arena_sound": "竞技声音点",
}


def scene_point_rows(xscene):
    """返回 [(tags组合, prefab, pos, yaw_deg)]。"""
    rows = []
    try:
        root = ET.parse(xscene).getroot()
    except Exception as e:
        return rows, str(e)
    for ge in root.iter("game_entity"):
        tags = [t.get("name") for t in ge.findall(".//tag") if t.get("name")]
        tr = ge.find(".//transform")
        if tr is None:
            continue
        pos = tr.get("position", "?")
        rot = tr.get("rotation_euler", "0,0,0")
        try:
            yaw = float(rot.split(",")[2]) * 57.29578  # 弧度→度
            yaw_s = "%.1f°" % yaw
        except Exception:
            yaw_s = rot
        rows.append((",".join(tags), ge.get("prefab", ge.get("name", "?")), pos, yaw_s))
    return rows, None


def main():
    args = sys.argv[1:]
    want_all = "--all" in args
    scene_filter = None
    for a in args:
        if not a.startswith("-") and not want_all:
            scene_filter = a
    if want_all:
        f = open(OUT, "w", encoding="utf-8")
        lines = ["# 场景点位明细（生成物，禁止手改）\n",
                 "> 工具：`tools/gen_scene_point_detail.py --all` ｜ 每行 = 一个带 tag 的 `game_entity`："
                 "tag(中文意义) ｜ prefab 模型 ｜ 坐标(米) ｜ 朝向 yaw(度)\n"]
        for mod, root in ROOTS:
            if not os.path.isdir(root):
                continue
            for x in sorted(glob.glob(os.path.join(root, "*", "scene.xscene"))):
                s = os.path.basename(os.path.dirname(x))
                rows, err = scene_point_rows(x)
                if not rows:
                    continue
                lines.append("\n## %s（%s）\n" % (s, mod))
                lines.append("| tag(意义) | prefab | 坐标 | yaw |")
                lines.append("|---|---|---|---|")
                for tg, pf, pos, yaw in rows:
                    lines.append("| %s | %s | %s | %s |" % (tg, pf, pos, yaw))
        f.write("\n".join(lines) + "\n")
        f.close()
        print("OK ->", OUT)
        return

    if not scene_filter:
        print("用法: python gen_scene_point_detail.py <场景名> | --all")
        return
    for mod, root in ROOTS:
        p = os.path.join(root, scene_filter, "scene.xscene")
        if os.path.exists(p):
            rows, err = scene_point_rows(p)
            print("场景:%s（%s） 带 tag 实体数:%d" % (scene_filter, mod, len(rows)))
            for tg, pf, pos, yaw in rows:
                cn = " ".join(TAG_CN.get(t, t) for t in tg.split(","))
                print("  %-28s | %-34s | %-24s | %s" % (cn, pf, pos, yaw))
            return
    print("未找到场景:", scene_filter)


if __name__ == "__main__":
    main()
