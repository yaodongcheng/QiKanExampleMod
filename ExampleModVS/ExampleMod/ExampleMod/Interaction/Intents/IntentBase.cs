using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 意图来源标志 — 决定此意图对谁可用。
    /// </summary>
    [Flags]
    public enum IntentSource
    {
        None    = 0,
        Player  = 1 << 0,  // 玩家可用（菜单选项）
        Npc     = 1 << 1,  // NPC 可用（主动发起）
        Both    = Player | Npc
    }

    /// <summary>
    /// 一个交互意图 = 一个类。只声明三件事：资格规则(Evaluate)、目标类型(Goal)、成败后果(OnSuccess/OnFail/OnInstant)。
    /// 通用机制（成功率公式、掷骰、冷却、台词、置灰）由 IntentResolver / 基类统一提供，零重复。
    /// 加新意图 = 新建一个子类 + 在 IntentRegistry 注册一行。内容包亦可注册自己的意图类。
    ///
    /// NPC 和玩家共享同一套 IntentBase 注册体系——NPC 意图在 OnInstant 中直接操作 AgentBrain 入队行为，
    /// 玩家意图通过菜单点击直接触发。
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

        /// <summary>
        /// 菜单显示文本（默认值）。
        /// - InteractionOptionManager.BuildOptionVMs 主路径：直接显示此文本。
        /// - DialogueInjector 追责路径：JSON 模板优先，DisplayName 仅作兜底（JSON 未配 OptionText 时用）。
        /// </summary>
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

        // ═══ 新增：意图来源标志 ═══

        /// <summary>此意图对谁可用。默认 Both = 玩家和 NPC 都能用。</summary>
        public virtual IntentSource Source => IntentSource.Both;

        /// <summary>此意图响应哪些 AIEvent.EventType。空数组 = 不响应任何事件（纯玩家侧意图）。</summary>
        public virtual string[] TriggerEvents => Array.Empty<string>();

        // ── 子类实现 ──

        /// <summary>三态资格判定。</summary>
        public abstract Eligibility Evaluate(IntentContext ctx);

        /// <summary>
        /// 收到匹配的 EventType 后，检查事件参数是否满足此意图的触发条件。
        /// 基类默认返回 true（EventType 匹配即可）。子类可 override 做更细的判断。
        /// </summary>
        public virtual bool CanHandle(AIEvent aiEvent, IntentContext ctx)
        {
            return true;
        }

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
