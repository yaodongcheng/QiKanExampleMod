using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    // ── 求婚（对抗类）──
    public class ProposeMarriageIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.ProposalMarriage; } }
        // 求婚意图名：向对方表达爱意
        public override string DisplayName { get { return LWNTextHelper.ResolveText("LWN_intent_social_propose_marriage_name", "Proposal: Declare your affection"); } }
        // 求婚意图提示：表达爱意，希望对方接受求婚
        public override string ToolTip { get { return LWNTextHelper.ResolveText("LWN_intent_social_propose_marriage_tooltip", "Express your love, hoping they accept your proposal"); } }
        public override NegotiationGoalType? Goal { get { return NegotiationGoalType.ProposeMarriage; } }
        public override NegotiationTactic Tactic { get { return NegotiationTactic.Flatter; } }
        public override int FailRelationPenalty { get { return 5; } }
        public override float CooldownDays { get { return 10f; } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
            if (!ctx.IsHero) return Eligibility.Hide();
            if (!ctx.OppositeSex || ctx.IsMarried || Hero.MainHero.Spouse != null) return Eligibility.Hide();
            // 求婚冷却置灰：对方仍在回避，显示剩余天数
            if (ctx.OnCooldown(Goal.Value)) return Eligibility.Grey(LWNTextHelper.ResolveCompound("LWN_intent_social_propose_marriage_cooldown",
                ("DAYS", ctx.CooldownDaysLeft(Goal.Value).ToString())));
            // 求婚置灰：交情未到，需先培养感情
            if (ctx.Relation < 0) return Eligibility.Grey(LWNTextHelper.ResolveText("LWN_intent_social_propose_marriage_relation_low", "Not enough closeness - build the bond first"));
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            MarriageAction.Apply(ctx.Listener, ctx.Speaker);
        }
    }

    // ── 送礼（即时类）──
    public class GiftIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.Gift; } }
        // 送礼意图名：赠予物品
        public override string DisplayName { get { return LWNTextHelper.ResolveText("LWN_intent_social_gift_name", "Gift: Give a present"); } }
        // 送礼意图提示：贵重或投其所好的物品效果更佳
        public override string ToolTip { get { return LWNTextHelper.ResolveText("LWN_intent_social_gift_tooltip", "Give a gift to improve relations (valuable or well-suited gifts work best)"); } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
            return ctx.IsHero ? Eligibility.Show() : Eligibility.Hide();
        }

        public override void OnInstant(IntentContext ctx)
        {
            ctx.Controller.OpenGiftMenu(ctx.Speaker);
        }
    }

    // ── 茶席（即时类，按性格出好感）──
    public class TeaCeremonyIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.TeaCeremony; } }
        // 茶席意图名：邀对方共饮一盏
        public override string DisplayName { get { return LWNTextHelper.ResolveText("LWN_intent_social_tea_name", "Tea: Share a cup"); } }
        // 茶席意图提示：依对方性情增进交情
        public override string ToolTip { get { return LWNTextHelper.ResolveText("LWN_intent_social_tea_tooltip", "Invite them to tea, deepening the bond to their temperament"); } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
            if (!ctx.IsHero) return Eligibility.Hide();
            // 茶席置灰：敌对之人不会共饮
            if (ctx.EnemyFaction) return Eligibility.Grey(LWNTextHelper.ResolveText("LWN_intent_social_tea_enemy", "An enemy would never share tea with you"));
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            int delta = 3;
            var p = ctx.Profile;
            if (p != null)
            {
                if (p.ActStyle == NPCProfile.ActStyleEnum.Considerate) delta += 3;   // 稳重者重礼节
                if (p.theImportanceOfFriendship == NPCProfile.FriendshipImportanceEnum.Important) delta += 2;
                if (p.AlcoholDesire == NPCProfile.AlcoholDesireEnum.Alcoholic) delta -= 1; // 酒鬼对茶无感
            }
            if (delta < 1) delta = 1;
            ChangeRelationAction.ApplyPlayerRelation(ctx.Speaker, delta);
            // 共饮一盏飘字：与 {NAME} 共饮，关系 +{DELTA}
            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_intent_social_tea_shared",
                ("NAME", ctx.Speaker.Name.ToString()), ("DELTA", delta.ToString())), Colors.Green));
        }
    }

    // ── 切磋（即时类，发起不致命战斗）──
    public class SparIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.Spar; } }
        // 交手意图名：切磋武艺
        public override string DisplayName { get { return LWNTextHelper.ResolveText("LWN_intent_social_spar_name", "Spar: Test your skill"); } }
        // 交手意图提示：点到为止不伤性命
        public override string ToolTip { get { return LWNTextHelper.ResolveText("LWN_intent_social_spar_tooltip", "A friendly bout, no blood drawn"); } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
            if (ctx.Agent == null) return Eligibility.Hide();
            // Hero：未受伤的领主可切磋；战场敌人也可
            if (ctx.IsHero)
            {
                // 交手置灰：对方负伤不宜动武
                if (ctx.Speaker.IsWounded) return Eligibility.Grey(LWNTextHelper.ResolveText("LWN_intent_social_spar_wounded", "They are wounded - no fighting"));
                if (!ctx.Speaker.IsLord) return Eligibility.Hide();
                return Eligibility.Show();
            }
            return ctx.IsEnemyAgent ? Eligibility.Show() : Eligibility.Hide();
        }

        public override void OnInstant(IntentContext ctx)
        {
            AgentAIController.Instance.SendEventToAgent(ctx.Agent, "order_attack", Agent.Main);
            ctx.Controller._vm.Close();
        }
    }
}
