using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static System.Net.Mime.MediaTypeNames;

namespace ExampleMod
{
    public class BubbleSayVM : ViewModel
    {
        private string _brushName;
        
        private float _posX;
        private float _posY;
        private float _scale;
        private bool _isVisible;

        private float _bubbleWidth;
        private float _bubbleHeight;

        private const float MaxBarWidth = 100f;
        private float _currentHealthWidth;
        private float _targetHealthWidth;  // 目标宽度（真实血量对应的宽度）
        private float _prevHealth;         //上一帧的血量，用于判断是否受伤
                                           // 文本内容
        private string _agentName;
        private string _speechText;
        private string _damageText;

        // 可见性控制
        private bool _showSpeech;
        private bool _showDamage;

        // 计时器
        private float _speechTimer;
        private float _damageTimer;

        // 【优化】静态缓存逻辑类的引用，不用每帧去 Find
        private static AttackTriggerMissionLogic _cachedDuelLogic;


        // 持有 Agent 的引用，用于每帧计算坐标，但不需要绑定到 UI
        public Agent TargetAgent { get; private set; }
        // 【方法 A】低频逻辑：只负责获取数据，不负责动画和计时
        // 建议每 10-15 帧调用一次
        public void UpdateLogic()
        {
            if (TargetAgent == null || !TargetAgent.IsActive())
            {
                IsVisible = false;
                return;
            }

            // 1. 获取血量 (比较重的操作)
            float currentHp = TargetAgent.Health;

            // 缓存单例引用，避免重复 Find
            if (_cachedDuelLogic == null) _cachedDuelLogic = AttackTriggerMissionLogic.Instance;

            if (_cachedDuelLogic != null)
            {
                float? virtualHp = _cachedDuelLogic.GetVirtualHealth(TargetAgent);
                if (virtualHp.HasValue)
                {
                    currentHp = virtualHp.Value;
                    if (currentHp < 0) currentHp = 0;
                }
            }

            // 2. 伤害判断 (数值计算)
            if (Math.Abs(currentHp - _prevHealth) > 0.1f)
            {
                if (currentHp < _prevHealth)
                {
                    float damageTaken = _prevHealth - currentHp;
                    DamageText = "-" + damageTaken.ToString("F0");
                    ShowDamage = true;
                    _damageTimer = 2.0f; // 重置计时器
                }
                _prevHealth = currentHp;
            }

            // 3. 计算目标宽度 (只计算目标值，不执行 Lerp)
            float hpPercentage = currentHp / TargetAgent.HealthLimit;
            _targetHealthWidth = MaxBarWidth * MBMath.ClampFloat(hpPercentage, 0f, 1f);

            // 4. 决定是否显示 (逻辑层面的可见性)
            bool isWeaponDrawn = !TargetAgent.WieldedWeapon.IsEmpty;
            bool isHealthLow = hpPercentage < 0.95f && currentHp > 0;

            // 如果有内容要显示，就标记为可见
            bool hasContent = (isHealthLow && (isWeaponDrawn || ShowDamage)) || ShowSpeech || ShowDamage;

            // 注意：这里只负责把 IsVisible 设为 true，
            // 如果 hasContent 为 false，我们暂时不强制设为 false，
            // 而是让计时器跑完后再在 UpdateFrame 里决定是否隐藏。
            if (hasContent)
            {
                IsVisible = true;
            }
            
        }

        // 【方法 B】高频逻辑：只负责动画插值和计时器
        // 必须每帧调用
        public void UpdateFrame(float dt)
        {
            // 如果逻辑上都不可见，就没必要跑动画了
            if (!IsVisible) return;

            // 1. 计时器更新 (必须每帧跑)
            if (ShowDamage)
            {
                _damageTimer -= dt;
                if (_damageTimer <= 0) ShowDamage = false;
            }

            if (ShowSpeech)
            {
                _speechTimer -= dt;
                if (_speechTimer <= 0) ShowSpeech = false;
            }

            // 2. 血条平滑动画 (必须每帧跑，否则卡顿)
            if (Math.Abs(_currentHealthWidth - _targetHealthWidth) > 0.01f)
            {
                _currentHealthWidth = MBMath.Lerp(_currentHealthWidth, _targetHealthWidth, dt * 10.0f); // 这里的速度稍微调快点
                OnPropertyChangedWithValue(_currentHealthWidth, "CurrentHealthWidth");
            }

            // 3. 最终可见性检查
            // 如果没有任何东西要显示了，才关掉 IsVisible
            if (!ShowSpeech && !ShowDamage && _targetHealthWidth <= 0) // 举例：死人或者无内容
            {
                // 这里可以加更多条件，比如满血且没说话就隐藏
                bool isFullHealth = _targetHealthWidth >= MaxBarWidth * 0.99f;
                if (!ShowSpeech && !ShowDamage && isFullHealth)
                {
                    IsVisible = false;
                }
            }
        }
        public void UpdateData(float dt)
        {
            if (TargetAgent == null || !TargetAgent.IsActive())
            {
                IsVisible = false;
                return;
            }
            
            // --- 1. 获取血量数据 (支持虚拟血量逻辑) ---
            float currentHp = TargetAgent.Health;
            // 【优化】单例模式缓存获取，或者每帧只获取一次（这里简化为判空获取）
            if (_cachedDuelLogic == null)
            {
                _cachedDuelLogic = AttackTriggerMissionLogic.Instance;
            }

            if (_cachedDuelLogic != null)
            {
                float? virtualHp = _cachedDuelLogic.GetVirtualHealth(TargetAgent);
                if (virtualHp.HasValue)
                {
                    currentHp = virtualHp.Value;
                    if (currentHp < 0) currentHp = 0;
                }
            }

            // --- 2. 伤害检测 ---
            // 只有当血量真的发生变化时才执行
            if (Math.Abs(currentHp - _prevHealth) > 0.1f)
            {
                if (currentHp < _prevHealth)
                {
                    float damageTaken = _prevHealth - currentHp;
                    DamageText = "-" + damageTaken.ToString("F0");
                    ShowDamage = true;
                    _damageTimer = 2.0f;
                }
                _prevHealth = currentHp;
            }


            // --- 3. 计时器更新 ---
            if (ShowDamage)
            {
                _damageTimer -= dt;
                if (_damageTimer <= 0) ShowDamage = false;
            }

            if (ShowSpeech)
            {
                _speechTimer -= dt;
                if (_speechTimer <= 0) ShowSpeech = false;
            }

            // --- 4. 血条动画逻辑 ---
            float hpPercentage = currentHp / TargetAgent.HealthLimit;

            // 只有血条可见时才计算 Lerp 动画，节省 CPU
            if (IsVisible)
            {
                _targetHealthWidth = MaxBarWidth * MBMath.ClampFloat(hpPercentage, 0f, 1f);

                // 简单的浮点比较代替 Abs，性能极微小的提升
                if (_currentHealthWidth != _targetHealthWidth)
                {
                    _currentHealthWidth = MBMath.Lerp(_currentHealthWidth, _targetHealthWidth, dt * 5.0f);
                    // 如果非常接近，直接吸附，停止计算
                    if (MathF.Abs(_currentHealthWidth - _targetHealthWidth) < 0.5f)
                    {
                        _currentHealthWidth = _targetHealthWidth;
                    }
                    OnPropertyChangedWithValue(_currentHealthWidth, "CurrentHealthWidth");
                }
            }

            bool isWeaponDrawn = !TargetAgent.WieldedWeapon.IsEmpty;
            bool isHealthLow = hpPercentage < 0.95f && currentHp > 0;
            bool hasContentToShow = (isHealthLow && (isWeaponDrawn || ShowDamage)) || ShowSpeech || ShowDamage;

            // 如果业务逻辑认为不该显示，直接强制隐藏
            if (!hasContentToShow)
            {
                IsVisible = false;
            }
            else
            {
                IsVisible = true; // 有事可干，请求显示（最终由 View 决定是否在屏幕内）
            }

        }
    
        
        public BubbleSayVM(Agent agent)
        {
            if (agent == null)
                return;
            TargetAgent = agent;
            _brushName = "MyBrush24";
            AgentName = agent.Name;

            // 初始状态：全部隐藏，只在 UpdateData 里根据条件显示
            IsVisible = false;
            ShowSpeech = false;
            ShowDamage = false;

            _currentHealthWidth = MaxBarWidth; // 假设初始满血
            _prevHealth = agent.Health;

            // 如果需要，这里可以初始化默认尺寸
            BubbleWidth = 200; // 示例值，根据 XML 调整
            BubbleHeight = 100;

        }

        public void Speak(string text)
        {
            SpeechText = text;
            ShowSpeech = true;
            _speechTimer = 4.0f + (text.Length * 0.1f); // 动态计算显示时间
        }


        [DataSourceProperty]
        public float CurrentHealthWidth
        {
            get => _currentHealthWidth;
            set
            {
                if (Math.Abs(value - _currentHealthWidth) > 0.01f)
                {
                    _currentHealthWidth = value;
                    OnPropertyChangedWithValue(value, "CurrentHealthWidth");
                }
            }
        }

        [DataSourceProperty]
        public float PosX
        {
            get => _posX;
            set { if (value != _posX) { _posX = value; OnPropertyChangedWithValue(value, "PosX"); } }
        }

        [DataSourceProperty]
        public float PosY
        {
            get => _posY;
            set { if (value != _posY) { _posY = value; OnPropertyChangedWithValue(value, "PosY"); } }
        }

        [DataSourceProperty]
        public float Scale
        {
            get => _scale;
            set { if (value != _scale) { _scale = value; OnPropertyChangedWithValue(value, "Scale"); } }
        }

        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            set { if (value != _isVisible) { _isVisible = value; OnPropertyChangedWithValue(value, "IsVisible"); } }
        }


        [DataSourceProperty]
        public float BubbleWidth
        {
            get => _bubbleWidth;
            set { if (value != _bubbleWidth) { _bubbleWidth = value; OnPropertyChangedWithValue(value, "BubbleWidth"); } }
        }

        [DataSourceProperty]
        public float BubbleHeight
        {
            get => _bubbleHeight;
            set { if (value != _bubbleHeight) { _bubbleHeight = value; OnPropertyChangedWithValue(value, "BubbleHeight"); } }
        }

        [DataSourceProperty]
        public string BrushName
        {
            get => _brushName;
            set
            {
                if (_brushName != value)
                {
                    _brushName = value;
                    OnPropertyChangedWithValue(value, "BrushName");
                }
            }
        }

        [DataSourceProperty]
        public string AgentName
        {
            get => _agentName;
            set { if (value != _agentName) { _agentName = value; OnPropertyChangedWithValue(value, "AgentName"); } }
        }

        [DataSourceProperty]
        public string SpeechText
        {
            get => _speechText;
            set { if (value != _speechText) { _speechText = value; OnPropertyChangedWithValue(value, "SpeechText"); } }
        }

        [DataSourceProperty]
        public string DamageText
        {
            get => _damageText;
            set { if (value != _damageText) { _damageText = value; OnPropertyChangedWithValue(value, "DamageText"); } }
        }

        [DataSourceProperty]
        public bool ShowSpeech
        {
            get => _showSpeech;
            set { if (value != _showSpeech) { _showSpeech = value; OnPropertyChangedWithValue(value, "ShowSpeech"); } }
        }

        [DataSourceProperty]
        public bool ShowDamage
        {
            get => _showDamage;
            set { if (value != _showDamage) { _showDamage = value; OnPropertyChangedWithValue(value, "ShowDamage"); } }
        }
    }
}
