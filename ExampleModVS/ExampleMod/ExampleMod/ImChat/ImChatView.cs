using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.GauntletUI.GamepadNavigation;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>🔴 2026-08-15（缩略模式）：IM 面板形态——完整模式（1000x640 微信式）/ 缩略模式（贴底小面板，战斗观察用）。</summary>
    public enum ImChatMode
    {
        Full,
        Compact,
    }

    /// <summary>
    /// IM 静态 UI 管理器（NinjaNotification 同款：TopScreen.AddLayer，Mission/Campaign 通吃，需求 1）。
    /// - 打开/关闭/切换：热键（ModInput IM 玩法行）与通知点击；
    /// - 战斗/模态禁开（用户决策 4）：Mission 内 IsInteractionDisabled() + 系统弹窗检查；
    /// - Tick 驱动（ImChatMissionView / ImChatCampaignBehavior）：回复管线 + 0.3s 增量刷新；
    /// - 新消息通知（🔴 2026-08-17 用户裁定）：IM 关闭时来消息（私聊+群聊）统一由呼出按钮
    ///   （ImChatOpenButtonManager）未读徽标跳动提示——ninjareport 圆环与 IM 消息 NinjaNotification
    ///   横幅已一并废除（NinjaNotificationManager 本体保留：WorldEvent/Quest 通用通知仍在使用）。
    /// - 🔴 2026-08-15（缩略模式）：同一 layer 换 prefab 切换形态（SwitchMode）；
    ///   缩略面板输入安全靠引擎 hit-test 门控（面板矩形外场景输入不被吞）+ ClearFocus 释放键盘；
    ///   🔴 2026-08-17（Q3）：门控对原始鼠标轮询不成立，缩略层 mask 改位置感知（HitTest 命中才拦 Mouse）。
    /// </summary>
    public static class ImChatView
    {
        private static GauntletLayer _layer;
        /// <summary>层挂载时的 owner Screen（Open() 记录，Close() 置 null）——TopScreen 切换检测用
        /// （GauntletLayer 无 Screen 属性，反编译 ScreenBase 核实；2026-08-17 家族 UI 崩溃修复）。</summary>
        private static ScreenBase _layerOwnerScreen;
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
        // 🔴 2026-08-15（按钮调试日志节流）：上次已打日志的 latestCard 引用——锚点竞争结果只打一次（防 0.3s 轮询刷屏）
        private static ImMessage _lastAnchorLogCard;
        // 🔴 2026-08-12（模板 NPC 密信 · 粘性 @）：最近一次定向喊话的 @前缀（含尾随空格，如「@守卫 #12 」）——
        // @命中发送后回填输入框，连发多条给同一 NPC 不用重复打 @；玩家删掉前缀发普通喊话 → 解除。
        private static string _lastMentionPrefix;

        // 🔴 2026-08-15（缩略模式）：形态状态 + 面板 widget 缓存
        private static ImChatMode _mode = ImChatMode.Full;
        private static Widget _compactPanel;        // 缩略面板（矩形判定）
        private static Widget _compactChannelList;  // 频道列表（上开式，外部点击收起矩形判定 + 手动命中）
        private static InputUsageMask _lastCompactMask; // 🔴 2026-08-15（性能）：mask 缓存，变化才 SetInputRestrictions
        private static readonly List<ImConversation> _compactChannels = new List<ImConversation>(); // 下拉频道顺序（左右箭头循环用）

        // 🔴 Q2（2026-08-17，密信入口「关屏再开」）：队伍/家族屏点密信 → PopScreen 后待打开的私聊目标
        //（不立即 Open——PopScreen 后底层屏激活是帧边界异步的，过渡期内 IM 层叠在空屏上 = 黑屏，
        //  实测 2026-08-17；覆盖式语义：连点多个按钮后点者胜；~2s 超时丢弃防永久卡住）
        private static string _pendingSecretLetterHeroId;
        private static float _pendingSecretLetterElapsed;
        private const float PendingSecretLetterTimeoutSec = 2f;

        // 🔴 Q4（2026-08-17，手柄支持）：
        // 手柄模态（Mission 面板打开 = 角色输入整体冻结；设备切换自动解冻/冻结）
        private static bool _lastUsingGamepad;
        private static bool _playerFrozen;
        // 手柄导航焦点视觉（LastTargetedWidget 轮询 → 高亮 Hovered 复用 hover 视觉；关闭/失焦复位）
        private static Widget _lastPadFocus;

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

        /// <summary>Mission/大地图均可开（需求 1）；战斗中/系统弹窗中禁开（用户决策 4）。
        /// 🔴 2026-08-15（用户裁定）：MCM 密聊开关（PlotEnabled）关闭时 O 无法呼出聊天——
        /// 密聊入口整体隐藏（含通知点击路径，OpenCompact 也走 CanOpen）。</summary>
        public static bool CanOpen()
        {
            try
            {
                if (!Settings.Instance.PlotEnabled) return false;
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
                // 🔴 2026-08-17（Q4 手柄）：{OPEN_KEY} 动态 = 当前设备呼出键字形（O / ↑）——
                // 手柄设备玩家看到「↑ 打开」而非「O 打开」
                if (!_welcomed)
                {
                    _welcomed = true;
                    string openKey = ModInput.Glyph(InteractionIds.IM);
                    // 首次打开引导（LWN_im_first_open）
                    string hint = LWNTextHelper.ResolveCompound("LWN_im_first_open",
                        "Secret messaging (IM) — talk to heroes across the land, or send secret orders to companions. Open with {OPEN_KEY}; close with ESC, B, or by clicking outside the panel.",
                        ("OPEN_KEY", openKey));
                    InformationManager.DisplayMessage(new InformationMessage(hint));
                }

                _vm = new ImChatVM();
                // 🔴 层级 400：反编译 SandBox.GauntletUI.dll 实测原生层序——地图名标 90 / 菜单 100 /
                // MapBar+定居点菜单覆盖层(MapMenuOverlay) 202 / 地图对话 205 / 百科 310 / 系统菜单 4400。
                // 原 20 会被定居点菜单和地图 HUD 盖住（玩家报告）；400 高于全部地图玩法 UI，低于系统菜单。
                _layer = V.NewLayer(400, "ImChatLayer");
                // LoadMovie 字符串 = GUI/Prefabs/<名>.xml 文件名（不带后缀）；返回类型跨版本不同（同上 #if）
                // 🔴 2026-08-15（缩略模式）：按 _mode 选 prefab（缩略 = ImChatCompact.xml）
                _movie = _layer.LoadMovie(_mode == ImChatMode.Compact ? "ImChatCompact" : "ImChat", _vm);
                // 🔴 输入限制：MouseButtons|MouseWheels|Keyboardkeys——滚轮必须留在 IM 层（含 MouseWheels 位，
                // 否则穿透到地图层触发镜头缩放，六轮实机修复）；滚动派发由 MessageClip DoNotAcceptEvents 保证
                // （EventManager.MouseScroll 只调用 hit test 命中的 widget，不冒泡）。
                // 🔴 2026-08-15（缩略模式）：缩略层去掉 MouseWheels（面板无滚动内容，滚轮穿透到场景 = 镜头缩放，
                // 下拉开时 Tick 内补上 MouseWheels 位给列表滚动）；键盘靠 FocusTest 门控（输入框聚焦才吃键）。
                // 🔴 2026-08-17（Q3 位置感知 mask 实锤）：缩略层不再常驻 Mouse——「hit-test 门控 → 面板矩形外
                // 场景输入天然不被吞」只对 UI 事件分发成立，对原始轮询不成立：鼠标键在 native 层有「UI 捕获」
                // 判定，与鼠标位置无关（pitfalls 2026-08-11 实机记录 + 用户反馈双证）。初始只挂 Keyboardkeys
                //（键盘拦不住物理轮询，留着不影响 WASD），Mouse/MouseWheels 由 HandleCompactInput 每帧
                // 位置感知修正（HitTest 命中面板才拦）。完整模式保持模态语义不变（三件套全拦）。
                InputUsageMask mask = _mode == ImChatMode.Compact
                    ? InputUsageMask.Keyboardkeys
                    : InputUsageMask.MouseButtons | InputUsageMask.MouseWheels | InputUsageMask.Keyboardkeys;
                _layer.InputRestrictions.SetInputRestrictions(true, mask);
                if (_mode == ImChatMode.Compact) _lastCompactMask = InputUsageMask.Keyboardkeys;
                if (ScreenManager.TopScreen != null)
                {
                    ScreenManager.TopScreen.AddLayer(_layer);
                    _layerOwnerScreen = ScreenManager.TopScreen;
                }

                // 🔴 2026-08-16（用户裁定：唤起保持上次频道）：selectConv 未指定时优先恢复上次选中
                //（Close 保留的 _selected），无历史才回队伍兜底
                SelectConversation(selectConv ?? _selected ?? BuildDefaultConversation());
                if (prefill != null && _vm != null) _vm.InputText = prefill;

                // 🔴 Q4（2026-08-17，手柄模态）：Mission 内面板打开 = 角色输入整体冻结（键盘已证
                // InputRestrictions 拦不住手柄键，SetPlayerControlFrozen 是确定性方案；冻结只冻角色 Agent，
                // 不冻 UI 层导航——实机验证点）；设备切换在 Tick 内检测（UpdateGamepadFreeze 幂等）。
                _lastUsingGamepad = ModInput.UsingGamepad;
                UpdateGamepadFreeze();
                _vm?.RefreshPadHint();
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
            // 🔴 Q4（2026-08-17）：手柄模态解冻（关闭面板 = 角色输入还给玩家；Mission 可能已退出，
            // Agent.Main 判空兜底）。幂等（_playerFrozen 门控）。
            if (_playerFrozen)
            {
                _playerFrozen = false;
                try { if (Agent.Main != null) V.SetPlayerControlFrozen(Agent.Main, false); } catch (Exception ex) { DebugLogger.Log($"[ImChat] 解冻失败: {ex.Message}"); }
            }
            // 🔴 Q2（2026-08-17）：手动关闭 → 待打开的密信目标作废（防关屏后再开 IM 的意外弹出）
            _pendingSecretLetterHeroId = null;
            _pendingSecretLetterElapsed = 0f;
            _lastPadFocus = null;   // 面板关闭 → 焦点视觉缓存复位（widget 树销毁）
            if (_layer != null)
            {
                try
                {
                    if (_movie != null)
                    {
                        _layer.ReleaseMovie(_movie);
                        _movie = null;
                    }
                    // 🔴 2026-08-17（实机崩溃修复）：从层实际挂载的屏摘（_layerOwnerScreen），不用
                    // ScreenManager.TopScreen——层可能挂在非 TopScreen 的屏上（家族/队伍屏 Push 叠层），
                    // 从 TopScreen 摘 = 摘错屏 + 层 Finalize 却残留在 owner 屏 _layers → owner 屏下次
                    // 激活（PopScreen 回地图）遍历死层 → GauntletLayer.OnActivate NRE 崩溃（实机）。
                    // HasLayer 校验：层可能已随屏销毁（PopScreen），跳过摘除。
                    if (_layerOwnerScreen != null && _layerOwnerScreen.HasLayer(_layer))
                        _layerOwnerScreen.RemoveLayer(_layer);
                    else if (ScreenManager.TopScreen != null && ScreenManager.TopScreen.HasLayer(_layer))
                        ScreenManager.TopScreen.RemoveLayer(_layer);
                    _layer.InputRestrictions.ResetInputRestrictions();
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[ImChat] Close 失败: {ex.Message}");
                }
                _layer = null;
                _layerOwnerScreen = null;
            }
            _vm = null;
            // 🔴 2026-08-16（用户裁定：唤起保持上次频道）：_selected **不置 null**——关闭时保留选中
            // 会话（纯数据引用：Id/Type/Title，跨开关持久有效），再次 Open 恢复；Close 只销毁层与 VM。
            // 旧行为每次唤起回队伍频道（BuildDefaultConversation），私聊/其他频道丢失。
            _messageScrollPanel = null;   // 层关闭后 widget 树失效，缓存清空
            _compactPanel = null;         // 🔴 2026-08-15（缩略模式）：同埋——widget 树随层销毁
            _compactChannelList = null;
        }

        // ───────────────────────── 层归属迁移 / 手柄模态 / 焦点视觉（2026-08-17）─────────────────────────

        /// <summary>
        /// 🔴 Q2（2026-08-17，密信入口「关屏再开」）：密信按钮点击后调用——先 PopScreen 关掉队伍/家族屏，
        /// 目标 Hero 存入 pending（不立即 Open——PopScreen 后底层屏激活是帧边界异步的，过渡期内 IM 层叠在
        /// 空屏上 = 黑屏实测）；OnScreenFrameTick 检测 TopScreen 已稳定（回 MapScreen）→ Open 定位私聊。
        /// 覆盖式语义：连点多个密信按钮 → 后点者胜。TouchDirectChat 在此登记（关屏前），保证左栏私聊列表可寻。
        /// </summary>
        public static void SetPendingSecretLetter(string heroId)
        {
            _pendingSecretLetterHeroId = heroId;
            _pendingSecretLetterElapsed = 0f;
            if (!string.IsNullOrEmpty(heroId))
                ImChatStore.TouchDirectChat(heroId, ImChatManager.NowUnixMs());
        }

        /// <summary>
        /// 🔴 2026-08-17（B'：层归属迁移提升）：Open() 把层挂到当时的 TopScreen——在家族/队伍屏打开 IM 时
        /// 层叠在 ClanScreen/PartyScreen 上；点完成关屏 → PopScreen 销毁其层 → _layer C# 引用仍在但
        /// native 已释放 → 后续 Tick 访问死 widget 抛 NRE（2026-08-17 家族 UI 崩溃修复，原只在缩略分支，
        /// 完整模式无保护 → 滚动静默失效）。
        /// 🔴 2026-08-17（实机日志定位）：判定 = **IsFinalized（权威死层标志）|| owner 屏已不持有层**——
        /// HasLayer 单独使用有盲区（ScreenBase 销毁时层仍在 _layers 列表，对已 Finalize 的层误报 true）；
        /// IsFinalized 捕捉死层，HasLayer 捕捉屏销毁。面板层**保留 Push 叠层不误杀**（家族/队伍屏
        /// Push 时面板被原版屏层序盖住属正常，关屏后自然回来——与按钮层「重挂零成本」不同，面板
        /// Close 会丢玩家状态，所以不按 TopScreen 判定）。
        /// </summary>
        private static void MigrateLayerIfNeeded()
        {
            if (_layer == null || _layerOwnerScreen == null) return;
            bool held = false;
            try { held = _layerOwnerScreen.HasLayer(_layer); } catch { }
            bool finalized = false;
            try { finalized = _layer.IsFinalized; } catch { finalized = true; }
            if (held && !finalized) return;   // 层还活着（owner 屏仍在，即使不是 TopScreen）→ 不动
            DebugLogger.Log($"[ImChat] 层已失效（owner={_layerOwnerScreen.GetType().Name} held={held} Finalized={finalized}），关闭面板");
            Close();
        }

        /// <summary>
        /// 🔴 Q4（2026-08-17，手柄模态）：Mission 内面板打开且手柄在用 → 角色输入整体冻结
        ///（SetPlayerControlFrozen，幂等——_playerFrozen 门控；冻结只冻角色 Agent，不冻 UI 层导航）。
        /// 解冻时机 = 设备切回键盘/鼠标 / Close / Mission 退出（Close 路径已有，Mission 退出自动恢复）。
        /// 大地图手柄不冻结：十字键分流（引擎导航方向只认十字键，左摇杆 = 地图移动照常，反编译实锤）
        /// ——完美分流，无模态需求。
        /// </summary>
        private static void UpdateGamepadFreeze()
        {
            if (_layer == null) return;
            bool shouldFreeze = ModInput.UsingGamepad && Mission.Current != null;
            if (shouldFreeze == _playerFrozen) return;
            _playerFrozen = shouldFreeze;
            try
            {
                if (Agent.Main != null)
                    V.SetPlayerControlFrozen(Agent.Main, shouldFreeze);
                DebugLogger.Log($"[ImChat] 手柄模态 {(shouldFreeze ? "冻结" : "解冻")} 玩家控制");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] 手柄模态冻结失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 🔴 Q4（2026-08-17，焦点视觉，用户裁定：焦点必须可见——否则玩家看不到光标在哪）：
        /// 引擎 ButtonWidget.RefreshState 只处理 Disabled/Selected/Pressed/Hovered/Default，无 Focused 分支
        ///（反编译实锤）——手柄导航聚焦不自动高亮。轮询 GauntletGamepadNavigationManager.Instance.LastTargetedWidget
        ///（全局活跃 scope 的聚焦项，public 实锤），聚焦自己面板的按钮 → SetState("Hovered") 复用 hover
        /// 视觉（零新 Brush）；旧焦点按身份复位（选中行回 Selected，其余回 Default）。
        /// 🔴 守卫：① UsingGamepad 门控（鼠标 hover 由引擎自己管 + RefreshState，我们不碰，键盘/鼠标不跑此逻辑）；
        /// ② IsOurPanelWidget 向上找 LWN 根——LastTargetedWidget 可能指向面板外原版按钮（鼠标在 scope 区域外移动），
        ///    对原版按钮 SetState 会造成视觉错乱；③ null（引擎：鼠标不在聚焦按钮范围且无导航动画）→ 当焦点丢失复位。
        /// 🔴 残留缝隙（设备切换瞬间）：动鼠标 → UsingGamepad 立即 false → 轮询停，最后高亮可能残留——本方法
        /// 非手柄分支把 _lastPadFocus 置 null 即清理（下一帧引擎自己刷新原版按钮状态）。
        /// </summary>
        private static void UpdatePadFocus()
        {
            if (!ModInput.UsingGamepad)
            {
                _lastPadFocus = null;
                return;
            }
            Widget padFocus = null;
            try { padFocus = GauntletGamepadNavigationManager.Instance?.LastTargetedWidget; } catch { }
            if (padFocus == _lastPadFocus) return;
            if (_lastPadFocus != null && _lastPadFocus != padFocus)
            {
                try { _lastPadFocus.SetState(_lastPadFocus is ButtonWidget b && b.IsSelected ? "Selected" : "Default"); } catch { }
            }
            _lastPadFocus = padFocus;
            if (padFocus != null)
            {
                if (IsOurPanelWidget(padFocus))
                {
                    try { padFocus.SetState("Hovered"); } catch { }
                }
                else
                {
                    _lastPadFocus = null;   // 外部 widget：不高亮（引擎自己管原版按钮状态）
                }
            }
        }

        /// <summary>焦点视觉守卫：widget 是否属于本 IM 面板树（向上找 Id="LWN" 的 Window 根）。</summary>
        private static bool IsOurPanelWidget(Widget w)
        {
            for (Widget p = w; p != null; p = p.ParentWidget)
            {
                if (p.Id == "LWN") return true;
            }
            return false;
        }

        // ───────────────────────── 模式切换（完整 ⇄ 缩略）─────────────────────────

        /// <summary>
        /// 🔴 2026-08-15（缩略模式）：模式切换集中清理（审查 P1-3），顺序不可乱：
        /// ① ReleaseMovie 先于 LoadMovie（同 name 双 movie 并存会双面板叠显）；
        /// ② _movie 必须赋新值（否则 Close() 的 ReleaseMovie 因 Contains=false 静默 no-op，新 movie 泄漏）；
        /// ③ widget 缓存清空（HandleManualScroll 只在 null 时重查，旧引用指向已释放树 = 操作死 widget）；
        /// ④ 清焦点防旧输入框悬挂；⑤ 重放拖动位置；⑥ 下拉状态复位（防新 prefab 加载即展开）；⑦ RefreshAll。
        /// </summary>
        private static void SwitchMode()
        {
            if (_layer == null || _vm == null) return;
            try
            {
                if (_movie != null)
                {
                    _layer.ReleaseMovie(_movie);
                    _movie = null;
                }
                if (_layer.UIContext?.EventManager != null)
                    _layer.UIContext.EventManager.ClearFocus();
                _movie = _layer.LoadMovie(_mode == ImChatMode.Compact ? "ImChatCompact" : "ImChat", _vm);
                _messageScrollPanel = null;
                _compactPanel = null;
                _compactChannelList = null;
                    RefreshAll();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] 模式切换失败: {ex.Message}");
            }
        }

        /// <summary>完整模式 → 缩略模式（标题带「缩略」按钮，关闭按钮左侧）。</summary>
        public static void ToggleCompact()
        {
            if (_mode == ImChatMode.Compact) return;
            _mode = ImChatMode.Compact;
            SwitchMode();
        }

        /// <summary>缩略模式 → 完整模式（缩略标题行「放大」按钮）。</summary>
        public static void ToggleExpand()
        {
            if (_mode != ImChatMode.Compact) return;
            _mode = ImChatMode.Full;
            SwitchMode();
        }

        /// <summary>以缩略模式打开并定位会话（密信通知点击入口）。
        /// 🔴 2026-08-17（用户裁定）：ninjareport 密信通知已废除（私聊通知统一由呼出按钮徽标承担），
        /// 本入口当前无调用者——保留为「缩略模式定位会话」的公开入口（未来缩略入口可复用）。</summary>
        public static void OpenCompact(ImConversation conv)
        {
            if (IsOpen)
            {
                ToggleCompact();
                SelectConversation(conv);
                return;
            }
            _mode = ImChatMode.Compact;
            Open(conv);
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
            // 🔴 2026-08-12（粘性 @ 按会话）：@前缀只在附近频道保留——切走时输入框若还是纯前缀 → 清空
            //（不丢玩家打字内容）；切回附近频道 → 恢复前缀（用户裁定：切到队伍前缀自动空、切回又有了）
            if (_vm != null)
            {
                if (conv != null && conv.Type == ImConversationType.Nearby && _lastMentionPrefix != null)
                    _vm.InputText = _lastMentionPrefix;
                else if (_lastMentionPrefix != null && _vm.InputText == _lastMentionPrefix)
                    _vm.InputText = "";
                // 🔴 2026-08-17（实机：缩略模式单聊之间切换聊天记录不刷新）：**切会话必须清空消息流**——
                // RefreshMessages 的增量逻辑只在「消息变少」或「generating 转态」时重建；切到消息数
                // ≥ 旧会话的私聊 → 旧消息残留 + 新消息追加 = 显示旧会话记录（缩略模式行 B 也取
                // _vm.Messages，同样残留）。群聊切频道恰好消息数少触发重建，掩盖了此 bug。
                _vm.Messages.Clear();
                _hadGenerating = false;   // 转态检测基线重置（防旧会话 generating 残留误判）
            }
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
            string title = _selected?.Title ?? "";
            // 🔴 2026-08-15（私聊标题好感，用户需求）：私聊会话标题 = NPC 名字 + 当前好感
            //（玩家视角 MainHero.GetRelation，正负都显示：+42 / -15）。左栏列表标题不动（空间有限，
            // 预览行已够用）；只在打开会话后的顶部标题带显示。模板 NPC 无 Hero → 原样（防 null）。
            if (_selected != null && _selected.Type == ImConversationType.Direct
                && !string.IsNullOrEmpty(_selected.PartnerHeroId))
            {
                try
                {
                    var hero = TaleWorlds.CampaignSystem.Hero.AllAliveHeroes
                        .FirstOrDefault(h => h.StringId == _selected.PartnerHeroId);
                    if (hero != null && Hero.MainHero != null)
                    {
                        int rel = Hero.MainHero.GetRelation(hero);
                        string relText = rel > 0 ? "+" + rel.ToString() : rel.ToString();
                        // 本地化：私聊标题好感（LWN_im_title_relation，{NAME}/{REL} 变量）
                        title = LWNTextHelper.ResolveCompound("LWN_im_title_relation",
                            "{NAME} ({REL} relation)", ("NAME", title), ("REL", relText));
                    }
                }
                catch { /* 好感获取失败 → 原样标题 */ }
            }
            // 🔴 2026-08-16（用户裁定：标题显示人数）：群聊频道（队伍/家族/王国）标题 = 频道名 + 当前
            // 成员数（算上玩家自己）——人多人少一目了然。成员口径 = GetChannelMembers（队伍 = 主队 roster
            // Hero，分兵随从掉出不计，与群聊成员口径一致）；切换频道时刷新（RefreshTitle 由 SelectConversation 驱动）。
            if (_selected != null && _selected.Type != ImConversationType.Direct)
            {
                try
                {
                    int count = ImChatManager.GetChannelMembers(_selected.Type).Count + 1; // +1 玩家自己
                    // 本地化：LWN_im_title_members（玩家可见文本）
                    title = LWNTextHelper.ResolveCompound("LWN_im_title_members",
                        "{NAME} ({COUNT})", ("NAME", title), ("COUNT", count.ToString()));
                }
                catch { /* 人数获取失败 → 原样标题 */ }
            }
            _vm.Title = title;
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
                RefreshCompact();
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
            RefreshCompact();
        }

        /// <summary>
        /// 🔴 2026-08-15（缩略模式）：刷新缩略面板数据（消息区两行 + 频道下拉）。
        /// 锚点行 = _vm.Messages 中 IsCardAnchor==true 的实例（含按钮行）；
        /// 行 B = 最近 1~2 条消息（用户裁定：有 2 条显示 2 条，给回复提供基础上下文；锚点消息本身
        /// 不在列表里——行 A 有完整形态，列表取其之前的消息）。
        /// 🔴 实例必须复用 _vm.Messages 的同一实例（审查 P1-2）——UpdateCardAnchors /
        /// NotifyMessageShapeChanged / NotifyPlanStateChanged 的广播都打在那些实例上，
        /// 独立新实例会漏广播 → 按钮不显示类 bug 复现。RefreshMessages 两个出口都调本方法。
        /// </summary>
        private static void RefreshCompact()
        {
            if (_vm == null || _selected == null) return;
            // 锚点 vm（UpdateCardAnchors 已把最新可操作卡片的 IsCardAnchor 置 true）
            ImMessageVM anchorVm = null;
            for (int i = _vm.Messages.Count - 1; i >= 0; i--)
            {
                var vm = _vm.Messages[i];
                if (vm != null && vm.IsCardAnchor) { anchorVm = vm; break; }
            }
            _vm.CompactAnchor = anchorVm;
            _vm.HasCompactAnchor = anchorVm != null;

            // 行 B：最近 1~2 条非锚点消息（从最新往前取，锚点消息跳过；🔴 2026-08-15 用户裁定
            // 修复：顺序反转——聊天流旧在上新在下，我原实现把最新放最上 = 后说的出现在上面）。
            // 🔴 增量：集合内容不变时不 Clear+Add（MBBindingList 重建项 widget，0.3s 刷新会抖动）
            var target = new List<ImMessageVM>();
            for (int i = _vm.Messages.Count - 1; i >= 0 && target.Count < 2; i--)
            {
                var vm = _vm.Messages[i];
                if (vm == null) continue;
                if (anchorVm != null && vm.Message == anchorVm.Message) continue;
                target.Add(vm);
            }
            target.Reverse();   // 旧 → 新（上 → 下）
            bool changed = target.Count != _vm.CompactLatestMessages.Count;
            if (!changed)
            {
                for (int k = 0; k < target.Count; k++)
                {
                    if (_vm.CompactLatestMessages[k] != target[k]) { changed = true; break; }
                }
            }
            if (changed)
            {
                _vm.CompactLatestMessages.Clear();
                foreach (var v in target) _vm.CompactLatestMessages.Add(v);
            }
            _vm.HasCompactLatest = target.Count > 0;

            RefreshChannelOptions();
        }

        /// <summary>缩略模式频道下拉项重建（顺序与完整模式左栏一致：附近/队伍/家族/王国/私聊；
        /// 同时维护 <see cref="_compactChannels"/> 供左右箭头循环切换）。
        /// 🔴 2026-08-15（用户裁定 hover 闪烁修复）：**增量刷新**——按 ConversationId 复用既有
        /// ImChannelOptionVM 实例，只更新 StringItem（未读数）；0.3s 全量重建会让项 widget 被销毁重建，
        /// hover 高亮每 0.3s 重置一次 = 底色闪烁。原版控件自己读 ItemList/SelectedIndex（双向绑定）。</summary>
        private static void RefreshChannelOptions()
        {
            if (_vm == null) return;
            var convs = new List<ImConversation>();
            if (Mission.Current != null) convs.Add(NearbyFeed.Conversation);
            convs.Add(ImChatManager.GetGroupConversation(ImConversationType.Party));
            convs.Add(ImChatManager.GetGroupConversation(ImConversationType.Clan));
            if (ImChatManager.CanSeeKingdomChannel())
                convs.Add(ImChatManager.GetGroupConversation(ImConversationType.Kingdom));
            convs.AddRange(ImChatManager.GetRecentDirectConversations());

            _compactChannels.Clear();
            foreach (var conv in convs)
            {
                if (conv == null) continue;
                _compactChannels.Add(conv);
                // 增量：已存在的项复用实例（widget 不重建 → hover 稳定），只刷未读数
                ImChannelOptionVM item = null;
                for (int i = 0; i < _vm.ChannelSelector.ItemList.Count; i++)
                {
                    if (_vm.ChannelSelector.ItemList[i].ConversationId == conv.Id) { item = _vm.ChannelSelector.ItemList[i]; break; }
                }
                string t = conv.Title ?? "";
                int unread = ImChatStore.GetUnread(conv.Id);
                if (unread > 0) t = $"{t} ({unread})";
                if (item == null)
                    _vm.ChannelSelector.ItemList.Add(new ImChannelOptionVM(conv) { StringItem = t });
                else
                    item.StringItem = t;
            }
            // 移除已不存在的项（会话列表变化）
            for (int i = _vm.ChannelSelector.ItemList.Count - 1; i >= 0; i--)
            {
                bool stillExists = false;
                for (int j = 0; j < convs.Count; j++)
                {
                    if (convs[j] != null && _vm.ChannelSelector.ItemList[i].ConversationId == convs[j].Id) { stillExists = true; break; }
                }
                if (!stillExists) _vm.ChannelSelector.ItemList.RemoveAt(i);
            }
            // 选中索引同步（中心文本 + 每项 IsSelected 高亮 + SelectedIndex）
            int selIdx = -1;
            if (_selected != null)
            {
                for (int i = 0; i < _compactChannels.Count; i++)
                {
                    if (_compactChannels[i].Id == _selected.Id) { selIdx = i; break; }
                }
            }
            if (_vm.ChannelSelector.SelectedIndex != selIdx)
                _vm.ChannelSelector.SelectedIndex = selIdx;
            // 中心按钮文本：选中频道标题 + 未读数
            string selText = _selected?.Title ?? "";
            if (_selected != null)
            {
                int unread = ImChatStore.GetUnread(_selected.Id);
                if (unread > 0) selText = $"{selText} ({unread})";
            }
            if (_vm.ChannelSelector.SelectedChannelText != selText)
                _vm.ChannelSelector.SelectedChannelText = selText;
            // 每项选中高亮（Radio 视觉）
            for (int i = 0; i < _vm.ChannelSelector.ItemList.Count; i++)
            {
                bool isSel = i == selIdx;
                if (_vm.ChannelSelector.ItemList[i].IsSelected != isSel)
                    _vm.ChannelSelector.ItemList[i].IsSelected = isSel;
            }
            // 列表高度随项数自适应：每项 34px + 上下边距 16，钳制 [60, 348]
            _vm.ChannelSelector.ChannelListHeight = MathF.Clamp(_vm.ChannelSelector.ItemList.Count * 34f + 16f, 60f, 348f);
        }

        /// <summary>原版下拉选中（CurrentSelectedIndex → SelectedIndex 双向绑定回调）→ 切会话。
        /// 🔴 2026-08-15（实机「第三项选不中」取证）：打印收到的索引与频道数。</summary>
        public static void SelectChannelByIndex(int index)
        {
            DebugLogger.Log($"[CompactSelect] SelectChannelByIndex({index}) 频道数={_compactChannels.Count}");
            if (index < 0 || index >= _compactChannels.Count) return;
            SelectConversation(_compactChannels[index]);
        }

        /// <summary>左箭头：上一个频道（循环）。</summary>
        public static void SelectPreviousChannel() => SelectRelativeChannel(-1);

        /// <summary>右箭头：下一个频道（循环）。</summary>
        public static void SelectNextChannel() => SelectRelativeChannel(1);

        private static void SelectRelativeChannel(int delta)
        {
            if (_compactChannels.Count == 0 || _selected == null) return;
            int idx = _compactChannels.FindIndex(c => c != null && c.Id == _selected.Id);
            if (idx < 0) return;
            int next = (idx + delta + _compactChannels.Count) % _compactChannels.Count;
            SelectConversation(_compactChannels[next]);
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
        /// 🔴 2026-08-15（按钮不显示根因修复，实机 08:56:03 日志复盘）：消息**上屏后**被打标
        /// （needPlan 建议 TryAttachSuggestion / 宾语确认等运行时形态变化）→ 该消息 VM 的
        /// ShowCardBubble（卡片气泡容器可见性）是计算属性，打标时无 OnPropertyChanged → 容器
        /// 一直保持"普通气泡"渲染，按钮行（容器内）不可见；切面板重开 = 全量重建才恢复。
        /// 本方法：打标后立即重算锚点（IsPlanSuggest 参与竞争）+ 广播形态属性，一帧内按钮可用。
        /// </summary>
        public static void NotifyMessageShapeChanged(ImMessage msg)
        {
            if (_vm == null || msg == null) return;
            try
            {
                UpdateCardAnchors();
                foreach (var vm in _vm.Messages)
                {
                    if (vm != null && vm.Message == msg)
                    {
                        vm.OnPropertyChanged(nameof(ImMessageVM.IsPlanSuggest));
                        vm.OnPropertyChanged(nameof(ImMessageVM.IsTargetConfirm));
                        vm.OnPropertyChanged(nameof(ImMessageVM.ShowCardBubble));
                        vm.OnPropertyChanged(nameof(ImMessageVM.ShowOtherBubble));
                        vm.OnPropertyChanged(nameof(ImMessageVM.ShowSelfBubble));
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] NotifyMessageShapeChanged 异常: {ex.Message}");
            }
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
                // 🔴 2026-08-13（模板 NPC 目标确认）：宾语确认消息（待选）同参与竞争——最新者接管
                else if (m.IsTargetConfirm && !m.IsTargetConfirmResolved) latestCard = m;
                // 🔴 2026-08-15（ask_player 询问步骤）：密信决策卡（待点选）同参与竞争——最新者接管
                else if (m.IsAskPlayerCard && !m.IsAskPlayerCardResolved) latestCard = m;
            }
            foreach (var vm in _vm.Messages)
            {
                var m = vm.Message;
                ImMessage card = null;
                if (m != null)
                {
                    if (m.IsPlanCard || m.IsProposal || m.IsPlanSuggest || m.IsTargetConfirm || m.IsAskPlayerCard)
                        card = m;
                    else if (m.IsPlanChainMessage)
                    {
                        foreach (var x in msgs)
                        {
                            if (x != null && x.IsPlanCard && x.ChainId == m.ChainId) { card = x; break; }
                        }
                    }
                }
                // 🔴 2026-08-15（按钮不显示根因，实机日志 08:38:42.962-964 实锤）：**先设 IsCardAnchor 再设
                // AnchorCard**——旧顺序 AnchorCard 先触发 RebuildCardButtons（按钮构建时 IsCardAnchor 还是
                // 旧值 False，[SuggestBtn] 日志显示 IsHorizontalButtons=False），随后 IsCardAnchor 才设 True，
                // 可见性联动丢失。反转后：可见性先就绪，按钮数据后到（MBBindingList 添加自动刷新数据源）。
                bool isAnchor = card != null
                    && card == latestCard
                    && IsCardAnchorPosition(m, card, msgs);
                vm.IsCardAnchor = isAnchor;
                vm.AnchorCard = card;
            }
            // 🔴 2026-08-15（按钮不显示调试，实机）：打印锚点竞争结果——latestCard 是谁、
            // 被选中的消息是否就是玩家看到的建议消息（IsPlanSuggest + 未解决）。
            // ⚠️ 节流：UpdateCardAnchors 每 0.3s 轮询跑一次，latestCard 不变时打印会刷屏——
            // 只在 latestCard 变化（打标前 null → 打标后建议消息）时打一次。
            if (latestCard != null && _lastAnchorLogCard != latestCard)
            {
                _lastAnchorLogCard = latestCard;
                DebugLogger.Log($"[CardAnchor] latestCard={latestCard.Kind}(suggest={latestCard.IsPlanSuggest}, resolved={latestCard.IsSuggestionResolved}, exec={latestCard.ExecutorId ?? "空"}) 消息数={msgs.Count}");
                foreach (var vm in _vm.Messages)
                {
                    var m = vm.Message;
                    if (m != null && (m.IsPlanSuggest || m.IsProposal || m.IsPlanCard))
                        DebugLogger.Log($"[CardAnchor]   vm: kind={m.Kind} sender={m.SenderName} content={(m.Content?.Length > 20 ? m.Content.Substring(0, 20) + "…" : m.Content)} anchor={vm.IsCardAnchor} card={vm.AnchorCard != null}");
                }
            }
        }

        /// <summary>本消息是否为卡片锚点位置：提议/建议 = 自身；计划 = 链内最新一条（🔴 2026-08-12 修复：
        /// 只扫 m **之后**的消息——原实现扫全表，卡片自身同链 → 讲解消息永远判定「后面还有同链消息」
        /// → 按钮全消失）。旧格式卡片无 ChainId = 仅自身。</summary>
        private static bool IsCardAnchorPosition(ImMessage m, ImMessage card, List<ImMessage> msgs)
        {
            if (m == null || card == null) return false;
            // 提议/建议/宾语确认/ask_player 无链：自身即锚点
            if (card.IsProposal || card.IsPlanSuggest || card.IsTargetConfirm || card.IsAskPlayerCard) return true;
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
        /// Q5b：Campaign 大地图私聊「有独立 party 的 Hero」也可用——规则解析计划（零 LLM）。
        /// 🔴 临时止血（2026-08-11 用户裁定）：队伍频道/随从私聊仅 Mission 可切计划模式——
        /// Campaign 下频道计划无执行载体（规则解析计划仅私聊+独立 party 有效），随从计划被 MainParty 拦截，
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
                    if (Mission.Current == null && hero.PartyBelongedTo != null) return true; // Campaign 规则解析计划
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
                // 🔴 队伍屏/家族屏密信按钮注入（纯 C# 动态插入，0.3s 扫描节流；仅 Party/ClanScreen 生效）
                SecretLetterButtonInjector.TickInject(dt);
                // 🔴 Q5（2026-08-17 呼出按钮）：Campaign 侧驱动（Mission 侧由 ImChatMissionView 驱动）
                ImChatOpenButtonManager.Tick(dt);
                // 🔴 O 只负责「打开」：面板开着时输入 o 不再触发任何动作（打字不误关）
                // 🔴 2026-08-15（用户裁定）：MCM 密聊开关（PlotEnabled）关闭 → O 无法呼出聊天
                if (ModInput.ShortFired(InteractionIds.IM) && !IsOpen && Settings.Instance.PlotEnabled)
                    Open();

                // 🔴 Q2（2026-08-17，密信入口「关屏再开」）：队伍/家族屏 PopScreen 后回大地图，
                // 打开 IM 定位私聊。🔴 黑屏教训（方案 §2 + 实机复现 2026-08-17）：PopScreen 虽为同步
                //（反编译 ScreenManager.PopScreen 实锤：HandleActivate 同栈执行），但「延迟 0.1s 再开」
                // 仍被实测黑屏——保守化：要求 TopScreen 已是 MapScreen 且 IsActive 且稳定 ≥0.3s
                //（18 帧，给地图恢复渲染留足时间）+ 打开前后诊断日志（再黑屏直接看日志定位断点）。
                // 失败路径：CanOpen() false（过渡帧异常/模态残留）→ pending 保留，~2s 超时丢弃 + 日志
                //（防永久卡住）；Close() 一并清 pending（手动关闭 = 目标作废）。
                if (!string.IsNullOrEmpty(_pendingSecretLetterHeroId))
                {
                    _pendingSecretLetterElapsed += dt;
                    string topName = ScreenManager.TopScreen?.GetType().Name ?? "";
                    bool mapActive = false;
                    try { mapActive = ScreenManager.TopScreen != null && ScreenManager.TopScreen.IsActive; } catch { }
                    bool stable = topName.Contains("MapScreen") && mapActive
                        && _pendingSecretLetterElapsed >= 0.3f;
                    if (stable && CanOpen())
                    {
                        var conv = ImChatManager.GetDirectConversation(_pendingSecretLetterHeroId);
                        _pendingSecretLetterHeroId = null;
                        _pendingSecretLetterElapsed = 0f;
                        DebugLogger.Log($"[SecretLetter] 关屏完成（TopScreen={topName} IsActive={mapActive} elapsed={_pendingSecretLetterElapsed:0.00}），打开 IM 定位私聊");
                        bool opened = Open(conv);
                        DebugLogger.Log($"[SecretLetter] pending 打开结果: {opened}");
                    }
                    else if (_pendingSecretLetterElapsed >= PendingSecretLetterTimeoutSec)
                    {
                        DebugLogger.Log($"[SecretLetter] 关屏后打开 IM 超时丢弃（TopScreen={topName} IsActive={mapActive} CanOpen={CanOpen()}）");
                        _pendingSecretLetterHeroId = null;
                        _pendingSecretLetterElapsed = 0f;
                    }
                }

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
            // 🔴 世界背景生成同样依赖墙钟帧（暂停也运转）——与 IM 同轮子（ImScreenFrameTickPatch）：
            // CampaignEvents.TickEvent 暂停时 dt=0 停发，世界背景会永不生成（2026-08-17 实机教训）
            WorldBackgroundBehavior.Instance?.OnFrameTick(dt);
            ImChatManager.Tick(dt);
            // 🔴 2026-08-15（密信通知）：通知层驱动（自动消失计时）挂在 IM Tick 上——
            // 不依赖面板是否打开（提前 return 之前），Mission/Campaign 双端都到这里。
            // 🔴 2026-08-17（用户裁定）：ImSecretNotify（ninjareport 密信圆环）已废除——私聊通知
            // 统一由呼出按钮徽标承担（ImChatOpenButtonManager 自行订阅 MessageArrived），此处不再驱动。
            if (!IsOpen) return;

            // 🔴 2026-08-17（B'：层归属迁移提升到 Tick 顶层——原只在 HandleCompactInput（缩略分支），
            // 完整模式无此保护：关屏后滚动缓存指向已释放树 → 滚动静默失效。Q5 呼出按钮层迁移复用同一模式）
            MigrateLayerIfNeeded();

            // 🔴 Q4（2026-08-17）：设备切换检测（input.md 范式：缓存逐帧对比）——
            // 手柄→键盘/鼠标：解冻（UpdateGamepadFreeze）+ 焦点视觉轮询自行停摆；键盘→手柄：冻结（Mission）。
            // 手柄提示行随设备刷新（PadHintText/HasPadHint 是计算属性，需显式广播）。
            bool usingGamepad = ModInput.UsingGamepad;
            if (usingGamepad != _lastUsingGamepad)
            {
                _lastUsingGamepad = usingGamepad;
                _vm?.RefreshPadHint();
                DebugLogger.Log($"[ImChat] 设备切换 → {(usingGamepad ? "手柄" : "键盘/鼠标")}");
            }
            UpdateGamepadFreeze();
            UpdatePadFocus();

            // 🔴 关闭改用独立键（用户要求）：ESC / 手柄 B——O 只负责打开，打字不再误关。
            // 注：本层 InputRestrictions(All) 是模态掩码，ESC 已被层拦截（不会触发系统菜单，与 Inquiry 同理），
            // 这里轮询全局输入状态消费关闭动作。
            // 🔴 2026-08-17（Q4 手柄）：B 键二分——缩略下拉打开中 = 先收下拉，再按才关面板（原版 UI 同款心智）
            if (Input.IsKeyReleased(InputKey.Escape))
            {
                Close();
                return;
            }
            if (Input.IsKeyReleased(InputKey.ControllerRRight))
            {
                if (_mode == ImChatMode.Compact && _vm != null && _vm.ChannelSelector.IsChannelListOpen)
                {
                    CloseChannelList();
                    DebugLogger.Log("[ImChat] 手柄 B：先收频道下拉");
                }
                else
                {
                    Close();
                    return;
                }
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

            // 🔴 2026-08-15（缩略模式）：缩略面板 = 拖动/焦点释放/下拉收起（无消息流滚动）；
            // 完整模式 = 手动滚轮接管（七轮）
            if (_mode == ImChatMode.Compact)
                HandleCompactInput(dt);
            else
                HandleManualScroll(dt);
        }

        // ───────────────────────── 缩略模式输入（焦点/下拉收起）─────────────────────────

        /// <summary>
        /// 🔴 2026-08-15（缩略模式）缩略面板每帧处理（用户裁定：拖动已移除、标题行已去掉）：
        /// ① 输入框焦点释放（审查 P2-5）：输入框有焦点 && 点击面板外 && 下拉未开 → ClearFocus 键盘回游戏——
        ///    只在「点击」时清（打字中鼠标悬停面板外不打断）；
        /// ② 频道下拉收起（审查 P2-2）：根无点击盾只能轮询——点击面板+下拉矩形外 → 收起；
        /// ③ 位置感知 mask（🔴 2026-08-17 Q3 实锤）：以鼠标位置为开关——HitTest 命中面板才拦 Mouse
        ///    （点按钮不挥刀）；移出面板 → 摘 Mouse → 攻击/格挡/滚轮/视角还给游戏（替换旧「常驻 Mouse」
        ///    方案——pitfalls 2026-08-11 实机记录：任何 Gauntlet 层只要含 Mouse 拦截，战斗场景就是
        ///    攻击/格挡杀手）；下拉开时补 MouseWheels 位（列表滚动），关时去掉。
        /// </summary>
        private static void HandleCompactInput(float dt)
        {
            try
            {
                // 🔴 2026-08-17（B'）：层归属迁移已提升到 Tick() 顶层（MigrateLayerIfNeeded）——
                // 两种模式同享保护，此处不再重复（旧代码：家族 UI 关闭 → PopScreen 销毁层 →
                // _layer 引用指向已释放 native → 缩略分支 IsFocusedOnInput 抛 NRE 崩溃修复）
                if (_compactPanel == null)
                {
                    FindCompactWidgets();
                    if (_compactPanel == null) return;
                }
                // 🔴 布局诊断延迟到 ~1s（面板创建首帧布局未跑，GlobalPosition/Size 全 0，早打无意义）
                _compactDiagTimer += dt;
                if (_compactDiagTimer >= 1f)
                {
                    _compactDiagTimer = -100f;   // 只打一次
                    LogCompactLayoutDiagnostic();
                }
                Vec2 mouse = Input.MousePositionPixel;

                // ── 🔴 2026-08-15（列表项交互手动化）：浮出面板的列表收不到引擎事件
                //    （EventManager.CollectEnableWidgetsAt 祖先矩形门控：点 y<面板顶 时遍历在面板层断；
                //    原生 DropdownWidget 靠 reparent 到 Root 绕开，但 Window 根级 child 的树链不可靠
                //    （FindWidgetById 找不到，实机回归），已回退标题行内布局）。
                //    与 HandleManualScroll 同思路：手动命中——按坐标算行号（项高 34 = 30+边距 4，
                //    VerticalBottomToTop + LWN swap：child 0 在顶），hover/pressed 视觉直接 SetState
                //    到项内 ImageWidget（引擎不更新这些状态就不会覆盖），点选按下即触发 ExecuteSelect。
                if (_vm != null && _vm.ChannelSelector.IsChannelListOpen && _compactChannelList != null)
                {
                    var inner = FindWidgetById(_compactChannelList, "LWN_ImChat_ChannelListInner");
                    if (inner != null)
                    {
                        var listPos = _compactChannelList.GlobalPosition;
                        var listSize = _compactChannelList.Size;
                        int count = inner.ChildCount;
                        int hoverIdx = -1;
                        if (IsPointInRect(mouse, listPos, listSize) && count > 0)
                        {
                            // 🔴 2026-08-15（实机反馈修复）：hover 队伍却高亮阿速甘——索引倒置。
                            // VerticalBottomToTop + LWN swap 补丁下 child 0 在列表顶部（知识文档
                            // 实操建议：第一个 child = 屏幕顶部），直接按从顶向下算行号即可，
                            // 不要 count-1-fromTop 反转（那是 child 0 在底的反向布局）。
                            int fromTop = (int)((mouse.Y - listPos.Y) / 34f);
                            hoverIdx = fromTop;
                            if (hoverIdx < 0 || hoverIdx >= count) hoverIdx = -1;
                        }
                        bool pressing = Input.IsKeyPressed(InputKey.LeftMouseButton) && hoverIdx >= 0;
                        var opts = _vm.ChannelSelector.ItemList;
                        for (int i = 0; i < count && i < opts.Count; i++)
                        {
                            var btn = inner.GetChild(i);
                            if (btn == null || btn.ChildCount == 0) continue;
                            string st = (pressing && i == hoverIdx) ? "Pressed"
                                : i == hoverIdx ? "Hovered"
                                : opts[i].IsSelected ? "Selected" : "Default";
                            btn.GetChild(0).SetState(st);
                        }
                        if (pressing && hoverIdx < opts.Count)
                        {
                            var opt = opts[hoverIdx];
                            DebugLogger.Log($"[CompactSelect] 手动项点击: {opt.ConversationId} idx={hoverIdx}");
                            opt.ExecuteSelect();
                        }
                    }
                }

                // ── ③ 位置感知 mask（🔴 2026-08-17 Q3 实锤，替换旧「常驻 Mouse」方案）：
                //    引擎无区域化输入限制 API——鼠标键在 native 层有「UI 捕获」判定，与鼠标位置无关，
                //    层 mask 常驻 Mouse = 位置无关地全局拦鼠标 = Mission 内攻击/格挡/滚轮/视角全死
                //    （pitfalls 2026-08-11 实机记录 + 用户反馈双证）。
                //    方案 = 以鼠标位置为开关的全局 mask（模拟半模态岛）：
                //      HitTest 命中面板（含浮出的频道下拉，widget 命中天然覆盖）→ mask 含 Mouse
                //      （点按钮不挥刀——维持现状可用行为）；
                //      鼠标移出面板 → 摘 Mouse → 攻击/格挡/滚轮/视角还给游戏。
                //    Keyboardkeys 常驻（键盘拦不住物理轮询，留着不影响 WASD）。
                //    MouseWheels 一致性：下拉开时位置无关地补上（现状保留——拉开时间短，接受；实机不适再并入 overUi）。
                // 🔴 2026-08-15（性能）：SetInputRestrictions 只在 mask 变化时调用——每帧调用可能
                // 触发输入上下文重置（用户反馈 UI 卡顿疑点之一）──
                bool overUi = false;
                try { overUi = _layer != null && _layer.HitTest(); } catch { }
                InputUsageMask mask = InputUsageMask.Keyboardkeys;
                if (overUi) mask |= InputUsageMask.MouseButtons;
                if (_vm != null && _vm.ChannelSelector.IsChannelListOpen)
                    mask |= InputUsageMask.MouseWheels;
                if (mask != _lastCompactMask && _layer != null)
                {
                    _lastCompactMask = mask;
                    _layer.InputRestrictions.SetInputRestrictions(true, mask);
                }

                // ── 🔴 2026-08-15（点击透传诊断）：按下帧打印鼠标位置 / 面板矩形 / 层 hit-test 结果——
                //    实机「点 UI 透传到地图部队移动」取证：层命中 false 却点在面板矩形内 = 层没拦住
                if (Input.IsKeyPressed(InputKey.LeftMouseButton))
                {
                    var panelPos = _compactPanel.GlobalPosition;
                    var panelSize = _compactPanel.Size;
                    bool layerHit = false;
                    try { layerHit = _layer != null && _layer.HitTest(); } catch { }
                    bool inPanel = IsPointInRect(mouse, panelPos, panelSize);
                    DebugLogger.Log($"[CompactClickDiag] mouse=({mouse.X:0},{mouse.Y:0}) panel=({panelPos.X:0},{panelPos.Y:0},{panelSize.X:0},{panelSize.Y:0}) inPanel={inPanel} layerHit={layerHit}");
                }

                // ── ① 输入框焦点释放（点击面板外 = 明确回游戏意图；下拉开着时不打断）──
                if (_layer != null && _layer.IsFocusedOnInput()
                    && Input.IsKeyPressed(InputKey.LeftMouseButton)
                    && !IsPointInRect(mouse, _compactPanel.GlobalPosition, _compactPanel.Size)
                    && (_vm == null || !_vm.ChannelSelector.IsChannelListOpen))
                {
                    _layer.UIContext.EventManager.ClearFocus();
                }

                // ── ② 频道下拉收起（点击面板矩形 ∪ 列表矩形外 = 场景；列表在标题行内向上展开，
                //    可能溢出面板顶部 → 矩形判定用列表自身 GlobalPosition/Size）──
                if (_vm != null && _vm.ChannelSelector.IsChannelListOpen
                    && Input.IsKeyPressed(InputKey.LeftMouseButton)
                    && !IsPointInRect(mouse, _compactPanel.GlobalPosition, _compactPanel.Size)
                    && !IsPointInRect(mouse, _compactChannelList?.GlobalPosition ?? new Vec2(-1, -1), _compactChannelList?.Size ?? new Vec2(0, 0)))
                {
                    _vm.ChannelSelector.IsChannelListOpen = false;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] 缩略模式输入异常: {ex.Message}");
            }
        }

        /// <summary>查找并缓存缩略面板 widget（LoadMovie 后懒加载；模式切换后缓存已清空）。</summary>
        private static void FindCompactWidgets()
        {
            if (_layer?.UIContext?.Root == null) return;
            if (_compactPanel == null)
                _compactPanel = FindWidgetById(_layer.UIContext.Root, "LWN_ImChat_CompactPanel");
            if (_compactChannelList == null)
                _compactChannelList = FindWidgetById(_layer.UIContext.Root, "LWN_ImChat_ChannelList");
        }

        /// <summary>🔴 2026-08-15（下拉点不开修复）：中心按钮直连切换列表显隐（弃用原版控件机制）。
        /// 🔴 诊断日志（2026-08-15）：同时打印 VM 状态与列表 widget 实际 IsVisible——区分
        /// 「点击没到 VM」（VM 不变）vs「绑定没生效」（VM=true 但 widget 仍不可见）。</summary>
        public static void ToggleChannelList()
        {
            if (_vm != null)
                _vm.ChannelSelector.IsChannelListOpen = !_vm.ChannelSelector.IsChannelListOpen;
            DebugLogger.Log($"[CompactSelect] ToggleChannelList → open={_vm?.ChannelSelector.IsChannelListOpen} widgetVisible={_compactChannelList?.IsVisible}");
        }

        /// <summary>收起频道列表（点选频道后调用——原版下拉选中即收起行为）。</summary>
        public static void CloseChannelList()
        {
            if (_vm != null) _vm.ChannelSelector.IsChannelListOpen = false;
        }

        private static bool IsPointInRect(Vec2 p, Vec2 pos, Vec2 size)
        {
            return p.X >= pos.X && p.X <= pos.X + size.X && p.Y >= pos.Y && p.Y <= pos.Y + size.Y;
        }

        // 🔴 2026-08-15（缩略模式布局诊断）：延迟 ~1s 打印面板与 body 子元素的运行时位置/尺寸/顺序——
        // 实机「消息显示不出来」等布局问题取证用（布局跑完后 GlobalPosition/Size 才有真实值）
        private static float _compactDiagTimer;

        private static void LogCompactLayoutDiagnostic()
        {
            try
            {
                var panel = _compactPanel ?? FindWidgetById(_layer.UIContext.Root, "LWN_ImChat_CompactPanel");
                if (panel == null) return;
                DebugLogger.Log($"[CompactDiag] panel pos=({panel.GlobalPosition.X:0},{panel.GlobalPosition.Y:0}) size=({panel.Size.X:0},{panel.Size.Y:0})");
                var body = FindWidgetById(_layer.UIContext.Root, "LWN_ImChat_CompactBody");
                if (body != null)
                {
                    for (int i = 0; i < body.ChildCount; i++)
                    {
                        var c = body.GetChild(i);
                        if (c == null) continue;
                        DebugLogger.Log($"[CompactDiag] body child[{i}] id={c.Id} pos=({c.GlobalPosition.X:0},{c.GlobalPosition.Y:0}) size=({c.Size.X:0},{c.Size.Y:0}) visible={c.IsVisible}");
                    }
                }
                else
                {
                    DebugLogger.Log("[CompactDiag] body 未找到！");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[CompactDiag] 异常: {ex.Message}");
            }
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
                    // 🔴 2026-08-13：candidateIndex（模板 NPC 目标）随卡透传 → 批准后重扫候选锁定
                    ActionHandler.HandleImAction(msg.ActionCode, msg.SenderHeroId, msg.SenderName,
                        msg.ActionTarget, msg.ActionLevel, conv, msg.Content, bypassConfirm: true,
                        candidateIndex: msg.TargetConfirmIndex);
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

        /// <summary>🔴 2026-08-13（模板 NPC 目标确认，用户裁定：无新卡片）：玩家从宾语确认消息的
        /// 按钮行选定候选（"① 右侧约10米"）→ 写入 TargetConfirmIndex（本消息按钮消失）→
        /// 投递**常规同意/拒绝卡**（PostActionProposal，candidateIndex=选定项）——目标确认完毕，
        /// 再走常规计划批准流程。批准后 HandleProposal → HandleImAction(candidateIndex) 重扫锁定。</summary>
        public static void HandleTargetConfirm(ImMessage msg, int index)
        {
            if (msg == null || !msg.IsTargetConfirm || msg.IsTargetConfirmResolved) return;
            var conv = ConversationOf(msg.ConvId);
            if (conv == null) return;
            msg.TargetConfirmIndex = index;
            // 按钮行是重建式数据（CardButtons 按锚点重建）→ 全量重建（本消息按钮消失）
            if (_vm != null) { _vm.Messages.Clear(); RefreshMessages(); }
            try
            {
                Hero attacker = null;
                try { attacker = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == msg.SenderHeroId); } catch { }
                if (attacker == null) return;
                ActionHandler.PostActionProposal(conv, attacker, msg.SenderName, null,
                    ActionRegistry.FindByCode(msg.ActionCode), msg.ActionCode, msg.ActionTarget, msg.ActionLevel,
                    ActionHandler.FindAgentByHeroId(msg.SenderHeroId),
                    templateTargetName: msg.TargetConfirmName, candidateIndex: index);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] 宾语确认投递常规卡失败: {ex.Message}");
            }
        }

        /// <summary>🔴 2026-08-12（合并闲聊/计划模式）：needPlan 建议按钮（制定计划/先不用）。
        /// 制定计划 → RequestCommand（命令 = 玩家原话 CommandText，私聊玩家消息不在 store 必须冗余存；
        /// Mission = LLM 计划管线；Campaign = 规则解析计划）；先不用 → 了结回闲聊（密谋互斥释放，同「切回闲聊」语义）。
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
            // 🔴 2026-08-15（plan_needed 全手动裁定）：战术方向（risk_analysis）随按钮存储于
            // RiskAnalysisText——点「制定计划」时传入计划轮【随从的打算】段（M4 think-aloud 不丢失）；
            // 已解析目标（含 #N）随 ResolvedTargetText 传入计划轮【目标指认】段（不再二次解析玩家原话）。
            ImCommandFlow.RequestCommand(conv, command, companionIntention: msg.RiskAnalysisText,
                resolvedTargetText: msg.ResolvedTargetText);
        }

        /// <summary>🔴 2026-08-15（ask_player 询问步骤）：玩家点选密信决策卡按钮（撤退/强制执行）→
        /// 卡片了结（按钮消失）→ 事件回投执行者 PlanExecutor（NotifyDecisionEvent）→
        /// 步骤 on_event 路由（retreat → 撤退收尾 / force → 强制执行步骤）。执行者已结束/离场 →
        /// 事件丢弃（计划收尾语义不变，日志可查）。</summary>
        public static void HandleAskPlayerOption(ImMessage msg, string eventType)
        {
            if (msg == null || !msg.IsAskPlayerCard || msg.IsAskPlayerCardResolved) return;
            msg.ExecutorId = "done";
            // 按钮行是重建式数据（CardButtons 按锚点重建）→ 全量重建（本消息按钮消失，锚点前移）
            if (_vm != null) { _vm.Messages.Clear(); RefreshMessages(); }
            if (string.IsNullOrEmpty(eventType)) return;
            try
            {
                PlanExecutor executor = null;
                if (Mission.Current != null && !string.IsNullOrEmpty(msg.SenderHeroId))
                {
                    foreach (var a in Mission.Current.Agents)
                    {
                        if (a == null || !a.IsActive()) continue;
                        var hero = (a.Character as CharacterObject)?.HeroObject;
                        if (hero != null && hero.StringId == msg.SenderHeroId)
                        {
                            executor = PlanExecutor.GetExecutorFor(a);
                            break;
                        }
                    }
                }
                if (executor != null && !executor.IsFinished)
                {
                    executor.NotifyDecisionEvent(eventType);
                    DebugLogger.Log($"[ImChat] ask_player 决策: {msg.SenderName} → {eventType}");
                }
                else
                {
                    DebugLogger.Log($"[ImChat] ask_player 决策 {eventType} 丢弃: 执行者 {msg.SenderName} 已结束/离场");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] ask_player 决策回投失败: {ex.Message}");
            }
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
                // 🔴 2026-08-17（用户裁定：IM 消息通知统一走呼出按钮）：IM 关闭时来消息（私聊+群聊）
                // 不再弹 NinjaNotification 横幅——由 ImChatOpenButtonManager 未读徽标承担
                //（它自行订阅 MessageArrived：徽标 +1 + 3s 脉冲，总未读口径，IM 关闭时按钮常显）。
                // 原 NotifyIncoming（摘要横幅 + 点击定位会话）已删除——ninjareport 圆环与群聊
                // NinjaNotification 横幅（IM 消息路径）一并废除；NinjaNotificationManager 本体保留
                //（WorldEvent/Quest 等其他系统的通用通知横幅仍在使用）。
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] OnMessageArrived 异常: {ex.Message}");
            }
        }
    }
}
