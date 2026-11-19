using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.MountAndBlade;
using TaleWorlds.Library;

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
        private sealed class GroupRun
        {
            public StoryFlowRunner Flow;
            public string BehaviorId, ActorId;
        }
        private readonly List<GroupRun> _runs = new List<GroupRun>();
        private readonly Dictionary<string, float> _lastOpened = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _outsideSince = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _pending = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<StoryLayoutDef, Dictionary<string, Vec3>> _anchors = new Dictionary<StoryLayoutDef, Dictionary<string, Vec3>>();
        private readonly Dictionary<StoryLayoutDef, Dictionary<string, string>> _anchorResults = new Dictionary<StoryLayoutDef, Dictionary<string, string>>();
        private string _lastReport = "not evaluated";
        private string _lastRefusalReport;
        private float _nextRefusalLogAt;
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
                                    + "'; v2 rules disabled here.");
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
                      .Append(" members=").Append(string.Join(",", b.Members))
                      .Append(" target=").Append(b.Trigger.TargetPoint ?? b.Trigger.TargetGate ?? "member-posts")
                      .Append(" active=").Append(_runs.Count(r => r.BehaviorId == b.Id && !r.Flow.Done))
                      .Append("/").Append(b.MaxActive)
                      .Append(" pending=").Append(_pending.TryGetValue(b.Id, out var pending) ? pending : "-")
                      .Append(" range=").Append(b.Perception.RangeMeters.ToString("F1"));
                    if (_opened.TryGetValue(b.Id, out int count)) sb.Append(" opened=").Append(count);
                }
            }
            sb.Append("\n  last: ").Append(_lastReport);
            foreach (var run in _runs.Where(r => !r.Flow.Done))
                sb.Append("\n  guard group ").Append(run.BehaviorId).Append(" actor=").Append(run.ActorId).Append(" ").Append(run.Flow.Describe());
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
            if (!HasRules(stageId))
            {
                _lastReport = "no matching published post rules";
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
                if (!InGroupZone(layout, behavior, null))
                { lines.Add(behavior.Id + ": player outside gate passage"); continue; }

                foreach (string id in stage.Actors.Concat(stage.OptionalActors).Distinct())
                {
                    var def = StoryProbeDefs.GetNpc(id);
                    if (def == null) { lines.Add(id + ": no cast def"); continue; }

                    // 正式规则只认本布局已登记的岗位，不借用某场戏的选角旗标。
                    var standing = layout.Standing.FirstOrDefault(s => s != null && s.Actor == id);
                    bool onPost = standing != null && behavior.Posts.Contains(standing.Point ?? "");
                    if (behavior.Members.Count > 0 && !behavior.Members.Contains(id)) continue;
                    if (!onPost) { lines.Add(id + ": not on a post of " + behavior.Id); continue; }

                    if (_runs.Count(r => r.BehaviorId == behavior.Id && !r.Flow.Done)
                        + (_pending.ContainsKey(behavior.Id) ? 1 : 0) >= behavior.MaxActive)
                    { lines.Add(behavior.Id + ": active limit " + behavior.MaxActive); continue; }
                    if (_lastOpened.TryGetValue(behavior.Id, out float previous) && _host.Clock - previous < behavior.Cooldown.ReentrySec)
                    { lines.Add(behavior.Id + ": group reentry cooldown"); continue; }
                    var agent = _host.GetAgent(id);
                    if (_pending.Values.Contains(id)) { lines.Add(id + ": group request pending"); continue; }
                    if (_runs.Any(r => r.ActorId == id && !r.Flow.Done)) { lines.Add(id + ": group flow active"); continue; }
                    if (agent == null) { lines.Add(id + ": no agent"); continue; }
                    if (!AgentControlHelper.SafeIsActive(agent) || agent.Health <= 0f)
                    { lines.Add(id + ": inactive/dead"); continue; }
                    if (AgentBrain.IsRoped(agent) || AgentBrain.IsHeldByStatus(agent) || AgentBrain.IsKnockedOut(agent))
                    { lines.Add(id + ": bound/held/downed"); continue; }

                    var brain = AgentAIController.GetBrainForAgent(agent);
                    if (brain == null) { lines.Add(id + ": no brain"); continue; }
                    if (brain.IsPerforming || brain.IsInCombat || brain.IsStoryIntercepting
                        || agent.GetScriptedFlags().HasFlag(Agent.AIScriptedFrameFlags.InConversation))
                    { lines.Add(id + ": brain busy"); continue; }

                    // 岗位职责区（2026-11-16）：进了"我这名守卫"的感知半径就算轮到我，
                    // 不再要求玩家正好踩在门轴中线 ±3 米那条窄带里（旧口径见 Trigger.PlayerInsideGate）。
                    if (!InGroupZone(layout, behavior, standing?.Point))
                    { lines.Add(id + ": outside my post zone"); continue; }

                    // 资格只查询视线；注视与身体方向由感知/Brain执行。
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
                    bool better = bestActor == null || bestBehavior.Id == behavior.Id
                        && CompareCandidates(behavior, id, distance, bestActor, bestDistance) < 0;
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
        /// 复用准备批次的场景锚点；尚无对应批次时自行只读解析。
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

            var def = layout.FindPoint(standing.Point);
            if (def == null) return false;
            // 挂道具旁的点位没有独立坐标（由 Prop + Side/Offset 推导）；这里不猜，交调用方兜底。
            if (!string.IsNullOrEmpty(def.Prop)) return false;
            StoryPoint point = def.Place;
            if (point == null) return false;

            // 绝对坐标优先（2026-11-16：点位可以不带 Anchor）
            if (point.HasAbsolute)
            {
                position = new TaleWorlds.Library.Vec3(point.Absolute[0], point.Absolute[1], point.Absolute[2]);
                return true;
            }

            var anchors = GetAnchors(layout);
            if (!CanResolve(layout, point)) return false;
            position = StoryPrepRunner.PointPos(_host, anchors, point);
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
                _lastOpened[behaviorId] = _host.Clock;
            }
            _lastReport = (opened ? "opened " : "refused ") + key + " (" + (reason ?? "-") + ")";
            DebugLogger.Log("[StoryV2] intercept " + _lastReport);
        }

        /// <summary>按流程开会话（流程里的 dialogue_open 才是真正开窗点）。</summary>
        public bool TryRequestQuestion(string stageId, Action<bool> onResult = null)
        {
            if (AgentAIController.Instance == null || ConversationEntryPatch.IsConversationActive()) return false;
            if (!TrySelectQuestioner(stageId, out string actorId, out string behaviorId, out string report))
            {
                // 只在实际申请时记录；状态查询不写身体，也不刷拒绝日志。
                if (report != _lastRefusalReport && _host.Clock >= _nextRefusalLogAt)
                {
                    _lastRefusalReport = report;
                    _nextRefusalLogAt = _host.Clock + 5f;
                    DebugLogger.Log("[StoryV2] no eligible questioner: " + report);
                }
                return false;
            }
            var actor = _host.GetAgent(actorId);
            if (actor == null || _pending.ContainsKey(behaviorId)) return false;
            var requestedFile = StoryLayoutRegistry.Published;
            var requestedPhase = _host.Phase;
            _pending[behaviorId] = actorId;
            _lastRefusalReport = null;
            DebugLogger.Log("[StoryV2] Brain questioning request stage=" + stageId + " group=" + behaviorId + " actor=" + actorId);
            AgentAIController.Instance.SendEventToAgent(actor, "event_story_intercept", Agent.Main, "auto",
                new Action<Agent>(ready =>
                {
                    _pending.Remove(behaviorId);
                    if (!_host.Active || StoryProbeMissionBehavior.Current != _host
                        || _host.Phase != requestedPhase || StoryLayoutRegistry.Published != requestedFile)
                    { onResult?.Invoke(false); return; }
                    bool opened = false;
                    string reason = "brain refused";
                    if (ready == actor && AgentControlHelper.SafeIsActive(actor)
                        && BeginQuestionFlow(behaviorId, actorId, out reason))
                        opened = ConversationEntryPatch.IsConversationActive();
                    if (!opened) NoteAttempt(behaviorId, actorId, false, reason);
                    onResult?.Invoke(opened);
                }));
            return true;
        }

        public bool BeginQuestionFlow(string behaviorId, string actorId, out string reason)
        {
            reason = null;
            var file = StoryLayoutRegistry.Published;
            if (file == null || !file.Behaviors.TryGetValue(behaviorId, out var behavior) || behavior == null)
            { reason = "unknown behavior " + behaviorId; return false; }
            if (string.IsNullOrEmpty(behavior.Flow) || !file.Flows.TryGetValue(behavior.Flow, out var flow) || flow == null)
            { reason = "unknown flow " + behavior.Flow; return false; }

            if (_runs.Count(r => r.BehaviorId == behaviorId && !r.Flow.Done) >= behavior.MaxActive)
            { reason = "group active limit reached"; return false; }
            var run = new GroupRun { Flow = new StoryFlowRunner(_host, behavior, flow, actorId, this),
                BehaviorId = behaviorId, ActorId = actorId };
            _runs.Add(run);
            try { run.Flow.Start(); }
            catch (Exception ex)
            {
                _runs.Remove(run);
                reason = "flow start failed: " + ex.Message;
                DebugLogger.Log("[StoryV2] " + reason);
                return false;
            }
            reason = run.Flow.Done ? "finished immediately: " + run.Flow.Reason : "started";
            return !run.Flow.Done || run.Flow.TerminalToken == "end" || run.Flow.TerminalToken == "release";
        }

        public void Tick(float dt, string stageId)
        {
            // 离开本组职责区足够久才算新遭遇；查询不写回执。
            var file = StoryLayoutRegistry.Published;
            if (file != null && file.Stages.TryGetValue(stageId, out var stage)
                && file.Layouts.TryGetValue(stage.Layout, out var layout))
                foreach (var b in file.Behaviors.Values.Where(b => b.Kind == "post" && b.Stages.Contains(stageId)))
                {
                    bool inside = b.Posts.Any(point => InGroupZone(layout, b, point));
                    if (inside) _outsideSince.Remove(b.Id);
                    else if (!_outsideSince.ContainsKey(b.Id)) _outsideSince[b.Id] = _host.Clock;
                    else if (_host.Clock - _outsideSince[b.Id] >= b.Cooldown.ReentrySec
                        && !_pending.ContainsKey(b.Id) && !_runs.Any(r => r.BehaviorId == b.Id && !r.Flow.Done)) _opened.Remove(b.Id);
                }
            foreach (var run in _runs.ToArray())
            {
                try { if (!run.Flow.Done) run.Flow.Tick(dt); }
                catch (Exception ex)
                {
                    NoteAttempt(run.BehaviorId, run.ActorId, false, "flow failed: " + ex.Message);
                    _runs.Remove(run);
                    continue;
                }
                if (!run.Flow.Done) continue;
                if (run.Flow.TerminalToken == "retry" || run.Flow.TerminalToken == "reselect")
                    NoteAttempt(run.BehaviorId, run.ActorId, false, run.Flow.Reason);
                _runs.Remove(run);
            }
        }

        private static int CompareCandidates(StoryBehaviorDef behavior, string actor, float distance, string other, float otherDistance)
        {
            foreach (string rank in behavior.Rank)
            {
                int comparison = rank == "npcId" ? string.CompareOrdinal(actor, other) : distance.CompareTo(otherDistance);
                if (comparison != 0) return comparison;
            }
            return 0;
        }

        public void TickLeash(string stageId)
        {
            var file = StoryLayoutRegistry.Published;
            if (file == null || !file.Stages.TryGetValue(stageId, out var stage)
                || !file.Layouts.TryGetValue(stage.Layout, out var layout)) return;
            foreach (var behavior in file.Behaviors.Values.Where(b => b.Kind == "post" && b.Stages.Contains(stageId)))
                foreach (var standing in layout.Standing.Where(s => behavior.Posts.Contains(s.Point)
                    && (behavior.Members.Count == 0 || behavior.Members.Contains(s.Actor))))
                {
                    bool owns = _pending.TryGetValue(behavior.Id, out var pending) && pending == standing.Actor
                        || _runs.Any(r => r.BehaviorId == behavior.Id && r.ActorId == standing.Actor && !r.Flow.Done);
                    var agent = _host.GetAgent(standing.Actor);
                    if (agent == null || !AgentControlHelper.SafeIsActive(agent)) continue;
                    var brain = AgentAIController.GetBrainForAgent(agent);
                    if (owns && brain?.IsStoryIntercepting == true && !InGroupZone(layout, behavior, standing.Point, 2f))
                    {
                        AgentAIController.Instance?.SendEventToAgent(agent, "event_story_intercept_cancel");
                        DebugLogger.Log("[StoryV2] guard group " + behavior.Id + " chase cancelled actor=" + standing.Actor);
                    }
                }
        }

        private bool InGroupZone(StoryLayoutDef layout, StoryBehaviorDef behavior, string postId, float margin = 1f)
        {
            var player = Agent.Main;
            if (player == null) return false;
            var anchors = GetAnchors(layout);
            var trigger = behavior.Trigger;
            if (trigger.PlayerInsideGate)
            {
                if (string.IsNullOrEmpty(trigger.TargetGate) || !layout.Gates.TryGetValue(trigger.TargetGate, out var gate)) return false;
                if (!CanResolve(layout, gate.Center) || !CanResolve(layout, gate.Inner) || !CanResolve(layout, gate.Outside)) return false;
                Vec3 center = StoryPrepRunner.PointPos(_host, anchors, gate.Center);
                Vec2 direction = (StoryPrepRunner.PointPos(_host, anchors, gate.Inner)
                    - StoryPrepRunner.PointPos(_host, anchors, gate.Outside)).AsVec2;
                if (direction.LengthSquared < 0.0001f) return false;
                direction.Normalize();
                Vec2 delta = (player.Position - center).AsVec2;
                float along = Vec2.DotProduct(delta, direction);
                float side = Math.Abs(Vec2.DotProduct(delta, new Vec2(direction.y, -direction.x)));
                if (along < -gate.OutsideMeters * margin || along > gate.InsideMeters * margin || side > gate.HalfWidth * margin) return false;
            }
            if (trigger.PlayerInPostZone)
            {
                string pointId = string.IsNullOrEmpty(trigger.TargetPoint) ? postId : trigger.TargetPoint;
                if (pointId == null) return true; // 每名成员的岗位区在候选循环中判。
                var point = layout.FindPoint(pointId);
                if (point == null) return false;
                Vec3 position;
                if (!string.IsNullOrEmpty(point.Prop))
                {
                    var prop = layout.FindProp(point.Prop);
                    if (prop == null || _host.Prep == null || !_host.Prep.TryGetPropEntity(prop.Id, out var entity)) return false;
                    position = StoryPrepRunner.WorkPos(entity, prop, point);
                }
                else
                {
                    if (!CanResolve(layout, point.Place)) return false;
                    position = StoryPrepRunner.PointPos(_host, anchors, point.Place);
                }
                if (position.Distance(player.Position) > behavior.Perception.RangeMeters * margin) return false;
            }
            return true;
        }

        private Dictionary<string, Vec3> GetAnchors(StoryLayoutDef layout)
        {
            if (_host.Prep?.Layout == layout && _host.Prep.ResolvedAnchors != null) return _host.Prep.ResolvedAnchors;
            if (!_anchors.TryGetValue(layout, out var anchors))
            {
                var results = new Dictionary<string, string>();
                _anchors[layout] = anchors = StoryPrepRunner.ResolveAnchors(_host, layout, results);
                _anchorResults[layout] = results;
            }
            return anchors;
        }

        private bool CanResolve(StoryLayoutDef layout, StoryPoint point)
        {
            if (point == null) return false;
            if (point.HasAbsolute || string.IsNullOrEmpty(point.Anchor) || point.Anchor == "player") return true;
            if (_host.Prep?.Layout == layout && _host.Prep.ResolvedAnchors != null)
                return _host.Prep.IsAnchorResolved(point.Anchor);
            GetAnchors(layout);
            return _anchorResults[layout].TryGetValue("anchor:" + point.Anchor, out var result)
                && result.StartsWith("ok", StringComparison.Ordinal);
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
        private string _dialogueActor;
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
            string identity = ResolveActor(step.Actor);
            var agent = identity == "player" ? Agent.Main : string.IsNullOrEmpty(identity) ? null : _host.GetAgent(identity);
            switch ((step.Atom ?? "").ToLowerInvariant())
            {
                case "dialogue_open":
                    // 唯一的开窗入口：对话 ID 由数据指定，等待只看**本次 session**。
                    if (agent == null) return false;
                    _dialogueId = Arg(step, "Dialogue");
                    _dialogueActor = identity;
                    _host.StartStoryConversation(agent, null, Arg(step, "Trigger"), _dialogueId);
                    // 记下这次真正开出来的会话编号：后面判定“还是我们”靠它，不靠对话 ID。
                    _sessionId = StoryDialogueRuntime.Current?.Id ?? 0;
                    bool opened = SessionMatches() && ConversationEntryPatch.IsConversationActive();
                    if (opened)
                    {
                        _director?.NoteAttempt(_behavior.Id, _actorId, true, "flow dialogue opened actor=" + identity);
                        if (_waitKind == "dialogue_open") _waitKind = null;
                    }
                    return opened;

                case "dialogue_wait":
                    return true;

                case "actor_say":
                {
                    if (agent == null) return false;
                    string key = Arg(step, "TextKey");
                    if (string.IsNullOrEmpty(key)) return false;
                    string listenerId = ResolveActor(Arg(step, "Listener"));
                    var listener = listenerId == "player" ? Agent.Main : _host.GetAgent(listenerId);
                    if (!AgentControlHelper.SafeIsActive(listener) || listener == agent) return false;
                    _host.SayOnAgent(agent, key, null, listener: listener);
                    return true;
                }

                case "actor_action":
                {
                    if (agent == null) return false;
                    string action = Arg(step, "Action");
                    if (string.IsNullOrEmpty(action)) return false;
                    var brain = AgentAIController.GetBrainForAgent(agent);
                    if (brain == null || !brain.IsStoryIntercepting || brain.IsInCombat || brain.IsPerforming
                        || AgentBrain.IsRoped(agent) || AgentBrain.IsHeldByStatus(agent) || AgentBrain.IsKnockedOut(agent)) return false;
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

        private string ResolveActor(string actor)
        {
            if (string.IsNullOrEmpty(actor)) return null;
            if (actor.StartsWith("$", StringComparison.Ordinal))
            {
                if (!_flow.Roles.TryGetValue(actor.Substring(1), out actor)) return null;
            }
            return actor == "candidate" ? _actorId : actor;
        }

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
            if (!string.IsNullOrEmpty(_dialogueActor)
                && !string.Equals(s.Request?.ActorId, _dialogueActor, StringComparison.Ordinal)) return false;
            return true;
        }
    }
}
