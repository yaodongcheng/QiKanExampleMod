using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════
    // PlanReplan.cs — 低频 LLM 重入（§7.2，唯一允许执行期之外再调 LLM 的路径）
    //
    // 意外（R5 目标敌对/战斗）→ 执行器 OnAborted → 记录事件日志
    //     → 暂停随从（已恢复默认跟随/护卫）→ LLM 调用：原命令 + 事件日志 + 新快照
    //     → 输出新计划 / "不可行" → 玩家无需重新批准（原命令仍在），summary 播报后重新下发
    //
    // 节流：同计划 replan ≤ 2 次，超限 → Aborted（防死循环烧钱）。
    // 驱动：InteractionMissionView.OnMissionTick → PlanReplan.Tick()（主线程消费 LLM 结果）。
    // ═══════════════════════════════════════════════════════════════

    public static class PlanReplan
    {
        private const int MaxReplans = 2;

        private static PlanResponse _pendingResult;
        private static Agent _pendingOwner;
        private static bool _resultReady;

        /// <summary>接线：executor 创建后调用（PlanCommandFlow 批准 / 调试注入）。</summary>
        public static void Wire(PlanExecutor executor, string originalCommand, string intentType)
        {
            if (executor == null) return;
            executor.OriginalCommand = originalCommand;
            executor.OnAborted += (ex, reason) => OnExecutorAborted(ex, reason, intentType);
        }

        private static void OnExecutorAborted(PlanExecutor ex, string reason, string intentType)
        {
            try
            {
                if (!Settings.Instance.IsLLMConfigured) return;           // 铁律 1
                if (ex.ReplanCount >= MaxReplans) return;            // 节流 ≤ 2
                if (string.IsNullOrEmpty(ex.OriginalCommand)) return;
                var owner = ex.OwnerAgent;
                if (owner == null || !owner.IsActive()) return;

                DebugLogger.Log($"[PlanReplan] 意外 {reason} → 重入计划阶段（第 {ex.ReplanCount + 1} 次）");

                var snapshot = SceneSnapshot.Build(Mission.Current, agentLimit: 30);
                string history = string.Join("\n", ex.EventLog);
                string command = ex.OriginalCommand;
                string intent = intentType;
                // 世界观段切片（🔴 主线程现取——Task.Run 内构建 prompt 禁碰引擎对象）
                string worldSection = WorldBackgroundProvider.GetWorldSection(owner);

                _ = Task.Run(async () =>
                {
                    PlanResponse response = null;
                    bool ok = false;
                    try
                    {
                        string prompt = PromptBuilder.BuildPlanPrompt(
                            snapshot.ToPromptText(), command,
                            "你是一名随从。计划上次出了意外，需要你重新想办法。",
                            history,
                            PlanCommandFlow.IntentTableForPrompt(),
                            PlanCommandFlow.GrammarForPrompt(),
                            worldSection: worldSection);
                        string json = await LLMService.Instance.ChatAsync(prompt, 4000, true, 0.4f, disableReasoning: true);
                        response = JsonConvert.DeserializeObject<PlanResponse>(LLMService.CleanJson(json));
                        ok = response != null && response.Plan != null;
                    }
                    catch (Exception ex2)
                    {
                        DebugLogger.Log($"[PlanReplan] LLM 重入失败: {ex2.Message}");
                    }
                    if (ok)
                    {
                        // 只有成功产出新计划才消耗 replan 额度（网络错误/解析失败不计数，§7.2 节流语义）
                        ex.ReplanCount++;
                    }
                    _pendingResult = response;
                    _pendingOwner = owner;
                    _resultReady = true;
                });
            }
            catch (Exception ex2)
            {
                DebugLogger.Log($"[PlanReplan] 接线异常: {ex2.Message}");
            }
        }

        /// <summary>主线程消费（InteractionMissionView.OnMissionTick 调用）。</summary>
        public static void Tick()
        {
            if (!_resultReady) return;
            _resultReady = false;
            var response = _pendingResult;
            var owner = _pendingOwner;
            _pendingResult = null;
            _pendingOwner = null;
            if (owner == null || !owner.IsActive()) return;

            if (response == null || response.Plan == null)
            {
                // 不可行 → 报告玩家，结束
                // 本地化：Replan 不可行提示
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_plan_replan_fail", "The companion could not find another way."), Colors.Yellow));
                DebugLogger.Log("[PlanReplan] 新计划不可行 → 结束");
                return;
            }

            // 新计划：玩家无需重新批准，但 summary 播报（§7.2）
            // 本地化：Replan 新计划摘要播报
            // 🔴 统一说话框架：重规划摘要（前因=plan_replan；SpeechChannel 线程安全，LLM 回调可直调）。
            // 摘要 response.Plan.Summary 已是 LLM 生成 → 不再 SayPolished（避免嵌套 LLM 请求）
            SpeechChannel.Say(owner, LWNTextHelper.ResolveCompound("LWN_plan_replan_summary", ("SUMMARY", response.Plan.Summary ?? "")),
                SpeechPriority.Dialogue,
                SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(owner), Agent.Main, "plan_replan", null));
            DebugLogger.Log($"[PlanReplan] 新计划下发: {response.Plan.Summary}");

            // 反应计划更新
            if (response.Reactions != null)
            {
                foreach (var rp in response.Reactions)
                {
                    if (rp == null || string.IsNullOrEmpty(rp.Role)) continue;
                    var info = SceneSnapshot.Build(Mission.Current).FindAgent(rp.Role);
                    if (info?.Agent != null)
                        ReactiveAgent.ApplyPlan(info.Agent, rp);
                }
            }

            try
            {
                string planJson = JsonConvert.SerializeObject(response.Plan,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
                AgentAIController.Instance?.SendEventToAgent(owner, "order_execute_plan",
                    planJson, response.Intent?.IntentType, null);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PlanReplan] 新计划下发失败: {ex.Message}");
            }
        }
    }
}
