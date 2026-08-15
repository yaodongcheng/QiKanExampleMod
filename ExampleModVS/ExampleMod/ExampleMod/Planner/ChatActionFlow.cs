using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
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
        /// 🔴 2026-08-14（M2b）：偷窃目标是否为 Hero 的构建期判定（决定补不补 give_gold 尾步骤）。
        /// 与执行期目标解析同口径（SceneSnapshot.FindAgent 五层匹配 / explicitTarget 角色锁定）：
        /// 解析到 Hero → 补尾步骤；模板 NPC / 解析失败 → 不补（钱袋路径或失败路径内部处理）。
        /// </summary>
        private static bool StolenTargetIsHero(Agent actor, string targetText, Agent explicitTarget)
        {
            try
            {
                Agent target = explicitTarget;
                if (target == null && !string.IsNullOrEmpty(targetText) && Mission.Current != null)
                {
                    var info = SceneSnapshot.Build(Mission.Current).FindAgent(targetText);
                    target = info?.Agent;
                }
                if (target == null) return false;
                return (target.Character as CharacterObject)?.HeroObject != null;
            }
            catch { return false; }
        }
        /// <summary>
        /// 执行单条闲聊动作（§5.3）。返回是否成功进入执行。
        /// </summary>
        /// <param name="actor">attacker 的物理载体（InScene 动作执行者）</param>
        /// <param name="actionCode">原子动作名（PlanVocab 词表内）</param>
        /// <param name="targetText">target 名字文本（agent 名/语义 tag zone；C# 解析，铁律 2）</param>
        /// <param name="level">档位：EMOTE=动画 key（白名单 9 动画）；GIVE_GOLD=金额档位；其余忽略</param>
        /// <param name="sayText">SAY_TO 台词（v1 = IM 回复正文复述，一句话两用）</param>
        /// <param name="explicitTarget">🔴 2026-08-13：模板 NPC 目标精确锁定——同名多候选已由玩家选定后传入；
        /// 非空时 step.Target="target" + roleAgents["target"]=该 agent → 执行器 TryResolveAgent("target")
        /// 命中 RoleAgents（RuntimeWorldState.cs:421），不靠名字模糊匹配（多同名会取错）。</param>
        /// <param name="onFinished">🔴 2026-08-14（M6 分头配合）：执行完成回调（主线程，PlanExecutor.OnFinished
        /// 触发）——B 侧配合完成后发 assist_done 回执给 A；普通闲聊动作不传（null）。</param>
        public static bool TryExecute(Agent actor, string actionCode, string targetText, string level, string sayText,
            Agent explicitTarget = null, Action<Agent> onFinished = null)
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
                // 目标文本（可选；不传 = 执行器缺省语义）；显式目标走角色名 "target"
                if (explicitTarget != null && explicitTarget.IsActive())
                {
                    step.Target = "target";
                    targetText = null;
                }
                else if (!string.IsNullOrEmpty(targetText))
                    step.Target = targetText;
                // 🔴 2026-08-13：move_to/follow 给宽松超时——步骤校验的默认补时仅 30s（PlanGrammar
                // 补 timeout_s 30s），追移动中的玩家/长距离步行经常超时 → "拖太久了，先撤" 中止
                // （实机：让随从来身边，随从跟着走动的玩家追不上 → 30s 后中止 + 看不懂的密信）。
                // 玩家走远 >30m 已有 PlanExecutor「玩家走远了」暂停追回兜底，120s 只是最后防线。
                // 🔴 2026-08-13（knockout/steal_attempt 同病）：接近型动作——目标可能 50 米开外，
                // 走速接近 30s 不够（实机：52.5 米目标 → 超时中止「拖太久没成」）。同放宽 120s。
                if (actionCode == "move_to" || actionCode == "follow"
                    || actionCode == "knockout" || actionCode == "steal_attempt")
                    step.TimeoutS = 120f;
                // 参数（C# 确定，铁律 2）——2026-08-13 重构：FillParams 查 ActionRegistry 主表
                //（6 个参数化动作：emote/look_at/follow/steal_attempt/give_gold/say_to）
                ActionRegistry.FindByCode(actionCode)?.FillParams?.Invoke(step, level, sayText);
                var plan = new Plan
                {
                    Summary = "chat action",
                    Steps = new List<PlanStep> { step },
                };
                // 🔴 2026-08-14（M2b，npc-risk-aware-planning.md）：聊天单步计划缺尾步骤，赃物到不了
                // 玩家手上——按目标类型处理：
                //   模板 NPC 目标（无 Hero）：StealAttemptInlineState 内部走 StealPurseGold 钱袋路径，
                //     当场守恒移交（金库→玩家）→ 无尾步骤（MarkGoldHanded 防双移交）
                //   Hero 目标：补 give_gold(stolen) 尾步骤（GiveInlineState 时 TransferGold 个人钱包）
                if (actionCode == "steal_attempt" && StolenTargetIsHero(actor, targetText, explicitTarget))
                {
                    plan.Steps.Add(new PlanStep
                    {
                        Id = "chat_2",
                        Action = "give_gold",
                        Amount = new Newtonsoft.Json.Linq.JValue("stolen"),
                        TimeoutS = 10f,
                    });
                    DebugLogger.Log($"[ChatActionFlow] {actor.Name} 偷窃目标为 Hero → 补 give_gold(stolen) 尾步骤（赃物移交）");
                }
                var executor = PlanExecutor.Create(actor, plan, "CUSTOM",
                    explicitTarget != null
                        ? new Dictionary<string, Agent>(StringComparer.OrdinalIgnoreCase) { ["target"] = explicitTarget }
                        : null);
                if (executor == null)
                {
                    DebugLogger.Log($"[ChatActionFlow] 单步计划构建失败: {actionCode}（目标: {targetText ?? "-"}）→ 降级 NONE");
                    return false;
                }
                if (onFinished != null)
                {
                    var actorRef = actor;
                    executor.OnFinished += _ => { try { onFinished(actorRef); } catch (Exception ex) { DebugLogger.Log($"[ChatActionFlow] onFinished 回调异常: {ex.Message}"); } };
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