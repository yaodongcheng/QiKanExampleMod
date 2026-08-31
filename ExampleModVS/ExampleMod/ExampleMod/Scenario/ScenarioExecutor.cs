using System;
using System.Collections.Generic;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 剧本执行器（W4）：按 01 步骤类型表跑事件 script（同步顺序；步骤完成才下一步）。
    /// 支撑步 = effect（划景的 ScenarioActions）／ if（when + then/else，嵌套 ≤2，01 纪律）／ note（日志）。
    /// 演出与外部系统步（perform/inquiry/scene_enter/actor_*/bgm 等）= 桩 [Scenario][TODO]（W5/W6 接真——00 v3 §2.3）。
    /// 🔴 异常纪律（铁律 1/2）：单步异常 = 日志 + 跳过该步，不崩；演出截断 = 步骤完成（01，W5 播放器落实）。
    /// </summary>
    public static class ScenarioExecutor
    {
        public static int MaxIfDepth = 2;

        private static readonly HashSet<string> PendingSteps = new HashSet<string>(StringComparer.Ordinal)
        {
            "perform", "inquiry", "cutscene", "im_message", "wait", "bgm", "se",
            "scene_enter", "scene_exit", "choice", "loop", "break", "module_exit",
            "bgm_change", "se_start", "se_stop", "se_loop", "image_show", "image_hide",
            "bg_change", "bg_restore", "screen_effect", "scene_next", "message_close",
            "container_set", "container_filter", "container_exclude", "container_sort",
            "container_pick", "container_clear",
            "actor_enter", "actor_move", "actor_leave", "camera", "actor_action",
        };

        /// <summary>执行一串步骤。返回是否完成（false = 异常中止；调用方（调度器）记已完成仅当 true）。</summary>
        public static bool RunSteps(List<ScenarioScriptStep> steps, ScenarioContext ctx, int depth = 0)
        {
            if (steps == null) return true;
            if (depth > MaxIfDepth)
            {
                DebugLogger.Log($"[Scenario][Executor] if 嵌套超限（{MaxIfDepth} 层，01 纪律）——跳过剩余步骤");
                return false;
            }
            foreach (var step in steps)
            {
                try
                {
                    switch (step.Step)
                    {
                        case "effect":
                            if (!ScenarioActions.Execute(step, ctx)) return false;
                            break;
                        case "if":
                        {
                            bool cond = DslEvaluator.Evaluate(step.When, ctx);
                            bool ok = RunSteps(cond ? step.Then : step.Else, ctx, depth + 1);
                            if (!ok) return false;
                            break;
                        }
                        case "note":
                            DebugLogger.Log($"[Scenario][Note] {step.Note ?? step.Get("note") ?? "(空)"}");
                            break;
                        default:
                            if (PendingSteps.Contains(step.Step))
                            {
                                DebugLogger.Log($"[Scenario][TODO] 步骤 {step.Step}（{step.PlaybackId ?? step.Action ?? ""}）未实现——桩，跳过");
                                break;
                            }
                            DebugLogger.Log($"[Scenario][Executor] 未知步骤 {step.Step}（加载期已拦，防御跳过）");
                            break;
                    }
                }
                catch (Exception e)
                {
                    DebugLogger.Log($"[Scenario][Executor] 步骤 {step.Step} 异常（跳过继续，不崩）: {e.Message}");
                }
            }
            return true;
        }
    }
}
