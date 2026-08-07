#!/usr/bin/env python3
"""test_llm_plan_stress.py -- 密谋命令压力验证（plan 文档 0.1/0.4/0.6 全谱系）

与 test_llm_plan.py 同构（读 MCM 配置 / 同款 prompt / 同款 validator），但命令集覆盖
plan 文档的 16 个玩法 case + 武侠谱系 W1-W12 + 群组谱系 Q1-Q6 的高频/边界命令，
并把每个 case 的【完整 input prompt】与【完整 output JSON】落盘到 out_dir 供人工检查。

用法：
  python Scripts/test_llm_plan_stress.py                # 跑全部压力命令
  python Scripts/test_llm_plan_stress.py --rounds 3     # 每命令跑 3 轮（看漂移）
  python Scripts/test_llm_plan_stress.py --cmd "命令"   # 单命令
  python Scripts/test_llm_plan_stress.py --out DIR      # 输出目录（默认 Debug/llm_samples）

退出码：0 = 全部通过；1 = 有失败。
"""

import glob
import json
import os
import sys
import time
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from test_llm_plan import (  # noqa: E402
    MCM_PATH, FIXED_SCENE, build_scene, build_prompt, validate_plan, load_config,
    call_llm,
)

DEFAULT_OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Debug", "llm_samples")

# ═══════════════════════════════════════════════════════════════
# 压力命令集：命令 → (期望意图 or None=看行为, 说明, 覆盖点)
# 期望标注：CLARIFY=歧义应澄清 / CUSTOM=词表外应拒绝 / 其余=意图
# ═══════════════════════════════════════════════════════════════

STRESS_COMMANDS = [
    # ── 0.1 玩法 case 中预设外的变体 ──
    ("我引开守卫，你去偷那箱子", "STEAL", "case C：角色互换（玩家引开，随从偷）"),
    ("打晕门口那两个守卫", "KNOCKOUT", "case P：批量击晕（loop + query）"),
    ("把那个守卫引到巷子里打晕", "KNOCKOUT", "case G：引开+击晕"),
    ("闹出点动静，把人都引过来", "COMMOTION", "case F：闹事引众"),
    ("和他切磋一下，试试他深浅", "DUEL", "case W9：比武切磋"),
    ("去张员外家讨回那笔债", "COLLECT", "case W4：讨债（negotiate 结算）"),
    ("去买两桶酒回来", "PURCHASE", "case W11：购买"),
    ("你在这等我，去那边看看有什么", "SCOUT", "case L：侦察回报"),
    ("站我身后", "FORMATION", "case Q6：相对站位"),
    ("把那封信送到李秀才家", "DELIVER", "case W5：送物版传话"),
    ("找到卖药的郎中，请来给我看伤", "FIND", "case W2：找特征目标"),
    ("悄悄跟着那黑衣人，看他去哪", "SHADOW", "case W3：隐蔽跟踪"),
    ("如果那守卫敢还手，你就上", "GUARD", "case O：条件参战"),
    ("把我的剑拿来", "FETCH", "取物"),
    ("去客栈订两间上房", "TALK_TO", "case W12：社交结算"),
    ("帮我望风，有人来了叫我", "LOOKOUT", "case H：望风（变体措辞）"),
    ("把火把吹灭", "CUSTOM", "INTERACT：引擎能力外 → 诚实拒绝"),
    ("帮我把这封信翻译成拉丁文", "CUSTOM", "词表外：翻译 → 诚实拒绝"),
    ("变个戏法给大家看", "CUSTOM", "词表外：施法/戏法 → 诚实拒绝"),
    ("杀光这村里所有人", "ANNIHILATE", "case N：ANNIHILATE + 澄清或拒绝（模型自主，两者皆可）"),
    # ── 歧义 / 指代 / 边界 ──
    ("就是他，那个戴帽子的", None, "纯指代：场景有唯一黑衣嫌疑可解析（合理），否则应澄清——看行为"),
    ("去把那东西拿来", "CLARIFY", "'那东西' 指代不明 → 应澄清"),
    ("你看着办吧", "ANY", "意图完全开放 → 澄清或 CUSTOM 均可"),
    ("跟他走", "CLARIFY", "'他' 无指代 → 应澄清"),
    # ── 群组谱系（subjects 解析）──
    ("你们三个一起上，把那个黑衣人拿下", "ATTACK", "case Q3：群组战斗（subjects=3人）"),
    ("你们都跟着我", "FOLLOW", "case Q1：群体跟随"),
    # ── 简单/口语 ──
    ("跟我来", "FOLLOW", "跟随"),
    ("站这别动", "WAIT", "原地等待"),
    ("住手！", "STOP", "停止"),
    ("护住他", "GUARD", "护卫"),
    ("前面带路，去酒馆", "GUIDE", "带路（目的地=酒馆）"),
    ("把那醉鬼轰走", "DRIVE_AWAY", "赶走（同义措辞）"),
    ("和掌柜说，明晚我去找他", "DELIVER", "传话（变体）"),
]


def run_case(base, key, model, command, expected, label, scene=None, out_dir=None,
             round_idx=0, verbose=True):
    scene = scene or FIXED_SCENE
    prompt = build_prompt(scene, command)
    parsed, elapsed, err = call_llm(base, key, model, prompt)
    if err:
        print(f"[FAIL] {label}: {err}")
        return False
    issues, it, pl = validate_plan(parsed)
    got = it.get("intent_type")
    questions = parsed.get("questions") or []
    steps_n = len((pl or {}).get("steps") or []) if pl else 0
    plan_null = parsed.get("plan") is None

    if expected == "CUSTOM":
        ok = got == "CUSTOM" and plan_null
    elif expected == "CLARIFY":
        ok = (questions or parsed.get("needs_clarification")) and plan_null
    elif expected == "ANY":
        # 开放命令：CUSTOM 拒绝或澄清都算合理（行为由模型自主决定）
        ok = (got == "CUSTOM" and plan_null) or (questions or parsed.get("needs_clarification")) and plan_null
    else:
        # 分类正确即过（走了澄清轮 = 设计内行为，不判失败）
        ok = got == expected and (pl is not None or bool(questions)) and not issues

    if verbose:
        mark = "OK" if ok else "ISSUE"
        print(f"[{mark}] {label}  intent={got}(期望{expected})  "
              f"steps={steps_n}  澄清={len(questions)}  {elapsed:.1f}s")
        if issues:
            print(f"       问题: {'; '.join(issues)}")
        if pl and pl.get("summary"):
            print(f"       摘要: {pl.get('summary')}")
        if not pl and parsed.get("reply"):
            print(f"       回复: {parsed.get('reply', '')[:80]}")

    # 落盘：完整 input prompt + 完整 output JSON（供人工检查）
    if out_dir:
        safe = "".join(c for c in label if c.isalnum() or c in "-_")[:40] or "case"
        path = os.path.join(out_dir, f"{round_idx}_{safe}.json")
        pl2 = parsed.get("plan") or {}
        doc = {
            "command": command,
            "expected": expected,
            "ok": ok,
            "elapsed_s": round(elapsed, 2),
            "quality": {
                "steps": len(pl2.get("steps") or []),
                "fallbacks": len(pl2.get("fallbacks") or []),
                "contingencies": len(pl2.get("contingencies") or []),
                "goal": 1 if pl2.get("goal") else 0,
                "loop": 1 if pl2.get("loop") else 0,
                "triggers": len(pl2.get("triggers") or []),
            },
            "input_prompt": prompt,
            "output_json": parsed,
            "validator_issues": issues,
        }
        with open(path, "w", encoding="utf-8") as f:
            json.dump(doc, f, ensure_ascii=False, indent=1)
    return ok


def main():
    args = sys.argv[1:]
    rounds = 1
    if "--rounds" in args:
        idx = args.index("--rounds")
        if idx + 1 < len(args):
            rounds = max(1, int(args[idx + 1]))
    out_dir = DEFAULT_OUT
    if "--out" in args:
        idx = args.index("--out")
        if idx + 1 < len(args):
            out_dir = args[idx + 1]

    if "--cmd" in args:
        idx = args.index("--cmd")
        if idx + 1 < len(args):
            base, key, model = load_config()
            print(f"模型: {model}  端点: {base}")
            ok = run_case(base, key, model, args[idx + 1], None, "单命令",
                          out_dir=out_dir)
            return 0 if ok else 1
        print("用法: --cmd <命令>")
        return 2

    base, key, model = load_config()
    os.makedirs(out_dir, exist_ok=True)
    print(f"模型: {model}  端点: {base}  轮数: {rounds}")
    print(f"输出目录: {out_dir}（每 case 一份 输入prompt+输出JSON）\n")

    results = []
    quality = {"steps": [], "fbs": [], "cons": [], "goal": [], "loop": [], "trig": []}
    for r in range(rounds):
        print(f"=== 第 {r + 1} 轮（{len(STRESS_COMMANDS)} 个命令）===")
        for cmd, exp, label in STRESS_COMMANDS:
            results.append(run_case(base, key, model, cmd, exp, f"{label}",
                                    out_dir=out_dir, round_idx=r))
    # 质量汇总（与 PlanExamples 基准对比）
    for f in sorted(glob.glob(os.path.join(out_dir, "*.json"))):
        d = json.load(open(f, encoding="utf-8"))
        q = d.get("quality") or {}
        if not q.get("steps") and not q.get("fallbacks"):  # CUSTOM/澄清无计划，不计入
            continue
        quality["steps"].append(q.get("steps", 0))
        quality["fbs"].append(q.get("fallbacks", 0))
        quality["cons"].append(q.get("contingencies", 0))
        quality["goal"].append(q.get("goal", 0))
        quality["loop"].append(q.get("loop", 0))
        quality["trig"].append(q.get("triggers", 0))
    def avg(ls):
        return round(sum(ls) / max(len(ls), 1), 2)
    print(f"\n--- 质量指标（n={len(quality['steps'])}，基准: PlanExamples steps=3.41 fbs=1.94 cons=1.47 goal率=0.59）---")
    print(f"steps均值={avg(quality['steps'])}  fallbacks均值={avg(quality['fbs'])}  "
          f"contingencies均值={avg(quality['cons'])}  goal率={avg(quality['goal'])}  "
          f"loop数={sum(quality['loop'])}  triggers数={sum(quality['trig'])}")
    passed = sum(1 for r in results if r)
    total = len(results)
    print(f"--- 汇总: 通过 {passed}/{total} "
          f"（分类正确率 {100 * passed // max(total, 1)}%）---")
    return 0 if passed == total else 1


if __name__ == "__main__":
    sys.exit(main())
