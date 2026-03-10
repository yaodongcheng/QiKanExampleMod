using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Library;

namespace ExampleMod
{
    public class CameraDebuggerVM: TaleWorlds.Library.ViewModel
    {
        private float _camOffsetX;
        private float _camOffsetY;
        private float _camOffsetZ;
        private float _camRotYaw;
        private float _camRotPitch;
        private float _camRotRoll;
        private readonly Action _onClose;
        private float _camFov = 65.0f;
        // --- 构造函数 ---
        public CameraDebuggerVM(Action onClose)
        {
            _onClose = onClose;
            // 初始化一些默认值，避免相机一开始在地下

            CameraParm currentCameraParam = CameraDebuggerView.GetCameraParm();
            CamOffsetX = currentCameraParam.x;
            CamOffsetY = currentCameraParam.y;
            CamOffsetZ = currentCameraParam.z;

            CamFov = currentCameraParam.fov;
            CamRotPitch = currentCameraParam.pitch;
            CamRotRoll = currentCameraParam.roll;
            CamRotYaw = currentCameraParam.yaw;

        }
        // --- 关闭命令 ---
        public void ExecuteClose()
        {
            _onClose?.Invoke();
        }

        [DataSourceProperty]
        public float CamOffsetX
        {
            get => _camOffsetX;
            set
            {
                if (value != _camOffsetX)
                {
                    _camOffsetX = value;
                    OnPropertyChangedWithValue(value, nameof(CamOffsetX));
                    OnPropertyChanged(nameof(CamOffsetXStr));
                }
            }
        }
        // 【新增这个属性】: 专门给 XML 里的 TextWidget 用
        [DataSourceProperty]
        public string CamOffsetXStr
        {
            // 获取时：把 float 转成 string (保留2位小数)
            get => _camOffsetX.ToString("F2");
            // 设置时：把 string 尝试转回 float (处理用户输入)
            set
            {
                if (float.TryParse(value, out float result))
                {
                    CamOffsetX = result;
                }
            }
        }

        [DataSourceProperty]
        public float CamOffsetY
        {
            get => _camOffsetY;
            set
            {
                if (value != _camOffsetY)
                {
                    _camOffsetY = value;
                    OnPropertyChangedWithValue(value, nameof(CamOffsetY));
                    OnPropertyChanged(nameof(CamOffsetYStr));
                }
            }
        }
        // 【新增这个属性】: 专门给 XML 里的 TextWidget 用
        [DataSourceProperty]
        public string CamOffsetYStr
        {
            // 获取时：把 float 转成 string (保留2位小数)
            get => _camOffsetY.ToString("F2");
            // 设置时：把 string 尝试转回 float (处理用户输入)
            set
            {
                if (float.TryParse(value, out float result))
                {
                    CamOffsetY = result;
                }
            }
        }

        [DataSourceProperty]
        public float CamOffsetZ
        {
            get => _camOffsetZ;
            set
            {
                if (value != _camOffsetZ)
                {
                    _camOffsetZ = value;
                    OnPropertyChangedWithValue(value, nameof(CamOffsetZ));
                    OnPropertyChanged(nameof(CamOffsetZStr));
                }
            }
        }
        public string CamOffsetZStr
        {
            // 获取时：把 float 转成 string (保留2位小数)
            get => _camOffsetZ.ToString("F2");
            // 设置时：把 string 尝试转回 float (处理用户输入)
            set
            {
                if (float.TryParse(value, out float result))
                {
                    CamOffsetZ = result;
                }
            }
        }

        [DataSourceProperty]
        public float CamRotYaw
        {
            get => _camRotYaw;
            set
            {
                if (value != _camRotYaw)
                {
                    _camRotYaw = value;
                    OnPropertyChangedWithValue(value, nameof(CamRotYaw));
                    OnPropertyChanged(nameof(CamRotYawStr));
                }
            }
        }
        public string CamRotYawStr
        {
            // 获取时：把 float 转成 string (保留2位小数)
            get => _camRotYaw.ToString("F2");
            // 设置时：把 string 尝试转回 float (处理用户输入)
            set
            {
                if (float.TryParse(value, out float result))
                {
                    CamRotYaw = result;
                }
            }
        }

        [DataSourceProperty]
        public float CamRotPitch
        {
            get => _camRotPitch;
            set
            {
                if (value != _camRotPitch)
                {
                    _camRotPitch = value;
                    OnPropertyChangedWithValue(value, nameof(CamRotPitch));
                    OnPropertyChanged(nameof(CamRotPitchStr));
                }
            }
        }
        public string CamRotPitchStr
        {
            // 获取时：把 float 转成 string (保留2位小数)
            get => _camRotPitch.ToString("F2");
            // 设置时：把 string 尝试转回 float (处理用户输入)
            set
            {
                if (float.TryParse(value, out float result))
                {
                    CamRotPitch = result;
                }
            }
        }

        [DataSourceProperty]
        public float CamRotRoll
        {
            get => _camRotRoll;
            set
            {
                if (value != _camRotRoll)
                {
                    _camRotRoll = value;
                    OnPropertyChangedWithValue(value, nameof(CamRotRoll)); 
                    OnPropertyChanged(nameof(CamRotRollStr));
                }
            }
        }
        public string CamRotRollStr
        {
            // 获取时：把 float 转成 string (保留2位小数)
            get => _camRotRoll.ToString("F2");
            // 设置时：把 string 尝试转回 float (处理用户输入)
            set
            {
                if (float.TryParse(value, out float result))
                {
                    CamRotRoll = result;
                }
            }
        }

        [DataSourceProperty]
        public float CamFov
        {
            get => _camFov;
            set
            {
                if (value != _camFov)
                {
                    _camFov = value;
                    OnPropertyChangedWithValue(value, nameof(CamFov));
                    OnPropertyChanged(nameof(CamFovStr)); 
                }
            }
        }

        [DataSourceProperty]
        public string CamFovStr
        {
            get => _camFov.ToString("F1"); // 保留1位小数
            set
            {
                if (float.TryParse(value, out float result))
                {
                    CamFov = result;
                }
            }
        }

    }
}
