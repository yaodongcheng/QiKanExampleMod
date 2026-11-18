using System;
using System.Collections.Generic;
using LivingWorldNpcs.Animation;
using SandBox.Conversation.MissionLogics;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
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
    /// 序章的 Mission 侧宿主（2026-11-12，plans/逆水寒511/逆水寒511-总纲与实施计划.md §十五的需求§11.2）。
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

        /// <summary>
        /// 调试：跳过演出。准备期跳过 = 先把布景/摆人当场做完并判定，再交还控制，
        /// 不再播开场（准备没齐则直接判失败，不假装 ready）。
        /// </summary>
        public string SkipStage()
        {
            string prepState = "n/a";
            if (_prep != null)
            {
                _prep.Skip();
                prepState = _prep.Ready ? "ready"
                          : _prep.Failed ? "failed: " + _prep.Reason
                          : "pending";
            }
            _stage?.Skip();
            return prepState;
        }
        private float _time;

        /// <summary>P4 岗位规则调度（数据驱动质问者与流程）；没有正式 v2 数据时为空。</summary>
        private StoryPostDirector _posts;
        public StoryPostDirector Posts => _posts;

        /// <summary>P3 准备批次（布景 + 站位 + R1～R7）。</summary>
        private StoryPrepRunner _prep;
        public StoryPrepRunner Prep => _prep;
        /// <summary>准备失败已经处置过（停台本/发提示），只做一次。</summary>
        private bool _prepFailureHandled;
        /// <summary>场景起不来的弹窗只弹一次（开场机位重试用尽 / 布景失败共用）。</summary>
        private bool _sceneAbortHandled;

        /// <summary>P4 工作点（占用/走位/工作动作/局部让位）。</summary>
        private StoryWorkDirector _work;
        private bool _workInitialized;
        public StoryWorkDirector Work => _work;

        /// <summary>本 Mission 的演出时钟（秒）。对话请求的冷却/重入与台本共用同一个钟。</summary>
        public float Clock => _time;
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
        private readonly HashSet<string> _firstFightActors = new HashSet<string>(StringComparer.Ordinal);
        private int _firstDuelReceipt;
        private float _verticalGateSpeed;
        private float _gateGroundStable;
        private float _nextBarrierAt;
        private bool _pendingBlockerDown;
        private readonly HashSet<string> _pendingQuestDone = new HashSet<string>();
        private bool _goalReached;
        private bool _forcedDefeatQueued;
        private string _playerDetentionMode;
        private string _playerDetentionActor;
        private bool _firstFightStarted;
        private bool _firstGuardQuestioned;
        /// <summary>本场已经拦问过几次（受 Gate.MaxIntercepts 上限约束；用满后再闯才算硬闯）。</summary>
        private int _gateIntercepts;
        /// <summary>职责点是否已写进各人脑里（每场只写一次）。</summary>
        private bool _dutyPointsApplied;
        /// <summary>每名演员的**岗位点**（SpawnNpc 时记下）：布局里没有站位时用它兜底。</summary>
        private readonly Dictionary<string, Vec3> _postPositions = new Dictionary<string, Vec3>(StringComparer.Ordinal);
        private bool _firstInterceptRequested;
        private bool _firstDefeatResolved;
        private float _firstFightRequestedAt;
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
        /// <summary>P0-D.3：拦问请求失败后的退避秒数（禁止每帧重试；2026-11-15 实机 124ms 内 8 轮）。</summary>
        private const float InterceptRetryCooldownSec = 0.75f;
        private float _nextInterceptRetryAt;
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
                    ResolveApproach("force", "dockhands", -20, true, null, "blocker");
                    DebugLogger.Log("[StoryProbe] blocker downed -> gate open (force)");
                }
            }

            if (_pendingQuestDone.Count > 0)
            {
                foreach (var npcId in _pendingQuestDone)
                {
                    if (!StoryProbeState.IsQuestAccepted(npcId) || StoryProbeState.IsQuestDone(npcId)) continue;
                    StoryProbeState.CompleteQuest(npcId);
                    if (!StoryProbeState.Data.QuestResolutions.ContainsKey(npcId))
                        StoryProbeState.Data.QuestResolutions[npcId] = "force";
                    StoryProbeDialogue.ResolveCompletedQuestConflict(npcId);
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

            // 扣押只在原版对话关闭后的正常 tick 转场，不能与战斗泵同时推进。
            StoryEnvironment.ReportActual(Mission);
            if (PumpPlayerDetention()) return;

            _time += dt;

            ProcessPendingCombatResults();

            // 演出台本（布景 / 摆人 / 镜头 / 冻解玩家）——自己的时间轴，与剧本阶段无关
            try { _stage?.Tick(dt); }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryStage] tick failed: {ex.Message}");
                if (_stage?.Active == true) _stage.Stop("tick exception: " + ex.Message, "LWN_story_stage_failure_exception");
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
                StoryProbeDialogue.TickDialogueFacing(dt);
                if (_defeatAnim != null && _defeatPlayer == Agent.Main && AgentControlHelper.SafeIsActive(_defeatPlayer))
                    _defeatAnim.Tick(_defeatPlayer, dt);
                if (Phase == StoryProbePhase.FirstAttempt) TickFirstAttempt(dt);
                else if (Phase == StoryProbePhase.Jail) TickJailScene();
                else if (Phase == StoryProbePhase.Second) TickSecondPhase(dt);
                TickGuardLeash();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] tick failed: {ex.Message}");
                if (_stage?.Active == true) _stage.Stop("story tick exception: " + ex.Message, "LWN_story_stage_failure_exception");
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
            StoryProbeDialogue.ReleaseDialogueFacing();
            _schedule.Clear();
            _playerDetentionMode = null;
            _playerDetentionActor = null;
            // 仅实际发出的本轮回执可撤战；pending-only请求不拥有目标后来参加的外部战斗。
            var fights = new Dictionary<string, Action<Agent>>(_duelCallbacks);
            if (_firstDuelCallback != null)
                foreach (string id in _firstFightActors) fights[id] = _firstDuelCallback;
            _firstFightActors.Clear();
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
            try { _work?.Stop(reason); }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] work stop failed: {ex.Message}"); }
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
                // 本场人口标记随 Mission 一起作废：下一场（普通逛城或重进剧情）重新按入口决定。
                StoryPopulationGuard.Release(Mission, "mission ending");
                // 提前离开牢房要回牢；真实败退事实已在回执里落盘，不依赖视觉延迟。
                if (resumeJail) StoryProbeBehavior.RequestJailInquiry();
            }
            finally { base.OnEndMissionInternal(); }
        }

        public override void OnRemoveBehavior()
        {
            StoryPopulationGuard.Release(Mission, "host removed");
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
            // 剧情场景允许玩家随时打开背包换装（2026-11-16 用户裁定）：
            // 原版城镇在"未被标记为伪装"时本来就开着物品栏，这里显式确认一次，
            // 避免被其他系统关掉后玩家没地方换。换装后的外观刷新走事件补丁
            // （StoryProbeInventoryPatch），不做每帧轮询。
            try
            {
                Mission.IsInventoryAccessible = true;
                DebugLogger.Log("[StoryProbe] inventory access enabled (scene dressing allowed)");
            }
            catch (Exception invEx) { DebugLogger.Log($"[StoryProbe] enable inventory access failed: {invEx.Message}"); }
            ResolveAnchors();
            if (!Active) return;

            _posts = new StoryPostDirector(this);
            _posts.HasRules(Phase == StoryProbePhase.FirstAttempt ? "first_meet" : "second_meet");

            if (Phase == StoryProbePhase.FirstAttempt) SetupFirstAttempt();
            else SetupSecondPhase();
            if (!Active) return;

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
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText(
                    "LWN_story_err_gate_layout", "This town's prologue gate is not ready. Choose a supported town.")));
                return;
            }
            Vec2 inward = (_gateCenter - outside).AsVec2;
            if (inward.LengthSquared <= 0.0001f || gate.HalfWidthMeters <= 0f || gate.InsideMeters <= 0f)
            {
                Active = false;
                DebugLogger.Log("[StoryProbe] invalid gate direction or bounds; setup stopped");
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText(
                    "LWN_story_err_gate_layout", "This town's prologue gate is not ready. Choose a supported town.")));
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
            // 通行权 = 背包里的通行令牌（2026-11-16）：**两个阶段都认** —— 手里有牌就不必再被拦一次。
            if (HasGatePass())
            {
                _gateOpen = true;
                DebugLogger.Log($"[StoryGate] gate open at geometry setup by pass token: item={StoryProbeTuning.Current?.Gate?.PassItem}");
            }
            DebugLogger.Log($"[StoryProbe] physical gate: outside={outside} gate={_gateCenter} blocker={BlockerPos} goal={GoalPos} width={gate.HalfWidthMeters * 2f:F1} gateOpen={_gateOpen}");
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
            // P2：首遇名单来自选角表（FirstMeetGuard），不再写死「只有铁头」。
            RestoreFirstGuardEnhancement();
            StoryStageDef firstStage = null;
            StoryLayoutRegistry.Published?.Stages.TryGetValue("first_meet", out firstStage);
            var guards = firstStage == null ? StoryProbeDefs.Npcs.Where(n => n.FirstMeetGuard).ToList()
                : firstStage.Actors.Concat(firstStage.OptionalActors).Select(StoryProbeDefs.GetNpc).Where(n => n != null).ToList();
            if (guards.Count == 0 && StoryProbeDefs.Blocker != null) guards.Add(StoryProbeDefs.Blocker);
            DebugLogger.Log("[StoryProbe] setup first attempt guards=" + string.Join(",", guards.Select(g => g.Id)));

            foreach (var def in guards)
            {
                var hero = EnsureStoryHero(def);
                if (hero == null) continue;
                // 只有主质问者（IsBlocker）吃首遇教学强化；副卫保持正常数值。
                if (def.IsBlocker && !BoostFirstGuardSkills(hero)) return;

                var pos = BlockerPos
                          + new Vec3(Forward.x, Forward.y, 0f) * def.FirstMeetOffsetForward
                          + new Vec3(Forward.y, -Forward.x, 0f) * def.FirstMeetOffsetRight;
                // 出生与质问范围共用正式岗位点，不能只把出生改成另一份门口偏移。
                if (_posts != null && _posts.TryGetStandingPosition("first_meet", def.Id, out var placed))
                    pos = placed;
                else if (firstStage != null)
                {
                    Active = false;
                    DebugLogger.Log("[StoryProbe] first-meet standing unresolved: " + def.Id + "; setup stopped, no alternate spawn");
                    ShowSceneAbortInquiry(LWNTextHelper.ResolveText("LWN_story_err_gate_layout",
                        "This town's prologue gate is not ready. Choose a supported town."), "first-meet standing unresolved: " + def.Id);
                    return;
                }
                var agent = SpawnNpc(def.Id, pos, def.Civilian);
                if (agent != null)
                {
                    if (def.IsBlocker) BoostFirstGuardAgent(agent);
                    AgentControlHelper.HoldInPlace(agent);
                    var layout = firstStage == null ? null : StoryLayoutRegistry.Published.Layouts[firstStage.Layout];
                    var standing = layout?.Standing.FirstOrDefault(st => st.Actor == def.Id);
                    float facing = layout?.FindPoint(standing?.Point)?.Facing ?? -1f;
                    float angle = facing * (float)Math.PI / 180f;
                    AgentControlHelper.SetBodyDirection(agent, new Vec2((float)Math.Cos(angle), (float)Math.Sin(angle)));
                }
                Schedule(1.5f, () => DebugLogger.Log("[StoryProbe] first guard live: " + DescribeActor(def.Id)));
            }
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

        private void TickFirstAttempt(float dt)
        {
            var player = Agent.Main;
            var blocker = GetAgent("blocker");
            if (player == null || Settings.Instance.IsInteractionDisabled()) return;
            // 首遇没有准备批次：守卫的职责点 = 出生岗位。
            if (!_dutyPointsApplied && _postPositions.Count > 0)
            {
                _dutyPointsApplied = true;
                ApplyDutyPoints();
            }
            _posts?.Tick(dt, "first_meet");
            Vec3 previousPosition = _lastGatePosition;
            float previousAlong = GateAlong(previousPosition);
            _lastGatePosition = player.Position;
            // 只滤掉浮点抖动；不能以固定每帧五毫米漏掉高帧率下的慢走。
            bool walkingIn = GateAlong(player.Position) > previousAlong + 0.0001f
                && player.Position.Distance(previousPosition) <= StoryProbeTuning.Current.Gate.CrossingMaxStepMeters;

            if (_forcedDefeatQueued)
            {
                // 事件尚未被战斗动作接受就取消/丢弃时允许重试；超时绝不判败。
                if (_firstFightStarted && !_firstDefeatResolved
                    && _time - _firstFightRequestedAt > StoryProbeTuning.Current.Intro.FirstFightStartTimeoutSec
                    && !_firstFightActors.Any(id => AttackTriggerMissionLogic.Instance?.HasDuelPair(GetAgent(id), player) == true))
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
            // 🔴 门禁在**第一次进城**也要生效（2026-11-16 实机：此前只有第二阶段调它）。
            //    症状：玩家选「离开」后可以直接穿过城门走进去；一旦越过 PastMeters，
            //    `IsPlayerInPostZone` 就永久返回 false（它自己那条 "人已经过去了" 短路），
            //    两名守卫都判 "outside my post zone" ⇒ 再也不拦 = 白送进城。
            //    门是物理障碍，跟阶段无关，两个 tick 都要推。
            EnforceGateBarrier(player, dt);
            if (_firstGuardQuestioned)
            {
                // 已被问过之后又往门里走：**先允许"再拦一次"**（可以换手段重新谈），
                // 拦问次数用满才算硬闯（2026-11-16 用户反馈：以前退后回来直接开打，中间一句提醒都没有）。
                if (walkingIn && IsPlayerInGateApproach() && GateAlong(player.Position) >= 0f)
                {
                    var gt = StoryProbeTuning.Current.Gate;
                    if (_gateIntercepts < gt.MaxIntercepts)
                    {
                        _firstGuardQuestioned = false;
                        _blockerAutoTalkArmed = true;
                        _nextInterceptRetryAt = 0f;
                        DebugLogger.Log($"[StoryGate] re-approach after questioning -> intercept again ({_gateIntercepts}/{gt.MaxIntercepts})");
                    }
                    else
                    {
                        DebugLogger.Log($"[StoryGate] re-approach after questioning, intercept limit {gt.MaxIntercepts} reached -> hard entry");
                        PlayForcedDefeat();
                    }
                }
                return;
            }
            // 正式规则按职责区和感知触发；站住或Brain拒绝后重试不要求玩家再迈一步。
            if (_blockerAutoTalkArmed && !_firstInterceptRequested && _time >= _nextInterceptRetryAt)
            {
                // P4：谁去问由数据（Behaviors + 岗位资格 + 次序）决定；没有正式数据才退回铁头。
                if (_posts?.HasRules("first_meet") == true)
                {
                    _firstInterceptRequested = true;
                    _blockerAutoTalkArmed = false;
                    bool requested = _posts.TryRequestQuestion("first_meet", opened =>
                    {
                        _firstInterceptRequested = false;
                        if (!opened)
                        {
                            _blockerAutoTalkArmed = true;
                            _nextInterceptRetryAt = _time + InterceptRetryCooldownSec;
                        }
                    });
                    if (!requested)
                    {
                        _firstInterceptRequested = false;
                        _blockerAutoTalkArmed = true;
                        _nextInterceptRetryAt = _time + InterceptRetryCooldownSec;
                    }
                    return;
                }
                // 正式规则缺失/拒绝不能伪装成旧城门脚本成功；旧路径只兼容没有正式源的模块。
                if (StoryV2Data.HasSource || StoryV2Data.Published) return;
                string questionerId = "blocker";
                string behaviorId = null;
                if (_posts?.HasRules("first_meet") == true)
                {
                    if (!_posts.TrySelectQuestioner("first_meet", out questionerId, out behaviorId, out string why))
                    {
                        _nextInterceptRetryAt = _time + InterceptRetryCooldownSec;
                        DebugLogger.Log("[StoryV2] no eligible questioner: " + why);
                        return;
                    }
                }
                else if (!IsPlayerInGateApproach()) return;
                var questioner = GetAgent(questionerId);
                if (questioner == null)
                {
                    _nextInterceptRetryAt = _time + InterceptRetryCooldownSec;
                    return;
                }

                _firstInterceptRequested = true;
                _blockerAutoTalkArmed = false;
                AgentAIController.Instance?.SendEventToAgent(questioner, "event_story_intercept", player,
                    "auto", new Action<Agent>(ready =>
                    {
                        if (!Active || Current != this || Phase != StoryProbePhase.FirstAttempt) return;
                        _firstInterceptRequested = false;
                        if (ready == null)
                        {
                            // P0-D.3：失败要退避，不能每帧重试（2026-11-15 实机：124ms 内请求了 8 轮）。
                            _posts?.NoteAttempt(behaviorId, questionerId, false, "brain refused");
                            _blockerAutoTalkArmed = true;
                            _nextInterceptRetryAt = _time + InterceptRetryCooldownSec;
                            return;
                        }
                        // P0-D.2/P4：入口与流程都由正式定义裁决；未发布定义时拒绝开剧情窗口。
                        if (!string.IsNullOrEmpty(behaviorId)
                            && _posts != null && _posts.BeginQuestionFlow(behaviorId, questionerId, out string flowReason))
                        {
                            _posts.NoteAttempt(behaviorId, questionerId, true, flowReason);
                        }
                        else
                        {
                            StartStoryConversation(ready, null, "first_meet_auto");
                        }
                        if (!ConversationEntryPatch.IsConversationActive()
                            && (_posts == null || string.IsNullOrEmpty(behaviorId)))
                        {
                            _blockerAutoTalkArmed = true;
                            _nextInterceptRetryAt = _time + InterceptRetryCooldownSec;
                        }
                    }));
                DebugLogger.Log("[StoryProbe] Brain questioning requested from '" + questionerId
                    + "' behavior=" + (behaviorId ?? "legacy") + "; no combat for approaching/crossing");
            }
        }

        public bool IsPlayerInGateApproach()
            => _gateGeometryReady && Agent.Main != null
                && GateAlong(Agent.Main.Position) >= -StoryProbeTuning.Current.Gate.OutsideMeters
                && GateAlong(Agent.Main.Position) <= StoryProbeTuning.Current.Gate.InsideMeters
                && GateLateral(Agent.Main.Position) <= StoryProbeTuning.Current.Gate.HalfWidthMeters;

        public void NotifyFirstGuardQuestioned()
        {
            if (!Active || Phase != StoryProbePhase.FirstAttempt) return;
            _gateIntercepts++;
            _firstGuardQuestioned = true;
            DebugLogger.Log($"[StoryGate] guard questioning #{_gateIntercepts} (max {StoryProbeTuning.Current.Gate.MaxIntercepts}); hard-entry choice is now available");
        }

        /// <summary>兼容旧对话动作名；仅在正常质问后明确选择硬闯时请求交战。</summary>
        public bool PlayForcedDefeat()
        {
            if (!Active || Phase != StoryProbePhase.FirstAttempt || !_firstGuardQuestioned
                || _playerDetentionMode != null) return false;
            if (_forcedDefeatQueued) return true;
            _forcedDefeatQueued = true;
            DebugLogger.Log("[StoryProbe] hard-entry chosen after questioning; await actual combat defeat");
            return true;
        }

        /// <summary>真实首遇交战状态只供目标显示；不改持久化任务进度。</summary>
        public bool FirstGuardFightPending => Active && Phase == StoryProbePhase.FirstAttempt
            && _forcedDefeatQueued && !_firstDefeatResolved;
        public bool FirstGuardDefeated => Active && Phase == StoryProbePhase.FirstAttempt && _firstDefeatResolved;

        private void StartFirstGuardFight()
        {
            var player = Agent.Main;
            var guards = StoryProbeDefs.Npcs.Where(n => n.FirstMeetGuard).Select(n => GetAgent(n.Id))
                .Where(a => AgentControlHelper.SafeIsActive(a) && a.Health > 0f
                    && !AgentBrain.IsRoped(a) && !AgentBrain.IsHeldByStatus(a) && !AgentBrain.IsKnockedOut(a)
                    && AgentAIController.GetBrainForAgent(a) is AgentBrain b && !b.IsPerforming && !b.IsInCombat).ToList();
            if (player == null || guards.Count == 0 || AgentAIController.Instance == null
                || AttackTriggerMissionLogic.Instance == null)
            {
                DebugLogger.Log("[StoryProbe] first fight not started: player/guard/brain/arbiter missing; no defeat permitted");
                return;
            }
            if (Settings.Instance.IsInteractionDisabled()) return;
            _firstFightStarted = true;
            _firstFightRequestedAt = _time;
            _firstFightActors.Clear();
            foreach (var guard in guards)
                _firstFightActors.Add(StoryProbeDialogue.ResolveNpcId(guard));
            DebugLogger.Log($"[StoryProbe] first real fight broadcast: guards={string.Join(",", _firstFightActors)} player={player.Index}");
            int receipt = ++_duelSerial;
            _firstDuelReceipt = receipt;
            _firstDuelCallback = new Action<Agent>(loser =>
            {
                if (Current != this || _firstDuelReceipt != receipt) return;
                OnFirstDuelResolved(loser);
            });
            var excluded = new HashSet<Agent>(Mission.Agents.Where(a => !guards.Contains(a)));
            float radius = guards.Max(a => a.Position.Distance(player.Position)) + 1f;
            AgentAIController.Instance.BroadcastEventInRange(player.Position, radius, "duel", excluded,
                requireSight: false, isCrime: false, player, true, _firstDuelCallback);
        }

        private void OnFirstDuelResolved(Agent loser)
        {
            if (!Active || Current != this || Phase != StoryProbePhase.FirstAttempt || _firstDefeatResolved) return;
            // 广播中的某一人拒绝并不代表整组失败。正常 tick 确认无人参战后才重试。
            if (loser == null)
            {
                DebugLogger.Log("[StoryProbe] first fight member rejected/cancelled; await remaining guards or tick retry");
                return;
            }
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
            DebugLogger.Log("[StoryProbe] first guard hits resolved -> player down -> queue map sentence");
            // 先安排转场，视觉层异常也不能永久冻结已经真实败退的玩家。
            Schedule(Math.Max(0.1f, StoryProbeTuning.Current.Intro.ForcedDefeatHoldSec), () =>
            {
                DebugLogger.Log("[StoryProbe] defeated after actual combat -> end center mission; map sentence pending");
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
                if (_stage?.Active == true) _stage.Stop("defeat presentation exception: " + ex.Message, "LWN_story_stage_failure_exception");
            }
        }

        private void TickJailScene()
        {
            StoryProbeBehavior.RequestJailInquiry();
            Mission.EndMission();
        }

        public void CompleteJailDisguise() => StoryProbeBehavior.RequestJailInquiry();

        // ═══════════════════════════════════════════════════════════════
        // 第二次进场：四家 + 6 起事件 + 随从 + 目标点
        // ═══════════════════════════════════════════════════════════════

        private void SetupSecondPhase()
        {
            DebugLogger.Log("[StoryProbe] setup second phase (full prologue)");

            // 🔴 演出层（2026-11-13）：台本 StoryOpeningTimeline.json 驱动「布景 → 摆人 → 玩家出场」。
            //    准备失败 ⇒ 停止台本并安全回城镇菜单，不能绕过准备继续演。
            // 🔴 P3 顺序：**先准备、再开演**。准备批次（布景/摆人）没 Ready 之前，
            //    台本时间轴被 HoldWhile 闸住，不许“演员还没站位就开始演”。
            _stage ??= new StoryStageRunner(this);
            _prepFailureHandled = false;
            _sceneAbortHandled = false;
            _prep = new StoryPrepRunner(this);
            _prep.HoldSpawning = () => !_stage.OpeningCameraReady;
            bool staged = _stage.Begin("StoryOpeningTimeline");
            if (!staged)
            {
                // Begin 只在台本文件加载失败时返回 false（相机未就绪不再算失败，见 StoryStageRunner）。
                DebugLogger.Log("[StoryProbe] opening timeline script unavailable; asking player before leaving");
                ShowSceneAbortInquiry(
                    LWNTextHelper.ResolveText("LWN_story_abort_prep", "the scene layout data is missing"),
                    "opening timeline unavailable");
                return;
            }
            if (!_prep.Begin("second_meet"))
            {
                DebugLogger.Log("[StoryProbe] prep unavailable: " + _prep.Reason);
                ShowSceneAbortInquiry(
                    LWNTextHelper.ResolveText("LWN_story_abort_prep", "the scene layout data is missing")
                    + " " + (_prep.DescribeMissing() ?? ""),
                    "layout unavailable");
                return;
            }
            _stage.HoldWhile = () => _prep != null && !_prep.Ready;

            // 四家代表（表里有委托的那些）—— 台本/准备批次已排过位的跳过，避免"先站一次再被挪一次"的闪烁
            foreach (var def in StoryProbeDefs.Representatives)
            {
                if (staged && _stage.ScriptPlacesActor(def.Id)) continue;
                if (_prep != null && _prep.PlansActor(def.Id)) continue;
                SpawnNpc(def.Id, OffsetPos(def.OffsetForward, def.OffsetRight), def.Civilian);
            }

            // 守门人 + 仇家（全部从表里推，代码里不再列名单）
            var blockerDef = StoryProbeDefs.Blocker;
            if (blockerDef != null && !(staged && _stage.ScriptPlacesActor(blockerDef.Id))
                && !(_prep != null && _prep.PlansActor(blockerDef.Id)))
                SpawnNpc(blockerDef.Id, BlockerPos, blockerDef.Civilian);

            foreach (var def in StoryProbeDefs.QuestTargets)
            {
                if (staged && _stage.ScriptPlacesActor(def.Id)) continue;
                if (_prep != null && _prep.PlansActor(def.Id)) continue;
                SpawnNpc(def.Id, OffsetPos(def.OffsetForward, def.OffsetRight), def.Civilian);
            }

            // 通行权 = **主队背包里的通行令牌**（2026-11-16 用户裁定，实物凭据）。
            // 🔴 不再用存档布尔 `GateOpened` 当凭据：打晕 / 翻墙 / 潜行进来一次就会把它置真，
            //    于是"开过一次 = 以后永远免费通行"，正是用户报的那条。现在只有"被正式放行过"才有牌。
            // 用 OR：本场已经开过的门绝不能被这里的重新判定关回去。
            _gateOpen = _gateOpen || HasGatePass();
            if (_gateOpen)
                DebugLogger.Log($"[StoryGate] gate open at second-phase setup: item={StoryProbeTuning.Current?.Gate?.PassItem}");

            // 市井导演（常驻动作 / 桥段 / 事件循环）
            _director = new StreetLifeDirector(this);
            _director.Begin();

            // P4：工作点（只在准备批次建出道具之后才算得准位置）。
            if (_prep != null)
            {
                _work = new StoryWorkDirector(this);
                _workInitialized = false;
            }

            // 随从 + 3 句
            EnsureCompanion();

            // 回忆、观察目标与跳过选择由台本 inquiry 原子呈现。
        }

        /// <summary>
        /// 场景起不来的统一收场（2026-11-16）：**先弹窗说清原因，点确认才离开**。
        /// 覆盖此前"无声踢人"的两条路径：开场镜头拿不到、布景准备数据缺失。
        /// 60 秒兜底，避免玩家没看见弹窗就被永远关在一场坏掉的戏里。
        /// </summary>
        private void ShowSceneAbortInquiry(string reasonText, string logReason)
        {
            bool answered = false;
            void Leave()
            {
                if (answered) return;
                answered = true;
                try
                {
                    StoryProbeBehavior.CancelPrologue(logReason);
                    Mission.EndMission();
                }
                catch (Exception ex) { DebugLogger.Log($"[StoryProbe] aborted scene exit failed: {ex.Message}"); }
            }
            try
            {
                string body = LWNTextHelper.ResolveCompound("LWN_story_scene_abort_detail",
                    "The scene could not start.\n\n{REASON}\n\nPress OK to return to the town menu.",
                    ("REASON", string.IsNullOrEmpty(reasonText) ? "unknown reason" : reasonText));
                InformationManager.ShowInquiry(new InquiryData(
                    LWNTextHelper.ResolveText("LWN_story_prep_failed_title", "Scene not ready"),
                    body, true, false,
                    LWNTextHelper.ResolveText("LWN_story_prep_failed_ok", "OK"),
                    "",
                    Leave, null));
                Schedule(60f, () =>
                {
                    if (answered) return;
                    DebugLogger.Log("[StoryProbe] scene abort inquiry timed out; leaving scene");
                    Leave();
                });
                return;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] scene abort inquiry failed: {ex.Message}");
            }
            Leave();
        }

        /// <summary>
        /// 准备失败的收场：先弹窗告诉玩家缺什么，**点确认才离开**；不点就一直留在原地
        /// （60 秒兜底，避免玩家没看见弹窗就被永远关在一场坏掉的戏里）。
        /// </summary>
        private void ShowPrepFailureInquiry(string missing)
        {
            bool answered = false;
            try
            {
                string body = LWNTextHelper.ResolveCompound("LWN_story_prep_failed_detail",
                    "The scene could not be laid out, so it will not start.\n\nMissing: {MISSING}\n\nPress OK to return to the town menu.",
                    ("MISSING", string.IsNullOrEmpty(missing)
                        ? LWNTextHelper.ResolveText("LWN_story_prep_failed_unknown", "unknown reason")
                        : missing));
                InformationManager.ShowInquiry(new InquiryData(
                    LWNTextHelper.ResolveText("LWN_story_prep_failed_title", "Scene not ready"),
                    body, true, false,
                    LWNTextHelper.ResolveText("LWN_story_prep_failed_ok", "OK"),
                    "",
                    () => { if (answered) return; answered = true; LeaveAfterPrepFailure(); },
                    null));
                Schedule(60f, () =>
                {
                    if (answered) return;
                    answered = true;
                    DebugLogger.Log("[StoryProbe] prep failure inquiry timed out; leaving scene");
                    LeaveAfterPrepFailure();
                });
                return;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] prep failure inquiry failed: {ex.Message}");
            }
            if (!answered) { answered = true; LeaveAfterPrepFailure(); }
        }

        private void LeaveAfterPrepFailure()
        {
            try
            {
                StoryProbeBehavior.CancelPrologue("layout preparation failed");
                Mission.EndMission();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] prep failure exit failed: {ex.Message}");
            }
        }

        private void TickSecondPhase(float dt)
        {
            var player = Agent.Main;
            _prep?.Tick(dt);
            // 准备就绪后按布局给每个人写职责点（只写一次）。
            if (_prep != null && _prep.Ready && !_dutyPointsApplied)
            {
                _dutyPointsApplied = true;
                ApplyDutyPoints();
            }
            if (_prep?.Ready == true && _work != null && !_workInitialized)
            {
                _workInitialized = true;
                _work.Begin("second_meet");
            }

            // 🔴 准备失败 ⇒ 不许照常开演：停台本、交还机位与玩家。
            // 🔴 2026-11-16 用户裁定：**不要直接把玩家踢出场景** —— 先弹窗说清缺什么，
            //    玩家点确认之后再退出。以前这里是 `CancelPrologue + Mission.EndMission` 立刻执行。
            if (_prep != null && _prep.Failed && !_prepFailureHandled)
            {
                _prepFailureHandled = true;
                _stage?.Stop("prep failed: " + _prep.Reason);
                DebugLogger.Log("[StoryProbe] prep FAILED, performance cancelled: " + _prep.Reason);
                ShowPrepFailureInquiry(_prep.DescribeMissing());
            }

            if (_prep?.Failed == true) return;

            // 开场机位重试用尽（相机视图始终没就位）——同样先弹窗再离开，不许无声踢人。
            if (_stage?.OpeningCameraFailed == true && !_sceneAbortHandled)
            {
                _sceneAbortHandled = true;
                DebugLogger.Log("[StoryProbe] opening camera failed after retries; asking player before leaving");
                ShowSceneAbortInquiry(
                    LWNTextHelper.ResolveText("LWN_story_abort_camera", "the opening camera could not be placed"),
                    "opening camera unavailable");
                return;
            }

            if (_stage?.Active == true) return; // 镜头演出占用期间不识破、不开战、不判通关。
            _posts?.Tick(dt, "second_meet");
            if (_prep?.Ready == true && _posts?.HasRules("second_meet") == true)
                _posts.TryRequestQuestion("second_meet");
            _work?.Tick(dt);
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

        /// <summary>
        /// 让玩家当场换上指定物品（2026-11-16）：把「行囊里的一件」穿到**本场景实际使用的那套装备**
        /// （城镇剧情 = 民用装），旧衣由 ReplaceEquippedItem 退回行囊 ⇒ 守恒；随后立刻刷新场景外观。
        /// 只换一套而不是两套：玩家只有一件实物，换两套会凭空多穿一份。
        /// </summary>
        public static bool WearStoryItem(string itemId, string slotName, out string error)
        {
            error = null;
            var hero = Hero.MainHero;
            var mission = Mission.Current;
            if (hero == null || mission == null) { error = "no mission/hero"; return false; }
            var item = StoryProbeDefs.FindItem(itemId);
            if (item == null) { error = "item not found: " + itemId; return false; }
            var slot = EquipmentIndex.Body;
            if (!string.IsNullOrEmpty(slotName)
                && !Enum.TryParse(slotName, true, out slot))
            { error = "bad slot: " + slotName; return false; }
            var equipment = mission.DoesMissionRequireCivilianEquipment
                ? hero.CivilianEquipment : hero.BattleEquipment;
            if (equipment == null) { error = "no equipment"; return false; }
            // 已经穿在身上 = 幂等成功，不再扣行囊。
            if (equipment[slot].Item == item) return true;
            var roster = hero.PartyBelongedTo?.ItemRoster ?? MobileParty.MainParty?.ItemRoster;
            if (roster == null) { error = "no item roster"; return false; }
            if (roster.GetItemNumber(item) < 1) { error = "item not in inventory: " + itemId; return false; }
            // 行囊 → 身上：先扣实物（Sink 到 null = 穿到身上，不是虚空），旧衣由 ReplaceEquippedItem 退回。
            int moved = AgentControlHelper.TransferItems(hero, null, item, 1);
            if (moved != 1) { error = "transfer failed"; return false; }
            if (!AgentControlHelper.ReplaceEquippedItem(hero, equipment, slot, new EquipmentElement(item)))
            { error = "equip failed"; return false; }
            if (AgentControlHelper.SafeIsActive(Agent.Main))
                AgentControlHelper.RefreshSpawnEquipment(Agent.Main, equipment);
            DebugLogger.Log($"[StoryProbe] wore '{itemId}' into {slot} (civilian={mission.DoesMissionRequireCivilianEquipment})");
            InformationManager.DisplayMessage(new InformationMessage(
                LWNTextHelper.ResolveText("LWN_story_wore_disguise", "You put on the noble costume.")));
            return true;
        }

        /// <summary>
        /// 玩家在物品栏里换装后的外观同步（**由事件触发，不轮询**）。
        /// 只有装备真的变了才调 `UpdateSpawnEquipmentAndRefreshVisuals`（重建武器实体有开销）。
        /// </summary>
        public static void SyncPlayerAppearanceFromInventory(string reason)
        {
            try
            {
                var host = Current;
                if (host == null || !host.Active) return;   // 只在剧情探针场景里接管
                var mission = Mission.Current;
                var player = Agent.Main;
                var hero = Hero.MainHero;
                if (mission == null || player == null || hero == null) return;
                if (!AgentControlHelper.SafeIsActive(player)) return;
                var equipment = mission.DoesMissionRequireCivilianEquipment
                    ? hero.CivilianEquipment : hero.BattleEquipment;
                if (equipment == null || SameEquipment(player.SpawnEquipment, equipment)) return;
                AgentControlHelper.RefreshSpawnEquipment(player, equipment);
                DebugLogger.Log($"[StoryProbe] appearance refreshed from inventory ({reason})");
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] appearance sync failed ({reason}): {ex.Message}"); }
        }

        private static bool SameEquipment(Equipment a, Equipment b)
        {
            if (a == null || b == null) return a == b;
            for (int i = 0; i < 12; i++)
            {
                var slot = (EquipmentIndex)i;
                if (a[slot].Item != b[slot].Item) return false;
                if (a[slot].ItemModifier != b[slot].ItemModifier) return false;
            }
            return true;
        }

        /// <summary>验收用：真实穿戴状态（不是存档旗标）。不在场景里时返回 n/a。</summary>
        public static string DescribeWearingNoble()
        {
            if (Mission.Current == null || Agent.Main == null) return "n/a";
            return IsWearingStoryDisguise(true) ? "true" : "false";
        }

        /// <summary>对话动作只排请求，等原版对话退出再交给 Brain。</summary>
        public bool CanRequestPlayerDetention(string npcId, string mode)
        {
            var target = GetAgent(npcId);
            return Active && Current == this && Mission.Current == Mission
                && (mode == "intro_sentence" || mode == "custody")
                && (mode != "intro_sentence" || (!StoryProbeState.Data.JailDone
                    && Settlement.CurrentSettlement?.StringId == StoryProbeSettings.SettlementId))
                && AgentControlHelper.SafeIsActive(Agent.Main) && Agent.Main.Health > 0f
                && AgentControlHelper.SafeIsActive(target) && target != Agent.Main && target.Health > 0f
                && !AgentBrain.IsRoped(target) && !AgentBrain.IsHeldByStatus(target) && !AgentBrain.IsKnockedOut(target)
                && Hero.MainHero != null && !Hero.MainHero.IsPrisoner
                && Settlement.CurrentSettlement?.Party != null && PlayerDetentionBehavior.Instance != null
                && !PlayerDetentionBehavior.IsDetentionActive && !_firstFightStarted && _duelCallbacks.Count == 0
                && !Settings.Instance.IsInteractionDisabled()
                && (_playerDetentionMode == null
                    || (_playerDetentionMode == mode && _playerDetentionActor == npcId));
        }

        public bool RequestPlayerDetention(string npcId, string mode)
        {
            if (!CanRequestPlayerDetention(npcId, mode)) return false;
            if (_playerDetentionMode != null) return true;
            // 只能撤本 Host 尚未发出的请求；已参战时上方拒绝，不能替外部系统停战。
            _forcedDefeatQueued = false;
            _fightRequests.Clear();
            _playerDetentionMode = mode;
            _playerDetentionActor = npcId;
            DebugLogger.Log($"[StoryProbe] surrender queued: actor={npcId} mode={mode}; no combat/defeat receipt");
            return true;
        }

        private bool PumpPlayerDetention()
        {
            if (_playerDetentionMode == null) return false;
            if (Campaign.Current?.ConversationManager?.IsConversationInProgress == true) return true;
            if (!CanRequestPlayerDetention(_playerDetentionActor, _playerDetentionMode))
            {
                DebugLogger.Log($"[StoryProbe] surrender handoff cancelled: actor={_playerDetentionActor} mode={_playerDetentionMode}; context unavailable");
                _playerDetentionMode = null;
                _playerDetentionActor = null;
                return false;
            }
            string mode = _playerDetentionMode;
            var settlement = Settlement.CurrentSettlement;
            _playerDetentionMode = null;
            _playerDetentionActor = null;
            if (mode == "intro_sentence")
            {
                StoryProbeBehavior.RequestJailInquiry();
                ApplyFirstArrestBounty();
            }
            DebugLogger.Log($"[StoryProbe] surrender handoff: mode={mode} settlement={settlement.StringId}; ending mission");
            Mission.EndMission();
            if (mode == "custody")
                // 与既有主动坐牢入口一致；不拿同城任意进行中事件替玩家定罪。
                PlayerDetentionBehavior.ApplyImmediateDetention(settlement, null, "story-dialogue-surrender");
            return true;
        }

        /// <summary>对话动作只排请求，等原版对话退出再交给 Brain。</summary>
        public bool RequestStoryFight(string npcId)
        {
            var target = GetAgent(npcId);
            if (!Active || Current != this || Phase != StoryProbePhase.Second || target == null
                || !AgentControlHelper.SafeIsActive(target) || _playerDetentionMode != null) return false;
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
                if (!StoryProbeState.Data.QuestResolutions.ContainsKey(def.Id))
                    StoryProbeState.Data.QuestResolutions[def.Id] = "force";
                StoryProbeDialogue.ResolveCompletedQuestConflict(def.Id);
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
                    ResolveApproach("force", "dockhands", -20, true, null, "blocker");
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
                        ResolveApproach("guile", "nightwalkers", -20, true, "hazard_dragged_the_guard", "blocker");
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
            // 谁离得近谁推（副卫也履职），并留下可查的日志 —— 以前这里一行日志都没有，
            // 出现"人就这么走过去了"时查不出是没推还是推了没拦住。
            Agent pusher = NearestGateGuard();
            if (pusher != null && AgentAIController.GetBrainForAgent(pusher)?.IsInCombat == true) return;
            _nextBarrierAt = _time + gt.PushBackCooldownSec;
            float lateralAtPush = GateLateral(player.Position);
            Vec3 outside = _gateCenter - new Vec3(Forward.x, Forward.y, 0f) * gt.PushBackMeters;
            player.TeleportToPosition(outside);
            _lastGatePosition = outside;
            _crossedGate = false;
            string pusherId = pusher?.Character?.StringId ?? "none";
            DebugLogger.Log($"[StoryGate] pushback by {pusherId} along={along:F2} lateral={lateralAtPush:F2} → outside {gt.PushBackMeters:F1}m");
            // 朝向交给 Brain 质问/反应，屏障不再另写守卫身体。
            SayOnAgent(pusher ?? GetAgent("blocker"), "LWN_story_blocker_bar", "Papers.", SpeechPriority.Warning);
            InformationManager.DisplayMessage(new InformationMessage(
                LWNTextHelper.ResolveText("LWN_story_gate_blocked", "He pushes you back.")));
        }

        /// <summary>离玩家最近、还能正常履职的守门人（数据里的 FirstMeetGuard 名单，不写死 id）。</summary>
        private Agent NearestGateGuard()
        {
            Agent best = null;
            float bestDist = float.MaxValue;
            foreach (var def in StoryProbeDefs.Npcs)
            {
                if (def == null || !def.FirstMeetGuard) continue;
                var a = GetAgent(def.Id);
                if (a == null || !AgentControlHelper.SafeIsActive(a) || a.Health <= 0f) continue;
                if (AgentBrain.IsRoped(a) || AgentBrain.IsHeldByStatus(a) || AgentBrain.IsKnockedOut(a)) continue;
                float d = Agent.Main != null ? a.Position.Distance(Agent.Main.Position) : 0f;
                if (d < bestDist) { bestDist = d; best = a; }
            }
            return best;
        }

        /// <summary>当前阶段的岗位感知半径（数据决定；没数据回退 8 米）。</summary>
        public float PostZoneRangeMeters
        {
            get
            {
                try
                {
                    var file = StoryLayoutRegistry.Published;
                    if (file == null) return 8f;
                    string stage = Phase == StoryProbePhase.Second ? "second_meet" : "first_meet";
                    float best = 0f;
                    foreach (var b in file.Behaviors.Values)
                    {
                        if (b == null || b.Kind != "post" || b.Stages == null || !b.Stages.Contains(stage)) continue;
                        if (b.Perception.RangeMeters > best) best = b.Perception.RangeMeters;
                    }
                    return best > 0f ? best : 8f;
                }
                catch { return 8f; }
            }
        }

        /// <summary>
        /// 玩家是否进了这名守卫的**岗位职责区**：他在我感知半径内、我还站着、门没放行、玩家也还没走过去。
        /// 与旧 `IsPlayerInGateApproach`（门轴 ±3 米窄带）的区别 = 按"岗位到玩家的距离"判，
        /// 不再要求玩家正好踩中线 —— 这才是"每个守卫有自己的职责区"。
        /// </summary>
        public bool IsPlayerInPostZone(Agent guard, float rangeMeters)
        {
            if (!_gateGeometryReady || guard == null || Agent.Main == null) return false;
            if (_gateOpen) return false;                                  // 已放行 = 不用再拦
            if (!AgentControlHelper.SafeIsActive(guard) || guard.Health <= 0f) return false;
            if (GateAlong(Agent.Main.Position) >= StoryProbeTuning.Current.Gate.PastMeters) return false; // 人已经过去了
            if (rangeMeters <= 0f) return false;
            return guard.Position.Distance(Agent.Main.Position) <= rangeMeters;
        }

        /// <summary>同上，半径用当前阶段岗位数据的感知半径。</summary>
        public bool IsPlayerInPostZone(Agent guard) => IsPlayerInPostZone(guard, PostZoneRangeMeters);

        /// <summary>
        /// 追击 leash（2026-11-16 用户裁定）：玩家跑出职责区（超过两倍岗位半径）就撤销这次拦截，
        /// 不许一路跟着跑出城。**回岗位不在这里** —— 那是默认行为，归 `AgentBrain.DecideDefaultBehavior`
        /// （职责点由 `ApplyDutyPoints` 写进脑里：离岗走回去、到位就站住）。
        /// </summary>
        private void TickGuardLeash()
        {
            string stageId = Phase == StoryProbePhase.FirstAttempt ? "first_meet" : "second_meet";
            if (_posts?.HasRules(stageId) == true)
            {
                _posts.TickLeash(stageId);
                return;
            }
            if (!_gateGeometryReady) return;
            float zone = PostZoneRangeMeters;
            var player = Agent.Main;

            foreach (var def in StoryProbeDefs.Npcs)
            {
                if (def == null || !def.FirstMeetGuard) continue;
                var agent = GetAgent(def.Id);
                if (agent == null || !AgentControlHelper.SafeIsActive(agent) || agent.Health <= 0f) continue;
                if (AgentBrain.IsRoped(agent) || AgentBrain.IsHeldByStatus(agent) || AgentBrain.IsKnockedOut(agent)) continue;
                var brain = AgentAIController.GetBrainForAgent(agent);
                if (brain == null || brain.IsInCombat) continue;

                // ① 追出职责区就放弃：玩家已经跑远了，不许一路跟过去。
                if (brain.IsStoryIntercepting && player != null && !IsPlayerInPostZone(agent, zone * 2f))
                {
                    try
                    {
                        AgentAIController.Instance?.SendEventToAgent(agent, "event_story_intercept_cancel");
                        DebugLogger.Log($"[StoryGate] {def.Id}: player left the duty zone -> chase cancelled");
                    }
                    catch (Exception ex) { DebugLogger.Log($"[StoryGate] chase cancel failed: {ex.Message}"); }
                    continue;
                }

            }
        }

        /// <summary>
        /// 把每个人的**职责点**写进他自己的脑里（通用规则，2026-11-16 用户裁定）：
        ///   数据来源 = 本场布局里这名演员的站位（Standing → Post/WorkPoint）；
        ///   布局里没有的（首遇只有两名守卫）→ 用出生点。
        ///   写完由 `AgentBrain.DecideDefaultBehavior` 消费：离开岗位就走回去、到位就站住。
        ///   屠夫回肉铺、守卫回门口——区别只在 JSON 的点位，代码里不区分职业。
        /// </summary>
        private void ApplyDutyPoints()
        {
            // 数据来源：本阶段布局的 Standing → Point（位置已在准备阶段解好；朝向在这里读）
            StoryLayoutDef layout = null;
            var file = StoryLayoutRegistry.Published;
            string stageId = Phase == StoryProbePhase.Second ? "second_meet" : "first_meet";
            if (file != null && file.Stages.TryGetValue(stageId, out var stage) && stage != null)
                file.Layouts.TryGetValue(stage.Layout ?? "", out layout);

            int applied = 0;
            foreach (var def in StoryProbeDefs.Npcs)
            {
                if (def == null || string.IsNullOrEmpty(def.Id)) continue;
                var agent = GetAgent(def.Id);
                if (agent == null) continue;
                var brain = AgentAIController.GetBrainForAgent(agent);
                if (brain == null) continue;

                Vec3 point;
                if (_prep != null && _prep.TryGetStandingPosition(def.Id, out var stand)) point = stand;
                else if (_postPositions.TryGetValue(def.Id, out var spawn)) point = spawn;
                else continue;

                // 朝向：唯一来源 = 点位自己的 Facing（站位不再带方向）
                float facing = -1f;
                var standing = layout?.Standing.FirstOrDefault(x => x != null && x.Actor == def.Id);
                var pt = layout?.FindPoint(standing?.Point);
                if (pt != null) facing = pt.Facing;

                brain.SetDutyPoint(point, facing);
                applied++;
            }
            DebugLogger.Log($"[StoryGate] duty points applied: {applied} npc(s) (stage={stageId}, facing from layout)");
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
            if (_goalReached || StoryProbeState.Data.GoalReached)
            {
                _goalReached = true;
                return;
            }
            if (StoryProbeState.Data.CancellationPending) { _goalReached = false; return; }
            if (_time < _belongingsRestoreRetryAt) { _goalReached = false; return; }
            try
            {
                DebugLogger.Log($"[StoryProbe] goal reached (approach={StoryProbeState.Data.ChosenApproach}) -> restoring belongings");
                if (!RestorePlayerBelongings() || !PrologueQuestLauncher.CancelAll())
                {
                    _goalReached = false;
                    _belongingsRestoreRetryAt = _time + 2f;
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveText("LWN_info_equipment_error") + " " + LWNTextHelper.ResolveText("LWN_plan_btn_retry")));
                    return;
                }
                _goalReached = true;
                Phase = StoryProbePhase.Done;
                StoryProbeState.Data.GoalReached = true;
                StoryProbeState.Phase = StoryProbePhase.Done;
                StoryProbeState.SetGoal("done");
                CancelRuntime("prologue completed");

                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveText("LWN_story_narr_done",
                        "You are inside. Whatever happens from here, the gate is behind you.")));
                MBInformationManager.AddQuickInformation(new TextObject(
                    LWNScreenGoalText.Resolve()));
            }
            catch (Exception ex)
            {
                _goalReached = StoryProbeState.Data.GoalReached;
                if (!_goalReached) _belongingsRestoreRetryAt = _time + 2f;
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
                _postPositions[npcId] = pos;
                ApplyStoryPersona(def, hero, existing);
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
                    _postPositions[npcId] = pos;
                    ApplyStoryPersona(def, hero, agent);
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
            {
                ApplyStoryPersona(def, cached);
                return cached;
            }

            if (StoryProbeState.Data.SpawnedHeroIds.TryGetValue(def.Id, out string savedId) && !string.IsNullOrEmpty(savedId))
            {
                var found = Hero.AllAliveHeroes.FirstOrDefault(h => h != null && h.StringId == savedId);
                if (found != null)
                {
                    _heroes[def.Id] = found;
                    ApplyStoryPersona(def, found);
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
                ApplyStoryPersona(def, hero);
                DebugLogger.Log($"[StoryProbe] hero created '{def.Id}' -> {hero.StringId} ({hero.Name})");
                return hero;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] EnsureStoryHero({def.Id}) failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>角色设定由 Cast 与本地化提供；更新旧档身份，不清除对话历史或经历。</summary>
        private static void ApplyStoryPersona(StoryNpcDef def, Hero hero, Agent agent = null)
        {
            var name = new TextObject("{=" + def.NameKey + "}" + def.NameFallback);
            hero.SetName(name, name);
            var memory = AllNpcMemoryManager.GetMemory(hero.StringId);
            if (memory != null)
            {
                if (!string.IsNullOrEmpty(def.IdentityKey))
                    memory._profile.IdentityOverride = LWNTextHelper.ResolveText(def.IdentityKey, def.IdentityFallback);
                if (!string.IsNullOrEmpty(def.BackgroundKey))
                    memory.BackgroundStory = string.Join("\n", new[] {
                        LWNTextHelper.ResolveText(def.BackgroundKey, def.BackgroundFallback),
                        LWNTextHelper.ResolveText(def.GoalKey, def.GoalFallback),
                        LWNTextHelper.ResolveText(def.TroubleKey, def.TroubleFallback),
                        LWNTextHelper.ResolveText(def.PlayerHelpKey, def.PlayerHelpFallback)
                    }.Where(s => !string.IsNullOrEmpty(s)));
                if (!string.IsNullOrEmpty(def.TemperamentKey))
                    memory.Personality = LWNTextHelper.ResolveText(def.TemperamentKey, def.TemperamentFallback);
                if (!string.IsNullOrEmpty(def.SpecialtyKey))
                    memory.Specialty = LWNTextHelper.ResolveText(def.SpecialtyKey, def.SpecialtyFallback);
            }
            if (agent != null)
                ReactiveAgent.ApplyPlan(agent, new ReactivePlan { Role = def.ReactiveRole, Personality = def.Personality });
            DebugLogger.Log($"[StoryProbe] persona applied: actor={def.Id} hero={hero.StringId} name={hero.Name} identity={memory?._profile?.IdentityOverride} role={def.ReactiveRole}");
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
            => npcId != null && _heroes.TryGetValue(npcId, out var h) ? h : null;

        public IEnumerable<KeyValuePair<string, Agent>> AllAgents => _agents;

        public StreetLifeDirector Director => _director;

        // ═══════════════════════════════════════════════════════════════
        // 对话 / 说话
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// 开一场剧情对话。入口节点由 StoryProbeDialogue 决定（默认按五步关系挑；传 entryNode 则强制）。
        /// 🔴 注入必须早于 StartConversation（ConversationEntryPatch 的 Prefix 统一收口）。
        /// </summary>
        public void StartStoryConversation(Agent agent, string entryNode = null, string triggerId = "player_talk",
            string dialogueId = null)
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

                // 🔴 P0-D.3（2026-11-15）：所有非显式入口都走**同一请求入口** —— 触发器冷却、遭遇去重、
                //    入口裁决、会话编号与取消令牌都在 StoryDialogueRuntime 里；这里只负责把结果交给注入器。
                //    已有发布定义但请求被拒 = 明确拒绝，不回落旧文件猜入口。
                string resolvedId = !string.IsNullOrEmpty(dialogueId)
                    ? dialogueId : StoryDialogueResolver.FindDialogueForActor(npcId, triggerId);
                if (string.IsNullOrEmpty(resolvedId))
                {
                    DebugLogger.Log($"[StoryDialogue] no definition for actor={npcId} trigger={triggerId}; conversation skipped");
                    return;
                }
                if (!StoryDialogueRuntime.TryRequest(resolvedId, triggerId, npcId, null, null,
                        out var session, out string reason))
                {
                    DebugLogger.Log($"[StoryDialogue] request rejected dialogue={resolvedId} trigger={triggerId} actor={npcId}: {reason}");
                    return;
                }
                if (!string.IsNullOrEmpty(entryNode) && entryNode != session.Entry.Node)
                {
                    StoryDialogueRuntime.CancelSession("requested node differs from resolved entry");
                    return;
                }
                if (!StoryDialogueRuntime.ConfirmForOpen(session, out reason))
                {
                    StoryDialogueRuntime.CancelSession(reason);
                    DebugLogger.Log($"[StoryDialogue] request {session.Request.Id} cancelled before open: {reason}");
                    return;
                }
                entryNode = session.Entry.Node;
                DebugLogger.Log($"[StoryDialogue] request {session.Request.Id} session {session.Id} entry={entryNode} actor={npcId}");

                StoryProbeDialogue.PendingEntryNode = entryNode;
                // 「见过」由对话节点的 mark:met 动作标记 —— 在注入前统一标会让入口永远跳到 _hub，看不到 intro。
                // 但**事件接入**不走 intro 节点，这一条由事件本身完成"认识"，所以在这里补标。
                if (triggerId == "event_join" || (!string.IsNullOrEmpty(entryNode) && entryNode.StartsWith("event_")))
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

        public Agent ResolveSpeechListener(string identity)
            => identity == "player" ? Agent.Main : GetAgent(identity ?? "");

        public void SayLine(string npcId, string key, string fallback, SpeechPriority priority = SpeechPriority.Chat)
        {
            var agent = GetAgent(npcId);
            if (agent == null) return;
            try
            {
                SayOnAgent(agent, key, fallback, priority);
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] SayLine({npcId}) failed: {ex.Message}"); }
        }

        public void SayOnAgent(Agent agent, string key, string fallback, SpeechPriority priority = SpeechPriority.Chat,
            Agent listener = null, bool forwardToNearby = false, bool selfTalk = false)
        {
            if (agent == null || !AgentControlHelper.SafeIsActive(agent)) return;
            try
            {
                listener = selfTalk ? null : listener ?? Agent.Main;
                if (!selfTalk && (!AgentControlHelper.SafeIsActive(listener) || listener == agent || listener.Health <= 0f)) return;
                SpeechChannel.Say(agent, LWNTextHelper.ResolveText(key, fallback), priority,
                    SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(agent), listener,
                        selfTalk ? "story-self-talk" : "story", null, listener),
                    0f, forwardToNearby);

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
            // 存档布尔只当"本轮剧情里开过门"的历史痕迹，**不再**作为下次进城的凭据（凭据 = 背包里的令牌）。
            StoryProbeState.Data.GateOpened = true;
            TryGrantGatePass(approach);
            StoryProbeState.Data.ChosenApproach = approach;
            StoryProbeState.Data.TalentPoints += 0;   // 天赋点由 ResolveApproach 统一加（避免重复）

            // 开门只是撤销剧情限制；不传送正在被绑、扛、对话或演出的守卫。
            // 后续移动/朝向仍由已有 Brain、原版对话、钩索各自的身体所有者处理。
            StoryProbeState.SetGoal("tool");

            if (!string.IsNullOrEmpty(messageKey))
                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveText(messageKey, messageFallback)));
            DebugLogger.Log($"[StoryProbe] gate opened approach={approach}");
            InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText(
                "LWN_story_gate_finish_notice", "Finish any outstanding jobs before stepping inside. Entering ends this chapter.")));
        }

        // ═══════════════════════════════════════════════════════════════
        // 通行权 = 实物凭据（2026-11-16 用户裁定）
        //   持有 = 主队背包里有那枚通行令牌（`StoryProbeTuning.json` → Gate.PassItem）。
        //   存档 = 背包由引擎自己保存，我们不另造字段；令牌丢/卖掉了就等于没有通行权。
        //   发放 = 只有「守卫正式放行」的路线才发（Gate.PassApproaches，默认 charm / money）；
        //          打晕、绑走、潜行、翻墙、硬闯都不发 —— 那是进去了，不是被允许。
        // ═══════════════════════════════════════════════════════════════

        /// <summary>玩家现在是否持有通行令牌（= 被正式放行过）。</summary>
        public static bool HasGatePass()
        {
            try
            {
                string id = StoryProbeTuning.Current?.Gate?.PassItem;
                if (string.IsNullOrEmpty(id)) return false;
                var item = StoryProbeDefs.FindItem(id);
                if (item == null) return false;
                var roster = PartyBase.MainParty?.ItemRoster;
                return roster != null && roster.GetItemNumber(item) > 0;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryGate] HasGatePass failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>这条过门路线算不算「守卫正式放行」；算就发一枚令牌（已持有则不重复发）。</summary>
        private static void TryGrantGatePass(string approach)
        {
            try
            {
                var gt = StoryProbeTuning.Current?.Gate;
                string id = gt?.PassItem;
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(approach)) return;

                bool authorized = false;
                var list = gt.PassApproaches;
                if (list != null)
                {
                    for (int i = 0; i < list.Length; i++)
                    {
                        if (list[i] == null) continue;
                        if (string.Equals(list[i].Trim(), approach.Trim(), StringComparison.OrdinalIgnoreCase))
                        { authorized = true; break; }
                    }
                }
                if (!authorized) return;
                if (HasGatePass()) return;                 // 幂等：一个人只发一枚
                if (Hero.MainHero == null) return;

                if (!StoryProbeDefs.GiveItem(Hero.MainHero, id, 1))
                {
                    DebugLogger.Log($"[StoryGate] pass token grant FAILED: item={id} approach={approach}");
                    return;
                }
                DebugLogger.Log($"[StoryGate] pass token granted: item={id} approach={approach}");
                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveText("LWN_story_gate_pass_granted",
                        "The guard hands you a gate pass. Show it next time and you walk straight in.")));
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryGate] TryGrantGatePass failed: {ex.Message}"); }
        }

        /// <summary>收回通行令牌（剧情重置/调试用）。返回是否确实收回了东西。</summary>
        public static bool RevokeGatePass(string reason)
        {
            try
            {
                string id = StoryProbeTuning.Current?.Gate?.PassItem;
                if (string.IsNullOrEmpty(id) || Hero.MainHero == null) return false;
                var item = StoryProbeDefs.FindItem(id);
                var roster = PartyBase.MainParty?.ItemRoster;
                int have = item != null && roster != null ? roster.GetItemNumber(item) : 0;
                if (have <= 0) return false;
                bool ok = StoryProbeDefs.RemoveItem(Hero.MainHero, id, have);
                DebugLogger.Log($"[StoryGate] pass token revoked: item={id} count={have} ok={ok} reason={reason}");
                return ok;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryGate] RevokeGatePass failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>本次选择结算：实际交互对象的个人关系 / 天赋点 / 通缉 / 隐患。</summary>
        /// <param name="factionId">兼容旧调用的路线归属，不自动结算势力关系。</param>
        /// <param name="gatekeeperDelta">**守门人本人**的关系变化（§9 单独一列：钱/冒充 −5、技巧 0、武力 −20）</param>
        public void ResolveApproach(string approach, string factionId, int gatekeeperDelta, bool crime, string hazardId,
            string relationshipActorId = null)
        {
            if (!Active || Current != this || Phase != StoryProbePhase.Second || StoryProbeState.Data.ApproachResolved) return;
            StoryProbeState.Data.ApproachResolved = true;
            StoryProbeState.Data.ChosenApproach = approach;
            StoryProbeState.Data.TalentPoints += 1;
            if (!string.IsNullOrEmpty(hazardId)) StoryProbeState.AddHazard(hazardId);

            // 个人关系只记实际交互对象；无对象的潜行/翻墙不扣无关人物。
            try
            {
                if (Hero.MainHero != null)
                {
                    var gateHero = !string.IsNullOrEmpty(relationshipActorId) ? GetHero(relationshipActorId) : null;
                    if (gateHero != null && gatekeeperDelta != 0)
                        ChangeRelationAction.ApplyRelationChangeBetweenHeroes(gateHero, Hero.MainHero, gatekeeperDelta);
                    DebugLogger.Log($"[StoryProbe] approach settled: route={approach} actor={relationshipActorId ?? "none"} personalDelta={(gateHero != null ? gatekeeperDelta : 0)} factionDelta=0 talent=1");
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

            // 当场反馈实际对象、个人关系和天赋点。
            try
            {
                string path = approach == "money"
                    ? LWNTextHelper.ResolveText("LWN_story_ability_money", "Money")
                    : LWNTextHelper.ResolveText(StoryProbeDefs.AbilityDisplayKey(
                    approach == "charm" ? StoryAbility.Charm : approach == "guile" ? StoryAbility.Guile : StoryAbility.Force),
                    approach);
                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveCompound("LWN_story_narr_approach",
                        "Path: {PATH} — talent +1; relationship with {TARGET}: {REL}.",
                        ("PATH", path), ("TARGET", GetHero(relationshipActorId)?.Name?.ToString()
                            ?? LWNTextHelper.ResolveText("LWN_story_relation_no_target", "no participant")),
                        ("REL", (!string.IsNullOrEmpty(relationshipActorId) && GetHero(relationshipActorId) != null
                            ? gatekeeperDelta : 0).ToString()))));
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
                    // P2/S3：只借用**已有的可用同伴**（已经在本队、还活着）；不自动招募、
                    // 不把流浪者拉进队、结束时也不移队。没有就走旁白分支。
                    comp = Hero.AllAliveHeroes.FirstOrDefault(h =>
                        h != null && h.IsAlive && !h.IsHumanPlayerCharacter
                        && h.IsPlayerCompanion && h.PartyBelongedTo == MobileParty.MainParty);
                }

                if (comp == null)
                {
                    // 没得借 → 旁白兜底，不阻断（§13 风险表）
                    DebugLogger.Log("[StoryProbe] no available companion to borrow; narration fallback");
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveText("LWN_story_intro_no_companion",
                            "There is no one left to walk in with you.")));
                    return;
                }

                // 借用 ≠ 招募：不再调用 AddCompanionAction，离开场景也不移队。
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
                "You've served your sentence. You still need the guard's permission to pass.",
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
            bool sentenceContext = Mission.Current == null && StoryProbeState.Phase == StoryProbePhase.Jail
                && d.TutorialLost && !string.IsNullOrEmpty(d.DetentionOwnerId);
            if (hero == null || d.CancellationPending
                || (!sentenceContext && CampaignMission.Current?.Location?.StringId != "prison")) return false;
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
            bool sentenceReleased = Mission.Current == null
                && PlayerDetentionBehavior.IsSentenceCompleted(d.DetentionOwnerId);
            if (d.CancellationPending || (!sentenceReleased
                && (!d.JailSceneEntered || CampaignMission.Current?.Location?.StringId != "prison"))) return false;
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
                        "Your sentence is complete. You have put on the old clothes and face covering; your belongings remain in safekeeping.")));
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
                // Entitlements persist when suspended; recognition still checks actual equipment.
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
            var host = StoryProbeMissionBehavior.Current;
            if (host?.FirstGuardDefeated == true)
                return LWNTextHelper.ResolveText("LWN_story_goal_guard_defeated", "You have fallen. Await the guards' next move.");
            if (host?.FirstGuardFightPending == true)
                return LWNTextHelper.ResolveText("LWN_story_goal_guard_fight", "Defeat the guards.");
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
