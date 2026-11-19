using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Localization;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 511 剧情对话自建 UI 的数据源（2026-11-19）。
    /// 只负责把运行器此刻的状态摊平成控件能绑的字段；**不判断对话逻辑**（可见性/可选性都在运行器里）。
    /// </summary>
    public sealed class StoryDialogueVM : ViewModel
    {
        private string _speakerName = "";
        private string _lineText = "";
        private string _hintText = "";
        private bool _isHintVisible;
        private bool _isContinueVisible;
        private string _continueText = "";
        private bool _isVisible;

        public StoryDialogueVM()
        {
            Options = new MBBindingList<StoryDialogueOptionVM>();
        }

        [DataSourceProperty]
        public string SpeakerName
        {
            get => _speakerName;
            set { if (_speakerName != value) { _speakerName = value; OnPropertyChangedWithValue(value, nameof(SpeakerName)); } }
        }

        [DataSourceProperty]
        public string LineText
        {
            get => _lineText;
            set { if (_lineText != value) { _lineText = value; OnPropertyChangedWithValue(value, nameof(LineText)); } }
        }

        /// <summary>悬停说明（铁律 43：检定行 + 后果行）。没有悬停时隐藏。</summary>
        [DataSourceProperty]
        public string HintText
        {
            get => _hintText;
            set { if (_hintText != value) { _hintText = value; OnPropertyChangedWithValue(value, nameof(HintText)); } }
        }

        [DataSourceProperty]
        public bool IsHintVisible
        {
            get => _isHintVisible;
            set { if (_isHintVisible != value) { _isHintVisible = value; OnPropertyChangedWithValue(value, nameof(IsHintVisible)); } }
        }

        [DataSourceProperty]
        public bool IsContinueVisible
        {
            get => _isContinueVisible;
            set { if (_isContinueVisible != value) { _isContinueVisible = value; OnPropertyChangedWithValue(value, nameof(IsContinueVisible)); } }
        }

        [DataSourceProperty]
        public string ContinueText
        {
            get => _continueText;
            set { if (_continueText != value) { _continueText = value; OnPropertyChangedWithValue(value, nameof(ContinueText)); } }
        }

        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            set { if (_isVisible != value) { _isVisible = value; OnPropertyChangedWithValue(value, nameof(IsVisible)); } }
        }

        [DataSourceProperty]
        public MBBindingList<StoryDialogueOptionVM> Options { get; }

        /// <summary>「继续 / 离开」按钮（原版对话的同一个语义：没有选项时读完这一句再往下）。</summary>
        public void ExecuteContinue()
        {
            StoryDialogueUiRunner.Continue();
        }

        public void Refresh()
        {
            Options.Clear();
            if (!StoryDialogueUiRunner.IsRunning)
            {
                IsVisible = false;
                IsContinueVisible = false;
                IsHintVisible = false;
                return;
            }

            IsVisible = true;
            SpeakerName = StoryDialogueUiView.CurrentSpeakerName();
            LineText = StoryDialogueUiView.CurrentLineText();

            var visible = StoryDialogueUiRunner.Options;
            for (int i = 0; i < visible.Count; i++)
            {
                var transition = visible[i];
                bool selectable = DialogueInjector.IsTransitionSelectable(transition);
                Options.Add(new StoryDialogueOptionVM(
                    i,
                    (i + 1) + ". " + DialogueInjector.GetTransitionText(transition),
                    DialogueInjector.GetTransitionHint(transition),
                    selectable));
            }

            bool continuePending = StoryDialogueUiRunner.IsContinuePending;
            IsContinueVisible = continuePending;
            ContinueText = continuePending ? StoryDialogueUiView.ContinueLabel() : "";
            IsHintVisible = false;
            HintText = "";
        }

        public void ShowHint(string hint)
        {
            HintText = hint ?? "";
            IsHintVisible = !string.IsNullOrEmpty(HintText);
        }

        public void ClearHint()
        {
            HintText = "";
            IsHintVisible = false;
        }
    }

    public sealed class StoryDialogueOptionVM : ViewModel
    {
        private readonly int _index;
        private readonly string _hint;
        private string _text;
        private bool _isEnabled;
        private bool _isHovered;
        private HintViewModel _optionHint;

        public StoryDialogueOptionVM(int index, string text, string hint, bool enabled)
        {
            _index = index;
            _hint = hint;
            _text = text;
            _isEnabled = enabled;
            // 原版选项的悬停说明由 HintWidget 在鼠标旁弹出（DataSource=OptionHint），
            // 不是自绘文本 —— 位置与样式都跟原版一致。
            _optionHint = string.IsNullOrEmpty(hint) ? null : new HintViewModel(new TextObject(hint));
        }

        /// <summary>原版 ConversationItem 的 HintWidget 数据源（鼠标旁浮窗）。</summary>
        [DataSourceProperty]
        public HintViewModel OptionHint
        {
            get => _optionHint;
            set { if (_optionHint != value) { _optionHint = value; OnPropertyChangedWithValue(value, nameof(OptionHint)); } }
        }

        /// <summary>原版 ConversationItemImageWidget 的 IsSpecial（我们不用特殊底色，恒 false）。</summary>
        [DataSourceProperty]
        public bool IsSpecial
        {
            get => false;
            set { }
        }

        [DataSourceProperty]
        public string Text
        {
            get => _text;
            set { if (_text != value) { _text = value; OnPropertyChangedWithValue(value, nameof(Text)); } }
        }

        [DataSourceProperty]
        public bool IsEnabled
        {
            get => _isEnabled;
            set { if (_isEnabled != value) { _isEnabled = value; OnPropertyChangedWithValue(value, nameof(IsEnabled)); } }
        }

        [DataSourceProperty]
        public bool IsHovered
        {
            get => _isHovered;
            set { if (_isHovered != value) { _isHovered = value; OnPropertyChangedWithValue(value, nameof(IsHovered)); } }
        }

        public void ExecuteClick()
        {
            // 点击是否到达是"点不动"这类反馈的唯一判据：这行出现在日志里 = UI 收到了点击。
            DebugLogger.Log("[StoryDialogueUI] click index=" + _index + " enabled=" + _isEnabled + " text=" + (_text ?? ""));
            if (!_isEnabled) return;
            StoryDialogueUiRunner.Select(_index);
        }

        public void ExecuteHoverBegin()
        {
            IsHovered = true;
            // 悬浮链路的到达证据（与点击同一条鼠标事件通道）。
            if (!string.IsNullOrEmpty(_hint))
                DebugLogger.Log("[StoryDialogueUI] hover index=" + _index);
            StoryDialogueUiView.ShowHint(_hint);
        }

        public void ExecuteHoverEnd()
        {
            IsHovered = false;
            StoryDialogueUiView.ClearHint();
        }
    }
}