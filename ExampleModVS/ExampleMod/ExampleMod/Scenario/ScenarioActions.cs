using System;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 剧本命令执行（W4：机制族 A 真实现；其余 = 桩表——16b 归属分批接真，桩统一 [Scenario][TODO] 日志）。
    /// 🔴 资源归口（铁律 4）：金额类一律 AgentControlHelper；对象写回 = ScenarioAttrStore 唯一入口。
    /// </summary>
    public static class ScenarioActions
    {
        /// <summary>执行一条 effect 步骤。返回是否继续（false = 中止本事件流——01 纪律：异常不崩，记日志）。</summary>
        public static bool Execute(ScenarioScriptStep step, ScenarioContext ctx)
        {
            string action = step.Action;
            try
            {
                switch (action)
                {
                    // ── A 组机制族（真实现）──
                    case "update":
                        return HandleUpdate(step, ctx);
                    case "assign_ctx":
                        return HandleAssign(step, ctx, toCtx: true);
                    case "assign_var":
                        return HandleAssign(step, ctx, toCtx: false);
                    case "set_flag":
                        ScenarioStateStore.SetFlag(step.Get("flag"), true);
                        return true;
                    case "clear_flag":
                        ScenarioStateStore.ClearFlag(step.Get("flag"));
                        return true;
                    case "set_variable":
                        ScenarioStateStore.SetVariable(step.Get("variable"), EvalToText(step.Get("value"), ctx));
                        return true;
                    case "global_set":
                        ScenarioStateStore.GlobalSet(step.Get("slot"), EvalToText(step.Get("value"), ctx));
                        return true;
                    case "counter_reset":
                        ScenarioStateStore.CounterReset(int.Parse(step.Get("n") ?? "0"));
                        return true;

                    // ── 其余 = 桩（B 组引擎动作/容器/演出——接真 = 16b 归属：02/03/13/17/05，TODO 日志）──
                    default:
                        Pending($"action:{action}（{(step.Get("actor") ?? step.Get("target") ?? "")}）");
                        return true;
                }
            }
            catch (Exception e)
            {
                DebugLogger.Log($"[Scenario][Executor] action {action} 异常（步骤中止，事件流继续）: {e.Message}");
                return true;
            }
        }

        /// <summary>ТK5 更新：target = DSL 引用（域:对象.字段）→ 字段写入指定值（值 = 求值/字面量字符串化）</summary>
        private static bool HandleUpdate(ScenarioScriptStep step, ScenarioContext ctx)
        {
            string target = step.Get("target") ?? step.Get("actor");
            if (string.IsNullOrEmpty(target)) { DebugLogger.Log("[Scenario][Update] 缺 target"); return true; }
            var node = DslParser.Parse(target);
            var refNode = node as DslRef;
            if (refNode == null || refNode.Attr == null)
            {
                DebugLogger.Log($"[Scenario][Update] target 非「域:对象.字段」引用（跳过）: {target}");
                return true;
            }
            string value = EvalToText(step.Get("value"), ctx);
            ScenarioAttrStore.SetAttr(refNode.Domain + refNode.Id, refNode.Attr, value);
            return true;
        }

        /// <summary>代入（assign_ctx = Ctx 槽；assign_var = 持久 Variable——16 §一 三档）</summary>
        private static bool HandleAssign(ScenarioScriptStep step, ScenarioContext ctx, bool toCtx)
        {
            string slot = step.Get("slot");
            string value = EvalToText(step.Get("value"), ctx);
            if (slot == null) return true;
            if (toCtx) ctx.Set(slot, value);
            else ScenarioStateStore.SetVariable(slot, value);
            return true;
        }

        /// <summary>
        /// 求值一个"值文本"为字符串：形态 = "(Clan::x.home)" DSL 引用 / "\"字面量\"" 字符串 / "Time::year" 裸引用。
        /// 求值失败 = 原样返回（更新类不因坏值崩——加载期 validator 拦截语法）。
        /// </summary>
        public static string EvalToText(string text, ScenarioContext ctx)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                var node = DslParser.Parse(text);
                if (node == null) return text;
                var v = node.Eval(ctx ?? ScenarioContext.Instance);
                switch (v.Kind)
                {
                    case DslValueKind.Number: return v.Num.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    case DslValueKind.Bool: return v.Bool ? "true" : "false";
                    case DslValueKind.String: return v.Str;
                }
                return null;
            }
            catch (Exception e)
            {
                DebugLogger.Log($"[Scenario][EvalToText] 求值失败（原样返回）: {text} → {e.Message}");
                return text;
            }
        }

        private static void Pending(string what)
        {
            DebugLogger.Log($"[Scenario][TODO] 未实现命令（桩）: {what} — 接真归属见 16b 落点裁定表");
        }
    }
}
