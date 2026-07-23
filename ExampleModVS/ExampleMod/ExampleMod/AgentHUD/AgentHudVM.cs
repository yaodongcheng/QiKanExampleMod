using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using static System.Net.Mime.MediaTypeNames;

namespace LivingWorldNpcs
{
    public class AgentHudVM : ViewModel
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
        private float _targetHealthWidth;
        private float _prevHealth;

        // 文本内容
        private string _agentName;
        private string _speechText;
        private string _damageText;

        // 可见性控制
        private bool _showName;
        private bool _showSpeech;
        private bool _showHealth;
        private bool _showDamage;
        private bool _showAlert;

        // 计时器
        private float _speechTimer;
        private float _damageTimer;

        // 警戒值属性
        private float _alertValue;
        private float _alertFillHeight;
        private string _eyeBgColor;
        private string _eyeFillColor;

        // 缓存
        private static AttackTriggerMissionLogic _cachedDuelLogic;

        // 持有 Agent 的引用
        public Agent TargetAgent { get; private set; }

        // 【方法 A】低频逻辑：获取数据，不负责动画和计时
        // 建议每 10-15 帧调用一次
        public void UpdateLogic()
        {
            if (TargetAgent == null || !TargetAgent.IsActive())
            {
                IsVisible = false;
                return;
            }

            // 1. 获取血量
            float currentHp = TargetAgent.Health;

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

            // 2. 伤害判断
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

            // 3. 计算目标血量宽度
            float hpPercentage = currentHp / TargetAgent.HealthLimit;
            _targetHealthWidth = MaxBarWidth * MBMath.ClampFloat(hpPercentage, 0f, 1f);

            // 4. 血条显示条件（敌意驱动，不看手里有没有武器——守卫巡逻恒持械，武器是制服不是敌意）
            //    戒备信号用 CurrentWatchState（两版 DLL 都有，免去 #if）：
            //    我方战斗系统开打时 CombatManager.ActivateFightMode 设 Alarmed、打完重置 Patrolling；
            //    守卫由原版 AlarmedBehaviorGroup 驱动，起疑 Cautious → 确认威胁 Alarmed。
            bool isFighting = AgentAIController.GetBrainForAgent(TargetAgent)?.IsCurrentOrPending<FightEnemyAction>() ?? false;
            bool isHealthLow = hpPercentage < 0.95f && currentHp > 0;
            bool isAlerted = TargetAgent.CurrentWatchState == Agent.WatchState.Alarmed;

            ShowHealth = isFighting || isHealthLow || isAlerted;

            // 🆕 感知关闭模式下：只有玩家攻击过的 Agent 才显示血条/伤害（避免满屏 HUD 碍眼）
            if (Settings.Instance.IsInteractionDisabled())
            {
                var atkLogic = AttackTriggerMissionLogic.Instance;
                bool playerAttacked = atkLogic?.IsAgentAttackedByPlayer(TargetAgent) ?? false;
                ShowHealth = ShowHealth && playerAttacked;
                ShowDamage = ShowDamage && playerAttacked;
            }

            // 🆕 NpcIntent 调试文本（玩家自己/战场中不显示——玩家无 AI Intent）
            {
                var brain = AgentAIController.GetBrainForAgent(TargetAgent);
                var intent = brain?.CurrentIntent;
                NpcIntentDebugText = intent?.ToString() ?? "";
                ShowIntentDebug = !TargetAgent.IsMainAgent
                    && !Settings.Instance.IsInteractionDisabled()
                    && intent != null;
            }

            // 5. 名字总领规则：FOV 内任意元素显示时浮现名字
            //    ShowAlert 在此处生效是因为 UpdateLogic 只在 FOV 内执行——
            //    FOV 外 NPC 的 ShowName 不会被计算，眼睛独立显示但不带名字
            ShowName = ShowSpeech || ShowHealth || ShowDamage || ShowAlert;

            // 6. 容器可见性
            //    IsVisible = ShowName || ShowAlert（警戒眼睛可以独立触发容器显示）
            bool hasContent = ShowName || ShowAlert;
            if (hasContent)
            {
                IsVisible = true;
            }
        }

        // 【方法 B】高频逻辑：动画插值和计时器
        // 必须每帧调用
        public void UpdateFrame(float dt)
        {
            if (!IsVisible) return;

            // 伤害显示计时
            if (ShowDamage)
            {
                _damageTimer -= dt;
                if (_damageTimer <= 0) ShowDamage = false;
            }

            // 说话显示计时
            if (ShowSpeech)
            {
                _speechTimer -= dt;
                if (_speechTimer <= 0) ShowSpeech = false;
            }

            // 血条平滑动画
            if (Math.Abs(_currentHealthWidth - _targetHealthWidth) > 0.01f)
            {
                _currentHealthWidth = MBMath.Lerp(_currentHealthWidth, _targetHealthWidth, dt * 10.0f);
                OnPropertyChangedWithValue(_currentHealthWidth, "CurrentHealthWidth");
            }

            // 最终可见性检查
            // 如果没有任何东西要显示且不在警戒状态，关闭 IsVisible
            if (!ShowSpeech && !ShowDamage && !_showHealth && !ShowAlert)
            {
                IsVisible = false;
            }
        }

        // 警戒值计算（由 MissionView 每帧注入 AlertValue 后自动更新）
        public void UpdateAlertVisuals()
        {
            float maxIconHeight = 20f;

            if (_alertValue <= 0.01f)
            {
                ShowAlert = false;
                AlertFillHeight = 0f;
            }
            else if (_alertValue <= 1f)
            {
                ShowAlert = true;
                EyeBgColor = "#FFFFFFFF";    // 白底
                EyeFillColor = "#FFD700FF";   // 黄进度
                AlertFillHeight = _alertValue / 1f * maxIconHeight;
            }
            else if (_alertValue <= 2f)
            {
                ShowAlert = true;
                EyeBgColor = "#FFD700FF";    // 黄底
                EyeFillColor = "#FF0000FF";   // 红进度
                AlertFillHeight = (_alertValue - 1f) / 1f * maxIconHeight;
            }
            else
            {
                ShowAlert = true;
                EyeBgColor = "#FF0000FF";    // 纯红
                EyeFillColor = "#FF0000FF";
                AlertFillHeight = maxIconHeight;
            }
        }

        public AgentHudVM(Agent agent)
        {
            if (agent == null)
                return;
            TargetAgent = agent;
            _brushName = "MyBrush24";
            AgentName = agent.Name;

            // 初始状态：全部隐藏
            IsVisible = false;
            ShowSpeech = false;
            ShowDamage = false;
            ShowHealth = false;
            ShowName = false;
            ShowAlert = false;
            ShowIntentDebug = false;

            _currentHealthWidth = MaxBarWidth;
            _prevHealth = agent.Health;

            BubbleWidth = 300;
            BubbleHeight = 150;

            // 警戒值初始
            _alertValue = 0f;
            UpdateAlertVisuals();
        }

        public void Speak(string text)
        {
            SpeechText = text;
            ShowSpeech = true;
            _speechTimer = 4.0f + (text.Length * 0.1f);
        }

        // ============================================================
        // 位置/缩放
        // ============================================================

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

        // ============================================================
        // 名字
        // ============================================================

        [DataSourceProperty]
        public string AgentName
        {
            get => _agentName;
            set { if (value != _agentName) { _agentName = value; OnPropertyChangedWithValue(value, "AgentName"); } }
        }

        [DataSourceProperty]
        public bool ShowName
        {
            get => _showName;
            set { if (value != _showName) { _showName = value; OnPropertyChangedWithValue(value, "ShowName"); } }
        }

        // ============================================================
        // 说话
        // ============================================================

        [DataSourceProperty]
        public string SpeechText
        {
            get => _speechText;
            set { if (value != _speechText) { _speechText = value; OnPropertyChangedWithValue(value, "SpeechText"); } }
        }

        [DataSourceProperty]
        public bool ShowSpeech
        {
            get => _showSpeech;
            set { if (value != _showSpeech) { _showSpeech = value; OnPropertyChangedWithValue(value, "ShowSpeech"); } }
        }

        // ============================================================
        // 血条
        // ============================================================

        [DataSourceProperty]
        public bool ShowHealth
        {
            get => _showHealth;
            set { if (value != _showHealth) { _showHealth = value; OnPropertyChangedWithValue(value, "ShowHealth"); } }
        }

        // ============================================================
        // 伤害
        // ============================================================

        [DataSourceProperty]
        public string DamageText
        {
            get => _damageText;
            set { if (value != _damageText) { _damageText = value; OnPropertyChangedWithValue(value, "DamageText"); } }
        }

        [DataSourceProperty]
        public bool ShowDamage
        {
            get => _showDamage;
            set { if (value != _showDamage) { _showDamage = value; OnPropertyChangedWithValue(value, "ShowDamage"); } }
        }

        // ============================================================
        // 警戒值 🆕
        // ============================================================

        /// <summary>警戒值 0~2+（由 MissionView 每帧注入）</summary>
        public float AlertValue
        {
            get => _alertValue;
            set
            {
                if (Math.Abs(value - _alertValue) > 0.001f)
                {
                    _alertValue = value;
                    UpdateAlertVisuals();
                }
            }
        }

        [DataSourceProperty]
        public float AlertFillHeight
        {
            get => _alertFillHeight;
            set { if (Math.Abs(value - _alertFillHeight) > 0.01f) { _alertFillHeight = value; OnPropertyChangedWithValue(value, "AlertFillHeight"); } }
        }

        [DataSourceProperty]
        public bool ShowAlert
        {
            get => _showAlert;
            set { if (value != _showAlert) { _showAlert = value; OnPropertyChangedWithValue(value, "ShowAlert"); } }
        }

        [DataSourceProperty]
        public string EyeBgColor
        {
            get => _eyeBgColor;
            set { if (value != _eyeBgColor) { _eyeBgColor = value; OnPropertyChangedWithValue(value, "EyeBgColor"); } }
        }

        [DataSourceProperty]
        public string EyeFillColor
        {
            get => _eyeFillColor;
            set { if (value != _eyeFillColor) { _eyeFillColor = value; OnPropertyChangedWithValue(value, "EyeFillColor"); } }
        }

        // 🆕 NpcIntent 调试文本
        private bool _showIntentDebug;
        [DataSourceProperty]
        public bool ShowIntentDebug
        {
            get => _showIntentDebug;
            set { if (value != _showIntentDebug) { _showIntentDebug = value; OnPropertyChangedWithValue(value, "ShowIntentDebug"); } }
        }

        private string _npcIntentDebugText;
        [DataSourceProperty]
        public string NpcIntentDebugText
        {
            get => _npcIntentDebugText;
            set { if (value != _npcIntentDebugText) { _npcIntentDebugText = value; OnPropertyChangedWithValue(value, "NpcIntentDebugText"); } }
        }
    }
}
