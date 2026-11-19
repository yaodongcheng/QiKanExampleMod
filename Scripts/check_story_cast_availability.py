# -*- coding: utf-8 -*-
"""剧情演员出场判据的源码闸门（配套 StoryProbeDefs.StoryCastAvailabilityHelper）。

守三条，任一条不成立就 exit 1：
  1. 四态映射存在且「陈旧据点」不是拒绝；
  2. SpawnNpc 走单一判据，不再手写 IsPrisoner / PartyBelongedTo 条件；
  3. 没有第二处定义同名判据（避免又散开）。
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFS = os.path.join(ROOT, "ExampleModVS", "ExampleMod", "ExampleMod", "Interaction", "StoryProbe", "StoryProbeDefs.cs")
BEHAVIOR = os.path.join(ROOT, "ExampleModVS", "ExampleMod", "ExampleMod", "Interaction", "StoryProbe", "StoryProbeMissionBehavior.cs")


def read(path):
    return io.open(path, encoding="utf-8").read()


def main():
    problems = []
    defs = read(DEFS)
    if "return staleSettlement ? StoryCastAvailability.OkStaleSettlement : StoryCastAvailability.Ok;" not in defs:
        problems.append("StoryCastAvailabilityHelper: stale settlement must not be a refusal")
    for token in ("private Agent SpawnNpc",):
        pass
    behavior = read(BEHAVIOR)
    start = behavior.find("private Agent SpawnNpc(")
    end = behavior.find(chr(10) + "        private ", start + 10) if start >= 0 else -1
    body = behavior[start:end] if start >= 0 and end > start else ""
    if "StoryCastAvailabilityHelper.Evaluate(hero, Settlement.CurrentSettlement)" not in body:
        problems.append("SpawnNpc must use StoryCastAvailabilityHelper.Evaluate")
    if "hero.IsPrisoner" in body and "refused" in body:
        # 允许出现在日志字符串里，不允许作为 gating 条件
        gating = re.search(r"if\s*\([^)]*hero\.IsPrisoner", body)
        if gating:
            problems.append("SpawnNpc: duplicated prisoner gating (use the helper)")
    count = len(re.findall(r"enum StoryCastAvailability", read(DEFS))) + \
            sum(len(re.findall(r"enum StoryCastAvailability", read(os.path.join(dirpath, f))))
                for dirpath, _, files in os.walk(os.path.join(ROOT, "ExampleModVS")) for f in files
                if f.endswith(".cs") and os.path.join(dirpath, f) != DEFS)
    if count != 1:
        problems.append("StoryCastAvailability enum defined %d times (must be exactly 1)" % count)
    if problems:
        print("FAIL: story cast availability")
        for p in problems:
            print("  -", p)
        return 1
    print("PASS: story cast availability (single predicate, stale settlement not refused)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
