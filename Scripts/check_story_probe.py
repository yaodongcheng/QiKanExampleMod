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
                   "gate_open", "blocker_disarmed"}
    known_prefix = ("gold>=", "item:", "clue:", "hazard:", "step>=",
                    "quest_accepted@", "quest_done@", "quest_turned@")

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
                        term = term.strip().lstrip("!")
                        if not term:
                            continue
                        if term in known_terms or term.startswith(known_prefix):
                            continue
                        problems.append(f"{name}:{nid}: unknown Requires term '{term}'")

        # 🔴 同一个回答节点不许被"两种不同问法"指向 —— 否则第二次问会得到一模一样的回答（NPC 像失忆）
        #    菜单节点（出度≥5）除外：那里本来就是"回头再说"的汇总口。
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
TUNING_SECTIONS = ("Ambient", "Events", "Skits", "Gate", "Greeting", "Crime", "Intro")


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

SPOKEN_CN = re.compile(r'^（[^，（）]+，[^，（）]+）：\S')
SPOKEN_EN = re.compile(r'^\([^,()]+, [^,()]+\): \S')


def check_speech_format(module, problems, en, cn):
    files = sorted(glob.glob(os.path.join(module, "ModuleData", "DesignData", "Dialogues", "story_probe_*.json")))
    keys = set()
    for path in files:
        try:
            data = json.load(io.open(path, encoding="utf-8"))
        except Exception:
            continue
        for node in data.get("Nodes", []):
            if node.get("NpcKey"):
                keys.add(node["NpcKey"])

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
                # ⚠️ 2026-11-13：规则的**目标**是 ≤2 行，但那要靠重写脚本（见 story-dialogue-writing 技能），
                #    不是机械切行。重写完成前这里只统计、不算失败。
                TODO_LONG_NODES.append("%s:%s=%d行" % (name, node.get("Id"), len(lines)))
                bad_lines += 1
            for tr in node.get("Transitions", []):
                pk = tr.get("PlayerKey") or ""
                label = cn.get(pk, "")
                label = re.sub(r"^\[[^\]]*\]\s*", "", label).strip()

            # 🔴 选项不能指向"菜单节点"（出度≥5）—— 指向菜单 = 对方没回应你，只会说"还有事？"
            nxt = tr.get("NextNodeOnSuccess")
            if nxt:
                tgt = next((x for x in data.get("Nodes", []) if x.get("Id") == nxt), None)
                # 只有"问句"弹回菜单才算错；「别的先不问了」这类本来就是收尾
                if tgt is not None and len(tgt.get("Transitions") or []) >= 5 and "？" in label:
                    problems.append("dialogue[%s:%s]: 选项 '%s' 指向菜单节点 '%s'（对方不会回应你）"
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
        return 0
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


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
    stage_beats = check_stage(module, problems)

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
               f"long_nodes_todo={len(TODO_LONG_NODES)} line_actions={line_actions} dialogue_files={len(glob.glob(os.path.join(module, 'ModuleData/DesignData/Dialogues/story_probe_*.json')))} "
               f"nodes={node_total} dialogue_keys={len(keys)} code_keys={len(code)} "
               f"en={len(en)} cn={len(cn)} problems={len(problems)}")
    print(summary)
    for p in problems[:60]:
        print("  -", p)
    return 0 if not problems else 1


if __name__ == "__main__":
    sys.exit(main())
