using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace LivingWorldNpcs.CampaignMode
{
    /// <summary>
    /// navmesh 可视化绘制循环 · Campaign 大地图侧（2026-09-10）。
    /// 绘制核心在 <see cref="NavMeshDebugRenderer"/>（与场景侧 NavMeshDebugMissionView 共用）。
    ///
    /// 为什么挂 ScreenBase.OnFrameTick：CampaignEvents.TickEvent 在暂停（时间流速 0）时停发，
    /// 而调试看地图恰恰常在暂停下进行；ScreenBase.OnFrameTick 是 UI 层每帧回调，暂停照常触发
    /// （同 ImScreenFrameTickPatch 的选型理由）。
    /// 🔴 Mission 内本补丁不触发是正常现象（MissionScreen override OnFrameTick 不走基类，
    /// 2026-08-23 教训）——场景侧由 NavMeshDebugMissionView 驱动，两套 tick 隔离。
    ///
    /// ⚠️ 待实测项：MBDebug.RenderDebug* 调试渲染在大地图上是否实际显示
    /// （引擎调试渲染经 MapScene 渲染循环，理论有效，未实机验证）。
    /// </summary>
    [HarmonyPatch(typeof(ScreenBase), "OnFrameTick")]
    public static class NavMeshDebugMapTickPatch
    {
        [HarmonyPostfix]
        public static void Postfix(float dt)
        {
            try
            {
                // 仅大地图：campaign 已加载且不在 mission（其余 Screen 直接跳过）
                if (!NavMeshDebugRenderer.IsCampaignMap)
                    return;
                if (!NavMeshDebugState.Enabled && !NavMeshDebugState.HasPathRequest && NavMeshDebugState.WatchedParty == null)
                    return;

                Scene scene = NavMeshDebugRenderer.GetActiveScene();
                if (scene == null)
                    return;

                NavMeshDebugRenderer.TickCampaignMap(scene);
            }
            catch (Exception ex)
            {
                NavMeshDebugState.Enabled = false;
                NavMeshDebugState.HasPathRequest = false;
                NavMeshDebugState.WatchedParty = null;
                DebugLogger.Log($"[NavDebug] 大地图绘制异常，已自动关闭: {ex}");
            }
        }
    }
}
