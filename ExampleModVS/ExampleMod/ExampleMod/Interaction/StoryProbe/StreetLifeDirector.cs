using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 市井导演（2026-11-12，plans/逆水寒511玩法验证-城池剧情任务.md §6 / §7）。
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
        }
        private readonly Dictionary<Agent, AmbientSlot> _ambientActors = new Dictionary<Agent, AmbientSlot>();

        // ── 常驻 / 桥段 / 反应 ──
        private readonly Dictionary<string, float> _nextAmbientAt = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _nextGreetingAt = new Dictionary<string, float>();
        private float _nextSkitAt;
        private bool _playerWasArmed;

        // ── 调试 ──
        public bool Stopped { get; private set; }
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
            return id == _activeEvent.ActorA || (_activeEvent.TwoPerson && id == _activeEvent.ActorB);
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
            // 依次轮转 1..6；都演过一遍后重新轮（不锁死、没有"错过"）
            if (_playedEvents.Count >= StoryProbeDefs.Events.Count)
            {
                _playedEvents.Clear();
                _nextEventIndex = 1;
            }

            var def = StoryProbeDefs.GetEvent(_nextEventIndex) ?? StoryProbeDefs.Events[0];
            _nextEventIndex = def.Index % StoryProbeDefs.Events.Count + 1;
            StartEvent(def, skit: false);
        }

        private void StartEvent(StoryEventDef def, bool skit)
        {
            var a = _host.GetAgent(def.ActorA);
            if (a == null) { _nextEventAt = _time + StoryProbeTuning.Current.Events.RetryGapSec; return; }

            var b = def.TwoPerson ? _host.GetAgent(def.ActorB) : null;
            if (def.TwoPerson && b == null) { _nextEventAt = _time + StoryProbeTuning.Current.Events.RetryGapSec; return; }

            if (IsConversationActive()) { _nextEventAt = _time + StoryProbeTuning.Current.Events.RetryGapSec; return; }
            ReleaseAmbient(a);
            if (b != null) ReleaseAmbient(b);
            string session = "street-event-" + Guid.NewGuid().ToString("N");
            var accepted = new Dictionary<Agent, PerformanceAction>();
            foreach (var actor in b == null ? new[] { a } : new[] { a, b })
            {
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

            DebugLogger.Log($"[StoryProbe] event start #{def.Index} skit={skit} actors={def.ActorA}{(def.TwoPerson ? "+" + def.ActorB : "")}");
        }

        private bool OwnsBody(Agent actor)
        {
            if (actor == null) return false;
            if (_eventActors.TryGetValue(actor, out var eventLease) && eventLease.CanControl) return true;
            return _ambientActors.TryGetValue(actor, out var ambient) && ambient.Lease.CanControl;
        }

        private void PrepareEventActors()
        {
            var a = _host.GetAgent(_activeEvent.ActorA);
            var b = _activeEvent.TwoPerson ? _host.GetAgent(_activeEvent.ActorB) : null;
            if (!OwnsBody(a) || (_activeEvent.TwoPerson && !OwnsBody(b))) return;
            if (b != null)
            {
                if (a.Position.Distance(b.Position) > StoryProbeTuning.Current.Events.ActorSpacingMeters)
                {
                    Vec3 dir = b.Position - a.Position;
                    dir.z = 0f;
                    if (dir.LengthSquared < 0.01f) dir = new Vec3(0, 1, 0);
                    b.TeleportToPosition(a.Position + dir.NormalizedCopy() * StoryProbeTuning.Current.Events.ActorApproachMeters);
                }
                AgentControlHelper.FaceToActor(a, b);
                AgentControlHelper.FaceToActor(b, a);
            }
            _eventPrepared = true;
            PlayLoopAction(a, _activeEvent.ActionA);
            if (b != null) PlayLoopAction(b, _activeEvent.ActionB);
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
                    if (host != null) _host.StartStoryConversation(host, _activeEvent.JoinNode);
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
                var a = _host.GetAgent(_activeEvent.ActorA);
                var b = _activeEvent.TwoPerson ? _host.GetAgent(_activeEvent.ActorB) : null;
                PlayLoopAction(a, _activeEvent.ActionA);
                if (b != null) PlayLoopAction(b, _activeEvent.ActionB);
            }

            // 单人事件：一直重复发愁的动作（§6.1 硬规则 1）
            if (!_activeEvent.TwoPerson && _rng.NextDouble() < dt * StoryProbeTuning.Current.Events.SoloActionChancePerSec)
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
            if (_activeEvent.TwoPerson) actorId = (i % 2 == 0) ? _activeEvent.ActorA : _activeEvent.ActorB;
            else actorId = _activeEvent.ActorA;

            var agent = _host.GetAgent(actorId);
            if (agent == null || !OwnsBody(agent)) return;

            string fb = fallbacks != null && i < fallbacks.Length ? fallbacks[i] : "";
            _host.SayOnAgent(agent, keys[i], fb,
                _eventSkit ? SpeechPriority.Chat : SpeechPriority.Warning);
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
            var def = StoryProbeDefs.GetEvent(index);
            if (def == null) return;
            EndEvent("forced");
            StartEvent(def, skit: false);
        }

        private float NearestParticipantDistance()
        {
            var player = Agent.Main;
            if (player == null || _activeEvent == null) return float.MaxValue;
            float best = float.MaxValue;
            var a = _host.GetAgent(_activeEvent.ActorA);
            if (a != null) best = Math.Min(best, player.Position.Distance(a.Position));
            if (_activeEvent.TwoPerson)
            {
                var b = _host.GetAgent(_activeEvent.ActorB);
                if (b != null) best = Math.Min(best, player.Position.Distance(b.Position));
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

        private void ReleaseAmbient(Agent agent)
        {
            if (agent == null || !_ambientActors.TryGetValue(agent, out var slot)) return;
            _ambientActors.Remove(agent);
            slot.Lease.Release("ambient finished");
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
                    slot.Until = _time + StoryProbeTuning.Current.Performance.AmbientPoseDurationSec;
                    if (!string.IsNullOrEmpty(slot.LineKey)) _host.SayOnAgent(kv.Key, slot.LineKey, slot.LineFallback);
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
                if (_activeEvent != null && (def.NpcId == _activeEvent.ActorA || def.NpcId == _activeEvent.ActorB))
                    continue;

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
            return $"time={_time:F1} stopped={Stopped} event={evt} nextEventIn={Math.Max(0f, _nextEventAt - _time):F1}s " +
                   $"played=[{string.Join(",", _playedEvents)}]";
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
