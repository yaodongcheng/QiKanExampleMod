using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Flight
{
    /// <summary>
    /// 玩家飞行（2026-09-21）—— 状态机主体。
    ///
    /// 一句话：**封住原生移动 → 我们用镜头方向开木板 → 动画由我们直接指定**。
    ///
    /// 挂载：<c>MySubModule.OnMissionBehaviorInitialize</c>，必须**置于玩法闸门之前**
    /// （<c>Settings.Instance.IsInteractionDisabled()</c>）—— 战场正好被那道闸门拦在外面，
    /// 挂在后面 = 战场里飞不起来。
    ///
    /// 四个状态：
    /// <code>
    /// 地面 ──长按空格──▶ 起飞 ──升到悬停高度──▶ 空中 ──长按空格 / 贴地不动──▶ 落地 ──▶ 地面
    /// </code>
    ///
    /// 🔴 三条踩过的坑（别重犯）：
    ///   1. **不要碰玩家的 Controller 之外的东西** —— 冻结走 <c>V.SetPlayerControlFrozen</c>（项目既有封装），
    ///      它切 Controller=AI：角色原地待机、跳/走/攻击全死、**镜头照常跟着玩家**。
    ///   2. **动画不要每帧重设** —— 每帧调 <c>SetActionChannel</c> 会把动画卡在第 0 帧。
    ///      只在状态真的变了才设一次，靠 <c>blendInPeriod</c> 做交叉淡化。
    ///   3. **载具只能逐帧瞬移** —— 别试图用物理速度驱动它（实测完全不托人）。
    /// </summary>
    public class PlayerFlightBehavior : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        private enum Phase
        {
            Grounded,   // 不飞（原版状态）
            Takeoff,    // 抬升中（播起飞动画）
            Airborne,   // 空中自由飞（悬停 / 巡航 / 冲刺）
            Landing     // 落地中（播落地动画）
        }

        private readonly CarrierBoard _board = new CarrierBoard();

        private Phase _phase = Phase.Grounded;
        private Vec3 _velocity = Vec3.Zero;
        private float _hoverOriginZ;        // 悬停时「板原点」的目标高度
        private float _lowClampTimer;       // 贴着最低点停了多久（用于自动落地）
        private float _landTimer;
        private bool _controlFrozen;
        private bool _wasCrouching;
        private string _currentAction;
        private bool _warnedBadAction;
        private float _clock;               // 累计时间（只给动画核对用）
        private float _actionSetAt;         // 上次设置动作的时刻
        private int _pitchBand;             // 俯仰档：+1 爬升 / 0 水平 / −1 俯冲（带迟滞）

        /// <summary>飞行中（含起飞 / 落地）。</summary>
        public bool IsFlying => _phase != Phase.Grounded;

        // ─────────────────────────── 主循环 ───────────────────────────

        public override void OnMissionTick(float dt)
        {
            Mission mission = Mission.Current;
            if (mission == null)
            {
                // 场景卸载：把状态清干净，别把冻结带出场景
                if (_phase != Phase.Grounded)
                    AbortFlight();
                return;
            }

            Agent main = Agent.Main;
            if (main == null || !AgentControlHelper.SafeIsActive(main))
            {
                // 玩家 agent 没了（阵亡 / 换场景）—— 立刻收摊，否则冻结会泄漏
                if (_phase != Phase.Grounded)
                {
                    DebugLogger.Log("[Flight] 玩家 agent 失效，强制退出飞行");
                    AbortFlight();
                }
                FlightInput.Reset();
                return;
            }

            _clock += dt;
            FlightInput.Tick(dt);

            try
            {
                switch (_phase)
                {
                    case Phase.Grounded: TickGrounded(main); break;
                    case Phase.Takeoff: TickTakeoff(main, dt); break;
                    case Phase.Airborne: TickAirborne(mission, main, dt); break;
                    case Phase.Landing: TickLanding(main, dt); break;
                }
            }
            catch (Exception ex)
            {
                // 一次异常即收摊：防每帧刷屏，防冻结状态卡死玩家
                DebugLogger.Log($"[Flight] tick 异常，已强制退出飞行: {ex}");
                AbortFlight();
            }
        }

        /// <summary>场景结束时兜底回收（ESC 直接退场景也不泄漏）。</summary>
        public override void OnRemoveBehavior()
        {
            try
            {
                AbortFlight();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Flight] OnRemoveBehavior 清理异常: {ex.Message}");
            }
            base.OnRemoveBehavior();
        }

        // ─────────────────────────── 各状态 ───────────────────────────

        private void TickGrounded(Agent main)
        {
            if (_controlFrozen)
                RestoreControl(main);       // 兜底：不该出现，出现就是状态机漏了

            // 🔴 骑马时不许起飞：飞行期间控制权被切走，坐骑状态会乱（方案 §九 风险 4，本轮行为未定义）
            if (main.HasMount)
                return;

            if (FlightInput.ConsumeSpaceLongPress())
                BeginTakeoff(main);
        }

        private void TickTakeoff(Agent main, float dt)
        {
            Vec3 o = _board.Origin;
            float nz = Math.Min(o.z + FlightTuning.VerticalRate * dt, _hoverOriginZ);
            _board.MoveTo(new Vec3(o.x, o.y, nz));

            SetAction(main, FlightTuning.ActTakeoff);

            if (nz >= _hoverOriginZ - 0.01f)
            {
                _phase = Phase.Airborne;
                _velocity = Vec3.Zero;
                _lowClampTimer = 0f;
                SetAction(main, FlightTuning.ActIdle);
            }
        }

        private void TickAirborne(Mission mission, Agent main, float dt)
        {
            // ① 落地手势（同一个空格长按，管进出）
            if (FlightInput.ConsumeSpaceLongPress())
            {
                BeginLanding(main);
                return;
            }

            // ② 方向 = 镜头方向（含俯仰：抬头看天 + W 就是爬升）
            MatrixFrame cam = mission.GetCameraFrame();
            Vec3 forward = cam.rotation.f;
            Vec3 right = cam.rotation.s;

            Vec2 axis = FlightInput.MoveAxis;
            Vec3 dir = forward * axis.y + right * axis.x;
            if (dir.LengthSquared > 0.0001f)
                dir = dir.NormalizedCopy();
            else
                dir = Vec3.Zero;

            // ③ 速度朝目标趋近（限加速度 ⇒ 手感上有惯性）
            float targetSpeed = FlightInput.BoostHeld ? FlightTuning.BoostSpeed : FlightTuning.CruiseSpeed;
            Vec3 delta = dir * targetSpeed - _velocity;
            float maxDelta = FlightTuning.Accel * dt;
            if (delta.Length > maxDelta)
                delta = delta.NormalizedCopy() * maxDelta;
            _velocity += delta;

            // ④ 逐帧瞬移载具（唯一能载人的移动方式）
            Vec3 next = _board.Origin + _velocity * dt;

            // ⑤ 地形约束：不许钻进山里 / 飞出上限
            float floor = GetGroundZ(mission.Scene, next) + FlightTuning.MinClearance;
            if (next.z < floor)
            {
                next.z = floor;
                if (_velocity.z < 0f) _velocity.z = 0f;
            }
            if (next.z > FlightTuning.MaxAltitude)
            {
                next.z = FlightTuning.MaxAltitude;
                if (_velocity.z > 0f) _velocity.z = 0f;
            }

            _board.MoveTo(next);

            // ⑥ 贴着最低点不动 = 玩家想下来 ⇒ 自动落地
            if (next.z <= floor + 0.05f)
            {
                _lowClampTimer += dt;
                if (_lowClampTimer >= FlightTuning.AutoLandSeconds)
                {
                    BeginLanding(main);
                    return;
                }
            }
            else
            {
                _lowClampTimer = 0f;
            }

            // ⑦ 姿态：按「冲刺 > 俯仰 > 速度」挑一条（状态没变时 SetAction 内部会跳过）
            SetAction(main, PickAirAction(forward));

            // ⑧ 身体朝向跟着飞的方向（俯仰由动画表现 —— 引擎的 agent 转不了俯仰）
            TurnBody(main, dir);

            if (FlightTuning.VerboseLog)
            {
                DebugLogger.Log(string.Format(
                    "[Flight] air v=({0:F1},{1:F1},{2:F1}) |v|={3:F1} pos=({4:F1},{5:F1},{6:F1}) anim={7}",
                    _velocity.x, _velocity.y, _velocity.z, _velocity.Length,
                    _board.Origin.x, _board.Origin.y, _board.Origin.z, _currentAction));
            }
        }

        private void TickLanding(Agent main, float dt)
        {
            SetAction(main, FlightTuning.ActLand);

            float groundOriginZ = GetGroundZ(Mission.Current.Scene, _board.Origin) - FlightTuning.CarrierTopLocalZ;
            Vec3 o = _board.Origin;
            float nz = Math.Max(groundOriginZ, o.z - FlightTuning.LandRate * dt);
            _board.MoveTo(new Vec3(o.x, o.y, nz));

            _landTimer += dt;
            if (nz <= groundOriginZ + 0.02f || _landTimer > 3f)
                FinishFlight(main);
        }

        // ─────────────────────────── 进出 ───────────────────────────

        private void BeginTakeoff(Agent main)
        {
            Scene scene = Mission.Current?.Scene;
            if (scene == null)
                return;

            // 生成前先量地面 —— 板生成之后，地面查询可能查到板自己身上
            float groundZ = GetGroundZ(scene, main.Position);

            // 板生成在脚底（🔴 这是全方案唯一的未验证点，见实施方案 §六 阶段 0 E1）
            if (!_board.Spawn(scene, main.Position))
                return;

            FreezeControl(main);

            _hoverOriginZ = groundZ + FlightTuning.HoverAltitude - FlightTuning.CarrierTopLocalZ;
            _phase = Phase.Takeoff;
            _velocity = Vec3.Zero;
            _landTimer = 0f;
            _lowClampTimer = 0f;
            SetAction(main, FlightTuning.ActTakeoff);

            DebugLogger.Log($"[Flight] 起飞: groundZ={groundZ:F2} hoverZ={_hoverOriginZ:F2} {_board.Describe()}");
        }

        private void BeginLanding(Agent main)
        {
            _phase = Phase.Landing;
            _velocity = Vec3.Zero;
            _landTimer = 0f;
            SetAction(main, FlightTuning.ActLand);
        }

        private void FinishFlight(Agent main)
        {
            _board.Remove();

            // 把动作通道还回去（让引擎的走跑系统重新接管）
            try
            {
                main.SetActionChannel(0, ActionIndexCache.act_none, ignorePriority: false, blendInPeriod: 0.3f);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Flight] 归还动作通道异常（通常无害）: {ex.Message}");
            }
            _currentAction = null;

            RestoreControl(main);

            _phase = Phase.Grounded;
            _velocity = Vec3.Zero;
            _lowClampTimer = 0f;
            _landTimer = 0f;
            FlightInput.Reset();

            DebugLogger.Log("[Flight] 已落地，控制权与动作通道均已归还");
        }

        /// <summary>异常 / 场景结束时的强制收摊（幂等）。</summary>
        private void AbortFlight()
        {
            if (_phase == Phase.Grounded && !_board.IsSpawned && !_controlFrozen)
                return;

            _board.Remove();

            Agent main = Agent.Main;
            if (main != null && AgentControlHelper.SafeIsActive(main))
            {
                try
                {
                    main.SetActionChannel(0, ActionIndexCache.act_none, ignorePriority: false, blendInPeriod: 0.2f);
                }
                catch { /* 收摊阶段尽力而为 */ }
                RestoreControl(main);
            }
            else
            {
                _controlFrozen = false;
                _wasCrouching = false;
            }

            _phase = Phase.Grounded;
            _velocity = Vec3.Zero;
            _currentAction = null;
            _lowClampTimer = 0f;
            _landTimer = 0f;
            FlightInput.Reset();
        }

        // ─────────────────────────── 控制权 ───────────────────────────

        // 照 Interaction/InteractionMissionView.cs 的既有范式：
        // 幂等标志 → 进时保存蹲姿再切 AI（切 AI 会把姿态重置成站立）→ 出时先解脚本蹲姿再还控制。
        private void FreezeControl(Agent agent)
        {
            if (_controlFrozen || agent == null)
                return;

            _controlFrozen = true;
            _wasCrouching = agent.CrouchMode;
            V.SetPlayerControlFrozen(agent, true);
            if (_wasCrouching)
                agent.SetCrouchMode(true);
        }

        private void RestoreControl(Agent agent)
        {
            if (!_controlFrozen || agent == null)
                return;

            _controlFrozen = false;
            try
            {
                if (_wasCrouching)
                {
                    agent.SetCrouchMode(false);
                    _wasCrouching = false;
                }
                V.SetPlayerControlFrozen(agent, false);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Flight] 归还控制权异常: {ex.Message}");
            }
        }

        // ─────────────────────────── 动画 ───────────────────────────

        /// <summary>
        /// 切飞行动作。
        ///
        /// 两条节奏规则：
        ///   · **状态变了 → 立刻设**（新的动作名与当前不同，直接落到下面设）。
        ///   · **状态没变 → 不每帧重设**（每帧重设会把动画卡在第 0 帧）；
        ///     但每隔 <see cref="FlightTuning.ActionRecheckSeconds"/> **核对一次**有没有被引擎抢回去，
        ///     抢走了才重设 —— 0 号通道是引擎 locomotion 系统也有权写的。
        /// </summary>
        private void SetAction(Agent agent, string actionName)
        {
            if (string.IsNullOrEmpty(actionName) || agent == null)
                return;

            ActionIndexCache idx = ActionIndexCache.Create(actionName);
            if (idx.Index < 0)
            {
                // 🔴 静默失败点：动作名没声明（action_types.xml）或没绑定 clip（action_sets.xml）时
                //    引擎不给任何报错，只是播不出来。这里替它报一次。
                if (!_warnedBadAction)
                {
                    _warnedBadAction = true;
                    DebugLogger.Log($"[Flight] 动作 '{actionName}' 解析为 act_none —— 检查内容包的 action_types.xml / action_sets.xml（写错不报错，只是不播）");
                }
                return;
            }

            if (_currentAction == actionName)
            {
                // 状态没变：只有在核对窗口到了、且发现动画**已经不在我们这条上**时才重设
                if (_clock - _actionSetAt < FlightTuning.ActionRecheckSeconds)
                    return;
                if (IsActionStillOurs(agent, idx))
                {
                    _actionSetAt = _clock;      // 一切正常，顺延下一次核对
                    return;
                }
                if (FlightTuning.VerboseLog)
                    DebugLogger.Log($"[Flight] 动画 '{actionName}' 被引擎抢走了，重设");
            }

            _currentAction = actionName;
            _actionSetAt = _clock;
            try
            {
                agent.SetActionChannel(0, idx, ignorePriority: true, blendInPeriod: FlightTuning.AnimBlendIn);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Flight] 切动作 '{actionName}' 异常: {ex.Message}");
            }
        }

        private static bool IsActionStillOurs(Agent agent, ActionIndexCache idx)
        {
            try
            {
                return agent.GetCurrentAction(0) == idx;
            }
            catch
            {
                return true;      // 查不到就当正常，别因为查询失败把动画不停重置
            }
        }

        /// <summary>
        /// 空中姿态选择，优先级：**冲刺 &gt; 俯仰 &gt; 速度**。
        ///
        /// · 完全悬停不动 → 永远是悬浮待机（**不跟镜头翻** —— 站着发愣时身体突然头朝下会很怪）
        /// · 一动起来，姿态就跟镜头的俯仰走：抬头爬升姿态 / 低头俯冲姿态
        /// · 按 Shift → 超人趴姿，俯仰让位
        ///
        /// 🔴 为什么要靠动画表现俯仰：引擎的 agent **只能绕 Z 轴转**，转不了俯仰。
        ///    所以「抬头飞」这件事只能由我们挑一条抬头姿态的动画来做。
        /// </summary>
        private string PickAirAction(Vec3 camForward)
        {
            bool moving = FlightInput.HasMoveInput || _velocity.LengthSquared > 1f;
            if (!moving)
            {
                _pitchBand = 0;             // 悬停不动时把档位归零，下次一动重新判
                return FlightTuning.ActIdle;
            }

            if (FlightInput.BoostHeld)
                return FlightTuning.ActBoost;

            // 🔴 带迟滞的俯仰档：进用大阈值、出用小阈值。
            //    实测姿态之间是 120°~180° 的大翻转，单阈值下镜头停在阈值附近会让动画来回翻。
            float pitch = camForward.z;     // 相机前方向的竖直分量 = 俯仰
            float enter = FlightTuning.PitchThreshold;
            float exit = FlightTuning.PitchExitThreshold;

            if (_pitchBand == 0)
            {
                if (pitch > enter) _pitchBand = 1;
                else if (pitch < -enter) _pitchBand = -1;
            }
            else if (_pitchBand > 0)
            {
                if (pitch < exit) _pitchBand = 0;
            }
            else
            {
                if (pitch > -exit) _pitchBand = 0;
            }

            if (_pitchBand > 0) return FlightTuning.ActClimb;
            if (_pitchBand < 0) return FlightTuning.ActDive;
            return FlightTuning.ActCruise;
        }

        private static void TurnBody(Agent agent, Vec3 dir)        {
            Vec3 flat = new Vec3(dir.x, dir.y, 0f);
            if (flat.LengthSquared < 0.0001f)
                return;
            try
            {
                agent.LookDirection = flat.NormalizedCopy();
            }
            catch
            {
                // 朝向只是观感，失败不该拦住飞行
            }
        }

        // ─────────────────────────── 地形 ───────────────────────────

        /// <summary>取地表高度。只用地形，不查物理体 —— 免得查到我们自己的板。</summary>
        private static float GetGroundZ(Scene scene, Vec3 pos)
        {
            if (scene == null)
                return pos.z;
            try
            {
                return scene.GetTerrainHeight(new Vec2(pos.x, pos.y), true);
            }
            catch
            {
                return pos.z;
            }
        }

        // ─────────────────────────── 给控制台用的手动手控 ───────────────────────────

        /// <summary>控制台强制起飞（绕过长按）。返回一句英文回执。</summary>
        public string ForceStart()
        {
            if (_phase != Phase.Grounded)
                return "already flying";
            Agent main = Agent.Main;
            if (main == null || !AgentControlHelper.SafeIsActive(main))
                return "no player agent";
            BeginTakeoff(main);
            return _phase == Phase.Takeoff ? "takeoff started" : "takeoff failed (carrier spawn failed?)";
        }

        /// <summary>控制台强制落地。</summary>
        public string ForceStop()
        {
            if (_phase == Phase.Grounded)
                return "not flying";
            Agent main = Agent.Main;
            if (main == null || !AgentControlHelper.SafeIsActive(main))
            {
                AbortFlight();
                return "aborted (no player agent)";
            }
            BeginLanding(main);
            return "landing";
        }

        public string Status()
        {
            return string.Format("phase={0} frozen={1} anim={2} v={3:F1} {4}",
                _phase, _controlFrozen, _currentAction ?? "-", _velocity.Length, _board.Describe());
        }
    }
}
