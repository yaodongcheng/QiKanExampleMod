using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Library;
using TaleWorlds.TwoDimension;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 剧本演绎对话面板 VM（T7 重制版，2026-08-30 用户裁定：基于 DialogChoice 砍剩三要素）——
    /// 说话人名字 + 角色立绘位（PortraitImage null = 隐藏，没图占位不塌）+ 对白文本 + 选项按钮 + 继续按钮。
    /// 🔴 隔离纪律：新类新链，不碰旧 StoryDialogVM（谈判 UI + OnDialogClosed 旧链 GenerateEventAsync 崩溃路径，铁律 16）。
    /// </summary>
    public class PlaybackDialogVM : ViewModel
    {
        private string _speakerName = "";
        private string _dialogueContent = "";
        private Sprite _portraitLeft;              // 立绘槽（面板左；主角 = 镜像图——镜像在 Show 内做）
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
        public Sprite PortraitLeft
        {
            get => _portraitLeft;
            set { _portraitLeft = value; OnPropertyChangedWithValue(value, "PortraitLeft"); }
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

        /// <summary>显示一句话。立绘槽 = 面板左（0.2.31 用户："立绘在文字区左侧"）；isMainHero = 主体镜像图（SpriteMirror，面朝左）。
        /// portrait null = 无卡（占位隐藏）。</summary>
        public void Show(string speaker, string text, Sprite portrait = null, bool isMainHero = false)
        {
            SpeakerName = speaker;
            DialogueContent = text;
            PortraitLeft = isMainHero ? SpriteMirror.GetOrMirror(portrait) : portrait;   // 主角 = 镜像（朝左）；非主 = 原朝向（朝右）→ 都面向中央文字区
            IsPortraitVisible = portrait != null;
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

        /// <summary>继续（点屏幕任意处触发；选项出现时忽略——选项由选项按钮接收）</summary>
        public void OnClickContinue()
        {
            if (_areOptionsVisible) return;   // 旧 VM ExecuteClick 同款守卫
            DebugLogger.Log("[PlaybackDialog] 点击继续 → 播放下一步");
            Close();
        }

        /// <summary>选项按钮点击（参数 = 被点按钮的数据源 StoryOptionVM——确认协议用日志验证；StoryOptionVM 无 Text getter，日志走 Identifier）</summary>
        public void ExecuteOption(StoryOptionVM opt)
        {
            DebugLogger.Log($"[PlaybackDialog] 选项点击 = {(opt?.Identifier ?? "(无id)")}");
            if (opt == null) return;
            Close();
        }
    }
}
