using HarmonyLib;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// IM 的 UI 层每帧钩子（🔴 修复「大地图暂停时按 O 打不开」）：
    /// CampaignEvents.TickEvent 在时间流速 0 时停发（Campaign 时间静止 = 事件停发），
    /// 而 ScreenBase.OnFrameTick 是引擎渲染循环的 UI 层回调（定居点菜单/地图拖动同层）——
    /// 暂停时照常每帧触发。IM 是 UI 操作，就该挂在 UI 层的循环上。
    /// 🔴 Campaign 专用：Mission 由 MissionView 驱动（两套 tick 隔离，2026-08-23 教训——
    /// MissionScreen override OnFrameTick 不走基类，本补丁在 Mission 内不触发是正常现象，
    /// 不是 bug；Mission ESC 期间按钮刷新由 ImMissionButtonRefreshPatch 单独兜底）。
    /// </summary>
    [HarmonyPatch(typeof(ScreenBase), "OnFrameTick")]
    public static class ImScreenFrameTickPatch
    {
        [HarmonyPostfix]
        public static void Postfix(float dt)
        {
            ImChatView.OnScreenFrameTick(dt);
        }
    }

    /// <summary>
    /// 🔴 2026-08-23：呼出按钮的 Mission 侧「暂停兜底」驱动——只驱动 ImChatOpenButtonManager，
    /// 不经过 OnScreenFrameTick（Campaign 专用钩子，架构隔离）。
    /// 为什么需要：Mission ESC 打开时 MissionView.OnMissionTick（InteractionMissionView 驱动按钮）
    /// 可能因 PauseGameEngine 停摆 → 按钮隐藏判定（ShouldShow → IsEscapeMenuOpen）不刷新 →
    /// 按钮保持显示（层序 350 > ESC 层 50 穿透）。MissionScreen.OnFrameTick 是 UI 层回调
    ///（ESC 菜单要渲染交互，暂停也触发）→ 兜底刷新。
    /// 与 InteractionMissionView.OnMissionTick 双调幂等（Tick 状态比较 + _layer==null 保护）。
    /// </summary>
    [HarmonyPatch(typeof(MissionScreen), "OnFrameTick")]
    public static class ImMissionButtonRefreshPatch
    {
        [HarmonyPostfix]
        public static void Postfix(float dt)
        {
            if (Mission.Current != null)
                ImChatOpenButtonManager.Tick(dt);
        }
    }
}
