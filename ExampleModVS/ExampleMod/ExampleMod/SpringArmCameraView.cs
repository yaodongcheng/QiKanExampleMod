using ExampleMod.StoryEngineBag;
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

namespace ExampleMod
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
        private IGauntletMovie _movie;
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

            if (_isActive && Mission.Current.MainAgent != null)
            {
                ApplyCameraOverrideForUI();
            }
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



            float standEyeHeight = 1.4626f;
            float sitEyeHeight = 1.024f;

            //使用DebugLogger打印本次的anchorFrame，采用$"{} desc等"模式
            //DebugLogger.Log($"ApplySpringArmCamera targetAgentName {targetAgent.Name} anchorFrame: {anchorFrame}");

            float eyeHeightOffset = standEyeHeight;
            if(VariableManager.GetV(targetAgent.Character.StringId,"IsSit") == "True")
            {
               // DebugLogger.Log($"因为{targetAgent.Name}坐下了，使用坐姿高度");
                eyeHeightOffset = sitEyeHeight;
            }

            Vec3 anchorFootPos = anchorFrame.origin;
            Vec3 anchorEyePos = anchorFootPos;
            anchorEyePos.z += eyeHeightOffset;



           // DebugLogger.Log($"ApplySpringArmCamera targetAgentName {targetAgent.Name} anchorLoc: {anchorFrame.origin} eyePos {targetAgent.GetEyeGlobalPosition()} eyeheight {targetAgent.GetEyeGlobalHeight()}");

            Vec3 PivotOffset =  (anchorFrame.rotation.s * (p.PivotX))   + (anchorFrame.rotation.f * (p.PivotY))   + (anchorFrame.rotation.u * (p.PivotZ));


            Vec3 pivotPos = anchorEyePos + PivotOffset;               


            float degToRad = MathF.PI / 180.0f;
            Mat3 armRotMat = Mat3.Identity;
            p.IsAnchorWorld = false;
            if (p.IsAnchorWorld)
            {
                // 世界模式：完全由 Slider 控制，不跟随玩家转向
                // 此时基准是世界坐标系 (Identity)
                armRotMat.RotateAboutUp(p.ArmYaw * degToRad); // 世界 Yaw
                armRotMat.RotateAboutSide(p.ArmPitch * degToRad); // 世界 Pitch
            }
            else
            {
                
                armRotMat = targetAgent.LookFrame.rotation;               
                armRotMat.RotateAboutUp(-p.ArmYaw * degToRad); // 注意左右手定则，M&B中左转通常为正或负需测试
                armRotMat.RotateAboutSide(p.ArmPitch * degToRad);
            }

            float armLenMeters = p.ArmLength ;           

            Vec3 socketOffset = (armRotMat.s * (p.SocketX ))
                              + (armRotMat.f * (p.SocketY ))
                              + (armRotMat.u * (p.SocketZ ));

            Vec3 cameraPos = pivotPos - (armRotMat.f * armLenMeters) +  socketOffset;

            Mat3 finalCamRot = armRotMat;

            // 叠加自旋 (Yaw/Pitch/Roll)
            // 注意：通常相机看向 Arm 的前方。
            if (Math.Abs(p.SelfYaw) > 0.001f) finalCamRot.RotateAboutUp(-p.SelfYaw * degToRad);
            if (Math.Abs(p.SelfPitch) > 0.001f) finalCamRot.RotateAboutSide(p.SelfPitch * degToRad);
            if (Math.Abs(p.SelfRoll) > 0.001f) finalCamRot.RotateAboutForward(p.SelfRoll * degToRad);
            finalCamRot.RotateAboutSide(MathF.PI / 2); // 相机朝天修正


            // 确保坐标系正交
            finalCamRot.Orthonormalize();


            // 7. 应用到 Engine Camera
            MatrixFrame finalFrame = MatrixFrame.Identity;
            finalFrame.origin = cameraPos;



            finalFrame.rotation = finalCamRot;

            //测试，直接把相机放在角色的正后方
            //finalFrame.origin = pivotPos - armRotMat.f * armLenMeters;



            _customCamera.Frame = finalFrame;
            _customCamera.SetFovVertical(p.Fov * degToRad, Screen.AspectRatio, 0.1f, 1000f);
           // DebugLogger.Log($"ApplySpringArmCamera anchorFootPos{anchorFootPos} anchorEyePos {anchorEyePos} PivotOffset {PivotOffset} pivotPos {pivotPos} ArmPos {armRotMat.f * armLenMeters} SocketOffset {socketOffset} finalOrigin: {finalFrame.origin}");

            
        


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
            _gauntletLayer = new GauntletLayer(100);
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
