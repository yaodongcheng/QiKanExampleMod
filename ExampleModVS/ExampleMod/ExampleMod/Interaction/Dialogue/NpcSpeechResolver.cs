using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    /// <summary>
    /// NPC 台词模板的统一查询入口。
    /// 走 XML 本地化系统：{=LWN_speech_*} → PlaceholderResolver 填占位符。
    /// PlaceholderResolver 的核心能力（WorldEvent 语境、Campaign 层占位符）完整保留。
    /// </summary>
    public static class NpcSpeechResolver
    {
        /// <summary>单个模板的变体池上限：V0 基础模板 + _v1.._vN（N = MaxSpeechVariants - 1）</summary>
        private const int MaxSpeechVariants = 4;

        /// <summary>
        /// 查 XML 模板 + 解析占位符。所有占位符统一走 PlaceholderResolver。
        /// targetName / itemName 为 Mission 层脉冲上下文，传 null 时对应占位符解析为空字符串。
        ///
        /// 两阶段回落：① XML（LWN_speech_{templateId}）→ ② NarrativeResolver（仅当 narrativeFallback 非 null）。
        /// 均未命中返回 null，由调用方提供硬编码兜底（?? 运算符）。
        ///
        /// variantSeed：传入时启用变体池——先统计 LWN_speech_{id} 及 _v1.._vN 实际存在的条数，
        /// 按 seed % 条数 选一条。种子传 agent Index：同一 agent 同一情景台词稳定（人格一致），
        /// 不同 agent 选不同变体 → 同屏多 NPC 同时冒泡时不再全员复读同一句。
        /// </summary>
        public static string Resolve(string templateId, Hero speaker, Hero listener = null,
            WorldEvent evt = null, string targetName = null, string itemName = null,
            NarrativeFilters narrativeFallback = null, CharacterObject speakerCharacter = null,
            uint? variantSeed = null)
        {
            // ① XML：查 LWN_speech_{templateId}；variantSeed 提供时按变体池选条（V0 保底 + _v1.._vN）
            string xmlKey = $"LWN_speech_{templateId.ToLower()}";
            string key = xmlKey;
            if (variantSeed.HasValue)
            {
                int count = 1;
                while (count < MaxSpeechVariants && LWNTextHelper.TryResolveText($"{xmlKey}_v{count}") != null)
                    count++;
                int pick = (int)(variantSeed.Value % (uint)count);
                if (pick > 0) key = $"{xmlKey}_v{pick}";
            }

            // TryResolveText 只判 key 存不存在——不拿它的返回值做模板，因为 TextObject.ToString()
            // 在未设变量时会把 {SPEAKER_PLAYER_ADDR}/{TARGET} 等标记吞成空串。
            // 实际解析走 LWNTextHelper.Resolve：TextObject + SetTextVariable → ToString() 正确替换。
            if (LWNTextHelper.TryResolveText(key) != null)
            {
                var r = new PlaceholderResolver(evt, speaker, listener, targetName, itemName, speakerCharacter);
                return LWNTextHelper.Resolve(key, r);
            }

            // ② 回落 NarrativeResolver
            if (narrativeFallback != null)
            {
                string narrativeText = NarrativeResolver.TryResolveText(narrativeFallback);
                if (!string.IsNullOrEmpty(narrativeText))
                {
                    var r = new PlaceholderResolver(evt, speaker, listener, targetName, itemName, speakerCharacter);
                    return r.Resolve(narrativeText);
                }
            }

            // 均未命中 → null，调用方 ?? 硬编码兜底
            return null;
        }
    }
}
