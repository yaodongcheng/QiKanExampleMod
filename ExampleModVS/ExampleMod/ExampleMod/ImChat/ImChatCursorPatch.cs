using System.Reflection;
using HarmonyLib;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-18（实机：手柄 IM 导航态光标被原生锚定锁死屏幕中央 + alt+tab 失焦仍锁鼠标）：
    /// 全局光标强制隐藏补丁——绕过 ScreenManager.UpdateMouseVisibility 的聚合规则。
    /// 根因链（反编译实锤）：UpdateMouseVisibility 的规则是「任一活跃层 InputRestrictions.MouseVisibility=true
    /// → 全局光标显示」——vanilla MapScreen 层恒 true，IM 层 SetInputRestrictions(false) 藏不住光标；
    /// 「手柄在用 + 可见光标」→ native 锚定模式（光标 = 屏幕中心 + 摇杆向量，每帧 set_cursor_position 覆盖，
    /// 失焦不停）→ 中央准星 + alt+tab 出去鼠标仍被锁死。
    /// 处置：IM 打开 + 手柄 + 非输入框聚焦（导航态）→ Prefix 强制 SetMouseVisible(false) 并跳过原聚合；
    /// 输入框聚焦态（原生速度模式，需要光标点击）与鼠标态放行原逻辑。补丁目标已二进制 grep 验证存在
    /// （UpdateMouseVisibility / SetMouseVisible 均在 TaleWorlds.ScreenSystem.dll）。
    /// </summary>
    [HarmonyPatch(typeof(ScreenManager), "UpdateMouseVisibility")]
    public static class ImChatCursorHidePatch
    {
        private static readonly MethodInfo SetMouseVisibleMethod =
            AccessTools.Method(typeof(ScreenManager), "SetMouseVisible");

        [HarmonyPrefix]
        public static bool Prefix()
        {
            if (!ImChatView.ShouldForceHideCursor())
                return true; // 放行原聚合逻辑（鼠标态 / 输入框聚焦态 / IM 未开）

            // 导航态：强制隐藏全局光标，打破 native 锚定模式（SetInputRestrictions(false) 被
            // MapScreen 层的 MouseVisibility=true 覆盖，聚合层救不了，只能在聚合源头拦截）
            try
            {
                SetMouseVisibleMethod?.Invoke(null, new object[] { false });
            }
            catch (System.Exception)
            {
                // 隐藏失败兜底：静默（下一帧重试；失败也不影响导航本身）
            }
            return false;
        }
    }
}
