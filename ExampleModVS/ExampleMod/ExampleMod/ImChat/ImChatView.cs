using System;
using System.Linq;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
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

        // 🔴 七轮：手动滚轮接管（引擎 ScrollablePanel 滚轮派发在模态层下不可靠——官方 SPChatLog 用
        // 「查看模式」按钮规避贴底+滚轮冲突；这里直接从 UIContext 找 ScrollablePanel 操作 ValueFloat）
        private static ScrollablePanel _messageScrollPanel;
        private static float _scrollDiagTimer;

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
                // 🔴 层级 400：反编译 SandBox.GauntletUI.dll 实测原生层序——地图名标 90 / 菜单 100 /
                // MapBar+定居点菜单覆盖层(MapMenuOverlay) 202 / 地图对话 205 / 百科 310 / 系统菜单 4400。
                // 原 20 会被定居点菜单和地图 HUD 盖住（玩家报告）；400 高于全部地图玩法 UI，低于系统菜单。
                _layer = V.NewLayer(400, "ImChatLayer");
                // LoadMovie 字符串 = GUI/Prefabs/ImChat.xml 文件名（不带后缀）；返回类型跨版本不同（同上 #if）
                _movie = _layer.LoadMovie("ImChat", _vm);
                // 🔴 输入限制：MouseButtons|MouseWheels|Keyboardkeys——滚轮必须留在 IM 层（含 MouseWheels 位，
                // 否则穿透到地图层触发镜头缩放，六轮实机修复）；滚动派发由 MessageClip DoNotAcceptEvents 保证
                // （EventManager.MouseScroll 只调用 hit test 命中的 widget，不冒泡）
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.MouseButtons | InputUsageMask.MouseWheels | InputUsageMask.Keyboardkeys);
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
            _messageScrollPanel = null;   // 层关闭后 widget 树失效，缓存清空
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

            // 🔴 2026-08-10 三轮：删除「频道」分组标题（用户反馈不需要）——队伍/家族/王国频道直接列在顶部，
            // 仅保留「最近消息」分组标题区分私聊区块
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
                if (conv.Type != ImConversationType.Direct && last.SenderHeroId != ImChatManager.PlayerId)
                    preview = $"{last.SenderName}：{preview}";
                // 🔴 2026-08-10 三轮：前缀拼完再整体截断 13 字符（中文字符 13px 宽，13 字 ≈169px 不超 240px 左栏可用宽；
                //     ClipContents 兜底裁，C# 截断保证不触发溢出）
                if (preview.Length > 13) preview = preview.Substring(0, 13) + "…";
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

        /// <summary>增量刷新消息（追加新增；记忆层总结裁剪导致变少才重建，防滚动跳变）。
        /// 🔴 八轮：检测「新消息到达且玩家不在底部」→ 显示「有新消息」提示条（在底部则内容自动可见不提示）。</summary>
        private static void RefreshMessages()
        {
            if (_vm == null || _selected == null) return;
            var msgs = ImChatManager.GetMessages(_selected);
            bool hadNew = msgs.Count > _vm.Messages.Count;
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
            // 新消息到达：玩家不在底部 → 提示；在底部 → 内容自动可见（贴底闭环）
            if (hadNew && !IsMessageAtBottom())
                _vm.HasNewMessageHint = true;
            RefreshChannelsDynamic();
        }

        /// <summary>消息流是否在底部（val 接近 max；±8px 容差）。</summary>
        private static bool IsMessageAtBottom()
        {
            try
            {
                if (_messageScrollPanel?.VerticalScrollbar == null) return true;
                var sb = _messageScrollPanel.VerticalScrollbar;
                return sb.ValueFloat >= sb.MaxValue - 8f;
            }
            catch { return true; }
        }

        /// <summary>滚到消息流底部（🔴 八轮：发消息后自动贴底，翻历史后仍能看到自己的新消息）。</summary>
        public static void ScrollToBottom()
        {
            try
            {
                if (_messageScrollPanel?.VerticalScrollbar != null)
                    _messageScrollPanel.VerticalScrollbar.ValueFloat = _messageScrollPanel.VerticalScrollbar.MaxValue;
                if (_vm != null) _vm.HasNewMessageHint = false;
            }
            catch { }
        }

        /// <summary>「有新消息」提示条点击：滚底 + 清提示。</summary>
        public static void ExecuteNewMessageClick()
        {
            ScrollToBottom();
            RefreshMessages();
        }

        /// <summary>所有频道行动态刷新（未读 + 最后消息预览）。</summary>
        private static void RefreshChannelsDynamic()
        {
            if (_vm == null) return;
            foreach (var ch in _vm.ChannelList)
                RefreshChannelItem(ch);
        }

        /// <summary>动态项：标题带正在思考 + 模式状态/切换按钮 + 输入区联动（0.3s 节流调）。</summary>
        private static void RefreshDynamic()
        {
            if (_vm == null || _selected == null) return;
            // 🔴 五轮：正在输入提示移到标题带，且仅私聊显示（群聊不显示——用户反馈；私聊 = 回复在途时
            // 标题带显示「XX 正在思考回复…」）
            string typing = ImReplyService.GetTypingText(_selected.Id);
            _vm.IsTypingVisible = _selected.Type == ImConversationType.Direct && !string.IsNullOrWhiteSpace(typing);
            _vm.TypingText = typing;

            bool modeVisible = IsCommandModeAvailable(_selected);
            _vm.IsModeControlVisible = modeVisible;
            bool isCommand = ImChatStore.GetMode(_selected.Id) == ImMode.Command;
            // 密令侧模式名：Mission = 密令；Campaign 大地图 = 行军令（Q5b）
            string commandModeName = Mission.Current == null
                ? LWNTextHelper.ResolveText("LWN_im_mode_march", "March")   // 模式名：行军令
                : LWNTextHelper.ResolveText("LWN_im_mode_command", "Order"); // 模式名：密令
            string chatModeName = LWNTextHelper.ResolveText("LWN_im_mode_chat", "Chat"); // 模式名：闲聊
            // 状态静态文本：「当前：XX模式」；按钮动作文本：「切换到XX」（目标 = 非当前模式）
            _vm.ModeStatusText = LWNTextHelper.ResolveCompound("LWN_im_mode_status", "Mode: {MODE}",
                ("MODE", isCommand ? commandModeName : chatModeName));
            _vm.SwitchModeButtonText = LWNTextHelper.ResolveCompound("LWN_im_btn_switch_mode", "Switch to {MODE}",
                ("MODE", isCommand ? chatModeName : commandModeName));
            // 输入区随模式联动（双重反馈：placeholder + 发送按钮文案）
            _vm.PlaceholderText = isCommand
                ? LWNTextHelper.ResolveText("LWN_im_input_placeholder_cmd", "Type a command...")
                : LWNTextHelper.ResolveText("LWN_im_input_placeholder", "Type a message...");
            _vm.SendText = isCommand
                ? LWNTextHelper.ResolveText("LWN_im_btn_order", "Order")
                : LWNTextHelper.ResolveText("LWN_im_btn_send", "Send");
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

            // 🔴 七轮：手动滚轮接管（引擎滚轮派发在模态层下不可靠）+ 滚动条件诊断日志
            HandleManualScroll(dt);
        }

        // ───────────────────────── 手动滚轮接管（七轮）─────────────────────────

        /// <summary>从 UIContext 找消息流 ScrollablePanel（层不暴露树，但 UIContext.Root 可遍历）。</summary>
        private static void FindMessageScrollPanel()
        {
            _messageScrollPanel = null;
            try
            {
                if (_layer?.UIContext?.Root != null)
                    _messageScrollPanel = FindWidgetById(_layer.UIContext.Root, "MessageScroll") as ScrollablePanel;
            }
            catch { }
        }

        private static Widget FindWidgetById(Widget root, string id)
        {
            if (root == null) return null;
            if (root.Id == id) return root;
            for (int i = 0; i < root.ChildCount; i++)
            {
                var found = FindWidgetById(root.GetChild(i), id);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>手动滚轮：鼠标在消息流区域内时，把全局滚轮增量直接加到 ScrollbarWidget.ValueFloat
        /// （ScrollablePanel.Update 每帧从 ValueFloat 重算 InnerPanel 偏移——贴底闭环兼容）。
        /// 🔴 引擎链路（层 InputContext → EventManager 派发）在模态层下不可靠，官方 SPChatLog 也规避此问题。</summary>
        private static void HandleManualScroll(float dt)
        {
            if (_messageScrollPanel == null)
            {
                FindMessageScrollPanel();
                return;
            }
            try
            {
                // 🔴 诊断日志（滚动调试用，已确认修复后注释；需要取证时取消注释）：
                // 每 1s 输出 inner=内容高度 clip=可视高度 max=滚动范围 val=当前值——
                // inner=-1 说明 InnerPanel 路径解析失败（Id 与 LWN_ 前缀不一致）；
                // max 恒等于 XML 初值说明引擎滚动更新未运行（InnerPanel=null 异常中断）。
                //_scrollDiagTimer += dt;
                //if (_scrollDiagTimer >= 1f)
                //{
                //    _scrollDiagTimer = 0f;
                //    var panel = _messageScrollPanel;
                //    float inner = panel.InnerPanel?.Size.Y ?? -1f;
                //    float clip = panel.ClipRect?.Size.Y ?? -1f;
                //    DebugLogger.Log($"[ImChat] ScrollDiag inner={inner:0} clip={clip:0} max={panel.VerticalScrollbar?.MaxValue ?? -1f:0} val={panel.VerticalScrollbar?.ValueFloat ?? -1f:0.0}");
                //}

                float delta = Input.DeltaMouseScroll;
                if (MathF.Abs(delta) <= 0.001f) return;
                var scrollbar = _messageScrollPanel.VerticalScrollbar;
                if (scrollbar == null) return;

                // 鼠标是否在消息流区域（GlobalPosition + Size 为屏幕像素坐标，与 MousePositionPixel 同系）
                var pos = _messageScrollPanel.GlobalPosition;
                var size = _messageScrollPanel.Size;
                Vec2 mouse = Input.MousePositionPixel;
                if (mouse.X < pos.X || mouse.X > pos.X + size.X || mouse.Y < pos.Y || mouse.Y > pos.Y + size.Y)
                    return;

                // 滚轮向上（delta>0）→ 往历史（ValueFloat 减小）；速度系数与引擎 MouseScrollSpeed 一致量级
                scrollbar.ValueFloat -= delta * 0.05f;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] 手动滚轮异常: {ex.Message}");
            }
        }

        // ───────────────────────── 玩家操作 ─────────────────────────

        public static void ExecuteSend()
        {
            if (_vm == null || _selected == null) return;
            string text = _vm.InputText;
            if (string.IsNullOrWhiteSpace(text)) return;
            _vm.InputText = "";

            // 🔴 玩家消息落日志（上下文分析用，对齐 [VanillaDialog] Player says 惯例；闲聊/密令两路径都经过这里）
            DebugLogger.Log($"[ImChat] Player → {_selected.Title}: \"{text.Trim()}\"");

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
            // 🔴 八轮：发消息后自动滚底（玩家翻历史后发消息，新消息必须可见）
            ScrollToBottom();
        }

        /// <summary>切换按钮：切到闲聊模式（点击非当前模式才有效，否则无操作）。</summary>
        public static void ExecuteSwitchToChat()
        {
            if (_vm == null || _selected == null) return;
            if (ImChatStore.GetMode(_selected.Id) != ImMode.Command) return;
            SetMode(ImMode.Chat);
        }

        /// <summary>切换按钮（2026-08-10 终版：单按钮 + 文本变量「切换到密令」⇄「切换到闲聊」）。
        /// Command.Click 固定方法绑定 → 单方法内部按当前模式路由。</summary>
        public static void ExecuteSwitchMode()
        {
            if (_vm == null || _selected == null) return;
            if (ImChatStore.GetMode(_selected.Id) == ImMode.Command)
                SetMode(ImMode.Chat);
            else
                ExecuteSwitchToCommand();   // 含可用性 + 互斥检查
        }

        /// <summary>切到密令模式（含可用性 + 互斥检查）。</summary>
        public static void ExecuteSwitchToCommand()
        {
            if (_vm == null || _selected == null) return;
            if (ImChatStore.GetMode(_selected.Id) == ImMode.Command) return;
            var conv = _selected;

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

            SetMode(ImMode.Command);
        }

        private static void SetMode(ImMode mode)
        {
            if (_vm == null || _selected == null) return;
            ImChatStore.SetMode(_selected.Id, mode);
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
