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
        /// <summary>
        /// 查 XML 模板 + 解析占位符。所有占位符统一走 PlaceholderResolver。
        /// targetName / itemName 为 Mission 层脉冲上下文，传 null 时对应占位符解析为空字符串。
        ///
        /// 两阶段回落：① XML（LWN_speech_{templateId}）→ ② NarrativeResolver（仅当 narrativeFallback 非 null）。
        /// 均未命中返回 null，由调用方提供硬编码兜底（?? 运算符）。
        /// </summary>
        public static string Resolve(string templateId, Hero speaker, Hero listener = null,
            WorldEvent evt = null, string targetName = null, string itemName = null,
            NarrativeFilters narrativeFallback = null, CharacterObject speakerCharacter = null)
        {
            // ① XML：查 LWN_speech_{templateId}
            string xmlKey = $"LWN_speech_{templateId.ToLower()}";
            string xmlText = LWNTextHelper.TryResolveText(xmlKey);

            if (!string.IsNullOrEmpty(xmlText))
            {
                var r = new PlaceholderResolver(evt, speaker, listener, targetName, itemName, speakerCharacter);
                return r.Resolve(xmlText);
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
