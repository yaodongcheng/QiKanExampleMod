using LivingWorldNpcs;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace LivingWorldNpcs.Story
{
    /// <summary>
    /// 一个交互意图 = 一个类。只声明三件事：资格规则(Evaluate)、目标类型(Goal)、成败后果(OnSuccess/OnFail/OnInstant)。
    /// 通用机制（成功率公式、掷骰、冷却、台词、置灰）由 IntentResolver / 基类统一提供，零重复。
    /// 加新意图 = 新建一个子类 + 在 IntentRegistry 注册一行。内容包亦可注册自己的意图类。
    /// </summary>
    public abstract class IntentBase
    {
        /// <summary>UI/分类用的类型标识。</summary>
        public abstract InteractionOptionType Type { get; }

        /// <summary>UI 分组，由 Type 自动推导。仅当同一 Type 确需不同 Category 时才 override。</summary>
        public virtual InteractionCategory Category
        {
            get { return InteractionOptionCategoryMap.GetCategory(Type); }
        }

        /// <summary>菜单显示文本。</summary>
        public abstract string DisplayName { get; }
        public virtual string ToolTip { get { return ""; } }

        /// <summary>
        /// 谈判目标类型。非 null = 对抗类（走单次检定掷骰）；null = 即时类（送礼/茶席/情报，直接 OnInstant）。
        /// </summary>
        public virtual NegotiationGoalType? Goal { get { return null; } }

        /// <summary>对抗类用哪种手段做技能检定（决定查哪个属性）。</summary>
        public virtual NegotiationTactic Tactic { get { return NegotiationTactic.Flatter; } }

        /// <summary>查台词 CSV 的前缀；默认用 Goal 名（即时类用 Type 名）。</summary>
        public virtual string DialogueKey
        {
            get { return Goal.HasValue ? Goal.Value.ToString() : Type.ToString(); }
        }

        /// <summary>失败掉好感（按冒犯程度，子类可调）。</summary>
        public virtual int FailRelationPenalty { get { return 3; } }
        /// <summary>失败后冷却天数。</summary>
        public virtual float CooldownDays { get { return 5f; } }

        /// <summary>对抗类附带的「献礼/出价」价值（喂进成功率公式）。默认 0（纯说服）。</summary>
        public virtual float GetOfferValue(IntentContext ctx) { return 0f; }

        // ── 子类实现 ──

        /// <summary>三态资格判定。</summary>
        public abstract Eligibility Evaluate(IntentContext ctx);

        /// <summary>即时类结算（Goal==null 时调用）。</summary>
        public virtual void OnInstant(IntentContext ctx) { }

        /// <summary>对抗类成功后果。</summary>
        public virtual void OnSuccess(IntentContext ctx) { }

        /// <summary>对抗类失败后果。基类默认：掉好感 + 进冷却。子类可 override 但通常应调 base。</summary>
        public virtual void OnFail(IntentContext ctx)
        {
            if (ctx.Hero != null && FailRelationPenalty > 0)
                ChangeRelationAction.ApplyPlayerRelation(ctx.Hero, -FailRelationPenalty, false, true);
            if (ctx.Hero != null && Goal.HasValue)
                IntentCooldownStore.Set(ctx.Hero, Goal.Value, CooldownDays);
        }
    }
}
