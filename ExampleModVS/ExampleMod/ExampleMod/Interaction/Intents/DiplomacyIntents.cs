using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    // ── 登庸：招募流浪者入家族（对抗类）──
    public class RecruitWandererIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.RecruitHero; } }
        public override string DisplayName { get { return "【登庸】 招入麾下"; } }
        public override string ToolTip { get { return "邀请这位浪人加入你的家族"; } }
        public override NegotiationGoalType? Goal { get { return NegotiationGoalType.RecruitHero; } }
        public override NegotiationTactic Tactic { get { return NegotiationTactic.Flatter; } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
            if (!ctx.IsHero || !ctx.IsWanderer) return Eligibility.Hide();
            if (ctx.Speaker.Clan == Clan.PlayerClan) return Eligibility.Hide();
            if (ctx.OnCooldown(Goal.Value)) return Eligibility.Grey($"对方暂不愿再谈（还需 {ctx.CooldownDaysLeft(Goal.Value)} 天）");
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            AddCompanionAction.Apply(Clan.PlayerClan, ctx.Speaker);
            InformationManager.DisplayMessage(new InformationMessage($"{ctx.Speaker.Name} 加入了你的家族！", Colors.Green));
        }
    }

    // ── 劝诱：敌将倒戈（对抗类）──
    public class DefectEnemyIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.RecruitHero; } }
        public override string DisplayName { get { return "【劝诱】 劝说倒戈"; } }
        public override string ToolTip { get { return "良禽择木而栖——劝敌方领主弃暗投明"; } }
        public override NegotiationGoalType? Goal { get { return NegotiationGoalType.DefectFaction; } }
        public override NegotiationTactic Tactic { get { return NegotiationTactic.Flatter; } }
        public override int FailRelationPenalty { get { return 4; } }
        public override float CooldownDays { get { return 8f; } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
            if (!ctx.IsHero || !ctx.EnemyFaction) return Eligibility.Hide();
            if (ctx.Speaker == ctx.Speaker.MapFaction?.Leader) return Eligibility.Hide(); // 君主不可被简单劝降
            if (Clan.PlayerClan == null || Clan.PlayerClan.Kingdom == null)
                return Eligibility.Grey("你尚无王国可供其投奔");
            if (!ctx.IsClanLeader) return Eligibility.Grey("需先说动其族长，方能撼动整族");
            if (ctx.OnCooldown(Goal.Value)) return Eligibility.Grey($"风声已紧，暂难再谈（还需 {ctx.CooldownDaysLeft(Goal.Value)} 天）");
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            V.JoinDefect(ctx.Speaker.Clan, ctx.Speaker.Clan.Kingdom, Clan.PlayerClan.Kingdom);
            InformationManager.DisplayMessage(new InformationMessage($"{ctx.Speaker.Name} 率众归附了你的阵营！", Colors.Green));
        }
    }

    // ── 策反：同阵营领主造反脱离（对抗类）──
    public class BetrayalIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.Betrayal; } }
        public override string DisplayName { get { return "【策反】 密谋造反"; } }
        public override string ToolTip { get { return "煽动同阵营的领主脱离现主，另立门户"; } }
        public override NegotiationGoalType? Goal { get { return NegotiationGoalType.DefectFaction; } }
        public override NegotiationTactic Tactic { get { return NegotiationTactic.Reason; } }
        public override int FailRelationPenalty { get { return 6; } }
        public override float CooldownDays { get { return 12f; } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
            if (!ctx.IsHero || !ctx.SameFaction) return Eligibility.Hide();
            if (ctx.Speaker == Hero.MainHero || !ctx.Speaker.IsLord) return Eligibility.Hide();
            if (!ctx.IsClanLeader) return Eligibility.Hide();
            if (ctx.OnCooldown(Goal.Value)) return Eligibility.Grey($"他仍在为上次的密谈心惊（还需 {ctx.CooldownDaysLeft(Goal.Value)} 天）");
            if (ctx.Relation < 10) return Eligibility.Grey("交情不足，他不敢与你密谋");
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            ChangeKingdomAction.ApplyByLeaveKingdom(ctx.Speaker.Clan);
            InformationManager.DisplayMessage(new InformationMessage($"{ctx.Speaker.Name} 决意脱离现主，另立旗帜！", Colors.Green));
        }
    }

    // ── 请求军资：向主君要钱（对抗类）──
    public class RequestFundsIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.RequestFunds; } }
        public override string DisplayName { get { return "【索要】 请求军资"; } }
        public override string ToolTip { get { return "向主君请求调拨资金"; } }
        public override NegotiationGoalType? Goal { get { return NegotiationGoalType.Exaction; } }
        public override NegotiationTactic Tactic { get { return NegotiationTactic.Plead; } }
        public override int FailRelationPenalty { get { return 2; } }
        public override float CooldownDays { get { return 4f; } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
            if (!ctx.IsHero) return Eligibility.Hide();
            bool isMyLiege = ctx.IsLiege || (Clan.PlayerClan != null && ctx.Speaker == Clan.PlayerClan.Leader && ctx.Speaker != Hero.MainHero);
            if (!isMyLiege) return Eligibility.Hide();
            if (ctx.OnCooldown(Goal.Value)) return Eligibility.Grey($"国库刚拨过，暂难再请（还需 {ctx.CooldownDaysLeft(Goal.Value)} 天）");
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            int amount = Math.Min(ctx.Speaker.Gold / 2, 5000);
            if (amount < 100) amount = 100;
            int actual = AgentControlHelper.TransferGold(ctx.Speaker, Hero.MainHero, amount);
            InformationManager.DisplayMessage(new InformationMessage($"主君拨下军资 {actual} 第纳尔。", Colors.Green));
        }
    }

    // ── 仕官：玩家自由身请求加入对方王国（对抗类）──
    public class RequestWorkIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.RequestWork; } }
        public override string DisplayName { get { return "【仕官】 请求奉公"; } }
        public override string ToolTip { get { return "请求加入对方的王国，为其效力"; } }
        public override NegotiationGoalType? Goal { get { return NegotiationGoalType.JoinInFaction; } }
        public override NegotiationTactic Tactic { get { return NegotiationTactic.Flatter; } }
        public override float CooldownDays { get { return 6f; } }

        private static Kingdom TargetKingdom(IntentContext ctx)
        {
            if (ctx.Speaker == null) return null;
            return ctx.Speaker.Clan != null ? ctx.Speaker.Clan.Kingdom : (ctx.Speaker.MapFaction as Kingdom);
        }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
            if (!ctx.IsHero) return Eligibility.Hide();
            if (!ctx.PlayerHasNoKingdom) return Eligibility.Hide();   // 已有王国不能再仕官
            if (TargetKingdom(ctx) == null) return Eligibility.Grey("对方并无主家可引荐");
            if (ctx.OnCooldown(Goal.Value)) return Eligibility.Grey($"对方尚在考量（还需 {ctx.CooldownDaysLeft(Goal.Value)} 天）");
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            Kingdom kingdom = TargetKingdom(ctx);
            if (kingdom != null && Clan.PlayerClan != null)
            {
                ChangeKingdomAction.ApplyByJoinToKingdom(Clan.PlayerClan, kingdom);
                InformationManager.DisplayMessage(new InformationMessage($"你已入仕 {kingdom.Name}，为其效力。", Colors.Green));
            }
        }
    }
}
