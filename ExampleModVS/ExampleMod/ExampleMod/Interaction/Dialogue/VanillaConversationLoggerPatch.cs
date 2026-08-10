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

                // 解析对话英雄（NPC 台词归属 + 记忆写入都用它）
                Hero npcHero = null;
                try
                {
                    var npcChar = __instance.SpeakerAgent?.Character as CharacterObject;
                    npcHero = npcChar?.HeroObject;
                    if (npcHero == null)
                        npcHero = __instance.OneToOneConversationHero;
                }
                catch { }

                if (isPlayer)
                {
                    DebugLogger.Log($"[VanillaDialog] Player says: \"{cleanText}\"");
                }
                else
                {
                    string npcInfo = "";
                    try
                    {
                        if (npcHero != null)
                            npcInfo = $" | NPC: {npcHero.Name} (HeroId: {npcHero.StringId})";
                        else if (__instance.SpeakerAgent?.Character != null)
                            npcInfo = $" | NPC: {__instance.SpeakerAgent.Character.Name} (CharacterId: {__instance.SpeakerAgent.Character.StringId})";
                    }
                    catch { }

                    DebugLogger.Log($"[VanillaDialog] NPC {npcInfo} says: \"{cleanText}\"");
                }

                // 🔴 记录到 NPC 记忆（2026-08-10：背景故事拼接）——
                // 招募对话（"我的部队需要你这样的人" → 流浪者身世台词）必须进 NPC 自己的记忆，
                // 否则 prompt 里永远只有模板数据，没有他"自己说过的话"。
                // 只记 Hero 对话（模板 NPC 无个体身份）；玩家行 speakerId="player" 让裁剪段能匹配。
                try
                {
                    if (npcHero != null && !string.IsNullOrEmpty(cleanText))
                    {
                        var mem = AllNpcMemoryManager.GetMemory(npcHero.StringId);
                        if (mem != null)
                        {
                            string role = isPlayer ? "user" : "assistant";
                            string speakerId = isPlayer ? ImChatManager.PlayerId : npcHero.StringId;
                            string speakerName = isPlayer
                                ? (Hero.MainHero?.Name?.ToString() ?? "玩家")
                                : (npcHero.Name?.ToString() ?? "NPC");
                            string line = $"{speakerName}: {cleanText}";
                            // 去重：与最近一条同角色同内容不重复写（mod 注入对话可能双路径记录同一句）
                            var recent = mem.SnapshotRecentHistory();
                            if (recent.Count == 0
                                || recent[recent.Count - 1].Role != role
                                || recent[recent.Count - 1].Content != line)
                            {
                                mem.AddHistory(role, line, speakerId);
                            }
                        }
                    }
                }
                catch { }
            }
            catch { }
        }
    }
}
