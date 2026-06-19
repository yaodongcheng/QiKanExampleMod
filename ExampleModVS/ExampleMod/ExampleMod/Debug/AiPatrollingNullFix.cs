using HarmonyLib;
using System;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.Library;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Fix vanilla NullReferenceException in AiPatrollingBehavior.AiHourlyTick.
    ///
    /// Crash condition:
    ///   mobileParty.MapFaction.Leader.IsLord
    ///   — if MapFaction resolves to a Clan whose leader was killed (by world events,
    ///     assassination expiry, or any other cause), Leader is null and the .IsLord
    ///     call throws NRE.
    ///
    /// Fix: when MapFaction or MapFaction.Leader is null, skip the original method
    ///       (same outcome as the early-return path — no patrol scoring for this party).
    /// </summary>
    [HarmonyPatch(typeof(AiPatrollingBehavior), "AiHourlyTick")]
    public static class AiPatrollingNullFix
    {
        [HarmonyPrefix]
        public static bool Prefix(MobileParty mobileParty)
        {
            try
            {
                if (mobileParty?.MapFaction == null || mobileParty.MapFaction.Leader == null)
                {
                    return false; // skip original → equivalent to early-return (no patrol)
                }
                return true; // safe — let original method run
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[AiPatrollingNullFix] Prefix error: {ex.Message}");
                return true; // on error, let original run (original crash is logged anyway)
            }
        }
    }
}
