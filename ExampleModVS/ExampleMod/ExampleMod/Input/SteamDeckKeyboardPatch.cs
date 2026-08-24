using HarmonyLib;
using System;
using System.Reflection;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-22（Steam Deck 软键盘弹窗——用户实测）：
    /// Deck 上 MCM/原版文本框鼠标/触屏点击不弹 Steam 浮动键盘（上部带文本框 + 提交），
    /// 软键盘直通模式盲打。根因（反编译实锤）：引擎只在 EventManager.FocusedWidget setter 里
    /// IsControllerActive &amp;&amp; EditableTextWidget 时置 _isOnScreenKeyboardRequested → LateUpdate 消费 →
    /// Platform.OpenOnScreenKeyboard → ScreenManager.OnPlatformScreenKeyboardRequested →
    /// PlatformServices.ShowGamepadTextInput → SteamUtils.ShowGamepadTextInput（Steam 浮动键盘弹窗）。
    /// IsControllerActive = Input.IsGamepadActive = IsControllerConnected &amp;&amp; !IsMouseActive →
    /// 鼠标/触屏点击那帧必然 false → 不请求 → 直通模式。IM 输入框手柄 A 聚焦（FocusInputWidget →
    /// V.SetFocusedWidget）→ 控制器激活 → 引擎请求 → 弹窗 ✓。
    ///
    /// 本文件两件套：① SteamDeckKeyboard.IsSteamDeck()（Steamworks 官方 API 反射调用，无 csproj 硬引用——
    /// Epic/GOG 无 Steamworks.NET 时 Type.GetType 返回 null 安全降级）② 补丁 A：set_FocusedWidget postfix——
    /// Deck 上非手柄路径聚焦 EditableTextWidget → 直接请求引擎软键盘弹窗（参数复制引擎消费块反编译原文）。
    ///
    /// ⚠️ 门控必须 = IsSteamDeck()：PC 上 OpenOnScreenKeyboard 立即走取消回调链
    ///（OnOnscreenKeyboardCanceled → CancelMouseClick → ClearFocus + 模拟鼠标抬起 → 点中文本框即失焦，
    /// 2026-08-19 实机死锁根因，见 ImChatSoftKeyboardPatch.cs 头注释）——无条件触发会破坏 PC 文本框。
    /// </summary>
#if MB2_GE_130   // 1.2.12 无软键盘机制（InputSystem 无 OnScreenKeyboard API，二进制 grep 实锤）→ 整个补丁不编译
    internal static class SteamDeckKeyboard
    {
        private static bool _cached;
        private static bool _isSteamDeck;
        /// <summary>检测失败冷却起点（TickCount）：Steamworks 未初始化时 IsSteamRunningOnSteamDeck 抛异常，
        /// 冷却 3s 后重试（启动早期 SteamAPI.Init 竞态，16:28 实测：检测 False → 弹窗全灭）。</summary>
        private static int _retryTick = int.MinValue;

        /// <summary>是否运行在 Steam Deck（Steamworks 官方 API，反射调用避免对 Steamworks.NET 的硬依赖）。
        /// 🔴 失败不缓存 + 冷却重试：SteamAPI 初始化完成前调用会抛异常（TestIfAvailableClient），
        /// 若吞掉缓存 false，整个会话弹窗永久失效（2026-08-22 实机：启动早期首次聚焦触发检测撞竞态）。</summary>
        internal static bool IsSteamDeck()
        {
            if (_cached) return _isSteamDeck;
            // 失败冷却：3s 内不重试（不刷日志），冷却后重试
            if (_retryTick != int.MinValue && (uint)(Environment.TickCount - _retryTick) < 3000) return false;
            try
            {
                Type t = Type.GetType("Steamworks.SteamUtils, Steamworks.NET", false);
                MethodInfo m = t?.GetMethod("IsSteamRunningOnSteamDeck", BindingFlags.Public | BindingFlags.Static);
                _isSteamDeck = m != null && (bool)m.Invoke(null, null);
                _cached = true;   // 只有成功（含正常返回 false——非 Deck 设备）才缓存
                DebugLogger.Log($"[SteamDeckKb] Steam Deck 检测: {_isSteamDeck}");
            }
            catch (Exception ex)
            {
                _isSteamDeck = false;
                _retryTick = Environment.TickCount;   // 失败：冷却后重试（SteamAPI 可能尚未初始化）
                DebugLogger.Log($"[SteamDeckKb] Steam Deck 检测失败（SteamAPI 未就绪？）3s 后重试: {ex.Message}");
            }
            return _isSteamDeck;
        }
    }

    /// <summary>补丁 A：Deck 上任何 EditableTextWidget 获得焦点 → 请求引擎软键盘弹窗。
    /// 点击聚焦唯一入口 = GauntletEvent.MousePressed → FocusedWidget setter（反编译实锤），MCM/vanilla/IM 全覆盖。
    /// 引擎消费块原参（EventManager.Update 反编译）：Text/KeyboardInfoText/MaxLength/
    /// IsObfuscationEnabled→type 2（密码）、IntegerInputTextWidget/FloatInputTextWidget→type 1。
    /// 🔴 2026-08-22（16:06 Deck 日志实锤）：Steam Deck 虚拟手柄常驻 → IsGamepadActive 恒 true →
    /// controller 恒 true → 原「controller 守卫跳过」让补丁 A 在 Deck 上永远失效（弹窗只剩引擎链）。
    /// 修：Deck 上无视 controller 守卫无条件请求（引擎链也会请求——Steam 对重复请求 no-op，无害；
    /// kbActive 防已弹窗时重复请求）。PC 门控 deck=false 不受影响。</summary>
    [HarmonyPatch(typeof(EventManager), "set_FocusedWidget")]
    public static class SteamDeckEditableKeyboardPatch
    {
        [HarmonyPostfix]
        public static void Postfix(EventManager __instance, Widget value)
        {
            // 只有 EditableTextWidget 聚焦才打（非输入框点击=按钮/列表项，不打防刷屏）
            if (!(value is EditableTextWidget)) return;
            bool deck = SteamDeckKeyboard.IsSteamDeck();
            bool focused = __instance.FocusedWidget == value;
            bool controller = false;
            try { controller = __instance.IsControllerActive; } catch { }
            bool kbActive = false;
            try { kbActive = V.IsOnScreenKeyboardActive(); } catch { }
            // 🔴 诊断（2026-08-22 链路日志，2026-08-23 恢复）：EditableTextWidget 获得焦点就无条件打一行——
            // 链上每个守卫值都可见：deck=False（Deck 检测失败/Steamworks 未就绪）/ focused=False（setter
            // 被拒——含引擎早退分支：_isOnScreenKeyboardRequested 残留或 IsOnScreenKeyboardActive 卡 true）/
            // kbActive=True（引擎链已弹窗或状态卡死）→ 一眼定位弹窗被哪个守卫掐断。
            if (Settings.Instance.KbDiagEnabled)
            {
                int maxLen = 0;
                try { maxLen = ((EditableTextWidget)value).MaxLength; } catch { }
                DebugLogger.Log($"[KbDiag] 输入框聚焦 deck={deck} focused={focused} controller={controller} "
                    + $"kbActive={kbActive} widget={value.GetType().Name} maxLength={maxLen}");
            }
            if (!deck) return;                                          // PC/Epic/GOG：零行为变化
            if (!focused) return;                                       // setter 拒绝（不可聚焦）→ 焦点未生效
            if (kbActive) return;                                       // 软键盘已开（引擎链已请求过）
            try
            {
                var ew = (EditableTextWidget)value;
                string initialText = ew.Text ?? string.Empty;
                string descriptionText = ew.KeyboardInfoText ?? string.Empty;
                int maxLength = ew.MaxLength;
                int keyboardTypeEnum = ew.IsObfuscationEnabled ? 2 : 0;
                if (value is IntegerInputTextWidget || value is FloatInputTextWidget) keyboardTypeEnum = 1;

                // 🔴 2026-08-24（Steamworks 直连优先——桌面模式强制呼出尝试）：引擎桥
                //（OpenOnScreenKeyboard → PlatformServices.Instance.ShowGamepadTextInput）在
                // Steam 桌面模式返回 false 时，无法区分「Steam 客户端拒绝」vs「桥本身坏」
                //（PlatformServices.Instance 为 null / 非 Steam 实现——IsSteamDeck 检测是
                // Steamworks.NET 直连，不经过桥，桥坏不坏检测不出来）。直连 Steamworks API
                //（反射，同 IsSteamDeck 模式，无 csproj 硬依赖）绕开桥——返回值即 Steam 亲口
                // 回答：true = 键盘已弹（桥坏假说成立 → 桌面模式问题直接解决）；
                // false = Steam 客户端拒绝（桌面模式无键盘服务 → 实锤无解，非代码问题）。
                bool directOk = false;
                try
                {
                    Type t = Type.GetType("Steamworks.SteamUtils, Steamworks.NET", false);
                    MethodInfo m = t?.GetMethod("ShowGamepadTextInput", BindingFlags.Public | BindingFlags.Static);
                    if (m != null)
                    {
                        Type modeType = t.Assembly.GetType("Steamworks.EGamepadTextInputMode");
                        Type lineModeType = t.Assembly.GetType("Steamworks.EGamepadTextInputLineMode");
                        object mode = Enum.ToObject(modeType, keyboardTypeEnum == 2 ? 1 : 0);   // Normal=0 / Password=1（Steam 实现同映射）
                        object line = Enum.ToObject(lineModeType, 0);                            // SingleLine=0
                        // maxChars 至少 1：unCharMax=0 语义不明（IM 输入框 MaxLength 默认 0 = 未设限），
                        // 日志打出原始 maxLength 供对照
                        directOk = (bool)m.Invoke(null, new object[] { mode, line, descriptionText, (uint)Math.Max(1, maxLength), initialText });
                        DebugLogger.Log($"[SteamDeckKb] Steamworks 直连 ShowGamepadTextInput → {directOk} (type={keyboardTypeEnum} maxLength={maxLength})");
                    }
                    else
                    {
                        DebugLogger.Log("[SteamDeckKb] Steamworks 直连反射失败（无 ShowGamepadTextInput）——走引擎桥兜底");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[SteamDeckKb] Steamworks 直连异常: {ex.Message}——走引擎桥兜底");
                }

                if (!directOk)
                {
                    // 直连失败/反射不可用 → 引擎桥兜底（平台抽象路径，原逻辑；重复请求 Steam 对已弹键盘 no-op）
                    __instance.Context.TwoDimensionContext.Platform.OpenOnScreenKeyboard(initialText, descriptionText, maxLength, keyboardTypeEnum);
                    DebugLogger.Log($"[SteamDeckKb] 引擎桥请求软键盘 (type={keyboardTypeEnum})");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SteamDeckKb] 请求软键盘失败: {ex.Message}");
            }
        }
    }
#endif
}
