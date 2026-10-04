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

            // 交班后的采样（**诊断用，`custom.cam log 1` 才跑**）：第 1 帧 + 0.5 秒 + 之后每 0.5 秒共 3 秒。
            // 目的：把"引擎相机撒手后到底停在哪、有没有在动"记录成时间序列（原来是 grapple 侧的 24 帧观察窗，2026-10-04 搬过来）。
            if (DebugLogging)
            {
                if (_postReleaseLogFirst)
                {
                    _postReleaseLogFirst = false;
                    LogEngineDefaultCamera("撒手后第1帧");
                }
                if (_postReleaseLogTimer >= 0f)
                {
                    _postReleaseLogTimer -= dt;
                    if (_postReleaseLogTimer <= 0f)
                    {
                        _postReleaseLogTimer = -1f;
                        LogEngineDefaultCamera("交班后稳定值");
                    }
                }
                if (_postReleaseWatchLeft > 0)
                {
                    _postReleaseWatchTimer -= dt;
                    if (_postReleaseWatchTimer <= 0f)
                    {
                        _postReleaseWatchTimer = 0.5f;
                        _postReleaseWatchLeft--;
                        LogEngineDefaultCamera($"撒手后+{(PostReleaseWatchSeconds - _postReleaseWatchLeft * 0.5f):F1}s");
                    }
                }
            }

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

        private static bool _followWriteBackLook;   // 归还时是否把我们的朝向写回引擎（见 ApplyFollowFromEngineCamera 重载）
        private static bool _followClearSpecialOnReturn;   // 撒手前是否清掉引擎"特殊相机"的冻结修正（钩索用，见 ClearEngineSpecialCameraAdds）
        private static bool _followPredictReset;           // 归还时是否"预置俯仰到重置值"（钩索用：它一定会解冻⇒引擎必然重置；camtest 不设）

        // 🔴 外部朝向驱动（瞄准相机 2026-10-04）：非零 = 本帧 ArmYaw/ArmPitch 用外部每帧喂的值（鼠标 look）。
        //    瞄准相机是"接管期间玩家还要转视角"的场景 —— 方向自己掌控，不走归还 chase 那套。
        private static bool _followExternalLook;
        private static float _followExternalLookYaw;
        private static float _followExternalLookPitch;
        // 调用方写过 Pivot/Socket 偏移（瞄准相机）⇒ 归还时把它们滑回 0（= 引擎口径）。
        // 模板/演出跟随从不写这两个字段 ⇒ 保持 false ⇒ 行为不变。
        private static bool _followOffsetsDirty;
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
        private static float _followHandBlendSeconds = FollowBlendSeconds;   // 归还滑行时长（调用方可指定，见 RequestHandBack）
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

        /// <summary>撒手后隔多久采一次"稳定值"（交班诊断的第三组数，秒）。</summary>
        private const float PostReleaseLogDelaySeconds = 0.5f;
        private static float _postReleaseLogTimer = -1f;   // &lt;0 = 没在等
        private static bool _postReleaseLogFirst;          // 撒手后**第 1 帧**采一次（引擎机位刚写上，和交班行直接比）
        private const float PostReleaseWatchSeconds = 3f;  // 撒手后的连续采样总时长（每 0.5 秒一行；仅 DebugLogging）
        private static float _postReleaseWatchTimer = -1f;
        private static int _postReleaseWatchLeft;

        /// <summary>归还时"方向追引擎实时值"的平滑时间常数（秒）—— ~0.35 ⇒ 约一个滑行时长内收敛。</summary>
        private const float HandBackLookChaseTau = 0.35f;
        /// <summary>方向没跟完时的额外等待上限（秒）—— 引擎若在滑行末尾才改写角度，最多多等这么久把它跟平。</summary>
        private const float HandBackLookHoldMaxSeconds = 1.2f;
        /// <summary>方向收敛判据（度）—— 两端口径差小于它才允许撒手。</summary>
        private const float HandBackLookConvergedDeg = 2.0f;
        private static float _followHandHoldT;                     // 方向未收敛时的已等待时长
        private static float _followLastEngineYaw = float.NaN;     // 引擎角度改写检测（诊断日志用）
        private static float _followLastEnginePitch;
        // 🔴 归还滑行期间"方向"的**当前值**（chase 自己的状态，逐帧累积）。
        //    不能直接改 `_followCurrent` —— 它每帧都被 `Lerp(_followHandFrom, _followHandTo, t)` 重算回起点
        //    （方向两端是同一个值），写进去的增量下一帧就被抹掉 ⇒ 实测俯仰 2.4 秒纹丝不动（2026-10-04 07:42 日志）。
        private static float _followHandLookYaw;
        private static float _followHandLookPitch;
        // 🔴 **方向归还可以和臂长归还分开起跑**（2026-10-04 用户要求"从开始拉拽的时候就渐变"）：
        //    钩索在拉拽一开始就调 BeginLookReturn，方向先走；臂长/FOV 仍按 camret 的时机（默认后半程）。
        private static bool _followLookReturning;      // 方向归还中
        private static float _followLookTau = 0.35f;   // 本次方向归还的时间常数（按请求时长算）
        private static float _followLookSeconds;       // 请求的总时长（日志用）
        // 起跑那一刻"引擎冻着的那组值"（基准）：引擎的值一旦偏离它 >2° 就说明**重置已发生** ⇒ 之后以实时值为准。
        // 🔴 为什么不一直盯预测值：重置之后引擎的值还会被玩家/引擎自己继续动
        //    （实测 07:56 拉 2：解冻后引擎 bearing 以 ~40°/s 继续转），预测值立刻过期。
        private static float _followLookLiveYaw0 = float.NaN;
        private static float _followLookLivePitch0;

        /// <summary>跟随模式是否在跑（诊断/命令回显用）。</summary>
        public static bool IsFollowing => _followActive;

        /// <summary>
        /// **相机诊断日志总开关（默认关）** —— `custom.cam log 1` 打开（会话级）。
        /// 关着时只留少量生命周期行（接管 / 已归还 / 强制撒手 / 异常），逐帧 tick、交班明细、
        /// 撒手后采样、引擎内参等**诊断一律不打**（2026-10-04 用户要求：默认别开）。
        /// 显式命令（`custom.cam stat` 等）不受它影响 —— 那是人手敲的。
        /// </summary>
        public static bool DebugLogging;

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
            return ApplyFollowFromEngineCamera(agent, seconds, writeBackLookOnReturn: false);
        }

        /// <summary>
        /// 同上，但可选择**归还时把我们的朝向写回引擎**（`CameraBearing/Elevation`，反射写私有 setter）。
        ///
        /// 🔴 为什么需要（2026-10-03 钩索实机）：引擎的 bearing 在**接管期间也会变**
        ///    （实测：开火后瞄具复位把 bearing 转了 ~87°）—— 如果归还时"渐回引擎角度"，
        ///    玩家看到的就是**落地瞬间镜头大范围旋转**。写回 = 引擎从我们停的地方接着看 ⇒ **零旋转**。
        ///    做法与飞行工程 `FlightCameraRig.HandBackLookToEngine` 同源；默认 **false**。
        ///
        /// ⚠️ **钩索 2026-10-03 晚起不再用写回**（用户裁定"完全交还引擎"）：写回是把引擎的角度改掉，
        ///    而引擎会在滑行期间**自己把角度改回它自己的默认**（实测俯仰 49°→0.0°）⇒ 写回被覆盖、
        ///    撒手时俯仰突变 49°。新规则见 <see cref="BeginFollowHandBack"/> ②（**滑行期间追引擎实时值、
        ///    跟平才撒手**）。写回这条路保留给其它调用方。
        /// </summary>
        public static bool ApplyFollowFromEngineCamera(Agent agent, float seconds, bool writeBackLookOnReturn)
        {
            return ApplyFollowFromEngineCamera(agent, seconds, writeBackLookOnReturn, clearSpecialCameraOnReturn: false);
        }

        /// <summary>
        /// 同上，外加 <paramref name="clearSpecialCameraOnReturn"/>：**撒手前清掉引擎"特殊相机"的冻结修正**
        /// （钩索用 —— 这是"落地瞬间相机高度台阶"的根治，详见 <see cref="ClearEngineSpecialCameraAdds"/>）。
        /// 默认 false：演出/模板那两条路径行为不变。
        /// </summary>
        public static bool ApplyFollowFromEngineCamera(Agent agent, float seconds, bool writeBackLookOnReturn,
                                                       bool clearSpecialCameraOnReturn)
        {
            return ApplyFollowFromEngineCamera(agent, seconds, writeBackLookOnReturn, clearSpecialCameraOnReturn,
                                               predictResetOnReturn: false);
        }

        /// <summary>
        /// 同上，外加 <paramref name="predictResetOnReturn"/>：归还时**预置俯仰为"引擎重置后的值"**（=0）。
        /// 🔴 只有**确定引擎会重置**的调用方才能开（钩索：收尾会解冻 `Agent.Controller` ⇒ 引擎必然重置相机）；
        /// camtest 这类"不解冻、引擎不会重置"的场景必须关 —— 否则我们会把俯仰硬拉向 0，
        /// 而引擎保持它自己的值（实测 1.9°），撒手时反而制造一个不存在的差（2026-10-04 抓到的 bug）。
        /// </summary>
        public static bool ApplyFollowFromEngineCamera(Agent agent, float seconds, bool writeBackLookOnReturn,
                                                       bool clearSpecialCameraOnReturn, bool predictResetOnReturn)
        {
            if (!TryBuildEngineCameraParam(agent, out SpringArmCameraParam p))
                return false;
            _followWriteBackLook = writeBackLookOnReturn;
            _followClearSpecialOnReturn = clearSpecialCameraOnReturn;
            _followPredictReset = predictResetOnReturn;

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
            _followLookReturning = false;
            _followExternalLook = false;
            _followOffsetsDirty = false;
            _followHandT = 1f;
            _followUseTimeout = seconds > 0f;
            _followRemain = seconds;
            _followElapsed = 0f;
            _followHasLast = false;
            _followErrorLogged = false;
            _followActive = true;

            // 生命周期行（**始终打**，一行）：谁接管、多久。详细数值（引擎原始角度 / 起飞前机位 / 实体）默认关。
            DebugLogger.Log($"[FollowCam] 接管相机（{desc}）锚={agent.Name} " +
                            $"时长={(_followUseTimeout ? seconds.ToString("0.0") + "s" : "不限")} " +
                            $"臂长 {from.ArmLength:F1}->{target.ArmLength:F1} fov {from.Fov:F0}->{target.Fov:F0}");

            if (DebugLogging)
            {
                // 🔴 **交班诊断·第一组数**（2026-10-03 用户要求"前中后三组参数能并排比"）：
                //    接管**前**的引擎默认相机要留底 —— 本行 from = 引擎此刻的视距/FOV；
                //    再补引擎**原始**朝向（`CameraBearing/Elevation`，未钳位未换算）⇒ 与"我们照抄的那份"并排，
                //    钳位/口径差异一眼可见（俯仰被钳到 ±85 就是在这种地方露出来的）。
                // 🔴 **起飞前**的那组数（2026-10-04 用户要求"记录和角色眼睛的高度差、看是否平滑"）：
                //    接管发生在 CustomCamera 还是 null 的时候 ⇒ 这里读到的是**引擎相机自己的机位**。
                //    和接管后的每帧行（Δ眼）并排看 = "接管那一瞬间高度跳没跳"。
                string preTakeover = string.Empty;
                try
                {
                    Mission preMission = Mission.Current;
                    Agent preAgent = preMission?.MainAgent;
                    if (preMission != null)
                    {
                        Vec3 prePos = preMission.GetCameraFrame().origin;
                        preTakeover = $"| 起飞前引擎机位：相机=({prePos.x:F2},{prePos.y:F2},{prePos.z:F2}) "
                                    + $"Δ眼={FmtCameraVsEye(prePos, preAgent, true)}m ";
                    }
                }
                catch { }

                DebugLogger.Log($"[FollowCam] 接管明细（{desc}）：" +
                                $"我们 yaw={target.ArmYaw:F0} pitch={target.ArmPitch:F0} " +
                                EngineLookSuffix() +
                                preTakeover +
                                $"| 相机实体={DescribeEntity(view._customCamera)}");
            }
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
                // 🔴 **别收窄这个 pitch 钳位**（2026-10-03 实机教训）：以前是 [−75, 45]，
                //    而**钩索常常要仰头瞄屋顶/崖顶**（pitch 超出 45）⇒ 我们抓到的机位与引擎实际角度
                //    系统性对不上，归还渐变时镜头就得**转回那个差值**（用户症状："落地瞬间镜头和角色大幅旋转"）。
                //    钳到 ±85 只防"臂翻转"，其余一律照抄引擎 ⇒ 接管前后两边严丝合缝。
                ArmPitch = MBMath.ClampFloat(look.RotationX * RadToDeg, -85f, 85f),
                Fov = fov,
                IsAnchorWorld = true,                 // ← 方向冻在世界里（不跟角色转身）
                // 🔴 锚点高度用**引擎自己的公式**（脚底 + (monster 眼高 + 0.2) × 缩放 ≈ 1.90 米）——
                //    跟随相机撒手后是引擎接管，两边环绕点必须同高，否则撒手瞬间角色"跳高一截"（2026-10-04 用户实机）。
                UseEngineEyeHeight = true,
            };
            return true;
        }

        /// <summary>
        /// **跟随期间改臂长（米）** —— 给"脚本驱动的短期接管"用（钩索拉拽 2026-10-03：
        /// 方向用接管那一刻的引擎机位、**不硬切**，只把机位拉远到能看全身/看弧线）。
        ///
        /// 🔴 **只改 `<see cref="_followTarget"/>`，绝不碰 `<see cref="_followFrom"/>`**（2026-10-03 实机教训）：
        ///   `_followFrom` 是**接管那一刻的引擎相机快照**，归还渐变（<see cref="BeginFollowHandBack"/>）
        ///   拿它当"臂长/FOV 的终点" —— 一旦被覆盖，渐变就变成"8→8"，撒手瞬间引擎相机从 8 米**弹回**
        ///   它自己的视距 ⇒ 观感 = "最后释放缺一段平滑过渡"。
        ///   写进 target = 进场时臂长会从引擎值**平滑长到**目标值（更自然），归还时再平滑回落 ✓。
        /// </summary>
        public static void SetFollowArmLength(float meters)
        {
            if (!_followActive || meters <= 0f)
            {
                return;
            }
            _followTarget.ArmLength = meters;
        }

        /// <summary>
        /// **外部朝向驱动**（瞄准相机 2026-10-04）：每帧调用则本帧方向 = 给定角（spring-arm 世界口径，
        /// 与 <see cref="SpringArmCameraParam.ArmYaw"/>/<c>ArmPitch</c> 同源）。停止用 <see cref="ClearFollowLook"/>。
        /// 给"接管期间玩家还要转视角"的场景（鼠标 look 由调用方解算）。
        /// </summary>
        public static void SetFollowLook(float yawDeg, float pitchDeg)
        {
            _followExternalLook = true;
            _followExternalLookYaw = yawDeg;
            _followExternalLookPitch = pitchDeg;
        }

        /// <summary>停用外部朝向驱动（方向回到渐变/归还 chase 决定）。</summary>
        public static void ClearFollowLook()
        {
            _followExternalLook = false;
        }

        /// <summary>
        /// **改"环绕点"偏移 Z**（米；角色系 —— 直立时 = 上下）＝ UE 弹簧臂的 **TargetOffset.Z**。
        /// 瞄准相机用它把环绕点压到蹲姿身位（引擎眼高公式只认引擎 `CrouchMode`、认不出动画蹲姿）。
        /// 已写过偏移的跟随，**归还渐变会把它平滑滑回 0**（引擎口径），防撒手瞬间"环绕点跳一下"。
        /// </summary>
        public static void SetFollowPivotZ(float meters)
        {
            if (!_followActive)
            {
                return;
            }
            _followTarget.PivotZ = meters;
            _followOffsetsDirty = true;
        }

        /// <summary>
        /// **改"相机"偏移 Z**（米；相机系 —— 画面上下）＝ UE 弹簧臂的 **SocketOffset.Z**。
        /// 画面里人偏上/偏下时的最后微调（瞄准相机 `aimlift`）。归还同样滑回 0。
        /// </summary>
        public static void SetFollowSocketZ(float meters)
        {
            if (!_followActive)
            {
                return;
            }
            _followTarget.SocketZ = meters;
            _followOffsetsDirty = true;
        }

        /// <summary>读当前生效方向（瞄准相机接管时**播种**用 = 接管那一刻的引擎机位 ⇒ 不硬切）。</summary>
        public static bool TryGetFollowLook(out float yawDeg, out float pitchDeg)
        {
            yawDeg = _followCurrent.ArmYaw;
            pitchDeg = _followCurrent.ArmPitch;
            return _followActive;
        }

        /// <summary>改"归还三件套"（瞄准相机被拉拽**收编**时用：把钩索拉拽那套旗标接过来）。</summary>
        public static void SetFollowReturnBehavior(bool writeBackLook, bool clearSpecial, bool predictReset)
        {
            _followWriteBackLook = writeBackLook;
            _followClearSpecialOnReturn = clearSpecial;
            _followPredictReset = predictReset;
        }

        /// <summary>改跟随超时（收编时把拉拽侧的时长余量接过来；seconds ≤ 0 = 不限时）。</summary>
        public static void SetFollowTimeout(float seconds)
        {
            _followUseTimeout = seconds > 0f;
            _followRemain = seconds;
            _followElapsed = 0f;
        }

        /// <summary>
        /// **请求渐变归还**（= 超时那条路，只是由调用方主动触发）：
        /// 把臂长/FOV 滑回接管时引擎相机的值，滑完自动撒手。给"脚本驱动的短期接管"用
        /// （钩索拉拽 2026-10-03：拉完想滑回引擎相机，而不是硬切一下）。
        /// <paramref name="blendSeconds"/> &gt; 0 = 指定滑行时长（钩索"提前归还"要滑得久一点才看得出是渐变，
        /// 见 `GrapplePull.CameraReturnGlideSeconds`）；≤ 0 = 用默认 <see cref="FollowBlendSeconds"/>。
        /// 已经在归还中 / 没接管 = 什么都不做。
        /// </summary>
        public static void RequestHandBack(float blendSeconds = 0f)
        {
            if (_followActive && !_followHandingBack)
            {
                BeginFollowHandBack(blendSeconds);
            }
        }

        /// <summary>立刻归还相机（幂等；任何异常都要保证还）。**不渐变**（异常/收摊路径用）。</summary>
        public static void StopFollowCamera()        {
            if (!_followActive)
                return;

            _followActive = false;
            _followHandingBack = false;
            _followLookReturning = false;
            _followExternalLook = false;
            _followOffsetsDirty = false;
            try
            {
                MissionScreen screen = ScreenManager.TopScreen as MissionScreen;
                Camera cam = Mission.Current?.GetMissionBehavior<SpringArmCameraView>()?._customCamera;

                // 🔴 **交班诊断·第二组数**（2026-10-03 用户要求）：撒手那一刻，"我们最后一帧"与"引擎此刻"
                //    四项并排 —— 两行数字越接近 = 撒手越无缝（有残差 = 撒手瞬间会"跳"一下，数字直接指出是哪项）。
                //    **默认关**（`custom.cam log 1` 打开）。
                if (DebugLogging)
                {
                    LogHandOff(_followCurrent);
                }

                // 🔴 **撒手前清掉引擎冻结的"特殊相机"修正**（瞄准态残留 = 落地高度台阶的来源；见 ClearEngineSpecialCameraAdds）
                if (_followClearSpecialOnReturn)
                {
                    ClearEngineSpecialCameraAdds();
                }

                if (screen != null && cam != null && ReferenceEquals(screen.CustomCamera, cam))
                    screen.CustomCamera = null;      // 置空 = 引擎相机回来
                DebugLogger.Log("[FollowCam] 已归还相机");

                // 🔴 **交班诊断·第三组数**：第 1 帧 + 0.5s + 之后每 0.5 秒共 3 秒（**仅 DebugLogging**）。
                _postReleaseLogFirst = true;
                _postReleaseLogTimer = PostReleaseLogDelaySeconds;
                _postReleaseWatchLeft = (int)(PostReleaseWatchSeconds / 0.5f);
                _postReleaseWatchTimer = 0.5f;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[FollowCam] 归还相机异常（重进场景可恢复）: {ex.Message}");
            }
        }

        /// <summary>
        /// 归还前渐变：把**臂长 / FOV** 滑回接管时引擎相机的值（方向不渐，见下），滑完才真撒手。
        /// 为什么要：引擎相机复位时用它自己的视距/FOV，而我们的机位跟它差不少 ⇒ 硬切会"跳"一下。
        /// <paramref name="blendSeconds"/> = 滑行时长（≤ 0.05 用默认 <see cref="FollowBlendSeconds"/>）——
        /// 钩索"提前归还"给 1 秒出头（滑行短了看着像硬切，2026-10-03 用户反馈）。
        ///
        /// 🔴 **方向也要渐（2026-10-03 加，钩索拉拽实机要求）**：只有当**我们这一份是世界锚定**时才渐
        ///    （世界锚定 = 有"世界 yaw"可比），终点 = **预测的引擎机位** —— 引擎接管期间不处理鼠标 look，
        ///    所以它的 `CameraBearing/CameraElevation` 冻着不动，而那正是撒手后它要用的角度
        ///    （见 `CameraLook.TryGetEngineAnglesRaw`）⇒ 朝它渐 = 撒手那一帧两边严丝合缝。
        ///    另外老实现里 pitch 被钳到 [−75,45]（`TryBuildEngineCameraParam`），越界时方向本来就对不上，
        ///    这一渐把这种偏差也一起抹平。
        /// </summary>
        private static void BeginFollowHandBack(float blendSeconds = 0f)
        {
            _followHandingBack = true;
            _followHandT = 0f;
            _followHandHoldT = 0f;
            _followHandBlendSeconds = blendSeconds > 0.05f ? blendSeconds : FollowBlendSeconds;
            _followHandFrom = _followCurrent;
            _followHandTo = _followTarget;
            _followHandTo.ArmLength = _followFrom.ArmLength;   // ← _followFrom 存的正是引擎相机那组值
            _followHandTo.Fov = _followFrom.Fov;
            // 写过 Pivot/Socket 偏移的跟随（瞄准相机 / 拉拽收编它）⇒ 归还时滑回 0（= 引擎口径），
            // 不归零 = 撒手瞬间"环绕点/机位跳一下"（2026-10-04 落地高度台阶的同族问题）。
            // 模板/演出跟随从不写这两个字段 ⇒ dirty=false ⇒ 行为不变。
            if (_followOffsetsDirty)
            {
                _followHandTo.PivotZ = 0f;
                _followHandTo.SocketZ = 0f;
            }
            // 方向 = 独立状态、逐帧累积（见字段注释）。**已经在归还中就不要重置** ——
            // 重置 = 每帧从起点重来 = 增量丢失（2026-10-04 那个"俯仰纹丝不动"的 bug）。
            if (!_followLookReturning)
            {
                _followHandLookYaw = _followHandFrom.ArmYaw;
                _followHandLookPitch = _followHandFrom.ArmPitch;
                _followLookTau = HandBackLookChaseTau;
                SampleEngineLookBaseline();
            }
            _followLookReturning = true;

            // ① 写回引擎（只在对调用方显式要求时；钩索 2026-10-03 起不再走这条，见 ②）。
            bool wroteBack = false;
            if (_followWriteBackLook && _followFrom.IsAnchorWorld)
            {
                wroteBack = WriteBackLookToEngine(_followHandFrom.ArmYaw, _followHandFrom.ArmPitch);
            }

            // ② **方向：滑行期间朝"引擎重置后的值"走**（2026-10-03 用户裁定"完全交还引擎"）。
            //    🔴 为什么不能把目标定死就完事：**我们解冻玩家（`Agent.Controller = Player`）之后一帧，
            //       引擎会把自己的相机角重置成一组固定值** —— 反编译实证（完整链路见 `ChaseEngineLook`）：
            //       `Agent.Controller` setter（切到 Player 时）→ `Mission.MainAgent = this` → `OnMainAgentChanged`
            //       → 下一帧 `HandleUserInput`（由 `BeforeMissionTick` 每帧调用、**不受 CustomCamera 挡板保护**）
            //       执行 `CameraBearing = 角色移动方向; CameraElevation = 0f`。
            //       实测 22:52：解冻（02.951）之后我们读到 `0.0°/117.8°` —— 正是这组；撒手时引擎用的就是它，
            //       所以出现俯仰突变 49°（用户实机看到的）。
            //    所以：方向**不交给 Lerp**（两端设成同一个值），由 `ChaseEngineLook` 每帧朝
            //    **预测的重置值**（引擎自己的公式，不是猜）走；到点还没跟完就**延后撒手**
            //    （最多 `HandBackLookHoldMaxSeconds`），撤手与否由"与引擎**实时值**收敛"把关。
            float yawDelta = 0f, pitchDelta = 0f;
            bool chaseLook = false;
            if (!wroteBack && _followFrom.IsAnchorWorld)
            {
                _followHandFrom.IsAnchorWorld = true;              // 两端口径统一（世界锚定）
                _followHandTo.IsAnchorWorld = true;
                _followHandTo.ArmYaw = _followHandFrom.ArmYaw;     // 方向交给 ChaseEngineLook，不走 Lerp
                _followHandTo.ArmPitch = _followHandFrom.ArmPitch;
                chaseLook = true;
                if (CameraLook.TryGetEngineAnglesRaw(out float engYawDeg, out float engPitchDeg))
                {
                    yawDelta = Normalize180(_followHandFrom.ArmYaw - engYawDeg);
                    pitchDelta = _followHandFrom.ArmPitch - engPitchDeg;
                }
                _followLastEngineYaw = float.NaN;                  // 让滑行第一帧把"引擎当前角度"记进日志
            }

            DebugLogger.Log($"[FollowCam] 归还渐变开始：臂长 {_followHandFrom.ArmLength:F1}→{_followHandTo.ArmLength:F1} "
                + $"fov {_followHandFrom.Fov:F0}→{_followHandTo.Fov:F0} "
                + $"用时 {_followHandBlendSeconds:F2}s "
                + (wroteBack
                    ? $"方向：已写回引擎（{_followHandFrom.ArmYaw:F0}/{_followHandFrom.ArmPitch:F0}°）—— 撒手零旋转"
                    : (chaseLook
                        ? $"方向：朝预测重置值走（我们 {_followHandFrom.ArmYaw:F0}/{_followHandFrom.ArmPitch:F0}°，此刻引擎差 {yawDelta:F0}/{pitchDelta:F0}°）"
                        : "方向：不渐（非世界锚定）")));
        }

        /// <summary>
        /// 把我们的朝向**写回引擎**（`CameraBearing` / `CameraElevation`；角度是私有 setter，走反射 ——
        /// 本项目既有手法，见飞行工程 `FlightCameraRig.HandBackLookToEngine`）。
        /// 角度口径实测对称：引擎 look = `RotateAboutUp(bearing)` 再 `RotateAboutSide(elevation)`，
        /// 而 `Vec3.RotationZ/RotationX` 正好是它的可逆分解（反编译实证）⇒ 直接对写即可，无需符号校准。
        /// 失败返回 false（调用方退回"渐回引擎角度"），只告警一次。
        /// </summary>
        private static bool WriteBackLookToEngine(float yawDeg, float pitchDeg)
        {
            try
            {
                if (!(ScreenManager.TopScreen is MissionScreen screen))
                {
                    return false;
                }
                const System.Reflection.BindingFlags F =
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic;
                var bearingSet = typeof(MissionScreen).GetProperty("CameraBearing", F)?.GetSetMethod(true);
                var elevSet = typeof(MissionScreen).GetProperty("CameraElevation", F)?.GetSetMethod(true);
                if (bearingSet == null || elevSet == null)
                {
                    if (!_lookWriteWarned)
                    {
                        _lookWriteWarned = true;
                        DebugLogger.Log("[FollowCam] 写不回引擎朝向（反射找不到 setter）—— 归还时退回'渐回引擎角度'");
                    }
                    return false;
                }
                const float DegToRad = MathF.PI / 180f;
                bearingSet.Invoke(screen, new object[] { yawDeg * DegToRad });
                elevSet.Invoke(screen, new object[] { pitchDeg * DegToRad });
                DebugLogger.Log($"[FollowCam] 朝向已写回引擎：bearing={yawDeg:F0}° elev={pitchDeg:F0}°（撒手零旋转）");
                return true;
            }
            catch (Exception ex)
            {
                if (!_lookWriteWarned)
                {
                    _lookWriteWarned = true;
                    DebugLogger.Log($"[FollowCam] 写回引擎朝向异常（退回渐变）: {ex.Message}");
                }
                return false;
            }
        }

        private static bool _lookWriteWarned;

        /// <summary>
        /// **撒手前清掉引擎"特殊相机"的冻结修正**（2026-10-04；"落地瞬间相机高度台阶"的根治）。
        ///
        /// 背景（反编译 + 实测）：`_cameraSpecial*` 这一组值**只在 View 装配的 `UpdateCamera` 里更新**，
        /// 而我们接管相机期间 `UpdateCamera` 整段被跳过 ⇒ 它们**冻在接管那一刻**（开火时的瞄准态）。
        /// 撒手后引擎恢复的第一帧先带上这份冻值、再按 4/s 平滑抹掉 —— 玩家看到的就是
        /// "落地瞬间相机高度台阶"（实测 0.09~0.51 米，随开火姿态变；对照实验已排除"垂直瞄准修正"）。
        /// 我们在撒手前把它们**归零** ⇒ 引擎恢复的第一帧就是无修正的纯几何机位 = 我们的机位 ⇒ 零台阶。
        /// （同款手法 = `WriteBackLookToEngine` 反射写 `CameraBearing/Elevation`；反射失败 = 什么都不做，退回旧行为。）
        /// 只清**方向/位置/距离**三项，不动 FOV 那两项（FOV 由我们自己的归还渐变负责，实测两边一致）。
        /// </summary>
        private static void ClearEngineSpecialCameraAdds()
        {
            try
            {
                if (!(ScreenManager.TopScreen is MissionScreen screen))
                {
                    return;
                }
                const System.Reflection.BindingFlags F =
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic;
                Type t = typeof(MissionScreen);
                var sb = new System.Text.StringBuilder();
                int cleared = 0;
                bool anyNonZero = false;

                string[] floatFields =
                {
                    "_cameraSpecialCurrentAddedBearing", "_cameraSpecialTargetAddedBearing",
                    "_cameraSpecialCurrentAddedElevation", "_cameraSpecialTargetAddedElevation",
                    "_cameraSpecialCurrentDistanceToAdd", "_cameraSpecialTargetDistanceToAdd",
                };
                foreach (string name in floatFields)
                {
                    System.Reflection.FieldInfo f = t.GetField(name, F);
                    if (f == null || f.FieldType != typeof(float))
                    {
                        continue;
                    }
                    float old = (float)f.GetValue(screen);
                    if (Math.Abs(old) > 1e-6f)
                    {
                        anyNonZero = true;
                    }
                    sb.Append(name.Replace("_cameraSpecial", "")).Append('=').Append(old).Append(" ");
                    f.SetValue(screen, 0f);
                    cleared++;
                }
                string[] vecFields = { "_cameraSpecialCurrentPositionToAdd", "_cameraSpecialTargetPositionToAdd" };
                foreach (string name in vecFields)
                {
                    System.Reflection.FieldInfo f = t.GetField(name, F);
                    if (f == null || f.FieldType != typeof(Vec3))
                    {
                        continue;
                    }
                    Vec3 old = (Vec3)f.GetValue(screen);
                    if (old.LengthSquared > 1e-6f)
                    {
                        anyNonZero = true;
                    }
                    sb.Append(name.Replace("_cameraSpecial", "")).Append('=').Append(old).Append(" ");
                    f.SetValue(screen, Vec3.Zero);
                    cleared++;
                }

                // 有非零才值得报（真清掉了东西）；全零是常态，只在调试开关打开时打一行。
                if (anyNonZero)
                {
                    DebugLogger.Log($"[FollowCam] 撒手前清掉引擎特殊相机修正（{cleared} 项，**有非零**）：{sb}");
                }
                else if (DebugLogging)
                {
                    DebugLogger.Log($"[FollowCam] 撒手前引擎特殊相机修正 = 全零（{cleared} 项，无需清）");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[FollowCam] 清引擎特殊相机修正异常（忽略，退回旧行为）：{ex.GetType().Name} {ex.Message}");
            }
        }

        private static float Normalize180(float deg)
        {
            while (deg > 180f) deg -= 360f;
            while (deg <= -180f) deg += 360f;
            return deg;
        }

        // ─────────────────── 交班诊断（2026-10-03 用户要求：接管前 / 过程中 / 撒手后 三组数并排比） ───────────────────

        /// <summary>
        /// **只启动"方向归还"**（臂长/FOV 的归还仍由 <see cref="RequestHandBack"/> 按自己的时机起）——
        /// 给"想从更早开始把镜头转过去"的调用方用（钩索 2026-10-04：**拉拽一开始**就调它，
        /// 方向在整个拉拽里平顺走完；臂长仍保持宽镜到后半程）。
        /// <paramref name="seconds"/> = 期望在这一时长内转完（内部换算成时间常数，指数收敛 ~97%）。
        /// 幂等：已经在归还中就不重复初始化（否则每帧重置 = 增量不累积，就是 2026-10-04 那个 bug）。
        /// </summary>
        public static void BeginLookReturn(float seconds)
        {
            if (!_followActive || _followLookReturning)
            {
                return;
            }
            _followLookReturning = true;
            _followLookSeconds = seconds;
            _followLookTau = MBMath.ClampFloat((seconds > 0.1f ? seconds : 1.2f) / 3.5f, 0.2f, 1.5f);
            SampleEngineLookBaseline();
            // 还没进归还 → 从**当前**方向起跑（进了归还的话 chase 状态已经在跑，不要重置）
            if (!_followHandingBack)
            {
                _followHandLookYaw = _followCurrent.ArmYaw;
                _followHandLookPitch = _followCurrent.ArmPitch;
            }
            DebugLogger.Log($"[FollowCam] 方向归还开始：{seconds:F2}s 内平顺转到预测重置值（τ={_followLookTau:F2}s）");
        }

        /// <summary>
        /// **滑行期间：把我们的方向转向"引擎重置后的值"**（归还的核心 —— 见 <see cref="BeginFollowHandBack"/> ②）。
        ///
        /// 🔴 **目标不是"引擎此刻的值"，是"引擎即将重置成的值"** —— 因为重置的**时机与公式都是确定的**
        /// （反编译实证，`TaleWorlds.MountAndBlade.View.dll` 的 `MissionScreen.HandleUserInput`）：
        ///   ① `Agent.Controller` setter：`if (value == Player) Mission.MainAgent = this;`
        ///      —— **我们解冻玩家（`Controller = Player`）就会触发**；
        ///   ② `Mission.MainAgent` setter 无相等判断 ⇒ 触发 `OnMainAgentChanged`；
        ///   ③ `MissionScreen._isPlayerAgentAdded = true` ⇒ **下一帧** `HandleUserInput`
        ///      （由 `BeforeMissionTick` 每帧调用、**不受 `CustomCamera` 挡板保护**）执行一次性初始化：
        ///      第三人称 `CameraBearing = MainAgent.MovementDirectionAsAngle; CameraElevation = 0f;`
        ///      （第一人称则都取 `MainAgent.LookDirection`）。
        /// ⇒ 引擎手里的值会在**解冻后一帧跳成上面这组**。如果等它跳了再追，只剩零点几秒（实测 0.22s），
        ///    会甩得很快、还要靠"收敛才撒手"补时间；**提前朝这个已知值走，整个滑行平摊这一转**。
        ///
        /// 追不上/预测不了都不怕：撒手与否由 <see cref="EngineLookConverged"/>（对**引擎实时值**）把关。
        /// 🔴 **yaw 必须走最短弧**（<see cref="Normalize180"/>）：引擎可能给 −240° 这种等价角。
        /// </summary>
        private static void ChaseEngineLook(ref float yaw, ref float pitch, float dt)
        {
            bool hasLive = CameraLook.TryGetEngineAnglesRaw(out float liveYaw, out float livePitch);

            // 引擎还冻在"起跑那一刻那组"吗？（偏离 >2° ⇒ 重置已发生）
            bool liveMoved = false;
            if (hasLive && !float.IsNaN(_followLookLiveYaw0))
            {
                liveMoved = MathF.Abs(Normalize180(liveYaw - _followLookLiveYaw0)) > 2f
                         || MathF.Abs(livePitch - _followLookLivePitch0) > 2f;
            }

            float targetYaw, targetPitch;
            string src;
            if (_followPredictReset && !liveMoved && TryPredictEngineResetLook(out float predYaw, out float predPitch))
            {
                // ⚠️ **只预置俯仰**（= 0，稳定且正确）；**yaw 不预置**。
                //    🔴 2026-10-04 实机抓到：预测的 yaw = `MovementDirectionAsAngle` = 角色的**移动方向**，
                //    而拉拽刚开始时它还是**上一个走路方向**（实测 253° vs 瞄准 129°，差 124°）——
                //    一开火相机就朝它猛甩（~200°/s）再甩回来 = 用户报的"起飞 yaw 明显突变"。
                //    yaw 停在引擎当前值（= 瞄准方向）上就好；引擎重置时实际只差几度，跟一下即可。
                targetYaw = hasLive ? liveYaw : predYaw;
                targetPitch = predPitch;
                src = "预测俯仰(yaw 保持)";
            }
            else if (hasLive)
            {
                // 🔴 **重置已发生（或预测不了）⇒ 实时值为准**：此后引擎的值还会被继续动
                //    （实测解冻后以 ~40°/s 转），预测值已过期，追预测 = 越追越远。
                targetYaw = liveYaw;
                targetPitch = livePitch;
                src = "引擎实时值";
            }
            else
            {
                return;
            }

            // 诊断：第一帧 / 目标变了 >1° 记一行（仅 DebugLogging）
            if (DebugLogging
                && (float.IsNaN(_followLastEngineYaw)
                    || MathF.Abs(Normalize180(targetYaw - _followLastEngineYaw)) > 1f
                    || MathF.Abs(targetPitch - _followLastEnginePitch) > 1f))
            {
                _followLastEngineYaw = targetYaw;
                _followLastEnginePitch = targetPitch;
                DebugLogger.Log($"[FollowCam] 滑行中：目标（{src}）= {targetYaw:F1}/{targetPitch:F1}°"
                    + (hasLive ? $"，引擎实时 = {liveYaw:F1}/{livePitch:F1}°" : "，引擎实时=读不到")
                    + $"（我们 {yaw:F1}/{pitch:F1}° → 跟随中）");
            }

            // τ **收尾收紧**：越接近撒手跟得越紧 —— 引擎在解冻后会继续转（实测 ~40°/s），
            // 松跟会在最后一刻攒下十几度残差（= 强制撒手的那个跳）。收紧后残差 = τ×转速 ≈ 2-3°，看不出来。
            float tau = _followLookTau;
            if (_followHandingBack)
            {
                tau = MathF.Max(0.06f, _followLookTau * (1f - _followHandT));
            }
            float k = 1f - (float)Math.Exp(-dt / tau);   // .NET 4.7.2 没有 MathF.Exp
            yaw += Normalize180(targetYaw - yaw) * k;
            pitch += (targetPitch - pitch) * k;
        }

        /// <summary>采样"引擎此刻冻着的那组值"作为基准（方向归起的参照）——
        /// 之后它偏离 &gt;2° 就说明引擎的重置已发生，追法切换成"实时值"。</summary>
        private static void SampleEngineLookBaseline()
        {
            if (CameraLook.TryGetEngineAnglesRaw(out float yaw, out float pitch))
            {
                _followLookLiveYaw0 = yaw;
                _followLookLivePitch0 = pitch;
            }
            else
            {
                _followLookLiveYaw0 = float.NaN;
            }
        }

        /// <summary>
        /// **预测"引擎重置后"的相机朝向** —— 照抄引擎自己的初始化公式（不是猜）：
        /// 第三人称 = (`MainAgent.MovementDirectionAsAngle`, 俯仰 **0**)；第一人称 = `MainAgent.LookDirection` 的两个角。
        /// 触发时机 = `Agent.Controller = Player`（我们解冻）之后一帧，见 <see cref="ChaseEngineLook"/>。
        /// 读不到（没有主角 / native 抛异常）返回 false，调用方回落到"追引擎实时值"。
        /// </summary>
        private static bool TryPredictEngineResetLook(out float yawDeg, out float pitchDeg)
        {
            yawDeg = 0f;
            pitchDeg = 0f;
            try
            {
                Mission mission = Mission.Current;
                Agent main = mission?.MainAgent;
                if (mission == null || main == null)
                {
                    return false;
                }
                const float RadToDeg = 180f / MathF.PI;
                if (mission.CameraIsFirstPerson)
                {
                    Vec3 look = main.LookDirection;
                    yawDeg = look.RotationZ * RadToDeg;
                    pitchDeg = look.RotationX * RadToDeg;
                }
                else
                {
                    yawDeg = main.MovementDirectionAsAngle * RadToDeg;   // 弧度（引擎直接喂 CameraBearing）
                    pitchDeg = 0f;                                        // ← 引擎写死的常量
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>我们与引擎方向的当前最大差（度；读不到引擎角度 = 0 = 视为已收敛，不拦撒手）。</summary>
        private static float EngineLookDeltaDeg(in SpringArmCameraParam p)
        {
            if (!CameraLook.TryGetEngineAnglesRaw(out float engYaw, out float engPitch))
            {
                return 0f;
            }
            float dy = MathF.Abs(Normalize180(engYaw - p.ArmYaw));
            float dp = MathF.Abs(engPitch - p.ArmPitch);
            return MathF.Max(dy, dp);
        }

        private static bool EngineLookConverged(in SpringArmCameraParam p)
            => EngineLookDeltaDeg(p) <= HandBackLookConvergedDeg;

        /// <summary>日志片段：引擎**原始**朝向（`CameraBearing/Elevation`，未钳位、未换算）——
        /// 接管**前**默认相机的真实朝向，与"我们照抄的那份"并排 ⇒ 钳位/口径差异一眼可见
        /// （俯仰被钳到 ±85 这类偏差就藏在这里）。</summary>
        private static string EngineLookSuffix()
        {
            return CameraLook.TryGetEngineAnglesRaw(out float yaw, out float pitch)
                ? $"| 引擎原始 yaw={yaw:F0}° pitch={pitch:F0}° "
                : "| 引擎原始角度=读不到 ";
        }

        /// <summary>**交班行**（撒手那一刻）：我们最后一帧 vs 引擎此刻，臂长/俯仰/yaw/fov 四项并排 ——
        /// 数字越接近 = 撒手越无缝；有残差会直接指出是哪一项（不用靠肉眼猜"跳没跳"）。</summary>
        private static void LogHandOff(in SpringArmCameraParam ours)
        {
            bool hasDist = TryGetEngineCameraDistanceFov(out float dist, out float fov);
            bool hasLook = CameraLook.TryGetEngineAnglesRaw(out float yaw, out float pitch);
            // 我们这一刻的**相机世界位置**：用**我们每帧渲染用的那份**（`_followLastCam`，由 ApplyFollowFrame 记）。
            // 🔴 别读 `Mission.GetCameraFrame()` —— 实测（2026-10-04）它在我们持有相机期间**冻在接管前的机位**
            //    （交班行曾打出 −14 米的假 Δ眼，就是它）。交班行要和"撒手后第1帧"那行的引擎机位直接比。
            Vec3 ourPos = _followLastCam;
            Agent mainAgent = Mission.Current?.MainAgent;
            DebugLogger.Log("[FollowCam] 交班（撒手那一刻）我们："
                + $"相机=({ourPos.x:F2},{ourPos.y:F2},{ourPos.z:F2}) "
                + $"Δ眼={FmtCameraVsEye(ourPos, mainAgent, ours.UseEngineEyeHeight)}m "
                + $"臂长 {ours.ArmLength:F2} 俯仰 {ours.ArmPitch:F1}° yaw {ours.ArmYaw:F1}° fov {ours.Fov:F1} "
                + "| 引擎："
                + (hasDist ? $"臂长 {dist:F2} fov {fov:F1} " : "臂长=? fov=? ")
                + (hasLook ? $"俯仰 {pitch:F1}° yaw {yaw:F1}°" : "俯仰=? yaw=?"));
        }

        /// <summary>相机相对**角色眼睛**的高度差（米，保留两位；&gt;0 = 相机在眼睛之上 = 俯视角色）。
        /// **眼睛 = 字面眼高**（`monster.StandingEyeHeight × 缩放`）—— 引擎的锚点比它高 0.2 米（常量偏移），
        /// 所以稳态俯仰 0 时本值 ≈ +0.2；这个数是"交接前后高度跳没跳"的直接读数（2026-10-04 用户要求）。</summary>
        private static string FmtCameraVsEye(Vec3 camPos, Agent agent, bool engineFormula)
        {
            try
            {
                if (agent == null)
                {
                    return "?";
                }
                float eye = engineFormula
                    ? SpringArmMath.ResolveLiteralEyeHeight(agent)
                    : SpringArmMath.ResolveEyeHeightOffset(agent, false);
                return (camPos.z - (agent.Position.z + eye)).ToString("F2");
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>**跟随期间的固定高度修正**（米）—— `custom.cam lift` 调它（标定/应急用；正常应为 0，
        /// 因为引擎的 +0.484 米项已经按公式补进 `SpringArmMath.ComputeEngineLift`）。
        /// 正值 = 相机整体抬高。</summary>
        public static float CameraLiftMeters;

        /// <summary>跟随期间的固定高度修正（setter 版，兼容旧调用）。</summary>
        public static void SetCameraLiftMeters(float meters)
        {
            CameraLiftMeters = meters;
        }

        /// <summary>控制台诊断入口（`custom.cam info`）：一行报告**我们跟随相机**的现状。</summary>
        public static string DumpFollowCameraNow(string tag)
        {
            try
            {
                Agent agent = _followAgent ?? Mission.Current?.MainAgent;
                string eye = "?";
                string lift = "?";
                if (agent != null)
                {
                    eye = SpringArmMath.ResolveEyeHeightOffset(agent, _followCurrent.UseEngineEyeHeight).ToString("F2");
                    lift = SpringArmMath.ComputeEngineLift(agent, _followCurrent.ArmLength).ToString("F3");
                }
                string line = $"[FollowCam] {tag}：在跟={(_followActive ? 1 : 0)} 归还中={(_followHandingBack ? 1 : 0)} "
                    + $"方向归还中={(_followLookReturning ? 1 : 0)} 臂长={_followCurrent.ArmLength:F2} "
                    + $"俯仰={_followCurrent.ArmPitch:F1}° yaw={_followCurrent.ArmYaw:F1}° fov={_followCurrent.Fov:F0} "
                    + $"眼高={eye} 引擎抬升项={lift} 手动 lift={CameraLiftMeters:F2} 日志开关={(DebugLogging ? 1 : 0)}";
                DebugLogger.Log(line);
                return line;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.GetType().Name} {ex.Message}";
            }
        }

        /// <summary>控制台诊断入口（`custom.cam stat`）：**此刻**把引擎默认相机的机位/Δ眼/角度打一行到日志。
        /// 用途：在**任何状态**（站着不动、镜头放平、没拉钩）采样引擎相机，和我们的机位做无歧义对照 ——
        /// 排查"撒手后引擎相机比我们高 0.51 米"（2026-10-04）就靠它。</summary>
        public static void LogEngineCameraNow(string tag)
        {
            LogEngineDefaultCamera(tag);
        }

        /// <summary>**从引擎相机反推它的有效锚高**（相对脚底，米）：相机z + 臂长×sin(视角俯仰) − 脚底z。
        /// 和我们的公式值（站姿 2.17 / 蹲 1.3 …）一比，差多少就是"锚点还有一支我们没抄"（2026-10-04：
        /// 嫌疑最大 = **骑马分支**）。取不到打 `?`。</summary>
        private static string FmtEngineEffectiveAnchor(Vec3 camPos, float viewPitchDeg, float armLen, Agent agent)
        {
            try
            {
                if (agent == null || float.IsNaN(viewPitchDeg) || armLen <= 0.01f)
                {
                    return "?";
                }
                float anchorZ = camPos.z + armLen * MathF.Sin(viewPitchDeg * (MathF.PI / 180f));
                return (anchorZ - agent.Position.z).ToString("F2");
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>读引擎 `MissionScreen` 的私有 float 字段（诊断用；读不到返回 NaN）。</summary>
        private static float ReadScreenFloatField(string name)
        {
            try
            {
                if (!(ScreenManager.TopScreen is MissionScreen screen))
                {
                    return float.NaN;
                }
                System.Reflection.FieldInfo f = typeof(MissionScreen).GetField(name,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
                return f != null && f.FieldType == typeof(float) ? (float)f.GetValue(screen) : float.NaN;
            }
            catch
            {
                return float.NaN;
            }
        }

        /// <summary>引擎内部状态快照（诊断用）：附加仰角 / 锚点平滑高度 / 角色"视觉位置 vs 逻辑位置"的差。
        /// 用途：定位"撒手后引擎相机比我们高 0.5 米"到底出在哪个量上（2026-10-04）。</summary>
        private static string EngineInternalsSuffix(Agent agent)
        {
            float addedElevRad = ReadScreenFloatField("_cameraAddedElevation");
            float anchorH = ReadScreenFloatField("_cameraTargetAddedHeight");
            string visual = "?";
            try
            {
                if (agent != null)
                {
                    visual = (agent.VisualPosition.z - agent.Position.z).ToString("F2");
                }
            }
            catch { }
            return "| 引擎内参[附加仰角="
                + (float.IsNaN(addedElevRad) ? "?" : (addedElevRad * (180f / MathF.PI)).ToString("F1") + "°")
                + " 锚高=" + (float.IsNaN(anchorH) ? "?" : anchorH.ToString("F2"))
                + " 视觉Δz=" + visual + "] ";
        }

        /// <summary>引擎默认相机此刻的四个数 + 世界位置（日志用；取不到的项打 `?`）。</summary>
        private static void LogEngineDefaultCamera(string tag)
        {
            bool hasDist = TryGetEngineCameraDistanceFov(out float dist, out float fov);
            bool hasLook = CameraLook.TryGetEngineAnglesRaw(out float yaw, out float pitch);
            Vec3 pos = Vec3.Zero;
            float viewPitchDeg = float.NaN;      // 引擎相机的**实际视线俯仰**（帧向量算）——和字段俯仰对比可判断"相机高"是旋转还是平移造成的
            try
            {
                Mission m = Mission.Current;
                if (m != null)
                {
                    MatrixFrame camFrame = m.GetCameraFrame();
                    pos = camFrame.origin;
                    viewPitchDeg = (-camFrame.rotation.u).RotationX * (180f / MathF.PI);
                }
            }
            catch
            {
                // 取不到就留零
            }

            // 顺带记玩家位置与"相机↔玩家"距离 —— 用来核**引擎相机撒手后有没有真的跟住人**
            // （2026-10-04 曾出现"撒手后 0.5s 相机还在 13 米外"的读数，需要下一跑复验）。
            Vec3 playerPos = Vec3.Zero;
            float playerDist = -1f;
            Agent main = null;
            try
            {
                main = Mission.Current?.MainAgent;
                if (main != null)
                {
                    playerPos = main.Position;
                    playerDist = pos.Distance(playerPos);
                }
            }
            catch
            {
                // 忽略
            }

            DebugLogger.Log($"[FollowCam] {tag}（引擎默认相机）："
                + (hasDist ? $"臂长 {dist:F2} fov {fov:F1} " : "臂长=? fov=? ")
                + (hasLook ? $"俯仰 {pitch:F1}° yaw {yaw:F1}° " : "俯仰=? yaw=? ")
                + $"相机=({pos.x:F2},{pos.y:F2},{pos.z:F2}) Δ眼={FmtCameraVsEye(pos, main, true)}m"
                + (float.IsNaN(viewPitchDeg) ? "" : $" 视角俯仰={viewPitchDeg:F1}°")
                + $" 引擎有效锚高={FmtEngineEffectiveAnchor(pos, viewPitchDeg, dist, main)}m"
                + (main != null ? $" 骑马={(main.HasMount ? 1 : 0)}" : "")
                + $" 瞄准修正={(BannerlordConfig.EnableVerticalAimCorrection ? 1 : 0)}"   // 排查"落地 0.51m 高度台阶"用（见钩索计划 §10.9）
                + " " + EngineInternalsSuffix(main)
                + (playerDist >= 0f ? $" 玩家=({playerPos.x:F1},{playerPos.y:F1},{playerPos.z:F1}) 距={playerDist:F1}m" : ""));
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

                // 固定高度修正（`camlift` 标定用；0 = 不修）
                if (CameraLiftMeters != 0f)
                {
                    frame.origin.z += CameraLiftMeters;
                }

                LogFollowTick(agent, frame, cam);
                MatrixFrame f = frame;
                MatrixFrame camFrame = frame;

                // 🔴 两处都写：引擎读的是**实体**那份；相机自己那份跟着写（对话/剧情取景也靠它）。
                _followCamEntity.SetGlobalFrame(in f);
                cam.SetFovVertical(fovDeg * (MathF.PI / 180f), Screen.AspectRatio, 0.1f, 1000f);
                cam.Frame = camFrame;
                screen.CustomCamera = cam;

                _followDt = dt;
                if (DebugLogging)
                {
                    LogFollowTick(agent, frame, cam);   // 逐帧诊断（默认关，`custom.cam log 1` 打开）
                }
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

            // 🔴 **每帧一行是排查专用的**（一次拉拽就刷几百行；2026-10-05 用户要求关）——
            //    受 `custom.cam log 1`（`DebugLogging`）开关管；默认关 = 安静。
            //    Δ 记账（_followElapsed / _followHasLast）照留 —— 其它诊断读它，不能跟着一起砍。
            if (DebugLogging)
            {
                DebugLogger.Log($"[FollowCam] t={_followElapsed:F2} " +
                                $"锚=({a.x:F2},{a.y:F2},{a.z:F2})Δ{anchorMoved:F3} " +
                                $"相机=({frame.origin.x:F2},{frame.origin.y:F2},{frame.origin.z:F2})Δ{camMoved:F3} " +
                                $"Δ眼={FmtCameraVsEye(frame.origin, agent, _followCurrent.UseEngineEyeHeight)}m " +
                                $"回读Δ={(readBack < 0f ? "ERR" : readBack.ToString("F4"))} " +
                                $"yaw={_followCurrent.ArmYaw:F1} pitch={_followCurrent.ArmPitch:F1} " +
                                $"臂长={_followCurrent.ArmLength:F2} fov={_followCurrent.Fov:F0} " +
                                $"entity={DescribeEntity(cam)}");
            }
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
                _followHandT = Math.Min(1f, _followHandT + dt / _followHandBlendSeconds);
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

            // 🔴 方向归还（**独立于臂长归还，可以更早起跑** —— 钩索从拉拽一开始就调 BeginLookReturn）：
            //    chase 状态逐帧累积，算完再写进本帧参数。**不能直接改 `_followCurrent`** ——
            //    它每帧都被上面的 Lerp 重算回起点，写进去的增量下一帧就被抹掉（2026-10-04 实测俯仰纹丝不动）。
            if (_followLookReturning)
            {
                ChaseEngineLook(ref _followHandLookYaw, ref _followHandLookPitch, dt);
                _followCurrent.ArmYaw = _followHandLookYaw;
                _followCurrent.ArmPitch = _followHandLookPitch;
            }

            // 外部朝向驱动（瞄准相机）：**逐帧覆盖本帧方向**，优先级最高（在渐变与归还 chase 之后）。
            // 🔴 必须写在这里（和 chase 同位置）——写 `_followCurrent` 会被上面的 Lerp 每帧重算回起点，
            //    只有这个位置写进去的才是"本帧真正渲染的方向"（2026-10-04 拉拽 chase 同一个坑）。
            if (_followExternalLook)
            {
                _followCurrent.ArmYaw = _followExternalLookYaw;
                _followCurrent.ArmPitch = _followExternalLookPitch;
            }

            // 臂长归还走完 ⇒ 看方向是否已和引擎一致：一致才撒手（撒手那一帧两边必须严丝合缝）。
            if (_followHandingBack && _followHandT >= 1f)
            {
                if (EngineLookConverged(_followCurrent))
                {
                    StopFollowCamera();
                    return;
                }
                // 还没跟完 ⇒ 最多再等 HandBackLookHoldMaxSeconds 把它平滑跟完
                _followHandHoldT += dt;
                if (_followHandHoldT >= HandBackLookHoldMaxSeconds)
                {
                    DebugLogger.Log($"[FollowCam] 方向没在等待上限内跟完（还差 {EngineLookDeltaDeg(_followCurrent):F1}°）—— 强制撒手");
                    StopFollowCamera();
                    return;
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
