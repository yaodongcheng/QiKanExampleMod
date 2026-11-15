# -*- coding: utf-8 -*-
"""把旧 `story_probe_<npc>.json` 迁移成 `story_<npc>.dialog.json`（story_dialogue/2）。

- 只读旧文件、只写新文件；旧文件保留作回退副本。
- 动词/条件/检定的映射表在下方；遇到未登记动词直接报错（不静默丢效果）。
- 幂等：重复运行输出一致。
"""
import io
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
DIALOGUES = os.path.join(ROOT, "ModuleData", "DesignData", "Dialogues")

# 旧动词 -> 新效果。参数由 `_map_effect` 处理。
ONCE_EFFECTS = {"mark", "clue", "hazard", "resolve_event", "open_gate", "accept_quest",
                "turn_in", "complete_quest", "collect_debt", "street_help_reward",
                "redeem_keepsake"}
NEVER_EFFECTS = {"pay", "money_offer", "money_deal", "errand", "start_fight", "force_defeat"}


def fail(msg):
    raise SystemExit("migrate_story_dialogues: " + msg)


def split_list(text):
    return [x.strip() for x in (text or "").split(",") if x.strip()]


def map_requires(text, once):
    out = []
    for term in split_list(text):
        if term == "blocker_disarmed":
            term = "stripped_disarmed"
        out.append(term)
    if once and ("!asked:" + once) not in out:
        out.append("!asked:" + once)
    return out


def _effect(eid, params, commit, receipt):
    return {"Id": eid, "Params": params, "Commit": commit, "Receipt": receipt}


def _receipt(eid):
    if eid in ONCE_EFFECTS:
        return "once"
    if eid in NEVER_EFFECTS:
        return "never"
    return "never"


def map_effect(verb, param, commit, npc):
    verb = (verb or "").replace("PROBE:", "").strip()
    if not verb:
        return []
    verbs = [v.strip() for v in verb.split("+")]
    pars = [param] if len(verbs) == 1 else [x.strip() for x in (param or "").split(";")]
    out = []
    for i, v in enumerate(verbs):
        p = pars[i] if i < len(pars) else None
        p = p.strip() if isinstance(p, str) else p
        if v == "mark":
            if not p:
                fail("mark 缺 flag")
            out.append(_effect("mark", {"flag": p}, commit, "once"))
        elif v == "clue":
            if not p:
                fail("clue 缺参数")
            out.append(_effect("clue", {"clue": p}, commit, "once"))
        elif v == "hazard":
            if not p:
                fail("hazard 缺参数")
            out.append(_effect("hazard", {"flag": p}, commit, "once"))
        elif v == "met":
            out.append(_effect("mark", {"flag": "met"}, commit, "once"))
        elif v == "resolve_event":
            parts = (p or "").split("|")
            if not parts or not parts[0].isdigit():
                fail("resolve_event 参数必须是 '<n>|faction|delta'")
            out.append(_effect("resolve_event", {"event": parts[0]}, commit, "once"))
            if len(parts) >= 3:
                out.append(_effect("relation", {"faction": parts[1], "delta": parts[2]}, commit, "once"))
        elif v == "open_gate_award" or v == "open_gate":
            parts = (p or "").split("|")
            params = {"route": parts[0] if parts and parts[0] else "unknown"}
            if len(parts) >= 2 and parts[1]:
                params["faction"] = parts[1]
            if len(parts) >= 3 and parts[2]:
                params["delta"] = parts[2]
            if len(parts) >= 4 and parts[3] in ("1", "true"):
                params["crime"] = "true"
            out.append(_effect("open_gate", params, commit, "once"))
        elif v == "bribe":
            amount = p if p and p.isdigit() else "100"
            out.append(_effect("pay", {"amount": amount, "to": "questioner", "reason": "bribe"}, commit, "never"))
            out.append(_effect("open_gate", {"route": "money", "faction": "fixers", "delta": "-5",
                                             "hazard": "hazard_money_leverage"}, commit, "once"))
        elif v == "pay":
            amount = p if p and p.isdigit() else "30"
            out.append(_effect("pay", {"amount": amount, "to": "questioner"}, commit, "never"))
        elif v == "pay_to":
            parts = (p or "").split(";")
            if len(parts) < 2:
                fail("pay_to 参数必须是 '<npc>;<amount>'")
            out.append(_effect("pay", {"amount": parts[1], "to": parts[0]}, commit, "never"))
        elif v == "offer_money":
            out.append(_effect("money_offer", {}, commit, "never"))
        elif v == "money_deal":
            out.append(_effect("money_deal", {}, commit, "never"))
        elif v == "errand":
            pay = p if p and p.isdigit() else "50"
            out.append(_effect("errand", {"pay": pay}, commit, "never"))
        elif v == "street_help_reward":
            out.append(_effect("grant_gold", {"amount": "50"}, commit, "once"))
            out.append(_effect("reward_receipt", {"npc": "pawnbroker", "key": "street-help"}, commit, "once"))
        elif v == "redeem_keepsake":
            out.append(_effect("pay", {"amount": "30", "to": npc}, commit, "never"))
            out.append(_effect("reward_receipt", {"npc": "pawnbroker", "key": "keepsake-paid"}, commit, "once"))
        elif v == "accept_quest":
            params = {"npc": npc}
            solved = {"butcher": "2", "widow": "3"}.get(npc, "")
            if solved:
                params["solved_event"] = solved
            out.append(_effect("quest_accept", params, commit, "once"))
        elif v == "turn_in":
            out.append(_effect("quest_turn_in", {"npc": npc}, commit, "once"))
        elif v == "complete_quest":
            parts = (p or "").split("|")
            if len(parts) < 2:
                fail("complete_quest 参数必须是 '<npc>|<resolution>'")
            out.append(_effect("complete_quest", {"npc": parts[0], "resolution": parts[1]}, commit, "once"))
        elif v == "collect_debt":
            amount = p if p and p.isdigit() else "120"
            out.append(_effect("debt_collect", {"from": npc, "amount": amount, "quest": "pawnbroker"},
                               commit, "once"))
        elif v == "fight":
            target = p if p else None
            params = {"target": target} if target else {}
            out.append(_effect("start_fight", params, commit, "never"))
        elif v == "forced_fail":
            out.append(_effect("force_defeat", {}, commit, "never"))
        elif v == "favour":
            parts = (p or "").split(";")
            if len(parts) < 2:
                fail("favour 参数必须是 '<faction>;<delta>'")
            out.append(_effect("relation", {"faction": parts[0], "delta": parts[1]}, commit, "once"))
        elif v == "start_event":
            out.append(_effect("start_event", {"event": p or "1"}, commit, "never"))
        else:
            fail("未登记动词 '%s'（param=%r）" % (v, p))
    return out


def option_effects(action, param, commit, npc):
    return map_effect(action, param, commit, npc)


def stable_option_id(player_key, index, used):
    tail = player_key.rsplit("_", 1)[-1] if player_key else ""
    base = tail if re.match(r"^[a-z0-9_]+$", tail or "") else "opt%d" % index
    if not base:
        base = "opt%d" % index
    oid = base
    n = 2
    while oid in used:
        oid = "%s_%d" % (base, n)
        n += 1
    used.add(oid)
    return oid


def migrate(npc, legacy):
    nodes = legacy.get("Nodes") or []
    node_ids = [n.get("Id") for n in nodes if n.get("Id")]
    if not node_ids:
        fail("%s 没有节点" % npc)

    actors = [npc]
    out_nodes = []
    for n in nodes:
        nid = n.get("Id")
        text_key = n.get("NpcKey")
        if not text_key:
            fail("%s/%s 缺 NpcKey（新格式必须有本地化键）" % (npc, nid))
        on_enter = option_effects(n.get("EnterAction"), n.get("EnterParam"), "on_enter", npc)
        used = set()
        options = []
        for idx, t in enumerate(n.get("Transitions") or []):
            pkey = t.get("PlayerKey")
            if not pkey:
                fail("%s/%s 选项 %d 缺 PlayerKey" % (npc, nid, idx))
            oid = stable_option_id(pkey, idx, used)
            requires = map_requires(t.get("Requires"), t.get("Once"))
            check = None
            ctype = t.get("CheckType") or "None"
            if ctype == "SkillCheck":
                parts = (t.get("CheckTag") or "").split("|")
                if len(parts) < 2:
                    fail("%s/%s/%s CheckTag 非法" % (npc, nid, oid))
                target, ability = parts[0].strip(), parts[1].strip()
                if target not in actors:
                    actors.append(target)
                check = {"Skill": ability, "Target": target,
                         "Success": t.get("NextNodeOnSuccess") or "",
                         "Fail": t.get("NextNodeOnFail") or ""}
                if not check["Success"]:
                    fail("%s/%s/%s 检定缺少成功节点" % (npc, nid, oid))
            elif ctype not in ("None", "", None):
                fail("%s/%s/%s 未知 CheckType '%s'" % (npc, nid, oid, ctype))

            if (t.get("Action") or "").upper() == "PROBE:BRIBE":
                if "money_attitude:open" not in requires:
                    requires.append("money_attitude:open")

            opt = {"Id": oid, "PlayerKey": pkey, "Requires": requires,
                   "Effects": option_effects(t.get("Action"), t.get("ActionParam"), "on_select", npc)}
            if check is not None:
                opt["Check"] = check
            else:
                opt["NextNode"] = t.get("NextNodeOnSuccess") or ""
            options.append(opt)

        out_nodes.append({
            "Id": nid,
            "Speaker": "npc",
            "TextKey": text_key,
            "LineActions": n.get("NpcActions") or [],
            "Requires": [],
            "OnEnter": on_enter,
            "Options": options,
        })

    triggers = [
        {"Id": "player_talk", "Source": "player", "Purpose": "talk", "Conditions": [],
         "Rank": [], "CooldownSec": 0, "ReentrySec": 0, "OnCancel": "close"},
        {"Id": "event_join", "Source": "event", "Purpose": "join", "Conditions": [],
         "Rank": [], "CooldownSec": 0, "ReentrySec": 0, "OnCancel": "release"},
    ]
    entries = []

    def add_entry(rid, trigger, conds, prio, node):
        if node in node_ids and not any(e["Id"] == rid for e in entries):
            entries.append({"Id": rid, "Trigger": trigger, "Conditions": conds,
                            "Priority": prio, "Node": node})

    add_entry("player_turnin", "player_talk", ["quest_done", "!quest_turned"], 100, npc + "_turnin")
    add_entry("player_chat", "player_talk", ["quest_turned"], 90, npc + "_chat")
    add_entry("player_hub", "player_talk", ["step>=1"], 80, npc + "_hub")
    add_entry("player_intro", "player_talk", [], 70, npc + "_intro")
    legacy_entry = legacy.get("EntryNode")
    if legacy_entry and not any(e["Node"] == legacy_entry for e in entries):
        add_entry("player_entry", "player_talk", [], 60, legacy_entry)
    if not any(e["Trigger"] == "player_talk" for e in entries):
        add_entry("player_first", "player_talk", [], 10, node_ids[0])

    for nid in node_ids:
        m = re.match(r"^event_(\d+)_join$", nid)
        if m:
            entries.append({"Id": "join_" + m.group(1), "Trigger": "event_join",
                            "Conditions": ["event_active:" + m.group(1)],
                            "Priority": 100, "Node": nid})

    return {
        "_comment": "由 Scripts/migrate_story_dialogues.py 从旧 %s 迁移；旧文件仅作回退副本。改内容请改脚本/旧文件后重跑。" % ("story_probe_%s.json" % npc),
        "Schema": "story_dialogue/2",
        "DialogueId": "story_" + npc,
        "ContentVersion": 1,
        "EntryOption": legacy.get("EntryOption") or ("LWN_story_%s_entry" % npc),
        "SkipVanillaOpening": bool(legacy.get("SkipVanillaOpening")),
        "Participants": ["npc", "player"],
        "Actors": actors,
        "Triggers": triggers,
        "Entries": entries,
        "Nodes": out_nodes,
    }


def main():
    if not os.path.isdir(DIALOGUES):
        fail("找不到目录 " + DIALOGUES)
    written = 0
    for name in sorted(os.listdir(DIALOGUES)):
        if not name.startswith("story_probe_") or not name.endswith(".json"):
            continue
        if name.endswith(".dialog.json"):
            continue
        npc = name[len("story_probe_"):-len(".json")]
        if npc == "blocker":
            continue  # 门岗已手工整份迁移（gate_interrogation.dialog.json）
        path = os.path.join(DIALOGUES, name)
        legacy = json.load(io.open(path, encoding="utf-8"))
        if legacy.get("Npc") and legacy["Npc"] != npc:
            fail("%s 的 Npc 字段与文件名不一致：%s" % (name, legacy["Npc"]))
        data = migrate(npc, legacy)
        out = os.path.join(DIALOGUES, "story_%s.dialog.json" % npc)
        with io.open(out, "w", encoding="utf-8", newline="\n") as f:
            f.write(json.dumps(data, ensure_ascii=False, indent=2) + "\n")
        written += 1
        print("wrote %s nodes=%d entries=%d actors=%s" %
              (os.path.basename(out), len(data["Nodes"]), len(data["Entries"]), ",".join(data["Actors"])))
    if written == 0:
        fail("没有可迁移的旧对话文件")
    print("done: %d files" % written)


if __name__ == "__main__":
    sys.exit(main())
