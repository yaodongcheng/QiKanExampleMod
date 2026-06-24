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

            // ① 检查结构化 Suppress 表（因果链 Suppress action 写入）
            if (IssueFilterBehavior.IsIssueSuppressed(hero, issueData.IssueType))
            {
                IssueFilterBehavior.RecordBlockedIssue(WorldEventType.BanditRaid, hero, issueData.IssueType);
                return false;
            }

            // ② 检查 CurrentUrgentEvent 阻拦（紧急事件期间阻止日常类 Issue）
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
