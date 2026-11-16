# -*- coding: utf-8 -*-
"""从旧 `StoryStage.json`（v1 的 30 拍）生成 `StoryStageV2.json` 的第二阶段布局。

- 保留文件里已有的 first_meet（不覆盖）；
- 把 v1 的 7 个 `prop_spawn` 拍搬成布局 `Props`（几何只有一份）；
- 把 9 个 `actor_place` 拍搬成 `Standing`，并给老周/屠户/寡妇三个摊位加工作点；
- 工作动作取该 NPC 在 v1 里第一条 `actor_action`。
幂等：重复运行输出一致。
"""
import io
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
DESIGN = os.path.join(ROOT, "ModuleData", "DesignData")
V1 = os.path.join(DESIGN, "StoryStage.json")
V2 = os.path.join(DESIGN, "StoryStageV2.json")

WORK_MAP = {
    "pawnbroker": ("pawnbroker_desk", "act_wood_workshop_idle_part_01"),
    "butcher": ("butcher_block", "act_shipyard_worker_repair_loop"),
    "widow": ("widow_stage_a", "act_conversation_aggressive_trivial"),
}


POINTS_CSV = os.path.join(DESIGN, "StoryPoints.csv")
POINTS_HEADER = ["Id", "Scene", "Layout", "Role", "Anchor", "X", "Y", "Z", "Dir",
                 "Action", "MaxOccupants", "ArriveTolerance"]

def _fmt(v):
    return ("%g" % v) if isinstance(v, float) else str(v)

def write_points_csv(new_rows):
    """把 market_square 的点位行换成新算出来的一批；其它布局（如 gate_outer 的两个岗位）原样保留。"""
    rows = []
    if os.path.exists(POINTS_CSV):
        for line in io.open(POINTS_CSV, encoding="utf-8-sig").read().splitlines():
            if not line.strip():
                continue
            cols = [c.strip() for c in line.split(",")]
            if cols and cols[0] == "Id" and len(cols) > 2 and cols[1] == "Scene":
                continue
            if len(cols) >= 3 and cols[2] == "market_square":
                continue
            rows.append(cols)
    rows.extend([[_fmt(v) for v in r] for r in new_rows])
    out = [",".join(POINTS_HEADER)] + [",".join(r) for r in rows]
    with io.open(POINTS_CSV, "w", encoding="utf-8", newline="") as f:
        f.write(chr(10).join(out) + chr(10))


def group_behaviors(cast):
    """把选角表 6 起事件搬成 v2 `Behaviors(Kind=group)`（成员/台词/动作/主持/接入节点）。"""
    out = {}
    for e in cast.get("Events") or []:
        idx = e.get("Index")
        if not isinstance(idx, int) or idx <= 0:
            continue
        members = [e.get("ActorA")] if e.get("ActorA") else []
        if e.get("TwoPerson") and e.get("ActorB"):
            members.append(e["ActorB"])
        if not members:
            continue
        out["event_%d" % idx] = {
            "Kind": "group",
            "Stages": ["second_meet"],
            "Members": members,
            "Host": e.get("HostNpcId") or members[0],
            "Lines": e.get("LineKeys") or [],
            "Speakers": e.get("Speakers") or [],
            "Listeners": e.get("Listeners") or [],
            "LineFallbacks": e.get("LineFallbacks") or [],
            "ActionsA": e.get("ActionA") or [],
            "ActionsB": e.get("ActionB") or [],
            "JoinNode": e.get("JoinNode") or "",
        }
    return out


def update_groups(v2, cast):
    behaviors = {k: v for k, v in (v2.get("Behaviors") or {}).items()
                 if not (isinstance(v, dict) and v.get("Kind") == "group")}
    behaviors.update(group_behaviors(cast))
    if behaviors != v2.get("Behaviors"):
        v2["ContentVersion"] = int(v2.get("ContentVersion") or 1) + 1
    v2["Behaviors"] = behaviors


def fail(msg):
    raise SystemExit("gen_story_stage_v2: " + msg)


def main():
    if not os.path.exists(V2):
        fail("先有 StoryStageV2.json（first_meet）再生成 second_meet")
    v1 = json.load(io.open(V1, encoding="utf-8"))
    v2 = json.load(io.open(V2, encoding="utf-8"))
    # 只更新说话人物，不覆盖策划已经审定的布局、岗位、流程或开场名单。
    if "--groups-only" in sys.argv[1:]:
        cast = json.load(io.open(os.path.join(DESIGN, "StoryProbeCast.json"), encoding="utf-8"))
        update_groups(v2, cast)
        with io.open(V2, "w", encoding="utf-8", newline="\n") as f:
            f.write(json.dumps(v2, ensure_ascii=False, indent=2) + "\n")
        print("updated group speech bindings; contentVersion=%s" % v2["ContentVersion"])
        return
    beats = v1.get("Beats") or []

    props, prop_seen = [], set()
    actors, actor_seen = [], set()
    first_action = {}
    for b in beats:
        cmd = b.get("Cmd")
        if cmd == "prop_spawn":
            pid = b.get("Id")
            if not pid or pid in prop_seen:
                continue
            prop_seen.add(pid)
            props.append({
                "Id": pid,
                "Source": "prefab",
                "Prefab": b.get("Prefab"),
                "Anchor": b.get("Anchor") or "player",
                "Fwd": b.get("Fwd", 0), "Right": b.get("Right", 0), "Up": b.get("Up", 0),
                "Yaw": b.get("Yaw", 0), "Scale": b.get("Scale", 1),
                "Support": "ground",
                "Footprint": [1.2, 0.8],
                "Required": True,
            })
        elif cmd == "actor_place":
            aid = b.get("Actor")
            if not aid or aid in actor_seen:
                continue
            actor_seen.add(aid)
            actors.append({
                "Actor": aid,
                "Anchor": b.get("Anchor") or "player",
                "Fwd": b.get("Fwd", 0), "Right": b.get("Right", 0), "Up": b.get("Up", 0),
                "Facing": b.get("Facing", -1),
            })
        elif cmd == "actor_action":
            aid = b.get("Actor")
            if aid and aid not in first_action and b.get("Action"):
                first_action[aid] = b["Action"]

    actors.sort(key=lambda a: a["Actor"])
    props.sort(key=lambda p: p["Id"])
    scene = v2.get("Scene") or "empire_town_m"

    # 工作点不再挂道具：按"道具位置 + 侧向偏移"折算成**相对锚点**的坐标，写进 StoryPoints.csv
    # （2026-11-16 用户裁定：点位表只有 Anchor/X/Y/Z/Dir，Anchor 留空才是绝对坐标）。
    work_by_actor = {}
    work_csv_rows = []
    by_prop = {x["Id"]: x for x in props}
    for actor, (prop_id, action) in WORK_MAP.items():
        if actor not in actor_seen or prop_id not in prop_seen:
            continue
        wid = actor + "_work"
        prop = by_prop.get(prop_id)
        if prop is None:
            continue
        yaw = math.radians(prop.get("Yaw", 0.0))
        cos, sin = math.cos(yaw), math.sin(yaw)
        r0, f0 = 0.0, 1.0          # side = front
        offset = 0.65
        r1 = r0 * cos - f0 * sin
        f1 = r0 * sin + f0 * cos
        x = prop.get("Fwd", 0.0) + f1 * offset
        y = prop.get("Right", 0.0) + r1 * offset
        z = prop.get("Up", 0.0)
        # 动作必须真的在 as_human_warrior 里（铁律 41），不要抄职业集的动作。
        work_csv_rows.append([wid, scene, "market_square", "work", prop.get("Anchor", "player"),
                              x, y, z, 0, action, 1, 0.6])
        work_by_actor[actor] = wid

    standing = []
    for a in actors:
        # 空间信息只有一个来源 = Points：没工作点的演员也生成一个自己的点位，Standing 只写 Actor+Point。
        s = {"Actor": a["Actor"]}
        if a["Actor"] in work_by_actor:
            s["Point"] = work_by_actor[a["Actor"]]
        else:
            pid = a["Actor"] + "_spot"
            work_csv_rows.append([pid, scene, "market_square", "spot", a["Anchor"],
                                  a["Fwd"], a["Right"], a["Up"], a["Facing"], "", "", ""])
            s["Point"] = pid
        standing.append(s)

    layout = {
        "Version": 1,
        "Anchors": {
            "player": {"Source": "player", "Pick": "first", "Required": True},
            "gate_entity": {"Source": "entity", "Value": ["empire_castle_gate_outer_l2", "empire_castle_gate_outer_l1", "european_castle_gate_outer_l2"], "Pick": "nearestToPlayer", "Required": True},
            # 旧 v1 拍里用 `gate` 这个别名；这里显式登记，避免“未声明锚点”。
            "gate": {"Source": "entity", "Value": ["empire_castle_gate_outer_l2", "empire_castle_gate_outer_l1", "european_castle_gate_outer_l2"], "Pick": "nearestToPlayer", "Required": False},
            "idle": {"Source": "tag", "Value": ["npc_idle"], "Pick": "nearestToPlayer", "Required": False},
            "market": {"Source": "tag", "Value": ["sp_merchant"], "Pick": "nearestToPlayer", "Required": False},
        },
        "Gates": {},
        "Props": props,
        "Standing": standing,
    }

    v2.setdefault("Layouts", {})["market_square"] = layout
    v2.setdefault("Stages", {})["second_meet"] = {
        "Layout": "market_square",
        "Actors": [a["Actor"] for a in actors],
        "Props": [p["Id"] for p in props],
        "Opening": False,
        "Prep": {"SkipAllowed": True, "ActorReadyTimeoutSec": 5},
    }

    cast_path = os.path.join(DESIGN, "StoryProbeCast.json")
    if os.path.exists(cast_path):
        cast = json.load(io.open(cast_path, encoding="utf-8"))
        update_groups(v2, cast)
    v2["ContentVersion"] = int(v2.get("ContentVersion") or 1) + (0 if "market_square" in (v1.get("Layouts") or {}) else 0)
    with io.open(V2, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(v2, ensure_ascii=False, indent=2) + "\n")
    write_points_csv(work_csv_rows)
    print("second_meet: actors=%d props=%d work=%d standing=%d" %
          (len(actors), len(props), len(work_csv_rows), len(standing)))


if __name__ == "__main__":
    sys.exit(main())
