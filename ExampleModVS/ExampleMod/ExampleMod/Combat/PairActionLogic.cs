using System;
using System.Collections.Generic;
using System.Globalization;
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
        private const float AlignYawToleranceDeg = 8.0f;   // 对齐后允许的朝向误差（超过就继续等它转过来）
        private const float AlignMaxSeconds = 0.80f;       // 等朝向收敛的上限（引擎转身有速率，不能无限等）
        /// <summary>
        /// 对齐时镜头转到最终朝向的时长（秒）—— **和起播解耦**：起播只看身体对齐，
        /// 镜头按自己的时长慢慢转，转完才交还相机。热调：`custom.pair camturn &lt;秒&gt;`。
        /// </summary>
        public static float CameraTurnSeconds = 0.80f;
        private const string PairCameraOwner = "pair";      // 相机服务的持有者标签
        private const float NpcReissueSeconds = 0.50f;
        private const float NpcRunDistance = 2.0f;
        private const float ExitTimeoutSeconds = 8.0f;

        // ── 全局默认（不进 CSV：没有哪个动作真正需要逐条调它们）──
        private const float DefaultApproachMaxDistance = 4.0f;   // 发起者距站位点上限（米）
        private const float ApproachNpcTimeout = 6.0f;
        private const float ArrivalStallMaxDistance = 0.80f;   // 离站位点这么近 + 停住 = 到了（引擎寻路会停在 0.2~0.5m）
        private const float ArrivalStallSeconds = 0.20f;
        private const float ArrivalStallSpeed = 0.20f;         // 米/秒
        private const float WalkNoProgressSeconds = 0.30f;     // 距离多久没实质改善 = 走到了（"在挪但没靠近"兜底）
        /// <summary>玩家长按触发时离站位点多近就**直接贴过去**（不走位）：长按暗杀本来就是贴脸，走位只会让人干等。</summary>
        private const float PlayerSnapMaxDistance = 2.0f;
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
        private Vec3 _startSpot;
        private float _attackerYawDeg;
        private float _defenderYawDeg;

        // 🔴 2026-10-22：触发那一刻冻结的“发起者→目标”水平方向 —— 站位/朝向只沿它算。
        private Vec3 _lineDir;
        private bool _haveLine;

        // 🔴 2026-10-22：对齐时镜头跟着渐变（只用引擎相机，不接管）
        private float _camStartYawDeg;      // 渐变起点 = 对齐那一刻的引擎真实角度
        private float _camPitchDeg;
        private float _camTurnElapsed;      // 镜头转动已经走了多久（和 _phaseTimer 解耦）
        private float _camLogTimer;         // [PairCam] 取证日志节流
        private bool _camTurnDone;          // 对齐转向已走完（一次性演出继续持有镜头，见 TickPerformanceCamera）
        private float _camPerfYawDeg;       // 演出镜头 yaw（低通后的值）
        private bool _camPerfYawInit;
        private bool _haveCamStart;

        // ── 音效 / 粒子轨道（2026-10-23）──
        private float _playTime;            // 从演出（Playing）开始算的秒
        private int _soundFired;            // 已触发到第几条（表按时间排好）
        private int _fxFired;

        /// <summary>演出镜头 yaw 的低通时间常数（秒）—— 滤掉骨盆随动作的高频摇摆，只跟整体转身。
        /// 2026-10-23 实测从 0.15 提到 0.60：暗杀 clip 里骨盆会先甩到 -169° 再回 -105°，
        /// 0.15 跟得太紧（镜头明显甩一下）；0.60 只跟净转身。</summary>
        private const float PerfCamYawTau = 0.60f;
        /// <summary>演出镜头 yaw 的最大转速（度/秒）—— 防 180° 转身时甩头（2026-10-23 360→180）。</summary>
        private const float PerfCamYawMaxRate = 180f;

        /// <summary>`custom.pair trace 1` 打开：演出镜头每 0.25s 打一行取证（raw/smooth/bodyLook/位置）。</summary>
        public static bool PerfCameraDebug = false;
        private float _camPerfLogTimer;

        private float _npcReissueTimer;
        private Vec3 _lastWalkPos;          // 走到位判定：上一帧位置
        private float _stallTimer;          // 停住多久了
        private bool _hasWalkPos;
        private float _bestWalkDist;        // 走位：历史最近距离（"在挪但没靠近"判定用）
        private float _walkNoProgressTimer; // 走位：距离多久没有实质改善

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

            // 🔴 2026-10-22：先把“触发那一刻的连线”冻结下来 —— 之后所有站位/朝向重算都用它。
            Vec3 toDef = Defender.Position - Attacker.Position;
            toDef.z = 0f;
            _haveLine = toDef.LengthSquared > 1e-4f;
            _lineDir = _haveLine ? toDef.NormalizedCopy() : Vec3.Zero;

            if (!PairActionPose.TryComputeStartPose(Def, Attacker, Defender,
                    _haveLine ? _lineDir : Vec3.Zero,
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

            // 🔴 2026-10-22：玩家与 NPC 同一条 —— **真实步行**（引擎寻路），不再每帧 Teleport 滑步。
            //    玩家：冻结控制器（Controller=AI）但**不暂停 AI**，靠 scripted position 驱动他走过去；
            //    到 Align 才停脚本移动 + 暂停 AI。
            if (ReferenceEquals(Attacker, Agent.Main))
            {
                Lease?.AcquirePlayerControl(Attacker, pauseAi: false);

                // 🔴 2026-10-23 用户裁定：**长按暗杀 = 已经贴脸，别再让玩家走**。
                //    实测走位会卡满 6 秒（离站位点还差 0.75 m 就是不靠近），期间受害者站着、玩家干等；
                //    而且走位时间会把演出的超时预算吃掉（进而 knockout 没执行、人自己站起来）。
                //    ⇒ 近距离直接贴到站位点（`EnterAlign` 本来就会精确落位），相机同一刻接管，位移看不出来。
                if (dist <= PlayerSnapMaxDistance)
                {
                    DebugLogger.Log($"[Pair] {Def.Id}：发起者='{PairActionPose.Describe(Attacker)}' 离站位点 "
                        + $"{dist:F2} m（≤ {PlayerSnapMaxDistance:F1} m）⇒ 直接贴过去，不走位");
                    EnterAlign();
                    return true;
                }
            }

            AgentControlHelper.ScriptedMoveToPoint(Attacker, _startSpot, isRun: dist > NpcRunDistance, hasNav: false);
            Lease?.NoteScripted(Attacker);
            _npcReissueTimer = 0f;
            _lastWalkPos = Attacker.Position;
            _hasWalkPos = true;
            _stallTimer = 0f;
            _bestWalkDist = float.MaxValue;
            _walkNoProgressTimer = 0f;
            Phase = PairPhase.PrePosition;
            Note = $"pre-position (walk {dist:F2} m)";
            DebugLogger.Log($"[Pair] {Def.Id}：发起者='{PairActionPose.Describe(Attacker)}' 步行 {dist:F2} m → 站位点 "
                            + $"({_startSpot.x:F2},{_startSpot.y:F2})");
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

            // 🔴 2026-10-22：镜头有自己的时间轴 —— 起播后继续转，转完才交还（见 TickCameraTurn）。
            TickCameraTurn(dt);
            // 一次性演出：镜头继续持有，跟攻击者的**动画身体朝向**（execution02 会自己转 180°）。
            if (Phase == PairPhase.Playing || Phase == PairPhase.Ending)
                TickPerformanceCamera(dt);

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
                        _haveLine ? _lineDir : Vec3.Zero,
                        out Vec3 spot, out float aYaw, out float dYaw, out _))
                {
                    _startSpot = spot;
                    _attackerYawDeg = aYaw;
                    _defenderYawDeg = dYaw;
                }
            }

            // 🔴 2026-10-22：玩家与 NPC 同一条 —— 脚本走路（每 0.5s 重发），到位/超时才进 Align。
            //    不再每帧 Teleport（那是“滑步”，用户实机反馈太明显）。
            _npcReissueTimer += dt;
            if (_npcReissueTimer >= NpcReissueSeconds)
            {
                _npcReissueTimer = 0f;
                float d = PairActionPose.HorizontalDistance(Attacker.Position, _startSpot);
                AgentControlHelper.ScriptedMoveToPoint(Attacker, _startSpot, isRun: d > NpcRunDistance, hasNav: false);
                Lease?.NoteScripted(Attacker);
            }

            float dist = PairActionPose.HorizontalDistance(Attacker.Position, _startSpot);

            // 🔴 2026-10-22：引擎寻路**到不了 0.15m**（实机日志：停在 0.22m ⇒ 干等 6s 超时才起播）。
            //    ⇒ 加“停住即到达”判定：已经离站位点 ≤0.8m、且连续 0.2s 几乎不动 = 到了，
            //       剩下的几厘米由 `EnterAlign` 的精确贴点补掉（沿连线的一小步，肉眼无感）。
            float moved = _hasWalkPos ? PairActionPose.HorizontalDistance(Attacker.Position, _lastWalkPos) : 0f;
            _lastWalkPos = Attacker.Position;
            _hasWalkPos = true;
            float speed = dt > 1e-4f ? moved / dt : 0f;
            if (speed < ArrivalStallSpeed) _stallTimer += dt;
            else _stallTimer = 0f;

            // 🔴 2026-10-23：光看"速度 < 0.2"抓不住"在动但没靠近"（贴墙打转 / 撞人原地蹭），
            //    实测卡在 0.75m 干等 6s 超时 ⇒ 再按"距离有没有实质改善"兜一层（5cm 才算改善）。
            if (dist < _bestWalkDist - 0.05f)
            {
                _bestWalkDist = dist;
                _walkNoProgressTimer = 0f;
            }
            else
            {
                _walkNoProgressTimer += dt;
            }

            bool arrived = dist <= ApproachNpcStopDistance
                        || (dist <= ArrivalStallMaxDistance
                            && (_stallTimer >= ArrivalStallSeconds
                                || _walkNoProgressTimer >= WalkNoProgressSeconds));

            if (arrived)
            {
                if (dist > ApproachNpcStopDistance)
                    DebugLogger.Log($"[Pair] {Def.Id}：走路停住 = 到达（还差 {dist:F2} m，停顿 {_stallTimer:F2}s）⇒ 贴到站位点");
                EnterAlign();
            }
            else if (_phaseTimer > ApproachNpcTimeout)
            {
                DebugLogger.Log($"[Pair] {Def.Id}：步行超时（{_phaseTimer:F1}s，还差 {dist:F2} m）⇒ 贴到站位点");
                Attacker.TeleportToPosition(_startSpot);
                EnterAlign();
            }
        }

        private void EnterAlign()
        {
            Phase = PairPhase.Align;
            _phaseTimer = 0f;
            Note = "align";

            // 停掉脚本走路 + 暂停 AI（玩家在 TryInit 里特意没有暂停 AI；NPC 也停一下，别继续走）
            try { Attacker.DisableScriptedMovement(); } catch (Exception) { }
            if (ReferenceEquals(Attacker, Agent.Main))
            {
                try { Attacker.SetMaximumSpeedLimit(-1f, false); } catch (Exception) { }
                Lease?.PauseAi(Attacker);
            }

            // 精确贴到站位点 + 两人朝向对齐（movement 管身体；look 在 TickAlign 里双写）。
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

            // 🔴 2026-10-22：**走相机服务那套成熟方案**（与钩索同一套）——
            //    用 `follow_engine` 从引擎此刻的机位无缝接管（Seed=Engine，不硬切），
            //    对齐期间由我们逐帧 `SetLook` 把镜头平滑转向最终朝向，转完 `RequestHandBack` 渐变交还。
            _haveCamStart = false;
            try
            {
                if (ReferenceEquals(Attacker, Agent.Main)
                    && CameraLook.TryGetEngineAnglesRaw(out float camYaw, out float camPitch)
                    && CameraService.Play("pair_shot", Attacker, 0f, PairCameraOwner,
                        new CameraReturnPolicy
                        {
                            WriteBackLook = true,
                            WriteBackLookOnRelease = true,   // 收尾会解冻玩家 ⇒ 撒手那一刻写回（同钩索拉拽）
                            ClearSpecial = true,             // 清掉引擎瞄准态的冻结修正（防高度台阶）
                        }))
                {
                    _camStartYawDeg = camYaw;
                    _camPitchDeg = camPitch;
                    _camTurnElapsed = 0f;
                    _camTurnDone = false;
                    _camPerfYawInit = false;
                    _camPerfLogTimer = 0f;
                    _haveCamStart = true;
                }
                else
                {
                    DebugLogger.Log($"[Pair] {Def.Id}：相机没接管"
                        + $"（持有者={CameraService.Holder ?? "无"} / 读不到引擎角度）—— 镜头不跟转");
                }
            }
            catch (Exception) { }

            LogPairCam("takeover");

            Lease?.StopInteraction(Attacker);
            Lease?.StopInteraction(Defender);
        }

        private void TickAlign()
        {
            // 🔴 2026-10-22：等**朝向收敛**再起播。
            //    配对几何的基准是 `ForwardOf(attacker)`（人优先读 look）；如果它还停在“走过来的方向”，
            //    第一帧挂接会按旧朝向摆位 —— 误差 ≈ 2·|stance|·sin(Δ/2)，Δ=83° 时约 0.98 m。
            //    这里每帧重申最终朝向（movement + look 双写）；收敛或超时才进 Starting。
            try
            {
                Attacker.SetMovementDirection(PairActionPose.ForwardFromYaw(_attackerYawDeg).AsVec2);
                Attacker.LookDirection = PairActionPose.ForwardFromYaw(_attackerYawDeg);
            }
            catch (Exception) { }
            if (Defender != null && AgentControlHelper.SafeIsActive(Defender))
            {
                try
                {
                    Defender.SetMovementDirection(PairActionPose.ForwardFromYaw(_defenderYawDeg).AsVec2);
                    Defender.LookDirection = PairActionPose.ForwardFromYaw(_defenderYawDeg);
                }
                catch (Exception) { }
            }

            float yawErr = 0f;
            try
            {
                yawErr = MathF.Abs(PairActionPose.Normalize180(
                    PairActionPose.YawDegOf(PairActionPose.ForwardOf(Attacker)) - _attackerYawDeg));
            }
            catch (Exception) { }

            bool aligned = yawErr <= AlignYawToleranceDeg;
            if (_phaseTimer >= AlignHoldSeconds && (aligned || _phaseTimer >= AlignMaxSeconds))
            {
                DebugLogger.Log($"[Pair] {Def.Id}：对齐完成（{_phaseTimer:F2}s，发起者朝向误差 {yawErr:F1}°"
                    + (_haveCamStart ? $"，镜头开始转（{CameraTurnSeconds:F2}s，转完再交还）" : "")
                    + "）"
                    + (aligned ? "" : " 🔴 未收敛 —— look 没跟上 SetMovementDirection，起播会跳"));
                Phase = PairPhase.Starting;
                _phaseTimer = 0f;
                Note = "starting";
            }
        }

        /// <summary>
        /// **镜头的独立时间轴**（2026-10-22）：和配对起播解耦 —— 起播只看身体对齐（~0.1s），
        /// 镜头按 <see cref="CameraTurnSeconds"/> 慢慢转；**转完才** `RequestHandBack` 交还。
        /// 扛着人时的前 0.8 秒仍是自定义相机（`follow_engine`），转完才回到引擎相机。
        /// </summary>
        private void TickCameraTurn(float dt)
        {
            if (!_haveCamStart)
                return;

            _camTurnElapsed += dt;
            _camLogTimer += dt;
            if (_camLogTimer >= 0.10f)
            {
                _camLogTimer = 0f;
                LogPairCam($"turn t={_camTurnElapsed:F2}/{CameraTurnSeconds:F2}");
            }

            float dur = MathF.Max(0.05f, CameraTurnSeconds);
            float t = MathF.Min(1f, _camTurnElapsed / dur);
            float s = t * t * (3f - 2f * t);          // smoothstep
            // 🔴 **口径差 180°**：`CameraService.SetLook` / `ArmYaw` 用的是引擎 `RotationZ` 口径，
            //    与 `YawDegOf`/`ForwardFromYaw` **符号相反**（实机日志：rigYaw=-33° 时 camFwd 的
            //    `YawDegOf` = +33°）⇒ 目标要取负，否则镜头转到镜像方向 = 看着角色正脸。
            float camTargetYaw = PairActionPose.Normalize180(-_attackerYawDeg);
            float dy = PairActionPose.Normalize180(camTargetYaw - _camStartYawDeg);
            float yaw = PairActionPose.Normalize180(_camStartYawDeg + dy * s);

            CameraService.SetLook(yaw, _camPitchDeg);
            Lease?.SetSavedLook(yaw, _camPitchDeg);   // 万一归还被打断，释放时也写回转到的角度

            if (t >= 1f)
            {
                if (Def != null && !Def.IsPersistent)
                {
                    // 一次性演出：镜头**继续持有**，演出期间跟攻击者背影（见 TickPerformanceCamera）
                    _camTurnDone = true;
                }
                else
                {
                    ReleasePairCamera();              // 持续性挂接：转完就交还（玩家要自由视角）
                }
            }
        }

        /// <summary>
        /// **演出镜头（通用，一次性动作）**（2026-10-22 用户裁定）：演出期间镜头保持在攻击者**背后**，
        /// 且朝向他**当前动画里的身体朝向**。
        ///
        /// 🔴 为什么需要它：有些 clip 会自己转身（`execution02` 的根骨 180°），而 `Agent.LookFrame`/
        ///    `Frame` **不跟动画根骨转** —— 只看逻辑朝向的相机会停在旧方向（看到正脸）。这里每帧读
        ///    **骨盆骨的世界前向**（含动画根旋转）来驱动相机 yaw；机位仍是 `follow_engine` 的默认背后视角。
        ///
        /// 持续性挂接（扛人）不走这条：玩家要自由视角（`TickCameraTurn` 转完就交还）。
        /// </summary>
        private void TickPerformanceCamera(float dt)
        {
            if (!_haveCamStart || !_camTurnDone || Def == null || Def.IsPersistent)
                return;
            if (Attacker == null || Attacker.Monster == null)
                return;
            if (!SpellCastInput.TryReadBoneWorldForward(Attacker, Attacker.Monster.PelvisBoneIndex, out Vec3 fwd))
                return;

            // ArmYaw 口径与 YawDegOf 符号相反（见 TickCameraTurn 里的注释）
            float target = PairActionPose.Normalize180(-PairActionPose.YawDegOf(fwd));

            if (!_camPerfYawInit)
            {
                _camPerfYawInit = true;
                // 从对齐转完那一刻的机位接上，避免第一帧跳
                _camPerfYawDeg = PairActionPose.Normalize180(CameraService.Machine.LookYaw);
            }

            // 🔴 低通：骨盆的局部旋转带着动作的左右拧/前倾，直接跟 = 镜头跟着身体抖。
            //    τ=0.15s 只跟整体转身；再加转速上限，180° 转身也不甩头。
            float k = 1f - (float)Math.Exp(-MathF.Max(1e-4f, dt) / PerfCamYawTau);
            float step = PairActionPose.Normalize180(target - _camPerfYawDeg) * k;
            float maxStep = PerfCamYawMaxRate * dt;
            if (MathF.Abs(step) > maxStep)
                step = MathF.Sign(step) * maxStep;
            _camPerfYawDeg = PairActionPose.Normalize180(_camPerfYawDeg + step);

            if (PerfCameraDebug)
            {
                _camPerfLogTimer += dt;
                if (_camPerfLogTimer >= 0.25f)
                {
                    _camPerfLogTimer = 0f;
                    Vec3 camPos = Vec3.Zero; bool haveCam = false;
                    try { if (Mission.Current != null) { camPos = Mission.Current.GetCameraFrame().origin; haveCam = true; } } catch (Exception) { }
                    float bodyYaw = 0f;
                    try { bodyYaw = PairActionPose.YawDegOf(PairActionPose.ForwardOf(Attacker)); } catch (Exception) { }
                    Vec3 vp = Vec3.Zero; try { vp = Attacker.VisualPosition; } catch (Exception) { }
                    DebugLogger.Log($"[PairCam] perf t={Elapsed:F2} raw={target:F0}° smooth={_camPerfYawDeg:F0}°"
                        + $" rate={(dt > 1e-4f ? step / dt : 0f):F0}°/s bodyLook={bodyYaw:F0}°"
                        + $" pelvis=({fwd.x:F2},{fwd.y:F2})"
                        + (haveCam ? $" cam=({camPos.x:F2},{camPos.y:F2},{camPos.z:F2})" : "")
                        + $" player=({Attacker.Position.x:F2},{Attacker.Position.y:F2},{Attacker.Position.z:F2})"
                        + $" visual=({vp.x:F2},{vp.y:F2},{vp.z:F2})");
                }
            }

            CameraService.SetLook(_camPerfYawDeg, _camPitchDeg);
            Lease?.SetSavedLook(_camPerfYawDeg, _camPitchDeg);
        }

        /// <summary>
        /// **交还相机**（2026-10-22）：停掉外部朝向驱动 → 走与钩索同款的 0.35 s 渐变归还
        /// （`RequestHandBack` 把臂长/FOV 滑回接管时引擎的值，滑完才真撒手；方向在撒手那一刻写回引擎）。
        /// 幂等：没接管 / 已经在归还 = 空操作。
        /// </summary>
        private void ReleasePairCamera()
        {
            if (!_haveCamStart)
                return;
            _haveCamStart = false;
            try
            {
                LogPairCam("release");
                if (CameraService.IsHeldBy(PairCameraOwner))
                {
                    CameraService.ReleaseLook();
                    CameraService.RequestHandBack(0.35f);
                    DebugLogger.Log($"[Pair] {Def.Id}：相机交还（0.35s 渐变，最终朝向 {_attackerYawDeg:F0}°）");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Pair] 相机交还异常：{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// **相机/角色位姿取证**（2026-10-22，用户要求）：在相机变换的每个节点记录
        /// 「相机位置/朝向」+「角色位置/朝向」+ 三个判据点积。
        ///
        ///   · `behindDot` = (相机 − 角色) 与 **角色身体朝向** 的点积：
        ///       负 = 相机在角色**后方**（正常，看到后脑）；正 = 在**前方**（看到正脸）。
        ///   · `lookDot` = 相机前向 与 **角色视线** 的点积：
        ///       +1 = 同向（正常）；−1 = 反向（相机看着角色）。
        ///   · `bodyLookDot` = 角色身体朝向 与 角色视线 的点积：
        ///       +1 = 一致；−1 = 身体和视线反了（`SetMovementDirection` / `LookDirection` 打架）。
        /// </summary>
        private void LogPairCam(string tag)
        {
            try
            {
                Vec3 playerPos = Attacker.Position;
                Vec3 look = Vec3.Zero, body = Vec3.Zero;
                try { look = Attacker.LookDirection; } catch (Exception) { }
                try { body = Attacker.Frame.rotation.f; } catch (Exception) { }

                Vec3 camPos = Vec3.Zero;
                bool haveCamPos = false;
                try
                {
                    if (Mission.Current != null)
                    {
                        camPos = Mission.Current.GetCameraFrame().origin;
                        haveCamPos = true;
                    }
                }
                catch (Exception) { }

                Vec3 camFwd = Vec3.Zero;
                bool haveCamFwd = false;
                try { haveCamFwd = CameraService.TryGetBasis(out camFwd, out _); } catch (Exception) { }
                if (!haveCamFwd)
                {
                    try { haveCamFwd = CameraLook.TryGet(out camFwd); } catch (Exception) { }
                }

                float behindDot = 0f, lookDot = 0f, bodyLookDot = 0f;
                Vec3 b = body; b.z = 0f;
                Vec3 l = look; l.z = 0f;
                Vec3 cf = camFwd; cf.z = 0f;
                if (b.LengthSquared > 1e-4f && l.LengthSquared > 1e-4f)
                    bodyLookDot = Vec3.DotProduct(b.NormalizedCopy(), l.NormalizedCopy());
                if (haveCamPos && b.LengthSquared > 1e-4f)
                {
                    Vec3 toCam = camPos - playerPos;
                    toCam.z = 0f;
                    if (toCam.LengthSquared > 1e-4f)
                        behindDot = Vec3.DotProduct(toCam.NormalizedCopy(), b.NormalizedCopy());
                }
                if (haveCamFwd && l.LengthSquared > 1e-4f && cf.LengthSquared > 1e-4f)
                    lookDot = Vec3.DotProduct(cf.NormalizedCopy(), l.NormalizedCopy());

                float engYaw = float.NaN;
                try
                {
                    if (!CameraLook.TryGetEngineAnglesRaw(out engYaw, out _))
                        engYaw = float.NaN;
                }
                catch (Exception) { }

                float rigYaw = float.NaN;
                try { rigYaw = CameraService.Machine.LookYaw; } catch (Exception) { }

                string camPosStr = haveCamPos ? $"({camPos.x:F2},{camPos.y:F2},{camPos.z:F2})" : "?";
                string camFwdStr = haveCamFwd ? $"({camFwd.x:F2},{camFwd.y:F2})" : "?";
                DebugLogger.Log($"[PairCam] {tag} | rigYaw={rigYaw:F0}° bodyYaw={_attackerYawDeg:F0}°"
                    + $" armTarget={PairActionPose.Normalize180(-_attackerYawDeg):F0}° engYaw={engYaw:F0}°"
                    + $" | camPos={camPosStr} camFwd={camFwdStr}"
                    + $" | playerPos=({playerPos.x:F2},{playerPos.y:F2},{playerPos.z:F2})"
                    + $" look=({look.x:F2},{look.y:F2}) body=({body.x:F2},{body.y:F2})"
                    + $" | behindDot={behindDot:F2} lookDot={lookDot:F2} bodyLookDot={bodyLookDot:F2}"
                    + (behindDot > 0.3f ? " 🔴 相机在角色前方" : "")
                    + (lookDot < -0.3f ? " 🔴 相机与角色反向" : "")
                    + (bodyLookDot < -0.3f ? " 🔴 身体与视线反向" : ""));
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[PairCam] {tag} 取证异常：{ex.GetType().Name}: {ex.Message}");
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

            LogPairCam("starting");

            // 轨道计时从演出那一帧开始（对齐/走路不算）
            _playTime = 0f;
            _soundFired = 0;
            _fxFired = 0;

            Phase = Def.IsPersistent ? PairPhase.Attached : PairPhase.Playing;
            _phaseTimer = 0f;
            Note = Def.IsPersistent ? "attached" : "playing";
            DebugLogger.Log($"[Pair] {Def.Id}：两侧同帧起播成功 → {Phase}（发起者控制权已归还）");
        }

        private void TickPlaying(float dt)
        {
            TickTracks(dt);

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
            // 🔴 演出超时从**开演**算起（`_playTime`），不是从 pair 创建算起：
            //    走位/对齐各有自己的超时，不该吃掉演出的预算。2026-10-23 实机：走位卡 6s ⇒ 演出只剩 ~3.9s
            //    就被掐断 ⇒ `end.policy=knockout` 没执行 ⇒ 受害者自己站起来（日志 `🔴 中止（timeout (10.0s)）`）。
            if (!keepAlive && _playTime > PlayTimeoutSeconds)
                Bail($"play timeout ({_playTime:F1}s)");
        }

        /// <summary>
        /// **音效 / 粒子轨道**（2026-10-23）：从演出（Playing）开始计时，到点触发一次。
        /// 数据 = `PairActions.csv` 的 `Sound_Track` / `Fx_Track`（`time@id`，`|` 分隔）。
        /// 位置 / 挂点 = **受害者的颈根骨**（`NeckRootBoneIndex`）—— 暗杀的发力点。
        /// </summary>
        private void TickTracks(float dt)
        {
            if (Def == null) return;
            _playTime += dt;

            if (Def.SoundTrack != null)
            {
                while (_soundFired < Def.SoundTrack.Count
                       && Def.SoundTrack[_soundFired].Time <= _playTime)
                {
                    FireSoundTrack(Def.SoundTrack[_soundFired].Id);
                    _soundFired++;
                }
            }

            if (Def.FxTrack != null)
            {
                while (_fxFired < Def.FxTrack.Count
                       && Def.FxTrack[_fxFired].Time <= _playTime)
                {
                    FireFxTrack(Def.FxTrack[_fxFired]);
                    _fxFired++;
                }
            }
        }

        private void FireSoundTrack(string id)
        {
            try
            {
                Vec3 at = ImpactPoint();
                SoundFx.Play3D(id, at);
                DebugLogger.Log($"[Pair] {Def.Id}：音效 '{id}' @ t={_playTime:F2}s "
                    + $"({at.x:F2},{at.y:F2},{at.z:F2})");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Pair] {Def.Id}：音效 '{id}' 失败：{ex.GetType().Name}");
            }
        }

        /// <summary>
        /// 运行期粒子覆盖：`custom.pair fx &lt;粒子名&gt;`。空 = 用 `PairActions.csv` 的 `Fx_Track`。
        /// 存在的理由 = 换一种血要重启游戏才生效（CSV 是启动期加载的），调观感时没法当场对比。
        /// </summary>
        public static string FxOverrideName;

        private void FireFxTrack(PairFxEvent ev)
        {
            if (ev == null) return;
            string id = ev.Particle;
            try
            {
                string fxName = string.IsNullOrEmpty(FxOverrideName) ? id : FxOverrideName;
                int pid = ParticleSystemManager.GetRuntimeIdByName(fxName);
                if (pid == -1)
                {
                    DebugLogger.Log($"[Pair] {Def.Id}：粒子 '{fxName}' 未注册（id=-1）");
                    return;
                }

                // 🔴 挂点写在粒子轨道里（`time=...;psys=...;bone=neck;offset=右:前:上`），每个双人动作自己定。
                AttachFxToVictimBone(pid, fxName, ev);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Pair] {Def.Id}：粒子 '{id}' 失败：{ex.GetType().Name} {ex.Message}");
            }
        }

        /// <summary>
        /// 把粒子挂到受害者的一根骨头上（2026-10-23）。
        /// **挂哪根 = `Fx_Track` 里的 `bone=`**（省 = `neck` 颈根）；
        /// **位置微调 = `offset=右:前:上`**（米，受害者自身坐标系）—— 骨点埋在身体里时把特效挪出来。
        /// 骨骼挂接是实机验证过能看见、能跟随的路（`custom.psys_head` 同款）。
        /// </summary>
        private void AttachFxToVictimBone(int pid, string fxName, PairFxEvent ev)
        {
            Agent victim = Defender;
            if (victim == null || !AgentControlHelper.SafeIsActive(victim))
                return;

            MBAgentVisuals visuals = victim.AgentVisuals;
            if (visuals == null || !visuals.IsValid())
                return;

            string note;
            if (!TryResolveFxBone(victim, ev != null ? ev.Bone : null, out sbyte bone, out note))
            {
                DebugLogger.Log($"[Pair] {Def?.Id}：粒子 '{fxName}' 没挂 —— {note}");
                return;
            }

            // 偏移：受害者自身系（右/前/上）→ 骨局部系。骨帧是"角色局部坐标"（不是世界），
            // 所以直接用骨 entitial 的朝向做一次反变换即可，不必碰世界坐标。
            Vec3 localOffset = Vec3.Zero;
            string offsetNote = "none";
            if (ev != null && ev.HasOffset)
            {
                try
                {
                    Skeleton skel = visuals.GetSkeleton();
                    if (skel != null)
                    {
                        MatrixFrame boneEntitial = skel.GetBoneEntitialFrameWithIndex(bone);
                        localOffset = boneEntitial.rotation.TransformToLocal(
                            new Vec3(ev.OffsetRight, ev.OffsetForward, ev.OffsetUp));
                        offsetNote = $"{ev.OffsetRight:F2}:{ev.OffsetForward:F2}:{ev.OffsetUp:F2}";
                    }
                    else
                    {
                        offsetNote = "no skeleton (offset skipped)";
                    }
                }
                catch (Exception ex)
                {
                    offsetNote = $"offset failed ({ex.GetType().Name})";
                }
            }

            MatrixFrame local = new MatrixFrame(Mat3.Identity, localOffset);
            visuals.CreateParticleSystemAttachedToBone(pid, bone, ref local);
            DebugLogger.Log($"[Pair] {Def?.Id}：粒子 '{fxName}' @ t={_playTime:F2}s（挂在 "
                + $"'{PairActionPose.Describe(victim)}' 骨 {bone} = {note}，偏移 {offsetNote}）");
        }

        /// <summary>
        /// `Fx_Track` 里的 `bone=` → 骨索引。名字取 `Monster` 的语义骨位（跨种族通用），也接受纯数字 = 原始骨索引。
        /// 返回 false = 名字不认识（不瞎猜，直接不挂并说明）。
        /// </summary>
        private static bool TryResolveFxBone(Agent victim, string spec, out sbyte bone, out string note)
        {
            bone = -1;
            string key = string.IsNullOrWhiteSpace(spec) ? "neck" : spec.Trim().ToLowerInvariant();

            if (sbyte.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte raw))
            {
                bone = raw;
                note = "raw index";
                return true;
            }

            Monster m = victim != null ? victim.Monster : null;
            if (m == null)
            {
                note = "victim has no monster";
                return false;
            }

            switch (key)
            {
                case "neck": bone = m.NeckRootBoneIndex; note = "NeckRootBoneIndex"; break;
                case "head": bone = m.HeadLookDirectionBoneIndex; note = "HeadLookDirectionBoneIndex"; break;
                case "chest":
                case "thorax": bone = m.ThoraxLookDirectionBoneIndex; note = "ThoraxLookDirectionBoneIndex"; break;
                case "spine": bone = m.SpineUpperBoneIndex; note = "SpineUpperBoneIndex"; break;
                case "waist": bone = m.SpineLowerBoneIndex; note = "SpineLowerBoneIndex"; break;
                case "pelvis": bone = m.PelvisBoneIndex; note = "PelvisBoneIndex"; break;
                case "arm":
                case "shoulder": bone = m.RightUpperArmBoneIndex; note = "RightUpperArmBoneIndex"; break;
                case "hand": bone = m.MainHandBoneIndex; note = "MainHandBoneIndex"; break;
                default:
                    note = $"unknown bone '{key}' in Fx_Track (use neck/head/chest/spine/waist/pelvis/arm/hand or a raw index)";
                    return false;
            }

            if (bone < 0)
            {
                note += " (this monster has no such bone)";
                return false;
            }
            return true;
        }

        /// <summary>发力点 = 受害者颈根骨的世界位置；读不到就退到身体上方 1.5 m。</summary>
        private Vec3 ImpactPoint()
        {
            Agent victim = Defender;
            if (victim == null)
                return Attacker != null ? Attacker.Position : Vec3.Zero;
            if (victim.Monster != null
                && SpellCastInput.TryReadBoneWorld(victim, victim.Monster.NeckRootBoneIndex, 5f,
                    out Vec3 p, out _))
                return p;
            return victim.Position + Vec3.Up * 1.5f;
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
            ReleasePairCamera();
            // 🔴 2026-10-22：**结束提交（X/Y）**必须在 Release **之前**做 —— 那时 clip 还停在末帧，
            //    骨盆世界位置 = agent + 根轨位移 + 姿态；提交后同一帧根轨消失、agent 移动同样的量 ⇒ 视觉连续。
            ApplyEndCommit();
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
            ReleasePairCamera();
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

        /// <summary>
        /// **结束提交（X/Y）**（2026-10-22）：一次性动作若用**纯视觉根轨**（`Motion=none`，如伏击 Root 版），
        /// 释放 clip 时根轨消失 = 视觉弹回。这里在 Release **之前**把 agent 的 X/Y 设成**骨盆世界位置**
        /// （骨盆世界 = agent + 根轨位移 + 姿态）⇒ 同一帧根轨消失 + agent 移动同样的量 = 视觉连续。
        /// 🔴 只提交 X/Y；Z 永不提交（引擎按地形重算）。朝向暂不提交（伏击演完即死，ragdoll 会盖住）。
        /// </summary>
        private void ApplyEndCommit()
        {
            if (Def == null || Def.IsPersistent || Def.Motion != PairMotionKind.None)
                return;
            CommitXyFromPelvis(Attacker);
            CommitXyFromPelvis(Defender);
        }

        private static void CommitXyFromPelvis(Agent a)
        {
            if (a == null || !AgentControlHelper.SafeIsActive(a) || a.Monster == null)
                return;
            try
            {
                if (!SpellCastInput.TryReadBoneWorld(a, a.Monster.PelvisBoneIndex, 10f, out Vec3 p, out _))
                    return;
                Vec3 pos = a.Position;
                float dx = p.x - pos.x, dy = p.y - pos.y;
                if (dx * dx + dy * dy < 1e-4f)
                    return;   // 没有位移（原地动作）——不提交
                pos.x = p.x;
                pos.y = p.y;
                a.TeleportToPosition(pos);
                DebugLogger.Log($"[Pair] 结束提交 X/Y：'{PairActionPose.Describe(a)}' 移动 ({dx:F2},{dy:F2}) m（根轨 → 逻辑位置）");
            }
            catch (Exception) { }
        }

        /// <summary>
        /// **古老的重伤/击晕落地**（2026-10-22 用户裁定）：暗杀/处决**不杀死**，走 `KnockoutFlow.Resolve`
        /// 的旧管线 —— 袭击记账 + Knockout 罪 + `event_agent_knocked_out`（脑侧 `StayAction(isKnockout:true)`
        /// 永久占位，可被搜身/俘获，直到 `KnockoutFlow.StandUp`）。
        ///
        /// 🔴 **2026-10-23 修正（用户指出“动画和逻辑位置长时间不一致”）**：
        ///    `ambushed01` 的末帧还带着 **2.16m 根轨**；只 keep 的话，视觉一直停在“agent + 根轨”的位置，
        ///    而 agent 已被结束提交挪到骨盆处 ⇒ 两者差 2.16m。这里改成：
        ///    ① 先由 `ApplyEndCommit` 把 agent 的 X/Y 提交到**骨盆世界位置**（= 画面倒地处）；
        ///    ② 再把受害者切到**我们自己的趴地循环** `binded_lay_loop`（`act_grapple_bound_cycle`）——
        ///       2.0s cyclic、**原地无位移**、循环保持；视觉与逻辑重合。
        ///    ❌ 不用原版 `act_death_fall_front`：它是“站→倒”的一次性动画，且带 **1.5m displacement**。
        /// </summary>
        private void KnockoutDefender()
        {
            if (Defender == null || !AgentControlHelper.SafeIsActive(Defender))
                return;

            // ① 从“伏击 clip 的倒地末帧（带 2.16m 根轨）”切到**我们自己的趴地循环**：
            //    `binded_lay_loop`（cyclic、原地、无位移）。循环件必须 blendOutPeriodToNoAnim=0，
            //    否则末尾会淡出成“无动画”一闪一闪（见 Knowledge/骑砍2动画通道与上下半身分层.md）。
            try
            {
                AgentControlHelper.ForcePlayAction(Defender, "act_grapple_bound_cycle",
                    startProgress: 0f, blendIn: 0.2f, blendOutPeriodToNoAnim: 0f);
            }
            catch (Exception) { }

            // ② 旧击晕管线的记账/罪/事件（不重播倒地动画）
            var r = new KnockoutFlow.RollResult
            {
                Success = true,
                SuccessRate = 1f,
                Roll = 1f,
                Threshold = 0f,
                IsChild = false,
            };
            KnockoutFlow.Resolve(Attacker, Defender, r, playFall: false);
            DebugLogger.Log($"[Pair] {Def.Id}：end.policy=knockout —— "
                + $"'{PairActionPose.Describe(Defender)}' 进入旧击晕/重伤占位（不杀，已切被绑趴地循环）");
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
                case PairEndPolicy.Knockout:
                    KnockoutDefender();
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
