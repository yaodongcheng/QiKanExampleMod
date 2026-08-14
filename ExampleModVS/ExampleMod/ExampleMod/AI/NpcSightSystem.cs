using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 统一视线引擎：任意 Agent 对任意 Agent 的 FOV + RayCast 视线检测。
    ///
    /// 两层 API：
    /// 1. 静态查询（无状态，各处按需调）：CanAgentSeeTarget / GetObserversOf / IsPlayerSeeing / CanNpcSeePlayer
    /// 2. 事件订阅（tick 驱动，按注册的 tracked target 触发）：RegisterTrackedTarget → OnAgentStartObserving 等
    ///
    /// 性能边界：只对少量重要 Agent（玩家 + 随从等，预期 ≤5）做 tick 追踪。
    /// 大批 NPC 间偶发查询走静态 API，O(1) 按需调用。
    ///
    /// 🆕 警戒值已迁移至 AgentBrain（Phase 1）：每个 NPC 独立维护自己对玩家的警戒值。
    /// NpcSightSystem 回归纯感知工具角色——只回答"能不能看到"，不维护认知状态。
    /// </summary>
    public class NpcSightSystem : MissionLogic
    {
        // 单例：供 AgentHudMissionView / ProcessAgentCandidate 等处查询缓存
        public static NpcSightSystem Instance { get; private set; }

        // 🔴 2026-08-14 实机修复：本行为在 MySubModule.OnMissionBehaviorInitialize 中追加——
        // 引擎对追加行为只调 OnCreated()，不调 OnBehaviorInitialize（反编译 Mission.AddMissionBehavior
        // 实锤：AddMissionBehavior → OnCreated；OnBehaviorInitialize 循环在此之前已跑完）。
        // 静态 Instance 必须在构造函数赋值（与 AgentAIController 同模式），否则恒为 null，
        // 所有 `NpcSightSystem.Instance?.` 静默 no-op（实机：全场景脑读 Instance=null）。
        public NpcSightSystem()
        {
            Instance = this;
        }

        // ============================================================
        // 静态查询（任意 Agent → 任意 Agent）
        // ============================================================

        /// <summary>observer 能否看到 target（距离 + 高度 + FOV + RayCast）。
        /// 玩家 observer 直接委托给 IsPlayerSeeing（屏幕投影判断）。</summary>
        public static bool CanAgentSeeTarget(Agent observer, Agent target,
            float radius = 15f, float fovDegrees = 120f)
        {
            if (observer == null || target == null) return false;
            if (!observer.IsActive() || !target.IsActive()) return false;
            if (observer == target) return false;

            // 玩家：用屏幕投影判断（最符合玩家认知）
            if (observer == Agent.Main)
                return IsPlayerSeeing(target);

            // NPC observer：距离 + 高度 + FOV + RayCast
            float dist = observer.Position.Distance(target.Position);
            if (dist > radius) return false;

            if (MathF.Abs(observer.Position.z - target.Position.z) > 3.0f) return false;

            float fovDotThreshold = MathF.Cos(MathF.DegToRad * (fovDegrees / 2f));
            Vec3 dirToTarget3D = target.Position - observer.Position;
            Vec2 dirToTarget2D = dirToTarget3D.AsVec2.Normalized();
            Vec2 lookDir2D = observer.LookDirection.AsVec2.Normalized();
            if (Vec2.DotProduct(lookDir2D, dirToTarget2D) < fovDotThreshold) return false;

            return !IsOccluded(observer, target);
        }

        /// <summary>能看到 target 的所有活跃 Agent。</summary>
        public static List<Agent> GetObserversOf(Agent target,
            float radius = 15f, float fovDegrees = 120f)
        {
            var result = new List<Agent>();
            if (target == null || Mission.Current == null) return result;

            MBList<Agent> nearby = new MBList<Agent>();
            Mission.Current.GetNearbyAgents(target.Position.AsVec2, radius, nearby);

            foreach (var agent in nearby)
            {
                if (!AgentControlHelper.IsHumanOrChild(agent) || !agent.IsActive()) continue;
                if (agent == target) continue;
                if (CanAgentSeeTarget(agent, target, radius, fovDegrees))
                    result.Add(agent);
            }
            return result;
        }

        // ── 玩家快捷包装 ──

        /// <summary>NPC 能否看到玩家（标准 FOV + RayCast）。</summary>
        public static bool CanNpcSeePlayer(Agent npc)
        {
            if (npc == null || Mission.Current == null) return false;
            Agent player = Mission.Current.MainAgent;
            if (player == null) return false;
            return CanAgentSeeTarget(npc, player, 15f, 120f);
        }

        public static List<Agent> GetNpcsPlayerSees()
        {
            var result = new List<Agent>();
            if (Mission.Current == null) return result;
            foreach (var agent in Mission.Current.Agents)
            {
                if (AgentControlHelper.IsHumanOrChild(agent) && agent.IsActive() && agent != Mission.Current.MainAgent)
                    if (IsPlayerSeeing(agent)) result.Add(agent);
            }
            return result;
        }

        public static List<Agent> GetNpcsObservingPlayer()
        {
            if (Mission.Current == null) return new List<Agent>();
            return GetObserversOf(Mission.Current.MainAgent, 15f, 120f);
        }

        // ============================================================
        // 射线检测（复用 StealManager 里的 RayCastForClosestEntityOrTerrain）
        // ============================================================
        private static bool IsOccluded(Agent observer, Agent target)
        {
            return IsOccludedFrom(observer.GetEyeGlobalPosition(), target);
        }

        private static bool IsOccludedFrom(Vec3 eyePos, Agent target)
        {
            Vec3 targetChestPos = target.AgentVisuals != null
                ? target.AgentVisuals.GetGlobalFrame().origin + new Vec3(0, 0, 1.2f)
                : target.Position + new Vec3(0, 0, 1.5f);

            float distanceToTarget = eyePos.Distance(targetChestPos);

            float collisionDistance;
            Vec3 closestPoint;
            bool hasHitObstacle = V.RayCastForClosestEntityOrTerrain(
                eyePos, targetChestPos,
                out collisionDistance, out closestPoint,
                0.01f, BodyFlags.CommonCollisionExcludeFlags);

            if (hasHitObstacle && collisionDistance < distanceToTarget - 0.2f)
                return true; // 被遮挡

            return false;
        }

        // ── 玩家视角遮挡缓存（IsPlayerSeeing 用）──
        // 缓存字典即"兴趣集合"：查询冷 miss 时同步算一次插入，之后由 OnMissionTick 的
        // 0.1s 闸门统一维护（投影还在 → 重射；离开屏幕/死亡 → 驱逐）。
        // 键 = Agent.Index，mission 结束随 NpcSightSystem 移除清空。
        private class PlayerSightCacheEntry
        {
            public Agent Agent;          // 引用直接持有，tick 刷新时免按 Index 反查
            public float LastCheckTime;
            public bool Occluded;
        }
        private static readonly Dictionary<int, PlayerSightCacheEntry> _playerSightCache = new Dictionary<int, PlayerSightCacheEntry>();

        // 安全上限（非主节奏）：主节奏是 tick 的 0.1s 闸门。仅当 tick 停摆（如行为未注册）
        // 缓存超过 1s 未刷新时，查询侧同步重算兜底，退化为低频懒加载依然正确。
        private const float PlayerSightCacheSafetyCeiling = 1f;

        /// <summary>玩家视角起点：相机位置（第三人称越肩视角以相机为准），拿不到回退 MainAgent 眼位。</summary>
        private static Vec3 GetPlayerViewEyePos(Agent fallbackTarget, MissionScreen ms)
        {
            return ms.CombatCamera?.Position
                ?? Agent.Main?.GetEyeGlobalPosition()
                ?? fallbackTarget.Position;
        }

        /// <summary>玩家视角 → target 胸口是否被遮挡。命中缓存直接返回；冷 miss 或超安全上限时同步算并写入。</summary>
        private static bool IsOccludedFromPlayerView(Agent target, MissionScreen ms)
        {
            float now = Mission.Current?.CurrentTime ?? 0f;
            if (_playerSightCache.TryGetValue(target.Index, out PlayerSightCacheEntry entry)
                && now - entry.LastCheckTime < PlayerSightCacheSafetyCeiling)
            {
                return entry.Occluded;
            }

            bool occluded = IsOccludedFrom(GetPlayerViewEyePos(target, ms), target);

            if (entry == null)
            {
                entry = new PlayerSightCacheEntry { Agent = target };
                _playerSightCache[target.Index] = entry;
            }
            entry.LastCheckTime = now;
            entry.Occluded = occluded;
            return occluded;
        }

        /// <summary>tick 感知段：维护玩家视线遮挡缓存（0.1s 节奏，战斗模式也运行——感知层不冻结）。</summary>
        private void RefreshPlayerSightCache()
        {
            if (_playerSightCache.Count == 0) return;

            MissionScreen ms = ScreenManager.TopScreen as MissionScreen;
            if (ms == null) return;

            float now = Mission.Current?.CurrentTime ?? 0f;
            List<int> evictKeys = null;

            foreach (KeyValuePair<int, PlayerSightCacheEntry> kv in _playerSightCache)
            {
                Agent agent = kv.Value.Agent;
                if (agent == null || !agent.IsActive() || !IsProjectedOnScreen(agent, ms))
                {
                    // 死亡/离开屏幕：驱逐。下次进入视野走查询侧冷路径同步算
                    if (evictKeys == null) evictKeys = new List<int>();
                    evictKeys.Add(kv.Key);
                    continue;
                }

                kv.Value.Occluded = IsOccludedFrom(GetPlayerViewEyePos(agent, ms), agent);
                kv.Value.LastCheckTime = now;
            }

            if (evictKeys != null)
                foreach (int key in evictKeys) _playerSightCache.Remove(key);
        }

        // ============================================================
        // 重点目标追踪（tick 驱动 + 事件）
        // ============================================================

        private class TrackedTarget
        {
            public Agent Agent;
            public float ObserverRadius;
            public float ViewRadius;
            public HashSet<Agent> PrevObservers = new HashSet<Agent>();   // 上一帧：谁在看我
            public HashSet<Agent> PrevSeen = new HashSet<Agent>();        // 上一帧：我在看谁
        }

        private List<TrackedTarget> _tracked = new List<TrackedTarget>();
        private float _tickTimer;

        /// <summary>注册一个需要持续追踪的重点 Agent（如玩家、随从）。
        /// 🔴 2026-08-14：注册列表即「感知目标列表」——AgentBrain.UpdateAlertCognition 蹲姿感知
        /// 遍历它（玩家读 CrouchMode / NPC 读脑 CrouchPoseActive），与 TrackedTargets 同步维护。</summary>
        public void RegisterTrackedTarget(Agent target, float observerRadius, float viewRadius)
        {
            if (target == null) return;
            // 防重复注册
            foreach (var t in _tracked)
                if (t.Agent == target) return;
            _tracked.Add(new TrackedTarget
            {
                Agent = target,
                ObserverRadius = observerRadius,
                ViewRadius = viewRadius
            });
            TrackedTargets.Add(target);
            // 事件驱动低频日志：验证「感知目标列表」的注册情况（实机排查：随从没进列表 = 感知永远不触发）
            DebugLogger.Log($"[SightTrack] 注册追踪: {target.Name}(Idx={target.Index}) | 当前 tracked={TrackedTargets.Count}");
        }

        public void UnregisterTrackedTarget(Agent target)
        {
            _tracked.RemoveAll(t => t.Agent == target);
            TrackedTargets.Remove(target);
            if (target != null)
                DebugLogger.Log($"[SightTrack] 注销追踪: {target.Name}(Idx={target.Index}) | 当前 tracked={TrackedTargets.Count}");
        }

        /// <summary>当前被追踪的目标 Agent 列表（玩家自动注册 + 随从 OnAgentCreated 注册，预期 ≤5）。
        /// 感知侧消费：AgentBrain.UpdateAlertCognition 遍历本列表读各目标蹲姿，sight 职责统一归本类——
        /// 不搞「每操作一个缓存列表」。</summary>
        public List<Agent> TrackedTargets = new List<Agent>();

        // ── 事件 ──
        public event Action<Agent, Agent> OnAgentStartObserving;   // (observer, target)
        public event Action<Agent, Agent> OnAgentStopObserving;
        public event Action<Agent, Agent> OnTargetStartSeeing;     // (target, seenAgent)
        public event Action<Agent, Agent> OnTargetStopSeeing;

        // ============================================================
        // MissionLogic 生命周期
        // ============================================================

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            Instance = this;
            DebugLogger.Log($"[NpcSightSystem] OnBehaviorInitialize OK | Instance={Instance != null}");
        }

        public override void OnRemoveBehavior()
        {
            if (Instance == this) Instance = null;
            _playerSightCache.Clear();   // Agent.Index 跨 mission 会复用，缓存必须随 mission 销毁
            base.OnRemoveBehavior();
        }

        /// <summary>玩家是否实际看到 agent：①屏幕投影（WorldPointToScreenPoint，最符合玩家认知的 FOV）②相机→胸口遮挡射线（tick 0.1s 维护的缓存）。墙后/屋内 NPC 一律不可见。</summary>
        public static bool IsPlayerSeeing(Agent agent)
        {
            if (agent == null || !agent.IsActive()) return false;
            if (Mission.Current == null) return false;

            MissionScreen ms = ScreenManager.TopScreen as MissionScreen;
            if (ms == null) return false;

            // 第一道：屏幕投影（便宜，每帧可做）
            if (!IsProjectedOnScreen(agent, ms)) return false;

            // 第二道：遮挡射线（墙后/屋内的 NPC 投影也在屏幕内，但玩家实际看不到——
            // 血条/seeing 事件等所有调用方统一不许穿透，否则就是上帝视角情报泄露）
            return !IsOccludedFromPlayerView(agent, ms);
        }

        /// <summary>agent 头顶是否投影在玩家屏幕内（含 100px padding）。查询路径和 tick 驱逐判断共用。</summary>
        private static bool IsProjectedOnScreen(Agent agent, MissionScreen ms)
        {
            Vec3 agentPos = agent.Position;
            agentPos.z += agent.GetEyeGlobalHeight() + 0.1f;
            var screenPos = ms.SceneLayer.WorldPointToScreenPoint(agentPos);

            float screenWidth = Screen.RealScreenResolutionWidth;
            float screenHeight = Screen.RealScreenResolutionHeight;
            float pixelX = screenPos.x * screenWidth;
            float pixelY = screenPos.y * screenHeight;

            const float padding = 100f;
            return pixelX >= -padding && pixelX <= screenWidth + padding &&
                   pixelY >= -padding && pixelY <= screenHeight + padding;
        }

        // 标记是否已完成第一次 tick（用于延迟注册玩家等）
        private bool _firstTickDone;

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            // 0.1 秒闸门：全类统一节奏，感知段和认知段共用
            _tickTimer += dt;
            if (_tickTimer < 0.1f) return;
            float tickDt = _tickTimer;  // 保存实际累积时间
            _tickTimer = 0f;

            // ── 感知层（战斗模式也运行）：维护玩家视线遮挡缓存 ──
            RefreshPlayerSightCache();

            // ── 认知层：战斗模式下追踪/事件冻结（静态查询 IsPlayerSeeing 仍可用）──
            if (Settings.Instance.IsInteractionDisabled())
                return;

            //只有被注册过的Agent，才会被Npc视野跟踪，比如玩家，或者玩家自己的随从
            if (!_firstTickDone)
            {
                _firstTickDone = true;
                if (Agent.Main != null)
                    RegisterTrackedTarget(Agent.Main, 15f, 50f);

                // 🔴 2026-08-14 兜底补注册：AgentAIController.AfterStart 的随从补注册可能被吞——
                // 本行为若晚于它初始化，Instance 为 null → `?.` 静默 no-op（实机：随从 member=True
                // 却没进 TrackedTargets）。首帧统一扫玩家队伍成员补注册；
                // RegisterTrackedTarget 自带防重复，已注册的直接 return。
                if (Mission.Current != null)
                {
                    foreach (var agent in Mission.Current.Agents)
                    {
                        if (agent == Agent.Main || !AgentControlHelper.IsHumanOrChild(agent) || !agent.IsActive()) continue;
                        if (FriendlinessHelper.IsPlayerPartyMember(agent))
                            RegisterTrackedTarget(agent, 15f, 50f);
                    }
                }
            }

            // 🔴 刷新失效引用：注册时 Agent.Main 可能尚未 spawn，重进场景后引用过时。
            // 只替换「引用失效」的目标（原逻辑 `t.Agent != Agent.Main 就替换` 会把注册的随从
            // 全部误替换成玩家——随从注册后必踩，2026-08-14 修正）。随从引用失效靠 OnAgentDeleted 注销。
            foreach (var t in _tracked)
            {
                if (t.Agent == null || !t.Agent.IsActive())
                {
                    if (Agent.Main != null && Agent.Main.IsActive())
                    {
                        t.Agent = Agent.Main;
                        for (int i = 0; i < TrackedTargets.Count; i++)
                            if (TrackedTargets[i] == null || !TrackedTargets[i].IsActive())
                                TrackedTargets[i] = Agent.Main;
                    }
                }
            }

            // 🆕 清理死 Agent 的警戒值条目（已迁移至 AgentBrain，不再需要）
            // 注意：AgentBrain.Tick 内部自行处理死 Agent 的 _alertBreakdown 清理

            foreach (var tracked in _tracked)
            {
                if (tracked.Agent == null || !tracked.Agent.IsActive()) continue;
                TickTrackedTarget(tracked, tickDt);  // 传入实际累积时间
            }
        }

        private void TickTrackedTarget(TrackedTarget tracked, float dt)
        {
            MBList<Agent> nearby = new MBList<Agent>();
            float maxRadius = MathF.Max(tracked.ObserverRadius, tracked.ViewRadius);
            Mission.Current.GetNearbyAgents(tracked.Agent.Position.AsVec2, maxRadius, nearby);

            var curObservers = new HashSet<Agent>();  // 谁正在看 tracked target
            var curSeen = new HashSet<Agent>();        // tracked target 正在看谁

            foreach (var agent in nearby)
            {
                if (!AgentControlHelper.IsHumanOrChild(agent) || !agent.IsActive()) continue;
                if (agent == tracked.Agent) continue;

                if (CanAgentSeeTarget(agent, tracked.Agent, tracked.ObserverRadius, 120f))
                    curObservers.Add(agent);

                if (CanAgentSeeTarget(tracked.Agent, agent, tracked.ViewRadius, 140f))
                    curSeen.Add(agent);

                // 🆕 警戒值已迁移至 AgentBrain.UpdateAlertCognition。
                // NpcSightSystem 回归纯感知工具——只回答 CanNpcSeePlayer，不维护认知状态。
            }

            // Diff observers
            DiffSets(tracked.PrevObservers, curObservers,
                added => OnAgentStartObserving?.Invoke(added, tracked.Agent),
                removed => OnAgentStopObserving?.Invoke(removed, tracked.Agent));

            // Diff seen
            DiffSets(tracked.PrevSeen, curSeen,
                added => OnTargetStartSeeing?.Invoke(tracked.Agent, added),
                removed => OnTargetStopSeeing?.Invoke(tracked.Agent, removed));

            tracked.PrevObservers = curObservers;
            tracked.PrevSeen = curSeen;
        }

        private static void DiffSets(HashSet<Agent> prev, HashSet<Agent> cur,
            Action<Agent> onAdded, Action<Agent> onRemoved)
        {
            foreach (var a in cur)
                if (!prev.Contains(a)) onAdded(a);
            foreach (var a in prev)
                if (!cur.Contains(a)) onRemoved(a);
        }
    }
}
