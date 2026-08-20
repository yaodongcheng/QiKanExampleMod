using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-14（npc-risk-aware-planning.md M4）：风险审视裁决（C# 确定性壳）。
    /// 挂点：ImReplyService.Tick 投递点（主线程）——回复轮 LLM 出 risk_analysis/risk_verdict 后分流：
    ///   feasible             → 现状（动作卡/直接执行），零额外动作
    ///   plan_needed          → 挂「制定计划」按钮（🔴 2026-08-15 用户裁定改全手动：不再自动触发计划轮；
    ///                          玩家点按钮 → RequestCommand 复用，命令 + 战术方向【随从的打算】段）；
    ///                          npc_action 忽略（计划接管）
    ///   risky                → risk_analysis 作为随从台词投递（风险讲透）→ RequiresConfirm 动作的
    ///                          决策卡带风险摘要（_risk 变体文案）→ 玩家确认 → 坚定执行；玩家拒绝 → 不执行
    ///   refuse（办不到才拒绝） → risk_analysis 作为随从消息（说明原因），动作不执行
    ///   字段缺失 / verdict 非法 → 默认 feasible（现状直发，铁律 2 防御）
    /// 降级链（铁律 1）：LLM 是增强不是门禁——风险审视不阻断任何既有路径。
    /// 触发范围：npc_action != NONE 或 need_plan（既有判定字段）；闲聊不触发。
    /// </summary>
    public static class RiskAssessor
    {
        /// <summary>verdict 分流入口（主线程，ImReplyService 投递点调用）。
        /// 返回 true = 本动作已被分流接管（plan_needed 计划接管 / risky 风险卡 / refuse 拒绝）——
        /// 调用方跳过 HandleImAction；false = feasible 现状（动作照常执行）。</summary>
        public static bool Route(ImConversation conv, string heroId, string heroName, string respondText,
            string riskAnalysis, string riskVerdict,
            string actionCode, string actionTarget, string actionLevel)
        {
            try
            {
                if (string.IsNullOrEmpty(heroId) || conv == null) return false;
                if (string.IsNullOrEmpty(riskVerdict)) return false;   // 字段缺失 → 现状（铁律 2）

                switch (riskVerdict.Trim().ToLowerInvariant())
                {
                    case "plan_needed":
                        RoutePlanNeeded(conv, heroId, heroName, respondText, riskAnalysis, actionTarget);
                        return true;   // 已挂「制定计划」按钮（全手动裁定 2026-08-15）；npc_action 忽略（计划接管）
                    case "risky":
                        RouteRisky(conv, heroId, heroName, riskAnalysis, actionCode, actionTarget, actionLevel);
                        return true;   // 风险卡已投递（或直接执行）——避免二次 HandleImAction
                    case "refuse":
                        RouteRefuse(conv, heroId, heroName, riskAnalysis);
                        return true;   // 办不到：动作不执行
                    default:
                        // feasible / 非法 verdict → 现状（动作卡/直接执行由既有 HandleImAction 处理）
                        DebugLogger.Log($"[RiskAssessor] {heroName} verdict={riskVerdict}（默认 feasible，现状直发）");
                        return false;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[RiskAssessor] 分流异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>plan_needed（2026-08-15 用户裁定改全手动）：挂「制定计划」按钮等玩家确认——
        /// **不再自动触发计划轮**（自动触发 = 玩家不要该命令时 LLM 调用白烧 + 与按钮双入口）。
        /// 按钮随带战术方向（risk_analysis 存消息 RiskAnalysisText）与已解析目标（action_target 含 #N
        /// 存 ResolvedTargetText），玩家点「制定计划」→ HandleSuggestion → RequestCommand 进计划轮
        /// 【随从的打算】+【目标指认】段。计划生成必须玩家手动触发（与普通 need_plan 同入口，规则统一）。</summary>
        private static void RoutePlanNeeded(ImConversation conv, string heroId, string heroName,
            string respondText, string riskAnalysis, string actionTarget)
        {
            try
            {
                ImCommandFlow.TryAttachSuggestion(conv, heroId, heroName, respondText, riskAnalysis, actionTarget);
                DebugLogger.Log($"[RiskAssessor] {heroName} verdict=plan_needed → 挂「制定计划」按钮（全手动；战术方向+目标 {actionTarget ?? "-"} 随带）");
            }
            catch (Exception ex) { DebugLogger.Log($"[RiskAssessor] plan_needed 按钮挂载失败: {ex.Message}"); }
        }

        /// <summary>risky：风险讲透（risk_analysis 随从台词）→ RequiresConfirm 动作的决策卡带风险摘要
        /// （_risk 变体）→ 玩家确认后坚定执行；玩家拒绝 → 不执行（玩家自己的选择，不是 NPC 拒绝）。
        /// v1：仅 RequiresConfirm 动作走风险卡；非确认动作 → 台词已讲透风险，直接执行（低危动作 risky 几乎不出现）。
        /// 🔴 2026-08-15（三句连发实机）：risk_analysis 台词与决策卡/执行**分时投递**——间隔按前句字数估算
        /// + 随机抖动（ImChatManager.SpeechPauseFor），npc_reply（ImReplyService 已同步投递）→ 台词 →
        /// 卡片/动作，模拟真人说话节奏，不再 11ms 三句齐发。
        /// 🔴 2026-08-20（用户反馈：问一个问题随从回答多句）：risky 时 npc_reply（ImReplyService 已投递）
        /// + 风险台词（本方法）+ 决策卡 = 一句问话两条消息。风险台词删除——风险摘要已在决策卡上
        ///（riskSummary 变体文案），一句话 + 一张卡，信息不损、话不重复。</summary>
        private static void RouteRisky(ImConversation conv, string heroId, string heroName,
            string riskAnalysis, string actionCode, string actionTarget, string actionLevel)
        {
            // 前句 = 刚投递的 npc_reply 台词（store 最后一条 Text）——卡片间隔仍按前句字数挂钩
            string prev = "";
            try
            {
                var msgs = ImChatStore.GetGroupMessages(conv.Id);
                for (int i = msgs.Count - 1; i >= 0; i--)
                {
                    if (msgs[i] != null && msgs[i].Kind == ImMessageKind.Text && !string.IsNullOrWhiteSpace(msgs[i].Content))
                    { prev = msgs[i].Content; break; }
                }
            }
            catch { }
            if (string.IsNullOrEmpty(actionCode) || actionCode == "NONE")
            {
                DebugLogger.Log($"[RiskAssessor] {heroName} risky 但无动作（npc_action=NONE）→ 只讲风险");
                return;
            }
            // 决策卡在台词之后（间隔 = 前句字数；风险摘要 riskSummary 随卡携带，不再单独投风险台词）
            float d1 = ImChatManager.SpeechPauseFor(prev);
            var actionDef = ActionRegistry.FindByCode(actionCode);
            if (actionDef != null && actionDef.RequiresConfirm)
            {
                ImChatManager.ScheduleDelayedAction(() =>
                {
                    try
                    {
                        ActionHandler.HandleImAction(actionCode, heroId, heroName,
                            actionTarget, actionLevel, conv, null, bypassConfirm: false,
                            explicitTarget: null, candidateIndex: null, riskSummary: riskAnalysis);
                    }
                    catch (Exception ex) { DebugLogger.Log($"[RiskAssessor] risky 决策卡投递失败: {ex.Message}"); }
                }, d1);
                DebugLogger.Log($"[RiskAssessor] {heroName} risky → 决策卡带风险摘要延迟投递（{d1:F1}s 后，风险台词并入卡片）");
            }
            else
            {
                ImChatManager.ScheduleDelayedAction(() =>
                {
                    try
                    {
                        ActionHandler.HandleImAction(actionCode, heroId, heroName,
                            actionTarget, actionLevel, conv, null, bypassConfirm: false);
                    }
                    catch (Exception ex) { DebugLogger.Log($"[RiskAssessor] risky 非确认动作执行失败: {ex.Message}"); }
                }, d1);
                DebugLogger.Log($"[RiskAssessor] {heroName} risky → 非确认动作延迟执行（{d1:F1}s 后）");
            }
        }

        /// <summary>refuse（缩窄为「办不到」）：risk_analysis 作为随从消息说明原因，动作不执行。
        /// 拒绝纪律沿用现有「只拒绝一次，主公重申必须执行」兜底（LLM 侧纪律）。</summary>
        private static void RouteRefuse(ImConversation conv, string heroId, string heroName, string riskAnalysis)
        {
            if (!string.IsNullOrWhiteSpace(riskAnalysis))
            {
                try
                {
                    ImChatManager.DeliverNpcMessage(conv, heroId, heroName, riskAnalysis);
                }
                catch (Exception ex) { DebugLogger.Log($"[RiskAssessor] refuse 消息投递失败: {ex.Message}"); }
            }
            DebugLogger.Log($"[RiskAssessor] {heroName} verdict=refuse → 不执行（办不到）");
        }
    }
}
