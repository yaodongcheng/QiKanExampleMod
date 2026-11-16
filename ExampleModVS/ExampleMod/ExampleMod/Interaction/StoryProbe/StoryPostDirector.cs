using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// P3/P4（2026-11-15）：发布正式 `story_stage/2` 数据并**真正消费**岗位规则与流程。
    ///
    /// 职责边界：
    ///   · 数据决定：哪些岗位算候选、感知范围、门区触发、冷却/重入、排序键、流程步骤。
    ///   · 本文件只做：按数据筛选候选 → 稳定排序 → 申请 Brain 拦截 → 按流程开窗/等待/收尾。
    ///   · 没有正式数据时**不假装成功**：`EnsurePublished` 失败返回 false，调用方走旧单守卫路径。
    ///
    /// 🔴 纪律：
    ///   · 末位排序键必须是稳定身份（npcId），与解析器 `unstable_tiebreak` 一致。
    ///   · 资格同时核真实身体（死亡/战斗/绑缚/击晕/Brain 在忙）与视线；不看字典顺序。
    ///   · 同一次遭遇 `MaxPerEncounter` 到了就停；失败按 `RetrySec` 退避，不每帧重试。
    /// </summary>
    public sealed class StoryPostDirector
    {
        private readonly StoryProbeMissionBehavior _host;
        private readonly Dictionary<string, float> _lastAttempt = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _opened = new Dictionary<string, int>(StringComparer.Ordinal);
        private StoryFlowRunner _flow;
        private string _flowBehaviorId;
        private string _flowActorId;
        private string _lastReport = "not evaluated";
        private bool _sceneMismatchLogged;

        public StoryPostDirector(StoryProbeMissionBehavior host) { _host = host; }

        public bool HasRules(string stageId)
        {
            if (!StoryV2Data.EnsurePublished()) return false;
            var file = StoryLayoutRegistry.Published;
            if (file == null) return false;
            string scene = Mission.Current?.SceneName;
            if (!string.IsNullOrEmpty(file.Scene) && !string.IsNullOrEmpty(scene)
                && !string.Equals(file.Scene, scene, StringComparison.OrdinalIgnoreCase))
            {
                if (!_sceneMismatchLogged)
                {
                    _sceneMismatchLogged = true;
                    DebugLogger.Log("[StoryV2] data scene='" + file.Scene + "' but mission scene='" + scene
                                    + "'; v2 rules disabled here (legacy fallback).");
                }
                return false;
            }
            return file.Behaviors.Values.Any(b =>
                b != null && b.Kind == "post" && b.Stages.Contains(stageId));
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(StoryV2Data.Report);
            if (StoryLayoutRegistry.Published != null)
            {
                sb.Append("\n").Append(StoryLayoutRegistry.Describe());
                foreach (var b in StoryLayoutRegistry.Published.Behaviors.Values.OrderBy(b => b.Id, StringComparer.Ordinal))
                {
                    sb.Append("\n  behavior ").Append(b.Id)
                      .Append(" stages=").Append(string.Join(",", b.Stages))
                      .Append(" posts=").Append(string.Join(",", b.Posts))
                      .Append(" rank=").Append(string.Join(">", b.Rank))
                      .Append(" range=").Append(b.Perception.RangeMeters.ToString("F1"));
                    if (_opened.TryGetValue(b.Id, out int count)) sb.Append(" opened=").Append(count);
                }
            }
            sb.Append("\n  last: ").Append(_lastReport);
            if (_flow != null && !_flow.Done) sb.Append("\n  flow: ").Append(_flow.Describe());
            return sb.ToString();
        }

        /// <summary>
        /// 按数据选本次质问者。`report` 列出每个候选的资格与排序，便于 `intercept status` 直接看。
        /// </summary>
        public bool TrySelectQuestioner(string stageId, out string actorId, out string behaviorId, out string report)
        {
            actorId = null;
            behaviorId = null;
            report = "";
            if (!StoryV2Data.EnsurePublished())
            {
                _lastReport = "no published v2 data";
                report = _lastReport;
                return false;
            }

            var file = StoryLayoutRegistry.Published;
            var player = Agent.Main;
            if (player == null) { _lastReport = "no player agent"; report = _lastReport; return false; }

            var lines = new List<string>();
            StoryBehaviorDef bestBehavior = null;
            string bestActor = null;
            float bestDistance = float.MaxValue;

            foreach (var behavior in file.Behaviors.Values.Where(b => b != null && b.Kind == "post"
                         && b.Stages.Contains(stageId)).OrderBy(b => b.Id, StringComparer.Ordinal))
            {
                if (!file.Stages.TryGetValue(stageId, out var stage) || stage == null) continue;
                if (!file.Layouts.TryGetValue(stage.Layout, out var layout) || layout == null) continue;
                if (behavior.Trigger.PlayerInsideGate && !_host.IsPlayerInGateApproach())
                { lines.Add(behavior.Id + ": player outside gate passage"); continue; }

                foreach (string id in stage.Actors.Concat(stage.OptionalActors).Distinct())
                {
                    var def = StoryProbeDefs.GetNpc(id);
                    if (def == null) { lines.Add(id + ": no cast def"); continue; }

                    // 只认登记在岗位上的演员（有 Standing 就按数据筛；没写就退回首遇守卫名单）。
                    var standing = layout.Standing.FirstOrDefault(s => s != null && s.Actor == id);
                    bool onPost = standing == null
                        ? def.FirstMeetGuard
                        : (behavior.Posts.Count == 0 || behavior.Posts.Contains(standing.Post ?? ""));
                    if (!onPost) { lines.Add(id + ": not on a post of " + behavior.Id); continue; }

                    var agent = _host.GetAgent(id);
                    if (agent == null) { lines.Add(id + ": no agent"); continue; }
                    if (!AgentControlHelper.SafeIsActive(agent) || agent.Health <= 0f)
                    { lines.Add(id + ": inactive/dead"); continue; }
                    if (AgentBrain.IsRoped(agent) || AgentBrain.IsHeldByStatus(agent) || AgentBrain.IsKnockedOut(agent))
                    { lines.Add(id + ": bound/held/downed"); continue; }

                    var brain = AgentAIController.GetBrainForAgent(agent);
                    if (brain == null) { lines.Add(id + ": no brain"); continue; }
                    if (brain.IsPerforming || brain.IsInCombat)
                    { lines.Add(id + ": brain busy"); continue; }

                    // 岗位职责区（2026-11-16）：进了"我这名守卫"的感知半径就算轮到我，
                    // 不再要求玩家正好踩在门轴中线 ±3 米那条窄带里（旧口径见 Trigger.PlayerInsideGate）。
                    if (behavior.Trigger.PlayerInPostZone && !_host.IsPlayerInPostZone(agent, behavior.Perception.RangeMeters))
                    { lines.Add(id + ": outside my post zone"); continue; }

                    // 在区里的人必须真的朝玩家看：站着不动的 AI 会一直保持旧朝向，
                    // 光有距离也会被判"看不见"（LookDirection 是视线判定的输入）。
                    if (behavior.Trigger.PlayerInPostZone)
                        AgentControlHelper.LookAtAgent(agent, player);

                    float distance = agent.Position.Distance(player.Position);
                    if (distance > behavior.Perception.RangeMeters)
                    { lines.Add(id + ": out of range " + distance.ToString("F1")); continue; }
                    if (behavior.Perception.RequiresSight
                        && !NpcSightSystem.CanAgentSeeTarget(agent, player, behavior.Perception.RangeMeters))
                    { lines.Add(id + ": no sight (" + NpcSightSystem.ExplainSight(agent, player, behavior.Perception.RangeMeters) + ")"); continue; }

                    string key = behavior.Id + "|" + id;
                    if (_opened.TryGetValue(behavior.Id, out int opened) && opened >= behavior.Trigger.MaxPerEncounter)
                    { lines.Add(id + ": encounter limit " + opened + "/" + behavior.Trigger.MaxPerEncounter); continue; }
                    if (behavior.Cooldown.RetrySec > 0f && _lastAttempt.TryGetValue(key, out float last)
                        && _host.Clock - last < behavior.Cooldown.RetrySec)
                    {
                        lines.Add(id + ": retry cooldown " + (behavior.Cooldown.RetrySec - (_host.Clock - last)).ToString("F1"));
                        continue;
                    }

                    lines.Add(id + ": eligible dist=" + distance.ToString("F1"));
                    // 排序：distanceToPlayer 升序，末位 npcId 升序（稳定身份）。
                    bool better = bestActor == null
                        || distance < bestDistance - 0.01f
                        || (Math.Abs(distance - bestDistance) <= 0.01f
                            && string.CompareOrdinal(id, bestActor) < 0);
                    if (better)
                    {
                        bestActor = id;
                        bestDistance = distance;
                        bestBehavior = behavior;
                    }
                }
            }

            report = "stage=" + stageId + "\n  " + string.Join("\n  ", lines);
            _lastReport = report;
            if (bestActor == null) return false;
            actorId = bestActor;
            behaviorId = bestBehavior?.Id;
            return true;
        }

        /// <summary>
        /// P3 准备：按 v2 布局给演员解出站位（岗位优先，其次工作点/显式点）。
        /// 当前兼容实现：player 用玩家基准，其他 Anchor 均用宿主 BlockerPos。
        /// 注册锚点与 Facing 尚未在首遇完整消费，不能把偏移计算当成真实门轴对齐。
        /// </summary>
        public bool TryGetStandingPosition(string stageId, string actorId, out TaleWorlds.Library.Vec3 position)
        {
            position = default;
            if (!StoryV2Data.EnsurePublished()) return false;
            var file = StoryLayoutRegistry.Published;
            if (file == null || !file.Stages.TryGetValue(stageId, out var stage) || stage == null) return false;
            if (!file.Layouts.TryGetValue(stage.Layout, out var layout) || layout == null) return false;
            var standing = layout.Standing.FirstOrDefault(s => s != null && s.Actor == actorId);
            if (standing == null) return false;

            StoryPoint point = null;
            if (!string.IsNullOrEmpty(standing.Post))
                point = layout.Posts.FirstOrDefault(p => p != null && p.Id == standing.Post)?.Place;
            else if (!string.IsNullOrEmpty(standing.WorkPoint))
                // 工作点没有独立坐标（由 Prop + Side/Offset 推导）；这里不猜，交调用方兜底。
                return false;
            else
                point = standing.Place;
            if (point == null) return false;

            var f = _host.Forward;
            var origin = string.Equals(point.Anchor, "player", StringComparison.OrdinalIgnoreCase)
                ? _host.PlayerAnchor : _host.BlockerPos;
            position = origin
                       + new TaleWorlds.Library.Vec3(f.x, f.y, 0f) * point.Fwd
                       + new TaleWorlds.Library.Vec3(f.y, -f.x, 0f) * point.Right
                       + new TaleWorlds.Library.Vec3(0f, 0f, point.Up);
            return true;
        }

        /// <summary>记录一次拦截结果（开窗成功计数 / 失败退避）。</summary>
        public void NoteAttempt(string behaviorId, string actorId, bool opened, string reason)
        {
            if (string.IsNullOrEmpty(behaviorId) || string.IsNullOrEmpty(actorId)) return;
            string key = behaviorId + "|" + actorId;
            _lastAttempt[key] = _host.Clock;
            if (opened)
            {
                _opened.TryGetValue(behaviorId, out int n);
                _opened[behaviorId] = n + 1;
            }
            _lastReport = (opened ? "opened " : "refused ") + key + " (" + (reason ?? "-") + ")";
            DebugLogger.Log("[StoryV2] intercept " + _lastReport);
        }

        /// <summary>按流程开会话（流程里的 dialogue_open 才是真正开窗点）。</summary>
        public bool BeginQuestionFlow(string behaviorId, string actorId, out string reason)
        {
            reason = null;
            var file = StoryLayoutRegistry.Published;
            if (file == null || !file.Behaviors.TryGetValue(behaviorId, out var behavior) || behavior == null)
            { reason = "unknown behavior " + behaviorId; return false; }
            if (string.IsNullOrEmpty(behavior.Flow) || !file.Flows.TryGetValue(behavior.Flow, out var flow) || flow == null)
            { reason = "unknown flow " + behavior.Flow; return false; }

            if (_flow != null && !_flow.Done)
            { reason = "flow already running"; return false; }

            _flow = new StoryFlowRunner(_host, behavior, flow, actorId, this);
            _flowBehaviorId = behaviorId;
            _flowActorId = actorId;
            try { _flow.Start(); }
            catch (Exception ex)
            {
                DebugLogger.Log("[StoryV2] flow start failed: " + ex.Message);
                _flow = null;
                reason = "flow start failed: " + ex.Message;
                return false;
            }
            reason = _flow.Done ? "finished immediately: " + _flow.Reason : "started";
            return true;
        }

        public void Tick(float dt)
        {
            if (_flow == null || _flow.Done) return;
            try { _flow.Tick(dt); }
            catch (Exception ex)
            {
                DebugLogger.Log("[StoryV2] flow tick failed: " + ex.Message);
                _flow = null;
                _lastReport = "flow failed: " + ex.Message;
                return;
            }
            if (_flow.Done)
            {
                DebugLogger.Log("[StoryV2] flow done (" + _flow.Reason + ")");
                _lastReport = "flow " + _flow.Reason;
                // retry / reselect：本次不成立 → 记失败退避，下一轮重新选人；release/end 不重试。
                if (_flow.TerminalToken == "retry" || _flow.TerminalToken == "reselect")
                    NoteAttempt(_flowBehaviorId, _flowActorId, false, _flow.Reason);
            }
        }

        internal void OnFlowTerminal(string actorId, string token)
        {
            DebugLogger.Log("[StoryV2] flow terminal '" + token + "' actor=" + actorId);
        }
    }

    /// <summary>
    /// 最小流程解释器（P4）：只执行已登记原子，等待必须是**真实结果**（对话真的开了/真的结束），
    /// 超时走数据给出的出口，不把超时映射成成功。
    /// </summary>
    public sealed class StoryFlowRunner
    {
        private readonly StoryProbeMissionBehavior _host;
        private readonly StoryBehaviorDef _behavior;
        private readonly StoryFlowDef _flow;
        private readonly string _actorId;
        private readonly StoryPostDirector _director;
        private int _index = -1;
        private float _elapsed;
        private string _waitKind;
        private bool _done;
        private string _reason = "not started";
        private string _terminal = "";
        private float _timerSeconds;
        private string _dialogueId;
        /// <summary>本次流程真正开出来的会话编号（0 = 还没开窗）；用来分辨“我们的窗口”与“别人顶掉了”。</summary>
        private int _sessionId;

        public StoryFlowRunner(StoryProbeMissionBehavior host, StoryBehaviorDef behavior,
            StoryFlowDef flow, string actorId, StoryPostDirector director)
        {
            _host = host; _behavior = behavior; _flow = flow; _actorId = actorId; _director = director;
        }

        public bool Done => _done;
        public string Reason => _reason;
        public string TerminalToken => _terminal;

        public string Describe()
        {
            return "flow=" + _flow.Id + " step=" + Math.Max(0, _index) + "/" + _flow.Steps.Count
                 + " wait=" + (_waitKind ?? "-") + " elapsed=" + _elapsed.ToString("F1")
                 + " done=" + _done + " reason=" + _reason;
        }

        public void Start()
        {
            if (_flow.Steps.Count == 0) { Finish("end", "empty flow"); return; }
            Advance();
        }

        public void Tick(float dt)
        {
            if (_done) return;
            _elapsed += dt;
            bool satisfied = false;
            switch (_waitKind)
            {
                case "dialogue_open":
                    satisfied = ConversationEntryPatch.IsConversationActive() && SessionMatches();
                    break;
                case "dialogue_end":
                {
                    bool active = ConversationEntryPatch.IsConversationActive();
                    if (!active) { satisfied = true; break; }
                    // 没经过 dialogue_open 的流程（等外部开的窗）：沿用旧判定。
                    if (_sessionId == 0) { satisfied = !SessionMatches(); break; }
                    var s = StoryDialogueRuntime.Current;
                    // 🔴 窗口还开着、但已经不是我们那一个：被别人顶掉 / 本次会话失败。
                    //    这不能算“正常说完了”，要走数据里的中断分支。
                    if (s == null || s.Id != _sessionId)
                    { Finish(TokenOr(CurrentStep?.OnInterrupt, "end"), "dialogue interrupted: session replaced"); return; }
                    if (s.Failed)
                    { Finish(TokenOr(CurrentStep?.OnInterrupt, "end"), "dialogue interrupted: session failed"); return; }
                    satisfied = false;
                    break;
                }
                case "timer":
                    satisfied = _elapsed >= _timerSeconds;
                    break;
                case null:
                    satisfied = true;
                    break;
            }
            if (satisfied)
            {
                _waitKind = null;
                Advance();
                return;
            }
            var step = CurrentStep;
            float timeout = step?.Wait?.TimeoutSec ?? 0f;
            if (timeout > 0f && _elapsed >= timeout)
                Finish(TokenOr(step?.OnTimeout, "end"), "timeout in " + (_waitKind ?? "?"));
        }

        private StoryFlowStep CurrentStep
            => _index >= 0 && _index < _flow.Steps.Count ? _flow.Steps[_index] : null;

        private void Advance()
        {
            if (_done) return;
            _index++;
            if (_index >= _flow.Steps.Count) { Finish("end", "flow complete"); return; }
            var step = _flow.Steps[_index];
            _elapsed = 0f;
            _waitKind = step.Wait?.Kind;
            _timerSeconds = 0f;
            if (_waitKind == "timer")
                _timerSeconds = TryFloat(step.Args, "Seconds", 0f);

            if (!RunAtom(step))
            {
                Finish(TokenOr(step.OnFail, "end"), "atom failed: " + step.Atom);
                return;
            }
            if (_done) return;
            if (_waitKind == null) Advance();
        }

        private bool RunAtom(StoryFlowStep step)
        {
            var agent = _host.GetAgent(_actorId);
            switch ((step.Atom ?? "").ToLowerInvariant())
            {
                case "dialogue_open":
                    // 唯一的开窗入口：对话 ID 由数据指定，等待只看**本次 session**。
                    if (agent == null) return false;
                    _dialogueId = Arg(step, "Dialogue");
                    _host.StartStoryConversation(agent, null, "first_meet_auto", _dialogueId);
                    // 记下这次真正开出来的会话编号：后面判定“还是我们”靠它，不靠对话 ID。
                    _sessionId = StoryDialogueRuntime.Current?.Id ?? 0;
                    return SessionMatches();

                case "dialogue_wait":
                    return true;

                case "actor_say":
                {
                    if (agent == null) return false;
                    string key = Arg(step, "TextKey");
                    if (string.IsNullOrEmpty(key)) return false;
                    _host.SayOnAgent(agent, key, null);
                    return true;
                }

                case "actor_action":
                {
                    if (agent == null) return false;
                    string action = Arg(step, "Action");
                    if (string.IsNullOrEmpty(action)) return false;
                    AgentControlHelper.ForcePlayAction(agent, action, restoreAfter: false);
                    return true;
                }

                default:
                    DebugLogger.Log("[StoryV2] flow atom not executable: " + step.Atom);
                    return false;
            }
        }

        private string Arg(StoryFlowStep step, string key)
            => step.Args != null && step.Args.TryGetValue(key, out var v) ? v : null;

        private static float TryFloat(Dictionary<string, string> args, string key, float fallback)
            => args != null && args.TryGetValue(key, out var v)
               && float.TryParse(v, System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out float f)
                ? f : fallback;

        private static string TokenOr(string token, string fallback)
            => string.IsNullOrEmpty(token) ? fallback : token;

        private void Finish(string token, string reason)
        {
            if (string.IsNullOrEmpty(token) || token == "end" || token == "release"
                || token == "retry" || token == "reselect")
            {
                _done = true;
                _reason = token + ": " + reason;
                _terminal = token ?? "";
                _director?.OnFlowTerminal(_actorId, token);
                return;
            }
            if (token == "next") { _waitKind = null; Advance(); return; }
            int target = _flow.Steps.FindIndex(s => s != null && string.Equals(s.Id, token, StringComparison.Ordinal));
            if (target >= 0)
            {
                _index = target - 1;
                _waitKind = null;
                _elapsed = 0f;
                Advance();
                return;
            }
            _done = true;
            _reason = "unknown branch '" + token + "': " + reason;
            _terminal = token;
            _director?.OnFlowTerminal(_actorId, token);
        }

        /// <summary>
        /// 是否仍然处于**本次流程开的那个会话**：比会话编号 + 对话 ID + 实际演员。
        /// 🔴 只比 DialogueId 不够 —— 同一份对话被另一个流程/另一个演员重开时会被误判成“还是我们”，
        ///    「被替换/被取消」也就走不到对应数据分支。
        /// </summary>
        private bool SessionMatches()
        {
            var s = StoryDialogueRuntime.Current;
            if (s == null || s.Ended) return false;
            if (_sessionId != 0 && s.Id != _sessionId) return false;
            if (!string.IsNullOrEmpty(_dialogueId)
                && !string.Equals(s.Definition?.DialogueId, _dialogueId, StringComparison.Ordinal)) return false;
            if (!string.IsNullOrEmpty(_actorId)
                && !string.Equals(s.Request?.ActorId, _actorId, StringComparison.Ordinal)) return false;
            return true;
        }
    }
}
