using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    // 定义一个新的结构体来存储 Spring Arm 类型的参数
    public struct SpringArmCameraParam
    {
        // 1. Pivot (根部偏移)
        public float PivotX, PivotY, PivotZ;
        // 2. Arm (摇臂)
        public float ArmLength;
        public float ArmYaw, ArmPitch;
        // 3. Socket (相机插槽偏移)
        public float SocketX, SocketY, SocketZ;
        // 4. Self Rot
        public float SelfYaw, SelfPitch, SelfRoll;
        // 5. Misc
        public float Fov;
        // 方向是否**锚定在世界里**（角色转身镜头不甩）—— 飞行/钩索口径；演出模板 = false（角色相对）。
        public bool IsAnchorWorld;
        // 6. 跟随滞后（**只有飞行相机用**；0 = 不滞后 = 老的"焊死在角色身上"行为）
        //    口径与 UE 的 `SpringArmComponent.CameraLagSpeed` 一致：相机**位置**以这个速率追理想机位。
        //    ⚠️ 裁剪相机（SpringArmCameraView 演出用）一律不填 = 0，行为与加这个字段之前逐字节一致。
        public float LagSpeed;
        // 拖尾距离上限（米）—— 对应 UE 的 `CameraLagMaxDistance`；0 = 不限。
        // 为什么要有：拖尾 = 速度 ÷ LagSpeed，出机坠落能到 36 m/s（尾巴 9 米）⇒ 相机被拉得太远。
        public float LagMaxDistance;

        // 7. 运动驱动（2026-09-28）—— 由调用方每帧喂 `SpringArmMotion`（见 `SpringArmMath.WithMotion`）。
        //    **全填 0 = 关掉 = 加这组字段之前的行为。** 我们的速度大小是离散的（0/9/26），
        //    所以驱动量取**连续**的那两个：竖直速度分量、航向角速度。
        public float FovPerVz;        // 竖直速率每 1 m/s → FOV 加多少度（正 = 升降越快视场越广）
        public float ArmPerVz;        // 竖直速率每 1 m/s → 臂长加多少米（正 = 越快镜头越远）
        public float RollPerYawRate;  // 航向角速度每 1°/s → 相机侧倾多少度（正负号：觉得反了填负）

        // 8. 锚点高度口径（2026-10-04 加）：true = 用**引擎自己的公式**（脚底 + (monster 眼高 + 0.2) × 缩放
        //    ≈ 1.90 米），false = 旧常量 1.4626 米（飞行/相机模板演出沿用，行为不变）。
        //    🔴 跟随相机（钩索/脚本接管）**必须 true** —— 否则撒手瞬间环绕点低 0.44 米，
        //    画面里角色"跳高一截"、取景从"看得到全身"变成"只剩肩以上"（2026-10-04 用户实机）。
        public bool UseEngineEyeHeight;
    }

    /// <summary>
    /// **弹簧臂相机的视图**（MissionView）—— 2026-10-05 阶段 2 起它只剩三件事：
    /// ① **生命周期**（建/销毁那台相机）；② **相机实体**（引擎读的是实体帧，见下）；③ **调试滑杆 UI**。
    ///
    /// 🔴 **机器的本体已经搬走了**：摆相机的状态机 = `Camera/SpringArmRig.cs`；
    /// 唯一入口 + 持有者仲裁 = `Camera/CameraService.cs`。**别在本文件里写任何"每帧摆相机"的逻辑**。
    ///
    /// 🔴🔴 **引擎读的是【相机实体】的全局帧**（2026-09-23 反编译 + 2026-10-05 复核
    /// <c>MissionScreen.CheckForUpdateCamera</c>）：
    /// <code>
    /// if (CustomCamera != null) {
    ///     CombatCamera.FillParametersFrom(CustomCamera);
    ///     if (CustomCamera.Entity != null) { CombatCamera.Frame = CustomCamera.Entity.GetGlobalFrame(); }
    ///     SceneView.SetCamera(CombatCamera); return;
    /// }
    /// </code>
    /// ⇒ 实体为空时**中间那块整段被跳过**（画面冻在接管那一刻）⇒ 现成相机若没有实体，就地给它挂一个空实体
    /// （无网格、无碰撞、无脚本 ⇒ 隐形零副作用）。
    /// </summary>
    public class SpringArmCameraView : MissionView
    {
        private GauntletLayer _gauntletLayer;
        private SpringArmCameraDebuggerVM _dataSource;
#if !MB2_V1212
        private GauntletMovieIdentifier _movie;
#else
        private IGauntletMovie _movie;
#endif
        private bool _isActive;
        private Camera _customCamera;

        /// <summary>调试滑杆 UI 的锚点角色（UI 专用；业务路径一律走 `CameraService`）。</summary>
        public static Agent targetAgent = null;

        /// <summary>**引擎真正读的那台相机**（`CameraService` 与 `SpringArmRig` 通过它摆相机）。</summary>
        internal Camera Camera => _customCamera;

        /// <summary>相机实体（挂了它引擎才会读我们的帧）。用前先 <see cref="EnsureCameraEntity"/>。</summary>
        internal GameEntity CamEntity => _followCamEntity;

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            _customCamera = Camera.CreateCamera();
            CameraService.ResetForMission();      // 跨场景不留残留（幂等）
        }

        public override void OnMissionScreenFinalize()
        {
            CameraService.Stop();                 // 幂等：没接管就是空操作
            CameraService.ResetForMission();
            if (_isActive) CloseUI();
            _customCamera = null;
            base.OnMissionScreenFinalize();
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            long t0 = PerfProfiler.Now();          // perf: CAM_SpringArm

            // 🔴 相机机器**每帧都要推进**（没接管时也要跑：撒手后的诊断采样挂在里面）。
            CameraService.Tick(dt);

            // 调试滑杆 UI（只在 UI 打开时跑；它自己会向 service 要持有权）
            if (_isActive && Mission.Current?.MainAgent != null)
            {
                ApplyCameraOverrideForUI();
            }
            PerfProfiler.Accum(PerfSlot.CAM_SpringArm, t0); // perf: CAM_SpringArm
        }

        /// <summary>
        /// 引擎读的是 `CustomCamera.Entity.GetGlobalFrame()`，实体为空时那一整块被跳过 ⇒ 画面冻住。
        /// 现成那台相机若没实体，**就地给它挂一个空实体**（隐形零副作用）。
        /// 🔴 `Entity` 取值本身可能抛（native 包装）—— 抛也当成"没有"，照样尝试挂。
        /// </summary>
        internal bool EnsureCameraEntity()
        {
            if (_customCamera == null)
                return false;
            if (HasEntity(_customCamera))
                return true;
            if (_camEntityBound)
                return true;                          // 挂过了（读不出来但已经绑上，别再重复绑）

            try
            {
                if (!IsEntityAlive(_followCamEntity))
                {
                    _followCamEntity = GameEntity.CreateEmpty(Mission.Current.Scene, isModifiableFromEditor: false);
                    if (_followCamEntity == null)
                    {
                        DebugLogger.Log("[FollowCam] 建相机实体失败（CreateEmpty 返回 null）");
                        return false;
                    }
                }

                _customCamera.Entity = _followCamEntity;
                _camEntityBound = true;
                DebugLogger.Log("[FollowCam] 现成相机没有实体 —— 已给它挂上空实体（引擎只读实体帧）");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[FollowCam] 给相机挂实体失败: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>相机有没有实体（取值可能抛 —— 抛 = 没有）。</summary>
        private static bool HasEntity(Camera camera)
        {
            try { return camera != null && camera.Entity != null; }
            catch { return false; }
        }

        /// <summary>实体是不是还活着（GameEntity 是 native 包装，已销毁时访问成员会抛）。判据同 `CarrierBoard.IsAlive`。</summary>
        private static bool IsEntityAlive(GameEntity entity)
        {
            try { return entity != null && entity.Pointer != UIntPtr.Zero; }
            catch { return false; }
        }

        /// <summary>跟随模式用的**空实体**（挂到现成那台相机上，让引擎能读到我们的机位）。</summary>
        private static GameEntity _followCamEntity;
        private static bool _camEntityBound;

        // ───────────────────────────── 调试滑杆 UI（dev 工具） ─────────────────────────────

        /// <summary>滑杆 UI 每帧把参数喂给 service（第一次喂时由 service 抢占持有权）。</summary>
        private void ApplyCameraOverrideForUI()
        {
            if (_dataSource == null) return;

            SpringArmCameraParam param = new SpringArmCameraParam
            {
                PivotX = _dataSource.TargetOffsetX,
                PivotY = _dataSource.TargetOffsetY,
                PivotZ = _dataSource.TargetOffsetZ,
                ArmLength = _dataSource.ArmLength,
                ArmYaw = _dataSource.ArmYaw,
                ArmPitch = _dataSource.ArmPitch,
                SocketX = _dataSource.SocketOffsetX,
                SocketY = _dataSource.SocketOffsetY,
                SocketZ = _dataSource.SocketOffsetZ,
                SelfYaw = _dataSource.CamSelfYaw,
                SelfPitch = _dataSource.CamSelfPitch,
                SelfRoll = _dataSource.CamSelfRoll,
                Fov = _dataSource.CamFov,
                IsAnchorWorld = _dataSource.IsAnchorWorld
            };

            CameraService.SetLivePose("ui:springarm", targetAgent, in param);
        }

        // --- Command Line Functions (保留但简化) ---

        [CommandLineFunctionality.CommandLineArgumentFunction("openSpringArmCamDebugger", "custom")]
        public static string ExecuteOpenCamDebugger(List<string> args)
        {
            if (Mission.Current == null) return "Error: No active mission.";
            var debuggerView = Mission.Current.GetMissionBehavior<SpringArmCameraView>();
            if (debuggerView != null)
            {
                debuggerView.ToggleUI();
                return "UI Toggled.";
            }
            return "Error: View not registered.";
        }

        // --- UI 开关逻辑 ---
        private void ToggleUI()
        {
            if (_isActive) CloseUI();
            else OpenUI();
        }

        private void OpenUI()
        {
            if (_isActive) return;

            // 创建 VM (会初始化为默认值，而不读取当前相机，防止 Spring Arm 参数混乱)
            _dataSource = new SpringArmCameraDebuggerVM(CloseUI);
            _gauntletLayer = V.NewLayer(100);
            _movie = _gauntletLayer.LoadMovie("SpringArmCameraDebugger", _dataSource);
            _gauntletLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);

            MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
            if (missionScreen != null)
            {
                missionScreen.AddLayer(_gauntletLayer);
                _isActive = true;
            }
        }

        private void CloseUI()
        {
            MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
            if (!_isActive) return;

            missionScreen.RemoveLayer(_gauntletLayer);
            _gauntletLayer = null;
            _dataSource = null;
            _movie = null;
            _isActive = false;
            CameraService.Stop();       // 归还相机（只有 UI 这条沟还持有时才真的还）
        }

        // ───────────────────────────── 业务入口（5 处演出调用点一字不动） ─────────────────────────────

        /// <summary>
        /// **一次性静态机位**（对话/剧情取景）——签名保留（5 处调用点不用改），内部转
        /// <see cref="CameraService.ApplyPose"/>（它会先抢占当前持有者再摆机位）。
        /// </summary>
        public static string UseCameraTemlate(string templateName, Agent speakerAgent, Agent listenerAgent, Vec3 AnchorWorldPos)
        {
            if (Mission.Current == null)
            {
                return "Error: No active mission found.";
            }
            var Instance = Mission.Current.GetMissionBehavior<SpringArmCameraView>();
            if (Instance == null)
            {
                return "Error: SpringArmCameraView not found in current mission.";
            }
            if (!CameraService.ApplyPose(templateName, speakerAgent, listenerAgent, AnchorWorldPos))
            {
                Instance.CloseUI();
                return "Error: Camera template not found.";
            }
            return "Spring Arm Camera Applied.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("useSpringArmCamera", "custom")]
        public static string ExecuteUseSpringArmCameraTemplate(List<string> args)
        {
            if (Mission.Current == null)
            {
                return "Error: No active mission found.";
            }
            if (args.Count == 0)
            {
                return "Error: No template name provided.";
            }
            var Instance = Mission.Current.GetMissionBehavior<SpringArmCameraView>();
            if (Instance == null)
            {
                return "Error: SpringArmCameraView not found in current mission.";
            }
            string templateName = args[0];

            // 目标角色（首参之后可选）：按 Character.StringId 找；找不到/没给 = 主角
            Agent target = Agent.Main;
            if (args.Count > 1 && !string.IsNullOrEmpty(args[1]))
            {
                string agentId = args[1];
                if (agentId != "player")
                {
                    Agent found = Mission.Current.Agents.FirstOrDefault(ag => ag.Character != null && ag.Character.StringId == agentId);
                    if (found != null)
                        target = found;
                }
            }
            SpringArmCameraView.targetAgent = target;

            return CameraService.ApplyPoseTo(templateName, target)
                ? "Spring Arm Camera Applied."
                : "Error: Camera template not found.";
        }
    }
}
