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
                    DebugLogger.Log($"[VanillaDialog] Options ({optionTexts.Count}): {string.Join(" | ", optionTexts)}");
            }
            catch { }
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
                        Hero npcHero = null;
                        CharacterObject npcChar = null;

                        // Try SpeakerAgent first (mission conversation, 3D scene)
                        var speakerAgent = __instance.SpeakerAgent;
                        if (speakerAgent != null)
                        {
                            npcChar = (CharacterObject)speakerAgent.Character;
                            npcHero = npcChar?.HeroObject;
                        }

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
