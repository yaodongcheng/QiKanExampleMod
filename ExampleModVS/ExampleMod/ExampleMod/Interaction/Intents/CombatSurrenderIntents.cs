using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 玩家认输 → 交钱保命。无检定，必定执行。
    /// 正常罚金 200G；counteroffer 后罚金翻倍 400G。
    /// 后果：罚金 + 荣誉 -1 + 勇敢 -1 + 战斗结束。
    /// </summary>
    public class PlayerSurrenderPayIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.PersuadeSurrender;
        public override string DisplayName => "（交出钱袋）";
        public override NegotiationGoalType? Goal => null; // 无条件，不检定

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (Mission.Current == null) return Eligibility.Hide();

            // counteroffer 后：罚金翻倍
            bool isCounteroffer = ctx.ActionParam == "counteroffer_beg"
                               || ctx.ActionParam == "counteroffer_threaten";
            int baseCost = isCounteroffer ? 400 : 200;
            int penalty = Math.Min(Hero.MainHero.Gold, baseCost);

            if (Hero.MainHero.Gold < baseCost)
                return Eligibility.Grey($"钱不够（需要 {baseCost} 第纳尔，你只有 {Hero.MainHero.Gold}）");
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            bool isCounteroffer = ctx.ActionParam == "counteroffer_beg"
                               || ctx.ActionParam == "counteroffer_threaten";
            int baseCost = isCounteroffer ? 400 : 200;
            int penalty = Math.Min(Hero.MainHero.Gold, baseCost);
            if (penalty > 0)
                AgentControlHelper.TransferGold(Hero.MainHero, null, penalty);

            // ② 荣誉惩罚
            int honor = Hero.MainHero.GetTraitLevel(DefaultTraits.Honor);
            Hero.MainHero.SetTraitLevel(DefaultTraits.Honor, honor - 1);
            int valor = Hero.MainHero.GetTraitLevel(DefaultTraits.Valor);
            Hero.MainHero.SetTraitLevel(DefaultTraits.Valor, valor - 1);

            // ③ 战斗结束
            AgentAIController.Instance?.SendEventToAgent(
                ctx.Agent, "event_player_surrendered", Agent.Main, ctx.Agent);

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
        public override string DisplayName => "求你放过我……";
        public override NegotiationGoalType? Goal => NegotiationGoalType.ResolveConflict_Explain;
        public override NegotiationTactic Tactic => NegotiationTactic.Flatter;
        public override float CooldownDays => 0f; // 每次战斗仅一次
        public override bool ReofferOnFail => true; // 🆕 失败后重新渲染选项

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (Mission.Current == null) return Eligibility.Hide();
            // 已经求饶失败过了 → 置灰
            if (ctx.ActionParam == "counteroffer_beg")
                return Eligibility.Grey("已经求饶过了");
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            // 魅力说服成功：免单放人，但荣誉仍 -1（求饶本身就不光彩）
            int honor = Hero.MainHero.GetTraitLevel(DefaultTraits.Honor);
            Hero.MainHero.SetTraitLevel(DefaultTraits.Honor, honor - 1);

            AgentAIController.Instance?.SendEventToAgent(
                ctx.Agent, "event_player_surrendered", Agent.Main, ctx.Agent);

            DebugLogger.Log($"[Combat] SurrenderBeg SUCCESS: 免单放人, honor={honor}→{honor - 1}");
        }

        public override void OnFail(IntentContext ctx)
        {
            base.OnFail(ctx); // 正常掉好感 + 冷却

            // ⭐ 关键：不扣钱、不扣属性、不结束战斗！
            // 只标记 counteroffer 状态，让下一轮 PayIntent 读到翻倍的罚金。
            // ReofferOnFail=true → ResolveAdversarialIntent 会调 RefreshInitialOptions()
            // → BuildOptionVMs 重新跑所有 Evaluate → PayIntent 读 ActionParam 显示 400G
            ctx.ActionParam = "counteroffer_beg";

            DebugLogger.Log($"[Combat] SurrenderBeg FAIL: counteroffer — 罚金翻倍至 400G");
        }
    }

    /// <summary>
    /// 玩家认输 → 破口大骂虚张声势。检定通过 = NPC 怂了放人，失败 = 继续打。
    /// </summary>
    public class PlayerSurrenderThreatenIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.PersuadeSurrender;
        public override string DisplayName => "你这条狗！……";
        public override NegotiationGoalType? Goal => NegotiationGoalType.ResolveConflict_Intimidate;
        public override NegotiationTactic Tactic => NegotiationTactic.Threaten;
        public override float CooldownDays => 0f;

        /// <summary>威胁失败后延迟进入战斗的 Agent（对标 AccountabilityIntents.ThreatIntent 模式）</summary>
        internal static Agent PendingCombatAgent;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (Mission.Current == null) return Eligibility.Hide();
            // counteroffer 阶段：已经求饶失败过了，威胁选项不可用
            if (ctx.ActionParam == "counteroffer_beg")
                return Eligibility.Grey("已经求饶过了");
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            // 成功：NPC 怂了，不付钱，荣誉 -1（不扣勇敢——至少还有骨气）
            int honor = Hero.MainHero.GetTraitLevel(DefaultTraits.Honor);
            Hero.MainHero.SetTraitLevel(DefaultTraits.Honor, honor - 1);

            AgentAIController.Instance?.SendEventToAgent(
                ctx.Agent, "event_player_surrendered", Agent.Main, ctx.Agent);

            DebugLogger.Log($"[Combat] SurrenderThreaten SUCCESS: NPC 怂了, honor={honor}→{honor - 1}");
        }

        public override void OnFail(IntentContext ctx)
        {
            // 失败：NPC 暴怒，战斗继续（对话关闭后由 Patch 消费 PendingCombatAgent）
            PendingCombatAgent = ctx.Agent;

            AgentAIController.Instance?.SendEventToAgent(
                ctx.Agent, "event_surrender_refused");

            DebugLogger.Log($"[Combat] SurrenderThreaten FAIL: NPC 暴怒，继续战斗");
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

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (Mission.Current != null && ctx.Agent != null)
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
                    AgentAIController.Instance?.SendEventToAgent(
                        ctx.Agent, "event_surrender_accepted");
                    DebugLogger.Log("[Combat] ResolveNpcSurrender: accept (+2 relation)");
                    break;

                case "humiliate":
                    // 侮辱：好感 -10 + 嗑头动画
                    if (ctx.Speaker != null)
                        ChangeRelationAction.ApplyPlayerRelation(ctx.Speaker, -10, false, true);
                    AgentControlHelper.ForcePlayAction(ctx.Agent, "act_kneel");
                    AgentAIController.Instance?.SendEventToAgent(
                        ctx.Agent, "event_surrender_accepted");
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
                    AgentAIController.Instance?.SendEventToAgent(
                        ctx.Agent, "event_surrender_accepted");
                    DebugLogger.Log("[Combat] ResolveNpcSurrender: ransom");
                    break;

                case "refuse":
                    // 拒绝认输：NPC 意图回到 Fighting，继续战斗
                    AgentAIController.Instance?.SendEventToAgent(
                        ctx.Agent, "event_surrender_refused");
                    AgentHudMissionView.AgentSay(ctx.Agent, "不——！！");
                    DebugLogger.Log("[Combat] ResolveNpcSurrender: refuse (back to Fighting)");
                    break;
            }
        }
    }
}
