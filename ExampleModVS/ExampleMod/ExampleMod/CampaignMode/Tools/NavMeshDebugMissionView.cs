using System;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.CampaignMode
{
    /// <summary>
    /// navmesh 可视化绘制循环 · Mission（场景）侧（2026-09-10）。
    /// 绘制核心在 <see cref="NavMeshDebugRenderer"/>（与大地图侧 NavMeshDebugMapTickPatch 共用）。
    /// 开关与命令见 <see cref="NavMeshDebugCommands"/>。
    ///
    /// 挂载：MySubModule.OnMissionBehaviorInitialize，置于玩法闸门（IsInteractionDisabled）之前
    /// —— 战场 / 攻城场景调试也需要。无请求时每帧只做一次 bool 判断，零开销。
    ///
    /// ⚠️ 大地图（campaign）上本视图不 tick（无 mission）——由 NavMeshDebugMapTickPatch 接管。
    /// </summary>
    public class NavMeshDebugMissionView : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

        public override void OnMissionTick(float dt)
        {
            if (!NavMeshDebugState.Enabled && !NavMeshDebugState.HasPathRequest && NavMeshDebugState.WatchedAgentIndex < 0)
                return;

            Mission mission = Mission.Current;
            Scene scene = mission?.Scene;
            if (scene == null)
                return;

            try
            {
                Agent mainAgent = Agent.Main;
                Vec3 refPos = mainAgent != null ? mainAgent.Position : mission.GetCameraFrame().origin;

                if (NavMeshDebugState.Enabled)
                    NavMeshDebugRenderer.DrawFaceSpheres(scene, refPos);
                if (NavMeshDebugState.HasPathRequest)
                    NavMeshDebugRenderer.DrawPathRaw(scene, NavMeshDebugState.PathStart, NavMeshDebugState.PathEnd,
                        NavMeshDebugRenderer.ColPath, "nav_path");
                if (NavMeshDebugState.WatchedAgentIndex >= 0)
                    DrawWatchedAgent(mission, scene);
            }
            catch (Exception ex)
            {
                // 绘制异常一次即全部关闭（防每帧刷屏 + 防持续异常拖垮游戏）
                NavMeshDebugState.Enabled = false;
                NavMeshDebugState.HasPathRequest = false;
                NavMeshDebugState.WatchedAgentIndex = -1;
                DebugLogger.Log($"[NavDebug] 绘制异常，已自动关闭: {ex}");
            }
        }

        private void DrawWatchedAgent(Mission mission, Scene scene)
        {
            Agent watched = null;
            foreach (Agent agent in mission.Agents)
            {
                if (agent.IsActive() && agent.Index == NavMeshDebugState.WatchedAgentIndex)
                {
                    watched = agent;
                    break;
                }
            }
            if (watched == null)
            {
                // agent 死亡 / 离开场景 → 自动取消监视
                DebugLogger.Log($"[NavDebug] 被监视 agent #{NavMeshDebugState.WatchedAgentIndex} 已不存在，监视自动取消");
                NavMeshDebugState.WatchedAgentIndex = -1;
                return;
            }

            NavMeshDebugRenderer.DrawAgentWatch(scene, watched);
        }
    }
}
