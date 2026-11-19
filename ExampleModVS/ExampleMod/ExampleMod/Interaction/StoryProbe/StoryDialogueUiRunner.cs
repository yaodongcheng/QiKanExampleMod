using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 511 剧情对话的**自建 UI 运行器**（2026-11-19）。
    ///
    /// 为什么要有它
    ///   原版对话流（`MissionConversationLogic.StartConversation`）在开始时会对参与者调
    ///   `CampaignMission.OnConversationStart` / `ForceAiBehaviorSelection`，结束时再把身体还回去；
    ///   我们召唤出来的 NPC 被 AgentBrain 托管，这条「还身体」的链一旦漏一步，实机就是
    ///   「谈过一次话就钉在原地，再也不走」。反复补 EndConversation 后处理仍会复发。
    ///   本运行器**完全不启动引擎对话流**：同一份 dialog.json、同一套后果/检定/跳转，
    ///   但对话生命周期、站位、朝向、收尾全部由我们自己持有 —— 从根上不产生那条副作用链。
    ///
    /// 纪律
    ///   · **对话逻辑只有一份**：可见性/可选性/选中执行/检定/跳转全部调
    ///     `DialogueInjector.IsTransitionVisible / IsTransitionSelectable / ExecutePlayerChoice`，
    ///     禁止在这里复制一套自己的对话规则。
    ///   · **节点效果只在真实展示时提交**：进入节点必须调节点自己的 `PrepareTurn` / `OnShown`
    ///     （它们才是 `NotifyNodeShown` → on_enter 效果、台词动作、表情、朝向、机位的唯一提交点）。
    ///   · **收尾必须幂等且不留钉子**：`End` 一定走 `StoryProbeDialogue.OnConversationEnded`
    ///     （结束会话效果 + 释放朝向/机位/钩子），并清掉本运行器自己钉住的玩家站位。
    ///   · 开关 = `Settings.StoryDialogueUi`（`vanilla` / `custom`），只作用于 511 剧情对话；
    ///     犯罪对话、原版对话、IM 一律不受影响。
    /// </summary>
    public static class StoryDialogueUiRunner
    {
        private static bool _running;
        private static DialogueInjector.DialogueInjectScript _script;
        private static HashSet<string> _nodeIds;
        private static DialogueInjector.DialogueNode _node;
        private static StoryDialogueSession _session;
        private static Agent _entryAgent;
        private static string _npcId;

        private static readonly List<DialogueInjector.DialogueTransition> _options
            = new List<DialogueInjector.DialogueTransition>();

        /// <summary>本次对话的实际参与者（入口演员 + 当前说话的双方）。
        /// 供 AgentBrain / 工作中枢判断"这个身体正在被我方对话占用"——
        /// 引擎会话没启动，它们原来那条 `IsConversationAgent` 判据在自建面板下恒为 false。</summary>
        private static readonly HashSet<Agent> _participants = new HashSet<Agent>();

        /// <summary>本节点没有可选选项、但有 NextNode：NPC 要连说下一句，等玩家点「继续」。</summary>
        private static bool _continuePending;
        /// <summary>本节点没有选项也没有下一句：玩家读完最后一句后关窗。</summary>
        private static bool _endPending;
        /// <summary>玩家身体被本运行器钉住（对话期间不许走开）。</summary>
        private static bool _playerPinned;
        private static AgentControlHelper.FacingTracker _playerTracker;
        private static string _playerTrackerOwner;

        public static bool IsRunning => _running;
        public static StoryDialogueSession Session => _session;
        public static DialogueInjector.DialogueNode Node => _node;
        public static string NpcId => _npcId;
        public static IReadOnlyList<DialogueInjector.DialogueTransition> Options => _options;

        /// <summary>本节点是否在等「继续 / 离开」（没有可选选项时才有意义）。</summary>
        public static bool IsContinuePending => _continuePending || _endPending;

        /// <summary>这个身体是否正被当前这段自建对话占用（含说话人、听话人、入口演员）。</summary>
        public static bool IsParticipant(Agent agent)
            => _running && agent != null && _participants.Contains(agent);

        /// <summary>自定义 UI 是否接管这个会话（供 StoryProbeDialogue 判断"这段对话归谁"）。</summary>
        public static bool Owns(StoryDialogueSession session)
            => _running && session != null && ReferenceEquals(session, _session) && !session.Ended && !session.Failed;

        // ═══════════════════════════════════════════════════════════════
        // 生命周期
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 接管一段已经裁决好的剧情会话。返回 false = 没接（调用方应回退原版流程）。
        /// `script` 必须来自 `StoryProbeDialogue.BuildFilteredFromDefinition`（与原版注入的是同一份图）。
        /// </summary>
        public static bool Start(DialogueInjector.DialogueInjectScript script, Agent entryAgent,
            string npcId, StoryDialogueSession session)
        {
            // 单窗口：已经有一段自建对话在跑时不再接管（调用方回退原版路径，由入口层拒绝重复开启）。
            if (_running) return false;
            if (script?.Nodes == null || script.Nodes.Count == 0) return false;
            if (session == null || session.Ended || session.Failed) return false;

            _script = script;
            _session = session;
            _entryAgent = entryAgent;
            _npcId = npcId;
            _node = null;
            _nodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in script.Nodes)
                if (n?.Id != null) _nodeIds.Add(n.Id);

            _running = true;
            _playerPinned = false;
            _playerTracker = null;
            _playerTrackerOwner = null;

            // 🔴 面板打不开就不接管：调用方会回退原版对话流，玩家至少还能正常说话。
            if (!StoryDialogueUiView.Open())
            {
                DebugLogger.Log("[StoryDialogueUI] panel unavailable; falling back to vanilla flow");
                ClearFields();
                return false;
            }
            if (!EnterNode(script.EntryNode))
            {
                End("entry node missing");
                return false;
            }
            DebugLogger.Log($"[StoryDialogueUI] opened npc={npcId ?? "-"} dialogue={session.Definition?.DialogueId ?? "-"} entry={script.EntryNode} nodes={script.Nodes.Count}");
            return true;
        }

        /// <summary>玩家选中第 index 个可见选项（必须落在当前可见列表内）。</summary>
        public static bool Select(int index)
        {
            if (!_running || _node == null) return false;
            if (index < 0 || index >= _options.Count) return false;
            var transition = _options[index];
            if (!DialogueInjector.IsTransitionSelectable(transition))
            {
                DebugLogger.Log($"[StoryDialogueUI] option {index} not selectable; ignored");
                return false;
            }

            DebugLogger.Log($"[StoryDialogueUI] select node={_node.Id} option={index} text=\"{Safe(DialogueInjector.GetTransitionText(transition))}\"");

            // 唯一的执行链：提交选项后果 → 检定（同一次掷骰）→ 目标节点裁决。
            string dest;
            bool checkFailed;
            try
            {
                dest = DialogueInjector.ExecutePlayerChoice(_node, transition, _nodeIds, out checkFailed);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryDialogueUI] choice failed: {ex}");
                End("choice exception");
                return false;
            }

            // 一次选择的完整结果：从哪句、选第几条、掷没掷、去了哪（或关窗）。
            DebugLogger.Log("[StoryDialogueUI] choice from=" + (_node.Id ?? "-")
                + " option=" + (index + 1) + " dest=" + (string.IsNullOrEmpty(dest) ? "(close)" : dest)
                + " checkFailed=" + checkFailed);

            _options.Clear();
            if (string.IsNullOrEmpty(dest))
            {
                End(checkFailed ? "check failed - closed" : "closed by dialogue");
                return true;
            }
            EnterNode(dest);
            return true;
        }

        /// <summary>玩家点「继续 / 离开」（只在没有可选选项时出现）。</summary>
        public static bool Continue()
        {
            if (!_running || _node == null) return false;
            if (_options.Count > 0) return false;
            string next = _continuePending ? _node.NextNode : null;
            _continuePending = false;
            _endPending = false;
            if (string.IsNullOrEmpty(next)) { End("player closed"); return true; }
            EnterNode(next);
            return true;
        }

        /// <summary>每帧：钉住玩家、检测会话失效。朝向/机位由 StoryProbeDialogue 的既有管线负责。</summary>
        public static void Tick(float dt)
        {
            if (!_running) return;

            // 会话被外部结束（剧情取消、Mission 卸载、失败）→ 立刻收尾，不留钉子。
            var session = _session;
            if (session == null || session.Ended || session.Failed
                || StoryDialogueRuntime.Current != session
                || Mission.Current == null)
            {
                End("session no longer active");
                return;
            }
            if (_entryAgent != null && !AgentControlHelper.SafeIsActive(_entryAgent))
            {
                End("entry actor left the scene");
                return;
            }
            if (Agent.Main == null || !AgentControlHelper.SafeIsActive(Agent.Main))
            {
                End("player unavailable (left/downed)");
                return;
            }

            PinPlayer(dt);
        }

        /// <summary>
        /// 结束对话并**把身体全部还回去**。幂等：重复调用不会重复提交会话结束效果。
        /// </summary>
        public static void End(string reason)
        {
            if (!_running)
            {
                StoryDialogueUiView.Close();
                return;
            }
            _running = false;
            DebugLogger.Log($"[StoryDialogueUI] end reason={reason ?? "-"} node={_node?.Id ?? "-"}");

            // 1) 先把我们钉住的玩家放开（放在最前：后面任何一步抛异常都不能把玩家留着）。
            ReleasePlayerPin();

            // 2) 走既有收尾入口：结束会话效果 + 释放朝向/机位 + 清空对话钩子。
            try { StoryProbeDialogue.OnConversationEnded(); }
            catch (Exception ex) { DebugLogger.Log($"[StoryDialogueUI] OnConversationEnded failed: {ex.Message}"); }

            // 3) 兜底：即使上面的收尾链断在中间，也把对话参与者从"钉住"状态解出来。
            try { ReleaseActorsTargetFrame(); }
            catch (Exception ex) { DebugLogger.Log($"[StoryDialogueUI] release actors failed: {ex.Message}"); }

            // 4) 入口 Prefix 在接管时也占了"质问独占"锁（它原本由 EndConversation 释放）。
            //    引擎对话流没启动 ⇒ 那条释放链不会跑，这里必须自己还回去，否则别的 NPC 警戒永久冻结。
            try
            {
                if (AgentBrain.ConfrontingBrain != null)
                {
                    DebugLogger.Log($"[StoryDialogueUI] release confrontation lock by {AgentBrain.ConfrontingBrain.Owner?.Name ?? "null"}");
                    AgentBrain.ConfrontingBrain = null;
                }
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryDialogueUI] release confrontation lock failed: {ex.Message}"); }

            ClearFields();
            StoryDialogueUiView.Close();
        }

        private static void ClearFields()
        {
            _running = false;
            _node = null;
            _script = null;
            _nodeIds = null;
            _options.Clear();
            _participants.Clear();
            _continuePending = false;
            _endPending = false;
            _session = null;
            _entryAgent = null;
            _npcId = null;
        }

        public static string Describe()
        {
            if (!_running) return "custom-ui=idle";
            return "custom-ui=active session=" + (_session?.Id.ToString() ?? "-")
                + " dialogue=" + (_session?.Definition?.DialogueId ?? "-")
                + " npc=" + (_npcId ?? "-")
                + " node=" + (_node?.Id ?? "-")
                + " options=" + _options.Count
                + (_continuePending || _endPending ? " continue=pending" : "");
        }

        // ═══════════════════════════════════════════════════════════════
        // 节点推进
        // ═══════════════════════════════════════════════════════════════

        private static bool EnterNode(string nodeId)
        {
            // 选项执行可能已经让会话失败/收场（付款失败、参与者离场、运行期令牌变化）——
            // 这种情况绝不能再把下一句推上屏，直接收尾。
            var live = _session;
            if (live == null || live.Ended || live.Failed || StoryDialogueRuntime.Current != live)
            {
                End("session no longer valid before entering " + (nodeId ?? "-"));
                return false;
            }
            if (string.IsNullOrEmpty(nodeId)) { End("empty node"); return false; }
            var node = _script?.Nodes?.FirstOrDefault(n => n != null
                && string.Equals(n.Id, nodeId, StringComparison.Ordinal));
            if (node == null)
            {
                DebugLogger.Log($"[StoryDialogueUI] node '{nodeId}' not found in script");
                End("node missing: " + nodeId);
                return false;
            }

            _node = node;

            // 人物到位 / 身体让渡（与原版同一实现；新 UI 模式下不再往引擎会话里加人）。
            // 准备失败 = 参与者不在/被别的系统持有 → 这一句根本不该上屏，直接收场。
            bool prepared = true;
            try { prepared = node.PrepareTurn?.Invoke() ?? true; }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryDialogueUI] PrepareTurn('{nodeId}') failed: {ex.Message}");
                prepared = false;
            }
            if (!prepared)
            {
                End("turn preparation failed: " + nodeId);
                return false;
            }

            RefreshParticipants(node);

            // 🔴 节点效果/台词动作/表情/朝向/机位的唯一提交点 = 这一句真的上屏。
            try { node.OnShown?.Invoke(); }
            catch (Exception ex) { DebugLogger.Log($"[StoryDialogueUI] OnShown('{nodeId}') failed: {ex.Message}"); }

            RefreshOptions();
            StoryDialogueUiView.Refresh();

            // 每说一句一条日志（与原版一样的可追溯粒度）：节点 / 说话人 / 听话人 / 台词原文。
            DebugLogger.Log("[StoryDialogueUI] say node=" + (node.Id ?? "-")
                + " speaker=" + (node.Speaker ?? "-") + " listener=" + (node.Listener ?? "-")
                + " line=\"" + Safe(DialogueInjector.GetNodeLine(node) ?? "") + "\"");
            return true;
        }

        /// <summary>参与者集合 = 入口演员 + 本句说话人 + 本句听话人（外加选项的听话人）。</summary>
        private static void RefreshParticipants(DialogueInjector.DialogueNode node)
        {
            _participants.Clear();
            if (_entryAgent != null && _entryAgent != Agent.Main) _participants.Add(_entryAgent);
            if (node == null) return;
            var speaker = ResolveAgent(_session, node.Speaker);
            var listener = ResolveAgent(_session, node.Listener);
            if (speaker != null && speaker != Agent.Main) _participants.Add(speaker);
            if (listener != null && listener != Agent.Main) _participants.Add(listener);
        }

        private static void RefreshOptions()
        {
            _options.Clear();
            _continuePending = false;
            _endPending = false;
            if (_node == null) return;

            var hidden = new List<string>();
            if (_node.Transitions != null)
            {
                foreach (var t in _node.Transitions)
                {
                    if (t == null) continue;
                    bool visible;
                    try { visible = DialogueInjector.IsTransitionVisible(t); }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[StoryDialogueUI] visibility failed on '{_node.Id}': {ex.Message}");
                        visible = false;
                    }
                    if (visible) _options.Add(t);
                    else hidden.Add(Safe(DialogueInjector.GetTransitionText(t)));
                }
            }

            if (_options.Count == 0)
            {
                // 原版语义：选项为空 → NPC 连说的下一句（NextNode）或收场。
                if (!string.IsNullOrEmpty(_node.NextNode)) _continuePending = true;
                else _endPending = true;
            }

            // 每次刷新把「哪些选项出现、哪些置灰、哪些被条件挡掉」一次打全：
            // 这三样正是设计里写过的东西，日志必须能逐条核对，而不是只能看屏幕猜。
            var shown = new List<string>();
            for (int i = 0; i < _options.Count; i++)
            {
                bool selectable = true;
                try { selectable = DialogueInjector.IsTransitionSelectable(_options[i]); } catch { }
                shown.Add((i + 1) + (selectable ? ":ok" : ":grey") + "=" + Safe(DialogueInjector.GetTransitionText(_options[i])));
            }
            DebugLogger.Log("[StoryDialogueUI] options node=" + (_node.Id ?? "-")
                + " shown=[" + string.Join(" | ", shown) + "]"
                + (hidden.Count > 0 ? " hidden=[" + string.Join(" | ", hidden) + "]" : " hidden=[]")
                + (_continuePending ? " continue=pending" : _endPending ? " continue=leave" : ""));
        }

        // ═══════════════════════════════════════════════════════════════
        // 玩家站位（对话期间不许走开；结束一定放开）
        // ═══════════════════════════════════════════════════════════════

        private static void PinPlayer(float dt)
        {
            var player = Agent.Main;
            if (player == null) return;
            var focus = CurrentFocusAgent();
            if (focus == null || focus == player) return;

            if (!_playerPinned)
            {
                _playerPinned = true;
                _playerTracker = new AgentControlHelper.FacingTracker();
                _playerTrackerOwner = "story_ui:" + (_session?.Id.ToString() ?? "-");
            }
            try
            {
                _playerTracker?.Tick(player, focus, true, false, Math.Max(0f, dt), _playerTrackerOwner);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryDialogueUI] player pin failed: {ex.Message}");
            }
        }

        private static Agent CurrentFocusAgent()
        {
            var session = _session;
            var node = _node;
            if (session?.Definition == null || node == null) return _entryAgent;
            // 说话人不是玩家 → 面向说话人；说话人是玩家 → 面向听话人。
            string identity = string.IsNullOrEmpty(node.Speaker) ? session.Request?.ActorId : node.Speaker;
            var speaker = ResolveAgent(session, identity);
            if (speaker != null && speaker != Agent.Main) return speaker;
            var listener = ResolveAgent(session, node.Listener);
            return listener != null && listener != Agent.Main ? listener : _entryAgent;
        }

        private static Agent ResolveAgent(StoryDialogueSession session, string identity)
        {
            if (session == null || string.IsNullOrEmpty(identity)) return null;
            if (identity == "player") return Agent.Main;
            var host = StoryProbeMissionBehavior.Current;
            if (host == null) return null;
            string actor = identity;
            var def = session.Definition;
            if ((identity == "npc" || identity == "questioner")
                && def != null && def.Participants != null && def.Participants.Contains(identity))
                actor = session.Request?.ActorId;
            else if (def != null && def.Actors != null && !def.Actors.Contains(identity))
                return null;
            return host.GetAgent(actor);
        }

        private static void ReleasePlayerPin()
        {
            if (!_playerPinned) return;
            _playerPinned = false;
            _playerTracker = null;
            _playerTrackerOwner = null;
            try
            {
                var player = Agent.Main;
                if (player != null && AgentControlHelper.SafeIsActive(player))
                {
                    player.ClearTargetFrame();
                    AgentControlHelper.StopLooking(player);
                }
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryDialogueUI] release player pin failed: {ex.Message}"); }
        }

        /// <summary>
        /// 兜底解钉：把本次会话里被我们摆过站位的 NPC 的 scripted frame 清掉。
        /// 只在 `ReleaseDialogueFacing` 之后跑 —— 正常路径那边会清，这里防"它早退没清"。
        /// </summary>
        private static void ReleaseActorsTargetFrame()
        {
            var session = _session;
            var def = session?.Definition;
            if (def == null) return;
            var seen = new HashSet<Agent>();
            foreach (var identity in AllIdentities(def))
            {
                var agent = ResolveAgent(session, identity);
                if (agent == null || agent == Agent.Main || !seen.Add(agent)) continue;
                if (!AgentControlHelper.SafeIsActive(agent)) continue;
                // 🔴 不碰正在被别的系统持有的身体（绑缚/抬走/击晕/战斗）。
                if (AgentBrain.IsKnockedOut(agent)) continue;
                var brain = AgentAIController.GetBrainForAgent(agent);
                if (brain != null && (brain.IsBound || brain.IsStatusHeld || brain.IsStunned || brain.IsInCombat)) continue;
                agent.ClearTargetFrame();
                AgentControlHelper.StopLooking(agent);
            }
        }

        private static IEnumerable<string> AllIdentities(StoryDialogueDef def)
        {
            if (def?.Actors != null)
                foreach (var a in def.Actors)
                    if (!string.IsNullOrEmpty(a)) yield return a;
        }

        private static string Safe(string s) => s == null ? "" : s.Replace('\n', ' ').Trim();
    }
}