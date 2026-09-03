using HarmonyLib;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// UI 层汇总计时（暂停/读档场景的归因关键——Mission 无、TickEvent 停发时，
    /// 唯一每帧在跑的托管链 = UI 层，本补丁量化「UI 层每帧花多少」）。
    /// 两处独立补丁类（同目标多 postfix 共存已验证安全；跨基类不合并，architecture 纪律）。
    /// Prefix/Postfix 用静态字段配对（主线程顺序执行，无嵌套并发）。
    /// </summary>
    [HarmonyPatch(typeof(ScreenBase), "OnFrameTick")]
    public static class PerfUiScreenTickPatch
    {
        private static long _t0;

        [HarmonyPrefix]
        public static void Prefix()
        {
            _t0 = PerfProfiler.Now();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            PerfProfiler.Accum(PerfSlot.UI_ScreenTick, _t0);
        }
    }

    /// <summary>Mission UI 层每帧汇总（MissionScreen.OnFrameTick；含 ESC 暂停期间——UI 层回调暂停也触发）。</summary>
    [HarmonyPatch(typeof(MissionScreen), "OnFrameTick")]
    public static class PerfUiMissionFrameTickPatch
    {
        private static long _t0;

        [HarmonyPrefix]
        public static void Prefix()
        {
            _t0 = PerfProfiler.Now();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            PerfProfiler.Accum(PerfSlot.UI_MissionUIFrame, _t0);
        }
    }
}
