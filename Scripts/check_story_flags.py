# -*- coding: utf-8 -*-
"""剧情标记登记闸门（配套 ModuleData/DesignData/StoryFlags.json）。

规则：
  · 对白里用到的 mark/asked 标记必须在登记表里（未登记 = 拒收）；
  · 非 Legacy 标记必须带命名空间（knows. / npc. / scene.），且 Type 合法；
  · 登记了但没人用 → 只报警告，不判失败（可能是预留给下一步内容）；
  · 同一标记同时有 writers/readers 为空 → 警告。
"""
import glob
import io
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REGISTRY = os.path.join(ROOT, "ModuleData", "DesignData", "StoryFlags.json")
CLUES = os.path.join(ROOT, "ModuleData", "DesignData", "StoryClues.json")
DIALOGUES = os.path.join(ROOT, "ModuleData", "DesignData", "Dialogues")
NAMESPACE = re.compile(r"^(knows|npc|scene)\.\w+$")
TYPES = {"bool", "count"}


def collect():
    writers, readers = {}, {}
    cl_writers, cl_readers = {}, {}
    for path in sorted(glob.glob(os.path.join(DIALOGUES, "*.dialog.json"))):
        data = json.loads(io.open(path, encoding="utf-8").read())
        name = os.path.basename(path)
        for node in data.get("Nodes", []):
            where = "%s:%s" % (name, node["Id"])
            for eff in node.get("OnEnter") or []:
                if eff.get("Id") == "mark":
                    flag = (eff.get("Params") or {}).get("flag")
                    if flag:
                        writers.setdefault(flag, set()).add(where)
            for eff in node.get("OnEnter") or []:
                if eff.get("Id") == "clue":
                    clue = (eff.get("Params") or {}).get("clue")
                    if clue:
                        cl_writers.setdefault(clue, set()).add(where)
            for cond in node.get("Requires") or []:
                if isinstance(cond, str) and cond.lstrip("!").startswith("asked:"):
                    readers.setdefault(cond.lstrip("!")[6:], set()).add(where)
                if isinstance(cond, str) and cond.lstrip("!").startswith("clue:"):
                    cl_readers.setdefault(cond.lstrip("!")[5:], set()).add(where)
            for opt in node.get("Options") or []:
                for eff in opt.get("Effects") or []:
                    if eff.get("Id") == "mark":
                        flag = (eff.get("Params") or {}).get("flag")
                        if flag:
                            writers.setdefault(flag, set()).add(where + "/" + opt["Id"])
                for cond in opt.get("Requires") or []:
                    if isinstance(cond, str) and cond.lstrip("!").startswith("asked:"):
                        readers.setdefault(cond.lstrip("!")[6:], set()).add(where + "/" + opt["Id"])
    return writers, readers, cl_writers, cl_readers


def main():
    if not os.path.isfile(REGISTRY):
        print("FAIL: missing %s" % REGISTRY)
        return 1
    doc = json.loads(io.open(REGISTRY, encoding="utf-8").read())
    flags = {f["Id"]: f for f in doc.get("Flags", [])}
    writers, readers, cl_writers, cl_readers = collect()
    used = set(writers) | set(readers)
    errors, warnings = [], []

    for flag in sorted(used - set(flags)):
        errors.append("unregistered flag '%s' (writers=%d readers=%d)" % (flag, len(writers.get(flag, ())), len(readers.get(flag, ()))))
    for flag in sorted(set(flags) - used):
        warnings.append("registered but unused: '%s'" % flag)
    for flag, spec in sorted(flags.items()):
        if spec.get("Type") not in TYPES:
            errors.append("flag '%s': bad Type '%s'" % (flag, spec.get("Type")))
        if not spec.get("Legacy") and not NAMESPACE.match(flag):
            errors.append("flag '%s': non-legacy flags need a namespace (knows./npc./scene.)" % flag)
        if flag.startswith("quest_"):
            errors.append("flag '%s': use the structured predicate quest_*@npc instead of a mark flag" % flag)

    # 同义候选：去掉常见前缀/后缀后撞名 → 警告（不自动合并，交给人确认）
    def normalize(flag):
        name = flag.split(".")[-1]
        for prefix in ("ask_", "lesson_", "attempt_", "knows_", "met_", "chat_"):
            if name.startswith(prefix):
                name = name[len(prefix):]
        for suffix in ("_done", "_ok", "_reviewed", "_2", "_more"):
            if name.endswith(suffix):
                name = name[: -len(suffix)]
        return name
    buckets = {}
    for flag in sorted(used):
        buckets.setdefault(normalize(flag), []).append(flag)
    for key, group in sorted(buckets.items()):
        if len(group) > 1:
            warnings.append("possible synonyms (%s): %s" % (key, ", ".join(group)))

    for flag in sorted(used):
        if flag.startswith("knows"):
            errors.append("flag '%s': knowledge must use clue, not mark (knows.* is forbidden)" % flag)
    if os.path.isfile(CLUES):
        cdoc = json.loads(io.open(CLUES, encoding="utf-8").read())
        known = {c["Id"]: c for c in cdoc.get("Clues", [])}
        for clue in sorted(set(cl_writers) | set(cl_readers)):
            if clue not in known:
                errors.append("unregistered clue '%s' (writers=%d readers=%d)" % (clue, len(cl_writers.get(clue, ())), len(cl_readers.get(clue, ()))))
            elif not cl_writers.get(clue):
                warnings.append("clue '%s' has no writer (nothing ever grants it)" % clue)
    else:
        warnings.append("StoryClues.json missing; clue registry unchecked")

    print("story flags: registry=%d used=%d writers=%d readers=%d"
          % (len(flags), len(used), sum(len(v) for v in writers.values()), sum(len(v) for v in readers.values())))
    for w in warnings:
        print("  warn:", w)
    for e in errors:
        print("  [X]", e)
    if errors:
        print("FAIL: story flag registry (%d errors)" % len(errors))
        return 1
    print("PASS: story flag registry")
    return 0


if __name__ == "__main__":
    sys.exit(main())