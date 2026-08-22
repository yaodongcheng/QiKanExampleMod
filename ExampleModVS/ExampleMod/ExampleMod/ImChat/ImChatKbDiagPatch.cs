using HarmonyLib;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-22（Steam Deck IM 弹窗提交回填失败——临时诊断补丁）：
    /// Deck 实测 MCM 提交回填成功、IM 提交无反应。Done 链入口 = ScreenManager.OnOnscreenKeyboardDone
    ///（静态，Steam 提交 → OnTextEnteredFromPlatform → 这里），它用 ScreenManager.FocusedLayer 分发——
    /// 如果 IM 层不是 FocusedLayer，IM 的回填补丁全程旁观，文字必然丢失。
    /// 本补丁在链入口打日志：FocusedLayer 是谁 + 提交文本长度，一眼定位断点。
    /// ⚠️ 定位完成后删除（诊断补丁不交付）；#if MB2_GE_130：1.2.12 无软键盘机制，无需诊断。
    /// </summary>
#if MB2_GE_130
    [HarmonyPatch(typeof(ScreenManager), "OnOnscreenKeyboardDone")]
    public static class ImChatKbDiagDonePatch
    {
        [HarmonyPrefix]
        public static void Prefix(string inputText)
        {
            DebugLogger.Log($"[KbDiag] ScreenManager.Done 到达 focusedLayer={ScreenManager.FocusedLayer?.GetType()?.Name} "
                + $"textLen={inputText?.Length} deck={SteamDeckKeyboard.IsSteamDeck()}");
        }
    }

    [HarmonyPatch(typeof(ScreenManager), "OnOnscreenKeyboardCanceled")]
    public static class ImChatKbDiagCancelPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            DebugLogger.Log($"[KbDiag] ScreenManager.Cancel 到达 focusedLayer={ScreenManager.FocusedLayer?.GetType()?.Name} "
                + $"deck={SteamDeckKeyboard.IsSteamDeck()}");
        }
    }

    /// <summary>平台键盘请求结果：ScreenManager.OnPlatformScreenKeyboardRequested 返回值 =
    /// PlatformServices.ShowGamepadTextInput 的结果（true = Steam 接受，键盘该弹；false = 请求被拒）。
    /// 同时确认委托链（MountAndBlade OnPlatformTextRequested）是否注册。
    /// ⚠️ 引擎链落点 TwoDimensionEnginePlatform.OpenOnScreenKeyboard 是 ITwoDimensionPlatform
    /// 显式接口实现（IL 方法名带接口前缀）——Harmony 字符串补丁找不到，PatchAll 抛异常崩启动
    ///（2026-08-22 实机）。OnPlatformScreenKeyboardRequested 只在引擎链请求到达后才会被调，
    /// 本补丁的到达日志即引擎链落点证明，无需再补丁引擎类。</summary>
    [HarmonyPatch(typeof(ScreenManager), "OnPlatformScreenKeyboardRequested")]
    public static class ImChatKbDiagPlatformRequestPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string initialText, int maxLength, int keyboardTypeEnum)
        {
            DebugLogger.Log($"[KbDiag] 平台键盘请求 initialLen={initialText?.Length} maxLen={maxLength} type={keyboardTypeEnum}");
        }

        [HarmonyPostfix]
        public static void Postfix(bool __result)
        {
            DebugLogger.Log($"[KbDiag] 平台键盘请求结果 → {__result}（true=Steam 接受；false=请求被拒/委托未注册）");
        }
    }
#endif
}
