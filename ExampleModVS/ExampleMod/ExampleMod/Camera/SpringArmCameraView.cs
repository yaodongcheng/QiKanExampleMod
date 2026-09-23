using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
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
        public bool IsAnchorWorld;
    }

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

        // 静态目标 Agent
        public static Agent targetAgent = null;

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            _customCamera = Camera.CreateCamera();
        //    InformationManager.DisplayMessage(new InformationMessage("Advanced Camera Debugger Loaded!"));
        }

        public override void OnMissionScreenFinalize()
        {
            StopFollowCamera();                 // 跟随模式不靠 UI，独立收摊（幂等）
            if (_isActive) CloseUI();
            _customCamera = null;
            base.OnMissionScreenFinalize();
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            long t0 = PerfProfiler.Now();          // perf: CAM_SpringArm
            if (_followActive)
            {
                TickFollowCamera(dt);              // 跟随模式优先（与调试 UI 互斥）
            }
            else if (_isActive && Mission.Current.MainAgent != null)
            {
                ApplyCameraOverrideForUI();
            }
            PerfProfiler.Accum(PerfSlot.CAM_SpringArm, t0); // perf: CAM_SpringArm
        }

        // --- 核心数学逻辑: 将 UI 参数应用到相机 ---
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

            ApplySpringArmCamera(param);
        }

        private void ApplySpringArmCamera(SpringArmCameraParam p)
        {
            MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
            if (targetAgent == null || targetAgent.Mission == null) targetAgent = Mission.Current.MainAgent;
            if (targetAgent == null) return;

            MatrixFrame anchorFrame = targetAgent.LookFrame;



            // 🔴 2026-09-21：算法抽到 SpringArmMath 共用（飞行相机也用同一份，见 Flight/FlightCameraRig.cs）。
            //    这里保留原行为不变 —— 尤其 `p.IsAnchorWorld = false;` 那句"提前强制"照旧，
            //    不是顺手修 bug（那是另一个决定，改了会影响所有相机模板）。
            p.IsAnchorWorld = false;

            SpringArmMath.ComputeFrame(targetAgent, in p, out MatrixFrame finalFrame, out float fovDeg);

            _customCamera.Frame = finalFrame;
            _customCamera.SetFovVertical(fovDeg * (MathF.PI / 180.0f), Screen.AspectRatio, 0.1f, 1000f);

            missionScreen.CustomCamera = _customCamera;
        }

        // ═════════════ 跟随模式（2026-09-23）：用现成相机 + 每帧推 + 补上引擎要的实体 ═════════════
        //
        // **相机 = 本类现成那台 `_customCamera`**（与对话/剧情取景同一台），不另建一台。
        //
        // 🔴🔴 **引擎读的是【相机实体】的全局帧，不是相机自己的 `Frame`**（2026-09-23 反编译实锤）。
        //    <c>MissionScreen.CheckForUpdateCamera</c> 在 <c>CustomCamera != null</c> 时只做三件事：
        //    <code>
        //    CombatCamera.FillParametersFrom(CustomCamera);
        //    if (CustomCamera.Entity != null) { CombatCamera.Frame = CustomCamera.Entity.GetGlobalFrame(); }
        //    SceneView.SetCamera(CombatCamera);
        //    </code>
        //    ⇒ 实体为空时中间那块**整段被跳过** ⇒ 画面冻在接管那一刻
        //    （2026-09-23 实机症状 = "镜头根本没变"，日志每帧 `entity=NULL`）。
        //    **做法 = 现成相机若没有实体，就地给它挂一个空实体**
        //    （无网格、无碰撞、无脚本 ⇒ 隐形零副作用；建空实体的做法见 `FlySpike` / `CarrierBoard`）。
        //
        // ⚠️ **为什么以前没暴露**：模板机位是**设一次就不动**的（对话两人站着说），所以"每帧写能不能动"
        //    在我们的相机系统里**从来没被验证过**；飞行相机是唯一逐帧写的，但它那台相机带不带实体我没实机确认。
        //
        // 🔴 **为什么需要"跟随"**：处决动画会把玩家往前推 3.68 m，机位不跟人就走出画面。
        //
        // 用法：`ApplyFollowTemplate("模板名", agent, 秒数)` / `ApplyFollowFromEngineCamera(agent, 秒数)`；
        // 到点自己渐变归还，也能 `StopFollowCamera()` 立刻还。模板名 = `DesignData/Camera.csv` 的 ID 列。

        /// <summary>进出场渐变时长（秒）—— 臂长/FOV 从引擎相机值滑到机位值，避免硬切。</summary>
        private const float FollowBlendSeconds = 0.35f;

        private static bool _followActive;
        private static bool _followHandingBack;
        private static bool _followUseTimeout;
        private static Agent _followAgent;
        private static SpringArmCameraParam _followTarget;    // 模板机位（只读，不就地改）
        private static SpringArmCameraParam _followCurrent;   // 本帧实际用的机位
        private static SpringArmCameraParam _followFrom;      // 进场起点（方向=模板，臂长/FOV=引擎相机）
        private static SpringArmCameraParam _followHandFrom;  // 归还渐变起点
        private static SpringArmCameraParam _followHandTo;    // 归还渐变终点
        private static float _followBlendT = 1f;
        private static float _followHandT = 1f;
        private static float _followRemain;

        /// <summary>跟随模式用的**空实体**（挂到现成那台相机上，让引擎能读到我们的机位；见本节顶部注释）。</summary>
        private static GameEntity _followCamEntity;
        private static bool _camEntityBound;       // 空实体是否已经绑到那台相机上

        private static float _followElapsed;       // 跟随已运行秒数（日志用）
        private static float _followDt;            // 本帧 dt（日志用）
        private static Vec3 _followLastAnchor;     // 上一帧锚点位置（算 Δ 用）
        private static Vec3 _followLastCam;        // 上一帧相机位置（算 Δ 用）
        private static bool _followHasLast;
        private static bool _followErrorLogged;    // 每帧写相机异常只报一次

        /// <summary>跟随模式是否在跑（诊断/命令回显用）。</summary>
        public static bool IsFollowing => _followActive;

        /// <summary>相机模板行（读 DesignData 的 Camera 表；没有这张表 / 没有这个模板 → null）。</summary>
        private static DynamicRecord FindCameraTemplate(string templateName)
        {
            if (string.IsNullOrWhiteSpace(templateName))
                return null;
            return GameDatabase.Camera?.GetByID(templateName);
        }

        /// <summary>模板行 → 弹簧臂参数（三个入口共用这一份，别再各抄一遍）。</summary>
        private static SpringArmCameraParam BuildCameraParam(DynamicRecord row)
        {
            return new SpringArmCameraParam
            {
                PivotX = row.GetFloat("PivotX"),
                PivotY = row.GetFloat("PivotY"),
                PivotZ = row.GetFloat("PivotZ"),
                ArmLength = row.GetFloat("ArmLength"),
                ArmYaw = row.GetFloat("ArmYaw"),
                ArmPitch = row.GetFloat("ArmPitch"),
                SocketX = row.GetFloat("SocketX"),
                SocketY = row.GetFloat("SocketY"),
                SocketZ = row.GetFloat("SocketZ"),
                SelfYaw = row.GetFloat("SelfYaw"),
                SelfPitch = row.GetFloat("SelfPitch"),
                SelfRoll = row.GetFloat("SelfRoll"),
                Fov = row.GetFloat("Fov"),
                IsAnchorWorld = false,
            };
        }

        /// <summary>相机模板 → 弹簧臂参数。模板不存在返回 false。</summary>
        private static bool TryBuildCameraParam(string templateName, out SpringArmCameraParam param)
        {
            param = default;
            DynamicRecord row = FindCameraTemplate(templateName);
            if (row == null)
                return false;

            param = BuildCameraParam(row);
            return true;
        }

        /// <summary>
        /// **按"接管那一刻的引擎相机机位"跟随**（2026-09-22 用户裁定，**表演镜头的默认做法**）：
        /// 方向 / 臂长 / FOV 全部照抄引擎相机此刻的值，之后**世界锚定**（方向冻住、不跟着角色转）+ 每帧跟位置。
        /// ⇒ 观感 = "镜头一直是开演前你看到的那个角度，只是跟着人平移"，不会自己找角度、也不会随角色转身甩。
        /// </summary>
        /// <param name="seconds">≤0 = 不限时（要手动 <see cref="StopFollowCamera"/>）。</param>
        public static bool ApplyFollowFromEngineCamera(Agent agent, float seconds)
        {
            if (!TryBuildEngineCameraParam(agent, out SpringArmCameraParam p))
                return false;

            // 起点 = 终点（本来就是从引擎相机抄的）⇒ 没有进场渐变，也就没有"镜头先动一下"
            return StartFollow(p, p, agent, "engine-camera", seconds);
        }

        /// <summary>
        /// **用相机模板接管相机并跟随**（模板给的是"看角色的哪个角度"，见 Camera.csv）。
        /// 方向按模板（世界锚定，不跟角色转），臂长/FOV 从引擎相机此刻的值渐变过去。
        /// </summary>
        public static bool ApplyFollowTemplate(string templateName, Agent agent, float seconds)
        {
            if (!TryBuildCameraParam(templateName, out SpringArmCameraParam target))
            {
                DebugLogger.Log($"[FollowCam] 模板 '{templateName}' 不在 Camera 表里 —— 不接管相机");
                return false;
            }

            // 进场：**方向照模板不渐，只渐臂长/FOV**（起点 = 引擎相机此刻的值）
            SpringArmCameraParam from = target;
            if (TryGetEngineCameraDistanceFov(out float engDist, out float engFov))
            {
                from.ArmLength = engDist;
                from.Fov = engFov;
            }

            return StartFollow(target, from, agent, $"template:{templateName}", seconds);
        }

        /// <summary>共享的接管流程（两个入口只差"机位参数从哪来"）。</summary>
        private static bool StartFollow(SpringArmCameraParam target, SpringArmCameraParam from,
                                        Agent agent, string desc, float seconds)
        {
            if (Mission.Current == null || agent == null)
                return false;

            if (ScreenManager.TopScreen as MissionScreen == null)
                return false;

            // 🔴 用 **Camera/ 里现成那台相机**（`_customCamera`，与对话/剧情取景同一台），不另建。
            SpringArmCameraView view = Mission.Current.GetMissionBehavior<SpringArmCameraView>();
            if (view?._customCamera == null)
                return false;

            targetAgent = agent;                  // ApplySpringArmCamera 的静态锚点（调试 UI 那条路径仍在用）
            _followAgent = agent;
            _followTarget = target;
            _followCurrent = target;
            _followFrom = from;

            _followBlendT = 0f;
            _followHandingBack = false;
            _followHandT = 1f;
            _followUseTimeout = seconds > 0f;
            _followRemain = seconds;
            _followElapsed = 0f;
            _followHasLast = false;
            _followErrorLogged = false;
            _followActive = true;

            DebugLogger.Log($"[FollowCam] 接管相机（{desc}）锚={agent.Name} " +
                            $"时长={(_followUseTimeout ? seconds.ToString("0.0") + "s" : "不限")} " +
                            $"yaw={target.ArmYaw:F0} pitch={target.ArmPitch:F0} " +
                            $"臂长 {from.ArmLength:F1}->{target.ArmLength:F1} fov {from.Fov:F0}->{target.Fov:F0} " +
                            $"| 相机实体={DescribeEntity(view._customCamera)}");
            return true;
        }

        /// <summary>
        /// 引擎读的是 `CustomCamera.Entity.GetGlobalFrame()`，实体为空时那一整块被跳过 ⇒ 画面冻住。
        /// 现成那台相机若没实体，**就地给它挂一个空实体**（无网格无碰撞无脚本 = 隐形零副作用）。
        /// 🔴 `Entity` 取值本身可能抛（native 包装）—— 抛也当成"没有"，照样尝试挂。
        /// </summary>
        /// <returns>true = 这台相机现在有实体可读。</returns>
        private static bool EnsureCameraEntity(Camera camera)
        {
            if (camera == null)
                return false;

            if (HasEntity(camera))
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

                camera.Entity = _followCamEntity;
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

        /// <summary>相机实体状态（日志用）：`OK` / `NULL` / `THROW:<异常名>`。</summary>
        private static string DescribeEntity(Camera camera)
        {
            try
            {
                if (camera == null)
                    return "NO-CAMERA";
                return camera.Entity != null ? "OK" : "NULL";
            }
            catch (Exception ex)
            {
                return "THROW:" + ex.GetType().Name;
            }
        }

        /// <summary>引擎相机此刻的视距 / FOV（取不到或明显不合理 → false）。</summary>
        private static bool TryGetEngineCameraDistanceFov(out float distance, out float fov)
        {
            distance = 0f;
            fov = 0f;
            try
            {
                MissionScreen screen = ScreenManager.TopScreen as MissionScreen;
                if (screen == null)
                    return false;

                float d = screen.CameraResultDistanceToTarget;
                float f = screen.CameraViewAngle;
                if (d <= 0.5f || d >= 30f || f <= 20f || f >= 130f)
                    return false;

                distance = MBMath.ClampFloat(d, 1f, 15f);
                fov = f;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// **把引擎相机此刻的机位抄成弹簧臂参数**（方向 + 臂长 + FOV，世界锚定）。
        ///
        /// 🔴 方向换算走 `Vec3.RotationZ` / `RotationX`（**不是** `atan2(y, x)`）—— 角约定差 90°，
        ///    飞行相机 2026-09-21 在这上面栽过一次（接管瞬间镜头横甩 90°）。同一个坑别踩第二遍。
        /// </summary>
        private static bool TryBuildEngineCameraParam(Agent agent, out SpringArmCameraParam param)
        {
            const float RadToDeg = 180f / MathF.PI;
            param = default;

            Vec3 look = Vec3.Zero;
            try
            {
                Mission mission = Mission.Current;
                if (mission != null)
                {
                    MatrixFrame camFrame = mission.GetCameraFrame();
                    look = -camFrame.rotation.u;      // 引擎相机帧约定：视线 = −u
                }
            }
            catch { /* 相机帧取不到 → 回落角色朝向 */ }

            if (look.LengthSquared < 0.0001f)
            {
                try { look = agent?.LookDirection ?? Vec3.Zero; } catch { look = Vec3.Zero; }
            }
            if (look.LengthSquared < 0.0001f)
                return false;

            if (!TryGetEngineCameraDistanceFov(out float dist, out float fov))
            {
                dist = 4f;                            // 兜底：引擎值取不到时的常用第三人称视距
                fov = 65f;
            }

            param = new SpringArmCameraParam
            {
                ArmLength = dist,
                ArmYaw = look.RotationZ * RadToDeg,
                ArmPitch = MBMath.ClampFloat(look.RotationX * RadToDeg, -75f, 45f),
                Fov = fov,
                IsAnchorWorld = true,                 // ← 方向冻在世界里（不跟角色转身）
            };
            return true;
        }

        /// <summary>立刻归还相机（幂等；任何异常都要保证还）。**不渐变**（异常/收摊路径用）。</summary>
        public static void StopFollowCamera()
        {
            if (!_followActive)
                return;

            _followActive = false;
            _followHandingBack = false;
            try
            {
                MissionScreen screen = ScreenManager.TopScreen as MissionScreen;
                Camera cam = Mission.Current?.GetMissionBehavior<SpringArmCameraView>()?._customCamera;
                if (screen != null && cam != null && ReferenceEquals(screen.CustomCamera, cam))
                    screen.CustomCamera = null;      // 置空 = 引擎相机回来
                DebugLogger.Log("[FollowCam] 已归还相机");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[FollowCam] 归还相机异常（重进场景可恢复）: {ex.Message}");
            }
        }

        /// <summary>
        /// 归还前渐变：把**臂长 / FOV** 滑回接管时引擎相机的值（方向不渐），滑完才真撒手。
        /// 为什么要：引擎相机复位时用它自己的视距/FOV，而我们的机位跟它差不少 ⇒ 硬切会"跳"一下。
        /// </summary>
        private static void BeginFollowHandBack()
        {
            _followHandingBack = true;
            _followHandT = 0f;
            _followHandFrom = _followCurrent;
            _followHandTo = _followTarget;
            _followHandTo.ArmLength = _followFrom.ArmLength;   // ← _followFrom 存的正是引擎相机那组值
            _followHandTo.Fov = _followFrom.Fov;
        }

        /// <summary>
        /// 把跟随参数写进相机 —— **每帧**。
        ///
        /// 🔴 **两处都要写**（2026-09-23 实机教训）：
        ///   ① `相机实体.SetGlobalFrame(frame)` —— **引擎真正读的是这个**（见本节顶部注释）；
        ///   ② `相机.Frame = frame` —— 相机自己那份也保持一致（引擎的 FOV 那条分支会碰 CustomCamera）。
        ///
        /// 🔴 **不走 <see cref="ApplySpringArmCamera"/>**：那条为了保持原行为，进去第一件事就是
        ///    `p.IsAnchorWorld = false`（见那里的注释）⇒ 我们要的"世界锚定"会被它悄悄改掉。
        ///    这里直接调同一份数学 <see cref="SpringArmMath.ComputeFrame"/> 自己摆相机。
        ///
        /// 🔴 **必须 try/catch**：相机出错绝不能拖垮任务，出错就归还引擎相机。
        /// </summary>
        private static void ApplyFollowFrame(SpringArmCameraParam p, Agent agent, float dt)
        {
            try
            {
                SpringArmCameraView view = Mission.Current?.GetMissionBehavior<SpringArmCameraView>();
                Camera cam = view?._customCamera;
                MissionScreen screen = ScreenManager.TopScreen as MissionScreen;
                if (cam == null || screen == null)
                    return;

                if (!EnsureCameraEntity(cam))
                    return;

                targetAgent = agent;
                SpringArmMath.ComputeFrame(agent, in p, out MatrixFrame frame, out float fovDeg);

                MatrixFrame f = frame;
                MatrixFrame camFrame = frame;

                // 🔴 两处都写：引擎读的是**实体**那份；相机自己那份跟着写（对话/剧情取景也靠它）。
                _followCamEntity.SetGlobalFrame(in f);
                cam.SetFovVertical(fovDeg * (MathF.PI / 180f), Screen.AspectRatio, 0.1f, 1000f);
                cam.Frame = camFrame;
                screen.CustomCamera = cam;

                _followDt = dt;
                LogFollowTick(agent, frame, cam);
            }
            catch (Exception ex)
            {
                if (!_followErrorLogged)
                {
                    _followErrorLogged = true;
                    DebugLogger.Log($"[FollowCam] 每帧写相机异常，已归还引擎相机: {ex.GetType().Name}: {ex.Message}");
                }
                StopFollowCamera();
            }
        }

        /// <summary>
        /// 跟随期间的日志（**每帧一行**）—— 排查"镜头跟没跟"的现场证据。
        /// `回读Δ` = 写完再从**实体**读回来的位置差：≈0 = 实体确实收下了（引擎读的是同一份）；明显不为 0 = 写没进去。
        /// </summary>
        private static void LogFollowTick(Agent agent, in MatrixFrame frame, Camera cam)
        {
            _followElapsed += _followDt;

            Vec3 a = Vec3.Zero;
            try { a = agent.Position; } catch { /* 取不到就留零 */ }

            float anchorMoved = _followHasLast ? a.Distance(_followLastAnchor) : 0f;
            float camMoved = _followHasLast ? frame.origin.Distance(_followLastCam) : 0f;
            _followLastAnchor = a;
            _followLastCam = frame.origin;
            _followHasLast = true;

            // 回读实体：证明"写进去了没有"
            float readBack = -1f;
            try { readBack = _followCamEntity.GetGlobalFrame().origin.Distance(frame.origin); }
            catch { readBack = -1f; }

            DebugLogger.Log($"[FollowCam] t={_followElapsed:F2} " +
                            $"锚=({a.x:F2},{a.y:F2},{a.z:F2})Δ{anchorMoved:F3} " +
                            $"相机=({frame.origin.x:F2},{frame.origin.y:F2},{frame.origin.z:F2})Δ{camMoved:F3} " +
                            $"回读Δ={(readBack < 0f ? "ERR" : readBack.ToString("F4"))} " +
                            $"yaw={_followCurrent.ArmYaw:F1} pitch={_followCurrent.ArmPitch:F1} " +
                            $"臂长={_followCurrent.ArmLength:F2} fov={_followCurrent.Fov:F0} " +
                            $"entity={DescribeEntity(cam)}");
        }

        /// <summary>每帧重算跟随机位 —— **这一步就是"角色动、镜头跟着动"**。</summary>
        private static void TickFollowCamera(float dt)
        {
            if (!_followActive)
                return;

            // 锚点没了（移除/换场景）就收摊。native 属性可能抛，按项目惯例全包起来。
            Agent a = _followAgent;
            bool anchorAlive;
            try { anchorAlive = a != null && a.Mission != null && Mission.Current?.MainAgent != null; }
            catch { anchorAlive = false; }
            if (!anchorAlive)
            {
                StopFollowCamera();
                return;
            }

            if (_followHandingBack)
            {
                _followHandT = Math.Min(1f, _followHandT + dt / FollowBlendSeconds);
                if (_followHandT >= 1f)
                {
                    StopFollowCamera();
                    return;
                }
                _followCurrent = SpringArmMath.Lerp(in _followHandFrom, in _followHandTo, SpringArmMath.Ease(_followHandT));
            }
            else
            {
                if (_followBlendT < 1f)
                    _followBlendT = Math.Min(1f, _followBlendT + dt / FollowBlendSeconds);

                _followCurrent = _followBlendT >= 1f
                    ? _followTarget
                    : SpringArmMath.Lerp(in _followFrom, in _followTarget, SpringArmMath.Ease(_followBlendT));

                if (_followUseTimeout)
                {
                    _followRemain -= dt;
                    if (_followRemain <= 0f)
                    {
                        BeginFollowHandBack();
                        return;                    // 本帧不写相机，下一帧开始渐变
                    }
                }
            }

            ApplyFollowFrame(_followCurrent, a, dt);
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
            //    InformationManager.DisplayMessage(new InformationMessage("Spring Arm Debugger Opened"));
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
            missionScreen.CustomCamera = null; // 恢复游戏默认相机
        }

        public static string UseCameraTemlate(string templateName,Agent speakerAgent,Agent listenerAgent,Vec3 AnchorWorldPos)
        {
            

            var Instance = Mission.Current.GetMissionBehavior<SpringArmCameraView>();
            if (Instance == null)
            {
                return "Error: SpringArmCameraView not found in current mission.";
            }           
            DynamicRecord cameraTemplate = FindCameraTemplate(templateName);
            if (cameraTemplate == null)
            {
                Instance.CloseUI();
                return "Error: Camera template not found.";
            }
            SpringArmCameraParam springArmCameraParam = BuildCameraParam(cameraTemplate);
            string attachType = cameraTemplate.GetString("AttachType");
            if (attachType == "Player")
            {
                SpringArmCameraView.targetAgent = Agent.Main;
            }
            else if (attachType == "Speaker")
            {
                SpringArmCameraView.targetAgent = speakerAgent;
            }
            else if (attachType == "Listener")
            {
                SpringArmCameraView.targetAgent = listenerAgent;
            }
            else if (attachType == "AnchorWorld")
            {
                SpringArmCameraView.targetAgent = null;
                springArmCameraParam.IsAnchorWorld = true;
            }

            DebugLogger.Log($"UseCameraTemlate:{templateName} {attachType}");
            Instance.ApplySpringArmCamera(springArmCameraParam);

            return "Spring Arm Camera Applied.";

        }

        [CommandLineFunctionality.CommandLineArgumentFunction("useSpringArmCamera", "custom")]
        public static string ExecuteUseSpringArmCameraTemplate(List<string> args)
        {
            if (Mission.Current == null)
            {
                return "Error: No active mission found.";
            }
            if(args.Count ==0)
            {
                return "Error: No template name provided.";
            }
            var Instance = Mission.Current.GetMissionBehavior<SpringArmCameraView>();
            if(Instance == null)
            {
                return "Error: SpringArmCameraView not found in current mission.";
            }   
            string templateName = args[0];
            
            if(args.Count >1)
            {
                string agentId = args[1];
                if(agentId == "player")
                {
                    SpringArmCameraView.targetAgent = Agent.Main;
                }
                var agent = Mission.Current.Agents.FirstOrDefault(ag => ag.Character != null && ag.Character.StringId == agentId);
                if(agent != null)
                {
                    SpringArmCameraView.targetAgent = agent;
                }
                else
                {
                    SpringArmCameraView.targetAgent = Agent.Main;
                }
            }
            else
            {
                SpringArmCameraView.targetAgent = Agent.Main;
            }

            DynamicRecord cameraTemplate = FindCameraTemplate(templateName);
            if(cameraTemplate == null)
            {
                Instance.CloseUI();
                return "Error: Camera template not found.";
            }
            SpringArmCameraParam springArmCameraParam = BuildCameraParam(cameraTemplate);

            Instance.ApplySpringArmCamera(springArmCameraParam);
            
            return "Spring Arm Camera Applied.";

          

        }
    }
}
