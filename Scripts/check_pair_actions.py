#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""check_pair_actions.py —— 双人动作设计表体检（2026-10-19 精简版：15 列）。

检查对象：`ModuleData/DesignData/PairActions.csv`（四行头，与 Camera.csv 同格式）。
检查项（都是"写错会静默失效/运行时才炸"的那类）：
  · 表头列数 = 类型列数；数据行字段数 = 表头列数（多出来的 = 值里有半角逗号，静默裂列）
  · ID 非空且唯一
  · Start / Attach_Mode / Motion / End_Policy 取值合法
  · `Attach_Mode=persistent ⇒ Motion=none`（挂接绝不走引擎位移）
  · `HandoffTo` / `HandoffOn` 必须成对：目标 ID 必须存在，触发名不能空
  · 同一组 `machine:攻击方/防守方` 只能出现一次（适配器按状态机名找行）
  · `Start=handoff` 必须有 `EnterEvent`；`machine:` 驱动必须同时有 Enter/ExitEvent
  · `Attacker_Play` / `Defender_Play` = 空 / `action:动作名` / `machine:状态机名`

用法：
    python Scripts/check_pair_actions.py
    python Scripts/check_pair_actions.py --selftest   # 负面测试：坏行必须被抓到
退出码：0 = 绿；1 = 有错。
"""
import argparse
import csv
import io
import os
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CSV_PATH = os.path.join(REPO, "ModuleData", "DesignData", "PairActions.csv")

REQUIRED = [
    "ID", "Start",
    "Stance_Right", "Stance_Forward", "Stance_Direction",
    "Attacker_Play", "Defender_Play",
    "EnterEvent", "ExitEvent",
    "Motion", "Attach_Mode", "End_Policy", "HandoffTo", "HandoffOn",
]

VALID = {
    "Start": {"preposition", "handoff"},
    "Attach_Mode": {"none", "persistent"},
    "Motion": {"engine", "clip", "none", ""},
    "End_Policy": {"release", "kill_defender", "ragdoll_defender", "hold"},
}


def load(path):
    if not os.path.exists(path):
        return None, None, None, None
    with io.open(path, encoding="utf-8-sig", newline="") as f:
        rows = list(csv.reader(f))
    if len(rows) < 4:
        return rows[0] if rows else [], rows[1] if len(rows) > 1 else [], [], []
    return rows[0], rows[1], rows[2], rows[3:]


def check_play(value, pid, col, errors):
    v = (value or "").strip()
    if not v:
        return
    if v.lower().startswith("action:"):
        if not v[7:].strip():
            errors.append("%s：%s='action:' 但动作名为空" % (pid, col))
        return
    if v.lower().startswith("machine:"):
        if not v[8:].strip():
            errors.append("%s：%s='machine:' 但状态机名为空" % (pid, col))
        return
    errors.append("%s：%s='%s'（合法值：空 / action:动作名 / machine:状态机名）" % (pid, col, v))


def validate(keys, data):
    errors, warnings = [], []
    if not keys:
        return ["表头为空"], warnings

    for req in REQUIRED:
        if req not in keys:
            errors.append("缺列 '%s'" % req)
    if errors:
        return errors, warnings

    ids = {}
    machine_pairs = {}
    for i, row in enumerate(data):
        line = i + 4
        if not row or all(not str(v).strip() for v in row):
            continue
        if len(row) != len(keys):
            errors.append("第 %d 行字段数 %d ≠ 表头 %d（值里可能有半角逗号）" % (line, len(row), len(keys)))
            continue
        d = dict(zip(keys, row))
        pid = (d.get("ID") or "").strip()
        if not pid:
            errors.append("第 %d 行 ID 为空" % line)
            continue
        if pid in ids:
            errors.append("%s：ID 重复（第 %d 行与第 %d 行）" % (pid, ids[pid], line))
        ids[pid] = line

        for field, allowed in VALID.items():
            val = (d.get(field) or "").strip().lower()
            if val not in allowed:
                errors.append("%s：%s='%s'（合法值 %s）"
                              % (pid, field, d.get(field), "/".join(sorted(allowed))))

        check_play(d.get("Attacker_Play"), pid, "Attacker_Play", errors)
        check_play(d.get("Defender_Play"), pid, "Defender_Play", errors)

        start = (d.get("Start") or "").strip().lower()
        if start == "handoff" and not (d.get("EnterEvent") or "").strip():
            errors.append("%s：Start=handoff 必须有 EnterEvent（交接时 Force 给 defender）" % pid)

        a_play = (d.get("Attacker_Play") or "").strip().lower()
        d_play = (d.get("Defender_Play") or "").strip().lower()
        if a_play.startswith("machine:") and d_play.startswith("machine:"):
            key = a_play + "/" + d_play
            machine_pairs[key] = machine_pairs.get(key, 0) + 1
        if "machine:" in a_play or "machine:" in d_play:
            if not (d.get("EnterEvent") or "").strip() or not (d.get("ExitEvent") or "").strip():
                errors.append("%s：machine 驱动必须同时有 EnterEvent / ExitEvent" % pid)

        if (d.get("Attach_Mode") or "").strip().lower() == "persistent":
            motion = (d.get("Motion") or "").strip().lower()
            if motion != "none":
                errors.append("%s：Attach_Mode=persistent 时 Motion 必须是 none（现在 %s）"
                              % (pid, motion or "空"))

        handoff_to = (d.get("HandoffTo") or "").strip()
        handoff_on = (d.get("HandoffOn") or "").strip()
        if handoff_to and not handoff_on:
            errors.append("%s：HandoffTo 非空时必须写 HandoffOn（交接触发动作名）" % pid)
        if handoff_on and not handoff_to:
            errors.append("%s：HandoffOn 非空时必须写 HandoffTo（后继 pair id）" % pid)

    for key, n in machine_pairs.items():
        if n > 1:
            errors.append("状态机组 '%s' 出现 %d 次（FindByMachines 只取第一条，必须唯一）" % (key, n))

    # 交接目标必须存在（第二遍，等 ids 收齐）
    for i, row in enumerate(data):
        if not row or len(row) != len(keys):
            continue
        d = dict(zip(keys, row))
        pid = (d.get("ID") or "").strip()
        target = (d.get("HandoffTo") or "").strip()
        if target and target not in ids:
            errors.append("%s：HandoffTo='%s' 在表里不存在" % (pid, target))

    return errors, warnings


def selftest():
    keys = list(REQUIRED)
    good = {k: "" for k in keys}
    good.update({
        "ID": "selftest_a", "Start": "preposition",
        "Attacker_Play": "action:act_x", "Defender_Play": "action:act_y",
        "Motion": "engine", "Attach_Mode": "persistent",
        "End_Policy": "release",
    })
    rows = [[good[k] for k in keys]]
    errors, _warnings = validate(keys, rows)
    ok = any("persistent" in e or "Motion" in e for e in errors)
    print("selftest:", "OK（persistent+engine 被抓到）" if ok else "FAIL（坏行没被抓到）")
    if not ok:
        print("  errors:", errors)
    return 0 if ok else 1


def main():
    ap = argparse.ArgumentParser(description="PairActions.csv 体检")
    ap.add_argument("--selftest", action="store_true", help="负面测试（坏行必须被抓到）")
    args = ap.parse_args()

    if args.selftest:
        return selftest()

    keys, types, labels, data = load(CSV_PATH)
    if keys is None:
        print("[pair] FAIL 找不到 %s" % CSV_PATH)
        return 1
    if len(keys) != len(types):
        print("[pair] FAIL 表头列数 %d ≠ 类型列数 %d" % (len(keys), len(types)))
        return 1

    errors, warnings = validate(keys, data)
    if errors:
        for e in errors:
            print("[pair] FAIL", e)
    for w in warnings:
        print("[pair] WARN", w)
    if errors:
        return 1
    print("[pair] OK %d 行 / %d 列" % (len([r for r in data if r and any(str(v).strip() for v in r)]), len(keys)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
