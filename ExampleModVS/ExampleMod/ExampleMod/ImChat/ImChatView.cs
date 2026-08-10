using System;
using System.Linq;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// IM 静态 UI 管理器（NinjaNotification 同款：TopScreen.AddLayer，Mission/Campaign 通吃，需求 1）。
    /// - 打开/关闭/切换：热键（ModInput IM 玩法行）与通知点击；
    /// - 战斗/模态禁开（用户决策 4）：Mission 内 IsInteractionDisabled() + 系统弹窗检查；
    /// - Tick 驱动（ImChatMissionView / ImChatCampaignBehavior）：回复管线 + 0.3s 增量刷新；
    /// - 新消息通知：IM 关闭时 NinjaNotification 圆环（点击打开定位会话）。
    /// </summary>
    public static class ImChatView
    {
        private static GauntletLayer _layer;
        private static ImChatVM _vm;
#if !MB2_V1212
        private static GauntletMovieIdentifier _movie;
#else
        private static IGauntletMovie _movie;
#endif
        private static ImConversation _selected;
        private static float _refreshTimer;
        private static bool _subscribed;
        private static bool _welcomed;   // 首次打开引导提示（会话内一次）

        public static bool IsOpen => _layer != null;

        /// <summary>当前选中会话（命令模式/通知定位用）。</summary>
        public static ImConversation Selected => _selected;

        public static void EnsureSubscribed()
        {
            if (_subscribed) return;
            _subscribed = true;
            ImChatManager.MessageArrived += OnMessageArrived;
        }

        // ───────────────────────── 打开/关闭 ─────────────────────────

        /// <summary>Mission/大地图均可开（需求 1）；战斗中/系统弹窗中禁开（用户决策 4）。</summary>
        public static bool CanOpen()
        {
            try
            {
                if (Mission.Current != null && Settings.Instance.IsInteractionDisabled()) return false;
                if (ModInput.IsSystemModalActive()) return false;
                return ScreenManager.TopScreen != null;
            }
            catch { return false; }
        }

        public static void Toggle() => ToggleTo(null);

        /// <summary>打开并定位到指定会话（通知点击路径）。</summary>
        public static void ToggleTo(ImConversation selectConv)
        {
            if (IsOpen)
            {
                Close();
                return;
            }
            Open(selectConv);
        }

        public static void Open(ImConversation selectConv = null)
        {
            if (IsOpen || !CanOpen()) return;
            EnsureSubscribed();

            try
            {
                // 玩家体验完善（Q1f）：首次打开引导提示（玩家可能不知道热键/功能存在）
                if (!_welcomed)
                {
                    _welcomed = true;
                    // 首次打开引导（LWN_im_first_open）
                    string hint = LWNTextHelper.ResolveText("LWN_im_first_open",
                        "Messaging (IM) — talk to heroes across the land, or give orders to companions. Open/close with the configured key (default O).");
                    InformationManager.DisplayMessage(new InformationMessage(hint));
                }

                _vm = new ImChatVM();
                _layer = V.NewLayer(20, "ImChatLayer");
                // LoadMovie 字符串 = GUI/Prefabs/ImChat.xml 文件名（不带后缀）；返回类型跨版本不同（同上 #if）
                _movie = _layer.LoadMovie("ImChat", _vm);
                // 对话框级输入限制：键盘鼠标全给 IM（输入框打字 + 点击发送）
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                if (ScreenManager.TopScreen != null)
                    ScreenManager.TopScreen.AddLayer(_layer);

                SelectConversation(selectConv ?? BuildDefaultConversation());
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] Open 失败: {ex.Message}");
                Close();
            }
        }

        public static void Close()
        {
            if (_layer != null)
            {
                try
                {
                    if (_movie != null)
                    {
                        _layer.ReleaseMovie(_movie);
                        _movie = null;
                    }
                    if (ScreenManager.TopScreen != null)
                        ScreenManager.TopScreen.RemoveLayer(_layer);
                    _layer.InputRestrictions.ResetInputRestrictions();
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[ImChat] Close 失败: {ex.Message}");
                }
                _layer = null;
            }
            _vm = null;
            _selected = null;
        }

        // ───────────────────────── 会话选择与刷新 ─────────────────────────

        private static ImConversation BuildDefaultConversation() => ImChatManager.GetGroupConversation(ImConversationType.Party);

        public static void SelectConversation(ImConversation conv)
        {
            _selected = conv;
            if (conv != null)
                ImChatStore.ClearUnread(conv.Id);
            RefreshAll();
        }

        /// <summary>全量重建（打开/切会话时）。</summary>
        public static void RefreshAll()
        {
            if (_vm == null) return;
            RefreshChannels();
            RefreshTitle();
            RefreshMessages();
            RefreshDynamic();
        }

        private static void RefreshChannels()
        {
            if (_vm == null) return;
            _vm.ChannelList.Clear();

            // 分组标题：频道（队伍/家族/王国）
            // 分组标题：频道
            _vm.ChannelList.Add(ImChannelVM.CreateHeader(LWNTextHelper.ResolveText("LWN_im_group_channels", "Channels")));

            // 队伍频道（恒显示）
            AddChannel(ImChatManager.GetGroupConversation(ImConversationType.Party));

            // 家族频道（恒显示）
            AddChannel(ImChatManager.GetGroupConversation(ImConversationType.Clan));

            // 王国频道（仅玩家是族长且所属王国存在，需求 2）
            if (ImChatManager.CanSeeKingdomChannel())
                AddChannel(ImChatManager.GetGroupConversation(ImConversationType.Kingdom));

            // 分组标题：私聊（最近的单个人的聊天，需求 2；无私聊则不显示分组）
            var directs = ImChatManager.GetRecentDirectConversations();
            if (directs.Count > 0)
            {
                // 分组标题：私聊
                _vm.ChannelList.Add(ImChannelVM.CreateHeader(LWNTextHelper.ResolveText("LWN_im_group_directs", "Recent")));
                foreach (var conv in directs)
                    AddChannel(conv);
            }
        }

        private static void AddChannel(ImConversation conv)
        {
            if (conv == null || _vm == null) return;
            var item = new ImChannelVM(conv)
            {
                IsSelected = _selected != null && _selected.Id == conv.Id,
            };
            RefreshChannelItem(item);
            _vm.ChannelList.Add(item);
        }

        /// <summary>频道行动态内容刷新：未读 + 最后消息预览（微信会话列表语义）。
        /// 副标题 = 最后一条消息摘要（群聊带发送者前缀）；私聊直接是内容。</summary>
        private static void RefreshChannelItem(ImChannelVM item)
        {
            if (item == null || item.IsGroupHeader) return;
            var conv = item.Conversation;
            if (conv == null) return;
            var msgs = ImChatManager.GetMessages(conv);
            var last = msgs.LastOrDefault();
            if (last != null && !string.IsNullOrWhiteSpace(last.Content))
            {
                string preview = last.Content;
                if (preview.Length > 18) preview = preview.Substring(0, 18) + "…";
                if (conv.Type != ImConversationType.Direct && last.SenderHeroId != ImChatManager.PlayerId)
                    preview = $"{last.SenderName}：{preview}";
                item.Subtitle = preview;
            }
            else
            {
                item.Subtitle = "";
            }
            item.RefreshUnread();
        }

        private static void RefreshTitle()
        {
            if (_vm == null) return;
            _vm.Title = _selected?.Title ?? "";
            // 空会话引导文案（本地化）
            _vm.EmptyHint = LWNTextHelper.ResolveText("LWN_im_empty_hint",
                "No messages yet. Say something to break the silence...");
        }

        /// <summary>增量刷新消息（追加新增；记忆层总结裁剪导致变少才重建，防滚动跳变）。</summary>
        private static void RefreshMessages()
        {
            if (_vm == null || _selected == null) return;
            var msgs = ImChatManager.GetMessages(_selected);
            if (msgs.Count < _vm.Messages.Count)
            {
                _vm.Messages.Clear();
                foreach (var m in msgs) _vm.Messages.Add(new ImMessageVM(m));
            }
            else
            {
                for (int i = _vm.Messages.Count; i < msgs.Count; i++)
                    _vm.Messages.Add(new ImMessageVM(msgs[i]));
            }
            // 空会话引导（UI 优化：新频道无消息时给玩家一个提示而非空白）
            _vm.IsEmpty = msgs.Count == 0;
            RefreshChannelsDynamic();
        }

        /// <summary>所有频道行动态刷新（未读 + 最后消息预览）。</summary>
        private static void RefreshChannelsDynamic()
        {
            if (_vm == null) return;
            foreach (var ch in _vm.ChannelList)
                RefreshChannelItem(ch);
        }

        /// <summary>动态项：正在输入 + 模式标签/可见性（0.3s 节流调）。</summary>
        private static void RefreshDynamic()
        {
            if (_vm == null || _selected == null) return;
            _vm.TypingText = ImReplyService.GetTypingText(_selected.Id);

            bool modeVisible = IsCommandModeAvailable(_selected);
            _vm.IsModeToggleVisible = modeVisible;
            bool isCommand = ImChatStore.GetMode(_selected.Id) == ImMode.Command;
            if (isCommand && Mission.Current == null)
            {
                // 模式标签：行军令（Campaign 大地图 = 规则行军令，Q5b）
                _vm.ModeLabel = LWNTextHelper.ResolveText("LWN_im_mode_march", "March");
            }
            else if (isCommand)
            {
                // 模式标签：密令
                _vm.ModeLabel = LWNTextHelper.ResolveText("LWN_im_mode_command", "Order");
            }
            else
            {
                // 模式标签：闲聊
                _vm.ModeLabel = LWNTextHelper.ResolveText("LWN_im_mode_chat", "Chat");
            }
        }

        /// <summary>密令模式可用性：Plot 总闸 + LLM + 会话类型（队伍频道 / 私聊随从）。
        /// Q5b：Campaign 大地图私聊「有独立 party 的 Hero」也可用——行军令（规则解析，非 LLM 密令）。</summary>
        public static bool IsCommandModeAvailable(ImConversation conv)
        {
            if (conv == null) return false;
            if (!Settings.Instance.PlotEnabled || !Settings.Instance.IsLLMConfigured) return false;
            if (conv.Type == ImConversationType.Party) return true;
            if (conv.Type == ImConversationType.Direct)
            {
                try
                {
                    var hero = TaleWorlds.CampaignSystem.Hero.AllAliveHeroes
                        .FirstOrDefault(h => h.StringId == conv.PartnerHeroId);
                    if (hero == null) return false;
                    if (FriendlinessHelper.IsPlayerPartyMember(hero)) return true;   // 随从：Mission 内完整密令
                    if (Mission.Current == null && hero.PartyBelongedTo != null) return true; // 行军令
                }
                catch { return false; }
                return false;
            }
            return false;
        }

        // ───────────────────────── 每帧驱动 ─────────────────────────

        /// <summary>
        /// UI 层每帧钩子（ImScreenFrameTickPatch → ScreenBase.OnFrameTick，暂停时也触发）：
        /// 大地图/城镇菜单的热键检测与 UI 驱动（🔴 修复：CampaignEvents.TickEvent 暂停时停发）。
        /// Mission 内由 ImChatMissionView 驱动（本方法门控跳过，防双驱动）。
        /// </summary>
        public static void OnScreenFrameTick(float dt)
        {
            try
            {
                if (TaleWorlds.CampaignSystem.Campaign.Current == null) return; // 非战役（主菜单等）
                if (Mission.Current != null) return;                             // Mission 由 MissionView 驱动
                EnsureSubscribed();

                ModInput.Tick(dt);
                // 🔴 O 只负责「打开」：面板开着时输入 o 不再触发任何动作（打字不误关）
                if (ModInput.ShortFired(InteractionIds.IM) && !IsOpen)
                    Open();

                Tick(dt);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] Screen tick 异常: {ex.Message}");
            }
        }

        /// <summary>Mission（ImChatMissionView.OnMissionTick）/ Campaign（OnScreenFrameTick）双端调用。</summary>
        public static void Tick(float dt)
        {
            ImChatManager.Tick(dt);
            if (!IsOpen) return;

            // 🔴 关闭改用独立键（用户要求）：ESC / 手柄 B——O 只负责打开，打字不再误关。
            // 注：本层 InputRestrictions(All) 是模态掩码，ESC 已被层拦截（不会触发系统菜单，与 Inquiry 同理），
            // 这里轮询全局输入状态消费关闭动作。
            if (Input.IsKeyReleased(InputKey.Escape) || Input.IsKeyReleased(InputKey.ControllerRRight))
            {
                Close();
                return;
            }

            // UI 优化：回车发送（微信习惯；IM 打开时唯一键盘输入焦点就是输入框）
            if (Input.IsKeyReleased(InputKey.Enter))
                ExecuteSend();

            _refreshTimer += dt;
            if (_refreshTimer >= 0.3f)
            {
                _refreshTimer = 0f;
                try { RefreshMessages(); RefreshDynamic(); }
                catch (Exception ex) { DebugLogger.Log($"[ImChat] Tick 刷新失败: {ex.Message}"); }
            }
        }

        // ───────────────────────── 玩家操作 ─────────────────────────

        public static void ExecuteSend()
        {
            if (_vm == null || _selected == null) return;
            string text = _vm.InputText;
            if (string.IsNullOrWhiteSpace(text)) return;
            _vm.InputText = "";

            // 密令模式 → 命令管线（Phase 4）；闲聊 → 常规发送
            if (ImChatStore.GetMode(_selected.Id) == ImMode.Command)
            {
                ImCommandFlow.RequestCommand(_selected, text.Trim());
            }
            else
            {
                ImChatManager.SendPlayerMessage(_selected, text.Trim());
            }
            RefreshMessages();
        }

        public static void ExecuteToggleMode()
        {
            if (_vm == null || _selected == null) return;
            var conv = _selected;
            bool nextCommand = ImChatStore.GetMode(conv.Id) != ImMode.Command;

            if (nextCommand)
            {
                if (!IsCommandModeAvailable(conv))
                {
                    // 提示：密令不可用
                    ShowHint(LWNTextHelper.ResolveText("LWN_im_mode_unavailable", "Command mode is unavailable here."));
                    return;
                }
                // Campaign 大地图 = 行军令模式（IsCommandModeAvailable 已把关：私聊有 party 的 Hero）；
                // Mission 内还需互斥检查（PlanCommandFlow 面谈进行中）
                if (Mission.Current != null && PlanCommandFlow.IsActive)
                {
                    // 提示：另有密谋进行中
                    ShowHint(LWNTextHelper.ResolveText("LWN_im_mode_plot_active", "Another secret order is already being discussed."));
                    return;
                }
            }

            ImChatStore.SetMode(conv.Id, nextCommand ? ImMode.Command : ImMode.Chat);
            RefreshDynamic();
        }

        /// <summary>密令卡片操作（Phase 4 ImCommandFlow 处理）。</summary>
        public static void HandlePlanAction(ImMessage msg, bool approve, bool abort = false)
        {
            if (msg == null) return;
            try
            {
                if (abort) ImCommandFlow.Abort(msg);
                else ImCommandFlow.Resolve(msg, approve);
                // 🔴 卡片状态变更后强制重建消息列表：CanApprove/CanReject/CanAbort 是只读计算属性，
                // 增量追加不会刷新已存在消息的按钮状态（批准后「同意/拒绝」会常驻、「中止」不出现）
                if (_vm != null) _vm.Messages.Clear();
                RefreshMessages();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] HandlePlanAction 失败: {ex.Message}");
            }
        }

        private static void ShowHint(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            InformationManager.DisplayMessage(new InformationMessage(text));
        }

        // ───────────────────────── 新消息通知 ─────────────────────────

        private static void OnMessageArrived(ImConversation conv)
        {
            try
            {
                if (conv == null) return;
                if (IsOpen && _selected != null && _selected.Id == conv.Id)
                {
                    // 正在看这个会话：消息直接上屏，未读计数同步清零（防左栏徽标只增不减）
                    ImChatStore.ClearUnread(conv.Id);
                    RefreshMessages();
                    RefreshChannelsDynamic();
                    return;
                }
                if (!IsOpen)
                    NotifyIncoming(conv);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] OnMessageArrived 异常: {ex.Message}");
            }
        }

        /// <summary>IM 关闭时来消息 → NinjaNotification 圆环（点击打开并定位会话）。
        /// 玩家体验完善（Q1a）：摘要带会话名（群聊能区分是哪个频道来的消息）。</summary>
        private static void NotifyIncoming(ImConversation conv)
        {
            var msgs = ImChatManager.GetMessages(conv);
            var last = msgs.LastOrDefault();
            if (last == null) return;
            string content = last.Content ?? "";
            if (content.Length > 24) content = content.Substring(0, 24) + "…";
            string summary = $"{conv.Title} · {last.SenderName}：{content}";
            NinjaNotificationManager.Show(summary, () => { Open(conv); });
        }
    }
}
