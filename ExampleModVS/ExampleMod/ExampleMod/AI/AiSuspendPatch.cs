using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SandBox;
using SandBox.Missions.MissionLogics;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Harmony 补丁：拦截 AgentNavigator.RefreshBehaviorGroups，
    /// 当 Agent 在 SuspendedAgentIndices 集合中时直接返回，
    /// 防止 Navigator 每 1 秒重新激活 DailyBehaviorGroup。
    ///
    /// 配合 AgentBrain 内部的 SuspendVanillaAI / ResumeVanillaAI 使用。
    /// </summary>
    [HarmonyPatch(typeof(AgentNavigator), "RefreshBehaviorGroups")]
    public static class AiSuspendPatch
    {
        public static bool Prefix(AgentNavigator __instance)
        {
            return !AgentBrain.SuspendedAgentIndices.Contains(__instance.OwnerAgent.Index);
        }
    }

    /// <summary>
    /// 原版兴趣点张望也必须让出已接管的岗位NPC；它不属于DailyBehaviorGroup。
    /// 只在原版每人循环的资格检查处插入闸门，保留原列表、计时器和其余NPC的逻辑。
    /// 1.2.12／1.3.15／1.5.7实程序集均有唯一Agent.IsActive检查；结构变化时明确拒绝挂载。
    /// </summary>
    [HarmonyPatch(typeof(MissionAgentLookHandler), nameof(MissionAgentLookHandler.OnMissionTick))]
    public static class VanillaLookSuspendPatch
    {
        private static readonly HashSet<Agent> _blocked = new HashSet<Agent>();
        private static Mission _mission;
        private static bool _installed;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var active = AccessTools.Method(typeof(Agent), nameof(Agent.IsActive), Type.EmptyTypes);
            var gate = AccessTools.Method(typeof(VanillaLookSuspendPatch), nameof(IsActiveForVanillaLook));
            var targets = code.Where(instruction => instruction.Calls(active)).ToList();
            if (targets.Count != 1)
                throw new InvalidOperationException("Vanilla look gate expected one Agent.IsActive check, found " + targets.Count);
            // 原指令就地换等签名调用，保留分支标签和异常块；不修改全局Agent.IsActive。
            targets[0].opcode = OpCodes.Call;
            targets[0].operand = gate;
            _installed = true;
            DebugLogger.Log("[VanillaLook] gate installed: MissionAgentLookHandler.OnMissionTick; managed duty NPCs only");
            return code;
        }

        private static bool IsActiveForVanillaLook(Agent agent)
        {
            bool active = agent.IsActive(); // 保留原版失活检查；失活后只清托管引用。
            if (!active) { Forget(agent); return false; }
            try
            {
                if (!ReferenceEquals(_mission, Mission.Current))
                {
                    Reset();
                    _mission = Mission.Current;
                }
                var brain = agent == Agent.Main ? null : AgentAIController.GetBrainForAgent(agent);
                bool owned = brain != null && ReferenceEquals(brain.Owner, agent) && brain.HasDutyPoint
                    && !brain.IsInCombat && !Settings.Instance.IsInteractionDisabled();
                if (!owned)
                {
                    if (_blocked.Remove(agent)) DebugLogger.Log("[VanillaLook] released agent#" + agent.Index);
                    return true;
                }
                if (!_blocked.Contains(agent))
                {
                    // 跳过将来的写入还不够：原生层可能已保存前一帧的兴趣点。
                    // 只清兴趣点，不改SetLookAgent；对话／演出自己的听话者继续有效。
                    AgentControlHelper.StopLookingAtPointOfInterest(agent);
                    _blocked.Add(agent);
                    DebugLogger.Log("[VanillaLook] blocked agent#" + agent.Index + "; previous interest cleared");
                }
                return false;
            }
            catch (Exception ex)
            {
                Forget(agent);
                DebugLogger.Log("[VanillaLook] gate failed; original logic allowed: " + ex);
                return true;
            }
        }

        /// <summary>只读实际闸门记录，供单人探针验收；不写人物状态。</summary>
        public static string Describe(Agent agent)
            => !_installed ? "unpatched" : ReferenceEquals(_mission, Mission.Current) && agent != null && _blocked.Contains(agent) ? "blocked" : "unblocked";

        internal static void Forget(Agent agent) { if (agent != null) _blocked.Remove(agent); }
        internal static void Reset() { _blocked.Clear(); _mission = null; }
    }
}
