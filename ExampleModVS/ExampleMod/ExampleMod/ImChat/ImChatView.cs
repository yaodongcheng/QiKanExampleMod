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
#pragma warning disable CS0169 // 滚动诊断节流器——诊断日志注释期间未使用，取证时随注释块一起启用
        private static float _scrollDiagTimer;
#pragma warning restore CS0169
        // 🔴 十一轮：贴底状态机——锁定 = 内容增长时每帧重 pin 到最新（引擎 offset=MaxValue-val，
        // 内容增长会让 max 变大、offset 漂移出新消息）；玩家上拉翻历史解锁
        private static bool _pinnedToBottom;

        // 🔴 Q2（2026-08-10）：修改输入态——当前待修改的卡片（发送时走 RequestModify 管线）
        private static ImMessage _modifyingMsg;

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

        /// <summary>
        /// 打开 IM 面板并定位会话。
        /// 🔴 2026-08-10（im-command-action-upgrade.md Q1）：新增 mode 参数——Plot 入口
        /// （PlanCommandFlow.Start）呼出面板后直接切「密令」模式，省去玩家手动切换。
        /// </summary>
        public static void Open(ImConversation selectConv = null, ImMode? mode = null)
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
                // 🔴 Q1：Plot 入口指定模式（如 Command）→ 打开后立即切换，输入框 placeholder 同步
                if (mode.HasValue) SetMode(mode.Value);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] Open 失败: {ex.Message}");
                Close();
            }
        }

        public static void Close()
        {
            // 🔴 Q1：IM 关闭 = 密谋输入阶段结束（Talk 行互斥恢复；执行中的计划不受影响——StopPlan 独立判断）
            PlanCommandFlow.End();
            // 🔴 Q2：修改输入态清理
            _modifyingMsg = null;
            if (_vm != null) _vm.IsModifying = false;
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
            // 🔴 Q2：切会话清修改输入态（修改意见属于原会话）
            _modifyingMsg = null;
            if (_vm != null) _vm.IsModifying = false;
            // 🔴 十一轮：切会话默认贴底（IM 惯例：打开会话看最新；面板引用首帧才解析，
            // ScrollToBottom 的 val=max 由 Tick 里的贴底闭环持续补上）
            _pinnedToBottom = true;
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
            // 🔴 §5.7 附近频道（仅 Mission 场景；Campaign 隐藏——场景外无冒泡可听）
            if (Mission.Current != null)
                AddChannel(NearbyFeed.Conversation);

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
            // 🔴 §3.3：生成中占位行的进度/阶段文案是动态值（消息对象已更新但 VM 是旧副本）——
            // 存在 Generating 消息时全量重建（0.3s 节流驱动，消息量小成本可接受）
            bool hasGenerating = false;
            foreach (var m in msgs) { if (m.IsGenerating) { hasGenerating = true; break; } }
            if (hasGenerating)
            {
                _vm.Messages.Clear();
                foreach (var m in msgs) _vm.Messages.Add(new ImMessageVM(m));
                _vm.IsEmpty = false;
                RefreshChannelsDynamic();
                return;
            }
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
            // 🔴 九轮：新消息处理二分——玩家在底部（未上拉）→ 自动滚底把新消息弹出来（贴底闭环兜底）；
            // 玩家在上部（翻历史）→ 才提示「有新消息」
            // 🔴 十一轮：判底改用「贴底锁定态」而非数值——内容增长的同一帧 MaxValue 尚未更新，
            // 数值判底（val≥max-8）会误判为「不在底部」→ 弹提示 + 停止跟新（>8px 的新消息必中招）
            if (hadNew)
            {
                if (_pinnedToBottom)
                    ScrollToBottom();
                else
                    _vm.HasNewMessageHint = true;
            }
            RefreshChannelsDynamic();
        }

        /// <summary>消息流是否在底部（🔴 十一轮：引擎 Bottom 对齐 offset=MaxValue-val——贴底 = offset=0 =
        /// val=max，引擎每帧经 AdjustVerticalScrollBar 把 val 同步为 MaxValue；±8 容差）。
        /// 十轮误判为 |val|≤8（贴底=val≈0），实为「内容不溢出时引擎强制 val=0」的巧合，溢出即翻转。</summary>
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

        /// <summary>滚到消息流底部并锁定贴底（🔴 十一轮：val=MaxValue → offset=MaxValue-MaxValue=0 =
        /// 贴底（Bottom 对齐自然位，新消息可见）；十轮设 val=0 → offset=MaxValue → 面板下移露出最旧消息——
        /// 「滚动条出现后发送没拉到底」根因，反编译 ScrollablePanel.UpdateScrollablePanel 确诊）。</summary>
        public static void ScrollToBottom()
        {
            try
            {
                if (_messageScrollPanel?.VerticalScrollbar != null)
                    _messageScrollPanel.VerticalScrollbar.ValueFloat = _messageScrollPanel.VerticalScrollbar.MaxValue;
                _pinnedToBottom = true;
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
            // 输入区随模式联动（双重反馈：placeholder + 发送按钮文案）；🔴 Q2 修改态优先级最高
            bool isModifying = _vm.IsModifying && _modifyingMsg != null;
            _vm.PlaceholderText = isModifying
            // 修改态输入框 placeholder
                ? LWNTextHelper.ResolveText("LWN_im_input_placeholder_modify", "Revise the plan: ...")
                : isCommand
                    ? LWNTextHelper.ResolveText("LWN_im_input_placeholder_cmd", "Type a command...")
                    : LWNTextHelper.ResolveText("LWN_im_input_placeholder", "Type a message...");
            _vm.SendText = isModifying
            // 修改态发送按钮
                ? LWNTextHelper.ResolveText("LWN_im_btn_submit_modify", "Submit")
                : isCommand
                    ? LWNTextHelper.ResolveText("LWN_im_btn_order", "Order")
                    : LWNTextHelper.ResolveText("LWN_im_btn_send", "Send");
        }

        /// <summary>密令模式可用性：Plot 总闸 + LLM + 会话类型（队伍频道 / 私聊随从）。
        /// Q5b：Campaign 大地图私聊「有独立 party 的 Hero」也可用——行军令（规则解析，非 LLM 密令）。
        /// 🔴 临时止血（2026-08-11 用户裁定）：队伍频道/随从私聊仅 Mission 可切计划模式——
        /// Campaign 下频道密令无执行载体（行军令仅私聊+独立 party 有效），随从密令被 MainParty 拦截，
        /// 都是无效入口，禁掉避免玩家误点（家族/王国频道本就不开放）。</summary>
        public static bool IsCommandModeAvailable(ImConversation conv)
        {
            if (conv == null) return false;
            // 🔴 §5.7：附近频道无密令（场景喊话不走计划管线）
            if (conv.Type == ImConversationType.Nearby) return false;
            if (!Settings.Instance.PlotEnabled || !Settings.Instance.IsLLMConfigured) return false;
            if (conv.Type == ImConversationType.Party) return Mission.Current != null;   // 队伍频道：仅 Mission（临时止血）
            if (conv.Type == ImConversationType.Direct)
            {
                try
                {
                    var hero = TaleWorlds.CampaignSystem.Hero.AllAliveHeroes
                        .FirstOrDefault(h => h.StringId == conv.PartnerHeroId);
                    if (hero == null) return false;
                    if (FriendlinessHelper.IsPlayerPartyMember(hero)) return Mission.Current != null; // 随从：仅 Mission（临时止血）
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

            // 🔴 十一轮：贴底持续闭环——内容增长时引擎 offset 漂移（max 变大、val 不变），
            // 锁定态每帧重 pin 到最新（val=max → offset=0），新消息永远弹出；解锁态不碰
            if (_pinnedToBottom)
                ScrollToBottom();

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
                // 🔴 诊断日志（十一轮已确认修复后注释；需要取证时取消注释）：
                // 每 0.5s 输出 inner=内容高度 clip=可视高度 max=滚动范围 val=ValueFloat off=InnerPanel 下移量——
                // 贴底态特征：val≈max、off≈0（引擎 Bottom 对齐 offset=MaxValue-val）。
                //_scrollDiagTimer += dt;
                //if (_scrollDiagTimer >= 0.5f)
                //{
                //    _scrollDiagTimer = 0f;
                //    var panel = _messageScrollPanel;
                //    float inner = panel.InnerPanel?.Size.Y ?? -1f;
                //    float clip = panel.ClipRect?.Size.Y ?? -1f;
                //    float offset = panel.InnerPanel?.ScaledPositionYOffset ?? -1f;
                //    DebugLogger.Log($"[ImChat] ScrollDiag inner={inner:0} clip={clip:0} max={panel.VerticalScrollbar?.MaxValue ?? -1f:0} val={panel.VerticalScrollbar?.ValueFloat ?? -1f:0.0} off={offset:0}");
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

                // 方向与引擎公式逐字一致（引擎 offset += DeltaMouseScroll ⇔ val -= DeltaMouseScroll，
                // 引擎方向经实机验证正确）；clamp [0, max] 防越界（val<0 或 >max 会让 InnerPanel 超界出空白）
                scrollbar.ValueFloat = MathF.Clamp(scrollbar.ValueFloat - delta * 0.05f, 0f, scrollbar.MaxValue);

                // 🔴 十一轮：贴底状态机——上拉（delta>0=往历史）解锁跟新；下拉到底（val 被 clamp 顶到 max）重新锁定
                if (delta > 0f)
                    _pinnedToBottom = false;
                else if (IsMessageAtBottom())
                    _pinnedToBottom = true;
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

            // 🔴 Q2：修改输入态 → 修改管线（发送的是修改意见）
            if (_modifyingMsg != null)
            {
                var target = _modifyingMsg;
                _modifyingMsg = null;
                _vm.IsModifying = false;
                ImCommandFlow.RequestModify(target, text.Trim());
                RefreshMessages();
                ScrollToBottom();
                return;
            }

            // 🔴 玩家消息落日志（上下文分析用，对齐 [VanillaDialog] Player says 惯例；闲聊/密令两路径都经过这里）
            DebugLogger.Log($"[ImChat] Player → {_selected.Title}: \"{text.Trim()}\"");

            // 🔴 §5.7 附近频道：玩家喊话（头顶冒泡 + 广播 spoken_to 给最近 NPC → 响应不确定）
            if (_selected.Type == ImConversationType.Nearby)
            {
                AgentHudMissionView.AgentSay(Agent.Main, text.Trim());
                NearbyFeed.BroadcastPlayerCall(text.Trim());
                RefreshMessages();
                ScrollToBottom();
                return;
            }

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

        /// <summary>🔴 Q2 修改入口（ImMessageVM.ExecuteModify）：进入修改输入态——输入框聚焦 +
        /// placeholder 联动「修改计划：…」；发送走 RequestModify。额度用尽/非命令模式 → 提示。</summary>
        public static void BeginModify(ImMessage msg)
        {
            if (_vm == null || _selected == null || msg == null || !msg.IsPlanCard) return;
            if (msg.PlanModifyCount >= ImCommandFlow.MaxModifyCount)
            {
            // 修改额度用尽提示
                ShowHint(LWNTextHelper.ResolveText("LWN_im_cmd_modify_exhausted", "The plan has been revised too many times. Approve it or start over."));
                return;
            }
            _modifyingMsg = msg;
            _vm.IsModifying = true;
            RefreshDynamic();
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
            // Mission 内还需互斥检查（PlanCommandFlow 面谈进行中，本会话除外——Plot 入口已切 Command 模式）
            if (Mission.Current != null && PlanCommandFlow.IsActiveForOtherConv(conv))
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
            // 🔴 Q1：切回闲聊 = 放弃密谋输入阶段（互斥解除；命令已批准的执行不受影响）
            if (mode != ImMode.Command) PlanCommandFlow.End();
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

        /// <summary>🔴 Q4：NPC 主动提议处理——批准 = 提议文本作为命令走计划管线（RequestCommand → PlanCard → 玩家批准后执行）；
        /// 拒绝 = 提议了结。计划元数据不写 NPC 记忆（裁定：批准/修改/拒绝不构成经历）。
        /// 🔴 2026-08-11：带 ActionCode 的动作提议（闲聊高风险动作卡片）——批准 = 直接执行该动作
        /// （空间/冷却/IsValid 复检在 ActionHandler 内，NPC 已离场自然降级）；拒绝 = 了结。</summary>
        public static void HandleProposal(ImMessage msg, bool approve)
        {
            if (msg == null || !msg.IsProposal || msg.IsProposalResolved) return;
            var conv = ConversationOf(msg.ConvId);
            if (conv == null) return;

            // 动作提议分支（闲聊高风险动作）：不走计划管线，批准即执行
            if (!string.IsNullOrEmpty(msg.ActionCode))
            {
                msg.ExecutorId = "done";
                RefreshMessages();
                if (!approve) return;
                try
                {
                    // bypassConfirm=true：玩家已批准，直接执行（空间/冷却/IsValid 复检在 ActionHandler 内）
                    ActionHandler.HandleImAction(msg.ActionCode, msg.SenderHeroId, msg.SenderName,
                        msg.ActionTarget, msg.ActionLevel, conv, msg.Content, bypassConfirm: true);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[ImChat] 动作提议执行失败 {msg.ActionCode}: {ex.Message}");
                }
                // 消息列表重建（提议按钮消失 + 可能的动作副作用刷新）
                if (_vm != null) { _vm.Messages.Clear(); RefreshMessages(); }
                return;
            }

            if (!approve)
            {
                msg.ExecutorId = "done";
                RefreshMessages();
                return;
            }
            // 批准：提议文本即命令（NPC 提议的计划 = 玩家批准后执行）
            if (string.IsNullOrWhiteSpace(msg.Content))
            {
                msg.ExecutorId = "done";
                RefreshMessages();
                return;
            }
            msg.ExecutorId = "done";
            ImCommandFlow.RequestCommand(conv, msg.Content);
            // 消息列表重建（提议按钮消失）
            if (_vm != null) { _vm.Messages.Clear(); RefreshMessages(); }
        }

        private static ImConversation ConversationOf(string convId)
        {
            if (string.IsNullOrEmpty(convId)) return null;
            if (convId.StartsWith("direct_"))
                return ImChatManager.GetDirectConversation(convId.Substring("direct_".Length));
            return ImChatManager.GetGroupConversation(convId == ImChatStore.ChannelClan
                ? ImConversationType.Clan
                : convId == ImChatStore.ChannelKingdom ? ImConversationType.Kingdom : ImConversationType.Party);
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
