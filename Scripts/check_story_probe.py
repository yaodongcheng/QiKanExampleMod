# -*- coding: utf-8 -*-
"""序章（市井 · 城门序章）离线体检。

检查四件事（每条都是实机前能抓的错）：
  1. 对话 JSON 结构：节点 Id 唯一、EntryNode 存在、NextNode* 不悬空、每个节点至少有 1 个出口。
  2. 对话 JSON 用到的本地化键，在 **英文层 + 中文层** 都存在。
  3. C# 里出现过的 `LWN_story_*` 字面量键，两层都存在（防手滑打错 key → 玩家看到 key 名）。
  4. 两个语言 XML 里没有 BMP 外字符（emoji → 引擎解析崩，CLAUDE.md 铁律 14）。

退出码：0 = 全过；1 = 有错（负面测试见 Scripts/test_negative_checks.py 同名条目）。
用法：python Scripts/check_story_probe.py [--module <模块根>]
"""
import argparse
import glob
import io
import json
import math
import os
import re
import sys
import xml.etree.ElementTree as ET

DEFAULT_MODULE = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))


def load_lang(path):
    if not os.path.exists(path):
        return {}
    root = ET.parse(path).getroot()
    out = {}
    for el in root.iter("string"):
        out[el.get("id")] = el.get("text")
    return out


def dialogue_keys(module):
    keys = set()
    files = sorted(glob.glob(os.path.join(module, "ModuleData", "DesignData", "Dialogues", "story_probe_*.json")))
    for path in files:
        data = json.load(io.open(path, encoding="utf-8"))
        if data.get("EntryOption"):
            keys.add(data["EntryOption"])
        for node in data.get("Nodes", []):
            if node.get("NpcKey"):
                keys.add(node["NpcKey"])
            for tr in node.get("Transitions", []):
                if tr.get("PlayerKey"):
                    keys.add(tr["PlayerKey"])
    return files, keys


def code_keys(module):
    keys = set()
    src = os.path.join(module, "ExampleModVS", "ExampleMod", "ExampleMod")
    for path in glob.glob(os.path.join(src, "**", "*.cs"), recursive=True):
        try:
            text = io.open(path, encoding="utf-8").read()
        except UnicodeDecodeError:
            continue
        for m in re.finditer(r'"(LWN_story_[A-Za-z0-9_]+)"', text):
            keys.add(m.group(1))
    return keys


def check_json(module, problems):
    cn = _load_lang_cached(module, True)
    files, keys = dialogue_keys(module)
    if not files:
        problems.append("no dialogue json files under ModuleData/DesignData/Dialogues/")
        return keys, 0

    known_terms = {"disguised", "noble", "stripped", "quest_accepted", "quest_done", "quest_turned",
                   "gate_open", "blocker_disarmed", "can_redeem_keepsake", "errand_ready", "street_help_pending",
                   "resolution:force", "resolution:charm", "resolution:guile",
                   "attitude:open", "attitude:covert", "attitude:disdain"}
    known_prefix = ("gold>=", "item:", "clue:", "hazard:", "step>=",
                    "quest_accepted@", "quest_done@", "quest_turned@", "asked:", "reward:", "event_resolved:")

    node_total = option_total = 0
    for path in files:
        name = os.path.basename(path)
        data = json.load(io.open(path, encoding="utf-8"))
        nodes = data.get("Nodes", [])
        node_total += len(nodes)

        ids = [n.get("Id") for n in nodes]
        if len(ids) != len(set(ids)):
            dup = sorted({i for i in ids if ids.count(i) > 1})
            problems.append(f"{name}: duplicate node ids {dup}")

        idset = set(ids)
        if data.get("EntryNode") not in idset:
            problems.append(f"{name}: EntryNode '{data.get('EntryNode')}' missing")

        for node in nodes:
            nid = node.get("Id")
            if not node.get("NpcKey"):
                problems.append(f"{name}:{nid}: missing NpcKey")
            trs = node.get("Transitions") or []
            option_total += len(trs)
            if not trs:
                problems.append(f"{name}:{nid}: no transitions (dead end)")
            for tr in trs:
                if not tr.get("PlayerKey"):
                    problems.append(f"{name}:{nid}: transition without PlayerKey")
                nxt = tr.get("NextNodeOnSuccess")
                if nxt and nxt not in idset:
                    problems.append(f"{name}:{nid}: -> missing node '{nxt}'")
                fail = tr.get("NextNodeOnFail")
                if fail and fail not in idset:
                    problems.append(f"{name}:{nid}: -> missing fail node '{fail}'")
                if tr.get("CheckType") == "SkillCheck" and not tr.get("NextNodeOnFail"):
                    problems.append(f"{name}:{nid}: SkillCheck without NextNodeOnFail")

                # Requires 词表校验：写错一个词 = 那条选项**永远不出现**（静默失效，最难查）
                req = tr.get("Requires")
                if req:
                    for term in req.split(","):
                        term = term.strip()
                        if term.startswith("!"):
                            term = term[1:]
                        if not term:
                            problems.append(f"{name}:{nid}: empty Requires term")
                            continue
                        numeric = next((p for p in ("gold>=", "step>=", "event_resolved:") if term.startswith(p)), None)
                        if numeric:
                            value = term[len(numeric):]
                            if not re.fullmatch(r"[0-9]+", value) or int(value) > 2147483647:
                                problems.append(f"{name}:{nid}: invalid numeric Requires term '{term}'")
                            continue
                        prefix = next((p for p in known_prefix if term.startswith(p)), None)
                        if prefix and not term[len(prefix):]:
                            problems.append(f"{name}:{nid}: missing argument in Requires term '{term}'")
                            continue
                        if term in known_terms or term.startswith(known_prefix):
                            continue
                        problems.append(f"{name}:{nid}: unknown Requires term '{term}'")

        # 🔴 规则七：同一个 Once 闸门不许被同一个节点下的多条不同选项共用
        # 🔴 规则十二：所有节点必须从合法入口可达（禁止孤立死节点）
        # 🔴 规则十三：禁止自指循环（NextNode == node.Id）与中途跳回 *_intro
        # 🔴 规则十四：付费动作（pay / pay_to / bribe / money_deal / offer_money）必须有 gold>= 条件
        valid_roots = set()
        if data.get("EntryNode"):
            valid_roots.add(data["EntryNode"])
        for nid in idset:
            if (nid.endswith("_intro") or nid.endswith("_hub") or nid.endswith("_turnin")
                    or nid.endswith("_chat") or nid.startswith("event_") and nid.endswith("_join")
                    or nid in ("blocker_first", "blocker_open")):
                valid_roots.add(nid)

        reachable = set(valid_roots)
        queue = list(valid_roots)
        by_id = {n.get("Id"): n for n in nodes}
        while queue:
            cur = queue.pop(0)
            cnode = by_id.get(cur)
            if not cnode:
                continue
            for tr2 in (cnode.get("Transitions") or []):
                for target in (tr2.get("NextNodeOnSuccess"), tr2.get("NextNodeOnFail")):
                    if target and target in idset and target not in reachable:
                        reachable.add(target)
                        queue.append(target)

        for nid in ids:
            if nid not in reachable:
                problems.append(f"dialogue[{name}]: 孤立不可达节点 '{nid}'（没有任何入口或选项能走到它）")

        for nd in nodes:
            nid = nd.get("Id")
            once_seen = {}
            for tr2 in (nd.get("Transitions") or []):
                pk = tr2.get("PlayerKey") or ""
                once_tag = tr2.get("Once")
                if once_tag:
                    if once_tag in once_seen and once_seen[once_tag] != pk:
                        problems.append(f"dialogue[{name}:{nid}]: 两条选项共用同一个 Once='{once_tag}'（点一条另一条也会消失）")
                    once_seen[once_tag] = pk

                for target in (tr2.get("NextNodeOnSuccess"), tr2.get("NextNodeOnFail")):
                    if not target:
                        continue
                    if target == nid:
                        problems.append(f"dialogue[{name}:{nid}]: 选项 '{pk}' 自指循环指向自身 '{nid}'")
                    if target.endswith("_intro") and not (nid.startswith("event_") and nid.endswith("_join")):
                        problems.append(f"dialogue[{name}:{nid}]: 选项 '{pk}' 中途跳回开场节点 '{target}'（应跳 *_hub）")

                act = tr2.get("Action") or ""
                req = tr2.get("Requires") or ""
                if any(v in act for v in ("PROBE:pay", "PROBE:bribe", "PROBE:money_deal", "PROBE:offer_money")):
                    if "gold>=" not in req:
                        problems.append(f"dialogue[{name}:{nid}]: 付费选项 '{pk}' (Action={act}) 缺少 'gold>=' 显示条件")

        # 🔴 同一个回答节点不许被"两种不同问法"指向 —— 否则第二次问会得到一模一样的回答（NPC 像失忆）
        #    菜单节点（出度≥5 或 *_hub）除外：那里本来就是"回头再说"的汇总口。
        seg = []
        for nd in data.get("Nodes", []):
            for tr2 in nd.get("Transitions", []):
                tg = tr2.get("NextNodeOnSuccess")
                if not tg:
                    continue
                _outdeg = {x.get("Id"): len(x.get("Transitions") or []) for x in data["Nodes"]}
                if _outdeg.get(tg, 0) >= 5 or str(tg).endswith("_hub"):
                    continue
                lbl = re.sub(r"^\[[^\]]*\]\s*", "", cn.get(tr2.get("PlayerKey"), "")).strip()
                if lbl:
                    seg.append((tg, lbl))
        by_target = {}
        for tg, lbl in seg:
            by_target.setdefault(tg, set()).add(lbl)
        for tg, labels in by_target.items():
            if len(labels) > 1:
                problems.append("dialogue[%s]: 节点 '%s' 被 %d 种不同问法指向（重复问会得到同样的回答）"
                                % (name, tg, len(labels)))


    return keys, node_total


# ══════════════════════════════════════════════════════════════════
# 数据文件体检（2026-11-13）：Cast / Tuning / Stage
#   设计稿改表最容易犯的错 = 写错一个字段名 / 引用一个不存在的人 / 键拼错。
#   这些错误在游戏里全是**静默失效**，所以必须在离线体检里抓。
# ══════════════════════════════════════════════════════════════════

STAGE_ATOMS = {"player_freeze", "player_release", "camera_cut", "camera_return",
               "prop_spawn", "actor_place", "actor_action", "actor_say", "wait"}
STAGE_ANCHORS_BUILTIN = {"player", "gate", "gate_inner"}
TUNING_SECTIONS = ("Ambient", "Events", "Skits", "Gate", "Greeting", "Crime", "Intro", "Performance")


def load_data_json(path):
    if not os.path.exists(path):
        return None
    try:
        return json.load(io.open(path, encoding="utf-8"))
    except Exception as exc:
        return {"__error__": str(exc)}


def check_cast(module, problems):
    """StoryProbeCast.json：结构 + 引用闭合；返回 (本地化键, 数据)。"""
    path = os.path.join(module, "ModuleData", "DesignData", "StoryProbeCast.json")
    data = load_data_json(path)
    keys = set()
    if data is None:
        problems.append("StoryProbeCast.json missing")
        return keys, None
    if "__error__" in data:
        problems.append("StoryProbeCast.json parse error: %s" % data["__error__"])
        return keys, None

    factions = data.get("Factions") or []
    npcs = data.get("Npcs") or []
    street = data.get("StreetLife") or []
    events = data.get("Events") or []

    fids = {f.get("Id") for f in factions}
    nids = [n.get("Id") for n in npcs]
    if len(nids) != len(set(nids)):
        problems.append("cast: duplicate npc id")

    for f in factions:
        if f.get("NameKey"):
            keys.add(f["NameKey"])
        if not f.get("Id"):
            problems.append("cast: faction without Id")

    for n in npcs:
        nid = n.get("Id") or "<no id>"
        if not n.get("TemplateId"):
            problems.append("cast:%s: missing TemplateId" % nid)
        if not n.get("NameKey"):
            problems.append("cast:%s: missing NameKey" % nid)
        else:
            keys.add(n["NameKey"])
        if n.get("MoneyHintKey"):
            keys.add(n["MoneyHintKey"])
        if n.get("QuestLogKey"):
            keys.add(n["QuestLogKey"])

        age = n.get("Age", -1)
        if age != -1 and age < 18:
            problems.append("cast:%s: Age=%s < 18 (engine treats under-18 as a child)" % (nid, age))

        fac = n.get("Faction")
        if fac and fac not in fids:
            problems.append("cast:%s: unknown Faction '%s'" % (nid, fac))

        tgt = n.get("QuestTargetNpcId")
        if tgt and tgt not in nids:
            problems.append("cast:%s: QuestTargetNpcId '%s' is not an npc id" % (nid, tgt))

        money = n.get("Money") or "Disdain"
        if money not in ("Open", "Covert", "Disdain"):
            problems.append("cast:%s: bad Money '%s' (Open/Covert/Disdain)" % (nid, money))
        if money == "Open" and not n.get("MoneyPrice"):
            problems.append("cast:%s: Money=Open but MoneyPrice is empty" % nid)
        if money == "Covert" and not n.get("MoneyCovertPrice"):
            problems.append("cast:%s: Money=Covert but MoneyCovertPrice is empty" % nid)

        for it in (n.get("RewardItems") or []):
            if it not in (data.get("ItemAliases") or {}) and not isinstance(it, str):
                problems.append("cast:%s: bad RewardItems entry %r" % (nid, it))

    for s in street:
        if s.get("NpcId") not in nids:
            problems.append("cast: StreetLife for unknown npc '%s'" % s.get("NpcId"))
        if not s.get("Actions"):
            problems.append("cast: StreetLife '%s' has no Actions" % s.get("NpcId"))
        keys.update(s.get("LineKeys") or [])

    for e in events:
        for field in ("ActorA", "ActorB", "HostNpcId"):
            v = e.get(field)
            if v and v not in nids:
                problems.append("cast: event %s %s='%s' is not an npc" % (e.get("Index"), field, v))
        lk = e.get("LineKeys") or []
        lf = e.get("LineFallbacks") or []
        if len(lk) != len(lf):
            problems.append("cast: event %s LineKeys(%d) != LineFallbacks(%d)"
                            % (e.get("Index"), len(lk), len(lf)))
        if not lk:
            problems.append("cast: event %s has no LineKeys" % e.get("Index"))
        if e.get("Index") is None:
            problems.append("cast: an event has no Index")
        keys.update(lk)

    return keys, data


def check_tuning(module, problems):
    """StoryProbeTuning.json：五个段都要在，且 min/max 成对时 min <= max。"""
    path = os.path.join(module, "ModuleData", "DesignData", "StoryProbeTuning.json")
    data = load_data_json(path)
    if data is None:
        problems.append("StoryProbeTuning.json missing")
        return
    if "__error__" in data:
        problems.append("StoryProbeTuning.json parse error: %s" % data["__error__"])
        return

    for sec in TUNING_SECTIONS:
        if not isinstance(data.get(sec), dict):
            problems.append("tuning: section '%s' missing" % sec)

    for sec, obj in data.items():
        if not isinstance(obj, dict):
            continue
        for key, val in obj.items():
            if key.endswith("MinSec") or key.endswith("MaxSec"):
                continue
        # 成对的 Min/Max
        for key in obj:
            if key.endswith("MinSec"):
                mate = key[:-len("MinSec")] + "MaxSec"
                if mate in obj and obj[mate] < obj[key]:
                    problems.append("tuning: %s.%s > %s" % (sec, key, mate))

    import math
    for key in ("ActorReadyTimeoutSec", "DebugActorDurationSec", "AmbientPoseDurationSec"):
        value = (data.get("Performance") or {}).get(key)
        if type(value) not in (int, float) or not math.isfinite(value) or value <= 0:
            problems.append("tuning: Performance.%s must be finite and positive" % key)
    gate = data.get("Gate") or {}
    for key in ("HalfWidthMeters", "ClimbHalfWidthMeters", "InsideMeters", "OutsideMeters",
                "PushBackMeters", "GroundToleranceMeters", "LandingToleranceMeters", "LandingVerticalSpeedMeters",
                "LandingStableSec", "CrossingMaxStepMeters",
                "PastMeters", "GoalRadiusMeters", "ClimbHeightMeters"):
        value = gate.get(key)
        if type(value) not in (int, float) or not math.isfinite(value) or value <= 0:
            problems.append("tuning: Gate.%s must be finite and positive" % key)
    layouts = gate.get("Layouts")
    if not isinstance(layouts, dict) or not layouts:
        problems.append("tuning: Gate.Layouts must define a verified physical gate")
    else:
        for scene_name, layout in layouts.items():
            valid = isinstance(layout, dict)
            for key in ("Gate", "Outside"):
                point = layout.get(key) if valid else None
                if (not isinstance(point, list) or len(point) != 3
                    or any(type(x) not in (int, float) or not math.isfinite(x) for x in point)):
                    problems.append("tuning: Gate.Layouts.%s.%s must be three finite coordinates" % (scene_name, key))
                    valid = False
            if valid and sum((layout["Gate"][i] - layout["Outside"][i]) ** 2 for i in (0, 1)) <= 0:
                problems.append("tuning: Gate.Layouts.%s has no outside-to-inside direction" % scene_name)


def check_stage(module, problems):
    """StoryStage.json：原子名 / 语义位 / 必填参数。"""
    path = os.path.join(module, "ModuleData", "DesignData", "StoryStage.json")
    data = load_data_json(path)
    if data is None:
        problems.append("StoryStage.json missing")
        return 0
    if "__error__" in data:
        problems.append("StoryStage.json parse error: %s" % data["__error__"])
        return 0

    anchors = set(STAGE_ANCHORS_BUILTIN) | set((data.get("Anchors") or {}).keys())
    for name, tags in (data.get("Anchors") or {}).items():
        if not isinstance(tags, list) or not tags:
            problems.append("stage: anchor '%s' has no scene tags" % name)

    beats = data.get("Beats") or []
    if not beats:
        problems.append("StoryStage.json: no Beats")
        return 0

    for i, b in enumerate(beats):
        cmd = b.get("Cmd")
        if cmd not in STAGE_ATOMS:
            problems.append("stage[%d]: unknown atom '%s'" % (i, cmd))
            continue
        anchor = b.get("Anchor")
        if anchor and anchor not in anchors:
            problems.append("stage[%d]: unknown anchor '%s'" % (i, anchor))
        if cmd == "prop_spawn" and not b.get("Prefab"):
            problems.append("stage[%d]: prop_spawn without Prefab" % i)
        if cmd == "camera_cut" and not b.get("Case"):
            problems.append("stage[%d]: camera_cut without Case" % i)
        if cmd in ("actor_place", "actor_action", "actor_say") and not b.get("Actor"):
            problems.append("stage[%d]: %s without Actor" % (i, cmd))
        if cmd == "actor_action" and not b.get("Action"):
            problems.append("stage[%d]: actor_action without Action" % i)
        if b.get("At") is None:
            problems.append("stage[%d]: missing At" % i)

    return len(beats)


# ══════════════════════════════════════════════════════════════════
# 引用存在性审查（2026-11-13 立，用户裁定）
#
# 🔴 这一段的由来：`StoryProbeCast.json` 里写过 `ledger_book` / `pick_lock_kit` /
#    `whetstone` 三个物品 id —— **游戏里根本没有这三件东西**，靠别名表回退成了 jewelry，
#    玩家拿到的东西和设计写的不是一回事，而且**离线体检全绿**。
#    ⇒ 从此以后：**数据里写下的每一个 id，都必须在这里被证明存在**。
#
# 查七类：物品 / 动作名 / prefab / 角色模板 / 机位 case / 语义位标签 / 物品XML注册
# ══════════════════════════════════════════════════════════════════

# 只认「原版卡拉迪亚」这个运行环境（用户 2026-11-13 裁定：不挂任何世界观 mod）
LOADED_MODULES = ("Native", "SandBoxCore", "SandBox", "LivingWorldNpcs")

# 故意不注册在 SubModule 里的物品文件（由代码按需注册 / 未发布）
UNREGISTERED_BY_DESIGN = {"items/spells"}

_CACHE = {}


def _game_root(module):
    """LWN 模块在 <游戏根>/Modules/LivingWorldNpcs ⇒ 游戏根 = 上两级。"""
    return os.path.dirname(os.path.dirname(os.path.abspath(module)))


def _module_data(game_root, mod):
    return os.path.join(game_root, "Modules", mod, "ModuleData")


def _iter_xml(paths):
    for p in paths:
        try:
            yield p, io.open(p, encoding="utf-8-sig", errors="ignore").read()
        except Exception:
            continue


def _glob_mod(game_root, mod, pattern, recursive=True):
    d = _module_data(game_root, mod)
    if not os.path.isdir(d):
        return []
    return glob.glob(os.path.join(d, pattern), recursive=recursive)


def known_items(game_root):
    key = ("items", game_root)
    if key in _CACHE:
        return _CACHE[key]
    ids = set()
    files = []
    for mod in LOADED_MODULES:
        files += _glob_mod(game_root, mod, "**/*.xml")
    for _, text in _iter_xml(files):
        if "<Item" not in text and "<CraftedItem" not in text:
            continue
        for m in re.finditer(r'<(?:Item|CraftedItem)\b[^>]*\bid="([^"]+)"', text):
            ids.add(m.group(1))
    _CACHE[key] = ids
    return ids


def known_actions(game_root):
    key = ("acts", game_root)
    if key in _CACHE:
        return _CACHE[key]
    acts = set()
    for mod in LOADED_MODULES:
        for _, text in _iter_xml(_glob_mod(game_root, mod, "**/action_sets*.xml")):
            acts.update(re.findall(r'type="(act_[A-Za-z0-9_]+)"', text))
    _CACHE[key] = acts
    return acts


def known_prefabs(game_root, scene=None):
    """prefab 名 = 各模块 `Prefabs/*.xml` 定义的 ∪ **目标场景 scene.xscene 里实际摆着的**。

    🔴 `bd_table_*` / `carpet_empire_a` 这类随资产包编译进去的**没有 XML 定义**，
       只在场景文件里以 `prefab="..."` 出现 —— 只扫 Prefabs 目录会把它们全误判成"不存在"。
    🔴 Prefabs 在**模块根**（`Modules/<mod>/Prefabs/`），不在 ModuleData 下。
    """
    key = ("prefabs", game_root, scene)
    if key in _CACHE:
        return _CACHE[key]
    names, tags = set(), set()
    for mod in LOADED_MODULES:
        d = os.path.join(game_root, "Modules", mod, "Prefabs")
        if not os.path.isdir(d):
            continue
        for _, text in _iter_xml(glob.glob(os.path.join(d, "*.xml"))):
            names.update(re.findall(r'<game_entity[^>]*name="([^"]+)"', text))
            tags.update(re.findall(r'<tag name="([^"]+)"', text))
    if scene:
        xscene = os.path.join(game_root, "Modules", "SandBoxCore", "SceneObj", scene, "scene.xscene")
        if os.path.exists(xscene):
            text = io.open(xscene, encoding="utf-8", errors="ignore").read()
            names.update(re.findall(r'prefab="([^"]+)"', text))
    _CACHE[key] = (names, tags)
    return names, tags


def known_templates(game_root):
    key = ("tmpl", game_root)
    if key in _CACHE:
        return _CACHE[key]
    ids = set()
    for mod in LOADED_MODULES:
        for fname in ("spnpccharacters.xml", "spnpccharactertemplates.xml", "bandits.xml"):
            for _, text in _iter_xml(_glob_mod(game_root, mod, fname, recursive=False)):
                ids.update(re.findall(r'<NPCCharacter[^>]*\bid="([^"]+)"', text))
    _CACHE[key] = ids
    return ids


def known_camera_cases(module):
    path = os.path.join(module, "ModuleData", "DesignData", "Camera.csv")
    if not os.path.exists(path):
        return set()
    text = io.open(path, encoding="utf-8-sig", errors="ignore").read()
    return {line.split(",")[0].strip() for line in text.split("\n")[1:] if line.strip()}


def _load_lang_cached(module, cn):
    key = ("cn" if cn else "en", module)
    if key in _CACHE:
        return _CACHE[key]
    rel = ("ModuleData", "Languages", "CNs", "std_LivingWorldNpcs_strings.xml") if cn else           ("ModuleData", "Languages", "std_LivingWorldNpcs_strings.xml")
    path = os.path.join(module, *rel)
    data = load_lang(path) if os.path.exists(path) else {}
    _CACHE[key] = data
    return data

HUMAN_SET_CACHE = {}


def human_playable(game_root, module=None):
    """`monsters.xml` 写明 human 的默认 action_set = as_human_warrior ⇒ 只有这里的动作 NPC 能播。"""
    key = ("human_plays", game_root)
    if key in HUMAN_SET_CACHE:
        return HUMAN_SET_CACHE[key]
    acts = set()
    path = os.path.join(game_root, "Modules", "Native", "ModuleData", "action_sets.xml")
    if os.path.exists(path):
        text = io.open(path, encoding="utf-8-sig", errors="ignore").read()
        blocks = re.split(r'<action_set\s+id="', text)
        for b in blocks[1:]:
            if b.split('"')[0] == "as_human_warrior":
                acts = set(re.findall(r'<action\s+type="(act_[a-z0-9_]+)"', b))
                break
    # 我们自己在 LWN 的 action_sets.xml 里追加合并进 as_human_warrior 的动作也算可播
    lwn = os.path.join(module, "ModuleData", "action_sets.xml") if module else ""
    if os.path.exists(lwn):
        lt = io.open(lwn, encoding="utf-8-sig", errors="ignore").read()
        acts |= set(re.findall(r'<action\s+type="(act_[a-z0-9_]+)"', lt))
    HUMAN_SET_CACHE[key] = acts
    return acts


def check_line_actions(module, problems, cast):
    """动作写在**对话节点**的 `NpcActions` 上：必须能播，且条数与台词行数一致。"""
    game_root = _game_root(module)
    playable = human_playable(game_root, module)
    if not playable:
        problems.append("line actions: cannot read Native action_sets.xml (game root=%s)" % game_root)
        return 0

    cn = _load_lang_cached(module, True)
    total = 0
    files = sorted(glob.glob(os.path.join(module, "ModuleData", "DesignData", "Dialogues", "story_probe_*.json")))
    for path in files:
        data = json.load(io.open(path, encoding="utf-8"))
        for node in data.get("Nodes", []):
            key = node.get("NpcKey") or ""
            acts = node.get("NpcActions")
            if not acts:
                continue
            total += len(acts)
            for a in acts:
                if a and a not in playable:
                    problems.append("node[%s:%s]: 动作 '%s' 不在 as_human_warrior（NPC 播不出来）"
                                    % (os.path.basename(path), node.get("Id"), a))
            lines = [l for l in cn.get(key, "").replace("&#10;", chr(10)).split(chr(10)) if l.strip()]
            if key and len(lines) != len(acts):
                problems.append("node[%s:%s]: %d 个动作 vs %d 行台词"
                                % (os.path.basename(path), node.get("Id"), len(acts), len(lines)))

    # 常驻冒泡/群戏的动作仍写在 cast 表里，同样查可播性
    if cast:
        for s2 in (cast.get("StreetLife") or []):
            for a in (s2.get("Actions") or []):
                if a not in playable:
                    problems.append("streetlife[%s]: '%s' 播不出来" % (s2.get("NpcId"), a))
    return total


def check_references(module, problems, cast, stage, scene=None):
    """数据里写下的每一个外部 id —— 逐条证明它存在。"""
    game_root = _game_root(module)
    if not os.path.isdir(os.path.join(game_root, "Modules", "Native")):
        print("  (skip reference audit: game root not found at %s)" % game_root)
        return

    items = known_items(game_root)
    acts = known_actions(game_root)
    prefabs, prefab_tags = known_prefabs(game_root, scene)
    templates = known_templates(game_root)
    cameras = known_camera_cases(module)

    # ── 物品 ──
    if cast:
        for n in (cast.get("Npcs") or []):
            nid = n.get("Id")
            a = n.get("AttrItemId")
            if a and a not in items:
                problems.append("ref: %s.AttrItemId '%s' is NOT a registered item" % (nid, a))
            for r in (n.get("RewardItems") or []):
                if r not in items:
                    problems.append("ref: %s.RewardItems '%s' is NOT a registered item" % (nid, r))
        for k, alts in (cast.get("ItemAliases") or {}).items():
            if k not in items:
                problems.append("ref: ItemAliases key '%s' is NOT a registered item" % k)
            for alt in (alts or []):
                if alt not in items:
                    problems.append("ref: ItemAliases['%s'] -> '%s' is NOT a registered item" % (k, alt))

        # ── 角色模板 ──
        for n in (cast.get("Npcs") or []):
            nid = n.get("Id")
            for t in [n.get("TemplateId")] + list(n.get("TemplateCandidates") or []):
                if t and t not in templates:
                    problems.append("ref: %s template '%s' is NOT a known NPCCharacter" % (nid, t))

        # ── 动作名 ──
        for s2 in (cast.get("StreetLife") or []):
            for a in (s2.get("Actions") or []):
                if a not in acts:
                    problems.append("ref: StreetLife[%s] action '%s' is NOT in action_sets" % (s2.get("NpcId"), a))
        for e in (cast.get("Events") or []):
            for field in ("ActionA", "ActionB"):
                for a in (e.get(field) or []):
                    if a not in acts:
                        problems.append("ref: event %s %s '%s' is NOT in action_sets" % (e.get("Index"), field, a))

    # ── 台本里的 prefab / 动作 / 机位 / 锚点标签 ──
    if stage:
        for i, b in enumerate(stage.get("Beats") or []):
            pf = b.get("Prefab")
            if pf and pf not in prefabs:
                problems.append("ref: stage[%d] prefab '%s' is NOT a known prefab" % (i, pf))
            if b.get("Cmd") == "actor_action":
                a = b.get("Action")
                if a and a not in acts:
                    problems.append("ref: stage[%d] action '%s' is NOT in action_sets" % (i, a))
            if b.get("Cmd") == "camera_cut":
                c = b.get("Case")
                if c and c not in cameras:
                    problems.append("ref: stage[%d] camera case '%s' is NOT in Camera.csv" % (i, c))
        for name, taglist in (stage.get("Anchors") or {}).items():
            for t in (taglist or []):
                if t not in prefab_tags:
                    problems.append("ref: anchor '%s' tag '%s' does NOT exist on any prefab" % (name, t))

    # ── 物品 XML 里的 name="{=KEY}" 必须两层本地化都有 ──
    item_dir0 = os.path.join(module, "ModuleData", "items")
    if os.path.isdir(item_dir0):
        for f in glob.glob(os.path.join(item_dir0, "*.xml")):
            text = io.open(f, encoding="utf-8-sig", errors="ignore").read()
            for key in re.findall(r'name="\{=(LWN_[A-Za-z0-9_]+)\}', text):
                if key not in _CACHE.get(("en", module), _load_lang_cached(module, False)):
                    problems.append("ref: %s uses localization key '%s' missing in EN"
                                    % (os.path.basename(f), key))
                if key not in _CACHE.get(("cn", module), _load_lang_cached(module, True)):
                    problems.append("ref: %s uses localization key '%s' missing in CN"
                                    % (os.path.basename(f), key))

    # ── 物品 XML 有没有真的注册进 SubModule ──
    sub_path = os.path.join(module, "SubModule.xml")
    if os.path.exists(sub_path):
        sub = io.open(sub_path, encoding="utf-8-sig", errors="ignore").read()
        registered = set(re.findall(r'<XmlName[^>]*\bpath="([^"]+)"', sub))
        item_dir = os.path.join(module, "ModuleData", "items")
        if os.path.isdir(item_dir):
            for f in glob.glob(os.path.join(item_dir, "*.xml")):
                rel = "items/" + os.path.basename(f)[:-4]
                if rel in UNREGISTERED_BY_DESIGN:
                    continue
                if rel not in registered:
                    problems.append("ref: %s exists but is NOT registered in SubModule.xml <Xmls> "
                                    "(= the whole file is ignored by the engine)" % rel)



# ══════════════════════════════════════════════════════════════════
# 台词书写格式（2026-11-13 用户裁定）
#
#   · 每句台词 `（说话人，听话人）：文本`；玩家的话也算台词。
#   · 3D 游戏不是 AVG：演不出来的动作不写，心理描写不写。
#   · 只有「结果播报」允许 `（旁白，玩家）：…`。
#
# 只检查**对话树真正会显示的那些键**（dialogue JSON 的 NpcKey），
# 按钮/目标/错误提示这类 UI 串不在此列。
# ══════════════════════════════════════════════════════════════════

SPOKEN_CN = re.compile(r'^（[^，（）A-Za-z]+，[^，（）A-Za-z]+）：\S')
SPOKEN_EN = re.compile(r'^\([^,()]+, [^,()]+\): \S')
LEAKED_ID_CN = re.compile(r'\b(debtor|thug|blocker|pawnbroker|widow|outlaw|rival|Player|Narration|Narrator)\b')


def check_speech_format(module, problems, en, cn):
    files = sorted(glob.glob(os.path.join(module, "ModuleData", "DesignData", "Dialogues", "story_probe_*.json")))
    keys = set()
    pkeys = set()
    for path in files:
        try:
            data = json.load(io.open(path, encoding="utf-8"))
        except Exception:
            continue
        for node in data.get("Nodes", []):
            if node.get("NpcKey"):
                keys.add(node["NpcKey"])
            for tr in node.get("Transitions", []):
                if tr.get("PlayerKey"):
                    pkeys.add(tr["PlayerKey"])

    bad = 0
    for key in sorted(keys):
        for label, text, pattern in (("CN", cn.get(key), SPOKEN_CN), ("EN", en.get(key), SPOKEN_EN)):
            if not text:
                continue
            for line in text.split("&#10;"):
                line = line.strip()
                if not line:
                    continue
                if not pattern.match(line):
                    problems.append("speech[%s]: %s not in '(speaker, listener): text' form -> %s"
                                    % (label, key, line[:48]))
                    bad += 1
                    break
                if label == "CN" and LEAKED_ID_CN.search(line):
                    problems.append("speech[CN]: %s 含未本地化英文代号 -> %s" % (key, line[:48]))
                    bad += 1
                    break
    for pk in sorted(pkeys):
        ctext = cn.get(pk, "")
        if ctext and LEAKED_ID_CN.search(ctext):
            problems.append("option[CN]: %s 含未本地化英文代号 -> %s" % (pk, ctext[:48]))
            bad += 1
    return bad

# ══════════════════════════════════════════════════════════════════
# 台词硬规则（2026-11-13 用户裁定，见 .claude/skills/story-dialogue-writing）
#   ① 一个节点 = NPC 一次开口，**最多 2 行**
#   ② **禁止假选项**（"……（听他说下去）" / "继续" / "嗯" 这类不给玩家任何选择的填充）
# ══════════════════════════════════════════════════════════════════

TODO_LONG_NODES = []
FAKE_OPTION = re.compile(r'^[.…·。\s（(]*(听他说下去|继续说|继续|嗯|哦|然后呢)[.…·。\s）)]*$')


def check_dialogue_rules(module, problems, en, cn):
    files = sorted(glob.glob(os.path.join(module, "ModuleData", "DesignData", "Dialogues", "story_probe_*.json")))
    bad_lines = 0
    bad_opts = 0
    for path in files:
        name = os.path.basename(path)
        try:
            data = json.load(io.open(path, encoding="utf-8"))
        except Exception:
            continue
        for node in data.get("Nodes", []):
            key = node.get("NpcKey") or ""
            text = cn.get(key, "")
            lines = [l for l in text.replace("&#10;", chr(10)).split(chr(10)) if l.strip()]
            # 只数「无条件、非离开」的选项 —— 挂了 Requires 的是分批放出来的，不占一屏
            n_opts = len([t for t in (node.get("Transitions") or [])
                          if t.get("PlayerKey") and not t.get("Requires")
                          and t.get("PlayerKey") != "LWN_story_opt_leave"])
            if n_opts > 4:
                problems.append("dialogue[%s:%s]: 一屏 %d 个选项（上限 4，多的用显示条件分批）"
                                % (name, node.get("Id"), n_opts))
            if len(lines) > 2:
                TODO_LONG_NODES.append("%s:%s=%d行" % (name, node.get("Id"), len(lines)))
                problems.append("dialogue[%s:%s]: 节点台词 %d 行（上限 2 行，见规则一）"
                                % (name, node.get("Id"), len(lines)))
                bad_lines += 1
            for tr in node.get("Transitions", []):
                pk = tr.get("PlayerKey") or ""
                label = cn.get(pk, "")
                label = re.sub(r"^\[[^\]]*\]\s*", "", label).strip()

                # 🔴 选项不能指向"菜单节点"（出度≥5 或 *_hub）—— 指向菜单 = 对方没回应你，只会说"还有事？"
                nxt = tr.get("NextNodeOnSuccess")
                if nxt:
                    tgt = next((x for x in data.get("Nodes", []) if x.get("Id") == nxt), None)
                    # 只有"问句"弹回菜单才算错；「别的先不问了」这类本来就是收尾
                    is_menu = tgt is not None and (len(tgt.get("Transitions") or []) >= 5 or str(nxt).endswith("_hub"))
                    if is_menu and ("？" in label or "?" in label):
                        problems.append("dialogue[%s:%s]: 问句选项 '%s' 指向菜单节点 '%s'（对方不会回应你）"
                                        % (name, node.get("Id"), label[:20], nxt))
                        bad_opts += 1
                    if label and FAKE_OPTION.match(label):
                        problems.append("dialogue[%s:%s]: 假选项 '%s'（玩家没有任何选择）" % (name, node.get("Id"), label))
                        bad_opts += 1
    return bad_lines, bad_opts

def selftest():
    """负面测试：造一个"对话引用了不存在的 key"的临时模块，必须报错。"""
    import shutil
    import tempfile
    tmp = tempfile.mkdtemp(prefix="lwn_story_probe_")
    try:
        os.makedirs(os.path.join(tmp, "ModuleData", "DesignData", "Dialogues"))
        os.makedirs(os.path.join(tmp, "ModuleData", "Languages", "CNs"))
        with io.open(os.path.join(tmp, "ModuleData", "DesignData", "Dialogues", "story_probe_x.json"), "w", encoding="utf-8") as f:
            json.dump({"Npc": "x", "EntryOption": "LWN_story_x_entry", "EntryNode": "x_intro",
                       "Nodes": [{"Id": "x_intro", "NpcKey": "LWN_story_x_intro",
                                  "Transitions": [{"PlayerKey": "LWN_story_x_missing", "NextNodeOnSuccess": "x_nowhere"}]}]}, f)
        for name in ("std_LivingWorldNpcs_strings.xml", os.path.join("CNs", "std_LivingWorldNpcs_strings.xml")):
            with io.open(os.path.join(tmp, "ModuleData", "Languages", name), "w", encoding="utf-8") as f:
                f.write('<base><strings><string id="LWN_story_x_entry" text="e" />'
                        '<string id="LWN_story_x_intro" text="i" /></strings></base>')
        os.makedirs(os.path.join(tmp, "ExampleModVS", "ExampleMod", "ExampleMod"))
        problems = []
        keys, _ = check_json(tmp, problems)
        en = load_lang(os.path.join(tmp, "ModuleData", "Languages", "std_LivingWorldNpcs_strings.xml"))
        missing = [k for k in keys if k not in en]
        if not missing:
            print("SELFTEST FAILED: a missing key was not detected")
            return 1
        if not problems:
            print("SELFTEST FAILED: no structural problems detected")
            return 1
        print("SELFTEST OK: detected missing key(s) " + ", ".join(sorted(missing)[:3]))
        src = os.path.join(DEFAULT_MODULE, "ExampleModVS", "ExampleMod", "ExampleMod")
        negative_cases = [
            ("Combat/AttackTriggerMissionLogic.cs", "blow.InflictedDamage > 0", "false", "actual opponent damaging hits"),
            ("Interaction/StoryProbe/StoryProbeBehavior.cs", 'GetLocationWithId("prison")', 'GetLocationWithId("center")', "real prison transition"),
            ("Combat/AttackTriggerMissionLogic.cs", "&& affectedAgent.Health < 1f)", "&& true)", "real lethal health"),
            ("Interaction/StoryProbe/StoryProbeMissionBehavior.cs", "RestoreFirstGuardEnhancement();", "/* restore removed */", "restored before second entry"),
            ("Interaction/StoryProbe/StoryProbeMissionBehavior.cs", '"event_story_intercept"', '"old_direct_talk"', "guard must question"),
            ("AI/AgentBrain.cs", '_performance?.Finish("queue-cleared")', '/* queued lease cancellation removed */', "cancel queued/deleted ownership"),
            ("AI/AgentAIController.cs", "IsInteractionDisabled() && !cleanup", "IsInteractionDisabled()", "cleanup events must survive"),
            ("Interaction/StoryProbe/StoryProbeMissionBehavior.cs", "_climbSucceeded = roll.Success;", "_climbSucceeded = true;", "latched success before landing"),
            ("Interaction/StoryProbe/StoryProbeBehavior.cs", "if (firstRequest) StoryProbeState.InvalidateRuntime();", "/* invalidation removed */", "cancel must invalidate delayed receipts"),
            ("Interaction/StoryProbe/StoryProbeMissionBehavior.cs", "StoryProbeState.IsRuntimeCurrent(token)", "true", "cancel must invalidate delayed receipts"),
            ("Interaction/StoryProbe/StoryProbeBehavior.cs", "d.Suspended = true;", "/* pause removed */", "pause retains progress"),
            ("Interaction/StoryProbe/StoryProbeMissionBehavior.cs", "!PrologueQuestLauncher.CancelAll()", "false", "completion must retire outstanding chapter quests"),
            ("Interaction/StoryProbe/StoryProbeMissionBehavior.cs", "player.SpawnEquipment[slot].Item == expected", "true", "actual wearing and shared sight"),
            ("Interaction/StoryProbe/StoryProbeMissionBehavior.cs", "_crossedGate", "_fakeCrossing", "real bounded outside-to-inside crossing"),
            ("Interaction/StoryProbe/StoryStageRunner.cs", "_props.ContainsKey(b.Id)", "false", "deduplicate replayed props"),
        ]
        for relative, original, replacement, expected in negative_cases:
            with io.open(os.path.join(src, *relative.split("/")), encoding="utf-8") as stream:
                current = stream.read()
            if original not in current:
                print("SELFTEST FAILED: source mutation anchor missing: " + original)
                return 1
            failures = []
            check_flow_contract(DEFAULT_MODULE, failures, {relative: current.replace(original, replacement)})
            if not any(expected in failure for failure in failures):
                print("SELFTEST FAILED: flow regression not detected: " + expected)
                return 1
        print("SELFTEST OK: caught fake-hit and fake-prison source regressions")
        return 0
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def check_flow_contract(module, problems, overrides=None):
    """源码接线防退回旧计时倒地/地图换装；不代替 C# 编译或实机。"""
    src = os.path.join(module, "ExampleModVS", "ExampleMod", "ExampleMod")
    def source(relative):
        if overrides is not None and relative in overrides:
            return overrides[relative]
        path = os.path.join(src, *relative.split("/"))
        with io.open(path, encoding="utf-8") as stream:
            return stream.read()
    def member(text, name):
        # The name must precede the first parameter list, not occur in a forwarding body's call.
        match = re.search(r"(?m)^        (?:public|private|protected|internal) [^\n;={}()]*\b" + re.escape(name) + r"\s*\(", text)
        if not match:
            return ""
        tail = text[match.end():]
        end = re.search(r"(?m)^        (?:public|private|protected|internal) ", tail)
        return text[match.start():match.end() + (end.start() if end else len(tail))]
    host = source("Interaction/StoryProbe/StoryProbeMissionBehavior.cs")
    campaign = source("Interaction/StoryProbe/StoryProbeBehavior.cs")
    combat = source("Combat/AttackTriggerMissionLogic.cs")
    helper = source("Core/AgentControlHelper.cs")
    app = source("Core/MySubModule.cs")
    brain = source("AI/AgentBrain.cs")
    atomic = source("AI/Actions/AtomicAction.cs")
    controller = source("AI/AgentAIController.cs")
    stage = source("Interaction/StoryProbe/StoryStageRunner.cs")
    street = source("Interaction/StoryProbe/StreetLifeDirector.cs")
    dialogue = source("Interaction/StoryProbe/StoryProbeDialogue.cs")
    state = source("Interaction/StoryProbe/StoryProbeState.cs")
    defs = source("Interaction/StoryProbe/StoryProbeDefs.cs")
    request = member(host, "PlayForcedDefeat")
    hit = re.sub(r"\s+", "", member(combat, "OnAgentHit"))
    checks = [
        (bool(request) and "Schedule(" not in request and "SetPose(" not in request,
         "hard entry must only request combat, not schedule defeat"),
        ('"duel"' in member(host, "StartFirstGuardFight") and "OnFirstDuelResolved" in member(host, "StartFirstGuardFight"),
         "first guard must use shared duel and outcome callback"),
        ("attackerAgent==opponent" in hit and "blow.InflictedDamage>0" in hit
         and "&&affectedAgent.Health<1f" in hit and "_duelDamageHits" not in combat,
         "tutorial defeat requires actual opponent damaging hits reaching real lethal health"),
        ("PlayForcedDefeat(" not in member(host, "TickFirstAttempt")
         and '"event_story_intercept"' in member(host, "TickFirstAttempt")
          and 'StartStoryConversation(ready, null, "first_meet_auto")' in member(host, "TickFirstAttempt")
          and "EnqueueAction(" not in host
         and "_firstGuardQuestioned" in request,
         "guard must question before explicit hard-entry choice, never fight merely for crossing"),
        ("BoostFirstGuardSkills" in member(host, "SetupFirstAttempt")
         and member(host, "SetupFirstAttempt").find("BoostFirstGuardSkills") < member(host, "SetupFirstAttempt").find("SpawnNpc(")
         and "CancelRuntime" in member(host, "OnEndMissionInternal")
          and "RestoreFirstGuardEnhancement" in member(host, "CancelRuntime")
         and "RestoreFirstGuardSkills" in member(host, "SpawnNpc"),
         "first guard strength must be real and restored before second entry"),
        ("ApplyFirstArrestBounty" not in member(host, "SetupFirstAttempt")
         and "ApplyFirstArrestBounty" in member(host, "OnFirstDuelResolved"),
         "entry must not manufacture crime and bypass guard questioning"),
        ('GetAllUsablePointsWithTag("sp_prisoner")' in member(host, "TickJailScene")
         and "AgentControlHelper.PlaceAtStandingPoint(" in member(host, "TickJailScene"),
         "jail must place player at native prisoner standing point"),
        ("point.GameEntity.GetGlobalFrame()" in member(helper, "PlaceAtStandingPoint"),
         "jail placement must use point frame, not current player corridor frame"),
        ("TickPendingTransition(dt)" in app and 'GetLocationWithId("prison")' in campaign,
         "real prison transition must tick outside paused Campaign.Tick"),
        ("Mission.Current != null && StoryProbeState.EnterViaOption" not in campaign,
         "mission opening receipt must not depend on consumed entry flag"),
        ("d.Stripped && (d.ConfiscationComplete || d.EscrowVersion == 0)" in member(host, "ConfiscatePlayerBelongings")
          and "pending.Contains(i)" in member(host, "ConfiscateEquipment"),
         "retries and disguise must not confiscate belongings again"),
        ("ReplaceEquippedItem" in member(host, "StripPlayer") and "SavedBattleModifiers" in host,
         "disguise and escrow must preserve replaced equipment and saved modifiers"),
        ("HasDuelPair" in member(host, "TickFirstAttempt") and "IsRoped" in member(host, "StartFirstGuardFight"),
         "rejected combat must allow retry without timer defeat"),
        ('"event_story_intercept"' in member(brain, "ReceiveEvent") and "ConfrontingBrain" in member(brain, "StartStoryIntercept")
         and "FollowAgentAction" in member(brain, "StartStoryIntercept"),
         "story interrogation must reuse Brain follow and conversation ownership"),
        ("public sealed class PerformanceAction : IAtomicAction" in atomic
         and "Started && !Paused && !_ended" in atomic and "_brain.OwnsPerformance(this)" in atomic
         and '_performance?.Finish("queue-cleared")' in member(brain, "ClearAllActions")
         and "InvalidateManaged" in member(brain, "OnOwnerDeleted"),
         "performance must confirm start and cancel queued/deleted ownership"),
        ("PerformanceAction.TryAcquire" in stage and "CanControl" in member(stage, "Tick")
         and "EnsureStageActor" in stage and "SpawnOrPlaceActor" not in stage
         and "_props.ContainsKey(b.Id)" in stage and "AgentBrain.IsHeldByStatus(player)" in stage
         and "DisposeManaged" in member(host, "OnRemoveBehavior"),
         "stage must acquire actors before body writes and deduplicate replayed props"),
        ("PerformanceAction.TryAcquire" in street and "CanControl" in street and "lease.Pause(true)" in street
         and "StopAll" in member(host, "CancelRuntime"),
         "street performance must release paired actors and pause for conversations"),
        ('"event_performance_end"' in member(controller, "SendEventToAgent")
         and "IsInteractionDisabled() && !cleanup" in member(controller, "SendEventToAgent"),
         "cleanup events must survive interaction mode switches"),
        ("Layouts.TryGetValue" in member(host, "ResolveAnchors") and "no guessed success" in member(host, "ResolveAnchors")
         and "CrossingMaxStepMeters" in member(host, "TrackGateCrossing")
         and "_crossedGate" in member(host, "TickSecondPhase"),
         "gate passage requires verified geometry and real bounded outside-to-inside crossing"),
        ("player.SpawnEquipment[slot].Item == expected" in member(host, "IsWearingStoryDisguise")
         and "IsWearingStoryDisguise(true)" in dialogue and "CanAgentSeeTarget" in member(host, "IsBlockerWatching"),
         "recognition requires actual wearing and shared sight"),
        ("_crossingDoor" in member(host, "CheckHandsOnGate")
         and "GrappleLogic.GetSession(player)?.IsPullingSelf" in member(host, "EnforceGateBarrier")
         and "_climbSucceeded = roll.Success" in member(host, "EnforceGateBarrier")
         and "_climbSucceeded && !_climbSeen && _climbCrossed" in member(host, "EnforceGateBarrier")
         and "IsAtGateGround" in member(host, "EnforceGateBarrier")
         and "V.GroundHeight" in member(host, "IsAtGateGround")
         and "LandingVerticalSpeedMeters" in member(host, "IsAtGateGround")
         and "LandingStableSec" in member(host, "IsAtGateGround"),
         "sneak needs actual crossing; climb needs actual grapple and latched success before landing"),
        ('"duel"' in member(host, "PumpStoryFights") and "OnStoryDuelResolved" in member(host, "PumpStoryFights")
         and "RequestStoryFight(npcId)" in member(dialogue, "StartFight")
         and "CombatManager.StartFight" not in member(dialogue, "StartFight")
         and "loser != target" in member(host, "OnStoryDuelResolved"),
         "all story duels must use shared outcome receipts instead of polling healed health"),
        ("public static bool RestorePlayerBelongings" in host
         and "PendingBattleRestoreSlots" in member(host, "RestorePlayerBelongings")
         and "owed - Math.Max(0, hero.Gold - before)" in member(host, "RestorePlayerBelongings")
         and member(host, "CompletePrologue").find("RestorePlayerBelongings()") < member(host, "CompletePrologue").find("StoryProbePhase.Done"),
         "escrow must discharge remaining debts before completion, not fabricate full restoration"),
        ("InvalidateRuntime" in member(campaign, "CancelPrologue")
         and "CancellationPending" in member(campaign, "CancelPrologue")
         and '"event_story_intercept_cancel"' in member(host, "CancelRuntime")
         and '"event_stop_combat", ownedReceipt' in member(host, "CancelRuntime")
         and "HasDuelReceipt" in member(brain, "ReceiveEvent")
         and "NotifyQueuedDuelCancelled" in member(brain, "ClearAllActions")
         and "IsRuntimeCurrent" in member(host, "Schedule"),
         "cancel must invalidate delayed receipts and stop active story actions without deleting debt"),
        ("d.Suspended = true" in member(campaign, "CancelPrologue")
         and "d.CancellationRestart |= restart" in member(campaign, "CancelPrologue")
         and "PrologueQuestLauncher.CancelAll()" in member(campaign, "CancelPrologue")
         and "!d.Suspended && StoryProbeState.Phase == StoryProbePhase.Jail" in member(campaign, "TickPendingTransition"),
         "pause retains progress; restart settles native quests before resetting; suspended jail stays idle"),
        ("!PrologueQuestLauncher.CancelAll()" in member(host, "CompletePrologue")
         and member(host, "CompletePrologue").find("!PrologueQuestLauncher.CancelAll()")
             < member(host, "CompletePrologue").find("Phase = StoryProbePhase.Done")
         and 'CancelRuntime("prologue completed")' in member(host, "CompletePrologue"),
         "completion must retire outstanding chapter quests before committing Done and stopping runtime"),
        ("TryPayMoney" in dialogue and "HasRewardReceipt" in dialogue and "RecordRewardReceipt" in dialogue
         and "DebtCollected" in dialogue and "DebtRepaid" in dialogue
         and "FailStoryActionConversation" in member(dialogue, "HandleAction")
         and "IsQuestTurnedIn(currentNpc)" in member(dialogue, "Dispatch")
         and "SingleRollResolver.Roll(chance, r.Roll)" in defs,
         "payments and rewards require real receipts; reported dice must use shared resolver"),
    ]
    for ok, message in checks:
        if not ok:
            problems.append("flow contract: " + message)
    import math
    intro = (load_data_json(os.path.join(module, "ModuleData", "DesignData", "StoryProbeTuning.json")) or {}).get("Intro") or {}
    for key in ("FirstGuardMinCombatSkill", "FirstGuardSkillAdvantage"):
        value = intro.get(key)
        if type(value) is not int or value < 0:
            problems.append("flow tuning: " + key + " must be a nonnegative integer")
    value = intro.get("FirstGuardHealthMultiplier")
    if type(value) not in (int, float) or not math.isfinite(value) or value < 1:
        problems.append("flow tuning: FirstGuardHealthMultiplier must be finite and >= 1")


# ═══════════════════════════════════════════════════════════════════════════
# P0 契约：story_stage/2 夹具校验（2026-11-15）
#
# 🔴 本段与 C# 的 StoryLayoutParser（Interaction/StoryProbe/StoryLayoutDefs.cs）
#    是**同一套规则的两份实现**：正样例两边都必须 PASS，每个负样例两边都必须 FAIL
#    且命中同一个错误码。两边的公共口径就是 Debug/PlanExamples/ 那一组夹具。
# ═══════════════════════════════════════════════════════════════════════════

STAGE_V2_SCHEMA = "story_stage/2"
STAGE_V2_ROOT = ("Schema", "ContentVersion", "Scene", "WallLevel", "Layouts", "Stages", "Behaviors", "Flows", "Beats")
STAGE_V2_TERMINAL = ("release", "retry", "reselect", "end")

# name -> (params, required_params, actor_role, wait_kinds, implemented)
ATOM_SPECS = {
    "player_freeze": ((), (), "none", (), True),
    "player_release": ((), (), "none", (), True),
    "camera_cut": (("Case",), ("Case",), "none", (), True),
    "camera_return": ((), (), "none", (), True),
    "prop_spawn": (("Id", "Prefab"), ("Id",), "none", (), True),
    "actor_place": (("Post", "WorkPoint", "Anchor", "Fwd", "Right", "Up", "Facing"), (), "required", (), True),
    "actor_action": (("Action", "Async"), ("Action",), "required", (), True),
    "actor_say": (("TextKey",), ("TextKey",), "required", (), True),
    "wait": (("Seconds",), ("Seconds",), "none", (), False),
    "actor_move": (("To", "StopMeters"), ("To",), "required", ("arrival",), False),
    "dialogue_open": (("Dialogue",), ("Dialogue",), "required", ("dialogue_open",), True),
    "dialogue_wait": ((), (), "required", ("dialogue_end",), True),
}

# 原子参数类型与 C# StoryAtomSpec 对应；字符串是默认类型。
ATOM_NUMBER_PARAMS = {
    "actor_place": ("Fwd", "Right", "Up", "Facing"),
    "actor_move": ("StopMeters",),
    "wait": ("Seconds",),
}
ATOM_BOOLEAN_PARAMS = {"actor_action": ("Async",)}


def stage_json_pairs(pairs):
    data = {}
    for key, value in pairs:
        if key in data:
            raise ValueError("duplicate JSON property: " + key)
        data[key] = value
    return data

BEAT_KEYS = {
    "player_freeze": ("At", "Cmd", "Note"),
    "player_release": ("At", "Cmd", "Note"),
    "camera_return": ("At", "Cmd", "Note"),
    "camera_cut": ("At", "Cmd", "Case", "Anchor", "Fwd", "Right", "Up", "Note"),
    "prop_spawn": ("At", "Cmd", "Id", "Prefab", "Anchor", "Fwd", "Right", "Up", "Yaw", "Scale", "Note"),
    "actor_place": ("At", "Cmd", "Actor", "Post", "WorkPoint", "Anchor", "Fwd", "Right", "Up", "Facing", "Note"),
    "actor_action": ("At", "Cmd", "Actor", "Action", "Async", "Note"),
    "actor_say": ("At", "Cmd", "Actor", "TextKey", "Note"),
    "wait": ("At", "Cmd", "Seconds", "Note"),
}


def stage_v2_issues(data, cast_ids, formal=False, source="<memory>"):
    """校验一份 story_stage/2。返回 [{path, code, message}]；空列表 = 通过。"""
    issues = []

    def bad(path, code, message):
        issues.append({"path": path, "code": code, "message": message})

    def keys(obj, path, allowed, allow_comment=False):
        for k in obj:
            if allow_comment and k.startswith("_"):
                continue
            if k not in allowed:
                bad(path + "." + k, "unknown_key", "未知字段 '%s'（允许：%s）" % (k, ", ".join(allowed)))

    def num(parent, name, path, default, low, high, required=False):
        if name not in parent or parent[name] is None:
            if required:
                bad(path + "." + name, "missing_field", "缺少必需数字 '%s'" % name)
            return default
        v = parent[name]
        if type(v) not in (int, float) or isinstance(v, bool):
            bad(path + "." + name, "wrong_type", "'%s' 必须是数字" % name)
            return default
        if not math.isfinite(v):
            bad(path + "." + name, "bad_value", "'%s' 不是有限数" % name)
            return default
        if v < low or v > high:
            bad(path + "." + name, "bad_range", "'%s' 超出范围 [%s,%s]：实际 %s" % (name, low, high, v))
        return v

    def integer(parent, name, path, default, low, high):
        v = parent.get(name, default)
        if type(v) is not int or isinstance(v, bool):
            if name in parent:
                bad(path + "." + name, "wrong_type", "'%s' 必须是整数" % name)
            return default
        if v < low or v > high:
            bad(path + "." + name, "bad_range", "'%s' 超出范围 [%s,%s]：实际 %s" % (name, low, high, v))
        return v

    def text(parent, name, path, required=True):
        if name not in parent or parent[name] is None:
            if required:
                bad(path + "." + name, "missing_field", "缺少必需字符串 '%s'" % name)
            return ""
        v = parent[name]
        if not isinstance(v, str):
            bad(path + "." + name, "wrong_type", "'%s' 必须是字符串" % name)
            return ""
        if required and not v:
            bad(path + "." + name, "missing_field", "'%s' 不能为空" % name)
        return v

    def boolean(parent, name, path, default):
        if name not in parent or parent[name] is None:
            return default
        if not isinstance(parent[name], bool):
            bad(path + "." + name, "wrong_type", "'%s' 必须是布尔值" % name)
            return default
        return parent[name]

    def str_list(parent, name, path, required):
        if name not in parent or parent[name] is None:
            if required:
                bad(path + "." + name, "missing_field", "缺少必需数组 '%s'" % name)
            return []
        v = parent[name]
        if not isinstance(v, list):
            bad(path + "." + name, "wrong_type", "'%s' 必须是数组" % name)
            return []
        out = []
        for i, item in enumerate(v):
            if not isinstance(item, str):
                bad("%s.%s[%d]" % (path, name, i), "wrong_type", "元素必须是字符串")
                continue
            out.append(item)
        if required and not out:
            bad(path + "." + name, "empty_collection", "'%s' 不能为空" % name)
        return out

    def float_list(parent, name, path, length, required):
        if name not in parent or parent[name] is None:
            if required:
                bad(path + "." + name, "missing_field", "缺少必需数组 '%s'" % name)
            return []
        v = parent[name]
        if not isinstance(v, list):
            bad(path + "." + name, "wrong_type", "'%s' 必须是数组" % name)
            return []
        if len(v) != length:
            bad(path + "." + name, "bad_length", "'%s' 必须是 %d 个数，实际 %d" % (name, length, len(v)))
            return []
        for i, item in enumerate(v):
            if type(item) not in (int, float) or isinstance(item, bool):
                bad("%s.%s[%d]" % (path, name, i), "wrong_type", "必须是数字")
                return []
        return [float(x) for x in v]

    def point(parent, name, path, anchors, required):
        if name is not None:
            if not isinstance(parent.get(name), dict):
                if required:
                    bad(path + "." + name, "missing_field", "缺少必需对象 '%s'" % name)
                return {"Anchor": "", "Fwd": 0.0, "Right": 0.0, "Up": 0.0}
            obj = parent[name]
            path = path + "." + name
            keys(obj, path, ("Anchor", "Fwd", "Right", "Up"))
        else:
            obj = parent
        p = {
            "Anchor": text(obj, "Anchor", path, required),
            "Fwd": num(obj, "Fwd", path, 0.0, -1000.0, 1000.0),
            "Right": num(obj, "Right", path, 0.0, -1000.0, 1000.0),
            "Up": num(obj, "Up", path, 0.0, -1000.0, 1000.0),
        }
        if p["Anchor"] and anchors is not None and p["Anchor"] not in anchors:
            bad(path + ".Anchor", "unknown_anchor", "锚点 '%s' 不在同一布局里" % p["Anchor"])
        return p

    if not isinstance(data, dict):
        bad(source, "wrong_type", "根必须是对象")
        return issues

    keys(data, "$", STAGE_V2_ROOT, allow_comment=True)
    if data.get("Schema") != STAGE_V2_SCHEMA:
        bad("$.Schema", "schema_mismatch",
            "结构版本必须是 '%s'，实际 '%s'" % (STAGE_V2_SCHEMA, data.get("Schema")))
    integer(data, "ContentVersion", "$", 0, 1, 2 ** 31 - 1)
    if "ContentVersion" not in data:
        bad("$.ContentVersion", "missing_field", "缺少必需整数 'ContentVersion'")
    text(data, "Scene", "$")
    text(data, "WallLevel", "$")

    layouts = data.get("Layouts")
    if not isinstance(layouts, dict) or not layouts:
        bad("$.Layouts", "empty_collection", "至少要有一份布局")
        layouts = {}

    resolved = {}
    for lid, lo in layouts.items():
        path = "$.Layouts." + lid
        if not isinstance(lo, dict):
            bad(path, "wrong_type", "布局必须是对象")
            continue
        keys(lo, path, ("Version", "Anchors", "Gates", "Props", "WorkPoints", "Posts", "Standing"))
        integer(lo, "Version", path, 0, 1, 2 ** 31 - 1)

        anchors = lo.get("Anchors")
        if not isinstance(anchors, dict) or not anchors:
            bad(path + ".Anchors", "empty_collection", "至少要有一个锚点")
            anchors = {}
        for aid, ao in anchors.items():
            apath = path + ".Anchors." + aid
            if not isinstance(ao, dict):
                bad(apath, "wrong_type", "锚点必须是对象")
                continue
            keys(ao, apath, ("Source", "Value", "Pick", "Required"))
            source_kind = text(ao, "Source", apath) or "tag"
            if source_kind not in ("tag", "entity", "player"):
                bad(apath + ".Source", "bad_token", "Source 只能是 tag / entity / player，实际 '%s'" % source_kind)
            values = str_list(ao, "Value", apath, source_kind != "player")
            if source_kind == "player" and values:
                bad(apath + ".Value", "bad_field", "Source=player 时不允许写 Value")
            pick = text(ao, "Pick", apath, required=False) or "first"
            if pick not in ("first", "nearestToPlayer"):
                bad(apath + ".Pick", "bad_token", "Pick 只能是 first / nearestToPlayer，实际 '%s'" % pick)

        gates = lo.get("Gates") or {}
        if isinstance(gates, dict):
            for gid, go in gates.items():
                gpath = path + ".Gates." + gid
                if not isinstance(go, dict):
                    bad(gpath, "wrong_type", "门区必须是对象")
                    continue
                keys(go, gpath, ("Center", "Outside", "Inner", "HalfWidth", "InsideMeters", "OutsideMeters"))
                for pn in ("Center", "Outside", "Inner"):
                    point(go, pn, gpath, anchors, True)
                num(go, "HalfWidth", gpath, 3.0, 0.01, 1000.0, required=True)
                num(go, "InsideMeters", gpath, 8.0, 0.0, 10000.0, required=True)
                num(go, "OutsideMeters", gpath, 3.0, 0.0, 10000.0, required=True)

        props = {}
        for i, po in enumerate(lo.get("Props") or []):
            ppath = "%s.Props[%d]" % (path, i)
            if not isinstance(po, dict):
                bad(ppath, "wrong_type", "道具必须是对象")
                continue
            keys(po, ppath, ("Id", "Source", "Prefab", "EntityTag", "Anchor", "Fwd", "Right", "Up",
                             "Yaw", "Scale", "Support", "Footprint", "Required"))
            pid = text(po, "Id", ppath)
            if not pid:
                continue
            if pid in props:
                bad(ppath + ".Id", "duplicate_id", "道具 ID 重复 '%s'" % pid)
            src = text(po, "Source", ppath) or "prefab"
            place = {"Anchor": "", "Fwd": 0.0, "Right": 0.0, "Up": 0.0}
            yaw = 0.0
            if src == "prefab":
                text(po, "Prefab", ppath)
                place = point(po, None, ppath, anchors, True)
                yaw = num(po, "Yaw", ppath, 0.0, -3600.0, 3600.0)
                num(po, "Scale", ppath, 1.0, 0.01, 100.0)
                if "EntityTag" in po:
                    bad(ppath + ".EntityTag", "bad_field", "Source=prefab 时不允许写 EntityTag")
            elif src == "existing":
                str_list(po, "EntityTag", ppath, True)
                if any(k in po for k in ("Anchor", "Fwd", "Right", "Up", "Yaw", "Scale")):
                    bad(ppath, "bad_field", "Source=existing 时不允许写位置/朝向/缩放")
            else:
                bad(ppath + ".Source", "bad_token", "Source 只能是 prefab / existing，实际 '%s'" % src)
            support = text(po, "Support", ppath) or "ground"
            if support not in ("ground", "fixed-up"):
                bad(ppath + ".Support", "bad_token", "Support 只能是 ground / fixed-up，实际 '%s'" % support)
            foot = float_list(po, "Footprint", ppath, 2, True)
            if foot and (foot[0] <= 0 or foot[1] <= 0):
                bad(ppath + ".Footprint", "bad_range", "占地两个分量都必须 > 0")
            boolean(po, "Required", ppath, True)
            props[pid] = {"place": place, "yaw": yaw}

        works = set()
        for i, wo in enumerate(lo.get("WorkPoints") or []):
            wpath = "%s.WorkPoints[%d]" % (path, i)
            if not isinstance(wo, dict):
                bad(wpath, "wrong_type", "工作点必须是对象")
                continue
            keys(wo, wpath, ("Id", "Prop", "Side", "Offset", "Contact", "Yaw", "ArriveTolerance",
                             "MaxOccupants", "Action"))
            wid = text(wo, "Id", wpath)
            if not wid:
                continue
            if wid in works:
                bad(wpath + ".Id", "duplicate_id", "工作点 ID 重复 '%s'" % wid)
            works.add(wid)
            prop = text(wo, "Prop", wpath)
            if prop and prop not in props:
                bad(wpath + ".Prop", "unknown_prop", "工作点引用的道具 '%s' 不在同一布局里" % prop)
            side = text(wo, "Side", wpath) or "front"
            if side not in ("front", "back", "left", "right"):
                bad(wpath + ".Side", "bad_token", "Side 只能是 front / back / left / right，实际 '%s'" % side)
            num(wo, "Offset", wpath, 0.0, 0.0, 100.0, required=True)
            float_list(wo, "Contact", wpath, 3, True)
            num(wo, "Yaw", wpath, 0.0, -3600.0, 3600.0)
            num(wo, "ArriveTolerance", wpath, 0.6, 0.01, 20.0)
            integer(wo, "MaxOccupants", wpath, 1, 1, 8)
            text(wo, "Action", wpath)

        posts = set()
        for i, po in enumerate(lo.get("Posts") or []):
            ppath = "%s.Posts[%d]" % (path, i)
            if not isinstance(po, dict):
                bad(ppath, "wrong_type", "岗位必须是对象")
                continue
            keys(po, ppath, ("Id", "Gate", "Anchor", "Fwd", "Right", "Up", "Facing"))
            pid = text(po, "Id", ppath)
            if not pid:
                continue
            if pid in posts:
                bad(ppath + ".Id", "duplicate_id", "岗位 ID 重复 '%s'" % pid)
            posts.add(pid)
            gate = text(po, "Gate", ppath)
            if gate and gate not in (gates if isinstance(gates, dict) else {}):
                bad(ppath + ".Gate", "unknown_gate", "岗位引用的门区 '%s' 不在同一布局里" % gate)
            point(po, None, ppath, anchors, True)
            num(po, "Facing", ppath, -1.0, -3600.0, 3600.0)

        for i, so in enumerate(lo.get("Standing") or []):
            spath = "%s.Standing[%d]" % (path, i)
            if not isinstance(so, dict):
                bad(spath, "wrong_type", "站位必须是对象")
                continue
            keys(so, spath, ("Actor", "Post", "WorkPoint", "Anchor", "Fwd", "Right", "Up", "Facing"))
            text(so, "Actor", spath)
            post = text(so, "Post", spath, required=False)
            work = text(so, "WorkPoint", spath, required=False)
            sources = (1 if post else 0) + (1 if work else 0) + (1 if "Anchor" in so else 0)
            if sources != 1:
                bad(spath, "bad_field", "站位必须在 Post / WorkPoint / Anchor 里三选一（当前 %d 个）" % sources)
            if post and post not in posts:
                bad(spath + ".Post", "unknown_post", "站位引用的岗位 '%s' 不存在" % post)
            if work and work not in works:
                bad(spath + ".WorkPoint", "unknown_workpoint", "站位引用的工作点 '%s' 不存在" % work)
            if "Anchor" in so:
                point(so, None, spath, anchors, True)
            num(so, "Facing", spath, -1.0, -3600.0, 3600.0)

        resolved[lid] = {"props": props, "posts": posts, "work": works, "anchors": anchors,
                         "gates": gates if isinstance(gates, dict) else {}}

    stages = data.get("Stages")
    if not isinstance(stages, dict) or not stages:
        bad("$.Stages", "empty_collection", "至少要有一个阶段")
        stages = {}
    opening = []
    for sid, so in stages.items():
        spath = "$.Stages." + sid
        if not isinstance(so, dict):
            bad(spath, "wrong_type", "阶段必须是对象")
            continue
        keys(so, spath, ("Layout", "Actors", "Props", "OptionalActors", "OptionalProps", "Opening", "Prep"))
        layout_id = text(so, "Layout", spath)
        if layout_id and layout_id not in resolved:
            bad(spath + ".Layout", "unknown_layout", "阶段引用的布局 '%s' 不存在" % layout_id)
        actors = str_list(so, "Actors", spath, True)
        props_list = str_list(so, "Props", spath, False)
        optional_actors = str_list(so, "OptionalActors", spath, False)
        optional_props = str_list(so, "OptionalProps", spath, False)
        if boolean(so, "Opening", spath, False):
            opening.append(sid)
        prep = so.get("Prep")
        if isinstance(prep, dict):
            keys(prep, spath + ".Prep", ("SkipAllowed", "ActorReadyTimeoutSec"))
            boolean(prep, "SkipAllowed", spath + ".Prep", True)
            num(prep, "ActorReadyTimeoutSec", spath + ".Prep", 3.0, 0.1, 600.0)
        for label, values in (("演员", actors), ("道具", props_list)):
            seen = set()
            for item in values:
                if item in seen:
                    bad(spath, "duplicate_id", "%s '%s' 重复" % (label, item))
                seen.add(item)
        if cast_ids:
            for actor in actors + optional_actors:
                if actor and actor not in cast_ids:
                    bad(spath, "unknown_cast_actor", "演员 '%s' 不在选角表里" % actor)
        lay = resolved.get(layout_id)
        if lay:
            for prop in props_list + optional_props:
                if prop and prop not in lay["props"]:
                    bad(spath, "unknown_prop", "道具 '%s' 不在布局 '%s' 里" % (prop, layout_id))
    if len(opening) > 1:
        bad("$.Stages", "multiple_opening", "只能有一个阶段声明 Opening=true（当前 %d）" % len(opening))

    flows = data.get("Flows") or {}
    if isinstance(flows, dict):
        for fid, fo in flows.items():
            fpath = "$.Flows." + fid
            if not isinstance(fo, dict):
                bad(fpath, "wrong_type", "流程必须是对象")
                continue
            keys(fo, fpath, ("Roles", "Steps"))
            roles = fo.get("Roles")
            if roles is None:
                roles = {}
            if not isinstance(roles, dict):
                bad(fpath + ".Roles", "wrong_type", "角色绑定必须是对象")
                roles = {}
            for role in roles:
                binding = text(roles, role, fpath + ".Roles")
                if binding and binding not in ("candidate", "player") and binding not in cast_ids:
                    bad(fpath + ".Roles." + role, "unknown_cast_actor", "角色绑定必须是 candidate、player 或选角表 ID")

            def flow_actor(actor, path):
                if actor.startswith("$"):
                    if actor[1:] not in roles:
                        bad(path, "unknown_role", "未定义流程角色 '%s'" % actor)
                elif actor not in cast_ids:
                    bad(path, "unknown_cast_actor", "演员 '%s' 不在选角表" % actor)

            steps = fo.get("Steps")
            if not isinstance(steps, list) or not steps:
                bad(fpath + ".Steps", "empty_collection", "流程至少要有一个步骤")
                continue
            ids = []
            for i, st in enumerate(steps):
                step_path = "%s.Steps[%d]" % (fpath, i)
                if not isinstance(st, dict):
                    bad(step_path, "wrong_type", "步骤必须是对象")
                    continue
                keys(st, step_path, ("Id", "Atom", "Actor", "Args", "Wait",
                                     "OnFail", "OnTimeout", "OnReject", "OnCancel", "OnInterrupt"))
                text(st, "Id", step_path)
                atom = text(st, "Atom", step_path)
                actor = text(st, "Actor", step_path, required=False)
                if actor:
                    flow_actor(actor, step_path + ".Actor")
                spec = ATOM_SPECS.get(atom)
                if atom and spec is None:
                    bad(step_path + ".Atom", "unknown_atom", "未注册的原子 '%s'" % atom)
                elif spec is not None:
                    if spec[2] == "required" and not actor:
                        bad(step_path + ".Actor", "missing_field", "原子 '%s' 需要演员绑定" % atom)
                    if spec[2] != "required" and actor:
                        bad(step_path + ".Actor", "bad_field", "原子 '%s' 不接受演员绑定" % atom)
                    if not spec[4] and formal:
                        bad(step_path + ".Atom", "unimplemented_atom", "原子 '%s' 尚未实现，不能进正式数据" % atom)
                args = st.get("Args")
                if isinstance(args, dict):
                    if spec is not None:
                        for ak in args:
                            if ak not in spec[0]:
                                bad(step_path + ".Args." + ak, "unknown_param",
                                    "原子 '%s' 不接受参数 '%s'（允许：%s）" % (atom, ak, ", ".join(spec[0])))
                                continue
                            apath = step_path + ".Args"
                            if ak in ATOM_NUMBER_PARAMS.get(atom, ()):
                                v = num(args, ak, apath, 0.0, -100000.0, 100000.0)
                                if (ak == "Seconds" and v <= 0) or (ak == "StopMeters" and v < 0):
                                    bad(apath + "." + ak, "bad_range", "等待秒数须 >0；停止距离须 >=0")
                            elif ak in ATOM_BOOLEAN_PARAMS.get(atom, ()):
                                boolean(args, ak, apath, False)
                            else:
                                text(args, ak, apath)
                            if ak == "To" and isinstance(args[ak], str) and args[ak].startswith("$"):
                                flow_actor(args[ak], apath + "." + ak)
                            if args[ak] is None:
                                bad(apath + "." + ak, "wrong_type", "原子参数不能是 null")
                        for rp in spec[1]:
                            if rp not in args:
                                bad(step_path + ".Args", "missing_field", "缺少必需参数 '%s'" % rp)
                elif spec is not None and spec[1]:
                    bad(step_path + ".Args", "missing_field", "缺少 Args（必需参数：%s）" % ", ".join(spec[1]))
                if args is not None and not isinstance(args, dict):
                    bad(step_path + ".Args", "wrong_type", "Args 必须是对象")
                wait = st.get("Wait")
                if isinstance(wait, dict):
                    keys(wait, step_path + ".Wait", ("Kind", "TimeoutSec"))
                    kind = text(wait, "Kind", step_path + ".Wait")
                    timeout = num(wait, "TimeoutSec", step_path + ".Wait", 0.0, 0.0, 100000.0)
                    if spec is not None:
                        if not spec[3]:
                            bad(step_path + ".Wait", "bad_field", "原子 '%s' 不接受 Wait" % atom)
                        elif kind and kind not in spec[3]:
                            bad(step_path + ".Wait.Kind", "bad_token",
                                "原子 '%s' 只接受 wait.Kind=%s，实际 '%s'" % (atom, "/".join(spec[3]), kind))
                    if timeout <= 0:
                        bad(step_path + ".Wait.TimeoutSec", "wait_no_timeout", "等待必须写 TimeoutSec > 0")
                elif spec is not None and spec[3]:
                    bad(step_path + ".Wait", "missing_field",
                        "原子 '%s' 声明了等待语义，步骤必须写 Wait（含 TimeoutSec）" % atom)
                if wait is not None and not isinstance(wait, dict):
                    bad(step_path + ".Wait", "wrong_type", "Wait 必须是对象")
                if not text(st, "OnCancel", step_path, required=False):
                    bad(fpath, "missing_branch", "每个步骤都必须写 OnCancel（取消后去哪）")
                sid = st.get("Id")
                if isinstance(sid, str) and sid and sid in ids:
                    bad(step_path + ".Id", "duplicate_id", "流程步骤 ID 重复：'%s'" % sid)
                if sid in STAGE_V2_TERMINAL or sid == "next":
                    bad(step_path + ".Id", "bad_value", "步骤 ID 不能与分支保留词重名")
                ids.append(sid)
            for i, st in enumerate(steps):
                if not isinstance(st, dict):
                    continue
                for branch in ("OnFail", "OnTimeout", "OnReject", "OnCancel", "OnInterrupt"):
                    target = st.get(branch)
                    if not target:
                        continue
                    if target in STAGE_V2_TERMINAL or target == "next":
                        continue
                    if target not in ids:
                        bad("%s.Steps[%d].%s" % (fpath, i, branch), "unknown_branch",
                            "'%s' 既不是终态也不是本流程的步骤 ID" % target)
                    elif ids.index(target) <= i:
                        bad("%s.Steps[%d].%s" % (fpath, i, branch), "backward_branch",
                            "P0 只支持前向跳转，'%s' 不在当前步骤之后" % target)

    behaviors = data.get("Behaviors") or {}
    if isinstance(behaviors, dict):
        for bid, bo in behaviors.items():
            bpath = "$.Behaviors." + bid
            if not isinstance(bo, dict):
                bad(bpath, "wrong_type", "行为规则必须是对象")
                continue
            keys(bo, bpath, ("Kind", "Posts", "Stages", "Perception", "Trigger", "Cooldown", "Rank", "Flow", "MaxActive",
                             "Members", "Host", "Lines", "Speakers", "LineFallbacks", "ActionsA", "ActionsB", "JoinNode"))
            kind = text(bo, "Kind", bpath) or "post"
            if kind not in ("post", "group"):
                bad(bpath + ".Kind", "bad_token", "Kind 只能是 post/group，实际 '%s'" % kind)
            rule_stages = str_list(bo, "Stages", bpath, True)
            for stage_id in rule_stages:
                if stage_id not in stages:
                    bad(bpath + ".Stages", "unknown_stage", "阶段 '%s' 不存在" % stage_id)
            if kind == "post":
                posts = str_list(bo, "Posts", bpath, True)
            else:
                posts = []
                members = str_list(bo, "Members", bpath, True)
                if not members:
                    bad(bpath + ".Members", "empty_collection", "群组至少要有 1 名成员")
                host = text(bo, "Host", bpath)
                if host and host not in members:
                    bad(bpath + ".Host", "bad_value", "Host 必须在 Members 里")
                str_list(bo, "Lines", bpath, False)
                for sp in str_list(bo, "Speakers", bpath, False):
                    if sp not in members:
                        bad(bpath + ".Speakers", "bad_value", "说话者 '%s' 不在 Members 里" % sp)
                str_list(bo, "LineFallbacks", bpath, False)
                str_list(bo, "ActionsA", bpath, False)
                str_list(bo, "ActionsB", bpath, False)
                text(bo, "JoinNode", bpath, required=False)
            perception = bo.get("Perception")
            if isinstance(perception, dict):
                keys(perception, bpath + ".Perception", ("RequiresSight", "RangeMeters"))
                boolean(perception, "RequiresSight", bpath + ".Perception", True)
                num(perception, "RangeMeters", bpath + ".Perception", 12.0, 0.1, 500.0, required=True)
            trigger = bo.get("Trigger")
            if isinstance(trigger, dict):
                keys(trigger, bpath + ".Trigger", ("PlayerInsideGate", "MaxPerEncounter"))
                boolean(trigger, "PlayerInsideGate", bpath + ".Trigger", True)
                integer(trigger, "MaxPerEncounter", bpath + ".Trigger", 1, 1, 16)
            cooldown = bo.get("Cooldown")
            if isinstance(cooldown, dict):
                keys(cooldown, bpath + ".Cooldown", ("RetrySec", "ReentrySec"))
                num(cooldown, "RetrySec", bpath + ".Cooldown", 3.0, 0.0, 3600.0)
                num(cooldown, "ReentrySec", bpath + ".Cooldown", 8.0, 0.0, 3600.0)
            if kind == "post":
                rank = str_list(bo, "Rank", bpath, True)
                for rk in rank:
                    if rk not in ("distanceToPlayer", "npcId"):
                        bad(bpath + ".Rank", "bad_token", "排序键只能是 distanceToPlayer / npcId，实际 '%s'" % rk)
                if rank and rank[-1] != "npcId":
                    bad(bpath + ".Rank", "unstable_tiebreak", "排序键最后一项必须是 npcId")
                flow = text(bo, "Flow", bpath)
                if flow and flow not in flows:
                    bad(bpath + ".Flow", "unknown_flow", "流程 '%s' 不存在" % flow)
            integer(bo, "MaxActive", bpath, 1, 1, 8)
            for stage_id in rule_stages:
                stage = stages.get(stage_id)
                if not isinstance(stage, dict):
                    continue
                lay = resolved.get(stage.get("Layout"))
                if not lay:
                    continue
                for post in posts:
                    if post not in lay["posts"]:
                        bad(bpath + ".Posts", "unknown_post",
                            "岗位 '%s' 不在阶段 '%s' 的布局里" % (post, stage_id))

    beats = data.get("Beats")
    if isinstance(beats, list):
        roster = None
        opening_stage = {}
        if len(opening) == 1:
            opening_stage = stages.get(opening[0]) or {}
            roster = set(opening_stage.get("Actors") or []) | set(opening_stage.get("OptionalActors") or [])
        for i, bt in enumerate(beats):
            bpath = "$.Beats[%d]" % i
            if not isinstance(bt, dict):
                bad(bpath, "wrong_type", "台本拍必须是对象")
                continue
            num(bt, "At", bpath, 0.0, 0.0, 100000.0, required=True)
            cmd = text(bt, "Cmd", bpath)
            if not cmd:
                continue
            spec = ATOM_SPECS.get(cmd)
            if spec is None:
                bad(bpath + ".Cmd", "unknown_atom", "未注册的原子 '%s'" % cmd)
                continue
            if not spec[4] and formal:
                bad(bpath + ".Cmd", "unimplemented_atom", "原子 '%s' 尚未实现，不能进正式数据" % cmd)
            allowed = BEAT_KEYS.get(cmd)
            if allowed:
                keys(bt, bpath, allowed)

            if cmd in ("actor_place", "actor_action", "actor_say"):
                actor = text(bt, "Actor", bpath)
                if actor:
                    if roster is None:
                        bad(bpath + ".Actor", "no_opening_stage",
                            "台本里有演员动作，但没有任何阶段声明 Opening=true")
                    else:
                        if actor not in roster:
                            bad(bpath + ".Actor", "actor_not_in_roster", "演员 '%s' 不在开场阶段名单里" % actor)
                        if cast_ids and actor not in cast_ids:
                            bad(bpath + ".Actor", "unknown_cast_actor", "演员 '%s' 不在选角表里" % actor)
            if cmd == "camera_cut":
                text(bt, "Case", bpath)
                text(bt, "Anchor", bpath, required=False)
            elif cmd == "prop_spawn":
                pid = text(bt, "Id", bpath)
                if any(k in bt for k in ("Anchor", "Fwd", "Right", "Up", "Yaw", "Scale")):
                    bad(bpath, "dual_geometry", "prop_spawn 不得携带位置/朝向/缩放（几何只能来自布局 Props）")
                if pid and roster is not None:
                    if pid not in set(opening_stage.get("Props") or []) | set(opening_stage.get("OptionalProps") or []):
                        bad(bpath + ".Id", "prop_not_in_roster", "道具 '%s' 不在开场阶段道具名单里" % pid)
            elif cmd == "actor_place":
                post_id = text(bt, "Post", bpath, required=False)
                work_id = text(bt, "WorkPoint", bpath, required=False)
                sources = (1 if post_id else 0) + (1 if work_id else 0) + (1 if "Anchor" in bt else 0)
                if sources != 1:
                    bad(bpath, "bad_field", "actor_place 必须在 Post / WorkPoint / Anchor 里三选一（当前 %d 个）" % sources)
                lay = resolved.get((opening_stage or {}).get("Layout"))
                if lay:
                    if post_id and post_id not in lay["posts"]:
                        bad(bpath + ".Post", "unknown_post", "岗位 '%s' 不在布局里" % post_id)
                    if work_id and work_id not in lay["work"]:
                        bad(bpath + ".WorkPoint", "unknown_workpoint", "工作点 '%s' 不在布局里" % work_id)
            elif cmd == "actor_action":
                text(bt, "Action", bpath)
            elif cmd == "actor_say":
                text(bt, "TextKey", bpath)
            elif cmd == "wait":
                num(bt, "Seconds", bpath, 0.0, 0.0, 100000.0, required=True)

    return issues


# 夹具期望：文件名 -> (是否应通过, 必须命中的错误码)
STAGE_V2_EXPECT = {
    "STAGE_OK_two_guards.json": (True, None),
    "STAGE_BAD_unknown_token.json": (False, "unknown_atom"),
    "STAGE_BAD_roster_missing_actor.json": (False, "actor_not_in_roster"),
    "STAGE_BAD_dual_geometry.json": (False, "dual_geometry"),
    "STAGE_BAD_wait_no_timeout.json": (False, "wait_no_timeout"),
    "STAGE_BAD_empty_atom_args.json": (False, "unknown_param"),
    "STAGE_BAD_duplicate_step.json": (False, "duplicate_id"),
    "STAGE_BAD_unknown_role.json": (False, "unknown_role"),
    "STAGE_BAD_role_type.json": (False, "wrong_type"),
    "STAGE_BAD_arg_type.json": (False, "wrong_type"),
}


def stage_v2_fixture_paths(module):
    folder = os.path.join(module, "Debug", "PlanExamples")
    if not os.path.isdir(folder):
        return folder, []
    return folder, sorted(os.path.join(folder, n) for n in os.listdir(folder)
                          if n.startswith("STAGE_") and n.endswith(".json") and not n.startswith("STAGE_CAST_"))


def stage_v2_cast_ids(module):
    ids = set()
    for path in (os.path.join(module, "ModuleData", "DesignData", "StoryProbeCast.json"),
                 os.path.join(module, "Debug", "PlanExamples", "STAGE_CAST_two_guards.json")):
        if not os.path.exists(path):
            continue
        try:
            data = json.load(io.open(path, encoding="utf-8"), object_pairs_hook=stage_json_pairs)
        except Exception:
            continue
        for npc in data.get("Npcs") or []:
            if isinstance(npc, dict) and npc.get("Id"):
                ids.add(npc["Id"])
    return ids


def check_stage_v2(module, problems):
    """跑 P0 夹具：正样例必须通过、负样例必须命中指定错误码；正样例在正式模式下必须被拒绝。"""
    folder, files = stage_v2_fixture_paths(module)
    if not files:
        problems.append("stage v2: no fixtures under " + folder)
        return 0
    cast_ids = stage_v2_cast_ids(module)
    if not cast_ids:
        problems.append("stage v2: cast ids empty (StoryProbeCast.json / fixture cast missing)")
    positive = 0
    found = {os.path.basename(path) for path in files}
    for required in sorted(set(STAGE_V2_EXPECT) - found):
        problems.append("stage v2: required fixture missing: " + required)
    for path in files:
        name = os.path.basename(path)
        try:
            data = json.load(io.open(path, encoding="utf-8"), object_pairs_hook=stage_json_pairs)
        except Exception as ex:
            problems.append("stage v2: %s is not valid JSON (%s)" % (name, ex))
            continue
        issues = stage_v2_issues(data, cast_ids, formal=False, source=name)
        codes = {i["code"] for i in issues}
        expect = STAGE_V2_EXPECT.get(name)
        if expect is None:
            problems.append("stage v2: %s has no expected result in STAGE_V2_EXPECT" % name)
            continue
        should_pass, want_code = expect
        if should_pass:
            positive += 1
            for issue in issues[:10]:
                problems.append("stage v2: %s should PASS but got %s [%s] %s"
                                % (name, issue["path"], issue["code"], issue["message"]))
            formal = stage_v2_issues(data, cast_ids, formal=True, source=name)
            if "unimplemented_atom" not in {i["code"] for i in formal}:
                problems.append("stage v2: %s must be rejected in formal mode (unimplemented atoms)" % name)
        else:
            if not issues:
                problems.append("stage v2: %s should FAIL but passed" % name)
            elif want_code not in codes:
                problems.append("stage v2: %s did not report %s (got %s)"
                                % (name, want_code, ", ".join(sorted(codes))))
    return positive


def check_stage_v2_formal(module, problems):
    """校验正式 `StoryStageV2.json`（Formal 模式）：它是运行时真正发布/消费的布局，必须全绿。"""
    path = os.path.join(module, "ModuleData", "DesignData", "StoryStageV2.json")
    if not os.path.exists(path):
        problems.append("stage v2 formal: StoryStageV2.json missing")
        return 0
    try:
        data = json.load(io.open(path, encoding="utf-8"), object_pairs_hook=stage_json_pairs)
    except Exception as ex:
        problems.append("stage v2 formal: not valid JSON (%s)" % ex)
        return 0
    issues = stage_v2_issues(data, stage_v2_cast_ids(module), formal=True, source="StoryStageV2.json")
    for issue in issues[:20]:
        problems.append("stage v2 formal: %s [%s] %s" % (issue["path"], issue["code"], issue["message"]))
    return 1 if not issues else 0


def check_dialogue_annotations(module, problems):
    """对话 JSON 的 `*Zh` 审核注释必须与中文语言表一致（漂移即报错）。

    真源 = Languages/CNs 的 std_LivingWorldNpcs_strings.xml；JSON 里的中文只是只读副本，
    由 `Scripts/annotate_dialogue_zh.py` 生成。改了台词忘了重跑脚本 → 这里会红。
    """
    cn_path = os.path.join(module, "ModuleData", "Languages", "CNs", "std_LivingWorldNpcs_strings.xml")
    cn = load_lang(cn_path) if os.path.exists(cn_path) else {}
    if not cn:
        problems.append("dialogue annotations: CN localization missing")
        return 0
    fields = ("EntryOption", "NpcKey", "PlayerKey")
    checked = 0
    folder = os.path.join(module, "ModuleData", "DesignData", "Dialogues")
    for name in sorted(f for f in os.listdir(folder) if f.startswith("story_probe_") and f.endswith(".json")):
        try:
            data = json.load(io.open(os.path.join(folder, name), encoding="utf-8"))
        except Exception as ex:
            problems.append("dialogue annotations: %s not readable (%s)" % (name, ex))
            continue

        def walk(obj):
            nonlocal checked
            if isinstance(obj, dict):
                for key, value in obj.items():
                    if key in fields and isinstance(value, str):
                        checked += 1
                        want = cn.get(value, "（缺中文）")
                        got = obj.get(key + "Zh")
                        if got is None:
                            problems.append("%s: %s '%s' has no %sZh annotation" % (name, key, value, key))
                        elif got != want:
                            problems.append("%s: %sZh stale for '%s' (json=%r xml=%r)"
                                            % (name, key, value, got, want))
                    elif isinstance(value, (dict, list)):
                        walk(value)
            elif isinstance(obj, list):
                for item in obj:
                    walk(item)

        walk(data)
    return checked


# ═══════════════════════════════════════════════════════════════════════════
# P0-D.1 对话定义契约：story_dialogue/2（2026-11-15）
# 与 C# StoryDialogueParser 同一套规则，夹具在 Debug/PlanExamples/DIALOGUE_*.json
# ═══════════════════════════════════════════════════════════════════════════

DLG_SCHEMA = "story_dialogue/2"
DLG_ROOT = ("Schema", "DialogueId", "ContentVersion", "Participants", "Actors", "Triggers", "Entries", "Nodes",
            "EntryOption", "SkipVanillaOpening")
DLG_SOURCES = ("player", "post_ai", "event", "script")
DLG_COMMITS = ("on_enter", "on_select", "on_session_end")
DLG_RECEIPTS = ("once", "session", "never")
DLG_RANK = ("distanceToPlayer", "npcId")
DLG_ABILITIES = ("charm", "guile", "force")
# 条件谓词：name -> (required_params, implemented)
DLG_PREDICATES = {
    "phase": (True, True), "disguised": (False, True), "noble": (False, True),
    "stripped": (False, True), "gate_open": (False, True), "stripped_disarmed": (False, True),
    "asked": (True, True), "reward": (True, True), "quest_accepted": (False, True),
    "quest_done": (False, True), "quest_turned": (False, True), "gold>=": (True, True),
    "item": (True, True), "event_resolved": (True, True), "resolution": (True, True),
    "money_attitude": (True, True),
    "errand_ready": (False, True), "can_redeem_keepsake": (False, True),
    "street_help_pending": (False, True), "step>=": (True, True), "event_active": (True, True),
    "clue": (True, True), "hazard": (True, True), "attitude": (True, True),
    "can_see": (True, False), "knows": (True, False),
}
# 效果原子：name -> (params, required, implemented)
DLG_EFFECTS = {
    "mark": (("flag",), ("flag",), True),
    "hazard": (("flag",), ("flag",), True),
    "clue": (("clue",), ("clue",), True),
    "resolve_event": (("event",), ("event",), True),
    "open_gate": (("route", "faction", "delta", "crime", "hazard"), ("route",), True),
    "force_defeat": ((), (), True),
    "pay": (("amount", "to", "reason"), ("amount",), True),
    "relation": (("faction", "delta"), ("faction", "delta"), True),
    "skill_check": (("skill", "threshold"), ("skill",), True),
    "give_item": (("item", "count"), ("item",), True),
    "start_fight": (("target",), (), True),
    "accept_quest": ((), (), True),
    "turn_in": ((), (), True),
    "complete_quest": (("npc", "resolution"), ("npc", "resolution"), True),
    "collect_debt": (("amount",), ("amount",), True),
    "errand": (("pay",), ("pay",), True),
    "street_help_reward": ((), (), True),
    "redeem_keepsake": ((), (), True),
    "money_offer": ((), (), True),
    "money_deal": ((), (), True),
    "start_event": (("event",), ("event",), True),
    "quest_accept": (("npc", "solved_event"), ("npc",), True),
    "quest_turn_in": (("npc",), ("npc",), True),
    "debt_collect": (("from", "amount", "quest"), ("from", "amount", "quest"), True),
    "grant_gold": (("amount",), ("amount",), True),
    "reward_receipt": (("npc", "key"), ("npc", "key"), True),
    "message": (("key",), ("key",), True),
}


def _dlg_dup_keys(pairs):
    """object_pairs_hook：JSON 对象内重复键直接报错（Python 默认会静默保留最后一个）。"""
    seen = set()
    for k, _ in pairs:
        if k in seen:
            raise ValueError("duplicate JSON key: " + k)
        seen.add(k)
    return dict(pairs)


def story_dialogue_issues(data, formal=False):
    """校验一份 story_dialogue/2；返回 [{path, code, message}]。"""
    issues = []

    def bad(path, code, msg):
        issues.append({"path": path, "code": code, "message": msg})

    def keys(obj, path, allowed, allow_comment=False):
        for k in obj:
            if allow_comment and k.startswith("_"):
                continue
            if k not in allowed:
                bad(path + "." + k, "unknown_key", "未知字段 '%s'（允许：%s）" % (k, ", ".join(allowed)))

    def text(obj, path, name, required=True):
        if name not in obj or obj[name] is None:
            if required:
                bad(path + "." + name, "missing_field", "缺少字符串 '%s'" % name)
            return ""
        v = obj[name]
        if not isinstance(v, str):
            bad(path + "." + name, "wrong_type", "'%s' 必须是字符串" % name)
            return ""
        if required and not v:
            bad(path + "." + name, "missing_field", "'%s' 不能为空" % name)
        return v

    def str_list(obj, path, name, required=False):
        if name not in obj or obj[name] is None:
            if required:
                bad(path + "." + name, "missing_field", "缺少数组 '%s'" % name)
            return []
        v = obj[name]
        if not isinstance(v, list):
            bad(path + "." + name, "wrong_type", "'%s' 必须是数组" % name)
            return []
        out = []
        for item in v:
            if not isinstance(item, str):
                bad(path + "." + name, "wrong_type", "元素必须是字符串")
                continue
            out.append(item)
        if required and not out:
            bad(path + "." + name, "empty_collection", "'%s' 不能为空" % name)
        return out

    def dup(ids, path, what):
        seen = set()
        for i in ids:
            if not i:
                continue
            if i in seen:
                bad(path, "duplicate_id", "%s ID 重复 '%s'" % (what, i))
            seen.add(i)

    def conditions(values, path):
        for raw in values:
            term = raw[1:] if raw.startswith("!") else raw
            if not term:
                bad(path + ".Requires", "bad_value", "空条件")
                continue
            name, sep, value = term.partition(":")
            if not sep and term.startswith("gold>="):
                # 运算符式写法：gold>=100（与 item:xxx 这类冒号式共存）
                name, value = "gold>=", term[len("gold>="):]
            elif not sep and term.startswith("step>="):
                name, value = "step>=", term[len("step>="):]
            elif not sep and "@" in term:
                name, _, value = term.partition("@")
            if name not in DLG_PREDICATES:
                bad(path + ".Requires", "unknown_predicate", "未登记的条件谓词 '%s'" % name)
                continue
            required, implemented = DLG_PREDICATES[name]
            if required and not value:
                bad(path + ".Requires", "missing_param", "谓词 '%s' 需要参数" % name)
            if value and name in ("gold>=", "event_resolved") and not value.isdigit():
                bad(path + ".Requires", "bad_param", "谓词 '%s' 的参数必须是非负整数：%s" % (name, value))
            if not implemented and formal:
                bad(path + ".Requires", "unimplemented_predicate", "条件 '%s' 尚未实现，不能进正式数据" % name)

    def effects(obj, path, name):
        out = []
        for i, eff in enumerate(obj.get(name) or []):
            epath = "%s.%s[%d]" % (path, name, i)
            if not isinstance(eff, dict):
                bad(epath, "wrong_type", "效果必须是对象")
                continue
            keys(eff, epath, ("Id", "Params", "Commit", "Receipt"))
            eid = text(eff, epath, "Id")
            commit = text(eff, epath, "Commit", False) or "on_select"
            receipt = text(eff, epath, "Receipt", False) or "once"
            if commit not in DLG_COMMITS:
                bad(epath + ".Commit", "bad_token", "Commit 只能是 " + "/".join(DLG_COMMITS))
            if receipt not in DLG_RECEIPTS:
                bad(epath + ".Receipt", "bad_token", "Receipt 只能是 " + "/".join(DLG_RECEIPTS))
            params = eff.get("Params") or {}
            if not isinstance(params, dict):
                bad(epath + ".Params", "wrong_type", "Params 必须是对象")
                params = {}
            if eid and eid not in DLG_EFFECTS:
                bad(epath, "unknown_effect", "未登记的效果原子 '%s'" % eid)
            elif eid:
                allowed, required, implemented = DLG_EFFECTS[eid]
                for k in params:
                    if k not in allowed:
                        bad(epath + ".Params", "unknown_param", "效果 '%s' 不接受参数 '%s'" % (eid, k))
                for req in required:
                    if req not in params:
                        bad(epath + ".Params", "missing_param", "效果 '%s' 缺必需参数 '%s'" % (eid, req))
                if not implemented and formal:
                    bad(epath, "unimplemented_effect", "效果 '%s' 尚未实现，不能进正式数据" % eid)
            out.append({"Id": eid, "Commit": commit, "Receipt": receipt})
        return out

    if not isinstance(data, dict):
        bad("$", "wrong_type", "根必须是对象")
        return issues

    keys(data, "$", DLG_ROOT, allow_comment=True)
    if data.get("Schema") != DLG_SCHEMA:
        bad("$.Schema", "schema_mismatch", "必须是 '%s'，实际 '%s'" % (DLG_SCHEMA, data.get("Schema")))
    dialogue_id = text(data, "$", "DialogueId")
    if dialogue_id and not re.match(r"^[a-z0-9_]+$", dialogue_id):
        bad("$.DialogueId", "bad_token", "只能小写字母/数字/下划线：" + dialogue_id)
    if "ContentVersion" not in data:
        bad("$.ContentVersion", "missing_field", "缺少 ContentVersion")
    elif not isinstance(data.get("ContentVersion"), int) or isinstance(data.get("ContentVersion"), bool) \
            or data["ContentVersion"] < 1:
        bad("$.ContentVersion", "bad_value", "ContentVersion 必须是 >=1 的整数")
    participants = str_list(data, "$", "Participants", True)
    actors = str_list(data, "$", "Actors", False)
    dup(participants, "$.Participants", "参与者")
    dup(actors, "$.Actors", "实名演员")

    triggers = []
    for i, t in enumerate(data.get("Triggers") or []):
        tpath = "$.Triggers[%d]" % i
        if not isinstance(t, dict):
            bad(tpath, "wrong_type", "触发器必须是对象")
            continue
        keys(t, tpath, ("Id", "Source", "Purpose", "Conditions", "Rank", "CooldownSec", "ReentrySec", "OnCancel"))
        tid = text(t, tpath, "Id")
        src = text(t, tpath, "Source") or "player"
        if src not in DLG_SOURCES:
            bad(tpath + ".Source", "bad_token", "Source 只能是 " + "/".join(DLG_SOURCES))
        rank = str_list(t, tpath, "Rank", False)
        for r in rank:
            if r not in DLG_RANK:
                bad(tpath + ".Rank", "bad_token", "排序键只能是 " + "/".join(DLG_RANK))
        if rank and rank[-1] != "npcId":
            bad(tpath + ".Rank", "unstable_tiebreak", "排序键最后一项必须是 npcId")
        for num_key in ("CooldownSec", "ReentrySec"):
            v = t.get(num_key, 0)
            if type(v) not in (int, float) or isinstance(v, bool) or v < 0:
                bad(tpath + "." + num_key, "bad_range", "%s 必须是非负数" % num_key)
        conditions(str_list(t, tpath, "Conditions", False), tpath)
        triggers.append(tid)
    if not triggers:
        bad("$.Triggers", "empty_collection", "至少要有一个触发器")
    dup(triggers, "$.Triggers", "触发器")

    entries = []
    for i, e in enumerate(data.get("Entries") or []):
        epath = "$.Entries[%d]" % i
        if not isinstance(e, dict):
            bad(epath, "wrong_type", "入口规则必须是对象")
            continue
        keys(e, epath, ("Id", "Trigger", "Conditions", "Priority", "Node"))
        eid = text(e, epath, "Id")
        trig = text(e, epath, "Trigger")
        if trig and trig not in triggers:
            bad(epath + ".Trigger", "unknown_trigger", "触发器 '%s' 不存在" % trig)
        node = text(e, epath, "Node")
        nodes_ids = [n.get("Id") for n in (data.get("Nodes") or []) if isinstance(n, dict)]
        if node and node not in nodes_ids:
            bad(epath + ".Node", "unknown_node", "节点 '%s' 不存在" % node)
        conditions(str_list(e, epath, "Conditions", False), epath)
        entries.append(eid)
    if not entries:
        bad("$.Entries", "empty_collection", "至少要有一个入口规则")
    dup(entries, "$.Entries", "入口规则")

    node_ids = []
    for i, n in enumerate(data.get("Nodes") or []):
        npath = "$.Nodes[%d]" % i
        if not isinstance(n, dict):
            bad(npath, "wrong_type", "节点必须是对象")
            continue
        keys(n, npath, ("Id", "Speaker", "TextKey", "LineActions", "Requires", "OnEnter", "Options"))
        nid = text(n, npath, "Id")
        speaker = text(n, npath, "Speaker")
        if speaker and speaker not in participants and speaker not in actors:
            bad(npath + ".Speaker", "unknown_speaker", "说话者 '%s' 既不在 Participants 也不在 Actors" % speaker)
        text(n, npath, "TextKey")
        conditions(str_list(n, npath, "Requires", False), npath)
        effects(n, npath, "OnEnter")
        option_ids = []
        for j, o in enumerate(n.get("Options") or []):
            opath = "%s.Options[%d]" % (npath, j)
            if not isinstance(o, dict):
                bad(opath, "wrong_type", "选项必须是对象")
                continue
            keys(o, opath, ("Id", "PlayerKey", "Requires", "Effects", "Check", "NextNode"))
            oid = text(o, opath, "Id")
            text(o, opath, "PlayerKey")
            conditions(str_list(o, opath, "Requires", False), opath)
            effects(o, opath, "Effects")
            nxt = text(o, opath, "NextNode", False)
            check = o.get("Check")
            all_ids = [x.get("Id") for x in (data.get("Nodes") or []) if isinstance(x, dict)]
            if check is not None:
                if not isinstance(check, dict):
                    bad(opath + ".Check", "wrong_type", "Check 必须是对象")
                else:
                    keys(check, opath + ".Check", ("Skill", "Target", "Success", "Fail"))
                    skill = text(check, opath + ".Check", "Skill")
                    if skill and skill.lower() not in DLG_ABILITIES:
                        bad(opath + ".Check.Skill", "bad_token", "检定技能只能是 charm/guile/force：%s" % skill)
                    target = text(check, opath + ".Check", "Target", False)
                    if target and target not in participants and target not in actors:
                        bad(opath + ".Check.Target", "unknown_speaker",
                            "检定目标 '%s' 既不在 Participants 也不在 Actors" % target)
                    succ = text(check, opath + ".Check", "Success")
                    if succ and succ not in all_ids:
                        bad(opath + ".Check.Success", "unknown_node", "成功节点 '%s' 不存在" % succ)
                    fail = text(check, opath + ".Check", "Fail", False)
                    if fail and fail not in all_ids:
                        bad(opath + ".Check.Fail", "unknown_node", "失败节点 '%s' 不存在" % fail)
                    if nxt:
                        bad(opath, "bad_value", "Check 与 NextNode 只能二选一")
            if nxt:
                if nxt not in all_ids:
                    bad(opath + ".NextNode", "unknown_node", "节点 '%s' 不存在" % nxt)
            option_ids.append(oid)
        dup(option_ids, npath + ".Options", "选项")
        node_ids.append(nid)
    if not node_ids:
        bad("$.Nodes", "empty_collection", "至少要有一个节点")
    dup(node_ids, "$.Nodes", "节点")

    return issues


DIALOGUE_FIXTURE_EXPECT = {
    "DIALOGUE_OK_gate.json": (True, None),
    "DIALOGUE_BAD_unknown_field.json": (False, "unknown_key"),
    "DIALOGUE_BAD_duplicate_option.json": (False, "duplicate_id"),
    "DIALOGUE_BAD_dangling_node.json": (False, "unknown_node"),
    "DIALOGUE_BAD_unknown_predicate.json": (False, "unknown_predicate"),
}


def check_dialogue_v2(module, problems):
    """跑 DIALOGUE_* 夹具：正样例必须通过，负样例必须命中指定错误码。"""
    folder = os.path.join(module, "Debug", "PlanExamples")
    if not os.path.isdir(folder):
        problems.append("dialogue v2: fixture folder missing: " + folder)
        return 0
    files = sorted(os.path.join(folder, n) for n in os.listdir(folder)
                   if n.startswith("DIALOGUE_") and n.endswith(".json"))
    if not files:
        problems.append("dialogue v2: no DIALOGUE_*.json fixtures")
        return 0
    passed = 0
    for path in files:
        name = os.path.basename(path)
        expect = DIALOGUE_FIXTURE_EXPECT.get(name)
        if expect is None:
            problems.append("dialogue v2: %s has no expected result" % name)
            continue
        try:
            data = json.loads(io.open(path, encoding="utf-8").read(), object_pairs_hook=_dlg_dup_keys)
        except Exception as ex:
            issues = [{"path": name, "code": "json_parse", "message": str(ex)}]
        else:
            issues = story_dialogue_issues(data, formal=False)
        codes = {i["code"] for i in issues}
        should_pass, want = expect
        if should_pass:
            passed += 1
            for issue in issues[:8]:
                problems.append("dialogue v2: %s should PASS but got %s [%s] %s"
                                % (name, issue["path"], issue["code"], issue["message"]))
        else:
            if not issues:
                problems.append("dialogue v2: %s should FAIL but passed" % name)
            elif want not in codes:
                problems.append("dialogue v2: %s did not report %s (got %s)"
                                % (name, want, ", ".join(sorted(codes))))
    # 正式迁移桥：ModuleData/DesignData/Dialogues/*.dialog.json 必须在**正式模式**下通过
    formal_dir = os.path.join(module, "ModuleData", "DesignData", "Dialogues")
    if os.path.isdir(formal_dir):
        for name in sorted(n for n in os.listdir(formal_dir) if n.endswith(".dialog.json")):
            path = os.path.join(formal_dir, name)
            try:
                data = json.loads(io.open(path, encoding="utf-8").read(), object_pairs_hook=_dlg_dup_keys)
            except Exception as ex:
                problems.append("dialogue formal: %s not readable (%s)" % (name, ex))
                continue
            for issue in story_dialogue_issues(data, formal=True)[:10]:
                problems.append("dialogue formal: %s %s [%s] %s"
                                % (name, issue["path"], issue["code"], issue["message"]))
    return passed


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--module", default=DEFAULT_MODULE)
    ap.add_argument("--selftest", action="store_true")
    ap.add_argument("--scene", default="empire_town_m",
                    help="序章实际跑的城镇中心场景（决定布景 prefab 存不存在）")
    args = ap.parse_args()
    if args.selftest:
        return selftest()
    module = os.path.abspath(args.module)

    problems = []
    keys, node_total = check_json(module, problems)

    en_path = os.path.join(module, "ModuleData", "Languages", "std_LivingWorldNpcs_strings.xml")
    cn_path = os.path.join(module, "ModuleData", "Languages", "CNs", "std_LivingWorldNpcs_strings.xml")
    en = load_lang(en_path)
    cn = load_lang(cn_path)

    if not en:
        problems.append("english localization file missing/empty")
    if not cn:
        problems.append("chinese localization file missing/empty")

    missing_en = sorted(k for k in keys if en and k not in en)
    missing_cn = sorted(k for k in keys if cn and k not in cn)
    for k in missing_en:
        problems.append(f"dialogue key missing in EN: {k}")
    for k in missing_cn:
        problems.append(f"dialogue key missing in CN: {k}")

    # 对钱的态度：对话里的 `<npc>_money_<态度>` 节点必须与 StoryProbeDefs 的表一致
    # （改了表忘了改对话 = 当面递钱时演的和实际做的对不上，静默错）
    cast_keys, cast = check_cast(module, problems)
    tuning_ok = check_tuning(module, problems)
    check_flow_contract(module, problems)
    stage_beats = check_stage(module, problems)
    stage_v2_positive = check_stage_v2(module, problems)
    stage_v2_formal = check_stage_v2_formal(module, problems)
    annotated = check_dialogue_annotations(module, problems)
    dialogue_v2 = check_dialogue_v2(module, problems)

    stage_data = load_data_json(os.path.join(module, "ModuleData", "DesignData", "StoryStage.json")) or {}
    check_references(module, problems, cast, stage_data, args.scene)
    line_actions = check_line_actions(module, problems, cast)
    keys |= cast_keys

    if cast:
        for n in (cast.get("Npcs") or []):
            npc = n.get("Id")
            attitude = (n.get("Money") or "Disdain").lower()
            if npc == "blocker":
                continue   # 守门人的钱路是 blocker_open 里的「塞钱」选项，不是独立节点
            # 只有"要谈钱的人"（四家有委托的 + 有这个节点的人）才检查；
            # 打手/负债者/对台班子/强盗本来就没有钱这条对话，不该报错
            if not (n.get("QuestTargetNpcId") or n.get("IsBlocker")):
                continue

            json_path = os.path.join(module, "ModuleData", "DesignData", "Dialogues",
                                     "story_probe_%s.json" % npc)
            if not os.path.exists(json_path):
                continue
            data = json.load(io.open(json_path, encoding="utf-8"))
            ids = {n.get("Id") for n in data.get("Nodes", [])}
            want = "%s_money_%s" % (npc, attitude)
            if want not in ids:
                problems.append("money attitude mismatch: %s is %s in Defs but has no node %s"
                                % (npc, attitude, want))

    # 动态拼出来的键（代码里是 `"LWN_story_react_near_" + def.Id`）按 NPC 名单展开成具体键
    npc_ids = sorted(os.path.basename(f)[len("story_probe_"):-len(".json")]
                     for f in glob.glob(os.path.join(module, "ModuleData", "DesignData", "Dialogues", "story_probe_*.json")))
    dynamic = set()
    for prefix in ("LWN_story_react_near_", "LWN_story_react_weapon_"):
        for npc in npc_ids:
            dynamic.add((prefix, npc))
    # 常驻吆喝按 "<npc>_1"/"<npc>_2" 编号（每人至少一条）
    for npc in npc_ids:
        dynamic.add(("LWN_story_amb_", npc + "_1"))

    code = code_keys(module)
    for prefix, npc in dynamic:
        code.add(prefix + npc)

    check_speech_format(module, problems, en, cn)
    check_dialogue_rules(module, problems, en, cn)

    for k in sorted(code):
        # 前缀占位形式（以 _ 结尾）跳过 —— 已按 NPC 名单展开
        if k.endswith("_"):
            continue
        if en and k not in en:
            problems.append(f"code key missing in EN: {k}")
        if cn and k not in cn:
            problems.append(f"code key missing in CN: {k}")

    for label, mapping in (("EN", en), ("CN", cn)):
        for k, v in mapping.items():
            if v and any(ord(ch) > 0xFFFF for ch in v):
                problems.append(f"astral char (emoji) in {label}: {k}")

    summary = (f"story_probe: cast_npcs={len((cast or {}).get('Npcs') or [])} stage_beats={stage_beats} "
               f"stage_v2_positive={stage_v2_positive} dialogue_v2_positive={dialogue_v2} annotated_fields={annotated} "
               f"long_nodes_todo={len(TODO_LONG_NODES)} line_actions={line_actions} dialogue_files={len(glob.glob(os.path.join(module, 'ModuleData/DesignData/Dialogues/story_probe_*.json')))} "
               f"nodes={node_total} dialogue_keys={len(keys)} code_keys={len(code)} "
               f"en={len(en)} cn={len(cn)} problems={len(problems)}")
    print(summary)
    for p in problems[:60]:
        print("  -", p)
    return 0 if not problems else 1


if __name__ == "__main__":
    sys.exit(main())
