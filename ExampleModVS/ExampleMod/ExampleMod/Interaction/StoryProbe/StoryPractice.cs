using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
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
        // 潜行课的分步提示：到场招呼、蹲下、背后站位、距离、出手、被看见、脚步重、手滑。
        // 全部走数据（策划改文案不用重编），缺项在加载期拒绝，避免"教了但什么都不提示"。
        public string GreetKey, HintCrouchKey, HintBehindKey, HintApproachKey, HintStrikeKey,
            HintSeenKey, HintNoiseKey, HintBotchKey, HintWaitKey;
        /// <summary>完成后的系统下一步提示（只走左下角，不由 NPC 说）。</summary>
        public string NextKey;
        /// <summary>课完成后自动打开的剧情对话触发器（对话 JSON 的 Triggers 里声明；空 = 不自动开对话）。</summary>
        public string DoneDialogueTrigger;
        /// <summary>老师演示完成后自动打开的对话触发器（如「演示完了，钩爪得拿东西换」；空 = 不自动开）。</summary>
        public string DemoDialogueTrigger;
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
        private float _until, _nextRetry, _nextFollowLog, _retryBlockUntil;
        // 教学节奏：到场先说一句、再背身；租约拿不到只重试，不判失败；提示按状态切换播报。
        private float _greetUntil, _leaseRetryAt, _nextHint;
        private bool _greeted;
        private string _hintState;
        private int _demoPullReceipt, _demoTries;
        private float _demoUntil;
        private string _phase;
        private bool _arrived, _moveFailed, _approached;
        private PerformanceAction _lease;
        private MoveToPositionAction _move, _returnMove;
        private bool _returnInProgress;
        private GrappleLogic _demo, _playerGrapple;
        private int _demoReceipt, _playerReceipt;
        private EquipmentIndex _practiceSlot = EquipmentIndex.None;
        private readonly Dictionary<EquipmentIndex, EquipmentElement> _temporary = new Dictionary<EquipmentIndex, EquipmentElement>();
        public bool Active => _def != null;
        public string Actor => _def?.Actor;
        public string LessonId => _def?.Id;
        private bool _returnPending;
        // 上课期间暂停老师的「空闲就回岗位」兜底；岗位点本身不动，课结束恢复。
        private bool _dutySuspended;
        // 完成/取消后让老师先在原地留几秒（面向玩家），别一说完成就转身回岗位；期间玩家可以接话。
        private float _lingerUntil;
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
            && (d.Mode == "pickpocket" || d.Mode == "grapple" || d.Mode == "grapple_demo") && !string.IsNullOrEmpty(d.Actor)
            && d.Position?.Length == 3 && d.Position.All(Finite)
            && (d.Mode == "pickpocket" || d.Kit?.Length == 2 && d.Kit.All(i => !string.IsNullOrEmpty(i)) && d.Kit.Distinct().Count() == 2)
            && (d.Mode != "grapple" || d.Aim?.Length == 3 && d.Aim.All(Finite))
            && (d.Mode != "grapple_demo" || d.Aim == null || d.Aim.Length == 3 && d.Aim.All(Finite))
            && (d.Mode != "pickpocket" || !string.IsNullOrEmpty(d.Item))
            && (d.Mode != "pickpocket" || new[] { d.HintCrouchKey, d.HintBehindKey, d.HintApproachKey,
                d.HintStrikeKey, d.HintSeenKey, d.HintNoiseKey, d.HintBotchKey }.All(s => !string.IsNullOrEmpty(s)))
            && new[] { d.GreetKey, d.HintWaitKey }.All(s => !string.IsNullOrEmpty(s))
            && new[] { d.TitleKey, d.FollowKey, d.ReadyKey, d.RetryKey, d.DoneKey, d.CancelKey, d.DoneFlag, d.FollowObjectiveKey, d.PracticeObjectiveKey }.All(s => !string.IsNullOrEmpty(s))
            && Finite(d.FacingDegrees) && Math.Abs(d.FacingDegrees) <= 360 && Finite(d.SightMeters) && d.SightMeters > 0 && d.SightMeters <= 20
            && Finite(d.TimeoutSec) && d.TimeoutSec >= 30 && d.TimeoutSec <= 900
            && Finite(d.AimTolerance) && d.AimTolerance > 0 && d.AimTolerance <= 5;
        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        private static Vec3 V(float[] p) => new Vec3(p[0], p[1], p[2]);

        /// <summary>
        /// 钩索演示的落点：取演示点附近最近一段城墙实体的包围盒顶面中心。
        /// 不写死坐标（换城/换版本仍可用），也不要求场景里恰好有手工量过的钩点。
        /// 只挑 2~14 米高的墙段：太低不算上墙，太高演示完下不来、玩家绳长也不够。
        /// </summary>
        private Vec3 ResolveDemoAim(Vec3 near)
        {
            if (_def.Aim != null && _def.Aim.Length == 3) return V(_def.Aim);
            var scene = Mission.Current?.Scene;
            if (scene == null) return Vec3.Zero;
            TaleWorlds.Engine.GameEntity best = null;
            Vec3 bestPoint = Vec3.Zero;
            float bestScore = float.MaxValue;
            try
            {
                var all = new List<TaleWorlds.Engine.GameEntity>();
                scene.GetEntities(ref all);
                foreach (var e in all)
                {
                    if (e == null) continue;
                    string prefab = null, name = null;
                    try { prefab = e.GetPrefabName(); } catch { }
                    try { name = e.Name; } catch { }
                    string id = (prefab ?? "") + "|" + (name ?? "");
                    // 城墙 prefab/实体名各场景不一（castle_wall_d / city_wall / wall_*），
                    // 只认 castle_wall 会把演示课在真墙根下也判成"附近没有墙"（2026-11-20 实机）。
                    if (id.IndexOf("wall", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Vec3 p;
                    try { p = e.GlobalPosition; } catch { continue; }
                    float dist = p.Distance(near);
                    if (dist > 25f) continue;
                    Vec3 lo, hi;
                    try { lo = e.GlobalBoxMin; hi = e.GlobalBoxMax; } catch { continue; }
                    float height = hi.z - near.z;
                    if (height < 2f || height > 14f) continue;
                    // 近的、且高度接近 6 米的一段最像"能演一遍再下来的矮墙"。
                    float score = dist + Math.Abs(height - 6f) * 0.5f;
                    if (score >= bestScore) continue;
                    bestScore = score; best = e;
                    bestPoint = new Vec3((lo.x + hi.x) * 0.5f, (lo.y + hi.y) * 0.5f, hi.z - 0.25f);
                }
            }
            catch (Exception ex) { DebugLogger.Log("[StoryPractice] demo aim scan failed: " + ex.Message); }
            if (best == null)
            {
                DebugLogger.Log("[StoryPractice] demo aim: no castle_wall within 25m of ("
                    + near.x.ToString("F1") + "," + near.y.ToString("F1") + ")");
                return Vec3.Zero;
            }
            string wall = null;
            try { wall = best.Name ?? best.GetPrefabName(); } catch { }
            DebugLogger.Log("[StoryPractice] demo aim wall=" + wall + " point=("
                + bestPoint.x.ToString("F1") + "," + bestPoint.y.ToString("F1") + "," + bestPoint.z.ToString("F1")
                + ") height=" + (bestPoint.z - near.z).ToString("F1"));
            return bestPoint;
        }
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
            _greeted = false; _hintState = null; _leaseRetryAt = 0f; _nextHint = 0f; _demoTries = 0; _retryBlockUntil = 0f; _lingerUntil = 0f;
            if ((!_returnPending && !_returnInProgress) || previousTeacher != _teacher)
            { _home = _teacher.Position; _homeFacing = _teacher.Frame.rotation.f.AsVec2; }
            _returnMove?.RequestInterrupt(); _returnMove = null;
            _returnInProgress = _returnPending = false;
            _point = V(_def.Position);
            // 1.3+ 的 V.NavMeshSnap 是 in 查询、改不了点（只查询不吸附）⇒ 直接问 WorldPosition 要导航面高度。
            // 高度差 0.5 米就足以让「到达」永远不成立（老师停在格子边上、练习卡在 follow）。
            var scene = _mission?.Scene;
            if (scene != null)
            {
                var wp = new TaleWorlds.Engine.WorldPosition(scene, UIntPtr.Zero, _point, false);
                if (wp.GetNavMesh() != UIntPtr.Zero)
                {
                    var onMesh = wp.GetNavMeshVec3();
                    if (Math.Abs(onMesh.z - _point.z) > 0.05f)
                        DebugLogger.Log("[StoryPractice] " + _def.Id + " point z " + _point.z.ToString("F2") + " -> navmesh " + onMesh.z.ToString("F2"));
                    _point = onMesh;
                }
                else
                    DebugLogger.Log("[StoryPractice] " + _def.Id + " point NOT on navmesh (" + _point.x.ToString("F1") + "," + _point.y.ToString("F1") + "," + _point.z.ToString("F2") + ")");
                // 还有点落在「有导航面、但走不过去」的高台/ ledge 上的情况：先验连通性，不通就在 1~4 米内
                // 找最近的、确实走得到的点；找不到就照样开课（会在 follow 阶段超时失败），但日志写清楚。
                if (AgentControlHelper.SafeIsActive(_teacher))
                {
                    var teacherWp = new TaleWorlds.Engine.WorldPosition(scene, UIntPtr.Zero, _teacher.Position, false);
                    if (!LivingWorldNpcs.V.PathExists(teacherWp, new TaleWorlds.Engine.WorldPosition(scene, UIntPtr.Zero, _point, false)))
                    {
                        bool repaired = false;
                        for (float radius = 1f; radius <= 4f && !repaired; radius += 1f)
                            for (int i = 0; i < 8 && !repaired; i++)
                            {
                                float angle = i * (float)Math.PI / 4f;
                                var candidate = new Vec3(_point.x + (float)Math.Cos(angle) * radius, _point.y + (float)Math.Sin(angle) * radius, _point.z);
                                var wpC = new TaleWorlds.Engine.WorldPosition(scene, UIntPtr.Zero, candidate, false);
                                if (wpC.GetNavMesh() == UIntPtr.Zero) continue;
                                candidate = wpC.GetNavMeshVec3();
                                if (!LivingWorldNpcs.V.PathExists(teacherWp, new TaleWorlds.Engine.WorldPosition(scene, UIntPtr.Zero, candidate, false))) continue;
                                DebugLogger.Log("[StoryPractice] " + _def.Id + " point unreachable; repaired to (" + candidate.x.ToString("F1") + "," + candidate.y.ToString("F1") + "," + candidate.z.ToString("F2") + ")");
                                _point = candidate; repaired = true;
                            }
                        if (!repaired)
                            DebugLogger.Log("[StoryPractice] " + _def.Id + " point unreachable from teacher and no walkable spot within 4m");
                    }
                }
            }
            _aim = _def.Mode == "grapple" ? V(_def.Aim)
                : _def.Mode == "grapple_demo" ? ResolveDemoAim(_point) : Vec3.Zero;
            if (_def.Mode == "grapple_demo" && _aim.LengthSquared < 0.01f)
            {
                why = "no wall within reach for the grapple demonstration";
                _def = null; _teacher = null; _player = null;
                return false;
            }
            // 上课期间老师的「空闲回岗位」兜底暂停：他有正经教学任务，不能按原岗位点走回去。
            //（2026-11-20 实机：greet 后同一毫秒 Brain-Enqueue MoveToPositionAction，租约一直抢不到。）
            _dutySuspended = false;
            var dutyBrain = AgentAIController.GetBrainForAgent(_teacher);
            if (dutyBrain != null)
            {
                dutyBrain.SuspendDutyReturn(true);
                _dutySuspended = true;
                DebugLogger.Log("[StoryPractice] " + _def.Id + " duty fallback suspended");
            }
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
                // 完成/取消后先留几秒：老师面向玩家，玩家可以接话；时间到再回岗位。
                if (_lingerUntil > 0f)
                {
                    if (_host.Clock < _lingerUntil || StoryProbeDialogue.IsDialogueActiveNow) return;
                    _lingerUntil = 0f;
                    var lingerTeacher = _teacher; var lingerMission = _mission;
                    if (_host.Active && lingerMission == Mission.Current && _token == StoryProbeState.RuntimeToken
                        && AgentControlHelper.SafeIsActive(lingerTeacher))
                    {
                        var lingerBrain = AgentAIController.GetBrainForAgent(lingerTeacher);
                        if (_dutySuspended) { lingerBrain?.SuspendDutyReturn(false); _dutySuspended = false; }
                        _returnPending = true;
                    }
                    return;
                }
                if (_returnPending && !ConversationEntryPatch.IsConversationActive())
                {
                    _returnPending = false;
                    var teacher = _teacher; var mission = _mission; int token = _token; int serial = _serial;
                    if (_host.Active && mission == Mission.Current && token == StoryProbeState.RuntimeToken
                        && AgentControlHelper.SafeIsActive(teacher))
                        {
                        _returnInProgress = true;
                        AgentControlHelper.ForceMoveTo(teacher, _home, 0.5f, 90f,
                            new Action<bool>(ok => {
                                if (serial != _serial) return;
                                _returnInProgress = false; _returnMove = null;
                                if (ok && !Active && mission == Mission.Current && token == StoryProbeState.RuntimeToken)
                                    AgentControlHelper.SetBodyDirection(teacher, _homeFacing);
                            }), new Action<MoveToPositionAction>(move => { if (serial == _serial) _returnMove = move; }));
                        }
                }
                return;
            }
            var brain = AgentAIController.GetBrainForAgent(_teacher);
            if (_mission != Mission.Current || _token != StoryProbeState.RuntimeToken || !_host.Active
                || Agent.Main != _player || !AgentControlHelper.SafeIsActive(_player) || _player.Health <= 0
                || !AgentControlHelper.SafeIsActive(_teacher) || _teacher.Health <= 0 || brain == null
                || brain.IsInCombat || brain.IsBound || brain.IsStatusHeld || brain.IsStunned
                || _host.Director?.IsEventParticipant(Actor) == true)
            { Stop("interrupted"); return; }
            // 计时到了不判失败：玩家还在练就继续等，只提醒一次。
            // （旧行为是 _until 一到就 Stop → 任务失败；实机被当成"走到点就失败"。）
            if (_host.Clock > _until)
            {
                _until = _host.Clock + _def.TimeoutSec;
                Hint(_def.HintWaitKey);
            }
            // 自建剧情面板的对话也要让课暂停（引擎会话没启动，ConversationEntryPatch 判不出来）。
            if (StoryProbeDialogue.IsDialogueActiveNow)
            {
                if (_phase != "pending") _lease?.Pause(true);
                return;
            }
            // 对话结束后恢复本课自己的租约；否则 Pause(true) 会一直挂着，CanControl 永远为假。
            if (_lease != null && _lease.Paused) _lease.Pause(false);
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
                // 这句是系统说明（跟到哪、怎么重试），更新任务与屏幕提示即可，不能借老师的嘴说。
                _phase = "follow"; Notify(_def.FollowKey);
                _nextFollowLog = _host.Clock + 1.5f;
                var def = _def; int serial = _serial;
                DebugLogger.Log("[StoryPractice] " + def.Id + " move issued: teacher=(" + _teacher.Position.x.ToString("F1") + "," + _teacher.Position.y.ToString("F1") + "," + _teacher.Position.z.ToString("F1") + ") target=(" + _point.x.ToString("F1") + "," + _point.y.ToString("F1") + "," + _point.z.ToString("F1") + ") dist=" + _teacher.Position.Distance(_point).ToString("F1") + " pose=" + AgentControlHelper.GetPose(_teacher) + " crouch=" + _teacher.CrouchMode + " using=" + _teacher.IsUsingGameObject);
                AgentControlHelper.ForceMoveTo(_teacher, _point, 0.5f, 90f,
                    new Action<bool>(ok => { if (_def == def && serial == _serial) { _arrived = ok; _moveFailed = !ok;
                        DebugLogger.Log("[StoryPractice] " + def.Id + " move done ok=" + ok + " teacher=(" + _teacher.Position.x.ToString("F1") + "," + _teacher.Position.y.ToString("F1") + "," + _teacher.Position.z.ToString("F1") + ") dist=" + _teacher.Position.Distance(_point).ToString("F1")); } }),
                    new Action<MoveToPositionAction>(move => { if (_def == def && serial == _serial) _move = move; }));
                return;
            }
            if (_moveFailed) { Stop("move failed"); return; }
            if (_phase == "follow")
            {
                if (!_arrived)
                {
                    // 老师没到位时每 1.5 秒打一次位置：能一眼分清「没走」「走了没到」「到了没认」。
                    if (_host.Clock >= _nextFollowLog)
                    {
                        _nextFollowLog = _host.Clock + 1.5f;
                        DebugLogger.Log("[StoryPractice] " + _def.Id + " following: teacher=(" + _teacher.Position.x.ToString("F1") + "," + _teacher.Position.y.ToString("F1") + "," + _teacher.Position.z.ToString("F1") + ") target=(" + _point.x.ToString("F1") + "," + _point.y.ToString("F1") + "," + _point.z.ToString("F1") + ") dist=" + _teacher.Position.Distance(_point).ToString("F1"));
                    }
                    return;
                }
                // 到了先转身面对玩家、说一句，再背过去。玩家没到跟前之前不申请演出租约
                // （旧顺序在玩家还远时就申请，租约被市井动作占着 → 直接 Stop("lease unavailable")，
                //  实机表现就是"他刚走到点，任务就失败"）。
                if (!_greeted)
                {
                    float playerDist = _teacher.Position.Distance(_player.Position);
                    if (playerDist > 8f)
                    {
                        if (_host.Clock >= _nextHint)
                        {
                            _nextHint = _host.Clock + 5f;
                            Notify(_def.FollowObjectiveKey);
                        }
                        return;
                    }
                    var face = _player.Position - _teacher.Position; face.z = 0f;
                    float flen = (float)Math.Sqrt(face.x * face.x + face.y * face.y);
                    if (flen > 0.01f)
                        AgentControlHelper.SetBodyDirection(_teacher, new Vec2(face.x / flen, face.y / flen));
                    Say(_def.GreetKey);
                    _greeted = true; _greetUntil = _host.Clock + 2.5f;
                    DebugLogger.Log("[StoryPractice] " + _def.Id + " greeted player dist=" + playerDist.ToString("F1"));
                    return;
                }
                if (_host.Clock < _greetUntil) return;
                if (_player.Position.Distance(_point) > 6f)
                {
                    if (_host.Clock >= _nextHint)
                    {
                        _nextHint = _host.Clock + 5f;
                        Hint(_def.HintApproachKey);
                    }
                    return;
                }
                _move = null;
                _phase = "lease"; _leaseRetryAt = 0f;
                return;
            }
            if (_phase == "demo")
            {
                if (_demo == null) { _phase = "lease"; _leaseRetryAt = _host.Clock + 1f; return; }
                if (_host.Clock > _demoUntil)
                {
                    DebugLogger.Log("[StoryPractice] " + _def.Id + " demo timeout tries=" + _demoTries);
                    _demo.Release("demo timeout"); _demo = null;
                    // 老师演示不成就让玩家自己来——不能把整节课判失败。
                    if (_def.Mode == "grapple_demo" && _demoTries >= 3)
                    { Hint(_def.RetryKey); GivePlayerKit(); BeginPlayerPractice(); }
                    else { _phase = "lease"; _leaseRetryAt = _host.Clock + 2f; }
                    return;
                }
                if (_demo.WorldHitReceipt <= _demoReceipt) return;
                // 判定"打没打中"要比的是**人会被拉到哪里**：钩头常常只是擦在平台侧面（z 差两三米），
                // 落点解算已经算出正确站立点。有落点用落点，没有（真打空）才退回钩头命中点。
                Vec3 judgedHit = _demo.Landing.Found ? _demo.Landing.Endpoint : _demo.LastWorldHit;
                if (judgedHit.Distance(_aim) > _def.AimTolerance)
                {
                    DebugLogger.Log("[StoryPractice] " + _def.Id + " demo missed target dist="
                        + judgedHit.Distance(_aim).ToString("F1") + " landing=" + _demo.Landing.Found
                        + " judged=(" + judgedHit.x.ToString("F1") + "," + judgedHit.y.ToString("F1") + "," + judgedHit.z.ToString("F1") + ")");
                    _demo.Release("demo missed target"); _demo = null;
                    _phase = "lease"; _leaseRetryAt = _host.Clock + 2f;
                    if (_demoTries >= 2) Say(_def.RetryKey);
                    return;
                }
                if (_def.Mode == "grapple_demo")
                {
                    // 演示要看他真的拉上去：只命中不算，拉拽完成（= 人到墙上）才算演完。
                    if (_demo.PullFinishedReceipt <= _demoPullReceipt) return;
                    DebugLogger.Log("[StoryPractice] " + _def.Id + " teacher demo climb complete");
                    _demo.Release("demo climb complete"); _demo = null;
                    // 「演示看过了」单独记一笔：钩爪只能在这之后向老师索要（用户裁定：演示不扣人情，索要才扣）。
                    StoryProbeState.MarkAsked(_def.Actor, "demo_shown");
                    // 演示本身不扣人情、也不发东西：钩爪还在老师手上，
                    // 玩家随后在对话里自己开口换回来（2026-11-20 用户裁定）。
                    Say(_def.ReadyKey);
                    BeginPlayerPractice(spoken: true);
                    if (!string.IsNullOrEmpty(_def.DemoDialogueTrigger))
                    {
                        // 🔴 演示完先**回到地面**再开口（2026-11-20 用户裁定：墙上跟玩家说话不合理）。
                        // 走回玩家身边（navmesh 连通时沿墙下来）；8 秒没走到就照样开口，绝不把人卡在墙上。
                        var demoTeacher = _teacher; var demoMission = _mission;
                        int demoToken = _token; int demoSerial = _serial;
                        string demoTalk = _def.DemoDialogueTrigger;
                        var landPoint = _player != null && AgentControlHelper.SafeIsActive(_player)
                            ? _player.Position : _point;
                        bool spoken = false;
                        Action openTalk = () =>
                        {
                            if (spoken) return;
                            spoken = true;
                            if (demoSerial != _serial || demoMission != Mission.Current
                                || demoToken != StoryProbeState.RuntimeToken || !_host.Active) return;
                            // 落地/超时后再钉住老师，玩家练习期间他站在旁边看着。
                            if (AgentControlHelper.SafeIsActive(demoTeacher)) AgentControlHelper.HoldInPlace(demoTeacher);
                            _host.SchedulePublic(0.25f, () => _host.StartStoryConversation(demoTeacher, null, demoTalk));
                        };
                        AgentControlHelper.ForceMoveTo(demoTeacher, landPoint, 1.6f, 8f,
                            new Action<bool>(ok => openTalk()), null);
                    }
                    return;
                }
                _demo.Release("practice demo complete"); _demo = null;
                BeginPlayerPractice();
                return;
            }
            if (_phase == "lease")
            {
                if (_lease == null)
                {
                    if (_host.Clock < _leaseRetryAt) return;
                    // 每次重试都先让市井工作让位；仍拿不到就等下一轮，绝不判失败。
                    _host.Work?.YieldActor(Actor, "practice");
                    _host.Director?.YieldActorForDialogue(_teacher);
                    _lease = PerformanceAction.TryAcquire(_teacher, "practice:" + _def.Id, null, replaceAmbient: true);
                    if (_lease == null || _lease.Ended)
                    {
                        _lease = null; _leaseRetryAt = _host.Clock + 1.5f;
                        DebugLogger.Log("[StoryPractice] " + _def.Id + " lease unavailable; retry in 1.5s");
                        if (_host.Clock >= _nextHint) { _nextHint = _host.Clock + 6f; Hint(_def.HintWaitKey); }
                        return;
                    }
                }
                if (_lease.Ended) { _lease = null; _leaseRetryAt = _host.Clock + 1f; return; }
                if (!_lease.CanControl) return;
                _lease.AllowSpeechBodyTurn = false; _lease.AllowSpeechGaze = false;
                float yaw = _def.FacingDegrees * (float)Math.PI / 180f;
                AgentControlHelper.StopLooking(_teacher);
                AgentControlHelper.SetBodyDirection(_teacher, new Vec2((float)Math.Cos(yaw), (float)Math.Sin(yaw)));
                _phase = "practice"; _nextRetry = _host.Clock + 2f;
                if (_def.Mode == "pickpocket")
                {
                    _quest.Note(_def.ReadyKey); Hint(_def.ReadyKey); StoryGoalHudMissionView.RequestFlash = true;
                }
                else
                {
                    // 钩索课：先放手让钩索套件接管身体，老师先甩一遍给玩家看。
                    _lease?.Release("practice demo"); _lease = null;
                    StartDemo(_def.Mode == "grapple_demo");
                }
                return;
            }
            if (_phase != "practice") return;
            if (_def.Mode == "grapple" || _def.Mode == "grapple_demo")
            {
                if (GrappleLogic.GetSession(_player) == _playerGrapple && _playerGrapple.PullFinishedReceipt > _playerReceipt)
                {
                    _playerReceipt = _playerGrapple.PullFinishedReceipt;
                    if (_playerGrapple.LastPullAnchor.Distance(_aim) <= _def.AimTolerance) Complete();
                    else Retry("anchor");
                }
                return;
            }
            // 潜行：租约丢了就等它恢复，不判失败。
            if (_lease == null || !_lease.CanControl) return;
            if (StealManager.IsUIOpen) return;
            GuidePickpocket();
        }
        /// <summary>老师先甩一遍：autoPull=true 时他自己拉上墙，玩家看完整演示；玩家课只要他甩中示范点。</summary>
        private void StartDemo(bool autoPull)
        {
            // 老师"示范"不受技能散布影响：小六 Roguery=0 ⇒ 散布 7° ⇒ 钩子常飞过平台（2026-11-20 实机）。
            try { var demoOwner = _teacher; GrappleLogic.SpreadOverride = a => a == demoOwner ? 0f : -1f; } catch { }
            _demo = GrappleLogic.EnsureFor(_teacher);
            if (_demo == null)
            {
                DebugLogger.Log("[StoryPractice] " + _def.Id + " demo session unavailable");
                _phase = "lease"; _leaseRetryAt = _host.Clock + 2f;
                return;
            }
            _demoReceipt = _demo.WorldHitReceipt;
            _demoPullReceipt = _demo.PullFinishedReceipt;
            if (!_demo.BeginNpcThrow(_aim, false, autoPull, out string why))
            {
                DebugLogger.Log("[StoryPractice] " + _def.Id + " demo rejected: " + why);
                _demo = null; _phase = "lease"; _leaseRetryAt = _host.Clock + 2f;
                return;
            }
            _demoTries++;
            _demoUntil = _host.Clock + 20f; _phase = "demo";
            DebugLogger.Log("[StoryPractice] " + _def.Id + " demo throw #" + _demoTries + " autoPull=" + autoPull
                + " aim=(" + _aim.x.ToString("F1") + "," + _aim.y.ToString("F1") + "," + _aim.z.ToString("F1") + ")");
        }

        /// <summary>轮到玩家自己动手：不再需要老师的演出租约，只观察玩家的真实拉拽回执。</summary>
        private void BeginPlayerPractice(bool spoken = false)
        {
            _phase = "practice"; _nextRetry = _host.Clock + 1f; _hintState = null;
            // 演示课的老师此刻还在墙上、要自己走下来（见 demo 分支），这里不能提前钉住。
            _playerGrapple = GrappleLogic.Ensure();
            _playerReceipt = _playerGrapple != null ? _playerGrapple.PullFinishedReceipt : 0;
            _quest.Note(_def.ReadyKey);
            if (!spoken) Hint(_def.ReadyKey);
            StoryGoalHudMissionView.RequestFlash = true;
        }

        /// <summary>演示结束后把绳、钩交给玩家并**当场装到手上**；已持有的不重复发（取消重练不会越练越多）。
        /// 🔴 只进背包等于没给：滚轮只切已装备的武器槽，玩家在场景里调不出钩索（2026-11-20 实机）。</summary>
        private void GivePlayerKit()
        {
            if (_def.Kit == null) return;
            var roster = Hero.MainHero?.PartyBelongedTo?.ItemRoster;
            var handed = new List<ItemObject>();
            foreach (string id in _def.Kit)
            {
                var item = StoryProbeDefs.FindItem(id);
                if (item == null) { DebugLogger.Log("[StoryPractice] kit item missing: " + id); continue; }
                if (roster == null || roster.GetItemNumber(item) <= 0)
                    if (!StoryProbeDefs.GiveItem(Hero.MainHero, id, 1))
                        DebugLogger.Log("[StoryPractice] give kit failed: " + id);
                handed.Add(item);
            }
            EquipOnPlayerHands(handed);
        }

        private static void EquipOnPlayerHands(List<ItemObject> items) => StoryProbeDefs.EquipOnPlayerHands(items);

        /// <summary>
        /// 潜行课的分步提示：按"被看见 / 脚步重 / 没蹲 / 距离远 / 可出手"切换。
        /// 同一状态不刷屏，状态变了或 5~7 秒才再提示一次；键位从引擎热键表取，改键后提示也跟着变。
        /// </summary>
        private void GuidePickpocket()
        {
            float dist = _teacher.Position.Distance(_player.Position);
            bool seen = NpcSightSystem.CanAgentSeeTarget(_teacher, _player, _def.SightMeters)
                && NpcSightSystem.IsTargetInViewCone(_teacher, _player, useBodyFacing: true);
            bool noisy = dist <= StoryProbeTuning.Current.Noise.OrdinaryFootstepRadiusMeters && !_player.CrouchMode
                && _player.Velocity.Length > StoryProbeTuning.Current.Noise.RunSpeedMps;
            var fwd = _teacher.Frame.rotation.f;
            var toPlayer = _player.Position - _teacher.Position;
            bool behind = fwd.x * toPlayer.x + fwd.y * toPlayer.y < 0f;
            string state, key;
            if (seen) { state = "seen"; key = _def.HintSeenKey; }
            else if (noisy) { state = "noise"; key = _def.HintNoiseKey; }
            else if (!behind) { state = "behind"; key = _def.HintBehindKey; }
            else if (!_player.CrouchMode) { state = "crouch"; key = _def.HintCrouchKey; }
            else if (dist > 2f) { state = "approach"; key = _def.HintApproachKey; }
            else { state = "strike"; key = _def.HintStrikeKey; }
            if (state != _hintState || _host.Clock >= _nextHint)
            {
                _hintState = state;
                _nextHint = _host.Clock + (state == "strike" ? 5f : 7f);
                Hint(key);
            }
            if (seen || noisy)
            {
                if (_host.Clock >= _nextRetry) Retry(state);
            }
            else if (dist <= 2f && _player.CrouchMode) _approached = true;
        }

        /// <summary>逐步提示是小六在教人（"先蹲下""绕到我背后"），按人物开口的通道发：气泡 + 左下角各一次。</summary>
        private void Hint(string key) => Say(key);

        private static string KeyName(int gameKeyId)
        {
            try { return HyperlinkTexts.GetKeyHyperlinkText(HotKeyManager.GetHotKeyId("CombatHotKeyCategory", gameKeyId)).ToString(); }
            catch { return "?"; }
        }

        private void Retry(string reason = null)
        {
            _approached = false; _hintState = null; _nextRetry = _host.Clock + 3f; _retryBlockUntil = _nextRetry;
            Say(reason == "botch" ? _def.HintBotchKey : _def.RetryKey);
            StoryGoalHudMissionView.RequestFlash = true;
        }
        /// <summary>系统说明（去哪、按什么键、怎么重试）只更新任务与屏幕提示——不借 NPC 的嘴说，人物只说自己会说的话。</summary>
        private void Notify(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            InformationManager.DisplayMessage(new InformationMessage(
                LWNTextHelper.ResolveCompound(key, "Practice",
                    ("CROUCH", KeyName(15)), ("ACTION", KeyName(13)))));
        }
        /// <summary>人物开口（到场招呼、逐步提示、重试、完成话）：**只走头顶气泡**，不再往左下角塞系统消息。
        /// 玩家嫌屏幕消息太吵（2026-11-20 用户裁定）；系统说明（去哪、任务目标、下一步）仍走 <see cref="Notify"/>。
        /// unbound = 不绑定演出租约：这句常在教学收尾时说，租约已释放也要照常播出。</summary>
        private void Say(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _host.SayOnAgentText(_teacher, LWNTextHelper.ResolveCompound(key, "Practice",
                ("CROUCH", KeyName(15)), ("ACTION", KeyName(13))), SpeechPriority.Chat, _player,
                unbound: true, interrupt: true);
        }
        public bool IsConsentingTeacher(Agent target) => Active && target == _teacher && _mission == Mission.Current
            && _token == StoryProbeState.RuntimeToken && _host.Active;
        public bool IsPracticeTarget(Agent target) => Active && _phase == "practice" && _def.Mode == "pickpocket"
            && _mission == Mission.Current && _token == StoryProbeState.RuntimeToken && _host.Clock <= _until && target == _teacher
            && _player == Agent.Main && AgentControlHelper.SafeIsActive(_player) && _player.Health > 0 && _lease?.CanControl == true && _temporary.ContainsKey(_practiceSlot);
        // 只认「是不是这节课的练习目标/练习槽/那把匕首」——不再二次判位置。
        // 位置条件属于"能不能打开偷窃条"的入口（CanAttempt）与提示；绿/黄区命中是通用偷窃的成败，教程不另判。
        public bool AllowsTake(Agent target, EquipmentIndex slot) => IsPracticeTarget(target) && slot == _practiceSlot
            && target.SpawnEquipment[slot].Item == _temporary[slot].Item;

        /// <summary>入口用：练习偷窃此刻是否允许打开偷窃条（蹲、背身、2 米内、无重试冷却）。
        /// 它不参与命中判定；命中后只由通用偷窃决定成败，AllowsTake 只核对身份。</summary>
        public bool CanAttempt(Agent target) => IsPracticeTarget(target)
            && _host.Clock >= _retryBlockUntil
            && _player.CrouchMode && _player.Position.Distance(_teacher.Position) <= 2f
            && !NpcSightSystem.IsTargetInViewCone(_teacher, _player, useBodyFacing: true);
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
            bool allowed = hit && slot.HasValue && AllowsTake(target, slot.Value);
            float dist = _teacher.Position.Distance(_player.Position);
            bool front = NpcSightSystem.IsTargetInViewCone(_teacher, _player, useBodyFacing: true);
            DebugLogger.Log("[StoryPractice] attempt hit=" + hit + " allowed=" + allowed + " slot=" + (slot?.ToString() ?? "-")
                + " practiceSlot=" + _practiceSlot + " dist=" + dist.ToString("F2") + " crouch=" + _player.CrouchMode
                + " front=" + front + " approached=" + _approached);
            if (!hit)
            {
                // 真的没打中判定区：这才叫手滑（通用偷窃的成败）。
                Retry("botch");
            }
            else if (!allowed)
            {
                // 命中了但不是练习槽（正常流程不会出现）：只记日志并提示，不把位置问题说成手滑。
                DebugLogger.Log("[StoryPractice] hit but not practice slot; no transfer");
                Retry("botch");
            }
            return true;
        }
        public void TickCompletion()
        { if (Active && _phase == "taken" && !StealManager.IsUIOpen) Complete(); }
        private void Complete()
        {
            // 交还钩子后的正式课 = 真的从城墙翻进去；付费练习课（grapple_wall）只练手，不算进城。
            if (_def.Id == "grapple_demo")
                _host.OpenGate("guile", "LWN_story_gate_climbed", "You came over the wall.");
            string doneKey = _def.DoneKey, nextKey = _def.NextKey, doneTrigger = _def.DoneDialogueTrigger;
            // 任务日志记的是「你练完了」这件事实，不是小六说的话——人物口吻只从人物嘴里出。
            _quest.Finish(_def.TitleKey);
            StoryProbeState.MarkAsked(_def.Actor, _def.DoneFlag);
            // 说完成话时转回来看玩家。
            var face = _player.Position - _teacher.Position; face.z = 0f;
            float flen = (float)Math.Sqrt(face.x * face.x + face.y * face.y);
            if (flen > 0.01f) AgentControlHelper.SetBodyDirection(_teacher, new Vec2(face.x / flen, face.y / flen));
            // 🔴 先结束课、再开口（2026-11-20 实机）：先说后停会让完成话带上演出租约，
            // 排队到租约释放后才播时被「晚到的旧演出台词」规则丢掉——小六一个字都没说出来。
            Stop("completed", completed: true);
            Say(doneKey);
            if (!string.IsNullOrEmpty(nextKey)) Notify(nextKey);
            // 完成话出完再自动开对话，把下一件事（找杜野）交到玩家手上；触发哪段由课程数据指定。
            if (!string.IsNullOrEmpty(doneTrigger))
            {
                var teacher = _teacher;
                _host.SchedulePublic(2f, () => _host.StartStoryConversation(teacher, null, doneTrigger));
            }
        }
        public string Describe() => !Active ? "OK: practice inactive revision=" + Revision : "OK: practice=" + _def.Id + " actor=" + Actor
            + " phase=" + _phase + " approached=" + _approached + " lease=" + (_lease?.CanControl == true)
            + " teacher=(" + _teacher.Position.x.ToString("F1") + "," + _teacher.Position.y.ToString("F1") + "," + _teacher.Position.z.ToString("F1") + ")"
            + (_def.Mode == "pickpocket" ? "" : " aim=(" + _aim.x.ToString("F1") + "," + _aim.y.ToString("F1") + "," + _aim.z.ToString("F1") + ")");
        public void DisposeManaged()
        {
            _def = null; _serial++; _returnInProgress = _returnPending = false; _lingerUntil = 0f;
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
            try { GrappleLogic.SpreadOverride = null; } catch { }   // 演示散布覆盖随课程结束撤掉
            // 清掉还没轮到的教学提示：玩家已经做完了，那些"按 F 出手"不该再排队念出来（2026-11-20 实机）。
            try { SpeechChannel.ClearAgent(_teacher); } catch { }
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
                // 教学收尾留几秒：老师面向玩家站住，别一说完就转身回岗位。
                // 这里不再用 brain 的瞬时状态否决——战斗中/被捆/被架/被晕任意一项都会让 linger 判 false，
                // duty 立刻恢复，人当场走掉（2026-11-20 实机：Stop 后 1 毫秒就入队回岗）。
                bool linger = completed && _host.Active && _mission == Mission.Current;
                _lingerUntil = linger ? _host.Clock + 4f : 0f;
                DebugLogger.Log("[StoryPractice] " + def.Id + " linger=" + linger + " dutySuspended=" + _dutySuspended
                    + " brain=" + (brain != null) + " completed=" + completed);
                if (!linger && _dutySuspended)
                {
                    brain?.SuspendDutyReturn(false);
                    _dutySuspended = false;
                }
                if (!linger && _host.Active && brain != null)
                {
                    _returnPending = true;
                }
            }
            _temporary.Clear(); _practiceSlot = EquipmentIndex.None; _playerGrapple = null;
            _arrived = _moveFailed = _approached = false; _quest = null; _phase = null;
            _greeted = false; _hintState = null; _demoTries = 0; _retryBlockUntil = 0f;
            StoryGoalHudMissionView.RequestFlash = true;
            DebugLogger.Log("[StoryPractice] " + def.Id + " stopped: " + reason);
        }
    }
}
