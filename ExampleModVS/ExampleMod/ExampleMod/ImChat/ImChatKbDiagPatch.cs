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
#endif
}
