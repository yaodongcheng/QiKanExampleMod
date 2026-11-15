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
            "LineFallbacks": e.get("LineFallbacks") or [],
            "ActionsA": e.get("ActionA") or [],
            "ActionsB": e.get("ActionB") or [],
            "JoinNode": e.get("JoinNode") or "",
        }
    return out


def fail(msg):
    raise SystemExit("gen_story_stage_v2: " + msg)


def main():
    if not os.path.exists(V2):
        fail("先有 StoryStageV2.json（first_meet）再生成 second_meet")
    v1 = json.load(io.open(V1, encoding="utf-8"))
    v2 = json.load(io.open(V2, encoding="utf-8"))
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

    work = []
    work_by_actor = {}
    for actor, (prop_id, action) in WORK_MAP.items():
        if actor not in actor_seen or prop_id not in prop_seen:
            continue
        wid = actor + "_work"
        work.append({
            "Id": wid, "Prop": prop_id, "Side": "front", "Offset": 0.65,
            "Contact": [0.35, 0.55, 0.9], "Yaw": 0,
            "ArriveTolerance": 0.6, "MaxOccupants": 1,
            # 动作必须真的在 as_human_warrior 里（铁律 41），不要抄职业集的动作。
            "Action": action,
        })
        work_by_actor[actor] = wid

    standing = []
    for a in actors:
        s = {"Actor": a["Actor"], "Facing": a["Facing"]}
        if a["Actor"] in work_by_actor:
            s["WorkPoint"] = work_by_actor[a["Actor"]]
        else:
            s["Anchor"] = a["Anchor"]
            s["Fwd"] = a["Fwd"]
            s["Right"] = a["Right"]
            s["Up"] = a["Up"]
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
        "WorkPoints": work,
        "Posts": [],
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
        behaviors = {k: v for k, v in (v2.get("Behaviors") or {}).items()
                     if not (isinstance(v, dict) and v.get("Kind") == "group")}
        behaviors.update(group_behaviors(cast))
        v2["Behaviors"] = behaviors
    v2["ContentVersion"] = int(v2.get("ContentVersion") or 1) + (0 if "market_square" in (v1.get("Layouts") or {}) else 0)
    with io.open(V2, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(v2, ensure_ascii=False, indent=2) + "\n")
    print("second_meet: actors=%d props=%d work=%d standing=%d" %
          (len(actors), len(props), len(work), len(standing)))


if __name__ == "__main__":
    sys.exit(main())
