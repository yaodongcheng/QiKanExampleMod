using System;
using TaleWorlds.Core;
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
    /// 🔴 Q5（2026-08-17）：IM 常驻呼出按钮（右侧 UI 按钮，三入口等价：键盘 O / 鼠标点击按钮 / 手柄 ↑ 十字）。
    /// 设计文档：plans/im-layer-and-input-design.md §5。
    ///
    /// - 层序 350：&gt; 310（百科/地图玩法 UI，按钮可见可点）、&lt; 400（IM 面板打开时全屏遮罩盖住 + 事件被拦，
    ///   零额外隐藏逻辑）、&lt; 4400（系统菜单/选项界面——满足「不超过选项界面层次」）。
    /// - 🔴 零输入限制（不调 SetInputRestrictions）：按钮层是**常驻层**，任何输入拦截 = 常驻拦输入 =
    ///   pitfalls 灾难（「任何 Gauntlet 层只要含 Mouse 拦截，在战斗场景都是攻击/格挡杀手」，2026-08-11
    ///   实机记录）；按钮点击靠引擎 hit-test 门控（层只在鼠标位于层内 DoNotAcceptEvents=false 的
    ///   widget 矩形时获得鼠标输入），不需要 mask。
    /// - 显示条件：PlotEnabled &amp;&amp; !IM 打开 &amp;&amp; !战斗模式（与 O 键行为一致；战斗/开关关闭时
    ///   按钮隐藏，点了也开不了——Open 内部已查 CanOpen 兜底）。
    /// - 徽标：总未读（ImChatStore.GetTotalUnread，2026-08-17 新增）；0 → 隐藏；&gt;99 → 「99+」；
    ///   新消息到达（MessageArrived 事件）→ 即时刷新 + 3s 正弦脉冲跳动（0.35s/脉冲，上跳 6px + alpha 0.7→1）；
    ///   Tick 0.3s 轮询兜底（打开面板/查看会话 ClearUnread → 数字回落）。
    /// - hover 提示：手动 hit-test + MBInformationManager.ShowHint（项目先例）。
    /// - 点击：ImChatView.Open()——与 O 键完全等价（Open 内部已查 IsOpen/CanOpen/PlotEnabled，
    ///   缩略模式开着时点击 = Open 返回 false 无副作用）。
    /// - 驱动：Campaign = ImChatView.OnScreenFrameTick；Mission = ImChatMissionView.OnMissionTick。
    /// - 层归属迁移：TopScreen 切换 → 摘旧屏挂新屏；旧屏已销毁 → Close（2026-08-17 家族 UI 崩溃修复同款模式）。
    /// - 不设 GamepadNavigationIndex：按钮层不参与手柄导航（防手柄导航在世界 UI 与按钮间混乱；
    ///   手柄玩家用 ↑ 十字呼出，不需要按钮）。
    /// </summary>
    public static class ImChatOpenButtonManager
    {
        private const int LayerOrder = 350;
        private const float BounceDurationSec = 3f;
        private const float BouncePulseSec = 0.35f;
        private const float RefreshIntervalSec = 0.3f;
        private const string ButtonId = "LWN_ImChatOpenButton";
        private const string BadgeId = "LWN_ImChatOpenBadge";
        private const string BadgeTextId = "LWN_ImChatOpenBadgeText";
        // 本地化 key：呼出按钮 hover 提示（LWN_im_open_button_hint，{KEY} 变量）
        private const string HintKey = "LWN_im_open_button_hint";

        private static GauntletLayer _layer;
#if !MB2_V1212
        private static GauntletMovieIdentifier _movie;
#else
        private static IGauntletMovie _movie;
#endif
        private static ScreenBase _layerOwnerScreen;
        private static ButtonWidget _button;
        private static Widget _badge;
        private static TextWidget _badgeText;
        private static Widget _hoverOn;            // 当前 hover 的按钮（防 ShowHint 刷屏）
        private static bool _subscribed;

        private static float _refreshTimer;
        private static int _lastTotalUnread = -1;
        private static float _bounceTimer;
        private static float _bouncePhase;
        /// <summary>🔴 2026-08-17（实机崩溃修复）：widget 属性写入放行标志——挂载/重找 widget 的下一帧才置 true
        ///（LoadMovie 当帧 EventManager 注册未完成，IsVisible setter 触发 RefreshState 链可能踩未就绪容器）。</summary>
        private static bool _widgetsReady;
        /// <summary>诊断：按钮未找到节流计时（1s）。</summary>
        private static float _findDiagTimer;
        /// <summary>诊断：按钮布局打印倒计时（找到后 1s 打一次）。</summary>
        private static float _layoutDiagTimer;

        /// <summary>空 VM（LoadMovie 需要 dataSource 参数；本 prefab 无绑定，纯 C# 驱动）。</summary>
        private sealed class NoopVM : ViewModel { }

        public static void EnsureSubscribed()
        {
            if (_subscribed) return;
            _subscribed = true;
            ImChatManager.MessageArrived += OnMessageArrived;
        }

        /// <summary>每帧驱动（Campaign: ImChatView.OnScreenFrameTick / Mission: ImChatMissionView.OnMissionTick）。
        /// 懒挂载：条件满足才建层；条件不满足只藏按钮（层留着——零输入限制常驻无害）。
        /// 🔴 2026-08-17（实机崩溃修复）：不再「迁移」层——TopScreen 切换 → Close + 下帧自动重挂。
        /// 引擎实锤：ScreenBase.RemoveLayer 必然 HandleFinalize（层死，EventManager._widgetContainers=null），
        /// AddLayer 死层被 FailedAssert 拒绝——「摘旧屏挂新屏复用同一层」在引擎里不成立；
        /// 层随屏销毁就重建（零成本）。</summary>
        public static void Tick(float dt)
        {
            try
            {
                EnsureSubscribed();
                bool visible = ShouldShow();
                // 🔴 层存活检测（2026-08-17 实机修复第二弹）：只看层是否还被 owner 屏持有——
                // ① owner 屏 PopScreen/切换销毁层（HasLayer=false）→ Close + 下帧重挂；
                // ② 家族/队伍屏 Push 叠在 MapScreen 上（层还活着）→ **不动**（按钮被原版屏层序盖住
                //    属正常——350 低于原版屏，符合「不超过选项界面层次」；关屏后按钮自然回来）。
                //    旧逻辑「TopScreen 变了就 Close」会在 Push 时误杀 + Close 摘错屏（从 TopScreen 摘
                //    一个挂在 MapScreen 的层）→ 层 Finalize 却残留在 MapScreen._layers → 下次
                //    PopScreen 激活 MapScreen 时遍历死层 → GauntletLayer.OnActivate NRE 崩溃。
                if (_layer != null && _layerOwnerScreen != null
                    && !_layerOwnerScreen.HasLayer(_layer))
                {
                    DebugLogger.Log("[ImChatOpenButton] 层随屏销毁（HasLayer=false），重新挂载");
                    Close();
                }
                if (visible && _layer == null)
                    Mount();
                if (_layer == null) return;

                if (_button == null)
                {
                    FindWidgets();
                    if (_button == null)
                    {
                        // 🔴 诊断：按钮未找到（节流 1s，防刷屏）——定位「大世界看不到按钮」用
                        _findDiagTimer += dt;
                        if (_findDiagTimer >= 1f)
                        {
                            _findDiagTimer = 0f;
                            DebugLogger.Log($"[ImChatOpenButton] 按钮未找到（Root={_layer?.UIContext?.Root?.Id ?? "null"} ChildCount={_layer?.UIContext?.Root?.ChildCount ?? -1}）");
                        }
                        return;
                    }
                    // 🔴 诊断：找到后延迟 1s 打布局（确认按钮真实渲染位置）
                    _layoutDiagTimer = 1f;
                }
                if (_layoutDiagTimer > 0f)
                {
                    _layoutDiagTimer -= dt;
                    if (_layoutDiagTimer <= 0f)
                    {
                        _layoutDiagTimer = -100f;   // 只打一次
                        try
                        {
                            DebugLogger.Log($"[ImChatOpenButton] 按钮布局: pos=({_button.GlobalPosition.X:0},{_button.GlobalPosition.Y:0}) size=({_button.Size.X:0},{_button.Size.Y:0}) visible={_button.IsVisible} IsHidden={_button.IsHidden}");
                        }
                        catch (Exception ex) { try { DebugLogger.Log($"[ImChatOpenButton] 布局诊断异常: {ex.Message}"); } catch { } }
                    }
                }
                // 🔴 2026-08-17（实机崩溃修复）：widget 属性写入延迟到挂载后下一帧——LoadMovie 当帧
                // EventManager 注册未完成，IsVisible setter 触发 RefreshState 链（ButtonWidget →
                // ImageWidget → RegisterWidgetForEvent）可能踩未就绪的容器。
                if (!_widgetsReady)
                {
                    _widgetsReady = true;   // 挂载后下一帧才放行（首帧只挂引用 + 点击事件）
                    return;
                }
                // 显示条件变化才设置（setter 触发 RefreshState，避免每帧刷）
                if (visible != _button.IsVisible)
                {
                    DebugLogger.Log($"[ImChatOpenButton] 按钮可见性: {visible}（TopScreen={ScreenManager.TopScreen?.GetType().Name} Mission={Mission.Current != null}）");
                    _button.IsVisible = visible;
                }

                UpdateHover();
                UpdateBadge(dt);
                UpdateBounce(dt);
            }
            catch (Exception ex)
            {
                try { DebugLogger.Log($"[ImChatOpenButton] Tick 异常: {ex.Message}"); } catch { }
            }
        }

        /// <summary>显示条件（与 O 键行为一致）：PlotEnabled 总闸 + 非战斗模式。
        /// 🔴 2026-08-17：不含 !ImChatView.IsOpen——IM 打开时按钮被 400 层全屏遮罩盖住 + 事件被拦
        /// （层序红利 350 &lt; 400），零额外隐藏逻辑（方案 §5.1）；去掉该条件同时消灭「IM 打开 →
        /// 隐藏按钮 setter」这个崩溃触发面。</summary>
        private static bool ShouldShow()
        {
            if (!Settings.Instance.PlotEnabled) return false;
            if (Mission.Current != null && Settings.Instance.IsInteractionDisabled()) return false;
            return ScreenManager.TopScreen != null;
        }

        // ───────────────────────── 挂载 / 关闭 ─────────────────────────

        private static void Mount()
        {
            try
            {
                _layer = V.NewLayer(LayerOrder, "ImChatOpenButtonLayer");
                _movie = _layer.LoadMovie("ImChatOpenButton", new NoopVM());
                if (ScreenManager.TopScreen != null)
                {
                    ScreenManager.TopScreen.AddLayer(_layer);
                    _layerOwnerScreen = ScreenManager.TopScreen;
                }
                // 🔴 2026-08-17（实机修复：点击没反应根因）：**必须调 SetInputRestrictions 激活输入接收**——
                // 反编译 ScreenManager.EarlyUpdate 实锤：层要接收鼠标按钮，InputUsageMask 必须含
                // MouseButtons 位（默认 Invalid = 永不分发鼠标输入 → 按钮点击永远到不了层）。
                // SetInputRestrictions(true, MouseButtons)：mask 只声明「接收」，真正的拦截范围由
                // hit-test 门控决定——全屏根 DoNotAcceptEvents=true → 只有按钮矩形 HitTest 命中时层才
                // 获得鼠标（点按钮不挥刀）；鼠标移出按钮 → HitTest false → 游戏层照常接收（攻击/格挡/
                // 视角全正常）。⚠️ 与 pitfalls 2026-08-11 教训的区别：那个灾难是全屏接收层 + 常驻 mask；
                // 本层是「小矩形 hit-test 门控」，等效缩略面板 Q3 位置感知方案。
                // 键盘不拦：EarlyUpdate 的键盘分发走 FocusTest（本层无焦点 widget → 不抢键盘，WASD 正常）。
                // 🔴 放 AddLayer 之后 + try/catch 隔离：任何异常都不影响层挂载（挂载成功优先，输入激活次之）。
                try { _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.MouseButtons); }
                catch (Exception ex) { try { DebugLogger.Log($"[ImChatOpenButton] SetInputRestrictions 失败: {ex.Message}"); } catch { } }
                _widgetsReady = false;   // widget 属性写入延迟到下一帧
                DebugLogger.Log($"[ImChatOpenButton] 挂载（层序 {LayerOrder}，TopScreen={ScreenManager.TopScreen?.GetType().Name}）");
            }
            catch (Exception ex)
            {
                try { DebugLogger.Log($"[ImChatOpenButton] 挂载失败: {ex.Message}"); } catch { }
                Close();
            }
        }

        /// <summary>摘层（Mission Finalize 兜底：ESC 退 Mission 防层泄漏；层随屏销毁 → Close 后下帧重挂）。
        /// 🔴 2026-08-17（实机崩溃修复）：**从 _layerOwnerScreen 摘**（层实际挂载的屏），不用
        /// ScreenManager.TopScreen——层可能挂在非 TopScreen 的屏上（家族/队伍屏 Push 叠层场景），
        /// 从 TopScreen 摘 = 摘错屏 + 层 Finalize 却残留在 owner 屏的 _layers 里 → owner 屏下次
        /// 激活（PopScreen 回地图）遍历死层 → GauntletLayer.OnActivate NRE 崩溃（实机 2026-08-17）。</summary>
        public static void Close()
        {
            if (_hoverOn != null) { MBInformationManager.HideInformations(); _hoverOn = null; }
            if (_layer != null)
            {
                try
                {
                    if (_movie != null)
                    {
                        _layer.ReleaseMovie(_movie);
                        _movie = null;
                    }
                    // 从层实际挂载的屏摘（HasLayer 校验：层可能已随屏销毁，跳过摘除）
                    if (_layerOwnerScreen != null && _layerOwnerScreen.HasLayer(_layer))
                        _layerOwnerScreen.RemoveLayer(_layer);
                    else if (ScreenManager.TopScreen != null && ScreenManager.TopScreen.HasLayer(_layer))
                        ScreenManager.TopScreen.RemoveLayer(_layer);
                }
                catch (Exception ex)
                {
                    try { DebugLogger.Log($"[ImChatOpenButton] Close 失败: {ex.Message}"); } catch { }
                }
                _layer = null;
                _layerOwnerScreen = null;
            }
            _button = null;
            _badge = null;
            _badgeText = null;
            _widgetsReady = false;
        }

        // ───────────────────────── widget 查找 / 交互 ─────────────────────────

        private static void FindWidgets()
        {
            if (_layer?.UIContext?.Root == null) return;
            _button = FindWidgetById(_layer.UIContext.Root, ButtonId) as ButtonWidget;
            _badge = FindWidgetById(_layer.UIContext.Root, BadgeId);
            _badgeText = FindWidgetById(_layer.UIContext.Root, BadgeTextId) as TextWidget;
            if (_button != null)
            {
                _button.ClickEventHandlers.Add(OnButtonClick);
                // 🔴 不设 GamepadNavigationIndex：按钮层不参与手柄导航（防手柄导航在世界 UI 与按钮间混乱；
                // 手柄玩家用 ↑ 十字呼出，不需要按钮）
            }
            // 🔴 2026-08-17：**这里不写任何 widget 属性**——FindWidgets 在挂载当帧执行，
            // 违反 _widgetsReady 门控（属性写入统一由 Tick 主路径在门控放行后执行——
            // 徽标刷新走 UpdateBadge/UpdateBounce，按钮可见性走 Tick 的 visible 同步）。
        }

        private static void OnButtonClick(Widget w)
        {
            try
            {
                DebugLogger.Log("[ImChatOpenButton] 点击呼出按钮");
                bool opened = ImChatView.Open();
                if (!opened)
                    DebugLogger.Log("[ImChatOpenButton] Open 未执行（IsOpen/CanOpen/PlotEnabled 门控）");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ImChatOpenButton] 点击异常: {ex.Message}");
            }
        }

        private static void UpdateHover()
        {
            if (_button == null) return;
            // 手动 hit-test（SecretLetterButtonInjector 同款）：按钮矩形内 → 提示；移出 → 隐藏
            bool over = _button.IsVisible && IsPointInRect(Input.MousePositionPixel, _button.GlobalPosition, _button.Size);
            if (over && _hoverOn != _button)
            {
                if (_hoverOn != null) MBInformationManager.HideInformations();
                MBInformationManager.ShowHint(GetHintText());
                _hoverOn = _button;
            }
            else if (!over && _hoverOn == _button)
            {
                MBInformationManager.HideInformations();
                _hoverOn = null;
            }
        }

        private static string GetHintText()
        {
            // 本地化：呼出按钮 hover 提示（玩家可见文本）；{KEY} = 当前设备呼出键字形（O / ↑）
            string key = ModInput.Glyph(InteractionIds.IM);
            return LWNTextHelper.ResolveCompound(HintKey, "Open messaging (key: {KEY})", ("KEY", key));
        }

        // ───────────────────────── 徽标（总未读 + 脉冲跳动）─────────────────────────

        private static void OnMessageArrived(ImConversation conv)
        {
            try
            {
                // 🔴 2026-08-17（实机崩溃修复）：事件回调**只更新数值**（线程安全 + 不碰 widget）——
                // widget 刷新（徽标数字/可见性/跳动）统一由 Tick 主路径的 UpdateBadge/UpdateBounce 执行
                //（那里有 _widgetsReady 门控 + try/catch；事件回调可能在任何帧边界触发，直接操作
                // widget 属性在层未就绪/已销毁时同样会踩 RegisterWidgetForEvent 崩溃链）。
                int total = ImChatStore.GetTotalUnread();
                bool changed = total != _lastTotalUnread;
                _lastTotalUnread = total;
                if (changed && total > 0)
                {
                    _bounceTimer = BounceDurationSec;
                    _bouncePhase = 0f;
                }
            }
            catch (Exception ex)
            {
                try { DebugLogger.Log($"[ImChatOpenButton] OnMessageArrived 异常: {ex.Message}"); } catch { }
            }
        }

        /// <summary>0.3s 轮询兜底（查看会话 ClearUnread / 面板操作导致未读回落 → 数字更新）。</summary>
        private static void UpdateBadge(float dt)
        {
            _refreshTimer += dt;
            if (_refreshTimer < RefreshIntervalSec && _lastTotalUnread >= 0) return;
            _refreshTimer = 0f;
            int total = ImChatStore.GetTotalUnread();
            if (total == _lastTotalUnread) return;
            _lastTotalUnread = total;
            if (_badge == null || _badgeText == null) return;
            _badge.IsVisible = total > 0;
            _badgeText.Text = total > 99 ? "99+" : total.ToString();
        }

        /// <summary>脉冲跳动动画（C# 定时脉冲，无引擎动画依赖，可控可停）：
        /// 新消息到达后 3s 内每 0.35s 一个「上跳 6px 回落 + Alpha 0.7→1」正弦脉冲；跳完归位。</summary>
        private static void UpdateBounce(float dt)
        {
            if (_badge == null) return;
            if (_bounceTimer > 0f)
            {
                _bounceTimer -= dt;
                _bouncePhase += dt;
                float t = (_bouncePhase % BouncePulseSec) / BouncePulseSec;   // 0..1 每脉冲
                float pulse = MathF.Sin(t * MathF.PI);                          // 0→1→0 单峰
                _badge.PositionYOffset = -6f * pulse;                           // 上跳 6px 回落
                _badge.AlphaFactor = 0.7f + 0.3f * pulse;
                if (_bounceTimer <= 0f)
                {
                    _badge.PositionYOffset = 0f;
                    _badge.AlphaFactor = 1f;
                }
            }
        }

        // ───────────────────────── 工具 ─────────────────────────

        /// <summary>深度优先按 Id 找 widget（ImChatView.FindWidgetById 同款）。</summary>
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

        private static bool IsPointInRect(Vec2 p, Vec2 pos, Vec2 size)
        {
            return p.X >= pos.X && p.X <= pos.X + size.X && p.Y >= pos.Y && p.Y <= pos.Y + size.Y;
        }
    }
}
