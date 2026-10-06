using System;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs.Flight
{
    /// <summary>飞行运动相机的 4 个机位。</summary>
    public enum FlightCamPreset
    {
        /// <summary>悬停：离得近，看清角色。</summary>
        Hover,
        /// <summary>巡航：标准跟随距离。</summary>
        Cruise,
        /// <summary>加速：拉远 + 广角 = 速度感。</summary>
        Boost,
        /// <summary>瞄准：过肩近景，**人物偏左**（只在悬停/巡航可用，加速时不进）。</summary>
        Aim
    }

    /// <summary>
    /// 飞行运动相机（2026-09-21 N5）—— 复用 <see cref="SpringArmMath"/> 的算法。
    ///
    /// 🔴🔴 **接管相机 = 必须同时接管「看」**（2026-09-21 实机栽过一次，这是本类的核心设计）：
    ///    <c>MissionScreen.CheckForUpdateCamera</c> 里有一句决定性的早退：
    ///    <code>
    ///    if (CustomCamera != null) { ...; SceneView.SetCamera(CombatCamera); return; }   // ← 引擎相机逻辑整段跳过
    ///    </code>
    ///    ⇒ 一旦挂上 <c>CustomCamera</c>，**引擎不再处理鼠标 look**，<c>ms.CameraBearing/CameraElevation</c>
    ///    **冻结在接管那一刻**。如果这时飞行方向还去读那两个值（<c>GetCameraBasis</c> 原来就是这么干的），
    ///    就会得到"画面是 A、WASD 往 B 飞、鼠标还没反应"的三重错位。
    ///    **所以本类必须自己干三件事**：① 读鼠标累加 yaw/pitch ② 用**世界锚定**摆相机 ③ 把相机前向暴露出去
    ///    给飞行方向用（<see cref="TryGetBasis"/>）。
    ///
    /// 🔴 **为什么用世界锚定（<c>IsAnchorWorld = true</c>）而不是跟随角色朝向**：
    ///    飞行时角色会随移动方向转身（N3），若相机锚在角色身上，人一转向相机就跟着甩。
    ///    自由视角的运动相机应该是**世界锚定 + 鼠标驱动**，角色在相机下自由转身。
    ///    （顺带：这条分支在 <c>SpringArmCameraView</c> 里是死代码 —— 它在调用前把
    ///     <c>IsAnchorWorld</c> 强制置 false 了。算法本身是好的，只是从没被走过。）
    /// </summary>
    public sealed class FlightCameraRig : ICameraExternalHolder, IFlightCameraDriver
    {
        // ─────────────────────── 机位参数（可直接改；也能用 custom.flight cam 热调）───────────────────────
        //
        // 🔴 **世界锚定下的参数口径**（和跟随角色时不一样，别照抄那边的直觉）：
        //   `ArmYaw` / `ArmPitch` = **世界朝向角（度）**，本类用鼠标驱动，**预置值一律填 0**
        //     （它们是"额外偏置"，不是"相机高度" —— 相机俯仰现在由鼠标决定）
        //   `ArmLength`          = 相机离角色的距离（米）—— **这是各机位的主要差别**
        //   `SocketX`            = 相机横向偏移；**>0 ⇒ 相机往右移 ⇒ 角色在画面里偏左**
        //   `Fov`                = 垂直视场角（度）

        /// <summary>
        /// 4 个机位的**表行**（下标 = <see cref="FlightCamPreset"/>；数值 = 行里的 `Param`，**唯一来源**）。
        ///
        /// 🔴 **2026-10-05（阶段 1）起：数值不在代码里了** —— 唯一来源 =
        ///    `ModuleData/DesignData/Camera.csv` 的 `fly_hover` / `fly_cruise` / `fly_boost` / `fly_aim` 四行，
        ///    由 <see cref="EnsureCases"/> 装载。
        ///    **CSV 缺行 ⇒ 不接管相机**（`EnsureCases` 返回 false + 明确日志）—— **不做代码兜底**：
        ///    代码里留一份"看起来还在"的值，只会掩盖表格坏掉这件事。
        ///
        /// 标定来历（数值在表里，理由留在这儿，**改表前先读**）：
        /// · 三档臂长按"画面上的距离 = 臂长 + 拖尾"标定（冲刺拖尾 = 26 ÷ LagSpeed ≈ 3.5 米）——
        ///   实机两次反馈"太远"，冲刺档 15.5 → 12.5 → 11.0 → **8.0** 米。
        /// · `LagSpeed` / `LagMaxDistance` = 弹簧跟随（UE `CameraLagSpeed` / `CameraLagMaxDistance`）；
        /// · `FovPerVz` / `ArmPerVz` / `RollPerYawRate` = 运动驱动（竖直速率→FOV/臂长、航向角速度→侧倾），
        ///   量纲见 `SpringArmCameraView.cs` 的字段注释；标定口径 = 竖直速率上限 20 m/s
        ///   （`SpringArmMath.MotionVzCap`）、航向角速度上限 = 航向追随速率（巡航 360°/s、冲刺 180°/s）。
        /// · **瞄准档全关**（滞后 + 运动驱动都填 0）：玩家在瞄目标时镜头必须是硬的
        ///   （滞后会让准心飘、FOV 随俯仰变会让瞄准距离感失真）。
        /// </summary>
        private static readonly CameraCase[] Cases = new CameraCase[4];

        /// <summary>四档是否已从表里装载成功（<see cref="EnsureCases"/> 的一次性开关）。</summary>
        private static bool _casesLoaded;

        /// <summary>机位名（给控制台回显 / 日志用，下标与 <see cref="Cases"/> 对齐）。</summary>
        public static readonly string[] PresetNames = { "hover", "cruise", "boost", "aim" };

        /// <summary>某档的机位参数（唯一来源 = 表行；没装载 = 全零）。</summary>
        public static SpringArmCameraParam PresetOf(int index)
            => index >= 0 && index < Cases.Length && Cases[index] != null ? Cases[index].Param : default;

        /// <summary>
        /// **从 Camera.csv 装载四档机位**（幂等；缺行 → false + 明确日志 = 本次不接管相机）。
        /// 没有表 / 表里没有 `fly_*` 行 = 内容坏了 —— 明确失败，别静默拿零值去飞。
        /// </summary>
        public static bool EnsureCases()
        {
            if (_casesLoaded)
                return true;

            string missing = null;
            for (int i = 0; i < PresetNames.Length; i++)
            {
                string id = "fly_" + PresetNames[i];
                if (!CameraCase.TryGet(id, out CameraCase c))
                {
                    missing = missing == null ? id : missing + ", " + id;
                    continue;
                }
                Cases[i] = c;
            }
            if (missing != null)
            {
                DebugLogger.Log($"[FlightCam] 🔴 Camera.csv 缺行：{missing} —— **本次不接管相机**"
                              + "（零值机位只会把镜头搞坏；不设代码兜底，请检查 ModuleData/DesignData/Camera.csv）");
                return false;
            }
            _casesLoaded = true;
            DebugLogger.Log("[FlightCam] 四档机位已从 Camera.csv 装载：" + DescribeAll());
            return true;
        }

        /// <summary>四档是否可用（`custom.flight cam` 标题 / 诊断用）。</summary>
        public static bool CasesLoaded => _casesLoaded;

        /// <summary>改一档的机位参数（**写回表行** —— 表行就是唯一来源，`custom.cam set` 改的也是它）。</summary>
        public static void SetPresetParam(int index, in SpringArmCameraParam p)
        {
            if (index < 0 || index >= Cases.Length)
                return;
            CameraCase c = Cases[index];
            if (c != null)
                c.Param = p;
        }

        /// <summary>四档一起改鼠标灵敏度（`custom.flight tune camsens`）。没装载 → false。</summary>
        public static bool SetAllLookSens(float value)
        {
            if (!_casesLoaded)
                return false;
            for (int i = 0; i < Cases.Length; i++)
                Cases[i].LookSens = value;
            return true;
        }

        /// <summary>四档一起改**俯仰下限**（`custom.flight tune campitchmin`）。没装载 → false。</summary>
        public static bool SetAllPitchMin(float value)
        {
            if (!_casesLoaded)
                return false;
            for (int i = 0; i < Cases.Length; i++)
                Cases[i].PitchMin = value;
            return true;
        }

        /// <summary>四档一起改**俯仰上限**（`custom.flight tune campitchmax`）。没装载 → false。</summary>
        public static bool SetAllPitchMax(float value)
        {
            if (!_casesLoaded)
                return false;
            for (int i = 0; i < Cases.Length; i++)
                Cases[i].PitchMax = value;
            return true;
        }

        /// <summary>非瞄准档一起改滞后速率（`custom.flight tune camlag`；**瞄准档固定不滞后**）。没装载 → false。</summary>
        public static bool SetAllLagSpeed(float value)
        {
            if (!_casesLoaded)
                return false;
            for (int i = 0; i < PresetNames.Length; i++)
            {
                if (PresetNames[i] == "aim")
                    continue;
                SpringArmCameraParam p = PresetOf(i);
                p.LagSpeed = value;
                SetPresetParam(i, in p);
            }
            return true;
        }

        /// <summary>四档一行一个（装载日志 / `custom.flight cam` 列表用）。</summary>
        public static string DescribeAll()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < PresetNames.Length; i++)
            {
                if (i > 0)
                    sb.Append(" | ");
                sb.Append(Describe((FlightCamPreset)i));
            }
            return sb.ToString();
        }

        // ─────────────────────────────── 实例状态 ───────────────────────────────

        private Camera _camera;
        private MissionScreen _screen;
        private bool _active;

        private FlightCamPreset _preset = FlightCamPreset.Cruise;
        private SpringArmCameraParam _current;
        private SpringArmCameraParam _from;
        private float _blendT = 1f;
        private float _blendDur = 0.35f;

        // 鼠标累加出来的世界朝向（度）
        private float _lookYaw;
        private float _lookPitch;

        // ── 弹簧跟随（相机位置滞后，2026-09-27）—— UE `SpringArmComponent.CameraLagSpeed` 的等价物 ──
        // 🔴 **它补的是"弹性"那一半**：只做航向惯性的话，相机焊死在角色身上，
        //    角色永远在画面正中央 —— 航向与镜头的夹角**在画面上看不见**。
        //    相机一滞后，那个夹角就变成"角色滑到画面一侧"，追上后再滑回来。
        // 🔴 **状态本身是共用件**（2026-09-28 抽出去，见 `SpringArmMath.SpringArmLagState`）：
        //    这里只留一个字段 + 每帧一次 `Update`，别的相机（演出跟随 / 以后的载具镜头）照抄这两行即可。
        // 口径：滞后的是**角色锚点**（不是相机位置）：相机位 = 锚点 − 前向×臂长，
        // 所以"锚点滞后多少，相机就跟着挪多少"，与 UE 的 pivot 滞后完全等价
        // （前提：PivotX/Y/Z = 0 —— 飞行四个机位都是 0；若哪天给某个机位填了 pivot 偏移，这条要重推）。
        private SpringArmLagState _lag;

        /// <summary>本帧的运动量（行为层每帧喂，见 <see cref="SetMotion"/>）—— 驱动 FOV/臂长/侧倾的连续量。</summary>
        private SpringArmMotion _motion;

        // ── 交接（2026-09-21/22：进场渐近视距·FOV；出场**先渐变回默认相机参数**再撒手）──
        private float _engineElevSign = 1f;   // 引擎 CameraElevation 与我们的 pitch 的符号关系（接管时自校准）
        private bool _handBackWarned;         // 写回失败只报一次

        // 🔴 接管那一刻记下的**默认相机参数**（2026-09-22 用户要求）：
        //    "起飞前和起飞后的默认相机机位基本一致" ⇒ 归还时按这些值渐变过去，
        //    不然就是硬切（实测引擎 视距 3.4 / fov 65，而我们巡航 5.5/70、冲刺 9/80 —— 俯冲落地最明显）。
        private float _engineDistAtTakeover = -1f;
        private float _engineFovAtTakeover = -1f;
        private bool _handingBack;            // 正在"渐变回默认相机"
        private float _handBackT = 1f;
        private SpringArmCameraParam _handBackFrom;
        private SpringArmCameraParam _handBackTo;

        /// <summary>相机是否正在接管。</summary>
        public bool IsActive => _active;

        /// <summary>当前机位名（日志用）。</summary>
        public string CurrentPresetName => PresetNames[(int)_preset];

        /// <summary>渐变进度 0..1（诊断用）。</summary>
        public float BlendProgress => _blendT;

        /// <summary>当前朝向角（诊断用）。</summary>
        public float LookYaw => _lookYaw;
        public float LookPitch => _lookPitch;

        /// <summary>
        /// 接管相机。<paramref name="agent"/> 只在**取不到引擎相机帧**时兜底用。
        ///
        /// 🔴 **播种口径（2026-09-21 二修，实机症状：起步时角色朝向「抖一下」）**：
        ///    初版是 `_lookYaw = atan2(look.y, look.x)`（角色朝向角），**差 90°** —— 反编译实锤：
        ///    <list type="bullet">
        ///      <item><c>Mat3.RotateAboutUp(a)</c> 作用在 Identity 上得到 <c>f = (−sin a, cos a, 0)</c>；
        ///            要让 f 指向向量 v，必须 <c>a = atan2(−v.x, v.y)</c> = <c>Vec3.RotationZ</c>。</item>
        ///      <item>而 <c>atan2(v.y, v.x)</c> 得到的角度 a′ 满足 <c>a′ = a + 90°</c> —— 拿它当 yaw
        ///            喂进去，相机就看向角色朝向**左边 90°** 的方向。</item>
        ///    </list>
        ///    ⇒ 结果：接管那一刻镜头**横甩 90°**（看起来像角色转了一下），起步按 W 时机身又
        ///    瞬时对齐镜头（本来"机身朝移动方向"那套逻辑是对的）⇒ 用户看到的"抖一下 + 脸不在正前方"。
        ///
        ///    **现在的口径 = 直接读引擎相机此刻的视线，用同一套 `RotationZ/RotationX` 换算** ——
        ///    这样接管瞬间镜头方向**和引擎相机逐度一致**，不甩、不抖；机身也本来就朝那个方向
        ///    （引擎第三人称相机在角色背后），起步时 `TurnBody` 写下去的方向与它相同 ⇒ 不翻身。
        /// </summary>
        public bool Enter(Agent agent)
        {
            if (_active)
                return true;

            try
            {
                _screen = ScreenManager.TopScreen as MissionScreen;
                if (_screen == null)
                    return false;

                // 🔴 机位数值来自 `Camera.csv` 的 `fly_*` 四行（2026-10-05 阶段 1）——
                //    缺行 = **本次不接管相机**（`EnsureCases` 里已有明确日志；不设代码兜底）
                if (!EnsureCases())
                    return false;

                if (_camera == null)
                    _camera = Camera.CreateCamera();

                _preset = FlightCamPreset.Cruise;      // 先定档：下面播种的俯仰钳位按"当前档"的表行取

                // 播种：与引擎相机当前的视线完全对齐（取不到就回落到角色朝向，见方法内注释）
                SeedLookFromEngineCamera(agent);

                _current = PresetOf((int)FlightCamPreset.Cruise);
                _blendDur = Math.Max(0.01f, FlightTuning.CamBlendIn);
                _handingBack = false;              // 二次起飞：取消可能还在走的归还渐变

                // 记下默认相机此刻的**视距 / FOV**（归还时按它们渐变回去）
                _engineDistAtTakeover = _screen != null ? _screen.CameraResultDistanceToTarget : -1f;
                _engineFovAtTakeover = _screen != null ? _screen.CameraViewAngle : -1f;

                // 🔴 进场过渡的**正确做法 = 只对齐"距离与 FOV"，不对齐"相机那一帧"**（2026-09-21 修正）：
                //    第一版是从引擎相机的那一帧（位置 + 朝向）整体插值过来 —— 结果是**镜头在过渡期间绕着角色转**
                //    （引擎相机的机位和我们的机位不在同一条视线轴上），玩家看到的是"角色没朝前"，
                //    而其实是我们相机还没滑到位。
                //    现在：方向用播种值（逐度对齐、不退让），只把**臂长**（视距）和 **FOV** 从引擎相机的值
                //    渐变到我们的机位 —— 观感是"镜头拉近/推远"，而不是"绕着人转"。
                _blendT = 1f;
                if (FlightTuning.UseCamHandover)
                {
                    _from = _current;
                    float engDist = _screen != null ? _screen.CameraResultDistanceToTarget : 0f;
                    float engFov = _screen != null ? _screen.CameraViewAngle : 0f;
                    if (engDist > 0.5f && engDist < 30f)
                        _from.ArmLength = MBMath.ClampFloat(engDist, 1f, 15f);
                    if (engFov > 20f && engFov < 130f)
                        _from.Fov = engFov;
                    _blendT = 0f;
                }

                _active = true;
                // 🔴 向相机服务登记"飞行在持有相机"（2026-10-05 阶段 2）：别人起播会被**拒绝**、
                //    演出/对话的一次性机位会**抢占**（叫我们立刻放手，见 OnCameraPreempted）。
                //    "视线找谁"也一并由服务分发（服务那唯一的 CameraLook 提供者会转给本 rig）。
                // ⚠️ 起飞是主要玩法：**先顶掉当前持有者**（否则两台机器会各写各的 = 画面抖）。
                if (CameraService.IsHeld && !CameraService.IsHeldBy("flight"))
                {
                    DebugLogger.Log($"[FlightCam] 起飞顶掉当前相机持有者 {CameraService.Holder}（飞行优先）");
                    CameraService.Stop();
                }
                CameraService.TakeExternal("flight", this);
                _lag.Reset();                      // 弹簧跟随重新播种（接管那一帧不滞后，随后自然拖起来）
                _motion = default;                 // 运动量清零（行为层下一帧就会喂）
                DebugLogger.Log($"[FlightCam] 已接管相机（世界锚定，鼠标驱动）yaw={_lookYaw:F0} pitch={_lookPitch:F0} " +
                                $"| 引擎相机 bearing={RadToDeg(_screen?.CameraBearing ?? 0f):F0} elev={RadToDeg(_screen?.CameraElevation ?? 0f):F1} " +
                                $"视距={_screen?.CameraResultDistanceToTarget ?? 0f:F1} fov={_from.Fov:F0} 进场过渡={(FlightTuning.UseCamHandover ? "开" : "关")}");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[FlightCam] 接管相机失败（继续用引擎相机）: {ex.Message}");
                _active = false;
                return false;
            }
        }

        /// <summary>
        /// 把 <see cref="_lookYaw"/> / <see cref="_lookPitch"/> 对齐到**引擎相机此刻的视线方向**。
        ///
        /// 🔴 换算口径（反编译实证，别改回 `atan2(y, x)`）：
        ///    `RotateAboutUp(a)` 的角约定 = <c>Vec3.RotationZ</c> = `atan2(−x, y)`；
        ///    俯仰 = <c>Vec3.RotationX</c> = `atan2(z, √(x²+y²))`（正 = 抬头），与
        ///    <see cref="SpringArmMath"/> 里 `RotateAboutSide(pitch)` 的语义一致（正 = 抬头）。
        ///
        /// 相机帧取不到（罕见：场景刚切、相机还没建）→ 回落到角色 `LookDirection`，
        /// 同样走 `RotationZ/RotationX`。**回落路径也只差"角色朝向"与"镜头朝向"那点差别**，
        /// 不会再出现 90° 的甩动。
        /// </summary>
        private void SeedLookFromEngineCamera(Agent agent)
        {
            const float DegPerRad = 180f / MathF.PI;

            Vec3 look = Vec3.Zero;
            try
            {
                Mission mission = Mission.Current;
                if (mission != null)
                {
                    MatrixFrame camFrame = mission.GetCameraFrame();
                    look = -camFrame.rotation.u;     // 引擎相机帧约定：视线 = −u（= 相机帧的"背面朝前"）
                }
            }
            catch { /* 相机帧取不到就往下走兜底 */ }

            if (look.LengthSquared < 0.0001f)
            {
                try { look = agent?.LookDirection ?? Vec3.Zero; } catch { look = Vec3.Zero; }
            }

            if (look.LengthSquared < 0.0001f)
            {
                // 两者都取不到（理论上不会）—— 给一个朝世界 +Y、略微俯视的安全值
                _lookYaw = 0f;
                _lookPitch = -15f;
                return;
            }

            _lookYaw = look.RotationZ * DegPerRad;
            // 🔴 俯仰钳位 = **当前档的表行**（`Camera.csv` 的 `PitchMin`/`PitchMax` 列，2026-10-05 阶段 1）——
            //    原来是全局 `FlightTuning.CamPitchMin/Max`，现在逐 case 配（四档当前同值）。
            CameraCase c = Cases[(int)_preset];
            if (c != null)
            {
                _lookPitch = MBMath.ClampFloat(look.RotationX * DegPerRad, c.PitchMin, c.PitchMax);
            }
            else
            {
                _lookPitch = look.RotationX * DegPerRad;
            }

            // 同一个相机方向 → 顺便把"引擎 elevation 与我们的 pitch 谁正谁负"标定出来（出场写回要用）
            CalibrateEngineElevationSign();
        }

        /// <summary>
        /// **开始"渐变回默认相机"**（2026-09-22 用户要求）—— 不马上撒手，先用
        /// <see cref="FlightTuning.CamBlendIn"/> 的时间把**视距 / FOV** 渐变回接管时记下的默认值，
        /// 渐变走完才真的 `CustomCamera = null`。
        ///
        /// 为什么：归还时引擎相机按它自己的视距/FOV 复位，而我们的机位跟它差很多
        /// （实测引擎 3.4/65 vs 巡航 5.5/70、冲刺 9/80）⇒ 硬切一下，俯冲落地尤其明显。
        /// 朝向不渐（我们的 yaw/pitch 就是玩家刚看的方向，撒手前会写回引擎，见 <see cref="HandBackLookToEngine"/>）。
        /// </summary>
        public void BeginHandBack()
        {
            if (!_active || _handingBack)
                return;

            _handBackFrom = _current;
            _handBackTo = _current;
            if (_engineDistAtTakeover > 0.5f && _engineDistAtTakeover < 30f)
                _handBackTo.ArmLength = MBMath.ClampFloat(_engineDistAtTakeover, 1f, 15f);
            if (_engineFovAtTakeover > 20f && _engineFovAtTakeover < 130f)
                _handBackTo.Fov = _engineFovAtTakeover;

            _handBackT = 0f;
            _handingBack = true;
            DebugLogger.Log($"[FlightCam] 开始渐变回默认相机（视距 {_handBackFrom.ArmLength:F1}→{_handBackTo.ArmLength:F1} " +
                            $"fov {_handBackFrom.Fov:F0}→{_handBackTo.Fov:F0}，用时 {_blendDur:F2}s）");
        }

        /// <summary>归还渐变是否还在走（行为层据此决定还要不要继续 Tick 相机）。</summary>
        public bool IsHandingBack => _handingBack;

        /// <summary>把相机还给引擎（幂等；任何异常都要保证还）。**立即切，不渐变**（异常/收摊路径用）。</summary>
        public void Exit()
        {
            if (!_active)
                return;

            _active = false;
            _lag.Reset();                        // 弹簧跟随状态不留到下次（下次接管当帧对齐）
            _motion = default;
            try
            {
                HandBackLookToEngine();          // 🔴 先写回朝向，再撒手（否则引擎相机甩回接管那一刻）
                // 🔴 **只还自己那台**（2026-10-05 阶段 2 修的老 bug：原来无条件置 null，
                //    会把别人（演出/钩索）正在用的相机踩掉）：服务先退登记，再按对象比对撒手。
                CameraService.ReleaseExternal("flight");
                CameraService.ReleasePresentedIf(_camera);
                DebugLogger.Log("[FlightCam] 已归还相机");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[FlightCam] 归还相机异常（可能仍是自定义相机，重进场景可恢复）: {ex.Message}");
            }
            _screen = null;
        }

        /// <summary>
        /// 喂鼠标增量（**每帧一次**）。这是"接管相机 = 接管看"的那一半 ——
        /// 不喂它，玩家就完全没法转视角。
        /// 灵敏度乘了引擎自己的 <see cref="Input.MouseSensitivity"/>，跟随玩家的设置。
        /// </summary>
        public void ApplyLook(float mouseDx, float mouseDy)
        {
            if (!_active)
                return;

            // 🔴 灵敏度 / 俯仰钳位 = **当前档的表行**（`Camera.csv` 的 `LookSens` / `PitchMin` / `PitchMax` 列；
            //    2026-10-05 阶段 1 从全局 `FlightTuning` 挪到逐 case）。鼠标左右/上下反向仍是全局设备偏好。
            CameraCase c = Cases[(int)_preset];
            if (c == null)
                return;      // 正常不会（Enter 已挡）；真到了这 = 不写方向，交给归还路径

            float s = c.LookSens * Math.Max(0.05f, Input.MouseSensitivity);
            float sx = FlightTuning.InvertCamX ? 1f : -1f;
            float sy = FlightTuning.InvertCamY ? 1f : -1f;

            _lookYaw += mouseDx * s * sx;
            _lookPitch += mouseDy * s * sy;

            if (_lookYaw > 180f) _lookYaw -= 360f;
            if (_lookYaw < -180f) _lookYaw += 360f;
            _lookPitch = MBMath.ClampFloat(_lookPitch, c.PitchMin, c.PitchMax);
        }

        /// <summary>
        /// 喂本帧的**运动量**（在 <see cref="Tick"/> 之前调一次）—— 运动驱动的唯一入口。
        ///
        /// 🔴 **为什么要行为层喂、不自己读 agent 速度**：飞行时**玩家的速度被冻结成 0**
        ///    （输入冻结 + AI 暂停），真实速度在行为层的 `_velocity`（木板速度）里 —— 相机拿不到。
        ///    演出相机不喂 ⇒ `_motion` 全零 ⇒ 与加这套之前逐字节一致。
        ///
        /// 全局增益 = <see cref="FlightTuning.CamMotionGain"/>（`custom.flight tune cammotion`）：
        /// **填 0 = 一键关掉整套运动驱动**，方便和"没有运动驱动"的观感对比。
        /// </summary>
        public void SetMotion(in SpringArmMotion m)
        {
            float g = FlightTuning.CamMotionGain;
            _motion.Vz = m.Vz * g;
            _motion.YawRate = m.YawRate * g;
            _motion.Speed = m.Speed;        // 只做诊断（飞行速度大小只有 0/9/26 三档），不乘增益
        }

        /// <summary>切换机位。**只有机位真的变了才重启渐变**（每帧同一个机位不会重置进度）。</summary>
        public void SetPreset(FlightCamPreset preset, float transitionSeconds)
        {
            if (preset == _preset)
                return;

            _from = _current;
            _preset = preset;
            _blendT = 0f;
            _blendDur = Math.Max(0.01f, transitionSeconds);
        }

        /// <summary>每帧推进渐变并写入相机。**必须在飞行期间每帧调**。</summary>
        public void Tick(Agent agent, float dt)
        {
            if (!_active || agent == null)
                return;

            try
            {
                if (_handingBack)
                {
                    // 归还渐变：**不再走常规机位解算**（否则下面那两行会把 _current 覆盖回预置）
                    _handBackT = Math.Min(1f, _handBackT + (_blendDur <= 0f ? 1f : dt / _blendDur));
                    _current = SpringArmMath.Lerp(in _handBackFrom, in _handBackTo, SpringArmMath.Ease(_handBackT));
                    if (_handBackT >= 1f)
                    {
                        _handingBack = false;
                        Exit();                       // 渐变走完 → 真的撒手（内部会写回朝向）
                        return;
                    }
                }
                else
                {
                    if (_blendT < 1f)
                        _blendT = Math.Min(1f, _blendT + (_blendDur <= 0f ? 1f : dt / _blendDur));

                    SpringArmCameraParam target = PresetOf((int)_preset);
                    _current = (_blendT >= 1f)
                        ? target
                        : SpringArmMath.Lerp(in _from, in target, SpringArmMath.Ease(_blendT));
                }

                // 🔴 世界锚定 + 鼠标朝向：机位预置里的 ArmYaw/ArmPitch 当**额外偏置**加在鼠标角上
                SpringArmCameraParam p = _current;
                p.IsAnchorWorld = true;
                p.ArmYaw = _lookYaw + _current.ArmYaw;
                p.ArmPitch = _lookPitch + _current.ArmPitch;

                // 运动驱动（2026-09-28）：把本帧的运动量叠到 FOV / 臂长 / 侧倾上（`_motion` 全零 ⇒ 等于没这行）
                p = SpringArmMath.WithMotion(in p, in _motion);

                SpringArmMath.ComputeFrame(agent, in p, out MatrixFrame frame, out float fovDeg);

                // ── 弹簧跟随（相机位置滞后，2026-09-27；状态已抽成共用件）──
                //    · 归还渐变期间**不滞后**、并把偏移按 `1−进度` 淡出 ⇒ 撒手那一刻正好归零，不弹
                //    · LagSpeed ≤ 0（含瞄准档）= 不滞后，偏移以 `SpringArmLagState.FadeSpeed` 渐隐
                float lagSpeed = _handingBack ? 0f : _current.LagSpeed;
                float lagFade = _handingBack ? (1f - _handBackT) : 1f;
                frame.origin += _lag.Update(agent.LookFrame.origin, lagSpeed, _current.LagMaxDistance, dt, lagFade);

                _camera.Frame = frame;
                _camera.SetFovVertical(fovDeg * (MathF.PI / 180f), Screen.AspectRatio, 0.1f, 1000f);

                // 呈现（`CustomCamera = 相机`）走相机服务 —— 全项目唯一写者（阶段 2）
                CameraService.Present(_camera);
            }
            catch (Exception ex)
            {
                // 相机出错不能拖垮飞行 —— 直接还给引擎，让玩家至少能继续玩
                DebugLogger.Log($"[FlightCam] tick 异常，已归还相机: {ex.Message}");
                Exit();
            }
        }

        /// <summary>
        /// 把**相机自己的**前向 / 右向给出去 —— 飞行方向必须用它，**不能再用 `ms.CameraBearing`**
        /// （那个在接管后是冻结的旧值，见类型注释）。
        /// 相机没接管时返回 false，调用方回退到引擎相机解算。
        /// </summary>
        public bool TryGetBasis(out Vec3 forward, out Vec3 right)
        {
            forward = Vec3.Zero;
            right = Vec3.Zero;
            if (!_active)
                return false;

            try
            {
                // 用与 ComputeFrame 同一套旋转重算朝向（比从 camera.Frame 读更稳，且不依赖引擎回调时序）
                Mat3 m = Mat3.Identity;
                m.RotateAboutUp((_lookYaw + _current.ArmYaw) * (MathF.PI / 180f));
                m.RotateAboutSide((_lookPitch + _current.ArmPitch) * (MathF.PI / 180f));
                if (m.f.LengthSquared < 0.0001f)
                    return false;
                forward = m.f.NormalizedCopy();
                right = m.s.NormalizedCopy();
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ─────────────────────── 相机服务的"外部持有者"接口（过渡期：阶段 4 合并后退役）───────────────────────

        /// <summary>被**抢占**（演出/对话的一次性机位要镜头）—— 立刻归还，不渐变（服务在抢占路径上同步调用）。</summary>
        void ICameraExternalHolder.OnCameraPreempted()
        {
            DebugLogger.Log("[FlightCam] 被抢占（演出/对话要相机）—— 立刻归还");
            Exit();
        }

        /// <summary>把"相机看向哪"给出去（相机服务那唯一的 `CameraLook` 提供者会转到这里）。</summary>
        bool ICameraExternalHolder.TryGetLook(out Vec3 forward)
        {
            return TryGetBasis(out forward, out _);
        }

        // 🔴 这里曾有一个 `YawDegOf(agent)`（`atan2(look.y, look.x)`），已删除（2026-09-21）。
        //    它的角约定与 `RotateAboutUp` 差 90° —— 留着它 = 留着一个**看起来对、用起来错 90°** 的
        //    工具，下一个人一定会再踩一次。要角就统一走 `Vec3.RotationZ` / `RotationX`
        //    （见 SeedLookFromEngineCamera 的注释）。

        // ─────────────────────── 相机交接（进/出都从对方那一帧接上）───────────────────────

        private static float RadToDeg(float rad) => rad * (180f / MathF.PI);
        private static float DegToRad(float deg) => deg * (MathF.PI / 180f);

        /// <summary>
        /// 校准引擎 `CameraElevation` 与我们 pitch 的**符号关系**（归还相机写回时要用）。
        ///
        /// 做法：接管那一刻两者指的是同一个相机方向 —— 符号不一致就记 −1。
        /// **不靠猜"正数是不是抬头"**（反编译里那两条公式的符号读起来是矛盾的，实测才作数）。
        /// </summary>
        private void CalibrateEngineElevationSign()
        {
            try
            {
                float engElev = _screen != null ? _screen.CameraElevation : 0f;
                _engineElevSign = (Math.Abs(engElev) > 0.01f && Math.Abs(_lookPitch) > 0.01f
                                   && Math.Sign(engElev) != Math.Sign(_lookPitch)) ? -1f : 1f;
            }
            catch
            {
                _engineElevSign = 1f;
            }
        }

        /// <summary>
        /// 归还相机前把**我们当前的朝向**写回引擎（`CameraBearing` / `CameraElevation`）。
        ///
        /// 🔴 为什么必须写：接管期间 `CheckForUpdateCamera` 早退，**引擎相机的朝向冻在接管那一刻**。
        ///    飞行中玩家转过视角的话，一撒手引擎相机就从那个旧朝向恢复 ⇒ 落地瞬间镜头甩回去。
        ///
        /// 角度是**私有 setter**，走反射（本项目既有手法，见 wheels.d/campaign-mode.md 的
        /// `GetSetMethod(true)`）。拿不到就只打一行日志 —— 相机照旧能用，只是会甩一下。
        /// </summary>
        private void HandBackLookToEngine()
        {
            if (!FlightTuning.UseCamHandover || !FlightTuning.CamHandBackLook || _screen == null)
                return;

            try
            {
                const System.Reflection.BindingFlags F =
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic;

                var bearingProp = typeof(MissionScreen).GetProperty("CameraBearing", F);
                var elevProp = typeof(MissionScreen).GetProperty("CameraElevation", F);
                var bearingSet = bearingProp?.GetSetMethod(true);
                var elevSet = elevProp?.GetSetMethod(true);

                if (bearingSet == null || elevSet == null)
                {
                    if (!_handBackWarned)
                    {
                        _handBackWarned = true;
                        DebugLogger.Log("[FlightCam] 写不回引擎相机朝向（反射找不到 setter）—— 归还时可能甩一下");
                    }
                    return;
                }

                bearingSet.Invoke(_screen, new object[] { DegToRad(_lookYaw) });
                elevSet.Invoke(_screen, new object[] { _engineElevSign * DegToRad(_lookPitch) });
                DebugLogger.Log($"[FlightCam] 朝向已写回引擎: bearing={_lookYaw:F0} elev(sign={_engineElevSign:F0})={_lookPitch:F1}");
            }
            catch (Exception ex)
            {
                if (!_handBackWarned)
                {
                    _handBackWarned = true;
                    DebugLogger.Log($"[FlightCam] 写回引擎相机朝向异常: {ex.Message}");
                }
            }
        }

        /// <summary>一行参数摘要（控制台打印 / 日志用）。</summary>
        public static string Describe(FlightCamPreset p)
        {
            SpringArmCameraParam v = PresetOf((int)p);
            return string.Format(
                "{0,-6} arm={1:F1} yawBias={2:F1} pitchBias={3:F1} pivot=({4:F2},{5:F2},{6:F2}) socket=({7:F2},{8:F2},{9:F2}) fov={10:F0}" +
                " | lag={11:F1} lagmax={12:F1} | fovvz={13:F2} armvz={14:F3} rollyaw={15:F3}",
                PresetNames[(int)p], v.ArmLength, v.ArmYaw, v.ArmPitch,
                v.PivotX, v.PivotY, v.PivotZ, v.SocketX, v.SocketY, v.SocketZ, v.Fov,
                v.LagSpeed, v.LagMaxDistance, v.FovPerVz, v.ArmPerVz, v.RollPerYawRate);
        }
    }
}
