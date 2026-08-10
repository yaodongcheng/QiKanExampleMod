#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
IM 群聊语义检索回归测试（Q4：脱离游戏验证 ImTopicMatcher 准确性）。

- 词表从 ImTopicMatcher.cs 解析（单一事实源 = C# 文件，防止 py/C# 词表漂移——check_vocab_sync 同思路）；
- 打分逻辑按 C# 复刻（Affinity：职业命中 2.0 / 未命中 0.5；抖动 C# 用 MBRandom.RandomFloat*2，这里注入固定值做确定性断言）；
- 覆盖：主题匹配 / 挑人正确性 / default 兜底 / 跟随概率统计 / 词表结构（中英双语、数量）。

用法：python Scripts/test_im_topics.py
"""
import io
import os
import random
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CS_PATH = os.path.join(ROOT, "ExampleModVS/ExampleMod/ExampleMod/ImChat/ImTopicMatcher.cs")

CJK = re.compile(r"[一-鿿]")
DEFAULT_TOPIC = "default"
FOLLOWUP_CHANCE = 0.1  # 与 Settings.ImGroupFollowUpChance 默认值同步


def load_topics():
    """从 C# 文件解析 TopicKeywords 与 OccupationTopics（与 C# 单一事实源同步）。"""
    with io.open(CS_PATH, encoding="utf-8") as f:
        src = f.read()

    topics = {}
    # 主题键 = ASCII 小写（[a-z]+）；职业键 = 中文开头（\w 在 Python 会匹配中文，必须分开）
    for m in re.finditer(r'\["([a-z]+)"\]\s*=\s*new\[\]\s*\{\s*(.*?)\s*\}', src):
        topics[m.group(1)] = re.findall(r'"([^"]*)"', m.group(2))

    occupations = {}
    for m in re.finditer(r'\["([一-鿿][^"]*)"\]\s*=\s*new\[\]\s*\{\s*(.*?)\s*\}', src):
        occupations[m.group(1)] = re.findall(r'"([^"]*)"', m.group(2))

    return topics, occupations


# ── 打分逻辑复刻（与 C# ImTopicMatcher 一致）──

def match_topics(text, topics):
    if not text or not text.strip():
        return []
    result = []
    for topic, kws in topics.items():
        for kw in kws:  # C#: 每主题内层关键词命中即 break 内层，继续下一个主题（可多主题命中）
            if kw.lower() in text.lower():
                result.append(topic)
                break
    if not result:
        result.append(DEFAULT_TOPIC)
    return result


def affinity(occupation, topic, occupations):
    if not occupation:
        return 0.5
    if topic in occupations.get(occupation, []):
        return 2.0
    return 0.5


def score(occupation, topics, occupations, heat_bonus, jitter):
    return sum(affinity(occupation, t, occupations) for t in topics) + heat_bonus + jitter


def pick_repliers(occupations_list, text, topics, occupations, heat_bonus_list=None, jitter=0.0, member_names=None):
    """复刻 PickRepliers：返回 (primary_index, followup_index_or_None)。
    occupations_list = 成员职业列表（顺序即成员顺序）；heat_bonus_list = 每成员热度加成（C# ReplyBonus 逐 Hero）；
    member_names = 每成员名字（@提及优先：玩家点名 → +5 必回）；jitter 注入固定值做确定性断言。"""
    ts = match_topics(text, topics)
    scored = []
    for i, occ in enumerate(occupations_list):
        if occ is None:
            continue
        bonus = heat_bonus_list[i] if heat_bonus_list else 0.0
        s = score(occ, ts, occupations, bonus, jitter)
        # @提及优先（C# ImTopicMatcher：文本含成员名 → +5）
        name = member_names[i] if member_names else None
        if name and name.lower() in text.lower():
            s += 5.0
        scored.append((i, s))
    if not scored:
        return (None, None)
    scored.sort(key=lambda x: -x[1])
    primary = scored[0][0]
    followup = None
    if len(scored) >= 2 and random.random() < FOLLOWUP_CHANCE:
        followup = scored[1][0]
    return (primary, followup)


# ── 测试 ──

PASS = 0
FAIL = 0


def check(name, cond, detail=""):
    global PASS, FAIL
    if cond:
        PASS += 1
        print(f"  [PASS] {name}")
    else:
        FAIL += 1
        print(f"  [FAIL] {name} {detail}")


def main():
    topics, occupations = load_topics()
    print(f"=== 词表同步（从 ImTopicMatcher.cs 解析）===")
    print(f"  主题数: {len(topics)}  {sorted(topics)}")
    print(f"  职业表: {sorted(occupations)}")

    # 1. 词表结构：每个主题有中英双语关键词且数量 ≥ 3
    print("\n--- 1. 词表结构 ---")
    for t, kws in topics.items():
        cn = sum(1 for k in kws if CJK.search(k))
        en = sum(1 for k in kws if not CJK.search(k))
        check(f"主题[{t}] 中英双语（cn={cn}, en={en}, total={len(kws)}）",
              cn >= 2 and en >= 2 and len(kws) >= 4, kws)

    # 2. 主题匹配正确性（C# contains 子串命中，大小写不敏感）
    print("\n--- 2. 主题匹配 ---")
    check("「粮食收成怎么样」→ food", "food" in match_topics("粮食收成怎么样", topics))
    check("「敌军来犯，准备迎战」→ combat", "combat" in match_topics("敌军来犯，准备迎战", topics))
    check("「商队最近在跑哪条线」→ trade", "trade" in match_topics("商队最近在跑哪条线", topics))
    check("「听说城里出了贼」→ news", "news" in match_topics("听说城里出了贼", topics))
    check("「hello there」→ greeting", "greeting" in match_topics("hello there", topics))
    check("「HARVEST is coming」→ food（大小写不敏感）", "food" in match_topics("HARVEST is coming", topics))
    check("「今天天气不错」→ default 兜底", match_topics("今天天气不错", topics) == [DEFAULT_TOPIC])
    # 防误报：「家」单字已从 family 词表移除（审查修复）——「回家」才是 family
    check("「家在城里」不再误报 family（单字「家」已移除）",
          "family" not in match_topics("家在城里", topics))
    check("「回家看看」→ family", "family" in match_topics("回家看看", topics))

    # 3. 挑人正确性（职业亲和 + 确定性抖动=0）
    print("\n--- 3. 挑人（确定性断言，jitter=0）---")
    p, f = pick_repliers(["商人", "足轻"], "粮食价格怎么样", topics, occupations, jitter=0.0)
    check("「粮食价格」→ 商人（trade/food 命中，商人亲和高）", p == 0, f"got {p}")
    p, f = pick_repliers(["商人", "足轻"], "敌军来犯，准备迎战", topics, occupations, jitter=0.0)
    check("「敌军来犯」→ 足轻（combat 亲和 2.0）", p == 1, f"got {p}")
    p, f = pick_repliers(["村民", "商人", "足轻"], "麦子熟了，收成不错", topics, occupations, jitter=0.0)
    check("「麦子收成」→ 村民（food 亲和 2.0 > 商人 0.5）", p == 0, f"got {p}")
    # 热度加成（逐 Hero，C# ReplyBonus 语义）：商人 +3 热度 > 足轻 combat 2.0
    p, f = pick_repliers(["商人", "足轻"], "敌军来犯", topics, occupations, heat_bonus_list=[3.0, 0.0], jitter=0.0)
    check("热度加成（商人+3 热度 > 足轻 combat 2.0）", p == 0, f"got {p}")
    # @提及优先（微信群聊语义）：文本含成员名 → 该人必回（+5 压过职业亲和）
    p, f = pick_repliers(["足轻", "商人"], "张三，你来说说粮食的事", topics, occupations,
                         member_names=["张三", "李四"], jitter=0.0)
    check("@提及「张三」→ 足轻张三必回（+5 压过商人 trade 亲和）", p == 0, f"got {p}")
    # 无命中平局：default 双 0.5 → 抖动决定
    p, f = pick_repliers(["商人", "足轻"], "今天天气不错", topics, occupations, jitter=0.1)
    check("「天气不错」平局 → 抖动 0.1 偏向商人（同分取先）", p == 0, f"got {p}")
    # 成员为空
    p, f = pick_repliers([], "随便说点什么", topics, occupations)
    check("空成员 → (None, None)", p is None and f is None)

    # 4. 跟随概率统计（10% 概率，模拟 20000 次，允许 ±1.5% 波动）
    print("\n--- 4. 跟随概率（ImGroupFollowUpChance=0.1）---")
    random.seed(20260809)
    followups = 0
    trials = 20000
    for _ in range(trials):
        p, f = pick_repliers(["商人", "足轻", "村民"], "粮食价格", topics, occupations)
        if f is not None:
            followups += 1
    rate = followups / trials
    check(f"跟随概率 ≈ 0.10（实测 {rate:.4f}）", abs(rate - 0.1) < 0.015)

    print(f"\n=== 结果: {PASS} PASS / {FAIL} FAIL ===")
    sys.exit(1 if FAIL else 0)


if __name__ == "__main__":
    main()
