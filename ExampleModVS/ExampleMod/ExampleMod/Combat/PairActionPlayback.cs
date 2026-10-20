using System;
using System.Collections.Generic;
using LivingWorldNpcs.Animation;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 一侧的"播放驱动"（2026-10-19）。
    ///
    /// 为什么要有它：双人动作有两类播放方式 ——
    ///   · 一次性直接播 clip（处决）；
    ///   · 驱动现有动画状态机（扛人 / 被绑，状态机属于 <see cref="GrappleCarry"/> / <see cref="GrappleBind"/>）。
    /// 运行时只认这个接口，**不拥有状态机**；状态机仍归各自原来的宿主 Tick。
    /// </summary>
    public interface IPairSideDriver
    {
        /// <summary>起播前的解析/校验（不写通道）。两侧都通过才允许同帧起播。</summary>
        bool CanStart(Agent agent, out string why);

        /// <summary>起播（必须在与另一侧同一个 C# 调用里完成）。失败时给出原因。</summary>
        bool Start(Agent agent, out string why);

        /// <summary>每帧推进（直接播动作时只累计时间；状态机驱动可为空）。</summary>
        void Tick(Agent agent, float dt);

        /// <summary>演出是否已到出机点。</summary>
        bool IsFinished(Agent agent);

        /// <summary>
        /// 这个驱动是否让 pair **保持存活**（后置状态循环，例如躺地）。
        /// true ⇒ `Playing` 阶段不套整段超时；`RequestStop` 会走"演退出"（`RequestExit`）而不是直接取消。
        /// </summary>
        bool KeepsPairAlive { get; }

        /// <summary>请求演"退出"（放下 / 起身）；一次性动作可为空。</summary>
        void RequestExit(Agent agent);

        /// <summary>收摊（归还自己拿的东西；不碰租约）。</summary>
        void Release(Agent agent);
    }

    /// <summary>
    /// **通用状态机驱动**（2026-10-19）：按 `machine:名字` 创建一台已注册的动画状态机，
    /// 用 `EnterEvent`/`ExitEvent` 两个时刻名进出；状态机 XML 决定具体转到哪个状态。
    ///
    /// 适用：**只用事件边 + 一次性/循环状态**的机器（如 `executed.xml`）。
    /// 带自定义谓词的机器（`carry` / `bind` / `flight`）仍由各自适配器提供驱动。
    /// </summary>
    public sealed class MachinePairDriver : IPairSideDriver
    {
        private sealed class SimpleAnimContext : AnimContext, IAnimInputFacts
        {
            public float AnimRemainFrac = float.PositiveInfinity;
            bool IAnimInputFacts.KeyHeld(int keyIndex) => false;
            float IAnimInputFacts.AnimRemainFrac => AnimRemainFrac;
        }

        private readonly string _machineName;
        private readonly string _enterEvent;
        private readonly string _exitEvent;
        private AgentAnimStateMachine _machine;
        private SimpleAnimContext _ctx;

        public MachinePairDriver(string machineName, string enterEvent, string exitEvent)
        {
            _machineName = machineName;
            _enterEvent = enterEvent;
            _exitEvent = exitEvent;
        }

        /// <summary>状态机可以停在循环状态（如躺地）⇒ pair 不套超时。</summary>
        public bool KeepsPairAlive => true;

        public bool CanStart(Agent agent, out string why)
        {
            why = "-";
            if (string.IsNullOrWhiteSpace(_machineName))
            {
                why = "machine name is empty";
                return false;
            }
            if (!AnimMachineRegistry.IsRegistered(_machineName))
            {
                why = $"anim machine '{_machineName}' is not registered";
                return false;
            }
            if (string.IsNullOrWhiteSpace(_enterEvent))
            {
                why = "EnterEvent is empty (machine driver needs an entry seam)";
                return false;
            }
            return true;
        }

        public bool Start(Agent agent, out string why)
        {
            if (!CanStart(agent, out why))
                return false;

            _ctx = new SimpleAnimContext();
            _machine = AnimMachineRegistry.Create(_machineName, _ctx);
            if (_machine == null)
            {
                why = $"anim machine '{_machineName}' could not be created";
                return false;
            }

            if (!_machine.TryEventTarget(_enterEvent, out string state))
            {
                why = $"anim machine '{_machineName}' has no edge for EnterEvent '{_enterEvent}'";
                _machine = null;
                return false;
            }

            _machine.Force(agent, state, -1f);   // -1 = 用定义里的默认 blend
            return true;
        }

        public void Tick(Agent agent, float dt)
        {
            if (_machine == null)
                return;
            // 给 `anim="remaining"` 的边喂进度（与 CarryAnimContext 同一套口径）。
            if (_ctx != null)
                _ctx.AnimRemainFrac = _machine.CurrentRemainFrac;
            _machine.Tick(agent, dt);
        }

        public bool IsFinished(Agent agent)
            => _machine == null || _machine.Current == AgentAnimStateMachine.OutsideState;

        public void RequestExit(Agent agent)
        {
            if (_machine == null)
                return;
            if (string.IsNullOrWhiteSpace(_exitEvent))
            {
                _machine.Release(agent);
                return;
            }
            if (_machine.TryEventTarget(_exitEvent, out string state))
                _machine.Force(agent, state, -1f);
            else
                DebugLogger.Log($"[Pair] anim machine '{_machineName}' has no edge for ExitEvent '{_exitEvent}' (state={_machine.Current})");
        }

        public void Release(Agent agent)
        {
            _machine?.Release(agent);
            _machine = null;
        }
    }

    /// <summary>
    /// 一次性直接播一个动作（`play=action`）。玩家走 12 参 `SetActionChannel`，
    /// NPC 走 <see cref="AgentControlHelper.ForcePlayAction"/>（切 `as_human_warrior`、打断交互）。
    /// 调用序列与已验证的 `custom.exec_pair` 逐字一致。
    /// </summary>
    public sealed class DirectActionDriver : IPairSideDriver
    {
        private readonly PairSideDef _side;
        private readonly float _finishMargin;

        private ActionIndexCache _index;
        private float _duration;
        private float _pendingDuration;
        private float _elapsed;
        private bool _started;

        public DirectActionDriver(PairSideDef side, float finishMargin)
        {
            _side = side;
            _finishMargin = MathF.Max(0f, MathF.Min(0.9f, finishMargin));
        }

        /// <summary>只解析、不写通道。NPC 的动作时长按 `as_human_warrior` 查（它还没切过去）。</summary>
        public bool CanStart(Agent agent, out string why)
        {
            why = "-";
            if (agent == null || !AgentControlHelper.SafeIsActive(agent))
            {
                why = "agent is not active";
                return false;
            }
            if (_side == null || string.IsNullOrWhiteSpace(_side.ActionId))
            {
                why = "action id is empty";
                return false;
            }

            _index = ActionIndexCache.Create(_side.ActionId);
            // 🔴 1.3.15 的坑：查不到名字时 native 返回索引 0（不是 -1）⇒ 必须判 <= 0。
            if (_index == ActionIndexCache.act_none || _index.Index <= 0)
            {
                why = $"action '{_side.ActionId}' is NOT registered (act_none / index<=0)";
                return false;
            }

            _pendingDuration = DurationFor(agent, _index);
            if (_pendingDuration <= 0.01f)
            {
                why = $"action '{_side.ActionId}' resolved but duration is 0.00s (clip name not wired in action_sets?)";
                return false;
            }
            return true;
        }

        private static float DurationFor(Agent agent, ActionIndexCache idx)
        {
            try
            {
                MBActionSet warrior = MBActionSet.GetActionSet("as_human_warrior");
                if (warrior.IsValid)
                    return MBActionSet.GetActionAnimationDuration(warrior, idx);
            }
            catch (Exception) { }
            try { return MBActionSet.GetActionAnimationDuration(agent.ActionSet, idx); }
            catch (Exception) { return 0f; }
        }

        public bool Start(Agent agent, out string why)
        {
            _started = false;
            _elapsed = 0f;

            if (!CanStart(agent, out why))
                return false;

            try
            {
                if (ReferenceEquals(agent, Agent.Main))
                {
                    // 与 custom.exec_pair / custom.do_anim 同一条调用。
                    agent.SetActionChannel(0, _index, false, 0UL, 0f, 1f, -0.2f, 0.4f, 0f, false, -0.2f, 0, true);
                }
                else
                {
                    // NPC 必须走它：切 as_human_warrior + 打断坐椅子之类的交互。
                    // 默认混合参数（0.15 / 0.4）与已验证的 custom.exec_pair 逐字一致。
                    AgentControlHelper.ForcePlayAction(agent, _side.ActionId);
                }

                _duration = _pendingDuration;
                // 起播后再读一次（NPC 已切到 warrior set）；读不到就沿用校验值。
                float after = DurationFor(agent, _index);
                if (after > 0.01f)
                    _duration = after;

                _started = true;
                DebugLogger.Log($"[Pair] 起播：'{PairActionPose.Describe(agent)}' → '{_side.ActionId}'"
                                + $"，时长 {_duration:F2}s");
                return true;
            }
            catch (Exception ex)
            {
                why = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        public void Tick(Agent agent, float dt)
        {
            if (_started)
                _elapsed += dt;
        }

        public bool IsFinished(Agent agent)
        {
            if (!_started)
                return true;
            float done = _duration * (1f - _finishMargin);
            return _elapsed >= done;
        }

        public bool KeepsPairAlive => false;

        public void RequestExit(Agent agent)
        {
            // 一次性动作没有"退出 clip"；让 clip 自己演完。
        }

        public void Release(Agent agent)
        {
            _started = false;
        }

        public string Describe()
            => _started
                ? $"{_side.ActionId} {_elapsed:F2}/{_duration:F2}s"
                : (_side != null ? _side.ActionId + " (not started)" : "(null)");
    }

    /// <summary>不播任何东西（`play=none`，或动画已由别处驱动）。</summary>
    public sealed class NoopPairSideDriver : IPairSideDriver
    {
        public bool CanStart(Agent agent, out string why) { why = "-"; return true; }
        public bool Start(Agent agent, out string why) { why = "-"; return true; }
        public void Tick(Agent agent, float dt) { }
        public bool IsFinished(Agent agent) => true;
        public bool KeepsPairAlive => false;
        public void RequestExit(Agent agent) { }
        public void Release(Agent agent) { }
    }

    /// <summary>
    /// **控制权租约**（2026-10-19）：只记"我拿走的"，结束只归还自己那份。
    ///
    /// 🔴 三条硬纪律：
    ///   ① `GrappleBind` 已经冻住的人，pair 不得解冻（记录 PrePaused，不归还）；
    ///   ② 玩家解冻会让引擎把 `CameraBearing` 重置成移动方向、俯仰清零 ⇒ 冻结前存角度，
    ///      解冻后用 `SpringArmRig.WriteBackLookToEngine` 写回；
    ///   ③ 脚本移动只清自己发的（`DisableScriptedMovement`），**不要**顺手把控制器切成 AI
    ///      （`ForceUnlockAgent` 会那样做，对玩家是错的）。
    /// </summary>
    public sealed class PairControlLease
    {
        private sealed class Rec
        {
            public Agent Agent;
            public bool PlayerFrozen;
            public bool AiPaused;
            public bool PrePaused;
            public bool Scripted;
        }

        private readonly Dictionary<Agent, Rec> _recs = new Dictionary<Agent, Rec>();

        private float _savedBearingDeg;
        private float _savedElevationDeg;
        private bool _haveCameraAngles;

        /// <summary>
        /// 玩家发起者/玩家受击方：冻结控制器 + 暂停 AI + 保存相机角度。
        /// <paramref name="takeOwnership"/> = false 时只记 PrePaused（调用方自己负责归还，如 `GrappleBind` 按住的玩家）。
        /// </summary>
        public void AcquirePlayerControl(Agent a, bool takeOwnership = true)
        {
            if (a == null)
                return;
            Rec rec = GetOrCreate(a);
            if (rec.PlayerFrozen || rec.PrePaused)
                return;
            if (!takeOwnership)
            {
                rec.PrePaused = true;
                return;
            }

            SaveCameraAngles();
            try { V.SetPlayerControlFrozen(a, true); } catch (Exception ex) { DebugLogger.Log($"[Pair] 冻结玩家失败：{ex.Message}"); }
            rec.PlayerFrozen = true;
            try { a.SetIsAIPaused(true); } catch (Exception) { }
            rec.AiPaused = true;
        }

        /// <summary>
        /// 目标侧（defender）：由我们暂停 AI（原版 AI 不再抢动作）。
        /// 玩家当 defender 时走 <see cref="AcquirePlayerControl"/>（冻结控制器）。
        /// <paramref name="takeOwnership"/> = false 时只记 PrePaused，不抢别人的冻结。
        /// </summary>
        public void AcquireDefenderHold(Agent a, bool takeOwnership = true)
        {
            if (a == null)
                return;
            if (ReferenceEquals(a, Agent.Main))
            {
                AcquirePlayerControl(a, takeOwnership);
                return;
            }

            Rec rec = GetOrCreate(a);
            if (rec.AiPaused || rec.PrePaused)
                return;
            if (!takeOwnership)
            {
                rec.PrePaused = true;
                return;
            }
            try { a.SetIsAIPaused(true); } catch (Exception) { }
            rec.AiPaused = true;
        }

        /// <summary>接近/对齐阶段：停掉椅子/对话之类的交互（否则引擎每帧覆盖姿势）。</summary>
        public void StopInteraction(Agent a)
        {
            if (a == null || !AgentControlHelper.SafeIsActive(a))
                return;
            try
            {
                if (a.IsUsingGameObject)
                    a.StopUsingGameObject(true, Agent.StopUsingGameObjectFlags.None);
            }
            catch (Exception) { }
        }

        /// <summary>记下"我们给这个 agent 下过脚本移动"，收摊时只清这一份。</summary>
        public void NoteScripted(Agent a)
        {
            if (a == null) return;
            GetOrCreate(a).Scripted = true;
        }

        /// <summary>
        /// 标记"这个 agent 已被别的系统按住"（如 `GrappleBind`）：本租约**不抢冻结、也不解冻**。
        /// 用于扛人这种"defender 早在绑缚阶段就被按住"的场景。
        /// </summary>
        public void MarkHeldByOther(Agent a)
        {
            if (a == null) return;
            GetOrCreate(a).PrePaused = true;
        }

        /// <summary>这个 agent 现在归租约管吗。</summary>
        public bool Has(Agent a) => a != null && _recs.ContainsKey(a);

        /// <summary>全部归还（正常结束 / 中止 / handoff 的旧 attacker 都走这里）。</summary>
        public void ReleaseAll()
        {
            var agents = new List<Agent>(_recs.Keys);
            foreach (Agent a in agents)
                ReleaseActor(a);
            _haveCameraAngles = false;
        }

        /// <summary>只归还某一个 agent（handoff 时旧 attacker 退场用）。</summary>
        public void ReleaseActor(Agent a)
        {
            if (a == null || !_recs.TryGetValue(a, out Rec rec))
                return;
            _recs.Remove(a);

            try
            {
                if (rec.Scripted)
                {
                    a.DisableScriptedMovement();
                    a.SetMaximumSpeedLimit(-1f, false);
                    a.SetScriptedFlags(Agent.AIScriptedFrameFlags.None);
                }
                if (rec.AiPaused && !rec.PrePaused)
                    a.SetIsAIPaused(false);
                if (rec.PlayerFrozen)
                {
                    V.SetPlayerControlFrozen(a, false);
                    // 🔴 解冻后引擎下一帧会把 bearing 重置成移动方向 ⇒ 立刻写回我们记下的角度。
                    WriteBackCameraAngles();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Pair] 归还控制权失败（{PairActionPose.Describe(a)}）：{ex.GetType().Name}: {ex.Message}");
            }
        }

        private Rec GetOrCreate(Agent a)
        {
            if (!_recs.TryGetValue(a, out Rec rec))
            {
                rec = new Rec { Agent = a };
                _recs[a] = rec;
            }
            return rec;
        }

        private void SaveCameraAngles()
        {
            if (_haveCameraAngles)
                return;
            try
            {
                if (!(ScreenManager.TopScreen is MissionScreen screen))
                    return;
                _savedBearingDeg = screen.CameraBearing * (180f / MathF.PI);
                _savedElevationDeg = screen.CameraElevation * (180f / MathF.PI);
                _haveCameraAngles = true;
            }
            catch (Exception) { }
        }

        private void WriteBackCameraAngles()
        {
            if (!_haveCameraAngles)
                return;
            _haveCameraAngles = false;
            try
            {
                SpringArmRig.WriteBackLookToEngine(_savedBearingDeg, _savedElevationDeg);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Pair] 写回引擎相机角度失败：{ex.Message}");
            }
        }

        /// <summary>
        /// **交接**：把某个 agent 的控制权记录拆到一个新租约里（原租约不再持有它）。
        /// 用于「旧 pair 的 defender 转交给后继 pair」——两边各管各的归还。
        /// </summary>
        public PairControlLease SplitActor(Agent a)
        {
            var child = new PairControlLease();
            if (a == null || !_recs.TryGetValue(a, out Rec rec))
                return child;

            _recs.Remove(a);
            child._recs[a] = rec;

            // 相机角度跟着玩家走（一次 pair 里最多一个玩家）。
            if (rec.PlayerFrozen && _haveCameraAngles)
            {
                child._savedBearingDeg = _savedBearingDeg;
                child._savedElevationDeg = _savedElevationDeg;
                child._haveCameraAngles = true;
                _haveCameraAngles = false;
            }
            return child;
        }

        /// <summary>命令/日志用：一行租约摘要。</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("lease[");
            bool first = true;
            foreach (Rec rec in _recs.Values)
            {
                if (!first) sb.Append("，");
                first = false;
                sb.Append(PairActionPose.Describe(rec.Agent))
                  .Append(rec.PlayerFrozen ? "/playerFrozen" : "")
                  .Append(rec.AiPaused && !rec.PrePaused ? "/aiPaused" : "")
                  .Append(rec.PrePaused ? "/prePaused" : "")
                  .Append(rec.Scripted ? "/scripted" : "");
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
