using System;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;
using LivingWorldNpcs.Animation;

namespace LivingWorldNpcs.Flight
{
    /// <summary>
    /// 玩家飞行 —— 主行为。
    ///
    /// 一句话：**板托着人走，方向由镜头定，播哪条动画由状态机定**。
    ///
    /// 挂载：<c>MySubModule.OnMissionBehaviorInitialize</c>，必须**置于玩法闸门之前**
    /// （<c>Settings.Instance.IsInteractionDisabled()</c>）—— 战场正好被那道闸门拦在外面，
    /// 挂在后面 = 战场里飞不起来。
    ///
    /// ═══════════════════════════════════════════════════════════════════
    /// **本类有两级"状态"，别搞混**（读代码先认这两级，就不会迷路）
    ///
    ///   ① **相位**（<see cref="Phase"/>，管**物理与流程**）：<see cref="TickGrounded"/> /
    ///      <see cref="TickTakeoff"/> / <see cref="TickAirborne"/> / <see cref="TickLanding"/> /
    ///      <see cref="TickFalling"/>
    ///      ```
    ///      地面 ──空中按空格──▶ 起飞 ──踩实+入姿演完──▶ 空中 ──(XML: 空格⇒坠落)──▶ 坠落 ──(空格⇒回飞 / 触地⇒地面)──▶ 地面
    ///                                              └──(XML: 撞地⇒落地)──▶ 落地 ──▶ 地面
    ///      🔴 **出机与出机的收尾都是 XML 里的边**：`<edge … to="坠落" keys="Space"/>`（出机）
    ///         与 `<edge from="坠落" to="outside" when="land-trigger"/>`（触地时由 C# 送它出机）。
    ///      🔴 **坠落 = 板载下坠**（2026-09-27 用户裁定「增加一个空中坠落状态」，实机定位后定的形）：
    ///         板**不拆**，运着人一起往下掉 —— **支撑不断**，所以引擎那句"由悬空变成被托住
    ///         就把人按地形重算位置"永不触发（拆板自由落体的接回那一瞬会瞬移，实测 63 米一帧）。
    ///         姿势由状态机那条「坠落」状态自己驱动；相机与输入闸留在我们手里 —— 见 <see cref="BeginFalling"/>。
    ///      ```
    ///   ② **动画状态机**（<see cref="AgentAnimStateMachine"/>，管**播哪条动画**）：
    ///      **规则不在这里** —— 在注册制的那份定义 <see cref="FlightAnimMachine"/>（状态表 + 转移表，一眼读完）。
    ///      本类只负责"喂事实"（填 <see cref="FlightAnimContext"/>）+ 相位自己掌控的两段（`Hold` + `Force`）。
    /// ═══════════════════════════════════════════════════════════════════
    ///
    /// 🔴🔴 **移动逻辑必须保持"读回真实坐标 + 加增量 + 写回"这一种形态**（2026-09-21 血泪教训）：
    ///     它是**开环**的，没有第二个人碰那块板，任何状态错了都不会被放大。
    ///     我曾一次堆了五层（加速趋近 / 地形夹取 / 高度上限 / 单帧钳 / 安全绳），
    ///     结果两个回路（板的位置、玩家的位置）互相耦合出了正反馈，花了一整天才排干净。
    ///     **加任何一层之前先问：它会不会和别的回路耦合？**
    ///
    /// 🔴 **冻结玩家**（T1）：飞行期间要让主角"别自己走"，否则按 WASD 他会自己走下木板。
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

        /// <summary>
        /// 本场景里的飞行行为（没挂 = null）。给**别的系统**查询用 —— 目前一个消费者：
        /// 飞行中施法（<c>Combat/SpellCastInput</c> 要问"在飞吗"）。
        /// 🔴 每个 <c>OnMissionTick</c> 开头重设一次（行为是新对象时也不会漏），<c>OnRemoveBehavior</c> 清掉。
        /// </summary>
        public static PlayerFlightBehavior Current { get; private set; }

        /// <summary>
        /// **相机中心方向**（= 玩家看见的准星方向）。飞行中它就是施法/飞行的方向。
        /// 🔴 必须走这里、**不能读 `MissionScreen.CameraBearing/Elevation`** —— 我们接管相机后那两个值是**冻的**（同 <see cref="GetCameraBasis"/> 那段注释）。
        /// </summary>
        public bool TryGetAimForward(out Vec3 forward)
        {
            GetCameraBasis(out forward, out _);
            return forward.LengthSquared > 1e-6f;
        }

        /// <summary>
        /// 🪦 2026-10-05（阶段 2）：本类原来直接实现 <c>ICameraLookProvider</c> 并每帧写
        /// <c>CameraLook.Provider</c> —— 那是单槽互踩的老写法（甲退出会踩掉乙的登记）。现在
        /// **视线由相机服务统一分发**：飞行 rig 登记为服务的"外部持有者"（`FlightCameraRig` 实现
        /// <see cref="ICameraExternalHolder"/>），服务的提供者把 `TryGetLook` 转给 rig。
        /// 要方向照旧走 <see cref="CameraLook.TryGet"/>（铁律 35 的唯一入口），别直接读本类。
        /// </summary>

        public PlayerFlightBehavior()
        {
            // 相机驱动：`UseMergedRig` 打开 = 走相机服务（合并机器）；关 = 旧的飞行机（默认）
            _camRig = FlightTuning.UseMergedRig
                ? (IFlightCameraDriver)new ServiceFlightDriver()
                : new FlightCameraRig();

            // 从注册表按名字取（定义在 FlightAnimMachine；注册在 MySubModule.OnSubModuleLoad）。
            // 定义没注册也不会崩 —— 注册表会给一台空机器（动画不播，其它照常）。
            _anim = AnimMachineRegistry.Create(FlightAnimMachine.Name, _animCtx);
        }

        private enum Phase
        {
            Grounded,   // 不飞（原版状态）
            Takeoff,    // 抬升中（播起飞动画）
            Airborne,   // 空中自由飞（悬停 / 巡航 / 冲刺）
            Landing,    // 落地中（播落地动画）
            Falling     // 出机后**坠落**（板载下坠：板不拆、运着人掉；姿势归状态机，相机与输入闸归我们）
        }

        private readonly CarrierBoard _board = new CarrierBoard();

        // ── 掉落相位的三条硬编码口径（都是"判据"不是可调手感，所以不进 FlightTuning）──

        /// <summary>出机后多久之内不判"着地"（秒）—— 出机那一两帧板还托着脚，`IsOnLand` 可能仍是真。</summary>
        private const float FallLandGraceSeconds = 0.2f;

        /// <summary>掉多久还没着地就强制交还控制（秒）—— 防"卡在天上"把玩家永久冻住（正常掉落用不到）。</summary>
        private const float FallGiveUpSeconds = 20f;

        /// <summary>"玩家被外部挪动"监视器在掉落期间用的速度上限（m/s）—— 只有瞬移量级才会触发。</summary>
        private const float FallWatchSpeedLimit = 60f;
        private Phase _phase = Phase.Grounded;
        private Vec3 _velocity = Vec3.Zero;
        private float _landTimer;
        private float _clock;               // 累计时间
        private float _statusTimer;         // 状态行的节流计时
        private float _bodyYawDeg = float.NaN;  // 机身当前水平朝向角（度，0=+X 逆时针）；NaN = 未知（起飞/落地时重置）
        private float _velYawDeg = float.NaN;   // 实际航向的水平角（度）—— 航向惯性用；NaN = 未播种（起飞/落地/停稳时重置）
        private float _velPitchDeg;             // 实际航向的俯仰（度，+上）—— 同上
        private float _steerTargetYawDeg;       // 上一帧的**目标**航向角（只给诊断日志看"差多少度"）
        private float _steerIdleTimer;          // 无输入累计时长（到 SteerIdleResetSeconds 就把航向重新播种）
        private float _velYawRateRaw;           // 本帧实际航向角速度（度/秒，未平滑）—— 喂相机侧倾用
        private float _velYawRateDeg;           // 平滑后的航向角速度（度/秒）—— 运动驱动真正消费的那个
        private bool _takeoffSettled;        // 起飞阶段：玩家是否已经真的站到板上（没站住不抬升）
        private float _takeoffTimer;         // 起飞阶段计时（登板等待用）
        private float _landingApproach;      // 进入落地那一刻的下冲速度（硬着陆按它下降）
        private string _landPoseState;       // 落地动作的状态名 —— 从 XML 的事件边读（`超人落地 → 机外` 的 from）
        private float _landExitPct = -1f;    // 出机时机（剩余百分比）—— 同上那条边上的 anim-rem-pct；<0 = 没写
        private bool _boardRemoved;          // 落地时板是否已拆（拆了 = 人已站在真实地面）
        private FlightCamPreset _camPreset = FlightCamPreset.Hover;   // 本帧机位（PickCamPreset 写，Tick 用）
        private bool _bodyDiagLogged;        // 取证：本次飞行是否已打过"首帧朝向"那行
        private bool _bodyDiagPending;       // 取证：等着回读引擎实际朝向
        private float _bodyDiagTimer;
        private float _airTime;              // 进入空中态后过了多久（撞地检测的宽限期用）
        private float _dodgeTimer;           // 闪避位移还剩多久（>0 = 这段时间速度归闪避）
        private Vec3 _dodgeDir;              // 闪避位移方向（世界向量，单位化）
        private float _dodgeCooldown;        // 两次闪避之间的剩余冷却（秒）
        private bool _dodgeKeyUsed;          // Z 键"这一次按下已经用掉了"（一次按下 = 一次闪避；松开才重新武装）
        private float _fallTimer;            // 掉落已经多久（秒）—— 着地判定有一段宽限，见 TickFalling
        private float _fallSpeedZ;           // 掉落当前竖直速度（m/s，负 = 往下）：自己按前后两帧的 z 算
        private float _fallPrevZ;            // 上一帧的 z（同上）
        private bool _hasFallPrevZ;
        private float _fallLogTimer;         // 掉落诊断节流（0.5 秒一行）
        private int _fallLogLines;           // 掉落诊断已打行数（封顶 6 行 —— 掉一分钟也只多 6 行）
        private float _fallRideVel;          // 坠落=板载（FallRide）时的下坠速度（m/s，正数 = 往下）
        private string _fallPoseState;       // 坠落姿势的状态名 —— 从 XML `when="fall-trigger"` 那条边读（C# 里没有状态名）
        private bool _fallStateResolved;     // 上面那个名字是否已经查过（定义一次装载、进程内不变，查一次就够）
        // 🪦 2026-09-26 删除 `_pendingDodge` / `_pendingDodgeTimer`：闪避**姿态**现在由 XML 的
        //    `keys="Space+A"` 这类条件直接触发（状态机自己读键）⇒ C# 不再"请求"某个状态，
        //    也就没有"请求有没有被接走"要对账。C# 只负责位移（见 BeginDodge）。
        private string _lastAnimState;       // 上一帧的动画状态名（变了就弹一条提示；见 OnMissionTick）
        private bool _boardSpawned;          // 本次起飞：板是否已经召唤出来（延迟召唤用）
        private bool _headAimActive;         // 施法瞄准期间我们设过"头看相机"的 POI（退出时要撤掉，别留给别人）
        private int _castCh0Restores;        // 施法期间「通道 0 被挤掉又补回」的次数（诊断用）
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

        // 🔴 **相机驱动 = 按开关二选一**（2026-10-05 阶段 4）：旧的 `FlightCameraRig`（自己摆相机）
        //    或 `ServiceFlightDriver`（走相机服务 + 合并机器）。两者接口相同，本类其余代码一行不用改。
        //    ⚠️ 字段在构造函数里赋值 —— **别写成字段初始化器**（那样拿不到 FlightTuning 的运行期值）。
        private readonly IFlightCameraDriver _camRig;
        private bool _camEntered;               // rig 是否已接管（避免每帧重复 Enter）

        // ─────────────────────── 动画状态机（注册制，定义见 Flight/FlightAnimMachine.cs）───────────────────────
        //
        // 🔴 **"什么时候播哪条动画"的规则不在这里** —— 在 FlightAnimMachine 那份**注册的定义**里
        //    （状态表 + 转移表，一眼读完）。本类只负责：
        //      ① 每帧把事实填进上下文（下面那个 ctx）
        //      ② 相位自己掌控动画的两段用 Hold + Force
        private readonly FlightAnimContext _animCtx = new FlightAnimContext();
        private readonly AgentAnimStateMachine _anim;

        /// <summary>俯仰档（带迟滞）：+1 抬头 / 0 水平 / −1 低头。进用大阈值、出用小阈值。</summary>
        private readonly AnimLatch _pitchLatch = new AnimLatch(0f, 0f);   // 阈值每帧按 FlightTuning 刷新
        private readonly AnimLatch _climbLatch = new AnimLatch(0f, 0f);   // 升降档（竖直运动驱动姿态）—— 同上

        /// <summary>压弯档（带迟滞）：+1 按 D（右移）/ 0 / −1 按 A（左移）。取自横移输入。</summary>
        private readonly AnimLatch _bankLatch = new AnimLatch(0f, 0f);    // 同上

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
            Current = this;                 // 给别的系统查（飞行中施法要问"在飞吗"）

            // 🔴 **MCM 总闸**（`Settings.FlightEnabled`，默认关闭）—— 这里管"已经在飞"的那一半：
            //    开关一关就**立刻收摊**，走的是和「玩家阵亡 / tick 异常」同一条收尾路径
            //    （`AbortFlight`：拆板 + 把动画通道还给引擎 + 相位归位），不会把冻结状态泄漏出去。
            //    起飞侧的限制在 `TickGrounded` 里（键盘不响应，但按下沿照样消费）。
            if (!Settings.Instance.FlightEnabled && _phase != Phase.Grounded)
            {
                DebugLogger.Log("[Flight] MCM 飞行开关已关闭 → 强制退出飞行");
                AbortFlight();
            }

            // 🔴 **坠落接缝：从 XML 读"哪个状态算坠落"**（`when="fall-trigger"` 那条边）——
            //    定义一个进程只装载一次 ⇒ 查一次就够。名字读不到 = 退回"机外 = 出机那一刻"的老写法
            //    （那条路下空中回飞会把人瞬移到地面，只是别把飞行卡死）。
            if (!_fallStateResolved)
            {
                _fallStateResolved = true;
                _anim.TryEventTarget(FlightAnimConditions.FallTrigger, out _fallPoseState);
                DebugLogger.Log(_fallPoseState != null
                    ? $"[Flight] 坠落接缝：状态「{_fallPoseState}」= 出机后那段（定义里 fall-trigger 那条边的 to）"
                    : "[Flight] ⚠️ 定义里没有 `when=\"fall-trigger\"` 的边 ⇒ 退回老写法（机外 = 出机那一刻）");
            }
            // 🔴 相机归我们管 ⇒ "要视线找我"已由**相机服务**统一分发（2026-10-05 阶段 2）：
            //    飞行 rig 在 `Enter` 时向 `CameraService.TakeExternal("flight", rig)` 登记，
            //    服务那唯一的 `CameraLook` 提供者会把 `TryGetLook` 转给 rig。
            //    **别再在这里直接写 `CameraLook.Provider`** —— 那是单槽互踩的老写法。
            FlightInput.Tick(dt);
            UpdateKeyFacts();
            LogInputEdges();
            WatchPlayerDisplacement(main, dt);

            try
            {
                switch (_phase)
                {
                    case Phase.Grounded: TickGrounded(main); break;
                    case Phase.Takeoff: TickTakeoff(main, dt); break;
                    case Phase.Airborne: TickAirborne(mission, main, dt); break;
                    case Phase.Landing: TickLanding(main, dt); break;
                    case Phase.Falling: TickFalling(main, dt); break;
                }
            }
            catch (Exception ex)
            {
                // 一次异常即收摊：防每帧刷屏，防冻结状态卡死玩家
                DebugLogger.Log($"[Flight] tick 异常，已强制退出飞行: {ex}");
                AbortFlight();
            }

            // 🔴 不在空中 ⇒ 撤掉"头看相机"（落地 / 中止时清；只撤我们自己设过的那个 POI）
            if (_phase != Phase.Airborne)
                StopAimingHead(main);

            // 🔴 **喂事实**：状态机要读的"当前帧事实"（按键 / 有没有推方向 / 相机俯仰档）——
            //    每帧一次，**在 Tick 之前**（相位怎么变都不影响这条顺序）。
            //    🪦 2026-09-26 起这里统一喂：以前只在空中态喂（TickAirborne 里），
            //    现在**落地下降段也由状态机自己演**（不再 Force 待机姿势），所以提到主循环里来。
            GetCameraBasis(out Vec3 camFwd, out _);
            UpdateAnimContext(camFwd);

            // 🔴 动画状态机：**每帧一次**（含起飞/落地）——
            //    它在这两段被 Hold 住（相位自己 Force），但**仍要跑**：维持当前动作 + 定期核对
            //    有没有被引擎的走跑系统抢走 0 号通道。
            // 🔴 状态机那两档日志的开关**不在飞行这边**了（2026-09-25 搬到 `Animation/AnimDebug.cs`）——
            //    状态机是通用件，开关挂在飞行身上等于"换个系统就得再抄一份"，而且会出现两套打架的开关。
            //    控制台：`custom.anim_log off|on|full`（**默认 off**，要查时敲 `custom.anim_log on`）。
            //    本行为类负责的只是"**默认飞行在开日志时**，输入沿那三行也一起打"（见 LogInputEdges）。
            _anim.Tick(main, dt);

            // ⑦″ 🔴 **两条通用规则**（2026-09-27 第二轮：出机那条边直接连到「坠落」状态，姿势归机器自己管）：
            //     ① 机器**进了坠落状态** ⇒ 出机开始下坠（板载下坠，见 BeginFalling）
            //     ② 机器**说机外了** ⇒ 收摊：拆板 / 还通道 / 还相机 / 解冻
            //     🔴 两条都不认键、也不写死状态名 —— "坠落"那个名字从 XML 的 `when="fall-trigger"`
            //        那条边读（同起飞 / 落地两条接缝）；②的"机外"是机器自己的哨兵（不是 XML 里的状态名）。
            //     ⚠️ 定义里没声明坠落状态时**退回老写法**（机外 = 出机那一刻），那时②不能跟着一起判 ——
            //        否则坠落刚起来就会被它当场收掉（那一档的起点本来就是机外）。
            bool fallDeclared = !string.IsNullOrEmpty(_fallPoseState);
            if (_phase != Phase.Grounded && _phase != Phase.Falling)
            {
                bool entered = fallDeclared
                    ? string.Equals(_anim.Current, _fallPoseState, StringComparison.Ordinal)
                    : string.Equals(_anim.Current, AgentAnimStateMachine.OutsideState, StringComparison.Ordinal);
                if (entered)
                {
                    BeginFalling(main, fallDeclared ? $"状态机进「{_fallPoseState}」" : "状态机出机（定义里没声明坠落状态）");
                    return;
                }
            }
            else if (_phase == Phase.Falling && fallDeclared
                     && string.Equals(_anim.Current, AgentAnimStateMachine.OutsideState, StringComparison.Ordinal))
            {
                DebugLogger.Log("[Flight] 状态机说机外（坠落触地）⇒ 拆板 / 还通道 / 还相机 / 解冻");
                EndFalling();
                return;
            }

            // 🔴 姿态变化时的调试输出（**受总闸 FlightTuning.DebugLog 管，默认关**，2026-09-22 用户要求）。
            //    开：`custom.flight log on`。屏幕提示另有子开关 `tune statemsg 0` 可单独关掉（只留日志）。
            //    文本走 LWNTextHelper（铁律 13）。
            if (FlightTuning.DebugLog && _anim.Current != _lastAnimState)
            {
                _lastAnimState = _anim.Current;
                if (!string.IsNullOrEmpty(_lastAnimState))
                {
                    DebugLogger.Log($"[Flight] 姿态 → {_lastAnimState}（{_anim.CurrentAction}）");
                    if (FlightTuning.ShowStateMessages)
                    {
                        try
                        {
                            InformationManager.DisplayMessage(new InformationMessage(
                                LWNTextHelper.ResolveCompound("LWN_ui_flight_state", ("LWN_STATE", _lastAnimState)),
                                Colors.Yellow));
                        }
                        catch
                        {
                            // 弹提示只是调试辅助，它自己出问题不该影响飞行
                        }
                    }
                }
            }

            // 🔴 运动相机（N5）—— **飞行全程**（含起飞/落地）都推进，免得起降瞬间相机跳回引擎相机。
            //    机位过渡时长与动画交叉淡化**2026-09-21 起已解绑**（用户实机裁定镜头慢一点更像运镜，
            //    见 FlightTuning.CamBlendIn）；要一起调用 `custom.flight tune camblend`。
            // 🔴 归还渐变期间（`_phase` 已经回到 Grounded）也要继续 Tick，否则渐变走不完、相机永远撒不了手
            if (_camEntered && (_phase != Phase.Grounded || _camRig.IsHandingBack))
            {
                // 🔴 先喂鼠标 —— 接管相机后引擎不再处理 look（CheckForUpdateCamera 早退），
                //    这一行是玩家唯一能转视角的地方。
                _camRig.ApplyLook(Input.MouseMoveX, Input.MouseMoveY);
                // 🔴 **运动量必须在 Tick 之前喂**（2026-09-28）：相机那边拿不到真实速度 ——
                //    飞行时玩家的速度被冻结成 0，真速度是我们手上的 `_velocity`（木板速度）。
                _camRig.SetMotion(BuildCamMotion(dt));
                PickCamPreset();
                _camRig.SetPreset(_camPreset, FlightTuning.CamBlendIn);
                _camRig.Tick(main, dt);
                if (!_camRig.IsActive)
                    _camEntered = false;        // 渐变走完 / 异常归还 —— 相机已回到引擎
            }

            // 🔴 冻结层（T1，2026-09-21）—— 放在相位更新【之后】：
            //    起飞那一帧就冻、落地那一帧就松开，中间不留缝。
            //    具体手法见 FlightFreezeMode；只有 Flags 档需要每帧做，其余档是"设一次就生效"的状态。
            // 🔴 **掉落相位要看那个开关**（2026-09-27）：`fallgate 0` 时输入闸是放开的，
            //    这里若照"非地面就冻"来判，下一帧就会把刚解开的冻**又加回去**（开关等于失效）。
            bool wantFreeze = _phase != Phase.Grounded
                              && !(_phase == Phase.Falling && !FlightTuning.HoldInputWhileFalling);
            if (wantFreeze)
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
            if (Current == this)
                Current = null;
            CameraService.ReleaseExternal("flight");   // 幂等；视线登记由相机服务统一管（阶段 2）
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

            // 🔴 **MCM 总闸**（`Settings.FlightEnabled`，默认关闭）—— 关着 = 键盘不响应起飞。
            //    ⚠️ 但上面那行**照样消费按下沿**、下面长按也照原样消费：只"读掉丢掉"、不进相位逻辑。
            //       不消费会跨帧滞留，玩家下次打开开关时一按就直接飞（与上面那条纪律同源）。
            //    控制台 `custom.flight on` 走 ForceStart，**不受本闸门限制**（显式开发/验收命令）。
            bool takeoffAllowed = Settings.Instance.FlightEnabled;

            if (takeoffAllowed && FlightTuning.TakeoffByDoubleJump && !main.IsOnLand() && pressed)
            {
                DebugLogger.Log("[Flight] 二段跳起飞（跳跃中按空格）");
                BeginTakeoff(main);
                return;
            }

            // 后备：长按空格起飞。🔴 **默认保留** —— 长按同时还是**落地**的触发
            // （`TickAirborne` 里那条），所以"长按管进出"的对称手感还在。
            // 不想要长按起飞就 `custom.flight tune longpressjump 0`（落地触发不受影响）。
            if (FlightTuning.TakeoffByLongPress)
            {
                bool longPress = FlightInput.ConsumeSpaceLongPress();
                if (takeoffAllowed && longPress)
                {
                    DebugLogger.Log("[Flight] 长按空格起飞（后备触发）");
                    BeginTakeoff(main);
                }
            }
        }

        private void TickTakeoff(Agent main, float dt)
        {
            // 起飞入姿由相位 Force 进（见 BeginTakeoff），这里不每帧重设 ——
            // 状态机在 Hold 期间会自己维持当前动作并定期核对有没有被引擎抢走。

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
                DebugLogger.Log($"[Flight] 登板完成: 等待={_takeoffTimer:F2}s 站住={standing} | {CarrierBoard.DescribeCapsule(main)}");
                _takeoffTimer = 0f;               // 计时切给"入姿动画演多久"（避免这一帧被记两次）
                return;
            }

            // 🔴 **起飞头几帧：把"搭便车"的人请下板**（2026-10-10 立；与钩索拉拽共用同一道兜底）——
            //    板面上的**任何人**都会被一起抬走（承载是物理体干的，它不认人）⇒ 板还贴地的时候
            //    把站上来的"别人"挪到板外（掉一两米，不疼）。判据与挪法见 `CarrierBoard.EvictRiders`。
            if (FlightTuning.CarrierEvictOthers)
            {
                float lift = (_board.Origin.z + FlightTuning.CarrierTopLocalZ)
                             - GetGroundZ(Mission.Current?.Scene, _board.Origin);
                if (lift <= FlightTuning.CarrierEvictMaxLift)
                {
                    int evicted = _board.EvictRiders(main, FlightTuning.CarrierEvictMargin);
                    if (evicted > 0)
                        DebugLogger.Log($"[Flight] 载具搭便车：请下板 {evicted} 人（板面离地 {lift:F2}m）");
                }
            }

            // 🔴 **板载坠落后回飞：板不能急停**（2026-09-27）—— 人这时带着下坠速度压在板上，
            //    板一停人就离开板面 = 支撑断开 = 又给了引擎那句"按地形重算位置"机会。所以让它
            //    用 `FallRideBrake` 那档减速度把速度收干（36 m/s 大约 1.2 秒），全程人压着板。
            if (_board.IsSpawned && _fallRideVel > 0f)
            {
                _fallRideVel = Math.Max(0f, _fallRideVel - FlightTuning.FallRideBrake * dt);
                Vec3 bo = _board.Origin;
                float gj = GetGroundZ(Mission.Current?.Scene, bo) - FlightTuning.CarrierTopLocalZ;
                _board.MoveTo(new Vec3(bo.x, bo.y, Math.Max(gj, bo.z - _fallRideVel * dt)));
                if (_fallRideVel <= 0f)
                    DebugLogger.Log("[Flight] 坠落速度已收干 ⇒ 交回飞行控制");
            }

            // 🔴 **踩实之后板也不动**（2026-09-21 用户裁定："只允许玩家 WASD 移动时候让他动"）。
            //    原来这里有一段自动抬升（7 m/s 升到悬停高度）—— 已删。
            //    现在只做一件事：**把控制权交给玩家**（有方向输入，或入姿那份时长到了）。
            //    这期间板纹丝不动 —— 要升空就自己抬头 + W（与"抬头爬升"那套一致）。
            //
            // 🔴 **"相位交棒" ≠ "姿态结束"**（2026-09-26 澄清）：这里只放开 `Hold`，**不切动作** ——
            //    入姿那条 clip 由 XML 的 `<edge from="进入飞行" to="悬浮飞行" anim-rem-pct="15"/>` 收尾，
            //    所以按着 W 抢跑也不会把入姿砍半（以前靠"这里有个秒数"来管，现在是定义说了算）。
            // 🔴 **收速没收完就不交棒**（2026-09-27）：否则带着 30 m/s 的下坠速度转进空中相位、
            //    板一停人就脱板（同上，= 支撑断开）。
            if ((FlightInput.HasMoveInput || _takeoffAnimTimer >= FlightTuning.TakeoffAnimSeconds)
                && _fallRideVel <= 0.01f)
            {
                _phase = Phase.Airborne;
                _velocity = Vec3.Zero;
                _velYawDeg = float.NaN;       // 航向重新播种（起飞第一帧不插值 = 不让机头从旧航向慢慢转过来）
                _steerIdleTimer = 0f;
                    _airTime = 0f;
                _anim.Hold = false;           // 交回状态机：下一帧按转移表挑姿态（待机 / 巡航）
            }
        }

        private void TickAirborne(Mission mission, Agent main, float dt)
        {
            _airTime += dt;
            if (_dodgeTimer > 0f)
                _dodgeTimer -= dt;
            if (_dodgeCooldown > 0f)
                _dodgeCooldown -= dt;

            // ① 先取镜头方向（下面几处都要用）
            GetCameraBasis(out Vec3 forward, out Vec3 right);

            // ①′ （状态机的事实已在 OnMissionTick 主循环里喂过 —— 见那里的 `UpdateAnimContext`）

            // ② 手势分工（🔴 2026-09-27 用户裁定：**空格专管出机，闪避改用 C**）
            //    · **空格 = 退出飞行**（两个家族都是）—— 它是 **XML 里的一条边**
            //      （`<edge from="悬浮飞行|冲刺飞行" to="outside" keys="Space"/>`），
            //      本类只遵守一句通用规则 —— "状态机说机外了 ⇒ 开始掉落"（见 OnMissionTick ⑦″）。
            //      掉着的时候**再按一次空格就重新进机**（见 `TickFalling`，**同一个手势，进出对称**）。
            //    · **冲刺中按 Z = 前闪**（不退出飞行）—— 见 ②-0。
            //    · **撞地**（板顶触地）照旧自动落地并播落地动作。
            float groundZNow = GetGroundZ(mission.Scene, _board.Origin);
            float heightAboveGround = (_board.Origin.z + FlightTuning.CarrierTopLocalZ) - groundZNow;

            // 🔴 空格**按下沿**在这里一次读掉（**丢弃**，不派任何用场）——
            //    目的是"出机那一下"别留到下一帧：`FlightInput` 的按下沿只活一帧，
            //    但 `TickFalling` 也读同一个通道，出机与"重新进机"必须在**不同的按下**上发生。
            FlightInput.ConsumeSpacePress();

            // ②-0 **冲刺中按 Z = 前闪**（2026-09-27 用户裁定：闪避键从空格改成 Z ——
            //      它就是游戏自己的蹲下键，UE 超人项目里闪避也正是蹲下键（空中不需要蹲）⇒ 最贴原意）。
            //      · 只在冲刺态（按住 Shift）里成立 —— 闪避动画的基准姿势就是趴姿，
            //        从悬停/巡航（直立）切过去会硬翻 ~90°；姿态那条边在 XML 里也挂在趴姿族上。
            //      · 🔴 **一次按下 = 一次闪避**：`_dodgeKeyUsed` 闩住，松开 C 才重新武装
            //        （不然按住不放会在冷却结束后反复重播闪避姿态 = "闪了一下人没动"）。
            if (FlightInput.ConsumeDodgePress())
            {
                _dodgeKeyUsed = true;
                if (FlightTuning.DodgeInBoost && FlightInput.BoostHeld)
                {
                    if (_dodgeCooldown <= 0f)
                        BeginDodge(forward);
                    else
                        DebugLogger.Log($"[Flight] 闪避被冷却挡下（还剩 {_dodgeCooldown:F2}s）");
                }
            }

            Vec2 axis = FlightInput.MoveAxis;
            // 🔴 **冲刺档：只按 Shift、一个方向键都没按 ⇒ 当作按着 W 往前飞**（2026-09-28 用户裁定）。
            //    改之前那种情况是"趴姿原地悬停"（姿势进了冲刺、人却不动）—— 别扭，而且"冲刺"这个词
            //    本身的意思就是"我要往前冲"，还额外要求按 W 是多余的。
            //    ⚠️ **只在"一个方向键都没按"时生效**：按着 A/D（哪怕没按 W）仍按横移算，别去改横移语义。
            //    开关 = FlightTuning.BoostImpliesForward（`custom.flight tune boostfwd 0` 可关）。
            bool impliedForward = FlightTuning.BoostImpliesForward
                                  && FlightInput.BoostHeld
                                  && axis.LengthSquared < 1e-4f;
            if (impliedForward)
                axis = new Vec2(0f, 1f);      // (x, y) = (横, 前) ⇒ 这就是 W

            Vec3 dir = forward * axis.y + right * axis.x;
            if (dir.LengthSquared > 0.0001f)
            {
                dir = dir.NormalizedCopy();
                _steerIdleTimer = 0f;
            }
            else
            {
                dir = Vec3.Zero;
                _velYawRateRaw = 0f;          // 没在转 ⇒ 航向角速度归零（相机侧倾跟着回正）
                // 停稳（无输入满 `SteerIdleResetSeconds`）⇒ 航向重新播种：悬停没有"航向动量"可言，
                // 镜头转过去再给输入应当**立刻朝那边走**；而按键之间的 1~2 帧空隙够不到这时长
                // （够到了就会把正在划的弧"啪"地掰直 —— W 换 A 那种换键最容易撞上）。
                _steerIdleTimer += dt;
                if (_steerIdleTimer >= FlightTuning.SteerIdleResetSeconds)
                    _velYawDeg = float.NaN;
            }

            // (3) 速度 = 恒定值（**大小**不做加速趋近）；**方向带惯性**（2026-09-27 用户裁定，A 方案）——
            //     相机转了方向，实际航向以 `SteerRateDegPerSec`（冲刺用 `SteerRateBoostDegPerSec`）
            //     追过去 ⇒ 掉头是**划一道弧**，不是当帧把整个速度横过来。两档填 0 = 回到旧的瞬时行为。
            //     ⚠️ 这里**只管方向**：大小仍是 Shift 一按一松当帧切 9 ⇄ 26（速度渐变是另一层，没做）。
            float targetSpeed = FlightInput.BoostHeld ? FlightTuning.BoostSpeed : FlightTuning.CruiseSpeed;
            Vec3 moveDir = (dir.LengthSquared > 0.0001f) ? SteerVelocityDir(dir, dt) : Vec3.Zero;
            _velocity = moveDir * targetSpeed;

            // 闪避位移：这段时间速度**整个交给闪避方向**（覆盖，不叠加）。
            // 位移走完自动交还普通飞行，而姿态动画继续按自己的时长演完（两者刻意解耦：
            // 位移是玩法，动画是表现，谁都不等谁）。
            if (_dodgeTimer > 0f)
            {
                float span = Math.Max(0.01f, FlightTuning.DodgeDisplaceSeconds);
                _velocity = _dodgeDir * (FlightTuning.DodgeDistance / span);
            }

            // (4) 逐帧瞬移载具 —— 就是 FlySpike 那三行，一个夹取都不加。
            //     地形夹取 / 高度上限 / 单帧上限 **全部删掉**：
            //     它们是"我猜的保险"，实测只会制造新问题（160 米上限当场把人卡死过）。
            _board.MoveBy(_velocity * dt);

            // ⑦ 姿态：**交给动画状态机**（规则全在 `ModuleData/statemachines/flight.xml` 那台定义里）
            //    这里只解挂（事实已在 OnMissionTick 主循环里喂过）；真正的 Tick 在 OnMissionTick
            //    末尾每帧一次 —— 因为起飞/落地期间也要跑（状态机在那两段负责维持动作 + 防被抢）。
            _anim.Hold = false;              // 空中态 = 允许自动转移

            // ⑦′ 🔴 **守通道 0**（2026-09-24 立；当日晚改成**常开**）：
            //    飞行姿势全在通道 0。实测「**往通道 1 发一条动作会把通道 0 清掉**」⇒ 腿失去飞行姿
            //    （看着像"下半身一起动了"，其实腿是没人驱动了）。这里每帧查一次通道 0，空了就补回当前飞行姿势
            //    （`Reassert` = 重写一次、不重播计时）。
            //
            //    🔴 原实现只在**我们自己施法**（`SpellCastInput.IsPlayerAiming`）期间守，于是
            //    `custom.anim_ch 1 <原版动作>` 这类**手验根本不走这条路** —— 用户据此看到的"腿也变了"
            //    **证明不了通道 1 有没有遮罩**（那一次通道 0 压根没人补）。现在改成只要在空中就一直守，
            //    手验与施法走同一条路。开关 = `FlightTuning.GuardChannelZero`。
            if (FlightTuning.GuardChannelZero)
            {
                bool ch0Empty = true;
                try { ch0Empty = main.GetCurrentAction(0) == ActionIndexCache.act_none; }
                catch (Exception) { ch0Empty = false; }
                if (ch0Empty && _anim.Reassert(main, blend: 0.05f))
                {
                    _castCh0Restores++;
                    if (_castCh0Restores <= 5)
                    {
                        DebugLogger.Log($"[Flight] 通道 0 空了，已补回飞行姿势（第 {_castCh0Restores} 次）");
                    }
                }
            }

            // ⑧ 机身朝向（🔴 2026-09-21 用户裁定 = **朝实际移动方向**，见下）
            //    有输入 → 朝【实际移动方向】的水平投影：
            //              W 朝镜头前方 / A 朝左 / D 朝右 / S 转身朝镜头（对着玩家）
            //    无输入 → **一个字都不写** ⇒ 保持最后朝向 ⇒ 镜头绕着转能看到各个面、转到正面就是正脸。
            //
            // 🔴 **喂的是 `_velocity`（实际航迹），不是相机方向 `dir`**（2026-09-27 加航向惯性时改）：
            //    航向有了惯性之后，相机方向与实际航迹**会差一个角度**（正在划弧的那段）。
            //    若还按相机方向转机身，身体会比航迹转得快 ⇒ 看着像"斜着平移"（侧滑）。
            //    喂 `_velocity` = 身体永远朝着**真正在走的方向**，与「朝实际移动方向」这条裁定也更贴。
            //    （闪避期间 `_velocity` 是闪避方向，但那一段被下面的 `_dodgeTimer` 挡在外面，不受影响。）
            //
            // 🔴 **本条推翻早先的"飞机式"裁定**（那条要求 A/D 平移时身体不转、始终朝镜头前方）。
            //    两条是相反的，**以现在这条为准**；要改回去只需把 `dir` 换成 `forward`（一行）。
            //
            // 🔴 **闪避位移期间不转**（2026-09-22）：那 4 条闪避动画是相对**身体正前方**做的
            //    （实测：左右闪 = 头 / 腿朝两侧摆），位移时把身体转过去就变成"朝前闪"了，看着不对。
            // 🔴 **施法瞄准优先**（2026-09-24 用户裁定："施法的时候让角色的身体和头看相机无限远处"）：
            //    蓄力 / 引导法术期间，身体朝**相机方向**（不是移动方向）—— 这是施法姿态的一部分。
            //    松手/取消/放完 ⇒ `IsPlayerAiming` 变 false，立刻回到下面那条"朝实际移动方向"。
            //    ⚠️ 这里**不看有没有移动输入**：站着不动也能瞄准（下面那条规则才要求有输入）。
            if (SpellCastInput.IsPlayerAiming)
            {
                TurnBodySmoothed(main, forward, dt);
                AimHeadAtCamera(main, forward);
            }
            else
            {
                StopAimingHead(main);
                // 🔴 **判据从"按着方向键"换成"这一帧有航向"**（2026-09-28）：冲刺的"隐式 W"也是航向，
                //    不然按着 Shift 往前飞、身体却纹丝不动（朝向停在起飞那一下）。
                //    等价性：旧口径下"有方向键"必然产生速度、"没方向键"必然速度归零 ⇒ 两者同真同假。
                if (axis.LengthSquared > 1e-4f && _dodgeTimer <= 0f)
                    TurnBodySmoothed(main, _velocity, dt);
            }

            // ⑧′ 掉下板检测（2026-09-22 用户实机：撞墙时板穿墙、人被墙挡住 ⇒ 人掉下来）
            if (CheckFellOffBoard(main))
                return;

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
                    DebugLogger.Log($"[Flight] 撞地 → 自动落地（板顶={boardTop:F2} 地面={groundZ:F2} 差={boardTop - groundZ:F2} " +
                                    $"下冲={-_velocity.z:F1}m/s）");
                    // 撞地是**唯一**的落地路径（2026-09-27 起：主动退出飞行不再播落地动作）⇒ 一律硬着陆。
                    BeginLanding(main, approachSpeed: -_velocity.z);
                    return;
                }
            }

            // ⑩ 每 0.5 秒打一组诊断 —— 板就算隐藏了，也能靠数字确认「人在不在板上、输入有没有读到」
            //    🔴 受总闸管（默认关）：平时不刷屏，`custom.flight log on` 才开。
            _statusTimer += dt;
            if (FlightTuning.DebugLog && _statusTimer >= 0.5f)
            {
                _statusTimer = 0f;
                LogDiag(mission, main);
            }

            if (FlightTuning.DebugLog && FlightTuning.VerboseLog)
            {
                DebugLogger.Log(string.Format(
                    "[Flight] air v=({0:F1},{1:F1},{2:F1}) |v|={3:F1} pos=({4:F1},{5:F1},{6:F1}) anim={7}",
                    _velocity.x, _velocity.y, _velocity.z, _velocity.Length,
                    _board.Origin.x, _board.Origin.y, _board.Origin.z, _anim.Current ?? "-"));
            }
        }

        /// <summary>
        /// 落地分**两段**（2026-09-22 用户裁定）：
        ///   ① **下降段** —— 板托着人往地面走，**保持待机姿态**（不播落地动画）；
        ///   ② **落地段** —— 触地那一刻**先拆板**（人已在真实地面），**再播落地动画**，演完才收摊。
        ///
        /// 为什么这么排：原来是一边下降一边播着陆动画 ⇒ 看着像"悬空演着陆、脚踩板往下掉"。
        /// **轻放**（空格/主动下降）没有第二段：触地当场收摊，让引擎走跑立刻接管。
        /// </summary>
        private void TickLanding(Agent main, float dt)
        {
            if (_boardRemoved)
            {
                // ② 落地段：人已站在真实地面、板已拆，只剩把动画演完
                // 🔴 **只在没进落地态时才 Force**（2026-09-22 修）：原来每帧无条件 Force 一次，
                //    后果是 ① 日志里刷出 357 行 `land → land` ② 状态机的抖动自检被它触发（每秒 61 次假警告）
                //    ③ 更要命的是"每帧重设动作通道会把动画卡在第 0 帧"（方案里记过的坑）。
                // 🔴 **状态名不在这里**（2026-09-26）：`_landPoseState` 是从 XML 的事件边读出来的
                //    （`<edge from="超人落地" to="outside" …/>` 的 from），编辑器里改名字这边自动跟着变。
                if (!string.IsNullOrEmpty(_landPoseState) && _anim.Current != _landPoseState)
                    _anim.Force(main, _landPoseState, FlightTuning.AnimBlendIn);
                _landTimer += dt;
                // 🔴 **出机时机也从定义里读**（同一条边上的 `anim="remaining" anim-rem-pct="20"`）：
                //    "落地动作剩多少就出机"由 XML 说 —— 不在这里硬编码秒数（换 clip 不用改代码，
                //    而且图上写的与实际生效的是同一个数）。
                //    没写百分比（或那个状态不是一次性 ⇒ 剩余 = +∞）⇒ 回退到 LandAnimSeconds，不会卡死。
                float remain = _anim.CurrentRemainFrac;
                bool animDone = _landExitPct > 0f ? remain * 100f <= _landExitPct
                                                 : _landTimer >= FlightTuning.LandAnimSeconds;
                if (animDone || _landTimer >= FlightTuning.LandMaxSeconds)
                {
                    DebugLogger.Log($"[Flight] 落地动画出机: 用时={_landTimer:F2}s" + (_landExitPct > 0f
                        ? $"（剩 {remain * 100f:F0}% ≤ 定义要求的 {_landExitPct:F0}%）"
                        : "（定义没写 anim-rem-pct ⇒ 按 LandAnimSeconds 兜底）"));
                    FinishFlight(main);
                }
                return;
            }

            // ① 下降段（🔴 **姿态不在这里管**（2026-09-26）：状态机自己按输入演，"落地动作只在地面播"由上面②保证；
            //    这里只管往下走 —— 硬着陆按下冲速度降，别让"刚才还在俯冲"变成慢悠悠飘下来）
            float rate = Math.Max(FlightTuning.LandRate, _landingApproach);
            float groundOriginZ = GetGroundZ(Mission.Current.Scene, _board.Origin) - FlightTuning.CarrierTopLocalZ;
            Vec3 o = _board.Origin;
            float nz = Math.Max(groundOriginZ, o.z - rate * dt);
            _board.MoveTo(new Vec3(o.x, o.y, nz));
            _landTimer += dt;

            if (nz > groundOriginZ + 0.02f)
                return;                                     // 还没触地

            // ── 触地：先拆板（人已经站在真实地面上），再看要不要演动画 ──

            _board.Remove();
            _boardRemoved = true;
            _landTimer = 0f;

            // 🔴 **触地这一刻才 Force 落地动作，而且从 XML 读它是哪个状态**（2026-09-26）：
            //      · **落地动作** = `when="land-trigger"` 那条事件边的 `to`
            //        （`<edge from="冲刺飞行" to="超人落地" when="land-trigger" phase="true"/>`）
            //      · **出机时机**   = `to="outside"` 那条边上的 `anim-rem-pct="20"`
            //      · **"什么时候"** = **本类自己**：板顶触地（硬着陆）——XML 只管"进哪个状态"，
            //        触发时刻在 C#（见本方法上面那段触地判定）。这两半拼起来才是完整的"落地动作"。
            //    定义里没那条边 ⇒ 不接管姿态（Hold 放开），只按 LandAnimSeconds 到点收摊。
            if (_anim.TryEventExit("outside", out string exitState, out float exitPct))
            {
                _landExitPct = exitPct;
                // 落地动作优先读"落地时刻"那条边声明的状态；没写就用出机边的来源（两者不一致就报一行）
                string poseState = _anim.TryEventTarget(FlightAnimConditions.LandTrigger, out string declared)
                    ? declared
                    : exitState;
                if (!string.Equals(poseState, exitState, StringComparison.Ordinal))
                {
                    DebugLogger.Log($"[Flight] ⚠️ 定义不一致：落地时刻那条边说要进 '{poseState}'，" +
                                    $"而 '{exitState}' 才是出机前必须处的状态 —— 以 'land-trigger' 那条为准");
                }
                _landPoseState = poseState;
                _anim.Hold = true;                                     // 落地这一段动画归相位管
                _anim.Force(main, poseState, FlightTuning.AnimBlendIn);
                DebugLogger.Log($"[Flight] 触地：板已拆、人在地面，开始播落地动作 '{poseState}'" +
                                (exitPct > 0f ? $"（剩 {exitPct:F0}% 出机）" : "（定义没写 anim-rem-pct ⇒ 按 LandAnimSeconds 兜底）"));
            }
            else
            {
                _anim.Hold = false;
                _landPoseState = null;
                _landExitPct = -1f;
                DebugLogger.Log("[Flight] ⚠️ 定义里没有 `to=\"outside\"` 的事件边 ⇒ 没有落地动作可播" +
                                "（按 LandAnimSeconds 到点收摊；要落地动作就在 XML 里画一条 机外 的事件边）");
            }
        }

        // ─────────────────────────── 进出 ───────────────────────────

        /// <summary>
        /// **起飞入姿该进哪个状态**（2026-09-26）：① 事件边 `when="takeoff-trigger"` 的 `to`；
        /// ② 没写那条就用"机外 → X"那条边界边。两者都没有 = false（飞行照常，只是没有入姿动画）。
        /// 🔴 这里**没有任何状态名** —— 状态名只存在于 XML。
        /// </summary>
        private bool TryResolveTakeoffState(out string state)
        {
            return _anim.TryEventTarget(FlightAnimConditions.TakeoffTrigger, out state)
                   || _anim.TryEventEnter("outside", out state);
        }

        /// <summary>
        /// 起飞（进机）。<paramref name="ridingBoard"/> = **人已经站在板上**（板载坠落后回飞那条路）——
        /// 这时**不重新召唤板、也不等"踩实"**：板和人的支撑关系从头到尾没断过，
        /// 拆了重召唤等于人为制造一次"悬空变成被托住"（引擎那句按地形重算位置的解算就会趁机发作）。
        /// </summary>
        private void BeginTakeoff(Agent main, bool ridingBoard = false)
        {
            Scene scene = Mission.Current?.Scene;
            if (scene == null)
                return;

            _takeoffSettled = ridingBoard;
            _takeoffTimer = 0f;
            _boardRemoved = false;
            _boardSpawned = ridingBoard;
            _boardSpawnTimer = 0f;
            _takeoffAnimTimer = 0f;
            _airTime = 0f;
            _phase = Phase.Takeoff;
            _velocity = Vec3.Zero;
            _landTimer = 0f;
            _bodyYawDeg = float.NaN;        // 机身朝向重新播种（首次写不插值 = 不甩头）
            _velYawDeg = float.NaN;         // 航向同理（新一次飞行从"当帧就朝镜头"开始，不带着上次的弧）
            _steerIdleTimer = 0f;
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
            // 🔴 **进哪个状态由 XML 说**（2026-09-26）：
            //    ① 先读事件边 `when="takeoff-trigger"` 的 `to`（图上标着"起飞这个时刻"的那条）
            //    ② 没有就用"机外 → X"那条边界边
            //    这里**一个字的状态名都不写** —— 编辑器里改名 / 换状态，这边自动跟着走
            //    （教训：`hoverstart` 被改名成 `进入飞行` 之后，写死的 `Force("hoverstart")` 只是安静地不播动画）。
            //    ⚠️ **"什么时候起飞"不在这里读** —— 那是本类自己的物理判定（空中按空格 / 长按空格，
            //       见 TickGrounded）；XML 只回答"起飞这一刻进哪个状态"。
            if (TryResolveTakeoffState(out string entryState))
            {
                _anim.Hold = true;                                      // 起飞入姿这一段动画归相位管
                _anim.Force(main, entryState, FlightTuning.TakeoffBlendIn, startProgress);
            }
            else
            {
                // 定义里没画"机外 → 某状态" ⇒ 不接管姿态，让状态机按转移表自己走（飞行照常，只是没有入姿）
                _anim.Hold = false;
                DebugLogger.Log("[Flight] ⚠️ 定义里没有起飞入姿的事件边（`when=\"takeoff-trigger\"` 或 `from=\"outside\"`）" +
                                " ⇒ 本次起飞没有入姿动画（要入姿就在 XML 里画一条 机外 → 某状态 的事件驱动边）");
            }

            if (FlightTuning.UseFlightCamera)
            {
                _camEntered = _camRig.Enter(main);
                if (!_camEntered)
                    DebugLogger.Log("[FlightCam] 接管失败，本次飞行用引擎默认相机");
            }

            // 🔴 **起飞那一刻把机身对到镜头前方**（2026-09-22 用户反馈"起飞时身体没朝镜头方向"）。
            //    起因：飞行中"无输入就不写朝向"那条规则（N3，为的是悬停时能绕着看各个面）——
            //    它让**起飞到第一次按方向键之间**（实测可达 2.9 秒）机身一直保持**起飞前**的朝向，
            //    这时候你转镜头，人不会跟。所以在这一刻补写一次（`_bodyYawDeg` 是 NaN ⇒ 直接对齐，
            //    不做插值，不会"转过去"）；之后仍按 N3：有输入才写、无输入不写。
            if (!main.HasMount)
            {
                GetCameraBasis(out Vec3 takeoffFwd, out _);
                if (takeoffFwd.LengthSquared > 0.0001f)
                    TurnBodySmoothed(main, takeoffFwd, 0f);
            }

            DebugLogger.Log($"[Flight] 起飞触发: 动作={_anim.CurrentAction ?? "-"} blendIn={FlightTuning.TakeoffBlendIn:F2}s " +
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

        /// <summary>
        /// **人还在板上吗**？不在 ⇒ 强制收摊（拆板 + 还相机 + 解冻 + 清飞行状态）并返回 true。
        ///
        /// 触发场景（2026-09-22 用户实机）：**撞墙**。板是逐帧 `SetFrame` 瞬移的，**会直接穿墙**，
        /// 而人会被墙体碰撞挡住 ⇒ 人从板上掉下去。不收摊的话会留下坏状态：
        /// 人被冻结（`Controller=AI` + AI 暂停）站在地上走不动、板还在天上飘。
        ///
        /// 判据 = 玩家碰撞体底面 与 板面 的**三维距离**（站着时只有 0.37 米）。
        /// 登板之前不判（那会儿人本来就在板上方一两米）。
        /// </summary>
        private bool CheckFellOffBoard(Agent main)
        {
            if (!_takeoffSettled || !_board.IsSpawned)
                return false;

            float boardTop = _board.Origin.z + FlightTuning.CarrierTopLocalZ;
            float feet = CarrierBoard.CollisionCapsuleBottomZ(main);
            float dz = feet - boardTop;
            float dx = new Vec2(main.Position.x - _board.Origin.x,
                                main.Position.y - _board.Origin.y).Length;

            float limit = FlightTuning.FallOffDistance;
            if (Math.Abs(dz) <= limit && dx <= limit)
                return false;

            DebugLogger.Log($"[Flight] 🔴 玩家脱离载具（离板面 竖直={dz:F2} 水平={dx:F2} 阈值={limit:F2}）" +
                            " —— 强制收摊（拆板/还相机/解冻）");
            AbortFlight();
            return true;
        }

        /// <summary>
        /// **发起一次闪避**（2026-09-22 用户裁定：冲刺中短按空格）。
        ///
        /// 🔴 **本方法只管"位移"**（2026-09-26 澄清）：朝请求方向冲 <see cref="FlightTuning.DodgeDistance"/> 米
        ///    （<see cref="FlightTuning.DodgeDisplaceSeconds"/> 秒内走完）。
        ///    **姿态**由 XML 的状态机自己判 —— `FlyFastPoses → dodgeU keys="Z"`（读的是同一批物理键），
        ///    所以两边不会打架；也不需要 C# 去"请求"任何状态名（那种写法一改名就失效）。
        ///    位移是玩法、动画是表现，**各管各的时长**，谁都不等谁。
        ///
        /// 🔴 **方向只有一个：前闪**（2026-09-27 用户实机感受后裁定）——
        ///    冲刺飞行**不响应 WASD**（方向只决定"往哪飞"，不决定姿态）⇒ 左右/下闪没有输入来源，
        ///    XML 那边也只保留了 `dodgeU` 一条边。位移方向 = **相机前向**（就是你正在飞的方向）。
        /// </summary>
        private void BeginDodge(Vec3 forward)
        {
            _dodgeDir = (forward.LengthSquared > 1e-6f ? forward : new Vec3(0f, 0f, 1f)).NormalizedCopy();
            _dodgeTimer = FlightTuning.DodgeDisplaceSeconds;
            _dodgeCooldown = FlightTuning.DodgeCooldownSeconds;

            // 航向状态**跟着闪避方向走**（2026-09-27）：闪避这 0.4 秒速度整个交给 `_dodgeDir`，
            // 若不同步，位移走完的那一帧航向还停在闪避**之前**的角度 ⇒ 会从旧航向再划一道弧回去。
            SeedSteerFrom(_dodgeDir);

            DebugLogger.Log($"[Flight] 闪避（前闪）方向=({_dodgeDir.x:F2},{_dodgeDir.y:F2},{_dodgeDir.z:F2}) " +
                            $"位移={FlightTuning.DodgeDistance:F1}m/{FlightTuning.DodgeDisplaceSeconds:F2}s " +
                            $"冷却={FlightTuning.DodgeCooldownSeconds:F1}s");
        }

        /// <summary>清掉闪避相关的临时状态（收摊时调）—— 不清的话下次起飞会带着上次的冷却 / 位移。</summary>
        private void ClearDodgeState()
        {
            _dodgeTimer = 0f;
            _dodgeCooldown = 0f;
            _dodgeDir = Vec3.Zero;
        }

        /// <summary>
        /// 进入落地段（**唯一入口 = 撞地**，2026-09-27 起）——
        /// 主动出机（空格）不走这里（<see cref="BeginFalling"/>：交还引擎原生掉落、不播落地动作）。
        /// </summary>
        private void BeginLanding(Agent main, float approachSpeed)
        {
            _phase = Phase.Landing;
            _velocity = Vec3.Zero;
            _landTimer = 0f;
            _landingApproach = approachSpeed;
            _boardRemoved = false;

            // 🔴 **下降段不接管姿态**（2026-09-26 改；用户裁定「数据驱动」）：
            //    让状态机**自己按输入演**（往下冲就是俯冲姿势），唯一要守的规矩是
            //    **"落地动作只在地面播"** —— 由 <see cref="TickLanding"/> 里"触地那一刻才 Force 出机前的姿态"保证。
            //    （改前这里是 `Hold = true` + 写死 Force `"idle"`：那个 idle 是从 XML 读不到的硬编码状态名，
            //      编辑器里一改名就静默失效。）
            _anim.Hold = false;
            DebugLogger.Log($"[Flight] 进入落地（撞地）下冲={approachSpeed:F1}m/s " +
                            $"下降速度={Math.Max(FlightTuning.LandRate, approachSpeed):F1}m/s 落地动作=待触地后播");
        }

        /// <summary>
        /// **出机 ⇒ 开始掉落**（2026-09-27 重做）—— 由"状态机进入 `outside`"触发（**不认任何键**，
        /// 见 <see cref="OnMissionTick"/> ⑦″ 那个调用点）。**按空格出机**与**撞地落地**是两条路，
        /// 后者走 <see cref="BeginLanding"/>（要播落地动作）。
        ///
        /// 🔴🔴 **掉落的物理与动画 100% 是引擎原生的**（用户 2026-09-27 裁定：「原版应该有自己原生的
        ///     跳跃掉落机制」）—— 本类在掉落期间**不写速度、不写位置、不驱动 0 号通道**，
        ///     拆掉板 + 把动作通道还给引擎（<see cref="AgentAnimStateMachine.Release"/>）就完事，
        ///     剩下的（重力、空中姿态、落地）全归引擎。
        ///
        /// 🔴 **但相机与输入闸先留着**，两条都各治一个具体病：
        ///   ① **相机不还**（<see cref="FlightTuning.KeepCameraWhileFalling"/>）：掉落全程镜头跟着，
        ///      玩家才看得清"自己在往下掉、离地还有多高"——出机当场还相机的话，画面一切换，
        ///      掉落过程就读成了一个"镜头跳了一下"。
        ///   ② **输入闸不撤**（<see cref="FlightTuning.HoldInputWhileFalling"/>）：玩家按键**不进引擎**。
        ///      掉落中本来就走不了路，真正的目的是**空格只归我们读** —— 引擎收不到那个键，
        ///      就**不会给玩家起跳**；引擎那次跳的落地解算会按**地形**重算 agent 位置，
        ///      实测（2026-09-27 日志）空中重进机时把玩家从 37 米一帧拽到地面、板留在半空 ⇒ 飞行当场收摊。
        ///      保留输入闸后，重进机全程引擎都没起过跳，那条解算根本不会发生。
        ///
        /// 🔴 **不调 `FlightInput.Reset()`**：Reset 会把"空格仍按着"重新算成一次**按下沿**，
        ///    下一帧 `TickFalling` 就会立刻把玩家重新送上飞机（"一按就退出又进机"）。保持输入状态不动即可。
        /// </summary>
        private void BeginFalling(Agent main, string why)
        {
            float z = 0f;
            try { z = main.Position.z; } catch { /* 取不到就按 0 打日志 */ }

            DebugLogger.Log($"[Flight] 出机 ⇒ 坠落开始（{why}；离地高度={z:F1}m）| " +
                            $"方式={(FlightTuning.FallRide ? "板载（支撑不断，不会触发引擎的地形重算）" : "自由落体（引擎原生；回飞时会被按地形重算）")} · " +
                            $"姿势={_fallPoseState ?? "(定义里没有 fall-trigger 边 ⇒ 退回老写法)"}（由状态机自己驱动） · " +
                            $"相机={(FlightTuning.KeepCameraWhileFalling ? "跟着" : "还给引擎")} · " +
                            $"输入闸={(FlightTuning.HoldInputWhileFalling ? "保留" : "放开")}");

            if (FlightTuning.FallRide)
            {
                // 🔴 **板不拆**：人继续踩在板上（支撑不断）⇒ 不存在"悬空变成被托住"那一瞬间，
                //    引擎那句"按地形重算位置"永远不触发（这是实机定位出来的唯一可行解，见 FlightTuning.FallRide）。
                if (!_board.IsSpawned && !SpawnBoardAtFeet(main))
                    DebugLogger.Log("[Flight] ⚠️ 坠落开始但板不在（网格没进包？）—— 本次坠落只能用自由落体");
                _fallRideVel = 0f;
                _boardSpawned = true;              // 板已有（或刚补上）⇒ 回飞时 TickTakeoff 不必再召唤
                _takeoffSettled = true;            // 人本来就站在板上 ⇒ 不必再等"踩实"
            }
            else
            {
                _board.Remove();                   // 自由落体：拆板
                _anim.Release(main);               // 0 号通道还给引擎 ⇒ 空中姿势由引擎自己演
            }
            // 🔴 姿势**不需要这里 Force**（2026-09-27 第二轮用户裁定）：出机那条边直接连到「坠落」，
            //    机器自己进了那个状态并驱动通道 0 了；C# 只等它说"机外"（触地时送它出机）。
            //    定义里没声明坠落状态（`_fallPoseState == null`）⇒ 那一档的机器还停在机外，姿势本就归引擎。

            _phase = Phase.Falling;
            _velocity = Vec3.Zero;
            _landTimer = 0f;
            _boardRemoved = false;
            _fallTimer = 0f;
            _fallSpeedZ = 0f;
            _hasFallPrevZ = false;
            _fallLogTimer = 0f;
            _fallLogLines = 0;
            ClearDodgeState();

            if (!FlightTuning.KeepCameraWhileFalling)
                ExitCamera();           // 渐变归还；Tick 里会继续推进到还完（`_camRig.IsHandingBack`）

            if (!FlightTuning.HoldInputWhileFalling)
                ExitFreeze();
        }

        /// <summary>
        /// **掉落中**（每帧）—— 只做三件事：**空格回飞** · **着地收工** · 打几行诊断。
        /// 掉落本身不归我们管（见 <see cref="BeginFalling"/>），所以这里没有任何运动代码。
        /// </summary>
        private void TickFalling(Agent main, float dt)
        {
            _fallTimer += dt;
            UpdateFallSpeed(main, dt);

            // ① **板载坠落**：板运着人往下加速掉 —— 人**始终踩在板上**（支撑不断）。
            //    🔴 这是"不触发引擎那次地形重算"的关键：引擎只在"agent 由悬空变成被托住"那一瞬间重算，
            //       支撑不断就永远不触发（正常飞行时板以 26 m/s 下坠也从来不出事，就是这个道理）。
            if (FlightTuning.FallRide && _board.IsSpawned)
            {
                _fallRideVel = Math.Min(FlightTuning.FallRideTerminal,
                                        _fallRideVel + FlightTuning.FallRideAccel * dt);
                Vec3 o = _board.Origin;
                float groundOriginZ = GetGroundZ(Mission.Current?.Scene, o) - FlightTuning.CarrierTopLocalZ;
                float nz = Math.Max(groundOriginZ, o.z - _fallRideVel * dt);
                _board.MoveTo(new Vec3(o.x, o.y, nz));
                if (nz <= groundOriginZ + 0.02f)
                {
                    LandFromFall(main, "坠落触地（板载：板顶落到地面，人随之落地）");
                    return;
                }
            }

            // ② 空格 ⇒ 重新进机（与地面那条"空中按空格起飞"同一个手势、同一段入姿）。
            //    🔴 板载坠落时**不重新召唤板**（人正踩在上面）—— 那两个 `_boardSpawned/_takeoffSettled`
            //       已经在 BeginFalling 里置真，TickTakeoff 会直接从"交回控制"那一步走。
            if (FlightInput.ConsumeSpacePress())
            {
                DebugLogger.Log($"[Flight] 坠落中按空格 ⇒ 重新进机（已坠 {_fallTimer:F2}s，下落 {_fallSpeedZ:F1} m/s，板速 {_fallRideVel:F1}）");
                BeginTakeoff(main, ridingBoard: FlightTuning.FallRide && _board.IsSpawned);
                return;
            }

            // ③ 着地 ⇒ 把相机与输入还给玩家（宽限 0.2 秒：出机那一两帧板还托着脚，`IsOnLand` 可能仍是真）。
            //    ⚠️ **只在自由落体档用这条**：板载档下 `IsOnLand` 全程为真（人一直站在板上），
            //       它的着地由上面 ① 的"板顶触地"判。
            if (!FlightTuning.FallRide && main.IsOnLand() && _fallTimer >= FallLandGraceSeconds)
            {
                LandFromFall(main, $"坠落着地（用时 {_fallTimer:F2}s）");
                return;
            }

            // ④ 兜底：坠不出结果（场景怪 / 卡住）也不能把玩家永久冻在天上
            if (_fallTimer >= FallGiveUpSeconds)
            {
                DebugLogger.Log($"[Flight] ⚠️ 坠落超过 {FallGiveUpSeconds:F0}s 仍未着地 ⇒ 强制交还控制 | {DescribeFall(main)}");
                EndFalling();
                return;
            }

            // ⑤ 诊断：每 0.5 秒一行、封顶 6 行（坠一分钟也只多 6 行）。
            //    🔴 **看 `差`（胶囊底 − pos.z）**：正常 ≈ 0.4；它忽然变成几十米 = agent 的位置
            //       被引擎按地形重算过（就是板载要避免的那句解算）—— 这是排查"回飞被摔到地上"的唯一书证。
            _fallLogTimer += dt;
            if (_fallLogTimer >= 0.5f && _fallLogLines < 6)
            {
                _fallLogTimer = 0f;
                _fallLogLines++;
                DebugLogger.Log("[Flight-Diag] " + DescribeFall(main));
            }
        }

        /// <summary>
        /// **坠落落地**：把机器**送出机外**（定义里声明了坠落状态时）—— 之后由主循环那条通用规则
        /// 收摊（拆板 / 还通道 / 还相机 / 解冻）。定义里没声明坠落状态的兜底档直接收 ✓。
        /// 🔴 走状态机这条路是用户 2026-09-27 的裁定：「坠落动画监听落地事件后才 outside」——
        ///    出机与出机的收尾**都写成边**，C# 只遵守"机器说机外 ⇒ 收摊"。
        /// </summary>
        private void LandFromFall(Agent main, string why)
        {
            DebugLogger.Log($"[Flight] {why} | {DescribeFall(main)}");
            if (!string.IsNullOrEmpty(_fallPoseState))
                _anim.Force(main, AgentAnimStateMachine.OutsideState, FlightTuning.AnimBlendIn);
            else
                EndFalling();       // 兜底档：机器本来就停在机外，没有"送它出机"这一步
        }

        /// <summary>着地 / 放弃：拆板 + 还动作通道 + 把相机与输入还给玩家（幂等）。</summary>
        private void EndFalling()
        {
            _phase = Phase.Grounded;
            _fallTimer = 0f;
            _fallSpeedZ = 0f;
            _fallRideVel = 0f;
            _hasFallPrevZ = false;
            // 🔴 **坠落姿势是相位 `Force` 进去 + `Hold` 住的**（板载档），收摊必须两件都还：
            //    拆板（不然地上留一块托脚的法阵）+ 把 0 号通道还给引擎。
            //    漏了 Release 的后果很具体：`Hold` 一直是真、状态机永远停在「坠落」⇒ 人锁死在坠落姿势里。
            _board.Remove();
            Agent main = Agent.Main;
            if (main != null && AgentControlHelper.SafeIsActive(main))
                _anim.Release(main);
            ExitCamera();
            ExitFreeze();
            FlightInput.Reset();
        }

        /// <summary>坠落进度一行（诊断用）。</summary>
        private string DescribeFall(Agent main)
        {
            float z = 0f, bottom = 0f;
            bool onLand = false;
            string anim = "-";
            try
            {
                z = main.Position.z;
                bottom = CarrierBoard.CollisionCapsuleBottomZ(main);
                onLand = main.IsOnLand();
                // ⚠️ 用 `.Name` —— `ActionIndexCache.ToString()` 没被重写，打出来是类型名（这个坑踩过一次）
                anim = main.GetCurrentAction(0).Name ?? "-";
            }
            catch { /* 诊断自己出问题不该影响坠落 */ }
            return string.Format(
                "坠落 t={0:F2}s 速度={1:F1}m/s 板速={2:F1} pos.z={3:F2} 胶囊底={4:F2} 差={5:F2} 着地={6} anim={7}",
                _fallTimer, _fallSpeedZ, _fallRideVel, z, bottom, bottom - z, onLand ? 1 : 0, anim);
        }

        /// <summary>掉落竖直速度（自算：引擎的 `GetCurrentVelocity` 只有水平两个分量）。</summary>
        private void UpdateFallSpeed(Agent main, float dt)
        {
            float z;
            try { z = main.Position.z; }
            catch { return; }
            if (_hasFallPrevZ && dt > 0f)
                _fallSpeedZ = (z - _fallPrevZ) / dt;
            _fallPrevZ = z;
            _hasFallPrevZ = true;
        }

        private void FinishFlight(Agent main)
        {
            _board.Remove();
            _anim.Release(main);        // 把 0 号通道还给引擎（走跑系统重新接管）

            _phase = Phase.Grounded;
            _velocity = Vec3.Zero;
            _landTimer = 0f;
            _bodyYawDeg = float.NaN;
            _velYawDeg = float.NaN;
            _steerIdleTimer = 0f;
            _takeoffSettled = false;
            _takeoffTimer = 0f;
            _boardRemoved = false;
            ClearDodgeState();
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
                _anim.Release(main);    // 收摊：通道还给引擎（内部已 try/catch）

            _phase = Phase.Grounded;
            _velocity = Vec3.Zero;
            _landTimer = 0f;
            _bodyYawDeg = float.NaN;
            _velYawDeg = float.NaN;
            _steerIdleTimer = 0f;
            _takeoffSettled = false;
            _takeoffTimer = 0f;
            _boardRemoved = false;
            ClearDodgeState();
            ExitCameraImmediate();      // 🔴 强制收摊不拖时间（不还 = 场景内相机永远被我们接管）
            ExitFreeze();
            FlightInput.Reset();
        }

        // ─────────────────────────── 运动相机（N5，2026-09-21）───────────────────────────

        /// <summary>
        /// 算出本帧喂给相机的**运动量**（2026-09-28，运动驱动的入口）。
        ///
        /// **喂哪两个量、为什么不喂"速度"**：我们的速度**大小**只有 0 / 9 / 26 三档（Shift 一按一换），
        /// 拿它驱参数只会得到三个台阶；真正连续的是
        /// · **竖直速率** = 木板速度的 z 分量（相机内部取绝对值）—— 俯冲/爬升越快，视场越广、镜头越远
        /// · **航向角速度** = 上一步航向惯性算出来的那个（**先平滑再用** —— 照抄 UE 侧 `FInterpTo(…, 5)` 的口径；
        ///   不平滑的话，转向"够到目标角"的那一帧角速度会从 180 突降到 0，侧倾会顿一下）
        ///
        /// 坠落相位单独给：那时 `_velocity` 是零，真实下坠速度在 `_fallRideVel` 里 ——
        /// 不喂它的话**俯冲坠落反而没有速度感**（而坠落正是最快的时候，36 m/s）。
        /// </summary>
        private SpringArmMotion BuildCamMotion(float dt)
        {
            // 航向角速度平滑（首次趋近速率 5/秒；dt 大时直接到位，不过冲）
            _velYawRateDeg += (_velYawRateRaw - _velYawRateDeg) * Math.Min(1f, 5f * dt);

            if (_phase == Phase.Falling)
                return new SpringArmMotion { Vz = _fallRideVel, YawRate = 0f, Speed = _fallRideVel };

            return new SpringArmMotion { Vz = _velocity.z, YawRate = _velYawRateDeg, Speed = _velocity.Length };
        }

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
            // 🔴 **掉落阶段同理用悬停机位**（2026-09-27）：那会儿板已经没了，按输入/冲刺挑机位没有意义，
            //    固定成悬停机位 = 镜头稳稳跟着人掉（玩家才看得清自己在下坠）。
            if (_phase == Phase.Landing || _phase == Phase.Falling)
            {
                _camPreset = FlightCamPreset.Hover;
                return;
            }

            bool boosting = FlightInput.BoostHeld;

            // 🔴 **施法期间不进瞄准机位**（2026-09-25 用户要求）：右键同时是施法键，
            //    而瞄准机位是过肩近景（人物偏左）⇒ 一蓄力镜头就贴上去、腿被挤出画面，
            //    没法验证"施法时腿保持飞行姿势"。开关 = `FlightTuning.AimCameraWhileCasting`。
            bool castingNow = SpellCastInput.IsPlayerAiming;
            if (FlightTuning.AimOnRightClick && FlightInput.AimHeld && !boosting
                && (FlightTuning.AimCameraWhileCasting || !castingNow))
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

        /// <summary>
        /// 归还相机（幂等）。**落地 / 收摊 / 异常都要走这里** —— 不还 = 场景内相机永远被接管。
        ///
        /// 🔴 **正常收摊走"渐变归还"**（2026-09-22 用户要求）：先按接管时记下的默认相机视距/FOV 渐变过去，
        /// 再撒手 —— 否则是硬切（实测引擎 3.4/65 vs 我们巡航 5.5/70、冲刺 9/80，俯冲落地那下最明显）。
        /// 渐变期间相机仍归我们（`_camEntered` 保持 true，Tick 继续推进），由 rig 走完后自己撒手。
        /// **异常/强制收摊**走 <see cref="ExitCameraImmediate"/>（不拖时间）。
        /// </summary>
        private void ExitCamera()
        {
            if (!_camEntered)
                return;
            _camRig.BeginHandBack();
            if (!_camRig.IsActive)          // 兜底：rig 本来就没接管/已异常归还
                _camEntered = false;
        }

        /// <summary>立即归还（异常 / 强制收摊用 —— 不渐变，保证一定还回去）。</summary>
        private void ExitCameraImmediate()
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

        /// <summary>飞行输入监听的**一个键**：怎么读它的原始状态 + 日志里叫什么 +（可选）按下/抬起的备注。</summary>
        private readonly struct KeyEdge
        {
            public readonly Func<bool> Read;
            public readonly string Name;
            public readonly string DownNote;
            public readonly string UpNote;

            public KeyEdge(Func<bool> read, string name, string downNote = "", string upNote = "")
            {
                Read = read; Name = name; DownNote = downNote; UpNote = upNote;
            }
        }

        /// <summary>
        /// **飞行要监听的键** —— 判据是"**这个键能引起飞行状态变化**"，用户 2026-09-25 划定：
        /// WASD / 小键盘方向（两者等价，都是飞行方向键）· 空格（起飞 / 闪避 / 落地 / 长按下降）·
        /// Shift（冲刺模式）· 右键（蓄力 / 瞄准机位）· 左键（发射）。
        ///
        /// 🔴 读的全是 <see cref="FlightInput"/> 的**原始读数**（`Diag*`，绕开一切逻辑）——
        ///    所以"按了没反应"能一眼分清是"键没读到"还是"逻辑没走"。
        /// 🔴 **不许改成读 `BoostHeld` / `AimHeld` 这类逻辑量**：UI 门控会把逻辑输入清零
        ///    （<see cref="FlightInput.Reset"/>），拿逻辑量当"按键日志"会把**门控**误报成**玩家松手**
        ///    —— 那正是"我明明没松手，角色却停了"最难查的一种。原始读数 + 下面那条门控行一起看，
        ///    三种原因（玩家真的松手 / 引擎没读到 / 被门控清空）才能分开。
        /// </summary>
        private static readonly KeyEdge[] LoggedKeys =
        {
            new KeyEdge(() => FlightInput.DiagWDown, "W"),
            new KeyEdge(() => FlightInput.DiagADown, "A"),
            new KeyEdge(() => FlightInput.DiagSDown, "S"),
            new KeyEdge(() => FlightInput.DiagDDown, "D"),
            new KeyEdge(() => FlightInput.DiagNumpad8Down, "小键盘8"),
            new KeyEdge(() => FlightInput.DiagNumpad4Down, "小键盘4"),
            new KeyEdge(() => FlightInput.DiagNumpad2Down, "小键盘2"),
            new KeyEdge(() => FlightInput.DiagNumpad6Down, "小键盘6"),
            new KeyEdge(() => FlightInput.DiagSpaceDown, "空格"),
            new KeyEdge(() => FlightInput.DiagShiftDown, "Shift", "冲刺（快移）", "松开冲刺"),
            new KeyEdge(() => FlightInput.DiagRightMouseDown, "右键", "蓄力 / 瞄准机位", "松开右键"),
            new KeyEdge(() => FlightInput.DiagLeftMouseDown, "左键", "发射（飞行中施法）", "松开左键"),
        };

        /// <summary>每个键**上一帧**的状态 —— 只用来判"变没变"。</summary>
        private readonly bool[] _loggedKey = new bool[LoggedKeys.Length];

        /// <summary>
        /// **把原始按键与"动画还剩多久"填进上下文**（每帧一次，**在状态机 Tick 之前**，与飞行相位无关）——
        /// 这样 XML 里就能直接写 `keys="W+Shift"` / `anim="remaining" anim-rem-pct="20"`（**百分比**），
        /// 不必为每种条件再登记一个 C# 谓词。
        /// 读的是 <see cref="FlightInput.RawKeyAt"/>（原始键，绕开一切逻辑）。
        /// 🔴 只填**电平**（按住 / 没按住）—— "沿"那两档已删，「刚按下一帧」会被起飞/落地 Hold 与施法让位吞掉。
        /// </summary>
        private void UpdateKeyFacts()
        {
            for (int i = 0; i < AnimPrimitives.KeyCount; i++)
            {
                bool held = FlightInput.RawKeyAt(i);
                // 🔴 **C（闪避键）对状态机报"没按"，直到玩家松开**（2026-09-27）：
                //    玩法这一侧是**动作级**的（一次按下 = 一次闪避 + 一段位移 + 一个冷却），
                //    而姿态那一侧由 XML 的 `keys="Z"` 直接触发、读的是**电平**。
                //    照实报"按着"，按住不放就会在冷却结束那一刻再触发一次闪避姿态 ——
                //    而玩法那边没有对应的位移（按下沿早用掉了）⇒ **看着像"闪了一下、人没动"**。
                //    ⇒ 让状态机看到与玩法**一致**的事实：这一次按下已经用过了。
                if (i == DodgeKeyIndex)
                {
                    if (!held)
                        _dodgeKeyUsed = false;      // 松开 ⇒ 重新武装
                    else if (_dodgeKeyUsed)
                        held = false;               // 用过了 ⇒ 对状态机报"没按"
                }
                _animCtx.SetKey(i, held);
            }
            _animCtx.AnimRemainFrac = _anim.CurrentRemainFrac;
        }

        /// <summary>闪避键（C）在 `AnimPrimitives.Keys` 里的下标（-1 = 没对上 ⇒ 上面那条抑制逻辑自动失效，不崩）。</summary>
        private static readonly int DodgeKeyIndex = AnimPrimitives.KeyIndex("Z");

        /// <summary>上一帧是否处于"UI 门控接管输入"状态（只在进入那一帧打一行）。</summary>
        private bool _loggedUiBlock;

        /// <summary>
        /// **输入的"按下 / 抬起"日志**（2026-09-25 立；跟着状态机那个开关 <see cref="AnimDebug.Trace"/> ——
        /// **默认关**，要看得 `custom.anim_log on`）。
        ///
        /// 为什么要有它：用户原话——"不然我都不知道按了 Shift 到底有没有反应"。
        /// 状态机那条切换行只说"动画变了"，不说是**哪个输入**让它变的；这一行补上前半截，
        /// 于是一条链路在日志里能连着读：`Shift↓` → `hovermove → fastmoveStart` → `✓ fastmoveStart 生效`。
        ///
        /// 🔴 **防刷屏的全部机制就一条：只在"和上一帧比变了"的那一帧打** ——
        ///    保持按着、保持没按，都不打。所以按一次键 = 两行（按下 + 抬起），长按不刷屏。
        ///
        /// 两条门控（不满足就**只同步游标、不打**）：
        ///   · **地面不打**（`_phase == Grounded`）—— <see cref="FlightInput.Tick"/> 在地面也一直在读键，
        ///     在城里跳一下就刷一行没意义。触发起飞的那一次空格由"二段跳起飞"那行自己交代。
        ///   · **被 UI 门控时不打** —— 进菜单会把键状态清零，不拦的话"开个菜单"就会报一串假的"抬起"。
        /// </summary>
        private void LogInputEdges()
        {
            // 🔴 **门控行**：进菜单 / ESC / 对话时 `FlightInput.Reset()` 会把**逻辑**输入清零
            //    （WASD 轴归零、Shift 视作松开）⇒ 飞行这边表现成"我没松手，角色却停了"。
            //    原始按键**不受影响**（上面表里读的就是原始读数），所以这一行是那种情况的**唯一书证**。
            if (FlightInput.BlockedByUi != _loggedUiBlock)
            {
                _loggedUiBlock = FlightInput.BlockedByUi;
                if (_loggedUiBlock && AnimDebug.Trace && _phase != Phase.Grounded)
                {   // ⚠️ 地面不打：`FlightInput.Tick` 在地面也跑，不拦的话"每次开菜单"都刷一行
                    DebugLogger.Log("[Flight-In] ⚠️ UI 门控接管输入（飞行逻辑收到空输入；原始按键仍在读，见上面的键行）");
                }
            }

            bool loggable = AnimDebug.Trace
                            && _phase != Phase.Grounded      // 飞行全程都算（含起飞 / 落地那两段，玩家也在按键）
                            && !FlightInput.BlockedByUi;

            for (int i = 0; i < LoggedKeys.Length; i++)
            {
                bool now = LoggedKeys[i].Read();
                if (now == _loggedKey[i])
                    continue;                        // 🔴 保持状态不打印 —— 防刷屏就靠这一行
                _loggedKey[i] = now;
                if (!loggable)
                    continue;                        // 只同步、不打（免得起飞 / 开菜单时补报一串假事件）

                KeyEdge k = LoggedKeys[i];
                string arrow = now ? "↓" : "↑";
                string note = now ? k.DownNote : k.UpNote;
                DebugLogger.Log(string.IsNullOrEmpty(note)
                    ? $"[Flight-In] {k.Name}{arrow}"
                    : $"[Flight-In] {k.Name}{arrow} {note}");
            }
        }

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

            // 上限 = 板最快能带出来的量；再加点容忍量。超过就是别人在动他。
            // 🔴 **掉落期间没有"板速"这个上限了**（板已经拆了，人是引擎在往下拽）——
            //    改用一个宽松的 60 m/s 上限：正常自由落体（几十米高也就 30 m/s 上下）不会误报，
            //    而"被引擎按地形重算位置"那种瞬移（实测一帧 20~35 米 = 上千 m/s）照样抓得到。
            float allowed = (_phase == Phase.Falling ? FallWatchSpeedLimit : FlightTuning.BoostSpeed + 2f) * dt + 0.05f;
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
            // 🔴 `steer=` 是**航向惯性**的判据（2026-09-27）：左边 = 实际航向角、右边 = 相机给的目标角。
            //    两个数**持续不等** = 正在划弧（惯性生效）；永远相等 = 归零了 / 初速就一致。
            //    `steerRate=0` 时它必然恒等 —— 这就是"回到旧行为"的对照。
            string steerTxt = float.IsNaN(_velYawDeg)
                ? "steer=-"
                : string.Format("steer={0:F0}°→{1:F0}° (off {2:F0}°, rate={3:F0}/s)",
                                _velYawDeg, _steerTargetYawDeg, Normalize180(_steerTargetYawDeg - _velYawDeg),
                                FlightInput.BoostHeld ? FlightTuning.SteerRateBoostDegPerSec : FlightTuning.SteerRateDegPerSec);
            DebugLogger.Log(string.Format(
                "[Flight-Diag] 引擎算法 look=({0:F2},{1:F2},{2:F2}) right=({3:F2},{4:F2},{5:F2}) | 相机帧 .f=({6:F2},{7:F2},{8:F2}) .u=({9:F2},{10:F2},{11:F2}) | vel=({12:F2},{13:F2},{14:F2}) |v|={15:F1} climb={16} camPitch={17} anim={18} | {19}",
                engF.x, engF.y, engF.z, engR.x, engR.y, engR.z,
                cf.x, cf.y, cf.z, cu.x, cu.y, cu.z,
                _velocity.x, _velocity.y, _velocity.z,
                _velocity.Length, _climbLatch.Value, _pitchLatch.Value, _anim.CurrentAction ?? "-", steerTxt));
        }

        /// <summary>
        /// 更新动画状态机的"当前帧事实"（**必须在 <see cref="AgentAnimStateMachine.Tick"/> 之前调**）。
        ///
        /// 这里只做"读量 + 算迟滞档"，**不做任何决策** —— 决策全在 <see cref="FlightAnimMachine"/> 那份注册的表里。
        /// </summary>
        private void UpdateAnimContext(Vec3 camForward)
        {
            _animCtx.MoveInput = FlightInput.HasMoveInput;                                  // 纯输入（这一帧按着方向键）
            _animCtx.Moving = _animCtx.MoveInput || _velocity.LengthSquared > 1f;           // 输入 或 惯性滑行
            _animCtx.Boost = FlightInput.BoostHeld;

            // 🪦 2026-09-25：这里原来消费"冲刺键按下沿"喂给状态机（进快移入姿）。已删 ——
            //    入姿的判据改成"按着 Shift 且在动"（键原语 / 命名谓词），不再需要一帧就消失的按下沿。
            // 🪦 2026-09-26：`DodgeRequest`（闪避请求）也删了 —— 闪避姿态由 XML 的 `keys="Space+A"` 直接触发。

            // 俯仰档（带迟滞）：进用大阈值、出用小阈值。
            // 实测姿态之间是 120°~180° 的大翻转，单阈值下镜头停在阈值附近会让动画来回翻。
            _pitchLatch.SetThresholds(FlightTuning.PitchThreshold, FlightTuning.PitchExitThreshold);
            if (!_animCtx.Moving)
                _pitchLatch.Reset();          // 悬停不动时把档位归零，下次一动重新判
            else
                _pitchLatch.Update(camForward.z);   // 相机前方向的竖直分量 = 俯仰
            _animCtx.PitchBand = _pitchLatch.Value;
            // 🪦 2026-09-28：姿态**不再读 `PitchBand`**（那是"镜头朝哪"）—— 改读下面的升降档
            //    （`ClimbBand` = "实际在往哪飞"）。理由与口径见 `FlightAnimContext.ClimbBand` 的注释。

            // 升降档（带迟滞；2026-09-28）：驱动"抬头 / 低头"姿态，**跟实际运动走**（照抄 UE 的竖直速度口径）。
            // 归一化口径 = **速度单位向量的 z 分量**（= 航迹倾角的正弦）⇒ 与速度大小无关
            // （巡航 9 与冲刺 26 在同样的倾角下进同一个档）；单位向量是"方向"，所以速度 ~0 时给 0。
            _climbLatch.SetThresholds(FlightTuning.PitchThreshold, FlightTuning.PitchExitThreshold);
            float speedNow = _velocity.Length;
            float velDirZ = speedNow > 0.01f ? _velocity.z / speedNow : 0f;
            _climbLatch.Update(velDirZ);
            _animCtx.ClimbBand = _climbLatch.Value;

            // 压弯档（带迟滞）：**横移输入 A/D** 的符号就是方向（−1 = A = 左压 / +1 = D = 右压）。
            // 阈值可热调：`tune bank` / `tune bankout`；两个都填 0 = 关掉压弯（永远不进压弯状态）。
            _bankLatch.SetThresholds(FlightTuning.BankThreshold, FlightTuning.BankExitThreshold);
            _bankLatch.Update(FlightInput.MoveAxis.x);
            _animCtx.BankBand = _bankLatch.Value;
            // 施法蓄力（决定进施法手势状态；释放那一下由下面 Force 进）
            _animCtx.SpellCharging = SpellCastInput.IsPlayerAiming;
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
        /// 🔴 **头看向相机方向的"无限远处"**（2026-09-24 用户裁定）—— 走引擎原生接口：
        ///    `Agent.SetLookToPointOfInterest(Vec3 点)`（反编译 `Agent.cs:2170`）+ `DisableLookToPointOfInterest()`（`:3797`）。
        /// 🔴 POI 是**粘性**的（设一次一直有效），所以：**进瞄准设一次、退出关一次**（<see cref="_headAimActive"/> 记账）。
        ///    不用每帧设；也**不去动别人的 POI** —— 原版对话/场景也用这个口，没我们开的就绝不关。
        /// </summary>
        private void AimHeadAtCamera(Agent agent, Vec3 forward)
        {
            if (forward.LengthSquared < 1e-6f)
                return;
            try
            {
                Vec3 eye = agent.Position;
                eye.z += agent.GetEyeGlobalHeight();
                agent.SetLookToPointOfInterest(eye + forward * 200f);   // 200 m ≈ 无限远（POI 只要方向对）
                _headAimActive = true;
            }
            catch (Exception)
            {
                // 头朝向只是观感，失败不该影响飞行 / 施法
            }
        }

        /// <summary>撤掉**我们自己设的** POI（幂等；别人的不动）。</summary>
        private void StopAimingHead(Agent agent)
        {
            if (!_headAimActive)
                return;
            try { agent.DisableLookToPointOfInterest(); } catch (Exception) { }
            _headAimActive = false;
        }

        /// <summary>
        /// **航向惯性**（2026-09-27 用户裁定，A 方案）—— 把"实际飞行方向"从当前航向以
        /// `SteerRateDegPerSec × dt` 的角速度朝相机方向转过去，而不是当帧就换。
        ///
        /// **为什么要它**：加它之前 `_velocity = 相机方向 × 定速` 是**每帧从头算**的 ——
        /// 转镜头当帧航向就换。冲刺 26 m/s 时甩一下镜头 = 画面整个横过来、人却像没有质量。
        /// 加上限速之后，掉头是**划一道弧**（180°/s 配 26 m/s ⇒ 转弯半径约 8 米）。
        ///
        /// **口径**（与 <see cref="TurnBodySmoothed"/> 同一套思路：先算目标角，再按速率走最短弧）：
        /// · **两档角速度** —— 悬停/巡航用 <see cref="FlightTuning.SteerRateDegPerSec"/>、
        ///   冲刺（按住 Shift）用 <see cref="FlightTuning.SteerRateBoostDegPerSec"/>（更低 ⇒ 高速转向更"重"）。
        ///   **任填 0 = 瞬时**（回到 2026-09-27 之前的旧行为）。
        /// · **首帧不插值**：<see cref="_velYawDeg"/> 是 NaN（起飞/落地时重置）就直接取目标角 ——
        ///   否则起飞第一帧会从"上一次飞行的航向"处慢慢转过来，多一次甩头。
        /// · **只管方向、不管大小**：速度大小仍是 Shift 一按一松当帧切（9 ⇄ 26）。
        /// · **无输入时不由这里管**：调用方只在有方向输入时调它；"停稳后重新播种"也在调用方
        ///   （见 `TickAirborne` 与 <see cref="FlightTuning.SteerIdleResetSeconds"/>）。
        ///
        /// 🔴 **不写的时候会不会与真实航向脱钩**：不会。飞行期间玩家输入被冻结、AI 被暂停，
        ///    没有第三方会动航向；无输入时我们既不写也不改它，下次给输入时它仍是上次的实际航向。
        /// </summary>
        private Vec3 SteerVelocityDir(Vec3 target, float dt)
        {
            float targetYaw = (float)(Math.Atan2(target.y, target.x) * (180.0 / Math.PI));
            float targetPitch = (float)(Math.Asin(Math.Max(-1f, Math.Min(1f, target.z))) * (180.0 / Math.PI));
            _steerTargetYawDeg = targetYaw;

            float rate = FlightInput.BoostHeld ? FlightTuning.SteerRateBoostDegPerSec
                                               : FlightTuning.SteerRateDegPerSec;

            if (float.IsNaN(_velYawDeg) || rate <= 0f)
            {
                _velYawDeg = targetYaw;          // 首帧 / 关掉惯性 = 瞬时到位
                _velPitchDeg = targetPitch;
                _velYawRateRaw = 0f;             // 瞬时到位不算"在转"（否则相机侧倾会跟着抽一下）
            }
            else
            {
                float step = rate * dt;

                float dYaw = Normalize180(targetYaw - _velYawDeg);
                float appliedYaw = (Math.Abs(dYaw) <= step) ? dYaw : Math.Sign(dYaw) * step;
                _velYawDeg += appliedYaw;
                // 实际转过去的角速度（度/秒）—— 相机侧倾的驱动量（照 UE 那套"角速度驱动侧倾"）
                _velYawRateRaw = dt > 1e-5f ? appliedYaw / dt : 0f;

                float dPitch = targetPitch - _velPitchDeg;   // 俯仰在 ±90 以内，不会绕圈，不用归一化
                _velPitchDeg += (Math.Abs(dPitch) <= step) ? dPitch : Math.Sign(dPitch) * step;
            }

            float yawRad = _velYawDeg * (MathF.PI / 180f);
            float pitchRad = _velPitchDeg * (MathF.PI / 180f);
            float cp = MathF.Cos(pitchRad);
            return new Vec3(cp * MathF.Cos(yawRad), cp * MathF.Sin(yawRad), MathF.Sin(pitchRad));
        }

        /// <summary>
        /// 把航向状态**播种**成 `dir` —— 给"方向换了但速度没断"的接缝用（闪避开始那一刻）。
        /// 传零向量 = 退回"未播种"（下次有输入时当帧到位）。
        /// </summary>
        private void SeedSteerFrom(Vec3 dir)
        {
            if (dir.LengthSquared < 1e-6f)
            {
                _velYawDeg = float.NaN;
                _steerIdleTimer = 0f;
                return;
            }

            Vec3 d = dir.NormalizedCopy();
            _velYawDeg = (float)(Math.Atan2(d.y, d.x) * (180.0 / Math.PI));
            _velPitchDeg = (float)(Math.Asin(Math.Max(-1f, Math.Min(1f, d.z))) * (180.0 / Math.PI));
            _steerTargetYawDeg = _velYawDeg;
            _steerIdleTimer = 0f;
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
        /// <summary>控制台强制起飞（绕过长按）。掉落中调用 = **直接回飞**（等价于掉着按空格）。返回一句英文回执。</summary>
        public string ForceStart()
        {
            if (_phase != Phase.Grounded && _phase != Phase.Falling)
                return "already flying";
            Agent main = Agent.Main;
            if (main == null || !AgentControlHelper.SafeIsActive(main))
                return "no player agent";
            BeginTakeoff(main, ridingBoard: _phase == Phase.Falling && FlightTuning.FallRide && _board.IsSpawned);
            string result = _phase == Phase.Takeoff ? "takeoff started" : "takeoff failed (carrier spawn failed?)";
            // 🔴 MCM 总闸关着时这条命令**仍然生效**（显式开发/验收命令不受玩家开关约束），
            //    但要说清楚 —— 否则"命令飞得起来、键盘飞不起来"看着像 bug。
            if (!Settings.Instance.FlightEnabled)
                result += "  [note: Mod Options 'Flight' is OFF - keyboard takeoff stays disabled; this console command bypasses it]";
            return result;
        }

        /// <summary>控制台强制出机（= 与"空中按空格"同一条路：引擎原生掉落接管，掉着按空格可回飞）。</summary>
        public string ForceStop()
        {
            if (_phase == Phase.Grounded)
                return "not flying";
            if (_phase == Phase.Falling)
                return $"already falling ({_fallTimer:F2}s, vz={_fallSpeedZ:F1})";
            Agent main = Agent.Main;
            if (main == null || !AgentControlHelper.SafeIsActive(main))
            {
                AbortFlight();
                return "aborted (no player agent)";
            }
            BeginFalling(main, "custom.flight stop");
            return "falling (engine-native drop; press Space in air or custom.flight start to re-enter)";
        }

        /// <summary>控制台强制闪避一次（<c>custom.flight dodge</c>）—— 只出**位移**那一半；
        /// 闪避**姿态**照常由冲刺中按 Z 触发（XML 那条 `FlyFastPoses → dodgeU keys="Z"` 的边）。</summary>
        public string ForceDodge()
        {
            if (_phase != Phase.Airborne)
                return $"not airborne (phase={_phase})";
            Agent main = Agent.Main;
            if (main == null || !AgentControlHelper.SafeIsActive(main))
                return "no player agent";
            if (_dodgeCooldown > 0f)
                return $"dodge on cooldown ({_dodgeCooldown:F2}s left)";
            GetCameraBasis(out Vec3 forward, out _);
            BeginDodge(forward);
            return $"dodge: {FlightTuning.DodgeDistance:F1}m over {FlightTuning.DodgeDisplaceSeconds:F2}s " +
                   "(pose needs the C key in boost: XML edge FlyFastPoses -> dodgeU keys=\"C\")";
        }

        /// <summary>控制台强制出机进入掉落（<c>custom.flight drop</c>）—— 与 <see cref="ForceStop"/> 同义，名字更贴新语义。</summary>
        public string ForceDrop() => ForceStop();

        public string Status()
        {
            string fall = _phase == Phase.Falling
                ? string.Format(" fall={0:F2}s vz={1:F1} ", _fallTimer, _fallSpeedZ)
                : " ";
            return string.Format("phase={0} frozen={1} anim={2} v={3:F1}{4}{5}",
                _phase, _frozenMode.HasValue ? _frozenMode.Value.ToString() : "off", _anim.CurrentAction ?? "-",
                _velocity.Length, fall, _board.Describe());
        }
    }
}
