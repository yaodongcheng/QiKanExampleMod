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
    ///      动作循环、头顶「!」，不会自己散场也不会自己结束；玩家走近 4 m 才暂停并接入对话；
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
        private bool _joinArmed = true;   // 走近可接入的闸门（谈完一次要离开 6 m 才重新武装）
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
        private readonly Dictionary<string, float> _nextAmbientAt = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _nextGreetingAt = new Dictionary<string, float>();
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
            _events = LoadEvents();

            foreach (var def in StoryProbeDefs.Npcs)
            {
                _nextAmbientAt[def.Id] = t.Ambient.FirstDelayMinSec
                                       + (float)_rng.NextDouble() * (t.Ambient.FirstDelayMaxSec - t.Ambient.FirstDelayMinSec);
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
            _joinArmed = true;
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
            _eventPrepared = true;
            PlayLoopAction(a, _activeEvent.ActionA);
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

            // ① 玩家走近 4 m → 暂停 + 自动开对话（只有真事件，桥段不接）
            float nearestNow = NearestParticipantDistance();
            if (!_joinArmed && nearestNow > StoryProbeTuning.Current.Events.JoinArmDistanceMeters) _joinArmed = true;   // 走远了才允许再次接入

            if (!_eventPaused && !inConversation && player != null && _joinArmed)
            {
                float nearest = nearestNow;
                if (nearest <= StoryProbeTuning.Current.Events.JoinDistanceMeters)
                {
                    if (_eventSkit)
                    {
                        // 桥段：玩家走近就让开，不接入
                        EndEvent("skit-interrupted");
                        return;
                    }
                    _eventPaused = true;
                    foreach (var lease in _eventActors.Values) lease.Pause(true);
                    _joinArmed = false;
                    var host = _host.GetAgent(_activeEvent.HostNpcId);
                    DebugLogger.Log($"[StoryProbe] event #{_activeEvent.Index} pause (player {nearest:F1} m) -> join dialogue");
                    if (host != null) _host.StartStoryConversation(host, null, "event_join");
                    return;
                }
            }

            // ② 暂停中：等对话结束 → **回到循环**（§6.2：选【走开】不散场，谈过一场也继续演）
            //    重新接入的门槛 = 先离开 6 m（_joinArmed），免得刚关窗又被拉进对话
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
                    _joinArmed = false;
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
            if (!_eventActors.ContainsKey(agent) || _eventPaused) return;
            // 相关群戏整体暂停保留游标；对话结束沿用既有恢复路径，不能独留搭档继续演。
            _eventPaused = true;
            foreach (var lease in _eventActors.Values) lease.Pause(true);
            DebugLogger.Log($"[StoryProbe] dialogue handoff event={_activeEvent?.Index} actor={agent.Name}");
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

            foreach (var def in StoryProbeDefs.StreetLife)
            {
                if (_activeEvent != null && IsEventParticipant(def.NpcId))
                    continue;

                // P4：工作点上的 NPC 由 StoryWorkDirector 接管；这里不再给常驻姿态，避免两边抢身体。
                if (_host.Work?.IsWorkActor(def.NpcId) == true)
                {
                    var worker = _host.GetAgent(def.NpcId);
                    if (worker != null) ReleaseAmbient(worker);
                    continue;
                }

                float next;
                if (!_nextAmbientAt.TryGetValue(def.NpcId, out next)) next = _time;
                if (_time < next) continue;

                var amb = StoryProbeTuning.Current.Ambient;
                _nextAmbientAt[def.NpcId] = _time + amb.GapMinSec + (float)_rng.NextDouble() * (amb.GapMaxSec - amb.GapMinSec);

                var agent = _host.GetAgent(def.NpcId);
                if (agent == null) continue;

                if (_ambientActors.ContainsKey(agent) || def.Actions == null || def.Actions.Length == 0) continue;
                BeginAmbient(agent, def.Actions[_rng.Next(def.Actions.Length)]);
                if (!_ambientActors.ContainsKey(agent)) continue;

                if (def.LineKeys != null && def.LineKeys.Length > 0 && _rng.NextDouble() < 0.35)
                {
                    int i = _rng.Next(def.LineKeys.Length);
                    string fb = def.LineFallbacks != null && i < def.LineFallbacks.Length ? def.LineFallbacks[i] : "";
                    _ambientActors[agent].LineKey = def.LineKeys[i];
                    _ambientActors[agent].LineFallback = fb;
                    _ambientActors[agent].Listener = _host.ResolveSpeechListener(def.Listener);
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
            try { AgentControlHelper.ForcePlayAction(agent, action); }
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
            if (def?.Actions == null || def.Actions.Length == 0 || def.LineKeys == null || def.LineKeys.Length == 0)
                return "Error: no ambient action/line for " + npcId;
            if (!AgentControlHelper.SafeIsActive(agent) || !AgentControlHelper.SafeIsActive(listener)
                || agent == listener || listener.Health <= 0f)
                return "Error: speaker/listener unavailable or identical.";
            if (_eventActors.ContainsKey(agent) || _host.Work?.IsWorkActor(npcId) == true)
                return "Error: actor belongs to a group or work performance.";
            BeginAmbient(agent, def.Actions[0]);
            if (!_ambientActors.TryGetValue(agent, out var slot)) return "Error: performance rejected by Brain.";
            slot.LineKey = def.LineKeys[0];
            slot.LineFallback = def.LineFallbacks?.Length > 0 ? def.LineFallbacks[0] : "";
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
                   $"interrupted={(_eventInterrupted ?? "-")} actors={_eventActors.Count} groups={_events.Count} " +
                   $"nextEventIn={Math.Max(0f, _nextEventAt - _time):F1}s played=[{string.Join(",", _playedEvents)}]";
        }

        public string ListDefs()
        {
            var lines = new List<string>();
            foreach (var e in StoryProbeDefs.Events)
                lines.Add($"event {e.Index}: {e.ActorA}{(e.TwoPerson ? "+" + e.ActorB : "")} host={e.HostNpcId} node={e.JoinNode}");
            foreach (var s in StoryProbeDefs.StreetLife)
                lines.Add($"ambient {s.NpcId}: {string.Join("|", s.Actions)}");
            return string.Join(" ; ", lines);
        }
    }
}
