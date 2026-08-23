using System;
using HarmonyLib;
using TaleWorlds.InputSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-23（用户裁定：ESC/B 统一——IM 打开时 ESC 只关面板，不许再开选项菜单）：
    /// 拦截 vanilla 的 ToggleEscapeMenu HotKey 释放沿。
    /// 机制（反编译实锤，三版本 1.2.12 / 1.3.15 / 1.4.6 签名一致，无需版本分支）：
    /// - ToggleEscapeMenu HotKey（GenericPanelGameKeyCategory）= Escape + ControllerROption（Menu 键），
    ///   是**所有**原版「ESC 选项菜单」入口的公共闸门：
    ///   · Mission：MissionScreen.HandleInputs（TaleWorlds.MountAndBlade.View.dll 16591 行）
    ///     `SceneLayer.Input.IsHotKeyReleased("ToggleEscapeMenu")` → OnEscape() →
    ///     MissionGauntletEscapeMenuBase.OnEscape 开 MissionEscapeMenu 层（层序 50）；
    ///   · Campaign 地图：MapScreen.TickNavigationInput（SandBox.View.dll 15464 行）同款 → OpenEscapeMenu；
    ///   · 对话（MissionGauntletConversationView）/教育/捏脸同款检查。
    /// - 层 mask 拦不住：这是 InputContext 层轮询（InputContext.IsHotKeyReleased → HotKey.IsReleased），
    ///   不受 IM 层 InputRestrictions 键盘 mask 过滤（与 GameKey.IsDown 同结论，ImChatMissionInputPatch 已记）。
    /// - 与 B 的差异（用户问题「ESC 触发选项、B 不触发」的答案）：B（ControllerRRight）已由
    ///   ImChatMissionInputPatch ②「B 全分类吞」堵死且不属于本 hotkey；ESC 漏 = 本 hotkey 没堵。
    /// 处置：Prefix 拦 HotKey.IsReleased——Id=="ToggleEscapeMenu" 且（IM 打开 || ESC 刚被 IM 消费
    ///（同帧吞窗 ImChatView.EscapeCloseHoldActive——本类 Tick 关面板与原版检查同帧先后执行，只靠
    /// IsOpen 会漏：实机「ESC 关面板的同时选项菜单弹出」））→ false。零挂接，IM 关 → 自然放行
    ///（面板关闭后的下一按 ESC 正常开选项菜单）。
    /// </summary>
    [HarmonyPatch(typeof(HotKey), "IsReleased", new Type[] { typeof(bool), typeof(bool), typeof(bool), typeof(bool) })]
    public static class ImChatEscapeMenuInputPatch
    {
        private const string ToggleEscapeMenuId = "ToggleEscapeMenu";

        [HarmonyPrefix]
        public static bool Prefix(HotKey __instance, ref bool __result)
        {
            if (__instance.Id == ToggleEscapeMenuId
                && (ImChatView.IsOpen || ImChatView.EscapeCloseHoldActive))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
