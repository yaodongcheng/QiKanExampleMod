#!/usr/bin/env python3
"""check_vocab_sync.py -- C# 词表 vs py 测试词表 一致性校验

C# 侧（单一事实源，prompt 动态拼接）：
  Planner/PlanGrammar.cs     PlanVocab.Actions / Predicates / Queries / ActionAliases
  Planner/ReactiveAgent.cs   ReactiveAgent.TriggerEvents / ReactionActions
py 侧（test_llm_plan.py，回归测试用，双份维护）：
  ALLOWED_ACTIONS / PREDICATES / ACTION_ALIASES / REACTIVE_EVENTS / REACTIVE_ACTIONS / QUERIES

用法：
  python Scripts/check_vocab_sync.py      # 校验全部词表，退出码 0 = 一致
  python Scripts/check_vocab_sync.py -v   # 详细输出（一致也打印）

注册新行为（动作/谓词/查询/触发词/反应动作）流程：
  1. C# 词表加一行（PlanGrammar.cs / ReactiveAgent.cs）→ prompt 自动读到
  2. py 对应常量加一行（test_llm_plan.py）
  3. 跑本脚本确认一致（不一致会列出差异项）
退出码：0 = 全部一致；1 = 有差异。
"""

import os
import re
import sys

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(SCRIPT_DIR)
CSRC = os.path.join(ROOT, "ExampleModVS", "ExampleMod", "ExampleMod")
PY = os.path.join(SCRIPT_DIR, "test_llm_plan.py")


def extract_cs_set(file_path, set_name):
    """提取 C# 静态集合定义的字符串字面量集合（支持多行）。

    优先匹配 *InPromptOrder 数组（单一事实源）；无则回退匹配 HashSet 定义。"""
    src = open(file_path, encoding="utf-8").read()
    # 数组形态：public static readonly string[] NAME = { ... };
    m = re.search(
        r'public static readonly string\[\]\s+' + re.escape(set_name)
        + r'\s*=\s*\{(.*?)\};',
        src, re.DOTALL)
    if not m:
        # HashSet 形态：public static readonly HashSet<string> NAME = new HashSet<string>(...){ ... };
        m = re.search(
            r'public static readonly HashSet<string>\s+' + re.escape(set_name)
            + r'\s*=\s*new\s+HashSet<string>\([^)]*\)\s*\{(.*?)\};',
            src, re.DOTALL)
    if not m:
        return None
    body = m.group(1)
    return set(re.findall(r'"([^"]+)"', body))


def extract_cs_dict(file_path, dict_name):
    """提取 C# 静态 Dictionary<string,string> 定义的键集合（ActionAliases 等）。"""
    src = open(file_path, encoding="utf-8").read()
    m = re.search(
        r'public static readonly Dictionary<string,\s*string>\s+' + re.escape(dict_name)
        + r'\s*=\s*new\s+Dictionary<string,\s*string>\s*\([^)]*\)\s*\{(.*?)\};',
        src, re.DOTALL)
    if not m:
        return None
    body = m.group(1)
    return set(re.findall(r'\{\s*"([^"]+)"', body))


def extract_py_set(const_name):
    """提取 py 常量集合（字典只取键）。"""
    src = open(PY, encoding="utf-8").read()
    m = re.search(
        r'^' + re.escape(const_name) + r'\s*=\s*\{(.*?)\}',
        src, re.DOTALL | re.MULTILINE)
    if not m:
        return None
    body = m.group(1)
    if '"' not in body:
        return None
    # 字典形态 {"k": "v", ...} → 只取键；集合形态 "a", "b" → 全取
    if re.search(r'"[^"]+"\s*:', body):
        return set(re.findall(r'"([^"]+)"\s*:', body))
    return set(re.findall(r'"([^"]+)"', body))


def compare(name, cs_set, py_set, verbose):
    if cs_set is None or py_set is None:
        print(f"[ERROR] {name}: 提取失败（C#={'未找到' if cs_set is None else 'ok'} / py={'未找到' if py_set is None else 'ok'}）")
        return False
    only_cs = cs_set - py_set
    only_py = py_set - cs_set
    if only_cs or only_py:
        print(f"[FAIL] {name}: 不一致")
        if only_cs:
            print(f"   只在 C#（py 缺）：{sorted(only_cs)}")
        if only_py:
            print(f"   只在 py（C# 缺）：{sorted(only_py)}")
        return False
    if verbose:
        print(f"[OK] {name}: {len(cs_set)} 项一致")
    return True


def main():
    verbose = "-v" in sys.argv
    plan_grammar = os.path.join(CSRC, "Planner", "PlanGrammar.cs")
    reactive = os.path.join(CSRC, "Planner", "ReactiveAgent.cs")

    checks = [
        ("Actions（动作词表）", extract_cs_set(plan_grammar, "ActionsInPromptOrder"),
         extract_py_set("ALLOWED_ACTIONS")),
        ("Predicates（谓词词表）", extract_cs_set(plan_grammar, "PredicatesInPromptOrder"),
         extract_py_set("PREDICATES")),
        ("Queries（动态查询）", extract_cs_set(plan_grammar, "QueriesInPromptOrder"),
         extract_py_set("QUERIES")),
        ("ActionAliases（动作别名）", extract_cs_dict(plan_grammar, "ActionAliases"),
         extract_py_set("ACTION_ALIASES")),
        ("TriggerEvents（触发词）", extract_cs_set(reactive, "TriggerEventsInPromptOrder"),
         extract_py_set("REACTIVE_EVENTS")),
        ("ReactionActions（反应动作）", extract_cs_set(reactive, "ReactionActionsInPromptOrder"),
         extract_py_set("REACTIVE_ACTIONS")),
    ]

    ok = True
    for name, cs, py in checks:
        ok = compare(name, cs, py, verbose) and ok

    print("--- 全部一致，词表同步 ---" if ok else "--- 存在差异，请同步两边词表 ---")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
