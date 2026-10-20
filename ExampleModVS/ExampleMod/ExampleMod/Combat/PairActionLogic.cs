using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    public enum PairPhase
    {
        Idle,
        PrePosition,
        Align,
        Starting,
        Playing,
        Attached,
        Handoff,
        Ending,
        Done,
    }

    /// <summary>
    /// **一对**双人动作的运行实例（2026-10-19）。
    ///
    /// 阶段机：
    ///   Idle → PrePosition → Align → Starting → Playing / Attached → (Handoff) → Ending → Done
    ///
    /// 分工：
    ///   · 本类管"站位 / 同帧起播 / 挂接摆位 / 结束提交 / 控制权租约"；
    ///   · **播什么**交给 <see cref="IPairSideDriver"/>（直接动作 / 状态机适配器 / 不播）。
    ///
    /// 🔴 玩家与 NPC 完全平权：本类没有 `Agent.Main` 分支，只在"玩家走自动小步、NPC 走寻路"
    ///    和租约（冻结控制器 vs 暂停 AI）两处分叉。
    /// </summary>
    public sealed class PairActionInstance
    {
        private const float AlignHoldSeconds = 0.10f;
        private const float NpcReissueSeconds = 0.50f;
        private const float NpcRunDistance = 2.0f;
        private const float ExitTimeoutSeconds = 8.0f;

        // ── 全局默认（不进 CSV：没有哪个动作真正需要逐条调它们）──
        private const float DefaultApproachMaxDistance = 4.0f;   // 发起者距站位点上限（米）
        private const float ApproachPlayerSpeed = 4.0f;          // 玩家自动小步速度（米/秒）
        private const float ApproachMinTime = 0.25f;
        private const float ApproachMaxTime = 0.50f;
        private const float ApproachNpcTimeout = 6.0f;
        private const float ApproachNpcStopDistance = 0.15f;
        private const float PlayTimeoutSeconds = 10.0f;

        public PairActionDef Def { get; private set; }
        public Agent Attacker { get; private set; }
        public Agent Defender { get; private set; }
        public IPairSideDriver AttackerDriver { get; private set; }
        public IPairSideDriver DefenderDriver { get; private set; }
        public PairControlLease Lease { get; private set; }

        public PairPhase Phase { get; private set; } = PairPhase.Idle;
        public string Note { get; private set; } = "-";
        public float Elapsed { get; private set; }

        /// <summary>defender 已经转交给后继 pair（旧 pair 只等 attacker 的退出 clip 演完）。</summary>
        public bool DefenderTransferred { get; private set; }

        /// <summary>测试命令可覆盖结束策略（`custom.pair start ... end=hold`）；null = 用 CSV 的值。</summary>
        public PairEndPolicy? EndPolicyOverride;

        /// <summary>
        /// 挂接摆位的**运行期微调**（米，沿锚点的右/前）—— 兼容旧的 `custom.grapple carry offset`。
        /// 默认 0；正式站位仍以 CSV 的 `Stance_*` 为准。
        /// </summary>
        public float ExtraAttachRight;
        public float ExtraAttachForward;

        /// <summary>挂接朝向的运行期微调（度，叠加在 `Attach_OffsetYaw` 上；马背 across/reverse 用）。</summary>
        public float ExtraAttachYawDeg;

        public bool IsDone => Phase == PairPhase.Done;

        private float _phaseTimer;
        private float _moveElapsed;
        private float _moveDuration;
        private Vec3 _moveStart;
        private Vec3 _startSpot;
        private float _attackerYawDeg;
        private float _defenderYawDeg;
        private float _npcReissueTimer;

        private bool _stopRequested;
        private string _stopReason;

        /// <summary>
        /// defender 的**倒地状态**已经移交 <see cref="AgentStatus"/>（一次性动作演完、交互结束）。
        /// 移交后本 pair 不再拥有他：`Finish` 不 Release 他的驱动，`Lease` 里也已经没有他的记录。
        /// </summary>
        private bool _defenderMovedToStatus;

        private string _handoffSuccessorId;
        private Agent _handoffNewAttacker;

        public PairActionInstance(PairActionDef def, Agent attacker, Agent defender,
            IPairSideDriver attackerDriver, IPairSideDriver defenderDriver, PairControlLease lease)
        {
            Def = def;
            Attacker = attacker;
            Defender = defender;
            AttackerDriver = attackerDriver;
            DefenderDriver = defenderDriver;
            Lease = lease;
            Phase = PairPhase.Idle;
            Note = "created";
        }

        // ───────────────────────────── 初始化 ─────────────────────────────

        public bool TryInit(out string why) => TryInit(false, false, out why);

        /// <summary>
        /// 建立阶段：算站位、检查距离、拿控制权、发起接近。
        /// 交接启动（`Initiator=none`）直接进 <see cref="PairPhase.Attached"/>。
        /// <paramref name="defenderHeldByOther"/> = true：defender 已被别的系统按住（如 `GrappleBind`），
        /// 租约只记 PrePaused，不抢冻结、也不在结束时解冻。
        /// </summary>
        public bool TryInit(bool ignoreApproachDistance, bool defenderHeldByOther, out string why)
        {
            why = "-";
            if (Def == null || !AgentControlHelper.SafeIsActive(Attacker)
                || (Defender != null && !AgentControlHelper.SafeIsActive(Defender)))
            {
                why = "def / attacker / defender is null or inactive";
                return false;
            }

            if (Def.StartsByHandoff)
            {
                Phase = PairPhase.Attached;
                Note = "attached (handoff)";
                return true;
            }

            if (!PairActionPose.TryComputeStartPose(Def, Attacker, Defender,
                    out _startSpot, out _attackerYawDeg, out _defenderYawDeg, out why))
            {
                return false;
            }

            float dist = PairActionPose.HorizontalDistance(Attacker.Position, _startSpot);
            if (!ignoreApproachDistance && dist > DefaultApproachMaxDistance)
            {
                why = $"attacker is {dist:F2} m from the stance point (> {DefaultApproachMaxDistance:F1} m)";
                return false;
            }

            // 目标先按住（别人已经冻住的，租约只记 PrePaused，不抢）。
            if (defenderHeldByOther)
                Lease?.MarkHeldByOther(Defender);
            else
                Lease?.AcquireDefenderHold(Defender);

            if (ReferenceEquals(Attacker, Agent.Main))
            {
                Lease?.AcquirePlayerControl(Attacker);
                _moveStart = Attacker.Position;
                _moveDuration = MathF.Max(ApproachMinTime,
                    MathF.Min(ApproachMaxTime, dist / ApproachPlayerSpeed));
                _moveElapsed = 0f;
                Phase = PairPhase.PrePosition;
                Note = $"pre-position (auto-step {_moveDuration:F2}s, {dist:F2} m)";
                DebugLogger.Log($"[Pair] {Def.Id}：发起者=玩家，自动小步 {dist:F2} m / {_moveDuration:F2}s → 站位点 "
                                + $"({_startSpot.x:F2},{_startSpot.y:F2})");
            }
            else
            {
                AgentControlHelper.ScriptedMoveToPoint(Attacker, _startSpot, isRun: dist > NpcRunDistance, hasNav: false);
                Lease?.NoteScripted(Attacker);
                _npcReissueTimer = 0f;
                Phase = PairPhase.PrePosition;
                Note = $"pre-position (npc walk {dist:F2} m)";
                DebugLogger.Log($"[Pair] {Def.Id}：发起者='{PairActionPose.Describe(Attacker)}' 寻路 {dist:F2} m → 站位点 "
                                + $"({_startSpot.x:F2},{_startSpot.y:F2})");
            }
            return true;
        }

        // ───────────────────────────── 每帧 ─────────────────────────────

        public void Tick(float dt)
        {
            if (Phase == PairPhase.Done)
                return;

            Elapsed += dt;
            _phaseTimer += dt;

            if (!AgentControlHelper.SafeIsActive(Attacker)
                || (Defender != null && !DefenderTransferred && !AgentControlHelper.SafeIsActive(Defender)))
            {
                Bail("agent gone");
                return;
            }

            switch (Phase)
            {
                case PairPhase.PrePosition: TickPrePosition(dt); break;
                case PairPhase.Align: TickAlign(); break;
                case PairPhase.Starting: TickStarting(); break;
                case PairPhase.Playing: TickPlaying(dt); break;
                case PairPhase.Attached: TickAttached(dt); break;
                case PairPhase.Handoff: TickAttached(dt); break;   // 交接当帧仍按旧的摆位，成功后立即切走
                case PairPhase.Ending: TickEnding(dt); break;
            }
        }

        private void TickPrePosition(float dt)
        {
            // 目标可能在动（玩家当 defender）⇒ 定期重算站位点；NPC 被按住时不会动。
            if (Math.Abs(_phaseTimer % 0.25f) < dt)
            {
                if (PairActionPose.TryComputeStartPose(Def, Attacker, Defender,
                        out Vec3 spot, out float aYaw, out float dYaw, out _))
                {
                    _startSpot = spot;
                    _attackerYawDeg = aYaw;
                    _defenderYawDeg = dYaw;
                }
            }

            if (ReferenceEquals(Attacker, Agent.Main))
            {
                _moveElapsed += dt;
                float u = _moveDuration <= 0.001f ? 1f : MathF.Min(1f, _moveElapsed / _moveDuration);
                float s = u * u * (3f - 2f * u);   // smoothstep
                Vec3 pos = _moveStart + (_startSpot - _moveStart) * s;
                pos.z = Attacker.Position.z;       // Z 交给引擎按地形重算
                Attacker.TeleportToPosition(pos);
                try { Attacker.SetMovementDirection(PairActionPose.ForwardFromYaw(_attackerYawDeg).AsVec2); }
                catch (Exception) { }

                if (u >= 1f)
                    EnterAlign();
            }
            else
            {
                _npcReissueTimer += dt;
                if (_npcReissueTimer >= NpcReissueSeconds)
                {
                    _npcReissueTimer = 0f;
                    float d = PairActionPose.HorizontalDistance(Attacker.Position, _startSpot);
                    AgentControlHelper.ScriptedMoveToPoint(Attacker, _startSpot, isRun: d > NpcRunDistance, hasNav: false);
                    Lease?.NoteScripted(Attacker);
                }

                float dist = PairActionPose.HorizontalDistance(Attacker.Position, _startSpot);
                if (dist <= ApproachNpcStopDistance)
                {
                    EnterAlign();
                }
                else if (_phaseTimer > ApproachNpcTimeout)
                {
                    DebugLogger.Log($"[Pair] {Def.Id}：NPC 寻路超时（{_phaseTimer:F1}s，还差 {dist:F2} m）⇒ 贴到站位点");
                    Attacker.TeleportToPosition(_startSpot);
                    EnterAlign();
                }
            }
        }

        private void EnterAlign()
        {
            Phase = PairPhase.Align;
            _phaseTimer = 0f;
            Note = "align";

            // 精确贴到站位点 + 两人朝向对齐（LookDirection 写不动，只有 SetMovementDirection 管用）。
            try
            {
                Attacker.TeleportToPosition(_startSpot);
                Attacker.SetMovementDirection(PairActionPose.ForwardFromYaw(_attackerYawDeg).AsVec2);
                if (Defender != null)
                    Defender.SetMovementDirection(PairActionPose.ForwardFromYaw(_defenderYawDeg).AsVec2);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Pair] {Def.Id}：对齐摆位异常：{ex.Message}");
            }

            Lease?.StopInteraction(Attacker);
            Lease?.StopInteraction(Defender);
        }

        private void TickAlign()
        {
            if (_phaseTimer >= AlignHoldSeconds)
            {
                Phase = PairPhase.Starting;
                _phaseTimer = 0f;
                Note = "starting";
            }
        }

        private void TickStarting()
        {
            string whyA = "-";
            bool aOk = true;
            if (AttackerDriver != null)
                aOk = AttackerDriver.CanStart(Attacker, out whyA);

            string whyD = "-";
            bool dOk = true;
            if (DefenderDriver != null)
                dOk = DefenderDriver.CanStart(Defender, out whyD);

            if (!aOk || !dOk)
            {
                Bail($"preflight failed: attacker={whyA} defender={whyD}");
                return;
            }

            // 🔴 两侧必须在这一个方法里连续起播（中间不 await、不插帧）。
            bool aStart = AttackerDriver == null || AttackerDriver.Start(Attacker, out whyA);
            if (AttackerDriver != null && !aStart)
            {
                Bail($"attacker start failed: {whyA}");
                return;
            }

            bool dStart = DefenderDriver == null || DefenderDriver.Start(Defender, out whyD);
            if (DefenderDriver != null && !dStart)
            {
                AttackerDriver?.Release(Attacker);
                Bail($"defender start failed: {whyD}");
                return;
            }

            // 🔴 **发起者的控制权到此归还**：接近/对齐已经完成。
            //    · 玩家：解冻控制器（否则扛着人时玩家走不了），并写回相机角度；
            //    · NPC：清掉脚本移动（否则他会继续走）。
            //    defender 的冻结**继续保留**（挂接期间要按住他），到 Ending 才还。
            Lease?.ReleaseActor(Attacker);

            Phase = Def.IsPersistent ? PairPhase.Attached : PairPhase.Playing;
            _phaseTimer = 0f;
            Note = Def.IsPersistent ? "attached" : "playing";
            DebugLogger.Log($"[Pair] {Def.Id}：两侧同帧起播成功 → {Phase}（发起者控制权已归还）");
        }

        private void TickPlaying(float dt)
        {
            AttackerDriver?.Tick(Attacker, dt);
            DefenderDriver?.Tick(Defender, dt);

            bool aDone = AttackerDriver == null || AttackerDriver.IsFinished(Attacker);
            bool dDone = DefenderDriver == null || DefenderDriver.IsFinished(Defender);

            // 🔴 一次性交互演完、但 defender 还要留在后置循环（躺地）⇒ **pair 到此结束**。
            //    倒地是受害者一个人的状态，交给 `AgentStatus`；本 pair 释放双方、立即解散。
            //    （挂接类 persistent 不走这里 —— 那里 defender 的状态就是这一对本身。）
            if (aDone && !dDone && !Def.IsPersistent
                && DefenderDriver != null && DefenderDriver.KeepsPairAlive)
            {
                if (TryTransferDefenderToStatus())
                {
                    Finish();
                    return;
                }
                // 挂载失败（倒地状态没挂上）⇒ 退回旧行为：本 pair 继续持有，至少别让人凭空弹起来。
            }

            if (aDone && dDone)
            {
                EnterEnding("one-shot finished");
                return;
            }

            bool keepAlive = HasKeepAliveDriver;
            if (!keepAlive && Elapsed > PlayTimeoutSeconds)
                Bail($"timeout ({Elapsed:F1}s)");
        }

        /// <summary>
        /// 把 defender 的**倒地状态**（状态机驱动 + 租约记录）原子移交给 <see cref="AgentStatus"/>。
        /// 成功后本 pair 不再拥有 defender；失败时什么都不动（pair 继续按旧方式持有）。
        /// </summary>
        private bool TryTransferDefenderToStatus()
        {
            if (_defenderMovedToStatus)
                return true;

            AgentStatus agentStatus = AgentStatus.Current;
            if (agentStatus == null || DefenderDriver == null)
                return false;

            PairControlLease child = Lease?.SplitActor(Defender);
            if (!agentStatus.TryApply(Defender, "downed", DefenderDriver, child, Def.Id, out string why))
            {
                DebugLogger.Log($"[Pair] {Def.Id}：倒地状态移交失败（{why}）⇒ 仍由本 pair 持有");
                return false;
            }

            _defenderMovedToStatus = true;
            DebugLogger.Log($"[Pair] {Def.Id}：交互结束，倒地状态交给 AgentStatus（pair 解散）");
            return true;
        }

        /// <summary>有没有"后置状态循环"的驱动（如躺地）⇒ 不套整段超时。</summary>
        private bool HasKeepAliveDriver
            => (AttackerDriver != null && AttackerDriver.KeepsPairAlive)
            || (DefenderDriver != null && DefenderDriver.KeepsPairAlive);

        private void TickAttached(float dt)
        {
            if (!DefenderTransferred)
            {
                if (!TryPlaceAttached(out string why))
                {
                    Bail(why);
                    return;
                }
            }

            AttackerDriver?.Tick(Attacker, dt);
            DefenderDriver?.Tick(Defender, dt);

            if (_stopRequested)
            {
                EnterEnding(_stopReason ?? "stop requested");
                return;
            }
        }

        private bool TryPlaceAttached(out string why)
        {
            if (!PairActionPose.TryComputeAttachPose(Def, Attacker,
                    out Vec3 pos, out Vec3 face, out why))
                return false;

            if (ExtraAttachRight != 0f || ExtraAttachForward != 0f)
            {
                float yaw = PairActionPose.YawDegOf(PairActionPose.ForwardOf(Attacker, Attacker.IsMount));
                pos += PairActionPose.RightFromYaw(yaw) * ExtraAttachRight
                     + PairActionPose.ForwardFromYaw(yaw) * ExtraAttachForward;
            }
            if (ExtraAttachYawDeg != 0f)
                face = PairActionPose.ForwardFromYaw(PairActionPose.YawDegOf(face) + ExtraAttachYawDeg);

            try
            {
                Defender.TeleportToPosition(pos);
                Defender.SetMovementDirection(face.AsVec2);
                return true;
            }
            catch (Exception ex)
            {
                why = $"place attached failed: {ex.Message}";
                return false;
            }
        }

        // ───────────────────────────── 交接 ─────────────────────────────

        /// <summary>
        /// 把 defender 交给后继 pair（坐骑）。旧 pair 的 attacker 继续演退出 clip，演完退场。
        /// 必须在 <see cref="PairActionLogic"/> 里换占用表（本类不直接改）。
        /// </summary>
        public bool RequestHandoff(string successorId, Agent newAttacker, out string why)
        {
            why = "-";
            if (Phase != PairPhase.Attached)
            {
                why = $"handoff only allowed while Attached (now {Phase})";
                return false;
            }
            if (string.IsNullOrWhiteSpace(successorId))
            {
                why = "successor id is empty";
                return false;
            }
            if (string.IsNullOrWhiteSpace(Def.HandoffTo)
                || !string.Equals(Def.HandoffTo, successorId, StringComparison.OrdinalIgnoreCase))
            {
                why = $"'{Def.Id}' HandoffTo='{Def.HandoffTo}' does not allow '{successorId}'";
                return false;
            }
            if (newAttacker == null || !AgentControlHelper.SafeIsActive(newAttacker))
            {
                why = "new attacker is null / inactive";
                return false;
            }

            _handoffSuccessorId = successorId;
            _handoffNewAttacker = newAttacker;

            // 🔴 **立即交接**（不等下一帧）：调用方（GrappleCarry.PlaceOnHorse）需要紧接着
            //    拿到后继 pair 去设置马背偏移（HorseBackOffset / HorseSideOffset / HorseFaceMode）。
            Phase = PairPhase.Handoff;
            _phaseTimer = 0f;
            Note = "handoff";
            TryHandoff();
            return true;
        }

        private void TryHandoff()
        {
            string why;
            PairActionLogic logic = PairActionLogic.Current;
            if (logic == null)
            {
                Bail("handoff failed: PairActionLogic.Current is null");
                return;
            }

            if (!logic.BeginHandoff(this, _handoffSuccessorId, _handoffNewAttacker, out why))
            {
                DebugLogger.Log($"[Pair] {Def.Id}：交接失败（{why}）⇒ 退回放下");
                EnterEnding("handoff failed: " + why);
                return;
            }

            DefenderTransferred = true;
            Note = "handoff done; waiting for attacker exit";
            // 🔴 defender 已转交：这里**只**演 attacker 的退出（`EnterEnding` 会跳过 defender 的 ExitEvent），
            //    否则会把刚进的 `马背` 状态又用 `put-trigger` 踢回站缚。
            EnterEnding("handoff: attacker exit");
        }

        // ───────────────────────────── 结束 ─────────────────────────────

        public void RequestStop(string reason)
        {
            _stopRequested = true;
            _stopReason = string.IsNullOrWhiteSpace(reason) ? "stop" : reason;

            switch (Phase)
            {
                case PairPhase.PrePosition:
                case PairPhase.Align:
                case PairPhase.Starting:
                    Bail(_stopReason);
                    break;
                case PairPhase.Playing:
                    // 结束策略 vs 退出动画（两件事，别混）：
                    //   · kill_defender = 直接死，**跳过** ExitEvent（不先演起身）；
                    //   · 有后置状态循环（KeepsPairAlive）或 hold = 先演 ExitEvent（起身/收势），演完再收尾；
                    //   · 纯一次性 = 取消，不执行 end.policy。
                    PairEndPolicy policy = EndPolicyOverride ?? Def.EndPolicy;
                    if (policy == PairEndPolicy.KillDefender)
                        Finish();
                    else if (HasKeepAliveDriver || policy == PairEndPolicy.Hold)
                        EnterEnding(_stopReason);
                    else
                        Bail(_stopReason);
                    break;
                case PairPhase.Attached:
                    // 纯挂接关系（两侧都 play=none，例如「坐骑 ↔ 人」）没有退出 clip ⇒ 立即收尾，
                    // 否则 TickEnding 会再摆一帧、把人放回坐骑上。
                    if (AttackerDriver is NoopPairSideDriver && DefenderDriver is NoopPairSideDriver)
                        Finish();
                    else
                        EnterEnding(_stopReason);
                    break;
            }
        }

        /// <summary>硬中止（异常路径：绑没了 / 场景要收摊）。不走退出 clip，直接释放。</summary>
        public void Abort(string reason) => Bail(reason);

        /// <summary>
        /// **补刀**：跳过退出动画，直接按 `kill_defender` 收尾（`custom.pair stop end=kill`）。
        /// 与 `RequestStop` 的区别 = 不 Force `ExitEvent`（不会先演"起身"）。
        /// </summary>
        public void RequestKill(string reason)
        {
            if (IsDone)
                return;
            EndPolicyOverride = PairEndPolicy.KillDefender;
            Note = "killed (" + reason + ")";
            Finish();
        }

        private void EnterEnding(string reason)
        {
            if (Phase == PairPhase.Ending || Phase == PairPhase.Done)
                return;
            Phase = PairPhase.Ending;
            _phaseTimer = 0f;
            Note = "ending (" + reason + ")";
            try
            {
                AttackerDriver?.RequestExit(Attacker);
                // 🔴 defender 已交接给后继 pair ⇒ **不能再 Force 旧行的 ExitEvent**。
                if (!DefenderTransferred)
                    DefenderDriver?.RequestExit(Defender);
            }
            catch (Exception) { }
        }

        private void TickEnding(float dt)
        {
            // 退出 clip 期间继续贴位（放下演完才松手），但 defender 已转交时不再碰他。
            if (Def.IsPersistent && !DefenderTransferred && Defender != null
                && AgentControlHelper.SafeIsActive(Defender))
            {
                TryPlaceAttached(out _);
            }

            AttackerDriver?.Tick(Attacker, dt);
            DefenderDriver?.Tick(Defender, dt);

            bool aDone = AttackerDriver == null || AttackerDriver.IsFinished(Attacker);
            bool dDone = DefenderDriver == null || DefenderDriver.IsFinished(Defender);

            if ((aDone && dDone) || _phaseTimer > ExitTimeoutSeconds)
            {
                Finish();
            }
        }

        private void Finish()
        {
            try { AttackerDriver?.Release(Attacker); } catch (Exception) { }
            // 倒地状态已挂到 AgentStatus ⇒ defender 的驱动归它 Tick/Release，这里不能再碰。
            if (!_defenderMovedToStatus)
            {
                try { DefenderDriver?.Release(Defender); } catch (Exception) { }
            }

            ApplyEndPolicy();

            Lease?.ReleaseAll();
            Lease = null;

            Phase = PairPhase.Done;
            if (!Note.StartsWith("aborted", StringComparison.Ordinal))
                Note = "done";
            DebugLogger.Log($"[Pair] {Def.Id}：结束（{Note}）");
        }

        private void Bail(string reason)
        {
            // 攻击方先消失（死亡/离场）时，如果这一对本来就该把倒地状态交出去，先交 ——
            // 否则受害者会随着 pair 一起被 Release，当场弹回站立。
            if (!_defenderMovedToStatus && Phase == PairPhase.Playing
                && Def != null && !Def.IsPersistent
                && DefenderDriver != null && DefenderDriver.KeepsPairAlive
                && (AttackerDriver == null || AttackerDriver.IsFinished(Attacker)))
            {
                TryTransferDefenderToStatus();
            }

            DebugLogger.Log($"[Pair] {Def.Id}：🔴 中止（{reason}）");
            try { AttackerDriver?.Release(Attacker); } catch (Exception) { }
            if (!_defenderMovedToStatus)
            {
                try { DefenderDriver?.Release(Defender); } catch (Exception) { }
            }

            // 交接时 defender 的记录已经拆到后继 pair 的租约里；这里只会释放剩下的那份。
            Lease?.ReleaseAll();
            Lease = null;
            Phase = PairPhase.Done;
            Note = "aborted: " + reason;
        }

        private void ApplyEndPolicy()
        {
            PairEndPolicy policy = EndPolicyOverride ?? Def.EndPolicy;
            switch (policy)
            {
                case PairEndPolicy.KillDefender:
                    KillDefender();
                    break;
                case PairEndPolicy.RagdollDefender:
                    DebugLogger.Log("[Pair] RagdollDefender 本版未实现（按 Hold 处理）");
                    break;
                case PairEndPolicy.Hold:
                    break;
                default:
                    break;
            }
        }

        private void KillDefender() => KillAgent(Defender, $"{Def.Id} end.policy=kill_defender");

        /// <summary>
        /// **补刀**（共享管线）：pair 的 `end.policy=kill_defender` 与
        /// <see cref="AgentStatus"/> 的 `custom.pair stop end=kill` 走同一份。
        /// </summary>
        public static void KillAgent(Agent agent, string tag)
        {
            try
            {
                if (agent == null || !AgentControlHelper.SafeIsActive(agent))
                    return;
                Blow blow = new Blow(-1);
                blow.DamageType = DamageTypes.Blunt;
                blow.BoneIndex = 0;
                blow.VictimBodyPart = BoneBodyPartType.Chest;
                blow.GlobalPosition = agent.Position + Vec3.Up * 1.0f;
                blow.BaseMagnitude = 1000f;
                blow.InflictedDamage = 1000;
                blow.BlowFlag = BlowFlags.NoSound;
                blow.DamageCalculated = true;
                blow.WeaponRecord.FillAsMeleeBlow(null, null, -1, -1);
                agent.Die(blow);
                DebugLogger.Log($"[Pair] {tag}：'{PairActionPose.Describe(agent)}' 已处决");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Pair] {tag}：处决失败：{ex.GetType().Name}: {ex.Message}");
            }
        }

        private void ApplyEndCorrection()
        {
            // 2026-10-19 精简：`End_CorrectRelative/End_Right/End_Forward/End_Tolerance` 四列
            // 没有任何动作在用，已从表里删除；这里保留空实现只为不打断调用顺序。
            // 真需要"结束相对校正"时，按动作在 `Finish` 里单独写，不要再开四列。
        }

        // ───────────────────────────── 诊断 ─────────────────────────────

        public string StatusLine()
        {
            string a = PairActionPose.Describe(Attacker);
            string d = PairActionPose.Describe(Defender);
            string dist = "?";
            try
            {
                if (Attacker != null && Defender != null)
                    dist = PairActionPose.HorizontalDistance(Attacker.Position, Defender.Position).ToString("F2") + "m";
            }
            catch (Exception) { }

            return $"{Def.Id}: {Phase} | attacker={a} | defender={d} | dist={dist}"
                 + $" | elapsed={Elapsed:F1}s | {Note}"
                 + (Lease != null ? " | " + Lease.Describe() : "");
        }
    }

    /// <summary>
    /// **双人动作宿主**（MissionLogic，2026-10-19）。
    ///
    /// 职责：活动实例表、`Agent → PairActionInstance` 唯一占用、handoff、每帧 Tick、Mission 结束清理。
    /// 全局唯一：<see cref="Current"/>（`OnBehaviorInitialize` 里设；`OnEndMission` 清）。
    /// </summary>
    public sealed class PairActionLogic : MissionLogic
    {
        public static PairActionLogic Current { get; private set; }

        private readonly List<PairActionInstance> _active = new List<PairActionInstance>();
        private readonly Dictionary<Agent, PairActionInstance> _byAgent = new Dictionary<Agent, PairActionInstance>();

        /// <summary>
        /// 🔴🔴 **`Current` 必须在 `OnCreated` 里设**（2026-10-20 实机踩到：`custom.pair state` 恒报
        /// "not mounted"，而同场景的 `custom.pair list` 一切正常）：引擎的顺序是「先给已在列表里的行为调
        /// `OnBehaviorInitialize`，**之后**才调各 SubModule 的 `OnMissionBehaviorInitialize`」
        /// （反编译 `Mission.Initialize`）；本行为是在后者里 `AddMissionBehavior` 挂上的，而
        /// `AddMissionBehavior` **只调 `OnCreated`** ⇒ 它**永远拿不到 `OnBehaviorInitialize`**。
        /// 同款修复先例 = <see cref="GrappleLogic.OnCreated"/>（2026-10-03 同一坑）。
        /// </summary>
        public override void OnCreated()
        {
            base.OnCreated();
            Current = this;
        }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            Current = this;
        }

        protected override void OnEndMission()
        {
            // 🔴 Mission 卸载期不再碰 agent API；只清托管状态。
            _active.Clear();
            _byAgent.Clear();
            if (ReferenceEquals(Current, this))
                Current = null;
            base.OnEndMission();
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (_active.Count == 0)
                return;

            // 拷贝一份：Tick 里可能新增（handoff）或移除（Done）。
            var snapshot = _active.ToArray();
            foreach (PairActionInstance inst in snapshot)
            {
                try { inst.Tick(dt); }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[Pair] 🔴 {inst?.Def?.Id} Tick 异常：{ex.GetType().Name}: {ex.Message}");
                }
            }

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].IsDone)
                    RemoveInstance(_active[i]);
            }
        }

        // ───────────────────────────── 起 / 停 ─────────────────────────────

        public bool Begin(PairActionDef def, Agent attacker, Agent defender,
            IPairSideDriver attackerDriver, IPairSideDriver defenderDriver, out string why)
            => Begin(def, attacker, defender, attackerDriver, defenderDriver, false, false, out why);

        public bool Begin(PairActionDef def, Agent attacker, Agent defender,
            IPairSideDriver attackerDriver, IPairSideDriver defenderDriver,
            bool ignoreApproachDistance, out string why)
            => Begin(def, attacker, defender, attackerDriver, defenderDriver, ignoreApproachDistance, false, out why);

        /// <summary>
        /// 开一对双人动作。两侧驱动由调用方给（一次性动作 = <see cref="DirectActionDriver"/>；
        /// 状态机 = 适配器；不播 = <see cref="NoopPairSideDriver"/>）。
        /// <paramref name="ignoreApproachDistance"/> = 测试命令用（跳过"发起者离站位点太远"的拒绝）。
        /// <paramref name="defenderHeldByOther"/> = defender 已被别的系统按住（如 `GrappleBind`）。
        /// </summary>
        public bool Begin(PairActionDef def, Agent attacker, Agent defender,
            IPairSideDriver attackerDriver, IPairSideDriver defenderDriver,
            bool ignoreApproachDistance, bool defenderHeldByOther, out string why)
        {
            why = "-";
            if (def == null) { why = "def is null"; return false; }
            if (def.StartsByHandoff)
            {
                why = $"'{def.Id}' has Initiator=none (starts by handoff); use BeginHandoff";
                return false;
            }
            if (attacker == null || defender == null) { why = "attacker/defender is null"; return false; }
            if (ReferenceEquals(attacker, defender)) { why = "attacker == defender"; return false; }

            if (!AgentControlHelper.SafeIsActive(attacker) || !AgentControlHelper.SafeIsActive(defender))
            {
                why = "attacker or defender is inactive";
                return false;
            }

            // 处在特殊状态里的人不参与任何双人动作（他归 AgentStatus 管，先结束状态）。
            AgentStatus status = AgentStatus.Current;
            if (status != null)
            {
                if (status.IsInStatus(attacker)) { why = $"attacker is in status '{status.StatusIdOf(attacker)}'"; return false; }
                if (status.IsInStatus(defender)) { why = $"defender is in status '{status.StatusIdOf(defender)}' (end it first)"; return false; }
            }

            if (_byAgent.TryGetValue(attacker, out PairActionInstance busyA))
            {
                why = $"attacker is busy with pair '{busyA.Def.Id}'";
                return false;
            }
            if (_byAgent.TryGetValue(defender, out PairActionInstance busyD))
            {
                why = $"defender is busy with pair '{busyD.Def.Id}'";
                return false;
            }

            var lease = new PairControlLease();
            var inst = new PairActionInstance(def, attacker, defender, attackerDriver, defenderDriver, lease);

            if (!inst.TryInit(ignoreApproachDistance, defenderHeldByOther, out why))
            {
                lease.ReleaseAll();
                return false;
            }

            _active.Add(inst);
            _byAgent[attacker] = inst;
            _byAgent[defender] = inst;
            DebugLogger.Log($"[Pair] 开始 '{def.Id}'：attacker='{PairActionPose.Describe(attacker)}' defender='{PairActionPose.Describe(defender)}'");
            return true;
        }

        /// <summary>
        /// **交接**：把旧 pair 的 defender 交给后继 pair（newAttacker = 坐骑）。
        /// 只由 <see cref="PairActionInstance.TryHandoff"/> 调用。
        /// </summary>
        internal bool BeginHandoff(PairActionInstance old, string successorId, Agent newAttacker, out string why)
        {
            why = "-";
            if (old == null || old.Defender == null || old.Lease == null)
            {
                why = "old instance / defender / lease is null";
                return false;
            }
            if (!PairActionTable.TryGet(successorId, out PairActionDef successor) || successor == null)
            {
                why = $"successor '{successorId}' not found in PairActions.csv";
                return false;
            }
            if (!successor.StartsByHandoff)
            {
                why = $"successor '{successorId}' must have Initiator=none";
                return false;
            }
            if (!AgentControlHelper.SafeIsActive(newAttacker))
            {
                why = "new attacker (mount) is inactive";
                return false;
            }
            if (_byAgent.TryGetValue(newAttacker, out PairActionInstance busy))
            {
                why = $"new attacker is busy with pair '{busy.Def.Id}'";
                return false;
            }

            // 租约拆分：defender 的记录转给后继 pair；旧 pair 只留 attacker。
            PairControlLease childLease = old.Lease.SplitActor(old.Defender);

            var successorInstance = new PairActionInstance(
                successor, newAttacker, old.Defender,
                new NoopPairSideDriver(), new NoopPairSideDriver(), childLease);

            if (!successorInstance.TryInit(out why))
            {
                childLease.ReleaseAll();
                return false;
            }

            // 占用表：defender 改归后继；旧 pair 只留 attacker。
            if (_byAgent.TryGetValue(old.Defender, out PairActionInstance cur) && ReferenceEquals(cur, old))
                _byAgent.Remove(old.Defender);
            _byAgent[newAttacker] = successorInstance;
            _byAgent[old.Defender] = successorInstance;
            _active.Add(successorInstance);

            DebugLogger.Log($"[Pair] 交接：'{old.Def.Id}' 的 defender='{PairActionPose.Describe(old.Defender)}' → "
                            + $"'{successor.Id}'（attacker='{PairActionPose.Describe(newAttacker)}'）");
            return true;
        }

        // ───────────────────────────── 查询 / 停止 ─────────────────────────────

        public PairActionInstance GetByAgent(Agent a)
            => a != null && _byAgent.TryGetValue(a, out PairActionInstance inst) ? inst : null;

        public bool IsBusy(Agent a) => GetByAgent(a) != null;

        /// <summary>按定义 id 找活动实例（`custom.pair commit/stop <id>` 用）。</summary>
        public PairActionInstance FindByDefId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;
            foreach (PairActionInstance inst in _active)
            {
                if (inst.Def != null && string.Equals(inst.Def.Id, id, StringComparison.OrdinalIgnoreCase))
                    return inst;
            }
            return null;
        }

        /// <summary>按"锚点/发起者"找活动实例（坐骑 pair 的 attacker = 马）。</summary>
        public PairActionInstance FindByAttacker(Agent a)
        {
            if (a == null)
                return null;
            foreach (PairActionInstance inst in _active)
            {
                if (ReferenceEquals(inst.Attacker, a))
                    return inst;
            }
            return null;
        }

        /// <summary>按 `Start` 找第一条活动实例（`handoff` 启动的后继 pair 用；不写死 id）。</summary>
        public PairActionInstance FindFirstByStart(string start)
        {
            if (string.IsNullOrWhiteSpace(start))
                return null;
            foreach (PairActionInstance inst in _active)
            {
                if (inst.Def != null && string.Equals(inst.Def.Start, start, StringComparison.OrdinalIgnoreCase))
                    return inst;
            }
            return null;
        }

        /// <summary>这个 agent 正在挂接/被挂接在 <paramref name="anchor"/> 上吗。</summary>
        public bool IsAttachedTo(Agent attached, Agent anchor)
        {
            PairActionInstance inst = GetByAgent(attached);
            return inst != null && inst.Phase == PairPhase.Attached
                && ReferenceEquals(inst.Defender, attached)
                && ReferenceEquals(inst.Attacker, anchor);
        }

        public void Stop(Agent a, string reason)
        {
            PairActionInstance inst = GetByAgent(a);
            inst?.RequestStop(reason);
        }

        public string DescribeAll()
        {
            if (_active.Count == 0)
                return "pair: none active";

            var sb = new System.Text.StringBuilder();
            sb.Append("pair: ").Append(_active.Count).Append(" active");
            foreach (PairActionInstance inst in _active)
                sb.Append('\n').Append("  ").Append(inst.StatusLine());
            return sb.ToString();
        }

        private void RemoveInstance(PairActionInstance inst)
        {
            _active.Remove(inst);
            RemoveMapping(inst.Attacker, inst);
            RemoveMapping(inst.Defender, inst);
        }

        private void RemoveMapping(Agent a, PairActionInstance inst)
        {
            if (a == null)
                return;
            if (_byAgent.TryGetValue(a, out PairActionInstance cur) && ReferenceEquals(cur, inst))
                _byAgent.Remove(a);
        }

        // 🔴 2026-10-19：`Requires` token 白名单已删除。
        //    前置条件归**适配器/入口**（它们本来就在检查：GrappleCarry 检查 bind/站立/距离，
        //    PlaceOnHorse / CmdHandoff 检查 IsRideableMount）——配对表不再放一套硬编码 token。
    }
}
