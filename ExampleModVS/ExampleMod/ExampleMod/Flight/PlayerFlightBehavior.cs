using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs.Flight
{
    /// <summary>
    /// 玩家飞行（2026-09-21）—— 状态机主体。
    ///
    /// 一句话：**板托着人走，方向由镜头定，动画我们直接指定**。
    ///
    /// 挂载：<c>MySubModule.OnMissionBehaviorInitialize</c>，必须**置于玩法闸门之前**
    /// （<c>Settings.Instance.IsInteractionDisabled()</c>）—— 战场正好被那道闸门拦在外面，
    /// 挂在后面 = 战场里飞不起来。
    ///
    /// 四个状态：
    /// <code>
    /// 地面 ──长按空格──▶ 起飞 ──升到悬停高度──▶ 空中 ──长按空格──▶ 落地 ──▶ 地面
    /// </code>
    ///
    /// 🔴🔴 **移动逻辑必须保持"读回真实坐标 + 加增量 + 写回"这一种形态**（2026-09-21 血泪教训）：
    ///     它是**开环**的，没有第二个人碰那块板，任何状态错了都不会被放大。
    ///     我曾一次堆了五层（加速趋近 / 地形夹取 / 高度上限 / 单帧钳 / 安全绳），
    ///     结果两个回路（板的位置、玩家的位置）互相耦合出了正反馈，花了一整天才排干净。
    ///     **加任何一层之前先问：它会不会和别的回路耦合？**
    ///
    /// 🔴 本轮**不冻结玩家**（曾用 Controller=AI，那是"把玩家交给引擎 AI 开"，会让角色自己乱走）。
    ///     代价：玩家按 WASD 会自己走下木板 —— 所以实验期用**小键盘 8/2/4/6** 控制飞行。
    ///     以后要接回 WASD，必须先把"冻结"这一层单独加回来并单独验。
    ///
    /// 🔴 载具只能逐帧瞬移（<c>SetFrame</c>）；用物理速度驱动 = 完全不托人（实测）。
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
        private string _currentAction;
        private bool _warnedBadAction;
        private float _clock;               // 累计时间（只给动画核对用）
        private float _actionSetAt;         // 上次设置动作的时刻
        private float _statusTimer;         // 状态行的节流计时
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
            WatchPlayerDisplacement(main, dt);

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
            GetCameraBasis(out Vec3 forward, out Vec3 right);

            Vec2 axis = FlightInput.MoveAxis;
            Vec3 dir = forward * axis.y + right * axis.x;
            if (dir.LengthSquared > 0.0001f)
                dir = dir.NormalizedCopy();
            else
                dir = Vec3.Zero;

            // (3) 速度 = 恒定值，**不做加速趋近**（对齐已实测丝滑的那套：那边就是恒定速度）
            float targetSpeed = FlightInput.BoostHeld ? FlightTuning.BoostSpeed : FlightTuning.CruiseSpeed;
            _velocity = dir * targetSpeed;

            // (4) 逐帧瞬移载具 —— 就是 FlySpike 那三行，一个夹取都不加。
            //     地形夹取 / 高度上限 / 单帧上限 **全部删掉**：
            //     它们是"我猜的保险"，实测只会制造新问题（160 米上限当场把人卡死过）。
            _board.MoveBy(_velocity * dt);

            // ⑦ 姿态：按「冲刺 > 俯仰 > 速度」挑一条（状态没变时 SetAction 内部会跳过）
            SetAction(main, PickAirAction(forward));

            // ⑧ 身体朝向跟着飞的方向（俯仰由动画表现 —— 引擎的 agent 转不了俯仰）
            TurnBody(main, dir);

            // ⑨ 每 0.5 秒打一组诊断 —— 板就算隐藏了，也能靠数字确认「人在不在板上、输入有没有读到」
            _statusTimer += dt;
            if (_statusTimer >= 0.5f)
            {
                _statusTimer = 0f;
                LogDiag(mission, main);
            }

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
            if (_phase == Phase.Grounded && !_board.IsSpawned)
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
            }
            _phase = Phase.Grounded;
            _velocity = Vec3.Zero;
            _currentAction = null;
            _lowClampTimer = 0f;
            _landTimer = 0f;
            FlightInput.Reset();
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
        /// 取「镜头看向哪里」—— 返回前向与右向两个基向量。
        ///
        /// 🔴🔴 **不要用 `Mission.GetCameraFrame().rotation.f`**（2026-09-21 实机踩）：
        ///     玩家明明平视前方，取出来却是 `(0.00, 0.29, 0.96)` —— 几乎垂直朝上。
        ///     也就是说相机帧的基向量排列和 `Mat3.Identity` 那套**不是一个约定**，取到的是上方向。
        ///     症状：按 W 不往前飞，一路往天上窜（实测窜到 160 米天花板）。
        ///
        ///     正确写法 = **照抄引擎自己**（`MissionMainAgentController.LookTick`）：
        ///     `Mat3.Identity` 依次绕 Up / Side 转 bearing / elevation，取 `.f` 就是视线。
        /// </summary>
        private static void GetCameraBasis(out Vec3 forward, out Vec3 right)
        {
            forward = Vec3.Zero;
            right = Vec3.Zero;

            // 主路：引擎自己的算法（MissionScreen 的相机角度）
            try
            {
                if (ScreenManager.TopScreen is MissionScreen ms)
                {
                    Mat3 m = Mat3.Identity;
                    m.RotateAboutUp(ms.CameraBearing);
                    m.RotateAboutSide(ms.CameraElevation);
                    if (m.f.LengthSquared > 0.0001f)
                    {
                        forward = m.f.NormalizedCopy();
                        right = m.s.NormalizedCopy();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Flight] 取相机角度异常，回退到相机帧: {ex.Message}");
            }

            // 兜底：至少别让飞行停摆（方向可能不对，但不会崩）
            try
            {
                MatrixFrame cam = Mission.Current.GetCameraFrame();
                forward = cam.rotation.f.NormalizedCopy();
                right = cam.rotation.s.NormalizedCopy();
            }
            catch { /* 全失败就留零向量，本帧不产生推力 */ }
        }

        private Vec3 _prevPlayerPos;
        private bool _hasPrevPos;

        /// <summary>
        /// 🔴 **位移监视器**：谁在动玩家，当场抓出来。
        ///
        /// 为什么要有它（2026-09-21）：排查"板飞得忽快忽慢"时连着绕了好几轮，
        /// 每次都是我猜一个原因就加一个补丁，越加越乱。真正的事实是
        /// **玩家被外部挪走了**（实测被挪到离板 41 米外），但当时没有监视器，
        /// 只能靠事后猜。它唯一的作用是**让根因自己报出来**，不做任何修正。
        ///
        /// 判据：玩家本帧位移明显超过「板最快能带出来的量」= 有第三方在动他。
        /// 触发时把现场一起打出来（控制权 / 编队 / AI 状态 / 两边坐标），一次日志就够定位。
        /// </summary>
        private void WatchPlayerDisplacement(Agent main, float dt)
        {
            Vec3 p = main.Position;
            if (!_hasPrevPos)
            {
                _prevPlayerPos = p;
                _hasPrevPos = true;
                return;
            }

            Vec3 playerStep = p - _prevPlayerPos;
            _prevPlayerPos = p;

            if (_phase == Phase.Grounded || dt <= 0f)
                return;

            // 板最快也就 BoostSpeed；再加点容忍量。超过就是别人在动他。
            float allowed = (FlightTuning.BoostSpeed + 2f) * dt + 0.05f;
            if (playerStep.Length <= allowed)
                return;

            string formation = main.Formation != null
                ? $"有(idx={main.Formation.FormationIndex})"
                : "无";

            DebugLogger.Log(string.Format(
                "[Flight-Watch] 🔴 玩家被外部挪动：本帧 {0:F2} 米（板极限 {1:F2}）| player=({2:F2},{3:F2},{4:F2}) board=({5:F2},{6:F2},{7:F2}) | ctrl={8} 编队={9} 骑乘={10} 场景={11}",
                playerStep.Length, allowed,
                p.x, p.y, p.z, _board.Origin.x, _board.Origin.y, _board.Origin.z,
                main.Controller, formation, main.HasMount,
                Mission.Current != null ? Mission.Current.Mode.ToString() : "?"));
        }

        /// <summary>
        /// 每 0.5 秒一组诊断。一次把排查"飞不动 / 摔下来"要看的量全打出来：
        /// **控制状态 · 玩家坐标 · 键盘原始输入 · 相机朝向 · 木板位置 · 木板速度**。
        /// 排查完可以整块删掉（只在 _statusTimer 里调用）。
        /// </summary>
        private void LogDiag(Mission mission, Agent main)
        {
            Vec3 p = main.Position;
            Vec3 b = _board.Origin;
            Vec3 cf = Vec3.Zero, cu = Vec3.Zero;
            try
            {
                MatrixFrame camFrame = mission.GetCameraFrame();
                cf = camFrame.rotation.f;
                cu = camFrame.rotation.u;
            }
            catch { /* 相机取不到就留零 */ }
            GetCameraBasis(out Vec3 engF, out Vec3 engR);

            // 行 1：控制权 + 两边坐标 + 人板偏移
            DebugLogger.Log(string.Format(
                "[Flight-Diag] ctrl={0} frozen={1} isMine={2} | player=({3:F2},{4:F2},{5:F2}) board=({6:F2},{7:F2},{8:F2}) offset=({9:F2},{10:F2})",
                main.Controller, 0, main.IsMine ? 1 : 0,
                p.x, p.y, p.z, b.x, b.y, b.z, p.x - b.x, p.y - b.y));

            // 行 2：键盘原始输入（绕开一切逻辑）
            DebugLogger.Log("[Flight-Diag] key: " + FlightInput.Diagnose());

            // 行 3：相机朝向 + 木板速度（含方向）
            DebugLogger.Log(string.Format(
                "[Flight-Diag] 引擎算法 look=({0:F2},{1:F2},{2:F2}) right=({3:F2},{4:F2},{5:F2}) | 相机帧 .f=({6:F2},{7:F2},{8:F2}) .u=({9:F2},{10:F2},{11:F2}) | vel=({12:F2},{13:F2},{14:F2}) |v|={15:F1} pitchBand={16} anim={17}",
                engF.x, engF.y, engF.z, engR.x, engR.y, engR.z,
                cf.x, cf.y, cf.z, cu.x, cu.y, cu.z,
                _velocity.x, _velocity.y, _velocity.z,
                _velocity.Length, _pitchBand, _currentAction ?? "-"));
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
            return string.Format("phase={0} frozen={1} anim={2} v={3:F1} hidden={4} {5}",
                _phase, "(已摘除冻结)", _currentAction ?? "-", _velocity.Length,
                FlightTuning.HideCarrier ? "on" : "off", _board.Describe());
        }
    }
}
