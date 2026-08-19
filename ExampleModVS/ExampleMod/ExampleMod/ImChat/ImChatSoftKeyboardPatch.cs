using HarmonyLib;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-19（设备翻转死锁根治——软键盘取消回调链）：
    /// IM 层跳过软键盘取消/完成回调。根因链（反编译实锤）：
    /// A 键聚焦 EditableTextWidget → EventManager setter 置 _isOnScreenKeyboardRequested →
    /// LateUpdate 消费调 Platform.OpenOnScreenKeyboard → PC 无软键盘 → native 立即回调
    /// OnOnScreenKeyboardCanceled（可能走 GauntletLayer 也可能直接调 UIContext）→
    /// CancelMouseClick() → ① ClearFocus 清掉焦点 ② 模拟鼠标抬起 → IsMouseActive 持续 true
    /// → 设备判定翻转提交 → 门控死锁（实机 09:48/09:52/09:59 三证：聚焦成功 0.5s 后翻转提交）。
    /// ⚠️ 版本教训：① 补丁 ITwoDimensionPlatform.OpenOnScreenKeyboard（抽象接口方法）→ Harmony
    /// PatchAll 直接崩游戏启动（实机）；② 只补丁 GauntletLayer 回调 → 没拦住（native 可能直接调
    /// UIContext）→ 聚焦仍被清（实机 09:59）。**两层回调都补丁**：GauntletLayer + UIContext。
    /// 跳过 = 不清焦点、不模拟鼠标事件。实体键盘输入不受影响。
    /// ⚠️ SteamDeck 有真软键盘，IM 内手柄弹软键盘会失效——当前用户是 PC（PC 上此路径本来就坏），
    /// SteamDeck 支持需平台判定，后续按需加。
    /// </summary>
    [HarmonyPatch(typeof(GauntletLayer), "OnOnScreenKeyboardCanceled")]
    public static class ImChatSoftKeyboardCancelPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(GauntletLayer __instance)
        {
            if (ImChatView.IsCurrentLayer(__instance)) return false; // IM 层：跳过取消链（ClearFocus + 模拟鼠标 → 死锁）
            return true;                                             // 其他层照常
        }
    }

    [HarmonyPatch(typeof(GauntletLayer), "OnOnScreenKeyboardDone")]
    public static class ImChatSoftKeyboardDonePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(GauntletLayer __instance)
        {
            if (ImChatView.IsCurrentLayer(__instance)) return false; // IM 层：跳过完成链（输入回填不需要——PC 无软键盘）
            return true;
        }
    }

    // 🔴 2026-08-19：native 可能直接调 UIContext 的取消/完成回调（不走层）——层补丁拦不住
    //（实机 09:59：聚焦仍被清）。两层都补丁，门控 = IM 层的 UIContext 实例。
    [HarmonyPatch(typeof(UIContext), "OnOnScreenKeyboardCanceled")]
    public static class ImChatSoftKeyboardContextCancelPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(UIContext __instance)
        {
            if (ImChatView.IsCurrentContext(__instance)) return false; // IM 层上下文：跳过（CancelMouseClick → ClearFocus + 模拟鼠标 → 死锁）
            return true;
        }
    }

    [HarmonyPatch(typeof(UIContext), "OnOnScreenkeyboardTextInputDone")]
    public static class ImChatSoftKeyboardContextDonePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(UIContext __instance)
        {
            if (ImChatView.IsCurrentContext(__instance)) return false; // IM 层上下文：跳过完成链
            return true;
        }
    }
}
