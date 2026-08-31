using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 剧本演绎对话面板 VM（T7 重制版，2026-08-30 用户裁定：基于 DialogChoice 砍剩三要素）——
    /// 说话人名字 + 角色立绘位（@PortraitTexture 空 = 隐藏，没图占位不塌）+ 对白文本 + 选项按钮 + 继续按钮。
    /// 🔴 隔离纪律：新类新链，不碰旧 StoryDialogVM（谈判 UI + OnDialogClosed 旧链 GenerateEventAsync 崩溃路径，铁律 16）；
    ///    关闭 = 本 VM 自清（OnClosed 事件 → 播放器/W 层收尾；IM confirmFight 禁令路径在此不存在）。
    /// </summary>
    public class PlaybackDialogVM : ViewModel
    {
        private string _speakerName = "";
        private string _dialogueContent = "";
        private string _portraitTexture;             // 立绘 sprite 名（"lwnprof_bustup_xxx"）；空 = 不显示
        private bool _isVisible;
        private bool _areOptionsVisible;
        private bool _areContinueVisible = true;
        private string _continueHintText = "继续";
        private bool _isPortraitVisible;

        /// <summary>关闭回调（普通属性 = 外部可整体重设；播放器（W5）每行前重设一次）。</summary>
        public Action OnClosedHandler { get; set; }

        [DataSourceProperty]
        public string SpeakerName
        {
            get => _speakerName;
            set { _speakerName = value ?? ""; OnPropertyChangedWithValue(_speakerName, "SpeakerName"); }
        }

        [DataSourceProperty]
        public string DialogueContent
        {
            get => _dialogueContent;
            set { _dialogueContent = value ?? ""; OnPropertyChangedWithValue(_dialogueContent, "DialogueContent"); }
        }

        [DataSourceProperty]
        public string PortraitTexture
        {
            get => _portraitTexture;
            set { _portraitTexture = value; OnPropertyChangedWithValue(_portraitTexture, "PortraitTexture"); }
        }

        [DataSourceProperty]
        public bool IsPortraitVisible
        {
            get => _isPortraitVisible;
            set { _isPortraitVisible = value; OnPropertyChangedWithValue(value, "IsPortraitVisible"); }
        }

        [DataSourceProperty]
        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChangedWithValue(value, "IsVisible"); }
        }

        [DataSourceProperty]
        public bool AreOptionsVisible
        {
            get => _areOptionsVisible;
            set { _areOptionsVisible = value; OnPropertyChangedWithValue(value, "AreOptionsVisible"); }
        }

        [DataSourceProperty]
        public bool AreContinueVisible
        {
            get => _areContinueVisible;
            set { _areContinueVisible = value; OnPropertyChangedWithValue(value, "AreContinueVisible"); }
        }

        [DataSourceProperty]
        public string ContinueHintText
        {
            get => _continueHintText;
            set { _continueHintText = value ?? ""; OnPropertyChangedWithValue(_continueHintText, "ContinueHintText"); }
        }

        public MBBindingList<StoryOptionVM> OptionList { get; } = new MBBindingList<StoryOptionVM>();

        public PlaybackDialogVM()
        {
        }

        /// <summary>显示一句话（portraitTexture null = 无立绘只说话人名+正文）</summary>
        public void Show(string speaker, string text, string portraitTexture = null)
        {
            SpeakerName = speaker;
            DialogueContent = text;
            PortraitTexture = portraitTexture;
            IsPortraitVisible = !string.IsNullOrEmpty(portraitTexture);
            IsVisible = true;
            AreOptionsVisible = false;
            AreContinueVisible = true;
            ContinueHintText = "继续";
        }

        /// <summary>显示选项（选完由 ExecuteOption 回调 OnClosed）</summary>
        public void ShowOptions(IEnumerable<StoryOptionVM> options, string prompt = null)
        {
            OptionList.Clear();
            foreach (var o in options ?? Enumerable.Empty<StoryOptionVM>())
                OptionList.Add(o);
            if (prompt != null) DialogueContent = prompt;
            AreOptionsVisible = true;
            AreContinueVisible = false;
            IsVisible = true;
        }

        /// <summary>关闭（新链唯一出口：清场 + OnClosed；🔴 不触发任何旧链事件）</summary>
        public void Close()
        {
            IsVisible = false;
            AreOptionsVisible = false;
            OnClosedHandler?.Invoke();
        }

        // ── DataSource 命令（XML Command.Click 绑定）──

        /// <summary>继续按钮点击 → 关闭（推进由订阅方处理）</summary>
        public void OnClickContinue()
        {
            Close();
        }

        /// <summary>选项按钮点击 → 关闭（选中值在 StoryOptionVM 内，W5 播放器按 OnClosed 取）</summary>
        public void ExecuteOption(StoryOptionVM opt)
        {
            if (opt == null) return;
            Close();
        }
    }
}
