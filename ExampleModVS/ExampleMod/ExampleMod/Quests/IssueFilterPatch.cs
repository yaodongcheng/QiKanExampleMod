using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Harmony prefix on IssueManager.AddPotentialIssueData.
    /// When an NPC has a CurrentUrgentEvent, blocks incompatible Issue types
    /// (e.g. daily-management Issues during a BanditRaid).
    /// </summary>
    [HarmonyPatch(typeof(IssueManager), nameof(IssueManager.AddPotentialIssueData))]
    public static class IssueFilterPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Hero hero, PotentialIssueData issueData)
        {
            if (hero == null) return true;

            var mem = AllNpcMemoryManager.GetMemory(hero.StringId);
            if (mem?.CurrentUrgentEvent == null) return true;

            WorldEventType eventType = mem.CurrentUrgentEvent.EventType;
            Type issueType = issueData.IssueType;

            if (IssueFilterBehavior.IsIssueTypeBlocked(eventType, issueType))
            {
                IssueFilterBehavior.RecordBlockedIssue(eventType, hero, issueType);
                return false;
            }

            IssueFilterBehavior.RecordPassedIssue(eventType, hero, issueType);
            return true;
        }
    }
}
