using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.SaveSystem;

namespace LivingWorldNpcs
{
    /// <summary>内容只给地点、道具、台词；完成信号来自真实扒窃与钩索管线。</summary>
    public sealed class StoryPracticeDef
    {
        public string Id, Mode, Actor, TitleKey, FollowKey, ReadyKey, RetryKey, DoneKey, CancelKey;
        public string Item, DoneFlag, FollowObjectiveKey, PracticeObjectiveKey;
        public string[] Kit = Array.Empty<string>();
        public float[] Position, Aim;
        public float FacingDegrees, SightMeters = 5f, TimeoutSec = 240f, AimTolerance = 2f;
    }

    /// <summary>特殊任务沿用主线日记的原生登记方式，不占用老师现有的委托Issue。</summary>
    public sealed class StoryPracticeQuest : QuestBase
    {
        [SaveableField(1)] private string _lesson;
        [SaveableField(2)] private string _title;
        [SaveableField(3)] private string _objective;
        [SaveableField(4)] private string _completionKey;
        public string LessonId => _lesson;
        public StoryPracticeQuest(string lesson, string title, string objective)
            : base("lwn_practice_" + lesson, null, CampaignTime.Never, 0)
        { _lesson = lesson; _title = title; _objective = objective; }
#if MB2_GE_130
        public override string SpecialQuestType => "lwn_practice";
#else
        public override bool IsSpecialQuest => true;
#endif
        public override bool IsRemainingTimeHidden => true;
        public override TextObject Title => new TextObject(LWNTextHelper.ResolveText(_title, "Practice"));
        protected override void SetDialogs() { }
        protected override void InitializeQuestOnGameLoad() { }
        protected override void HourlyTick() { }
        protected override void RegisterEvents() { }
        protected override void OnTimedOut() { }
        public override void OnFailed() { }
        protected override void OnStartQuest() => Note(_objective);
        public void Note(string key)
        {
            _objective = key;
            AddLog(new TextObject(LWNTextHelper.ResolveText(key, "Practice")));
        }
        public void Finish(string key) { if (!IsFinalized) { Note(key); CompleteQuestWithSuccess(); } }
        public void CommitCompletion(string key) { _completionKey = key; }
        public void Cancel()
        {
            if (IsFinalized) return;
            if (!string.IsNullOrEmpty(_completionKey)) Finish(_completionKey);
            else CompleteQuestWithCancel();
        }
        public static void CancelAll()
        {
            foreach (var q in Campaign.Current?.QuestManager?.Quests.OfType<StoryPracticeQuest>()
                .Where(q => !q.IsFinalized).ToList() ?? new List<StoryPracticeQuest>()) q.Cancel();
        }
    }

    public sealed class StoryPracticeRunner
    {
        private readonly StoryProbeMissionBehavior _host;
        private Dictionary<string, StoryPracticeDef> _defs;
        public string Revision { get; private set; }
        private StoryPracticeDef _def;
        private StoryPracticeQuest _quest;
        private Agent _teacher, _player;
        private Mission _mission;
        private int _token, _serial;
        private Vec3 _home, _point, _aim;
        private Vec2 _homeFacing;
        private float _until, _nextRetry;
        private string _phase;
        private bool _arrived, _moveFailed, _approached;
        private PerformanceAction _lease;
        private StoryMoveAction _move, _returnMove;
        private bool _returnInProgress;
        private GrappleLogic _demo, _playerGrapple;
        private int _demoReceipt, _playerReceipt;
        private EquipmentIndex _practiceSlot = EquipmentIndex.None;
        private readonly Dictionary<EquipmentIndex, EquipmentElement> _temporary = new Dictionary<EquipmentIndex, EquipmentElement>();
        public bool Active => _def != null;
        public string Actor => _def?.Actor;
        public string LessonId => _def?.Id;
        private bool _returnPending;
        private float _phaseUntil;
        public string ObjectiveKey => !Active ? null : _phase == "follow" || _phase == "pending"
            ? _def.FollowObjectiveKey : _def.PracticeObjectiveKey;
        public StoryPracticeRunner(StoryProbeMissionBehavior host)
        {
            _host = host;
            Reload();
        }
        public string Reload()
        {
            string json = File.ReadAllText(Path.Combine(StoryProbeDataFiles.DesignDataFolder, "StoryLessons.json"));
            JToken.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            var list = JsonConvert.DeserializeObject<List<StoryPracticeDef>>(json,
                new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
            if (list == null || list.Count == 0 || list.Any(d => !Valid(d)
                || StoryProbeDefs.GetNpc(d.Actor) == null
                || (d.Mode == "pickpocket" ? new[] { d.Item } : d.Kit).Any(id => StoryProbeDefs.FindItem(id) == null))
                || list.Select(d => d.Id).Distinct().Count() != list.Count)
                throw new InvalidDataException("Invalid StoryLessons definition");
            using (var hash = SHA256.Create())
                Revision = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json))).Replace("-", "").Substring(0, 12);
            _defs = list.ToDictionary(d => d.Id, StringComparer.Ordinal);
            return "OK: lessons revision=" + Revision + " definitions=" + _defs.Count
                + " active=" + Active + "; active exercise keeps its original definition; new starts use this revision.";
        }

        private static bool Valid(StoryPracticeDef d) => d != null && !string.IsNullOrEmpty(d.Id)
            && (d.Mode == "pickpocket" || d.Mode == "grapple") && !string.IsNullOrEmpty(d.Actor)
            && d.Position?.Length == 3 && d.Position.All(Finite)
            && (d.Mode != "grapple" || d.Aim?.Length == 3 && d.Aim.All(Finite) && d.Kit?.Length == 2 && d.Kit.All(i => !string.IsNullOrEmpty(i)) && d.Kit.Distinct().Count() == 2)
            && (d.Mode != "pickpocket" || !string.IsNullOrEmpty(d.Item))
            && new[] { d.TitleKey, d.FollowKey, d.ReadyKey, d.RetryKey, d.DoneKey, d.CancelKey, d.DoneFlag, d.FollowObjectiveKey, d.PracticeObjectiveKey }.All(s => !string.IsNullOrEmpty(s))
            && Finite(d.FacingDegrees) && Math.Abs(d.FacingDegrees) <= 360 && Finite(d.SightMeters) && d.SightMeters > 0 && d.SightMeters <= 20
            && Finite(d.TimeoutSec) && d.TimeoutSec >= 30 && d.TimeoutSec <= 900
            && Finite(d.AimTolerance) && d.AimTolerance > 0 && d.AimTolerance <= 5;
        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        private static Vec3 V(float[] p) => new Vec3(p[0], p[1], p[2]);
        public bool CanStart(string id, out string why)
        {
            why = null;
            if (Active || !_host.Active || !_defs.TryGetValue(id ?? "", out var d))
            { why = "practice unavailable or already active"; return false; }
            if (StoryProbeState.HasAsked(d.Actor, d.DoneFlag)) { why = "practice already completed"; return false; }
            var teacher = _host.GetAgent(d.Actor);
            var brain = AgentAIController.GetBrainForAgent(teacher);
            if (GrappleLogic.GetSession(teacher)?.IsSessionBusy == true) { why = "teacher using grapple"; return false; }
            if (!AgentControlHelper.SafeIsActive(teacher) || brain == null || brain.IsInCombat || brain.IsBound
                || brain.IsStatusHeld || brain.IsStunned || _host.Director?.IsEventParticipant(d.Actor) == true)
            { why = "teacher unavailable"; return false; }
            if (Campaign.Current?.QuestManager == null || !AgentControlHelper.SafeIsActive(Agent.Main))
            { why = "player or journal unavailable"; return false; }
            var items = d.Mode == "pickpocket" ? new[] { d.Item } : d.Kit;
            int empty = Enumerable.Range(0, 4).Count(i => teacher.SpawnEquipment[(EquipmentIndex)i].IsEmpty);
            if (items.Any(i => StoryProbeDefs.FindItem(i) == null) || empty < items.Length)
            { why = "practice equipment unavailable"; return false; }
            return true;
        }
        public bool Start(string id, out string why)
        {
            if (!CanStart(id, out why)) return false;
            var previousTeacher = _teacher;
            _def = _defs[id]; _teacher = _host.GetAgent(_def.Actor); _player = Agent.Main;
            _mission = Mission.Current; _token = StoryProbeState.RuntimeToken; _serial++;
            if ((!_returnPending && !_returnInProgress) || previousTeacher != _teacher)
            { _home = _teacher.Position; _homeFacing = _teacher.Frame.rotation.f.AsVec2; }
            _returnMove?.RequestInterrupt(); _returnMove = null;
            _returnInProgress = _returnPending = false;
            _point = V(_def.Position); _aim = _def.Mode == "grapple" ? V(_def.Aim) : Vec3.Zero;
            _until = _host.Clock + _def.TimeoutSec; _phase = "pending";
            // Old saved in-scene exercises resume by accepting again, never with a stale Agent/permission.
            StoryPracticeQuest.CancelAll();
            _quest = new StoryPracticeQuest(_def.Id, _def.TitleKey, _def.FollowKey);
            _quest.StartQuest(); StoryGoalHudMissionView.RequestFlash = true;
            return true;
        }
        public void Tick(float dt)
        {
            if (!Active)
            {
                if (_returnPending && !ConversationEntryPatch.IsConversationActive())
                {
                    _returnPending = false;
                    var teacher = _teacher; var mission = _mission; int token = _token; int serial = _serial;
                    if (_host.Active && mission == Mission.Current && token == StoryProbeState.RuntimeToken
                        && AgentControlHelper.SafeIsActive(teacher))
                        {
                        _returnInProgress = true;
                        AgentAIController.Instance?.SendEventToAgent(teacher, "event_story_move", _home, 0.5f,
                            new Action<bool>(ok => {
                                if (serial != _serial) return;
                                _returnInProgress = false; _returnMove = null;
                                if (ok && !Active && mission == Mission.Current && token == StoryProbeState.RuntimeToken)
                                    AgentControlHelper.SetBodyDirection(teacher, _homeFacing);
                            }), new Action<StoryMoveAction>(move => { if (serial == _serial) _returnMove = move; }), 90f);
                        }
                }
                return;
            }
            var brain = AgentAIController.GetBrainForAgent(_teacher);
            if (_mission != Mission.Current || _token != StoryProbeState.RuntimeToken || !_host.Active
                || Agent.Main != _player || !AgentControlHelper.SafeIsActive(_player) || _player.Health <= 0
                || !AgentControlHelper.SafeIsActive(_teacher) || _teacher.Health <= 0 || brain == null
                || brain.IsInCombat || brain.IsBound || brain.IsStatusHeld || brain.IsStunned
                || _host.Director?.IsEventParticipant(Actor) == true || _host.Clock > _until)
            { Stop("interrupted"); return; }
            if (ConversationEntryPatch.IsConversationActive())
            {
                if (_phase != "pending") _lease?.Pause(true);
                return;
            }
            if (_phase == "pending")
            {
                _host.Work?.YieldActor(Actor, "practice");
                _host.Director?.YieldActorForDialogue(_teacher);
                // Only unused weapon slots are reserved; existing possessions are never overwritten.
                var eq = _teacher.SpawnEquipment.Clone();
                if (Enumerable.Range(0, 4).Count(i => eq[(EquipmentIndex)i].IsEmpty) < (_def.Mode == "pickpocket" ? 1 : 2))
                { Stop("equipment slots changed"); return; }
                foreach (string id in _def.Mode == "pickpocket" ? new[] { _def.Item } : _def.Kit)
                {
                    var slot = Enumerable.Range(0, 4).Select(i => (EquipmentIndex)i).First(i => eq[i].IsEmpty);
                    var element = new EquipmentElement(StoryProbeDefs.FindItem(id));
                    eq[slot] = element; _temporary[slot] = element;
                    if (_def.Mode == "pickpocket") _practiceSlot = slot;
                }
                _teacher.UpdateSpawnEquipmentAndRefreshVisuals(eq);
                _teacher.UpdateAgentStats();
                _phase = "follow"; Say(_def.FollowKey);
                var def = _def; int serial = _serial;
                AgentAIController.Instance?.SendEventToAgent(_teacher, "event_story_move", _point, 0.5f,
                    new Action<bool>(ok => { if (_def == def && serial == _serial) { _arrived = ok; _moveFailed = !ok; } }),
                    new Action<StoryMoveAction>(move => { if (_def == def && serial == _serial) _move = move; }), 90f);
                return;
            }
            if (_moveFailed) { Stop("move failed"); return; }
            if (_phase == "follow")
            {
                if (!_arrived) return;
                if (_player.Position.Distance(_point) > 6f)
                {
                    if (_lease == null) _lease = PerformanceAction.TryAcquire(_teacher, "practice:" + _def.Id);
                    if (_lease == null || _lease.Ended) { Stop("waiting lease lost"); return; }
                    _lease.AllowSpeechBodyTurn = false; _lease.AllowSpeechGaze = false;
                    return;
                }
                _move = null;
                if (_def.Mode == "grapple")
                {
                    _lease?.Release("practice demonstration"); _lease = null;
                    _demo = GrappleLogic.EnsureFor(_teacher);
                    _demoReceipt = _demo.WorldHitReceipt;
                    if (!_demo.BeginNpcThrow(_aim, false, false, out string why))
                    { DebugLogger.Log("[StoryPractice] demo rejected: " + why); Stop("demo unavailable"); return; }
                    _phase = "demo"; _phaseUntil = _host.Clock + 15f;
                }
                else _phase = "lease";
                return;
            }
            if (_phase == "demo")
            {
                if (_host.Clock > _phaseUntil) { Stop("demo timeout"); return; }
                if (_demo.WorldHitReceipt <= _demoReceipt) return;
                if (_demo.LastWorldHit.Distance(_aim) > _def.AimTolerance) { Stop("demo missed target"); return; }
                _demo.Release("practice demo complete"); _demo = null; _phase = "lease";
                return;
            }
            if (_phase == "lease")
            {
                if (_lease == null) _lease = PerformanceAction.TryAcquire(_teacher, "practice:" + _def.Id);
                if (_lease == null || _lease.Ended) { Stop("lease unavailable"); return; }
                if (!_lease.CanControl) return;
                _lease.AllowSpeechBodyTurn = false; _lease.AllowSpeechGaze = false;
                float yaw = _def.FacingDegrees * (float)Math.PI / 180f;
                AgentControlHelper.StopLooking(_teacher);
                AgentControlHelper.SetBodyDirection(_teacher, new Vec2((float)Math.Cos(yaw), (float)Math.Sin(yaw)));
                _phase = "practice"; _nextRetry = _host.Clock + 2f;
                _quest.Note(_def.ReadyKey); Say(_def.ReadyKey); StoryGoalHudMissionView.RequestFlash = true;
                if (_def.Mode == "grapple")
                { _playerGrapple = GrappleLogic.Ensure(); _playerReceipt = _playerGrapple.PullFinishedReceipt; }
                return;
            }
            if (_phase != "practice") return;
            if (!_lease.CanControl) { Stop("control lost"); return; }
            if (_def.Mode == "grapple")
            {
                if (GrappleLogic.GetSession(_player) == _playerGrapple && _playerGrapple.PullFinishedReceipt > _playerReceipt)
                {
                    _playerReceipt = _playerGrapple.PullFinishedReceipt;
                    if (_playerGrapple.LastPullAnchor.Distance(_aim) <= _def.AimTolerance) Complete();
                    else Retry();
                }
                return;
            }
            if (StealManager.IsUIOpen || _host.Clock < _nextRetry) return;
            float dist = _teacher.Position.Distance(_player.Position);
            bool seen = NpcSightSystem.CanAgentSeeTarget(_teacher, _player, _def.SightMeters)
                && NpcSightSystem.IsTargetInViewCone(_teacher, _player, useBodyFacing: true);
            bool noisy = dist <= StoryProbeTuning.Current.Noise.OrdinaryFootstepRadiusMeters && !_player.CrouchMode
                && _player.Velocity.Length > StoryProbeTuning.Current.Noise.RunSpeedMps;
            if (seen || noisy) Retry();
            else if (dist <= 2f && _player.CrouchMode) _approached = true;
        }
        private void Retry()
        {
            _approached = false; _nextRetry = _host.Clock + 3f; Say(_def.RetryKey);
            StoryGoalHudMissionView.RequestFlash = true;
        }
        private void Say(string key)
        {
            _host.SayOnAgent(_teacher, key, "", listener: _player);
            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText(key, "Practice")));
        }
        public bool IsConsentingTeacher(Agent target) => Active && target == _teacher && _mission == Mission.Current
            && _token == StoryProbeState.RuntimeToken && _host.Active;
        public bool IsPracticeTarget(Agent target) => Active && _phase == "practice" && _def.Mode == "pickpocket"
            && _mission == Mission.Current && _token == StoryProbeState.RuntimeToken && _host.Clock <= _until && target == _teacher
            && _player == Agent.Main && AgentControlHelper.SafeIsActive(_player) && _player.Health > 0 && _lease?.CanControl == true && _temporary.ContainsKey(_practiceSlot);
        public bool AllowsTake(Agent target, EquipmentIndex slot) => IsPracticeTarget(target) && slot == _practiceSlot
            && _approached && _player.CrouchMode && _player.Position.Distance(_teacher.Position) <= 2f
            && !NpcSightSystem.IsTargetInViewCone(_teacher, _player, useBodyFacing: true)
            && target.SpawnEquipment[slot].Item == _temporary[slot].Item;
        public bool OwnsTemporarySlot(Agent target, EquipmentIndex slot) => Active && target == _teacher
            && _mission == Mission.Current && _token == StoryProbeState.RuntimeToken && _temporary.ContainsKey(slot);
        public EquipmentIndex? PracticeSlot(Agent target) => IsPracticeTarget(target) ? _practiceSlot : (EquipmentIndex?)null;
        public void TakeCommitted(Agent target, EquipmentIndex slot)
        {
            if (!IsPracticeTarget(target) || slot != _practiceSlot || !target.SpawnEquipment[slot].IsEmpty) return;
            // Persist the real completion before waiting for modal cleanup: cancelling or saving
            // in this interval must not grant another temporary dagger on the next attempt.
            StoryProbeState.MarkAsked(_def.Actor, _def.DoneFlag);
            _quest?.CommitCompletion(_def.DoneKey);
            _phase = "taken";
        }
        public bool PracticeAttempt(Agent target, bool hit, EquipmentIndex? slot)
        {
            if (!IsPracticeTarget(target)) return false;
            if (!hit || !slot.HasValue || !AllowsTake(target, slot.Value)) Retry();
            return true;
        }
        public void TickCompletion()
        { if (Active && _phase == "taken" && !StealManager.IsUIOpen) Complete(); }
        private void Complete()
        {
            _quest.Finish(_def.DoneKey);
            StoryProbeState.MarkAsked(_def.Actor, _def.DoneFlag); Say(_def.DoneKey); Stop("completed", completed: true);
        }
        public string Describe() => !Active ? "OK: practice inactive revision=" + Revision : "OK: practice=" + _def.Id + " actor=" + Actor
            + " phase=" + _phase + " approached=" + _approached + " lease=" + (_lease?.CanControl == true);
        public void DisposeManaged()
        {
            _def = null; _serial++; _returnInProgress = _returnPending = false;
            _lease?.InvalidateManaged("practice host removed"); _lease = null;
            _demo = null; _move = _returnMove = null; _playerGrapple = null; _temporary.Clear();
            _quest?.Cancel(); _quest = null;
        }
        public void Stop(string reason, bool completed = false)
        {
            if (!Active) return;
            // A committed transfer is success even when the UI/mission is interrupted before
            // TickCompletion. Normal completion still waits for the modal to close.
            if (_phase == "taken") { completed = true; _quest?.Finish(_def.DoneKey); }
            var def = _def; _def = null; _serial++; // revoke permission before cleanup and any callbacks
            if (!completed) { _quest?.Cancel(); if (_mission == Mission.Current && _host.Active) Say(def.CancelKey); }
            _move?.RequestInterrupt(); _move = null;
            _lease?.Release("practice " + reason); _lease = null;
            _demo?.Release("practice " + reason); _demo = null;
            if (_mission == Mission.Current && AgentControlHelper.SafeIsActive(_teacher))
            {
                var eq = _teacher.SpawnEquipment.Clone();
                bool changed = false;
                foreach (var pair in _temporary)
                    if (eq[pair.Key].Item == pair.Value.Item) { eq[pair.Key] = EquipmentElement.Invalid; changed = true; }
                if (changed) { _teacher.UpdateSpawnEquipmentAndRefreshVisuals(eq); _teacher.UpdateAgentStats(); }
                var brain = AgentAIController.GetBrainForAgent(_teacher);
                if (_host.Active && brain != null && !brain.IsInCombat && !brain.IsBound && !brain.IsStatusHeld && !brain.IsStunned)
                {
                    _returnPending = true;
                }
            }
            _temporary.Clear(); _practiceSlot = EquipmentIndex.None; _playerGrapple = null;
            _arrived = _moveFailed = _approached = false; _quest = null; _phase = null;
            StoryGoalHudMissionView.RequestFlash = true;
            DebugLogger.Log("[StoryPractice] " + def.Id + " stopped: " + reason);
        }
    }
}
