using System;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
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
    /// 地面 ──跳跃中按空格──▶ 起飞 ──升到悬停高度──▶ 空中 ──短按(贴地/俯冲)或长按降到撞地──▶ 落地 ──▶ 地面
    /// </code>
    ///
    /// 🔴🔴 **移动逻辑必须保持"读回真实坐标 + 加增量 + 写回"这一种形态**（2026-09-21 血泪教训）：
    ///     它是**开环**的，没有第二个人碰那块板，任何状态错了都不会被放大。
    ///     我曾一次堆了五层（加速趋近 / 地形夹取 / 高度上限 / 单帧钳 / 安全绳），
    ///     结果两个回路（板的位置、玩家的位置）互相耦合出了正反馈，花了一整天才排干净。
    ///     **加任何一层之前先问：它会不会和别的回路耦合？**
    ///
    /// 🔴 **冻结玩家**（2026-09-21 T1）：飞行期间要让主角"别自己走"，否则按 WASD 他会自己走下木板。
    ///     手法有 6 个候选档，见 <see cref="FlightFreezeMode"/> —— 用 <c>custom.flight freeze &lt;档&gt;</c> 热切。
    ///     第一版只做了「保留 Controller=Player、每帧清零移动输入」，**实机证明无效**（玩家走路不看那两个量）。
    ///     现役判断：真开关是 <c>Controller</c>；已观察到的两个坑是「AI 会跟移动中的木板较劲」，
    ///     对症档位 = <c>aipause</c>（停掉 AI）/ <c>aidetach</c>（掐掉 AI 的目标来源）。
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
        private float _hoverOriginZ;        // 🪦 已退役（2026-09-21：板不再自动抬升，见 TickTakeoff）
        private float _lowClampTimer;       // 贴着最低点停了多久（用于自动落地）
        private float _landTimer;
        private string _currentAction;
        private bool _warnedBadAction;
        private float _clock;               // 累计时间（只给动画核对用）
        private float _actionSetAt;         // 上次设置动作的时刻
        private float _statusTimer;         // 状态行的节流计时
        private int _pitchBand;             // 俯仰档：+1 爬升 / 0 水平 / −1 俯冲（带迟滞）
        private float _bodyYawDeg = float.NaN;  // 机身当前水平朝向角（度，0=+X 逆时针）；NaN = 未知（起飞/落地时重置）
        private bool _takeoffSettled;        // 起飞阶段：玩家是否已经真的站到板上（没站住不抬升）
        private float _takeoffTimer;         // 起飞阶段计时（登板等待用）
        private bool _landingGentle;         // 本次落地是"空格/主动下降"（true）还是"撞地"（false）
        private FlightCamPreset _camPreset = FlightCamPreset.Hover;   // 本帧机位（PickCamPreset 写，Tick 用）
        private bool _bodyDiagLogged;        // 取证：本次飞行是否已打过"首帧朝向"那行
        private bool _bodyDiagPending;       // 取证：等着回读引擎实际朝向
        private float _bodyDiagTimer;
        private float _airTime;              // 进入空中态后过了多久（撞地检测的宽限期用）
        private bool _descendArmed;          // 长按下降是否已"解锁"（起飞时手还按着空格 ⇒ 要松开重按）
        private bool _boardSpawned;          // 本次起飞：板是否已经召唤出来（延迟召唤用）
        private float _boardSpawnTimer;      // 本次起飞：从触发到召唤板过了多久
        private float _takeoffAnimTimer;     // 本次起飞：从按空格那一刻起算（入姿时长按它判，与板延迟重叠）
        private bool _freezeWarned;         // 冻结相关失败只报一次（防每帧刷屏）
        private FlightFreezeMode? _frozenMode;  // 当前**实际施加**的冻结档（null = 没冻）
        private Formation _savedFormation;  // AiDetach 档摘下来的编队，落地还回去
        private MissionMainAgentController _ctrl;   // CtrlOff 档要改的引擎玩家控制器
        private bool _savedCtrlDisabled;    // CtrlOff 档改之前它的 IsDisabled 值，落地还回去
        private uint _engineMoveFlags;      // Flags 档取证：冻结前引擎写的移动标志
        private Vec2 _engineInput;          // Flags 档取证：冻结前引擎写的移动向量
        private bool _engDisabledBeforeCamera;  // 取证：引擎相机冲刷【之前】读到的 IsDisabled（见 OnPreDisplayMissionTick）

        private readonly FlightCameraRig _camRig = new FlightCameraRig();
        private bool _camEntered;               // rig 是否已接管（避免每帧重复 Enter）

        /// <summary>
        /// 🔴 **取证钩子**（2026-09-21）：`MissionScreen.UpdateCamera` 在 <c>Mission.OnTick</c> 里
        /// **每帧**把 <c>MissionMainAgentController.IsDisabled</c> 先置 true 再置回 false。
        /// 本钩子是 <c>OnTick</c> 的第一个行为回调、**早于 UpdateCamera**，在这里读一次，
        /// 就能和 OnMissionTick 里（UpdateCamera 之后）读到的值对照：
        ///
        /// · 这里 = <b>true</b>、OnMissionTick = false ⇒ 我们的写入活过了帧边界，是**相机**在帧中冲掉的
        ///   ⇒ 那么 ControlTick（在 OnPreMissionTick，比这里还早）看到的**是 true**，
        ///   它确实被跳过了 ⇒ **玩家走路根本不走 ControlTick 这条路**（换路，别再修这条）。
        /// · 这里 = <b>false</b> ⇒ 在更早的地方就被冲掉了 ⇒ ControlTick 看到 false、照常跑
        ///   ⇒ 那条路的方向是对的，只是时机不对（要进 `OnPreMissionTick` 的窗口去写）。
        /// </summary>
        public override void OnPreDisplayMissionTick(float dt)
        {
            MissionMainAgentController view = Mission.Current?.GetMissionBehavior<MissionMainAgentController>();
            _engDisabledBeforeCamera = view != null && view.IsDisabled;
        }

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

            // 🔴 运动相机（N5）—— **飞行全程**（含起飞/落地）都推进，免得起降瞬间相机跳回引擎相机。
            //    机位过渡时长与动画交叉淡化**2026-09-21 起已解绑**（用户实机裁定镜头慢一点更像运镜，
            //    见 FlightTuning.CamBlendIn）；要一起调用 `custom.flight tune camblend`。
            if (_phase != Phase.Grounded && _camEntered)
            {
                // 🔴 先喂鼠标 —— 接管相机后引擎不再处理 look（CheckForUpdateCamera 早退），
                //    这一行是玩家唯一能转视角的地方。
                _camRig.ApplyLook(Input.MouseMoveX, Input.MouseMoveY);
                PickCamPreset();
                _camRig.SetPreset(_camPreset, FlightTuning.CamBlendIn);
                _camRig.Tick(main, dt);
            }

            // 🔴 冻结层（T1，2026-09-21）—— 放在相位更新【之后】：
            //    起飞那一帧就冻、落地那一帧就松开，中间不留缝。
            //    具体手法见 FlightFreezeMode；只有 Flags 档需要每帧做，其余档是"设一次就生效"的状态。
            if (_phase != Phase.Grounded)
            {
                EnterFreeze(main);      // 幂等：已冻结则直接返回
                TickFreeze(main);       // 幂等：非 Flags 档什么都不做
            }
            else
            {
                ExitFreeze();
            }
        }

        /// <summary>场景结束时兜底回收（ESC 直接退场景也不泄漏）。</summary>
        public override void OnRemoveBehavior()        {
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

            // 🔴 起飞触发 = **二段跳**（2026-09-21 N2 用户要求）：**跳跃中（不在地面）按空格**。
            //
            //    · 跳跃是正常工作的（空格即跳跃）—— 方案里曾写"human 跳不动"，那是文档误读，已订正。
            //    · 判定用 `IsOnLand()`：**离地即为真**，不要求"刚跳过"。所以从高处坠落时按空格
            //      同样能起飞 —— 这是想要的（摔下来时能自救）。
            //    · 🔴 按下沿**无条件消费**（不管冻没冻、开没开），否则它会跨帧滞留
            //      （在地面起跳那一帧没消费掉 → 下一帧正好离地 → 一跳就直接飞）。
            bool pressed = FlightInput.ConsumeSpacePress();
            if (FlightTuning.TakeoffByDoubleJump && !main.IsOnLand() && pressed)
            {
                DebugLogger.Log("[Flight] 二段跳起飞（跳跃中按空格）");
                BeginTakeoff(main);
                return;
            }

            // 后备：长按空格起飞。🔴 **默认保留** —— 长按同时还是**落地**的触发
            // （`TickAirborne` 里那条），所以"长按管进出"的对称手感还在。
            // 不想要长按起飞就 `custom.flight tune longpressjump 0`（落地触发不受影响）。
            if (FlightTuning.TakeoffByLongPress && FlightInput.ConsumeSpaceLongPress())
            {
                DebugLogger.Log("[Flight] 长按空格起飞（后备触发）");
                BeginTakeoff(main);
            }
        }

        private void TickTakeoff(Agent main, float dt)
        {
            SetAction(main, FlightTuning.ActTakeoff, FlightTuning.TakeoffBlendIn);   // 同一条 → 内部直接返回；被抢走时才重设

            // 🔴 入姿计时**从按下空格那一刻**起算（不是从登板起算）——
            //    这样 `takeoffdelay`（板延迟）与 `takeoffanim`（入姿时长）是**重叠**的，不是相加的：
            //    设 1.3 秒延迟时，人一踩上板入姿也快演完了，不会再多等 1.5 秒。
            _takeoffAnimTimer += dt;

            // ① 板延迟召唤（2026-09-22 用户裁定）：动作已经秒播了，板晚 <see cref="FlightTuning.TakeoffSpawnDelay"/> 秒出现。
            if (!_boardSpawned)
            {
                _boardSpawnTimer += dt;
                if (_boardSpawnTimer < FlightTuning.TakeoffSpawnDelay)
                    return;                       // 板还没出现：人继续落/升，动作已经在播
                if (!SpawnBoardAtFeet(main))
                {
                    DebugLogger.Log("[Flight] 载具召唤失败（网格不在包里？）—— 取消本次飞行");
                    AbortFlight();
                    return;
                }
                _boardSpawned = true;
                _takeoffTimer = 0f;               // 登板计时从"板出现"那一刻起算
                DebugLogger.Log($"[Flight] 板已召唤（延迟 {_boardSpawnTimer:F2}s） | {CarrierBoard.DescribeCapsule(main)} | {_board.Describe()}");
            }

            // 🔴 登板闸（2026-09-21 二修）：触发是**二段跳**，按空格那一帧人还在空中。
            //    板生成在脚下 3cm 之后**一动不动**，等人自己落回板面（或超时兜底）。
            //    为什么不跟人走（2026-09-21 用户裁定，我加过一版跟随，被否）：
            //    **板就在脚底 3cm，人落回来是几帧的事** —— 不需要板去追；跟着人动反而让"板在飘"。
            if (!_takeoffSettled)
            {
                _takeoffTimer += dt;
                bool standing = main.IsOnLand();
                if (!standing && _takeoffTimer < FlightTuning.TakeoffSettleSeconds)
                    return;                       // 板保持不动，等人落回来
                _takeoffSettled = true;
                _airTime = 0f;
                _descendArmed = false;            // 长按起飞时手还按着空格 —— 要松开重按才允许下降
                DebugLogger.Log($"[Flight] 登板完成: 等待={_takeoffTimer:F2}s 站住={standing} | {CarrierBoard.DescribeCapsule(main)}");
                _takeoffTimer = 0f;               // 计时切给"入姿动画演多久"（避免这一帧被记两次）
                return;
            }

            // 🔴 **踩实之后板也不动**（2026-09-21 用户裁定："只允许玩家 WASD 移动时候让他动"）。
            //    原来这里有一段自动抬升（7 m/s 升到悬停高度）—— 已删。
            //    现在只做一件事：**把"起飞"姿态播完**（那 1.5 秒的入姿动画），或者玩家一给方向输入就立刻交给他。
            //    这期间板纹丝不动 —— 要升空就自己抬头 + W（与"抬头爬升"那套一致）。
            if (FlightInput.HasMoveInput || _takeoffAnimTimer >= FlightTuning.TakeoffAnimSeconds)
            {
                _phase = Phase.Airborne;
                _velocity = Vec3.Zero;
                _lowClampTimer = 0f;
                _airTime = 0f;
                SetAction(main, FlightTuning.ActIdle);
            }
        }

        private void TickAirborne(Mission mission, Agent main, float dt)
        {
            _airTime += dt;

            // ① 先取镜头方向（下面几处都要用）
            GetCameraBasis(out Vec3 forward, out Vec3 right);

            // ② 下降 / 落地手势（🔴 2026-09-21 用户重新定义，与起飞不对称了）
            //
            //    · **短按空格 = 落地**，但**只有两种情况成立**：
            //        ㈠ 冲向地面（镜头朝下的分量够大）
            //        ㈡ 离地很近（≤ 刚二段跳进浮空的那个高度 —— 相当于"反悔刚才那一跳"）
            //      高空平飞时短按**不落地** —— 免得手一抖就从天上掉下来。
            //    · **长按空格 = 持续下降**（松手停），降到撞地由 N4 那条自动落地收尾。
            //
            //    🔴 与起飞不对称是**故意的**：起飞只要"在空中"就成立（跳一下按空格很简单），
            //       落地却是个"破坏性"操作，必须给两道闸（贴地 / 俯冲）挡误触。
            float groundZNow = GetGroundZ(mission.Scene, _board.Origin);
            float heightAboveGround = (_board.Origin.z + FlightTuning.CarrierTopLocalZ) - groundZNow;
            bool chargingGround = forward.z <= -FlightTuning.LandTapDivePitch;

            if (FlightTuning.LandByTap && FlightInput.ConsumeSpacePress())
            {
                bool lowEnough = heightAboveGround <= FlightTuning.LandTapMaxHeight;
                if (lowEnough || (chargingGround && FlightTuning.LandTapWhileDiving))
                {
                    DebugLogger.Log($"[Flight] 短按空格落地（距地 {heightAboveGround:F2}m 俯冲={chargingGround}）");
                    BeginLanding(main, gentle: true);   // 空格按的 = "轻轻放下"，不播落地动画
                    return;
                }
            }

            bool descendHeld = FlightTuning.LandByLongPressDescend
                               && _descendArmed
                               && FlightInput.SpaceHeld
                               && FlightInput.SpaceHoldSeconds >= FlightTuning.LongPressSeconds;

            // 🔴 长按下降要"松开重按"才解锁（2026-09-21）：起飞是**长按空格**触发的后备路径时，
            //    手还按在空格上 —— 不解锁的话一进空中态就立刻判成"持续下降"，刚起飞就往下掉。
            //    双跳起飞时手早就松了，这一条对主路径没有影响。
            if (!_descendArmed && !FlightInput.SpaceHeld)
                _descendArmed = true;

            Vec2 axis = FlightInput.MoveAxis;
            Vec3 dir = forward * axis.y + right * axis.x;
            if (dir.LengthSquared > 0.0001f)
                dir = dir.NormalizedCopy();
            else
                dir = Vec3.Zero;

            // (3) 速度 = 恒定值，**不做加速趋近**（对齐已实测丝滑的那套：那边就是恒定速度）
            float targetSpeed = FlightInput.BoostHeld ? FlightTuning.BoostSpeed : FlightTuning.CruiseSpeed;
            _velocity = dir * targetSpeed;

            // 长按空格 = **持续下降**：垂直分量**整个接管**（不看镜头俯仰）—— 用户要的是
            // "按住就一直往下降"，那就不该因为玩家抬头而改回爬升。水平分量保留（边降边飞）。
            if (descendHeld)
                _velocity = new Vec3(_velocity.x, _velocity.y, -FlightTuning.DescendRate);

            // (4) 逐帧瞬移载具 —— 就是 FlySpike 那三行，一个夹取都不加。
            //     地形夹取 / 高度上限 / 单帧上限 **全部删掉**：
            //     它们是"我猜的保险"，实测只会制造新问题（160 米上限当场把人卡死过）。
            _board.MoveBy(_velocity * dt);

            // ⑦ 姿态：按「冲刺 > 俯仰 > 速度」挑一条（状态没变时 SetAction 内部会跳过）
            SetAction(main, PickAirAction(forward));

            // ⑧ 机身朝向（🔴 2026-09-21 用户裁定 = **朝实际移动方向**，见下）
            //    有输入 → 朝【实际移动方向】的水平投影：
            //              W 朝镜头前方 / A 朝左 / D 朝右 / S 转身朝镜头（对着玩家）
            //    无输入 → **一个字都不写** ⇒ 保持最后朝向 ⇒ 镜头绕着转能看到各个面、转到正面就是正脸。
            //
            // 🔴 **本条推翻早先的"飞机式"裁定**（那条要求 A/D 平移时身体不转、始终朝镜头前方）。
            //    两条是相反的，**以现在这条为准**；要改回去只需把 `dir` 换成 `forward`（一行）。
            if (FlightInput.HasMoveInput)
                TurnBodySmoothed(main, dir, dt);

            // ⑨ 撞地检测（N4，2026-09-21 用户要求）—— 板顶触地 ⇒ 自动进落地。
            //    · 这是**纯检测、不做位置修正**：夹取会和"板的位置""玩家位置"两个回路耦合出正反馈
            //      （方案开头那条血泪教训），而"发现触地就换状态"不是修正回路，安全。
            //    · 用 GetGroundZ（只查**地形**，不查物理体）—— 查物理体会查到自己那块板，
            //      板永远"踩着"自己 ⇒ 每帧都判触地。
            //    · 阈值留一小段容差：飞行中贴地掠过不该被判成落地，真撞上去才落。
            if (FlightTuning.LandOnGroundTouch && _airTime >= FlightTuning.LandTouchGraceSeconds)
            {
                float groundZ = GetGroundZ(mission.Scene, _board.Origin);
                float boardTop = _board.Origin.z + FlightTuning.CarrierTopLocalZ;
                if (boardTop <= groundZ + FlightTuning.LandTouchEps)
                {
                    DebugLogger.Log($"[Flight] 撞地 → 自动落地（板顶={boardTop:F2} 地面={groundZ:F2} 差={boardTop - groundZ:F2}）");
                    // 长按空格主动下降导致的接地 = "空格导致的"，也算轻放；
                    // 其余（飞着撞上地形）= 硬着陆，照旧播落地动画。
                    BeginLanding(main, gentle: descendHeld);
                    return;
                }
            }

            // ⑩ 每 0.5 秒打一组诊断 —— 板就算隐藏了，也能靠数字确认「人在不在板上、输入有没有读到」
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
            bool playLandAnim = !_landingGentle || FlightTuning.LandAnimOnGentle;
            SetAction(main, playLandAnim ? FlightTuning.ActLand : FlightTuning.ActIdle);

            float groundOriginZ = GetGroundZ(Mission.Current.Scene, _board.Origin) - FlightTuning.CarrierTopLocalZ;
            Vec3 o = _board.Origin;
            float nz = Math.Max(groundOriginZ, o.z - FlightTuning.LandRate * dt);
            _board.MoveTo(new Vec3(o.x, o.y, nz));

            // 🔴 触地**不等于**收摊：要等落地动画播完（用户要求，2026-09-21）。
            //    原来一触地就 FinishFlight，而它同一帧还相机 + 清动作通道 ⇒ 动画被咔嚓掉。
            //    从贴地短按落地时落差只有 1 米、0.2 秒就触地，动画基本看不到。
            //
            // 🔴 **但"空格/主动下降"那种落地不等**（2026-09-21 用户裁定）：它本来就是轻放，
            //    保持待机姿势降到地面就当场收摊，让引擎走跑立刻接管 —— 用户原话
            //    "得真的接地了再恢复常规行走（如果是空格导致接地不需要 land 动画）"。
            _landTimer += dt;
            bool touchedDown = nz <= groundOriginZ + 0.02f;

            if (_landingGentle && !FlightTuning.LandAnimOnGentle)
            {
                if (touchedDown)
                {
                    DebugLogger.Log($"[Flight] 轻放收摊: 用时={_landTimer:F2}s（不播落地动画）");
                    FinishFlight(main);
                }
                return;
            }

            if ((touchedDown && _landTimer >= FlightTuning.LandAnimSeconds)
                || _landTimer >= FlightTuning.LandMaxSeconds)
            {
                DebugLogger.Log($"[Flight] 落地收摊: 用时={_landTimer:F2}s 动画时长={FlightTuning.LandAnimSeconds:F2}s 触地={touchedDown}");
                FinishFlight(main);
            }
        }

        // ─────────────────────────── 进出 ───────────────────────────

        private void BeginTakeoff(Agent main)
        {
            Scene scene = Mission.Current?.Scene;
            if (scene == null)
                return;

            _takeoffSettled = false;
            _takeoffTimer = 0f;
            _boardSpawned = false;
            _boardSpawnTimer = 0f;
            _takeoffAnimTimer = 0f;
            _airTime = 0f;
            _descendArmed = false;
            _phase = Phase.Takeoff;
            _velocity = Vec3.Zero;
            _landTimer = 0f;
            _lowClampTimer = 0f;
            _bodyYawDeg = float.NaN;        // 机身朝向重新播种（首次写不插值 = 不甩头）
            _bodyDiagLogged = false;        // 取向取证重新开一次
            _bodyDiagPending = false;
            _bodyDiagTimer = 0f;

            // 🔴 **先切动作，板晚一点再召唤**（2026-09-22 用户裁定）。
            //    理由：动作是玩家输入的即时反馈（按空格就该立刻起势），而板的出现晚 0.2 秒 ——
            //    这样"下落到板上"那一拍落在**起飞动作已经播起来之后**，不再读成一个独立的"落地"。
            //    淡入 = <see cref="FlightTuning.TakeoffBlendIn"/>（现在是 0 = 秒播，不淡化）。
            //    可选跳过 clip 开头（`takeoffskip`），若那段"踩平面"是 clip 自带的话用得上。
            float skip = MBMath.ClampFloat(FlightTuning.TakeoffSkipSeconds, 0f, FlightTuning.TakeoffAnimSeconds * 0.9f);
            float startProgress = FlightTuning.TakeoffAnimSeconds > 0.01f
                ? MBMath.ClampFloat(skip / FlightTuning.TakeoffAnimSeconds, 0f, 0.9f)
                : 0f;
            SetAction(main, FlightTuning.ActTakeoff, FlightTuning.TakeoffBlendIn, startProgress);

            if (FlightTuning.UseFlightCamera)
            {
                _camEntered = _camRig.Enter(main);
                if (!_camEntered)
                    DebugLogger.Log("[FlightCam] 接管失败，本次飞行用引擎默认相机");
            }

            DebugLogger.Log($"[Flight] 起飞触发: 动作={FlightTuning.ActTakeoff} blendIn={FlightTuning.TakeoffBlendIn:F2}s " +
                            $"跳过开头={skip:F2}s(startProgress={startProgress:F2}) 板延迟={FlightTuning.TakeoffSpawnDelay:F2}s " +
                            $"| {CarrierBoard.DescribeCapsule(main)}");
        }

        /// <summary>
        /// 在玩家**脚下**召唤载具（板面 = 碰撞体底面 − 间隙，见 <see cref="CarrierBoard.CollisionCapsuleBottomZ"/>）。
        ///
        /// 🔴 口径提醒：**不能用 `main.Position` 当脚底** —— 跳跃中碰撞体比它高约 0.43 米（实测 2026-09-21）。
        /// </summary>
        private bool SpawnBoardAtFeet(Agent main)
        {
            Scene scene = Mission.Current?.Scene;
            if (scene == null)
                return false;

            float boardTopZ = CarrierBoard.CollisionCapsuleBottomZ(main) - FlightTuning.CarrierSpawnGap;
            return _board.Spawn(scene, new Vec3(main.Position.x, main.Position.y, boardTopZ));
        }

        private void BeginLanding(Agent main, bool gentle)
        {
            _phase = Phase.Landing;
            _velocity = Vec3.Zero;
            _landTimer = 0f;
            _landingGentle = gentle;
            // 空格导致的接地 = 保持**悬停待机姿势**下降（用户裁定）；撞地才播落地动画。
            bool playLandAnim = !gentle || FlightTuning.LandAnimOnGentle;
            SetAction(main, playLandAnim ? FlightTuning.ActLand : FlightTuning.ActIdle);
            DebugLogger.Log($"[Flight] 进入落地（方式={(gentle ? "空格/主动下降" : "撞地")} " +
                            $"动作={(playLandAnim ? FlightTuning.ActLand : FlightTuning.ActIdle + "（保持待机姿态）")} " +
                            $"目标时长={FlightTuning.LandAnimSeconds:F2}s）");
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
            _bodyYawDeg = float.NaN;
            _takeoffSettled = false;
            _takeoffTimer = 0f;
            ExitCamera();               // 🔴 必须还相机
            ExitFreeze();
            FlightInput.Reset();

            DebugLogger.Log("[Flight] 已落地，控制权与动作通道均已归还");

            // 手动 ctrl off 是没有安全网的 —— 落地时提醒一句，免得"落地后走不动"被当成 bug
            MissionMainAgentController view = Mission.Current?.GetMissionBehavior<MissionMainAgentController>();
            if (view != null && view.IsDisabled)
                DebugLogger.Log("[Flight] ⚠️ 引擎玩家控制器仍是 IsDisabled=true（你手动 custom.flight ctrl off 关的）—— 想走路请下 custom.flight ctrl on");
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
            _bodyYawDeg = float.NaN;
            _takeoffSettled = false;
            _takeoffTimer = 0f;
            ExitCamera();               // 🔴 必须还相机 —— 不还 = 场景内相机永远被我们接管
            ExitFreeze();
            FlightInput.Reset();
        }

        // ─────────────────────────── 运动相机（N5，2026-09-21）───────────────────────────

        /// <summary>
        /// 按当前飞行状态挑机位。优先级：**瞄准 &gt; 加速 &gt; 移动 &gt; 悬停**。
        ///
        /// 🔴 **瞄准只在悬停 / 巡航可用**（用户裁定）：加速中不给进 —— 冲刺时视野要的是"快"，
        ///    拉近过肩会既看不清路又和速度感打架。
        /// </summary>
        private void PickCamPreset()
        {
            // 🔴 落地阶段一律用**悬停机位**（2026-09-21 用户裁定：落地动画一开始，镜头就该跟 idle 一样）。
            //    不加这条的话，落地那一刻若还按着 W（HasMoveInput 为真）就仍是巡航机位，收摊时镜头落差明显。
            if (_phase == Phase.Landing)
            {
                _camPreset = FlightCamPreset.Hover;
                return;
            }

            bool boosting = FlightInput.BoostHeld;

            if (FlightTuning.AimOnRightClick && FlightInput.AimHeld && !boosting)
            {
                _camPreset = FlightCamPreset.Aim;
                return;
            }

            if (boosting)
            {
                _camPreset = FlightCamPreset.Boost;
                return;
            }

            _camPreset = FlightInput.HasMoveInput ? FlightCamPreset.Cruise : FlightCamPreset.Hover;
        }

        /// <summary>归还相机（幂等）。**落地 / 收摊 / 异常都要走这里** —— 不还 = 场景内相机永远被接管。</summary>
        private void ExitCamera()
        {
            if (!_camEntered)
                return;
            _camEntered = false;
            _camRig.Exit();
        }

        // ─────────────────────────── 冻结（T1，2026-09-21）───────────────────────────

        /// <summary>
        /// 进入冻结 —— 按 <see cref="FlightTuning.Freeze"/> 的档位对玩家下手。**幂等**：已冻结则直接返回。
        ///
        /// 🔴 第一版只做了「保留 Controller=Player、每帧清零移动输入」，**实机证明无效**
        ///    （按 W 时 `engineMove` 恒为 `0x0`，人却比板快 2.25 m/s 自己在走 —— 玩家走路不看那两个量）。
        ///    现在的判断：**玩家走路的真开关是 `Controller`**，而各档位对「板还托不托得住人」
        ///    和「AI 会不会跟板较劲」的影响只能实测 —— 所以做成 6 个档一轮试出来。
        ///
        /// 🔴 **关于「AI 跟板较劲」**（2026-09-21 用户观察 + 推断）：
        ///    地上把 Controller 切成 AI 后 **WASD 完全无效**（= 冻结确实成立），
        ///    但上次在板上 AI 档会「乱走」—— 那不是没冻住，而是 **AI 在跟木板较劲**：
        ///    板每帧把人挪走，AI 想把 agent 带回它认定的位置，于是自己走回来。
        ///    对症的两档 = <see cref="FlightFreezeMode.AiPaused"/>（把 AI 停掉）与
        ///    <see cref="FlightFreezeMode.AiDetach"/>（掐掉 AI 的目标来源 = 编队）。
        /// </summary>
        private void EnterFreeze(Agent main)
        {
            if (_frozenMode.HasValue)
                return;

            FlightFreezeMode mode = FlightTuning.Freeze;
            _frozenMode = mode;

            try
            {
                switch (mode)
                {
                    case FlightFreezeMode.Off:
                        DebugLogger.Log("[Flight] 冻结档 = Off（不冻，按 WASD 角色会自己走下板 —— 仅作对照）");
                        break;

                    case FlightFreezeMode.Flags:
                        DebugLogger.Log("[Flight] 冻结档 = Flags（Controller=Player + 每帧清零移动输入）");
                        break;

                    case FlightFreezeMode.CtrlOff:
                        _ctrl = Mission.Current?.GetMissionBehavior<MissionMainAgentController>();
                        if (_ctrl == null)
                        {
                            // 找不到就退回 AI 档（至少能冻住人，代价是可能跟板较劲）
                            WarnFreezeOnce($"找不到 MissionMainAgentController，退回 Ai 档", null);
                            goto case FlightFreezeMode.Ai;
                        }
                        _savedCtrlDisabled = _ctrl.IsDisabled;
                        _ctrl.IsDisabled = true;
                        DebugLogger.Log($"[Flight] 冻结档 = CtrlOff（Controller={main.Controller} 不变 | 引擎玩家控制器 IsDisabled {_savedCtrlDisabled}→true）");
                        break;

                    case FlightFreezeMode.Ai:
                        V.SetPlayerControlFrozen(main, true);
                        DebugLogger.Log($"[Flight] 冻结档 = Ai（Controller={main.Controller}）");
                        break;

                    case FlightFreezeMode.AiPaused:
                        V.SetPlayerControlFrozen(main, true);
                        main.SetIsAIPaused(true);
                        DebugLogger.Log($"[Flight] 冻结档 = AiPaused（Controller={main.Controller} paused={main.IsPaused}）");
                        break;

                    case FlightFreezeMode.AiDetach:
                        _savedFormation = main.Formation;      // 记下来，落地还回去
                        V.SetPlayerControlFrozen(main, true);
                        main.Formation = null;
                        DebugLogger.Log($"[Flight] 冻结档 = AiDetach（Controller={main.Controller} 编队={(_savedFormation != null ? "已摘" : "本来就没有")}）");
                        break;

                    case FlightFreezeMode.None:
                        V.SetAgentControllerNone(main);
                        DebugLogger.Log($"[Flight] 冻结档 = None（Controller={main.Controller}）");
                        break;
                }
            }
            catch (Exception ex)
            {
                WarnFreezeOnce($"冻结档 {mode} 施加失败", ex);
            }
        }

        /// <summary>
        /// 冻结的**每帧**部分。**只有 <see cref="FlightFreezeMode.Flags"/> 档需要**（引擎每帧重写，我们也得每帧清零）；
        /// 其余档位是"设一次就生效"的状态（Controller / IsPaused / Formation），每帧不做任何事。
        ///
        /// 🔴 Flags 档顺手兼职**取证**：清零前先把引擎写进去的值记下来，诊断行里能看到
        ///    "不冻结的话他这一帧会往哪走"（2026-09-21 就是靠它证明这条路无效的：
        ///    按着 W 时 `engineMove` 恒为 `0x0`，说明那个量根本不是玩家走路的开关）。
        /// </summary>
        private void TickFreeze(Agent main)
        {
            if (_frozenMode != FlightFreezeMode.Flags)
                return;

            try
            {
                _engineMoveFlags = (uint)main.MovementFlags;
                _engineInput = main.MovementInputVector;

                main.MovementInputVector = Vec2.Zero;
                main.MovementFlags = 0;
                main.EventControlFlags = 0;      // 连跳跃 / 上下马 / 换武器一起封（空格同时是跳跃键）
            }
            catch (Exception ex)
            {
                WarnFreezeOnce("每帧清零失败（症状：角色会自己走下板）", ex);
            }
        }

        /// <summary>
        /// 松开冻结。**幂等**：没冻过就什么都不做。
        ///
        /// 🔴 <see cref="FlightFreezeMode.Flags"/> / <see cref="FlightFreezeMode.Off"/> 档**没有需要还原的状态**
        ///    （前者引擎每帧自己重写，后者我们压根没动）—— 这也是第一版敢说"冻结不会泄漏"的原因。
        ///    **其余档位改了真状态（Controller / IsPaused / Formation），必须还** ——
        ///    不还的后果是落地后玩家永久失去控制权。所以这里失败也要吼一声。
        /// </summary>
        private void ExitFreeze()
        {
            if (!_frozenMode.HasValue)
                return;

            FlightFreezeMode mode = _frozenMode.Value;
            _frozenMode = null;
            _engineMoveFlags = 0;
            _engineInput = Vec2.Zero;

            // Off / Flags 档：没改过引擎状态，直接收工
            if (mode == FlightFreezeMode.Off || mode == FlightFreezeMode.Flags)
                return;

            // 🔴 CtrlOff 档：把引擎玩家控制器还回去。**这一条尤其不能漏** ——
            //    漏了 = 落地后玩家永久不能走（比失去控制权还彻底）。
            //    它不依赖 agent 存活（控制器是 MissionView，与 agent 无关），所以放在最前面还。
            if (mode == FlightFreezeMode.CtrlOff)
            {
                if (_ctrl != null)
                {
                    try
                    {
                        _ctrl.IsDisabled = _savedCtrlDisabled;
                        DebugLogger.Log($"[Flight] 解冻（CtrlOff）：引擎玩家控制器 IsDisabled → {_savedCtrlDisabled}");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[Flight] 🔴 归还玩家控制器失败（落地后可能不能走，重进场景可恢复）: {ex.Message}");
                    }
                    _ctrl = null;
                }
                return;
            }

            Agent main = Agent.Main;
            if (main == null || !AgentControlHelper.SafeIsActive(main))
            {
                // agent 已经没了（阵亡 / 换场景）—— 新 agent 是重新建的，控制权天然是 Player，无从泄漏
                _savedFormation = null;
                DebugLogger.Log($"[Flight] 解冻（{mode}）：玩家 agent 已失效，无需归还");
                return;
            }

            try
            {
                main.SetIsAIPaused(false);
                if (_savedFormation != null)
                {
                    main.Formation = _savedFormation;
                    _savedFormation = null;
                }
                V.SetPlayerControlFrozen(main, false);

                DebugLogger.Log($"[Flight] 解冻（{mode}）：Controller={main.Controller} paused={main.IsPaused} 编队={(main.Formation != null ? "已还" : "无")}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Flight] 🔴 解冻失败，玩家可能失去控制权（重启场景可恢复）: {ex.Message}");
            }
        }

        private void WarnFreezeOnce(string what, Exception ex)
        {
            if (_freezeWarned)
                return;
            _freezeWarned = true;
            DebugLogger.Log(ex == null ? $"[Flight] {what}" : $"[Flight] {what}: {ex.Message}");
        }

        /// <summary>控制台热切冻结档（<c>custom.flight freeze &lt;模式&gt;</c>）。飞行中立即换档，不用重编译。</summary>
        public string SetFreezeMode(FlightFreezeMode mode)
        {
            FlightFreezeMode old = FlightTuning.Freeze;
            FlightTuning.Freeze = mode;

            if (_phase == Phase.Grounded)
                return $"freeze: {old} -> {mode} (applies on next takeoff)";

            ExitFreeze();                       // 先还旧档的状态
            Agent main = Agent.Main;
            if (main != null && AgentControlHelper.SafeIsActive(main))
                EnterFreeze(main);              // 再施新档
            return $"freeze: {old} -> {mode} (re-applied in flight)";
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
        private void SetAction(Agent agent, string actionName, float blendInOverride = -1f, float startProgress = 0f)
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

            string from = _currentAction ?? "-";
            _currentAction = actionName;
            _actionSetAt = _clock;
            float blend = blendInOverride >= 0f ? blendInOverride : FlightTuning.AnimBlendIn;
            try
            {
                agent.SetActionChannel(0, idx, ignorePriority: true,
                                       blendInPeriod: blend, startProgress: startProgress);
                if (FlightTuning.VerboseLog)
                    DebugLogger.Log($"[Flight] 切动作 {from}→{actionName} blendIn={blend:F2} start={startProgress:F2}");
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
        private void GetCameraBasis(out Vec3 forward, out Vec3 right)
        {
            forward = Vec3.Zero;
            right = Vec3.Zero;

            // 🔴 主路 = **我们自己的相机**（接管期间）—— 绝不能用 ms.CameraBearing/Elevation：
            //    挂上 CustomCamera 后引擎不再处理 look，那两个值是**冻结的旧值**
            //    ⇒ 会得到"画面 A、WASD 飞 B、鼠标没反应"的三重错位（2026-09-21 实机栽过）。
            if (_camEntered && _camRig.TryGetBasis(out forward, out right))
                return;

            // 回退：没接管相机时，引擎相机是活的，读它的角度（原逻辑）
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

            // 行 1：控制权 + 冻结状态 + 玩家**真实速度** + 引擎输入通道 + 人板偏移
            // 🔴 为什么要打 playerVel：光看 offset 分不清「打滑」和「走路」——
            //    · playerVel ≈ 板速  ⇒ 只是被板带着走时的**跟随滞后**（无害）
            //    · playerVel ≠ 板速（尤其方向不同/模长多出 2~3 m/s）⇒ 他**自己在动**
            //    这是判断"冻结到底生没生效"最直接的一个量。
            MissionMainAgentController engView = Mission.Current?.GetMissionBehavior<MissionMainAgentController>();
            Vec2 pv = Vec2.Zero;
            try { pv = main.GetCurrentVelocity(); } catch { /* 取不到就留零 */ }
            Vec3 look = Vec3.Zero;
            try { look = main.LookDirection; } catch { /* 取不到就留零 */ }
            DebugLogger.Log(string.Format(
                "[Flight-Diag] ctrl={0} frozen={1} isMine={2} | engDisabled={3}/{4} | engineMove=0x{5:X} engineAxis=({6:F2},{7:F2}) | playerVel=({8:F2},{9:F2})|{10:F1} boardVel={11:F1} | body=({12:F2},{13:F2}) | player=({14:F2},{15:F2},{16:F2}) board=({17:F2},{18:F2},{19:F2}) offset=({20:F2},{21:F2})",
                main.Controller, _frozenMode.HasValue ? _frozenMode.Value.ToString() : "-", main.IsMine ? 1 : 0,
                _engDisabledBeforeCamera ? 1 : 0, (engView != null && engView.IsDisabled) ? 1 : 0,
                _engineMoveFlags, _engineInput.x, _engineInput.y,
                pv.x, pv.y, pv.Length, _velocity.Length,
                look.x, look.y,
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

        /// <summary>
        /// 写机身的水平朝向（俯仰由动画表现 —— 引擎的 agent 转不了俯仰）。
        ///
        /// 🔴 **接口用 <c>SetMovementDirection(Vec2)</c>，不是 <c>LookDirection</c>**（2026-09-21 用户纠正）：
        ///    本项目既有做法就是它 —— `Story/VisualCommands.cs:512` 的"强制说话者看向听者"
        ///    （`speakerAgent.SetMovementDirection(dirToListener.AsVec2)`）。
        ///    `LookDirection` 那条（`agent.LookDirection = v`）实测**转不动**，别再用。
        ///
        /// 🔴 **由调用方决定"要不要写"**：本方法只管把向量落下去。
        ///    飞行的规则是「有输入才写、没输入不写」—— 不写就等于保持最后朝向，
        ///    这正是用户要的"悬停时镜头绕着转能看到各个面"（**别在这儿自作主张补一个朝向**，
        ///    一旦每帧都写，人就被钉死在某个方向、再也转不动了）。
        ///
        /// 🔴 传入的应当是**镜头前方的水平投影**，不是实际移动方向（用户裁定 = 飞机式，
        ///    侧移不甩头）。
        ///
        /// 🟡 **TODO（2026-09-21 用户确认留下）：转向要加平滑渐变** ——
        ///    现在是**瞬时**转向，转镜头时人「啪」地跟过去，观感生硬。
        ///    做法：每帧朝目标角度插值（限速 / 最短弧），速率进 <see cref="FlightTuning"/> 可调。
        /// </summary>
        private static void TurnBody(Agent agent, Vec3 dir)
        {
            Vec2 flat = new Vec2(dir.x, dir.y);
            float len = flat.Length;
            if (len < 0.0001f)
                return;
            try
            {
                agent.SetMovementDirection(flat * (1f / len));   // 归一化：只给方向，不给速度
            }
            catch
            {
                // 朝向只是观感，失败不该拦住飞行
            }
        }

        /// <summary>
        /// 带限速的机身转向（2026-09-21，治「转镜头时人啪地跟过去」）。
        ///
        /// **做法**：自己记一个当前朝向角 <see cref="_bodyYawDeg"/>，每帧朝目标角走
        /// `TurnRateDegPerSec × dt`（最短弧，跨越 ±180° 也不会绕远路），再把角度还原成方向
        /// 交给 <see cref="TurnBody"/> 写下去。**引擎侧接口没变**，只是喂给它的方向变平滑了。
        ///
        /// 🔴 **首次写不做插值**：<see cref="_bodyYawDeg"/> 是 NaN（起飞时重置）就直接取目标角 ——
        ///    否则起飞第一帧机身会从"上一次飞行的朝向"或 0° 处慢慢转过来，反而多一次甩头。
        ///
        /// 🔴 **无输入时一个字都不写**这条规则没变（调用方保证）—— 所以这里不需要处理"保持朝向"，
        ///    也不该在无输入时偷偷更新角度：镜头绕着转看各个面时，机身朝向本来就不该动。
        ///
        /// 🔴 **不写的时候 `_bodyYawDeg` 会与真实朝向脱钩吗**：不会。飞行期间玩家输入被冻结、
        ///    AI 被暂停，没有第三方会转他；而"无输入"时我们既不写也不改 `_bodyYawDeg`，
        ///    下次有输入时它仍等于上次写下去的值 = 机身实际朝向。
        /// </summary>
        private void TurnBodySmoothed(Agent agent, Vec3 dir, float dt)
        {
            Vec2 flat = new Vec2(dir.x, dir.y);
            if (flat.LengthSquared < 0.0001f)
                return;

            float targetDeg = (float)(Math.Atan2(flat.y, flat.x) * (180.0 / Math.PI));
            float rate = FlightTuning.TurnRateDegPerSec;

            // 🔴 取证：飞行中**第一次**写朝向时打一行 —— 分辨"相机方向本来就歪"还是"机身没跟上"。
            //    用户 2026-09-21 反馈"有几次起飞时角色又没朝前"，这一行就是判据：
            //      · 目标角 与 相机前向角 差很多 ⇒ 方向本身有问题（相机播种/输入）
            //      · 目标角 与 相机前向角 一致、但下面的"实际"对不上 ⇒ 引擎没吃我们的写入
            //    ⚠️ 比的是**相机前向**的 `atan2(y,x)`，**不是** `_camRig.LookYaw` ——
            //       后者是 `RotateAboutUp` 的角约定（`atan2(−x, y)`），两个口径相减永远差 90°（我第一版就写错了）。
            if (!_bodyDiagLogged)
            {
                _bodyDiagLogged = true;
                GetCameraBasis(out Vec3 camF, out _);
                float camDeg = (float)(Math.Atan2(camF.y, camF.x) * (180.0 / Math.PI));
                DebugLogger.Log(string.Format(
                    "[Flight] 首帧朝向: 目标角={0:F0}° 相机前向={1:F0}° 差={2:F0}° | 相机yaw={3:F0}° 输入轴=({4:F2},{5:F2})",
                    targetDeg, camDeg, Normalize180(targetDeg - camDeg), _camRig.LookYaw,
                    FlightInput.MoveAxis.x, FlightInput.MoveAxis.y));
                _bodyDiagPending = true;
                _bodyDiagTimer = 0f;
            }

            if (float.IsNaN(_bodyYawDeg) || rate <= 0f)
            {
                _bodyYawDeg = targetDeg;          // 首次 / 关闭平滑 = 瞬时到位
            }
            else
            {
                float delta = Normalize180(targetDeg - _bodyYawDeg);
                float step = rate * dt;
                _bodyYawDeg += (Math.Abs(delta) <= step) ? delta
                                                        : Math.Sign(delta) * step;
            }

            float rad = _bodyYawDeg * (MathF.PI / 180f);
            TurnBody(agent, new Vec3(MathF.Cos(rad), MathF.Sin(rad), 0f));

            // 取证：写入后 0.4 秒回读一次引擎那边的实际朝向（看我们的写入到底吃没吃）
            if (_bodyDiagPending)
            {
                _bodyDiagTimer += dt;
                if (_bodyDiagTimer >= 0.4f)
                {
                    _bodyDiagPending = false;
                    Vec2 actual = Vec2.Zero;
                    try { actual = agent.GetMovementDirection(); } catch { /* 读不到就留零 */ }
                    float actualDeg = (float)(Math.Atan2(actual.y, actual.x) * (180.0 / Math.PI));
                    DebugLogger.Log(string.Format(
                        "[Flight] 朝向回读(0.4s后): 我们写的={0:F0}° 引擎实际={1:F0}° 差={2:F0}° |v|=({3:F2},{4:F2})",
                        _bodyYawDeg, actualDeg, Normalize180(_bodyYawDeg - actualDeg), actual.x, actual.y));
                }
            }
        }

        /// <summary>把角度差折算到 (−180, 180]，保证转向走最短弧。</summary>
        private static float Normalize180(float deg)
        {
            while (deg > 180f) deg -= 360f;
            while (deg <= -180f) deg += 360f;
            return deg;
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
            BeginLanding(main, gentle: true);   // 控制台强制落地 = 当作"主动放下"
            return "landing";
        }

        public string Status()
        {
            return string.Format("phase={0} frozen={1} anim={2} v={3:F1} {4}",
                _phase, _frozenMode.HasValue ? _frozenMode.Value.ToString() : "off", _currentAction ?? "-", _velocity.Length,
                _board.Describe());
        }
    }
}
