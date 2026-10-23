#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
钩索冒泡台词表体检（2026-10-23 立）
========================================================================
检查对象：`ModuleData/DesignData/GrappleBarks.csv`（四行头，与 Camera.csv / PairActions.csv 同格式）
          + 两份语言层（英文层 / `CNs/` 中文层）。

判定（任一不过 = exit 1）：
  1. 表头必须是 ID / Event / Role / Relation / Occupation / Trait / Weight / TextKey 八列；
  2. Event ∈ {hit,tug,drag,carry,horse,horseoff,release}；
  3. Role ∈ {Victim,Bystander}；Relation ∈ {Any,Party,Stranger}；
  4. Occupation / Trait 是多值列（`|` 分隔），每个值必须在白名单里；
  5. Weight 必须是 ≥1 的整数；
  6. TextKey 必须 `LWN_` 开头，且**两份语言层都有**（缺中文 = 中文游戏显示英文）；
  7. 中文层那一条必须是中文（与英文不相同）；
  8. ID 不许重复；
  9. **每个「时机 × 谁」至少有一条通用行**（三个条件列全 Any）——
     否则没有专属台词的那种 NPC 会一句都不说；
  10. **长度硬上限**：中文 ≤15 字、英文 ≤10 词（用户 2026-10-23 口径：冒泡必须极短）。

负面测试：`python Scripts/check_grapple_barks.py --selftest`（故意造坏必须被抓到）。

Usage:
  python Scripts/check_grapple_barks.py                 # 默认本仓库
  python Scripts/check_grapple_barks.py --module PATH   # run_all_checks 传进来
"""
import argparse
import csv
import os
import sys
import xml.etree.ElementTree as ET

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)

EXPECTED_KEYS = ["ID", "Event", "Role", "Relation", "Occupation", "Trait", "Weight", "TextKey"]
EVENTS = {"hit", "tug", "drag", "carry", "horse", "horseoff", "release"}
ROLES = {"Victim", "Bystander"}
RELATIONS = {"Any", "Party", "Stranger"}
OCCUPATIONS = {"Any", "guard", "villager", "drunkard", "tavernkeeper", "merchant", "chief", "default"}
TRAITS = {"Any", "hot", "timid", "dutiful", "social", "greedy", "naive"}
# 用户口径（2026-10-23）：冒泡必须极短 —— 中文 ≤15 字，英文 ≤10 词
MAX_CJK = 15
MAX_EN_WORDS = 10


def read_csv(path):
    """返回 (header, rows)。CSV 无 BOM 也按 utf-8 读；数据从第 4 行起（四行头）。"""
    with open(path, "r", encoding="utf-8-sig", newline="") as f:
        raw = list(csv.reader(f))
    if not raw:
        return None, []
    header = [c.strip() for c in raw[0]]
    rows = [r for r in raw[3:] if r and any(str(c).strip() for c in r)]
    return header, rows


def read_lang(path):
    """读取 std_*.xml 的 {id: text}。根结构不同也都能取到 <string>。"""
    table = {}
    if not os.path.isfile(path):
        return table
    root = ET.parse(path).getroot()
    for node in root.iter("string"):
        key = (node.get("id") or "").strip()
        if key:
            table[key] = (node.get("text") or "").strip()
    return table


def validate(header, rows, en, cn, id_seen=None):
    """返回 (errors, warnings, 统计)。纯函数，selftest 直接喂假数据。"""
    errors = []
    warnings = []
    stats = {"rows": 0, "victim": 0, "witness": 0, "events": set(), "pairs": set(), "generic": set()}

    if header is None:
        return ["找不到/读不了台词表"], warnings, stats

    if header != EXPECTED_KEYS:
        errors.append("表头必须是 %s，实际 = %s" % (",".join(EXPECTED_KEYS), ",".join(header)))
        return errors, warnings, stats

    id_seen = id_seen if id_seen is not None else set()
    for lineno, row in enumerate(rows, start=4):
        if len(row) < len(EXPECTED_KEYS):
            errors.append("第 %d 行列数不足（%d < %d）：%s" % (lineno, len(row), len(EXPECTED_KEYS), row))
            continue
        rec = dict(zip(EXPECTED_KEYS, [str(v).strip() for v in row[:len(EXPECTED_KEYS)]]))
        rid = rec["ID"]
        if not rid:
            errors.append("第 %d 行 ID 为空" % lineno)
            continue
        if rid in id_seen:
            errors.append("第 %d 行 ID 重复：%s" % (lineno, rid))
        id_seen.add(rid)

        event = rec["Event"].lower()
        if event not in EVENTS:
            errors.append("第 %d 行 Event 非法：'%s'（只认 %s）" % (lineno, rec["Event"], "/".join(sorted(EVENTS))))
        role = rec["Role"]
        if role not in ROLES:
            errors.append("第 %d 行 Role 非法：'%s'（只认 Victim / Bystander）" % (lineno, role))

        relation = rec["Relation"] or "Any"
        if relation not in RELATIONS:
            errors.append("第 %d 行 Relation 非法：'%s'（只认 Any / Party / Stranger）" % (lineno, relation))

        occ_tokens = split_tokens(rec["Occupation"])
        for tok in occ_tokens:
            if tok not in OCCUPATIONS:
                errors.append("第 %d 行 Occupation 非法：'%s'（只认 %s）"
                              % (lineno, tok, "/".join(sorted(OCCUPATIONS))))
        trait_tokens = split_tokens(rec["Trait"])
        for tok in trait_tokens:
            if tok not in TRAITS:
                errors.append("第 %d 行 Trait 非法：'%s'（只认 %s）"
                              % (lineno, tok, "/".join(sorted(TRAITS))))

        try:
            weight = int(rec["Weight"])
            if weight < 1:
                errors.append("第 %d 行 Weight 必须 ≥1：%s" % (lineno, rec["Weight"]))
        except ValueError:
            errors.append("第 %d 行 Weight 不是整数：'%s'" % (lineno, rec["Weight"]))

        key = rec["TextKey"]
        if not key.startswith("LWN_"):
            errors.append("第 %d 行 TextKey 必须以 LWN_ 开头：'%s'" % (lineno, key))
        if key not in en:
            errors.append("第 %d 行 TextKey 英文层缺失：%s" % (lineno, key))
        if key not in cn:
            errors.append("第 %d 行 TextKey 中文层缺失：%s" % (lineno, key))
        elif key in en and cn.get(key) == en.get(key):
            errors.append("第 %d 行 中文层与英文相同（中文游戏里显示英文）：%s" % (lineno, key))
        elif key in cn and all(ord(ch) < 128 for ch in cn[key]):
            errors.append("第 %d 行 中文层不是中文：%s = '%s'" % (lineno, key, cn[key]))
        if key in cn:
            cjk = sum(1 for ch in cn[key] if ord(ch) >= 0x2E80)
            if cjk > MAX_CJK:
                errors.append("第 %d 行 中文台词太长（%d 字 > %d）：%s" % (lineno, cjk, MAX_CJK, cn[key]))
        if key in en:
            words = len(en[key].split())
            if words > MAX_EN_WORDS:
                errors.append("第 %d 行 英文台词太长（%d 词 > %d）：%s" % (lineno, words, MAX_EN_WORDS, en[key]))

        stats["rows"] += 1
        if role == "Victim":
            stats["victim"] += 1
        elif role == "Bystander":
            stats["witness"] += 1
        if event in EVENTS:
            stats["events"].add(event)
        pair = (event, role)
        stats["pairs"].add(pair)
        # 通用行 = 三个条件列全 Any —— 任何人都能说，用来给"没有专属台词的人"保底
        if relation == "Any" and not occ_tokens and not trait_tokens:
            stats["generic"].add(pair)

    # 每一类（时机 × 谁）都必须至少有一条通用行，否则某些职业/性格的 NPC 会**一句都没有**
    for pair in sorted(stats["pairs"]):
        if pair not in stats["generic"]:
            errors.append("事件 %s / %s 没有通用行（Relation/Occupation/Trait 全 Any）——"
                          "没有专属台词的 NPC 会完全不说话" % pair)

    # 有事件没人说（既没有当事人也没有旁白）= 八成是 Event 拼错了，提示但不判死
    for ev in sorted(EVENTS):
        if ev not in stats["events"]:
            warnings.append("没有任何一行覆盖事件 '%s'" % ev)

    return errors, warnings, stats


def split_tokens(value):
    """多值列：`|` 分隔；空 / Any 视为不限制。"""
    out = []
    for tok in (value or "").split("|"):
        tok = tok.strip()
        if not tok or tok.lower() == "any":
            continue
        out.append(tok)
    return out


def selftest():
    """负面测试：故意造坏数据，必须逐条抓到。"""
    header = list(EXPECTED_KEYS)
    good_en = {"LWN_x": "Hi"}
    good_cn = {"LWN_x": "喂"}

    def row(**kw):
        base = {"ID": "a", "Event": "hit", "Role": "Victim", "Relation": "Any",
                "Occupation": "Any", "Trait": "Any", "Weight": "1", "TextKey": "LWN_x"}
        base.update(kw)
        return [base[k] for k in EXPECTED_KEYS]

    cases = [
        ("unknown event", [row(Event="nope")]),
        ("unknown role", [row(Role="Npc")]),
        ("unknown relation", [row(Relation="Friend")]),
        ("unknown occupation", [row(Occupation="smith")]),
        ("unknown trait", [row(Trait="brave")]),
        ("bad weight", [row(Weight="x")]),
        ("zero weight", [row(Weight="0")]),
        ("missing en", [row(TextKey="LWN_q")]),
        ("bad prefix", [row(TextKey="X_x")]),
        ("missing generic line", [row(Relation="Party")]),
        ("duplicate id", [row(), row(TextKey="LWN_x")]),
    ]
    ok = True
    for name, rows in cases:
        cn = good_cn if all(r[7] == "LWN_x" for r in rows) else {}
        errors, _, _ = validate(header, rows, good_en, cn)
        if not errors:
            print("[bark-selftest] FAIL 没抓到：%s" % name)
            ok = False

    # 中文层缺失 / 中英同串
    errors, _, _ = validate(header, [row()], good_en, {})
    if not errors:
        print("[bark-selftest] FAIL 没抓到：missing cn")
        ok = False
    errors, _, _ = validate(header, [row()], good_en, {"LWN_x": "Hi"})
    if not errors:
        print("[bark-selftest] FAIL 没抓到：cn same as en")
        ok = False
    errors, _, _ = validate(header, [row()], good_en, {"LWN_x": "OK"})
    if not errors:
        print("[bark-selftest] FAIL 没抓到：cn ascii")
        ok = False

    # 长度上限：中文 >15 字 / 英文 >10 词 必须抓到
    errors, _, _ = validate(header, [row()], {"LWN_x": "Hi"},
                            {"LWN_x": "这是一句明显超过十五个汉字的台词内容"})
    if not errors:
        print("[bark-selftest] FAIL 没抓到：cn too long")
        ok = False
    errors, _, _ = validate(header, [row()],
                            {"LWN_x": "one two three four five six seven eight nine ten eleven"}, good_cn)
    if not errors:
        print("[bark-selftest] FAIL 没抓到：en too long")
        ok = False

    # 正向对照：好数据不许报错
    errors, _, stats = validate(header, [row()], good_en, good_cn)
    if errors:
        print("[bark-selftest] FAIL 好数据被误报：%s" % errors)
        ok = False
    print("[bark-selftest] %s" % ("OK（%d 个坏例全抓到，好数据零误报）" % (len(cases) + 5) if ok else "FAIL"))
    return 0 if ok else 1


def main():
    ap = argparse.ArgumentParser(description="GrappleBarks.csv 体检")
    ap.add_argument("--module", default=REPO, help="模块根目录（默认本仓库）")
    ap.add_argument("--selftest", action="store_true", help="负面测试（坏数据必须被抓到）")
    args = ap.parse_args()

    if args.selftest:
        return selftest()

    csv_path = os.path.join(args.module, "ModuleData", "DesignData", "GrappleBarks.csv")
    en_path = os.path.join(args.module, "ModuleData", "Languages", "std_LivingWorldNpcs_strings.xml")
    cn_path = os.path.join(args.module, "ModuleData", "Languages", "CNs", "std_LivingWorldNpcs_strings.xml")

    if not os.path.isfile(csv_path):
        print("[bark] FAIL 找不到 %s" % csv_path)
        return 1

    header, rows = read_csv(csv_path)
    en = read_lang(en_path)
    cn = read_lang(cn_path)
    if not en:
        print("[bark] FAIL 读不到英文语言层：%s" % en_path)
        return 1
    if not cn:
        print("[bark] FAIL 读不到中文语言层：%s" % cn_path)
        return 1

    errors, warnings, stats = validate(header, rows, en, cn)
    for e in errors:
        print("[bark] FAIL", e)
    for w in warnings:
        print("[bark] WARN", w)
    if errors:
        return 1
    print("[bark] OK %d 行（当事人 %d / 旁观者 %d，覆盖 %d 类事件）"
          % (stats["rows"], stats["victim"], stats["witness"], len(stats["events"])))
    return 0


if __name__ == "__main__":
    sys.exit(main())
