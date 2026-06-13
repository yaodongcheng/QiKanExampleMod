using LivingWorldNpcs;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Story
{
    // ── 求婚（对抗类）──
    public class ProposeMarriageIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.ProposalMarriage; } }
        public override string DisplayName { get { return "【求婚】 表达爱意"; } }
        public override string ToolTip { get { return "表达你的爱意，希望对方能接受你的求婚"; } }
        public override NegotiationGoalType? Goal { get { return NegotiationGoalType.ProposeMarriage; } }
        public override NegotiationTactic Tactic { get { return NegotiationTactic.Flatter; } }
        public override int FailRelationPenalty { get { return 5; } }
        public override float CooldownDays { get { return 10f; } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (!ctx.IsHero) return Eligibility.Hide();
            if (!ctx.OppositeSex || ctx.IsMarried || Hero.MainHero.Spouse != null) return Eligibility.Hide();
            if (ctx.OnCooldown(Goal.Value)) return Eligibility.Grey($"对方仍在回避你（还需 {ctx.CooldownDaysLeft(Goal.Value)} 天）");
            if (ctx.Relation < 0) return Eligibility.Grey("门第悬殊、情分未到，先培养感情");
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            MarriageAction.Apply(ctx.Player, ctx.Hero);
        }
    }

    // ── 送礼（即时类）──
    public class GiftIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.Gift; } }
        public override string DisplayName { get { return "【送礼】 赠予物品"; } }
        public override string ToolTip { get { return "赠送物品以提升关系（贵重 / 投其所好 效果更佳）"; } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            return ctx.IsHero ? Eligibility.Show() : Eligibility.Hide();
        }

        public override void OnInstant(IntentContext ctx)
        {
            ctx.Controller.OpenGiftMenu(ctx.Hero);
        }
    }

    // ── 茶席（即时类，按性格出好感）──
    public class TeaCeremonyIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.TeaCeremony; } }
        public override string DisplayName { get { return "【茶席】 共饮一盏"; } }
        public override string ToolTip { get { return "邀请对方共饮，依其性情增进交情"; } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (!ctx.IsHero) return Eligibility.Hide();
            if (ctx.EnemyFaction) return Eligibility.Grey("敌对之人，岂会与你共饮");
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
            ChangeRelationAction.ApplyPlayerRelation(ctx.Hero, delta);
            InformationManager.DisplayMessage(new InformationMessage($"你与{ctx.Hero.Name}共饮一盏，关系 +{delta}", Colors.Green));
        }
    }

    // ── 切磋（即时类，发起不致命战斗）──
    public class SparIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.Spar; } }
        public override string DisplayName { get { return "【交手】 切磋武艺"; } }
        public override string ToolTip { get { return "点到为止，不伤性命"; } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.Agent == null) return Eligibility.Hide();
            // Hero：未受伤的领主可切磋；战场敌人也可
            if (ctx.IsHero)
            {
                if (ctx.Hero.IsWounded) return Eligibility.Grey("对方负伤在身，不宜动武");
                if (!ctx.Hero.IsLord) return Eligibility.Hide();
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
