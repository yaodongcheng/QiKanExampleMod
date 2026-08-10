using HarmonyLib;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// IM 的 UI 层每帧钩子（🔴 修复「大地图暂停时按 O 打不开」）：
    /// CampaignEvents.TickEvent 在时间流速 0 时停发（Campaign 时间静止 = 事件停发），
    /// 而 ScreenBase.OnFrameTick 是引擎渲染循环的 UI 层回调（定居点菜单/地图拖动同层）——
    /// 暂停时照常每帧触发。IM 是 UI 操作，就该挂在 UI 层的循环上。
    /// Mission 内由 ImChatMissionView 驱动（本 patch 门控跳过，防双驱动）。
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
}
