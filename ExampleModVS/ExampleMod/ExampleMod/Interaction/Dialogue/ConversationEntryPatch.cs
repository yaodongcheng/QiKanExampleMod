using HarmonyLib;
using SandBox.Conversation.MissionLogics;
using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 对话入口统一拦截：大地图遇敌分流 + 定居点犯罪对话注入。
    ///
    /// 两个场景天然互斥：
    ///   - 大地图遇敌：Prefix 弹 inquiry 并 return false 拦截原版 → Postfix 不触发。
    ///   - 定居点对话：Prefix 放行 → 原版 OpenConversation 执行 → Postfix 检查犯罪事件注入。
    /// </summary>
    [HarmonyPatch(typeof(CampaignMapConversation), nameof(CampaignMapConversation.OpenConversation))]
    public static class ConversationEntryPatch
    {
        #region Prefix —— 大地图遇敌 → 闲聊/对话 分流

        /// <summary>防重入：闲聊分支绕开本补丁直调 OpenMapConversation 时放行</summary>
        private static bool _reentry;

        [HarmonyPrefix]
        public static bool Prefix(ConversationCharacterData playerCharacterData,
                                  ConversationCharacterData conversationPartnerData)
        {
            try
            {
                if (_reentry) return true;

                CharacterObject partnerChar = conversationPartnerData.Character;
                if (partnerChar == null) return true;

                // ── 定居点内对话（无 Party）：直接放行，让 Postfix 注入犯罪对话 ──
                if (conversationPartnerData.Party == null)
                    return true;

                // ── 如果 party 有 leader Hero，重定向对话对象到 leader ──
                Hero partyLeader = conversationPartnerData.Party?.MobileParty?.LeaderHero;
                if (partyLeader != null && partyLeader.CharacterObject != partnerChar)
                {
                    DebugLogger.Log($"[ConvEntry] Redirecting conversation from '{partnerChar.Name}' (hero=none) to party leader '{partyLeader.Name}'");
                    partnerChar = partyLeader.CharacterObject;
                }

                string npcName = partnerChar?.Name?.ToString() ?? "对方";

                var p = playerCharacterData;
                var q = conversationPartnerData;
                if (partyLeader != null && partyLeader.CharacterObject != conversationPartnerData.Character)
                {
                    q = new ConversationCharacterData(
                        partyLeader.CharacterObject,
                        q.Party,
                        q.NoHorse, q.NoWeapon, q.SpawnedAfterFight,
                        q.IsCivilianEquipmentRequiredForLeader,
                        q.IsCivilianEquipmentRequiredForBodyGuardCharacters,
                        noBodyguards: true);
                }

                InformationManager.ShowInquiry(new InquiryData(
                    $"你和{npcName}相遇了",
                    $"你想怎么和{npcName}说话？",
                    true, true,
                    "闲聊", "对话",
                    affirmativeAction: () =>
                    {
                        MapEncounterDialogState.Active = true;
                        MapEncounterDialogState.Partner = q.Character;
                        MapEncounterDialogState.PartnerParty = q.Party;
                        var p2 = new ConversationCharacterData(p.Character, p.Party, p.NoHorse, p.NoWeapon, p.SpawnedAfterFight, p.IsCivilianEquipmentRequiredForLeader, p.IsCivilianEquipmentRequiredForBodyGuardCharacters, noBodyguards: true);
                        var q2 = new ConversationCharacterData(q.Character, q.Party, q.NoHorse, q.NoWeapon, q.SpawnedAfterFight, q.IsCivilianEquipmentRequiredForLeader, q.IsCivilianEquipmentRequiredForBodyGuardCharacters, noBodyguards: true);
                        CampaignMission.OpenConversationMission(p2, q2);
                    },
                    negativeAction: () =>
                    {
                        _reentry = true;
                        try { CampaignMapConversation.OpenConversation(p, q); }
                        finally { _reentry = false; }
                    }));

                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConvEntry] Prefix error: {ex}");
                return true;
            }
        }

        #endregion

        #region Postfix —— 定居点犯罪对话注入

        private static string _lastInjectedEventId;

        [HarmonyPostfix]
        public static void Postfix(ConversationCharacterData playerCharacterData,
                                    ConversationCharacterData conversationPartnerData)
        {
            try
            {
                var partnerChar = conversationPartnerData.Character;
                if (partnerChar == null) return;

                Hero partner = partnerChar.HeroObject;
                if (partner == null) return;

                Settlement settlement = Settlement.CurrentSettlement
                    ?? partner.CurrentSettlement
                    ?? Hero.MainHero?.CurrentSettlement;
                if (settlement == null) return;

                var evt = WorldEventStore.FindActive(settlement.StringId);
                if (evt == null) return;

                if (_lastInjectedEventId == evt.EventId + "_" + partner.StringId)
                    return;

                DialogueInjector.RemoveRelatedLines($"crime_{evt.EventId}");

                var script = CrimeDialogueBuilder.BuildScript(partner, Hero.MainHero);
                if (script != null && script.Turns != null && script.Turns.Count > 0)
                {
                    DialogueInjector.InjectScript(script, $"crime_{evt.EventId}");
                    _lastInjectedEventId = evt.EventId + "_" + partner.StringId;
                    DebugLogger.Log($"[ConvEntry] Injected crime dialogue: event={evt.EventId} stage={evt.Stage} partner={partner.Name} turns={script.Turns.Count}");

                    // 删除原版委托对话选项——犯罪对话已接管，不需要重复入口
                    RemoveVanillaIssueSentence(script.EntryOption);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConvEntry] Postfix error: {ex.Message}");
            }
        }

        #region Sentence-level filtering — 删除原版委托对话选项

        /// <summary>
        /// 犯罪对话注入后，从 ConversationManager._sentences 中删除原版委托入口句子。
        /// hero.Issue 保持不变（! 标记不受影响），但对话流中不再出现 "我听说你有个问题需要帮助"。
        /// </summary>
        private static void RemoveVanillaIssueSentence(string ourEntryOption)
        {
            try
            {
                var cm = Campaign.Current?.ConversationManager;
                if (cm == null) return;

                var sentences = Traverse.Create(cm).Field("_sentences").GetValue<List<ConversationSentence>>();
                if (sentences == null || sentences.Count == 0) return;

                // 1. 找到我们注入的入口句子 → 获取 hero_main_options 对应的 token 值
                int heroMainToken = -1;
                ConversationSentence ourSentence = null;
                foreach (var s in sentences)
                {
                    string text = s.Text?.ToString() ?? "";
                    if (text == ourEntryOption)
                    {
                        heroMainToken = Traverse.Create(s).Field("InputToken").GetValue<int>();
                        ourSentence = s;
                        break;
                    }
                }
                if (heroMainToken < 0 || ourSentence == null) return;

                // 2. 找到 "close_window" token 值——通过匹配 "告辞/leave" 句子的 OutputToken
                int closeWindowToken = -1;
                foreach (var s in sentences)
                {
                    if (s == ourSentence) continue;
                    int inToken = Traverse.Create(s).Field("InputToken").GetValue<int>();
                    if (inToken != heroMainToken) continue;

                    string text = s.Text?.ToString() ?? "";
                    if (text.Contains("得走了") || text.Contains("告辞") || text.Contains("该走了"))
                    {
                        closeWindowToken = Traverse.Create(s).Field("OutputToken").GetValue<int>();
                        break;
                    }
                }

                // 3. 遍历删除：hero_main_options 下 IsPlayer、非我们注入、非告别句 = 原版委托入口
                for (int i = sentences.Count - 1; i >= 0; i--)
                {
                    var s = sentences[i];
                    if (s == ourSentence) continue;

                    int inToken = Traverse.Create(s).Field("InputToken").GetValue<int>();
                    if (inToken != heroMainToken) continue;

                    // 检查 IsPlayer（通过 flags 字段 bit 0）
                    bool isPlayer = false;
                    try
                    {
                        uint flags = Traverse.Create(s).Field("Flags").GetValue<uint>();
                        isPlayer = (flags & 1u) != 0;
                    }
                    catch
                    {
                        int outToken = Traverse.Create(s).Field("OutputToken").GetValue<int>();
                        if (closeWindowToken >= 0 && outToken == closeWindowToken) continue;
                        isPlayer = true;
                    }

                    if (!isPlayer) continue;

                    // 跳过标准告别句
                    if (closeWindowToken >= 0)
                    {
                        int outToken = Traverse.Create(s).Field("OutputToken").GetValue<int>();
                        if (outToken == closeWindowToken) continue;
                    }

                    string removedText = s.Text?.ToString() ?? "?";
                    sentences.RemoveAt(i);
                    DebugLogger.Log($"[ConvEntry] Removed vanilla issue sentence: '{removedText}'");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConvEntry] RemoveVanillaIssueSentence error: {ex.Message}");
            }
        }

        #endregion

        #endregion
    }

    /// <summary>
    /// 抑制「我们的遭遇 mission」中原版 ConversationMissionLogic 的自动对话与自动结束。
    /// </summary>
    [HarmonyPatch(typeof(ConversationMissionLogic), "OnMissionTick")]
    public static class SuppressVanillaConversationMissionPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !MapEncounterDialogState.Active;
        }
    }
}
