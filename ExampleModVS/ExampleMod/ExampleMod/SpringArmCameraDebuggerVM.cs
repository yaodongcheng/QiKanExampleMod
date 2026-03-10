using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Library;

namespace ExampleMod
{
    public class SpringArmCameraDebuggerVM : TaleWorlds.Library.ViewModel
    {
        private readonly Action _onClose;

        // --- 1. Anchor Mode ---
        private bool _isAnchorWorld = false; // 默认为 Agent 模式
        private string _currentAnchorModeStr;

        // --- 2. Pivot Offset (摇臂根部偏移) ---
        private float _targetOffsetX;
        private float _targetOffsetY;
        private float _targetOffsetZ;

        // --- 3. Spring Arm (摇臂参数) ---
        private float _armLength;
        private float _armYaw;
        private float _armPitch;

        // --- 4. Socket Offset (相机插槽偏移) ---
        private float _socketOffsetX;
        private float _socketOffsetY;
        private float _socketOffsetZ;

        // --- 5. Camera Self Rotation (相机自旋) ---
        private float _camSelfYaw;
        private float _camSelfPitch;
        private float _camSelfRoll;

        // --- Common ---
        private float _camFov;

        public SpringArmCameraDebuggerVM(Action onClose)
        {
            _onClose = onClose;

            // --- 初始化默认值 (一个标准的过肩视角) ---
            UpdateAnchorModeText();

            TargetOffsetX = 0f;
            TargetOffsetY = 0f;
            TargetOffsetZ = 0.0f;

            // 摇臂设置
            ArmLength = 2.5f; // 2.5米 
            ArmYaw = 0.0f;
            ArmPitch = -15.0f; // 稍微俯视

            // 插槽偏移 (可以用来做过肩偏移)
            SocketOffsetX = 0.0f; 
            SocketOffsetY = 0f;
            SocketOffsetZ = 0f;

            // 相机自旋 (通常为0，除非想做特殊的倾斜)
            CamSelfYaw = 0f;
            CamSelfPitch = 0f;
            CamSelfRoll = 0f;

            CamFov = 65.0f;
        }

        public void ExecuteClose()
        {
            _onClose?.Invoke();
        }

        // ==========================================
        // 1. ANCHOR MODE COMMANDS
        // ==========================================
        public void ExecuteSetAnchorAgent()
        {
            _isAnchorWorld = false;
            UpdateAnchorModeText();
        }

        public void ExecuteSetAnchorWorld()
        {
            _isAnchorWorld = true;
            UpdateAnchorModeText();
        }

        private void UpdateAnchorModeText()
        {
            CurrentAnchorModeStr = _isAnchorWorld ? "Current: WORLD (Fixed Pos)" : "Current: AGENT (Follows Player)";
        }

        [DataSourceProperty]
        public string CurrentAnchorModeStr
        {
            get => _currentAnchorModeStr;
            set
            {
                if (value != _currentAnchorModeStr)
                {
                    _currentAnchorModeStr = value;
                    OnPropertyChanged(nameof(CurrentAnchorModeStr));
                }
            }
        }

        public bool IsAnchorWorld => _isAnchorWorld;

        // ==========================================
        // 2. PIVOT OFFSET PROPERTIES
        // ==========================================
        [DataSourceProperty]
        public float TargetOffsetX
        {
            get => _targetOffsetX;
            set { if (value != _targetOffsetX) { _targetOffsetX = value; OnPropertyChangedWithValue(value, nameof(TargetOffsetX)); OnPropertyChanged(nameof(TargetOffsetXStr)); } }
        }
        [DataSourceProperty]
        public string TargetOffsetXStr
        {
            get => _targetOffsetX.ToString("F2");
            set { if (float.TryParse(value, out float res)) TargetOffsetX = res; }
        }

        [DataSourceProperty]
        public float TargetOffsetY
        {
            get => _targetOffsetY;
            set { if (value != _targetOffsetY) { _targetOffsetY = value; OnPropertyChangedWithValue(value, nameof(TargetOffsetY)); OnPropertyChanged(nameof(TargetOffsetYStr)); } }
        }
        [DataSourceProperty]
        public string TargetOffsetYStr
        {
            get => _targetOffsetY.ToString("F2");
            set { if (float.TryParse(value, out float res)) TargetOffsetY = res; }
        }

        [DataSourceProperty]
        public float TargetOffsetZ
        {
            get => _targetOffsetZ;
            set { if (value != _targetOffsetZ) { _targetOffsetZ = value; OnPropertyChangedWithValue(value, nameof(TargetOffsetZ)); OnPropertyChanged(nameof(TargetOffsetZStr)); } }
        }
        [DataSourceProperty]
        public string TargetOffsetZStr
        {
            get => _targetOffsetZ.ToString("F2");
            set { if (float.TryParse(value, out float res)) TargetOffsetZ = res; }
        }

        // ==========================================
        // 3. SPRING ARM PROPERTIES
        // ==========================================
        [DataSourceProperty]
        public float ArmLength
        {
            get => _armLength;
            set { if (value != _armLength) { _armLength = value; OnPropertyChangedWithValue(value, nameof(ArmLength)); OnPropertyChanged(nameof(ArmLengthStr)); } }
        }
        [DataSourceProperty]
        public string ArmLengthStr
        {
            get => _armLength.ToString("F1");
            set { if (float.TryParse(value, out float res)) ArmLength = res; }
        }

        [DataSourceProperty]
        public float ArmYaw
        {
            get => _armYaw;
            set { if (value != _armYaw) { _armYaw = value; OnPropertyChangedWithValue(value, nameof(ArmYaw)); OnPropertyChanged(nameof(ArmYawStr)); } }
        }
        [DataSourceProperty]
        public string ArmYawStr
        {
            get => _armYaw.ToString("F1");
            set { if (float.TryParse(value, out float res)) ArmYaw = res; }
        }

        [DataSourceProperty]
        public float ArmPitch
        {
            get => _armPitch;
            set { if (value != _armPitch) { _armPitch = value; OnPropertyChangedWithValue(value, nameof(ArmPitch)); OnPropertyChanged(nameof(ArmPitchStr)); } }
        }
        [DataSourceProperty]
        public string ArmPitchStr
        {
            get => _armPitch.ToString("F1");
            set { if (float.TryParse(value, out float res)) ArmPitch = res; }
        }

        // ==========================================
        // 4. SOCKET OFFSET PROPERTIES
        // ==========================================
        [DataSourceProperty]
        public float SocketOffsetX
        {
            get => _socketOffsetX;
            set { if (value != _socketOffsetX) { _socketOffsetX = value; OnPropertyChangedWithValue(value, nameof(SocketOffsetX)); OnPropertyChanged(nameof(SocketOffsetXStr)); } }
        }
        [DataSourceProperty]
        public string SocketOffsetXStr
        {
            get => _socketOffsetX.ToString("F2");
            set { if (float.TryParse(value, out float res)) SocketOffsetX = res; }
        }

        [DataSourceProperty]
        public float SocketOffsetY
        {
            get => _socketOffsetY;
            set { if (value != _socketOffsetY) { _socketOffsetY = value; OnPropertyChangedWithValue(value, nameof(SocketOffsetY)); OnPropertyChanged(nameof(SocketOffsetYStr)); } }
        }
        [DataSourceProperty]
        public string SocketOffsetYStr
        {
            get => _socketOffsetY.ToString("F2");
            set { if (float.TryParse(value, out float res)) SocketOffsetY = res; }
        }

        [DataSourceProperty]
        public float SocketOffsetZ
        {
            get => _socketOffsetZ;
            set { if (value != _socketOffsetZ) { _socketOffsetZ = value; OnPropertyChangedWithValue(value, nameof(SocketOffsetZ)); OnPropertyChanged(nameof(SocketOffsetZStr)); } }
        }
        [DataSourceProperty]
        public string SocketOffsetZStr
        {
            get => _socketOffsetZ.ToString("F2");
            set { if (float.TryParse(value, out float res)) SocketOffsetZ = res; }
        }

        // ==========================================
        // 5. CAMERA SELF ROTATION PROPERTIES
        // ==========================================
        [DataSourceProperty]
        public float CamSelfYaw
        {
            get => _camSelfYaw;
            set { if (value != _camSelfYaw) { _camSelfYaw = value; OnPropertyChangedWithValue(value, nameof(CamSelfYaw)); OnPropertyChanged(nameof(CamSelfYawStr)); } }
        }
        [DataSourceProperty]
        public string CamSelfYawStr
        {
            get => _camSelfYaw.ToString("F1");
            set { if (float.TryParse(value, out float res)) CamSelfYaw = res; }
        }

        [DataSourceProperty]
        public float CamSelfPitch
        {
            get => _camSelfPitch;
            set { if (value != _camSelfPitch) { _camSelfPitch = value; OnPropertyChangedWithValue(value, nameof(CamSelfPitch)); OnPropertyChanged(nameof(CamSelfPitchStr)); } }
        }
        [DataSourceProperty]
        public string CamSelfPitchStr
        {
            get => _camSelfPitch.ToString("F1");
            set { if (float.TryParse(value, out float res)) CamSelfPitch = res; }
        }

        [DataSourceProperty]
        public float CamSelfRoll
        {
            get => _camSelfRoll;
            set { if (value != _camSelfRoll) { _camSelfRoll = value; OnPropertyChangedWithValue(value, nameof(CamSelfRoll)); OnPropertyChanged(nameof(CamSelfRollStr)); } }
        }
        [DataSourceProperty]
        public string CamSelfRollStr
        {
            get => _camSelfRoll.ToString("F1");
            set { if (float.TryParse(value, out float res)) CamSelfRoll = res; }
        }

        // ==========================================
        // FOV
        // ==========================================
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
            get => _camFov.ToString("F1");
            set { if (float.TryParse(value, out float res)) CamFov = res; }
        }
    }
}
