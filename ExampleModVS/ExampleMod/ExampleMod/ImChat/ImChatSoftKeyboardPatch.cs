using System;
using HarmonyLib;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;

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
    /// 🔴 Steam Deck（2026-08-22 实装，门控 = SteamDeckKeyboard.IsSteamDeck()）：Deck 有真软键盘，
    /// Done 链是**提交回填**（不能跳过）——ImChatSoftKeyboardContextDonePatch 加 deck 分支：
    /// 自己 SetAllText 回填 + 跳过 CancelMouseClick；取消链（Canceled）仍跳过（焦点保持）。
    /// 配套：SteamDeckKeyboardPatch.cs 补丁 A（点击聚焦弹软键盘）+ EditableTextBackspacePatch.cs
    /// （直通软键盘退格 \b 吞键修复）。
    /// </summary>
    [HarmonyPatch(typeof(GauntletLayer), "OnOnScreenKeyboardCanceled")]
    public static class ImChatSoftKeyboardCancelPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(GauntletLayer __instance)
        {
            DebugLogger.Log($"[KbDiag] Cancel→Layer type={__instance.GetType().Name} isIm={ImChatView.IsCurrentLayer(__instance)}");
            if (ImChatView.IsCurrentLayer(__instance)) return false; // IM 层：跳过取消链（ClearFocus + 模拟鼠标 → 死锁）
            return true;                                             // 其他层照常
        }
    }

    [HarmonyPatch(typeof(GauntletLayer), "OnOnScreenKeyboardDone")]
    public static class ImChatSoftKeyboardDonePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(GauntletLayer __instance, string inputText)
        {
            DebugLogger.Log($"[KbDiag] Done→Layer type={__instance.GetType().Name} isIm={ImChatView.IsCurrentLayer(__instance)} "
                + $"textLen={inputText?.Length}");
            if (ImChatView.IsCurrentLayer(__instance))
            {
#if MB2_GE_130
                if (SteamDeckKeyboard.IsSteamDeck())
                {
                    // 🔴 2026-08-22（Deck 实测日志定位）：GauntletLayer.OnOnScreenKeyboardDone 方法体 =
                    // base.OnOnScreenKeyboardDone（ScreenLayer 空实现，反编译实锤）+
                    // UIContext.OnOnScreenkeyboardTextInputDone（SetAllText 回填在这）——之前整体跳过
                    // 把回填也跳了（日志只有 Done→Layer 没有 Done→Ctx，字丢）。
                    // Deck：放行，回填由 UIContext 层 ImChatSoftKeyboardContextDonePatch 接住。
                    return true;
                }
#endif
                return false; // PC：跳过完成链（输入回填不需要——PC 无软键盘）
            }
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
            DebugLogger.Log($"[KbDiag] Cancel→Ctx isIm={ImChatView.IsCurrentContext(__instance)}");
            if (ImChatView.IsCurrentContext(__instance)) return false; // IM 层上下文：跳过（CancelMouseClick → ClearFocus + 模拟鼠标 → 死锁）
            return true;
        }
    }

    [HarmonyPatch(typeof(UIContext), "OnOnScreenkeyboardTextInputDone")]
    public static class ImChatSoftKeyboardContextDonePatch
    {
        /// <summary>回填验证窗口起点（TickCount）：SetAllText 后 2s 内的 InputText setter 调用打日志（诊断用）。</summary>
        internal static int FillVerifyWindowStart = int.MinValue;

        [HarmonyPrefix]
        public static bool Prefix(UIContext __instance, string inputText)
        {
            DebugLogger.Log($"[KbDiag] Done→Ctx isIm={ImChatView.IsCurrentContext(__instance)} "
                + $"deck={SteamDeckKeyboard.IsSteamDeck()} textLen={inputText?.Length} "
                + $"focused={__instance.EventManager.FocusedWidget?.GetType()?.Name}");
            if (!ImChatView.IsCurrentContext(__instance)) return true;   // 非 IM 层：原版
#if MB2_GE_130
            if (SteamDeckKeyboard.IsSteamDeck())
            {
                // 🔴 2026-08-22（Steam Deck 实测：IM 输入框弹窗提交后文字丢失——功能阻断）：
                // Deck 有真软键盘，提交必须回填文本（SetAllText → 绑定推送 VM InputText）；
                // 跳过 CancelMouseClick（ClearFocus + 模拟鼠标抬起 → 设备翻转坑，见类头注释）。
                if (inputText != null && __instance.EventManager.FocusedWidget is EditableTextWidget editableTextWidget)
                {
                    editableTextWidget.SetAllText(inputText);
                    FillVerifyWindowStart = Environment.TickCount;   // 开验证窗口：2s 内 setter 调用打日志
                    DebugLogger.Log($"[KbDiag] IM 回填执行后 widget.Text=\"{editableTextWidget.Text}\"（目标 {inputText.Length} 字符）");
                }
                else
                {
                    DebugLogger.Log($"[KbDiag] ⛔ IM 回填失败：inputText null 或焦点不是 EditableTextWidget（焦点丢失？）");
                }
                return false;
            }
#endif
            return false;   // PC：跳过全部（原语义，PC 无真软键盘——该链是取消回调，跳过防死锁）
        }
    }

    /// <summary>🔴 2026-08-22（诊断用）：监听 IM 输入框 VM 绑定推送——SetAllText → widget.Text →
    /// 绑定 → ImChatVM.InputText setter（发送逻辑读的就是它）。只在回填后 2s 窗口内打日志，
    /// 平时打字不刷屏。定位「回填执行了但输入框没字」是绑定断链还是值没到位。</summary>
    [HarmonyPatch(typeof(ImChatVM), "set_InputText")]
    public static class ImChatFillVerifyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string value)
        {
            // 哨兵值跳过判定 + (uint) 差值：初始 int.MinValue 相减溢出为负会恒 < 2000（窗口常开刷屏）
            if (ImChatSoftKeyboardContextDonePatch.FillVerifyWindowStart != int.MinValue
                && (uint)(Environment.TickCount - ImChatSoftKeyboardContextDonePatch.FillVerifyWindowStart) < 2000)
            {
                DebugLogger.Log($"[KbDiag] InputText setter 收到值 \"{value}\"（回填后 {Environment.TickCount - ImChatSoftKeyboardContextDonePatch.FillVerifyWindowStart}ms）");
            }
        }
    }
}
