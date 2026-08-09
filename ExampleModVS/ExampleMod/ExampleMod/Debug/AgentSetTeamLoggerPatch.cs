using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Harmony Prefix on Agent.SetTeam(team, sync).
    /// 记录每一次 Agent 队伍变更（old → new），排查战斗 Team 模型/随从互砍/队伍还原类 bug 用。
    /// 搜 [TeamChange] 即可看到全部移队流水；调用点上下文见 [CombatManager] 的 SideFight/Spar/EndFight 日志。
    /// </summary>
    [HarmonyPatch(typeof(Agent), nameof(Agent.SetTeam))]
    public static class AgentSetTeamLoggerPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Agent __instance, Team team, bool sync)
        {
            try
            {
                if (__instance == null)
                    return;

                // 门禁：Mission 开始 2 秒后才记录（跳过出生初始化刷屏，基线见 [TeamBaseline]）
                if (!CombatManager.ShouldLogTeamChange())
                    return;

                int oldIndex = -1;
                if (__instance.Team != null)
                    oldIndex = __instance.Team.TeamIndex;

                DebugLogger.Log($"[TeamChange] {__instance.Name}(Idx={__instance.Index}): team {oldIndex} → {team?.TeamIndex ?? -1} (sync={sync})");
            }
            catch
            {
                // 日志系统绝不能影响游戏正常运行
            }
        }
    }
}
