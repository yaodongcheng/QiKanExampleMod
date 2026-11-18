using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 市井导演（2026-11-12，plans/逆水寒511/逆水寒511-总纲与实施计划.md §十五的需求§6 / §7）。
    ///
    /// 三层行为：
    ///   ① 常驻动作 —— 每人 8~20 秒挑一条原版动作 + 偶尔一句吆喝（原版动作名见 vanilla_action_names.txt）；
    ///   ② 开场事件 —— 6 起（4 双人 + 2 单人），**开演后停在开头自循环**：台词每 3~5 秒一句、
    ///      动作循环、头顶「!」；玩家主动按 F 才暂停并接入对话，靠近只观看；
    ///   ③ 随机桥段 —— 90 秒一段，只是氛围，不可接入（§7.1）。
    ///
    /// 纪律：
    ///   · 只接管我们 spawn 的 9 个人（原版村民 / 守卫一律不动）；
    ///   · 说话走 `SpeechChannel.Say(..., forwardToNearby: false)` —— 只冒泡，不进附近频道；
    ///   · 事件、桥段与常驻姿态统一持 PerformanceAction；演员全部 CanControl 才写身体；
    ///   · 对话暂停先归还身体，攻击/特殊状态撤销演出；不暂停全场 AI、不锁全体候场人物。
    /// </summary>
    public class StreetLifeDirector
    {
        private readonly StoryProbeMissionBehavior _host;

        private float _time;
        private readonly System.Random _rng = new System.Random();

        // ── 事件 ──
        private StoryEventDef _activeEvent;
        private bool _eventPaused;
        private bool _eventSkit;              // true = 随机桥段（无「!」、不可接入）
        private float _eventElapsed;
        private float _nextEventLineAt;
        private int _lineCursor;
        private float _playerFarSince = -1f;
        private float _nextEventAt;
        private int _nextEventIndex = 1;
        private readonly HashSet<int> _playedEvents = new HashSet<int>();
        private readonly Mission _mission = Mission.Current;
        private List<StoryEventDef> _events = new List<StoryEventDef>();
        private string _eventOwner;
        private string _eventInterrupted;
        private bool _eventPrepared;
        private float _eventReadyWait;
        private readonly Dictionary<Agent, PerformanceAction> _eventActors = new Dictionary<Agent, PerformanceAction>();
        private sealed class AmbientSlot
        {
            public PerformanceAction Lease;
            public string Action;
            public bool Played;
            public float Until;
            public string LineKey;
            public string LineFallback;
            public Agent Listener;
            public float Duration;
        }
        private readonly Dictionary<Agent, AmbientSlot> _ambientActors = new Dictionary<Agent, AmbientSlot>();

        // ── 常驻 / 桥段 / 反应 ──
        private readonly Dictionary<string, float> _nextGreetingAt = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _nextSpeechAt = new Dictionary<string, float>();
        private readonly Dictionary<string, int> _speechCursor = new Dictionary<string, int>();
        private float _nextAmbientSpeechAt;
        private int _nextSpeechDefIndex;
        private float _nextSkitAt;
        private bool _playerWasArmed;

        // ── 调试 ──
        public bool Stopped { get; private set; }

        /// <summary>当前市井事件编号（没有 = 0）；对话条件 `event_active:<n>` 读它。</summary>
        public int ActiveEventIndex => _activeEvent?.Index ?? 0;

        /// <summary>这个人是不是当前群组事件的参与者（工作点要让位）。</summary>
        public bool IsEventParticipant(string actorId)
            => _activeEvent != null && !_eventSkit && !string.IsNullOrEmpty(actorId)
               && (actorId == _activeEvent.ActorA
                   || (_activeEvent.TwoPerson && actorId == _activeEvent.ActorB));
        private string _forcedAmbientNpc;
        private int _forcedAmbientIndex = -1;

        public StreetLifeDirector(StoryProbeMissionBehavior host)
        {
            _host = host;
        }

        public void Begin()
        {
            _time = 0f;
            var t = StoryProbeTuning.Current;
            _nextEventAt = _time + t.Events.FirstDelaySec;   // 进场多久起第一起（§6.1）
            _nextSkitAt = t.Skits.FirstDelaySec;
            _playedEvents.Clear();
            _nextSpeechAt.Clear();
            _speechCursor.Clear();
            _nextAmbientSpeechAt = 0f;
            _nextSpeechDefIndex = 0;
            foreach (var speech in StoryProbeDefs.StreetLife)
                _nextSpeechAt[speech.NpcId] = SpeechDelay(speech.SpeechFirstDelayMinSec, speech.SpeechFirstDelayMaxSec);
            _events = LoadEvents();

            foreach (var def in StoryProbeDefs.Npcs)
            {
                _nextGreetingAt[def.Id] = 0f;
            }

            // 普通候场不锁全体 NPC；真正起演时才申请 Brain 演出租约。
        }

        public bool HasEventMark(Agent agent)
        {
            if (agent == null || _activeEvent == null || !_eventPrepared || _eventSkit || _eventPaused
                || _eventInterrupted != null || !OwnsBody(agent)) return false;
            string id = ResolveNpcId(agent);
            return EventMembers(_activeEvent).Contains(id);
        }

        private static List<string> EventMembers(StoryEventDef def)
        {
            if (def?.Members != null && def.Members.Count > 0) return def.Members;
            var list = new List<string>();
            if (!string.IsNullOrEmpty(def?.ActorA)) list.Add(def.ActorA);
            if (def != null && def.TwoPerson && !string.IsNullOrEmpty(def.ActorB)) list.Add(def.ActorB);
            return list;
        }

        public void Tick(float dt)
        {
            if (Mission.Current != _mission) { DisposeManaged(); return; }
            if (Stopped) return;
            _time += dt;

            bool inConversation = IsConversationActive();

            // 玩家接入 / 对话结束 → 收事件
            if (_activeEvent != null)
            {
                TickActiveEvent(dt, inConversation);
            }
            else if (_time >= _nextEventAt)
            {
                StartNextEvent();
            }

            if (!inConversation)
            {
                TickAmbient(dt);
                TickAmbientSpeech();
                TickReactions(dt);
                TickSkit(dt);
            }
            else
            {
                _playerWasArmed = PlayerHasWeaponDrawn();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 事件
        // ═══════════════════════════════════════════════════════════════

        private void StartNextEvent()
        {
            if (_events.Count == 0) return;
            // 依次轮转 1..6；都演过一遍后重新轮（不锁死、没有"错过"）
            if (_playedEvents.Count >= _events.Count)
            {
                _playedEvents.Clear();
                _nextEventIndex = 1;
            }

            // Resolved conflicts stay resolved; an exhausted cycle only waits.
            for (int remaining = _events.Count; remaining > 0; remaining--)
            {
                var def = FindEvent(_nextEventIndex) ?? _events[0];
                _nextEventIndex = def.Index % _events.Count + 1;
                if (StoryProbeState.IsStreetEventResolved(def.Index)) continue;
                StartEvent(def, skit: false);
                return;
            }
            _nextEventAt = _time + StoryProbeTuning.Current.Events.RetryGapSec;
        }

        /// <summary>
        /// P4：群组事件优先读 v2 `Behaviors(Kind=group)`；没有正式数据才回退选角表。
        /// 这样改成员 / 台词 / 动作 / 主持人 / 接入节点只改 JSON。
        /// </summary>
        private List<StoryEventDef> LoadEvents()
        {
            var fallback = StoryProbeDefs.Events.ToList();
            if (!StoryV2Data.EnsurePublished()) return fallback;
            var file = StoryLayoutRegistry.Published;
            if (file == null) return fallback;
            var groups = file.Behaviors.Values
                .Where(b => b != null && b.Kind == "group" && b.Id.StartsWith("event_", StringComparison.Ordinal))
                .OrderBy(b => b.Id, StringComparer.Ordinal).ToList();
            if (groups.Count == 0) return fallback;

            var list = new List<StoryEventDef>();
            foreach (var g in groups)
            {
                if (!int.TryParse(g.Id.Substring("event_".Length), out int index) || index <= 0) continue;
                var cast = fallback.FirstOrDefault(e => e.Index == index);
                var members = g.Members ?? new List<string>();
                if (members.Count == 0 && cast != null)
                {
                    members = cast.TwoPerson ? new List<string> { cast.ActorA, cast.ActorB }
                                             : new List<string> { cast.ActorA };
                }
                if (members.Count == 0) continue;
                list.Add(new StoryEventDef
                {
                    Index = index,
                    TwoPerson = members.Count > 1,
                    ActorA = members[0],
                    ActorB = members.Count > 1 ? members[1] : null,
                    LineKeys = g.Lines.Count > 0 ? g.Lines.ToArray() : cast?.LineKeys,
                    LineFallbacks = g.LineFallbacks.Count > 0 ? g.LineFallbacks.ToArray() : cast?.LineFallbacks,
                    ActionA = g.ActionsA.Count > 0 ? g.ActionsA.ToArray() : cast?.ActionA,
                    ActionB = g.ActionsB.Count > 0 ? g.ActionsB.ToArray() : cast?.ActionB,
                    HostNpcId = !string.IsNullOrEmpty(g.Host) ? g.Host : cast?.HostNpcId,
                    JoinNode = !string.IsNullOrEmpty(g.JoinNode) ? g.JoinNode : cast?.JoinNode,
                    Placement = g.Placement,
                    Members = new List<string>(members),
                    Speakers = g.Speakers != null ? new List<string>(g.Speakers) : new List<string>(),
                    Listeners = g.Listeners != null ? new List<string>(g.Listeners) : new List<string>(),
                });
            }
            DebugLogger.Log("[StoryProbe] events from v2 groups=" + list.Count + " (fallback=" + fallback.Count + ")");
            return list.Count > 0 ? list : fallback;
        }

        private StoryEventDef FindEvent(int index)
            => _events.FirstOrDefault(e => e.Index == index);

        private void StartEvent(StoryEventDef def, bool skit)
        {
            if (def == null || StoryProbeState.IsStreetEventResolved(def.Index))
            {
                _nextEventAt = _time + StoryProbeTuning.Current.Events.RetryGapSec;
                return;
            }
            var memberIds = EventMembers(def);
            var memberAgents = new List<Agent>();
            foreach (string id in memberIds)
            {
                var agent = _host.GetAgent(id);
                if (agent == null)
                {
                    _nextEventAt = _time + StoryProbeTuning.Current.Events.RetryGapSec;
                    return;
                }
                memberAgents.Add(agent);
            }
            if (memberAgents.Count == 0)
            {
                _nextEventAt = _time + StoryProbeTuning.Current.Events.RetryGapSec;
                return;
            }

            if (IsConversationActive()) { _nextEventAt = _time + StoryProbeTuning.Current.Events.RetryGapSec; return; }
            foreach (var agent in memberAgents) ReleaseAmbient(agent);
            string session = "street-event-" + Guid.NewGuid().ToString("N");
            var accepted = new Dictionary<Agent, PerformanceAction>();
            foreach (var actor in memberAgents)
            {
                // 工作点正占着身体时先让位（事件结束后 Work 自己恢复）。
                _host.Work?.YieldActor(ResolveNpcId(actor), "group event");
                var lease = PerformanceAction.TryAcquire(actor, session, reason =>
                {
                    // 对话暂停导致的正常清理不冒充攻击；任何真正攻击/特殊状态必须散场。
                    if (_eventOwner == session && !(_eventPaused &&
                        (reason == "EndInteraction" || reason == "queue-cleared" || reason == "replaced" || reason == "conversation")))
                        _eventInterrupted = reason;
                });
                if (lease == null)
                {
                    foreach (var rollback in accepted.Values) rollback.Release("group-unavailable");
                    _nextEventAt = _time + StoryProbeTuning.Current.Events.RetryGapSec;
                    return;
                }
                accepted[actor] = lease;
            }
            foreach (var kv in accepted) _eventActors[kv.Key] = kv.Value;
            _eventOwner = session;
            _eventInterrupted = null;
            _eventPrepared = false;
            _eventReadyWait = 0f;
            _activeEvent = def;
            _eventSkit = skit;
            _eventPaused = false;
            _eventElapsed = 0f;
            _lineCursor = 0;
            _nextEventLineAt = _time + StoryProbeTuning.Current.Events.FirstLineDelaySec;
            _playerFarSince = -1f;
            _playedEvents.Add(def.Index);

            DebugLogger.Log($"[StoryProbe] event start #{def.Index} skit={skit} actors={string.Join("+", EventMembers(def))}");
        }

        private bool OwnsBody(Agent actor)
        {
            if (actor == null) return false;
            if (_eventActors.TryGetValue(actor, out var eventLease) && eventLease.CanControl) return true;
            return _ambientActors.TryGetValue(actor, out var ambient) && ambient.Lease.CanControl;
        }

        private void PrepareEventActors()
        {
            var members = EventMembers(_activeEvent);
            var agents = members.Select(id => _host.GetAgent(id)).Where(a => a != null).ToList();
            if (agents.Count == 0 || agents.Any(a => !OwnsBody(a))) return;

            if (_activeEvent.Placement == "standing")
            {
                // 初始点位已经由Prep和开场统一消费；起演不再按距离改位置，也不自动朝搭档转身。
                foreach (var actor in agents)
                {
                    string id = ResolveNpcId(actor);
                    var prep = _host.Prep;
                    if (prep == null || !prep.TryGetStandingPosition(id, out var position))
                        throw new InvalidOperationException("event standing unavailable: " + id);
                    string pointId = prep.Layout?.Standing.FirstOrDefault(s => s.Actor == id)?.Point;
                    var point = prep.Layout?.FindPoint(pointId);
                    if (point == null) throw new InvalidOperationException("event standing point unavailable: " + id);
                    // 外部移动了演员时，不能用另一次瞬移掩盖偏离；结束本场，等正常恢复再申请。
                    if (actor.Position.Distance(position) > point.ArriveTolerance)
                        throw new InvalidOperationException("event standing drift: " + id);
                    float angle = point.Facing * (float)Math.PI / 180f;
                    _eventActors[actor].AllowSpeechBodyTurn = false;
                    AgentControlHelper.SetBodyDirection(actor, new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle)));
                    DebugLogger.Log($"[StoryProbe] event placement #{_activeEvent.Index} mode=standing actor={id} point={pointId} initial={position} actual={actor.Position} facing={point.Facing:F1} moved=0");
                }
            }
            else
            {
                var a = agents[0];
                for (int i = 1; i < agents.Count; i++)
                {
                    var other = agents[i];
                    if (a.Position.Distance(other.Position) > StoryProbeTuning.Current.Events.ActorSpacingMeters)
                    {
                        Vec3 dir = other.Position - a.Position;
                        dir.z = 0f;
                        if (dir.LengthSquared < 0.01f) dir = new Vec3(0, 1, 0);
                        var offset = dir.NormalizedCopy() * StoryProbeTuning.Current.Events.ActorApproachMeters;
                        // 第三人及以上绕圈排开，避免叠在同一点。
                        if (i > 1)
                        {
                            float ang = (i - 1) * 1.2f;
                            offset = new Vec3((float)Math.Cos(ang), (float)Math.Sin(ang), 0f)
                                     * StoryProbeTuning.Current.Events.ActorApproachMeters;
                        }
                        other.TeleportToPosition(a.Position + offset);
                    }
                    AgentControlHelper.FaceToActor(a, other);
                    AgentControlHelper.FaceToActor(other, a);
                }
            }
            _eventPrepared = true;
            PlayLoopAction(agents[0], _activeEvent.ActionA);
            for (int i = 1; i < agents.Count; i++)
                PlayLoopAction(agents[i], i == 1 ? _activeEvent.ActionB : _activeEvent.ActionA);
        }

        private void TickActiveEvent(float dt, bool inConversation)
        {
            if (inConversation && !_eventPaused)
            {
                _eventPaused = true;
                foreach (var lease in _eventActors.Values) lease.Pause(true);
            }
            if (_eventInterrupted != null || (!_eventPaused && _eventActors.Values.Any(a => a.Ended
                || (_eventPrepared && !a.CanControl))))
            {
                EndEvent("actor interrupted: " + _eventInterrupted);
                _nextEventAt = _time + StoryProbeTuning.Current.Events.RetryGapSec;
                return;
            }
            if (!_eventPaused && !_eventPrepared)
            {
                if (_eventActors.Values.Any(a => !a.CanControl))
                {
                    _eventReadyWait += dt;
                    if (_eventReadyWait > StoryProbeTuning.Current.Performance.ActorReadyTimeoutSec) EndEvent("actor readiness timeout");
                    return;
                }
                try { PrepareEventActors(); }
                catch (Exception ex) { EndEvent("actor preparation failed: " + ex.Message); }
                if (_activeEvent == null || !_eventPrepared) return;
            }
            if (!_eventPaused && !inConversation) _eventElapsed += dt;
            var player = Agent.Main;

            // ① 近距离只打断不可参与的随机桥段；正式事件继续循环，F入口负责交接。
            if (!_eventPaused && !inConversation && player != null && _eventSkit
                && NearestParticipantDistance() <= StoryProbeTuning.Current.Events.JoinDistanceMeters)
            {
                EndEvent("skit-interrupted");
                return;
            }

            // ② 暂停中：等对话结束 → **回到循环**（§6.2：选【走开】不散场，谈过一场也继续演）
            //    再次参与仍需主动按 F，不设走远再回来才能交谈的距离门槛。
            if (_eventPaused)
            {
                if (!inConversation)
                {
                    var resume = _activeEvent;
                    bool skit = _eventSkit;
                    int lineCursor = _lineCursor;
                    float elapsed = _eventElapsed;
                    EndEvent("conversation ended");
                    StartEvent(resume, skit);
                    _lineCursor = lineCursor;
                    _eventElapsed = elapsed;
                    _nextEventLineAt = _time + StoryProbeTuning.Current.Events.ResumeLineDelaySec;
                    DebugLogger.Log($"[StoryProbe] event #{resume.Index} reacquired after conversation");
                }
                return;
            }

            // ③ 玩家走远（>20 m 持续 3 秒）→ 换下一场
            if (player != null && NearestParticipantDistance() > StoryProbeTuning.Current.Events.PlayerFarMeters)
            {
                if (_playerFarSince < 0f) _playerFarSince = _time;
                else if (_time - _playerFarSince > StoryProbeTuning.Current.Events.PlayerFarGraceSec)
                {
                    EndEvent("player-left");
                    _nextEventAt = _time + StoryProbeTuning.Current.Events.ResumeAfterReturnSec;
                    return;
                }
            }
            else _playerFarSince = -1f;

            // 玩家正在跟别人说话（含跟事件里的人说话）→ 事件台词/动作全部让位，别抢话
            if (inConversation) return;

            // ④ 循环台词（每 3~5 秒一句，A/B 交替）
            if (_time >= _nextEventLineAt)
            {
                PlayNextEventLine();
                var et = StoryProbeTuning.Current.Events;
                _nextEventLineAt = _time + et.LineGapMinSec + (float)_rng.NextDouble() * (et.LineGapMaxSec - et.LineGapMinSec);
            }

            // ⑤ 动作循环（每 4~7 秒换一次姿态）
            if (_eventElapsed >= StoryProbeTuning.Current.Events.ActionChangeAfterSec
                && _rng.NextDouble() < dt * StoryProbeTuning.Current.Events.ActionChangeChancePerSec)
            {
                var members = EventMembers(_activeEvent);
                for (int i = 0; i < members.Count; i++)
                {
                    var agent = _host.GetAgent(members[i]);
                    PlayLoopAction(agent, i == 1 ? _activeEvent.ActionB : _activeEvent.ActionA);
                }
            }

            // 单人事件：一直重复发愁的动作（§6.1 硬规则 1）
            if (EventMembers(_activeEvent).Count == 1 && _rng.NextDouble() < dt * StoryProbeTuning.Current.Events.SoloActionChancePerSec)
            {
                var a = _host.GetAgent(_activeEvent.ActorA);
                PlayLoopAction(a, _activeEvent.ActionA);
            }

            // 桥段：20 秒后自己散开
            if (_eventSkit && _eventElapsed > StoryProbeTuning.Current.Skits.MaxDurationSec) EndEvent("skit-done");
        }

        private void PlayNextEventLine()
        {
            if (_activeEvent == null) return;

            var keys = _activeEvent.LineKeys;
            var fallbacks = _activeEvent.LineFallbacks;
            if (keys == null || keys.Length == 0) return;

            int i = _lineCursor % keys.Length;
            _lineCursor++;

            string actorId;
            if (_activeEvent.Speakers != null && i < _activeEvent.Speakers.Count)
                actorId = _activeEvent.Speakers[i];
            else
            {
                var members = EventMembers(_activeEvent);
                actorId = members.Count > 0 ? members[i % members.Count] : _activeEvent.ActorA;
            }

            var agent = _host.GetAgent(actorId);
            if (agent == null || !OwnsBody(agent)) return;
            if (_activeEvent.Listeners == null || i >= _activeEvent.Listeners.Count) return;
            var listener = _host.ResolveSpeechListener(_activeEvent.Listeners[i]);
            if (!AgentControlHelper.SafeIsActive(listener) || listener == agent || listener.Health <= 0f) return;

            string fb = fallbacks != null && i < fallbacks.Length ? fallbacks[i] : "";
            _host.SayOnAgent(agent, keys[i], fb,
                _eventSkit ? SpeechPriority.Chat : SpeechPriority.Warning, listener);
        }

        /// <summary>Only successful intervention resolves the event; leaving/failure resumes it.</summary>
        public bool ResolveCurrentEvent(int expectedIndex = 0)
        {
            if (_activeEvent == null || _eventSkit
                || (expectedIndex > 0 && _activeEvent.Index != expectedIndex)
                || StoryProbeState.IsStreetEventResolved(_activeEvent.Index)) return false;
            StoryProbeState.ResolveStreetEvent(_activeEvent.Index);
            EndEvent("resolved by player");
            _nextEventAt = _time + StoryProbeTuning.Current.Events.ResumeGapSec;
            return true;
        }

        private void EndEvent(string reason)
        {
            if (_activeEvent != null)
                DebugLogger.Log($"[StoryProbe] event end #{_activeEvent.Index} ({reason})");
            _eventOwner = null;
            var leases = _eventActors.Values.ToList();
            _eventActors.Clear();
            foreach (var lease in leases) lease.Release(reason);
            _activeEvent = null;
            _eventPrepared = false;
            _eventInterrupted = null;
            _eventPaused = false;
            _eventSkit = false;
            _eventElapsed = 0f;
            _playerFarSince = -1f;
        }

        public void ForceEvent(int index)
        {
            var def = FindEvent(index) ?? StoryProbeDefs.GetEvent(index);
            if (def == null) return;
            EndEvent("forced");
            StartEvent(def, skit: false);
        }

        private float NearestParticipantDistance()
        {
            var player = Agent.Main;
            if (player == null || _activeEvent == null) return float.MaxValue;
            float best = float.MaxValue;
            foreach (string id in EventMembers(_activeEvent))
            {
                var agent = _host.GetAgent(id);
                if (agent != null) best = Math.Min(best, player.Position.Distance(agent.Position));
            }
            return best;
        }

        // ═══════════════════════════════════════════════════════════════
        // 随机桥段（§7.1：90 秒一段，同时只 1 段，不可接入）
        // ═══════════════════════════════════════════════════════════════

        private void TickSkit(float dt)
        {
            if (_time < _nextSkitAt) return;
            _nextSkitAt = _time + StoryProbeTuning.Current.Skits.FirstDelaySec;
            if (_activeEvent != null) return;

            // 从 4 起双人事件里挑一段当氛围
            var pool = StoryProbeDefs.Events.Where(e => e.TwoPerson).ToList();
            if (pool.Count == 0) return;
            StartEvent(pool[_rng.Next(pool.Count)], skit: true);
        }

        // ═══════════════════════════════════════════════════════════════
        // 常驻动作 + 反应
        // ═══════════════════════════════════════════════════════════════

        /// <summary>纯读F入口路由：成员、业务主持人与接入节点均取当前事件数据。</summary>
        internal bool TryGetDialogueEntry(Agent clicked, out string hostActor, out string entryNode)
        {
            hostActor = null;
            entryNode = null;
            if (clicked == null || _activeEvent == null || _eventSkit || _eventInterrupted != null
                || !EventMembers(_activeEvent).Contains(ResolveNpcId(clicked))
                || string.IsNullOrEmpty(_activeEvent.JoinNode)) return false;
            hostActor = _activeEvent.HostNpcId;
            entryNode = _activeEvent.JoinNode;
            return _host.GetAgent(hostActor) != null;
        }

        private void ReleaseAmbient(Agent agent, string reason = "ambient finished")
        {
            if (agent == null || !_ambientActors.TryGetValue(agent, out var slot)) return;
            _ambientActors.Remove(agent);
            slot.Lease.Release(reason);
        }

        /// <summary>在原版准备真实台词时交接本导演的演员，不清外部动作或其他系统的租约。</summary>
        internal void YieldActorForDialogue(Agent agent)
        {
            if (agent == null || agent == Agent.Main) return;
            ReleaseAmbient(agent, "dialogue handoff");
            // 市井常驻态必须一起收：脑在对话期间**整体不 tick**，留着它 = 定格在半个姿态上过完整场对话。
            StopIdle(agent);
            if (!_eventActors.ContainsKey(agent) || _eventPaused) return;
            // 相关群戏整体暂停保留游标；对话结束沿用既有恢复路径，不能独留搭档继续演。
            _eventPaused = true;
            foreach (var lease in _eventActors.Values) lease.Pause(true);
            DebugLogger.Log($"[StoryProbe] dialogue handoff event={_activeEvent?.Index} actor={agent.Name}");
        }

        /// <summary>收掉某人的市井常驻态（复合动作）。没有就什么都不做。</summary>
        internal static void StopIdle(Agent agent)
        {
            if (agent == null) return;
            var brain = AgentAIController.GetBrainForAgent(agent);
            if (brain != null && brain.IsCurrentOrPending<StreetIdleAction>())
                brain.ClearAllActions(lockPlace: false);
        }

        /// <summary>收掉全部市井常驻态（停演 / 场景收尾）。</summary>
        internal static void StopAllIdle(StoryProbeMissionBehavior host)
        {
            if (host == null) return;
            foreach (var def in StoryProbeDefs.StreetLife)
                StopIdle(host.GetAgent(def.NpcId));
            foreach (var kv in host.AllAgents)
                StopIdle(kv.Value);
        }

        /// <summary>纯读：只有本导演已暂停并仍归该 Brain 所有的同一租约可进入对话。</summary>
        internal bool HasYieldedDialogueActor(Agent agent)
        {
            if (agent == null || !_eventPaused || !_eventActors.TryGetValue(agent, out var lease)) return false;
            return lease.Started && lease.Paused && !lease.Ended
                && AgentAIController.GetBrainForAgent(agent)?.OwnsPerformance(lease) == true;
        }

        private void BeginAmbient(Agent agent, string action)
        {
            if (agent == null || string.IsNullOrEmpty(action) || _eventActors.ContainsKey(agent)) return;
            ReleaseAmbient(agent);
            var lease = PerformanceAction.TryAcquire(agent, "street-ambient-" + Guid.NewGuid().ToString("N"));
            if (lease != null) _ambientActors[agent] = new AmbientSlot { Lease = lease, Action = action, Until = _time + StoryProbeTuning.Current.Performance.ActorReadyTimeoutSec };
        }

        private void TickAmbient(float dt)
        {
            foreach (var kv in _ambientActors.ToList())
            {
                var slot = kv.Value;
                if (slot.Lease.Ended || _time >= slot.Until) { ReleaseAmbient(kv.Key); continue; }
                if (!slot.Played && slot.Lease.CanControl)
                {
                    ForceAction(kv.Key, slot.Action);
                    slot.Played = true;
                    slot.Until = _time + (slot.Duration > 0f ? slot.Duration : StoryProbeTuning.Current.Performance.AmbientPoseDurationSec);
                    if (!string.IsNullOrEmpty(slot.LineKey) && AgentControlHelper.SafeIsActive(slot.Listener))
                        _host.SayOnAgent(kv.Key, slot.LineKey, slot.LineFallback, listener: slot.Listener);
                }
            }
            if (!string.IsNullOrEmpty(_forcedAmbientNpc))
            {
                // 命令强制：只播一次
                if (_forcedAmbientIndex >= 0)
                {
                    var def = StoryProbeDefs.GetStreetLife(_forcedAmbientNpc);
                    var agent = _host.GetAgent(_forcedAmbientNpc);
                    if (def != null && agent != null && _forcedAmbientIndex < def.Actions.Length)
                    {
                        BeginAmbient(agent, def.Actions[_forcedAmbientIndex]);
                        DebugLogger.Log($"[StoryProbe] ambient forced {_forcedAmbientNpc}[{_forcedAmbientIndex}]={def.Actions[_forcedAmbientIndex]}");
                    }
                }
                _forcedAmbientNpc = null;
                _forcedAmbientIndex = -1;
            }

            // 🔴 2026-11-16：日常小动作的节拍**不在这里**了——改由 StreetIdleAction 在脑内一次入队、
            //   内部循环"站 → 小动作 → 站"。原先这里每人每 8~20 秒申请/释放一条演出租约，
            //   每次让脑走一遍 清空→入队→开始→播→结束→清空→入队 Stay 共 7~9 行日志（44% 是填缝）。
            //   本方法现在只负责：强制命令的租约、以及让位给工作点。
            foreach (var def in StoryProbeDefs.StreetLife)
            {
                if (_host.Work?.IsWorkActor(def.NpcId) == true)
                {
                    var worker = _host.GetAgent(def.NpcId);
                    if (worker != null) ReleaseAmbient(worker);
                }
            }
        }

        private void TickReactions(float dt)
        {
            var player = Agent.Main;
            if (player == null) return;

            bool armed = PlayerHasWeaponDrawn();
            bool justDrew = armed && !_playerWasArmed;
            _playerWasArmed = armed;

            foreach (var def in StoryProbeDefs.Npcs)
            {
                var agent = _host.GetAgent(def.Id);
                if (agent == null) continue;
                var brain = AgentAIController.GetBrainForAgent(agent);
                if (brain == null || brain.IsPerforming || brain.IsInCombat || brain.IsStoryIntercepting
                    || brain.IsBound || brain.IsStatusHeld || brain.IsStunned) continue;

                float next;
                if (!_nextGreetingAt.TryGetValue(def.Id, out next)) next = 0f;
                if (_time < next) continue;

                float d = player.Position.Distance(agent.Position);

                var gr = StoryProbeTuning.Current.Greeting;
                if (justDrew && d <= gr.WeaponRadiusMeters)
                {
                    _nextGreetingAt[def.Id] = _time + gr.WeaponCooldownSec;
                    _host.SayOnAgent(agent, "LWN_story_react_weapon_" + def.Id,
                        "Put that away.", SpeechPriority.Warning);
                }
                else if (d <= gr.NearRadiusMeters)
                {
                    _nextGreetingAt[def.Id] = _time + gr.NearCooldownSec;
                    _host.SayOnAgent(agent, "LWN_story_react_near_" + def.Id,
                        "…", SpeechPriority.Chat);
                }
            }
        }

        private float SpeechDelay(float min, float max)
        {
            float lo = Math.Max(1f, min);
            return lo + (float)_rng.NextDouble() * (Math.Max(lo, max) - lo);
        }

        // 发话与姿态分开：工作人物也使用同一数据，但不能抢工作租约、转身或注视玩家。
        private void TickAmbientSpeech()
        {
            var player = Agent.Main;
            if (!AgentControlHelper.SafeIsActive(player) || _time < _nextAmbientSpeechAt) return;
            var definitions = StoryProbeDefs.StreetLife;
            for (int offset = 0; offset < definitions.Count; offset++)
            {
                int defIndex = (_nextSpeechDefIndex + offset) % definitions.Count;
                var def = definitions[defIndex];
                if (def.Speech == null || def.Speech.Count == 0) continue;
                if (_nextSpeechAt.TryGetValue(def.NpcId, out float next) && _time < next) continue;
                var agent = _host.GetAgent(def.NpcId);
                if (!AgentControlHelper.SafeIsActive(agent) || agent.Health <= 0f
                    || agent.Position.Distance(player.Position) > def.SpeechRadiusMeters
                    || _eventActors.ContainsKey(agent) || _ambientActors.ContainsKey(agent)) continue;
                var brain = AgentAIController.GetBrainForAgent(agent);
                if (brain == null || brain.IsInCombat || brain.IsStoryIntercepting || brain.IsBound
                    || brain.IsStatusHeld || brain.IsStunned) continue;
                bool working = _host.Work?.CanSpeakWhileWorking(def.NpcId) == true;
                if (!working && (brain.IsPerforming || !(brain.CurrentAction is StreetIdleAction))) continue;
                var context = new StoryDialogueContext { ActorId = def.NpcId };
                foreach (var rule in def.Speech)
                {
                    if (rule?.LineKeys == null || rule.LineKeys.Length == 0
                        || !StoryDialogueFacts.TryEvaluateAll(rule.Requires, context, out bool allowed, out _, out _)
                        || !allowed) continue;
                    bool selfTalk = string.IsNullOrEmpty(rule.Listener);
                    var listener = selfTalk ? null : _host.ResolveSpeechListener(rule.Listener);
                    if (!selfTalk && (!AgentControlHelper.SafeIsActive(listener) || listener == agent
                        || listener.Health <= 0f || agent.Position.Distance(listener.Position) > def.SpeechRadiusMeters)) continue;
                    _speechCursor.TryGetValue(def.NpcId, out int cursor);
                    int i = cursor % rule.LineKeys.Length;
                    _host.SayOnAgent(agent, rule.LineKeys[i], rule.LineFallbacks != null && i < rule.LineFallbacks.Length ? rule.LineFallbacks[i] : "",
                        listener: listener, selfTalk: selfTalk);
                    _speechCursor[def.NpcId] = cursor + 1;
                    _nextSpeechAt[def.NpcId] = _time + SpeechDelay(def.SpeechGapMinSec, def.SpeechGapMaxSec);
                    _nextAmbientSpeechAt = _time + Math.Max(1f, StoryProbeTuning.Current.Ambient.SpeechGapSec);
                    _nextSpeechDefIndex = (defIndex + 1) % definitions.Count;
                    DebugLogger.Log($"[StoryAmbient] npc={def.NpcId} line={rule.LineKeys[i]} selfTalk={selfTalk} working={working}");
                    return;
                }
            }
        }

        public string CueSpeech(string npcId)
        {
            var def = StoryProbeDefs.GetStreetLife(npcId);
            if (Stopped || def?.Speech == null || def.Speech.Count == 0) return "Error: no active ambient speech for " + npcId;
            _nextSpeechAt[npcId] = 0f;
            _nextAmbientSpeechAt = 0f;
            return "Queued: ambient speech for " + npcId + "; range, state, dialogue and control checks still apply; no movement or story effects.";
        }

        private bool PlayerHasWeaponDrawn()
        {
            try
            {
                var main = Agent.Main;
                if (main == null) return false;
                return !main.WieldedWeapon.IsEmpty;
            }
            catch { return false; }
        }

        // ═══════════════════════════════════════════════════════════════
        // 工具
        // ═══════════════════════════════════════════════════════════════

        private void PlayLoopAction(Agent agent, string[] pool)
        {
            if (agent == null || pool == null || pool.Length == 0) return;
            ForceAction(agent, pool[_rng.Next(pool.Length)]);
        }

        private void ForceAction(Agent agent, string action)
        {
            if (string.IsNullOrEmpty(action) || !OwnsBody(agent)) return;
            try
            {
                var lease = _eventActors.TryGetValue(agent, out var eventLease) ? eventLease
                    : _ambientActors.TryGetValue(agent, out var ambient) ? ambient.Lease : null;
                lease?.PlayAction(action, StoryLayoutRegistry.Published?.PresentationFor(action));
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] action '{action}' failed: {ex.Message}"); }
        }

        private static bool IsConversationActive()
        {
            try { return ConversationEntryPatch.IsConversationActive(); }
            catch { return false; }
        }

        private string ResolveNpcId(Agent agent)
        {
            foreach (var kv in _host.AllAgents)
                if (kv.Value == agent) return kv.Key;
            return null;
        }

        // ── 调试命令 ──

        public string ForceSpeech(string npcId, string listenerId)
        {
            if (Stopped) return "Error: street life is stopped.";
            var def = StoryProbeDefs.GetStreetLife(npcId);
            var agent = _host.GetAgent(npcId);
            var listener = _host.ResolveSpeechListener(listenerId);
            var keys = def?.LineKeys;
            var fallbacks = def?.LineFallbacks;
            // 保留显式听话者的表现调试入口；新台词表按实际剧情条件取句。
            if ((keys == null || keys.Length == 0) && def?.Speech != null)
            {
                var context = new StoryDialogueContext { ActorId = npcId };
                foreach (var rule in def.Speech)
                {
                    if (rule?.LineKeys == null || rule.LineKeys.Length == 0
                        || !StoryDialogueFacts.TryEvaluateAll(rule.Requires, context, out bool allowed, out _, out _)
                        || !allowed) continue;
                    keys = rule.LineKeys;
                    fallbacks = rule.LineFallbacks;
                    break;
                }
            }
            if (def?.Actions == null || def.Actions.Length == 0 || keys == null || keys.Length == 0)
                return "Error: no ambient action/line for " + npcId;
            if (!AgentControlHelper.SafeIsActive(agent) || !AgentControlHelper.SafeIsActive(listener)
                || agent == listener || listener.Health <= 0f)
                return "Error: speaker/listener unavailable or identical.";
            if (_eventActors.ContainsKey(agent) || _host.Work?.IsWorkActor(npcId) == true)
                return "Error: actor belongs to a group or work performance.";
            BeginAmbient(agent, def.Actions[0]);
            if (!_ambientActors.TryGetValue(agent, out var slot)) return "Error: performance rejected by Brain.";
            slot.LineKey = keys[0];
            slot.LineFallback = fallbacks?.Length > 0 ? fallbacks[0] : "";
            slot.Listener = listener;
            slot.Duration = StoryProbeTuning.Current.Performance.DebugSpeechDurationSec;
            return "Queued: speaker=" + npcId + " listener=" + listenerId + " line=" + slot.LineKey
                + " holdSeconds=" + slot.Duration + "; use life facing to confirm actual control.";
        }

        public string ReleaseSpeech(string npcId)
        {
            var agent = _host.GetAgent(npcId);
            if (agent == null || !_ambientActors.ContainsKey(agent)) return "OK: no ambient lease for " + npcId;
            ReleaseAmbient(agent);
            return "OK: ambient lease released for " + npcId;
        }

        public void StopAll()
        {
            Stopped = true;
            EndEvent("stopped");
            foreach (var actor in _ambientActors.Keys.ToList()) ReleaseAmbient(actor);
            StopAllIdle(_host);
            _forcedAmbientNpc = null;
            _forcedAmbientIndex = -1;
            DebugLogger.Log("[StoryProbe] street life stopped");
        }

        public void DisposeManaged()
        {
            Stopped = true;
            _eventOwner = null;
            foreach (var lease in _eventActors.Values.ToList()) lease.InvalidateManaged();
            foreach (var slot in _ambientActors.Values.ToList()) slot.Lease.InvalidateManaged();
            _eventActors.Clear();
            _ambientActors.Clear();
            _activeEvent = null;
            _forcedAmbientNpc = null;
        }

        public void ResumeAll()
        {
            Stopped = false;
            _nextEventAt = _time + StoryProbeTuning.Current.Events.ResumeGapSec;
            DebugLogger.Log("[StoryProbe] street life resumed");
        }

        public void ForceAmbient(string npcId, int index)
        {
            _forcedAmbientNpc = npcId;
            _forcedAmbientIndex = index;
        }

        public string Describe()
        {
            string evt = _activeEvent == null ? "-" : $"#{_activeEvent.Index}{( _eventSkit ? "(skit)" : "")}{(_eventPaused ? "(paused)" : "")}";
            return $"time={_time:F1} stopped={Stopped} event={evt} prepared={_eventPrepared} " +
                   $"placement={_activeEvent?.Placement ?? "-"} " +
                   $"interrupted={(_eventInterrupted ?? "-")} actors={_eventActors.Count} groups={_events.Count} " +
                   $"join=player-F nextEventIn={Math.Max(0f, _nextEventAt - _time):F1}s played=[{string.Join(",", _playedEvents)}]";
        }

        public string ListDefs()
        {
            var lines = new List<string>();
            foreach (var e in StoryProbeDefs.Events)
                lines.Add($"event {e.Index}: {e.ActorA}{(e.TwoPerson ? "+" + e.ActorB : "")} host={e.HostNpcId} node={e.JoinNode} placement={e.Placement}");
            foreach (var s in StoryProbeDefs.StreetLife)
                lines.Add($"ambient {s.NpcId}: {string.Join("|", s.Actions)} speechRules={s.Speech?.Count ?? 0} radius={s.SpeechRadiusMeters:F1}m nextSpeech={(_nextSpeechAt.TryGetValue(s.NpcId, out float next) ? Math.Max(0f, next - _time) : 0f):F1}s");
            return string.Join(" ; ", lines);
        }
    }

    /// <summary>
    /// **市井常驻态（复合行为，2026-11-16 用户裁定）**：把"站着 → 偶尔做个小动作 → 站着"
    /// 收进**一个长生命周期动作**，一次入队能待几分钟。
    ///
    /// 为什么不再用「StayAction + 每次小动作申请一条 PerformanceAction 租约」：
    ///   · 那样每换一次姿态就是 清空 → 入队 → 开始 → 播 → 结束 → 清空 → 入队 Stay → 开始 共 7~9 行日志，
    ///     而其中只有"播动画"那一行有物理意义（实测 20 秒 43 次入队，44% 是填缝动作）；
    ///   · 复合之后，队列只在这一态**开始时**动一次，之后每次换姿态只留 `[ForcePlayAction]` 一行 ——
    ///     也就是用户要保留的"真实切换"。
    ///
    /// 边界（照 StayAction / FollowAgentAction 的既有模式）：
    ///   · **不申请演出租约** ⇒ `brain.IsPerforming` 保持 false，所有"演出中不选人/不对话/不摸门"的判据不受影响；
    ///   · 永不自己结束（同 StayAction）；外部要身体时走 `ClearAllActions` / <see cref="StreetLifeDirector.StopIdle"/>；
    ///   · 战斗中脑整体不跑（战斗场景由原生 AI 接管），本动作自然跟着停。
    /// </summary>
    public sealed class StreetIdleAction : IAtomicAction
    {
        private readonly string _npcId;
        private readonly string[] _actions;
        private readonly string[] _lineKeys;
        private readonly string[] _lineFallbacks;
        private readonly string _listenerId;
        private readonly Vec2? _dutyDir;
        private readonly System.Random _rng = new System.Random();

        private float _t;
        private float _nextPoseAt;
        private float _poseEndsAt = -1f;
        private float _holdAt;
        private string _currentPose;
        private Vec3 _stayPos;
        private bool _bodyHeld;
        private bool _interrupted;

        public StreetIdleAction(string npcId, StreetLifeDef def, Vec2? dutyDir = null)
        {
            _npcId = npcId;
            _actions = def?.Actions ?? new string[0];
            _lineKeys = def?.LineKeys ?? new string[0];
            _lineFallbacks = def?.LineFallbacks ?? new string[0];
            _listenerId = def?.Listener;
            _dutyDir = dutyDir;
        }

        public string NpcId => _npcId;

        /// <summary>当前正在播的姿态动作名（没在播 = null）。HUD 用它在"意图"那行显示此刻在做什么。</summary>
        public string CurrentPose => _poseEndsAt >= 0f ? _currentPose : null;

        public string GetNarration(Agent owner) => null;
        public void RequestInterrupt() => _interrupted = true;

        public void OnStart(Agent agent)
        {
            _t = 0f;
            _poseEndsAt = -1f;
            _bodyHeld = false;
            _stayPos = agent.Position;
            // 接回日常时释放上个动作/原版会话留下的对象注视；其引用不会随距离自动失效。
            AgentControlHelper.StopLooking(agent);
            var amb = StoryProbeTuning.Current.Ambient;
            float lo = Math.Max(0.5f, amb.FirstDelayMinSec);
            float hi = Math.Max(lo, amb.FirstDelayMaxSec);
            _nextPoseAt = lo + (float)_rng.NextDouble() * (hi - lo);

            // 职责点朝向（绝对角度）先摆正；-1 就是 −1°（359°），不是特殊值。
            // 玩家靠近**不转身**——那是通用感知层（NpcPerception）的事：只有听到脚步声才回头。
            if (_dutyDir.HasValue)
            {
                Vec2 want = _dutyDir.Value;
                Vec2 look = agent.Frame.rotation.f.AsVec2;
                float dot = look.LengthSquared > 1e-4f && want.LengthSquared > 1e-4f
                    ? Vec2.DotProduct(look.Normalized(), want.Normalized()) : 1f;
                if (dot < 0.96f) AgentControlHelper.SetBodyDirection(agent, want);
            }
        }

        public void OnTick(Agent agent, float dt)
        {
            _t += dt;

            // 钉在原地：与 PerformanceAction.HoldBody 同一套（清移动输入 + 速度上限 0），
            // 不用 StayAction 的 SetScriptedPosition —— 姿态动画在那个合同下会被引擎压掉。
            if (_t >= _holdAt)
            {
                _holdAt = _t + 0.2f;
                AgentControlHelper.HoldInPlace(agent, resetScriptedMovement: !_bodyHeld);
                _bodyHeld = true;
            }

            // 「听到声音转头 / 看到人注视」由通用感知层 NpcPerception 统一处理，这里不再自己判玩家距离。

            if (_poseEndsAt >= 0f)
            {
                if (_t < _poseEndsAt) return;
                AgentControlHelper.ClearPose(agent);
                _poseEndsAt = -1f;
                var amb = StoryProbeTuning.Current.Ambient;
                float lo = Math.Max(1f, amb.GapMinSec);
                float hi = Math.Max(lo, amb.GapMaxSec);
                _nextPoseAt = _t + lo + (float)_rng.NextDouble() * (hi - lo);
                return;
            }

            if (_t < _nextPoseAt) return;
            PlayPose(agent);
        }

        private void PlayPose(Agent agent)
        {
            if (_actions.Length == 0) { _nextPoseAt = _t + 10f; return; }
            string action = _actions[_rng.Next(_actions.Length)];
            _currentPose = action;
            AgentControlHelper.ForcePlayAction(agent, action);
            _poseEndsAt = _t + Math.Max(0.5f, StoryProbeTuning.Current.Performance.AmbientPoseDurationSec);
            if (StoryPoseTrace.IsOn(_npcId)) StoryPoseTrace.Anim(agent, action, "StreetIdleAction");

            if (_lineKeys.Length > 0 && _rng.NextDouble() < 0.35)
            {
                var host = StoryProbeMissionBehavior.Current;
                if (host == null) return;
                int i = _rng.Next(_lineKeys.Length);
                string fb = i < _lineFallbacks.Length ? _lineFallbacks[i] : "";
                var listener = host.ResolveSpeechListener(_listenerId);
                if (AgentControlHelper.SafeIsActive(listener) && listener != agent && listener.Health > 0f
                    && agent.Position.Distance(listener.Position) <= StoryProbeTuning.Current.Noise.GazeRadiusMeters)
                    host.SayOnAgent(agent, _lineKeys[i], fb, listener: listener);
            }
        }

        public bool IsFinished(Agent agent)
        {
            if (_interrupted) return true;
            if (agent == null || !AgentControlHelper.SafeIsActive(agent) || agent.Health <= 0f) return true;
            if (AgentBrain.IsRoped(agent) || AgentBrain.IsHeldByStatus(agent) || AgentBrain.IsKnockedOut(agent)) return true;
            // 被人推到别处（推搡/绕行）：结束这一态，让默认行为把人走回岗位，别在原地硬撑。
            if (agent.Position.Distance(_stayPos) > 3f) return true;
            var host = StoryProbeMissionBehavior.Current;
            if (host == null || !host.Active || host.Director == null || host.Director.Stopped) return true;
            return false;
        }

        public void OnEnd(Agent agent)
        {
            try { AgentControlHelper.ClearPose(agent); } catch (Exception) { }
            try { agent?.SetLookAgent(null); } catch (Exception) { }
        }
    }
}
