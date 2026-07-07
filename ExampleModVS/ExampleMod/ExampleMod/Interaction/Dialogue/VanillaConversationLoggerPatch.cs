using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.Localization;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 记录原版对话的 NPC/玩家每句话 + 可选回复，与 StoryDialog 路径对比调试。
    ///
    /// Prefix: 记录当前可用选项（去重：只有变化时才打印）。
    /// Postfix: 记录刚处理的句子（区分 NPC/玩家）。
    /// </summary>
    [HarmonyPatch(typeof(ConversationManager), nameof(ConversationManager.ProcessSentence))]
    public static class VanillaConversationLoggerPatch
    {
        private static string _lastOptionsHash;

        [HarmonyPrefix]
        public static void Prefix(ConversationManager __instance)
        {
            try
            {
                var curOptions = __instance.CurOptions;
                if (curOptions == null || curOptions.Count == 0) return;

                // 去重：与上次相同的选项不重复打印
                var hash = string.Join("|", curOptions.Select(o => o.SentenceNo));
                if (hash == _lastOptionsHash) return;
                _lastOptionsHash = hash;

                // 🆕 打印当前活跃 token（便于定位模板 NPC 的对话注入点）
                string activeToken = GetActiveTokenString(__instance);
                string npcInfo = GetNpcDebugInfo(__instance);

                var optionTexts = new List<string>();
                var sentences = Traverse.Create(__instance)
                    .Field("_sentences")
                    .GetValue<List<ConversationSentence>>();

                foreach (var opt in curOptions)
                {
                    try
                    {
                        if (sentences != null && opt.SentenceNo >= 0 && opt.SentenceNo < sentences.Count)
                        {
                            string optText = sentences[opt.SentenceNo]?.Text?.ToString() ?? "";
                            optText = System.Text.RegularExpressions.Regex
                                .Replace(optText, @"\[if:[^\]]*\]|\[ib:[^\]]*\]|\[\?[^\]]*\]|\[\\?\]", "").Trim();
                            if (!string.IsNullOrEmpty(optText))
                                optionTexts.Add(optText);
                        }
                    }
                    catch { }
                }

                if (optionTexts.Count > 0)
                    DebugLogger.Log($"[VanillaDialog] Token='{activeToken}' {npcInfo} | Options ({optionTexts.Count}): {string.Join(" | ", optionTexts)}");
            }
            catch { }
        }

        /// <summary>
        /// 反射获取 ConversationManager 当前活跃 token 的字符串名。
        /// 复刻 DialogueInjector.GetCurrentConversationTokenString 的逻辑。
        /// </summary>
        private static string GetActiveTokenString(ConversationManager cm)
        {
            try
            {
                var cmType = cm.GetType();
                var stateMapField = cmType.GetField("stateMap",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (stateMapField == null) return "?";
                var stateMap = stateMapField.GetValue(cm) as Dictionary<string, int>;
                if (stateMap == null) return "?";

                var activeTokenField = cmType.GetField("ActiveToken",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (activeTokenField == null) return "?";
                int activeToken = (int)activeTokenField.GetValue(cm);

                foreach (var kv in stateMap)
                    if (kv.Value == activeToken)
                        return kv.Key;
            }
            catch { }
            return "?";
        }

        /// <summary>简短 NPC 标识，方便在日志中区分有名 Hero vs 模板 NPC</summary>
        private static string GetNpcDebugInfo(ConversationManager cm)
        {
            try
            {
                var hero = cm.OneToOneConversationHero;
                if (hero != null)
                    return $"Hero={hero.Name}";
                var agent = cm.SpeakerAgent;
                if (agent?.Character != null)
                    return $"NPC={agent.Character.Name}";
            }
            catch { }
            return "";
        }

        [HarmonyPostfix]
        public static void Postfix(ConversationManager __instance)
        {
            try
            {
                var sentenceText = Traverse.Create(__instance)
                    .Field("_currentSentenceText")
                    .GetValue<TextObject>();

                if (sentenceText == null) return;

                string cleanText = System.Text.RegularExpressions.Regex
                    .Replace(sentenceText.ToString(), @"\[if:[^\]]*\]|\[ib:[^\]]*\]|\[\?[^\]]*\]|\[\\?\]", "").Trim();
                if (string.IsNullOrEmpty(cleanText)) return;

                // 区分 NPC 还是玩家
                int currentSentenceNo = Traverse.Create(__instance)
                    .Field("_currentSentence").GetValue<int>();
                var sentences = Traverse.Create(__instance)
                    .Field("_sentences")
                    .GetValue<List<ConversationSentence>>();

                bool isPlayer = false;
                if (sentences != null && currentSentenceNo >= 0 && currentSentenceNo < sentences.Count)
                    isPlayer = sentences[currentSentenceNo].IsPlayer;

                if (isPlayer)
                {
                    DebugLogger.Log($"[VanillaDialog] Player says: \"{cleanText}\"");
                }
                else
                {
                    string npcInfo = "";
                    try
                    {
                        // Try SpeakerAgent first (mission conversation, 3D scene)
                        var npcChar = __instance.SpeakerAgent?.Character as CharacterObject;
                        var npcHero = npcChar?.HeroObject;

                        // Fallback to OneToOneConversationHero (map conversation, text-based)
                        if (npcHero == null)
                            npcHero = __instance.OneToOneConversationHero;
                        if (npcChar == null && npcHero != null)
                            npcChar = npcHero.CharacterObject;

                        if (npcHero != null)
                            npcInfo = $" | NPC: {npcHero.Name} (HeroId: {npcHero.StringId})";
                        else if (npcChar != null)
                            npcInfo = $" | NPC: {npcChar.Name} (CharacterId: {npcChar.StringId})";
                    }
                    catch { }

                    DebugLogger.Log($"[VanillaDialog] NPC {npcInfo} says: \"{cleanText}\"");
                }
            }
            catch { }
        }
    }
}
