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

        /// <summary>是否运行在 Steam Deck（Steamworks 官方 API，反射调用避免对 Steamworks.NET 的硬依赖）。</summary>
        internal static bool IsSteamDeck()
        {
            if (_cached) return _isSteamDeck;
            try
            {
                Type t = Type.GetType("Steamworks.SteamUtils, Steamworks.NET", false);
                MethodInfo m = t?.GetMethod("IsSteamRunningOnSteamDeck", BindingFlags.Public | BindingFlags.Static);
                _isSteamDeck = m != null && (bool)m.Invoke(null, null);
                DebugLogger.Log($"[SteamDeckKb] Steam Deck 检测: {_isSteamDeck}");
            }
            catch (Exception ex)
            {
                _isSteamDeck = false;
                DebugLogger.Log($"[SteamDeckKb] Steam Deck 检测降级 false: {ex.Message}");
            }
            _cached = true;
            return _isSteamDeck;
        }
    }

    /// <summary>补丁 A：Deck 上任何 EditableTextWidget 获得焦点（非手柄路径）→ 请求引擎软键盘弹窗。
    /// 点击聚焦唯一入口 = GauntletEvent.MousePressed → FocusedWidget setter（反编译实锤），MCM/vanilla/IM 全覆盖。
    /// 引擎消费块原参（EventManager.Update 反编译）：Text/KeyboardInfoText/MaxLength/
    /// IsObfuscationEnabled→type 2（密码）、IntegerInputTextWidget/FloatInputTextWidget→type 1。</summary>
    [HarmonyPatch(typeof(EventManager), "set_FocusedWidget")]
    public static class SteamDeckEditableKeyboardPatch
    {
        [HarmonyPostfix]
        public static void Postfix(EventManager __instance, Widget value)
        {
            if (!SteamDeckKeyboard.IsSteamDeck()) return;
            if (!(value is EditableTextWidget)) return;
            if (__instance.FocusedWidget != value) return;          // setter 拒绝（不可聚焦）→ 焦点未生效
            if (__instance.IsControllerActive) return;              // 手柄路径：引擎已置请求标志，自己会弹（防双发）
            if (V.IsOnScreenKeyboardActive()) return;               // 软键盘已开
            try
            {
                var ew = (EditableTextWidget)value;
                string initialText = ew.Text ?? string.Empty;
                string descriptionText = ew.KeyboardInfoText ?? string.Empty;
                int maxLength = ew.MaxLength;
                int keyboardTypeEnum = ew.IsObfuscationEnabled ? 2 : 0;
                if (value is IntegerInputTextWidget || value is FloatInputTextWidget) keyboardTypeEnum = 1;
                __instance.Context.TwoDimensionContext.Platform.OpenOnScreenKeyboard(initialText, descriptionText, maxLength, keyboardTypeEnum);
                DebugLogger.Log($"[SteamDeckKb] 点击聚焦 EditableTextWidget → 请求软键盘 (type={keyboardTypeEnum})");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SteamDeckKb] 请求软键盘失败: {ex.Message}");
            }
        }
    }
#endif
}
