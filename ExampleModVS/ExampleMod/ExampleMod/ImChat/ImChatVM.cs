using System;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>左栏频道/私聊行 VM。点击切换会话（Command.Click 绑 <see cref="ExecuteSelect"/>）。
    /// 分组标题行（IsGroupHeader）：频道 / 私聊 两组的视觉分隔（UI 优化：混排时玩家无法区分）。</summary>
    public class ImChannelVM : ViewModel
    {
        private readonly ImConversation _conv;
        private string _headerTitle = "";
        private bool _isGroupHeader;
        private bool _isSelected;
        private string _unreadText = "";
        private bool _hasUnread;
        private string _subtitle = "";

        public string ConversationId => _conv?.Id;

        /// <summary>底层会话引用（最后消息预览刷新用）。</summary>
        public ImConversation Conversation => _conv;

        /// <summary>分组标题行工厂（不可点击）。</summary>
        public static ImChannelVM CreateHeader(string title) => new ImChannelVM(null) { _isGroupHeader = true, _headerTitle = title };

        [DataSourceProperty]
        public bool IsGroupHeader => _isGroupHeader;

        [DataSourceProperty]
        public bool IsNotGroupHeader => !_isGroupHeader;

        [DataSourceProperty]
        public string Title => _isGroupHeader ? _headerTitle : (_conv?.Title ?? "");

        /// <summary>副标题（群聊 = 成员数，私聊 = 空）。</summary>
        [DataSourceProperty]
        public string Subtitle
        {
            get => _subtitle;
            set { if (_subtitle != value) { _subtitle = value; OnPropertyChangedWithValue(value, nameof(Subtitle)); } }
        }

        /// <summary>未读徽标文本（空 = 不显示）。</summary>
        [DataSourceProperty]
        public string UnreadText
        {
            get => _unreadText;
            set { if (_unreadText != value) { _unreadText = value; OnPropertyChangedWithValue(value, nameof(UnreadText)); } }
        }

        [DataSourceProperty]
        public bool HasUnread
        {
            get => _hasUnread;
            set { if (_hasUnread != value) { _hasUnread = value; OnPropertyChangedWithValue(value, nameof(HasUnread)); } }
        }

        /// <summary>选中态（高亮行背景）。</summary>
        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChangedWithValue(value, nameof(IsSelected)); } }
        }

        /// <summary>行标题颜色：选中亮白 / 未选灰白（绑 Color 必须 8 位 hex 且初始合法）。</summary>
        [DataSourceProperty]
        public string TitleColor => IsSelected ? "#FFFFFFFF" : "#C8C8C8FF";

        public ImChannelVM(ImConversation conv)
        {
            _conv = conv;
        }

        public void RefreshUnread()
        {
            int unread = _conv == null ? 0 : ImChatStore.GetUnread(_conv.Id);
            HasUnread = unread > 0;
            UnreadText = unread > 0 ? unread.ToString() : "";
        }

        public void ExecuteSelect()
        {
            if (_conv != null)
                ImChatView.SelectConversation(_conv);
        }
    }

    /// <summary>消息气泡 VM。他人左对齐 / 自己右对齐（IsSelf/IsNotSelf 双份互斥，规避对齐枚举绑定）。
    /// PlanCard 分支：摘要 + 同意/拒绝/中止按钮（Command.Click 绑 ExecuteXxx）。
    /// 微信标准优化：消息时间小字（TimeText）+ 群聊发送者成员色（NameColor 按人哈希）。</summary>
    public class ImMessageVM : ViewModel
    {
        private readonly ImMessage _msg;

        /// <summary>成员色板（微信群聊式：按发送者哈希固定着色；中世纪柔和色调）。</summary>
        private static readonly string[] MemberColors =
        {
            "#E8C55AFF", "#55CC55FF", "#66AADDAA", "#E06055AA", "#CC88DDFF",
            "#5AD4C8AA", "#E8964AAA", "#AABB66AA",
        };

        public ImMessage Message => _msg;

        [DataSourceProperty]
        public string SenderName => _msg?.SenderName ?? "";

        /// <summary>发送者名字颜色：按 SenderHeroId 哈希取成员色（自己 = 亮白）。</summary>
        [DataSourceProperty]
        public string NameColor
        {
            get
            {
                if (_msg == null || _msg.IsSelf) return "#FFFFFFFF";
                string id = _msg.SenderHeroId ?? "";
                int hash = 0;
                foreach (char c in id) hash = hash * 31 + c;
                return MemberColors[(hash & 0x7FFFFFFF) % MemberColors.Length];
            }
        }

        /// <summary>消息相对时间（微信式：刚刚 / N 分钟前 / N 小时前 / N 天前）。</summary>
        [DataSourceProperty]
        public string TimeText => FormatRelativeTime(_msg?.TimeStamp ?? 0);

        /// <summary>相对时间格式化（微信式）。</summary>
        public static string FormatRelativeTime(double unixMs)
        {
            if (unixMs <= 0) return "";
            double diffSec = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - unixMs) / 1000.0;
            if (diffSec < 60)
                // 相对时间：刚刚
                return LWNTextHelper.ResolveText("LWN_im_time_justnow", "just now");
            if (diffSec < 3600)
                // 相对时间：N 分钟前
                return LWNTextHelper.ResolveCompound("LWN_im_time_minutes", "{N} min ago", ("N", ((long)(diffSec / 60)).ToString()));
            if (diffSec < 86400)
                // 相对时间：N 小时前
                return LWNTextHelper.ResolveCompound("LWN_im_time_hours", "{N} h ago", ("N", ((long)(diffSec / 3600)).ToString()));
            // 相对时间：N 天前
            return LWNTextHelper.ResolveCompound("LWN_im_time_days", "{N} d ago", ("N", ((long)(diffSec / 86400)).ToString()));
        }

        [DataSourceProperty]
        public string Content => _msg?.Content ?? "";

        [DataSourceProperty]
        public bool IsSelf => _msg != null && _msg.IsSelf;

        [DataSourceProperty]
        public bool IsNotSelf => _msg != null && !_msg.IsSelf;

        /// <summary>他人气泡显示：非自己 且 非系统/计划卡片（卡片与系统消息有独立分支，避免与气泡双渲染）。</summary>
        [DataSourceProperty]
        public bool ShowOtherBubble => IsNotSelf && !IsSystem && !IsPlanCard;

        /// <summary>自己气泡显示：是自己 且 非系统/计划卡片（SenderHeroId="player" 的卡片/系统消息不能走气泡分支）。</summary>
        [DataSourceProperty]
        public bool ShowSelfBubble => IsSelf && !IsSystem && !IsPlanCard;

        [DataSourceProperty]
        public bool IsSystem => _msg != null && _msg.IsSystem;

        [DataSourceProperty]
        public bool IsPlanCard => _msg != null && _msg.IsPlanCard;

        [DataSourceProperty]
        public string PlanSummary => _msg?.PlanSummary ?? "";

        /// <summary>批准可用：计划卡片、尚未下发（无 ExecutorId）、有 Plan JSON。</summary>
        [DataSourceProperty]
        public bool CanApprove => _msg != null && _msg.IsPlanCard && string.IsNullOrEmpty(_msg.ExecutorId) && !string.IsNullOrEmpty(_msg.PlanJson);

        /// <summary>拒绝可用：计划卡片、尚未下发。</summary>
        [DataSourceProperty]
        public bool CanReject => _msg != null && _msg.IsPlanCard && string.IsNullOrEmpty(_msg.ExecutorId);

        /// <summary>中止可用：已下发执行中（ExecutorId 非空且非了结态）。</summary>
        [DataSourceProperty]
        public bool CanAbort => _msg != null && ImCommandFlow.IsExecuting(_msg);

        // ── 计划卡片按钮文案（本地化）──
        [DataSourceProperty]
        // 计划卡片按钮：同意
        public string ApproveText => LWNTextHelper.ResolveText("LWN_im_btn_approve", "Approve");

        [DataSourceProperty]
        // 计划卡片按钮：拒绝
        public string RejectText => LWNTextHelper.ResolveText("LWN_im_btn_reject", "Reject");

        [DataSourceProperty]
        // 计划卡片按钮：中止
        public string AbortText => LWNTextHelper.ResolveText("LWN_im_btn_abort", "Abort");

        public ImMessageVM(ImMessage msg)
        {
            _msg = msg;
        }

        public void ExecuteApprove() => ImChatView.HandlePlanAction(_msg, approve: true);

        public void ExecuteReject() => ImChatView.HandlePlanAction(_msg, approve: false);

        public void ExecuteAbort() => ImChatView.HandlePlanAction(_msg, approve: false, abort: true);
    }

    /// <summary>
    /// IM 主 VM：左栏频道列表 + 右栏消息流 + 底部输入。命令方法被 ImChat.xml 的 Command.Click 绑定，
    /// 内部转发到静态 <see cref="ImChatView"/>（列表重建由 View 驱动，见 RefreshMessages 增量追加）。
    /// </summary>
    public class ImChatVM : ViewModel
    {
        public MBBindingList<ImChannelVM> ChannelList { get; } = new MBBindingList<ImChannelVM>();

        public MBBindingList<ImMessageVM> Messages { get; } = new MBBindingList<ImMessageVM>();

        private string _title = "";
        private string _inputText = "";
        private string _typingText = "";
        private string _commandModeLabel = "";
        private bool _isModeControlVisible;
        private bool _isChatModeActive = true;
        private bool _isCommandModeActive;
        private string _placeholderText = "";
        private string _sendText = "";
        private bool _isEmpty;
        private string _emptyHint = "";

        [DataSourceProperty]
        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChangedWithValue(value, nameof(Title)); } }
        }

        /// <summary>输入框文本（EditableTextWidget 双向绑定）。</summary>
        [DataSourceProperty]
        public string InputText
        {
            get => _inputText;
            set
            {
                if (_inputText != value)
                {
                    _inputText = value;
                    OnPropertyChangedWithValue(value, nameof(InputText));
                    // 发送按钮可用性联动（微信：空输入置灰）
                    OnPropertyChanged(nameof(CanSend));
                }
            }
        }

        /// <summary>输入框 placeholder（微信：「输入消息」灰字提示；DefaultSearchText 官方属性）。
        /// 随模式联动：闲聊="输入消息…" / 密令="下达密令…"（让玩家在输入区也感知当前模式）。</summary>
        [DataSourceProperty]
        public string PlaceholderText
        {
            get => _placeholderText;
            set { if (_placeholderText != value) { _placeholderText = value; OnPropertyChangedWithValue(value, nameof(PlaceholderText)); } }
        }

        /// <summary>发送按钮可用（非空输入才可发，微信置灰语义）。</summary>
        [DataSourceProperty]
        public bool CanSend => !string.IsNullOrWhiteSpace(InputText);

        /// <summary>「XX 正在输入…」（输入栏上方灰字，空 = 隐藏）。</summary>
        [DataSourceProperty]
        public string TypingText
        {
            get => _typingText;
            set { if (_typingText != value) { _typingText = value; OnPropertyChangedWithValue(value, nameof(TypingText)); } }
        }

        /// <summary>分段控件：闲聊段标签（静态）。</summary>
        [DataSourceProperty]
        // 模式段标签：闲聊
        public string ChatModeLabel => LWNTextHelper.ResolveText("LWN_im_mode_chat", "Chat");

        /// <summary>分段控件：密令段标签（Mission=密令 / Campaign 大地图=行军令，动态）。</summary>
        [DataSourceProperty]
        public string CommandModeLabel
        {
            get => _commandModeLabel;
            set { if (_commandModeLabel != value) { _commandModeLabel = value; OnPropertyChangedWithValue(value, nameof(CommandModeLabel)); } }
        }

        /// <summary>闲聊段选中态（金卡高亮；二段互斥）。</summary>
        [DataSourceProperty]
        public bool IsChatModeActive
        {
            get => _isChatModeActive;
            set { if (_isChatModeActive != value) { _isChatModeActive = value; OnPropertyChangedWithValue(value, nameof(IsChatModeActive)); } }
        }

        /// <summary>密令段选中态（金卡高亮；二段互斥）。</summary>
        [DataSourceProperty]
        public bool IsCommandModeActive
        {
            get => _isCommandModeActive;
            set { if (_isCommandModeActive != value) { _isCommandModeActive = value; OnPropertyChangedWithValue(value, nameof(IsCommandModeActive)); } }
        }

        /// <summary>分段控件可见性（密令可用会话 + Plot 总闸 + LLM 已配置；不可用时整个控件隐藏）。</summary>
        [DataSourceProperty]
        public bool IsModeControlVisible
        {
            get => _isModeControlVisible;
            set { if (_isModeControlVisible != value) { _isModeControlVisible = value; OnPropertyChangedWithValue(value, nameof(IsModeControlVisible)); } }
        }

        /// <summary>发送按钮文案（随模式联动：闲聊=Send / 密令=Order）。</summary>
        [DataSourceProperty]
        public string SendText
        {
            get => _sendText;
            set { if (_sendText != value) { _sendText = value; OnPropertyChangedWithValue(value, nameof(SendText)); } }
        }

        /// <summary>会话无消息（空状态引导显示）。</summary>
        [DataSourceProperty]
        public bool IsEmpty
        {
            get => _isEmpty;
            set { if (_isEmpty != value) { _isEmpty = value; OnPropertyChangedWithValue(value, nameof(IsEmpty)); } }
        }

        /// <summary>空会话引导文案。</summary>
        [DataSourceProperty]
        public string EmptyHint
        {
            get => _emptyHint;
            set { if (_emptyHint != value) { _emptyHint = value; OnPropertyChangedWithValue(value, nameof(EmptyHint)); } }
        }

        public void ExecuteSend() => ImChatView.ExecuteSend();

        public void ExecuteClose() => ImChatView.Close();

        /// <summary>分段控件：切到闲聊段（点击非当前段才有效）。</summary>
        public void ExecuteSwitchToChat() => ImChatView.ExecuteSwitchToChat();

        /// <summary>分段控件：切到密令段（含可用性检查）。</summary>
        public void ExecuteSwitchToCommand() => ImChatView.ExecuteSwitchToCommand();
    }
}
