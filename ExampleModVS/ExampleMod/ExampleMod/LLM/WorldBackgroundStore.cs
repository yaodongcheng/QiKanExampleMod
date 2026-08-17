using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 自动世界观总结存储（静态单例，2026-08-17 计划 world-background-auto-summary.md）：
    /// blob（单段世界格局文本）+ fingerprint（生成时快照指纹）+ 战役纪元标记。
    /// 生成/持久化由 <see cref="WorldBackgroundBehavior"/> 负责；读取方只经
    /// <see cref="WorldBackgroundProvider.GetWorldSection"/>（纯字符串查表，线程安全，
    /// 禁在读取路径做引擎对象查找——PlanReplan 在 Task.Run 内构建 prompt）。
    /// </summary>
    public static class WorldBackgroundStore
    {
        /// <summary>世界格局单段文本（=== 世界格局 === 标记后正文；空 = 未生成/生成失败/未配置 LLM）。</summary>
        public static string Blob = "";

        /// <summary>生成时快照指纹（culture/kingdom/关键英雄 StringId 序列 + 语言 id）；变更 → 重新生成。</summary>
        public static string Fingerprint = "";

        /// <summary>生成时的战役纪元标记（Campaign.Current 实例引用）——跨战役污染防护（读档/新档丢弃旧结果）。</summary>
        public static Campaign CurrentCampaignEra;
    }
}
