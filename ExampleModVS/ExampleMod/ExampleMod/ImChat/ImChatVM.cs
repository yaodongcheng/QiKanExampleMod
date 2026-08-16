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

    /// <summary>缩略模式频道下拉项 VM（原版 SelectorItemVM 形态：StringItem + CanBeSelected + Hint）。
    /// StringItem = 频道标题 + 未读数（微信会话列表语义）；选中走原版 Radio 机制（ListPanel SelectEvent
    /// → DropdownWidget → SelectedIndex 双向绑定）。</summary>
    public class ImChannelOptionVM : ViewModel
    {
        private readonly ImConversation _conv;
        private string _stringItem = "";

        public string ConversationId => _conv?.Id;

        [DataSourceProperty]
        public string StringItem
        {
            get => _stringItem;
            set { if (_stringItem != value) { _stringItem = value; OnPropertyChangedWithValue(value, nameof(StringItem)); } }
        }

        /// <summary>可选（原版项模板 IsEnabled/@CanBeSelected；本项目恒可选）。</summary>
        [DataSourceProperty]
        public bool CanBeSelected => true;

        /// <summary>选中态（Radio 高亮；RefreshChannelOptions 维护——当前选中频道 true，其余 false）。</summary>
        private bool _isSelected;

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChangedWithValue(value, nameof(IsSelected)); } }
        }

        /// <summary>悬停提示（原版项模板 HintWidget；无提示 = null）。</summary>
        [DataSourceProperty]
        public object Hint => null;

        public ImChannelOptionVM(ImConversation conv)
        {
            _conv = conv;
        }

        /// <summary>🔴 2026-08-15（实机日志取证：Radio SelectEvent 链路首次选择后失效）：
        /// 项点击直连切会话（Command.Click 绑定，绕开 SelectEvent/SelectedIndex 绑定链路）；
        /// Radio 状态仅作视觉（选中高亮由 RefreshChannelOptions 的 SelectedIndex 同步驱动）。
        /// 🔴 2026-08-15：切会话后收起列表（原版下拉「选中即收起」行为）。</summary>
        public void ExecuteSelect()
        {
            DebugLogger.Log($"[CompactSelect] 项点击: {ConversationId}");
            if (_conv != null)
            {
                ImChatView.SelectConversation(_conv);
                ImChatView.CloseChannelList();
            }
        }
    }

    /// <summary>🔴 2026-08-15（缩略模式）：频道下拉的数据源（SelectorVM 形态，供原版
    /// AnimatedDropdownWidget 消费——用户提示：原版 Options 的 Control Block Direction 选项用的
    /// Standard.DropdownWithHorizontalControl 就是这套：ItemList + SelectedIndex + 箭头命令）。
    /// 项数据 = <see cref="ImChannelOptionVM"/>；选中回调走 ImChatView（切会话 + 收起）。</summary>
    public class ImChannelSelectorVM : ViewModel
    {
        public MBBindingList<ImChannelOptionVM> ItemList { get; } = new MBBindingList<ImChannelOptionVM>();

        private int _selectedIndex = -1;

        /// <summary>选中项索引（下拉控件双向绑定；变更 → 切会话）。</summary>
        [DataSourceProperty]
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (_selectedIndex != value)
                {
                    _selectedIndex = value;
                    OnPropertyChangedWithValue(value, nameof(SelectedIndex));
                    if (value >= 0)
                        ImChatView.SelectChannelByIndex(value);
                }
            }
        }

        /// <summary>🔴 2026-08-15（绑定作用域修复）：频道列表显隐。属性必须长在本类——
        /// XML 中频道列表/中心按钮都在 `DataSource="{ChannelSelector}"` 作用域内，
        /// 绑在根 ImChatVM 上解析不到（作用域内不回落根 VM，同 HasCompactAnchor 坑）。</summary>
        private bool _isChannelListOpen;

        [DataSourceProperty]
        public bool IsChannelListOpen
        {
            get => _isChannelListOpen;
            set { if (_isChannelListOpen != value) { _isChannelListOpen = value; OnPropertyChangedWithValue(value, nameof(IsChannelListOpen)); } }
        }

        /// <summary>🔴 2026-08-15（绑定作用域修复）：中心按钮文本（选中频道标题 + 未读数；
        /// RefreshChannelOptions 维护——列表不再由原版控件刷新文本，改 VM 驱动）。</summary>
        private string _selectedChannelText = "";

        [DataSourceProperty]
        public string SelectedChannelText
        {
            get => _selectedChannelText;
            set { if (_selectedChannelText != value) { _selectedChannelText = value; OnPropertyChangedWithValue(value, nameof(SelectedChannelText)); } }
        }

        /// <summary>🔴 2026-08-15：频道列表高度（随项数自适应，项数×34+16 钳制 [60, 348]；
        /// 防 3 项也留大空白）。</summary>
        private float _channelListHeight = 120f;

        [DataSourceProperty]
        public float ChannelListHeight
        {
            get => _channelListHeight;
            set { if (MathF.Abs(_channelListHeight - value) > 0.5f) { _channelListHeight = value; OnPropertyChangedWithValue(value, nameof(ChannelListHeight)); } }
        }

        /// <summary>左箭头：上一个频道（原版下拉三件套按钮命令）。</summary>
        public void ExecuteSelectPreviousItem() => ImChatView.SelectPreviousChannel();

        /// <summary>右箭头：下一个频道（原版下拉三件套按钮命令）。</summary>
        public void ExecuteSelectNextItem() => ImChatView.SelectNextChannel();

        /// <summary>🔴 2026-08-15（下拉点不开修复）：中心按钮直连 VM 显隐（弃用原版控件点击机制）。</summary>
        public void ExecuteToggleChannelList() => ImChatView.ToggleChannelList();
    }

    /// <summary>
    /// 🔴 2026-08-12（用户裁定：计划模式按钮 = 通用交互结构）：决策卡片通用按钮的数据项。
    /// 计划卡片（自审/重拟/同意/拒绝/中止）与提议卡片（同意/拒绝）共用同一按钮行——
    /// XML 按钮行 DataSource="{CardButtons}" + ItemTemplate，每按钮 Command.Click="Execute" 绑本类。
    /// </summary>
    public class ImButtonVM : ViewModel
    {
        private readonly string _text;
        private readonly bool _isEnabled;
        private readonly Action _onClick;

        public ImButtonVM(string text, Action onClick, bool isEnabled = true)
        {
            _text = text ?? "";
            _onClick = onClick;
            _isEnabled = isEnabled;
        }

        [DataSourceProperty]
        public string Text => _text;

        /// <summary>置灰（自审在途时禁点等）。</summary>
        [DataSourceProperty]
        public bool IsEnabled => _isEnabled;

        public void Execute() => _onClick?.Invoke();
    }

    /// <summary>消息气泡 VM。他人左对齐 / 自己右对齐（IsSelf/IsNotSelf 双份互斥，规避对齐枚举绑定）。
    /// 🔴 2026-08-12（用户裁定：决策卡片统一）：卡片气泡分支（ShowCardBubble）= 计划卡片/生成中占位/
    /// 讲解消息/NPC 提议/闲聊动作卡片共用的 NPC 自述形态，按钮行数据驱动（CardButtons）。
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

        /// <summary>🔴 2026-08-12（队伍频道在场标记）：消息流显示名——队伍/家族频道里 Hero 消息在名字旁加
        /// 括号标记（动态计算，渲染时实时反映 NPC 当前状态——不写进 SenderName 存档快照，
        /// 读档/离场后标记自动跟随现状）。玩家消息/私聊/群聊/附近频道原样。
        /// 🔴 2026-08-13（用户裁定）：在场 → 实际距离「（xx米外）」；不在场按身份分级
        /// （DescribeAwayLocation：随从 → 城外；家族成员 → 定居点名/远处；未知 → 他处）。</summary>
        [DataSourceProperty]
        public string DisplaySenderName
        {
            get
            {
                if (_msg == null) return "";
                bool isGroup = _msg.ConvId == ImChatStore.ChannelParty || _msg.ConvId == ImChatStore.ChannelClan;
                if (isGroup && !_msg.IsSelf && !string.IsNullOrEmpty(_msg.SenderHeroId))
                {
                    var dist = ImChatManager.GetMissionDistanceMeters(_msg.SenderHeroId);
                    if (dist.HasValue)
                    {
                        int meters = MathF.Ceiling(dist.Value);
                        if (meters < 1) meters = 1;
                        // 本地化：IM 消息发送者距离标签（铁律 13 走 LWN_im_sender_dist）
                        return LWNTextHelper.ResolveCompound("LWN_im_sender_dist",
                            "{NAME} ({DIST} m away)", ("NAME", _msg.SenderName), ("DIST", meters.ToString()));
                    }
                    // 不在场：随从 → 城外；家族成员 → 定居点名/远处；未知 → 他处
                    string away = ImChatManager.DescribeAwayLocation(_msg.SenderHeroId);
                    return $"{_msg.SenderName}（{away}）";
                }
                return _msg.SenderName;
            }
        }

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

        /// <summary>🔴 2026-08-12（模板 NPC 密信 @染色）：显示用内容——玩家自己的 nearby 普通文本消息，
        /// 开头 @提及前缀（@名字 #编号）染蓝（富文本 span style="Mention"，微信同款）；
        /// 其余消息原文返回（NPC 台词/系统/卡片不染，防误染）。预览/通知/LLM 上下文继续用原文 Content，
        /// 富文本标签零污染。</summary>
        [DataSourceProperty]
        public string DisplayContent
        {
            get
            {
                if (_msg == null) return "";
                if (_msg.IsSelf && _msg.Kind == ImMessageKind.Text && _msg.ConvId == NearbyFeed.ChannelId)
                    return NearbyFeed.HighlightMention(_msg.Content ?? "");
                return _msg.Content ?? "";
            }
        }

        [DataSourceProperty]
        public bool IsSelf => _msg != null && _msg.IsSelf;

        /// <summary>🔴 2026-08-15（缩略模式）：紧凑单行正文（截断 ~48 字 + …，缩略面板消息区用）。
        /// 用原文 Content（不带富文本标签），普通 TextWidget 渲染安全；完整内容在完整模式看。</summary>
        [DataSourceProperty]
        public string CompactContent
        {
            get
            {
                string c = _msg?.Content ?? "";
                if (c.Length > 48) c = c.Substring(0, 48) + "…";
                return c;
            }
        }

        [DataSourceProperty]
        public bool IsNotSelf => _msg != null && !_msg.IsSelf;

        /// <summary>他人气泡显示：普通文本消息（非自己 且 非系统 且 非卡片消息——卡片消息走卡片气泡分支）。
        /// 🔴 2026-08-12（用户裁定：卡片融入 NPC 气泡）：生成中占位/计划卡片/讲解消息（链消息）/
        /// NPC 提议/闲聊动作卡片/needPlan 建议消息 统一归「卡片气泡」分支（ShowCardBubble）渲染，不再走普通气泡。
        /// 🔴 2026-08-13：宾语确认消息（模板 NPC 目标）同归卡片气泡分支（按钮行挂底部）。</summary>
        [DataSourceProperty]
        public bool ShowOtherBubble => IsNotSelf && !IsSystem && !IsPlanCard && !IsGenerating && !IsPlanChainMessage && !IsProposal && !IsPlanSuggest && !IsTargetConfirm && !IsAskPlayerCard;

        /// <summary>自己气泡显示：是自己 且 非系统/计划卡片/生成中占位/提议（旧格式卡片 SenderHeroId="player"
        /// 走 IsLegacyPlanCard 旧居中卡片控件兜底，不能走气泡分支）。</summary>
        [DataSourceProperty]
        public bool ShowSelfBubble => IsSelf && !IsSystem && !IsPlanCard && !IsGenerating && !IsProposal;

        [DataSourceProperty]
        public bool IsSystem => _msg != null && _msg.IsSystem;

        [DataSourceProperty]
        public bool IsPlanCard => _msg != null && _msg.IsPlanCard;

        /// <summary>🔴 2026-08-12：计划链消息（讲解消息 = 带 ChainId 的 NPC 文本）——卡片气泡分支渲染。</summary>
        [DataSourceProperty]
        public bool IsPlanChainMessage => _msg != null && _msg.IsPlanChainMessage;

        /// <summary>🔴 2026-08-12（合并闲聊/计划模式）：needPlan 建议消息（NPC 回复底部挂「制定计划/先不用」）——
        /// 卡片气泡分支渲染（通用消息底部按钮结构，用户裁定不用计划卡片）。</summary>
        [DataSourceProperty]
        public bool IsPlanSuggest => _msg != null && _msg.IsPlanSuggest;

        /// <summary>🔴 2026-08-13（模板 NPC 目标确认）：宾语确认消息（底部候选按钮行，用户裁定无新卡片）——
        /// 卡片气泡分支渲染。</summary>
        [DataSourceProperty]
        public bool IsTargetConfirm => _msg != null && _msg.IsTargetConfirm;

        /// <summary>🔴 2026-08-15（ask_player 询问步骤）：执行中计划向玩家提问的密信决策卡
        ///（按钮 = 撤退/强制执行，点击回投事件）——与 IsPlanSuggest/IsTargetConfirm 同构
        ///（卡片气泡分支 + 通用按钮行）。</summary>
        [DataSourceProperty]
        public bool IsAskPlayerCard => _msg != null && _msg.IsAskPlayerCard;

        /// <summary>🔴 2026-08-12（用户裁定：卡片融入 NPC 气泡 + 决策卡片统一）：卡片气泡分支——
        /// NPC 自述形态（名字行 + 正文 + 通用按钮行）：计划卡片 / 生成中占位 / 讲解消息（链消息）/
        /// NPC 提议 / 闲聊动作卡片 / needPlan 建议消息 六类共用。
        /// 旧格式（SenderHeroId=player）IsSelf → 走 IsLegacyPlanCard 旧居中卡片控件兜底。
        /// 🔴 2026-08-13：宾语确认消息同归此分支。</summary>
        [DataSourceProperty]
        public bool ShowCardBubble => IsNotSelf && !IsSystem && (IsPlanCard || IsGenerating || IsPlanChainMessage || IsProposal || IsPlanSuggest || IsTargetConfirm || IsAskPlayerCard);

        /// <summary>🔴 2026-08-12：旧格式计划卡片/生成中占位（SenderHeroId=player）——旧居中卡片控件渲染兜底
        ///（旧存档兼容；新消息一律走卡片气泡分支）。</summary>
        [DataSourceProperty]
        public bool IsLegacyPlanCard => _msg != null && (_msg.IsPlanCard || _msg.IsGenerating) && _msg.IsSelf;

        // ── 🔴 2026-08-10（Q4）：NPC 主动提议（Proposal 消息 + 批准/拒绝按钮）──

        [DataSourceProperty]
        public bool IsProposal => _msg != null && _msg.IsProposal;

        // ── 🔴 2026-08-12（用户裁定：决策卡片统一）：卡片按钮锚点 + 数据驱动按钮行 ──

        /// <summary>按钮行显示标记（ImChatView.UpdateCardAnchors 每次刷新重算）：本消息是
        /// 会话内「最新可操作卡片」的锚点位置——计划链 = 链内最新一条；提议 = 卡片自身。
        /// 旧卡片按钮行 IsVisible=false（视觉保留、不可点），与旧 IsLatestProposal 同款纪律。</summary>
        private bool _isCardAnchor;

        [DataSourceProperty]
        public bool IsCardAnchor
        {
            get => _isCardAnchor;
            set
            {
                if (_isCardAnchor != value)
                {
                    _isCardAnchor = value;
                    OnPropertyChangedWithValue(value, nameof(IsCardAnchor));
                    // 🔴 2026-08-13：按钮行组合可见性联动（横排 = 锚点 && 非竖排；竖排 = 锚点 && 竖排）
                    OnPropertyChanged(nameof(IsHorizontalButtons));
                    OnPropertyChanged(nameof(IsVerticalButtonsVisible));
                }
            }
        }

        /// <summary>决策卡片按钮行（数据驱动）：计划卡片 = 自审/重拟?/同意/拒绝（执行中=中止）；
        /// 提议卡片 = 同意/拒绝。按钮数据按 AnchorCard 状态重建（RebuildCardButtons）。
        /// 🔴 2026-08-13（双面板数据互斥）：横排/竖排各绑独立集合——竖排模式时本集合清空，
        /// 按钮全部移入 VerticalCardButtons（即使 IsVisible 绑定失效，空集合也渲染不出按钮）。</summary>
        public MBBindingList<ImButtonVM> CardButtons { get; } = new MBBindingList<ImButtonVM>();

        /// <summary>🔴 2026-08-13（长文本按钮竖排）：竖排按钮行的独立数据源（与 CardButtons 互斥——
        /// 竖排模式按钮移入本集合，横排模式清空）。竖排面板 DataSource 绑本集合。</summary>
        public MBBindingList<ImButtonVM> VerticalCardButtons { get; } = new MBBindingList<ImButtonVM>();

        /// <summary>🔴 2026-08-13（用户裁定：长文本按钮竖排）：按钮行布局自适应——任一按钮文本过长
        /// （&gt; 6 字，固定宽 96 @ FontSize16 中文字宽 16px 放不下）→ 竖排（XML 双面板互斥显隐，
        /// 竖排按钮 CoverChildren 自适应宽度）。宾语确认候选按钮（「① 右侧约10米」等）触发竖排。</summary>
        private const int VerticalButtonTextThreshold = 6;

        private bool _isVerticalButtons;

        /// <summary>按钮行竖排判定：任一按钮文本超长 → true（横排固定宽 96 会截断文本）。</summary>
        [DataSourceProperty]
        public bool IsVerticalButtons
        {
            get => _isVerticalButtons;
            private set
            {
                if (_isVerticalButtons != value)
                {
                    _isVerticalButtons = value;
                    OnPropertyChangedWithValue(value, nameof(IsVerticalButtons));
                    // 组合可见性联动（横排 = 锚点 && 非竖排；竖排 = 锚点 && 竖排）
                    OnPropertyChanged(nameof(IsHorizontalButtons));
                    OnPropertyChanged(nameof(IsVerticalButtonsVisible));
                }
            }
        }

        /// <summary>按钮行可见性·横排（🔴 仅锚点消息 && 短文本；与竖排互斥）。</summary>
        [DataSourceProperty]
        public bool IsHorizontalButtons => IsCardAnchor && !IsVerticalButtons;

        /// <summary>按钮行可见性·竖排（🔴 仅锚点消息 && 长文本；与横排互斥）。</summary>
        [DataSourceProperty]
        public bool IsVerticalButtonsVisible => IsCardAnchor && IsVerticalButtons;

        /// <summary>🔴 2026-08-12：按锚点卡片种类/状态重建按钮行（待批/执行中/已了结 三态）。
        /// 状态变动的入口（AnchorCard 变更 / 自审回调 / 讲解完成）统一调 NotifyPlanState → 这里。
        /// 🔴 2026-08-13：重建末尾做布局判定——任一按钮文本超长 → 按钮全部移入 VerticalCardButtons
        /// （双面板数据互斥：横排/竖排集合二选一，另一个为空——即使 IsVisible 绑定失效也不渲染）。</summary>
        public void RebuildCardButtons()
        {
            CardButtons.Clear();
            VerticalCardButtons.Clear();
            var anchor = AnchorCard;
            // 🔴 2026-08-15（按钮不显示调试，实机）：重建入口日志——锚点种类决定走哪个分支。
            if (anchor != null)
                DebugLogger.Log($"[CardButtons] 重建入口: anchor=kind={anchor.Kind} suggest={anchor.IsPlanSuggest} resolved={anchor.IsSuggestionResolved} exec={anchor.ExecutorId ?? "空"}");
            if (anchor == null) { IsVerticalButtons = false; return; }
            if (anchor.IsPlanCard) { RebuildPlanCardButtons(anchor); }
            else if (anchor.IsProposal) { RebuildProposalButtons(anchor); }
            else if (anchor.IsPlanSuggest) { RebuildSuggestionButtons(anchor); }
            else if (anchor.IsTargetConfirm) { RebuildTargetConfirmButtons(anchor); }
            else if (anchor.IsAskPlayerCard) { RebuildAskPlayerButtons(anchor); }
            // 布局判定：任一按钮文本超长（> 6 字，固定宽 96 @ FontSize16 放不下）→ 竖排
            bool anyLong = false;
            for (int i = 0; i < CardButtons.Count; i++)
            {
                var t = CardButtons[i]?.Text;
                if (!string.IsNullOrEmpty(t) && t.Length > VerticalButtonTextThreshold)
                {
                    anyLong = true;
                    break;
                }
            }
            if (anyLong)
            {
                // 竖排模式：按钮移入竖排集合，横排集合清空（数据互斥，免疫 IsVisible 绑定失效）
                foreach (var b in CardButtons)
                    VerticalCardButtons.Add(b);
                CardButtons.Clear();
            }
            IsVerticalButtons = anyLong;
            // 🔴 2026-08-15（按钮不显示根因修复二重保险）：按钮构建完成后强制广播可见性——
            // 构建期间 IsCardAnchor 可能尚未就绪（UpdateCardAnchors 旧顺序 AnchorCard 先于 IsCardAnchor），
            // 构建后重新广播让 UI 用最终状态重评估按钮行（实机日志 08:38:42.963 IsHorizontalButtons=False）。
            OnPropertyChanged(nameof(IsHorizontalButtons));
            OnPropertyChanged(nameof(IsVerticalButtonsVisible));
        }

        /// <summary>计划卡片按钮（与原硬编码按钮同语义：待批 = 自审/重拟?/同意/拒绝；执行中 = 中止）。</summary>
        private void RebuildPlanCardButtons(ImMessage card)
        {
            if (string.IsNullOrEmpty(card.ExecutorId))
            {
                // 自审：始终有（计划自审 → 自审中…）；重拟仅讲解自查发现问题时出现
                CardButtons.Add(new ImButtonVM(DetailToggleText, ExecuteToggleDetail, isEnabled: CanToggleDetail));
                if (CanRegenerate) CardButtons.Add(new ImButtonVM(RegenerateText, ExecuteRegenerate));
                if (CanApprove) CardButtons.Add(new ImButtonVM(ApproveText, ExecuteApprove));
                if (CanReject) CardButtons.Add(new ImButtonVM(RejectText, ExecuteReject));
            }
            else if (ImCommandFlow.IsExecuting(card))
            {
                CardButtons.Add(new ImButtonVM(AbortText, ExecuteAbort));
            }
        }

        /// <summary>提议卡片按钮（NPC 主动提议 / 闲聊动作卡片 共用）：同意/拒绝 —— 与计划卡片同构。</summary>
        private void RebuildProposalButtons(ImMessage card)
        {
            if (card.IsProposalResolved) return;
            CardButtons.Add(new ImButtonVM(ApproveText, () => ImChatView.HandleProposal(card, approve: true)));
            CardButtons.Add(new ImButtonVM(RejectText, () => ImChatView.HandleProposal(card, approve: false)));
        }

        /// <summary>🔴 2026-08-12（合并闲聊/计划模式）：needPlan 建议消息按钮（制定计划/先不用）——
        /// NPC 回复消息底部通用按钮行。制定计划 → RequestCommand（Mission 计划管线 / Campaign 规则解析计划）；
        /// 先不用 → 了结回闲聊。</summary>
        private void RebuildSuggestionButtons(ImMessage card)
        {
            // 🔴 2026-08-15（按钮不显示调试，实机）：按钮构建前打印全部判定变量——
            // IsPlanSuggest（打标是否命中本消息）/ IsSuggestionResolved（是否已了结，了结 = 无按钮）/
            // ExecutorId（非空即已解决）/ 两个按钮文案解析结果（本地化 key 是否命中）。
            if (card != null)
            {
                DebugLogger.Log($"[SuggestBtn] 构建判定: IsPlanSuggest={card.IsPlanSuggest} Resolved={card.IsSuggestionResolved} ExecutorId={card.ExecutorId ?? "空"} 按钮文案=「{MakePlanText}」「{SkipText}」");
            }
            if (card.IsSuggestionResolved) return;
            CardButtons.Add(new ImButtonVM(MakePlanText, () => ImChatView.HandleSuggestion(card, makePlan: true)));
            CardButtons.Add(new ImButtonVM(SkipText, () => ImChatView.HandleSuggestion(card, makePlan: false)));
            DebugLogger.Log($"[SuggestBtn] 已构建 2 按钮 → CardButtons={CardButtons.Count} VerticalCardButtons={VerticalCardButtons.Count} IsCardAnchor={IsCardAnchor} IsHorizontalButtons={IsHorizontalButtons} IsVerticalButtonsVisible={IsVerticalButtonsVisible}");
        }

        /// <summary>🔴 2026-08-13（模板 NPC 目标确认，用户裁定：无新卡片）：宾语确认消息底部按钮行——
        /// 每候选一个按钮（"① 右侧约10米"），点选 → ImChatView.HandleTargetConfirm → 投递常规同意/拒绝卡。
        /// 已选定（TargetConfirmIndex 非空）→ 无按钮（常规卡接管）。</summary>
        private void RebuildTargetConfirmButtons(ImMessage card)
        {
            if (card.IsTargetConfirmResolved) return;
            var labels = card.TargetConfirmLabels;
            if (labels == null) return;
            for (int i = 0; i < labels.Count; i++)
            {
                int idx = i;
                string label = labels[i];
                CardButtons.Add(new ImButtonVM(label, () => ImChatView.HandleTargetConfirm(card, idx)));
            }
        }

        /// <summary>🔴 2026-08-15（ask_player 询问步骤）：密信决策卡按钮行——每选项一个按钮
        ///（撤退/强制执行），点选 → ImChatView.HandleAskPlayerOption（事件回投执行器）。
        /// 已点选（ExecutorId 非空）→ 无按钮。</summary>
        private void RebuildAskPlayerButtons(ImMessage card)
        {
            if (card.IsAskPlayerCardResolved) return;
            var options = card.AskPlayerOptions;
            if (options == null) return;
            foreach (var opt in options)
            {
                if (opt == null || string.IsNullOrEmpty(opt.EventType)) continue;
                string evt = opt.EventType;
                CardButtons.Add(new ImButtonVM(opt.Label ?? "", () => ImChatView.HandleAskPlayerOption(card, evt)));
            }
        }

        // 建议按钮文案（🔴 2026-08-12，本地化）
        [DataSourceProperty]
        // 建议按钮：制定计划
        public string MakePlanText => LWNTextHelper.ResolveText("LWN_im_btn_make_plan", "Make a plan");

        // 建议按钮：先不用
        [DataSourceProperty]
        // 建议按钮：先不用
        public string SkipText => LWNTextHelper.ResolveText("LWN_im_btn_skip", "Not now");

        [DataSourceProperty]
        public string PlanSummary => _msg?.PlanSummary ?? "";

        // ── 🔴 2026-08-10（im-command-action-upgrade.md Q2/Q3/§3.3）：生成中占位 / 修改版 / 详情 ──
        // 🔴 2026-08-12（用户裁定：卡片融入 NPC 气泡 + 按钮锚点跟随）：所有按钮状态改读 AnchorCard——
        // 计划卡片消息 = 自身；讲解消息 = 所属卡片（按钮渲染在锚点消息下方，数据仍在卡片上）。

        /// <summary>链锚点卡（ImChatView.UpdateCardAnchors 每次刷新重算）：按钮状态的数据源。
        /// 计划卡片消息 → 自身；讲解消息（链消息）→ 所属卡片；提议卡片 → 自身；普通消息 → null（无按钮）。</summary>
        public ImMessage AnchorCard
        {
            get => _anchorCard;
            set
            {
                if (_anchorCard != value)
                {
                    _anchorCard = value;
                    // 按钮状态全是 AnchorCard 的计算属性——变更即全量通知 + 按钮行重建（含增量追加路径）
                    NotifyPlanState();
                }
            }
        }
        private ImMessage _anchorCard;

        /// <summary>生成中占位行：NPC 思考气泡（🔴 2026-08-12：删进度条，文案与输入栏「正在输入」统一；
        /// 名字行保留——谁在思考要可见，正文纯「正在思考中…」无名字，不冗余）。</summary>
        [DataSourceProperty]
        public bool IsGenerating => _msg != null && _msg.IsGenerating;

        /// <summary>生成中文案（「{NAME}正在输入…」，与输入栏正在输入同款；旧格式占位行渲染用）。</summary>
        [DataSourceProperty]
        public string GenerateText => _msg?.GenerateText ?? "";

        /// <summary>修改可用（Q2）：卡片待批或执行中，且修改额度未用尽（≤2）。</summary>
        [DataSourceProperty]
        public bool CanModify => AnchorCard != null && AnchorCard.IsPlanCard
            && AnchorCard.PlanModifyCount < ImCommandFlow.MaxModifyCount
            && (string.IsNullOrEmpty(AnchorCard.ExecutorId) || ImCommandFlow.IsExecuting(AnchorCard));

        /// <summary>「修改版 vN」徽标（Q2）。</summary>
        [DataSourceProperty]
        public bool IsModifiedPlan => AnchorCard != null && AnchorCard.IsModifiedPlan;

        /// <summary>徽标文本（修改版 v{N}）。</summary>
        [DataSourceProperty]
        public string ModifiedBadgeText => AnchorCard != null && AnchorCard.PlanModifyCount > 0
            // 修改版徽标（Q2）
            ? LWNTextHelper.ResolveCompound("LWN_im_badge_modified", "Revised v{N}", ("N", AnchorCard.PlanModifyCount.ToString()))
            : "";

        /// <summary>自审在途（🔴 2026-08-12：标志活在卡片消息上 [JsonIgnore]——0.3s VM 重建保活；
        /// 锚点移到自审消息后仍正确显示「自审中…」）。</summary>
        [DataSourceProperty]
        public bool IsExplainPending => AnchorCard != null && AnchorCard.ExplainPending;

        /// <summary>自审按钮可点（生成中禁用）。</summary>
        [DataSourceProperty]
        public bool CanToggleDetail => !IsExplainPending;

        /// <summary>🔴 2026-08-12（用户裁定）：按钮 = 计划自审（讲解前自查的语义本名）——
        /// 生成中 → 「自审中…」；默认 → 「计划自审」（讲解一次后按钮保留：可重复自审；
        /// 自查发现问题 → 重拟按钮出现，LWN_im_btn_review 文案）。</summary>
        [DataSourceProperty]
        public string DetailToggleText => IsExplainPending
            // 自审中…
            ? LWNTextHelper.ResolveText("LWN_im_btn_reviewing", "Reviewing…")
            // 计划自审按钮
            : LWNTextHelper.ResolveText("LWN_im_btn_review", "Self-review");

        /// <summary>
        /// 计划自审（🔴 2026-08-11 用户裁定 → 2026-08-12 再裁定：按钮 = 确定性事件 → 执行者 NPC 口述自审
        /// （LLM 生成人话 → NPC 聊天消息 + 场景内冒泡；LLM 失败 → 用计划摘要口述，**绝不展示 JSON 详情**）。
        /// 🔴 2026-08-12：锚点移到自审消息后，按钮在自审消息上继续可用（再点 = 再自审一条，锚点再下移；
        /// 用户裁定：自审一次后按钮保留——自查发现问题 → 重拟按钮出现）。
        /// 回调由 ImCommandFlow.Tick 主线程执行（异步回包只入队，不在此线程碰 UI）。
        /// </summary>
        public void ExecuteToggleDetail()
        {
            var card = AnchorCard;
            if (card == null || !card.IsPlanCard) return;
            if (card.ExplainPending) return;                          // 讲解中禁重复点
            card.ExplainPending = true;
            // 🔴 2026-08-12：自审按钮文案「自审中…」+ 置灰 → 重建按钮行
            RebuildCardButtons();
            ImCommandFlow.RequestPlanExplain(card, ok =>
            {
                // 主线程回调（ImCommandFlow.Tick 消费讲解队列时执行）；降级已在管线内用摘要口述，无需展开 JSON
                card.ExplainPending = false;
                // 🔴 2026-08-12：锚点可能已移到讲解消息（新 VM 实例）——通知所有挂载本卡片的 VM
                ImChatView.NotifyPlanStateChanged(card);
            });
        }

        /// <summary>批准可用：计划卡片、尚未下发（无 ExecutorId）、有 Plan JSON。</summary>
        [DataSourceProperty]
        public bool CanApprove => AnchorCard != null && AnchorCard.IsPlanCard && string.IsNullOrEmpty(AnchorCard.ExecutorId) && !string.IsNullOrEmpty(AnchorCard.PlanJson);

        /// <summary>拒绝可用：计划卡片、尚未下发。</summary>
        [DataSourceProperty]
        public bool CanReject => AnchorCard != null && AnchorCard.IsPlanCard && string.IsNullOrEmpty(AnchorCard.ExecutorId);

        /// <summary>中止可用：已下发执行中（ExecutorId 非空且非了结态）。</summary>
        [DataSourceProperty]
        public bool CanAbort => AnchorCard != null && ImCommandFlow.IsExecuting(AnchorCard);

        // ── 计划卡片按钮文案（本地化）──
        // 计划卡片按钮：同意
        [DataSourceProperty]
        // 计划卡片按钮：同意
        public string ApproveText => LWNTextHelper.ResolveText("LWN_im_btn_approve", "Approve");

        // 计划卡片按钮：拒绝
        [DataSourceProperty]
        // 计划卡片按钮：拒绝
        public string RejectText => LWNTextHelper.ResolveText("LWN_im_btn_reject", "Reject");

        // 计划卡片按钮：中止
        [DataSourceProperty]
        // 计划卡片按钮：中止
        public string AbortText => LWNTextHelper.ResolveText("LWN_im_btn_abort", "Abort");

        // 计划卡片按钮：修改（Q2）
        [DataSourceProperty]
        // 计划卡片按钮：修改（Q2）
        public string ModifyText => LWNTextHelper.ResolveText("LWN_im_btn_modify", "Revise");

        // 计划卡片按钮：重拟（2026-08-12：二次校验发现问题 → 同命令重新生成）
        [DataSourceProperty]
        // 计划卡片按钮：重拟（2026-08-12）
        public string RegenerateText => LWNTextHelper.ResolveText("LWN_im_btn_regenerate", "Regenerate");

        /// <summary>重拟可用（🔴 2026-08-12 用户裁定）：仅当「讲解过且自查发现问题」才显示——
        /// 其他时候重拟没必要（有意见用输入框改，没问题直接同意）。讲解完成回调 OnPropertyChanged 联动。</summary>
        [DataSourceProperty]
        public bool CanRegenerate => AnchorCard != null && AnchorCard.IsPlanCard && string.IsNullOrEmpty(AnchorCard.ExecutorId)
            && AnchorCard.ReviewFoundIssue == true;

        public ImMessageVM(ImMessage msg)
        {
            _msg = msg;
        }

        /// <summary>🔴 2026-08-12：通知计划按钮状态刷新（AnchorCard 变更 / 讲解完成回调）+ 重建按钮行。
        /// 讲解回调时锚点可能已是讲解消息的新 VM——ImChatView.NotifyPlanStateChanged 对所有挂载 VM 调用。</summary>
        public void NotifyPlanState()
        {
            OnPropertyChanged(nameof(CanModify));
            OnPropertyChanged(nameof(IsModifiedPlan));
            OnPropertyChanged(nameof(ModifiedBadgeText));
            OnPropertyChanged(nameof(IsExplainPending));
            OnPropertyChanged(nameof(CanToggleDetail));
            OnPropertyChanged(nameof(DetailToggleText));
            OnPropertyChanged(nameof(CanApprove));
            OnPropertyChanged(nameof(CanReject));
            OnPropertyChanged(nameof(CanAbort));
            OnPropertyChanged(nameof(CanRegenerate));
            // 讲解完成 → 自查结果可能翻转重拟按钮显示/按钮文案（自审中→计划自审）→ 全量重建
            RebuildCardButtons();
        }

        public void ExecuteApprove() { if (AnchorCard != null) ImChatView.HandlePlanAction(AnchorCard, approve: true); }

        public void ExecuteReject() { if (AnchorCard != null) ImChatView.HandlePlanAction(AnchorCard, approve: false); }

        public void ExecuteAbort() { if (AnchorCard != null) ImChatView.HandlePlanAction(AnchorCard, approve: false, abort: true); }

        /// <summary>🔴 2026-08-12（重拟按钮）：同命令重新生成（二次校验发现问题时的出口）。</summary>
        public void ExecuteRegenerate() { if (AnchorCard != null) ImChatView.HandleRegenerate(AnchorCard); }
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
        private bool _isTypingVisible;
        private string _modeStatusText = "";
        private string _placeholderText = "";
        private string _sendText = "";
        private bool _isEmpty;
        private string _emptyHint = "";
        private bool _hasNewMessageHint;

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
        /// 随模式联动：闲聊="输入消息…" / 密令="下达密令…"（让玩家在输入区也感知当前模式）。
        /// 🔴 2026-08-12：待批计划卡片存在时 = 「修改计划：…」（发送即修改，ImChatView.RefreshDynamic 联动）。</summary>
        [DataSourceProperty]
        public string PlaceholderText
        {
            get => _placeholderText;
            set { if (_placeholderText != value) { _placeholderText = value; OnPropertyChangedWithValue(value, nameof(PlaceholderText)); } }
        }

        /// <summary>发送按钮可用（非空输入才可发，微信置灰语义）。</summary>
        [DataSourceProperty]
        public bool CanSend => !string.IsNullOrWhiteSpace(InputText);

        /// <summary>「XX 正在思考回复…」（🔴 五轮：移入标题带显示，仅私聊回复在途时可见；空 = 隐藏）。</summary>
        [DataSourceProperty]
        public string TypingText
        {
            get => _typingText;
            set { if (_typingText != value) { _typingText = value; OnPropertyChangedWithValue(value, nameof(TypingText)); } }
        }

        /// <summary>标题带「正在思考回复」可见性（私聊 && 回复在途；群聊不显示——用户反馈五轮）。</summary>
        [DataSourceProperty]
        public bool IsTypingVisible
        {
            get => _isTypingVisible;
            set { if (_isTypingVisible != value) { _isTypingVisible = value; OnPropertyChangedWithValue(value, nameof(IsTypingVisible)); } }
        }

        /// <summary>模式状态静态文本（🔴 2026-08-12 合并闲聊/计划模式：改为从会话状态派生的指示文本——
        /// 闲聊/计划生成中/计划待批准/计划执行中；不再可手动切换）。</summary>
        [DataSourceProperty]
        public string ModeStatusText
        {
            get => _modeStatusText;
            set { if (_modeStatusText != value) { _modeStatusText = value; OnPropertyChangedWithValue(value, nameof(ModeStatusText)); } }
        }

        /// <summary>发送按钮文案（随计划状态联动：待批计划卡片存在 = Submit（发送即修改），否则 Send）。</summary>
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

        /// <summary>「有新消息」提示条可见性（🔴 八轮：翻历史时新消息到达 → 输入区分隔线上方提示；在底部不提示）。</summary>
        [DataSourceProperty]
        public bool HasNewMessageHint
        {
            get => _hasNewMessageHint;
            set { if (_hasNewMessageHint != value) { _hasNewMessageHint = value; OnPropertyChangedWithValue(value, nameof(HasNewMessageHint)); } }
        }

        /// <summary>「有新消息」提示条文案。</summary>
        // 新消息提示条文案
        [DataSourceProperty]
        // 新消息提示条文案
        public string NewMessageHintText => LWNTextHelper.ResolveText("LWN_im_new_message", "New messages");

        /// <summary>空会话引导文案。</summary>
        [DataSourceProperty]
        public string EmptyHint
        {
            get => _emptyHint;
            set { if (_emptyHint != value) { _emptyHint = value; OnPropertyChangedWithValue(value, nameof(EmptyHint)); } }
        }

        public void ExecuteSend() => ImChatView.ExecuteSend();

        public void ExecuteClose() => ImChatView.Close();

        /// <summary>「有新消息」提示条点击：滚到消息流底部并清除提示。</summary>
        public void ExecuteNewMessageClick() => ImChatView.ExecuteNewMessageClick();

        // ── 🔴 2026-08-15（缩略模式）：频道下拉（原版 AnimatedDropdownWidget）+ 消息区 + 模式切换按钮 ──

        /// <summary>频道下拉数据源（原版 SelectorVM 形态；下拉控件双向绑定 SelectedIndex/ItemList）。
        /// 🔴 2026-08-15 用户提示：改原版 Standard.DropdownWithHorizontalControl 控件后，
        /// 展开/收起/选中高亮/自动收起全部由控件自身处理，VM 只供数据。</summary>
        public ImChannelSelectorVM ChannelSelector { get; } = new ImChannelSelectorVM();

        /// <summary>缩略模式行 A：未决锚点卡（含按钮行；实例 = _vm.Messages 中 IsCardAnchor 的同一实例，
        /// 广播路径天然覆盖——🔴 2026-08-15 审查 P1-2：独立新实例会漏 UpdateCardAnchors/NotifyPlanStateChanged 广播）。</summary>
        private ImMessageVM _compactAnchor;

        [DataSourceProperty]
        public ImMessageVM CompactAnchor
        {
            get => _compactAnchor;
            set { if (_compactAnchor != value) { _compactAnchor = value; OnPropertyChangedWithValue(value, nameof(CompactAnchor)); } }
        }

        private bool _hasCompactAnchor;

        [DataSourceProperty]
        public bool HasCompactAnchor
        {
            get => _hasCompactAnchor;
            set { if (_hasCompactAnchor != value) { _hasCompactAnchor = value; OnPropertyChangedWithValue(value, nameof(HasCompactAnchor)); } }
        }

        /// <summary>缩略模式行 B：最近 1~2 条消息迷你气泡（用户裁定 2026-08-15：有 2 条显示 2 条，
        /// 给回复提供基础上下文；锚点消息本身不在列表里——行 A 有完整形态）。
        /// 实例复用 _vm.Messages 的同一实例（P1-2 广播纪律同 CompactAnchor）。</summary>
        public MBBindingList<ImMessageVM> CompactLatestMessages { get; } = new MBBindingList<ImMessageVM>();

        private bool _hasCompactLatest;

        [DataSourceProperty]
        public bool HasCompactLatest
        {
            get => _hasCompactLatest;
            set { if (_hasCompactLatest != value) { _hasCompactLatest = value; OnPropertyChangedWithValue(value, nameof(HasCompactLatest)); } }
        }

        // 缩略按钮（完整模式标题带，关闭按钮左侧）
        [DataSourceProperty]
        // 缩略按钮文案
        public string CompactButtonText => LWNTextHelper.ResolveText("LWN_im_btn_compact", "Collapse");

        // 放大按钮（缩略面板右上角）
        [DataSourceProperty]
        // 放大按钮文案
        public string ExpandButtonText => LWNTextHelper.ResolveText("LWN_im_btn_expand", "Expand");

        /// <summary>完整模式标题带「缩略」按钮 → 切缩略模式。</summary>
        public void ExecuteToggleCompact() => ImChatView.ToggleCompact();

        /// <summary>缩略面板右上角「放大」按钮 → 切完整模式。</summary>
        public void ExecuteExpand() => ImChatView.ToggleExpand();
    }
}
