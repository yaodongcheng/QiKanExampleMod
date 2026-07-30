using HarmonyLib;
using SandBox.Conversation.MissionLogics;
using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

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

                // ── 闲聊关闭：跳过 Inquiry，直接走原版对话 ──
                if (!InteractionMissionView.EnableSmallTalk)
                {
                    _reentry = true;
                    try { CampaignMapConversation.OpenConversation(p, q); }
                    finally { _reentry = false; }
                    return false;
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

        /// <summary>待消费的对话触发类型。调用方设置，TryInjectCrimeDialogue 消费并重置为 Normal。</summary>
        internal static DialogueTrigger _pendingTrigger = DialogueTrigger.Normal;

        /// <summary>Alert trigger 专用的质问类型（Deter/Search/Recover/Stop）。</summary>
        internal static ConfrontationType _pendingConfrontation;

        /// <summary>Alert trigger 专用的触发动作类型（Crouching/Steal/AttackAlly 等）。</summary>
        internal static PlayerActionType _pendingTriggerAction;

        /// <summary>
        /// 共享注入逻辑：检查定居点犯罪事件，构建并注入犯罪对话。
        /// 供 CampaignMapConversation.OpenConversation（交谈）和
        /// MissionConversationLogic.StartConversation（造访）两路调用。
        /// </summary>
        /// <param name="partner">对话对象的 Hero（模板 NPC 为 null）</param>
        /// <param name="agent">对话对象的 Agent（Mission 内非 null；大地图对话为 null）。
        /// 优先从 Agent 提取 CharacterObject 做身份识别。</param>
        /// <param name="character">对话对象的 CharacterObject（大地图路径无 Agent 时显式传入）。
        /// agent 为 null 时用于身份回退（dedup key、{SPEAKER} 占位符解析）。</param>
        internal static void TryInjectCrimeDialogue(Hero partner, Agent agent = null, CharacterObject character = null)
        {
            try
            {
                // 优先从 Agent 提取 CharacterObject，回落显式传入的 character（大地图路径）
                character = agent?.Character as CharacterObject ?? character;

                // ── 1. 查找关联 WorldEvent（两层：持久化存储 + Mission 作用域）──
                Settlement settlement = Settlement.CurrentSettlement
                    ?? partner?.CurrentSettlement
                    ?? Hero.MainHero?.CurrentSettlement;

                WorldEvent evt = null;
                if (settlement != null)
                {
                    evt = WorldEventStore.FindActive(settlement.StringId);
                }

                // ── 2. 消费 trigger ──
                var trigger = _pendingTrigger;
                var confrontation = _pendingConfrontation;
                var triggerAction = _pendingTriggerAction;

                _pendingTrigger = DialogueTrigger.Normal;
                _pendingConfrontation = default;
                _pendingTriggerAction = default;

                // ── 3. 设计契约：Surrender 必须有关联 WorldEvent；Alert 可以无事件（纯警戒质问）──
                if (trigger != DialogueTrigger.Normal && evt == null)
                {
                    if (trigger == DialogueTrigger.Alert)
                    {
                        // 无 WorldEvent 的纯警戒质问（玩家蹲下/拔刀被看见，但尚未造成犯罪事件）。
                        // BuildAlertInterceptScriptInternal 内部用无 evt 的 PlaceholderResolver 构造器，
                        // {CRIME} 等占位符回落空串，对话仍然正常注入。
                        DebugLogger.Log($"[ConvEntry] 没有事件时发起的质问 Alert trigger without WorldEvent — proceeding with generic confrontation.");
                    }
                    else
                    {
                        // Surrender 必须有关联 WorldEvent（投降一定发生在战斗/犯罪现场）
                        DebugLogger.Log($"[ConvEntry] ERROR: trigger={trigger} but no WorldEvent (store or pending)! " +
                            "Dialogue will be skipped — this is a design contract violation.");
                        return;
                    }
                }

                // ── 4. 无事件 + Normal trigger → 清理上次注入残留，退出 ──
                if (evt == null && trigger == DialogueTrigger.Normal)
                {
                    if (_lastInjectedTag != null)
                    {
                        DialogueInjector.RemoveRelatedLines(_lastInjectedTag);
                        _lastInjectedTag = null;
                        _lastInjectedEventId = null;
                    }
                    return;
                }

                // ── 5. 防重复注入 ──
                // 身份回退链：Hero.StringId → CharacterObject.StringId → Agent.Index → "(template)"
                string partnerKey = partner?.StringId
                    ?? character?.StringId
                    ?? agent?.Index.ToString()
                    ?? "(template)";
                string eventKey = evt?.EventId ?? "no_event";
                if (_lastInjectedEventId == eventKey + "_" + partnerKey)
                    return;

                // ── 6. 统一走 BuildScript ──
                string tag = evt != null ? $"crime_{evt.EventId}" : $"crime_alert_{partnerKey}";
                DialogueInjector.RemoveRelatedLines(tag);

                var script = CrimeDialogueBuilder.BuildScript(
                    partner, Hero.MainHero, evt, trigger, confrontation, triggerAction, character, speakerAgent: agent);

                if (script != null && script.Nodes?.Count > 0)
                {
                    DialogueInjector.InjectScript(script, tag);
                    _lastInjectedEventId = eventKey + "_" + partnerKey;
                    _lastInjectedTag = tag;
                    DebugLogger.Log($"[ConvEntry] 注入对话执行成功 Injected dialogue: event={eventKey} stage={evt?.Stage.ToString() ?? "none"} " +
                        $"trigger={trigger} partner={partner?.Name?.ToString() ?? character?.Name?.ToString() ?? agent?.Name?.ToString() ?? "(template)"} nodes={script.Nodes.Count}");
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
                TryInjectCrimeDialogue(conversationPartnerData.Character?.HeroObject,
                    character: conversationPartnerData.Character);
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
        public static void Postfix(ConversationManager __instance)
        {
            // 对话结束 → 释放全局质问锁，允许其他 NPC 重新积累警戒值。
            // 锁在 MissionConversationStartPatch.Prefix/Postfix 中设置，
            // 确保整个对话周期内 UpdateAlertCognition 被冻结。
            var releasingBrain = AgentBrain.ConfrontingBrain;
            AgentBrain.ConfrontingBrain = null;
            DebugLogger.Log($"[ConvLock] Release by {(releasingBrain?.Owner?.Name ?? "null")}(Idx={releasingBrain?.Owner?.Index.ToString() ?? "?"}) | reason=EndConversation");

            DebugLogger.Log($"[ConvEnd] Conversation ended. lastEvent={ConversationEntryPatch._lastInjectedEventId} lastTag={ConversationEntryPatch._lastInjectedTag}");
            ConversationEntryPatch._lastInjectedEventId = null;
            ConversationEntryPatch._lastInjectedTag = null;

            // 🆕 清理残留 trigger（防御：正常路径在 TryInjectCrimeDialogue 中已消费，此处兜底）
            if (ConversationEntryPatch._pendingTrigger != DialogueTrigger.Normal)
            {
                DebugLogger.Log($"[ConvEnd] Cleaning up stale trigger: {ConversationEntryPatch._pendingTrigger}");
                ConversationEntryPatch._pendingTrigger = DialogueTrigger.Normal;
                ConversationEntryPatch._pendingConfrontation = default;
                ConversationEntryPatch._pendingTriggerAction = default;
            }

            // 🆕 清除砍价标记：对话结束 → 下次对话可以重新砍价
            {
                var st = Settlement.CurrentSettlement ?? Hero.MainHero?.CurrentSettlement;
                if (st != null)
                {
                    var activeEvt = WorldEventStore.FindActive(st.StringId);
                    if (activeEvt != null)
                    {
                        activeEvt._haggleAttempted = false;
                        activeEvt._hagglePrice = 0;
                        DebugLogger.Log($"[ConvEnd] Haggle flag cleared for {activeEvt.EventId}");
                    }
                }
            }

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

            // 🆕 投降谈判破裂延迟战斗：对话关闭后向 NPC 发送 event_surrender_refused。
            // 对标 ThreatIntent.PendingCombatAgent 的两阶段模式：
            // PlayerSurrenderThreatenIntent.OnFail / ResolveNpcSurrenderIntent.refuse / FightOnIntent
            // 在对话中只设置 PendingSurrenderRefusedAgent 标记，此处消费。
            // ⚠️ 必须在 PostConversationCleanup 之前处理：
            //    event_surrender_refused → Brain 设 PendingPostConversationCleanup=false →
            //    下游 PostConversationCleanup 检查自然跳过。
            var surrenderRefusedAgent = FightOnIntent.PendingSurrenderRefusedAgent;
            if (surrenderRefusedAgent != null)
            {
                FightOnIntent.PendingSurrenderRefusedAgent = null;
                AgentAIController.Instance?.SendEventToAgent(
                    surrenderRefusedAgent, "event_surrender_refused");
                DebugLogger.Log($"[ConvEnd] SurrenderRefused → event_surrender_refused sent to {surrenderRefusedAgent.Name}(Idx={surrenderRefusedAgent.Index})");
            }
            //其他情况，就让Npc回归原有行为
            {
                // 先清 ActiveConversationAgent（让 AlertForceConversationAction.IsFinished→true）
                var alertAgent = AlertForceConversationAction.ActiveConversationAgent;
                if (alertAgent != null)
                {
                    DebugLogger.Log($"[AlertForceConv] {alertAgent.Name}(Idx={alertAgent.Index}) ActiveConversationAgent 清空成功");
                    AlertForceConversationAction.ActiveConversationAgent = null;
                }

                // 从 MissionConversationLogic 获取战斗层 Agent（而不是 ConversationManager 的战役层 IAgent）
                var missionConvLogic = Mission.Current?.GetMissionBehavior<MissionConversationLogic>();
                var partnerAgent = missionConvLogic?.ConversationAgent;
                if (partnerAgent != null && partnerAgent != Agent.Main)
                {
                    var brain = AgentAIController.GetBrainForAgent(partnerAgent);
                    // ⚠️ 仅当谈判成功（Agent 仍在 StayAction 待命中）才清理。
                    // 如果 PendingPostConversationCleanup 已被 event_surrender_refused 置为 false
                    // （威胁失败 / 拼死一战），说明 Agent 已重回 FightEnemyAction，跳过。
                    if (brain != null && brain.PendingPostConversationCleanup)
                    {
                        brain.PostConversationCleanup();
                    }
                }
                else if (releasingBrain != null && releasingBrain.PendingPostConversationCleanup)
                {
                    // 兜底：投降等路径中 missionConvLogic.ConversationAgent 可能为 null，
                    // 但 ConfrontingBrain 已在 AgentBrain 发起对话时直接设置，直接用它清理。
                    releasingBrain.PostConversationCleanup();
                }

                // 广播 EndInteraction 给围观 NPC（bystanders 的 InteractedAgent 匹配时清理自己）
                if (alertAgent != null)
                {
                    try
                    {
                        AgentAIController.Instance?.BroadcastEventInRange(
                            alertAgent.Position, 15.0f, "EndInteraction", false, alertAgent);
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[ConvEnd] EndInteraction broadcast failed: {ex.Message}");
                    }
                }
            }

            // 🆕 威胁失败延迟战斗：对话关闭后向 NPC 发送 DeferredCombat 事件
            var combatAgent = ThreatIntent.PendingCombatAgent;
            if (combatAgent != null)
            {
                ThreatIntent.PendingCombatAgent = null;
                AgentAIController.Instance?.SendEventToAgent(
                    combatAgent, "DeferredCombat", Agent.Main);
                DebugLogger.Log($"[ConvEnd] DeferredCombat sent to {combatAgent.Name}(Idx={combatAgent.Index})");
            }

            // 🆕 转身就走围堵升级：对话关闭后广播 WitnessCrime（围观群众围过来）+ 点对点 ReEngageConfrontation
            var escalationAgent = WalkAwayIntent.PendingEscalationAgent;
            if (escalationAgent != null)
            {
                // 取出并清空全部围堵标记（detail/action 为嫌犯路径显式指定的质问上下文；gatherOnly 为逃脱成功只围观）
                var escalationDetail = WalkAwayIntent.PendingEscalationDetail;
                var escalationAction = WalkAwayIntent.PendingEscalationAction;
                var gatherOnly = WalkAwayIntent.PendingEscalationGatherOnly;
                WalkAwayIntent.PendingEscalationAgent = null;
                WalkAwayIntent.PendingEscalationDetail = null;
                WalkAwayIntent.PendingEscalationAction = null;
                WalkAwayIntent.PendingEscalationGatherOnly = false;

                // 复用 WitnessCrime 管道 → GroupStageManager → GatherOnLook/StayStare，NPC 围过来盯着
                // 排除 escalationAgent 自己——她已有 ReEngageConfrontation 点对点处理，
                // 不应再收 WitnessCrime_GatherOnLook 走犯罪指控流程
                AgentAIController.Instance?.BroadcastEventInRange(
                    escalationAgent.Position, 25f, "WitnessCrime",
                    exclude: new HashSet<Agent> { escalationAgent },
                    requireSight: false,
                    Agent.Main, escalationAgent);

                if (!gatherOnly)
                {
                    // 原 NPC 重新追上质问（显式 detail/action 时透传：嫌犯逃跑 → Stop+SuspectFlee 专属开场白）
                    if (escalationDetail.HasValue)
                        AgentAIController.Instance?.SendEventToAgent(
                            escalationAgent, "ReEngageConfrontation", Agent.Main, escalationDetail.Value, escalationAction ?? PlayerActionType.SuspectFlee);
                    else
                        AgentAIController.Instance?.SendEventToAgent(
                            escalationAgent, "ReEngageConfrontation", Agent.Main);
                    DebugLogger.Log($"[ConvEnd] Escalation: WitnessCrime broadcast + ReEngageConfrontation to {escalationAgent.Name}(Idx={escalationAgent.Index})");
                }
                else
                {
                    DebugLogger.Log($"[ConvEnd] Escalation (gather only): WitnessCrime broadcast around {escalationAgent.Name}(Idx={escalationAgent.Index})");
                }
            }

            // 坐牢：对话关闭后交给 PlayerDetentionBehavior 统一管（原生俘虏系统 + 原版
            // settlement_wait 俘虏界面 + 刑期 + 释放菜单 + 存档持久化）
            if (SurrenderJailIntent.PendingJailExit)
            {
                SurrenderJailIntent.PendingJailExit = false;

                var settlement = Settlement.CurrentSettlement;
                if (settlement != null)
                {
                    try
                    {
                        // 罪名已在 SurrenderJailIntent.OnInstant 里 ResolveMisconduct 结案，
                        // 这里不再传事件（避免重复结案）
                        PlayerDetentionBehavior.ApplyImmediateDetention(settlement, null, "surrender-jail");
                        DebugLogger.Log($"[ConvEnd] Player jailed by {settlement.Name}");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[ConvEnd] Jail handoff failed: {ex.Message}");
                    }
                }
            }

            // 🆕 诱捕 FadeOut：LureArrestIntent.OnSuccess 已在对话中捕获 NPC，
            // 但 Agent 需等对话窗口完全关闭后再淡出，避免 NPC 一边说话一边消失。
            var fadeAgent = LureArrestIntent.PendingFadeAgent;
            if (fadeAgent != null)
            {
                LureArrestIntent.PendingFadeAgent = null;
                try
                {
                    // IsActive 防重复：如果 Agent 已被其他路径移除则跳过
                    if (fadeAgent.IsActive())
                    {
                        fadeAgent.FadeOut(hideInstantly: false, hideMount: true);
                        DebugLogger.Log($"[ConvEnd] LureArrest FadeOut: {fadeAgent.Name}(Idx={fadeAgent.Index})");
                    }
                    else
                    {
                        DebugLogger.Log($"[ConvEnd] LureArrest FadeOut skipped (Agent already inactive): {fadeAgent.Name}");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[ConvEnd] LureArrest FadeOut failed: {ex.Message}");
                }
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
        /// <summary>
        /// 开场白替换模式的提前注入（Prefix）。
        ///
        /// PlayerSurrender / NpcSurrender / Alert 三类 trigger 生成的脚本设了
        /// SkipVanillaOpening=true，需要把 NPC 台词挂在 start token（优先级 200）
        /// 覆盖原版开场白。这必须在 StartConversation 处理 start token 之前完成。
        ///
        /// Postfix 注入来不及——那时原版开场白已经播放完毕。
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix(MissionConversationLogic __instance)
        {
            try
            {
                // ── 全局质问锁：对话开始即占领，覆盖投降/认输/质问/闲聊等所有路径 ──
                // UpdateAlertCognition 检查 ConfrontingBrain != null → 冻结其他 NPC 警戒值。
                // 锁在 EndConversation 时释放（ResetCrimeDialogueOnConversationEndPatch）。

                var trigger = ConversationEntryPatch._pendingTrigger;

                // ★ Alert trigger：取 ActiveConversationAgent（已由 AlertForceConversationAction
                // 在 StartConversation 之前设置），而不是 __instance.ConversationAgent。
                // 原因：__instance.ConversationAgent 可能返回过期值（例如玩家刚打晕的 NPC），
                // 导致 TryInjectCrimeDialogue 收到错误的 Hero partner 而跳过模板 NPC 的延迟注入路径。
                Agent effectiveAgent= AlertForceConversationAction.ActiveConversationAgent?? __instance.ConversationAgent;
                
                

                if (effectiveAgent != null)
                {
                    AgentBrain.ConfrontingBrain = AgentAIController.GetBrainForAgent(effectiveAgent);
                    DebugLogger.Log($"[ConvLock] Acquire by {effectiveAgent.Name}(Idx={effectiveAgent.Index}) | reason=MissionStartPrefix");
                }
                else
                {
                    DebugLogger.Log($"重要错误，一定要关注：[ConvLock] Prefix: effectiveAgent is null, cannot acquire lock.");
                }

                if (trigger != DialogueTrigger.PlayerSurrender
                    && trigger != DialogueTrigger.NpcSurrender
                    && trigger != DialogueTrigger.Alert)
                {
                    // 🆕 Normal trigger 的提前注入：权威 NPC 锁定玩家身份（Active+嫌犯=玩家 / Confrontation）
                    // 时脚本是 SkipVanillaOpening 模式，必须抢在原版 start token 评估之前注入。
                    // 否则 Postfix 注入太晚——原版开场白已播放、hero_main_options 也无入口，
                    // 玩家可经原版"我现在得走了"零后果离开（曾实测复现）。
                    var normalHero = (effectiveAgent?.Character as CharacterObject)?.HeroObject;
                    if (normalHero != null)
                    {
                        var st = Settlement.CurrentSettlement
                            ?? normalHero.CurrentSettlement
                            ?? Hero.MainHero?.CurrentSettlement;
                        var ev = st != null ? WorldEventStore.FindActive(st.StringId) : null;
                        if (CrimeDialogueBuilder.NeedsEarlyInjection(normalHero, ev))
                        {
                            DebugLogger.Log($"[ConvEntry] Mission start Prefix: pre-injecting confrontation (Normal trigger, SkipVanillaOpening) partner={normalHero.Name}");
                            ConversationEntryPatch.TryInjectCrimeDialogue(normalHero, effectiveAgent);
                        }
                    }
                    return;
                }

                var character = effectiveAgent?.Character as CharacterObject;
                DebugLogger.Log($"[ConvEntry] Mission start Prefix: pre-injecting for trigger={trigger} partner={character?.Name?.ToString() ?? "(template)"}");
                ConversationEntryPatch.TryInjectCrimeDialogue(character?.HeroObject, effectiveAgent);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConvEntry] Mission start Prefix error: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        public static void Postfix(MissionConversationLogic __instance)
        {
            try
            {
                // Postfix 注入对带开场白的 NPC（如村庄头人）至关重要：
                // Prefix 注入抢占 start token 后，Postfix 补充注入 hero_main_options 的 gateway 入口句。
                // 若无此段，原版开场白播完后玩家选项不显示（引擎已走过 hero_main_options 但未找到注入句）。
                var trigger = ConversationEntryPatch._pendingTrigger;
                Agent effectiveAgent;

                effectiveAgent = AlertForceConversationAction.ActiveConversationAgent ?? __instance.ConversationAgent;

                // 仅当 Prefix 未设置 ConfrontingBrain 时才设置（避免覆盖 Prefix 的正确值）
                if (AgentBrain.ConfrontingBrain == null && effectiveAgent != null)
                {
                    AgentBrain.ConfrontingBrain = AgentAIController.GetBrainForAgent(effectiveAgent);
                    DebugLogger.Log($"[ConvLock] Acquire by {effectiveAgent.Name}(Idx={effectiveAgent.Index}) | reason=MissionStartPostfix");
                }
                else if (effectiveAgent == null)
                {
                    DebugLogger.Log($"重要错误，一定要关注：[ConvLock] Postfix: effectiveAgent is null, cannot acquire lock.");
                }

                var heroObject = (effectiveAgent?.Character as CharacterObject)?.HeroObject;
                ConversationEntryPatch.TryInjectCrimeDialogue(heroObject, effectiveAgent);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConvEntry] Mission start Postfix error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 模板 NPC 警戒对话的延迟注入：在开场白 NPC 句子播放完毕后，
    /// 把 Alert 脚本的 gateway 挂在该句子的 OutputToken 上。
    ///
    /// 为什么不在对话开始前注入：模板 NPC（HeroObject==null）的对话树没有
    /// hero_main_options，注入到 start token 的 gateway 可能不可达。
    /// 此 Patch 在 ProcessSentence Postfix 中捕获开场白句子的 OutputToken，
    /// 引擎下一轮评估该 token 时就会看到我们的 gateway PlayerLine。
    ///
    /// 触发条件：_pendingTrigger == Alert 且尚未被 TryInjectCrimeDialogue 消费
    /// （对于模板 NPC，TryInjectCrimeDialogue 正常注入后 _pendingTrigger 已重置，
    /// 若注入成功则此 Patch 不触发；仅当 start token 注入不适用时才走此延迟路径）。
    /// </summary>
    [HarmonyPatch(typeof(ConversationManager), nameof(ConversationManager.ProcessSentence))]
    public static class AlertScriptDeferredInjectionPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ConversationManager __instance)
        {
            try
            {
                // 仅 Alert trigger 且尚未消费时才走延迟注入路径
                if (ConversationEntryPatch._pendingTrigger != DialogueTrigger.Alert) return;

                // 只响应 NPC 句子（玩家选项不触发）
                var sentences = Traverse.Create(__instance)
                    .Field("_sentences")
                    .GetValue<List<ConversationSentence>>();
                int currentNo = Traverse.Create(__instance)
                    .Field("_currentSentence").GetValue<int>();

                if (sentences == null || currentNo < 0 || currentNo >= sentences.Count)
                    return;

                var sentence = sentences[currentNo];
                if (sentence.IsPlayer) return; // 等 NPC 说完再注入

                // ── 确认是 NPC 句子后才消费 trigger ──
                var confrontation = ConversationEntryPatch._pendingConfrontation;
                var triggerAction = ConversationEntryPatch._pendingTriggerAction;

                // 获取本句的输出 token（引擎下一轮评估的目标）
                string outputToken = null;
                try
                {
                    var sentType = sentence.GetType();
                    var otProp = sentType.GetProperty("OutputToken",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (otProp != null)
                    {
                        int outputTokenInt = (int)otProp.GetValue(sentence);
                        var cmType = __instance.GetType();
                        var stateMapField = cmType.GetField("stateMap",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (stateMapField != null)
                        {
                            var stateMap = stateMapField.GetValue(__instance) as Dictionary<string, int>;
                            if (stateMap != null)
                            {
                                foreach (var kv in stateMap)
                                {
                                    if (kv.Value == outputTokenInt)
                                    {
                                        outputToken = kv.Key;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(outputToken))
                {
                    DebugLogger.Log($"[AlertDeferredInject] 无法获取句子 OutputToken，回退到 'start'");
                    outputToken = "start";
                }

                // 获取 speaker（模板 NPC 的 Hero 为 null）
                var agent = __instance.OneToOneConversationAgent;
                var speaker = (agent?.Character as CharacterObject)?.HeroObject;
                var settlement = Settlement.CurrentSettlement
                    ?? speaker?.CurrentSettlement
                    ?? Hero.MainHero?.CurrentSettlement;
                var evt = settlement != null ? WorldEventStore.FindActive(settlement.StringId) : null;

                // 统一走 BuildScript 构建脚本
                var speakerCharacter = agent?.Character as CharacterObject;
                var script = CrimeDialogueBuilder.BuildScript(
                    speaker, Hero.MainHero, evt,
                    DialogueTrigger.Alert, confrontation, triggerAction, speakerCharacter, speakerAgent: agent as Agent);

                if (script == null)
                {
                    DebugLogger.Log($"[AlertDeferredInject] BuildScript 返回 null！");
                    return;
                }

                DebugLogger.Log($"[AlertDeferredInject] NPC 句子结束，OutputToken='{outputToken}' → 注入 gateway");

                // 注入：设 InjectAtToken 为开场白输出 token，显式覆盖为 Gateway 模式
                script.InjectAtToken = outputToken;
                script.SkipVanillaOpening = false;
                string label = $"AlertL3_deferred_{(agent as Agent)?.Index ?? 0}";
                string result = DialogueInjector.InjectScript(script, label);
                DebugLogger.Log($"[AlertDeferredInject] 注入结果: {result}");

                // 消费 trigger，确保只注入一次
                ConversationEntryPatch._pendingTrigger = DialogueTrigger.Normal;
                ConversationEntryPatch._pendingConfrontation = default;
                ConversationEntryPatch._pendingTriggerAction = default;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[AlertDeferredInject] 注入异常: {ex.Message}");
                ConversationEntryPatch._pendingTrigger = DialogueTrigger.Normal;
                ConversationEntryPatch._pendingConfrontation = default;
                ConversationEntryPatch._pendingTriggerAction = default;
            }
        }
    }
}
