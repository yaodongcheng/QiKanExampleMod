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
        /// 接管相机。<paramref name="agent"/> 用来**播种初始朝向** —— 从角色当前朝向起步，
        /// 免得起飞瞬间镜头猛甩一下。
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

                // 播种：从角色当前朝向取 yaw；pitch 取一个略微俯视的舒服值
                _lookYaw = YawDegOf(agent);
                _lookPitch = -15f;

                _current = Presets[(int)FlightCamPreset.Cruise];
                _preset = FlightCamPreset.Cruise;
                _blendT = 1f;

                _active = true;
                DebugLogger.Log($"[FlightCam] 已接管相机（世界锚定，鼠标驱动）yaw={_lookYaw:F0} pitch={_lookPitch:F0}");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[FlightCam] 接管相机失败（继续用引擎相机）: {ex.Message}");
                _active = false;
                return false;
            }
        }

        /// <summary>把相机还给引擎（幂等；任何异常都要保证还）。</summary>
        public void Exit()
        {
            if (!_active)
                return;

            _active = false;
            try
            {
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
                if (_blendT < 1f)
                    _blendT = Math.Min(1f, _blendT + (_blendDur <= 0f ? 1f : dt / _blendDur));

                SpringArmCameraParam target = Presets[(int)_preset];
                _current = (_blendT >= 1f)
                    ? target
                    : SpringArmMath.Lerp(in _from, in target, SpringArmMath.Ease(_blendT));

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

        /// <summary>角色朝向的水平角（度）。用来给相机播种初始 yaw。</summary>
        private static float YawDegOf(Agent agent)
        {
            try
            {
                if (agent == null)
                    return 0f;
                Vec3 look = agent.LookDirection;
                if (look.LengthSquared < 0.0001f)
                    return 0f;
                return (float)(Math.Atan2(look.y, look.x) * (180.0 / Math.PI));
            }
            catch
            {
                return 0f;
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
