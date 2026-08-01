#if MB2_GE_130
using HarmonyLib;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.GauntletUI.Layout;

namespace LivingWorldNpcs
{
    /// <summary>
    /// v1.2.12 的 StackLayout.LayoutLinearVertical 中存在 bug：
    /// VerticalBottomToTop 和 VerticalTopToBottom 的实现互换了。
    /// v1.3.0+ 修复了该 bug，但导致自定义 UI 的 XML 布局视觉顺序反转。
    ///
    /// 此补丁仅对根节点带有 Id="LWN" 的自定义 UI 生效（往上查 ParentWidget 链），
    /// 不影响官方界面的布局。
    /// </summary>
    [HarmonyPatch(typeof(StackLayout), "OnLayout")]
    public static class StackLayoutVerticalSwapPatch
    {
        /// <summary>沿 ParentWidget 链向上查找，检查是否为自定义 UI（根节点 Id == "LWN"）</summary>
        private static bool IsCustomUI(Widget widget)
        {
            while (widget != null)
            {
                if (widget.Id == "LWN")
                    return true;
                widget = widget.ParentWidget;
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
