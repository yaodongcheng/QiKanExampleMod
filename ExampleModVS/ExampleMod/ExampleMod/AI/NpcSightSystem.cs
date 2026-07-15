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
                if (!agent.IsHuman || !agent.IsActive()) continue;
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
                if (agent.IsHuman && agent.IsActive() && agent != Mission.Current.MainAgent)
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
            Vec3 eyePos = observer.GetEyeGlobalPosition();
            Vec3 targetChestPos = target.AgentVisuals != null
                ? target.AgentVisuals.GetGlobalFrame().origin + new Vec3(0, 0, 1.2f)
                : target.Position + new Vec3(0, 0, 1.5f);

            float distanceToTarget = eyePos.Distance(targetChestPos);

            float collisionDistance;
            Vec3 closestPoint;
#if !MB2_V1212
            WeakGameEntity weakEntity;
            bool hasHitObstacle = Mission.Current.Scene.RayCastForClosestEntityOrTerrain(
                eyePos, targetChestPos,
                out collisionDistance, out closestPoint, out weakEntity,
                0.01f, BodyFlags.CommonCollisionExcludeFlags);
#else
            GameEntity collidedEntity;
            bool hasHitObstacle = Mission.Current.Scene.RayCastForClosestEntityOrTerrain(
                eyePos, targetChestPos,
                out collisionDistance, out closestPoint, out collidedEntity,
                0.01f, BodyFlags.CommonCollisionExcludeFlags);
#endif

            if (hasHitObstacle && collisionDistance < distanceToTarget - 0.2f)
                return true; // 被遮挡

            return false;
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

        /// <summary>注册一个需要持续追踪的重点 Agent（如玩家、随从）。</summary>
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
        }

        public void UnregisterTrackedTarget(Agent target)
        {
            _tracked.RemoveAll(t => t.Agent == target);
        }

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
            base.OnRemoveBehavior();
        }

        /// <summary>agent 当前是否在玩家屏幕内（WorldPointToScreenPoint 投影判断，最符合玩家认知的 FOV）</summary>
        public static bool IsPlayerSeeing(Agent agent)
        {
            if (agent == null || !agent.IsActive()) return false;
            if (Mission.Current == null) return false;

            MissionScreen ms = ScreenManager.TopScreen as MissionScreen;
            if (ms == null) return false;

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

            // 延迟注册：Agent.Main 在 OnMissionBehaviorInitialize 时尚未 spawn，
            // 第一次 tick 时补注册玩家作为默认追踪目标。
            if (!_firstTickDone)
            {
                _firstTickDone = true;
                if (_tracked.Count == 0 && Agent.Main != null)
                {
                    RegisterTrackedTarget(Agent.Main, 15f, 50f);
                }
            }

            //0.1秒检查一次（警戒值需要高频响应）
            _tickTimer += dt;
            if (_tickTimer < 0.1f) return;
            float tickDt = _tickTimer;  // 保存实际累积时间
            _tickTimer = 0f;

            // 战场感知关闭列表（Settings.Instance.DisabledSightMissionModes）中的模式
            // 不追踪观察者/视野事件（静态查询 IsPlayerSeeing 仍可用）
            if (Settings.Instance.IsSightDisabled())
                return;

            // 🆕 刷新玩家 tracked target 的 Agent 引用（防注册时 Agent.Main 尚未 spawn 导致引用过时）
            foreach (var t in _tracked)
            {
                if (t.Agent != Agent.Main && Agent.Main != null && Agent.Main.IsActive())
                {
                    // Agent.Main 引用已更新，替换旧的
                    t.Agent = Agent.Main;
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
                if (!agent.IsHuman || !agent.IsActive()) continue;
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
