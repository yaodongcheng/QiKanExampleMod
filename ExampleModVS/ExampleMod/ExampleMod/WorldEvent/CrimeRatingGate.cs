using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 犯罪等级的**唯一提升出口**（2026-11-18 用户需求：MCM 选项「犯罪会提升犯罪等级」）。
    ///
    /// 为什么要多这一层：原版 <see cref="ChangeCrimeRatingAction.Apply"/> 不只是加个分数 ——
    /// 反编译实证（1.2.12 / 1.5.x 一致）它还会：
    ///   ① 弹原版提示「你在 {王国} 的犯罪等级上升了」（玩家说的"出来之后显示犯罪了"）；
    ///   ② 犯罪等级一旦越过 <c>CrimeModel.DeclareWarCrimeRatingThreshold</c>，
    ///      当场 <c>DeclareWarAction.ApplyByCrimeRatingChange</c> + 对敌方首领关系 -10
    ///      （玩家说的"然后被宣战了"）。
    /// 有些玩家只想要本 mod 的犯罪玩法（偷窃 / 袭击 / 目击 / 质问 / 赔偿），不想背犯罪记录、
    /// 更不想被整国宣战 ⇒ MCM 关掉开关后，本 mod 触发的**所有**犯罪等级提升一律不落地。
    ///
    /// 🔴 只拦「提升」（delta &gt; 0）。扣减（结案洗罪 / 坐牢释放）不受开关影响：
    ///    · 开关打开 → 正常加分；结案按原逻辑扣回（WorldEvent.RollbackCrimeRating）；
    ///    · 开关关闭 → 一分不加（调用方把 CrimeRatingDelta 留 0），结案时也就不会反向扣分。
    /// 🔴 新增任何"犯罪 → 提升犯罪等级"的调用点时，一律走这里，
    ///    **禁止**再直接调 <c>ChangeCrimeRatingAction.Apply(faction, 正数)</c>。
    /// </summary>
    internal static class CrimeRatingGate
    {
        /// <summary>MCM 开关当前是否允许提升（true = 默认行为）。只读诊断用。</summary>
        public static bool IncreaseAllowed => Settings.Instance.CrimeRatingIncreaseEnabled;

        /// <summary>
        /// 按 MCM 开关决定是否真的提升犯罪等级。
        /// 返回 true = 已提升（调用方可以记 CrimeRatingDelta / 打成功日志）；
        /// 返回 false = 未提升（faction 为空、delta 非正数、或开关关闭）。
        /// 开关关闭时只打一行诊断日志，不改任何游戏状态（不弹提示、不触发宣战）。
        /// </summary>
        public static bool TryApplyIncrease(IFaction faction, float delta, string reason, bool showNotification = true)
        {
            if (faction == null || delta <= 0f) return false;
            if (!Settings.Instance.CrimeRatingIncreaseEnabled)
            {
                DebugLogger.Log($"[CrimeRating] +{delta:F0} suppressed ({reason}) - MCM option 'Crimes Raise Crime Rating' is off; faction={faction.Name}");
                return false;
            }
            ChangeCrimeRatingAction.Apply(faction, delta, showNotification);
            return true;
        }
    }
}