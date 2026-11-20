using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 序章对话构建器（2026-11-12，plans/逆水寒511-序章对话树.md）。
    ///
    /// 分工：
    ///   · 数据 = `ModuleData/DesignData/Dialogues/*.dialog.json`（唯一人工维护源，含中文审核副本），
    ///     文本 = Languages XML 的 `LWN_story_*` 键（铁律 13）。
    ///   · 本类只做四件事：**使用会话裁决入口 → 按状态过滤选项 → 交给 DialogueInjector 注入 → 执行已有动作/检定**。
    ///
    /// 🔴 注入时机：必须早于引擎评估 start token。统一由 ConversationEntryPatch 的
    /// `MissionConversationStartPatch.Prefix` 调 `TryInjectEarly(agent)`（全项目唯一收口）。
    /// </summary>
    public static class StoryProbeDialogue
    {
        /// <summary>调用方指定入口节点（事件接入 / 命令强制）；消费一次即清。</summary>
        public static string PendingEntryNode;

        private static string _lastTag;
        private static bool _actionFailed; // 本次原版对话付款失败后，所有后续节点动作都停止。
        private static int _conversationRound;

        /// <summary>本次入口和业务归属演员；每句真实说话者由节点Speaker决定。</summary>
        public static string CurrentNpcId;

        /// <summary>
        /// 511 剧情对话的呈现形态（2026-11-19 用户裁定：只作用于 511 玩法对话）。
        ///   "vanilla" = 原版对话流（默认，行为不变）；
        ///   "custom"  = 自建面板（StoryDialogueUiRunner），**不启动引擎对话流**。
        /// 犯罪对话 / 原版对话 / IM 恒走原版，与本开关无关。
        /// </summary>
        internal static bool UsesCustomUi => Settings.Instance != null
            && string.Equals(Settings.Instance.StoryDialogueUi ?? "", "custom", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 本次 StartConversation 已被自建面板接管 —— 入口 Prefix 读它并跳过原版对话流。
        /// 一次性：Prefix 消费后清零。
        /// </summary>
        internal static bool CustomUiTakeover;

        /// <summary>
        /// 此刻是否处在「一段剧情对话」里 —— 原版对话流与自建面板都算。
        /// 别处（门禁推人 / 首战触发 / 扣押推进 / 道具交互 / 工作让位）一律用这一个判据，
        /// 否则自建面板模式下它们会以为"没在对话"，在玩家说话时把人推走或直接开打。
        /// </summary>
        internal static bool IsDialogueActiveNow
            => StoryDialogueUiRunner.IsRunning
               || Campaign.Current?.ConversationManager?.IsConversationInProgress == true;

        /// <summary>入口 Prefix 消费接管标志（读完即清，避免影响下一次对话）。</summary>
        internal static bool ConsumeCustomUiTakeover()
        {
            bool takeover = CustomUiTakeover;
            CustomUiTakeover = false;
            return takeover;
        }
        private static Agent _entryAgent;
        private static StoryDialogueSession _entrySession;

        // ═══════════════════════════════════════════════════════════════
        // 入口
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// StartConversation 之前调用：给"这次对话的对象"注入序章对话树。
        /// 非序章 NPC / 未启用 / 加载失败一律静默返回（原版对话照常）。
        /// </summary>
        public static void TryInjectEarly(Agent agent)
        {
            try
            {
                string npcId = ResolveNpcId(agent);
                if (string.IsNullOrEmpty(npcId))
                {
                    // 不是剧情 NPC：清掉上次残留（对话激活中禁止删，交给 EndConversation）
                    if (_lastTag != null && !ConversationEntryPatch.IsConversationActive())
                    {
                        DialogueInjector.RemoveRelatedLines(_lastTag);
                        _lastTag = null;
                    }
                    return;
                }

                // 明确的犯罪质问/投降必须独占开场，不能被已有剧情会话或新 player_talk 覆盖。
                if (ConversationEntryPatch._pendingTrigger != DialogueTrigger.Normal)
                {
                    StoryDialogueRuntime.CancelSession("yield to " + ConversationEntryPatch._pendingTrigger);
                    PendingEntryNode = null;
                    DebugLogger.Log($"[StoryDialogue] opening yielded actor={npcId} trigger={ConversationEntryPatch._pendingTrigger}");
                    return;
                }

                // P0-D.3/Q01：普通玩家按 F 交谈也必须走同一请求入口；没有活跃会话时先建 player_talk 请求。
                // 被冷却/重入/原版犯罪仲裁拒绝时**不注入**，让原版树照常走，而不是静默回旧剧情入口。
                if (StoryDialogueRuntime.Current == null)
                {
                    string triggerId = "player_talk";
                    var director = StoryProbeMissionBehavior.Current?.Director;
                    if (director != null && director.TryGetDialogueEntry(agent, out string hostActor, out string eventNode))
                    {
                        triggerId = "event_join";
                        DebugLogger.Log($"[StoryDialogue] player F event join clicked={npcId} host={hostActor} groupNode={eventNode}");
                        npcId = hostActor;
                    }
                    string dlg = StoryDialogueResolver.FindDialogueForActor(npcId, triggerId);
                    if (!string.IsNullOrEmpty(dlg))
                    {
                        if (!StoryDialogueRuntime.TryRequest(dlg, triggerId, npcId, null, null,
                                out var requested, out string why))
                        {
                            DebugLogger.Log($"[StoryDialogue] player_talk rejected dialogue={dlg} actor={npcId}: {why}");
                            PendingEntryNode = null;
                            return;
                        }
                        _entryAgent = agent;
                        _entrySession = requested;
                        if (!StoryDialogueRuntime.ConfirmForOpen(requested, out why))
                        {
                            StoryDialogueRuntime.CancelSession(why);
                            PendingEntryNode = null;
                            return;
                        }
                    }
                }

                var session = StoryDialogueRuntime.Current;
                bool fromDefinition = session != null && !session.Ended
                    && string.Equals(session.Request?.ActorId, npcId, StringComparison.Ordinal);

                DialogueInjector.DialogueInjectScript built;
                string entry;
                if (fromDefinition)
                {
                    // P0-D.5：已迁移的定义整份来自 story_dialogue/2，节点内容不再读旧文件。
                    entry = session.Entry.Node;
                    PendingEntryNode = null;
                    // 真实 NPC sentence consequence 是唯一进入点；玩家选项不提前提交下一句。
                    DialogueInjector.CustomNodeEnter = null;
                    built = BuildFilteredFromDefinition(session.Definition, session, npcId);
                    if (built == null)
                    {
                        StoryDialogueRuntime.CancelSession("definition build failed");
                        DebugLogger.Log($"[StoryDialogue] build from definition failed npc={npcId} entry={entry}");
                        return;
                    }
                }
                else
                {
                    // 正式定义未裁决出本演员的会话时，不再从旧文件猜入口或补一棵树。
                    DebugLogger.Log($"[StoryDialogue] no authorized definition session for '{npcId}'; injection skipped");
                    PendingEntryNode = null;
                    return;
                }

                if (!ConversationEntryPatch.IsConversationActive())
                {
                    // 只在一段新 talk 开始清失败；同一对话节点重入不能恢复已失败付款的奖励资格。
                    _actionFailed = false;
                    unchecked { _conversationRound++; }
                    if (_lastTag != null) DialogueInjector.RemoveRelatedLines(_lastTag);
                    DialogueInjector.RemoveRelatedLines("story_probe_" + npcId);
                }

                // 两个钩子：检定与 PROBE 动作
                DialogueInjector.CustomCheck = RunCheck;
                DialogueInjector.CustomAction = HandleAction;

                CurrentNpcId = npcId;

                // ── 511 自建对话面板（2026-11-19）──
                // 同一份对话图、同一套后果；区别只是不进 ConversationManager。
                if (UsesCustomUi && StoryDialogueUiRunner.Start(built, agent, npcId, session))
                {
                    _lastTag = null;
                    // 会话效果落账靠这两个钩子（选项提交 / 检定掷骰），与原版路径共用同一实现。
                    DialogueInjector.CustomNodeEnter = null;
                    CustomUiTakeover = true;
                    DebugLogger.Log($"[StoryProbe] custom-ui takeover npc={npcId} entry={entry} nodes={built.Nodes.Count}");
                    PendingEntryNode = null;
                    return;
                }
                // 自建面板启动失败（面板打不开/入口节点缺失）→ 继续走原版注入，玩家不会两手空空。
                if (UsesCustomUi)
                    DebugLogger.Log($"[StoryProbe] custom-ui unavailable for npc={npcId}; falling back to vanilla flow");

                _lastTag = "story_probe_" + npcId;
                string result = DialogueInjector.InjectScript(built, _lastTag);
                DebugLogger.Log($"[StoryProbe] inject npc={npcId} entry={entry} nodes={built.Nodes.Count} -> {result}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] TryInjectEarly failed: {ex}");
                PendingEntryNode = null;
            }
        }

        /// <summary>原版启动生命周期通知；节点进入由真实sentence consequence处理。</summary>
        public static void OnConversationStarted()
        {
            // 保留生命周期通知入口，节点效果已由原版实际展示该句时提交。
        }

        /// <summary>
        /// 节点真正到达时才播这一句配的动作。
        /// 🔴 只由原版真实处理该句后的OnShown调用，
        ///    绝不放进 LazyNpcLine —— 文本读取是纯读，读一次不能触发一次表演。
        /// </summary>
        private static void PlayNodeLineActions(string nodeId)
        {
            try
            {
                var session = StoryDialogueRuntime.Current;
                var node = session?.Definition?.FindNode(nodeId);
                if (node?.LineActions == null || node.LineActions.Count == 0) return;
                var host = StoryProbeMissionBehavior.Current;
                StoryLineActions.Play(host, ResolveTurnAgent(session, node.Speaker), node.LineActions.ToArray());
            }
            catch (Exception ex)
            { DebugLogger.Log($"[StoryDialogue] line actions on '{nodeId}' failed: {ex.Message}"); }
        }

        /// <summary>对话结束（ConversationManager.EndConversation postfix 调用）：清理注入的台词，释放钩子。</summary>
        public static void OnConversationEnded()
        {
            ReleaseDialogueFacing();
            ReleaseDialogueCamera();
            _entryAgent = null;
            _entrySession = null;
            try
            {
                // P0-D.4：会话结束是第三个（也是最后一个）效果提交点；异常也必须走到，不能把窗口留在打开态。
                StoryDialogueRuntime.EndSession("conversation ended");
                DialogueInjector.CustomNodeEnter = null;
                if (_lastTag != null)
                {
                    DialogueInjector.RemoveRelatedLines(_lastTag);
                    _lastTag = null;
                }
                DialogueInjector.CustomCheck = null;
                DialogueInjector.CustomAction = null;
                PendingEntryNode = null;
                CurrentNpcId = null;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] OnConversationEnded failed: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 身份 / 入口
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Agent → 序章 NPC id（查 Mission 行为里那张表；不是剧情 NPC 返回 null）。</summary>
        public static string ResolveNpcId(Agent agent)
        {
            if (agent == null) return null;
            var host = StoryProbeMissionBehavior.Current;
            if (host == null || !host.Active) return null;
            foreach (var kv in host.AllAgents)
                if (kv.Value == agent) return kv.Key;
            return null;
        }

        private static Agent ResolveTurnAgent(StoryDialogueSession session, string identity)
        {
            if (session == null || string.IsNullOrEmpty(identity)) return null;
            if (identity == "player") return Agent.Main;
            string actor = identity;
            if ((identity == "npc" || identity == "questioner") && session.Definition.Participants.Contains(identity))
                actor = session.Request.ActorId;
            else if (!session.Definition.Actors.Contains(identity)) return null;
            return StoryProbeMissionBehavior.Current?.GetAgent(actor);
        }

        private static bool CanUseTurnAgent(Agent agent)
        {
            if (agent == null || !AgentControlHelper.SafeIsActive(agent) || agent.Health <= 0) return false;
            if (agent == Agent.Main) return true;
            var brain = AgentAIController.GetBrainForAgent(agent);
            return brain == null || (!brain.IsBound && !brain.IsStunned && !brain.IsStatusHeld && !brain.IsInCombat
                && (!brain.IsPerforming || StoryProbeMissionBehavior.Current?.Director?.HasYieldedDialogueActor(agent) == true));
        }

        private static bool IsTurnValid(StoryDialogueSession session, StoryDialogueNodeDef node)
        {
            if (StoryDialogueRuntime.Current != session || session.Ended || session.Failed
                || session.Request.RuntimeToken != StoryProbeState.RuntimeToken) return false;
            var speaker = ResolveTurnAgent(session, node.Speaker);
            var listener = ResolveTurnAgent(session, node.Listener);
            if (speaker == listener || !CanUseTurnAgent(speaker) || !CanUseTurnAgent(listener)) return false;
            return StoryDialogueFacts.TryEvaluateAll(node.Requires, new StoryDialogueContext
            {
                DialogueId = session.Definition.DialogueId, TriggerId = session.Trigger?.Id,
                Purpose = session.Trigger?.Purpose ?? "talk", ActorId = session.Request.ActorId,
            }, out bool allowed, out _, out _) && allowed;
        }

        private static bool OwnsConversation(StoryDialogueSession session, ConversationManager cm)
        {
            if (session == null || StoryDialogueRuntime.Current != session || session.Ended
                || session.Request.RuntimeToken != StoryProbeState.RuntimeToken) return false;
            // 🔴 自建面板模式下引擎会话根本没启动（cm.ConversationAgents 恒空），归属判据改问运行器。
            if (StoryDialogueUiRunner.Owns(session)) return true;
            if (cm == null) return false;
            var primary = ResolveTurnAgent(session, session.Request.ActorId);
            return primary != null && (cm.ConversationAgents.Contains(primary)
                || (_entrySession == session && _entryAgent != null && cm.ConversationAgents.Contains(_entryAgent)));
        }

        /// <summary>已裁决的剧情会话所用真实演员；普通犯罪网关不能再覆盖其开场或选项。</summary>
        internal static bool OwnsCurrentConversationAgent(Agent agent)
        {
            var session = StoryDialogueRuntime.Current;
            return agent != null && session != null && !session.Failed
                && OwnsConversation(session, Campaign.Current?.ConversationManager)
                && (agent == ResolveTurnAgent(session, session.Request.ActorId)
                    || (_entrySession == session && agent == _entryAgent));
        }

        private static bool PrepareTurn(StoryDialogueSession session, StoryDialogueNodeDef node)
        {
            var cm = Campaign.Current?.ConversationManager;
            var primary = ResolveTurnAgent(session, session.Request.ActorId);
            if (!OwnsConversation(session, cm) || session.Failed)
                return RejectTurn(session, node.Id);
            var required = new List<Agent> { primary, ResolveTurnAgent(session, node.Speaker), ResolveTurnAgent(session, node.Listener) };
            // 玩家选项的听话对象也必须在选项出现前到位；不是文本查询时才加入。
            foreach (var option in session.Definition.OptionsFor(node))
            {
                var listener = ResolveTurnAgent(session, option.Listener);
                required.Add(listener);
            }
            // 原版首句在 StartConversation 内同步处理；不能等下一帧 Brain/导演才归还身体。
            // 只让出剧情导演拥有的工作/环境/群戏租约；查询保持纯读，外部演出仍拒绝。
            var host = StoryProbeMissionBehavior.Current;
            foreach (var actor in required.Distinct())
            {
                host?.Work?.YieldActor(ResolveNpcId(actor), "dialogue handoff");
                host?.Director?.YieldActorForDialogue(actor);
            }
            if (!IsTurnValid(session, node) || required.Any(a => !CanUseTurnAgent(a)))
                return RejectTurn(session, node.Id);
            if (!StoryDialogueUiRunner.Owns(session))
                cm.AddConversationAgents(required.Where(a => a != Agent.Main).Distinct(), false);
            DebugLogger.Log($"[StoryDialogue] prepared session={session.Id} node={node.Id} entry={ResolveNpcId(_entryAgent)} host={session.Request.ActorId} speaker={node.Speaker} listener={node.Listener} participants={string.Join("+", required.Distinct().Select(a => ResolveNpcId(a) ?? "player"))}");
            return true;
        }

        private static StoryDialogueSession _facingSession;
        private static StoryDialogueNodeDef _facingNode;
        private static Mission _facingMission;
        private static float _facingElapsed;
        private static bool _facingReported;
        private static readonly HashSet<Agent> _facingActors = new HashSet<Agent>();
        private static readonly Dictionary<Agent, AgentControlHelper.FacingTracker> _facingTrackers
            = new Dictionary<Agent, AgentControlHelper.FacingTracker>();

        private static void BeginDialogueFacing(StoryDialogueSession session, StoryDialogueNodeDef node)
        {
            if (_facingSession != session) ReleaseDialogueFacing();
            _facingSession = session;
            _facingNode = node;
            _facingMission = Mission.Current;
            _facingElapsed = 0f;
            _facingReported = false;
            if (Settings.Instance.ShowPerformanceFacingDebugMessages)
                DebugLogger.Log($"[StoryDialogueFacing] tracking requested session={session.Id} node={node.Id} speaker={node.Speaker} listener={node.Listener} body=scripted-position-direction+FaceToActor gaze=SetLookAgent");
            TickDialogueFacing(0f);
        }

        /// <summary>正式会话逐句持有朝向；不重新申请演出动作，不控制玩家，不播放/推进台词。</summary>
        public static void TickDialogueFacing(float dt)
        {
            TickDialogueCamera(dt);
            if (_facingSession == null) return;
            // 自建面板模式：引擎会话没启动，归属判据问运行器（否则每个 Tick 都会把朝向释放掉）。
            bool customUi = StoryDialogueUiRunner.Owns(_facingSession);
            var cm = Campaign.Current?.ConversationManager;
            if (Mission.Current != _facingMission || StoryDialogueRuntime.Current != _facingSession
                || _facingSession.Ended || _facingSession.Failed
                || (!customUi && cm?.IsConversationInProgress != true))
            { ReleaseDialogueFacing(); return; }
            var speaker = ResolveTurnAgent(_facingSession, _facingNode.Speaker);
            var listener = ResolveTurnAgent(_facingSession, _facingNode.Listener);
            if (speaker == listener || !CanUseTurnAgent(speaker) || !CanUseTurnAgent(listener)
                || (!customUi && speaker != Agent.Main && cm?.IsConversationAgent(speaker) != true)
                || (!customUi && listener != Agent.Main && cm?.IsConversationAgent(listener) != true))
            {
                var session = _facingSession;
                string nodeId = _facingNode.Id;
                ReleaseDialogueFacing();
                RejectTurn(session, nodeId);
                return;
            }
            foreach (var old in _facingActors.ToList())
                if (old != speaker && old != listener) ReleaseFacingActor(old);
            HoldDialogueFacing(speaker, listener, dt);
            HoldDialogueFacing(listener, speaker, dt);
            _facingElapsed += Math.Max(0f, dt);
            if (!_facingReported && _facingElapsed >= 0.5f)
            {
                _facingReported = true;
                if (Settings.Instance.ShowPerformanceFacingDebugMessages)
                {
                    DebugLogger.Log("[StoryDialogueFacing] facing sample " + DescribeDialogueFacing());
                    InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompoundMixed(
                        "LWN_dialogue_facing_debug", "[Dialogue debug] {SPEAKER} -> {LISTENER}: body error {ANGLE} degrees (0 = facing the listener); gaze requested.",
                        ("SPEAKER", (object)speaker.Name), ("LISTENER", (object)listener.Name),
                        ("ANGLE", (object)FacingError(speaker, listener)))));
                }
            }
        }

        private static void HoldDialogueFacing(Agent actor, Agent target, float dt)
        {
            if (actor == Agent.Main) return;
            _facingActors.Add(actor);
            if (!_facingTrackers.TryGetValue(actor, out var tracker))
                _facingTrackers[actor] = tracker = new AgentControlHelper.FacingTracker();
            tracker.Tick(actor, target, true, true, dt,
                $"dialogue:{_facingSession.Id}/{_facingNode.Id}");
        }

        private static string FacingError(Agent actor, Agent target)
        {
            if (!AgentControlHelper.SafeIsActive(actor) || !AgentControlHelper.SafeIsActive(target)) return "n/a";
            Vec2 direction = (target.Position - actor.Position).AsVec2;
            Vec2 facing = actor.Frame.rotation.f.AsVec2;
            if (direction.LengthSquared < 0.0001f || facing.LengthSquared < 0.0001f) return "n/a";
            direction.Normalize(); facing.Normalize();
            double dot = Math.Max(-1d, Math.Min(1d, direction.x * facing.x + direction.y * facing.y));
            return (Math.Acos(dot) * 180d / Math.PI).ToString("F1", CultureInfo.InvariantCulture);
        }

        public static string DescribeDialogueFacing()
        {
            if (_facingSession == null) return "facing=inactive";
            var speaker = ResolveTurnAgent(_facingSession, _facingNode.Speaker);
            var listener = ResolveTurnAgent(_facingSession, _facingNode.Listener);
            return $"facing=session:{_facingSession.Id} node={_facingNode.Id} speaker={ResolveNpcId(speaker) ?? "player"} listener={ResolveNpcId(listener) ?? "player"} bodyErrorDeg={FacingError(speaker, listener)} listenerBodyErrorDeg={FacingError(listener, speaker)} gaze=SetLookAgent-requested";
        }

        private static void ReleaseFacingActor(Agent actor)
        {
            // 🔴 必须「谁摆的谁收」：只要本层真的给他写过站位，就一定把 scripted frame 清掉。
            //    旧写法在 `!CanUseTurnAgent(actor)` 时直接 return —— 那一帧身体状态一变
            //    （战斗中/被打断/正在执行别的表演）就留着一个"钉在原地"的 frame，
            //    实机表现就是「谈完话再也不走」。清帧只针对我们自己写过的那个合同。
            bool weHeld = _facingActors.Remove(actor);
            _facingTrackers.Remove(actor);
            if (!weHeld || actor == null) return;
            if (Mission.Current != _facingMission || !AgentControlHelper.SafeIsActive(actor)) return;
            actor.ClearTargetFrame();
            AgentControlHelper.StopLooking(actor);
        }

        public static void ReleaseDialogueFacing()
        {
            foreach (var actor in _facingActors.ToList()) ReleaseFacingActor(actor);
            _facingSession = null;
            _facingNode = null;
            _facingMission = null;
        }

        /// <summary>
        /// 每句展示时把"相机关注对象"切到本句真正的主角：说话人是玩家 → 关注听话人；否则关注说话人。
        /// 原版 MissionConversationCameraView 每帧做同一件事，但它依赖 SpeakerAgent / 原版视图链；
        /// 2026-11-17 实机出现"说话人换了、镜头没动"，所以这里显式补一次（幂等，不夺控制权、不摆相机）。
        /// 只有 MissionScreen 的构图重算需要 ConversationCharacterChanged，缺了它目标值不会更新。
        /// </summary>
        private static void ApplyConversationCameraFocus(StoryDialogueSession session, StoryDialogueNodeDef node)
        {
            try
            {
                var mission = Mission.Current;
                var main = Agent.Main;
                if (mission == null || main == null) return;
                var speaker = ResolveTurnAgent(session, node.Speaker);
                var listener = ResolveTurnAgent(session, node.Listener);
                if (speaker == null || listener == null) return;

                var focus = speaker == main ? listener : speaker;
                var controller = mission.GetMissionBehavior<MissionMainAgentController>();
                if (controller == null) return;

                V.SetConversationFocus(controller, focus);
                Vec3 direction = focus.Position - main.Position;
                if (direction.LengthSquared > 0.0001f)
                    controller.CustomLookDir = direction.NormalizedCopy();
                main.SetLookAgent(focus);
                mission.ConversationCharacterChanged();

                // 🔴 2026-11-17 实机：焦点确实换了、进对话那次构图也生效了，但换发言人时画面不动（原版链只认第一次）。
                //    这里改成我们自己摆机位：等下一帧 `Agent.LookFrame` 跟上新方向后，用剧情机位接口平滑切过去。
                //    机位模板 = 中远景（玩家侧过肩，FOV 50）：既对准说话人，又能把其他在场的人留在画里。
                FocusDialogueCamera(focus);

                // 验收用：mode 必须是 Conversation(1)/Barter(5)，customCamera 必须是 False，focused 必须等于本句主角。
                var screen = controller.MissionScreen;
                var focused = controller.InteractionComponent.CurrentFocusedObject as Agent;
                DebugLogger.Log($"[StoryDialogueCamera] node={node.Id} speaker={ResolveNpcId(speaker) ?? "player"} listener={ResolveNpcId(listener) ?? "player"} focus={ResolveNpcId(focus) ?? "player"} mode={mission.Mode} firstPerson={mission.CameraIsFirstPerson} customCamera={(screen?.CustomCamera != null)} focused={ResolveNpcId(focused) ?? "none"}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[StoryDialogueCamera] skipped: " + ex.Message);
            }
        }

        // ── 剧情对话机位（2026-11-17 起由我们接管，原版链在换发言人时不工作）──
        private const string DialogueCameraOwner = "dialogue";
        private const string DialogueCameraCase = "dlg_group_mid";
        private static Agent _pendingCameraSpeaker;
        private static float _pendingCameraDelay = -1f;
        private static Agent _lastCameraFocus;

        /// <summary>
        /// **玩家按下交互键那一刻**就接管相机（在 `MissionConversationLogic.StartConversation` 之前调）。
        /// 为什么必须这么早：引擎切对话模式时会把相机"瞬移"到 NPC 身上（`_cameraApplySpecialMovementsInstantly`），
        /// 晚一步接管 = 先硬切一下再过渡。先占住 `CustomCamera`，`CheckForUpdateCamera` 整段跳过，引擎根本没机会切。
        /// 起点 = 当前呈现帧（玩家正看着 NPC 的走路机位），所以是从玩家视角平滑推过去。
        /// </summary>
        /// <summary>剧情对话专用入口：只有确实开出了剧情会话才接管相机（普通原版对话一律放过）。</summary>
        internal static bool ReserveDialogueCameraEarly(Agent partner)
        {
            var session = StoryDialogueRuntime.Current;
            if (session == null || session.Ended || session.Failed) return false;
            return ReserveDialogueCamera(partner);
        }

        /// <summary>
        /// **多人对话通用机位①：进场即接管**。在 `MissionConversationLogic.StartConversation` **之前**调。
        /// 为什么必须这么早：引擎切对话模式时会把相机"瞬移"到 NPC 身上（`_cameraApplySpecialMovementsInstantly`），
        /// 晚一步接管 = 先硬切一下再过渡。先占住 `CustomCamera`，`CheckForUpdateCamera` 整段跳过，引擎根本没机会切。
        /// 起点 = 当前呈现帧（玩家正看着 NPC 的走路机位），所以是从玩家视角平滑推过去。
        /// 返回 true = 相机已经是我们的（或本来就是我们）。
        /// </summary>
        public static bool ReserveDialogueCamera(Agent partner)
        {
            try
            {
                if (partner == null || Agent.Main == null) return false;
                if (CameraService.IsHeldBy(DialogueCameraOwner)) return true;
                bool ok = CameraService.BlendPose(DialogueCameraCase, partner, Agent.Main, Vec3.Zero, DialogueCameraOwner, 0.7f);
                _lastCameraFocus = partner;
                _pendingCameraDelay = -1f;
                _pendingCameraSpeaker = null;
                DebugLogger.Log($"[StoryDialogueCamera] early reserve partner={ResolveNpcId(partner) ?? "-"} ok={ok} holder={CameraService.Holder ?? "none"}");
                return ok;
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[StoryDialogueCamera] early reserve failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// **多人对话通用机位②：换发言人**。每次"这句谁在说/谁在被说"变化时调一次（不用每帧调）。
        /// 规则：同一人连续说话**不重摆**（否则每句一次微调 = 画面一直晃）；相机被别的系统顶掉则下一句自动拿回来。
        /// 真正摆机位延后 0.25 秒，等玩家 `LookFrame` 转向新说话人后再算——机位方向就是靠这个朝向决定的。
        /// </summary>
        public static void FocusDialogueCamera(Agent focus)
        {
            if (focus == null) return;
            if (_lastCameraFocus != focus)
            {
                _lastCameraFocus = focus;
                _pendingCameraSpeaker = focus;
                _pendingCameraDelay = 0.25f;
            }
            else if (_pendingCameraDelay < 0f && !CameraService.IsHeldBy(DialogueCameraOwner))
            {
                _pendingCameraSpeaker = focus;
                _pendingCameraDelay = 0.25f;
            }
        }

        /// <summary>**多人对话通用机位③：每帧推进**（由 MissionBehavior 的 tick 调）。</summary>
        public static void TickDialogueCamera(float dt)
        {
            if (_pendingCameraDelay >= 0f)
            {
                _pendingCameraDelay -= dt;
                if (_pendingCameraDelay < 0f)
                {
                    var speaker = _pendingCameraSpeaker;
                    _pendingCameraSpeaker = null;
                    if (speaker != null && Agent.Main != null)
                    {
                        try
                        {
                            bool ok = CameraService.BlendPose(DialogueCameraCase, speaker, Agent.Main, Vec3.Zero, DialogueCameraOwner, 0.45f);
                            DebugLogger.Log($"[StoryDialogueCamera] blend case={DialogueCameraCase} speaker={ResolveNpcId(speaker) ?? "player"} ok={ok} holder={CameraService.Holder ?? "none"}");
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.Log("[StoryDialogueCamera] blend failed: " + ex.Message);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// **多人对话通用机位④：收场归还**。在会话结束（EndConversation）时调。
        /// 滑 0.6 秒回引擎接管那一刻的机位再撒手——引擎"模式切回"的构图接在终帧上，避免硬切。
        /// </summary>
        public static void ReleaseDialogueCamera(float seconds = 0.6f)
        {
            _pendingCameraDelay = -1f;
            _pendingCameraSpeaker = null;
            _lastCameraFocus = null;
            if (CameraService.IsHeldBy(DialogueCameraOwner))
            {
                CameraService.RequestHandBack(seconds);
                DebugLogger.Log($"[StoryDialogueCamera] camera hand-back requested ({seconds:F1}s)");
            }
            else
            {
                DebugLogger.Log($"[StoryDialogueCamera] release skipped: holder={CameraService.Holder ?? "none"}");
            }
        }

        private static bool RejectTurn(StoryDialogueSession session, string nodeId)
        {
            // 迟到的旧回调不能关闭新会话。
            if (StoryDialogueRuntime.Current != session) return false;
            session.Failed = true;
            session.EndReason = "turn participants or conditions unavailable: " + nodeId;
            DebugLogger.Log("[StoryDialogue] " + session.EndReason);
            FailStoryActionConversation();
            return false;
        }

        /// <summary>
        /// P0-D.5：把已发布的 `story_dialogue/2` 定义构造成可注入脚本。
        /// 🔴 与旧路径的区别：条件走三态求值（错误不当 false）、效果走会话提交（带收据）、
        ///    检定标签指向**本次实际说话者**而不是写死铁头。
        /// </summary>
        private static DialogueInjector.DialogueInjectScript BuildFilteredFromDefinition(
            StoryDialogueDef def, StoryDialogueSession session, string npcId)
        {
            if (def == null || session == null || session.Entry == null) return null;
            var result = new DialogueInjector.DialogueInjectScript
            {
                Npc = npcId,
                EntryNode = session.Entry.Node,
                EntryOption = LWNTextHelper.ResolveText(def.EntryOption ?? "LWN_story_entry_option", "Say a word."),
                SkipVanillaOpening = def.SkipVanillaOpening,
                Nodes = new List<DialogueInjector.DialogueNode>(),
            };

            var ctx = new StoryDialogueContext
            {
                DialogueId = def.DialogueId,
                TriggerId = session.Trigger?.Id,
                Purpose = session.Trigger?.Purpose ?? "talk",
                ActorId = npcId,
            };

            foreach (var node in def.Nodes)
            {
                if (node == null || string.IsNullOrEmpty(node.Id)) continue;
                var copy = new DialogueInjector.DialogueNode
                {
                    Id = node.Id,
                    NextNode = node.NextNode,
                    Speaker = node.Speaker,
                    Listener = node.Listener,
                    NpcKey = node.TextKey,
                    NpcFallback = null,
                    NpcActions = node.LineActions != null && node.LineActions.Count > 0 ? node.LineActions.ToArray() : null,
                    Transitions = new List<DialogueInjector.DialogueTransition>(),
                };

                // 文本查询仅取正文；真实展示该句才调用OnShown，不能把查询次数当进入次数。
                copy.LazyNpcLine = () => LWNTextHelper.ResolveText(node.TextKey, null);
                int preparedTurn = 0, shownTurn = -1;
                // F点到群戏成员时，业务主持人可能尚未加入；必须先认真实入口，再准备本句人物。
                copy.OwnsTurn = () => OwnsConversation(session, Campaign.Current?.ConversationManager);
                copy.PrepareTurn = () =>
                {
                    try
                    {
                        if (!PrepareTurn(session, node)) return false;
                        preparedTurn++;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log("[StoryDialogue] prepare turn exception: " + ex.Message);
                        return RejectTurn(session, node.Id);
                    }
                };
                copy.RuntimeCondition = () => IsTurnValid(session, node);
                copy.RuntimeSpeaker = agent => agent == ResolveTurnAgent(session, node.Speaker);
                copy.RuntimeListener = agent => agent == ResolveTurnAgent(session, node.Listener);
                copy.OnShown = () =>
                {
                    if (StoryDialogueRuntime.Current != session || shownTurn == preparedTurn) return;
                    shownTurn = preparedTurn;
                    if (!IsTurnValid(session, node) || !StoryDialogueRuntime.NotifyNodeShown(node.Id))
                    { RejectTurn(session, node.Id); return; }
                    PrologueJournalQuest.NoteShown(node.TextKey);
                    PlayNodeLineActions(node.Id);
                    // 这一句的脸：与原版动作同帧设置；不写 Face 就回到该角色的原版默认待机脸。
                    StoryFaceAnimations.PlayOrDefault(ResolveTurnAgent(session, node.Speaker), node.Face);
                    BeginDialogueFacing(session, node);
                    ApplyConversationCameraFocus(session, node);
                    DebugLogger.Log("[StoryDialogue] turn node=" + node.Id + " speaker=" + node.Speaker
                        + " listener=" + node.Listener + " next=" + (node.NextNode ?? "options/close"));
                };

                foreach (var opt in def.OptionsFor(node))
                {
                    if (opt == null || string.IsNullOrEmpty(opt.Id)) continue;
                    var t = new DialogueInjector.DialogueTransition
                    {
                        PlayerKey = opt.PlayerKey,
                        PlayerFallback = null,
                        Requires = null,
                        Once = null,
                        Action = "PROBE:dialogue_opt",
                        ActionParam = node.Id + "|" + opt.Id,
                        RuntimeSpeaker = agent => agent == Agent.Main,
                        RuntimeListener = agent => agent == ResolveTurnAgent(session, opt.Listener),
                        CloseOnFailedCheck = true,
                    };
                    t.RuntimeHint = () =>
                    {
                        string hint = DescribeOptionHint(opt, npcId);
                        return string.IsNullOrEmpty(hint) ? null : new TextObject(hint);
                    };
                    // 2026-11-19 用户裁定：随机检定不设门槛，选项永远可选；成功率可以是 0%。
                    t.RuntimeCanSelect = () => true;
                    if (opt.Check != null)
                        t.LazyPlayerLine = () => DescribeOptionLine(opt);

                    // 显示条件：选项自己的 Requires + 目标节点的准入条件（三态求值，出错 = 不显示）。
                    var option = opt;
                    t.RuntimeCondition = () =>
                    {
                        if (StoryDialogueRuntime.Current != session || session.Ended || session.Failed
                            || session.Request.RuntimeToken != StoryProbeState.RuntimeToken
                            || !CanUseTurnAgent(ResolveTurnAgent(session, option.Listener))) return false;
                        if (!StoryDialogueFacts.TryEvaluateAll(def.OptionsNode(node).Requires, ctx, out bool menuOk, out _, out _) || !menuOk) return false;
                        if (!StoryDialogueFacts.TryEvaluateAll(option.Requires, ctx, out bool ok, out _, out _) || !ok)
                            return false;
                        string target = option.Check != null ? option.Check.Success : option.NextNode;
                        if (target == node.Id || option.Check?.Fail == node.Id) return false;
                        if (string.IsNullOrEmpty(target)) return true;
                        var targetNode = def.FindNode(target);
                        if (targetNode == null) return false;
                        return StoryDialogueFacts.TryEvaluateAll(targetNode.Requires, ctx, out bool nodeOk, out _, out _)
                               && nodeOk;
                    };

                    if (opt.Check != null)
                    {
                        string target = string.IsNullOrEmpty(opt.Check.Target) || opt.Check.Target == "questioner"
                            || opt.Check.Target == "npc" ? npcId : opt.Check.Target;
                        if (!def.Actors.Contains(target) && !def.Participants.Contains(target)) target = npcId;
                        t.CheckType = DialogueInjector.TransitionCheckType.SkillCheck;
                        t.CheckTag = target + "|" + opt.Check.Tactic + "|" + (opt.Check.Capability ?? "") + "|" + opt.Check.MinimumGrowth;
                        t.NextNodeOnSuccess = opt.Check.Success ?? "";
                        t.NextNodeOnFail = opt.Check.Fail ?? "";
                    }
                    else
                    {
                        t.NextNodeOnSuccess = opt.NextNode ?? "";
                    }
                    copy.Transitions.Add(t);
                }

                // 无选项且有 NextNode = 原生连续 NPC 句；都为空 = 原生关闭，不补假玩家选项。

                result.Nodes.Add(copy);
            }

            if (!result.Nodes.Any(n => n.Id == session.Entry.Node))
            {
                DebugLogger.Log($"[StoryDialogue] definition '{def.DialogueId}' has no entry node '{session.Entry.Node}'");
                return null;
            }
            return result;
        }

        /// <summary>条件表达式：逗号 = AND，`!` 前缀 = 取反。</summary>
        public static bool EvaluateRequires(string requires, string npcId)
        {
            if (string.IsNullOrWhiteSpace(requires)) return true;

            foreach (var raw in requires.Split(','))
            {
                string term = raw.Trim();
                if (term.Length == 0) return false;

                bool negate = term.StartsWith("!");
                if (negate) term = term.Substring(1);

                bool? value = EvaluateTerm(term, npcId);
                // 求值错误不是真假值；不能被 ! 翻转成有效资格。
                if (!value.HasValue) return false;
                if (negate ? value.Value : !value.Value) return false;
            }
            return true;
        }

        /// <summary>
        /// P0-D.2：把单个条件词求值暴露给对话底座（StoryDialogueFacts）。
        /// 返回 null = 求值失败或未实现 —— 调用方**不得**把它当成 false 或取反后放行。
        /// </summary>
        public static bool? EvaluateTermForDialogue(string term, string npcId) => EvaluateTerm(term, npcId);

        private static bool? EvaluateTerm(string term, string npcId)
        {
            try
            {
                if (term.EndsWith(":", StringComparison.Ordinal) || term.EndsWith("@", StringComparison.Ordinal)) return null;
                if (term == "disguised") return StoryProbeMissionBehavior.IsWearingStoryDisguise(false);
                if (term == "noble") return StoryProbeMissionBehavior.IsWearingStoryDisguise(true);
                if (term == "stripped") return StoryProbeState.Data.Stripped;
                if (term == "can_redeem_keepsake")
                    return StoryProbeState.HasRewardReceipt("pawnbroker", "keepsake-paid")
                        || (Hero.MainHero != null && Hero.MainHero.Gold >= 30);
                if (term == "errand_ready") return CampaignTime.Now.ToHours >= StoryProbeState.Data.NextErrandHour;
                if (term == "street_help_pending")
                    return StoryProbeState.IsStreetEventResolved(1)
                        && !StoryProbeState.HasRewardReceipt("pawnbroker", "street-help");
                if (term.StartsWith("asked:")) return StoryProbeState.HasAsked(npcId, term.Substring(6));
                if (term.StartsWith("reward:")) return StoryProbeState.HasRewardReceipt(npcId, term.Substring(7));
                if (term.StartsWith("event_resolved:"))
                    return int.TryParse(term.Substring(15), NumberStyles.None, CultureInfo.InvariantCulture, out int eventId)
                        ? (bool?)StoryProbeState.IsStreetEventResolved(eventId) : null;
                if (term.StartsWith("resolution:"))
                    return StoryProbeState.Data.QuestResolutions.TryGetValue(npcId, out string resolution)
                        && resolution == term.Substring(11);
                // 跨 NPC 引用：quest_accepted@pawnbroker（"别人的委托"——收数人身上要用这个）
                if (term.StartsWith("quest_accepted@"))
                    return StoryProbeState.IsQuestAccepted(term.Substring("quest_accepted@".Length));
                if (term.StartsWith("quest_done@"))
                    return StoryProbeState.IsQuestDone(term.Substring("quest_done@".Length));
                if (term.StartsWith("quest_turned@"))
                    return StoryProbeState.IsQuestTurnedIn(term.Substring("quest_turned@".Length));

                if (term == "quest_accepted") return StoryProbeState.IsQuestAccepted(npcId);
                if (term == "quest_done") return StoryProbeState.IsQuestDone(npcId);
                if (term == "quest_turned") return StoryProbeState.IsQuestTurnedIn(npcId);
                if (term == "coop_recruitable") return Planner.Cooperation.CoopRecruitment.Evaluate(npcId).Ok;
                if (term == "coop_available") return Planner.Cooperation.CoopRecruitment.CanCooperate(npcId, null, out var coopReason);
                if (term == "coop_favor_available")
                {
                    var eligibility = Planner.Cooperation.CoopRecruitment.Evaluate(npcId);
                    return eligibility.Ok && Planner.Cooperation.CoopRecruitment.HasFavor(eligibility.Hero);
                }
                if (term == "gate_open") return StoryProbeMissionBehavior.Current?.GateOpen == true;

                if (term.StartsWith("gold>="))
                    return int.TryParse(term.Substring(6), NumberStyles.None, CultureInfo.InvariantCulture, out int gold)
                        ? (bool?)(Hero.MainHero != null && Hero.MainHero.Gold >= gold) : null;

                // 人情余额门槛（如「用这份人情换回钩爪」只在余额够时出现）。
                if (term.StartsWith("favor>="))
                {
                    if (!int.TryParse(term.Substring(7), NumberStyles.None, CultureInfo.InvariantCulture, out int need)) return null;
                    var favorHero = StoryProbeMissionBehavior.Current?.GetHero(npcId);
                    var favorMemory = favorHero != null ? AllNpcMemoryManager.GetMemory(favorHero.StringId) : null;
                    return favorMemory != null && Hero.MainHero != null
                        && favorMemory.FavorBalance(Hero.MainHero.StringId) >= need;
                }

                if (term.StartsWith("item:"))
                {
                    var item = StoryProbeDefs.FindItem(term.Substring(5));
                    return item != null && Hero.MainHero != null
                        && Hero.MainHero.PartyBelongedTo?.ItemRoster != null
                        && Hero.MainHero.PartyBelongedTo.ItemRoster.GetItemNumber(item) > 0;
                }

                // 守门人被扒空了主手（扒窃条的战果）—— 这条不读存档，读场上那个 agent
                if (term == "blocker_disarmed")
                {
                    var b = StoryProbeMissionBehavior.Current?.GetAgent("blocker");
                    return StoryProbeMissionBehavior.IsReallyDisarmed(b);
                }

                if (term.StartsWith("attitude:"))
                {
                    var d = StoryProbeDefs.GetNpc(npcId);
                    if (d == null) return false;
                    string want = term.Substring("attitude:".Length).ToLowerInvariant();
                    return (want == "open" && d.Money == MoneyAttitude.Open)
                        || (want == "covert" && d.Money == MoneyAttitude.Covert)
                        || (want == "disdain" && d.Money == MoneyAttitude.Disdain);
                }

                if (term.StartsWith("clue:")) return StoryProbeState.HasClue(term.Substring(5));
                if (term.StartsWith("hazard:")) return StoryProbeState.HasHazard(term.Substring(6));
                if (term.StartsWith("step>="))
                    return int.TryParse(term.Substring(6), NumberStyles.None, CultureInfo.InvariantCulture, out int step)
                        ? (bool?)(StoryProbeState.Step(npcId) >= step) : null;

                DebugLogger.Log($"[StoryProbe] invalid requires term '{term}' -> rejected (including negation)");
                return null;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] requires '{term}' failed: {ex.Message}");
                return null;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 检定（DialogueInjector.CustomCheck 钩子）
        // ═══════════════════════════════════════════════════════════════

        private static string DescribeOptionLine(StoryDialogueOption option)
        {
            string text = LWNTextHelper.ResolveText(option.PlayerKey, null);
            if (option.Check == null || !StoryProbeDefs.TryParseDialogueTactic(option.Check.Tactic, out var tactic)) return text;
            // 正式源已有功能前缀时保留；不再额外拼一个括号里的同义标签。
            if (text.TrimStart().StartsWith("[", StringComparison.Ordinal)) return text;
            return LWNTextHelper.ResolveCompound("LWN_story_option_check_prefix", "[{SKILL}] {LINE}",
                ("LINE", text), ("SKILL", string.IsNullOrEmpty(option.Check.Capability)
                    ? SkillCheckSystem.MapTacticToSkill(tactic).Name.ToString()
                    : StoryProbeDefs.CapabilityName(option.Check.Capability)));
        }

        public static string DescribeOptionHint(StoryDialogueOption option, string actorId)
        {
            // 🔴 2026-11-19 用户裁定：**没写 HintKey 的选项不弹浮框**。
            //    兜底文案不含任何实际信息，弹出来只是噪声。
            //    唯一例外：带检定的选项 —— 检定行是代码按真实数值生成的，有信息量，铁律 43 要求必须给。
            if (string.IsNullOrEmpty(option.HintKey) && option.Check == null) return null;

            string note = !string.IsNullOrEmpty(option.HintKey)
                ? LWNTextHelper.ResolveText(option.HintKey, null)
                : "";
            if (option.Check == null) return note;
            if (!StoryProbeDefs.TryParseDialogueTactic(option.Check.Tactic, out var tactic)) return note;
            string target = string.IsNullOrEmpty(option.Check.Target) || option.Check.Target == "npc"
                || option.Check.Target == "questioner" ? actorId : option.Check.Target;
            var preview = StoryProbeDefs.PreviewDialogueCheck(target, tactic, option.Check.Capability, option.Check.MinimumGrowth);
            if (preview == null) return LWNTextHelper.ResolveText("LWN_story_hint_check_unavailable", "The check target is unavailable.");
            string attribute = preview.Capability != null
                ? LWNTextHelper.ResolveText(StoryProbeDefs.AbilityDisplayKey(preview.Ability), preview.Ability.ToString())
                : preview.Skill.Name.ToString();
            // 统一格式（2026-11-19 用户裁定，两行）：
            //   ① 一行说清：比的是哪个属性、我方数值 vs 对方数值、成功率（不设门槛，可以是 0%）。
            //   ② 后果由作者写在 HintKey：随机检定写「成功：…｜失败：…」，确定性/无检定写会发生什么。
            // 永久值 + 道具临时加成：临时部分用括号标出，例如 "力量 20(+10) vs 70"。
            float playerBase = preview.PlayerAttr - preview.PlayerBonus;
            string playerText = playerBase.ToString("F0")
                + (preview.PlayerBonus > 0 ? "(+" + preview.PlayerBonus.ToString() + ")" : "");
            string head = LWNTextHelper.ResolveCompound("LWN_story_hint_check_line",
                "{ATTRIBUTE} {PLAYER} vs {NPC} · {CHANCE}% chance",
                ("ATTRIBUTE", attribute),
                ("PLAYER", playerText),
                ("NPC", preview.NpcAttr.ToString("F0")),
                ("CHANCE", (preview.Chance * 100f).ToString("F0")));
            return string.IsNullOrEmpty(note) ? head : head + "\n" + note;
        }

        /// <summary>CheckTag = npcId|NegotiationTactic；概率与掷点均复用已有对话轮子。</summary>
        public static bool? RunCheck(DialogueInjector.DialogueTransition transition)
        {
            if (transition == null || string.IsNullOrEmpty(transition.CheckTag)) return null;

            string[] parts = transition.CheckTag.Split('|');
            if (parts.Length < 2) return null;

            string npcId = parts[0].Trim();
            if (_actionFailed || !StoryProbeDefs.TryParseDialogueTactic(parts[1], out var tactic)) return false;

            int minimumGrowth = 0;
            if (parts.Length > 3 && (!int.TryParse(parts[3], out minimumGrowth) || minimumGrowth < 0 || minimumGrowth > 100)) return false;
            StoryRoll roll = StoryProbeDefs.RollDialogueCheck(npcId, tactic, parts.Length > 2 ? parts[2] : null, minimumGrowth);
            if (roll == null) return false;
            if (!roll.Eligible) return false;
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(roll.PlayerMessage()));
            }
            catch { }
            return roll.Success;
        }

        // ═══════════════════════════════════════════════════════════════
        // 动作（DialogueInjector.CustomAction 钩子）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>动作入口：支持复合动作 `verb1+verb2` / `param1;param2`（按位置配对）。</summary>
        public static void HandleAction(string verb, string param)
        {
            if (_actionFailed || string.IsNullOrEmpty(verb) || StoryProbeState.Data.CancellationPending
                || !StoryProbeSettings.Enabled || StoryProbeMissionBehavior.Current?.Active != true) return;
            // 节点进入动作和复合动作都可能带前缀，逐条归一化。
            string[] verbs = verb.Split('+').Select(NormalizeStoryVerb).ToArray();
            // 单条动作自己的参数可能含分号（pay_to="thug;80"），不能当成复合动作拆丢金额。
            string[] pars = verbs.Length == 1 ? new[] { param }
                : string.IsNullOrEmpty(param) ? new string[0] : param.Split(';');
            for (int i = 0; i < verbs.Length; i++)
            {
                string p = i < pars.Length ? pars[i] : null;
                bool succeeded;
                try { succeeded = Dispatch(verbs[i], p); }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[StoryProbe] action {verbs[i]} failed: {ex.Message}");
                    succeeded = false;
                }
                if (!succeeded)
                {
                    if (!_actionFailed) FailStoryActionConversation(); // 接单/交差失败也不能继续成功回答节点的自定义奖励。
                    break;
                }
            }
        }

        private static string NormalizeStoryVerb(string verb)
        {
            string normalized = (verb ?? "").Trim();
            if (normalized.StartsWith("PROBE:", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(6).Trim();
            return normalized;
        }

        private static bool FailStoryActionConversation()
        {
            _actionFailed = true;
            var cm = Campaign.Current?.ConversationManager;
            var host = StoryProbeMissionBehavior.Current;
            string npcId = CurrentNpcId;
            string ownerTag = _lastTag;
            int round = _conversationRound;
            // 自建面板模式下没有注入 tag、也没有引擎会话：归属判据改问运行器，收场改由它关面板。
            bool customUi = StoryDialogueUiRunner.Owns(StoryDialogueRuntime.Current);
            Func<bool> stillOwnsConversation = () => customUi
                ? host != null && host.Active && StoryProbeMissionBehavior.Current == host
                    && round == _conversationRound && _actionFailed
                    && CurrentNpcId == npcId && !string.IsNullOrEmpty(npcId)
                    && StoryDialogueUiRunner.Owns(StoryDialogueRuntime.Current)
                : cm != null && host != null && host.Active
                    && StoryProbeMissionBehavior.Current == host && round == _conversationRound
                    && _actionFailed && CurrentNpcId == npcId && !string.IsNullOrEmpty(npcId)
                    && _lastTag == ownerTag && ownerTag == "story_probe_" + npcId
                    && Campaign.Current?.ConversationManager == cm
                    && OwnsConversation(StoryDialogueRuntime.Current, cm);
            try
            {
                if (!stillOwnsConversation()) return false;
                host.SchedulePublic(0f, () =>
                {
                    if (!stillOwnsConversation()) return;
                    if (customUi) { StoryDialogueUiRunner.End("action failed - closed"); return; }
                    if (cm.IsConversationInProgress) cm.EndConversation();
                });
                // 复用 Injector 的 SkillCheck 路由写法，只改输出 token，不在 consequence 栈结束对话。
                // 普通付款的成功应答尚未选择时，这样阻止其显示；已显示的台词无法事后撤回。
                if (!customUi) cm.ActiveToken = cm.GetStateIndex("close_window");
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] failed payment conversation cleanup deferred: {ex.Message}"); }
            return false;
        }

        /// <summary>P0-D.4：回滚已付款项（收款人 → 玩家），与 TryPayMoney 成对。</summary>
        internal static bool RefundMoney(Hero recipient, int amount)
        {
            if (recipient == null || Hero.MainHero == null || amount <= 0) return false;
            try
            {
                int moved = AgentControlHelper.TransferGold(recipient, Hero.MainHero, amount, notify: false);
                return moved == amount;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] RefundMoney({amount}) failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Q09：数据原子调用的通用入口（不读 CurrentNpcId）。</summary>
        internal static void TurnInFor(string npcId) => TurnIn(npcId);

        /// <summary>Q09：显示一条本地化消息（key 即 fallback）。</summary>
        internal static void ShowMessageKey(string key) => Message(key, key);

        internal static bool TryPayMoney(Hero recipient, int amount)
        {
            var payer = Hero.MainHero;
            if (payer == null || recipient == null || recipient == payer || amount <= 0)
            {
                DebugLogger.Log("[StoryProbe] payment refused: real payer/recipient/positive amount required");
                Message("LWN_story_turnin_not_done", "That is not done yet.");
                return FailStoryActionConversation();
            }
            if (payer.Gold < amount)
            {
                Message("LWN_story_no_money", "You don't have enough.");
                return FailStoryActionConversation();
            }
            if ((long)recipient.Gold + amount > int.MaxValue)
            {
                Message("LWN_story_turnin_not_done", "That is not done yet.");
                return FailStoryActionConversation();
            }
            int payerBefore = payer.Gold;
            int recipientBefore = recipient.Gold;
            int transferred = 0;
            try { transferred = AgentControlHelper.TransferGold(payer, recipient, amount, notify: false); }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] payment settlement exception: {ex.Message}"); }
            int debited = Math.Max(0, payerBefore - payer.Gold);
            int credited = Math.Max(0, recipient.Gold - recipientBefore);
            // 引擎可能结算后通知抛错；双方实际足额才认付款，不能只凭调用过或余额够。
            bool paid = debited == amount && credited == amount;
            if (!paid)
            {
                // 共享转移正常是原子且足额；异常的成对部分款退回，不能留下半笔钱却继续开门。
                int refund = Math.Min(amount, Math.Min(debited, credited));
                if (refund > 0)
                {
                    try { AgentControlHelper.TransferGold(recipient, payer, refund, notify: false); }
                    catch (Exception ex) { DebugLogger.Log($"[StoryProbe] partial payment refund failed: {ex.Message}"); }
                }
                Message("LWN_story_turnin_not_done", "That is not done yet.");
            }
            DebugLogger.Log($"[StoryProbe] payment actual={transferred} debit={debited} credit={credited} required={amount} paid={paid}");
            return paid || FailStoryActionConversation();
        }

        private static bool Dispatch(string verb, string param)
        {
            var host = StoryProbeMissionBehavior.Current;
            string currentNpc = CurrentNpcId;
            DebugLogger.Log($"[StoryProbe] action {verb} param={param ?? "-"} npc={currentNpc ?? "-"}");

            switch ((verb ?? "").ToLowerInvariant())
            {
                case "forced_fail":
                    host?.PlayForcedDefeat();
                    break;

                case "accept_quest":
                    if (!string.IsNullOrEmpty(currentNpc))
                    {
                        // 原生 Issue/Quest 确认创建后才提交剧情接单标志。
                        var nativeQuest = PrologueQuestLauncher.Start(currentNpc);
                        if (nativeQuest == null)
                        {
                            Message("LWN_story_turnin_not_done", "That is not done yet.");
                            return false;
                        }
                        StoryProbeState.AcceptQuest(currentNpc);
                        StoryProbeState.SetGoal("quest");
                        // 播报带上任务名与目标（QuestLogKey 此前没有消费者，接了也不说去哪）
                        var qdef = StoryProbeDefs.GetNpc(currentNpc);
                        string qtitle = qdef != null && !string.IsNullOrEmpty(qdef.QuestLogKey)
                            ? LWNTextHelper.ResolveText(qdef.QuestLogKey, currentNpc)
                            : currentNpc;
                        string qtarget = qdef != null && !string.IsNullOrEmpty(qdef.QuestTargetNpcId)
                            ? StoryProbeMissionBehavior.NpcName(qdef.QuestTargetNpcId)
                            : "";
                        Message("LWN_story_narr_quest_accepted", "You took the job.");
                        try
                        {
                            InformationManager.DisplayMessage(new InformationMessage(
                                LWNTextHelper.ResolveCompound("LWN_story_narr_quest_taken",
                                    ("TASK", qtitle), ("TARGET", qtarget))));
                        }
                        catch { }
                        int solvedEvent = currentNpc == "butcher" ? 2 : currentNpc == "widow" ? 3 : 0;
                        if (solvedEvent > 0 && StoryProbeState.IsStreetEventResolved(solvedEvent))
                            if (!SyncResolvedStreetQuest(solvedEvent)) return false;
                    }
                    break;

                case "redeem_keepsake":
                    return RedeemKeepsake();

                case "street_help_reward":
                    return GrantStreetHelpReward();

                case "complete_quest":
                {
                    var parts = (param ?? "").Split('|');
                    return parts.Length == 2 && CompleteTargetQuest(parts[0], parts[1]);
                }

                case "resolve_event":
                {
                    var parts = (param ?? "").Split('|');
                    if (parts.Length < 1 || !int.TryParse(parts[0], out int eventId)) return false;
                    if (StoryProbeState.IsStreetEventResolved(eventId)) break;
                    if (host?.Director?.ResolveCurrentEvent(eventId) != true) return false;
                    if (!SyncResolvedStreetQuest(eventId)) return false;
                    if (parts.Length >= 3 && int.TryParse(parts[2], out int delta))
                    {
                        string faction = parts[1];
                        StoryProbeState.AddFactionRelation(faction, delta);
                        var rep = StoryProbeDefs.GetNpcByFaction(faction);
                        if (rep != null) AdjustNpcRelation(rep.Id, delta);
                    }
                    break;
                }

                case "turn_in":
                    TurnIn(currentNpc);
                    if (string.IsNullOrEmpty(currentNpc) || !StoryProbeState.IsQuestTurnedIn(currentNpc)) return false;
                    break;

                case "pay_to":
                {
                    // param = "<npcId>;<amount>"：钱给到事件里的那个人（守恒转移）
                    string npcId = "pawnbroker";
                    int amount = 50;
                    if (!string.IsNullOrEmpty(param))
                    {
                        var parts = param.Split(';');
                        if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0])) npcId = parts[0].Trim();
                        if (parts.Length > 1) int.TryParse(parts[1], out amount);
                    }
                    if (!TryPayMoney(host?.GetHero(npcId), amount)) return false;
                    break;
                }

                case "open_gate_award":
                {
                    // 检定已经掷过（CustomCheck）→ 这里只落地，不再掷第二次。
                    // param = "approach|faction|delta"
                    string approach = "force", faction = "dockhands";
                    int delta = -20;
                    bool crime = false;
                    if (!string.IsNullOrEmpty(param))
                    {
                        var parts = param.Split('|');
                        if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0])) approach = parts[0].Trim();
                        if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])) faction = parts[1].Trim();
                        if (parts.Length > 2) int.TryParse(parts[2], out delta);
                        if (parts.Length > 3) crime = parts[3].Trim() == "1";
                    }
                    host?.OpenGate(approach, "LWN_story_gate_open", "The way is clear.");
                    host?.ResolveApproach(approach, faction, delta, crime, null, currentNpc);
                    break;
                }

                case "resolve_gate":
                {
                    if (!StoryProbeDefs.TryParseAbility(param, out StoryAbility ab)) ab = StoryAbility.Force;
                    var roll = StoryProbeDefs.RollCheck("blocker", ab);
                    if (roll.Success)
                    {
                        string approach = StoryProbeDefs.AbilityId(ab);
                        host?.OpenGate(approach, "LWN_story_gate_open", "The way is clear.");
                        host?.ResolveApproach(approach,
                            ab == StoryAbility.Charm ? "players" : ab == StoryAbility.Guile ? "nightwalkers" : "dockhands",
                            ab == StoryAbility.Force ? -20 : -5,
                            ab == StoryAbility.Force, null, currentNpc);
                    }
                    else
                    {
                        Message("LWN_story_check_fail", "It did not work.");
                    }
                    break;
                }

                case "open_gate":
                    host?.OpenGate(param ?? "unknown", "LWN_story_gate_open", "The way is clear.");
                    if (!string.IsNullOrEmpty(currentNpc))
                        StoryProbeState.SetStep(currentNpc, 4);
                    break;

                case "bribe":
                {
                    // 🔴 行贿也要过"这个人收不收钱"这道闸（2026-11-13 用户裁定）：
                    //    只有 MoneyAttitude.Open 的人接；Covert 要绕正当买卖，Disdain 直接翻脸。
                    {
                        var bd = StoryProbeDefs.GetNpc(currentNpc);
                        if (bd != null && bd.Money != MoneyAttitude.Open)
                        {
                            DebugLogger.Log($"[StoryProbe] bribe refused: {currentNpc} attitude={bd.Money}");
                            Message("LWN_story_money_refused", "He doesn't take money — not like this.");
                            return FailStoryActionConversation();
                        }
                    }

                    int amount = 100;
                    int.TryParse(param, out amount);
                    if (amount <= 0) amount = 100;
                    if (!TryPayMoney(host?.GetHero(currentNpc), amount)) return false;
                    StoryProbeState.Data.BribePaid += amount; // 双方实际足额，才记打点、让路并结算路线。
                    host?.OpenGate("money", "LWN_story_gate_bribe", "He weighs the coins and steps aside.");
                    host?.ResolveApproach("money", "fixers", -5, false, "hazard_money_leverage", currentNpc);
                    break;
                }

                case "fight":
                {
                    string targetId = string.IsNullOrEmpty(param) ? currentNpc : param;
                    StartFight(targetId);
                    break;
                }

                case "met":
                    if (!string.IsNullOrEmpty(currentNpc)) StoryProbeState.MarkMet(currentNpc);
                    break;

                case "mark":
                    if (!string.IsNullOrEmpty(currentNpc) && !string.IsNullOrEmpty(param))
                        StoryProbeState.MarkAsked(currentNpc, param);
                    if (currentNpc == "blocker" && param == "met")
                        StoryProbeMissionBehavior.Current?.NotifyFirstGuardQuestioned();
                    break;

                case "clue":
                    StoryProbeState.AddClue(param);
                    StoryProbeState.SetGoal("asked");
                    break;

                case "pay":
                {
                    int amount = 30;
                    if (!int.TryParse(param, out amount) || amount < 0) amount = 30;
                    if (!TryPayMoney(host?.GetHero(currentNpc), amount)) return false;
                    break;
                }

                case "offer_money":
                {
                    // 同一条「递钱」动作，按这个人对钱的态度给三种结果（2026-11-13 用户裁定）。
                    // Open=当面收（钱走人、给实话、你被拿捏）；Covert=推回来（他自己不收，得走正经买卖）；
                    // Disdain=把这当成侮辱（关系掉，钱一动不动）。
                    var def = StoryProbeDefs.GetNpc(currentNpc);
                    if (def == null) return false;
                    int amount = def.Money == MoneyAttitude.Covert
                        ? Math.Max(1, def.MoneyCovertPrice) : Math.Max(1, def.MoneyPrice);

                    switch (def.Money)
                    {
                        case MoneyAttitude.Open:
                        {
                            if (!TryPayMoney(host?.GetHero(currentNpc), amount)) return false;
                            StoryProbeState.AddHazard("hazard_money_leverage");
                            AdjustNpcRelation(currentNpc, -5);
                            ShowHint(def);
                            DebugLogger.Log($"[StoryProbe] money OPEN {currentNpc} took {amount}");
                            break;
                        }

                        case MoneyAttitude.Covert:
                            // 当面递钱：他手都不伸。钱一分没动 —— 要走下面那条「正经买卖」。
                            DebugLogger.Log($"[StoryProbe] money COVERT {currentNpc} refused direct cash");
                            break; // 正常拒绝，继续显示专属应答。

                        default: // Disdain
                            AdjustNpcRelation(currentNpc, -10);
                            StoryProbeState.AddHazard("hazard_bribed_the_proud");
                            DebugLogger.Log($"[StoryProbe] money DISDAIN {currentNpc} insulted");
                            break;
                    }
                    break;
                }

                case "money_deal":
                {
                    // Covert 的"正经买卖"：当/买 —— 钱照收，但面上是生意。
                    var def = StoryProbeDefs.GetNpc(currentNpc);
                    if (def == null) return false;
                    int amount = Math.Max(1, def.MoneyCovertPrice);
                    if (!TryPayMoney(host?.GetHero(currentNpc), amount)) return false;
                    StoryProbeState.AddHazard("hazard_money_leverage");
                    AdjustNpcRelation(currentNpc, -5);
                    ShowHint(def);
                    DebugLogger.Log($"[StoryProbe] money DEAL {currentNpc} took {amount} (covert)");
                    break;
                }

                case "money_hint":
                {
                    var def = StoryProbeDefs.GetNpc(currentNpc);
                    if (def != null) ShowHint(def);
                    break;
                }

                case "favour":
                {
                    // param = "<faction>;<delta>"：事件里帮了谁，就给那家好感（§9 选择反馈）
                    string faction = "players";
                    int delta = 5;
                    if (!string.IsNullOrEmpty(param))
                    {
                        var parts = param.Split(';');
                        if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0])) faction = parts[0].Trim();
                        if (parts.Length > 1) int.TryParse(parts[1], out delta);
                    }
                    StoryProbeState.AddFactionRelation(faction, delta);
                    try
                    {
                        var rep = StoryProbeDefs.GetNpcByFaction(faction);
                        var hero = rep != null ? host?.GetHero(rep.Id) : null;
                        if (hero != null && Hero.MainHero != null)
                            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(hero, Hero.MainHero, delta);
                    }
                    catch (Exception ex) { DebugLogger.Log($"[StoryProbe] favour failed: {ex.Message}"); }
                    break;
                }

                case "hazard":
                    StoryProbeState.AddHazard(param);
                    break;

                case "collect_debt":
                {
                    int amount = 120;
                    if (int.TryParse(param, out int p) && p > 0) amount = p;
                    var nativeQuest = PrologueQuestLauncher.FindActive("pawnbroker");
                    if (currentNpc != "debtor" || nativeQuest == null
                        || !StoryProbeState.IsQuestAccepted("pawnbroker") || StoryProbeState.IsQuestDone("pawnbroker")) return false;
                    if (CollectDebt(amount))
                    {
                        nativeQuest.MarkObjectivesDone();
                        StoryProbeState.Data.QuestResolutions["pawnbroker"] = "charm";
                        StoryProbeState.CompleteQuest("pawnbroker");
                    }
                    else
                    {
                        Message("LWN_story_turnin_not_done", "That is not done yet.");
                        return false;
                    }
                    break;
                }

                case "errand":
                {
                    // 零活（§5.5）：每次 50 第纳尔，1 战役小时冷却。
                    // 钱从"世界"发（Grant，铁律 4 的虚空来源合法情形；注释即标注）。
                    int pay = 50;
                    if (!string.IsNullOrEmpty(param)) int.TryParse(param, out pay);
                    if (pay <= 0) pay = 50;
                    double now = CampaignTime.Now.ToHours;
                    if (now < StoryProbeState.Data.NextErrandHour)
                    {
                        Message("LWN_story_errand_cooldown", "Nothing needs doing right now. Come back later.");
                        return false;
                    }
                    var player = Hero.MainHero;
                    if (player == null || (long)player.Gold + pay > int.MaxValue) return false;
                    int before = player.Gold;
                    try { AgentControlHelper.TransferGold((Hero)null, player, pay, notify: false); }
                    catch (Exception ex) { DebugLogger.Log($"[StoryProbe] errand settlement deferred: {ex.Message}"); }
                    finally
                    {
                        int moved = Math.Min(pay, Math.Max(0, player.Gold - before));
                        StoryProbeState.Data.CoinsFound += moved;
                        if (moved > 0) StoryProbeState.Data.NextErrandHour = now + 1.0;
                    }
                    if (player.Gold - before != pay) return false;
                    Message("LWN_story_errand_done", "You ran the errand. A few coins for it.");
                    break;
                }

                case "give_disguise":
                    return GiveNobleDisguise();

                case "give_items":
                    return GiveRewards(param);

                case "start_event":
                    if (int.TryParse(param, out int ev)) host?.Director?.ForceEvent(ev);
                    break;

                case "dialogue_opt":
                {
                    // P0-D.4：选项效果由运行期统一提交（复核 + 收据 + 失败停止），不在这里逐条写账。
                    var parts = (param ?? "").Split('|');
                    if (parts.Length != 2 || string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1]))
                        return false;
                    var session = StoryDialogueRuntime.Current;
                    var node = session?.Definition?.FindNode(parts[0]);
                    // 🔴 选项可能挂在别的节点上（OptionsFrom）：必须与显示侧同一口径展开，
                    //    否则叙述节点（Options: [] + OptionsFrom）点任何选项都查不到 → 判失败关窗。
                    var option = session == null || node == null
                        ? null
                        : session.Definition.OptionsFor(node).FirstOrDefault(o => o.Id == parts[1]);
                    if (session == null || node == null || option == null)
                    {
                        DebugLogger.Log($"[StoryDialogue] option not found {parts[0]}/{parts[1]}"
                            + (node == null ? " (node missing)"
                                : $" (own={node.Options.Count} OptionsFrom={node.OptionsFrom ?? "-"})"));
                        return false;
                    }
                    if (!IsTurnValid(session, node) || !CanUseTurnAgent(ResolveTurnAgent(session, option.Listener)))
                        return RejectTurn(session, parts[0]);
                    if (!StoryDialogueRuntime.NotifyOptionSelected(parts[0], parts[1], out string reason))
                    {
                        DebugLogger.Log($"[StoryDialogue] option commit failed {parts[0]}/{parts[1]}: {reason}");
                        return FailStoryActionConversation();
                    }
                    break;
                }

                default:
                    DebugLogger.Log($"[StoryProbe] unknown action verb '{verb}'");
                    return false;
            }
            return true;
        }

        /// <summary>把关系记到当前对话的这个人身上（正负都行）。</summary>
        private static void AdjustNpcRelation(string npcId, int delta)
        {
            try
            {
                var host = StoryProbeMissionBehavior.Current;
                var hero = host?.GetHero(npcId);
                if (hero != null && Hero.MainHero != null && delta != 0)
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(hero, Hero.MainHero, delta);
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] AdjustNpcRelation({npcId}) failed: {ex.Message}"); }
        }

        /// <summary>收了钱之后多给你的那句实话（每个 NPC 一句，见 StoryNpcDef.MoneyHint*）。</summary>
        private static void ShowHint(StoryNpcDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.MoneyHintKey)) return;
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveText(def.MoneyHintKey, def.MoneyHintFallback)));
            }
            catch { }
        }

        private static void Message(string key, string fallback)
        {
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText(key, fallback)));
            }
            catch { }
        }

        private static bool RedeemKeepsake()
        {
            if (CurrentNpcId != "pawnbroker" || !StoryProbeState.HasClue("sister_1")) return false;
            var player = Hero.MainHero;
            var giver = StoryProbeMissionBehavior.Current?.GetHero("pawnbroker");
            // 缺物品或真实接收背包时先不收钱；到货失败后的重试沿用付款回执。
            if (giver == null || player?.PartyBelongedTo?.ItemRoster == null
                || StoryProbeDefs.FindItem("jewelry") == null) return false;
            if (!StoryProbeState.HasRewardReceipt("pawnbroker", "keepsake-paid"))
            {
                if (!TryPayMoney(giver, 30)) return false;
                StoryProbeState.RecordRewardReceipt("pawnbroker", "keepsake-paid");
            }
            if (!GrantRewardItem("pawnbroker", "keepsake", "jewelry")) return false;
            StoryProbeState.AddClue("sister_3");
            return true;
        }

        private static bool CompleteTargetQuest(string giverId, string resolution)
        {
            var def = StoryProbeDefs.GetNpc(giverId);
            if (def == null || def.QuestTargetNpcId != CurrentNpcId
                || !StoryProbeState.IsQuestAccepted(giverId) || StoryProbeState.IsQuestTurnedIn(giverId)
                || (resolution != "charm" && resolution != "guile" && resolution != "force")) return false;
            if (StoryProbeState.IsQuestDone(giverId)) return true;
            var nativeQuest = PrologueQuestLauncher.FindActive(giverId);
            if (nativeQuest == null) return false;
            nativeQuest.MarkObjectivesDone();
            StoryProbeState.Data.QuestResolutions[giverId] = resolution;
            StoryProbeState.CompleteQuest(giverId);
            ResolveCompletedQuestConflict(giverId);
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveCompound("LWN_story_narr_quest_done",
                        ("NAME", StoryProbeMissionBehavior.NpcName(giverId)))));
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] quest completion feedback failed: {ex.Message}"); }
            return true;
        }

        public static void ResolveCompletedQuestConflict(string giverId)
        {
            if (!StoryProbeState.IsQuestDone(giverId)) return;
            int eventId = giverId == "widow" ? 3 : giverId == "butcher" ? 2 : 0;
            if (eventId == 0) return;
            var def = StoryProbeDefs.GetNpc(giverId);
            var streetEvent = StoryProbeDefs.GetEvent(eventId);
            if (def == null || streetEvent == null
                || (def.QuestTargetNpcId != streetEvent.ActorA && def.QuestTargetNpcId != streetEvent.ActorB)) return;
            StoryProbeMissionBehavior.Current?.Director?.ResolveCurrentEvent(eventId);
            StoryProbeState.ResolveStreetEvent(eventId);
        }

        internal static bool SyncResolvedStreetQuest(int eventId)
        {
            // Event 1 is protection money, not debt; event 4 is an accusation, not the robber job.
            string giverId = eventId == 2 ? "butcher" : eventId == 3 ? "widow" : null;
            if (giverId == null) return true;
            if (!StoryProbeState.IsStreetEventResolved(eventId)) return false;
            var def = StoryProbeDefs.GetNpc(giverId);
            var streetEvent = StoryProbeDefs.GetEvent(eventId);
            if (def == null || streetEvent == null
                || (def.QuestTargetNpcId != streetEvent.ActorA && def.QuestTargetNpcId != streetEvent.ActorB)) return false;
            // Remember the actual intervention even before an Issue has been accepted.
            if (!StoryProbeState.Data.QuestResolutions.TryGetValue(giverId, out string resolution))
            {
                resolution = eventId == 2 ? "force" : "charm";
                StoryProbeState.Data.QuestResolutions[giverId] = resolution;
            }
            if (!StoryProbeState.IsQuestAccepted(giverId) || StoryProbeState.IsQuestDone(giverId)
                || StoryProbeState.IsQuestTurnedIn(giverId)) return true;
            var nativeQuest = PrologueQuestLauncher.FindActive(giverId);
            if (nativeQuest == null) return false;
            try { nativeQuest.MarkObjectivesDone(); }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] street objective log failed: {ex.Message}"); }
            if (!nativeQuest.ObjectivesDone) return false;
            StoryProbeState.CompleteQuest(giverId);
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveCompound("LWN_story_narr_quest_done",
                        ("NAME", StoryProbeMissionBehavior.NpcName(giverId)))));
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] street completion feedback failed: {ex.Message}"); }
            return true;
        }

        private static bool GrantStreetHelpReward()
        {
            const string giverId = "pawnbroker", receipt = "street-help";
            const int amount = 50;
            if (CurrentNpcId != giverId || !StoryProbeState.IsStreetEventResolved(1)) return false;
            if (StoryProbeState.HasRewardReceipt(giverId, receipt)) return true;
            var player = Hero.MainHero;
            if (player == null || (long)player.Gold + amount > int.MaxValue) return false;
            int before = player.Gold;
            try { AgentControlHelper.TransferGold((Hero)null, player, amount, notify: false); }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] street help reward deferred: {ex.Message}"); }
            int credited = Math.Max(0, player.Gold - before);
            if (credited != amount) return false;
            StoryProbeState.Data.CoinsFound += credited;
            StoryProbeState.RecordRewardReceipt(giverId, receipt);
            return true;
        }

        /// <summary>交差：给奖励 + 关系 + 进度（§4.1）。</summary>
        private static bool CollectDebt(int amount)
        {
            var d = StoryProbeState.Data;
            var player = Hero.MainHero;
            var debtor = StoryProbeMissionBehavior.Current?.GetHero("debtor");
            if (player == null || debtor == null || amount <= 0) return false;
            int remaining = Math.Max(0, amount - d.DebtCollected);
            if (remaining == 0) return true;
            if ((long)player.Gold + remaining > int.MaxValue) return false;
            int before = player.Gold;
            try { AgentControlHelper.TransferGold(debtor, player, remaining, notify: false); }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] debt collection failed: {ex.Message}"); }
            finally
            {
                int moved = Math.Min(remaining, Math.Max(0, player.Gold - before));
                d.DebtCollected += moved;
                d.CoinsFound += moved;
                DebugLogger.Log($"[StoryProbe] debt actually collected={moved} total={d.DebtCollected}/{amount}");
            }
            return d.DebtCollected >= amount;
        }

        private static bool GrantRewardItem(string npcId, string receipt, string itemId)
        {
            if (StoryProbeState.HasRewardReceipt(npcId, receipt)) return true;
            var player = Hero.MainHero;
            var roster = player?.PartyBelongedTo?.ItemRoster;
            var item = StoryProbeDefs.FindItem(itemId);
            // TransferItems 的 null roster 分支不能当作实际到货；必须验证真实接收背包。
            if (roster == null || item == null) return false;
            int before = roster.GetItemNumber(item);
            try { StoryProbeDefs.GiveItem(player, itemId); }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] reward grant failed {npcId}/{itemId}: {ex.Message}"); }
            if (roster.GetItemNumber(item) <= before) return false;
            StoryProbeState.RecordRewardReceipt(npcId, receipt);
            AnnounceItemBonus(itemId, item);
            return true;
        }

        /// <summary>拿到带加成的道具时说清它有什么用（2026-11-19 用户裁定：任务线奖励要让玩家看得见数值意义）。</summary>
        private static void AnnounceItemBonus(string itemId, ItemObject item)
        {
            if (!StoryProbeDefs.TryGetItemBonus(itemId, out var ability, out int bonus)) return;
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound(
                    "LWN_story_item_bonus_grant", "{ITEM}: +{BONUS} {ATTRIBUTE} in conversation checks",
                    ("ITEM", item?.Name?.ToString() ?? itemId),
                    ("ATTRIBUTE", LWNTextHelper.ResolveText(StoryProbeDefs.AbilityDisplayKey(ability), ability.ToString())),
                    ("BONUS", bonus.ToString()))));
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] item bonus notice failed {itemId}: {ex.Message}"); }
        }

        private static void TurnIn(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            var def = StoryProbeDefs.GetNpc(npcId);
            if (def == null) return;

            if (!StoryProbeState.IsQuestDone(npcId))
            {
                Message("LWN_story_turnin_not_done", "That is not done yet.");
                return;
            }
            if (StoryProbeState.IsQuestTurnedIn(npcId))
            {
                Message("LWN_story_turnin_already", "We are square.");
                return;
            }

            var player = Hero.MainHero;
            var giver = StoryProbeMissionBehavior.Current?.GetHero(npcId);
            if (player?.PartyBelongedTo?.ItemRoster == null || giver == null) return;
            var nativeQuest = PrologueQuestLauncher.FindActive(npcId);
            if (nativeQuest == null && !StoryProbeState.HasRewardReceipt(npcId, "native-finished"))
                nativeQuest = PrologueQuestLauncher.Start(npcId);
            if (nativeQuest == null && !StoryProbeState.HasRewardReceipt(npcId, "native-finished")) return;
            var items = def.RewardItems ?? new string[0];
            // 所有资源先预检；缺一件时不先吃掉交差资格或收走账款。
            if (items.Any(id => StoryProbeDefs.FindItem(id) == null)
                || (!string.IsNullOrEmpty(def.AttrItemId) && StoryProbeDefs.FindItem(def.AttrItemId) == null))
            {
                Message("LWN_info_equipment_error", "Equipment unavailable.");
                return;
            }
            var d = StoryProbeState.Data;
            try
            {
                if (npcId == "pawnbroker")
                {
                    // 打倒 debtor 的替代路线也必须实际收账，不能把目标倒地当作凭空得到 120。
                    if (!CollectDebt(120)) { Message("LWN_story_turnin_not_done", "That is not done yet."); return; }
                    int owed = Math.Max(0, 120 - d.DebtRepaid);
                    if (player.Gold < owed) { Message("LWN_story_turnin_not_done", "That is not done yet."); return; }
                    int before = player.Gold;
                    try { AgentControlHelper.TransferGold(player, giver, owed, notify: false); }
                    finally { d.DebtRepaid += Math.Min(owed, Math.Max(0, before - player.Gold)); }
                    if (d.DebtRepaid < 120) return;
                    int fee = Math.Max(0, 30 - d.PawnbrokerRewardPaid);
                    before = player.Gold;
                    try { AgentControlHelper.TransferGold(giver, player, fee, notify: false); }
                    finally { d.PawnbrokerRewardPaid += Math.Min(fee, Math.Max(0, player.Gold - before)); }
                    if (d.PawnbrokerRewardPaid < 30) return;
                }
                for (int i = 0; i < items.Length; i++)
                {
                    string key = npcId == "widow" && items[i] == "fine_town_tunic"
                        ? "wardrobe" : "item:" + i + ":" + items[i];
                    if (!GrantRewardItem(npcId, key, items[i])) return;
                }
                if (!string.IsNullOrEmpty(def.AttrItemId)
                    && !GrantRewardItem(npcId, "attribute-item", def.AttrItemId)
                    && StoryProbeDefs.FindItem(def.AttrItemId) == null)
                    // 属性道具找不到（数据写错/被别的内容包屏蔽）不能把整个交差卡死：记日志继续。
                    DebugLogger.Log($"[StoryProbe] attribute item missing for {npcId}: {def.AttrItemId} (turn-in continues)");
                if (npcId == "widow" && !GiveNobleDisguise()) return;
                if (!StoryProbeState.HasRewardReceipt(npcId, "skill-xp"))
                {
                    var skill = StoryProbeDefs.ResolveSkill(def.AttrSkill);
                    if (skill != null)
                    {
                        player.AddSkillXp(skill, 40f);
                        StoryProbeState.RecordRewardReceipt(npcId, "skill-xp");
                        Message("LWN_story_skill_xp_reward", "You gained 40 skill experience.");
                    }
                }
                if (!StoryProbeState.HasRewardReceipt(npcId, "relation"))
                {
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(giver, player, 10);
                    StoryProbeState.RecordRewardReceipt(npcId, "relation");
                }
                if (!StoryProbeState.HasRewardReceipt(npcId, "faction"))
                {
                    StoryProbeState.AddFactionRelation(def.Faction, 10);
                    StoryProbeState.RecordRewardReceipt(npcId, "faction");
                }
                if (!StoryProbeState.HasRewardReceipt(npcId, "native-finished"))
                {
                    try { nativeQuest.MarkObjectivesDone(); nativeQuest.Finish(); }
                    catch
                    {
                        // 完成回调抛错但原生任务已结算时，仍记录事实，不能另开任务重复发奖。
                        if (!nativeQuest.IsFinalized) throw;
                    }
                    StoryProbeState.RecordRewardReceipt(npcId, "native-finished");
                }
                StoryProbeState.TurnInQuest(npcId); // 所有应付项目成功后才提交总完成位。
                // 2026-11-19 用户裁定：交差可以直接涨**原版属性**（永久，走引擎自己的 AddAttribute）。
                if (def.AttributeGrants != null && Hero.MainHero != null)
                    foreach (var grant in def.AttributeGrants)
                    {
                        if (grant.Value == 0 || !StoryProbeDefs.TryParseCharacterAttribute(grant.Key, out var attribute)) continue;
                        try
                        {
                            Hero.MainHero.HeroDeveloper?.AddAttribute(attribute, grant.Value, checkUnspentPoints: false);
                            DebugLogger.Log($"[StoryProbe] attribute grant {npcId}: {grant.Key} {grant.Value:+#;-#;0} -> {Hero.MainHero.GetAttributeValue(attribute)}");
                            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveCompound(
                                "LWN_story_attribute_grant", "Training: {ATTRIBUTE} +{GAIN}",
                                ("ATTRIBUTE", LWNTextHelper.ResolveText(StoryProbeDefs.AttributeDisplayKey(grant.Key), grant.Key)),
                                ("GAIN", grant.Value.ToString()))));
                        }
                        catch (Exception ex) { DebugLogger.Log($"[StoryProbe] attribute grant failed {npcId}/{grant.Key}: {ex.Message}"); }
                    }
                StoryProbeState.SetGoal("tool");
                Message("LWN_story_narr_turnin", "He pays up, and he remembers your face.");
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] turn_in deferred with receipts retained {npcId}: {ex.Message}"); }
        }

        /// <summary>贵族戏服 + 剧情通行资格；玩家仍须穿上衣服通过门卫检定。</summary>
        private static bool GiveNobleDisguise()
        {
            // 与 RewardItems 共用一张到货回执，不能在剧团交差时再发第二件衣服。
            if (!GrantRewardItem("widow", "wardrobe", "fine_town_tunic")) return false;
            bool first = !StoryProbeState.Data.NobleDisguise;
            StoryProbeState.Data.NobleDisguise = true;
            if (first) Message("LWN_story_got_disguise", "You have the fine costume. Wear it before approaching the gate.");
            return true;
        }

        private static bool GiveRewards(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return false;
            var ids = csv.Split('|').Select(id => id.Trim()).Where(id => id.Length > 0).ToArray();
            if (Hero.MainHero?.PartyBelongedTo?.ItemRoster == null || ids.Any(id => StoryProbeDefs.FindItem(id) == null)) return false;
            for (int i = 0; i < ids.Length; i++)
                if (!GrantRewardItem(CurrentNpcId ?? "scene", "give-items:" + csv + ":" + i, ids[i])) return false;
            return true;
        }

        /// <summary>非致命决斗：玩家 vs 目标（CombatManager 的 Peace 模式）。</summary>
        private static void StartFight(string npcId)
        {
            var host = StoryProbeMissionBehavior.Current;
            var target = host?.GetAgent(npcId);
            if (target == null || Agent.Main == null)
            {
                Message("LWN_story_fight_no_target", "There is no one to fight.");
                return;
            }
            try
            {
                if (host.RequestStoryFight(npcId))
                    DebugLogger.Log($"[StoryProbe] Brain duel queued after conversation vs {npcId}");
                else Message("LWN_story_fight_no_target", "There is no one to fight.");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] StartFight({npcId}) failed: {ex.Message}");
            }
        }

        /// <summary>P0-D.5 迁移桥：新原子按参数调用既有通用管线（内容后续再拆成更小原子）。</summary>
        internal static bool RunLegacyVerb(string verb, string param)
        {
            if (string.IsNullOrEmpty(verb)) return false;
            try { return Dispatch(NormalizeStoryVerb(verb), param); }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] RunLegacyVerb({verb}) failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>P0-D.4：数据效果 `start_fight` 的入口（首遇走真实败退请求，其余走共享决斗管线）。</summary>
        internal static bool StartStoryFight(string npcId, string mode = null, string reason = null,
            string beneficiaries = null, string startLine = null, string endLine = null, string resolveEvent = null)
        {
            if (string.IsNullOrEmpty(npcId)) return false;
            var host = StoryProbeMissionBehavior.Current;
            if (host == null) return false;
            // 首遇守门人：唯一合法的「硬闯→真实战斗」入口是 PlayForcedDefeat（战斗回执决定真假败退）。
            if (StoryProbeState.Phase == StoryProbePhase.FirstAttempt
                && StoryProbeDefs.Npcs.Any(n => n.Id == npcId && n.FirstMeetGuard))
            {
                return host.PlayForcedDefeat();
            }
            if (!host.RequestStoryFight(npcId, mode, reason, beneficiaries, startLine, endLine, resolveEvent)) return false;
            DebugLogger.Log($"[StoryProbe] real fight queued after conversation vs {npcId}; mode={mode ?? "wound"}");
            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        // 验证 / 调试
        // ═══════════════════════════════════════════════════════════════

        /// <summary>只读校验唯一维护源：正式定义、入口/节点/选项引用与跨文件 ID。</summary>
        public static string ValidateAll()
        {
            var problems = new List<string>();
            int totalNodes = 0, totalOptions = 0, definitions = 0;
            string dir = Path.Combine(StoryProbeDataFiles.DesignDataFolder ?? "", "Dialogues");
            if (!Directory.Exists(dir)) return "Error: dialogue folder missing: " + dir;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(dir, "*.dialog.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                try
                {
                    var parsed = StoryDialogueParser.Parse(File.ReadAllText(path), path, formalMode: true);
                    if (!parsed.Ok) { problems.Add(parsed.Describe()); continue; }
                    var def = parsed.Data;
                    definitions++;
                    if (!ids.Add(def.DialogueId)) problems.Add("duplicate DialogueId: " + def.DialogueId);
                    totalNodes += def.Nodes.Count;
                    totalOptions += def.Nodes.Sum(n => n.Options.Count);
                }
                catch (Exception ex) { problems.Add(Path.GetFileName(path) + ": " + ex.Message); }
            }
            if (definitions == 0) problems.Add("no valid *.dialog.json definitions");
            string summary = $"definitions={definitions} nodes={totalNodes} options={totalOptions} problems={problems.Count}";
            return problems.Count == 0 ? summary : summary + " ; " + string.Join(" ; ", problems.Take(20));
        }

    }
}
