# -*- coding: utf-8 -*-
"""按「说话人气质 + 这句话的语气」给每句台词挑一个**原版真能播的**动作。

🔴 铁律：只从 `as_human_warrior` 里挑（`monsters.xml` 写明 `human` 的默认 action_set 就是它）。
   别的合集（`as_human_hideout_bandit` / `as_human_musician` …）里的动作，我们的 NPC **播不出来**。

用法：
    python Scripts/gen_line_actions.py            # 打印预览
    python Scripts/gen_line_actions.py --write    # 写 对话节点的 NpcActions
"""
import importlib.util
import io
import json
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OFFLINE = os.path.join(ROOT, "Debug", "offline")
OUT = os.path.join(ROOT, "ModuleData", "DesignData", "对话节点的 NpcActions")
ACTSETS = os.path.join(os.path.dirname(ROOT), "Native", "ModuleData", "action_sets.xml")

# ── 每个 NPC 的身体语言底色 ──
STYLE = {
    "老周": "closed",        # 守柜台的人：抱臂、盘算
    "寡妇": "hip",           # 戏班领班：叉腰、带派
    "小贼": "confident",     # 半大孩子：话快、爱吹
    "屠户": "closed",        # 话少、戒备
    "铁头": "aggressive",    # 看门的：先凶
    "打手": "aggressive",
    "收数人": "closed",
    "对台领班": "hip",
    "野贼": "aggressive",
    "老囚": "closed",
}

# ── 语气关键词 → 表情档（只用多字词，单字容易误命中：如「通行文书」里的「行」）──
TONE = [
    (("滚", "找死", "动手", "杀了", "见血", "亮刀", "威胁", "退后", "拿下", "站住", "别逼"), "very_negative"),
    (("爽快", "谢谢", "成交", "值这个", "应该的", "好说"), "positive"),
    (("不知道", "算了", "不卖", "没拿", "别问", "……"), "negative"),
]

# 表情档 → 该风格下可用的动作名（都用 as_human_warrior 里确实存在的）
def act_for(style, emotion):
    return "act_conversation_%s_%s" % (style, emotion)


def pick_style(name):
    return STYLE.get(name)


def pick_emotion(text):
    for words, emo in TONE:
        for w in words:
            if w in text:
                return emo
    if "？" in text or "?" in text:
        return "trivial"
    return "trivial"


def load_texts():
    """🔴 从**语言 XML**读（运行时真源）：节点拆分后行数会变，源文件的旧文案不再准。"""
    path = os.path.join(ROOT, "ModuleData", "Languages", "CNs", "std_LivingWorldNpcs_strings.xml")
    raw = io.open(path, encoding="utf-8").read()
    pairs = re.findall(r'<string id="([^"]+)" text="((?:[^"]|"")*)"', raw)
    d = {}
    for k, v in pairs:
        if k.startswith("LWN_story_"):
            d[k] = (v.replace("&#10;", chr(10)), "")
    return d


def human_actions():
    s = io.open(ACTSETS, encoding="utf-8-sig", errors="ignore").read()
    blk = re.split(r'<action_set\s+id="', s)[1]
    return set(re.findall(r'<action\s+type="(act_[a-z0-9_]+)"', blk))


def main():
    write = "--write" in sys.argv
    texts = load_texts()
    pool = human_actions()

    out = {}
    unknown = []
    for key, val in texts.items():
        if re.search(r"_opt_|_entry$|_goal_|_ability_|_check_|_err_|_menu_|_btn_|_title$|_faction_|_name_", key):
            continue
        cn = val[0]
        acts = []
        for line in cn.split(chr(10)):
            line = line.strip()
            if not line:
                continue
            m = re.match(r"^（([^，]+)，[^）]+）：(.+)$", line)
            if not m:
                acts.append("")
                continue
            name, body = m.group(1), m.group(2)
            st = pick_style(name)
            if not st:                      # 玩家 / 旁白：不配动作
                acts.append("")
                continue
            a = act_for(st, pick_emotion(body))
            if a not in pool:
                unknown.append((key, a))
                a = "act_conversation_talk_explain"   # 兜底：万能"解释"手势
            acts.append(a)
        if any(acts):
            out[key] = acts

    print("键数", len(out), " 总行数", sum(len(v) for v in out.values()))
    if unknown:
        print("⚠ 不在 as_human_warrior 里的组合:", sorted(set(a for _, a in unknown))[:8])

    sample = ["LWN_story_pawnbroker_intro", "LWN_story_blocker_first", "LWN_story_event1_join"]
    for k in sample:
        if k in out:
            print("###", k, "->", out[k])

    if write:
        # 写进**对话节点自己身上**（Dialogues/story_probe_*.json 的 NpcActions），不再单独成表
        import glob
        n = 0
        for f in sorted(glob.glob(os.path.join(ROOT, "ModuleData", "DesignData", "Dialogues", "story_probe_*.json"))):
            d = json.load(io.open(f, encoding="utf-8"))
            changed = False
            for node in d.get("Nodes", []):
                k = node.get("NpcKey")
                if k and k in out:
                    node["NpcActions"] = out[k]
                    changed = True
                    n += 1
            if changed:
                json.dump(d, io.open(f, "w", encoding="utf-8", newline=chr(10)), ensure_ascii=False, indent=2)
        print("写入节点 NpcActions:", n, "条")


if __name__ == "__main__":
    main()
