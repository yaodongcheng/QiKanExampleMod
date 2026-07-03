using HarmonyLib;
using SandBox.Conversation.MissionLogics;
using System;
using System.Collections.Generic;
using System.Reflection;
using LivingWorldNpcs.Story;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

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

        #region Postfix —— 定居点犯罪对话注入（交谈 + 造访两路共用）

        internal static string _lastInjectedEventId;
        internal static string _lastInjectedTag;

        /// <summary>
        /// 共享注入逻辑：检查定居点犯罪事件，构建并注入犯罪对话。
        /// 供 CampaignMapConversation.OpenConversation（交谈）和
        /// MissionConversationLogic.StartConversation（造访）两路调用。
        /// </summary>
        internal static void TryInjectCrimeDialogue(Hero partner)
        {
            try
            {
                if (partner == null) return;

                Settlement settlement = Settlement.CurrentSettlement
                    ?? partner.CurrentSettlement
                    ?? Hero.MainHero?.CurrentSettlement;
                if (settlement == null) return;

                var evt = WorldEventStore.FindActive(settlement.StringId);
                if (evt == null)
                {
                    // 事件已不存在（Resolved/Unsolved）→ 清理上次注入的旧对话残留
                    if (_lastInjectedTag != null)
                    {
                        DialogueInjector.RemoveRelatedLines(_lastInjectedTag);
                        DebugLogger.Log($"[ConvEntry] Cleaned up stale crime dialogue: tag={_lastInjectedTag}");
                        _lastInjectedTag = null;
                        _lastInjectedEventId = null;
                    }
                    return;
                }

                if (_lastInjectedEventId == evt.EventId + "_" + partner.StringId)
                    return;

                string tag = $"crime_{evt.EventId}";
                DialogueInjector.RemoveRelatedLines(tag);

                var script = CrimeDialogueBuilder.BuildScript(partner, Hero.MainHero);
                if (script != null && script.Turns != null && script.Turns.Count > 0)
                {
                    DialogueInjector.InjectScript(script, tag);
                    _lastInjectedEventId = evt.EventId + "_" + partner.StringId;
                    _lastInjectedTag = tag;
                    DebugLogger.Log($"[ConvEntry] Injected crime dialogue: event={evt.EventId} stage={evt.Stage} partner={partner.Name} turns={script.Turns.Count}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConvEntry] Inject error: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        public static void Postfix(ConversationCharacterData playerCharacterData,
                                    ConversationCharacterData conversationPartnerData)
        {
            try
            {
                TryInjectCrimeDialogue(conversationPartnerData.Character?.HeroObject);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConvEntry] Postfix error: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// 对话结束时重置 _lastInjectedEventId，确保下次对话能刷新犯罪对话注入。
    /// 否则同一事件+同一 NPC 的对话只注入一次，后续对话中玩家看不到更新后的犯罪对话选项
    /// （例如接调查任务后应该出现的汇报 turn "怎么样，查到什么了吗？"）。
    /// </summary>
    [HarmonyPatch(typeof(ConversationManager), nameof(ConversationManager.EndConversation))]
    public static class ResetCrimeDialogueOnConversationEndPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DebugLogger.Log($"[ConvEnd] Conversation ended. lastEvent={ConversationEntryPatch._lastInjectedEventId} lastTag={ConversationEntryPatch._lastInjectedTag}");
            ConversationEntryPatch._lastInjectedEventId = null;
            ConversationEntryPatch._lastInjectedTag = null;

            // 延迟弹出：WalkAwayIntent 存入的 Inquiry，等对话 UI 完全关闭后再弹
            if (WalkAwayIntent.PendingInquiryTitle != null)
            {
                // 自首未解决 → 推进 stage（弥补"太贵了不赔"等无 intent 退出的路径）
                var settlement = Settlement.CurrentSettlement ?? Hero.MainHero?.CurrentSettlement;
                if (settlement != null)
                {
                    var evt = WorldEventStore.FindActive(settlement.StringId);
                    if (evt != null && evt.Stage == EventStage.Emerging && evt.SuspectIsPlayer)
                    {
                        WorldEventStore.TransitionStage(evt, EventStage.Active);
                        foreach (var q in Campaign.Current.QuestManager.Quests)
                        {
                            if (q is CommissionQuest cq
                                && cq.Data?.WorldEventId == evt.EventId
                                && cq.Data?.Category == CommissionCategory.Investigation)
                            {
                                cq.NotifySuspectIdentified(Hero.MainHero.Name?.ToString() ?? "你");
                                break;
                            }
                        }
                    }
                }

                string title = WalkAwayIntent.PendingInquiryTitle;
                string body = WalkAwayIntent.PendingInquiryBody;
                WalkAwayIntent.PendingInquiryTitle = null;
                WalkAwayIntent.PendingInquiryBody = null;
                InformationManager.ShowInquiry(new InquiryData(
                    title, body,
                    true, false,
                    "……", null, null, null));
            }
        }
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

    /// <summary>
    /// 拦截原版 Issue 对话入口句的条件检查 —— 从根本上阻止 "我听说你有个问题需要帮助。" 出现。
    ///
    /// 原版引擎在填充 hero_main_options 时，对每条注册的 PlayerLine 调用其 OnCondition delegate。
    /// "hero_give_issue" 句子的 OnCondition 是
    /// LordConversationsCampaignBehavior.conversation_hero_main_options_have_issue_on_condition()：
    ///
    ///   if (Hero.OneToOneConversationHero?.Issue != null)
    ///       return issue.IsOngoingWithoutQuest;
    ///   return false;
    ///
    /// 当 CommissionHubIssue 激活时（hero.Issue 非 null 且 IsOngoingWithoutQuest==true），
    /// 原版条件返回 true → 显示 "我听说你有个问题需要帮助。" 及其下游整个 Issue 对话流。
    ///
    /// 此 Prefix 在 CommissionHubIssue 场景下强制返回 false，跳过原版 Issue 入口，
    /// 由我们的 CrimeDialogueBuilder 入口句（"村长，听说特维亚出了点事？"）替代。
    ///
    /// 为什么这比 _sentences 级别删除更好：
    ///   - 条件在句子评估阶段就被拦截，句子根本不会被加入 CurOptions
    ///   - 不依赖下游内容（避开懒加载问题）
    ///   - 不碰 hero.Issue 引用（避开 IssueManager 状态不一致崩溃）
    ///   - 对原版 Issue 零影响：hero.Issue 是原版 IssueBase 子类时正常放行
    /// </summary>
    [HarmonyPatch(typeof(LordConversationsCampaignBehavior), "conversation_hero_main_options_have_issue_on_condition")]
    public static class SuppressVanillaIssueConditionPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            try
            {
                Hero hero = Hero.OneToOneConversationHero;
                if (hero?.Issue is CommissionHubIssue)
                {
                    __result = false;
                    return false; // 跳过原始方法，直接返回 false
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SuppressIssue] Prefix error: {ex.Message}");
            }
            return true; // 放行：非 CommissionHubIssue，让原版条件正常评估
        }
    }

    /// <summary>
    /// 造访（Visit）路径的犯罪对话注入。
    ///
    /// CampaignMapConversation.OpenConversation（交谈）走 OpenMapConversation，
    /// 造访走 MissionConversationLogic.StartConversation → SetupAndStartMissionConversation。
    /// 两者共用 ConversationManager 底层引擎但入口不同，所以需要单独 patch。
    ///
    /// 覆盖场景：
    ///   - 造访自动对话（OnMissionTick 检测 _teleportNearCharacter → StartConversation）
    ///   - Mission 内手动按 F 对话（HandleInput → StartVanillaConversation → StartConversation）
    /// </summary>
    [HarmonyPatch(typeof(MissionConversationLogic), nameof(MissionConversationLogic.StartConversation))]
    public static class MissionConversationStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MissionConversationLogic __instance)
        {
            try
            {
                var character = __instance.ConversationAgent?.Character as CharacterObject;
                ConversationEntryPatch.TryInjectCrimeDialogue(character?.HeroObject);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConvEntry] Mission start Postfix error: {ex.Message}");
            }
        }
    }
}
