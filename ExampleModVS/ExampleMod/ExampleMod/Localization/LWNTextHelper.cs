using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 本地化文本统一入口：封装 TextObject + PlaceholderResolver。
    /// TextObject("{=KEY}fallback") 自带 XML 查找：查到用翻译，查不到用 fallback。
    ///
    /// 使用方式：
    ///   // 带 PlaceholderResolver 的叙事文本
    ///   string text = LWNTextHelper.Resolve("LWN_narr_supply_emergency_opening_any", resolver, "fallback text");
    ///
    ///   // 纯文本（无占位符）
    ///   string text = LWNTextHelper.ResolveText("LWN_ui_detention_pay_fine", "Pay fine");
    ///
    ///   // 拼接场景：key + 显式键值对（语序由 XML 控制）
    ///   string text = LWNTextHelper.ResolveCompound("LWN_ph_suspect_description",
    ///       ("IDENTITY", identity), ("NAME", name));
    /// </summary>
    public static class LWNTextHelper
    {
        /// <summary>
        /// 用 localization key 取文本 + 用 PlaceholderResolver 填占位符。
        /// TextObject("{=KEY}fallback") 自带 XML 查找：查到用翻译，查不到用 fallback。
        /// </summary>
        public static string Resolve(string key, PlaceholderResolver resolver, string fallback = null)
        {
            string fallbackText = fallback ?? key;
            TextObject text = new TextObject($"{{={key}}}{fallbackText}", null);
            ApplyAllVariables(text, resolver);
            return text.ToString();
        }

        /// <summary>不带 PlaceholderResolver 的纯文本解析。</summary>
        public static string ResolveText(string key, string fallback = null)
        {
            string fallbackText = fallback ?? key;
            TextObject text = new TextObject($"{{={key}}}{fallbackText}", null);
            return text.ToString();
        }

        /// <summary>
        /// 尝试取文本，key 不存在时返回 null（不返回 fallback/key 名）。
        /// 与 ResolveText 的区别：ResolveText 查不到返回 fallback；此方法查不到返回 null。
        /// </summary>
        public static string TryResolveText(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var text = new TextObject($"{{={key}}}{SentinelValue}", null);
            string result = text.ToString();
            return result == SentinelValue ? null : result;
        }
        private const string SentinelValue = "\0LWN_NOKEY\0";

        /// <summary>
        /// 拼接场景专用：key + 显式指定的键值对（不走 PlaceholderResolver 全局扫）。
        /// 用于语序敏感拼接——每个变量由调用方显式传递，不由 ResolveOne 全局匹配。
        /// 无 fallback 参数的重载：XML 缺失时直接显示 key（调试期便于发现遗漏）。
        /// </summary>
        public static string ResolveCompound(string key, params (string var, string value)[] variables)
        {
            TextObject text = new TextObject($"{{={key}}}{key}", null);
            foreach (var (var, value) in variables)
            {
                if (!string.IsNullOrEmpty(value))
                    text.SetTextVariable(var, value);
            }
            return text.ToString();
        }

        /// <summary>
        /// 拼接场景专用（带 fallback）：key + 英文兜底 + 显式键值对。
        /// XML 缺失时展示 fallback（其中的 {VAR} 仍会被显式变量替换）。
        /// 与无 fallback 重载并存：`(key, tuple...)` 走旧重载，`(key, "str", tuple...)` 走本重载。
        /// </summary>
        public static string ResolveCompound(string key, string fallback, params (string var, string value)[] variables)
        {
            TextObject text = new TextObject($"{{={key}}}{fallback}", null);
            foreach (var (var, value) in variables)
            {
                if (!string.IsNullOrEmpty(value))
                    text.SetTextVariable(var, value);
            }
            return text.ToString();
        }

        /// <summary>从 PlaceholderResolver 提取全部占位符值，批量 SetTextVariable。</summary>
        private static void ApplyAllVariables(TextObject text, PlaceholderResolver r)
        {
            if (r == null) return;
            foreach (var key in AllKnownPlaceholders)
            {
                string value = r.ResolveOne(key);
                if (!string.IsNullOrEmpty(value))
                    text.SetTextVariable(key, value);
            }
        }

        /// <summary>
        /// PlaceholderResolver.ResolveOne 支持的全部占位符 key。
        /// 新增占位符时必须同步更新此列表。
        /// </summary>
        private static readonly string[] AllKnownPlaceholders =
        {
            // NpcSpeech 别名
            "PLAYER", "SPEAKER", "SPEAKER_SELF", "SPEAKER_PLAYER_ADDR", "SPEAKER_EMOTION",
            "TARGET", "ITEM", "StolenItemName", "LOCATION",

            // A. EventConfig
            "EventTypeName", "CrimeVerb", "CrimeVerbPast", "CrimeVerbGerund", "CrimeScene",
            "VictimLabel", "AuthorityRole", "SeverityWord", "DefaultPenalty",

            // B. WorldEvent
            "EventId", "StolenCount", "StolenItemDesc", "DiscoveryFacts", "StolenItemClause",
            "ActionDescription", "TargetHeroName", "TargetHeroIdentity", "TargetSettlementName",
            "LocationDetail",

            // C. Time
            "DaysSinceEvent", "TimeWord", "DaysSinceDiscovery", "DaysRemaining",
            "InvestigationDuration",

            // D. Cognition
            "PublicAwarenessWord", "InvestigationProgressWord",
            "SuspectName", "SuspectIdentity", "SuspectDescription",
            "SuspectIsPlayer", "SuspectIsUnknown", "InitiatorIsPlayer",
            "PlayerIsAccused", "PlayerIsNotAccused",

            // E. Witness/Evidence
            "WitnessExist", "WitnessCount", "WitnessCountWord",
            "PrimaryWitnessName", "PrimaryWitnessIdentity", "PrimaryWitnessDesc",
            "WitnessesSilenced", "EvidenceExist", "EvidenceCount", "TopEvidenceDesc",

            // F. Speaker
            "SpeakerName", "SpeakerIdentity", "SpeakerRole",
            "SpeakerSelfRef", "SpeakerPlayerAddr", "SpeakerEmotion",
            "SpeakerAttitudeWord", "SpeakerIsAuthority",

            // G. Listener
            "ListenerName", "ListenerIdentity",
            "ListenerIsThief", "ListenerIsSuspect", "ListenerIsDetective",

            // G2. Closing
            "ConfrontClosingLine",

            // H. Options
            "RestitutionCost", "RestitutionCostOnSpot", "RestitutionCostHaggle",
            "RestitutionBreakdown", "AlertFineCost", "BountyAmount",
            "CharmReprieveUsed", "FailCount", "FailCountRemaining",
        };
    }
}
