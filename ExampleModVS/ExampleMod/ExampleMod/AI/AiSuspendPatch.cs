using HarmonyLib;
using SandBox;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Harmony 补丁：拦截 AgentNavigator.RefreshBehaviorGroups，
    /// 当 Agent 在 SuspendedAgentIndices 集合中时直接返回，
    /// 防止 Navigator 每 1 秒重新激活 DailyBehaviorGroup。
    ///
    /// 配合 AgentControlHelper.SuspendVanillaAI / ResumeVanillaAI 使用。
    /// </summary>
    [HarmonyPatch(typeof(AgentNavigator), "RefreshBehaviorGroups")]
    public static class AiSuspendPatch
    {
        public static bool Prefix(AgentNavigator __instance)
        {
            return !AgentControlHelper.SuspendedAgentIndices.Contains(__instance.OwnerAgent.Index);
        }
    }
}
