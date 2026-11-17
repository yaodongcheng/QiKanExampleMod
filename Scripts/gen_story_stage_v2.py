# -*- coding: utf-8 -*-
"""从StoryGateSetup.json与StoryOpeningTimeline.json生成正式场景布局。

- 首遇岗位、职责区和流程来自StoryGateSetup.json；
- 把市集 `prop_spawn` 搬成布局 `Props`（几何只有一份）；
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
V1 = os.path.join(DESIGN, "StoryOpeningTimeline.json")
V2 = os.path.join(DESIGN, "StoryScenePlan.json")
GATE_SOURCE = os.path.join(DESIGN, "StoryGateSetup.json")

WORK_MAP = {
    "pawnbroker": "pawnbroker_desk",
    "butcher": "butcher_block",
    "widow": "widow_stage",
}


POINTS_CSV = os.path.join(DESIGN, "StoryPoints.csv")
POINTS_HEADER = ["Id", "Scene", "Layout", "Role", "Anchor", "X", "Y", "Z", "Dir",
                 "Action", "MaxOccupants", "ArriveTolerance"]

def _fmt(v):
    return ("%g" % v) if isinstance(v, float) else str(v)

def write_points_csv(new_rows):
    """把 market_square 的点位行换成新算出来的一批；其它布局（如 gate_outer 的两个岗位）原样保留。"""
    rows = []
    previous = io.open(POINTS_CSV, encoding="utf-8-sig").read() if os.path.exists(POINTS_CSV) else ""
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
    rendered = chr(10).join(out) + chr(10)
    with io.open(POINTS_CSV, "w", encoding="utf-8", newline="") as f:
        f.write(rendered)
    return rendered != previous


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
            "Placement": e.get("Placement") or "gather",
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
        fail("先有 StoryScenePlan.json（first_meet）再生成 second_meet")
    v1 = json.load(io.open(V1, encoding="utf-8"))
    v2 = json.load(io.open(V2, encoding="utf-8"))
    previous = json.loads(json.dumps(v2))
    # 只更新说话人物，不覆盖策划已经审定的布局、岗位、流程或开场名单。
    if "--groups-only" in sys.argv[1:]:
        cast = json.load(io.open(os.path.join(DESIGN, "StoryProbeCast.json"), encoding="utf-8"))
        update_groups(v2, cast)
        with io.open(V2, "w", encoding="utf-8", newline="\n") as f:
            f.write(json.dumps(v2, ensure_ascii=False, indent=2) + "\n")
        print("updated group speech bindings; contentVersion=%s" % v2["ContentVersion"])
        return
    # 首遇也有明确维护源；不再靠保留生成物里的半份数据续命。
    gate = json.load(io.open(GATE_SOURCE, encoding="utf-8"))
    # 环境是阶段入场参数；先校验维护源，不能把无效源发布成生成物。
    from check_story_probe import environment_issues
    for stage_id, stage in (gate.get("Stages") or {}).items():
        if "Environment" in stage:
            issues = environment_issues(stage["Environment"], "$.Stages." + stage_id + ".Environment")
            if issues:
                fail(str(issues))
    if "Environment" in v1:
        issues = environment_issues(v1["Environment"], "$.Environment")
        if issues:
            fail(str(issues))
    for key in ("Scene", "WallLevel"):
        v2[key] = gate[key]
    for key in ("Layouts", "Stages", "Behaviors", "Flows"):
        v2[key] = gate.get(key) or {}
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
                "Footprint": [6.8, 5.3] if b.get("Prefab") == "lwn_market_stage"
                    else [1.6, 3.5] if b.get("Prefab") in ("lwn_market_meat_counter", "lwn_market_pawn_counter") else [1.2, 0.8],
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

    # 工作点挂组合道具锚点，直接消费道具贴地后的最终基准，写进StoryPoints.csv。
    # （2026-11-16 用户裁定：点位表只有 Anchor/X/Y/Z/Dir，Anchor 留空才是绝对坐标）。
    work_by_actor = {}
    work_csv_rows = []
    by_prop = {x["Id"]: x for x in props}
    for actor, prop_id in WORK_MAP.items():
        action = first_action.get(actor)
        if not action:
            raise ValueError("work actor %s has no source actor_action" % actor)
        if actor not in actor_seen or prop_id not in prop_seen:
            continue
        wid = actor + "_work"
        prop = by_prop.get(prop_id)
        if prop is None:
            continue
        # 初始几何由源JSON维护；工作点不得再覆盖演员的初始位置或方向。
        initial = next(a for a in actors if a["Actor"] == actor)
        if initial["Anchor"] != prop_id:
            raise ValueError("work actor %s must use its prop anchor %s" % (actor, prop_id))
        # 动作必须真的在 as_human_warrior 里（铁律 41），不要抄职业集的动作。
        work_csv_rows.append([wid, scene, "market_square", "work", prop_id,
                              initial["Fwd"], initial["Right"], initial["Up"],
                              initial["Facing"], action, 1, 0.6])
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
            # 第二阶段没有使用 gate_entity 的点位或门区；不要把一级/二级门实体
            # 列为开演必需项，否则实际加载 level_3 时会取消已准备好的整场演出。
            # 旧 v1 拍里用 `gate` 这个别名；这里显式登记，避免“未声明锚点”。
            "gate": dict(gate["Layouts"]["gate_outer"]["Anchors"]["gate_entity"], Required=False),
            "idle": {"Source": "tag", "Value": ["npc_idle"], "Pick": "nearestToPlayer", "Required": False},
            "market": {"Source": "tag", "Value": ["sp_merchant"], "Pick": "nearestToPlayer", "Required": False},
        },
        "Gates": {},
        "Props": props,
        "Standing": standing,
    }
    for prop_id in WORK_MAP.values():
        if prop_id in by_prop:
            layout["Anchors"][prop_id] = {"Source": "prop", "Value": [prop_id], "Required": True}

    v2.setdefault("Layouts", {})["market_square"] = layout
    v2.setdefault("Stages", {})["second_meet"] = {
        "Layout": "market_square",
        "Actors": [a["Actor"] for a in actors],
        "Props": [p["Id"] for p in props],
        "Opening": False,
        "Prep": {"SkipAllowed": True, "ActorReadyTimeoutSec": 5},
    }
    if "Environment" in v1:
        v2["Stages"]["second_meet"]["Environment"] = v1["Environment"]

    cast_path = os.path.join(DESIGN, "StoryProbeCast.json")
    if os.path.exists(cast_path):
        cast = json.load(io.open(cast_path, encoding="utf-8"))
        update_groups(v2, cast)
    old_version = int(previous.get("ContentVersion") or 1)
    current_body = {k: v for k, v in v2.items() if k != "ContentVersion"}
    previous_body = {k: v for k, v in previous.items() if k != "ContentVersion"}
    points_changed = write_points_csv(work_csv_rows)
    v2["ContentVersion"] = old_version + int(current_body != previous_body or points_changed)
    with io.open(V2, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(v2, ensure_ascii=False, indent=2) + "\n")
    print("second_meet: actors=%d props=%d work=%d standing=%d" %
          (len(actors), len(props), len(work_csv_rows), len(standing)))


if __name__ == "__main__":
    sys.exit(main())
