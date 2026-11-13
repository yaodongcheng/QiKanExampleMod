using System;
using System.Collections.Generic;
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
        private float _nextBarrierAt;
        private bool _pendingBlockerDown;
        private readonly HashSet<string> _pendingQuestDone = new HashSet<string>();
        private bool _goalReached;
        private bool _forcedDefeatQueued;

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
                if (affectedAgent.Health > 0.5f) return;
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
                if (!_gateOpen)
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
            catch (Exception ex) { DebugLogger.Log($"[StoryStage] tick failed: {ex.Message}"); }

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
                if (Phase == StoryProbePhase.FirstAttempt) TickFirstAttempt();
                else if (Phase == StoryProbePhase.Second) TickSecondPhase(dt);
            }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] tick failed: {ex.Message}"); }
        }

        public override void OnRemoveBehavior()
        {
            base.OnRemoveBehavior();
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
            if (!StoryProbeState.EnterViaOption)
            {
                // 原版「在城里逛逛」永远不加载序章
                return;
            }
            // 🔴 先消费标记再判开关：开关关着时留着它，会让**下一次原版进城**误加载序章
            StoryProbeState.EnterViaOption = false;
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
            if (!IsCenterLocation(settlement))
            {
                DebugLogger.Log($"[StoryProbe] skipped: location={CampaignMission.Current?.Location?.StringId ?? "null"} (want center)");
                return;
            }

            if (Agent.Main == null)
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
                // 还没换装就被塞进场景（例如用命令强行进场）→ 当作第二次进场，先给换装
                DebugLogger.Log("[StoryProbe] phase=Jail on entry -> auto strip + disguise");
                StripPlayer();
                StoryProbeState.Phase = StoryProbePhase.Second;
                Phase = StoryProbePhase.Second;
            }

            Active = true;
            ResolveAnchors();

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
            PlayerAnchor = player?.Position ?? Vec3.Zero;
            if (player != null)
            {
                Vec3 look = player.LookDirection;
                Vec2 f = look.AsVec2;
                Forward = f.LengthSquared > 0.0001f ? f.Normalized() : new Vec2(0, 1);
            }

            UsedFallback = true;
            try
            {
                var guards = FindGuardSpawnEntities();
                if (guards != null && guards.Count > 0)
                {
                    var ordered = guards.OrderBy(g => g.GlobalPosition.Distance(PlayerAnchor)).ToList();
                    BlockerPos = ordered[0].GlobalPosition;

                    // 🔴 目标点必须是**门内**那个 sp_guard（§3.3 实测在守门人身后 18.8 m），
                    //    不能按"第 2 近"取 —— empire_town_g 里第 2 近的那个在门外 5.8 m，
                    //    会让"序章完成"提前在门外触发（实机验收 item 16 直接挂）。
                    //    判据 = 距守门人最远 **且** 沿"朝城门方向"的投影为正、距离 > 8 m。
                    Vec3 fwd3 = new Vec3(Forward.x, Forward.y, 0);
                    GameEntity best = null;
                    float bestScore = -1f;
                    foreach (var g in ordered.Skip(1))
                    {
                        Vec3 delta = g.GlobalPosition - BlockerPos;
                        float along = delta.x * fwd3.x + delta.y * fwd3.y;
                        float dist = delta.Length;
                        if (dist < 8f) continue;                 // 太近的一定不是门内点
                        if (along <= 0f) continue;               // 在守门人身后（背离城门）
                        if (dist > bestScore) { bestScore = dist; best = g; }
                    }
                    GoalPos = best != null ? best.GlobalPosition : BlockerPos + fwd3 * 10f;
                    UsedFallback = false;

                    // 🔴 站位坐标系的"前方"以**玩家→城门**为准，不以玩家视线为准：
                    //    视线可能背对城门（进场景时相机朝向不保证），那样四家会被摆到街对面去。
                    Vec2 toGate = (BlockerPos - PlayerAnchor).AsVec2;
                    if (toGate.LengthSquared > 0.25f) Forward = toGate.Normalized();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] anchor lookup failed: {ex.Message}");
            }

            if (UsedFallback)
            {
                // 兜底。⚠️ 目标点必须在**门内**，否则序章会在门外判完成。
                // 先找城门实体（prefab 名 european_castle_gate_outer_*）；找到了就以城门为界：
                // 守门人在门外 4 m、目标点在门内 4 m。找不到才退回"守门人身后 3 m"。
                Vec3 fwd = Forward.ToVec3();
                Vec3 gate = TryFindGatePosition();
                if (gate != PlayerAnchor)
                {
                    BlockerPos = gate - fwd * 4f;
                    GoalPos = gate + fwd * 4f;
                    DebugLogger.Log($"[StoryProbe] fallback with gate at ({gate.x:F1},{gate.y:F1})");
                }
                else
                {
                    BlockerPos = PlayerAnchor + fwd * 8f;
                    GoalPos = BlockerPos + fwd * 3f;
                    DebugLogger.Log("[StoryProbe] fallback without gate entity");
                }
            }

            // 高度统一贴到玩家脚下（城镇门口这一片是平的；Agent 生成后引擎会贴地）
            BlockerPos = new Vec3(BlockerPos.x, BlockerPos.y, PlayerAnchor.z);
            GoalPos = new Vec3(GoalPos.x, GoalPos.y, PlayerAnchor.z);
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

        private void SetupFirstAttempt()
        {
            DebugLogger.Log("[StoryProbe] setup first attempt (blocker only)");
            SpawnNpc("blocker", BlockerPos, civilian: false);

            // §3.2.1：城门上挂着你的小额通缉 —— 这条**必须真的挂上**，否则后面的"遮面"没有任何意义。
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
            if (player == null || blocker == null) return;

            // ① 硬闯必败（§一「硬闯必败」）：不打嘴仗、直接想绕过去 → 当场被按倒。
            //    没有这一条，玩家可以绕着守门人走进城，第一次进城的教学就整个没了。
            if (!_forcedDefeatQueued)
            {
                Vec3 fwd = new Vec3(Forward.x, Forward.y, 0);
                Vec3 delta = player.Position - BlockerPos;
                float along = delta.x * fwd.x + delta.y * fwd.y;
                if (along > 1.0f || player.Position.Distance(GoalPos) < 8f)
                {
                    DebugLogger.Log($"[StoryProbe] first attempt: player tried to pass (along={along:F1}) -> forced defeat");
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveText("LWN_story_first_caught", "He grabs you by the collar.")));
                    PlayForcedDefeat();
                    return;
                }
            }

            // ② 走近守门人 → 自动开口（每次离开 8 m 再回来都会重新触发，不是一次性的）
            float d = player.Position.Distance(blocker.Position);
            if (d <= AutoTalkRadius && _blockerAutoTalkArmed)
            {
                _blockerAutoTalkArmed = false;
                StartStoryConversation(blocker, "blocker_first");
            }
            else if (d > AutoTalkRearmRadius && !_blockerAutoTalkArmed)
            {
                _blockerAutoTalkArmed = true;
            }
        }

        /// <summary>第一次进场的强制失败：不掷骰，直接判负 → 倒地 → 收场。</summary>
        public void PlayForcedDefeat()
        {
            if (_forcedDefeatQueued) return;
            _forcedDefeatQueued = true;

            var player = Agent.Main;
            var blocker = GetAgent("blocker");
            DebugLogger.Log("[StoryProbe] forced defeat sequence start");

            Schedule(0.9f, () =>
            {
                try
                {
                    if (blocker != null && player != null && AgentControlHelper.SafeIsActive(blocker) && AgentControlHelper.SafeIsActive(player))
                    {
                        KnockoutFlow.PlayStrikeAnim(blocker, player);
                        AgentControlHelper.SetPose(player, "act_death_fall_front");
                        // 剧情旁白，不是调试信息 → 不受 ShowDebugMessages 门控（§12 第一次必败要"看得到"）
                        InformationManager.DisplayMessage(new InformationMessage(
                            LWNTextHelper.ResolveText("LWN_story_narr_ko", "Everything goes dark.")));
                    }
                }
                catch (Exception ex) { DebugLogger.Log($"[StoryProbe] forced defeat anim failed: {ex.Message}"); }
            });

            Schedule(StoryProbeTuning.Current.Intro.ForcedDefeatHoldSec, () =>
            {
                StoryProbeState.Data.TutorialLost = true;
                StoryProbeState.Phase = StoryProbePhase.Jail;
                DebugLogger.Log("[StoryProbe] forced defeat done -> end mission (jail inquiry comes from campaign layer)");
                try { Mission.Current?.EndMission(); }
                catch (Exception ex) { DebugLogger.Log($"[StoryProbe] EndMission failed: {ex.Message}"); StoryProbeBehavior.RequestJailInquiry(); }
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // 第二次进场：四家 + 6 起事件 + 随从 + 目标点
        // ═══════════════════════════════════════════════════════════════

        private void SetupSecondPhase()
        {
            DebugLogger.Log("[StoryProbe] setup second phase (full prologue)");

            // 🔴 演出层（2026-11-13）：台本 StoryStage.json 驱动「布景 → 摆人 → 玩家出场」。
            //    起不来（文件缺/解析失败）⇒ 退回旧的硬编码偏移，保证不失能。
            _stage = new StoryStageRunner(this);
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

            // 🔴 被认出来（§5.2）：通缉状态 + 没有遮面/冒充 → 他不上来问话，直接动手。
            //    有遮面（Disguised）→ 还能站在门口，只是要过一次魅力对拼（在对话里）。
            if (!_gateOpen && player != null && !_recognized && IsPlayerWanted()
                && !StoryProbeState.Data.Disguised && !StoryProbeState.Data.NobleDisguise)
            {
                var b = GetAgent("blocker");
                if (b != null && player.Position.Distance(b.Position) < StoryProbeTuning.Current.Gate.RecognizeDistanceMeters)
                {
                    _recognized = true;
                    DebugLogger.Log("[StoryProbe] wanted + no disguise -> blocker recognizes you and draws");
                    InformationManager.DisplayMessage(new InformationMessage(
                        LWNTextHelper.ResolveText("LWN_story_recognized", "He looks at your face, then at the paper on the gate. Then he draws.")));
                    SayOnAgent(b, "LWN_story_blocker_recognize", "That's him. On the gate.", SpeechPriority.Warning);
                    try { CombatManager.StartFight(player, b, Peace: true); } catch { }
                }
            }

            // 🔴「手上的事」总闸（2026-11-13，对齐需求 §5.3/§5.4）：
            //    潜行（真蹲着）、绑走、扛走、扒掉他的家伙 —— 这四种不是对话选项，是在世界里的动作。
            if (!_gateOpen && player != null) CheckHandsOnGate(player);

            // 目标点：**必须先过门**（R1）。门没开就贴近目标 = 脚本硬拦，弹回门外。
            if (!_goalReached && player != null)
            {
                EnforceGateBarrier(player);

                if (_gateOpen && player.Position.Distance(GoalPos) < GoalRadius)
                {
                    _goalReached = true;
                    CompletePrologue();
                }
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
            var b = GetAgent("blocker");
            if (b == null || player == null) return false;
            if (player.Position.Distance(b.Position) > 25f) return false;

            Vec3 to = player.Position - b.Position;
            to.z = 0f;
            float len = (float)Math.Sqrt(to.x * to.x + to.y * to.y);
            if (len < 0.01f) return true;
            Vec3 look = b.LookDirection;
            look.z = 0f;
            float lookLen = (float)Math.Sqrt(look.x * look.x + look.y * look.y);
            if (lookLen < 0.01f) return true;
            float dot = (to.x * look.x + to.y * look.y) / (len * lookLen);
            return dot > 0.2f;
        }

        /// <summary>
        /// 「手上的事」——不需要跟守门人说话的四条路（对齐需求 §5.3 / §5.4）。
        ///
        ///   ① **潜行**：玩家真的蹲着（`CrouchMode`），且在他背后 8 m 内 → 贴过去（无检定，位置+姿态判定）。
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

                // ① 潜行：真蹲着 + 在他背后 8 m 内（朝向点积 < 0：你在他的背侧）
                if (!player.CrouchMode) return;
                float d = player.Position.Distance(blocker.Position);
                if (d > 8f) return;
                Vec3 toPlayer = player.Position - blocker.Position;
                toPlayer.z = 0f;
                float len = (float)Math.Sqrt(toPlayer.x * toPlayer.x + toPlayer.y * toPlayer.y);
                if (len < 0.01f) return;
                Vec3 look = blocker.LookDirection;
                look.z = 0f;
                float dot = (toPlayer.x * look.x + toPlayer.y * look.y) / (len * Math.Max(0.01f, (float)Math.Sqrt(look.x * look.x + look.y * look.y)));
                if (dot > 0.2f) return;     // 他正看着你 → 不算潜行

                DebugLogger.Log($"[StoryProbe] sneak past: crouch=1 dist={d:F1} dot={dot:F2}");
                OpenGate("guile", "LWN_story_gate_sneaked", "You slip past while his back is turned.");
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
        /// 规则：越过守门人 1 m 或离目标点 &lt; 8 m ⇒ 一秒钟一次把玩家弹回门外 2.5 m + 守门人喊一句；
        /// **例外**：人在 2 m 以上（钩索把人拉起来了）⇒ 算翻过去了，直接开门放行（技巧路）。
        /// </summary>
        private void EnforceGateBarrier(Agent player)
        {
            if (_gateOpen || player == null) return;

            Vec3 fwd = new Vec3(Forward.x, Forward.y, 0);
            Vec3 delta = player.Position - BlockerPos;
            float along = delta.x * fwd.x + delta.y * fwd.y;
            var gt = StoryProbeTuning.Current.Gate;
            bool past = along > gt.PastMeters || player.Position.Distance(GoalPos) < gt.GoalRadiusMeters;
            if (!past) return;

            // 高处（钩索拉起来）⇒ **要看他有没有看着你**（2026-11-13 用户裁定：有钩索不能等于无敌）
            //    · 他没看着你 → 翻过去了，门算开
            //    · 他正看着你 → 他喊人，门不开（落地后照样被弹回门外），还记一条隐患
            if (player.Position.z - BlockerPos.z > gt.ClimbHeightMeters)
            {
                if (IsBlockerWatching(player))
                {
                    if (!_climbSeen)
                    {
                        _climbSeen = true;
                        StoryProbeState.AddHazard("hazard_climb_seen");
                        var b = GetAgent("blocker");
                        SayOnAgent(b, "LWN_story_climb_seen", "Get down! Get down from there!", SpeechPriority.Warning);
                        InformationManager.DisplayMessage(new InformationMessage(
                            LWNTextHelper.ResolveText("LWN_story_climb_seen_narr",
                                "He saw you go over. By the time you land, the gate is watching for you.")));
                        DebugLogger.Log("[StoryProbe] climb SEEN -> gate stays shut");
                    }
                    return;
                }
                // 🔴 2026-11-13 用户追问：钩索不能是"拿到就无敌"，它必须跟**技巧**挂钩。
                //    做法 = 翻墙这条也要掷一次技巧（对拼守门人的 30），一次腾空只掷一次。
                //    成功 → 勾住，翻过去；失败 → 钩子滑了，他没被勾住，落地照样被弹回门外（记隐患）。
                if (!_climbRolled)
                {
                    _climbRolled = true;
                    var roll = StoryProbeDefs.RollCheck("blocker", StoryAbility.Guile);
                    InformationManager.DisplayMessage(new InformationMessage(roll.PlayerMessage()));
                    if (!roll.Success)
                    {
                        StoryProbeState.AddHazard("hazard_climb_missed");
                        SayOnAgent(GetAgent("blocker"), "LWN_story_climb_missed", "What was that?", SpeechPriority.Chat);
                        DebugLogger.Log("[StoryProbe] climb roll FAILED -> gate stays shut");
                        return;
                    }
                }
                OpenGate("guile", "LWN_story_gate_climbed", "You came over the wall.");
                return;
            }
            else
            {
                // 回到地面 = 下一次腾空重新掷（一次尝试一次骰）
                _climbRolled = false;
            }

            if (_time < _nextBarrierAt) return;
            _nextBarrierAt = _time + gt.PushBackCooldownSec;
            DebugLogger.Log($"[StoryProbe] barrier: pushed back (along={along:F1} distToGoal={player.Position.Distance(GoalPos):F1}) gateOpen=False");

            try
            {
                player.TeleportToPosition(BlockerPos - fwd * 2.5f);
            }
            catch { }

            var blocker = GetAgent("blocker");
            if (blocker != null)
            {
                try { AgentControlHelper.FaceToActor(blocker, player); } catch { }
                SayOnAgent(blocker, "LWN_story_blocker_bar", "Papers.", SpeechPriority.Warning);
            }
            InformationManager.DisplayMessage(new InformationMessage(
                LWNTextHelper.ResolveText("LWN_story_gate_blocked", "He pushes you back.")));
        }

        /// <summary>序章完成：钱与没收的装备归还，目标点记完成。</summary>
        private void CompletePrologue()
        {
            try
            {
                DebugLogger.Log($"[StoryProbe] PROLOGUE COMPLETE (approach={StoryProbeState.Data.ChosenApproach}) -> restoring belongings");
                RestorePlayerBelongings();
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
                DebugLogger.Log($"[StoryProbe] CompletePrologue failed: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 生成 / 取人
        // ═══════════════════════════════════════════════════════════════

        private Agent SpawnNpc(string npcId, Vec3 pos, bool civilian)
        {
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
                    DebugLogger.Log($"[StoryProbe] spawned {npcId} hero={hero.StringId} agent={agent.Index} pos=({pos.x:F1},{pos.y:F1})");
                }
                return agent;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] spawn '{npcId}' threw: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// **演出层专用入口**（2026-11-13 立）：把人摆到位 —— 没生成就生成，已生成就瞬移。
        /// 与 <see cref="SpawnNpc"/> 的分工：这里额外设朝向并**按住不动**（演出期间不允许他自己走）。
        /// </summary>
        public bool SpawnOrPlaceActor(string npcId, Vec3 pos, float facingDegrees, bool civilian)
        {
            var agent = SpawnNpc(npcId, pos, civilian);
            if (agent == null) return false;

            try { agent.LookDirectionAsAngle = facingDegrees * (float)Math.PI / 180f; }
            catch (Exception ex) { DebugLogger.Log($"[StoryProbe] facing '{npcId}' failed: {ex.Message}"); }

            try { AgentControlHelper.HoldInPlace(agent); } catch { }
            return true;
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
            _schedule.Add((_time + Math.Max(0f, delaySeconds), action));
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

            var blocker = GetAgent("blocker");
            if (blocker != null)
            {
                try
                {
                    Vec3 side = new Vec3(Forward.y, -Forward.x, 0) * 3.2f;
                    blocker.TeleportToPosition(BlockerPos + side);
                    AgentControlHelper.FaceToActor(blocker, Agent.Main);
                }
                catch { }
            }
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
        public static void StripPlayer()
        {
            var hero = Hero.MainHero;
            if (hero == null) return;
            var d = StoryProbeState.Data;

            try
            {
                if (!d.Stripped)
                {
                    d.GoldBeforeStrip = hero.Gold;
                    AgentControlHelper.SetGold(hero, 0, notify: false);

                    SaveEquipment(hero.BattleEquipment, d.SavedBattleEquip);
                    SaveEquipment(hero.CivilianEquipment, d.SavedCivilianEquip);
                    d.Stripped = true;
                }

                ClearEquipment(hero.BattleEquipment);
                ClearEquipment(hero.CivilianEquipment);

                var clothes = StoryProbeDefs.FindItem("hemp_tunic");
                var mask = StoryProbeDefs.FindItem("head_wrapped");
                if (clothes != null)
                {
                    hero.BattleEquipment[EquipmentIndex.Body] = new EquipmentElement(clothes);
                    hero.CivilianEquipment[EquipmentIndex.Body] = new EquipmentElement(clothes);
                }
                if (mask != null)
                {
                    hero.BattleEquipment[EquipmentIndex.Head] = new EquipmentElement(mask);
                    hero.CivilianEquipment[EquipmentIndex.Head] = new EquipmentElement(mask);
                }
                else
                {
                    DebugLogger.Log("[StoryProbe] mask item not found; disguise is flag-only");
                }

                d.Disguised = true;
                d.JailDone = true;
                d.Phase = (int)StoryProbePhase.Second;

                InformationManager.DisplayMessage(new InformationMessage(
                    LWNTextHelper.ResolveText("LWN_story_jail_done",
                        "You have no weapon, and your purse is empty. The cloth over your face is all you have.")));
                DebugLogger.Log($"[StoryProbe] stripped: goldBefore={d.GoldBeforeStrip} battleSlots={d.SavedBattleEquip.Count}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] StripPlayer failed: {ex.Message}");
            }
        }

        /// <summary>序章完成：把没收的东西与钱还回去（不重复发）。</summary>
        public static void RestorePlayerBelongings()
        {
            var hero = Hero.MainHero;
            if (hero == null) return;
            var d = StoryProbeState.Data;
            if (!d.Stripped) return;

            try
            {
                RestoreEquipment(hero.BattleEquipment, d.SavedBattleEquip);
                RestoreEquipment(hero.CivilianEquipment, d.SavedCivilianEquip);
                // 🔴 归还 = **加回**没收的那份，不是覆盖：序章里赚的（零活 20 / 收账 120）不能凭空消失。
                int restored = hero.Gold + d.GoldBeforeStrip;
                AgentControlHelper.SetGold(hero, restored, notify: false);
                d.Stripped = false;
                DebugLogger.Log($"[StoryProbe] belongings restored: +{d.GoldBeforeStrip} -> {restored}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] RestorePlayerBelongings failed: {ex.Message}");
            }
        }

        private static void SaveEquipment(Equipment equipment, List<string> into)
        {
            into.Clear();
            if (equipment == null) return;
            for (int i = 0; i < 12; i++)
            {
                var el = equipment[(EquipmentIndex)i];
                into.Add(el.IsEmpty || el.Item == null ? "" : el.Item.StringId);
            }
        }

        private static void ClearEquipment(Equipment equipment)
        {
            if (equipment == null) return;
            for (int i = 0; i < 12; i++)
                equipment[(EquipmentIndex)i] = EquipmentElement.Invalid;
        }

        private static void RestoreEquipment(Equipment equipment, List<string> from)
        {
            if (equipment == null || from == null) return;
            for (int i = 0; i < 12 && i < from.Count; i++)
            {
                if (string.IsNullOrEmpty(from[i])) continue;
                var item = StoryProbeDefs.FindItem(from[i]);
                if (item != null) equipment[(EquipmentIndex)i] = new EquipmentElement(item);
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

        public string DescribeWhere()
        {
            if (!Active) return "story probe not active in this mission";
            return $"anchor=({PlayerAnchor.x:F1},{PlayerAnchor.y:F1},{PlayerAnchor.z:F1}) " +
                   $"blocker=({BlockerPos.x:F1},{BlockerPos.y:F1},{BlockerPos.z:F1}) " +
                   $"goal=({GoalPos.x:F1},{GoalPos.y:F1},{GoalPos.z:F1}) " +
                   $"dist={PlayerAnchor.Distance(BlockerPos):F1}/{BlockerPos.Distance(GoalPos):F1} " +
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
