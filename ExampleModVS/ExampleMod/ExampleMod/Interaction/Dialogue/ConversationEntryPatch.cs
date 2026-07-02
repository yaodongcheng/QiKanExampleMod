using HarmonyLib;
using SandBox.Conversation.MissionLogics;
using System;
using System.Collections.Generic;
using System.Reflection;
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
                if (evt == null)
                {
                    OurEntryText = null;
                    return;
                }

                if (_lastInjectedEventId == evt.EventId + "_" + partner.StringId)
                    return;

                DialogueInjector.RemoveRelatedLines($"crime_{evt.EventId}");

                var script = CrimeDialogueBuilder.BuildScript(partner, Hero.MainHero);
                if (script != null && script.Turns != null && script.Turns.Count > 0)
                {
                    DialogueInjector.InjectScript(script, $"crime_{evt.EventId}");
                    _lastInjectedEventId = evt.EventId + "_" + partner.StringId;
                    DebugLogger.Log($"[ConvEntry] Injected crime dialogue: event={evt.EventId} stage={evt.Stage} partner={partner.Name} turns={script.Turns.Count}");

                    // 尝试从 dialog graph 中删除以 Hero.Issue 为 owner 的原版对话节点——
                    // 原版引擎检测到 hero.Issue!=null 后注册的入口句（"我听说你有个问题需要帮助。"）
                    // 可能以 IssueBase 为 owner。如果命中，从根本上消除，不再依赖 _sentences 级别的清理。
                    if (partner.Issue != null)
                    {
                        try
                        {
                            Campaign.Current.ConversationManager.RemoveRelatedLines(partner.Issue);
                            DebugLogger.Log($"[ConvEntry] Called RemoveRelatedLines(hero.Issue) for {partner.Name}");
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.Log($"[ConvEntry] RemoveRelatedLines(hero.Issue) failed: {ex.Message}");
                        }
                    }

                    // 推迟到 ProcessSentence 时兜底删除——如果 RemoveRelatedLines 没命中，
                    // CleanupVanillaIssuePrefix 会在 _sentences 层面再次尝试。
                    OurEntryText = script.EntryOption;
                    _firstCleanupLogged = false;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConvEntry] Postfix error: {ex.Message}");
            }
        }

        #region Sentence-level filtering — 删除原版委托对话选项

        /// <summary>
        /// Postfix 中暂不直接删除——此时对话可能还在介绍阶段，hero_main_options 尚未激活。
        /// 改为存储入口文本，由 <see cref="CleanupVanillaIssuePrefix"/> 在 ConversationManager.ProcessSentence
        /// 时（_sentences 真正包含 hero_main_options 内容）执行删除。
        /// </summary>
        internal static string OurEntryText;
        private static bool _dumpedCmFields;
        private static bool _firstCleanupLogged;

        /// <summary>
        /// 从 _sentences 中删除原版委托入口句子。
        /// hero.Issue 保持不变（! 标记不受影响），但对话流中不再出现 "我听说你有个问题需要帮助"。
        ///
        /// 识别方式：图遍历——对 hero_main_options 下的每个玩家句，沿 OutputToken
        /// 找到 NPC 回应，与 Hero.Issue 的 IssueBrief / IssueQuestSolutionExplanation / Title
        /// 做文本匹配。匹配到的即是原版 Issue 入口，删除之。告别句自然不受影响。
        /// </summary>
        internal static void RemoveVanillaIssueSentenceNow(ConversationManager cm, string ourEntryText)
        {
            try
            {
                // ── 一次性：反射枚举 ConversationManager 所有字段，找到存全量对话图的字段 ──
                if (!_dumpedCmFields)
                {
                    _dumpedCmFields = true;
                    var cmType = cm.GetType();
                    foreach (var f in cmType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public))
                    {
                        try
                        {
                            var val = f.GetValue(cm);
                            string summary = val == null ? "null" : $"type={val.GetType().Name}";
                            if (val is System.Collections.ICollection col)
                                summary += $" count={col.Count}";
                            DebugLogger.Log($"[ConvEntry] CM field: {f.Name} ({f.FieldType.Name}) → {summary}");
                        }
                        catch { }
                    }
                }

                var sentences = Traverse.Create(cm).Field("_sentences").GetValue<List<ConversationSentence>>();
                if (sentences == null || sentences.Count == 0) return;

                // 1. 从 stateMap 获取 hero_main_options token
                var stateMap = Traverse.Create(cm).Field("stateMap").GetValue<Dictionary<string, int>>();
                if (stateMap == null || !stateMap.TryGetValue("hero_main_options", out int heroMainToken))
                {
                    DebugLogger.Log($"[ConvEntry] Cleanup: stateMap missing hero_main_options");
                    return;
                }

                // 2. 从 Hero.Issue 收集预期的 Issue 文本（含 NPC 回应和后续玩家句）
                var hero = cm.OneToOneConversationHero;
                var expectedTexts = new List<string>();
                if (hero?.Issue != null)
                {
                    AddIssueText(expectedTexts, hero.Issue.IssueBriefByIssueGiver);
                    AddIssueText(expectedTexts, hero.Issue.IssueQuestSolutionExplanationByIssueGiver);
                    AddIssueText(expectedTexts, hero.Issue.Title);
                    AddIssueText(expectedTexts, hero.Issue.IssueAcceptByPlayer);
                    AddIssueText(expectedTexts, hero.Issue.IssueQuestSolutionAcceptByPlayer);
                }
                DebugLogger.Log($"[ConvEntry] Cleanup: hero={hero?.Name} issue={hero?.Issue?.GetType().Name} expectedTexts=[{string.Join(" | ", expectedTexts)}]");

                if (expectedTexts.Count == 0) return;

                // 3. 构建 InputToken → 句子文本 的查找表（同 token 可能有多句，用 List 防覆盖）
                var textByToken = new Dictionary<int, List<string>>();
                foreach (var s in sentences)
                {
                    int inToken = s.InputToken;
                    string clean = CleanFormatting(s.Text?.ToString() ?? "");
                    if (!string.IsNullOrEmpty(clean))
                    {
                        if (!textByToken.TryGetValue(inToken, out var list))
                            textByToken[inToken] = list = new List<string>();
                        list.Add(clean);
                    }
                }

                // 首次调用时打印诊断信息，后续静默
                if (!_firstCleanupLogged)
                {
                    _firstCleanupLogged = true;
                    DebugLogger.Log($"[ConvEntry] Cleanup: hero={hero?.Name} issue={hero?.Issue?.GetType().Name} expectedTexts=[{string.Join(" | ", expectedTexts)}]");
                    DebugLogger.Log($"[ConvEntry] Cleanup: _sentences has {sentences.Count} entries, textByToken has {textByToken.Count} token-keys, heroMainToken={heroMainToken}");
                    for (int i = 0; i < Math.Min(5, sentences.Count); i++)
                    {
                        var s = sentences[i];
                        DebugLogger.Log($"[ConvEntry] Cleanup: sample[{i}] in={s.InputToken} out={s.OutputToken} isPlayer={s.IsPlayer} text='{CleanFormatting(s.Text?.ToString() ?? "")}'");
                    }
                }

                // 4. 图遍历：玩家句 → OutputToken → 下游任意句子匹配 Issue 文本 → 确认后删除
                for (int i = sentences.Count - 1; i >= 0; i--)
                {
                    var s = sentences[i];

                    if (s.InputToken != heroMainToken) continue;

                    string text = CleanFormatting(s.Text?.ToString() ?? "");

                    // 跳过我们自己注入的入口句
                    if (text == ourEntryText) continue;

                    if (!s.IsPlayer) continue;

                    // 沿 OutputToken 搜下游所有句子（同 token 可能有多条）
                    int outToken = s.OutputToken;

                    if (!textByToken.TryGetValue(outToken, out var downstreamTexts))
                        continue;

                    // 匹配 Issue 文本（Contains 而非 ==，防止引擎在文本中插入空白）
                    bool isIssueSentence = false;
                    string matchedDownstream = null;
                    foreach (var downstreamText in downstreamTexts)
                    {
                        foreach (var expected in expectedTexts)
                        {
                            if (downstreamText.Contains(expected))
                            {
                                isIssueSentence = true;
                                matchedDownstream = downstreamText;
                                break;
                            }
                        }
                        if (isIssueSentence) break;
                    }

                    if (isIssueSentence)
                    {
                        sentences.RemoveAt(i);
                        DebugLogger.Log($"[ConvEntry] Removed vanilla issue sentence: '{text}' → '{matchedDownstream}'");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConvEntry] RemoveVanillaIssueSentenceNow error: {ex.Message}");
            }
        }

        private static void AddIssueText(List<string> list, TextObject text)
        {
            string s = text?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(s))
                list.Add(CleanFormatting(s));
        }

        private static string CleanFormatting(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return System.Text.RegularExpressions.Regex.Replace(
                text, @"\[if:[^\]]*\]|\[ib:[^\]]*\]|\[\?[^\]]*\]|\[\\?\]", "").Trim();
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

    /// <summary>
    /// 在 ConversationManager.ProcessSentence 时清理原版 Issue 对话选项。
    ///
    /// 为什么不在 ConversationEntryPatch.Postfix 中直接删？
    /// Postfix 时对话可能还在介绍阶段（非 hero_main_options），_sentences 里没有目标句子。
    /// 等到 ProcessSentence 被调用时，当前 token 的内容已经填充到 _sentences，此时才是删除的正确时机。
    ///
    /// 每次 ProcessSentence 都检查——因为玩家可能先点进原版 Issue 流再退出，
    /// 此时 hero_main_options 重新激活、_sentences 重建，原版句子又回来了，需要再次清理。
    /// </summary>
    [HarmonyPatch(typeof(ConversationManager), "ProcessSentence")]
    public static class CleanupVanillaIssuePrefix
    {
        [HarmonyPrefix]
        public static void Prefix(ConversationManager __instance)
        {
            try
            {
                if (string.IsNullOrEmpty(ConversationEntryPatch.OurEntryText))
                    return;

                ConversationEntryPatch.RemoveVanillaIssueSentenceNow(
                    __instance, ConversationEntryPatch.OurEntryText);
            }
            catch { }
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
}
