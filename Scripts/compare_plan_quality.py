#!/usr/bin/env python3
"""compare_plan_quality.py -- PlanExamples vs LLM 输出 5 维质量评分对比

维度（各 0-10，总分 = 0.25A + 0.20B + 0.20C + 0.20D + 0.15E）：
  A 命令理解准确度：LLM = 分类正确率×10（从测试结果 ok 字段统计）；PlanExamples = 10（case 设计意图的权威实现）
  B 可执行性：主链收尾正确 / 步骤 timeout 齐全 / say_to 有 text / say_to 前有 move_to（接近步）/ 跳转目标全部存在
  C 分支完备性：fallbacks ≥2 / contingencies ≥2 / 带 goal / 预案 end_plan 收尾 / 失败出口覆盖
  D 对白合理性：有 say_to 互动 / 台词长度合适（10-50 字）/ 台词多样性 / 无 AI 腔黑名单 / 有口语词（您/吧/呢/啊）
  E 游戏乐趣性：signal_player / report / query refs（lure_spot 等动态找点）/ until·on_event 条件结构 / 社交·表演动作（ask/make_noise/steal_attempt/knockout）

用法：
  python Scripts/compare_plan_quality.py                  # PlanExamples vs llm_samples_v4
  python Scripts/compare_plan_quality.py --dir DIR        # 指定 LLM 输出目录
  python Scripts/compare_plan_quality.py --one FILE       # 单份打分
退出码：0。
"""

import glob
import json
import os
import sys

# ═══════════════════════════════════════════════════════════
# 维度 A：命令理解（LLM 侧由测试脚本统计，这里只接收）
# ═══════════════════════════════════════════════════════════

def intent_ok_rate(plan_docs):
    """LLM 输出目录里 ok 字段的通过率（CUSTOM/澄清/计划都算分类层面）。"""
    if not plan_docs:
        return 0.0
    return sum(1 for d in plan_docs if d.get("ok")) / len(plan_docs)

# ═══════════════════════════════════════════════════════════
# 维度 B：可执行性（0-10）
# ═══════════════════════════════════════════════════════════

AI_PHRASES = ["执行任务", "任务完成", "收到", "好的，我这就", "遵命", "好的主人", "明白", "照办", "接下来我会", "好的，"]

def all_steps(plan):
    """主链 + loop 段 + fallbacks + triggers 内全部步骤。"""
    steps = list(plan.get("steps") or [])
    loop = plan.get("loop")
    if isinstance(loop, dict):
        steps += loop.get("steps") or []
    for fb in (plan.get("fallbacks") or []):
        steps += fb
    for t in (plan.get("triggers") or []):
        if isinstance(t, dict) and isinstance(t.get("then"), dict):
            steps.append(t["then"])
    return steps

def score_executable(plan):
    s = 0.0
    steps = plan.get("steps") or []
    # 1. 主链收尾（2 分）
    last = steps[-1] if steps else None
    if last:
        a = last.get("action")
        if a == "end_plan" or a == "signal_player":
            s += 2
        elif a == "wait" and not last.get("seconds") and not last.get("until"):
            s += 2  # 无限保持（LOOKOUT/GUARD 合法）
        else:
            s += 0.5
    # 2. 动作步骤 timeout 齐全（2 分）
    act_need_to = {"move_to", "say_to", "steal_attempt", "knockout", "order_attack",
                   "lead", "follow", "give_item", "give_gold", "deliver_item", "negotiate", "duel"}
    n_act = 0; miss = 0
    for st in all_steps(plan):
        if st.get("action") in act_need_to:
            n_act += 1
            if not st.get("timeout_s"):
                miss += 1
    if n_act:
        s += 2 * (1 - miss / n_act)
    else:
        s += 1
    # 3. say_to 有 text（2 分）
    says = [st for st in all_steps(plan) if st.get("action") == "say_to"]
    if says:
        no_txt = sum(1 for st in says if not st.get("text"))
        s += 2 * (1 - no_txt / len(says))
    else:
        s += 0.5
    # 4. 接近步：say_to 前有 move_to（2 分）
    chain = [st.get("action") for st in steps if st.get("action")]
    ok_near = True
    for i, a in enumerate(chain):
        if a == "say_to" and (i == 0 or chain[i - 1] != "move_to"):
            ok_near = False
            break
    s += 2 if ok_near else 1
    # 5. 跳转目标存在（2 分）：ids 全集
    ids = {st.get("id") for st in all_steps(plan) if st.get("id")}
    bad = 0; total = 0
    def check_jump(j):
        nonlocal bad, total
        if not j or str(j).startswith("@"):
            return
        total += 1
        if j not in ids:
            bad += 1
    for st in all_steps(plan):
        check_jump(st.get("on_timeout")); check_jump(st.get("on_success"))
        for e in (st.get("on_event") or []):
            if isinstance(e, dict):
                check_jump(e.get("then"))
        res = st.get("result")
        if isinstance(res, dict):
            for v in res.values():
                check_jump(v)
    for c in (plan.get("contingencies") or []):
        if isinstance(c, dict):
            check_jump(c.get("then"))
    s += 2 * (1 - bad / max(total, 1)) if total else 2
    return round(min(s, 10), 1)

# ═══════════════════════════════════════════════════════════
# 维度 C：分支完备性（0-10）
# ═══════════════════════════════════════════════════════════

def score_branches(plan):
    s = 0.0
    fbs = plan.get("fallbacks") or []
    cons = plan.get("contingencies") or []
    # 1. fallbacks ≥ 2（3 分）
    s += min(3, 1.5 * len(fbs))
    # 2. contingencies ≥ 2（3 分）
    s += min(3, 1.5 * len(cons))
    # 3. 带 goal（2 分）
    s += 2 if plan.get("goal") else 0
    # 4. 预案 end_plan 收尾（1 分）
    if fbs:
        ok = sum(1 for fb in fbs if fb and fb[-1].get("action") == "end_plan")
        s += ok / len(fbs)
    # 5. 失败出口覆盖（1 分）：有 until/等待语义的步骤带 on_timeout/on_event 比例
    steps = plan.get("steps") or []
    waits = [st for st in steps if st.get("until") or st.get("action") == "wait"
             or st.get("action") == "move_to"]
    if waits:
        cov = sum(1 for st in waits if st.get("on_timeout") or st.get("on_event")
                  or st.get("until") and st.get("action") != "wait")
        s += cov / len(waits)
    return round(min(s, 10), 1)

# ═══════════════════════════════════════════════════════════
# 维度 D：对白合理性（0-10）
# ═══════════════════════════════════════════════════════════

def score_dialogue(plan):
    s = 0.0
    says = [st for st in all_steps(plan) if st.get("action") == "say_to" and st.get("text")]
    if not says:
        return 1.0  # 无对白计划（纯战斗/执行）：不扣死但不鼓励
    # 1. 有 say_to 互动（2 分）
    s += 2
    # 2. 台词长度 10-50 字（2 分）
    lens = [len(st["text"]) for st in says]
    avg = sum(lens) / len(lens)
    s += 2 * (1 - min(abs(avg - 30) / 30, 1))
    # 3. 台词多样性（2 分）
    uniq = len({st["text"] for st in says})
    s += 2 * min(uniq / max(len(says), 1), 1)
    # 4. 无 AI 腔（2 分）
    ai_hits = sum(1 for st in says for w in AI_PHRASES if w in st["text"])
    s += max(0, 2 - 0.4 * ai_hits)
    # 5. 口语词（2 分）
    cw = sum(1 for st in says if any(ch in st["text"] for ch in "您吧呢啊呀了"))
    s += 2 * cw / len(says)
    return round(min(s, 10), 1)

# ═══════════════════════════════════════════════════════════
# 维度 E：游戏乐趣性（0-10）
# ═══════════════════════════════════════════════════════════

QUERIES = ("lure_spot", "hidden_spot", "stand_spot", "nearest_enemy", "all_in",
           "zone(", "point(", "query")
SOCIAL_ACTS = ("ask", "make_noise", "steal_attempt", "knockout", "negotiate", "duel",
               "lead", "shadow", "follow")

def has_target_ref(st):
    t = st.get("target")
    if isinstance(t, dict) and t.get("query"):
        return True
    return isinstance(t, str) and any(q in t for q in QUERIES)

def score_fun(plan):
    s = 0.0
    steps = plan.get("steps") or []
    fbs = plan.get("fallbacks") or []
    allst = all_steps(plan)
    # 1. signal_player（与玩家即时互动）（2 分）
    s += 2 if any(st.get("action") == "signal_player" for st in allst) else 0
    # 2. report（收尾当面报告，叙事收口）（2 分）
    reps = [st for st in allst if st.get("action") == "end_plan" and st.get("report")]
    s += 2 if reps else 0
    # 3. query refs 动态找点（空间玩法）（2 分）
    s += 2 if any(has_target_ref(st) for st in allst) else 0
    # 4. until/on_event 条件结构（剧情等待：等窗口/等事件）（2 分）
    conds = sum(1 for st in allst if st.get("until") or st.get("when") or st.get("on_event"))
    s += min(2, 0.5 * conds)
    # 5. 社交/表演动作（有戏的动作，非纯走路说话）（2 分）
    soc = sum(1 for st in allst if st.get("action") in SOCIAL_ACTS or st.get("ask"))
    s += min(2, 0.5 * soc)
    # 6. 步骤节奏 4-8 步（加分项用 0.5 封顶）
    n = len(steps)
    if 4 <= n <= 8:
        s += 0.5
    return round(min(s, 10), 1)

# ═══════════════════════════════════════════════════════════
# 汇总
# ═══════════════════════════════════════════════════════════

def score_plan(plan):
    return {
        "B_exec": score_executable(plan),
        "C_branch": score_branches(plan),
        "D_dialogue": score_dialogue(plan),
        "E_fun": score_fun(plan),
    }

def total(scores, a):
    return round(0.25 * a + 0.20 * scores["B_exec"] + 0.20 * scores["C_branch"]
                 + 0.20 * scores["D_dialogue"] + 0.15 * scores["E_fun"], 2)

def load_llm_docs(directory):
    """v4 格式：{ok, output_json:{intent, plan}, validator_issues}。"""
    docs = []
    for f in sorted(glob.glob(os.path.join(directory, "*.json"))):
        d = json.load(open(f, encoding="utf-8"))
        docs.append(d)
    return docs

def plan_of(doc):
    """v4 壳 → plan dict；CUSTOM/澄清无计划返回 None。"""
    if "output_json" in doc:
        pl = doc["output_json"].get("plan") or {}
        return pl if pl else None
    return doc  # PlanExamples 裸 plan

def main():
    args = sys.argv[1:]
    if "--one" in args:
        f = args[args.index("--one") + 1]
        plan = json.load(open(f, encoding="utf-8-sig"))
        sc = score_plan(plan)
        print(f"{os.path.basename(f)}: B={sc['B_exec']} C={sc['C_branch']} "
              f"D={sc['D_dialogue']} E={sc['E_fun']} 总分(去A)={total(sc, 10)}")
        return 0

    llm_dir = args[args.index("--dir") + 1] if "--dir" in args else "Debug/llm_samples_v4"
    ex_plans = [json.load(open(f, encoding="utf-8-sig"))
                for f in sorted(glob.glob("Debug/PlanExamples/*.json"))]
    llm_docs = load_llm_docs(llm_dir)
    llm_plans = [plan_of(d) for d in llm_docs]
    a_rate = intent_ok_rate(llm_docs)

    def agg(plans, key):
        vs = [p[key] for p in plans if p]
        return round(sum(vs) / len(vs), 2) if vs else 0

    ex_sc = [score_plan(p) for p in ex_plans]
    llm_sc = [score_plan(p) for p in llm_plans if p]
    ex_a = 10.0
    llm_a = round(a_rate * 10, 2)

    rows = [
        ("A 命令理解", ex_a, llm_a, None),
        ("B 可执行性", agg(ex_sc, "B_exec"), agg(llm_sc, "B_exec"),
         agg(ex_sc, "B_exec") - agg(llm_sc, "B_exec")),
        ("C 分支完备", agg(ex_sc, "C_branch"), agg(llm_sc, "C_branch"),
         agg(ex_sc, "C_branch") - agg(llm_sc, "C_branch")),
        ("D 对白合理", agg(ex_sc, "D_dialogue"), agg(llm_sc, "D_dialogue"),
         agg(ex_sc, "D_dialogue") - agg(llm_sc, "D_dialogue")),
        ("E 游戏乐趣", agg(ex_sc, "E_fun"), agg(llm_sc, "E_fun"),
         agg(ex_sc, "E_fun") - agg(llm_sc, "E_fun")),
    ]
    ex_t = round(0.25 * ex_a + 0.2 * sum(rows[i][1] for i in (1, 2, 3)) + 0.15 * rows[4][1], 2)
    llm_t = round(0.25 * llm_a + 0.2 * sum(rows[i][2] for i in (1, 2, 3)) + 0.15 * rows[4][2], 2)

    print(f"对比: PlanExamples (n={len(ex_plans)}) vs {llm_dir} (n={len(llm_plans)} 份有计划的输出, "
          f"分类正确率 {a_rate*100:.0f}%)")
    print("-" * 58)
    for name, e, l, diff in rows:
        mark = "  " if diff is None else ("  LLM胜" if diff < 0 else "  LLM落后" if diff > 0 else "  持平")
        print(f"{name:<10} PlanExamples={e:<6} LLM={l:<6} 差={diff if diff is not None else '—'}{mark}")
    print(f"{'总分':<10} PlanExamples={ex_t:<6} LLM={llm_t:<6} "
          f"差={round(ex_t - llm_t, 2)}  {'LLM胜' if ex_t < llm_t else 'LLM落后'}")
    print("-" * 58)
    # 各维度 LLM 最弱 5 份
    print("\nLLM 各维度最弱输出（按总分升序前 6）：")
    tagged = []
    for d, p in zip(llm_docs, llm_plans):
        if not p:
            continue
        sc = score_plan(p)
        it = (d.get("output_json") or d).get("intent") or {}
        tagged.append((total(sc, 10), sc, it.get("intent_type"), d.get("command") or "?"))
    for t, sc, it, cmd in sorted(tagged, key=lambda x: x[0])[:6]:
        print(f"  {t:<6} {it:<12} {cmd[:26]:<28} B={sc['B_exec']} C={sc['C_branch']} "
              f"D={sc['D_dialogue']} E={sc['E_fun']}")
    return 0

if __name__ == "__main__":
    sys.exit(main())
