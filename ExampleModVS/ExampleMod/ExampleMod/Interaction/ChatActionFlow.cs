using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 闲聊单条指令执行（im-command-action-upgrade.md §5.3）：
    /// 闲聊动作被包装成**单步 Plan**（1 个 step：action + target）走 PlanExecutor 既有执行分支
    /// （TryCreateSubAction / InlineSteps 全复用），由既有 TickAll 驱动。
    /// 密令模式能跑的每个原子行为，闲聊一句话同样能触发（同空间同能力，出戏为零），执行层零新代码。
    ///
    /// 参数全 C# 确定（铁律 2）：LLM 只给动作码 + 档位 + 名字文本；
    /// 默认 target = defender 名（执行器 TryResolveAgent/TryResolvePosition 既有解析链：
    /// agent → 快照对象 → 语义 tag zone）；解析失败 → 步骤失败（执行器既有失败路径）。
    ///
    /// 收尾：单步 Plan 自然走完 = Finish(Succeeded, null) → 静默（无密信/无报告，不刷屏）。
    /// </summary>
    public static class ChatActionFlow
    {
        /// <summary>金额档位（§5.3）：LLM 只给档位，数值 C# 定。随从的赏钱量级（50/150/500 金币）。</summary>
        public static int GoldLevelAmount(string level)
        {
            switch (level?.ToLowerInvariant())
            {
                case "small": return 50;
                case "large": return 500;
                default: return 150;   // medium / 未给 → 默认 medium
            }
        }

        /// <summary>
        /// 执行单条闲聊动作（§5.3）。返回是否成功进入执行。
        /// </summary>
        /// <param name="actor">attacker 的物理载体（InScene 动作执行者）</param>
        /// <param name="actionCode">原子动作名（PlanVocab 词表内）</param>
        /// <param name="targetText">target 名字文本（agent 名/语义 tag zone；C# 解析，铁律 2）</param>
        /// <param name="level">档位：EMOTE=动画 key（白名单 9 动画）；GIVE_GOLD=金额档位；其余忽略</param>
        /// <param name="sayText">SAY_TO 台词（v1 = IM 回复正文复述，一句话两用）</param>
        public static bool TryExecute(Agent actor, string actionCode, string targetText, string level, string sayText)
        {
            if (actor == null || !actor.IsActive() || string.IsNullOrEmpty(actionCode)) return false;
            if (Mission.Current == null) return false;

            try
            {
                var step = new PlanStep
                {
                    Id = "chat_1",
                    Action = actionCode,
                };
                // 目标文本（可选；不传 = 执行器缺省语义）
                if (!string.IsNullOrEmpty(targetText))
                    step.Target = targetText;
                // 参数（C# 确定，铁律 2）
                switch (actionCode)
                {
                    case "emote":
                        step.Text = level;   // EmoteInlineState 白名单校验（9 动画），非法 → 降级无动作
                        break;
                    case "look_at":
                        step.Seconds = 2f;   // 时长默认 2s（§5.3）
                        break;
                    case "follow":
                        step.TimeoutS = 0f;  // 无限保持（与密令 follow 省略 timeout 同语义）
                        break;
                    case "steal_attempt":
                        step.Variant = "pickpocket";   // 人变体（扒窃 defender；result 路由既有）
                        break;
                    case "give_gold":
                        step.Amount = new JValue(GoldLevelAmount(level));
                        break;
                    case "say_to":
                        step.Text = sayText;   // v1：IM 回复正文复述（prompt 约束正文须可转述）
                        break;
                }

                var plan = new Plan
                {
                    Summary = "chat action",
                    Steps = new List<PlanStep> { step },
                };
                var executor = PlanExecutor.Create(actor, plan, "CUSTOM");
                if (executor == null)
                {
                    DebugLogger.Log($"[ChatActionFlow] 单步计划构建失败: {actionCode}（目标: {targetText ?? "-"}）→ 降级 NONE");
                    return false;
                }
                executor.Start(actor);
                // 🔴 2026-08-13：日志写明"包裹为单步计划"——区分 LLM 生成的任务计划
                // （模型只决策动作码，计划壳由本方法确定性构造，无玩家批准环节）
                DebugLogger.Log($"[ChatActionFlow] {actor.Name} 执行闲聊动作: {actionCode}（target: {targetText ?? "-"}）→ 包裹为单步 Custom 计划直接执行，无需批准");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ChatActionFlow] 执行异常 {actionCode}: {ex.Message}");
                return false;
            }
        }
    }
}
