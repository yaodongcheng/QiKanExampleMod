using HarmonyLib;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Campaign 侧性能面板驱动（独立补丁类——仓库惯例按职责拆类；与 ImScreenFrameTickPatch
    /// 同目标多 postfix 共存（Bannerlord.Harmony 允许多 postfix，已实机验证）。
    /// 🔴 MissionScreen override OnFrameTick 不走基类 → 本补丁在 Mission 内不触发是正常现象
    /// （与 ImChat 两套 tick 隔离同款）；Mission 由 PerfMissionFrameTickPatch 承接。
    /// </summary>
    [HarmonyPatch(typeof(ScreenBase), "OnFrameTick")]
    public static class PerfScreenFrameTickPatch
    {
        [HarmonyPostfix]
        public static void Postfix(float dt)
        {
            PerfHudManager.Tick(dt);
        }
    }

    /// <summary>
    /// Mission 侧性能面板驱动（MissionScreen.OnFrameTick —— UI 层回调，ESC 暂停期间也触发；
    /// 与 ImMissionButtonRefreshPatch 同目标多 postfix 共存已实机验证）。
    /// 🔴 不挂 MissionView：统一宿主 PerfHudManager（层挂 TopScreen，ImChatOpenButton 范本，
    /// 2026-09-03 用户指引——MissionView 挂载时机坑已移除）。
    /// Scene 判定防重复驱动：MissionScreen 只在 Mission 内是 TopScreen（MapScreen 等屏走
    /// ScreenBase 不触发本补丁，两条驱动天然互斥）。
    /// </summary>
    [HarmonyPatch(typeof(MissionScreen), "OnFrameTick")]
    public static class PerfMissionFrameTickPatch
    {
        [HarmonyPostfix]
        public static void Postfix(float dt)
        {
            PerfHudManager.Tick(dt);
        }
    }
}
