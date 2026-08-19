using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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
    /// - 🔴 2026-08-18（Q4 手动导航落地）：引擎 scope 黑盒实测失败 → 手动导航（PadItem 焦点项 +
    ///   ↑↓←→ 转移矩阵 + A 激活 + LB/RB 翻页 + 重建节流 + 焦点滚动跟随），详见 im-gamepad-navigation.md。
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
        // 🔴 2026-08-18：光标可见性缓存（手柄 = 隐藏；设备切换强制重算）
        private static bool _lastCompactMaskVisible = true;
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
        // 🔴 临时诊断（2026-08-19 mission 鼠标转镜头排查，测完删）：键鼠鼠标活动采样节流器
        private static float _mouseDiagTimer;
        // 🔴 2026-08-19（用户裁定：自监测最后输入来源）——判定已下沉到 ModInput.TickInputSource()
        //（全 Mod 共用：InteractArea 键帽/IM 提示行/呼出按钮/Mission 冻结同源），本类只消费
        // ModInput.UsingGamepad 并缓存 _lastUsingGamepad 做切换检测。原自建两源时间戳已删。
        // Mission 模态：面板打开 = 玩家角色输入整体冻结（UpdateGamepadFreeze 幂等门控）
        private static bool _playerFrozen;
        // 🔴 2026-08-19（状态①残留根因修复）：上一帧输入聚焦状态——引擎清焦点（软键盘链）后
        // inputFocused 边沿变化 → 立即 ApplyInputMask（导航态恢复隐藏光标），防「状态①光标可见残留 →
        // IsMouseActive 持续 true → 窗口过期提交翻转」（实机 09:43:07.743 聚焦 → 08.244 聚焦=False
        // IsMouseActive 持续 → 08.439 提交）。
        private static bool _lastInputFocusedState;

        // 🔴 2026-08-19（紧凑版 A 激活 input 焦点再固守）：聚焦 EditableTextWidget 后再固守 3 帧。
        // 实机根因（11:42:40）：IsMouseActive 粘性 true 时 A = native 点击链——click-down 聚焦输入框
        //（引擎点击聚焦路径），click-up 时 OS 光标已被锚定回拽到屏幕中心 (960,540) → 落在紧凑面板
        // 空隙/外部 → ClearFocus 清掉焦点（0.5s 后 聚焦=False）→ 设备翻转死锁。放大版点击落在面板
        // 内部（无焦点副作用）不受影响。再固守 = 只在焦点已被清时重设 FocusedWidget，覆盖 click-up
        // 窗口（≤3 帧），已聚焦时不动作（防重复触发软键盘请求链）。
        private static int _focusReaffirmFrames;
        private static Widget _reaffirmWidget;

        // 🔴 Q4（2026-08-18，手柄手动导航）：引擎 scope 黑盒实测失败（2026-08-17：prefab 已声明
        // NavigationScopeTargeter + GamepadNavigationIndex，但十字键无效果、无焦点视觉）→ 手动导航：
        // 每个可聚焦元素 = PadItem，显式定义 ↑↓←→ 转移矩阵 + A 激活动作（用户裁定）。完整设计见
        // plans/im-gamepad-navigation.md。焦点项表 + 焦点索引 + 重建门控（结构变化才重建，Fix 4 节流）
        private static readonly List<PadItem> _padItems = new List<PadItem>();
        private static int _padIndex = -1;                     // 唯一当前焦点（-1 = 无焦点）
        private static bool _padNavDirty = true;               // 结构变化（锚点卡/按钮集/模式/会话/下拉）才重建
        // 锚点卡结构缓存（引用 + 按钮数 + 横竖标记）——UpdateCardAnchors 每轮比对，任一变化 → 置 dirty
        // （含横竖翻转——按钮数不变也触发；v3）
        private static ImMessage _padNavAnchorRef;
        private static int _padNavAnchorBtnCount;
        private static bool _padNavAnchorVertical;
        // 构建快照（矩阵决策用）：卡按钮数
        private static int _padCardBtnCount;
        // 长按重复（每键独立 hold/重复计时，抬起复位；0.4s 延迟后每 0.18s 重复）
        private static bool _lastPadUp, _lastPadDown, _lastPadLeft, _lastPadRight, _lastPadA, _lastPadLB, _lastPadRB;
        private static float _padHoldUp, _padHoldDown, _padHoldLeft, _padHoldRight, _padHoldLB, _padHoldRB;
        private static float _padRepeatUp, _padRepeatDown, _padRepeatLeft, _padRepeatRight, _padRepeatLB, _padRepeatRB;
        private const float PadHoldDelay = 0.4f;               // 长按延迟（按住 0.4s 后开始重复）
        private const float PadRepeatInterval = 0.18f;         // 重复间隔（每 0.18s 移动一次）
        // 软键盘回落（降级预案）：输入框聚焦期间导航暂停；检测到「软键盘曾经激活 → 已关闭」转态 →
        // 主动 ClearFocus 恢复导航（不依赖引擎回填链路，🔴 待实机验证，见方案 §五/验证 14）
        private static bool _padWasKeyboardActive;
        // 🔴 2026-08-18（诊断日志防刷屏）：任意导航键按下沿闩锁——⛔ 门控/输入聚焦行只在按下沿打一次，
        // 禁止每帧打（按住键 = 60 行/s 刷屏 + 同步磁盘写拖慢游戏）
        private static bool _lastPadAnyKey;

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

        /// <summary>
        /// 呼出入口统一逻辑（🔴 2026-08-17，实机「Mission 内无法呼出」根因修复）：
        /// - 未开 → Open()（原行为）；
        /// - 缩略开着 → ToggleExpand()（玩家点按钮/按 O 的意图是看消息 → 放大为完整模式；
        ///   旧行为 Open 返回 false 静默无反应，配合「缩略层挂在被盖屏」场景 = 看起来呼不出）；
        /// - 完整开着 → 无动作（打字不误关，原设计）。
        /// </summary>
        public static bool OpenOrExpand()
        {
            if (IsOpen)
            {
                if (_mode == ImChatMode.Compact)
                {
                    DebugLogger.Log("[ImChat] 呼出入口：缩略模式开着 → 放大为完整模式");
                    ToggleExpand();
                    return true;
                }
                return false;   // 完整模式开着：无动作（打字不误关）
            }
            return Open();
        }

        /// <summary>🔴 2026-08-17（实机「Mission 内无法呼出」根因）：Mission 边界 = IM 面板生命周期边界——
        /// 大世界开着 IM（层挂 MapScreen）→ 进 Mission（MissionScreen Push 全屏盖住 MapScreen）→
        /// 面板不可见但 IsOpen=true → 呼出入口全被挡。检测 Mission.Current 变化 → Close 面板
        ///（场景切换后玩家重新打开；Mission 内打开的面板挂 MissionScreen，退出时 OnMissionScreenFinalize
        /// 已 Close，此检测幂等兜底）。</summary>
        private static Mission _lastMission;

        private static void CheckMissionBoundary()
        {
            if (Mission.Current == _lastMission) return;
            _lastMission = Mission.Current;
            if (IsOpen)
            {
                DebugLogger.Log($"[ImChat] Mission 边界变化（{(Mission.Current == null ? "退出" : "进入")}），关闭 IM 面板");
                Close();
            }
        }

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
                    // 首次打开引导（LWN_im_first_open；🔴 2026-08-17：「(IM)」字样已去除，通用称谓「传讯/Messaging」）
                    string hint = LWNTextHelper.ResolveCompound("LWN_im_first_open",
                        "Messaging - talk to heroes across the land, or give orders to companions. Open with {OPEN_KEY}; close with ESC, B, or by clicking outside the panel.",
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
                // 🔴 2026-08-17（用户裁定：缩略 = UI 模式）：**鼠标光标常驻**（MouseVisibility=true——
                // 光标模式下鼠标移动不转镜头，鼠标专门操作面板；之前光标随位置显隐 = 鼠标用来转镜头 →
                // 无法对缩略面板互动）+ **HitTest 门控限定接收范围**（mask 含 MouseButtons 但全屏根
                // DoNotAcceptEvents=true → 只有面板矩形命中时层才接收鼠标：点按钮不挥刀；鼠标在面板外
                // → HitTest false → 层不接收 → 左键攻击/右键旋转镜头照常——引擎无左右键独立 mask 位
                //（InputUsageMask 实锤：MouseButtons=1 合并左右键），右键放行靠 HitTest 门控实现）。
                // 键盘不拦（物理轮询拦不住，WASD 正常）。完整模式保持模态语义不变（三件套全拦）。
                // 🔴 2026-08-18（实机：光标被原生手柄光标模式锁死在屏幕正中）：mask 随设备——
                // 手柄在用 → 隐藏鼠标光标（原生引擎检测到「手柄 + 可见光标」即进入 gamepad cursor 模式：
                // 光标锚定屏幕中心 + 右摇杆驱动，每帧覆盖 SetMousePosition；详见 ApplyInputMask）
                ApplyInputMask();
                if (ScreenManager.TopScreen != null)
                {
                    ScreenManager.TopScreen.AddLayer(_layer);
                    _layerOwnerScreen = ScreenManager.TopScreen;
                }
                // 🔴 2026-08-18（实机三连击根因修复）：面板打开 = 引擎手柄导航整体屏蔽
                //（prefab scope 已删；见 SetEngineGamepadNavBlocked）——手动导航独占十字键
                SetEngineGamepadNavBlocked(true);

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
                // 🔴 临时诊断（2026-08-19 mission 鼠标转镜头排查，测完删）
                DebugLogger.Log($"[ImChat] Open 完成 mode={_mode} gamepad={_lastUsingGamepad} inputFocused={_layer.IsFocusedOnInput()}");
                // 🔴 2026-08-17（用户反馈）：Mission 内直接以缩略模式打开（_mode 记忆——上次关闭时是
                // 缩略，下一次开启仍是缩略）→ 同样提示镜头操作变化（不只「放大→缩略」路径）
                ShowCompactCameraHintIfNeeded();
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
            // 🔴 2026-08-18（实机三连击根因修复）：先解除引擎导航屏蔽（widget 树还活着——
            // UsedNavigationMovements=None 让引擎导航管理器从屏蔽列表移除本 widget，防残留引用）
            SetEngineGamepadNavBlocked(false);
            // 🔴 2026-08-17（用户反馈：缩略模式下直接叉掉界面也要提示）：关闭面板（叉掉/ESC/B）时
            // 若处于缩略模式 → 鼠标控制恢复提示（ToggleExpand 是缩略→完整，Close 是直接关——都要提示）。
            // Mission 退出时 Mission.Current 可能已 null → 不提示（回大地图本来就是拖拽操作）。
            if (_mode == ImChatMode.Compact && Mission.Current != null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    // 本地化：缩略模式镜头恢复提示（LWN_im_compact_camera_restored）
                    LWNTextHelper.ResolveText("LWN_im_compact_camera_restored",
                        "Mouse control restored: move the mouse to rotate the camera.")));
            }
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
            // 🔴 Q4（2026-08-18，手动导航）：面板关闭 → 焦点复位 + 下次打开重建（widget 树销毁，无高亮残留）
            ResetPadFocus();
            _padNavDirty = true;
            _padItems.Clear();
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
            // 🔴 2026-08-19（实机：第二次打开鼠标消失——mask 缓存跨层生命周期残留）：
            // 层销毁后静态缓存必须失效——否则下次 Open 目标 == 缓存 → SetInputRestrictions 被跳过，
            // 新层用默认 InputRestrictions（光标隐藏）→ Mission 内光标消失 + 鼠标转镜头。
            //（8-15 加缓存做性能优化时只改了 ApplyInputMask，漏了 Close 重置，8-19 日志实锤跳过）
            _lastCompactMask = InputUsageMask.Invalid;
            _lastCompactMaskVisible = false;
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
        /// 🔴 Q4（2026-08-17，手柄模态）：Mission 内**完整模式**面板打开且手柄在用 → 角色输入整体冻结
        ///（SetPlayerControlFrozen，幂等——_playerFrozen 门控；冻结只冻角色 Agent，不冻 UI 层导航）。
        /// 🔴 2026-08-18（实机：Mission 缩略模式左摇杆被屏蔽）：缩略模式 = 半模态岛——玩家应继续
        /// 操作角色（移动/镜头），**不冻结**；只有完整模式（模态）才冻结。
        /// 解冻时机 = 设备切回键盘/鼠标 / 切缩略 / Close / Mission 退出（Close 路径已有，Mission 退出自动恢复）。
        /// 大地图手柄不冻结：十字键分流（引擎导航方向只认十字键，左摇杆 = 地图移动照常，反编译实锤）
        /// ——完美分流，无模态需求。
        /// </summary>
        private static void UpdateGamepadFreeze()
        {
            if (_layer == null) return;
            // 🔴 2026-08-19：冻结判定用去抖后的 _lastUsingGamepad（裸值振荡 = 每帧冻结/解冻角色，Mission 卡顿）
            bool shouldFreeze = _lastUsingGamepad && Mission.Current != null && _mode == ImChatMode.Full;
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
        /// 🔴 2026-08-18（实机：手柄 IM 导航态光标被原生锚定锁死屏幕中央 + alt+tab 失焦仍锁鼠标）：
        /// 全局光标隐藏门控——供 ImChatCursorHidePatch（补丁 ScreenManager.UpdateMouseVisibility）判定。
        /// 背景：ScreenManager.UpdateMouseVisibility 聚合规则 =「任一活跃层 MouseVisibility=true → 全局光标
        /// 显示」，vanilla MapScreen 层恒 true → IM 层 SetInputRestrictions(false) 藏不住 →「手柄 + 可见光标」
        /// → native 锚定模式（光标=中心+摇杆向量，每帧 set_cursor_position 覆盖，失焦不停）。
        /// 门控 = IM 打开 + 手柄（去抖值）+ 非输入框聚焦（导航态）→ 强制隐藏；
        /// 输入框聚焦态（原生速度模式，需要光标点击）与鼠标态放行原聚合逻辑。
        /// </summary>
        internal static bool ShouldForceHideCursor()
        {
            if (_layer == null) return false;
            return _lastUsingGamepad && !_layer.IsFocusedOnInput();
        }

        /// <summary>🔴 2026-08-19：层实例判定——供 ImChatSoftKeyboardPatch（软键盘取消/完成回调跳过）门控。
        /// 仅 IM 层跳过（其他层的 vanilla 软键盘回调不受影响）。</summary>
        internal static bool IsCurrentLayer(GauntletLayer layer) => layer != null && layer == _layer;

        /// <summary>🔴 2026-08-19：UIContext 实例判定——native 软键盘取消可能直接调 UIContext（不走层），
        /// 补丁门控用（ImChatSoftKeyboardPatch 上下文版）。</summary>
        internal static bool IsCurrentContext(UIContext ctx) => _layer != null && ctx != null && ctx == _layer.UIContext;

        /// <summary>
        /// 🔴 2026-08-19（用户裁定：缩略半模态聚焦门控）：面板是否占用手柄键（A/十字键/LB/RB/B）——
        /// ImChatMissionInputPatch 的拦键门控。完整模式 = 模态恒占用；缩略模式 = 聚焦态
        ///（导航焦点 _padIndex ≥ 0 或输入框聚焦）才占用——无焦点时 A/D-pad 还给游戏（跳跃）。
        /// </summary>
        internal static bool IsPanelKeyOwner
        {
            get
            {
                if (_layer == null) return false;
                if (_mode == ImChatMode.Full) return true;
                return _padIndex >= 0 || _layer.IsFocusedOnInput();
            }
        }

        /// <summary>
        /// 🔴 2026-08-18：按设备 + 输入态重算层输入 mask（统一入口；Open / SwitchMode / Tick 设备切换 /
        /// UpdatePadFocus 输入聚焦分支 / HandleCompactInput 逐帧调用，内部缓存只在变化时 SetInputRestrictions）。
        /// 三态模型（实机裁决）：
        /// ① 手柄 + 输入框聚焦（软键盘/打字态）→ **光标可见 + 放行 MouseBits**——原生光标速度模式接管
        ///    （实机 2026-08-18：输入框聚焦时手柄左摇杆可自由移动光标，原生机制非锚定），面板可像鼠标
        ///    一样点击（点频道切会话；背景点击 = 关面板，与鼠标语义一致；无软键盘时按十字键 = 退出
        ///    输入态回导航，见 UpdatePadFocus）。本态导航暂停（软键盘），无冲突。
        /// ② 手柄 + 普通导航态 → **光标隐藏 + mask 只留 Keyboardkeys**——原生「手柄 + 可见光标」锚定模式
        ///    会把光标锁死屏幕中心并覆盖 SetMousePosition（十字键瞬移光标实测失败 2026-08-18），且模拟
        ///    点击会误触背景层 Command.Click="ExecuteClose" 关面板；高亮走 SetState 焦点视觉，A = 激活。
        /// ③ 鼠标在用 → 照常全 mask + 光标可见（含缩略下拉 MouseWheels 位）。
        /// </summary>
        private static void ApplyInputMask()
        {
            if (_layer == null) return;
            // 🔴 2026-08-19：用去抖后的 _lastUsingGamepad（裸值每帧振荡 → mask/光标每帧切换 = 振荡回路燃料）
            bool gamepad = _lastUsingGamepad;
            bool inputFocused = _layer.IsFocusedOnInput();
            bool visible;
            InputUsageMask mask;
            if (gamepad)
            {
                if (inputFocused)
                {
                    visible = true;   // ① 输入框聚焦：原生光标速度模式 + 面板可点击
                    mask = InputUsageMask.Keyboardkeys | InputUsageMask.MouseButtons;
                }
                else
                {
                    visible = false;  // ② 导航态：隐藏光标，防原生锚定锁中 + 背景误关
                    mask = InputUsageMask.Keyboardkeys;
                }
            }
            else if (_mode == ImChatMode.Compact)
            {
                visible = true;       // ③ 鼠标玩家照常
                mask = InputUsageMask.Keyboardkeys | InputUsageMask.MouseButtons;
                if (_vm != null && _vm.ChannelSelector.IsChannelListOpen)
                    mask |= InputUsageMask.MouseWheels;
            }
            else
            {
                visible = true;
                mask = InputUsageMask.MouseButtons | InputUsageMask.MouseWheels | InputUsageMask.Keyboardkeys;
            }
            if (mask != _lastCompactMask || visible != _lastCompactMaskVisible)
            {
                _layer.InputRestrictions.SetInputRestrictions(visible, mask);
                // 🔴 临时诊断（2026-08-19 mission 鼠标转镜头排查，测完删）：打印每次实际应用的 mask
                DebugLogger.Log($"[ImChatMask] SetInputRestrictions(visible={visible}, mask={mask}) gamepad={gamepad} focused={inputFocused} mode={_mode}");
                _lastCompactMask = mask;
                _lastCompactMaskVisible = visible;
            }
        }

        // ───────────────────────── 手柄手动导航（🔴 2026-08-18，Q4 落地）─────────────────────────
        // 引擎 scope 黑盒实测失败（2026-08-17：prefab 已声明 NavigationScopeTargeter + GamepadNavigationIndex
        // 但十字键无效果、无焦点视觉）→ 手动导航：每个可聚焦元素显式定义 ↑↓←→ 转移矩阵 + A 激活动作
        //（用户裁定：「设计下每一个按钮 hover 状态时候，下一个应该自动移动到什么地方，然后用 A 来按下」）。
        // 完整设计见 plans/im-gamepad-navigation.md（转移矩阵/回落点 C_sel/重建节流/滚动跟随）。

        /// <summary>手动导航焦点项：稳定 Id（重建映射）+ A 激活 + 视觉 widget 定位 + 组标识。</summary>
        private sealed class PadItem
        {
            public string Id;              // 稳定身份：static = c1/c2/cm/input/send/k1..k5；channel = channel_{会话Id}；cardbtn = cardbtn_{锚点时间戳}_{序}；dd = dd_{会话Id}
            public string Group;           // static / channel / cardbtn（横排）/ cardbtnv（竖排）/ dd
            public Action OnActivate;      // A 激活动作（频道行 = 移动即激活，A 无附加动作）
            public Func<Widget> GetWidget; // 视觉 widget 定位（null = 查找失败/频道行无独立视觉）
            public Widget LastWidget;      // 上次已应用视觉的 widget（旧焦点按身份复位用）
            public object Tag;             // 附加数据（频道会话 / 下拉项 VM）
        }

        /// <summary>
        /// 🔴 Q4（2026-08-18，手动导航）：每帧导航驱动——取代引擎 scope 黑盒（实测 2026-08-17：
        /// prefab 已声明 NavigationScopeTargeter 但十字键无效果、无焦点视觉）。
        /// - 重建（RebuildPadNavigation）只在结构变化时（_padNavDirty：锚点卡引用/按钮集/模式/会话/下拉）——
        ///   0.3s 轮询刷新不直接置 dirty（防焦点跳变与长按抖动，Fix 4）；
        /// - 按下沿 + 长按重复（按住 0.4s 后每 0.18s 一次，每键独立计时，抬起复位）；
        /// - 焦点视觉 = SetState("Hovered") 高亮（🔴 2026-08-18 实机裁决：十字键瞬移光标被原生锚定模式
        ///   覆盖 → 光标跟随方案废弃；输入框聚焦时原生转速度模式 → 游标态放行鼠标位，见 ApplyInputMask）；
        ///   A = 激活（OnActivate，与鼠标点击同效）；
        /// - 下拉打开 = 焦点接管为下拉项[1..M]（↑↓ 循环 + A 选中收起 + B 收起）；
        /// - 输入框聚焦（软键盘）期间导航暂停（引擎接管输入），软键盘关闭转态 → 主动 ClearFocus 恢复
        ///   （降级预案：不依赖引擎回填链路，🔴 待实机验证）；
        /// - 门控：仅 ModInput.UsingGamepad（设备切鼠标 → 焦点复位无残留高亮）。
        /// </summary>
        private static void UpdatePadFocus(float dt)
        {
            // 🔴 2026-08-18（诊断日志防刷屏）：按下沿闩锁先算——⛔ 行只在按键刚按下那帧打一次
            bool anyKey = AnyPadKeyPressed();
            bool anyKeyEdge = anyKey && !_lastPadAnyKey;
            _lastPadAnyKey = anyKey;
            // 🔴 2026-08-19：门控用自监测的 _lastUsingGamepad（晚者胜出；按键沿即时生效无去抖——
            // 用户裁定「按下手柄任何按键就是在激活手柄模式」）；门控期间置 dirty——
            // 真实切回手柄的下一帧立即重建（stale 项清零），防同类死锁复发
            // 🔴 2026-08-18（坑 12 兜底）：**输入框聚焦期间不早退**——即使设备翻转为键盘/鼠标，
            // 也放行进输入聚焦分支（ApplyInputMask 按真实设备重算 mask；降级预案：无软键盘时按
            // 十字键 ClearFocus 退出输入态），防「聚焦输入框 → 设备翻转 → 输入态卡死」残余路径
            bool inputFocusedNow = _layer != null && _layer.IsFocusedOnInput();
            if (!_lastUsingGamepad && !inputFocusedNow)
            {
                // 🔴 2026-08-18（诊断日志）：门控吞键实锤——手柄玩家按键但设备未提交时，
                // 这里按键按下沿打一行（证明「按键无反馈」是门控问题而非轮询问题）
                if (anyKeyEdge && PadDbg)
                {
                    DebugLogger.Log("[Pad] ⛔ 门控:设备未激活 按键被忽略（手柄未提交/未连接）");
                    PadScreenMsg("⛔ 设备未激活，按键被忽略");
                }
                _padNavDirty = true;
                ResetPadFocus();   // 设备切鼠标：焦点复位 + 高亮清理（下一帧引擎刷新原版按钮状态）
                return;
            }
            try
            {
                if (_padNavDirty) RebuildPadNavigation();
                if (_padItems.Count == 0) { _padIndex = -1; return; }

                // 🔴 2026-08-19（紧凑版 A 激活 input 焦点再固守）：native 点击链 click-up 在聚焦后
                // 1-2 帧清焦点（见 _focusReaffirmFrames 注释）——只在焦点已被清时重设 FocusedWidget，
                // 已聚焦不动作（防重复触发软键盘请求链）。3 帧后自动退出。
                if (_focusReaffirmFrames > 0)
                {
                    _focusReaffirmFrames--;
                    if (_reaffirmWidget != null && _layer != null && !_layer.IsFocusedOnInput())
                    {
                        try
                        {
                            _layer.UIContext.EventManager.FocusedWidget = _reaffirmWidget;
                            if (PadDbg) DebugLogger.Log($"[Pad] 焦点再固守 (剩余 {_focusReaffirmFrames}) IsFocusedOnInput={_layer.IsFocusedOnInput()}");
                        }
                        catch (Exception ex) { DebugLogger.Log($"[ImChat] 焦点再固守失败: {ex.Message}"); }
                    }
                }

                // 🔴 2026-08-19（用户裁定）：导航准星每帧驱动（含输入聚焦分支——聚焦时隐藏，编辑器光标接管）
                UpdateNavCursor();

                bool dropdownOpen = _mode == ImChatMode.Compact && _vm != null && _vm.ChannelSelector.IsChannelListOpen;
                bool inputFocused = _layer != null && _layer.IsFocusedOnInput();
                // 🔴 2026-08-19（状态①残留根因修复）：聚焦状态边沿变化 → 立即 ApplyInputMask——
                // 引擎清焦点（软键盘链）后本帧不聚焦，若状态①的「光标可见+MouseBits」残留 → 手柄+可见
                // 光标 → IsMouseActive 持续 true → 0.5s 窗口过期 → 设备翻转提交（实机 09:43:08.439）。
                if (inputFocused != _lastInputFocusedState)
                {
                    _lastInputFocusedState = inputFocused;
                    ApplyInputMask();
                }

                // 🔴 2026-08-19（用户裁定：缩略半模态聚焦门控）——聚焦态才占 A/十字键
                //（ImChatMissionInputPatch 的 IsPanelKeyOwner 门控）；无焦点态 A 还给游戏（跳跃）：
                //   ① 左摇杆移动 = 玩家在玩 → 退聚焦（准星隐藏、高亮全清、A 还给游戏）
                //   ② 无焦点态按十字键（任意向）→ 进入聚焦（该按下沿被吞，不落游戏）
                //   ③ 无焦点且没按面板键 → 本帧不消费任何键（A 跳、十字键下一按再进入）
                // 下拉接管 = 天然聚焦态（按下沿即进列表），不参与门控；完整模式 = 模态恒聚焦。
                if (_mode == ImChatMode.Compact && !inputFocused && !dropdownOpen)
                {
                    if (_padIndex >= 0 && LeftStickActive())
                    {
                        if (PadDbg) DebugLogger.Log($"[Pad] 左摇杆移动 → 退聚焦（A 还给游戏）{PadState()}");
                        _padIndex = -1;
                        HideNavCursor();
                        ResetPadHoldTimers();
                        ApplyPadVisual();   // index=-1 → 全项高亮复位
                        return;
                    }
                    if (_padIndex < 0)
                    {
                        if (AnyDpadPressed())
                        {
                            if (PadDbg) DebugLogger.Log($"[Pad] 十字键按下 → 进入聚焦（初始索引 0）{PadState()}");
                            _padIndex = _padItems.Count > 0 ? 0 : -1;
                            if (_padItems.Count > 0) SetMouseToWidget(_padItems[0]);
                        }
                        else
                        {
                            ResetPadHoldTimers();
                            // 同步 A 状态：无焦点早退不走 PollActivate，若玩家正按 A（跳跃中）且随后
                            // 按十字键进聚焦，防 _lastPadA 陈旧 → 假按下沿误激活焦点项
                            _lastPadA = Input.IsKeyPressed(InputKey.ControllerRDown);
                            return;   // 无焦点：A/D-pad 不消费（补丁门控 IsPanelKeyOwner=false → 游戏跳跃照常）
                        }
                    }
                }

                // ── 输入框聚焦（软键盘）：引擎接管输入，导航不抢键；mask 切到「游标模式」──
                if (inputFocused)
                {
                    if (anyKeyEdge && PadDbg) DebugLogger.Log("[Pad] ⛔ 输入聚焦:导航暂停（打字态，按键留给引擎）");

                    // 🔴 2026-08-18（实机：输入框聚焦时手柄可自由移动光标 = 原生速度模式）：
                    // 放行 MouseBits + 光标可见 → 面板可像鼠标一样点击（ApplyInputMask 内部缓存防抖）
                    ApplyInputMask();
                    // 降级预案：软键盘曾激活且已关闭（引擎回填链路未触发）→ 主动清焦点恢复导航；
                    // 无软键盘时（鼠标点击聚焦路径）按十字键 = 退出输入态回导航（防卡死在输入态）
                    bool kbActive = false;
                    try { kbActive = Input.IsOnScreenKeyboardActive; } catch { }
                    bool padPressed = Input.IsKeyPressed(InputKey.ControllerLUp) || Input.IsKeyPressed(InputKey.ControllerLDown)
                        || Input.IsKeyPressed(InputKey.ControllerLLeft) || Input.IsKeyPressed(InputKey.ControllerLRight);
                    if ((_padWasKeyboardActive && !kbActive) || (!kbActive && padPressed))
                    {
                        if (_layer != null) _layer.UIContext.EventManager.ClearFocus();
                    }
                    _padWasKeyboardActive = kbActive;
                    ResetPadHoldTimers();
                    return;
                }
                _padWasKeyboardActive = false;

                // ── 下拉打开：焦点接管为下拉项（↑↓ 循环 + A 选中收起；B 收下拉在 Tick 既有 B 键分支；
                //    ←→ 收下拉（v4 2026-08-18 用户裁定「每个方向都有通路」——下拉为纵向列表，横向 = 退出，
                //    等同 B 语义）──
                if (dropdownOpen)
                {
                    PollPadKey("↑", InputKey.ControllerLUp, ref _lastPadUp, ref _padHoldUp, ref _padRepeatUp, () => MovePad(0, -1), dt);
                    PollPadKey("↓", InputKey.ControllerLDown, ref _lastPadDown, ref _padHoldDown, ref _padRepeatDown, () => MovePad(0, 1), dt);
                    PollPadKey("←", InputKey.ControllerLLeft, ref _lastPadLeft, ref _padHoldLeft, ref _padRepeatLeft, () => CloseChannelList(), dt);
                    PollPadKey("→", InputKey.ControllerLRight, ref _lastPadRight, ref _padHoldRight, ref _padRepeatRight, () => CloseChannelList(), dt);
                    PollActivate();
                    ApplyPadVisual();
                    return;
                }

                // ── 正常导航：↑↓←→ 移动 + A 激活 + LB/RB 翻页滚动（仅完整模式）──
                PollPadKey("↑", InputKey.ControllerLUp, ref _lastPadUp, ref _padHoldUp, ref _padRepeatUp, () => MovePad(0, -1), dt);
                PollPadKey("↓", InputKey.ControllerLDown, ref _lastPadDown, ref _padHoldDown, ref _padRepeatDown, () => MovePad(0, 1), dt);
                PollPadKey("←", InputKey.ControllerLLeft, ref _lastPadLeft, ref _padHoldLeft, ref _padRepeatLeft, () => MovePad(-1, 0), dt);
                PollPadKey("→", InputKey.ControllerLRight, ref _lastPadRight, ref _padHoldRight, ref _padRepeatRight, () => MovePad(1, 0), dt);
                PollActivate();
                if (_mode == ImChatMode.Full)
                {
                    // LB/RB 翻页滚动（🔴 手柄滚消息 Fix 3；缩略模式无消息流滚动 → 无操作）
                    PollPadKey("LB", InputKey.ControllerLBumper, ref _lastPadLB, ref _padHoldLB, ref _padRepeatLB, () => ScrollPage(-1f), dt);
                    PollPadKey("RB", InputKey.ControllerRBumper, ref _lastPadRB, ref _padHoldRB, ref _padRepeatRB, () => ScrollPage(1f), dt);
                }
                ApplyPadVisual();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] 手柄导航异常: {ex.Message}");
            }
        }

        /// <summary>诊断用：鼠标像素位置（用户怀疑「A 键 = native 点击 → 点在鼠标位置（屏幕中央）→ 清焦点」验证）。</summary>
        private static string MousePosStr()
        {
            try { var p = Input.MousePositionPixel; return $"({p.X:0},{p.Y:0})"; } catch { return "?"; }
        }

        // 🔴 2026-08-19（紧凑版 A 激活 input 排障）：引擎 MousePositionPixel 在光标隐藏时是冻结读数——
        // 无法区分「OS 光标真没动」vs「引擎读数冻结」。P/Invoke 直读 OS 光标位置，日志里对照
        // MousePositionPixel 即可定性：相等 = 引擎读数真实（光标没动）；不等 = 读数冻结（光标已挪位）。
        [StructLayout(LayoutKind.Sequential)]
        private struct WinPoint { public int X; public int Y; }
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out WinPoint p);

        /// <summary>诊断用：真实 OS 光标位置（P/Invoke GetCursorPos，绕过引擎冻结读数）。</summary>
        private static string OsCursorStr()
        {
            try { WinPoint p; if (GetCursorPos(out p)) return $"({p.X},{p.Y})"; } catch { }
            return "?";
        }

        /// <summary>
        /// 🔴 2026-08-19（用户裁定：焦点变化 → 光标跟随）：把系统光标挪到 widget 中心。
        /// A 键 = native「点击」语义，点击位置 = 鼠标位置——焦点转移时把光标挪到新焦点项，
        /// 点击命中项本体（输入框=引擎点击聚焦路径 / 按钮=命中按钮）→ 不落空、不清焦点、不翻。
        /// 光标隐藏时无锚定覆盖（坑 2 覆盖仅光标可见时），SetMousePosition 有效；十字键导航全程
        /// 未触发 IsMouseActive 翻转（实测），说明程序 SetMousePosition 不算「鼠标活动」。
        /// </summary>
        private static void SetMouseToWidget(PadItem item)
        {
            if (item == null) return;
            try
            {
                var w = item.GetWidget?.Invoke();
                if (w == null) return;
                var gp = w.GlobalPosition;
                var sz = w.Size;
                if (gp.X < 0 || gp.Y < 0) return;
                Input.SetMousePosition((int)(gp.X + sz.X * 0.5f), (int)(gp.Y + sz.Y * 0.5f));
            }
            catch { }
        }

        /// <summary>诊断用：widget 全局像素位置（对比鼠标位置用）。</summary>
        private static string WidgetPosStr(Widget w)
        {
            try { var p = w.GlobalPosition; return $"({p.X:0},{p.Y:0})"; } catch { return "?"; }
        }

        /// <summary>
        /// 🔴 2026-08-19（用户裁定：准星不挡 native 点击）：A 键激活前强制隐藏导航准星。
        /// 为什么必须隐藏：准星 = 根下最顶层 widget（prefab 根 Children 最后一位），visible 时盖在
        /// 焦点项上，native 点击命中测试（CollectVisibleWidgetsAt 不检查 DoNotAcceptEvents，反编译
        /// 实锤）先命中准星 → 点击焦点链被吸走 → 焦点被清 + 设备翻转死锁（实机 2026-08-19 10:38:10：
        /// A 激活 input 后 0.5s 聚焦=False）。隐藏后点击路径 = 无准星提交版逐字节一致。
        /// 显示恢复由 UpdateNavCursor 每帧驱动（仍处导航态 → 下一帧自动重新显示）。
        /// </summary>
        private static void HideNavCursor()
        {
            try
            {
                Widget cursor = FindWidgetById(_layer?.UIContext?.Root, "LWN_NavCursor");
                if (cursor != null && !cursor.IsHidden) cursor.IsHidden = true;
            }
            catch { }
        }

        /// <summary>
        /// 🔴 2026-08-19（用户裁定：手柄导航准星）：自绘焦点指示器（LWN_NavCursor，prefab 根下最顶层）——
        /// 系统光标必须隐藏（手柄+可见光标 = native 锚定锁中，坑 2），hover 高亮又看不清楚焦点；
        /// 准星 = vanilla default_cursor sprite，导航态显示并跟随焦点 widget。
        /// 显示条件：手柄（去抖值）+ 非输入聚焦 + 焦点项有 widget；其余隐藏。
        /// 定位：PosOffset = 屏幕坐标 / UI Scale（PosOffset 是逻辑坐标，ScaledPositionOffset 只读 =
        /// 逻辑 × scale；准星中心对齐焦点中心，28×28 半宽 14）。
        /// 🔴 点击纪律：准星 visible 时会把 native 点击命中测试吸走（顶层遮挡），任何 A 键激活路径
        /// 必须先调 HideNavCursor（ActivatePad 统一入口已做）。
        /// </summary>
        private static void UpdateNavCursor()
        {
            try
            {
                Widget cursor = FindWidgetById(_layer?.UIContext?.Root, "LWN_NavCursor");
                if (cursor == null) return;
                bool show = _lastUsingGamepad
                    && _layer != null && !_layer.IsFocusedOnInput()
                    && _padIndex >= 0 && _padIndex < _padItems.Count;
                if (!show)
                {
                    if (!cursor.IsHidden) cursor.IsHidden = true;
                    return;
                }
                var w = _padItems[_padIndex].GetWidget?.Invoke();
                if (w == null)
                {
                    if (!cursor.IsHidden) cursor.IsHidden = true;
                    return;
                }
                var gp = w.GlobalPosition;
                var sz = w.Size;
                float scale = 1f;
                try { scale = _layer.UIContext.Scale; } catch { }
                if (scale <= 0f) scale = 1f;
                cursor.IsHidden = false;
                // 🔴 2026-08-19（用户裁定：框中心对准控件中心）：frame_small_9 焦点框中心 = 焦点控件中心。
                // 控件中心(逻辑) = gp(物理)/scale + size(逻辑)/2；框尺寸 = 控件尺寸 + 4px 余量（罩住控件），
                // 框左上角 = 控件中心 - 框尺寸/2（数学上等价于「框罩控件」，但显式中心写法防误解）
                float ctrlCX = gp.X / scale + sz.X * 0.5f;
                float ctrlCY = gp.Y / scale + sz.Y * 0.5f;
                float boxW = sz.X + 4f;
                float boxH = sz.Y + 4f;
                float targetX = ctrlCX - boxW * 0.5f;
                float targetY = ctrlCY - boxH * 0.5f;
                // 🔴 2026-08-19（准星位置诊断）：位置/尺寸变化 >1px 才打——对比 gp(物理) vs size(逻辑) vs scale
                if (PadDbg && (Math.Abs(targetX - cursor.PositionXOffset) > 1f || Math.Abs(targetY - cursor.PositionYOffset) > 1f
                    || Math.Abs(boxW - cursor.SuggestedWidth) > 1f || Math.Abs(boxH - cursor.SuggestedHeight) > 1f))
                    DebugLogger.Log($"[NavCursor] 焦点={_padItems[_padIndex].Id} 中心=({ctrlCX:0},{ctrlCY:0}) size=({sz.X:0},{sz.Y:0}) scale={scale:0.00} → 框左上=({targetX:0},{targetY:0} {boxW:0}x{boxH:0})");
                cursor.PositionXOffset = targetX;
                cursor.PositionYOffset = targetY;
                cursor.SuggestedWidth = boxW;
                cursor.SuggestedHeight = boxH;
            }
            catch { }
        }

        /// <summary>诊断日志用：当前导航状态快照（焦点索引 + 输入聚焦 + 下拉）。</summary>
        private static string PadState()
        {
            bool dd = _mode == ImChatMode.Compact && _vm != null && _vm.ChannelSelector.IsChannelListOpen;
            bool f = _layer != null && _layer.IsFocusedOnInput();
            return $"idx={_padIndex}{(f ? " 输入聚焦" : "")}{(dd ? " 下拉" : "")}";
        }

        // ── 屏上调试（🔴 2026-08-19 用户要求：实机直接观察按键/焦点，弹屏不走日志）──
        // 临时调试项，测完删除。动态调试文本走既有 debug 先例（MyCommands/CameraDebugger 裸字符串），
        // 不参与本地化表（铁律 13 的 debug 豁免先例）。
        // 🔴 2026-08-19（用户裁定：刷屏）：按键级诊断统一受 Settings.GamepadNavDebugLog 控制
        //（config.json，默认关）——[Pad]/[NavCursor]/[Input 设备沿] 日志 + 🎮/➤/🅰 屏显全归它。
        private static bool PadDbg => Settings.Instance.GamepadNavDebugLog;

        private static void PadScreenMsg(string msg)
        {
            if (!PadDbg) return;   // 开关：屏显黄字也归诊断，默认不弹
            try { InformationManager.DisplayMessage(new InformationMessage(msg, Colors.Yellow)); }
            catch { }
        }

        /// <summary>诊断 + 设备判定用：是否有任意手柄键按下（🔴 2026-08-19 用户裁定「任何手柄键都是
        /// 在激活手柄模式」——十字键/A/B/Y/X/LB/RB/L3 全覆盖）。门控吞键检测 + 自监测输入来源。</summary>
        private static bool AnyPadKeyPressed()
        {
            return Input.IsKeyPressed(InputKey.ControllerLUp) || Input.IsKeyPressed(InputKey.ControllerLDown)
                || Input.IsKeyPressed(InputKey.ControllerLLeft) || Input.IsKeyPressed(InputKey.ControllerLRight)
                || Input.IsKeyPressed(InputKey.ControllerRUp) || Input.IsKeyPressed(InputKey.ControllerRDown)
                || Input.IsKeyPressed(InputKey.ControllerRLeft) || Input.IsKeyPressed(InputKey.ControllerRRight)
                || Input.IsKeyPressed(InputKey.ControllerLBumper) || Input.IsKeyPressed(InputKey.ControllerRBumper)
                || Input.IsKeyPressed(InputKey.ControllerLStick);
        }

        /// <summary>🔴 2026-08-19（缩略半模态聚焦门控）：任意十字键按下沿（进入聚焦用——只认十字键，
        /// 面键/肩键不进入：A 无焦点时 = 游戏跳跃）。</summary>
        private static bool AnyDpadPressed()
        {
            return Input.IsKeyPressed(InputKey.ControllerLUp) || Input.IsKeyPressed(InputKey.ControllerLDown)
                || Input.IsKeyPressed(InputKey.ControllerLLeft) || Input.IsKeyPressed(InputKey.ControllerLRight);
        }

        /// <summary>🔴 2026-08-19（缩略半模态聚焦门控）：左摇杆是否推满（幅度 &gt; 0.5）——「玩家在玩」
        /// 信号：推摇杆移动 = 退出聚焦回玩态（A 还给游戏跳跃）。GetKeyState 对摇杆返回轴向量。</summary>
        private static bool LeftStickActive()
        {
            try
            {
                var v = Input.GetKeyState(InputKey.ControllerLStick);
                return (v.X * v.X + v.Y * v.Y) > 0.25f;
            }
            catch { return false; }
        }

        /// <summary>按下沿立即触发 + 长按重复（按住 PadHoldDelay 后每 PadRepeatInterval 一次；抬起复位）。
        /// 🔴 2026-08-18（诊断日志）：每次按下/长按重复/抬起（hold≥80ms）都打日志——实机「按键无反馈」
        /// 排查用：按键没打到 = 无日志；打到了没动 = 看焦点转移行；门控吞 = ⛔ 行。</summary>
        private static void PollPadKey(string name, InputKey key, ref bool last, ref float hold, ref float repeat, Action act, float dt)
        {
            bool pressed = Input.IsKeyPressed(key);
            if (pressed)
            {
                hold += dt;
                if (!last)
                {
                    if (PadDbg)
                    {
                        DebugLogger.Log($"[Pad] {name} 按下(edge) {PadState()}");
                        PadScreenMsg($"🎮 {name} 按下 idx={_padIndex}");
                    }
                    act(); repeat = 0f;                       // 按下沿：立即触发一次 + 计时起点
                }
                else if (hold > PadHoldDelay)                             // 长按重复
                {
                    repeat += dt;
                    if (repeat >= PadRepeatInterval) { repeat = 0f; if (PadDbg) DebugLogger.Log($"[Pad] {name} 长按重复 {PadState()}"); act(); }
                }
            }
            else
            {
                if (PadDbg && hold >= 0.08f) DebugLogger.Log($"[Pad] {name} 抬起 (hold={hold:F2}s)");
                hold = 0f; repeat = 0f;                              // 抬起 → 复位
            }
            last = pressed;
        }

        /// <summary>A 激活（按下沿）：_padItems[_padIndex].OnActivate。</summary>
        private static void PollActivate()
        {
            bool a = Input.IsKeyPressed(InputKey.ControllerRDown);
            if (a && !_lastPadA)
            {
                if (PadDbg) DebugLogger.Log($"[Pad] A 按下(edge) {PadState()}");
                ActivatePad();
            }
            _lastPadA = a;
        }

        private static void ActivatePad()
        {
            // 🔴 2026-08-19（用户裁定：A = native 点击语义，准星不挡点击）：A 键按下即产生 native 点击
            //（引擎层命中测试不受层 mask 过滤），OnActivate 前必须完成两件事：
            // ① SetMouseToWidget——把系统光标挪到焦点项中心，点击命中项本体（引擎点击聚焦路径与
            //    手动 FocusedWidget 一致；不挪则点击落在残留位置 → 焦点被清 + 设备翻转死锁）；
            // ② HideNavCursor——准星 = 根下最顶层 widget，visible 时盖在焦点项上，native 点击命中
            //    测试（CollectVisibleWidgetsAt 不检查 DoNotAcceptEvents，实锤反编译）先命中准星 →
            //    点击焦点链被吸走 → 焦点 0.5s 后被清（实机 2026-08-19 10:38:10 三连日志）。隐藏后
            //    点击路径 = 无准星提交版（HEAD）逐字节一致。下一帧 UpdateNavCursor 按条件自动恢复。
            if (_padIndex >= 0 && _padIndex < _padItems.Count) SetMouseToWidget(_padItems[_padIndex]);
            HideNavCursor();
            if (_padIndex < 0 || _padIndex >= _padItems.Count) return;
            var item = _padItems[_padIndex];
            if (PadDbg)
            {
                DebugLogger.Log($"[Pad] A 激活 → {item.Id} ({item.Group})");
                PadScreenMsg($"🅰 激活 {item.Id}");
            }
            try { item.OnActivate?.Invoke(); }
            catch (Exception ex) { DebugLogger.Log($"[ImChat] 焦点激活异常: {ex.Message}"); }
        }

        /// <summary>
        /// 按矩阵查表移动焦点（dx/dy 单轴 ±1，越界钳制）。落位后：频道行 = 移动即激活（微信式实时切会话，
        /// 🔴 性能：长按重复每 0.18s 一次 RefreshAll——卡顿则切备选「长按只移焦点、松开落地」，见方案验证 12）；
        /// 目标在视口外 → 焦点滚动跟随（§六.5）。
        /// </summary>
        private static void MovePad(int dx, int dy)
        {
            if (_padIndex < 0 || _padIndex >= _padItems.Count) return;
            var cur = _padItems[_padIndex];
            int target = dy < 0 ? PadUpTarget(cur)
                : dy > 0 ? PadDownTarget(cur)
                : dx < 0 ? PadLeftTarget(cur)
                : PadRightTarget(cur);
            if (target < 0 || target == _padIndex) return;
            // 🔴 2026-08-18（诊断日志）：焦点转移行——按键后「动没动」的判定依据
            string dir = dy < 0 ? "↑" : dy > 0 ? "↓" : dx < 0 ? "←" : "→";
            if (PadDbg)
            {
                DebugLogger.Log($"[Pad] 焦点 {cur.Id} → {_padItems[target].Id} ({dir})");
                PadScreenMsg($"➤ 焦点 {cur.Id} → {_padItems[target].Id} ({dir})");
            }
            _padIndex = target;
            var item = _padItems[target];
            if (item.Group == "channel")
            {
                try { item.OnActivate?.Invoke(); } catch (Exception ex) { DebugLogger.Log($"[ImChat] 频道行激活失败: {ex.Message}"); }
            }
            // 🔴 2026-08-19（用户裁定：焦点变化 → 光标跟随）：A 键 = native「点击」语义，点在鼠标位置
            //（残留屏幕中央 960,540 → 命中面板空白 → 焦点被清 + IsMouseActive 持续 → 设备翻转死锁）。
            // 焦点转移时把系统光标挪到新焦点 widget 中心 → A 键点击命中焦点项本体（输入框=引擎点击
            // 聚焦路径，与手动 FocusedWidget 一致；按钮=命中按钮）→ 不落空、不清焦点、不翻。
            SetMouseToWidget(item);
            ScrollPadIntoView(item);
        }

        /// <summary>LB/RB 翻页滚动（完整模式；复用 HandleManualScroll 的 scrollbar 语义——上翻解锁贴底，
        /// 下翻到底重新锁定；页幅 0.4×MaxValue，常量可调）。</summary>
        private static void ScrollPage(float dir)
        {
            try
            {
                if (_mode != ImChatMode.Full) return;
                if (_messageScrollPanel == null)
                {
                    FindMessageScrollPanel();
                    if (_messageScrollPanel == null) return;
                }
                var sb = _messageScrollPanel.VerticalScrollbar;
                if (sb == null) return;
                sb.ValueFloat = MathF.Clamp(sb.ValueFloat + dir * 0.4f * sb.MaxValue, 0f, sb.MaxValue);
                if (dir < 0f) _pinnedToBottom = false;                      // 上翻：解锁贴底
                else if (IsMessageAtBottom()) _pinnedToBottom = true;       // 下翻到底：重新锁定
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] 翻页滚动异常: {ex.Message}");
            }
        }

        // ── 转移矩阵（完整模式 ImChat.xml / 缩略模式 ImChatCompact.xml 各一套；回落点 C_sel）──

        /// <summary>C_sel 回落点（Fix 1 / v3）：当前选中会话所在行（_selected.Id 在频道行列表中定位）；不在列表 → -1。</summary>
        private static int CSelectedIdx()
        {
            if (_selected == null) return -1;
            return Idx("channel_" + _selected.Id);
        }

        private static int CSelectedIdxOrC1()
        {
            int s = CSelectedIdx();
            return s >= 0 ? s : Idx("c1");
        }

        private static int Idx(string id)
        {
            for (int i = 0; i < _padItems.Count; i++)
                if (_padItems[i].Id == id) return i;
            return -1;
        }

        /// <summary>第 n 个卡按钮（横排/竖排合并计数）在 _padItems 中的索引。</summary>
        private static int CardBtnIdx(int n)
        {
            int seen = 0;
            for (int i = 0; i < _padItems.Count; i++)
            {
                if (_padItems[i].Group == "cardbtn" || _padItems[i].Group == "cardbtnv")
                {
                    if (seen == n) return i;
                    seen++;
                }
            }
            return -1;
        }

        /// <summary>当前卡按钮在卡按钮组内的序号。</summary>
        private static int CardBtnIndex(PadItem cur)
        {
            int idx = 0;
            for (int i = 0; i < _padItems.Count; i++)
            {
                if (_padItems[i] == cur) return idx;
                if (_padItems[i].Group == "cardbtn" || _padItems[i].Group == "cardbtnv") idx++;
            }
            return -1;
        }

        /// <summary>最后一个卡按钮的索引（无卡 → -1）。</summary>
        private static int LastCardBtn()
        {
            int last = -1;
            for (int i = 0; i < _padItems.Count; i++)
                if (_padItems[i].Group == "cardbtn" || _padItems[i].Group == "cardbtnv") last = i;
            return last;
        }

        /// <summary>第 n 个频道行的索引（无 → -1）。</summary>
        private static int ChannelRowIdx(int n)
        {
            int seen = 0;
            for (int i = 0; i < _padItems.Count; i++)
            {
                if (_padItems[i].Group == "channel")
                {
                    if (seen == n) return i;
                    seen++;
                }
            }
            return -1;
        }

        /// <summary>当前焦点项在频道行内的序号。</summary>
        private static int ChannelRowIndex(PadItem cur)
        {
            int idx = 0;
            for (int i = 0; i < _padItems.Count; i++)
            {
                if (_padItems[i] == cur) return idx;
                if (_padItems[i].Group == "channel") idx++;
            }
            return -1;
        }

        private static int PadUpTarget(PadItem cur)
        {
            if (cur.Group == "dd")   // 下拉项：首项 → 末项循环
            {
                return _padIndex <= 0 ? _padItems.Count - 1 : _padIndex - 1;
            }
            if (_mode == ImChatMode.Compact)
            {
                switch (cur.Group)
                {
                    case "static":
                        switch (cur.Id)
                        {
                            case "k1": return Idx("send");            // 垂直闭环（v4：最上↑→最下，与 KS↓→K1 成环；原标题行内 ↑ 循环移除）
                            case "k2": return Idx("k3");
                            case "k3": return Idx("k4");
                            case "k4": return Idx("k5");
                            case "k5": return Idx("k1");
                            case "input": { int kbk = LastCardBtn(); return kbk >= 0 ? kbk : Idx("k1"); }   // KT ↑：上一项（KBK ? KBK : K1）
                            case "send": return Idx("input");
                        }
                        return -1;
                    case "cardbtn": return Idx("k1");                 // KB ↑ 回 K1（标题行是环，无回落误切风险）
                    case "cardbtnv":
                        { int j = CardBtnIndex(cur); return j <= 0 ? Idx("k1") : CardBtnIdx(j - 1); }
                }
                return -1;
            }
            // 完整模式
            switch (cur.Group)
            {
                case "static":
                    switch (cur.Id)
                    {
                        case "c1": return Idx("send");                // v5（2026-08-19 用户裁定）：缩略/关闭 按↑都是发送
                        case "c2": return Idx("send");                // v5：关闭 ↑ → 发送
                        case "cm": { int cbk = LastCardBtn(); if (cbk >= 0) return cbk; return CSelectedIdxOrC1(); }   // ↑：上一项（CBK 或 C_sel 或 C1）
                        case "input":                                 // CT ↑：CM ? CM : CBK ? CBK : C_sel（无频道行 → C1）
                        {
                            int cm = Idx("cm");
                            if (cm >= 0) return cm;
                            int cbk = LastCardBtn();
                            if (cbk >= 0) return cbk;
                            return CSelectedIdxOrC1();
                        }
                        case "send": return Idx("input");
                    }
                    return -1;
                case "channel":                                       // 频道列内部循环（v5 用户裁定：↑↓ 不出频道列，首行↑→末行）
                {
                    int i = ChannelRowIndex(cur);
                    return i <= 0 ? ChannelRowIdx(ChannelRowCount() - 1) : ChannelRowIdx(i - 1);
                }
                case "cardbtn": return CSelectedIdxOrC1();            // 横排 ↑ → C_sel（选中行；无频道行 → C1）
                case "cardbtnv":
                    { int j = CardBtnIndex(cur); return j <= 0 ? CSelectedIdxOrC1() : CardBtnIdx(j - 1); }
            }
            return -1;
        }

        private static int PadDownTarget(PadItem cur)
        {
            if (cur.Group == "dd")   // 下拉项：末项 → 首项循环
            {
                return _padIndex >= _padItems.Count - 1 ? 0 : _padIndex + 1;
            }
            if (_mode == ImChatMode.Compact)
            {
                switch (cur.Group)
                {
                    case "static":
                        switch (cur.Id)
                        {
                            case "k1": case "k2": case "k3": case "k4": case "k5":
                                // v6 2026-08-18 用户裁定：↓ 直达输入框（不再先落锚点卡按钮 KB1——
                                // 有卡时 ↓ 落 KB1 视觉上是卡片按钮，玩家以为「不能选 input」）
                                return Idx("input");
                            case "input": return Idx("send");
                            case "send": return Idx("k1");            // 底部循环回标题行
                        }
                        return -1;
                    case "cardbtn": return Idx("input");              // KB ↓ → KT
                    case "cardbtnv":
                        { int j = CardBtnIndex(cur); return j >= _padCardBtnCount - 1 ? Idx("input") : CardBtnIdx(j + 1); }
                }
                return -1;
            }
            // 完整模式
            switch (cur.Group)
            {
                case "static":
                    switch (cur.Id)
                    {
                        case "c1": case "c2":                         // v5（2026-08-19 用户裁定）：缩略/关闭 按↓都是发送
                            return Idx("send");
                        case "cm": return Idx("input");
                        case "input": return Idx("send");
                        case "send": return Idx("c1");                // 主干底部循环回顶部
                    }
                    return -1;
                case "channel":                                       // 频道列内部循环（v5 用户裁定：末行↓→首行，不出列）
                {
                    int i = ChannelRowIndex(cur);
                    return i >= ChannelRowCount() - 1 ? ChannelRowIdx(0) : ChannelRowIdx(i + 1);
                }
                case "cardbtn": return Idx("cm") >= 0 ? Idx("cm") : Idx("input");   // CM（有新消息条）? CM : CT
                case "cardbtnv":
                {
                    int j = CardBtnIndex(cur);
                    if (j >= _padCardBtnCount - 1)
                    {
                        int cm = Idx("cm");
                        return cm >= 0 ? cm : Idx("input");
                    }
                    return CardBtnIdx(j + 1);
                }
            }
            return -1;
        }

        private static int ChannelRowCount()
        {
            int n = 0;
            for (int i = 0; i < _padItems.Count; i++) if (_padItems[i].Group == "channel") n++;
            return n;
        }

        private static int PadLeftTarget(PadItem cur)
        {
            if (_mode == ImChatMode.Compact)
            {
                switch (cur.Group)
                {
                    case "static":
                        switch (cur.Id)
                        {
                            case "k1": return Idx("k5");              // 标题行横向循环（← 逆序）
                            case "k2": return Idx("k1");
                            case "k3": return Idx("k2");
                            case "k4": return Idx("k3");
                            case "k5": return Idx("k4");
                            case "input": return Idx("send");         // KT ← 与 → 对称成环（输入区）
                            case "send": return Idx("input");
                        }
                        return -1;
                    case "cardbtn":                                   // 横排双向（v4 2026-08-18：← 左移；KB1 ← 出口标题行）
                    {
                        int j = CardBtnIndex(cur);
                        return j > 0 ? CardBtnIdx(j - 1) : Idx("k1");
                    }
                    case "cardbtnv": return Idx("k1");                // 竖排 ← 出口标题行
                }
                return -1;
            }
            // 完整模式
            switch (cur.Group)
            {
                case "static":
                    switch (cur.Id)
                    {
                        case "c1":                                    // ← 直达第一个频道（用户裁定 2026-08-18：缩略按钮左侧 = 频道列）
                        {
                            int first = ChannelRowIdx(0);
                            if (first >= 0) return first;
                            return CSelectedIdxOrC1();                // 无频道行 → C_sel 兜底
                        }
                        case "c2": return Idx("c1");
                        case "cm": return CSelectedIdxOrC1();         // ← 回左栏选中行
                        case "input": return CSelectedIdxOrC1();      // v6（2026-08-18 用户裁定）：输入框 ← 到频道列（不再与 send 成环）
                        case "send": return Idx("input");
                    }
                    return -1;
                case "channel": { int cb1 = CardBtnIdx(0); return cb1 >= 0 ? cb1 : Idx("input"); }  // 左缘回绕进消息区（与 → 同；无卡 → CT）
                case "cardbtn": { int sel = CSelectedIdx(); return sel >= 0 ? sel : -1; }   // ← 回选中行
                case "cardbtnv": return CSelectedIdxOrC1();           // 竖排 ← 退出左栏
            }
            return -1;
        }

        private static int PadRightTarget(PadItem cur)
        {
            if (_mode == ImChatMode.Compact)
            {
                switch (cur.Group)
                {
                    case "static":
                        switch (cur.Id)
                        {
                            case "k1": return Idx("k2");              // 标题行横向循环（→ 顺序）
                            case "k2": return Idx("k3");
                            case "k3": return Idx("k4");
                            case "k4": return Idx("k5");
                            case "k5": return Idx("k1");              // → 环完成（v4：K5 → 回 K1）
                            case "input": return Idx("send");
                            case "send": return Idx("input");         // KS → 与 ← 对称成环
                        }
                        return -1;
                    case "cardbtn":
                    {
                        int j = CardBtnIndex(cur);
                        return j >= _padCardBtnCount - 1 ? Idx("input") : CardBtnIdx(j + 1);   // 最后一个 → KT
                    }
                    case "cardbtnv": return Idx("input");             // 竖排 → 沿主干向下
                }
                return -1;
            }
            // 完整模式
            switch (cur.Group)
            {
                case "static":
                    switch (cur.Id)
                    {
                        case "c1": return Idx("c2");
                        case "c2": return CSelectedIdxOrC1();         // v5（2026-08-19 用户裁定）：关闭 → 回频道（当前选中行，水平环闭合）
                        case "cm": return Idx("send");                // → 右下发送
                        case "input": return Idx("send");
                        case "send": return CSelectedIdxOrC1();       // v6（2026-08-18 用户裁定）：发送 → 频道列（不再与 input 成环）
                    }
                    return -1;
                case "channel": return Idx("c1");                     // v5（2026-08-19 用户裁定）：频道 → 先到缩略（水平环起点）
                case "cardbtn":
                {
                    int j = CardBtnIndex(cur);
                    if (j >= _padCardBtnCount - 1)
                    {
                        int cm = Idx("cm");
                        return cm >= 0 ? cm : Idx("input");           // 最后一个 → CM（有新消息条）? CM : CT
                    }
                    return CardBtnIdx(j + 1);
                }
                case "cardbtnv": { int cm = Idx("cm"); return cm >= 0 ? cm : Idx("input"); }   // 竖排 → 沿主干向下
            }
            return -1;
        }

        // ── 重建（RebuildPadNavigation）：结构变化时（_padNavDirty）──

        /// <summary>
        /// 重建焦点项表（锚点卡引用/按钮集/模式/会话/下拉变化时；Fix 4 节流——0.3s 轮询不置 dirty）。
        /// 重建后焦点按稳定 Id 映射旧项（v3 §六.6，禁止裸索引保持——中间插入/删除会整体错位）；
        /// 映射失败（项已消失）→ 钳制到相邻项；下拉收起 → 焦点回 K2；初始焦点 = 索引 0。
        /// </summary>
        private static void RebuildPadNavigation()
        {
            string oldId = _padIndex >= 0 && _padIndex < _padItems.Count ? _padItems[_padIndex].Id : null;
            int oldIdx = _padIndex;
            bool wasDropdown = oldId != null && oldId.StartsWith("dd_");
            if (_vm == null || _layer?.UIContext?.Root == null)
            {
                // 树未就绪（LoadMovie 首帧等）：保留 dirty，下一帧重试（否则导航永久停摆）
                _padIndex = -1;
                return;
            }
            _padNavDirty = false;
            _padItems.Clear();
            _padCardBtnCount = 0;

            bool dropdownOpen = _mode == ImChatMode.Compact && _vm.ChannelSelector.IsChannelListOpen;
            if (dropdownOpen) BuildDropdownPadItems();
            else if (_mode == ImChatMode.Compact) BuildCompactPadItems();
            else BuildFullPadItems();

            if (_padItems.Count == 0) { _padIndex = -1; return; }

            if (dropdownOpen)
            {
                // 下拉接管：初始焦点（v3）= 当前选中项（IsSelected 行；无 → 首项）——20+ 频道时落首项太远
                int init = -1;
                for (int i = 0; i < _padItems.Count; i++)
                {
                    if (_padItems[i].Tag is ImChannelOptionVM opt && opt.IsSelected) { init = i; break; }
                }
                _padIndex = init >= 0 ? init : 0;
                ScrollPadIntoView(_padItems[_padIndex]);   // 选中项可能在列表视口外 → 滚到可见
            }
            else if (wasDropdown)
            {
                _padIndex = Idx("k2") >= 0 ? Idx("k2") : 0;   // 下拉收起 → 焦点回中心按钮
            }
            else if (oldId != null)
            {
                int mapped = Idx(oldId);
                _padIndex = mapped >= 0 ? mapped : (int)MathF.Clamp(oldIdx, 0f, _padItems.Count - 1f);
            }
            else
            {
                // 🔴 2026-08-19（用户裁定：缩略半模态聚焦门控）：缩略模式初始 = 无焦点（-1）——
                // 打开后玩家继续玩（A 跳跃还给游戏），首次按十字键才进入聚焦（见 UpdatePadFocus）；
                // 完整模式 = 模态恒聚焦（索引 0 立即可见）。
                _padIndex = _mode == ImChatMode.Compact ? -1 : 0;
            }
            // 完整模式兜底钳制（缩略模式的 -1 = 合法的无焦点态，不许钳）
            if (_padIndex < 0 && _mode != ImChatMode.Compact) _padIndex = 0;
            // 🔴 2026-08-18（诊断日志）：重建结果 + 焦点映射（oldId → newId）——结构变化后焦点去向
            string newId = _padIndex >= 0 && _padIndex < _padItems.Count ? _padItems[_padIndex].Id : "无";
            if (PadDbg) DebugLogger.Log($"[Pad] 重建: {_padItems.Count}项 old={oldId ?? "无"} → {newId}");
            ApplyPadVisual();
        }

        /// <summary>完整模式焦点项：C1 缩略 / C2 关闭 / C3..CN 频道行 / CB 锚点卡按钮 / CM 新消息条 / CT 输入框 / CS 发送。</summary>
        private static void BuildFullPadItems()
        {
            _padItems.Add(new PadItem { Id = "c1", Group = "static", OnActivate = ToggleCompact, GetWidget = WidgetLookup("LWN_BtnCompact") });
            _padItems.Add(new PadItem { Id = "c2", Group = "static", OnActivate = Close, GetWidget = WidgetLookup("LWN_BtnClose") });
            // 频道行（跳过分组标题；构建顺序 = 屏幕视觉顺序）
            for (int i = 0; i < _vm.ChannelList.Count; i++)
            {
                var ch = _vm.ChannelList[i];
                if (ch == null || ch.IsGroupHeader) continue;
                var conv = ch.Conversation;
                int listIdx = i;   // ChannelList 索引（含分组标题；ChannelRowWidget 按此取行）
                _padItems.Add(new PadItem
                {
                    Id = "channel_" + conv.Id,
                    Group = "channel",
                    Tag = conv,
                    // 移动即激活（微信式预览）；已在当前会话 → A/移动落点无操作
                    OnActivate = () => { if (_selected != conv) SelectConversation(conv); },
                    GetWidget = () => ChannelRowWidget(listIdx),
                });
            }
            // 锚点卡按钮（🔴 数据源互斥：IsVerticalButtons ? VerticalCardButtons : CardButtons，v3）
            var anchorVm = _vm.Messages.FirstOrDefault(vm => vm != null && vm.IsCardAnchor);
            if (anchorVm != null && anchorVm.Message != null)
            {
                var btns = anchorVm.IsVerticalButtons ? anchorVm.VerticalCardButtons : anchorVm.CardButtons;
                int msgIndex = _vm.Messages.IndexOf(anchorVm);
                string stamp = anchorVm.Message.TimeStamp.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
                _padCardBtnCount = btns.Count;
                for (int j = 0; j < btns.Count; j++)
                {
                    var btn = btns[j];
                    int jj = j;
                    _padItems.Add(new PadItem
                    {
                        Id = $"cardbtn_{stamp}_{j}",
                        Group = anchorVm.IsVerticalButtons ? "cardbtnv" : "cardbtn",
                        OnActivate = () =>
                        {
                            try { btn.Execute(); } catch (Exception ex) { DebugLogger.Log($"[ImChat] 卡片按钮激活失败: {ex.Message}"); }
                        },
                        GetWidget = () => CardButtonWidget(msgIndex, jj, anchorVm),
                    });
                }
            }
            // CM 新消息条（仅可见时入列）
            if (_vm.HasNewMessageHint)
                _padItems.Add(new PadItem { Id = "cm", Group = "static", OnActivate = ExecuteNewMessageClick, GetWidget = WidgetLookup("LWN_BtnNewMsg") });
            // CT 输入框 / CS 发送
            _padItems.Add(new PadItem { Id = "input", Group = "static", OnActivate = FocusInputWidget, GetWidget = WidgetLookup("LWN_ImChat_Input") });
            _padItems.Add(new PadItem { Id = "send", Group = "static", OnActivate = ExecuteSend, GetWidget = WidgetLookup("LWN_BtnSend") });
        }

        /// <summary>缩略模式焦点项：K1..K5 标题行 / KB 锚点卡按钮 / KT 输入框 / KS 发送。</summary>
        private static void BuildCompactPadItems()
        {
            _padItems.Add(new PadItem { Id = "k1", Group = "static", OnActivate = SelectPreviousChannel, GetWidget = WidgetLookup("LWN_BtnPrev") });
            _padItems.Add(new PadItem { Id = "k2", Group = "static", OnActivate = ToggleChannelList, GetWidget = WidgetLookup("LWN_BtnCenter") });
            _padItems.Add(new PadItem { Id = "k3", Group = "static", OnActivate = SelectNextChannel, GetWidget = WidgetLookup("LWN_BtnNext") });
            _padItems.Add(new PadItem { Id = "k4", Group = "static", OnActivate = ToggleExpand, GetWidget = WidgetLookup("LWN_BtnExpand") });
            _padItems.Add(new PadItem { Id = "k5", Group = "static", OnActivate = Close, GetWidget = WidgetLookup("LWN_BtnCloseC") });
            // 锚点卡按钮（与完整模式同构）
            var anchorVm = _vm.Messages.FirstOrDefault(vm => vm != null && vm.IsCardAnchor);
            if (anchorVm != null && anchorVm.Message != null)
            {
                var btns = anchorVm.IsVerticalButtons ? anchorVm.VerticalCardButtons : anchorVm.CardButtons;
                string stamp = anchorVm.Message.TimeStamp.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
                _padCardBtnCount = btns.Count;
                for (int j = 0; j < btns.Count; j++)
                {
                    var btn = btns[j];
                    int jj = j;
                    _padItems.Add(new PadItem
                    {
                        Id = $"cardbtn_{stamp}_{j}",
                        Group = anchorVm.IsVerticalButtons ? "cardbtnv" : "cardbtn",
                        OnActivate = () =>
                        {
                            try { btn.Execute(); } catch (Exception ex) { DebugLogger.Log($"[ImChat] 卡片按钮激活失败: {ex.Message}"); }
                        },
                        GetWidget = () => CompactCardButtonWidget(jj, anchorVm),
                    });
                }
            }
            // KT 输入框 / KS 发送
            _padItems.Add(new PadItem { Id = "input", Group = "static", OnActivate = FocusInputWidget, GetWidget = WidgetLookup("LWN_ImChat_CompactInput") });
            _padItems.Add(new PadItem { Id = "send", Group = "static", OnActivate = ExecuteSend, GetWidget = WidgetLookup("LWN_BtnSendC") });
        }

        /// <summary>缩略模式下拉焦点项（仅 IsChannelListOpen 时构建；A = ExecuteSelect 选中 + 收起）。</summary>
        private static void BuildDropdownPadItems()
        {
            var opts = _vm.ChannelSelector.ItemList;
            for (int i = 0; i < opts.Count; i++)
            {
                var opt = opts[i];
                if (opt == null) continue;
                int ii = i;
                _padItems.Add(new PadItem
                {
                    Id = "dd_" + opt.ConversationId,
                    Group = "dd",
                    Tag = opt,
                    // 选中 + 收起（焦点回 K2 由重建映射：wasDropdown → k2）
                    OnActivate = () =>
                    {
                        try { opt.ExecuteSelect(); } catch (Exception ex) { DebugLogger.Log($"[ImChat] 下拉项激活失败: {ex.Message}"); }
                    },
                    GetWidget = () => DropdownItemWidget(ii),
                });
            }
        }

        // ── 视觉 widget 定位 ──

        /// <summary>静态按钮 lazy 查找（每次聚焦时从当前树找——模式切换后旧树失效自动换新，零缓存风险）。</summary>
        private static Func<Widget> WidgetLookup(string id) => () =>
        {
            try { return FindWidgetById(_layer?.UIContext?.Root, id); } catch { return null; }
        };

        /// <summary>频道行 widget（完整模式左栏）：LWN_ImChat_ChannelInner 按 ChannelList 索引取行 →
        /// 行内 child 1 = 频道行 ButtonWidget（child 0 = 分组标题）。</summary>
        private static Widget ChannelRowWidget(int channelListIndex)
        {
            var inner = FindWidgetById(_layer?.UIContext?.Root, "LWN_ImChat_ChannelInner");
            if (inner == null || channelListIndex < 0 || channelListIndex >= inner.ChildCount) return null;
            var item = inner.GetChild(channelListIndex);
            return item != null && item.ChildCount > 1 ? item.GetChild(1) : null;
        }

        /// <summary>
        /// 锚点卡按钮行 widget（完整模式）。🔴 禁止 FindWidgetById("LWN_ImChat_BubbleCard")——ItemTemplate
        /// 内每张卡重复，命中树中第一张旧卡 → 高亮错位（v3）。改按消息索引定位：
        /// LWN_ImChat_MessageInner.GetChild(锚点在 _vm.Messages 中的索引) 取行 → 行内按锚点卡类型分叉：
        /// 卡片气泡（非旧格式）= 行 child2（容器）→ 0（贴内容）→ 0（BubbleCard）→ child 3横/4竖 → GetChild(j)；
        /// 旧格式计划卡 = 行 child4（容器）→ 0（PlanCardBody）→ child 2横/3竖 → GetChild(j)。
        /// 查找失败（按钮行未构建/不可见）→ null（该项仍可 A 激活，视觉暂缺，重建后补齐）。
        /// </summary>
        private static Widget CardButtonWidget(int msgIndex, int btnIndex, ImMessageVM anchorVm)
        {
            var inner = FindWidgetById(_layer?.UIContext?.Root, "LWN_ImChat_MessageInner");
            if (inner == null || msgIndex < 0 || msgIndex >= inner.ChildCount) return null;
            var row = inner.GetChild(msgIndex);
            if (row == null) return null;
            ListPanel btnRow = null;
            if (anchorVm != null && anchorVm.IsLegacyPlanCard)
            {
                var body = row.ChildCount > 4 ? row.GetChild(4)?.GetChild(0) : null;   // 旧格式卡容器 → LWN_ImChat_PlanCardBody
                if (body != null)
                    btnRow = body.GetChild(anchorVm.IsVerticalButtons ? 3 : 2) as ListPanel;
            }
            else
            {
                var bubble = row.ChildCount > 2 ? row.GetChild(2)?.GetChild(0)?.GetChild(0) : null;   // 卡片气泡容器 → 贴内容 → LWN_ImChat_BubbleCard
                if (bubble != null)
                    btnRow = bubble.GetChild(anchorVm.IsVerticalButtons ? 4 : 3) as ListPanel;
            }
            if (btnRow == null || btnIndex < 0 || btnIndex >= btnRow.ChildCount) return null;
            return btnRow.GetChild(btnIndex);
        }

        /// <summary>锚点卡按钮行 widget（缩略模式）：LWN_ImChat_CompactCard 单卡全树唯一 → child 3横/4竖 → GetChild(j)。</summary>
        private static Widget CompactCardButtonWidget(int btnIndex, ImMessageVM anchorVm)
        {
            var card = FindWidgetById(_layer?.UIContext?.Root, "LWN_ImChat_CompactCard");
            if (card == null || anchorVm == null) return null;
            var btnRow = card.GetChild(anchorVm.IsVerticalButtons ? 4 : 3) as ListPanel;
            if (btnRow == null || btnIndex < 0 || btnIndex >= btnRow.ChildCount) return null;
            return btnRow.GetChild(btnIndex);
        }

        /// <summary>下拉项 widget（缩略）：LWN_ImChat_ChannelListInner → GetChild(j) → child 0 = ImageWidget
        ///（手动命中同款视觉目标——SetState 打 ImageWidget，引擎不覆盖）。</summary>
        private static Widget DropdownItemWidget(int itemIndex)
        {
            var inner = FindWidgetById(_layer?.UIContext?.Root, "LWN_ImChat_ChannelListInner");
            if (inner == null || itemIndex < 0 || itemIndex >= inner.ChildCount) return null;
            var btn = inner.GetChild(itemIndex);
            return btn != null && btn.ChildCount > 0 ? btn.GetChild(0) : null;
        }

        /// <summary>CT/KT 激活：聚焦输入框（EventManager.FocusedWidget public setter，反编译实锤——设为
        /// EditableTextWidget 且控制器激活 → _isOnScreenKeyboardRequested = true → 自动弹软键盘）。</summary>
        private static void FocusInputWidget()
        {
            try
            {
                var w = _padIndex >= 0 && _padIndex < _padItems.Count ? _padItems[_padIndex].GetWidget?.Invoke() : null;
                // 🔴 2026-08-18（坑 12 诊断）：聚焦结果打日志——w 为 null（查找失败/静默跳过）vs
                // 设置成功（IsFocusedOnInput 立即 true）；区分「没聚焦」与「聚焦了但设备翻转搞死」
                if (w == null)
                {
                if (PadDbg) DebugLogger.Log("[Pad] 聚焦输入框失败: widget 查找为 null（静默跳过）");
                    return;
                }
                // 🔴 2026-08-19（用户裁定）：光标挪位 + 准星隐藏已在 ActivatePad（A 键统一入口）完成——
                // 本行鼠标位置应等于输入框中心（验证 SetMouseToWidget 生效；若仍显示屏幕中心 = 光标
                // 冻结读数/锚定覆盖，据此判定是否需要 P/Invoke 兜底）。OsCursorStr = 真实 OS 光标，
                // 与引擎读数对照：相等 = 光标真没动（锚定覆盖），不等 = 引擎读数冻结（光标已挪位）。
                if (PadDbg) DebugLogger.Log($"[Pad] 聚焦输入框 → {w.Id} ({(w is EditableTextWidget ? "EditableText" : w.GetType().Name)}) 引擎光标={MousePosStr()} OS光标={OsCursorStr()} 输入框位置={WidgetPosStr(w)}");
                _layer.UIContext.EventManager.FocusedWidget = w;
                // 🔴 2026-08-19（焦点再固守）：native 点击链的 click-up（锚定回拽后的屏幕中心）会在
                // 1-2 帧内清掉焦点——登记 3 帧再固守（UpdatePadFocus 消费；只在焦点已被清时重设）
                _focusReaffirmFrames = 3;
                _reaffirmWidget = w;
                if (PadDbg) DebugLogger.Log($"[Pad] FocusedWidget 设置完成 IsFocusedOnInput={_layer.IsFocusedOnInput()}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] 聚焦输入框失败: {ex.Message}");
            }
        }

        // ── 焦点视觉（Hovered 复用 hover 视觉零新 Brush；旧焦点按身份复位）──

        /// <summary>新焦点 SetState("Hovered")；旧焦点按身份复位（ButtonWidget.IsSelected ? "Selected" : "Default"）。
        /// 频道行无独立视觉（自身 IsSelected 视觉即焦点视觉，移动即激活，不额外 SetState）。
        /// 查找失败 → 视觉暂缺（不影响 A 激活，重建后补齐）。</summary>
        private static void ApplyPadVisual()
        {
            for (int i = 0; i < _padItems.Count; i++)
            {
                var item = _padItems[i];
                if (item.Group == "channel") continue;
                if (i == _padIndex)
                {
                    Widget w = null;
                    try { w = item.GetWidget?.Invoke(); } catch { }
                    if (w == null) continue;
                    if (w != item.LastWidget)
                    {
                        if (item.LastWidget != null) RestorePadVisual(item);
                        item.LastWidget = w;
                    }
                    try { w.SetState("Hovered"); } catch { }
                }
                else if (item.LastWidget != null)
                {
                    RestorePadVisual(item);
                    item.LastWidget = null;
                }
            }
        }

        /// <summary>旧焦点复位（身份复位：ButtonWidget 自身/父级选中态 → Selected，否则 Default）。</summary>
        private static void RestorePadVisual(PadItem item)
        {
            if (item?.LastWidget == null) return;
            try
            {
                var w = item.LastWidget;
                bool sel = w is ButtonWidget bw ? bw.IsSelected
                    : (w.ParentWidget as ButtonWidget)?.IsSelected == true;
                w.SetState(sel ? "Selected" : "Default");
            }
            catch { }
        }

        /// <summary>设备切鼠标 / 面板关闭：焦点复位（高亮清理 + 索引 -1 + 计时器清零）。</summary>
        private static void ResetPadFocus()
        {
            if (_padIndex >= 0)
            {
                for (int i = 0; i < _padItems.Count; i++)
                {
                    if (_padItems[i].LastWidget != null)
                    {
                        RestorePadVisual(_padItems[i]);
                        _padItems[i].LastWidget = null;
                    }
                }
                _padIndex = -1;
            }
            ResetPadHoldTimers();
        }

        private static void ResetPadHoldTimers()
        {
            _lastPadUp = _lastPadDown = _lastPadLeft = _lastPadRight = _lastPadA = _lastPadLB = _lastPadRB = false;
            _padHoldUp = _padHoldDown = _padHoldLeft = _padHoldRight = _padHoldLB = _padHoldRB = 0f;
            _padRepeatUp = _padRepeatDown = _padRepeatLeft = _padRepeatRight = _padRepeatLB = _padRepeatRB = 0f;
        }

        /// <summary>
        /// 🔴 2026-08-18（实机三连击根因修复）：引擎手柄导航整体屏蔽/解除。
        /// 背景：prefab 原声明 NavigationScopeTargeter（IsDefaultNavigationScope=true）——引擎自动夺取
        /// 导航焦点并把光标瞬移到 scope 中心/最近 widget（GainNavigationAfterFrames →
        /// MoveCursorToFirstAvailableWidgetInScope / MoveCursorToBestAvailableScope，反编译实锤）；
        /// 光标被引擎挪动 → Input.IsMouseActive → IsGamepadActive=false → 手动导航（UsingGamepad 门控）
        /// 停摆；同时 D-pad 仍被原版下层 UI 的 scope 消费（campaign 地图按钮被十字键拨动）。
        /// 修复：① prefab 删除 NavigationScopeTargeter（断光标瞬移源）；② 本方法 = 面板根 widget 声明
        /// UsedNavigationMovements + IsUsingNavigation → GauntletGamepadNavigationManager.AnyWidgetUsingNavigation
        /// → OnGamepadNavigation 早退（反编译实锤）——引擎导航整体冻结，手动导航独占十字键，
        /// 原版下层 UI 不再响应 D-pad。Close 时解除（UsedNavigationMovements=None 从屏蔽列表移除）。
        /// </summary>
        private static void SetEngineGamepadNavBlocked(bool blocked)
        {
            try
            {
                var root = FindWidgetById(_layer?.UIContext?.Root, "LWN");
                if (root == null) return;
                root.UsedNavigationMovements = blocked
                    ? (GamepadNavigationTypes.Horizontal | GamepadNavigationTypes.Vertical)
                    : GamepadNavigationTypes.None;
                root.IsUsingNavigation = blocked;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] 引擎导航{(blocked ? "屏蔽" : "解除")}失败: {ex.Message}");
            }
        }

        // ── 焦点滚动跟随（v3 §六.5）：焦点落位后目标在视口外 → 对应面板滚动把目标滚入视野 ──

        /// <summary>频道行 → ChannelScroll；卡按钮 → MessageScroll；下拉项 → 下拉内 ScrollablePanel。
        /// 像素→val 换算按 (内容高-可视高)/MaxValue 线性比例（方向两版本一致：val 增 = 内容上移 =
        /// 靠底/靠新；经贴底语义反推）。只在目标真正不可见时触发一次，不干扰 LB/RB 手动翻页。</summary>
        private static void ScrollPadIntoView(PadItem item)
        {
            if (item == null) return;
            Widget w = null;
            try { w = item.GetWidget?.Invoke(); } catch { }
            if (w == null) return;
            ScrollablePanel panel = null;
            if (item.Group == "channel")
                panel = FindWidgetById(_layer?.UIContext?.Root, "ChannelScroll") as ScrollablePanel;
            else if (item.Group == "cardbtn" || item.Group == "cardbtnv")
                panel = _messageScrollPanel;
            else if (item.Group == "dd")
            {
                try { panel = _compactChannelList?.GetChild(0) as ScrollablePanel; } catch { }
            }
            if (panel == null || panel.VerticalScrollbar == null) return;
            try
            {
                var sb = panel.VerticalScrollbar;
                const float pad = 8f;
                float vTop = panel.GlobalPosition.Y;
                float vBottom = vTop + panel.Size.Y;
                float tTop = w.GlobalPosition.Y;
                float tBottom = tTop + w.Size.Y;
                if (tTop >= vTop + pad && tBottom <= vBottom - pad) return;   // 已可见
                float inner = panel.InnerPanel?.Size.Y ?? 0f;
                float clip = panel.ClipRect?.Size.Y ?? 0f;
                if (clip <= 0f || inner <= clip) return;                      // 无滚动空间
                float ratio = sb.MaxValue / (inner - clip);
                float deltaVal = tBottom > vBottom ? (tBottom - vBottom + pad) * ratio
                    : (tTop - vTop - pad) * ratio;                             // 上溢出为负 → val 减小（往历史）
                if (MathF.Abs(deltaVal) < 0.5f) return;
                sb.ValueFloat = MathF.Clamp(sb.ValueFloat + deltaVal, 0f, sb.MaxValue);
                // 贴底状态机协同：向上滚出解锁；向下滚到底重锁（与 HandleManualScroll 同语义）
                if (deltaVal < 0f) _pinnedToBottom = false;
                else if (IsMessageAtBottom()) _pinnedToBottom = true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] 焦点滚动跟随异常: {ex.Message}");
            }
        }

        // ── 重建节流（Fix 4 / v3）：锚点卡引用 + 按钮数 + 横竖标记 比对，任一变化 → 置 dirty ──

        /// <summary>结构比对（UpdateCardAnchors / NotifyPlanStateChanged 末尾调用——结构变化的汇聚点）。
        /// 锚点卡引用变 / 按钮集数量变 / IsVerticalButtons 横竖翻转（按钮数不变也触发）→ _padNavDirty。
        /// 0.3s 轮询 RefreshMessages 本身不置 dirty——比对命中的结构变化才会（防焦点跳变与长按抖动）。</summary>
        private static void CheckPadNavStructureChange()
        {
            if (_vm == null) return;
            ImMessage anchorRef = null;
            int btnCount = 0;
            bool vertical = false;
            foreach (var vm in _vm.Messages)
            {
                if (vm != null && vm.IsCardAnchor)
                {
                    anchorRef = vm.Message;
                    var list = vm.IsVerticalButtons ? vm.VerticalCardButtons : vm.CardButtons;
                    btnCount = list.Count;
                    vertical = vm.IsVerticalButtons;
                    break;
                }
            }
            if (anchorRef != _padNavAnchorRef || btnCount != _padNavAnchorBtnCount || vertical != _padNavAnchorVertical)
            {
                _padNavAnchorRef = anchorRef;
                _padNavAnchorBtnCount = btnCount;
                _padNavAnchorVertical = vertical;
                _padNavDirty = true;
            }
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
                // 🔴 2026-08-19：清掉焦点再固守登记（防切换后对已释放树重设 FocusedWidget）
                _focusReaffirmFrames = 0;
                _reaffirmWidget = null;
                _movie = _layer.LoadMovie(_mode == ImChatMode.Compact ? "ImChatCompact" : "ImChat", _vm);
                _messageScrollPanel = null;
                _compactPanel = null;
                _compactChannelList = null;
                // 🔴 2026-08-18（实机三连击根因修复）：模式切换 = 新 prefab 树 → 重新屏蔽引擎导航
                SetEngineGamepadNavBlocked(true);
                // 🔴 Q4（2026-08-18，手动导航）：模式切换 → 焦点复位 + 重建（新 prefab 树；初始焦点 = 索引 0）
                ResetPadFocus();
                _padNavDirty = true;
                _padItems.Clear();
                // 🔴 2026-08-17（UI 模式）：模式切换后 mask 立即按新模式设置（防残留——旧行为依赖
                // HandleCompactInput 首帧修正，完整模式无修正点会残留缩略 mask）
                // 🔴 2026-08-18：模式切换后 mask 随设备立即生效（手柄 → 隐藏光标，防原生手柄光标模式锁死）
                ApplyInputMask();
                    RefreshAll();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] 模式切换失败: {ex.Message}");
            }
        }

        /// <summary>完整模式 → 缩略模式（标题带「缩略」按钮，关闭按钮左侧）。
        /// 🔴 2026-08-17（用户裁定：缩略 = UI 模式）：切缩略时 Mission 内提示镜头操作变化
        ///（鼠标 = 面板操作，按住右键旋转镜头）——统一走 <see cref="ShowCompactCameraHintIfNeeded"/>
        ///（覆盖「Mission 内直接以缩略打开」路径，见 Open）。</summary>
        public static void ToggleCompact()
        {
            if (_mode == ImChatMode.Compact) return;
            _mode = ImChatMode.Compact;
            SwitchMode();
            ShowCompactCameraHintIfNeeded();
        }

        /// <summary>缩略模式 → 完整模式（缩略标题行「放大」按钮）。
        /// 🔴 2026-08-17（用户反馈）：退出缩略模式 = 鼠标控制恢复 → Mission 内提示（光标模式下
        /// 鼠标移动不转向，玩家需要知道恢复操作方式）。</summary>
        public static void ToggleExpand()
        {
            if (_mode != ImChatMode.Compact) return;
            _mode = ImChatMode.Full;
            SwitchMode();
            // 🔴 临时诊断（2026-08-19 mission 鼠标转镜头排查，测完删）
            DebugLogger.Log($"[ImChat] ToggleExpand → Full gamepad={ModInput.UsingGamepad}");
            if (Mission.Current != null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    // 本地化：缩略模式镜头恢复提示（LWN_im_compact_camera_restored）
                    LWNTextHelper.ResolveText("LWN_im_compact_camera_restored",
                        "Mouse control restored: move the mouse to rotate the camera.")));
            }
        }

        /// <summary>缩略模式镜头提示（🔴 2026-08-17 用户反馈：**所有进入缩略模式的路径**都要提示——
        /// ① 放大→缩略（ToggleCompact）；② Mission 内直接以缩略打开（Open 按 _mode 记忆——上次关闭时
        /// 是缩略，下一次开启仍是缩略）。大地图无镜头问题（地图拖拽），不提示。</summary>
        private static void ShowCompactCameraHintIfNeeded()
        {
            if (_mode != ImChatMode.Compact || Mission.Current == null) return;
            InformationManager.DisplayMessage(new InformationMessage(
                // 本地化：缩略模式镜头提示（LWN_im_compact_camera_hint）
                LWNTextHelper.ResolveText("LWN_im_compact_camera_hint",
                    "Compact panel active: mouse controls the panel, hold right mouse button to rotate the camera.")));
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
            // 🔴 Q4（2026-08-18，手动导航）：切会话 = 结构变化（频道行/消息流/锚点卡全换）→ 重建
            _padNavDirty = true;
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
            // 🔴 Q4（2026-08-18，手动导航）：锚点卡结构比对（引用/按钮数/横竖标记）——任一变化 → 重建
            CheckPadNavStructureChange();
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
            // 🔴 Q4（2026-08-18，手动导航）：讲解完成/自审回调 → RebuildCardButtons 可能改变按钮集 → 结构比对
            CheckPadNavStructureChange();
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
                // 🔴 2026-08-17（实机「Mission 内无法呼出」）：OpenOrExpand——缩略开着时 O = 放大为完整模式
                if (ModInput.ShortFired(InteractionIds.IM) && Settings.Instance.PlotEnabled)
                    OpenOrExpand();

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
            // 🔴 2026-08-19（用户裁定：自监测输入来源每帧更新——必须在本类任何设备判定消费之前，
            // 且面板开闭都跑：InteractArea 键帽等全 Mod 共用 ModInput.UsingGamepad）
            ModInput.TickInputSource();
            // 🔴 世界背景生成同样依赖墙钟帧（暂停也运转）——与 IM 同轮子（ImScreenFrameTickPatch）：
            // CampaignEvents.TickEvent 暂停时 dt=0 停发，世界背景会永不生成（2026-08-17 实机教训）
            WorldBackgroundBehavior.Instance?.OnFrameTick(dt);
            ImChatManager.Tick(dt);
            // 🔴 2026-08-15（密信通知）：通知层驱动（自动消失计时）挂在 IM Tick 上——
            // 不依赖面板是否打开（提前 return 之前），Mission/Campaign 双端都到这里。
            // 🔴 2026-08-17（用户裁定）：ImSecretNotify（ninjareport 密信圆环）已废除——私聊通知
            // 统一由呼出按钮徽标承担（ImChatOpenButtonManager 自行订阅 MessageArrived），此处不再驱动。
            // 🔴 2026-08-17（实机「Mission 内无法呼出」根因）：Mission 边界检测放最前（面板可能开着）
            CheckMissionBoundary();
            if (!IsOpen) return;

            // 🔴 2026-08-17（B'：层归属迁移提升到 Tick 顶层——原只在 HandleCompactInput（缩略分支），
            // 完整模式无此保护：关屏后滚动缓存指向已释放树 → 滚动静默失效。Q5 呼出按钮层迁移复用同一模式）
            MigrateLayerIfNeeded();

            // 🔴 Q4（2026-08-17）：设备切换检测——🔴 2026-08-19（用户裁定）判定统一走 ModInput：
            // 自监测最后输入来源（ModInput.TickInputSource 每帧更新，全 Mod 共用——InteractArea 键帽
            // /IM 提示行/呼出按钮/Mission 冻结同源），裁决 = 最后输入来源晚者胜出 + 按键（离散）>
            // 持续输入（摇杆/鼠标移动）同帧冲突按键胜。本段只做切换检测（缓存 + mask/提示随动）。
            bool usingGamepad = ModInput.UsingGamepad;
            if (usingGamepad != _lastUsingGamepad)
            {
                _lastUsingGamepad = usingGamepad;
                _vm?.RefreshPadHint();
                // 🔴 2026-08-18：设备切换 → 光标可见性随动（手柄 = 隐藏，防原生手柄光标模式锁死正中）
                ApplyInputMask();
                // 🔴 2026-08-19（用户要求：打印最近一次是什么输入让它判成键鼠/手柄——来源详情 + 时间）
                if (PadDbg) DebugLogger.Log($"[ImChat] 设备切换 → {(usingGamepad ? "手柄" : "键盘/鼠标")}（最后手柄输入={ModInput.LastPadActivityDetail}@{ModInput.SecondsSincePadActivity:0.0}s前 最后鼠标输入={ModInput.LastMouseActivityDetail}@{ModInput.SecondsSinceMouseActivity:0.0}s前）");
                // 🔴 2026-08-18（用户要求：切换必须可见，排查「点击后变鼠标感知」等怪象）：
                // 设备判定切换屏显提示（调试豁免裸字符串，PadScreenMsg 先例——测试完按用户反馈决定去留）
                PadScreenMsg($"🎮 设备判定 → {(usingGamepad ? "手柄" : "键鼠")}");
            }
            UpdateGamepadFreeze();
            UpdatePadFocus(dt);

            // 🔴 临时诊断（2026-08-19 mission 鼠标转镜头排查，测完删）：键鼠侧鼠标活动采样——
            // 2s 节流回答三个问题：①鼠标位移引擎是否上报（光标可见时 MouseMoveX 是否归零）
            // ②光标可见性（转镜头的直接机制 = MissionScreen.MouseVisible）③设备判定
            _mouseDiagTimer -= dt;
            if (_mouseDiagTimer <= 0f)
            {
                _mouseDiagTimer = 2f;
                Vec2 mouse = Input.MousePositionPixel;
                try
                {
                    DebugLogger.Log($"[ImChatMouse] gamepad={ModInput.UsingGamepad} move=({Input.MouseMoveX:0.0},{Input.MouseMoveY:0.0}) cursor={ScreenManager.GetMouseVisibility()} mouse=({mouse.X:0.0},{mouse.Y:0.0}) focused={_layer.IsFocusedOnInput()} mask=({_lastCompactMaskVisible},{_lastCompactMask})");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[ImChatMouse] 采样失败: {ex.Message}");
                }
            }

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
                // 🔴 2026-08-18（诊断日志）：B 用抬起沿（IsKeyReleased），单独打日志——与 A 的按下沿区分
                if (PadDbg) DebugLogger.Log($"[Pad] B 抬起(edge) {PadState()}");
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

                // ── ③ 缩略模式输入 mask（🔴 2026-08-17 用户裁定：UI 模式）：
                //    鼠标光标常驻（SetInputRestrictions 第一参 true）→ 光标模式下鼠标移动不转镜头，
                //    鼠标专门操作面板；mask 含 MouseButtons 但 **HitTest 门控限定接收范围**（全屏根
                //    DoNotAcceptEvents=true → 只有面板矩形命中时层才接收：点按钮不挥刀；面板外 HitTest
                //    false → 层不接收 → 左键攻击/右键旋转镜头照常——引擎无左右键独立 mask 位
                //    （InputUsageMask 实锤 MouseButtons=1），右键放行靠 HitTest 门控实现）。
                //    下拉开时补 MouseWheels 位（列表滚动）。键盘不拦（物理轮询拦不住，WASD 正常）。
                // 🔴 2026-08-18：mask 统一走 ApplyInputMask（设备 + 输入态三态模型——手柄导航态隐藏光标
                //   防原生锚定锁中；输入框聚焦放行鼠标位；鼠标玩家照常；内部缓存只在变化时 SetInputRestrictions）
                ApplyInputMask();

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
            // 🔴 Q4（2026-08-18，手动导航）：下拉开关 = 焦点接管/释放（结构变化）→ 重建
            _padNavDirty = true;
            DebugLogger.Log($"[CompactSelect] ToggleChannelList → open={_vm?.ChannelSelector.IsChannelListOpen} widgetVisible={_compactChannelList?.IsVisible}");
        }

        /// <summary>收起频道列表（点选频道后调用——原版下拉选中即收起行为）。
        /// 🔴 Q4（2026-08-18，手动导航）：收起 = 焦点释放回标题行 → 重建。</summary>
        public static void CloseChannelList()
        {
            if (_vm != null) _vm.ChannelSelector.IsChannelListOpen = false;
            _padNavDirty = true;
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

        /// <summary>密令卡片操作（Phase 4 ImCommandFlow 处理）。
        /// 🔴 2026-08-17（用户裁定）：批准执行 → 自动切成缩略模式（玩家该盯屏幕看执行，不被完整
        /// 面板挡住；拒绝/中止保持面板现状——玩家可能继续操作）。</summary>
        public static void HandlePlanAction(ImMessage msg, bool approve, bool abort = false)
        {
            if (msg == null) return;
            try
            {
                if (abort) ImCommandFlow.Abort(msg);
                else ImCommandFlow.Resolve(msg, approve);
                // 卡片状态变更后强制重建消息列表：CanApprove/CanReject/CanAbort 是只读计算属性，
                // 增量追加不会刷新已存在消息的按钮状态（批准后「同意/拒绝」会常驻、「中止」不出现）
                if (_vm != null) _vm.Messages.Clear();
                RefreshMessages();
                // 🔴 2026-08-17（用户裁定）：批准执行 → 自动缩略（看执行）
                if (approve && !abort && _mode == ImChatMode.Full)
                    ToggleCompact();
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
                // 🔴 2026-08-11（Q3）：同意后自动切缩略模式——开打了玩家该盯屏幕，而不是手动收面板
                //（2026-08-17 用户裁定：改为缩略而非关闭——执行状态仍可见）
                if (_mode == ImChatMode.Full) ToggleCompact();
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

        /// <summary>🔴 2026-08-19（澄清轮选项按钮化）：玩家点选澄清轮选项卡按钮 → 卡片了结（按钮消失）→
        /// 选项文本作为玩家回复走 RequestCommand 合并路径（_pendingClarify 并入命令上下文重新生成，
        /// 与玩家手打回复同语义）。区别于 HandleAskPlayerOption（执行期决策卡 → 事件回投执行器）。</summary>
        public static void HandleClarifyOption(ImMessage msg, string optionText)
        {
            if (msg == null || !msg.IsAskPlayerCard || msg.IsAskPlayerCardResolved) return;
            var conv = ConversationOf(msg.ConvId);
            if (conv == null) return;
            msg.ExecutorId = "done";
            // 按钮行是重建式数据（CardButtons 按锚点重建）→ 全量重建（本消息按钮消失，锚点前移）
            if (_vm != null) { _vm.Messages.Clear(); RefreshMessages(); }
            if (string.IsNullOrWhiteSpace(optionText)) return;
            try
            {
                // 选项文本 = 玩家回复（RequestCommand 内澄清合并：并入原命令上下文，≤2 轮上限不变）
                ImCommandFlow.RequestCommand(conv, optionText.Trim());
                DebugLogger.Log($"[ImChat] 澄清轮选择: {msg.SenderName} → {optionText.Trim()}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChat] 澄清轮选择投递失败: {ex.Message}");
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
