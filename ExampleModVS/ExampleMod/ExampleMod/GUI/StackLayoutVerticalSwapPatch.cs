using System.Collections.Generic;
#if MB2_GE_130
using HarmonyLib;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.GauntletUI.Layout;

namespace LivingWorldNpcs
{
    /// <summary>
    /// v1.3.0+ 修复了 v1.2.12 StackLayout.LayoutLinearVertical 的 VerticalBottomToTop/VerticalTopToBottom 互换 bug。
    /// 为保持双版本 XML 统一，在 v1.3.0+ 上对自定义 UI 的 ListPanel 做反向 swap。
    ///
    /// 标识方式：遍历 widget 的 ParentWidget 链，检查是否有任一节点的类型名包含 "LWN"（或检查特定的 Widget 类型）。
    /// </summary>
    /// 
    
    [HarmonyPatch(typeof(StackLayout), "OnLayout")]
    public static class StackLayoutVerticalSwapPatch
    {
        /// <summary>
        /// 判断当前 StackLayout 所属 widget 是否为自定义 UI。
        /// 通过检查 widget 及其祖先的 Id 是否包含 "LWN" 前缀来识别。
        /// 如果 XML 的 Id 确实不传递到运行时 Widget，则需要改用其他方式（如在 Prefab 加载后 C# 注册 widget 引用）。
        /// </summary>
        private static bool IsCustomUI(Widget widget)
        {
            // 遍历父链，检查是否有 widget 的 Id 包含 "LWN"
            var w = widget;
            while (w != null)
            {
                if (!string.IsNullOrEmpty(w.Id) && w.Id.StartsWith("LWN"))
                    return true;
                w = w.ParentWidget;
            }
            return false;
        }

        /// <summary>仅对自定义 UI 的 StackLayout 交换 VerticalBottomToTop ↔ VerticalTopToBottom</summary>
        static void Prefix(StackLayout __instance, Widget widget, out LayoutMethod __state)
        {
            __state = __instance.LayoutMethod;
            if (!IsCustomUI(widget))
                return;

            if (__instance.LayoutMethod == LayoutMethod.VerticalBottomToTop)
                __instance.LayoutMethod = LayoutMethod.VerticalTopToBottom;
            else if (__instance.LayoutMethod == LayoutMethod.VerticalTopToBottom)
                __instance.LayoutMethod = LayoutMethod.VerticalBottomToTop;
        }

        /// <summary>恢复原始 LayoutMethod</summary>
        static void Postfix(StackLayout __instance, LayoutMethod __state)
        {
            __instance.LayoutMethod = __state;
        }
    }
    
}
#endif
