using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.MountAndBlade.ViewModelCollection;
using TaleWorlds.ScreenSystem;

namespace ExampleMod
{
    public struct CameraParm
    {
        public float x;
        public float y;
        public float z;
        public float yaw;
        public float pitch;
        public float roll;
        public float fov;
    }
    public class CameraDebuggerView : MissionView
    {
        private GauntletLayer _gauntletLayer;
        private CameraDebuggerVM _dataSource;
        private IGauntletMovie _movie;
        private bool _isActive;
        // 用于覆盖相机的临时对象
        private Camera _customCamera;
        //相机看向的目标.可能是玩家也可能是其他Npc
        public static Agent targetAgent = null;

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            // 初始化自定义相机对象
            _customCamera = Camera.CreateCamera();

            InformationManager.DisplayMessage(new InformationMessage("Custom Camera Load Success!"));
        }

        public override void OnMissionScreenFinalize()
        {
            // 清理 UI
            if (_isActive)
                CloseUI();

            _customCamera = null;
            base.OnMissionScreenFinalize();
        }

        // --- 每一帧的逻辑 ---
        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);           

            // 如果菜单打开了，我们就执行相机覆盖逻辑
            if (_isActive && Mission.Current.MainAgent != null)
            {
                ApplyCameraOverrideForUI();
            }
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("openCamDebugger", "custom")]
        public static string ExecuteOpenCamDebugger(List<string> args)
        {
            // 1. 确保当前有 Mission 在运行
            if (Mission.Current == null)
            {
                return "Error: No active mission found.";
            }
            //基于Id来找要看向的Npc


            // 2. 从当前 Mission 中查找我们注册的 Behavior (CameraDebuggerView)
            var debuggerView = Mission.Current.GetMissionBehavior<CameraDebuggerView>();

            if (debuggerView != null)
            {
                // 3. 找到实例后，调用实例方法打开/切换 UI
                debuggerView.ToggleUI();
                return "Camera Debugger UI Toggled Success.";
            }

            return "Error: CameraDebuggerView is not registered in the current mission.";
        }

        [CommandLineFunctionality.CommandLineArgumentFunction("useCameraTemplate", "custom")]
        public static string ExecuteUseCameraTemplate(List<string> args)
        {
            if (Mission.Current == null)
            {
                return "Error: No active mission found.";
            }
            MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
            if (args.Count < 1 )
            {
                CameraDebuggerView.targetAgent = Mission.Current.MainAgent;

            }
            else
            {
                    
                    string targetHeroId = args[0];                            
                    if(targetHeroId.ToLower() == "reset")
                    {
                        missionScreen.CustomCamera = null;
                        CameraDebuggerView.targetAgent = Mission.Current.MainAgent;
                    return "Camera reset to default.";
                    }


                    var targetAgent = Mission.Current.Agents.FirstOrDefault(a => a.IsHuman && a.Character?.StringId == targetHeroId);
                    if (targetAgent != null)
                    {
                        CameraDebuggerView.targetAgent = targetAgent;
                    }
                    else
                    {
                        CameraDebuggerView.targetAgent = Mission.Current.MainAgent;                    
                    }               
            }
            string templateName = "";
            if (args.Count == 2)
            {
                templateName = args[1];
            }
            return UseCameraTemlate(CameraDebuggerView.targetAgent, templateName);

            
        }
        public static string UseCameraTemlate(Agent targetAgent,string templateName)
        {
            if (targetAgent != null)
            {
                CameraDebuggerView.targetAgent = targetAgent;
            }
            else
            {
                CameraDebuggerView.targetAgent = Mission.Current.MainAgent;
            }

            var debuggerView = Mission.Current.GetMissionBehavior<CameraDebuggerView>();
            if (debuggerView == null)
                return "debuggerView null";
            CameraParm templateCameraParm = new CameraParm { x = 0.0f, y = -1.75f, z = 1.79f, yaw = 0.0f, pitch = -5.73f, roll = 0.0f, fov = 65.0f };
            switch (templateName)
            {
                case "self_choice":
                    templateCameraParm = new CameraParm { x = 0.10f, y = 1.75f, z = 1.79f, yaw = -160.74f, pitch = -5.73f, roll = 0.0f, fov = 37.0f };
                    break;
                case "npc_say":
                    templateCameraParm = new CameraParm { x = 1.68f, y = -2.96f, z = 1.6f, yaw = -17.41f, pitch = 2.13f, roll = 0.0f, fov = 26.4f };
                    break;
                case "self_say":
                    templateCameraParm = new CameraParm { x = 1.68f, y = -2.96f, z = 1.6f, yaw = -17.41f, pitch = 2.13f, roll = 0.0f, fov = 26.4f };
                    break;
                case "2m_OverShoulder_Opposite_R":
                    templateCameraParm = new CameraParm { x = 1.93f, y = -1.50f, z = 1.99f, yaw = -41.61f, pitch = -5.73f, roll = 0.0f, fov = 20.6f };
                    break;
                case "2m_OverShoulder_Self_R":
                    templateCameraParm = new CameraParm { x = -2.10f, y = -2.06f, z = 1.76f, yaw = 36.98f, pitch = -1.11f, roll = 0.0f, fov = 20.7f };
                    break;
                case "2m_CloseFace_Self_R":
                    templateCameraParm = new CameraParm { x = 0.21f, y = 1.07f, z = 1.91f, yaw = -162.89f, pitch = -5.73f, roll = 0.0f, fov = 22.4f };
                    break;
                case "2m_CloseFace_Opposite_R":
                    templateCameraParm = new CameraParm { x = -0.43f, y = 1.03f, z = 1.91f, yaw = 150.26f, pitch = -5.73f, roll = 0.0f, fov = 22.4f };
                    break;
                case "2m_Full_Self_R":
                    templateCameraParm = new CameraParm { x = -0.21f, y = 1.39f, z = 1.94f, yaw = -144.86f, pitch = -13.82f, roll = 0.0f, fov = 93.0f };
                    break;
                case "2m_Full_Opposite_R":
                    templateCameraParm = new CameraParm { x = -0.21f, y = 1.39f, z = 1.91f, yaw = 143.32f, pitch = -14.98f, roll = 0.0f, fov = 93.0f };
                    break;
                case "2m_Middle_Self_R":
                    templateCameraParm = new CameraParm { x = -0.24f, y = -0.12f, z = 1.72f, yaw = -173.84f, pitch = -29.28f, roll = 0.0f, fov = 47.0f };
                    break;
                case "2m_Middle_Opposite_R":
                    templateCameraParm = new CameraParm { x = -0.29f, y = -0.14f, z = 1.92f, yaw = 142.09f, pitch = -29.28f, roll = 0.0f, fov = 47.0f };
                    break;
                case "2m_Middle_Third":
                    templateCameraParm = new CameraParm { x = -0.0f, y = -0.0f, z = 1.96f, yaw = 180.0f, pitch = -29.28f, roll = 0.0f, fov = 47.0f };
                    break;
                case "2m_Far_Self_R":
                    templateCameraParm = new CameraParm { x = -3.73f, y = -3.97f, z = 5.0f, yaw =52.4f, pitch = -32.31f, roll = 0.0f, fov = 71.8f };
                    break;
                case "2m_Far_Opposite_R":
                    templateCameraParm = new CameraParm { x = 3.73f, y = 3.97f, z = 5.0f, yaw = -131.76f, pitch = -32.31f, roll = 0.0f, fov = 71.8f };
                    break;
                case "2m_Far_Side_R":
                    templateCameraParm = new CameraParm { x = 10.0f, y = 1.40f, z = 3.30f, yaw = -90.92f, pitch = -4.57f, roll = 0.0f, fov = 65.2f };
                    break;
                case "2m_Middle_Side_R":
                    templateCameraParm = new CameraParm { x = 3.42f, y = 1.22f, z = 2.94f, yaw = -91.70f, pitch = -16.13f, roll = 0.0f, fov = 65.5f };
                    break;
                case "4m_Middle_SideSit_R":
                    templateCameraParm = new CameraParm { x = 4.55f, y = 1.46f, z = 2.69f, yaw = -86.92f, pitch = -16.21f, roll = -11.71f, fov = 42.2f };
                    break;
                case "4m_OverShoulder_OppositeSit_R":
                    templateCameraParm = new CameraParm { x = 0.41f, y = -0.55f, z = 0.80f, yaw = -34.52f, pitch = -18.18f, roll = -4.32f, fov = 67.4f };
                    break;
                case "4m_OverShoulder_SelfSit_R":
                    templateCameraParm = new CameraParm { x = -1.02f, y = -0.62f, z = 0.64f, yaw = 48.08f, pitch = 12.95f, roll = 25.27f, fov = 65.0f };
                    break;
                case "Far_OverLook":
                    templateCameraParm = new CameraParm { x = 0.0f, y = -5.0f, z = 1.77f, yaw = 0.0f, pitch = -4.93f, roll = 0.0f, fov = 76.1f };
                    break;
                default:
                    break;
            }


            CameraParm finalCameraParam = templateCameraParm;
            debuggerView.ApplyCamera(finalCameraParam);

            return "success";
        }

        public static CameraParm GetCameraParm()
        {
            CameraParm currentCameraParm = new CameraParm();
            if (Mission.Current == null)
                return currentCameraParm;
           
            MissionScreen currentMissionScreen = ScreenManager.TopScreen as MissionScreen;
            Agent targetAgent = Mission.Current.MainAgent;
            if (currentMissionScreen != null)
            {
                Camera mainCamera = currentMissionScreen.CombatCamera;
                // MatrixFrame camFrame = mainCamera.Frame; //获取当前相机的矩阵
                MatrixFrame camFrame = Mission.Current.GetCameraFrame();


                //有时候一些游戏里，相机的Forward并不是向前，而是向上，要看看是否进行一层转化
                Vec3 camUp = camFrame.rotation.u;
                if (camUp.y < -0.5f)
                    camFrame.rotation.RotateAboutSide(-MathF.PI / 2);


                MatrixFrame agentFrame = targetAgent.LookFrame; //获取玩家的观察矩阵
                //计算世界坐标系相机相对玩家的坐标差
                Vec3 worldOffset = camFrame.origin - agentFrame.origin;
                //计算局部坐标系的相机相对玩家的坐标差
                currentCameraParm.x = Vec3.DotProduct(worldOffset,agentFrame.rotation.s); //计算投影，把世界x轴offset投影到玩家的右侧向量上
                currentCameraParm.y = Vec3.DotProduct(worldOffset,agentFrame.rotation.f); //计算投影，把世界y轴offset投影到玩家的前向量上
                currentCameraParm.z = Vec3.DotProduct(worldOffset,agentFrame.rotation.u); //计算投影，把世界z轴offset投影到玩家的上向量上

                float toDeg = 180.0f / MathF.PI;  //弧度转角度的转换系数
                Vec3 camForwardLocal = agentFrame.rotation.TransformToLocal(camFrame.rotation.f); //把相机的前向量转换到玩家的局部坐标系下

                //错误方式：这种方式下，相机会认为角色的右方向是0度，前方向是90度，导致旋转角度不符合直觉    Atan2 的几何含义：  atan2(y, x) = 从 + X轴 到点(x, y) 的夹角 那么第二个参数应该填Y
               //  currentCameraParm.yaw = MathF.Atan2(camForwardLocal.y, camForwardLocal.x) * toDeg;// 长边为Y轴，短边为X轴，计算出相机在水平面XY的弧度值并转化为角度
                //正确方式：这种方式下，相机会认为角色的前方向是0度，右方向是90度，更符合直觉
                currentCameraParm.yaw = MathF.Atan2(camForwardLocal.x, camForwardLocal.y) * toDeg;// 长边为X轴，短边为Y轴，计算出相机在水平面XY的弧度值并转化为角度

                InformationManager.DisplayMessage(new InformationMessage($"camFrame.rotation.u {camFrame.rotation.u} camForwardLocal.z:{camForwardLocal.z}"));

                currentCameraParm.pitch = MathF.Asin(MathF.Clamp(camForwardLocal.z, -1f, 1f))* toDeg; // 计算出相机在垂直方向Z的弧度值并转化为角度

                currentCameraParm.roll = 0* toDeg; // Roll 通常不需要
                currentCameraParm.fov = mainCamera.GetFovVertical() * toDeg; // 获取垂直方向的 FOV 并转化为角度
            }
            return currentCameraParm;
        }


        [CommandLineFunctionality.CommandLineArgumentFunction("printCameraParam", "custom")]
        public static string GetCameraInfo(List<string> args)
        {
            if (Mission.Current == null)
                return "(Mission is null).";
            CameraParm currentCameraParm = GetCameraParm();
            // 5. 格式化输出
            string output = $"\n=== Current Camera Params ===\n" +
                            $"Position (XYZ): {currentCameraParm.x:F3}, {currentCameraParm.y:F3}, {currentCameraParm.z:F3}\n" +
                            $"Rotation (Deg): Yaw={currentCameraParm.yaw:F1}, Pitch={currentCameraParm.pitch:F1}, Roll={currentCameraParm.roll:F1}\n" +
                            $"FOV (Deg): {currentCameraParm.fov:F1}\n" +
                            $"------------------";

            return output;
        }

        private void ApplyCameraOverrideForUI()
        {
            ApplyCamera(new CameraParm { x = _dataSource.CamOffsetX, y = _dataSource.CamOffsetY, z = _dataSource.CamOffsetZ, pitch = _dataSource.CamRotPitch, roll = _dataSource.CamRotRoll, yaw = _dataSource.CamRotYaw, fov = _dataSource.CamFov });
        }

        private void ApplyCamera(CameraParm cameraParam)
        {
            MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
            if (targetAgent == null)
                targetAgent = Mission.Current.MainAgent;
            MatrixFrame agentFrame = targetAgent.LookFrame;
            float DegToRad = Math.Abs(MathF.PI / 180.0f); // 角度转弧度的转换系数
            float radYaw = cameraParam.yaw * DegToRad;
            float radPitch = cameraParam.pitch * DegToRad;
            float radRoll = cameraParam.roll * DegToRad;
            float radFov = cameraParam.fov * DegToRad;
            float offsetX = cameraParam.x;
            float offsetY = cameraParam.y;
            float offsetZ = cameraParam.z;

            Mat3 rotationOffset = Mat3.Identity;
            rotationOffset.RotateAboutUp(-radYaw); // 水平面的夹角，就是绕 Up 轴旋转
            rotationOffset.RotateAboutSide(radPitch); // 俯仰角，就是绕 Side 轴旋转
            rotationOffset.RotateAboutForward(radRoll); // 横滚角，就是绕 Forward 轴旋转
            rotationOffset.RotateAboutSide(MathF.PI / 2); // 俯仰角，修正90

            Vec3 targetPosition = agentFrame.origin
                      + (agentFrame.rotation.s * offsetX) // 沿 Agent 右轴移动
                      + (agentFrame.rotation.f * offsetY) // 沿 Agent 前轴移动
                      + (agentFrame.rotation.u * offsetZ); // 沿 Agent 上轴移动

            MatrixFrame finalCamFrame = MatrixFrame.Identity;
            finalCamFrame.origin = targetPosition;
            // 将计算好的局部旋转应用到 Agent 的朝向上
            finalCamFrame.rotation = agentFrame.rotation.TransformToParent(rotationOffset);


            _customCamera.SetFovVertical(radFov, Screen.AspectRatio, 0.1f, 10000.0f);
            _customCamera.Frame = finalCamFrame;

            missionScreen.CustomCamera = _customCamera;
            
        }

        private void ApplyCameraOverride()
        {
            ApplyCamera(new CameraParm {x = _dataSource.CamOffsetX,y = _dataSource.CamOffsetY,z = _dataSource.CamOffsetZ, pitch = _dataSource.CamRotPitch, roll = _dataSource.CamRotRoll, yaw = _dataSource.CamRotYaw,fov = _dataSource.CamFov });
                       
        }

        // --- UI 开关逻辑 ---
        private void ToggleUI()
        {
            if (_isActive)
                CloseUI();
            else
                OpenUI();
        }

       
        private void OpenUI()
        {
            InformationManager.DisplayMessage(new InformationMessage("Prepare Open UI!"));

            if (_isActive) return;

            // 创建 VM
            _dataSource = new CameraDebuggerVM(CloseUI);

            // 创建 Layer (设置优先级，保证显示在最上层)
            _gauntletLayer = new GauntletLayer(100);

            // 加载 XML (确保你的 XML 文件名为 "CameraDebugger.xml")
            _movie = _gauntletLayer.LoadMovie("CameraDebugger", _dataSource);

            // 设置输入限制：UI 打开时，鼠标可见，游戏操作被拦截
            _gauntletLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);


            MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
            if (missionScreen != null)
            {
                missionScreen.AddLayer(_gauntletLayer);
                // 添加 Layer 到屏幕
                // MissionScreen.AddLayer(_gauntletLayer);
                _isActive = true;
                InformationManager.DisplayMessage(new InformationMessage("Open Camera UI Success!"));
            }
            else
            {
                InformationManager.DisplayMessage(new InformationMessage("Mission Screen Failed!"));
            }

        }


        private void CloseUI()
        {
            MissionScreen missionScreen = ScreenManager.TopScreen as MissionScreen;
            if (!_isActive) return;

            // 移除 Layer
            missionScreen.RemoveLayer(_gauntletLayer);

            // 释放资源
            _gauntletLayer = null;
            _dataSource = null;
            _movie = null;
            _isActive = false;

            // *** 关键 ***：恢复游戏默认相机
            missionScreen.CustomCamera = null;
        }

    }
}
