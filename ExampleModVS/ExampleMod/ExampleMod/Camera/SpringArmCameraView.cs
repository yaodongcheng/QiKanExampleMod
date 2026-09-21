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
            if (_isActive) CloseUI();
            _customCamera = null;
            base.OnMissionScreenFinalize();
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            long t0 = PerfProfiler.Now();          // perf: CAM_SpringArm
            if (_isActive && Mission.Current.MainAgent != null)
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
            DataTable cameraTbl = GameDatabase.Camera;
            DynamicRecord cameraTemplate = cameraTbl.GetByID(templateName);
            if (cameraTemplate == null)
            {
                Instance.CloseUI();
                return "Error: Camera template not found.";
            }
            SpringArmCameraParam springArmCameraParam = new SpringArmCameraParam
            {
                PivotX = cameraTemplate.GetFloat("PivotX"),
                PivotY = cameraTemplate.GetFloat("PivotY"),
                PivotZ = cameraTemplate.GetFloat("PivotZ"),
                ArmLength = cameraTemplate.GetFloat("ArmLength"),
                ArmYaw = cameraTemplate.GetFloat("ArmYaw"),
                ArmPitch = cameraTemplate.GetFloat("ArmPitch"),
                SocketX = cameraTemplate.GetFloat("SocketX"),
                SocketY = cameraTemplate.GetFloat("SocketY"),
                SocketZ = cameraTemplate.GetFloat("SocketZ"),
                SelfYaw = cameraTemplate.GetFloat("SelfYaw"),
                SelfPitch = cameraTemplate.GetFloat("SelfPitch"),
                SelfRoll = cameraTemplate.GetFloat("SelfRoll"),

                Fov = cameraTemplate.GetFloat("Fov"),
                IsAnchorWorld = false,
            };
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

                DataTable cameraTbl = GameDatabase.Camera;
            DynamicRecord cameraTemplate = cameraTbl.GetByID(templateName);
            if(cameraTemplate == null)
            {
                Instance.CloseUI();
                return "Error: Camera template not found.";
            }
            SpringArmCameraParam springArmCameraParam = new SpringArmCameraParam { 
                PivotX = cameraTemplate.GetFloat("PivotX"),
                PivotY = cameraTemplate.GetFloat("PivotY"),
                PivotZ = cameraTemplate.GetFloat("PivotZ"),
                ArmLength = cameraTemplate.GetFloat("ArmLength"),
                ArmYaw = cameraTemplate.GetFloat("ArmYaw"),
                ArmPitch = cameraTemplate.GetFloat("ArmPitch"),
                SocketX = cameraTemplate.GetFloat("SocketX"),
                SocketY = cameraTemplate.GetFloat("SocketY"),
                SocketZ = cameraTemplate.GetFloat("SocketZ"),
                SelfYaw = cameraTemplate.GetFloat("SelfYaw"),
                SelfPitch = cameraTemplate.GetFloat("SelfPitch"),
                SelfRoll = cameraTemplate.GetFloat("SelfRoll"),

                Fov = cameraTemplate.GetFloat("Fov"),
                IsAnchorWorld = false,            
            };

            Instance.ApplySpringArmCamera(springArmCameraParam);
            
            return "Spring Arm Camera Applied.";

          

        }
    }
}
