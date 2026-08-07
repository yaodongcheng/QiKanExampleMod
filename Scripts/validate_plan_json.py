#!/usr/bin/env python3
"""validate_plan_json.py -- Plan JSON jump-integrity validation.

Checks every ```json block in a markdown file (default: the GOAP plan doc)
against the plan's jump 铁律 (plans/llm-goap-plan-execution.md §5.1):

  S1 (forward)  every jump target id (on_timeout / on_success / on_event[].then /
                contingencies[].then / result routing / time_since.step_id)
                must exist among defined step ids (steps / fallbacks /
                loop.steps), or be a reserved @-directive (e.g. @abort_gracefully).
  S2 (reverse)  every fallback entry's FIRST step id must be referenced by at
                least one jump source -- no dead fallback entries.
  S3 (entry)    a jump may only enter a fallback at its first step (预案入口),
                never mid-array (would skip the opening signal/action).
  S4 (unique)   step ids must be unique across steps / fallbacks / loop.steps.

Only full plan objects (top-level "steps" / "loop" / "fallbacks") are validated;
fragments (CommandIntent, reaction plan, script blocks) are reported as skipped.

Usage:
  python Scripts/validate_plan_json.py                  # validate the plan doc
  python Scripts/validate_plan_json.py FILE [FILE..]    # validate given files
  python Scripts/validate_plan_json.py --strict         # warnings become errors

Exit code: 0 = pass, 1 = errors (or warnings with --strict).
"""

import json
import os
import re
import sys
from collections import Counter

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(SCRIPT_DIR)
DEFAULT_DOC = os.path.join(PROJECT_ROOT, "plans", "llm-goap-plan-execution.md")

JSON_BLOCK_RE = re.compile(r"```json\s*\n(.*?)```", re.DOTALL)

STEP_JUMP_FIELDS = ("on_timeout", "on_success")


def find_json_blocks(text):
    return [m.group(1) for m in JSON_BLOCK_RE.finditer(text)]


def is_plan_object(obj):
    return isinstance(obj, dict) and any(k in obj for k in ("steps", "loop", "fallbacks"))


def iter_steps(obj):
    """Yield all step dicts from steps / fallbacks / loop.steps."""
    for s in obj.get("steps", []) or []:
        if isinstance(s, dict):
            yield s
    for entry in obj.get("fallbacks", []) or []:
        for s in entry:
            if isinstance(s, dict):
                yield s
    loop = obj.get("loop")
    if isinstance(loop, dict):
        for s in loop.get("steps", []) or []:
            if isinstance(s, dict):
                yield s


def collect_step_jump_targets(step):
    """All ids this step jumps to (on_timeout/on_success/on_event/result/time_since)."""
    refs = []
    for field in STEP_JUMP_FIELDS:
        v = step.get(field)
        if isinstance(v, str):
            refs.append(v)
    ev = step.get("on_event")
    if isinstance(ev, dict):
        ev = [ev]
    if isinstance(ev, list):
        for e in ev:
            if isinstance(e, dict) and isinstance(e.get("then"), str):
                refs.append(e["then"])
    result = step.get("result")
    if isinstance(result, dict):
        for v in result.values():
            if isinstance(v, str):
                refs.append(v)

    def walk_conditions(c):
        if isinstance(c, dict):
            if isinstance(c.get("step_id"), str):  # time_since(step_id: ...)
                refs.append(c["step_id"])
            for v in c.values():
                walk_conditions(v)
        elif isinstance(c, list):
            for v in c:
                walk_conditions(v)

    until = step.get("until")
    if until is not None:
        walk_conditions(until)
    return refs


def collect_contingency_jump_targets(obj):
    refs = []
    for c in obj.get("contingencies", []) or []:
        v = c.get("then")
        if isinstance(v, str):
            refs.append(v)
    return refs


def validate_plan(obj, name):
    errors = []
    warnings = []

    step_ids = [s.get("id") for s in iter_steps(obj) if isinstance(s, dict) and isinstance(s.get("id"), str)]
    ids_set = set(step_ids)

    # S4: id uniqueness
    for dup, count in sorted((k, v) for k, v in Counter(step_ids).items() if v > 1):
        errors.append(f"S4 duplicate step id: {dup!r} x{count}")

    # collect all jump targets (steps + contingencies)
    all_jumps = []
    for s in iter_steps(obj):
        all_jumps.extend(collect_step_jump_targets(s))
    all_jumps.extend(collect_contingency_jump_targets(obj))

    # S1: forward -- every target exists or is @-reserved
    for t in all_jumps:
        if t.startswith("@"):
            continue
        if t not in ids_set:
            errors.append(f"S1 dangling jump target: {t!r}")

    # fallback entry map: id -> (entry_idx, is_first_step)
    fb_map = {}
    for i, entry in enumerate(obj.get("fallbacks", []) or []):
        for j, s in enumerate(entry):
            if isinstance(s, dict) and isinstance(s.get("id"), str):
                fb_map[s["id"]] = (i, j == 0)

    # S3: jumps may only enter a fallback at its first step
    for t in all_jumps:
        if t in fb_map and not fb_map[t][1]:
            errors.append(f"S3 jump into mid-fallback: {t!r} (fallbacks[{fb_map[t][0]}], not entry step)")

    # S2: reverse -- every fallback entry's first id must have a jump source
    referenced = {t for t in all_jumps if not t.startswith("@")}
    for i, entry in enumerate(obj.get("fallbacks", []) or []):
        if not entry:
            warnings.append(f"fallbacks[{i}] is empty")
            continue
        first = entry[0].get("id") if isinstance(entry[0], dict) else None
        if not first:
            errors.append(f"S2 fallbacks[{i}][0] has no id")
        elif first not in referenced:
            errors.append(f"S2 dead fallback entry: fallbacks[{i}][0]={first!r} has no jump source")

    return errors, warnings


def validate_file(path, strict=False):
    if path.lower().endswith(".json"):
        # 纯 JSON 文件：整文件即一个 plan 对象
        with open(path, encoding="utf-8") as f:
            text = f.read()
        try:
            obj = json.loads(text)
        except json.JSONDecodeError as e:
            print(f"[{os.path.basename(path)}] invalid JSON: {e.msg} @ {e.lineno}")
            return 1
        if not is_plan_object(obj):
            print(f"[{os.path.basename(path)}] skipped (not a plan object)")
            return 0
        name = obj.get("intent", {}).get("intent_type") if isinstance(obj.get("intent"), dict) else None
        label = os.path.basename(path) if not name else f"{os.path.basename(path)} ({name})"
        errors, warnings = validate_plan(obj, name)
        status = "PASS" if not errors else "FAIL"
        suffix = f" ({len(errors)} error(s), {len(warnings)} warning(s))" if errors or warnings else ""
        print(f"[{os.path.basename(path)}] {label}: {status}{suffix}")
        for w in warnings:
            print(f"  WARN {w} (warning)")
        for e in errors:
            print(f"  ERROR {e}")
        return 1 if (errors or (strict and warnings)) else 0

    with open(path, encoding="utf-8") as f:
        text = f.read()
    blocks = find_json_blocks(text)
    if not blocks:
        print(f"[{os.path.basename(path)}] no ```json blocks found")
        return 0

    all_errors, all_warnings = [], []
    plan_count = 0
    for idx, block in enumerate(blocks, 1):
        try:
            obj = json.loads(block)
        except json.JSONDecodeError as e:
            # fragments (bare keys / JS comments) are not plan objects -- report as skipped
            print(f"[{os.path.basename(path)}] json#{idx}: skipped (not valid JSON: {e.msg} @ {e.lineno})")
            continue
        if not is_plan_object(obj):
            print(f"[{os.path.basename(path)}] json#{idx}: skipped (fragment, not a plan object)")
            continue
        plan_count += 1
        name = obj.get("intent", {}).get("intent_type") if isinstance(obj.get("intent"), dict) else None
        label = f"json#{idx}" if not name else f"case {name}"
        errors, warnings = validate_plan(obj, name)
        status = "PASS" if not errors else "FAIL"
        suffix = f" ({len(errors)} error(s), {len(warnings)} warning(s))" if errors or warnings else ""
        print(f"[{os.path.basename(path)}] {label}: {status}{suffix}")
        all_errors.extend(f"{label}: {e}" for e in errors)
        all_warnings.extend(f"{label}: {w} (warning)" for w in warnings)

    for w in all_warnings:
        print(f"  WARN {w}")
    for e in all_errors:
        print(f"  ERROR {e}")

    n_fail = 1 if (all_errors or (strict and all_warnings)) else 0
    print(f"[{os.path.basename(path)}] {plan_count} plan(s) checked, "
          f"{len(all_errors)} error(s), {len(all_warnings)} warning(s)")
    return n_fail


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    strict = "--strict" in sys.argv[1:]
    files = args or [DEFAULT_DOC]
    if not files:
        print("no files specified", file=sys.stderr)
        return 2
    exit_code = 0
    for path in files:
        exit_code |= validate_file(path, strict=strict)
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
