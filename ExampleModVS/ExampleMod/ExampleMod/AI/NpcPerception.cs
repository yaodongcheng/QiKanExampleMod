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
    ///      声音有响度（半径）：玩家蹲着不出声、走路小声（默认 6 米）、跑步大声（默认 15 米）。
    ///      所以玩家能不能被回头发现，是由**自己怎么走**决定的，不是被距离阈值出卖。
    ///   ② **头只朝"视野扇形里的东西"转** —— 目标进了身体正前方的扇形就 `SetLookAgent` 看过去（只转眼睛/头，不转身体），
    ///      出扇形就松开。于是"先被声音引得转过身，才可能正好看见你"这条链自然成立。
    ///
    /// 边界：
    ///   · 这是**横切层**，不进脑的行为队列 —— 不产生任何排队/切换日志，也不和动作抢控制权；
    ///   · 战斗 / 被捆 / 倒地 / 演出中的人不参与（演出有台本，不能被脚步声打断）；
    ///   · 正在走路/转身的人不写身体朝向（会和移动合同打架），只做注视。
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
        }

        private const int MaxNoises = 16;
        private static readonly List<Noise> _noises = new List<Noise>(MaxNoises);

        /// <summary>每个 NPC 已经"处理过"的最新声音时刻 + 上次回头时刻（按 agent 索引存，Agent 销毁后自然失效）。</summary>
        private static readonly Dictionary<int, float> _heardAt = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> _turnedAt = new Dictionary<int, float>();
        private static readonly HashSet<int> _lookingAt = new HashSet<int>();
        private static readonly Dictionary<int, Vec2> _requestedBodies = new Dictionary<int, Vec2>();
        private static readonly Dictionary<int, Vec2> _heardDirections = new Dictionary<int, Vec2>();
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
            _heardDirections.Clear();
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
            if (Settings.Instance.IsInteractionDisabled()) return;
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
                if (agent == null || !AgentControlHelper.SafeIsActive(agent) || agent.Health <= 0f) continue;
                if (agent == player) continue;
                // 只对我们接管的人（有岗位点）写身体；原版 NPC 由它们的导航器每帧写朝向，插一脚会打架。
                if (!brain.HasDutyPoint) continue;
                if (brain.IsPerforming || brain.IsStoryIntercepting || brain.IsInCombat || brain.IsBound || brain.IsStatusHeld || brain.IsStunned
                    || agent.GetScriptedFlags().HasFlag(Agent.AIScriptedFrameFlags.InConversation))
                {
                    // 控制权已交给其他系统，旧注视记录作废；归还后重新取得自己的注视。
                    _lookingAt.Remove(agent.Index);
                    _requestedBodies.Remove(agent.Index);
                    _turnedAt.Remove(agent.Index);
                    _heardDirections.Remove(agent.Index);
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
            Hear(player.Position, radius);
        }

        private static void TickAgent(AgentBrain brain, Agent agent, Agent player, float now, NoiseTuning cfg)
        {
            int idx = agent.Index;

            // ① 声音：只听最新一条、且只在响度半径内；处理过就不再重复转。
            Noise? latest = null;
            for (int i = _noises.Count - 1; i >= 0; i--)
            {
                var n = _noises[i];
                if (now - n.At > cfg.MaxAgeSec) continue;
                if (agent.Position.Distance(n.Pos) > n.Radius) continue;
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
                Vec2 duty = brain.DutyDirection;
                if (duty.LengthSquared > 1e-4f) RequestBody(agent, duty, brain.DutyPoint, "return-duty");
            }

            // ② 注视：玩家在身体正前方扇形内 + 距离内 → 只转眼睛/头；出扇形就松开。
            if (player == null || !AgentControlHelper.SafeIsActive(player)) return;
            bool inCone = agent.Position.Distance(player.Position) <= cfg.GazeRadiusMeters
                          && NpcSightSystem.IsTargetInViewCone(agent, player, useBodyFacing: true);
            bool wasLooking = _lookingAt.Contains(idx);
            if (inCone)
            {
                if (!wasLooking) { AgentControlHelper.LookAtAgent(agent, player); _lookingAt.Add(idx); }
            }
            else if (wasLooking)
            {
                // 只松开我们自己设的注视，不去清别人（对话/演出）挂的注视目标。
                AgentControlHelper.StopLooking(agent);
                _lookingAt.Remove(idx);
            }
        }

        private static void RequestBody(Agent agent, Vec2 direction, Vec3 source, string owner)
        {
            if (_requestedBodies.TryGetValue(agent.Index, out var last)
                && Vec2.DotProduct(last, direction) >= 0.9986f) return; // 约3度以内保留原请求。
            _requestedBodies[agent.Index] = direction;
            AgentControlHelper.SetBodyDirection(agent, direction);
            if (Settings.Instance.ShowPerformanceFacingDebugMessages)
                DebugLogger.Log("[PerceptionFacing] owner=" + owner + " actor=" + agent.Name + "#" + agent.Index
                    + " source=" + source + " direction=" + direction);
        }

        /// <summary>默认岗位行为须尊重仍在保持的声源方向，不能与感知层交替转身。</summary>
        public static bool TryGetHeardDirection(Agent agent, out Vec2 direction)
        {
            direction = Vec2.Zero;
            return agent != null && _turnedAt.ContainsKey(agent.Index)
                && _heardDirections.TryGetValue(agent.Index, out direction);
        }

        /// <summary>某个人最后一次听到声音的时刻（诊断用）。</summary>
        public static string Describe(Agent agent)
        {
            if (agent == null) return "none";
            _turnedAt.TryGetValue(agent.Index, out float t);
            return "noises=" + _noises.Count + " lastTurn=" + (t <= 0f ? "-" : (Now - t).ToString("F1") + "s ago");
        }
    }
}
