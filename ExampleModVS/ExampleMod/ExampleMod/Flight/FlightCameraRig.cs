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
    public sealed class FlightCameraRig
    {
        // ─────────────────────── 机位参数（可直接改；也能用 custom.flight cam 热调）───────────────────────
        //
        // 🔴 **世界锚定下的参数口径**（和跟随角色时不一样，别照抄那边的直觉）：
        //   `ArmYaw` / `ArmPitch` = **世界朝向角（度）**，本类用鼠标驱动，**预置值一律填 0**
        //     （它们是"额外偏置"，不是"相机高度" —— 相机俯仰现在由鼠标决定）
        //   `ArmLength`          = 相机离角色的距离（米）—— **这是各机位的主要差别**
        //   `SocketX`            = 相机横向偏移；**>0 ⇒ 相机往右移 ⇒ 角色在画面里偏左**
        //   `Fov`                = 垂直视场角（度）

        /// <summary>4 个机位的参数表，下标 = <see cref="FlightCamPreset"/>。</summary>
        public static readonly SpringArmCameraParam[] Presets = new SpringArmCameraParam[]
        {
            // 悬停：近一点 —— 悬停时会转镜头看角色
            new SpringArmCameraParam { ArmLength = 3.8f, Fov = 65f },
            // 巡航：标准跟随
            new SpringArmCameraParam { ArmLength = 5.5f, Fov = 70f },
            // 加速：拉远 + 广角 ⇒ 地面景物掠过更快 = 速度感
            new SpringArmCameraParam { ArmLength = 9.0f, Fov = 80f },
            // 瞄准：过肩近景；SocketX>0 ⇒ 人物偏左；FOV 收窄，视线集中
            new SpringArmCameraParam { ArmLength = 1.9f, Fov = 55f, SocketX = 0.75f, SocketZ = 0.25f },
        };

        /// <summary>机位名（给控制台回显 / 日志用，下标与 <see cref="Presets"/> 对齐）。</summary>
        public static readonly string[] PresetNames = { "hover", "cruise", "boost", "aim" };

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

                if (_camera == null)
                    _camera = Camera.CreateCamera();

                // 播种：与引擎相机当前的视线完全对齐（取不到就回落到角色朝向，见方法内注释）
                SeedLookFromEngineCamera(agent);

                _current = Presets[(int)FlightCamPreset.Cruise];
                _preset = FlightCamPreset.Cruise;
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
            _lookPitch = MBMath.ClampFloat(look.RotationX * DegPerRad,
                                           FlightTuning.CamPitchMin, FlightTuning.CamPitchMax);

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
            try
            {
                HandBackLookToEngine();          // 🔴 先写回朝向，再撒手（否则引擎相机甩回接管那一刻）
                if (_screen != null)
                    _screen.CustomCamera = null;     // 置空 = 引擎相机回来
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

            float s = FlightTuning.CamLookSensitivity * Math.Max(0.05f, Input.MouseSensitivity);
            float sx = FlightTuning.InvertCamX ? 1f : -1f;
            float sy = FlightTuning.InvertCamY ? 1f : -1f;

            _lookYaw += mouseDx * s * sx;
            _lookPitch += mouseDy * s * sy;

            if (_lookYaw > 180f) _lookYaw -= 360f;
            if (_lookYaw < -180f) _lookYaw += 360f;
            _lookPitch = MBMath.ClampFloat(_lookPitch, FlightTuning.CamPitchMin, FlightTuning.CamPitchMax);
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

                    SpringArmCameraParam target = Presets[(int)_preset];
                    _current = (_blendT >= 1f)
                        ? target
                        : SpringArmMath.Lerp(in _from, in target, SpringArmMath.Ease(_blendT));
                }

                // 🔴 世界锚定 + 鼠标朝向：机位预置里的 ArmYaw/ArmPitch 当**额外偏置**加在鼠标角上
                SpringArmCameraParam p = _current;
                p.IsAnchorWorld = true;
                p.ArmYaw = _lookYaw + _current.ArmYaw;
                p.ArmPitch = _lookPitch + _current.ArmPitch;

                SpringArmMath.ComputeFrame(agent, in p, out MatrixFrame frame, out float fovDeg);

                _camera.Frame = frame;
                _camera.SetFovVertical(fovDeg * (MathF.PI / 180f), Screen.AspectRatio, 0.1f, 1000f);

                MissionScreen screen = _screen ?? (ScreenManager.TopScreen as MissionScreen);
                if (screen != null)
                    screen.CustomCamera = _camera;
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
            SpringArmCameraParam v = Presets[(int)p];
            return string.Format(
                "{0,-6} arm={1:F1} yawBias={2:F1} pitchBias={3:F1} pivot=({4:F2},{5:F2},{6:F2}) socket=({7:F2},{8:F2},{9:F2}) fov={10:F0}",
                PresetNames[(int)p], v.ArmLength, v.ArmYaw, v.ArmPitch,
                v.PivotX, v.PivotY, v.PivotZ, v.SocketX, v.SocketY, v.SocketZ, v.Fov);
        }
    }
}
