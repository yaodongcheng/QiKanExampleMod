using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// NPC 台词模板的统一 CSV 查询入口。
    /// 查 NpcSpeech.csv 取模板文本 → 委托 PlaceholderResolver 做占位符替换。
    /// PlaceholderResolver 的核心能力（WorldEvent 语境、Campaign 层占位符）完整保留。
    /// </summary>
    public static class NpcSpeechResolver
    {
        /// <summary>
        /// 查模板 + 解析占位符。所有占位符统一走 PlaceholderResolver。
        /// targetName / itemName 为 Mission 层脉冲上下文，传 null 时对应占位符解析为空字符串。
        ///
        /// 两阶段回落：① NpcSpeech.csv 精确 ID 匹配 → ② Narrative.csv（仅当 narrativeFallback 非 null）。
        /// 均未命中返回 null，由调用方提供硬编码兜底（?? 运算符）。
        /// </summary>
        public static string Resolve(string templateId, Hero speaker, Hero listener = null,
            WorldEvent evt = null, string targetName = null, string itemName = null,
            NarrativeFilters narrativeFallback = null)
        {
            // ① 查 NpcSpeech.csv 取模板文本
            string template = LookupTemplate(templateId);
            if (!string.IsNullOrEmpty(template))
            {
                var r = new PlaceholderResolver(evt, speaker, listener, targetName, itemName);
                return r.Resolve(template);
            }

            // ② 回落 Narrative.csv（过渡期兼容，长期 Narrative.csv 迁移到 NpcSpeech.csv 后删除此段）
            if (narrativeFallback != null)
            {
                string narrativeText = NarrativeResolver.TryResolveText(narrativeFallback);
                if (!string.IsNullOrEmpty(narrativeText))
                {
                    var r = new PlaceholderResolver(evt, speaker, listener, targetName, itemName);
                    return r.Resolve(narrativeText);
                }
            }

            // 均未命中 → null，调用方 ?? 硬编码兜底
            return null;
        }

        /// <summary>
        /// 查 NpcSpeech.csv 取模板文本。
        /// 支持单行内 | 分隔的多变体，随机取一。
        /// </summary>
        public static string LookupTemplate(string templateId)
        {
            var row = GameDatabase.NpcSpeech?.GetByID(templateId);
            if (row != null)
            {
                string template = row.GetString("Template");
                if (string.IsNullOrEmpty(template)) return null;
                // 变体语法：单行内 | 分隔，随机取一
                var variants = template.Split('|');
                return variants.Length == 1
                    ? variants[0]
                    : variants[MBRandom.RandomInt(variants.Length)];
            }
            // CSV 未命中 → 返回 null，让调用方决定回落策略
            return null;
        }

        /// <summary>
        /// 查 NpcSpeech.csv 取 Emotion 列值。
        /// 用于 BubbleSay 时播放对应动画。
        /// </summary>
        public static string LookupEmotion(string templateId)
        {
            var row = GameDatabase.NpcSpeech?.GetByID(templateId);
            if (row != null)
            {
                string emotion = row.GetString("Emotion", "normal");
                // 校验 Emotion 是否存在，不存在回落 normal
                if (GameDatabase.Emotion?.GetByID(emotion) == null)
                    return "normal";
                return emotion;
            }
            return "normal";
        }
    }
}
