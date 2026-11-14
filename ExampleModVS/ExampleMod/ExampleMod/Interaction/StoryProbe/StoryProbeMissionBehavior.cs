using System;
using System.Collections.Generic;
using LivingWorldNpcs.Animation;
using SandBox.Conversation.MissionLogics;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.ObjectSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 序章的 Mission 侧宿主（2026-11-12，plans/逆水寒511玩法验证-城池剧情任务.md §11.2）。
    ///
    /// 职责（只做"场景里发生的事"）：
    ///   ① 判定这次进场是不是序章（开关 / 菜单入口 / 目标城 / center 地点）——原版进城永不加载；
    ///   ② 场景锚点（玩家 / 城门守门人 / 门内目标点），找不到走兜底；
    ///   ③ 生成 9 个剧情 NPC（HeroCreator 造 Hero → 按站位 spawn）；
    ///   ④ 第一次进场：只有守门人 + 强制失败演出 + 收场交回战役层（牢房弹窗在 StoryProbeBehavior）；
    ///   ⑤ 第二次进场：四家 + 6 起事件 + 招随从 + 3 句 + 目标点 + 过门判定；
    ///   ⑥ 剥夺 / 恢复（旧衣 + 面罩、武器甲钱袋没收、序章完成归还）；
    ///   ⑦ 把每帧的"市井生活"交给 StreetLifeDirector，不自己写行为。
    ///
    /// 🔴 不碰：原版村民 / 守卫 / 商贩一律不动；只接管我们 spawn 的这 9 个人。
    /// </summary>
    public class StoryProbeMissionBehavior : MissionLogic
    {
        public static StoryProbeMissionBehavior Current { get; private set; }

        // ── 场景锚点 ──
        public Vec3 PlayerAnchor { get; private set; }
        public Vec2 Forward { get; private set; } = new Vec2(0, 1);
        public Vec3 BlockerPos { get; private set; }
        public Vec3 GoalPos { get; private set; }
        public bool UsedFallback { get; private set; }
        private string _guardTagUsed;
        public bool Active { get; private set; }
        public StoryProbePhase Phase { get; private set; } = StoryProbePhase.FirstAttempt;

        // ── 生成的人 ──
        private readonly Dictionary<string, Agent> _agents = new Dictionary<string, Agent>();
        private readonly Dictionary<string, Hero> _heroes = new Dictionary<string, Hero>();

        private StreetLifeDirector _director;

        /// <summary>场景演出执行器（台本驱动：布景 → 摆人 → 玩家出场）。</summary>
        private StoryStageRunner _stage;

        public StoryStageRunner Stage => _stage;

        /// <summary>拿一个演出执行器（没有就建一个）—— `custom.story_probe stage begin` 用来在当前场景重播。</summary>
        public StoryStageRunner EnsureStage()
        {
            if (_stage == null) _stage = new StoryStageRunner(this);
            return _stage;
        }
        private float _time;
        private readonly List<(float at, Action action)> _schedule = new List<(float, Action)>();
        private bool _gateOpen;
        private bool _blockerWasDisarmed;
        private bool _recognized;
        private bool _climbSeen;
        private bool _climbRolled;
        private bool _climbSucceeded;
        private bool _climbAttempt;
        private bool _climbCrossed;
        private bool _maskChecked;
        private bool _maskPassed;
        private Vec3 _gateCenter;
        private Vec3 _lastGatePosition;
        private bool _gateGeometryReady;
        private bool _crossedGate;
        private bool _crossingDoor;
        private bool _crossingWall;
        private readonly HashSet<string> _fightRequests = new HashSet<string>();
        private readonly Dictionary<string, float> _fightStartedAt = new Dictionary<string, float>();
        private readonly Dictionary<string, int> _duelTokens = new Dictionary<string, int>();
        private int _duelSerial;
        private readonly Dictionary<string, Action<Agent>> _duelCallbacks = new Dictionary<string, Action<Agent>>();
        private Action<Agent> _firstDuelCallback;
        private int _firstDuelReceipt;
        private float _verticalGateSpeed;
        private float _gateGroundStable;
        private float _nextBarrierAt;
        private bool _pendingBlockerDown;
        private readonly HashSet<string> _pendingQuestDone = new HashSet<string>();
        private bool _goalReached;
        private bool _forcedDefeatQueued;
        private bool _firstFightStarted;
        private bool _firstGuardQuestioned;
        private bool _firstInterceptRequested;
        private bool _firstWasPastGate;
        private bool _firstDefeatResolved;
        private float _firstFightRequestedAt;
        private bool _jailPlaced;
        private float _jailPromptAt = -1f;
        private AgentAnimStateMachine _defeatAnim;
        private Agent _defeatPlayer;
        private sealed class DefeatAnimContext : AnimContext { }

        public static void RegisterAnimations()
        {
            var definition = new AnimMachineDef("story-probe-defeat");
            definition.Add(AnimState.Once("fall", "act_death_fall_front", next: null));
            AnimMachineRegistry.Register(definition);
        }

        // 第一次进场：走近守门人自动开口
        private bool _blockerAutoTalkArmed = true;

        // 第二次进场：随从 3 句
        private Agent _companionAgent;
        private int _introLine;
        private float _nextIntroAt = -1f;

        private const float AutoTalkRadius = 3.5f;
        private const float AutoTalkRearmRadius = 8f;
        private const float GoalRadius = 3.0f;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            Current = this;
        }

        public override void AfterStart()
        {
            base.AfterStart();
            // 本行为在 OnMissionBehaviorInitialize 内追加，此时引擎已跑完 OnBehaviorInitialize。
            // 必须在确实会收到的 AfterStart 登记，否则本地生成成功但对话/命令找不到宿主。
            Current = this;
            DebugLogger.Log("[StoryProbe] mission host registered in AfterStart");
            try { Setup(); }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] Setup failed: {ex}"); }
        }

        /// <summary>
        /// 决斗/击晕的胜负信号（§5.4 / §4.1）。
        /// 🔴 为什么不用 tick 轮询：Peace 切磋判负时引擎把血**当帧回满**
        ///（AttackTriggerMissionLogic.OnDuelLoser），下一帧轮询永远读不到 0。
        /// OnAgentHit 在引擎 `if (Health < 1) Die()` 之前跑，是唯一稳的采样点。
        /// </summary>
        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent,
            in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        {
            base.OnAgentHit(affectedAgent, affectorAgent, affectorWeapon, blow, attackCollisionData);
            if (!Active || affectedAgent == null) return;

            try
            {
                // 共享非致命战斗只消费仲裁回执，不能与其保命回血竞态采血量。
                if (blow.InflictedDamage <= 0 || affectedAgent.Health >= 1f
                    || AttackTriggerMissionLogic.Instance?.HasDuelPair(affectedAgent, affectorAgent) == true) return;
                string npcId = ResolveNpcIdOf(affectedAgent);
                if (string.IsNullOrEmpty(npcId)) return;

                // 🔴 只记 flag，**不在 blow 栈里做任何 native 操作**（同 AttackTriggerMissionLogic 的
                //    "判负拆两段"教训：blow 栈内改队/传送/播动画会与 asyncAITick 抢同一 agent →
                //    AccessViolation）。真正干活在下一帧 OnMissionTick 开头。
                if (npcId == "blocker") _pendingBlockerDown = true;

                foreach (var def in StoryProbeDefs.Representatives)
                {
                    if (def.QuestTargetNpcId != npcId) continue;
                    if (!StoryProbeState.IsQuestAccepted(def.Id) || StoryProbeState.IsQuestDone(def.Id)) continue;
                    _pendingQuestDone.Add(def.Id);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] OnAgentHit failed: {ex.Message}");
            }
        }

        /// <summary>把 blow 栈里记下的"谁被打倒了"在正常 tick 栈里兑现（§5.4 / §4.1）。</summary>
        private void ProcessPendingCombatResults()
        {
            if (_pendingBlockerDown)
            {
                _pendingBlockerDown = false;
                if (Phase == StoryProbePhase.Second && !_gateOpen)
                {
                    OpenGate("force", "LWN_story_gate_open", "The way is clear.");
                    ResolveApproach("force", "dockhands", -20, true, null);
                    DebugLogger.Log("[StoryProbe] blocker downed -> gate open (force)");
                }
            }

            if (_pendingQuestDone.Count > 0)
            {
                foreach (var npcId in _pendingQuestDone)
                {
                    if (!StoryProbeState.IsQuestAccepted(npcId) || StoryProbeState.IsQuestDone(npcId)) continue;
                    StoryProbeState.CompleteQuest(npcId);
                    try { PrologueQuestLauncher.FindActive(npcId)?.MarkObjectivesDone(); }
                    catch (Exception ex) { DebugLogger.Log($"[StoryProbe] objective journal update failed: {ex.Message}"); }
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveCompound("LWN_story_narr_quest_done",
                            "It's done. Go back and tell {NAME}.", ("NAME", NpcName(npcId)))));
                    DebugLogger.Log($"[StoryProbe] quest '{npcId}' target downed -> ready to turn in");
                }
                _pendingQuestDone.Clear();
            }
        }

        /// <summary>Agent → 剧情 NPC id（反查 _agents）。</summary>
        private string ResolveNpcIdOf(Agent agent)
        {
            if (agent == null) return null;
            foreach (var kv in _agents)
                if (kv.Value == agent) return kv.Key;
            return null;
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (!Active) return;

            _time += dt;

            ProcessPendingCombatResults();

            // 演出台本（布景 / 摆人 / 镜头 / 冻解玩家）——自己的时间轴，与剧本阶段无关
            try { _stage?.Tick(dt); }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryStage] tick failed: {ex.Message}");
                _stage?.Stop("tick exception");
            }

            // 延迟队列（倒序移除，回调里可能再排新的）
            for (int i = _schedule.Count - 1; i >= 0; i--)
            {
                if (_schedule[i].at > _time) continue;
                var act = _schedule[i].action;
                _schedule.RemoveAt(i);
                try { act?.Invoke(); }
                catch (Exception ex) { DebugLogger.Log($"[StoryProbe] scheduled action failed: {ex.Message}"); }
            }

            try
            {
                if (_defeatAnim != null && _defeatPlayer == Agent.Main && AgentControlHelper.SafeIsActive(_defeatPlayer))
                    _defeatAnim.Tick(_defeatPlayer, dt);
                if (Phase == StoryProbePhase.FirstAttempt) TickFirstAttempt();
                else if (Phase == StoryProbePhase.Jail) TickJailScene();
                else if (Phase == StoryProbePhase.Second) TickSecondPhase(dt);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] tick failed: {ex.Message}");
                _stage?.Stop("story tick exception");
                if (Phase == StoryProbePhase.Jail && StoryProbeState.Phase == StoryProbePhase.Jail)
                {
                    StoryProbeBehavior.AbortPrisonTransition("jail setup exception");
                    Mission.EndMission();
                }
            }
        }

        /// <summary>先停运行期工作再归还；不会修改已持久化尚欠账。</summary>
        public void CancelRuntime(string reason)
        {
            Active = false;
            _schedule.Clear();
            // 仅实际发出的本轮回执可撤战；pending-only请求不拥有目标后来参加的外部战斗。
            var fights = new Dictionary<string, Action<Agent>>(_duelCallbacks);
            if (_firstDuelCallback != null) fights["blocker"] = _firstDuelCallback;
            _duelCallbacks.Clear();
            _firstDuelCallback = null;
            _firstDuelReceipt = 0;
            _duelTokens.Clear();
            _fightRequests.Clear();
            _fightStartedAt.Clear();
            _pendingBlockerDown = false;
            _pendingQuestDone.Clear();
            _firstInterceptRequested = false;
            foreach (var pair in _agents)
            {
                var actor = pair.Value;
                if (actor == null || !AgentControlHelper.SafeIsActive(actor)) continue;
                try
                {
                    AgentAIController.Instance?.SendEventToAgent(actor, "event_story_intercept_cancel");
                    if (fights.TryGetValue(pair.Key, out var ownedReceipt))
                        AgentAIController.Instance?.SendEventToAgent(actor, "event_stop_combat", ownedReceipt);
                }
                catch (Exception ex) { DebugLogger.Log($"[StoryProbe] cancel actor '{pair.Key}' failed: {ex.Message}"); }
            }
            _firstFightStarted = false;
            try { _director?.StopAll(); }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] director stop failed: {ex.Message}"); }
            try { _stage?.Stop(reason); }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] stage stop failed: {ex.Message}"); }
            try
            {
                if (_defeatPlayer == Agent.Main && AgentControlHelper.SafeIsActive(_defeatPlayer))
                    _defeatAnim?.Release(_defeatPlayer);
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] defeat animation release failed: {ex.Message}"); }
            finally { _defeatAnim = null; _defeatPlayer = null; }
            RestoreFirstGuardEnhancement();
        }

        public override void OnEndMissionInternal()
        {
            bool resumeJail = Active && !StoryProbeState.Data.CancellationPending
                && Phase == StoryProbePhase.Jail && StoryProbeState.Phase == StoryProbePhase.Jail
                && !StoryProbeState.Data.JailDone && StoryProbeSettings.Enabled;
            try
            {
                CancelRuntime("mission ending");
                // 提前离开牢房要回牢；真实败退事实已在回执里落盘，不依赖视觉延迟。
                if (resumeJail) StoryProbeBehavior.RequestJailInquiry();
            }
            finally { base.OnEndMissionInternal(); }
        }

        public override void OnRemoveBehavior()
        {
            base.OnRemoveBehavior();
            Active = false;
            _director?.DisposeManaged();
            _stage?.DisposeManaged();
            _stage = null;
            _schedule.Clear();
            _duelCallbacks.Clear();
            _firstDuelCallback = null;
            _firstDuelReceipt = 0;
            _duelTokens.Clear();
            _fightRequests.Clear();
            _fightStartedAt.Clear();
            // 交还给引擎时清掉引用（Agent 已经无效）
            _agents.Clear();
            _director = null;
            if (Current == this) Current = null;
        }

        // ═══════════════════════════════════════════════════════════════
        // 进入判定 + 锚点
        // ═══════════════════════════════════════════════════════════════

        private void Setup()
        {
            bool jailEntry = StoryProbeState.EnterJailViaOption;
            if (!StoryProbeState.EnterViaOption && !jailEntry)
            {
                // 原版进城/拜访地牢永远不加载序章。
                return;
            }
            StoryProbeState.EnterViaOption = false;
            StoryProbeState.EnterJailViaOption = false;
            if (Settings.Instance == null || !Settings.Instance.StoryProbeEnabled)
            {
                DebugLogger.Log("[StoryProbe] skipped: disabled");
                return;
            }

            var settlement = Settlement.CurrentSettlement;
            if (settlement == null || settlement.StringId != StoryProbeSettings.SettlementId)
            {
                DebugLogger.Log($"[StoryProbe] skipped: settlement={settlement?.StringId ?? "null"} (want {StoryProbeSettings.SettlementId})");
                return;
            }
            string locationId = CampaignMission.Current?.Location?.StringId;
            if (jailEntry ? locationId != "prison" : !IsCenterLocation(settlement))
            {
                DebugLogger.Log($"[StoryProbe] skipped: location={locationId ?? "null"} jailEntry={jailEntry}");
                return;
            }

            if (Agent.Main == null && !jailEntry)
            {
                // 没有玩家 agent（观战/异常进场）→ 什么都不布置，避免把 NPC 摆到原点去
                DebugLogger.Log("[StoryProbe] skipped: no main agent");
                return;
            }

            Phase = StoryProbeState.Phase;
            if (Phase == StoryProbePhase.Done)
            {
                DebugLogger.Log("[StoryProbe] skipped: prologue already done");
                return;
            }
            if (Phase == StoryProbePhase.Jail)
            {
                if (!jailEntry)
                {
                    DebugLogger.Log("[StoryProbe] refused center during Jail; prison scene must complete first");
                    StoryProbeBehavior.RequestJailInquiry();
                    Mission.EndMission();
                    return;
                }
                Active = true;
                PlayerAnchor = Agent.Main?.Position ?? Vec3.Zero;
                DebugLogger.Log("[StoryProbe] prison mission ready; waiting for first tick to place player in cell");
                return;
            }
            if (jailEntry) return;

            Active = true;
            ResolveAnchors();
            if (!Active) return;

            if (Phase == StoryProbePhase.FirstAttempt) SetupFirstAttempt();
            else SetupSecondPhase();

            StoryProbeState.SetGoal(Phase == StoryProbePhase.Second ? StoryProbeState.Goal : "enter");
            StoryProbeScreenGoal.Show();

            DebugLogger.Log($"[StoryProbe] active phase={Phase} anchor=({PlayerAnchor.x:F1},{PlayerAnchor.y:F1},{PlayerAnchor.z:F1}) " +
                            $"blocker=({BlockerPos.x:F1},{BlockerPos.y:F1}) goal=({GoalPos.x:F1},{GoalPos.y:F1}) fallback={UsedFallback}");
        }

        /// <summary>
        /// 找"守门人刷新点"。
        ///
        /// 🔴 2026-11-13 实测（scene.xscene + sp_editor_spawnpoints.xml）：
        ///   场景里的 `<game_entity prefab="sp_guard">` **不带 tags 属性**，标签来自 prefab 本身；
        ///   而 prefab `sp_guard` 的标签是 **`sp_guard_with_spear`**（+ `npc_wait`），**不是** `sp_guard`。
        ///   更要命：裸标签 `sp_guard` 被 `sp_npc_merchant_taking_order` / `sp_npc_listener` 用着 ——
        ///   按 `sp_guard` 查会把**市场摊贩刷新点**当成守门人。
        ///   ⇒ 只认守卫专属标签，且只取玩家 60 m 内的点。
        /// </summary>
        private List<GameEntity> FindGuardSpawnEntities()
        {
            var scene = Mission.Current?.Scene;
            if (scene == null) return null;

            // 顺序 = 优先级（先命中的那类就直接用）
            string[] tags =
            {
                "sp_guard_with_spear",   // 本项目目标场景 empire_town_g 的实际标签（prefab sp_guard）
                "sp_guard_patrol",
                "sp_guard_unarmed",
                "sp_guard_patrol_unarmed",
                "sp_guard_castle",
            };

            foreach (var tag in tags)
            {
                try
                {
                    var list = scene.FindEntitiesWithTag(tag)?
                        .Where(e => e != null && e.GlobalPosition.Distance(PlayerAnchor)
                                    <= StoryProbeTuning.Current.Gate.GuardSpawnRadiusMeters)
                        .ToList();
                    if (list != null && list.Count > 0)
                    {
                        _guardTagUsed = tag;
                        DebugLogger.Log($"[StoryProbe] guard spawns via tag '{tag}': {list.Count}");
                        return list;
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[StoryProbe] FindEntitiesWithTag({tag}) failed: {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// 找城门实体的位置（兜底用）。按 prefab 名逐级尝试；找不到返回 PlayerAnchor 作哨兵。
        /// 注意：场景里城门是 prefab 实例（`european_castle_gate_outer_l2` / `_l1`），
        /// 不一定带 tag，所以这里走**名字**而不是 tag。
        /// </summary>
        private Vec3 TryFindGatePosition()
        {
            var scene = Mission.Current?.Scene;
            if (scene == null) return PlayerAnchor;
            string[] names =
            {
                "european_castle_gate_outer_l2", "european_castle_gate_outer_l1",
                "european_castle_gate_outer",
                "empire_castle_gate_outer_l2", "empire_castle_gate_outer_l1",   // empire_town_m（吕卡隆）用的是这组
                "aserai_castle_gate_outer_l1",
                "castle_gate_outer_l2", "castle_gate_outer",
            };
            foreach (var n in names)
            {
                try
                {
                    var e = scene.FindEntityWithName(n);
                    if (e != null) return e.GlobalPosition;
                }
                catch { }
            }
            return PlayerAnchor;
        }

        /// <summary>
        /// 是不是本城的 **center** 地点。
        /// 首选 `CampaignMission.Current.Location`（引擎自己填的，最准）；它还没就绪时退到场景名比对
        /// ——  tavern / lordshall / keep 的场景名与 center 不同，不会误判（§3.1「只在 center 触发」）。
        /// </summary>
        private static bool IsCenterLocation(Settlement settlement)
        {
            try
            {
                var loc = CampaignMission.Current?.Location;
                if (loc != null) return loc.StringId == "center";

                var center = settlement?.LocationComplex?.GetLocationWithId("center");
                if (center == null) return false;
                int wallLevel = settlement.IsTown ? settlement.Town.GetWallLevel() : 1;
                string centerScene = center.GetSceneName(wallLevel);
                string currentScene = Mission.Current?.SceneName;
                return !string.IsNullOrEmpty(centerScene) && centerScene == currentScene;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] IsCenterLocation failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>场景锚点：玩家位置为准；守门人/目标点取两个 `sp_guard` 实体（近的 = 门外，远的 = 门内）。</summary>
        private void ResolveAnchors()
        {
            var player = Agent.Main;
            var gate = StoryProbeTuning.Current.Gate;
            string sceneName = Mission.Current?.SceneName;
            if (player == null || sceneName == null || gate.Layouts == null
                || !gate.Layouts.TryGetValue(sceneName, out var layout)
                || !TryLayoutPoint(layout?.Gate, out _gateCenter)
                || !TryLayoutPoint(layout?.Outside, out var outside))
            {
                Active = false;
                DebugLogger.Log($"[StoryProbe] no verified physical gate layout for '{sceneName}'; setup stopped, no guessed success");
                return;
            }
            Vec2 inward = (_gateCenter - outside).AsVec2;
            if (inward.LengthSquared <= 0.0001f || gate.HalfWidthMeters <= 0f || gate.InsideMeters <= 0f)
            {
                Active = false;
                DebugLogger.Log("[StoryProbe] invalid gate direction or bounds; setup stopped");
                return;
            }
            Forward = inward.Normalized();
            Vec3 fwd = new Vec3(Forward.x, Forward.y, 0f);
            BlockerPos = _gateCenter - fwd * gate.OutsideMeters;
            GoalPos = _gateCenter + fwd * gate.InsideMeters;
            // 原版 center 默认出生点在城内；序章明确从源场景门外站点开始。
            player.TeleportToPosition(outside);
            PlayerAnchor = outside;
            _lastGatePosition = outside;
            _gateGeometryReady = true;
            _crossedGate = false;
            UsedFallback = false;
            _guardTagUsed = "verified-layout:" + sceneName;
            DebugLogger.Log($"[StoryProbe] physical gate: outside={outside} gate={_gateCenter} blocker={BlockerPos} goal={GoalPos} width={gate.HalfWidthMeters * 2f:F1}");
        }

        private static bool TryLayoutPoint(float[] values, out Vec3 point)
        {
            point = Vec3.Zero;
            if (values == null || values.Length != 3 || values.Any(x => float.IsNaN(x) || float.IsInfinity(x))) return false;
            point = new Vec3(values[0], values[1], values[2]);
            return true;
        }

        private float GateAlong(Vec3 position)
        {
            Vec3 d = position - _gateCenter;
            return d.x * Forward.x + d.y * Forward.y;
        }

        private float GateLateral(Vec3 position)
        {
            Vec3 d = position - _gateCenter;
            return Math.Abs(d.x * Forward.y - d.y * Forward.x);
        }

        private void TrackGateCrossing(Agent player, float dt)
        {
            _crossingDoor = _crossingWall = false;
            if (!_gateGeometryReady || player == null) return;
            var gt = StoryProbeTuning.Current.Gate;
            _verticalGateSpeed = dt > 0.0001f ? (player.Position.z - _lastGatePosition.z) / dt : float.PositiveInfinity;
            float before = GateAlong(_lastGatePosition);
            float after = GateAlong(player.Position);
            if (after < 0f) _crossedGate = false;
            if (before < 0f && after >= 0f
                && player.Position.Distance(_lastGatePosition) <= gt.CrossingMaxStepMeters)
            {
                float t = -before / (after - before);
                Vec3 cross = _lastGatePosition + (player.Position - _lastGatePosition) * t;
                float lateral = GateLateral(cross);
                _crossingDoor = lateral <= gt.HalfWidthMeters;
                _crossingWall = lateral <= gt.ClimbHalfWidthMeters
                    && cross.z - _gateCenter.z > gt.ClimbHeightMeters;
                if (_crossingDoor || _crossingWall) _crossedGate = true;
            }
            _lastGatePosition = player.Position;
        }

        /// <summary>站位：相对玩家开场点的本地偏移（+f 朝城门，+r 右手侧）。</summary>
        public Vec3 OffsetPos(float forward, float right)
        {
            Vec3 f = new Vec3(Forward.x, Forward.y, 0);
            Vec3 r = new Vec3(Forward.y, -Forward.x, 0);
            return PlayerAnchor + f * forward + r * right;
        }

        // ═══════════════════════════════════════════════════════════════
        // 第一次进场：只有守门人 + 强制失败
        // ═══════════════════════════════════════════════════════════════

        private Agent _firstEnhancedGuard;
        private float _firstGuardBaseHealthLimit;
        private float _firstGuardHealthLimit;

        private static SkillObject[] FirstGuardCombatSkills()
            => new[] { DefaultSkills.OneHanded, DefaultSkills.TwoHanded, DefaultSkills.Polearm, DefaultSkills.Athletics };

        internal static bool RestoreFirstGuardSkills()
        {
            var d = StoryProbeState.Data;
            if (string.IsNullOrEmpty(d.FirstGuardBoostHeroId)) return true;
            try
            {
                var hero = MBObjectManager.Instance?.GetObject<Hero>(d.FirstGuardBoostHeroId)
                    ?? Hero.AllAliveHeroes.FirstOrDefault(h => h != null && h.StringId == d.FirstGuardBoostHeroId);
                if (hero == null || d.FirstGuardOriginalSkills == null || d.FirstGuardOriginalSkills.Count == 0)
                {
                    DebugLogger.Log("[StoryProbe] first guard skill backup cannot yet be restored; keep backup");
                    return false;
                }
                foreach (var skill in FirstGuardCombatSkills())
                    if (d.FirstGuardOriginalSkills.TryGetValue(skill.StringId, out int original))
                        hero.SetSkillValue(skill, original);
                DebugLogger.Log($"[StoryProbe] first guard original skills restored: hero={hero.StringId}");
                d.FirstGuardBoostHeroId = "";
                d.FirstGuardOriginalSkills.Clear();
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] first guard skill restore failed: {ex.Message}");
                return false;
            }
        }

        private static bool BoostFirstGuardSkills(Hero hero)
        {
            if (hero == null || !RestoreFirstGuardSkills()) return false;
            var d = StoryProbeState.Data;
            var tuning = StoryProbeTuning.Current.Intro;
            d.FirstGuardOriginalSkills ??= new Dictionary<string, int>();
            d.FirstGuardOriginalSkills.Clear();
            d.FirstGuardBoostHeroId = hero.StringId;
            try
            {
                foreach (var skill in FirstGuardCombatSkills())
                {
                    int original = hero.GetSkillValue(skill);
                    d.FirstGuardOriginalSkills[skill.StringId] = original;
                    int player = Hero.MainHero?.GetSkillValue(skill) ?? 0;
                    int target = Math.Max(original, Math.Max(Math.Max(0, tuning.FirstGuardMinCombatSkill),
                        player + Math.Max(0, tuning.FirstGuardSkillAdvantage)));
                    hero.SetSkillValue(skill, target);
                    DebugLogger.Log($"[StoryProbe] first guard real skill: {skill.StringId} {original}->{target} player={player}");
                }
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] first guard skill boost failed: {ex.Message}");
                RestoreFirstGuardSkills();
                return false;
            }
        }

        private void BoostFirstGuardAgent(Agent guard)
        {
            if (!AgentControlHelper.SafeIsActive(guard)) return;
            guard.UpdateAgentProperties(); // Hero 技能变更交回原版模型算攻速、移动和格挡，不直接改伤害。
            _firstEnhancedGuard = guard;
            _firstGuardBaseHealthLimit = guard.BaseHealthLimit;
            _firstGuardHealthLimit = guard.HealthLimit;
            float multiplier = StoryProbeTuning.Current.Intro.FirstGuardHealthMultiplier;
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 1f) multiplier = 1f;
            guard.BaseHealthLimit = _firstGuardBaseHealthLimit * multiplier;
            guard.HealthLimit = _firstGuardHealthLimit * multiplier;
            guard.Health = guard.HealthLimit;
            DebugLogger.Log($"[StoryProbe] first guard real combat profile: hp={guard.Health}/{guard.HealthLimit} " +
                $"armor={AgentStatsHelper.GetArmorProfile(guard)} originalLimit={_firstGuardHealthLimit}");
        }

        private void RestoreFirstGuardEnhancement()
        {
            RestoreFirstGuardSkills();
            try
            {
                if (AgentControlHelper.SafeIsActive(_firstEnhancedGuard) && _firstEnhancedGuard.Mission == Mission)
                {
                    float fraction = _firstEnhancedGuard.HealthLimit > 0f
                        ? _firstEnhancedGuard.Health / _firstEnhancedGuard.HealthLimit : 1f;
                    _firstEnhancedGuard.BaseHealthLimit = _firstGuardBaseHealthLimit;
                    _firstEnhancedGuard.HealthLimit = _firstGuardHealthLimit;
                    _firstEnhancedGuard.Health = Math.Max(0f, Math.Min(_firstGuardHealthLimit, fraction * _firstGuardHealthLimit));
                    _firstEnhancedGuard.UpdateAgentProperties();
                }
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] first guard agent profile restore failed: {ex.Message}"); }
            finally { _firstEnhancedGuard = null; }
        }

        private void SetupFirstAttempt()
        {
            DebugLogger.Log("[StoryProbe] setup first attempt (blocker only)");
            // 原值必须先恢复再备份，避免读档/重复进场把已经增强的技能当作原值。
            RestoreFirstGuardEnhancement();
            var firstGuardHero = EnsureStoryHero(StoryProbeDefs.GetNpc("blocker"));
            if (firstGuardHero == null || !BoostFirstGuardSkills(firstGuardHero)) return;
            var blocker = SpawnNpc("blocker", BlockerPos, civilian: false);
            if (blocker != null)
            {
                BoostFirstGuardAgent(blocker);
                AgentControlHelper.HoldInPlace(blocker);
                AgentControlHelper.FaceToActor(blocker, Agent.Main);
            }
            // 检查实际 Agent，不把 SpawnAgent 返回非空当成出生点/可见性验收通过。
            Schedule(1.5f, () => DebugLogger.Log("[StoryProbe] first guard live: " + DescribeActor("blocker")));

        }

        // 硬闯实际败退后才登记拘押缘由，不能进场先加罪让原版守卫抢在正常质问前开打。
        private void ApplyFirstArrestBounty()
        {
            try
            {
                var kingdom = Settlement.CurrentSettlement?.MapFaction as Kingdom;
                if (kingdom != null && kingdom.MainHeroCrimeRating <= 0.1f)
                {
                    ChangeCrimeRatingAction.Apply(kingdom, StoryProbeTuning.Current.Crime.SmallBountyAtEntry, true);
                    DebugLogger.Log("[StoryProbe] small bounty applied (+15)");
                }
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] apply bounty failed: {ex.Message}"); }
        }

        /// <summary>
        /// 这个人是不是**真的没家伙**（四个武器槽全空）。
        /// 🔴 不能用"手上正握着什么"（`V.MainWpn`）判：他把刀**收着**的时候也可能返回 None ⇒
        ///    会出现"他明明挂着刀，却白送一条让他让开的路"。扒窃把槽位清空才是真缴械。
        /// </summary>
        public static bool IsReallyDisarmed(Agent agent)
        {
            if (agent == null) return false;
            try
            {
                for (var i = EquipmentIndex.WeaponItemBeginSlot; i <= EquipmentIndex.Weapon3; i++)
                {
                    if (!agent.Equipment[i].IsEmpty) return false;
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>玩家现在是被这座城通缉的状态吗（§5.2 遮面/冒充的前提）。</summary>
        public static bool IsPlayerWanted()
        {
            try
            {
                var kingdom = Settlement.CurrentSettlement?.MapFaction as Kingdom;
                return kingdom != null && kingdom.MainHeroCrimeRating > 0.1f;
            }
            catch { return false; }
        }

        private void TickFirstAttempt()
        {
            var player = Agent.Main;
            var blocker = GetAgent("blocker");
            if (player == null || blocker == null || Settings.Instance.IsInteractionDisabled()) return;
            if (AgentBrain.IsRoped(blocker) || AgentBrain.IsHeldByStatus(blocker))
            {
                _firstInterceptRequested = false;
                return; // 解除压制后可重新追近质问，不把等待当作硬闯或败退。
            }

            if (_forcedDefeatQueued)
            {
                // 事件尚未被战斗动作接受就取消/丢弃时允许重试；超时绝不判败。
                if (_firstFightStarted && !_firstDefeatResolved
                    && _time - _firstFightRequestedAt > StoryProbeTuning.Current.Intro.FirstFightStartTimeoutSec
                    && AttackTriggerMissionLogic.Instance?.HasDuelPair(blocker, player) != true)
                {
                    _firstFightStarted = false;
                    DebugLogger.Log("[StoryProbe] first fight was not accepted; retry only, no defeat");
                }
                // 对话彻底结束后才交给战斗脑，不能在对话/伤害回调里改队或播倒地。
                if (!_firstFightStarted && Campaign.Current?.ConversationManager?.IsConversationInProgress != true)
                    StartFirstGuardFight();
                return;
            }

            if (Campaign.Current?.ConversationManager?.IsConversationInProgress == true) return;
            var brain = AgentAIController.GetBrainForAgent(blocker);
            if (brain == null || brain.IsPerforming || brain.IsInCombat) return;
            bool triedToPass = _gateGeometryReady
                && GateAlong(player.Position) > StoryProbeTuning.Current.Gate.PastMeters
                && GateLateral(player.Position) <= StoryProbeTuning.Current.Gate.ClimbHalfWidthMeters;
            if (triedToPass && !_firstWasPastGate) _blockerAutoTalkArmed = true;
            _firstWasPastGate = triedToPass;
            float distance = player.Position.Distance(blocker.Position);
            if (distance > AutoTalkRearmRadius) _blockerAutoTalkArmed = true;
            if ((triedToPass || distance <= AutoTalkRadius) && _blockerAutoTalkArmed && !_firstInterceptRequested)
            {
                _firstInterceptRequested = true;
                _blockerAutoTalkArmed = false;
                AgentAIController.Instance?.SendEventToAgent(blocker, "event_story_intercept", player,
                    "blocker_first", new Action<Agent>(ready =>
                    {
                        if (!Active || Current != this || Phase != StoryProbePhase.FirstAttempt) return;
                        _firstInterceptRequested = false;
                        if (ready == null) { _blockerAutoTalkArmed = true; return; }
                        StartStoryConversation(ready, "blocker_first");
                        if (!ConversationEntryPatch.IsConversationActive()) _blockerAutoTalkArmed = true;
                    }));
                DebugLogger.Log("[StoryProbe] Brain questioning requested; no combat for approaching/crossing");
            }
        }

        public void NotifyFirstGuardQuestioned()
        {
            if (!Active || Phase != StoryProbePhase.FirstAttempt) return;
            _firstGuardQuestioned = true;
            DebugLogger.Log("[StoryProbe] first guard questioning entered; hard-entry choice is now available");
        }

        /// <summary>兼容旧对话动作名；仅在正常质问后明确选择硬闯时请求交战。</summary>
        public void PlayForcedDefeat()
        {
            if (!Active || Phase != StoryProbePhase.FirstAttempt || !_firstGuardQuestioned || _forcedDefeatQueued) return;
            _forcedDefeatQueued = true;
            DebugLogger.Log("[StoryProbe] hard-entry chosen after questioning; await actual combat defeat");
        }

        private void StartFirstGuardFight()
        {
            var player = Agent.Main;
            var blocker = GetAgent("blocker");
            if (player == null || blocker == null || AgentAIController.GetBrainForAgent(blocker) == null
                || AttackTriggerMissionLogic.Instance == null)
            {
                DebugLogger.Log("[StoryProbe] first fight not started: player/guard/brain/arbiter missing; no defeat permitted");
                return;
            }
            var brain = AgentAIController.GetBrainForAgent(blocker);
            if (Settings.Instance.IsInteractionDisabled() || brain.IsPerforming || brain.IsInCombat
                || AgentBrain.IsRoped(blocker) || AgentBrain.IsHeldByStatus(blocker)) return;
            _firstFightStarted = true;
            _firstFightRequestedAt = _time;
            DebugLogger.Log($"[StoryProbe] first real fight: guard={blocker.Index} player={player.Index} distance={blocker.Position.Distance(player.Position):F2}");
            int receipt = ++_duelSerial;
            _firstDuelReceipt = receipt;
            _firstDuelCallback = new Action<Agent>(loser =>
            {
                if (Current != this || _firstDuelReceipt != receipt) return;
                OnFirstDuelResolved(loser);
            });
            AgentAIController.Instance.SendEventToAgent(blocker, "duel", player, true, _firstDuelCallback);
        }

        private void OnFirstDuelResolved(Agent loser)
        {
            if (!Active || Current != this || Phase != StoryProbePhase.FirstAttempt || _firstDefeatResolved) return;
            _firstDuelCallback = null;
            _firstDuelReceipt = 0;
            if (loser == null || loser != Agent.Main)
            {
                _forcedDefeatQueued = false;
                _firstFightStarted = false;
                DebugLogger.Log("[StoryProbe] first fight cancelled without player defeat; no jail transition");
                return;
            }
            _firstDefeatResolved = true;
            // 事实先持久化：提前离场/保存不能把真实败退丢在被清掉的动画队列里。
            // 只改战役 Phase；本 Host 的 instance Phase 仍为 FirstAttempt，不能在 center tick 牢内换装。
            StoryProbeBehavior.RequestJailInquiry();
            ApplyFirstArrestBounty();
            // 共享仲裁已经在正常 tick 栈完整停战；现在才允许倒地演出和随后入牢。
            DebugLogger.Log("[StoryProbe] first guard hits resolved -> player down -> queue prison");
            // 先安排转场，视觉层异常也不能永久冻结已经真实败退的玩家。
            Schedule(Math.Max(0.1f, StoryProbeTuning.Current.Intro.ForcedDefeatHoldSec), () =>
            {
                DebugLogger.Log("[StoryProbe] defeated after actual combat -> end center mission; real prison pending");
                Mission.EndMission();
            });
            try
            {
                EnsureStage().RunAtom(new StageBeat { Cmd = "player_freeze" });
                _defeatPlayer = loser;
                _defeatAnim = AnimMachineRegistry.Create("story-probe-defeat", new DefeatAnimContext());
                _defeatAnim?.Force(loser, "fall");
                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveText("LWN_story_narr_ko", "Everything goes dark.")));
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] defeat presentation failed: {ex.Message}");
                _stage?.Stop("defeat presentation exception");
            }
        }

        private void TickJailScene()
        {
            if (StoryProbeState.Phase != StoryProbePhase.Jail || !StoryProbeSettings.Enabled)
            {
                _stage?.Stop("jail interrupted");
                Mission.EndMission();
                return;
            }
            var player = Agent.Main;
            if (!AgentControlHelper.SafeIsActive(player))
            {
                if (_time > 5f)
                {
                    StoryProbeBehavior.AbortPrisonTransition("prison player did not spawn");
                    Mission.EndMission();
                }
                return;
            }
            if (!_jailPlaced)
            {
                var handler = Mission.GetMissionBehavior<SandBox.Missions.MissionLogics.MissionAgentHandler>();
                var machines = handler?.GetAllUsablePointsWithTag("sp_prisoner");
                bool placed = false;
                EnsureStage().RunAtom(new StageBeat { Cmd = "player_freeze" });
                if (machines != null)
                {
                    foreach (var machine in machines)
                    {
                        foreach (var point in machine.StandingPoints)
                        {
                            if (!AgentControlHelper.PlaceAtStandingPoint(player, point)) continue;
                            placed = true;
                            break;
                        }
                        if (placed) break;
                    }
                }
                if (!placed || !ConfiscatePlayerBelongings())
                {
                    _stage.Stop("prison setup failed");
                    StoryProbeBehavior.AbortPrisonTransition("no free valid prisoner standing point or clothing unavailable");
                    Mission.EndMission();
                    return;
                }
                AgentControlHelper.RefreshSpawnEquipment(player, Hero.MainHero.CivilianEquipment);
                PlayerAnchor = player.Position;
                StoryProbeState.Data.JailSceneEntered = true;
                _jailPlaced = true;
                _stage.Stop("player placed inside jail");
                _jailPromptAt = _time + Math.Max(0.1f, StoryProbeTuning.Current.Intro.JailWakeDelaySec);
                DebugLogger.Log($"[StoryProbe] jail cell entered: location={CampaignMission.Current?.Location?.StringId} player={player.Position} controller={player.Controller} stripped={StoryProbeState.Data.Stripped}");
            }
            if (!StoryProbeState.Data.JailDone && _jailPromptAt >= 0 && _time >= _jailPromptAt)
            {
                _jailPromptAt = _time + 5f;
                StoryProbeBehavior.ShowJailInquiry();
            }
        }

        public void CompleteJailDisguise()
        {
            if (!Active || Phase != StoryProbePhase.Jail || !StoryProbeState.Data.JailSceneEntered
                || StoryProbeState.Data.JailDone || !AgentControlHelper.SafeIsActive(Agent.Main)) return;
            try
            {
                if (!StripPlayer(advancePhase: false)) return;
                AgentControlHelper.RefreshSpawnEquipment(Agent.Main, Hero.MainHero.CivilianEquipment);
                DebugLogger.Log($"[StoryProbe] jail disguise equipped in live scene: player={Agent.Main.Position}");
                StoryProbeBehavior.QueueSecondFromJail();
                Schedule(0.8f, () => Mission.EndMission());
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] jail disguise failed: {ex.Message}");
                StoryProbeBehavior.AbortPrisonTransition("jail disguise refresh exception");
                Schedule(0.1f, () => Mission.EndMission());
            }
            finally { _stage?.Stop("jail disguise complete or failed"); }
        }

        // ═══════════════════════════════════════════════════════════════
        // 第二次进场：四家 + 6 起事件 + 随从 + 目标点
        // ═══════════════════════════════════════════════════════════════

        private void SetupSecondPhase()
        {
            DebugLogger.Log("[StoryProbe] setup second phase (full prologue)");

            // 🔴 演出层（2026-11-13）：台本 StoryStage.json 驱动「布景 → 摆人 → 玩家出场」。
            //    起不来（文件缺/解析失败）⇒ 退回旧的硬编码偏移，保证不失能。
            _stage ??= new StoryStageRunner(this);
            bool staged = _stage.Begin("StoryStage");

            // 四家代表（表里有委托的那些）—— 台本里排过位的跳过，避免"先站一次再被挪一次"的闪烁
            foreach (var def in StoryProbeDefs.Representatives)
            {
                if (staged && _stage.ScriptPlacesActor(def.Id)) continue;
                SpawnNpc(def.Id, OffsetPos(def.OffsetForward, def.OffsetRight), def.Civilian);
            }

            // 守门人 + 仇家（全部从表里推，代码里不再列名单）
            var blockerDef = StoryProbeDefs.Blocker;
            if (blockerDef != null && !(staged && _stage.ScriptPlacesActor(blockerDef.Id)))
                SpawnNpc(blockerDef.Id, BlockerPos, blockerDef.Civilian);

            foreach (var def in StoryProbeDefs.QuestTargets)
            {
                if (staged && _stage.ScriptPlacesActor(def.Id)) continue;
                SpawnNpc(def.Id, OffsetPos(def.OffsetForward, def.OffsetRight), def.Civilian);
            }

            // 门已经开过（上一趟进来时开的）→ 这次直接是通的
            if (StoryProbeState.Data.GateOpened) _gateOpen = true;

            // 市井导演（常驻动作 / 桥段 / 事件循环）
            _director = new StreetLifeDirector(this);
            _director.Begin();

            // 随从 + 3 句
            EnsureCompanion();

            // 同上：剧情旁白不受调试开关门控
            InformationManager.DisplayMessage(new InformationMessage(
                LWNTextHelper.ResolveText("LWN_story_narr_second_enter",
                    "The gate again. Nothing has changed — except you have nothing left to lose.")));
        }

        private void TickSecondPhase(float dt)
        {
            var player = Agent.Main;

            if (_stage?.Active == true) return; // 镜头演出占用期间不识破、不开战、不判通关。
            TrackGateCrossing(player, dt);
            PumpStoryFights();
            var b = GetAgent("blocker");
            var brain = b != null ? AgentAIController.GetBrainForAgent(b) : null;
            if (!_gateOpen && player != null && !_recognized && IsPlayerWanted()
                && brain != null && !brain.IsPerforming && !brain.IsInCombat
                && !AgentBrain.IsRoped(b) && !AgentBrain.IsHeldByStatus(b) && !AgentBrain.IsKnockedOut(b)
                && NpcSightSystem.CanAgentSeeTarget(b, player, StoryProbeTuning.Current.Gate.RecognizeDistanceMeters))
            {
                bool covered = IsWearingStoryDisguise(false);
                bool noble = IsWearingStoryDisguise(true);
                if (covered && !_maskChecked)
                {
                    _maskChecked = true;
                    var roll = StoryProbeDefs.RollCheck("blocker", StoryAbility.Charm);
                    _maskPassed = roll.Success;
                    InformationManager.DisplayMessage(new InformationMessage(roll.PlayerMessage()));
                }
                if (!noble && !(covered && _maskPassed))
                {
                    _recognized = true;
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveText("LWN_story_recognized", "He looks at your face, then at the paper on the gate. Then he draws.")));
                    SayOnAgent(b, "LWN_story_blocker_recognize", "That's him. On the gate.", SpeechPriority.Warning);
                    RequestStoryFight("blocker");
                }
            }
            if (!_gateOpen && player != null && brain?.IsPerforming != true) CheckHandsOnGate(player);
            if (!_goalReached && player != null)
            {
                EnforceGateBarrier(player, dt);
                if (_gateGeometryReady && _gateOpen && _crossedGate
                    && GateAlong(player.Position) >= StoryProbeTuning.Current.Gate.PastMeters
                    && player.Position.Distance(GoalPos) < StoryProbeTuning.Current.Gate.GoalRadiusMeters)
                    CompletePrologue(); // 财物成功归还后由该方法提交 _goalReached。
            }

            // 随从 3 句（间隔 2.5 秒）
            if (_introLine >= 0 && _introLine < 3 && _nextIntroAt > 0f && _time >= _nextIntroAt)
            {
                SayCompanionLine(_introLine);
                _introLine++;
                _nextIntroAt = _introLine < 3 ? _time + 2.5f : -1f;
                if (_introLine >= 3) StoryProbeState.Data.IntroPlayed = true;
            }

            _director?.Tick(dt);
            TickQuests();
        }

        /// <summary>伪装是取得资格且实际穿戴；存档旗标不能代替现场衣装。</summary>
        public static bool IsWearingStoryDisguise(bool noble)
        {
            var d = StoryProbeState.Data;
            var player = Agent.Main;
            if (player == null || !AgentControlHelper.SafeIsActive(player)
                || (noble ? !d.NobleDisguise : !d.Disguised)) return false;
            var expected = StoryProbeDefs.FindItem(noble ? "fine_town_tunic" : "head_wrapped");
            var slot = noble ? EquipmentIndex.Body : EquipmentIndex.Head;
            return expected != null && player.SpawnEquipment[slot].Item == expected;
        }

        /// <summary>对话动作只排请求，等原版对话退出再交给 Brain。</summary>
        public bool RequestStoryFight(string npcId)
        {
            var target = GetAgent(npcId);
            if (!Active || Current != this || Phase != StoryProbePhase.Second || target == null
                || !AgentControlHelper.SafeIsActive(target)) return false;
            _fightRequests.Add(npcId);
            return true;
        }

        private void PumpStoryFights()
        {
            if (Campaign.Current?.ConversationManager?.IsConversationInProgress == true || _stage?.Active == true) return;
            var player = Agent.Main;
            if (player == null || AttackTriggerMissionLogic.Instance == null || AgentAIController.Instance == null) return;
            foreach (var npcId in _fightRequests.ToArray())
            {
                var target = GetAgent(npcId);
                var brain = target != null ? AgentAIController.GetBrainForAgent(target) : null;
                if (target == null || !AgentControlHelper.SafeIsActive(target)) { _fightRequests.Remove(npcId); continue; }
                if (brain == null || brain.IsPerforming || AgentBrain.IsRoped(target) || AgentBrain.IsHeldByStatus(target)) continue;
                if (AttackTriggerMissionLogic.Instance.HasDuelPair(target, player)) continue;
                if (_fightStartedAt.TryGetValue(npcId, out float started)
                    && _time - started <= StoryProbeTuning.Current.Intro.FirstFightStartTimeoutSec) continue;
                if (brain.IsInCombat) continue; // 不抢已有原版/外部战斗；共享回执负责本次收场。
                _fightStartedAt[npcId] = _time;
                int receipt = ++_duelSerial;
                _duelTokens[npcId] = receipt;
                _duelCallbacks[npcId] = new Action<Agent>(loser => OnStoryDuelResolved(npcId, target, player, receipt, loser));
                AgentAIController.Instance.SendEventToAgent(target, "duel", player, false, _duelCallbacks[npcId]);
            }
        }

        private void OnStoryDuelResolved(string npcId, Agent target, Agent player, int receipt, Agent loser)
        {
            if (!Active || Current != this || Phase != StoryProbePhase.Second
                || !_duelTokens.TryGetValue(npcId, out int current) || current != receipt) return;
            _duelTokens.Remove(npcId);
            _duelCallbacks.Remove(npcId);
            _fightStartedAt.Remove(npcId);
            _fightRequests.Remove(npcId);
            if (loser != target) return; // null=取消，玩家落败不假开门、不假交差。
            if (npcId == "blocker") _pendingBlockerDown = true;
            foreach (var def in StoryProbeDefs.Representatives)
                if (def.QuestTargetNpcId == npcId && StoryProbeState.IsQuestAccepted(def.Id)
                    && !StoryProbeState.IsQuestDone(def.Id)) _pendingQuestDone.Add(def.Id);
            DebugLogger.Log($"[StoryProbe] shared duel receipt: target '{npcId}' really lost");
        }

        /// <summary>委托完成检测（§4.1）：仇家被打倒（击晕/死亡）即算"教训到位"。</summary>
        private void TickQuests()
        {
            foreach (var def in StoryProbeDefs.Representatives)
            {
                if (string.IsNullOrEmpty(def.QuestTargetNpcId)) continue;
                if (!StoryProbeState.IsQuestAccepted(def.Id)) continue;
                if (StoryProbeState.IsQuestDone(def.Id)) continue;

                var target = GetAgent(def.QuestTargetNpcId);
                // 🔴 target == null 不能算完成（生成失败会把委托瞬间"办完"）
                if (target == null) continue;
                bool downed = !AgentControlHelper.SafeIsActive(target)
                              || target.Health <= 0f
                              || AgentBrain.IsKnockedOut(target);
                if (!downed) continue;

                StoryProbeState.CompleteQuest(def.Id);
                // 原生 Quest 同步：任务日志里记一条"回去交差"
                try { PrologueQuestLauncher.FindActive(def.Id)?.MarkObjectivesDone(); }
                catch (Exception qex) { DebugLogger.Log($"[StoryProbe] prologue quest mark done failed: {qex.Message}"); }
                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveCompound("LWN_story_narr_quest_done",
                        "It is done. Go back and tell {NAME}.", ("NAME", NpcName(def.Id)))));
                DebugLogger.Log($"[StoryProbe] quest '{def.Id}' target '{def.QuestTargetNpcId}' downed -> ready to turn in");
            }
        }

        internal static string NpcName(string npcId)
        {
            var def = StoryProbeDefs.GetNpc(npcId);
            return def == null ? npcId : LWNTextHelper.ResolveText(def.NameKey, def.NameFallback);
        }

        /// <summary>`custom.story_probe intro`：重播随从的 3 句。</summary>
        public void ReplayIntro()
        {
            _introLine = 0;
            _nextIntroAt = _time + 0.5f;
            StoryProbeState.Data.IntroPlayed = false;
        }

        /// <summary>
        /// 守门人此刻是否看着这名玩家（朝向点积 + 25 m 距离）。
        /// 🔴 与"潜行"用同一套判定（`CheckHandsOnGate` 里那条背后判定），口径必须一致，
        /// 否则会出现"蹲着背后算潜行、翻墙背后却算被看见"这种自相矛盾。
        /// </summary>
        private bool IsBlockerWatching(Agent player)
        {
            return NpcSightSystem.CanAgentSeeTarget(GetAgent("blocker"), player,
                StoryProbeTuning.Current.Gate.RecognizeDistanceMeters);
        }

        /// <summary>
        /// 「手上的事」——不需要跟守门人说话的四条路（对齐需求 §5.3 / §5.4）。
        ///
        ///   ① **潜行**：实际蹲行从门外穿过、未被看见、接近地面，再做一次技巧检定。
        ///   ② **绑走**：守门人被钩索绑住（`GrappleLogic.BoundTarget == 他`）→ 门等于没人看。
        ///   ③ **扛走**：同上，被扛在肩上（`CarriedTarget`）。
        ///   ④ **扒掉家伙**：他的主手武器槽被扒空（扒窃条的产物）→ 他打不过你，门就守不住。
        ///
        /// 🔴 全用现成系统（GrappleLogic / 扒窃 / CrouchMode），不新建任何武器或姿态逻辑。
        /// </summary>
        private void CheckHandsOnGate(Agent player)
        {
            var blocker = GetAgent("blocker");
            if (blocker == null || !AgentControlHelper.SafeIsActive(blocker)) return;

            try
            {
                // ⓪ 击晕：KnockoutFlow 不动血量（只播倒地 + 锁行为），所以 OnAgentHit 抓不到 —— 这里补一刀
                if (AgentBrain.IsKnockedOut(blocker))
                {
                    DebugLogger.Log("[StoryProbe] hands: blocker KNOCKED OUT -> gate open");
                    OpenGate("force", "LWN_story_gate_knocked", "He's on the ground. Nobody is watching the gate.");
                    ResolveApproach("force", "dockhands", -20, true, null);
                    return;
                }

                // ② / ③ 绑走 / 扛走
                var gl = GrappleLogic.Current;
                if (gl != null)
                {
                    bool bound = ReferenceEquals(gl.BoundTarget, blocker);
                    bool carried = ReferenceEquals(gl.CarriedTarget, blocker);
                    if (bound || carried)
                    {
                        // 代价：人是码头帮的，动了他就等于跟码头帮结了这个梁子（隐患记一条）
                        StoryProbeState.AddHazard("hazard_dragged_the_guard");
                        DebugLogger.Log($"[StoryProbe] hands: blocker {(bound ? "BOUND" : "CARRIED")} -> gate open");
                        OpenGate("guile", "LWN_story_gate_dragged", "Nobody is watching the gate now.");
                        ResolveApproach("guile", "nightwalkers", -20, true, "hazard_dragged_the_guard");
                        return;
                    }
                }

                // ④ 扒掉他的家伙（扒窃条扒走主手武器 → 主手槽空）
                if (IsReallyDisarmed(blocker) && !_blockerWasDisarmed)
                {
                    _blockerWasDisarmed = true;
                    DebugLogger.Log("[StoryProbe] hands: blocker DISARMED (all weapon slots empty)");
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveText("LWN_story_blocker_disarmed", "He's got nothing in his hand now.")));
                }

                // 潜行必须本帧从门外实际通过有限门区，且视线没有发现；蹲在背后不算通关。
                if (!_crossingDoor || !player.CrouchMode || IsBlockerWatching(player)
                    || Math.Abs(player.Position.z - _gateCenter.z) > StoryProbeTuning.Current.Gate.GroundToleranceMeters) return;
                var roll = StoryProbeDefs.RollCheck("blocker", StoryAbility.Guile);
                InformationManager.DisplayMessage(new InformationMessage(roll.PlayerMessage()));
                if (!roll.Success)
                {
                    SayOnAgent(blocker, "LWN_story_blocker_bar", "Papers.", SpeechPriority.Warning);
                    return;
                }
                OpenGate("guile", "LWN_story_gate_sneaked", "You slip past while his back is turned.");
                ResolveApproach("guile", "nightwalkers", 0, false, null);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] CheckHandsOnGate failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 门没开时的硬拦（R1，2026-11-13）。
        ///
        /// 为什么需要：场景里那道城门**本来就能走过去**，守门人只是个 NPC、挡不住人。
        /// 少了这道判据，「绕过守门人走进去」= 零成本通关（违反铁律 12：禁止零成本最优解）。
        ///
        /// 规则：只在已核对的有限门区拦住未获通行的玩家，不靠无限半平面或靠近目标判通关。
        /// 翻墙必须实际钩索尝试、技巧成功、未被看见、越过墙段且稳定落地；途中不传送玩家。
        /// </summary>
        private void EnforceGateBarrier(Agent player, float dt)
        {
            if (_gateOpen || player == null || !_gateGeometryReady) return;
            var gt = StoryProbeTuning.Current.Gate;
            float along = GateAlong(player.Position);
            bool pulling = GrappleLogic.GetSession(player)?.IsPullingSelf == true;
            if (!_climbAttempt && pulling && along < 0f && GateLateral(player.Position) <= gt.ClimbHalfWidthMeters)
            {
                _climbAttempt = true;
                _climbRolled = _climbSucceeded = _climbSeen = _climbCrossed = false;
                _gateGroundStable = 0f;
            }
            if (_climbAttempt)
            {
                if (!_climbSeen && IsBlockerWatching(player))
                {
                    _climbSeen = true;
                    StoryProbeState.AddHazard("hazard_climb_seen");
                    SayOnAgent(GetAgent("blocker"), "LWN_story_climb_seen", "Get down! Get down from there!", SpeechPriority.Warning);
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveText("LWN_story_climb_seen_narr", "He saw you go over. By the time you land, the gate is watching for you.")));
                }
                if (!_climbRolled && player.Position.z - _gateCenter.z > gt.ClimbHeightMeters)
                {
                    _climbRolled = true;
                    var roll = StoryProbeDefs.RollCheck("blocker", StoryAbility.Guile);
                    _climbSucceeded = roll.Success; // 失败保持失败，本次尝试不能下一帧变成功。
                    InformationManager.DisplayMessage(new InformationMessage(roll.PlayerMessage()));
                    if (!_climbSucceeded)
                    {
                        StoryProbeState.AddHazard("hazard_climb_missed");
                        SayOnAgent(GetAgent("blocker"), "LWN_story_climb_missed", "What was that?", SpeechPriority.Chat);
                    }
                }
                if (_crossingWall) _climbCrossed = true;
                if (!pulling && IsAtGateGround(player, dt))
                {
                    _climbAttempt = false;
                    if (_climbRolled && _climbSucceeded && !_climbSeen && _climbCrossed && along >= gt.PastMeters)
                    {
                        OpenGate("guile", "LWN_story_gate_climbed", "You came over the wall.");
                        ResolveApproach("guile", "nightwalkers", 0, false, null);
                        return;
                    }
                }
                else return; // 正在真实拉拽/下落时不能争抢其身体或传送。
            }
            if (along <= gt.PastMeters || GateLateral(player.Position) > gt.ClimbHalfWidthMeters
                || _time < _nextBarrierAt) return;
            var blocker = GetAgent("blocker");
            if (blocker != null && AgentAIController.GetBrainForAgent(blocker)?.IsInCombat == true) return;
            _nextBarrierAt = _time + gt.PushBackCooldownSec;
            Vec3 outside = _gateCenter - new Vec3(Forward.x, Forward.y, 0f) * gt.PushBackMeters;
            player.TeleportToPosition(outside);
            _lastGatePosition = outside;
            _crossedGate = false;
            // 朝向交给 Brain 质问/反应，屏障不再另写守卫身体。
            SayOnAgent(blocker, "LWN_story_blocker_bar", "Papers.", SpeechPriority.Warning);
            InformationManager.DisplayMessage(new InformationMessage(
                LWNTextHelper.ResolveText("LWN_story_gate_blocked", "He pushes you back.")));
        }

        private bool IsAtGateGround(Agent player, float dt)
        {
            var scene = Mission.Current?.Scene;
            if (scene == null) { _gateGroundStable = 0f; return false; }
            float ground = V.GroundHeight(scene, player.Position);
            var gt = StoryProbeTuning.Current.Gate;
            bool contact = !float.IsNaN(ground) && !float.IsInfinity(ground)
                && ground > -1e5f && ground < 1e5f
                && Math.Abs(player.Position.z - ground) <= gt.LandingToleranceMeters
                && Math.Abs(_verticalGateSpeed) <= gt.LandingVerticalSpeedMeters;
            _gateGroundStable = contact ? _gateGroundStable + Math.Max(0f, dt) : 0f;
            return contact && _gateGroundStable >= gt.LandingStableSec;
        }

        private float _belongingsRestoreRetryAt;
        /// <summary>财物实际归还成功才提交完成；缺资源时保留债务并允许重试。</summary>
        private void CompletePrologue()
        {
            if (StoryProbeState.Data.CancellationPending) { _goalReached = false; return; }
            if (_time < _belongingsRestoreRetryAt) { _goalReached = false; return; }
            try
            {
                DebugLogger.Log($"[StoryProbe] goal reached (approach={StoryProbeState.Data.ChosenApproach}) -> restoring belongings");
                if (!RestorePlayerBelongings())
                {
                    _goalReached = false;
                    _belongingsRestoreRetryAt = _time + 2f;
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveText("LWN_info_equipment_error") + " " + LWNTextHelper.ResolveText("LWN_plan_btn_retry")));
                    return;
                }
                StoryProbeState.Data.GoalReached = true;
                StoryProbeState.Phase = StoryProbePhase.Done;
                StoryProbeState.SetGoal("done");

                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveText("LWN_story_narr_done",
                        "You are inside. Whatever happens from here, the gate is behind you.")));
                MBInformationManager.AddQuickInformation(new TextObject(
                    LWNScreenGoalText.Resolve()));
            }
            catch (Exception ex)
            {
                _goalReached = false;
                _belongingsRestoreRetryAt = _time + 2f;
                DebugLogger.Log($"[StoryProbe] CompletePrologue failed: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 生成 / 取人
        // ═══════════════════════════════════════════════════════════════

        private Agent SpawnNpc(string npcId, Vec3 pos, bool civilian, bool repositionExisting = true)
        {
            // 第二次沿用同一个 Hero：读档或旧 Mission 没能收场时也不能带着首遇增强出生。
            if (Phase != StoryProbePhase.FirstAttempt && !RestoreFirstGuardSkills()) return null;
            var def = StoryProbeDefs.GetNpc(npcId);
            if (def == null) return null;

            Hero hero = EnsureStoryHero(def);
            if (hero == null)
            {
                DebugLogger.Log($"[StoryProbe] spawn '{npcId}' failed: no hero/template");
                return null;
            }

            // 已在场上 → 只挪位置，不重复生成
            var existing = Mission.Current?.Agents?.FirstOrDefault(a =>
                a != null && AgentControlHelper.SafeIsActive(a) && a.Character == hero.CharacterObject);
            if (existing != null)
            {
                if (repositionExisting)
                    try { existing.TeleportToPosition(pos); } catch { }
                _agents[npcId] = existing;
                return existing;
            }

            try
            {
                Vec3 dir = new Vec3(Forward.x, Forward.y, 0);
                var origin = new SimpleAgentOrigin(hero.CharacterObject, -1, null);
                var buildData = new AgentBuildData(origin)
                    .InitialPosition(pos)
                    .InitialDirection(dir.AsVec2)
                    .Team(PlayerTeam())
                    .NoHorses(true);
                if (civilian) buildData = buildData.CivilianEquipment(true);

                var agent = Mission.Current.SpawnAgent(buildData);
                if (agent != null)
                {
                    _agents[npcId] = agent;
                    DebugLogger.Log($"[StoryProbe] spawned {npcId} hero={hero.StringId} agent={agent.Index} requested=({pos.x:F1},{pos.y:F1},{pos.z:F1}) actual={agent.Position} controller={agent.Controller}");
                }
                return agent;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] spawn '{npcId}' threw: {ex.Message}");
                return null;
            }
        }

        /// <summary>只保证演员在场；已有演员的位置、朝向和限速由演出取得 Brain 占用后再写。</summary>
        public Agent EnsureStageActor(string npcId, Vec3 initialPos, bool civilian)
        {
            var existing = GetAgent(npcId);
            if (existing != null && AgentControlHelper.SafeIsActive(existing)) return existing;
            return SpawnNpc(npcId, initialPos, civilian, repositionExisting: false);
        }

        private Team PlayerTeam()
        {
            var main = Agent.Main;
            if (main?.Team != null) return main.Team;
            return Mission.Current?.PlayerTeam;
        }

        /// <summary>造/取剧情 Hero：先查状态里存的 StringId，再按模板造一个。</summary>
        public Hero EnsureStoryHero(StoryNpcDef def)
        {
            if (def == null) return null;
            if (_heroes.TryGetValue(def.Id, out var cached) && cached != null && cached.IsAlive)
                return cached;

            if (StoryProbeState.Data.SpawnedHeroIds.TryGetValue(def.Id, out string savedId) && !string.IsNullOrEmpty(savedId))
            {
                var found = Hero.AllAliveHeroes.FirstOrDefault(h => h != null && h.StringId == savedId);
                if (found != null)
                {
                    _heroes[def.Id] = found;
                    return found;
                }
            }

            try
            {
                CharacterObject template = FindTemplate(def);
                if (template == null)
                {
                    DebugLogger.Log($"[StoryProbe] template for '{def.Id}' not found: {def.TemplateId}");
                    return null;
                }

                var settlement = Settlement.CurrentSettlement ?? Hero.MainHero?.HomeSettlement;
                Hero hero = HeroCreator.CreateSpecialHero(template, settlement, null, null, def.Age);
                if (hero == null) return null;

                // 名字走本地化（铁律 13）；两个名字参数都给同一个，避免无名
                var name = new TextObject("{=" + def.NameKey + "}" + def.NameFallback);
                hero.SetName(name, name);
                hero.ChangeState(Hero.CharacterStates.Active);

                // 关系初始为 0（后续委托/选择才加减）
                // 收数人身上得真的有钱可收（§4.1 委托"一百二十第纳尔"）——世界发给他的活动资金。
                if (def.Id == "debtor")
                {
                    try { AgentControlHelper.SetGold(hero, 300, notify: false); } catch { }
                }

                StoryProbeState.Data.SpawnedHeroIds[def.Id] = hero.StringId;
                _heroes[def.Id] = hero;
                DebugLogger.Log($"[StoryProbe] hero created '{def.Id}' -> {hero.StringId} ({hero.Name})");
                return hero;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] EnsureStoryHero({def.Id}) failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>模板查找：两轮策略（铁律 5）。</summary>
        private static CharacterObject FindTemplate(StoryNpcDef def)
        {
            try
            {
                var candidates = def.TemplateCandidates ?? new[] { def.TemplateId };
                foreach (var id in candidates)
                {
                    var c = CharacterObject.Find(id)
                            ?? MBObjectManager.Instance?.GetObject<CharacterObject>(id);
                    if (c != null) return c;
                }
                var fallback = CharacterObject.Find("townsman_empire")
                            ?? MBObjectManager.Instance?.GetObject<CharacterObject>("townsman_empire");
                return fallback;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] FindTemplate({def.TemplateId}) failed: {ex.Message}");
                return null;
            }
        }

        public Agent GetAgent(string npcId)
            => _agents.TryGetValue(npcId, out var a) && a != null && AgentControlHelper.SafeIsActive(a) ? a : null;

        public Hero GetHero(string npcId)
            => _heroes.TryGetValue(npcId, out var h) ? h : null;

        public IEnumerable<KeyValuePair<string, Agent>> AllAgents => _agents;

        public StreetLifeDirector Director => _director;

        // ═══════════════════════════════════════════════════════════════
        // 对话 / 说话
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 开一场剧情对话。入口节点由 StoryProbeDialogue 决定（默认按五步关系挑；传 entryNode 则强制）。
        /// 🔴 注入必须早于 StartConversation（ConversationEntryPatch 的 Prefix 统一收口）。
        /// </summary>
        public void StartStoryConversation(Agent agent, string entryNode = null)
        {
            if (agent == null || !AgentControlHelper.SafeIsActive(agent)) return;
            if (Agent.Main == null) return;

            try
            {
                var hero = (agent.Character as CharacterObject)?.HeroObject;
                string npcId = StoryProbeDialogue.ResolveNpcId(agent);
                if (string.IsNullOrEmpty(npcId))
                {
                    DebugLogger.Log($"[StoryProbe] StartConversation: agent {agent.Name} is not a story NPC");
                    return;
                }

                StoryProbeDialogue.PendingEntryNode = entryNode;
                // 「见过」由对话节点的 PROBE:met 动作标记 —— 在注入前统一标会让入口永远跳到 _hub，看不到 intro。
                // 但**事件接入**（event_*_join）不走 intro 节点，这一条由事件本身完成"认识"，所以在这里补标。
                if (!string.IsNullOrEmpty(entryNode) && entryNode.StartsWith("event_"))
                    StoryProbeState.MarkMet(npcId);

                var logic = Mission.Current?.GetMissionBehavior<MissionConversationLogic>();
                if (logic == null)
                {
                    DebugLogger.Log("[StoryProbe] StartConversation: MissionConversationLogic missing");
                    return;
                }

                // 先把话说清楚的人转向玩家（体面一点）
                try { AgentControlHelper.FaceToActor(agent, Agent.Main); } catch { }

                logic.StartConversation(agent, true, false);
                DebugLogger.Log($"[StoryProbe] conversation started npc={npcId} entry={entryNode ?? "(auto)"} hero={hero?.StringId ?? "null"}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] StartStoryConversation failed: {ex.Message}");
                StoryProbeDialogue.PendingEntryNode = null;
            }
        }

        public void SayLine(string npcId, string key, string fallback, SpeechPriority priority = SpeechPriority.Chat)
        {
            var agent = GetAgent(npcId);
            if (agent == null) return;
            try
            {
                SpeechChannel.Say(agent, LWNTextHelper.ResolveText(key, fallback), priority,
                    SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(agent), Agent.Main, "story", null),
                    0f, false);
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] SayLine({npcId}) failed: {ex.Message}"); }
        }

        public void SayOnAgent(Agent agent, string key, string fallback, SpeechPriority priority = SpeechPriority.Chat)
        {
            if (agent == null || !AgentControlHelper.SafeIsActive(agent)) return;
            try
            {
                SpeechChannel.Say(agent, LWNTextHelper.ResolveText(key, fallback), priority,
                    SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(agent), Agent.Main, "story", null),
                    0f, false);

            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] SayOnAgent failed: {ex.Message}"); }
        }

        /// <summary>演出层/台本级用途：把回调排到 `delay` 秒后（`Schedule` 的公开版）。</summary>
        public void ScheduleFor(float delaySeconds, Action action) => Schedule(delaySeconds, action);

        private void Schedule(float delaySeconds, Action action)
        {
            if (action == null) return;
            int token = StoryProbeState.RuntimeToken;
            _schedule.Add((_time + Math.Max(0f, delaySeconds), () =>
            {
                if (Active && Current == this && StoryProbeState.IsRuntimeCurrent(token)) action();
            }));
        }

        public void SchedulePublic(float delaySeconds, Action action) => Schedule(delaySeconds, action);

        // ═══════════════════════════════════════════════════════════════
        // 过门（五条路）——守门人让路后由外部调用
        // ═══════════════════════════════════════════════════════════════

        public bool GateOpen => _gateOpen;

        /// <summary>守门人让路：把他挪到一侧、记住这条路的反馈。</summary>
        public void OpenGate(string approach, string messageKey, string messageFallback)
        {
            if (_gateOpen) return;
            _gateOpen = true;
            StoryProbeState.Data.GateOpened = true;   // 跨 Mission 记住：再进来不用重新开一次
            StoryProbeState.Data.ChosenApproach = approach;
            StoryProbeState.Data.TalentPoints += 0;   // 天赋点由 ResolveApproach 统一加（避免重复）

            // 开门只是撤销剧情限制；不传送正在被绑、扛、对话或演出的守卫。
            // 后续移动/朝向仍由已有 Brain、原版对话、钩索各自的身体所有者处理。
            StoryProbeState.SetGoal("tool");

            if (!string.IsNullOrEmpty(messageKey))
                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveText(messageKey, messageFallback)));
            DebugLogger.Log($"[StoryProbe] gate opened approach={approach}");
        }

        /// <summary>本次选择结算（§9）：势力好感 / 天赋点 / 通缉 / 隐患。</summary>
        /// <param name="factionId">这条路**对应**的势力（§9「对应势力 +5」）</param>
        /// <param name="gatekeeperDelta">**守门人本人**的关系变化（§9 单独一列：钱/冒充 −5、技巧 0、武力 −20）</param>
        public void ResolveApproach(string approach, string factionId, int gatekeeperDelta, bool crime, string hazardId)
        {
            if (!Active || Current != this || Phase != StoryProbePhase.Second || StoryProbeState.Data.ApproachResolved) return;
            StoryProbeState.Data.ApproachResolved = true;
            StoryProbeState.Data.ChosenApproach = approach;
            StoryProbeState.Data.TalentPoints += 1;
            if (!string.IsNullOrEmpty(factionId)) StoryProbeState.AddFactionRelation(factionId, 5);
            if (!string.IsNullOrEmpty(hazardId)) StoryProbeState.AddHazard(hazardId);

            // 🔴 两种关系分开记（§9 表把它们列成两列）：
            //   ① 对应势力 +5 → 记到该势力代表身上；② 守门人本人 −5/−20/0 → 只能记到守门人身上。
            //   旧实现把两者混成一个 delta、还按 faction 找代表 ⇒ 「冒充」会被记成剧团掉关系，明显错。
            try
            {
                if (Hero.MainHero != null)
                {
                    var factionRep = StoryProbeDefs.GetNpcByFaction(factionId);
                    var factionHero = factionRep != null ? GetHero(factionRep.Id) : null;
                    if (factionHero != null)
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(factionHero, Hero.MainHero, 5);

                    var gateHero = GetHero("blocker");
                    if (gateHero != null && gatekeeperDelta != 0)
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(gateHero, Hero.MainHero, gatekeeperDelta);
                }
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] ResolveApproach relation failed: {ex.Message}"); }

            if (crime)
            {
                try
                {
                    var kingdom = Settlement.CurrentSettlement?.MapFaction as Kingdom;
                    if (kingdom != null) ChangeCrimeRatingAction.Apply(kingdom, StoryProbeTuning.Current.Crime.RecognizedExtra, true);
                }
                catch (Exception ex) { DebugLogger.Log($"[StoryProbe] ResolveApproach crime failed: {ex.Message}"); }
            }

            // §9：选路反馈当场可见（势力好感 +5 / 天赋点 +1 / 结仇代价）——一条消息说清
            try
            {
                string path = LWNTextHelper.ResolveText(StoryProbeDefs.AbilityDisplayKey(
                    approach == "charm" ? StoryAbility.Charm : approach == "guile" ? StoryAbility.Guile : StoryAbility.Force),
                    approach);
                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveCompound("LWN_story_narr_approach",
                        "Path: {PATH} — favour +5, talent +1, standing {REL}.",
                        ("PATH", path), ("REL", gatekeeperDelta.ToString()))));
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════════
        // 随从（第二次进场）
        // ═══════════════════════════════════════════════════════════════

        private void EnsureCompanion()
        {
            Hero comp = null;
            try
            {
                string saved = StoryProbeState.Data.CompanionHeroId;
                if (!string.IsNullOrEmpty(saved))
                    comp = Hero.AllAliveHeroes.FirstOrDefault(h => h != null && h.StringId == saved);

                if (comp == null)
                {
                    var settlement = Settlement.CurrentSettlement;
                    comp = Hero.AllAliveHeroes.FirstOrDefault(h =>
                        h != null && h.IsAlive && !h.IsHumanPlayerCharacter
                        && h.Occupation == Occupation.Wanderer
                        && h.CurrentSettlement == settlement
                        && !h.IsPlayerCompanion);
                }

                if (comp == null)
                {
                    // 没得招 → 旁白兜底，不阻断（§13 风险表）
                    DebugLogger.Log("[StoryProbe] no wanderer to recruit; narration fallback");
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveText("LWN_story_intro_no_companion",
                            "There is no one left to walk in with you.")));
                    return;
                }

                if (!comp.IsPlayerCompanion)
                {
                    AddCompanionAction.Apply(Clan.PlayerClan, comp);
                    DebugLogger.Log($"[StoryProbe] companion recruited: {comp.Name} ({comp.StringId})");
                }
                StoryProbeState.Data.CompanionHeroId = comp.StringId;

                // 站到玩家左后 1.5 m
                Vec3 pos = PlayerAnchor + new Vec3(-Forward.x, -Forward.y, 0) * 1.2f
                         + new Vec3(-Forward.y, Forward.x, 0) * 1.5f;
                var origin = new SimpleAgentOrigin(comp.CharacterObject, -1, null);
                var buildData = new AgentBuildData(origin)
                    .InitialPosition(pos)
                    .InitialDirection(Forward.ToVec3().AsVec2)
                    .Team(PlayerTeam())
                    .NoHorses(true)
                    .CivilianEquipment(true);
                _companionAgent = Mission.Current.SpawnAgent(buildData);

                // §3.2.3：随从 3 句**只播一次**（IntroPlayed 存状态；custom.story_probe intro 可重播）
                if (StoryProbeState.Data.IntroPlayed)
                {
                    _introLine = -1;
                    _nextIntroAt = -1f;
                }
                else
                {
                    _introLine = 0;
                    _nextIntroAt = _time + 1.2f;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] EnsureCompanion failed: {ex.Message}");
            }
        }

        private void SayCompanionLine(int index)
        {
            string[] keys = { "LWN_story_intro_line_1", "LWN_story_intro_line_2", "LWN_story_intro_line_3" };
            string[] fall = {
                "This is it. The town your sister walked into three months ago.",
                "Your warrant is on the gate — walking up front is walking into a cell.",
                "Get in with the people at the gate. Help one of them, they teach you a way."
            };
            if (index < 0 || index >= keys.Length) return;
            DebugLogger.Log($"[StoryProbe] companion line {index + 1}/3");
            SayOnAgent(_companionAgent, keys[index], fall[index], SpeechPriority.Warning);
        }

        // ═══════════════════════════════════════════════════════════════
        // nobody：剥夺 / 恢复（§10）
        // ═══════════════════════════════════════════════════════════════

        /// <summary>出狱换装：钱袋 + 装备没收，只留旧衣 + 面罩；原值记进状态，序章完成归还。</summary>
        /// <summary>入牢没收，独立于换装/出牢；重复进入牢房不会覆盖托管物品或再扣一次钱。</summary>
        public static bool ConfiscatePlayerBelongings()
        {
            var hero = Hero.MainHero;
            var d = StoryProbeState.Data;
            if (hero == null || d.CancellationPending
                || CampaignMission.Current?.Location?.StringId != "prison") return false;
            if (d.Stripped && (d.ConfiscationComplete || d.EscrowVersion == 0)) return true;
            var clothes = StoryProbeDefs.FindItem("hemp_tunic");
            if (clothes == null || hero.BattleEquipment == null || hero.CivilianEquipment == null) return false;
            try
            {
                if (!d.Stripped)
                {
                    SaveEquipment(hero.BattleEquipment, d.SavedBattleEquip, d.SavedBattleModifiers);
                    SaveEquipment(hero.CivilianEquipment, d.SavedCivilianEquip, d.SavedCivilianModifiers);
                    d.PendingBattleRestoreSlots.Clear();
                    d.PendingCivilianRestoreSlots.Clear();
                    d.EscrowVersion = 1;
                    d.ConfiscationComplete = false;
                    d.GoldBeforeStrip = 0;
                    d.Stripped = true; // 任何后续异常都保留尚欠账，不能再次覆盖原物快照。
                    d.StripAtHours = CampaignTime.Now.ToHours;
                    int before = hero.Gold;
                    try { AgentControlHelper.TransferGold(hero, (Hero)null, before, notify: false); }
                    finally { d.GoldBeforeStrip = Math.Max(0, before - hero.Gold); }
                }
                ConfiscateEquipment(hero.BattleEquipment, d.PendingBattleRestoreSlots);
                ConfiscateEquipment(hero.CivilianEquipment, d.PendingCivilianRestoreSlots);
                if (!AgentControlHelper.ReplaceEquippedItem(hero, hero.BattleEquipment, EquipmentIndex.Body, new EquipmentElement(clothes))
                    || !AgentControlHelper.ReplaceEquippedItem(hero, hero.CivilianEquipment, EquipmentIndex.Body, new EquipmentElement(clothes)))
                    return false;
                d.ConfiscationComplete = true;
                d.Disguised = false;
                d.JailDone = false;
                DebugLogger.Log($"[StoryProbe] jail confiscated: escrowGold={d.GoldBeforeStrip} equipmentSaved={d.SavedBattleEquip.Count}");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] ConfiscatePlayerBelongings failed (escrow retained): {ex.Message}");
                return false;
            }
        }

        private static void ConfiscateEquipment(Equipment equipment, List<int> pending)
        {
            for (int i = 0; i < 12; i++)
            {
                if (pending.Contains(i)) continue;
                equipment[(EquipmentIndex)i] = EquipmentElement.Invalid;
                pending.Add(i); // 空槽也记账：归还时要把后来换上的物品保留到行囊。
            }
        }

        public static bool StripPlayer(bool advancePhase = true)
        {
            var hero = Hero.MainHero;
            var d = StoryProbeState.Data;
            if (d.CancellationPending || !d.JailSceneEntered || CampaignMission.Current?.Location?.StringId != "prison") return false;
            if (d.JailDone) return true;
            var mask = StoryProbeDefs.FindItem("head_wrapped");
            var clothes = StoryProbeDefs.FindItem("hemp_tunic");
            // 缺真实衣装不能用一个 disguised=true 的旗标冒充换装成功。
            if (hero == null || mask == null || clothes == null || !ConfiscatePlayerBelongings()) return false;
            try
            {
                if (!AgentControlHelper.ReplaceEquippedItem(hero, hero.BattleEquipment, EquipmentIndex.Body, new EquipmentElement(clothes))
                    || !AgentControlHelper.ReplaceEquippedItem(hero, hero.CivilianEquipment, EquipmentIndex.Body, new EquipmentElement(clothes)))
                    return false;
                if (!AgentControlHelper.ReplaceEquippedItem(hero, hero.BattleEquipment, EquipmentIndex.Head, new EquipmentElement(mask))
                    || !AgentControlHelper.ReplaceEquippedItem(hero, hero.CivilianEquipment, EquipmentIndex.Head, new EquipmentElement(mask)))
                    return false;
                d.Disguised = true;
                d.JailDone = true;
                if (advancePhase) d.Phase = (int)StoryProbePhase.Second;
                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveText("LWN_story_jail_done",
                        "You have no weapon, and your purse is empty. The cloth over your face is all you have.")));
                DebugLogger.Log($"[StoryProbe] jail disguise ready: goldBefore={d.GoldBeforeStrip} advancePhase={advancePhase}");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] StripPlayer failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>只归还尚欠槽位与尚欠钱；失败不销总账，已成功的槽位不重复发。</summary>
        public static bool RestorePlayerBelongings()
        {
            var d = StoryProbeState.Data;
            if (!d.Stripped) return true;
            var hero = Hero.MainHero;
            if (hero == null) return false;
            try
            {
                // 新版托管必须有完整修饰词数组；坏档不能悄悄把缺数组视为所有原物无修饰词。
                if (d.EscrowVersion > 0 && (d.SavedBattleModifiers == null || d.SavedBattleModifiers.Count != 12
                    || d.SavedCivilianModifiers == null || d.SavedCivilianModifiers.Count != 12))
                {
                    DebugLogger.Log("[StoryProbe] escrow modifier snapshot incomplete; outstanding debt retained");
                    return false;
                }
                // 所有原物和修饰词先解析成功，才允许改任何一件装备。
                var battle = ResolveEscrowEquipment(d.SavedBattleEquip, d.SavedBattleModifiers);
                var civilian = ResolveEscrowEquipment(d.SavedCivilianEquip, d.SavedCivilianModifiers);
                if (hero.BattleEquipment == null || hero.CivilianEquipment == null
                    || (hero.PartyBelongedTo?.ItemRoster ?? TaleWorlds.CampaignSystem.Party.MobileParty.MainParty?.ItemRoster) == null)
                    return false;
                if ((long)hero.Gold + Math.Max(0, d.GoldBeforeStrip) > int.MaxValue) return false;
                if (d.EscrowVersion == 0)
                {
                    // 老完整托管快照没有销账位；已经丢失的旧修饰词不能重建。
                    // 仅旧版按已解析的无修饰词结果补齐结构，保证升到 v1 后的部分归还仍可重试。
                    d.SavedBattleModifiers ??= new List<string>();
                    d.SavedCivilianModifiers ??= new List<string>();
                    while (d.SavedBattleModifiers.Count < 12) d.SavedBattleModifiers.Add("");
                    while (d.SavedCivilianModifiers.Count < 12) d.SavedCivilianModifiers.Add("");
                    d.PendingBattleRestoreSlots = Enumerable.Range(0, 12).ToList();
                    d.PendingCivilianRestoreSlots = Enumerable.Range(0, 12).ToList();
                    d.EscrowVersion = 1;
                    d.ConfiscationComplete = true;
                }
                RestoreEquipment(hero.BattleEquipment, battle, d.PendingBattleRestoreSlots);
                RestoreEquipment(hero.CivilianEquipment, civilian, d.PendingCivilianRestoreSlots);
                int before = hero.Gold;
                int owed = Math.Max(0, d.GoldBeforeStrip);
                try { AgentControlHelper.TransferGold((Hero)null, hero, owed, notify: false); }
                finally
                {
                    // 即使引擎结算后通知抛错，也按实际入账销掉这部分，重试不再发一次。
                    d.GoldBeforeStrip = Math.Max(0, owed - Math.Max(0, hero.Gold - before));
                }
                if (d.PendingBattleRestoreSlots.Count != 0 || d.PendingCivilianRestoreSlots.Count != 0
                    || d.GoldBeforeStrip != 0) return false;
                if (AgentControlHelper.SafeIsActive(Agent.Main) && Agent.Main.Character == hero.CharacterObject)
                    AgentControlHelper.RefreshSpawnEquipment(Agent.Main, hero.CivilianEquipment);
                d.Stripped = false;
                d.ConfiscationComplete = false;
                d.Disguised = false;
                d.NobleDisguise = false;
                d.StripAtHours = 0;
                DebugLogger.Log("[StoryProbe] belongings restored: all escrow slots and gold settled");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] RestorePlayerBelongings failed (outstanding escrow retained): {ex.Message}");
                return false;
            }
        }

        private static void SaveEquipment(Equipment equipment, List<string> into, List<string> modifiers)
        {
            into.Clear();
            modifiers.Clear();
            if (equipment == null) return;
            for (int i = 0; i < 12; i++)
            {
                var el = equipment[(EquipmentIndex)i];
                into.Add(el.IsEmpty || el.Item == null ? "" : el.Item.StringId);
                modifiers.Add(el.ItemModifier?.StringId ?? "");
            }
        }

        private static void ClearEquipment(Equipment equipment)
        {
            if (equipment == null) return;
            for (int i = 0; i < 12; i++)
                equipment[(EquipmentIndex)i] = EquipmentElement.Invalid;
        }

        private static EquipmentElement[] ResolveEscrowEquipment(List<string> from, List<string> modifiers)
        {
            if (from == null || from.Count != 12 || MBObjectManager.Instance == null)
                throw new InvalidOperationException("escrow snapshot incomplete; cannot discard outstanding items");
            var resolved = new EquipmentElement[12];
            for (int i = 0; i < 12; i++)
            {
                string itemId = from[i];
                if (string.IsNullOrEmpty(itemId)) { resolved[i] = EquipmentElement.Invalid; continue; }
                // 原物归还必须精确匹配，不把缺失原件换成另一个宽松候选。
                var item = MBObjectManager.Instance.GetObject<ItemObject>(itemId)
                    ?? MBObjectManager.Instance.GetObject<ItemObject>(candidate => candidate.StringId == itemId);
                string modifierId = modifiers != null && i < modifiers.Count ? modifiers[i] : "";
                ItemModifier modifier = null;
                if (!string.IsNullOrEmpty(modifierId))
                    modifier = MBObjectManager.Instance.GetObject<ItemModifier>(modifierId)
                        ?? MBObjectManager.Instance.GetObject<ItemModifier>(candidate => candidate.StringId == modifierId);
                if (item == null || (!string.IsNullOrEmpty(modifierId) && modifier == null))
                    throw new InvalidOperationException($"escrow resource missing: {itemId} / {modifierId}");
                resolved[i] = new EquipmentElement(item, modifier);
            }
            return resolved;
        }

        private static void RestoreEquipment(Equipment equipment, EquipmentElement[] original, List<int> pending)
        {
            foreach (int i in pending.ToArray())
            {
                if (i < 0 || i >= original.Length) throw new InvalidOperationException("invalid outstanding escrow slot");
                // 先把当前穿着转入行囊，再从托管归还原件。即使与原件同型号，也不能吞掉后来获得的那一件。
                if (!AgentControlHelper.ReplaceEquippedItem(Hero.MainHero, equipment, (EquipmentIndex)i, EquipmentElement.Invalid)
                    || !AgentControlHelper.ReplaceEquippedItem(Hero.MainHero, equipment, (EquipmentIndex)i, original[i]))
                    throw new InvalidOperationException("cannot preserve currently equipped item during restore");
                pending.Remove(i); // 逐槽立即销账；后面任何一步失败都不再补发本槽原件。
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 命令支持（custom.story_probe …）
        // ═══════════════════════════════════════════════════════════════

        public void CommandSpawnCast()
        {
            if (!Active) return;
            foreach (var def in StoryProbeDefs.Npcs)
            {
                if (def.IsBlocker) continue;
                if (GetAgent(def.Id) != null) continue;
                SpawnNpc(def.Id, OffsetPos(def.OffsetForward, def.OffsetRight), civilian: !IsHostileNpc(def.Id));
            }
            _director ??= new StreetLifeDirector(this);
        }

        private static bool IsHostileNpc(string npcId)
            => npcId == "blocker" || npcId == "thug" || npcId == "outlaw";

        public string DescribeActor(string npcId)
        {
            if (!_agents.TryGetValue(npcId, out var agent) || agent == null)
                return npcId + "=not spawned";
            try
            {
                bool live = AgentControlHelper.SafeIsActive(agent);
                if (!live) return npcId + "=inactive";
                var player = Agent.Main;
                string distance = player == null ? "-" : player.Position.Distance(agent.Position).ToString("F1");
                return $"{npcId}=agent{agent.Index} active={live} hp={agent.Health:F1} controller={agent.Controller} pos={agent.Position} playerDist={distance} tracked={StoryProbeDialogue.ResolveNpcId(agent) ?? "-"}";
            }
            catch (Exception ex) { return npcId + "=unavailable(" + ex.GetType().Name + ")"; }
        }

        public string DescribeWhere()
        {
            if (!Active) return "story probe not active in this mission";
            return $"anchor=({PlayerAnchor.x:F1},{PlayerAnchor.y:F1},{PlayerAnchor.z:F1}) " +
                   $"blocker=({BlockerPos.x:F1},{BlockerPos.y:F1},{BlockerPos.z:F1}) " +
                   $"goal=({GoalPos.x:F1},{GoalPos.y:F1},{GoalPos.z:F1}) " +
                   $"dist={PlayerAnchor.Distance(BlockerPos):F1}/{BlockerPos.Distance(GoalPos):F1} " +
                   $"live=[{DescribeActor("blocker")}] " +
                   $"source={(UsedFallback ? "fallback" : "tag:" + (_guardTagUsed ?? "?"))}";
        }

        public string DescribeCast()
        {
            if (!Active) return "not active";
            return string.Join(" ", _agents.Select(kv =>
                kv.Key + "=" + (kv.Value != null && AgentControlHelper.SafeIsActive(kv.Value) ? kv.Value.Index.ToString() : "-")));
        }
    }

    /// <summary>门内目标点的屏幕提示（左上角一句话，§6.8）。</summary>
    public static class StoryProbeScreenGoal
    {
        public static void Show()
        {
            try
            {
                MBInformationManager.AddQuickInformation(new TextObject(LWNScreenGoalText.Resolve()));
            }
            catch { }
        }
    }

    /// <summary>屏幕目标文本（§6.8：随阶段换一行）。</summary>
    public static class LWNScreenGoalText
    {
        public static string Resolve()
        {
            switch (StoryProbeState.Goal)
            {
                case "asked":
                    // 拿到"往码头找"这条线索后，目标别再只说"四个人四截线索"
                    if (StoryProbeState.HasClue("sister_3"))
                        return LWNTextHelper.ResolveText("LWN_story_goal_asked_dock",
                            "Look down at the docks. A man in oilskins took her. Do not go alone.");
                    return LWNTextHelper.ResolveText("LWN_story_goal_asked",
                        "Find out what happened to your sister. Four people, four pieces.");
                case "quest":
                {
                    // 已接的委托要说清楚：哪件事、去找谁（此前只有一句笼统的"帮谁谁教你一手"）
                    var job = StoryProbeDefs.Representatives
                        .FirstOrDefault(d => StoryProbeState.IsQuestAccepted(d.Id)
                                             && !StoryProbeState.IsQuestTurnedIn(d.Id));
                    if (job != null && !string.IsNullOrEmpty(job.QuestLogKey))
                    {
                        string title = LWNTextHelper.ResolveText(job.QuestLogKey, job.Id);
                        string target = string.IsNullOrEmpty(job.QuestTargetNpcId)
                            ? "" : StoryProbeMissionBehavior.NpcName(job.QuestTargetNpcId);
                        return LWNTextHelper.ResolveCompound("LWN_story_goal_quest_named",
                            ("TASK", title), ("TARGET", target));
                    }
                    return LWNTextHelper.ResolveText("LWN_story_goal_quest",
                        "Earn your way in. Help one of them, they teach you a way.");
                }
                case "tool":
                    return LWNTextHelper.ResolveText("LWN_story_goal_tool", "Get through that gate.");
                case "done":
                    return LWNTextHelper.ResolveText("LWN_story_goal_done",
                        "The prologue is over. The road inside is yours.");
                default:
                    return LWNTextHelper.ResolveText("LWN_story_goal_enter",
                        "Get into the town. Someone at the gate is in the way.");
            }
        }
    }
}
