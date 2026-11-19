# -*- coding: utf-8 -*-
"""选项"来源"闸门（2026-11-19 立）。

规则（对应《用户设计原则》「选项的来源」）：
  后果类选项（动手/威吓/拘捕/讨债/揭穿）必须有可说明的来源，三类之一：
    ① 现场：该对话的出场者里有当场履职的守卫（StoryProbeCast 的 FirstMeetGuard），
       拦路/盘问本身就是依据，不要求任何任务或线索；
    ② 已知情报：Requires 含已登记的 clue:<id>（登记表 StoryClues.json）；
    ③ 已承接业务：Requires 含 quest_accepted@ / quest_done@ / event_resolved:。
  三者都没有 → 失败（选项凭空出现）。
"""
import glob
import io
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
D = os.path.join(ROOT, "ModuleData", "DesignData")
CONSEQUENCE = {"start_fight", "collect_debt", "debt_collect", "detain_player", "force_defeat"}
THREAT_TACTICS = {"intimidate", "coerce", "threaten", "force"}


def load(path):
    return json.loads(io.open(path, encoding="utf-8").read())


def main():
    cast = load(os.path.join(D, "StoryProbeCast.json"))
    guards = {n["Id"] for n in cast.get("Npcs", []) if n.get("FirstMeetGuard")}
    clues = set()
    cp = os.path.join(D, "StoryClues.json")
    if os.path.isfile(cp):
        clues = {c["Id"] for c in load(cp).get("Clues", [])}

    errors, scene_ok = [], []
    for path in sorted(glob.glob(os.path.join(D, "Dialogues", "*.dialog.json"))):
        doc = load(path)
        name = os.path.basename(path)
        actors = set(doc.get("Actors") or []) | set(doc.get("EntryActors") or [])
        has_guard = bool(actors & guards)
        # 现场依据的另两种形态：①街头/群戏事件（玩家正看见它发生）②刚谈崩的对峙节点（刚刚发生过的对话）。
        has_event_trigger = any((t.get("Source") or "") == "event" for t in doc.get("Triggers") or [])
        for node in doc.get("Nodes", []):
            for opt in node.get("Options") or []:
                effects = {e.get("Id") for e in opt.get("Effects") or []}
                tactic = ((opt.get("Check") or {}).get("Tactic") or "").lower()
                consequence = bool(effects & CONSEQUENCE) or tactic in THREAT_TACTICS
                if not consequence:
                    continue
                req = opt.get("Requires") or []
                has_clue = any(isinstance(r, str) and r.lstrip("!").startswith("clue:") for r in req)
                has_quest = any(isinstance(r, str) and (
                    "quest_accepted@" in r or "quest_done@" in r or "quest_turned@" in r
                    or "event_resolved:" in r) for r in req)
                label = "%s:%s/%s" % (name, node["Id"], opt["Id"])
                if has_clue:
                    ids = [r.lstrip("!")[5:] for r in req if isinstance(r, str) and r.lstrip("!").startswith("clue:")]
                    unknown = [i for i in ids if i not in clues]
                    if unknown:
                        errors.append(label + ": unregistered clue " + ",".join(unknown))
                    else:
                        scene_ok.append(label + " [clue]")
                elif has_quest:
                    scene_ok.append(label + " [quest/event]")
                elif has_guard:
                    scene_ok.append(label + " [scene:gate_guard]")
                elif has_event_trigger:
                    scene_ok.append(label + " [scene:event]")
                elif "_refuse" in node["Id"]:
                    scene_ok.append(label + " [scene:just_argued]")
                else:
                    errors.append(label + ": consequence option with no basis (needs scene/clue/quest)")

    print("option basis: guards=%d clues=%d checked=%d" % (len(guards), len(clues), len(scene_ok) + len(errors)))
    for line in scene_ok:
        print("  ok:", line)
    for line in errors:
        print("  [X]", line)
    if errors:
        print("FAIL: dialogue option basis (%d)" % len(errors))
        return 1
    print("PASS: dialogue option basis")
    return 0


if __name__ == "__main__":
    sys.exit(main())