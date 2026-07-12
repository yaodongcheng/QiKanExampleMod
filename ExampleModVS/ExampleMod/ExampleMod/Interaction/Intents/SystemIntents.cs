using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 系统级 Intent（仅对话 Transition 触发，不出现在交互菜单）。
    /// 四个 Intent 对应旧式 Action（INCREASE_RELATION / DECREASE_RELATION / GIVE_GOLD / TAKE_GOLD），
    /// 迁移到 INTENT 体系统一管理。
    ///
    /// 共同特征：
    ///   - Type = InteractionOptionType.System / Category = InteractionCategory.System
    ///   - Goal = null（即时类，不检定）
    ///   - Evaluate 检查 ctx.ActionParam != null（以此区分对话注入 vs 交互菜单，确保不出现在菜单中）
    ///   - OnInstant 从 ActionParam 解析数值，用默认值兜底
    /// </summary>

    /// <summary>增加与 NPC 的好感度。ActionParam = 增加的数值（默认 5）。</summary>
    public class IncreaseRelationIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.System;
        public override InteractionCategory Category => InteractionCategory.System;
        public override string DisplayName => "【系统】增加好感";
        public override IntentSource Source => IntentSource.Player; // 仅玩家通过对话触发

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.ActionParam)) return Eligibility.Hide();
            if (ctx.Speaker == null) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            int amount = int.TryParse(ctx.ActionParam, out var a) ? a : 5;
            if (amount > 0)
                ChangeRelationAction.ApplyPlayerRelation(ctx.Speaker, amount, false, true);
        }
    }

    /// <summary>减少与 NPC 的好感度。ActionParam = 减少的数值（默认 5）。</summary>
    public class DecreaseRelationIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.System;
        public override InteractionCategory Category => InteractionCategory.System;
        public override string DisplayName => "【系统】减少好感";
        public override IntentSource Source => IntentSource.Player;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.ActionParam)) return Eligibility.Hide();
            if (ctx.Speaker == null) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            int amount = int.TryParse(ctx.ActionParam, out var a) ? a : 5;
            if (amount > 0)
                ChangeRelationAction.ApplyPlayerRelation(ctx.Speaker, -amount, false, true);
        }
    }

    /// <summary>给予玩家金币。ActionParam = 金额（默认 100）。</summary>
    public class GiveGoldIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.System;
        public override InteractionCategory Category => InteractionCategory.System;
        public override string DisplayName => "【系统】给予金币";
        public override IntentSource Source => IntentSource.Player;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.ActionParam)) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            int amount = int.TryParse(ctx.ActionParam, out var a) ? a : 100;
            if (amount > 0)
                GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, amount);
        }
    }

    /// <summary>从玩家收取金币。ActionParam = 金额（默认 100）。</summary>
    public class TakeGoldIntent : IntentBase
    {
        public override InteractionOptionType Type => InteractionOptionType.System;
        public override InteractionCategory Category => InteractionCategory.System;
        public override string DisplayName => "【系统】收取金币";
        public override IntentSource Source => IntentSource.Player;

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.ActionParam)) return Eligibility.Hide();
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            int amount = int.TryParse(ctx.ActionParam, out var a) ? a : 100;
            if (amount > 0)
                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, amount);
        }
    }
}
