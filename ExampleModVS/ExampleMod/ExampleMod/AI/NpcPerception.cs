using System;
using System.Collections.Generic;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// **通用感知层：声音 + 视野注视**（2026-11-16 用户裁定）。
    ///
    /// 为什么要它：以前"玩家靠近就把身体转过去"是按半径写死的 —— 那样玩家根本没法绕到背后偷东西。
    /// 现在的规则：
    ///   ① **身体只朝"声音"转** —— 谁发声就把声源坐标丢进 <see cref="Hear"/>，听觉半径内的 NPC 扭头看过去。
    ///      声音有响度（半径）：玩家蹲着不出声；普通人只回应身后2米内的大跑步声，敌对者才按走路6米／跑步15米听觉处理。
    ///      所以玩家能不能被回头发现，是由**自己怎么走**决定的，不是被距离阈值出卖。
    ///   ② **头只朝"视野扇形里的东西"转** —— 目标进扇形才更新眼部兴趣点，出扇形就释放。
    ///      日常感知不使用SetLookAgent；实机在持续对象注视下仍跟转身体，站位方向合同未能阻止。
    ///
    /// 边界：
    ///   · 这是**横切层**，不进脑的行为队列 —— 不产生任何排队/切换日志，也不和动作抢控制权；
    ///   · 战斗 / 被捆 / 倒地 / 演出中的人不参与（演出有台本，不能被脚步声打断）；
    ///   · 正在走路/转身的人不写身体或日常兴趣点，服从当前动作控制。
    ///
    /// 调用方：<see cref="AgentAIController.OnMissionTick"/> 每帧驱动；<see cref="Hear"/> 目前只有玩家脚步声，
    /// 以后任何会发声的东西（开门、打翻东西、打斗）都往这里丢一条即可。
    /// </summary>
    public static class NpcPerception
    {
        private struct Noise
        {
            public Vec3 Pos;
            public float Radius;
            public float At;
            public Agent Source;
            public bool Footstep;
            public bool Loud;
        }

        private const int MaxNoises = 16;
        private static readonly List<Noise> _noises = new List<Noise>(MaxNoises);

        /// <summary>每个 NPC 已经"处理过"的最新声音时刻 + 上次回头时刻（按 agent 索引存，Agent 销毁后自然失效）。</summary>
        private static readonly Dictionary<int, float> _heardAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> _turnedAt = new Dictionary<int, float>();
        private static readonly Dictionary<Agent, Agent> _lookingAt = new Dictionary<Agent, Agent>();
        private static readonly Dictionary<int, Vec2> _requestedBodies = new Dictionary<int, Vec2>();
        private static readonly Dictionary<int, IAtomicAction> _bodyActions = new Dictionary<int, IAtomicAction>();
        private static readonly Dictionary<int, string> _bodyReasons = new Dictionary<int, string>();
        private static readonly Dictionary<int, Vec2> _heardDirections = new Dictionary<int, Vec2>();
        private static readonly Dictionary<int, Noise> _heardNoises = new Dictionary<int, Noise>();
        private static Mission _mission;

        private static float _footstepAt;

        /// <summary>丢一条声音进来（世界坐标 + 传播半径，米）。</summary>
        public static void Hear(Vec3 position, float radius)
        {
            if (Mission.Current == null || radius <= 0.01f || float.IsNaN(radius) || float.IsInfinity(radius)) return;
            if (!ReferenceEquals(_mission, Mission.Current)) { _mission = Mission.Current; Reset(); }
            if (_noises.Count >= MaxNoises) _noises.RemoveAt(0);
            _noises.Add(new Noise { Pos = position, Radius = radius, At = Now });
        }

        public static void Reset()
        {
            _noises.Clear();
            _heardAt.Clear();
            _turnedAt.Clear();
            _lookingAt.Clear();
            _requestedBodies.Clear();
            _bodyActions.Clear();
            _bodyReasons.Clear();
            _heardDirections.Clear();
            _heardNoises.Clear();
            _footstepAt = 0f;
        }

        private static float Now
        {
            get
            {
                try { return Mission.Current?.CurrentTime ?? 0f; }
                catch { return 0f; }
            }
        }

        public static void Tick(float dt, IEnumerable<AgentBrain> brains)
        {
            if (brains == null || Mission.Current == null) return;
            // 换场景：所有缓存（声音、听过谁、转过谁、正在注视谁）一律作废，别把上个场景的状态带过来。
            if (!ReferenceEquals(_mission, Mission.Current)) { _mission = Mission.Current; Reset(); }
            // 战斗场景里脑整体不跑，感知也不该抢身体（原版 AI 在打）。
            if (Settings.Instance.IsInteractionDisabled())
            {
                foreach (var owner in new List<Agent>(_lookingAt.Keys)) ReleaseGaze(owner);
                return;
            }
            var cfg = StoryProbeTuning.Current?.Noise;
            if (cfg == null) return;

            float now = Now;
            if (_noises.Count > 0 && now - _noises[_noises.Count - 1].At > cfg.MaxAgeSec * 2f)
                _noises.Clear();   // 老声音不再需要

            EmitPlayerFootsteps(now, cfg);

            var player = Agent.Main;
            foreach (var brain in brains)
            {
                var agent = brain?.Owner;
                if (agent == null) continue;
                if (!AgentControlHelper.SafeIsActive(agent) || agent.Health <= 0f) { ReleaseGaze(agent); continue; }
                if (agent == player) continue;
                // 只对我们接管的人（有岗位点）写身体；原版 NPC 由它们的导航器每帧写朝向，插一脚会打架。
                if (!brain.HasDutyPoint) { ReleaseGaze(agent); continue; }
                if (brain.IsPerforming || brain.IsStoryIntercepting || brain.IsInCombat || brain.IsBound || brain.IsStatusHeld || brain.IsStunned
                    || agent.GetScriptedFlags().HasFlag(Agent.AIScriptedFrameFlags.InConversation))
                {
                    // 控制权已交给其他系统，旧注视记录作废；归还后重新取得自己的注视。
                    ReleaseGaze(agent);
                    _requestedBodies.Remove(agent.Index);
                    _bodyActions.Remove(agent.Index);
                    _bodyReasons.Remove(agent.Index);
                    _turnedAt.Remove(agent.Index);
                    _heardDirections.Remove(agent.Index);
                    _heardNoises.Remove(agent.Index);
                    continue;
                }

                TickAgent(brain, agent, player, now, cfg);
            }
        }

        /// <summary>玩家脚步：蹲着不出声；走路小声；跑步大声。想静步就蹲着走。</summary>
        private static void EmitPlayerFootsteps(float now, NoiseTuning cfg)
        {
            var player = Agent.Main;
            if (player == null || !AgentControlHelper.SafeIsActive(player)) return;
            if (now < _footstepAt) return;
            _footstepAt = now + Math.Max(0.1f, cfg.PublishIntervalSec);

            if (cfg.CrouchSilent && player.CrouchMode) return;

            float speed;
            try { speed = player.Velocity.Length; }
            catch { return; }
            if (speed < 0.3f) return;   // 站着不动没有脚步声

            float radius = speed > cfg.RunSpeedMps ? cfg.RunRadiusMeters : cfg.WalkRadiusMeters;
            if (_noises.Count >= MaxNoises) _noises.RemoveAt(0);
            _noises.Add(new Noise { Pos = player.Position, Radius = radius, At = now,
                Source = player, Footstep = true, Loud = speed > cfg.RunSpeedMps });
        }

        private static void TickAgent(AgentBrain brain, Agent agent, Agent player, float now, NoiseTuning cfg)
        {
            int idx = agent.Index;
            if (!_bodyActions.TryGetValue(idx, out var previousAction) || !ReferenceEquals(previousAction, brain.CurrentAction))
            {
                // 方向缓存只属于这一次动作，不能把上一待机的“已写过”交给新的动作。
                _requestedBodies.Remove(idx);
                _bodyReasons.Remove(idx);
                _bodyActions[idx] = brain.CurrentAction;
            }

            // ① 声音：只听最新一条、且只在响度半径内；处理过就不再重复转。
            Noise? latest = null;
            for (int i = _noises.Count - 1; i >= 0; i--)
            {
                var n = _noises[i];
                if (now - n.At > cfg.MaxAgeSec) continue;
                if (agent.Position.Distance(n.Pos) > n.Radius) continue;
                // 普通人不关心远处的脚步。只有身后两米内的重脚步才值得回头；
                // 通用声源（开门、撞物、打斗）仍按自己的传播半径处理。
                bool hostile = n.Source?.Team != null && agent.Team != null && agent.Team.IsEnemyOf(n.Source.Team);
                if (n.Footstep && !hostile && (!n.Loud
                    || agent.Position.Distance(n.Pos) > cfg.OrdinaryFootstepRadiusMeters
                    || NpcSightSystem.IsTargetInViewCone(agent, n.Source, useBodyFacing: true))) continue;
                latest = n;
                break;
            }

            bool moving = brain.IsMovingOrTurning;
            if (moving) _requestedBodies.Remove(idx); // 移动合同可以改朝向，停下后必须重新取得身体。

            if (latest.HasValue)
            {
                var n = latest.Value;
                bool heardBefore = _heardAt.TryGetValue(idx, out float handled);
                if ((!heardBefore || n.At > handled) && !moving)
                {
                    _heardAt[idx] = n.At;
                    Vec2 dir = (n.Pos - agent.Position).AsVec2;
                    if (dir.LengthSquared > 1e-4f)
                    {
                        dir.Normalize();
                        _heardDirections[idx] = dir;
                        _heardNoises[idx] = n;
                        RequestBody(agent, dir, n.Pos, "noise");
                    }
                    _turnedAt[idx] = now;
                }
            }
            else if (!moving && _turnedAt.TryGetValue(idx, out float turned) && now - turned > cfg.TurnHoldSec)
            {
                // 声音过去了：转回岗位朝向（有的话），免得一直盯着空处。
                _turnedAt.Remove(idx);
                _heardDirections.Remove(idx);
                _heardNoises.Remove(idx);
                Vec2 duty = brain.DutyDirection;
                if (duty.LengthSquared > 1e-4f) RequestBody(agent, duty, brain.DutyPoint, "return-duty");
            }

            // ② 日常头眼：只给眼部兴趣点坐标，不保存能够带动身体的SetLookAgent对象。
            bool enemy = player?.Team != null && agent.Team != null && agent.Team.IsEnemyOf(player.Team);
            float gazeRadius = enemy ? cfg.HostileGazeRadiusMeters : cfg.GazeRadiusMeters;
            bool inCone = player != null && AgentControlHelper.SafeIsActive(player)
                          && agent.Position.Distance(player.Position) <= gazeRadius
                          && NpcSightSystem.IsTargetInViewCone(agent, player, useBodyFacing: true);
            // 对象注视属于明确的动作/会话；日常交接残留的玩家对象必须解除。
            Agent actualTarget = agent.GetLookAgent();
            bool idle = brain.CurrentAction == null || brain.CurrentAction is StreetIdleAction
                || brain.CurrentAction is StayAction stay && !stay.OwnsLookTarget && !stay.IsKnockout;
            if (idle && !moving && actualTarget == player && actualTarget != null)
            {
                AgentControlHelper.StopLooking(agent);
                actualTarget = null;
            }
            if (inCone && idle && !moving && actualTarget == null)
            {
                // 逐帧更新目标眼睛坐标；这些写入来自本层，原版选择链仍被屏蔽。
                AgentControlHelper.LookAtPointOfInterest(agent, player);
                _lookingAt[agent] = player;
            }
            else ReleaseGaze(agent); // 不清其他动作设置的对象目标。
        }

        private static void RequestBody(Agent agent, Vec2 direction, Vec3 source, string owner)
        {
            if (_requestedBodies.TryGetValue(agent.Index, out var last)
                && Vec2.DotProduct(last, direction) >= 0.9986f) return; // 约3度以内保留原请求。
            AgentControlHelper.SetBodyDirection(agent, direction);
            _requestedBodies[agent.Index] = direction;
            _bodyReasons[agent.Index] = owner;
            if (Settings.Instance.ShowPerformanceFacingDebugMessages)
                DebugLogger.Log("[PerceptionFacing] owner=" + owner + " actor=" + agent.Name + "#" + agent.Index
                    + " source=" + source + " direction=" + direction + " " + Describe(agent));
        }

        /// <summary>默认岗位行为须尊重仍在保持的声源方向，不能与感知层交替转身。</summary>
        public static bool TryGetHeardDirection(Agent agent, out Vec2 direction)
        {
            direction = Vec2.Zero;
            return agent != null && _turnedAt.ContainsKey(agent.Index)
                && _heardDirections.TryGetValue(agent.Index, out direction);
        }

        /// <summary>只读本次感知方向请求，数值不是原生身体到位证明。</summary>
        public static string DescribeBodyRequest(Agent agent)
        {
            if (agent == null || !_requestedBodies.TryGetValue(agent.Index, out var direction)) return "none";
            float yaw = (float)(Math.Atan2(direction.y, direction.x) * 180d / Math.PI);
            return yaw.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "deg/"
                + (_bodyReasons.TryGetValue(agent.Index, out var reason) ? reason : "unknown");
        }

        /// <summary>在动作交接之前仅释放本层的眼部兴趣点；不改其他动作的SetLookAgent。</summary>
        public static void ReleaseGaze(Agent agent)
        {
            if (agent == null || !_lookingAt.Remove(agent)) return;
            if (ReferenceEquals(_mission, Mission.Current)) AgentControlHelper.StopLookingAtPointOfInterest(agent);
        }

        /// <summary>销毁回调只丢托管引用，不能访问已释放的native Agent。</summary>
        internal static void Forget(Agent agent) { if (agent != null) _lookingAt.Remove(agent); }

        public static string DescribeHeadTarget(Agent agent)
            => agent != null && _lookingAt.TryGetValue(agent, out var target)
                ? target == Agent.Main ? "player" : "agent#" + target.Index : "none";

        /// <summary>某个人最后一次听到声音的时刻（诊断用）。</summary>
        public static string Describe(Agent agent)
        {
            if (agent == null) return "none";
            _turnedAt.TryGetValue(agent.Index, out float t);
            var player = Agent.Main;
            var cfg = StoryProbeTuning.Current?.Noise;
            var target = agent.GetLookAgent();
            var brain = AgentAIController.GetBrainForAgent(agent);
            bool hasNoise = _heardNoises.TryGetValue(agent.Index, out var noise);
            return "noises=" + _noises.Count + " lastTurn=" + (_turnedAt.ContainsKey(agent.Index) ? (Now - t).ToString("F1") + "s ago" : "-")
                + " playerDistance=" + (player == null ? "-" : agent.Position.Distance(player.Position).ToString("F1"))
                + " gazeRadius=" + (cfg == null ? "-" : (player?.Team != null && agent.Team != null && agent.Team.IsEnemyOf(player.Team) ? cfg.HostileGazeRadiusMeters : cfg.GazeRadiusMeters).ToString("F1"))
                + " ordinaryFootstepRadius=" + (cfg == null ? "-" : cfg.OrdinaryFootstepRadiusMeters.ToString("F1"))
                + " headOwned=" + _lookingAt.ContainsKey(agent) + " headPointTarget=" + DescribeHeadTarget(agent)
                + " bodyFromNoise=" + _heardDirections.ContainsKey(agent.Index)
                + " bodyRequest=" + DescribeBodyRequest(agent)
                + " gazeTarget=" + (target == null ? "none" : target == player ? "player" : "agent#" + target.Index)
                + " inBodyCone=" + (player != null && AgentControlHelper.SafeIsActive(player)
                    && NpcSightSystem.IsTargetInViewCone(agent, player, useBodyFacing: true))
                + " action=" + (brain?.CurrentAction?.GetType().Name ?? "none")
                + " performing=" + (brain?.IsPerforming == true) + " moving=" + (brain?.IsMovingOrTurning == true)
                + " flags=" + agent.GetScriptedFlags()
                + " noiseSource=" + (hasNoise ? noise.Pos.ToString() : "-")
                + " noiseKind=" + (hasNoise ? (noise.Footstep ? "footstep" : "world") : "-")
                + " noiseLoud=" + (hasNoise && noise.Loud)
                + " noiseRadius=" + (hasNoise ? noise.Radius.ToString("F1") : "-")
                + " noiseDistance=" + (hasNoise ? agent.Position.Distance(noise.Pos).ToString("F1") : "-")
                + " noiseAge=" + (hasNoise ? (Now - noise.At).ToString("F1") : "-");
        }
    }
}
