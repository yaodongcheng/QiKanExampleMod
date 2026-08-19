using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    public class InteractionItemVM : ViewModel
    {
        private string _actionText;
        private string _keyText;
        private readonly string _interactionId;   // 玩法 ID（null/空 = 无键位提示）
        private bool _requiresHold;               // 该玩法配置 PressMode == Long → 显示四边进度
        private string _keycapColor = KeycapColorShort;   // 键帽底色：Short 纯白 / Long 淡青白（按法一眼区分，用户拍板 2026-08-06）。
                                                          // ⚠️ 必须字段级初始化合法颜色串——Gauntlet 绑定建立时读取初始值
                                                          // 推给 Color 属性，null/空串会让引擎 ConvertStringToColor 崩溃
        private string _segColor = SegColorCharging;   // 四边进度颜色：蓄力中绿 / 蓄力完成金。
                                                       // ⚠️ 必须字段级初始化合法颜色串——Gauntlet 绑定建立时读取初始值
                                                       // 推给 Color 属性，null/空串会让引擎 ConvertStringToColor 崩溃
                                                       // （Short 项进度条不可见但绑定仍存在，更要保证非 null）
        private float _segFillWidth0;             // 上条填充长度 px（左→右）
        private float _segFillHeight1;            // 右条填充长度 px（上→下）
        private float _segFillWidth2;             // 下条填充长度 px（右→左）
        private float _segFillHeight3;            // 左条填充长度 px（下→上）

        /// <summary>进度条单段最大长度 px（= 键帽边长 30，与 InteractArea.xml 布局常量一致——贴键帽边缘）。</summary>
        private const float SegLength = 30f;

        /// <summary>键帽底色：Short 纯白（无四边）/ Long 淡青白（+四边），按法一眼区分。</summary>
        private const string KeycapColorShort = "#FFFFFFFF";
        private const string KeycapColorLong = "#E8FAF6FF";   // 近白：保留一丝青调与 Short 纯白区分（用户拍板 2026-08-06）

        /// <summary>
        /// 四边进度条颜色——三种状态色（用户拍板 2026-08-06）：
        /// ① 没蓄力 = 灰白（InteractArea.xml 四条灰白底条常显 100% 不透明，即空白框状态）；
        /// ② 蓄力中 = 绿色（进度覆盖到哪，哪里就绿，顺时针推进，盖在灰黑边之上）；
        /// ③ 蓄力完成 = 金色（满框待命，"可以松手"）。
        /// ⚠️ 引擎 ConvertStringToColor 只支持 #RRGGBBAA（8 位 hex）——6 位 hex 会 Substring 越界崩溃（实机踩过），必须补齐 FF。
        /// </summary>
        private const string SegColorCharging = "#00E676FF";   // 蓄力中：绿
        private const string SegColorReady = "#FFE97FFF";      // 蓄力完成：金

        // 构造函数：键位/按法由 ModInput.GetBinding(interactionId) 从配置取（UI 与输入共享同一份配置）
        public InteractionItemVM(string actionText, string interactionId)
        {
            _actionText = actionText;
            _interactionId = interactionId;
            RefreshKeyText();
        }

        public string InteractionId => _interactionId;

        /// <summary>设备切换 / 配置热重载时重算键位提示与按法（字形按当前设备，按法随配置）。</summary>
        public void RefreshKeyText()
        {
            if (string.IsNullOrEmpty(_interactionId))
            {
                KeyText = "";
                RequiresHold = false;
                return;
            }
            var binding = ModInput.GetBinding(_interactionId);
            if (binding == null)
            {
                KeyText = "";
                RequiresHold = false;
                return;
            }
            KeyText = ModInput.Glyph(_interactionId);
            RequiresHold = binding.PressMode == ModInputPressMode.Long;
            // 键帽底色随按法：Short 纯白（无四边）/ Long 淡青白（+四边）——按法一眼区分
            KeycapColor = RequiresHold ? KeycapColorLong : KeycapColorShort;
            // 初始：绿色进度（未蓄力时长 0 不可见，露出灰白底条；满框待命由 UpdateHoldProgress 切金）
            if (RequiresHold) SegColor = SegColorCharging;
        }

        /// <summary>
        /// 四边进度每帧驱动：进度条底 = 灰白四边（XML 常显），绿色进度段覆盖其上（蓄力中），
        /// 满框切金色（蓄力完成待命，"可以松手"）。三种状态色（用户拍板 2026-08-06）：
        /// ① 没蓄力 = 灰白边（空白）；② 蓄力中 = 绿（进度覆盖到哪哪变绿，顺时针推进）；③ 完成 = 金。
        /// 第 i 段填充长度 = clamp(progress*4 − i, 0, 1) × 段长。
        /// </summary>
        public void UpdateHoldProgress(float progress)
        {
            if (!RequiresHold) return;   // Short 项无四边进度
            float p = MathF.Clamp(progress, 0f, 1f);
            SegFillWidth0 = MathF.Clamp(p * 4f - 0f, 0f, 1f) * SegLength;
            SegFillHeight1 = MathF.Clamp(p * 4f - 1f, 0f, 1f) * SegLength;
            SegFillWidth2 = MathF.Clamp(p * 4f - 2f, 0f, 1f) * SegLength;
            SegFillHeight3 = MathF.Clamp(p * 4f - 3f, 0f, 1f) * SegLength;
            SegColor = p >= 1f ? SegColorReady : SegColorCharging;   // 蓄力中绿 → 完成金
        }

        // 对应 XML 中的 Text="@ActionText"
        [DataSourceProperty]
        public string ActionText
        {
            get => _actionText;
            set
            {
                if (value != _actionText)
                {
                    _actionText = value;
                    OnPropertyChangedWithValue(value, nameof(ActionText));
                }
            }
        }

        // 对应 XML 中的 Text="@KeyText"
        [DataSourceProperty]
        public string KeyText
        {
            get => _keyText;
            set
            {
                if (value != _keyText)
                {
                    _keyText = value;
                    OnPropertyChangedWithValue(value, nameof(KeyText));
                }
            }
        }

        // 对应 XML 中的 IsVisible="@RequiresHold"（四周边 + 进度整体显隐；短按项无）
        [DataSourceProperty]
        public bool RequiresHold
        {
            get => _requiresHold;
            set
            {
                if (value != _requiresHold)
                {
                    _requiresHold = value;
                    OnPropertyChangedWithValue(value, nameof(RequiresHold));
                }
            }
        }

        // 对应 XML 中的 Color="@KeycapColor"（键帽底色：Short 纯白 / Long 近白）
        [DataSourceProperty]
        public string KeycapColor
        {
            get => _keycapColor;
            set
            {
                if (value != _keycapColor)
                {
                    _keycapColor = value;
                    OnPropertyChangedWithValue(value, nameof(KeycapColor));
                }
            }
        }

        // 对应 XML 中的 Color="@SegColor"（4 条填充条共用：蓄力中绿 / 满框待命金色）
        [DataSourceProperty]
        public string SegColor
        {
            get => _segColor;
            set
            {
                if (value != _segColor)
                {
                    _segColor = value;
                    OnPropertyChangedWithValue(value, nameof(SegColor));
                }
            }
        }

        // 对应 XML 中的 SuggestedWidth="@SegFillWidth0"（上条，左→右）
        [DataSourceProperty]
        public float SegFillWidth0
        {
            get => _segFillWidth0;
            set { if (value != _segFillWidth0) { _segFillWidth0 = value; OnPropertyChangedWithValue(value, nameof(SegFillWidth0)); } }
        }

        // 对应 XML 中的 SuggestedHeight="@SegFillHeight1"（右条，上→下）
        [DataSourceProperty]
        public float SegFillHeight1
        {
            get => _segFillHeight1;
            set { if (value != _segFillHeight1) { _segFillHeight1 = value; OnPropertyChangedWithValue(value, nameof(SegFillHeight1)); } }
        }

        // 对应 XML 中的 SuggestedWidth="@SegFillWidth2"（下条，右→左）
        [DataSourceProperty]
        public float SegFillWidth2
        {
            get => _segFillWidth2;
            set { if (value != _segFillWidth2) { _segFillWidth2 = value; OnPropertyChangedWithValue(value, nameof(SegFillWidth2)); } }
        }

        // 对应 XML 中的 SuggestedHeight="@SegFillHeight3"（左条，下→上）
        [DataSourceProperty]
        public float SegFillHeight3
        {
            get => _segFillHeight3;
            set { if (value != _segFillHeight3) { _segFillHeight3 = value; OnPropertyChangedWithValue(value, nameof(SegFillHeight3)); } }
        }

        // 你可以在这里添加 Execute 方法，用于处理玩家按下按键后的逻辑
        public void Execute()
        {
            // 处理点击或按键逻辑
        }
    }

    public class InteractionVM : ViewModel
    {
        private bool _isVisible;
        private string _targetName;
        private MBBindingList<InteractionItemVM> _interactionList;

        public InteractionVM()
        {
            // 初始化列表，必须实例化，否则报错
            _interactionList = new MBBindingList<InteractionItemVM>();
            _targetName = "";
            _isVisible = true;
        }

        // 对应 XML 中的 Text="@TargetName"
        [DataSourceProperty]
        public string TargetName
        {
            get => _targetName;
            set
            {
                if (value != _targetName)
                {
                    _targetName = value;
                    OnPropertyChangedWithValue(value, nameof(TargetName));
                    OnPropertyChangedWithValue(!string.IsNullOrEmpty(value), nameof(HasTarget));
                }
            }
        }

        [DataSourceProperty]
        public bool HasTarget => !string.IsNullOrEmpty(_targetName);

        private string _targetNameColor = NameDisplayRules.NeutralColor;
        /// <summary>目标名颜色（🔴 2026-08-19 统一规范，关系色：玩家金 / 友方绿 / 敌对红 / 中立白，
        /// NameDisplayRules 同源，与 HUD/IM 一致）。对应 XML TextColor="@TargetNameColor"。</summary>
        [DataSourceProperty]
        public string TargetNameColor
        {
            get => _targetNameColor;
            set { if (value != _targetNameColor) { _targetNameColor = value; OnPropertyChangedWithValue(value, nameof(TargetNameColor)); } }
        }

        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            set { if (value != _isVisible) { _isVisible = value; OnPropertyChangedWithValue(value, "IsVisible"); } }
        }



        // 对应 XML 中的 DataSource="{InteractionList}"
        [DataSourceProperty]
        public MBBindingList<InteractionItemVM> InteractionList
        {
            get => _interactionList;
            set
            {
                if (value != _interactionList)
                {
                    _interactionList = value;
                    OnPropertyChangedWithValue(value, nameof(InteractionList));
                }
            }
        }

        // === 辅助方法：用于游戏逻辑调用 ===

        // 用于外部刷新数据的方法（interactionId = 玩法 ID，键位/按法由 ModInput 从配置解析；null = 无键位提示）
        public void UpdateTarget(string name, List<(string action, string interactionId)> actions, string color = null)
        {
            TargetName = name;
            // 🔴 2026-08-19（统一规范）：目标名颜色随目标传入（null → 中立白兜底）
            TargetNameColor = color ?? NameDisplayRules.NeutralColor;
            InteractionList.Clear();
            foreach (var act in actions)
            {
                InteractionList.Add(new InteractionItemVM(act.action, act.interactionId));
            }
            IsVisible = true;
        }

        /// <summary>
        /// 每帧驱动长按进度框：对 Long 玩法行取 ModInput.HoldProgress → 重算 4 段像素值。
        /// 短按项（RequiresHold=false）内部跳过，不产生绑定刷新。
        /// </summary>
        public void UpdateHoldProgresses()
        {
            foreach (var item in InteractionList)
            {
                if (item.RequiresHold)
                    item.UpdateHoldProgress(ModInput.HoldProgress(item.InteractionId));
            }
        }

        // 输入设备切换（键盘↔手柄）或配置热重载时刷新全部键位提示与按法，无需重建列表
        public void RefreshGlyphs()
        {
            foreach (var item in InteractionList)
                item.RefreshKeyText();
        }

        public void ChangeInteractionName(string oldName, string newName)
        {
            var interaction = InteractionList.FirstOrDefault(h => h.ActionText == oldName);
            if (interaction != null)
                interaction.ActionText = newName;
        }

        public void Hide()
        {
            IsVisible = false;
        }

        // 添加一个交互选项（例如：添加 "F - 交谈"）
        public void AddInteraction(string action, string interactionId)
        {
            InteractionList.Add(new InteractionItemVM(action, interactionId));
        }

        // 清空所有交互
        public void ClearInteractions()
        {
            InteractionList.Clear();
        }
    }
}
