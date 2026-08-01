#if MB2_GE_130
using HarmonyLib;
using TaleWorlds.GauntletUI.Layout;

namespace LivingWorldNpcs
{
    /// <summary>
    /// v1.2.12 的 StackLayout.LayoutLinearVertical 中存在 bug：
    /// VerticalBottomToTop 和 VerticalTopToBottom 的实现互换了。
    /// v1.3.0+ 修复了该 bug，但导致所有使用 VerticalBottomToTop 的 XML 布局视觉顺序反转。
    ///
    /// 此补丁在 OnLayout 入口交换这两个枚举值，使 v1.3.0+ 的行为与 v1.2.12 保持一致，
    /// 避免需要同时修改两套 XML。
    /// </summary>
    [HarmonyPatch(typeof(StackLayout), "OnLayout")]
    public static class StackLayoutVerticalSwapPatch
    {
        /// <summary>保存原始 LayoutMethod，交换 VerticalBottomToTop ↔ VerticalTopToBottom</summary>
        static void Prefix(StackLayout __instance, out LayoutMethod __state)
        {
            __state = __instance.LayoutMethod;
            if (__instance.LayoutMethod == LayoutMethod.VerticalBottomToTop)
                __instance.LayoutMethod = LayoutMethod.VerticalTopToBottom;
            else if (__instance.LayoutMethod == LayoutMethod.VerticalTopToBottom)
                __instance.LayoutMethod = LayoutMethod.VerticalBottomToTop;
        }

        /// <summary>恢复原始 LayoutMethod，避免影响后续布局计算</summary>
        static void Postfix(StackLayout __instance, LayoutMethod __state)
        {
            __instance.LayoutMethod = __state;
        }
    }
}
#endif
