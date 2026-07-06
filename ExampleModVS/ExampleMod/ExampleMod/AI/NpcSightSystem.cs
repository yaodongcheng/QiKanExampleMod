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
    /// 🆕 警戒值系统：所有 NPC 对玩家的警戒值（替代原版 AlarmedBehaviorGroup 仅守卫有效的限制）。
    /// </summary>
    public class NpcSightSystem : MissionLogic
    {
        // 单例：供 AgentHudMissionView / ProcessAgentCandidate 等处查询缓存
        public static NpcSightSystem Instance { get; private set; }

        // ============================================================
        // 🆕 警戒值系统
        // ============================================================

        /// <summary>每个 NPC 对玩家的警戒值（key = Agent.Index），静态共享，无需实例即可查询</summary>
        private static Dictionary<int, float> _alertValues = new Dictionary<int, float>();

        private const float IdentityAlertRate = 0.15f;       // 敌人生成速率
        private const float ActionSuspiciousRate = 0.15f;     // 蹲下生成速率
        private const float DecayRate = 0.15f;                // 看不到时每秒衰减
        private const float AlertPulseKnockout = 2.0f;        // 击晕脉冲
        private const float AlertPulseSteal = 2.0f;           // 偷窃脉冲
        private const float AlertPulseAttackAlly = 2.0f;      // 攻击友军脉冲

        // 🐛 调试字段
        private float _lastIdentityVal;
        private float _lastActionVal;
        private int _debugAlertTickCount;

        /// <summary>查询 NPC 对玩家的警戒值（不存在返回 0），静态方法，无需实例</summary>
        public static float GetAlertValue(Agent npc)
        {
            if (npc == null) return 0f;
            if (_alertValues.TryGetValue(npc.Index, out float val))
                return val;
            return 0f;
        }

        /// <summary>一次性脉冲：直接加警戒值（不走 dt），静态方法</summary>
        public static void AddAlertPulse(Agent npc, float amount)
        {
            if (npc == null || !npc.IsActive() || !npc.IsHuman) return;
            if (_alertValues.ContainsKey(npc.Index))
                _alertValues[npc.Index] += amount;
            else
                _alertValues[npc.Index] = amount;
        }

        /// <summary>获取所有有警戒值的 Agent（供调试）</summary>
        public static Dictionary<int, float> GetAllAlertValues()
        {
            return new Dictionary<int, float>(_alertValues);
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
            _alertValues.Clear();
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

            // 🆕 刷新玩家 tracked target 的 Agent 引用（防注册时 Agent.Main 尚未 spawn 导致引用过时）
            foreach (var t in _tracked)
            {
                if (t.Agent != Agent.Main && Agent.Main != null && Agent.Main.IsActive())
                {
                    // Agent.Main 引用已更新，替换旧的
                    t.Agent = Agent.Main;
                }
            }

            // 🆕 清理死 Agent 的警戒值条目
            CleanupDeadAlertEntries();

            foreach (var tracked in _tracked)
            {
                if (tracked.Agent == null || !tracked.Agent.IsActive()) continue;
                TickTrackedTarget(tracked, tickDt);  // 传入实际累积时间
            }
        }

        /// <summary>清理死亡/失效 Agent 的警戒值条目</summary>
        private void CleanupDeadAlertEntries()
        {
            if (_alertValues.Count == 0) return;
            List<int> deadIndices = null;
            foreach (var kv in _alertValues)
            {
                // Agent.Index 可能已被回收复用，用 Mission.Current.FindAgentWithIndex 验证
                bool isDead = false;
                if (Mission.Current != null)
                {
                    var agent = Mission.Current.FindAgentWithIndex(kv.Key);
                    isDead = (agent == null || !agent.IsActive());
                }
                if (isDead)
                {
                    if (deadIndices == null) deadIndices = new List<int>();
                    deadIndices.Add(kv.Key);
                }
            }
            if (deadIndices != null)
            {
                foreach (int idx in deadIndices)
                    _alertValues.Remove(idx);
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

                // 🆕 警戒值计算：仅对玩家追踪目标
                if (tracked.Agent == Agent.Main)
                {
                    UpdateAlertValue(agent, dt);
                }
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

        /// <summary>对单个 NPC 更新警戒值</summary>
        private void UpdateAlertValue(Agent npc, float dt)
        {
            if (npc == null || !npc.IsActive() || !npc.IsHuman) return;
            if (Agent.Main == null) return;

            bool canSeePlayer = CanAgentSeeTarget(npc, Agent.Main, 15f, 120f);

            // 获取当前值
            float currentVal = 0f;
            _alertValues.TryGetValue(npc.Index, out currentVal);

            if (canSeePlayer)
            {
                // NPC 能看到玩家 → 警戒值上升
                // 检查是否为敌对阵营（避开 v1.2.12 Team.IsEnemyOf 的 native NRE，用 Side 比较）
                float identityVal = 0f;
                try
                {
                    Team npcTeam = npc.Team;
                    if (npcTeam != null && Agent.Main?.Team is Team playerTeam)
                    {
                        if (npcTeam.Side != playerTeam.Side && npcTeam.Side != BattleSideEnum.None)
                            identityVal = IdentityAlertRate;
                    }
                }
                catch (NullReferenceException) { }

                // 玩家蹲下检测
                float actionVal = 0f;
                if (Agent.Main.CrouchMode)
                {
                    actionVal = ActionSuspiciousRate;
                }

                float delta = dt * (identityVal + actionVal);
                currentVal += delta;

                // 🐛 调试
                _lastIdentityVal = identityVal;
                _lastActionVal = actionVal;
            }
            else
            {
                // NPC 看不到玩家 → 警戒值衰减
                currentVal -= dt * DecayRate;
                _lastIdentityVal = 0f;
                _lastActionVal = 0f;
            }

            // Clamp 到 [0, 2.1]
            const float AlertMax = 2.1f;
            currentVal = MBMath.ClampFloat(currentVal, 0f, AlertMax);

            // 🐛 调试日志：每次 tick 对能看到玩家的 NPC 输出（含 crouch 状态）
            if (canSeePlayer)
            {
                _debugAlertTickCount++;
                float oldVal;
                _alertValues.TryGetValue(npc.Index, out oldVal);
                if (_debugAlertTickCount % 10 == 0 || MathF.Abs(currentVal - oldVal) > 0.02f)
                {
                    DebugLogger.Log($"[Alert] {npc.Name} | alert={oldVal:F3}→{currentVal:F3} | " +
                        $"canSee={canSeePlayer} identity={_lastIdentityVal:F2} action={_lastActionVal:F2} " +
                        $"crouch={Agent.Main?.CrouchMode} enemy={npc.Team?.Side}!={Agent.Main?.Team?.Side}");
                }
            }

            // 更新或清理（阈值降到 0.0001f，避免数值太小时反复删→重建）
            if (currentVal <= 0.0001f)
            {
                _alertValues.Remove(npc.Index);
            }
            else
            {
                _alertValues[npc.Index] = currentVal;
            }
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
