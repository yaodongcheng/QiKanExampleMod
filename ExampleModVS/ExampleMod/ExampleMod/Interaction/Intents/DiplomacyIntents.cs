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
        // 登庸意图名：招募流浪者加入家族
        public override string DisplayName { get { return LWNTextHelper.ResolveText("LWN_intent_diplomacy_recruit_wanderer_name", "Recruit: Swear into my service"); } }
        // 登庸意图提示：邀请流浪者加入家族
        public override string ToolTip { get { return LWNTextHelper.ResolveText("LWN_intent_diplomacy_recruit_wanderer_tooltip", "Invite this wanderer to join your clan"); } }
        public override NegotiationGoalType? Goal { get { return NegotiationGoalType.RecruitHero; } }
        public override NegotiationTactic Tactic { get { return NegotiationTactic.Flatter; } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
            if (!ctx.IsHero || !ctx.IsWanderer) return Eligibility.Hide();
            if (ctx.Speaker.Clan == Clan.PlayerClan) return Eligibility.Hide();
            // 登庸冷却置灰：对方暂不愿再谈，显示剩余天数
            if (ctx.OnCooldown(Goal.Value)) return Eligibility.Grey(LWNTextHelper.ResolveCompound("LWN_intent_diplomacy_recruit_wanderer_cooldown",
                ("DAYS", ctx.CooldownDaysLeft(Goal.Value).ToString())));
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            AddCompanionAction.Apply(Clan.PlayerClan, ctx.Speaker);
            // 登庸成功飘字：{NAME} 加入了玩家的家族
            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_intent_diplomacy_recruit_success", ("NAME", ctx.Speaker.Name.ToString())), Colors.Green));
        }
    }

    // ── 劝诱：敌将倒戈（对抗类）──
    public class DefectEnemyIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.RecruitHero; } }
        // 劝诱意图名：劝说敌方领主倒戈
        public override string DisplayName { get { return LWNTextHelper.ResolveText("LWN_intent_diplomacy_defect_enemy_name", "Sway: Turn them to our side"); } }
        // 劝诱意图提示：劝敌方领主弃暗投明
        public override string ToolTip { get { return LWNTextHelper.ResolveText("LWN_intent_diplomacy_defect_enemy_tooltip", "Persuade an enemy lord to defect to your faction"); } }
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
                // 劝诱置灰：玩家尚无王国，对方无处可投奔
                return Eligibility.Grey(LWNTextHelper.ResolveText("LWN_intent_diplomacy_defect_no_kingdom", "You have no kingdom for them to defect to"));
            // 劝诱置灰：需先说动族长才能撼动整族
            if (!ctx.IsClanLeader) return Eligibility.Grey(LWNTextHelper.ResolveText("LWN_intent_diplomacy_defect_need_clan_leader", "You must win over their clan leader first"));
            // 劝诱冷却置灰：风声已紧，暂难再谈，显示剩余天数
            if (ctx.OnCooldown(Goal.Value)) return Eligibility.Grey(LWNTextHelper.ResolveCompound("LWN_intent_diplomacy_defect_cooldown",
                ("DAYS", ctx.CooldownDaysLeft(Goal.Value).ToString())));
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            V.JoinDefect(ctx.Speaker.Clan, ctx.Speaker.Clan.Kingdom, Clan.PlayerClan.Kingdom);
            // 劝诱成功飘字：{NAME} 率众归附了玩家的阵营
            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_intent_diplomacy_defect_success", ("NAME", ctx.Speaker.Name.ToString())), Colors.Green));
        }
    }

    // ── 策反：同阵营领主造反脱离（对抗类）──
    public class BetrayalIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.Betrayal; } }
        // 策反意图名：同阵营领主密谋造反
        public override string DisplayName { get { return LWNTextHelper.ResolveText("LWN_intent_diplomacy_betrayal_name", "Sedition: Plot a revolt"); } }
        // 策反意图提示：煽动同阵营领主脱离现主
        public override string ToolTip { get { return LWNTextHelper.ResolveText("LWN_intent_diplomacy_betrayal_tooltip", "Urge a fellow lord to break from their liege"); } }
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
            // 策反冷却置灰：对方仍为上次密谈心惊，显示剩余天数
            if (ctx.OnCooldown(Goal.Value)) return Eligibility.Grey(LWNTextHelper.ResolveCompound("LWN_intent_diplomacy_betrayal_cooldown",
                ("DAYS", ctx.CooldownDaysLeft(Goal.Value).ToString())));
            // 策反置灰：交情不足，对方不敢与你密谋
            if (ctx.Relation < 10) return Eligibility.Grey(LWNTextHelper.ResolveText("LWN_intent_diplomacy_betrayal_relation_low", "Not close enough for him to conspire with you"));
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            ChangeKingdomAction.ApplyByLeaveKingdom(ctx.Speaker.Clan);
            // 策反成功飘字：{NAME} 脱离现主另立旗帜
            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_intent_diplomacy_betrayal_success", ("NAME", ctx.Speaker.Name.ToString())), Colors.Green));
        }
    }

    // ── 请求军资：向主君要钱（对抗类）──
    public class RequestFundsIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.RequestFunds; } }
        // 请求军资意图名：向主君索要军费
        public override string DisplayName { get { return LWNTextHelper.ResolveText("LWN_intent_diplomacy_request_funds_name", "Request: Ask for funds"); } }
        // 请求军资意图提示：向主君请求调拨资金
        public override string ToolTip { get { return LWNTextHelper.ResolveText("LWN_intent_diplomacy_request_funds_tooltip", "Request funding from your liege"); } }
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
            // 请求军资冷却置灰：国库刚拨过，暂难再请，显示剩余天数
            if (ctx.OnCooldown(Goal.Value)) return Eligibility.Grey(LWNTextHelper.ResolveCompound("LWN_intent_diplomacy_request_funds_cooldown",
                ("DAYS", ctx.CooldownDaysLeft(Goal.Value).ToString())));
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            int amount = Math.Min(ctx.Speaker.Gold / 2, 5000);
            if (amount < 100) amount = 100;
            int actual = AgentControlHelper.TransferGold(ctx.Speaker, Hero.MainHero, amount);
            // 军资到账飘字：主君拨下 {AMOUNT} 第纳尔军资
            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_intent_diplomacy_funds_received", ("AMOUNT", actual.ToString())), Colors.Green));
        }
    }

    // ── 仕官：玩家自由身请求加入对方王国（对抗类）──
    public class RequestWorkIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.RequestWork; } }
        // 仕官意图名：自由身请求加入对方王国
        public override string DisplayName { get { return LWNTextHelper.ResolveText("LWN_intent_diplomacy_request_work_name", "Serve: Offer your service"); } }
        // 仕官意图提示：请求加入对方王国效力
        public override string ToolTip { get { return LWNTextHelper.ResolveText("LWN_intent_diplomacy_request_work_tooltip", "Ask to join their kingdom and serve them"); } }
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
            // 仕官置灰：对方并无主家可引荐
            if (TargetKingdom(ctx) == null) return Eligibility.Grey(LWNTextHelper.ResolveText("LWN_intent_diplomacy_request_work_no_kingdom", "They have no liege to recommend you to"));
            // 仕官冷却置灰：对方尚在考量，显示剩余天数
            if (ctx.OnCooldown(Goal.Value)) return Eligibility.Grey(LWNTextHelper.ResolveCompound("LWN_intent_diplomacy_request_work_cooldown",
                ("DAYS", ctx.CooldownDaysLeft(Goal.Value).ToString())));
            return Eligibility.Show();
        }

        public override void OnSuccess(IntentContext ctx)
        {
            Kingdom kingdom = TargetKingdom(ctx);
            if (kingdom != null && Clan.PlayerClan != null)
            {
                ChangeKingdomAction.ApplyByJoinToKingdom(Clan.PlayerClan, kingdom);
                // 仕官成功飘字：玩家已入仕 {NAME} 王国
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound("LWN_intent_diplomacy_serve_success", ("NAME", kingdom.Name.ToString())), Colors.Green));
            }
        }
    }
}
