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
    /// 1. 静态查询（无状态，各处按需调）：CanAgentSeeTarget / GetObserversOf / CanPlayerSee / CanNpcSeePlayer
    /// 2. 事件订阅（tick 驱动，按注册的 tracked target 触发）：RegisterTrackedTarget → OnAgentStartObserving 等
    ///
    /// 性能边界：只对少量重要 Agent（玩家 + 随从等，预期 ≤5）做 tick 追踪。
    /// 大批 NPC 间偶发查询走静态 API，O(1) 按需调用。
    ///
    /// 替代三个旧实现：
    ///   BubbleSayMissionView 的 camera dot + 距离 → CanPlayerSee
    ///   StealManager.GetWitnesses → GetNpcsObservingPlayer / GetObserversOf
    ///   InteractionMissionView.ProcessAgentCandidate → CanPlayerSee 辅助
    /// </summary>
    public class NpcSightSystem : MissionLogic
    {
        // 单例：供 BubbleSayMissionView / ProcessAgentCandidate 等处查询缓存
        public static NpcSightSystem Instance { get; private set; }

        // ============================================================
        // 静态查询（任意 Agent → 任意 Agent）
        // ============================================================

        /// <summary>observer 能否看到 target（距离 + 高度 + FOV + RayCast）。</summary>
        public static bool CanAgentSeeTarget(Agent observer, Agent target,
            float radius = 15f, float fovDegrees = 120f)
        {
            if (observer == null || target == null) return false;
            if (!observer.IsActive() || !target.IsActive()) return false;
            //自己看自己不算（主要是为了 GetObserversOf 里过滤自己，避免后续逻辑误判）
            if (observer == target) return false;

            // 1. 距离
            float dist = observer.Position.Distance(target.Position);
            if (dist > radius) return false;

            // 2. 高度差
            if (MathF.Abs(observer.Position.z - target.Position.z) > 3.0f) return false;

            // 3. FOV 角度，角度值还是弧度？回答：角度
            float fovDotThreshold = MathF.Cos(MathF.DegToRad * (fovDegrees / 2f));
            Vec3 dirToTarget3D = target.Position - observer.Position;
            Vec2 dirToTarget2D = dirToTarget3D.AsVec2.Normalized();
            Vec2 lookDir2D = observer.LookDirection.AsVec2.Normalized();
            if (Vec2.DotProduct(lookDir2D, dirToTarget2D) < fovDotThreshold) return false;

            // 4. RayCast 遮挡
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

        /// <summary>玩家摄像机视野内（大范围、广角，无 RayCast 遮挡——摄像机没有"视线被挡"概念）。</summary>
        public static bool CanPlayerSee(Agent npc)
        {
            if (npc == null || !npc.IsActive()) return false;
            if (Mission.Current == null) return false;
            Agent player = Mission.Current.MainAgent;
            if (player == null) return false;

            // 距离
            float dist = player.Position.Distance(npc.Position);
            if (dist > 50f) return false;

            // 相机方向 dot product（复用 BubbleSayMissionView 的逻辑：点积 ≤0 说明在背后）
            MissionScreen ms = ScreenManager.TopScreen as MissionScreen;
            Camera cam = ms?.CombatCamera;
            if (cam == null) return false;
            Vec3 dirToTarget = npc.Position - cam.Position;
            if (Vec3.DotProduct(cam.Direction, dirToTarget) <= 0) return false;

            return true;
        }

        /// <summary>NPC 能否看到玩家（标准 FOV + RayCast，复用 StealManager 逻辑）。</summary>
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
                    if (CanPlayerSee(agent)) result.Add(agent);
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
        }

        public override void OnRemoveBehavior()
        {
            if (Instance == this) Instance = null;
            base.OnRemoveBehavior();
        }

        /// <summary>查询缓存：agent 当前是否在玩家视野内（由 tick 维护，~1s 延迟）。</summary>
        public bool IsPlayerSeeing(Agent agent)
        {
            if (agent == null) return false;
            foreach (var t in _tracked)
                if (t.Agent == Agent.Main) return t.PrevSeen.Contains(agent);
            return false;
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

            //1秒检查一次，性能考虑不宜过频
            _tickTimer += dt;
            if (_tickTimer < 1.0f) return;
            _tickTimer = 0f;

            foreach (var tracked in _tracked)
            {
                if (tracked.Agent == null || !tracked.Agent.IsActive()) continue;
                TickTrackedTarget(tracked);
            }
        }

        private void TickTrackedTarget(TrackedTarget tracked)
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
