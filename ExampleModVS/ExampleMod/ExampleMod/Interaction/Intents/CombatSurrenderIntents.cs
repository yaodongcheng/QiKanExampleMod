using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 玩家认输 → 交钱保命。无检定，必定执行。
    /// 正常罚金 = CrimePenaltyCalculator.ComputeSurrenderRansom()（玩家金币15%或200取大值）；
    /// counteroffer 后罚金翻倍。
    /// 后果：罚金 + 荣誉 -1 + 勇敢 -1 + 战斗结束。
    /// </summary>
    public class PlayerSurrenderPayIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.PersuadeSurrender;
        // 玩家选项名：交出钱袋
        public override string DisplayName => LWNTextHelper.ResolveText("LWN_ui_option_surrender_pay", "(Hand over your purse)");
        public override NegotiationGoalType? Goal => null; // 无条件，不检定

        // 对话选项前缀：交钱
        public override string GetDialoguePrefix(string actionParam = null) => LWNTextHelper.ResolveText("LWN_ui_prefix_surrender_pay", "[Pay up]");

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (!ctx.InRealScene) return Eligibility.Hide();

            int baseRansom = CrimePenaltyCalculator.ComputeSurrenderRansom();
            bool isCounteroffer = ctx.ActionParam == "counteroffer_beg"
                               || ctx.ActionParam == "counteroffer_threaten";
            int cost = isCounteroffer ? baseRansom * 2 : baseRansom;

            if (Hero.MainHero.Gold < cost)
            {
                // 置灰原因：金币不足（{NEED}=所需金额，{HAVE}=现有金额）
                return Eligibility.Grey(LWNTextHelper.ResolveCompound("LWN_intent_surrender_insufficient_gold",
                    ("NEED", cost.ToString()), ("HAVE", Hero.MainHero.Gold.ToString())));
            }
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            int baseRansom = CrimePenaltyCalculator.ComputeSurrenderRansom();
            bool isCounteroffer = ctx.ActionParam == "counteroffer_beg"
                               || ctx.ActionParam == "counteroffer_threaten";
            int cost = isCounteroffer ? baseRansom * 2 : baseRansom;
            int penalty = Math.Min(Hero.MainHero.Gold, cost);
            if (penalty > 0)
                AgentControlHelper.TransferGold(Hero.MainHero, null, penalty);

            // ② 荣誉惩罚
            int honor = Hero.MainHero.GetTraitLevel(DefaultTraits.Honor);
            Hero.MainHero.SetTraitLevel(DefaultTraits.Honor, honor - 1);
            int valor = Hero.MainHero.GetTraitLevel(DefaultTraits.Valor);
            Hero.MainHero.SetTraitLevel(DefaultTraits.Valor, valor - 1);

            // ③ 停战事件已由 CombatManager.PlayerSurrenderToAgent 在对话前发送，
            //    Agent 已在 StayAction 中，无需重复发送。

            // ④ 清除"拼死一战"标记（防御：威胁失败 → counteroffer → 玩家最终交钱，不应再重回战斗）
            FightOnIntent.PendingSurrenderRefusedAgent = null;

            // ⑤ 结案：赎金已付，事件了结
            SurrenderIntentHelper.ResolveSurrenderWorldEvent("payment");

            DebugLogger.Log($"[Combat] SurrenderPay: penalty={penalty}G{(isCounteroffer ? " (counteroffer x2)" : "")}, honor={honor}→{honor - 1}, valor={valor}→{valor - 1}");
        }
    }

    /// <summary>
    /// 玩家认输 → 魅力求饶。检定通过 = 免单放人；失败 = NPC 嘲讽 + 罚金翻倍 → 弹回投降菜单。
    /// OnFail 不扣任何东西——玩家还没同意接受翻倍的代价。
    /// </summary>
    public class PlayerSurrenderBegIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.PersuadeSurrender;
        // 玩家选项名：求饶
        public override string DisplayName => LWNTextHelper.ResolveText("LWN_ui_option_surrender_beg", "Please spare me...");
        public override NegotiationGoalType? Goal => NegotiationGoalType.ResolveConflict_Explain;
        public override NegotiationTactic Tactic => NegotiationTactic.Flatter;
        public override float CooldownDays => 0f; // 每次战斗仅一次
        // 对话选项前缀：求饶
        public override string GetDialoguePrefix(string actionParam = null) => LWNTextHelper.ResolveText("LWN_ui_prefix_surrender_beg", "[Beg]");
        public override bool ReofferOnFail => true; // 🆕 失败后重新渲染选项

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (!ctx.InRealScene) return Eligibility.Hide();
            // 已经求饶失败过了 → 置灰
            if (ctx.ActionParam == "counteroffer_beg")
            {
                // 置灰原因：已经求饶过一次
                return Eligibility.Grey(LWNTextHelper.ResolveText("LWN_intent_surrender_beg_used", "You've already begged once"));
            }
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            // 魅力说服成功：免单放人，但荣誉仍 -1（求饶本身就不光彩）
            int honor = Hero.MainHero.GetTraitLevel(DefaultTraits.Honor);
            Hero.MainHero.SetTraitLevel(DefaultTraits.Honor, honor - 1);

            // 结案：NPC 宽恕放人
            SurrenderIntentHelper.ResolveSurrenderWorldEvent("forgiven");

            DebugLogger.Log($"[Combat] SurrenderBeg SUCCESS: 免单放人, honor={honor}→{honor - 1}");
        }

        public override void OnFail(IntentContext ctx)
        {
            base.OnFail(ctx); // 正常掉好感 + 冷却

            // ⭐ 关键：不扣钱、不扣属性、不结束战斗！
            // 只标记 counteroffer 状态，让下一轮 PayIntent 读到翻倍的罚金。
            // ReofferOnFail=true → ResolveAdversarialIntent 会调 RefreshInitialOptions()
            // → BuildOptionVMs 重新跑所有 Evaluate → PayIntent 读 ActionParam 显示翻倍金额
            ctx.ActionParam = "counteroffer_beg";

            DebugLogger.Log($"[Combat] SurrenderBeg FAIL: counteroffer — 罚金翻倍");
        }
    }

    /// <summary>
    /// 玩家认输 → 破口大骂虚张声势。检定通过 = NPC 怂了放人，失败 = 继续打。
    /// </summary>
    public class PlayerSurrenderThreatenIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.PersuadeSurrender;
        // 玩家选项名：辱骂威胁
        public override string DisplayName => LWNTextHelper.ResolveText("LWN_ui_option_surrender_threaten", "You cur!...");
        public override NegotiationGoalType? Goal => NegotiationGoalType.ResolveConflict_Intimidate;
        public override NegotiationTactic Tactic => NegotiationTactic.Threaten;
        public override float CooldownDays => 0f;

        // 对话选项前缀：威胁
        public override string GetDialoguePrefix(string actionParam = null) => LWNTextHelper.ResolveText("LWN_ui_prefix_surrender_threaten", "[Threaten]");

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (!ctx.InRealScene) return Eligibility.Hide();
            // counteroffer 阶段：已经求饶失败过了，威胁选项不可用
            if (ctx.ActionParam == "counteroffer_beg")
            {
                // 置灰原因：已经求饶过一次
                return Eligibility.Grey(LWNTextHelper.ResolveText("LWN_intent_surrender_beg_used", "You've already begged once"));
            }
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            // 成功：NPC 怂了，不付钱，荣誉 -1（不扣勇敢——至少还有骨气）
            int honor = Hero.MainHero.GetTraitLevel(DefaultTraits.Honor);
            Hero.MainHero.SetTraitLevel(DefaultTraits.Honor, honor - 1);

            // 结案：NPC 被吓退
            SurrenderIntentHelper.ResolveSurrenderWorldEvent("intimidated");

            DebugLogger.Log($"[Combat] SurrenderThreaten SUCCESS: NPC 怂了, honor={honor}→{honor - 1}");
        }

        public override void OnFail(IntentContext ctx)
        {
            // 失败：NPC 暴怒，战斗继续。
            // 两阶段模式（对标 ThreatIntent.PendingCombatAgent）：
            // 对话中只标记，EndConversation 消费后发送 event_surrender_refused。
            FightOnIntent.PendingSurrenderRefusedAgent = ctx.Agent;

            DebugLogger.Log($"[Combat] SurrenderThreaten FAIL: NPC 暴怒，标记战后重回战斗");
        }
    }

    /// <summary>
    /// 处决 NPC 认输请求。通过 ActionParam 区分四种模式。
    /// 对标 PayRestitutionIntent 的 "alert_fine" 分化模式。
    /// </summary>
    public class ResolveNpcSurrenderIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.PersuadeSurrender;
        public override string DisplayName => "…"; // 被 DialogueInjector PlayerLine 覆盖
        public override NegotiationGoalType? Goal => null; // 即时类

        public override string GetDialoguePrefix(string actionParam = null) => actionParam switch
        {
            // 对话选项前缀：放走投降的 NPC
            "accept" => LWNTextHelper.ResolveText("LWN_ui_prefix_surrender_npc_accept", "[Let go]"),
            // 对话选项前缀：羞辱投降的 NPC
            "humiliate" => LWNTextHelper.ResolveText("LWN_ui_prefix_surrender_npc_humiliate", "[Humiliate]"),
            // 对话选项前缀：向投降的 NPC 索要赎金
            "ransom" => LWNTextHelper.ResolveText("LWN_ui_prefix_surrender_npc_ransom", "[Ransom]"),
            // 对话选项前缀：拒绝投降并击杀
            "refuse" => LWNTextHelper.ResolveText("LWN_ui_prefix_surrender_npc_refuse", "[Kill]"),
            _ => null
        };

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.InRealScene && ctx.Agent != null)
                return Eligibility.Show();
            return Eligibility.Hide();
        }

        public override void OnInstant(IntentContext ctx)
        {
            var brain = AgentAIController.GetBrainForAgent(ctx.Agent);
            if (brain == null) return;

            switch (ctx.ActionParam)
            {
                case "accept":
                    // 宽宏大量：好感 +2
                    if (ctx.Speaker != null)
                        ChangeRelationAction.ApplyPlayerRelation(ctx.Speaker, 2, false, true);
                    // 停战事件已由 CombatManager.AcceptAgentSurrender 在对话前发送，
                    // Agent 已在 StayAction 中，无需重复发送。
                    DebugLogger.Log("[Combat] ResolveNpcSurrender: accept (+2 relation)");
                    break;

                case "humiliate":
                    // 侮辱：好感 -10 + 嗑头动画
                    if (ctx.Speaker != null)
                        ChangeRelationAction.ApplyPlayerRelation(ctx.Speaker, -10, false, true);
                    AgentControlHelper.ForcePlayAction(ctx.Agent, "act_kneel");
                    // 停战事件已由 CombatManager.AcceptAgentSurrender 在对话前发送。
                    DebugLogger.Log("[Combat] ResolveNpcSurrender: humiliate (-10 relation, kneel)");
                    break;

                case "ransom":
                    // 索钱：NPC → 玩家转账
                    if (ctx.Speaker != null)
                    {
                        int ransom = Math.Min(ctx.Speaker.Gold, 500);
                        if (ransom > 0)
                            AgentControlHelper.TransferGold(ctx.Speaker, Hero.MainHero, ransom);
                    }
                    // 停战事件已由 CombatManager.AcceptAgentSurrender 在对话前发送。
                    DebugLogger.Log("[Combat] ResolveNpcSurrender: ransom");
                    break;

                case "refuse":
                    // 拒绝认输 → NPC 战后重回战斗（两阶段：对话中只标记，EndConversation 消费）
                    FightOnIntent.PendingSurrenderRefusedAgent = ctx.Agent;
                    // 🔴 统一说话框架 + M4 双轨润色：NPC 拒绝投降喊话（Combat 前因=combat；终局时敏 = 1s 短预算）
                    SpeechChannel.SayPolished(ctx.Agent, LWNTextHelper.ResolveText("LWN_intent_surrender_npc_refused_say", "No——!!"),
                        SpeechPriority.Combat,
                        SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(ctx.Agent), Agent.Main, "combat", "战斗" /* lwn-ignore: A 话题词（prompt 材料） */),
                        budgetS: 1f);
                    DebugLogger.Log("[Combat] ResolveNpcSurrender: refuse (标记战后重回战斗)");
                    break;
            }
        }
    }

    /// <summary>
    /// 玩家投降 → counteroffer 阶段选择"拼死一战"。
    /// 不扣任何资源，直接终止谈判重回战斗。
    ///
    /// 两阶段模式（对标 ThreatIntent.PendingCombatAgent）：
    /// 对话中只设置 PendingSurrenderRefusedAgent 标记，
    /// EndConversation 消费后发送 event_surrender_refused → Brain 清 StayAction → 重回 FightEnemyAction。
    /// </summary>
    public class FightOnIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.PersuadeSurrender;
        // 玩家选项名：拼死一战
        public override string DisplayName => LWNTextHelper.ResolveText("LWN_ui_option_surrender_fight_on", "Fight to the death");
        public override NegotiationGoalType? Goal => null; // 即时类

        // 对话选项前缀：死战
        public override string GetDialoguePrefix(string actionParam = null) => LWNTextHelper.ResolveText("LWN_ui_prefix_surrender_fight_on", "[Fight on]");

        /// <summary>
        /// 投降谈判破裂 → 对话结束后重回战斗。
        /// 由 PlayerSurrenderThreatenIntent.OnFail / ResolveNpcSurrenderIntent.refuse / FightOnIntent.OnInstant 设置，
        /// ResetCrimeDialogueOnConversationEndPatch.Postfix 消费。
        /// </summary>
        internal static Agent PendingSurrenderRefusedAgent;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.InRealScene && ctx.Agent != null)
                return Eligibility.Show();
            return Eligibility.Hide();
        }

        public override void OnInstant(IntentContext ctx)
        {
            // 谈判破裂 → 标记战后重回战斗（不在此处发事件，对话尚未结束）
            PendingSurrenderRefusedAgent = ctx.Agent;
            DebugLogger.Log("[Combat] FightOn: 谈判破裂，标记战后重回战斗");
        }
    }

    /// <summary>
    /// 认输 Intent 共享工具方法。
    /// </summary>
    internal static class SurrenderIntentHelper
    {
        /// <summary>
        /// 查找认输关联的 WorldEvent 并结案。
        /// 攻击 NPC 本身就是犯罪（PendingWorldEvent 已在 Confrontation 阶段），
        /// 认输交赎金/求饶/威胁成功后应了结此案。
        /// </summary>
        internal static void ResolveSurrenderWorldEvent(string resolvedBy)
        {
            try
            {
                var settlement = Settlement.CurrentSettlement ?? Hero.MainHero?.CurrentSettlement;
                if (settlement == null) return;

                var evt = WorldEventStore.FindOnGoing(settlement.StringId);
                if (evt == null)
                {
                    DebugLogger.Log("[Combat] ResolveSurrenderWorldEvent: no ongoing event found");
                    return;
                }
                if (evt.Stage == EventStage.Resolved)
                {
                    DebugLogger.Log($"[Combat] ResolveSurrenderWorldEvent: event {evt.EventId} already resolved");
                    return;
                }

                WorldEventStore.TransitionStage(evt, EventStage.Resolved);
                evt.ResolvedBy = resolvedBy;
                TheftLedger.MarkCleared(evt.TargetSettlementId);
                DebugLogger.Log($"[Combat] ResolveSurrenderWorldEvent: event {evt.EventId} resolved as '{resolvedBy}'");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Combat] ResolveSurrenderWorldEvent failed: {ex.Message}");
            }
        }
    }
}
