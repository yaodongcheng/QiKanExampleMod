using System;
using System.Collections.Generic;
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
        // 🔴 2026-08-12：思考中占位是否存在于上一帧（转态帧检测 → 全量重建，防旧占位行残留）
        private static bool _hadGenerating;
        private static bool _subscribed;
        private static bool _welcomed;   // 首次打开引导提示（会话内一次）
        // 🔴 2026-08-12（模板 NPC 密信 · 粘性 @）：最近一次定向喊话的 @前缀（含尾随空格，如「@守卫 #12 」）——
        // @命中发送后回填输入框，连发多条给同一 NPC 不用重复打 @；玩家删掉前缀发普通喊话 → 解除。
        private static string _lastMentionPrefix;

        // 🔴 七轮：手动滚轮接管（引擎 ScrollablePanel 滚轮派发在模态层下不可靠——官方 SPChatLog 用
        // 「查看模式」按钮规避贴底+滚轮冲突；这里直接从 UIContext 找 ScrollablePanel 操作 ValueFloat）
        private static ScrollablePanel _messageScrollPanel;
#pragma warning disable CS0169 // 滚动诊断节流器——诊断日志注释期间未使用，取证时随注释块一起启用
        private static float _scrollDiagTimer;
#pragma warning restore CS0169
        // 🔴 十一轮：贴底状态机——锁定 = 内容增长时每帧重 pin 到最新（引擎 offset=MaxValue-val，
        // 内容增长会让 max 变大、offset 漂移出新消息）；玩家上拉翻历史解锁
        private static bool _pinnedToBottom;

        // 🔴 Q2（2026-08-10）修改输入态已废除（2026-08-12 用户裁定：输入框即修改，见 ExecuteSend）

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
        /// 打开 IM 面板并定位会话。返回 true = 已打开。
        /// 🔴 2026-08-12（合并闲聊/计划模式）：mode 参数已删除——不再有手动模式切换，
        /// 玩家恒走闲聊管线，needPlan 建议由 NPC 判定驱动（Plot 入口只打开私聊）。
        /// 🔴 2026-08-12（模板 NPC 密信）：prefill 参数 = 打开后输入框预填文本（G 长按模板 NPC →
        /// 定位附近频道 + 预填「@名字 #编号 」前缀，玩家可删掉转普通喊话）；SelectConversation 之后
        /// 设置（_vm 已创建 + LoadMovie 完成，双向绑定会把文本推给控件，无时序空隙）。
        /// </summary>
        public static bool Open(ImConversation selectConv = null, string prefill = null)
        {
            if (IsOpen || !CanOpen()) return false;
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
                if (prefill != null && _vm != null) _vm.InputText = prefill;
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] Open 失败: {ex.Message}");
                Close();
                return false;
            }
        }

        public static void Close()
        {
            // 🔴 Q1：IM 关闭 = 密谋输入阶段结束（Talk 行互斥恢复；执行中的计划不受影响——StopPlan 独立判断）
            PlanCommandFlow.End();
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
            // 🔴 2026-08-12：思考中→就绪转态（生成消息被移除、卡片上屏）→ 全量重建。
            // 原增量逻辑在转态帧只追加新行、旧占位行残留 → 实机"两张卡片"（思考中的还在）。
            bool hasGenerating = false;
            foreach (var m in msgs) { if (m.IsGenerating) { hasGenerating = true; break; } }
            bool transition = _hadGenerating && !hasGenerating;
            _hadGenerating = hasGenerating;
            // 🔴 2026-08-12 双保险：UI 已显示占位行但 store 已无（RemoveGenerating 历史 bug 的存档残留）→ 也全量重建
            bool uiHasGenerating = false;
            foreach (var vm in _vm.Messages)
            {
                if (vm.Message != null && vm.Message.IsGenerating) { uiHasGenerating = true; break; }
            }
            if (hasGenerating || transition || (uiHasGenerating && !hasGenerating))
            {
                _vm.Messages.Clear();
                foreach (var m in msgs) _vm.Messages.Add(new ImMessageVM(m));
                _vm.IsEmpty = false;
                UpdateCardAnchors();
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
            // 🔴 2026-08-12（用户裁定：决策卡片统一）：卡片按钮锚点重算（新卡片上屏/讲解消息上屏 → 锚点移动）
            UpdateCardAnchors();
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

        /// <summary>
        /// 🔴 2026-08-12（用户裁定：决策卡片统一，计划按钮 = 通用交互结构）：卡片按钮锚点计算
        ///（计划卡片 + NPC 提议/闲聊动作卡片共用，合并旧 UpdateLatestProposalFlag + UpdatePlanAnchors）。
        /// 规则（与旧两套纪律同款合并）：
        /// ① 会话内「最新可操作卡片」= 最新未决 Proposal（ExecutorId 空）或最新待批/执行中 PlanCard
        ///    （扫描顺序天然最新优先——两种卡片并存时新者接管，旧卡按钮隐藏、仍可回流）；
        /// ② 锚点消息 = 计划链 = 链内最新一条（讲解正文上屏 → 按钮移动到讲解消息下方）；
        ///    提议无链 = 卡片自身。
        /// 每次消息流刷新后调用（含增量追加——新卡片/新讲解消息上屏即锚点移动）。
        /// 旧格式卡片（无 ChainId）自身即锚点，沿用原行为。
        /// </summary>
        private static void UpdateCardAnchors()
        {
            if (_vm == null || _selected == null) return;
            var msgs = ImChatManager.GetMessages(_selected);
            // 会话内最新可操作卡片
            ImMessage latestCard = null;
            foreach (var m in msgs)
            {
                if (m == null) continue;
                if (m.IsProposal && !m.IsProposalResolved) latestCard = m;
                else if (m.IsPlanCard && (string.IsNullOrEmpty(m.ExecutorId) || ImCommandFlow.IsExecuting(m))) latestCard = m;
                // 🔴 2026-08-12（合并闲聊/计划模式）：needPlan 建议消息（待决）参与锚点竞争——最新者接管
                else if (m.IsPlanSuggest && !m.IsSuggestionResolved) latestCard = m;
            }
            foreach (var vm in _vm.Messages)
            {
                var m = vm.Message;
                ImMessage card = null;
                if (m != null)
                {
                    if (m.IsPlanCard || m.IsProposal || m.IsPlanSuggest)
                        card = m;
                    else if (m.IsPlanChainMessage)
                    {
                        foreach (var x in msgs)
                        {
                            if (x != null && x.IsPlanCard && x.ChainId == m.ChainId) { card = x; break; }
                        }
                    }
                }
                vm.AnchorCard = card;
                vm.IsCardAnchor = card != null
                    && card == latestCard
                    && IsCardAnchorPosition(m, card, msgs);
            }
        }

        /// <summary>本消息是否为卡片锚点位置：提议/建议 = 自身；计划 = 链内最新一条（🔴 2026-08-12 修复：
        /// 只扫 m **之后**的消息——原实现扫全表，卡片自身同链 → 讲解消息永远判定「后面还有同链消息」
        /// → 按钮全消失）。旧格式卡片无 ChainId = 仅自身。</summary>
        private static bool IsCardAnchorPosition(ImMessage m, ImMessage card, List<ImMessage> msgs)
        {
            if (m == null || card == null) return false;
            if (card.IsProposal || card.IsPlanSuggest) return true;   // 提议/建议无链：自身即锚点
            if (string.IsNullOrEmpty(card.ChainId)) return true;
            int mIdx = msgs.IndexOf(m);
            if (mIdx < 0) return false;
            for (int i = mIdx + 1; i < msgs.Count; i++)
            {
                if (msgs[i] != null && msgs[i].ChainId == card.ChainId)
                    return false;   // 后面还有同链消息 → 本消息不是锚点
            }
            return true;
        }

        /// <summary>
        /// 🔴 2026-08-12：讲解完成/失败后通知所有挂载该卡片的 VM（锚点可能已移到讲解消息——新 VM 实例，
        /// 只通知本 VM 会漏）。调用方在主线程（ImCommandFlow.Tick 消费讲解队列），安全触碰 UI。
        /// </summary>
        public static void NotifyPlanStateChanged(ImMessage card)
        {
            if (_vm == null || card == null) return;
            foreach (var vm in _vm.Messages)
            {
                if (vm != null && vm.AnchorCard == card)
                    vm.NotifyPlanState();
            }
        }

        /// <summary>动态项：标题带正在思考 + 模式指示文本（会话状态派生）+ 输入区联动（0.3s 节流调）。
        /// 🔴 2026-08-12（合并闲聊/计划模式）：模式文本不再可切换——按 ImCommandFlow.GetPhase 派生
        ///（闲聊 / 计划生成中 / 计划待批准 / 计划执行中），输入区按待批计划卡片联动（发送 = 修改）。</summary>
        private static void RefreshDynamic()
        {
            if (_vm == null || _selected == null) return;
            // 🔴 五轮：正在输入提示移到标题带，且仅私聊显示（群聊不显示——用户反馈；私聊 = 回复在途时
            // 标题带显示「XX 正在思考回复…」）
            string typing = ImReplyService.GetTypingText(_selected.Id);
            _vm.IsTypingVisible = _selected.Type == ImConversationType.Direct && !string.IsNullOrWhiteSpace(typing);
            _vm.TypingText = typing;

            // 模式指示文本：从会话计划状态派生（GetPhase 只反映「最新活动状态」）
            var phase = ImCommandFlow.GetPhase(_selected);
            string modeName = phase switch
            {
                // 模式名：计划生成中（GetPhase=Generating）
                ImCommandFlow.ImSessionPhase.Generating => LWNTextHelper.ResolveText("LWN_im_mode_generating", "Planning…"),
                // 模式名：计划待批准（GetPhase=PendingPlan）
                ImCommandFlow.ImSessionPhase.PendingPlan => LWNTextHelper.ResolveText("LWN_im_mode_pending", "Plan awaiting approval"),
                // 模式名：计划执行中（GetPhase=Executing）
                ImCommandFlow.ImSessionPhase.Executing => LWNTextHelper.ResolveText("LWN_im_mode_executing", "Executing"),
                // 模式名：闲聊（默认）
                _ => LWNTextHelper.ResolveText("LWN_im_mode_chat", "Chat"),
            };
            // 模式指示文本：「当前：{MODE}模式」（会话状态派生，常显）
            _vm.ModeStatusText = LWNTextHelper.ResolveCompound("LWN_im_mode_status", "Mode: {MODE}",
                ("MODE", modeName));
            // 输入区联动（双重反馈：placeholder + 发送按钮文案）。
            // 🔴 2026-08-12（用户裁定：修改按钮废除 → 输入框即修改）：待批计划卡片存在时，
            // placeholder/发送键提示「发送 = 修改该计划」（复用原修改态文案键）。
            bool hasPendingCard = Mission.Current != null && ImCommandFlow.FindLatestPendingCard(_selected) != null;
            _vm.PlaceholderText = hasPendingCard
            // 修改态输入框 placeholder（待批卡片存在时，发送 = 修改该计划）
                ? LWNTextHelper.ResolveText("LWN_im_input_placeholder_modify", "Revise the plan: ...")
                // 闲聊输入框 placeholder
                : LWNTextHelper.ResolveText("LWN_im_input_placeholder", "Type a message...");
            _vm.SendText = hasPendingCard
            // 修改态发送按钮（Submit）
                ? LWNTextHelper.ResolveText("LWN_im_btn_submit_modify", "Submit")
                // 普通发送按钮（Send）
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

            // 🔴 玩家消息落日志（上下文分析用，对齐 [VanillaDialog] Player says 惯例；闲聊/密令两路径都经过这里）
            DebugLogger.Log($"[ImChat] Player → {_selected.Title}: \"{text.Trim()}\"");

            // 🔴 §5.7 附近频道：玩家喊话（头顶冒泡 + 广播 spoken_to 给最近 NPC → 响应不确定）
            // 🔴 2026-08-12（模板 NPC 密信）：@提及前缀（@名字 #编号）→ 定向喊话给点名目标
            //（ForceRespond → 回复冒泡流入频道）；无 @ / 匹配失败 → 普通喊话。
            // 🔴 2026-08-12（粘性 @）：@命中后记录前缀并回填输入框——连发多条给同一 NPC 不用重复打 @；
            // 玩家删掉前缀发普通喊话 → 粘性解除。玩家消息进频道走 AgentSay → Forward，天然带完整 @ 前缀。
            if (_selected.Type == ImConversationType.Nearby)
            {
                string full = text.Trim();
                AgentHudMissionView.AgentSay(Agent.Main, full);
                var mention = NearbyFeed.TryResolveMention(full);
                if (mention != null && !string.IsNullOrWhiteSpace(mention.Value.body))
                {
                    NearbyFeed.BroadcastPlayerCallTo(mention.Value.target, mention.Value.body);
                    _lastMentionPrefix = mention.Value.prefix;   // 记录含尾随空格的 @前缀（如「@守卫 #12 」）
                    if (_vm != null) _vm.InputText = _lastMentionPrefix;   // 自动续上，直接回车连发
                }
                else
                {
                    _lastMentionPrefix = null;
                    NearbyFeed.BroadcastPlayerCall(full);
                }
                RefreshMessages();
                ScrollToBottom();
                return;
            }

            // 🔴 2026-08-12（合并闲聊/计划模式）：不再有模式路由——全部走闲聊管线，
            // 按会话派生状态路由（决策表）：
            //   ① 澄清轮挂起       → RequestCommand 并入命令上下文重生成（既有合并路径）
            //   ② 计划生成中       → 闲聊回复但本轮抑制 needPlan（防并发双计划）
            //   ③ PlanCard 待批    → RequestModify（保留「输入即修改」语义）
            //   ④ 计划执行中       → 闲聊回复 + 执行上下文注入（adjustPlan 判定可改计划）
            //   ⑤ 其他（闲聊/建议待决）→ 建议作废 + 闲聊回复（needPlan 正常启用）
            if (ImCommandFlow.HasPendingClarify(_selected))
            {
                ImCommandFlow.RequestCommand(_selected, text.Trim());
            }
            else if (HasGeneratingPlaceholder(_selected))
            {
                ImChatManager.SendPlayerMessage(_selected, text.Trim(), suppressNeedPlan: true);
            }
            else
            {
                var pendingCard = ImCommandFlow.FindLatestPendingCard(_selected);
                if (pendingCard != null)
                    ImCommandFlow.RequestModify(pendingCard, text.Trim());
                else
                {
                    // 建议待决 + 玩家发新消息 → 旧建议作废（按钮消失），新消息按新请求判定
                    ImCommandFlow.InvalidateSuggestions(_selected);
                    ImChatManager.SendPlayerMessage(_selected, text.Trim());
                }
            }
            RefreshMessages();
            // 🔴 八轮：发消息后自动滚底（玩家翻历史后发消息，新消息必须可见）
            ScrollToBottom();
        }

        /// <summary>会话是否有计划生成中占位（Generating 消息存在于 store；生成中发消息 → 抑制 needPlan 防并发）。</summary>
        private static bool HasGeneratingPlaceholder(ImConversation conv)
        {
            if (conv == null) return false;
            try
            {
                var msgs = ImChatStore.GetGroupMessages(conv.Id);
                for (int i = msgs.Count - 1; i >= 0; i--)
                {
                    if (msgs[i] != null && msgs[i].IsGenerating) return true;
                }
            }
            catch { }
            return false;
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

        /// <summary>🔴 2026-08-12（重拟按钮）：二次校验发现问题 → 同命令重新生成（RequestRegenerate）。</summary>
        public static void HandleRegenerate(ImMessage msg)
        {
            if (msg == null) return;
            try
            {
                ImCommandFlow.RequestRegenerate(msg);
                if (_vm != null) _vm.Messages.Clear();
                RefreshMessages();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] HandleRegenerate 失败: {ex.Message}");
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
                // 🔴 2026-08-11（Q2）：按钮行是重建式数据（CardButtons/IsCardAnchor），增量追加不会刷新
                // 已存在消息 → 全量重建（本卡按钮消失 + 前一卡成为最新未决恢复可点）
                if (_vm != null) { _vm.Messages.Clear(); RefreshMessages(); }
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
                // 🔴 2026-08-11（Q3）：同意后自动关闭 IM——开打了玩家该盯屏幕，而不是手动关面板
                Close();
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

        /// <summary>🔴 2026-08-12（合并闲聊/计划模式）：needPlan 建议按钮（制定计划/先不用）。
        /// 制定计划 → RequestCommand（命令 = 玩家原话 CommandText，私聊玩家消息不在 store 必须冗余存；
        /// Mission = LLM 计划管线；Campaign = 行军令）；先不用 → 了结回闲聊（密谋互斥释放，同「切回闲聊」语义）。
        /// 命令批准后的互斥释放由既有 Resolve 处理（End），此处不重复调。</summary>
        public static void HandleSuggestion(ImMessage msg, bool makePlan)
        {
            if (msg == null || !msg.IsPlanSuggest || msg.IsSuggestionResolved) return;
            var conv = ConversationOf(msg.ConvId);
            if (conv == null) return;
            msg.ExecutorId = "done";
            // 按钮行是重建式数据 → 全量重建（本消息按钮消失 + 前一张未决卡恢复可点）
            if (_vm != null) { _vm.Messages.Clear(); RefreshMessages(); }
            if (!makePlan)
            {
                // 先不用：放弃密谋输入阶段（互斥解除；已批准的执行不受影响）
                PlanCommandFlow.End();
                return;
            }
            string command = string.IsNullOrWhiteSpace(msg.CommandText) ? msg.Content : msg.CommandText;
            if (string.IsNullOrWhiteSpace(command)) return;
            ImCommandFlow.RequestCommand(conv, command);
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
