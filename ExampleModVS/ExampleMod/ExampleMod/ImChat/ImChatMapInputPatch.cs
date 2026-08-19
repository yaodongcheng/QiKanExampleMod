using HarmonyLib;
using TaleWorlds.InputSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-18（用户裁定：完整 + 缩略 IM 打开期间，十字键/方向键控制的部队移动必须禁）：
    /// 拦截 vanilla 地图部队移动（PartyMove*）。
    /// 机制（反编译实锤）：
    /// - PartyMoveUp/Down/Left/Right = MapHotKeyCategory GameKey 50-53，默认绑键盘 Up/Down/Left/Right
    /// - MapScreen.IMapStateHandler.BeforeTick（SandBox.View.dll 14433 行起）里
    ///   `SceneLayer.Input.IsGameKeyDown(50-53)` 收集
    /// - MapCameraView.OnBeforeTick（SandBox.View.dll 11148 行）判定任一 PartyMove* 按下 →
    ///   mainParty.ForceAiNoPathMode + 位移 = 部队移动
    /// - GameKey.IsDown（TaleWorlds.InputSystem.dll 423 行，internal 5 参）= 键盘/手柄键池统一判定
    ///   （KeyboardKey.IsDown() || ControllerKey.IsDown()）——**任何设备（含 native 手柄映射）都走这里**
    /// ⚠️ 弃用方案：① 补丁 MapScreen.get_IsInMenu → ExitMenuContext NRE 崩溃（实机即崩）；
    /// ② 改绑 KeyboardKey → 只拦键盘键池，native 手柄映射可绕过（实测十字键仍移动部队）。
    /// 处置：Prefix 拦 GameKey.IsDown——MapHotKeyCategory 的 50-53 且 IM 打开（完整+缩略）
    /// → 直接 false。零挂接（纯条件判定，IM 关 → 自然放行）。
    /// </summary>
    [HarmonyPatch(typeof(GameKey), "IsDown")]
    public static class ImChatMapInputPatch
    {
        private const string MapHotKeyCategoryId = "MapHotKeyCategory";
        private const int PartyMoveUpId = 50;
        private const int PartyMoveRightId = 53;

        [HarmonyPrefix]
        public static bool Prefix(GameKey __instance, ref bool __result)
        {
            // GroupId 精准门控：50-53 只在地图 context 是 PartyMove，防误伤其他 context 的同号键
            if (__instance.GroupId == MapHotKeyCategoryId
                && __instance.Id >= PartyMoveUpId && __instance.Id <= PartyMoveRightId
                && ImChatView.IsOpen)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
