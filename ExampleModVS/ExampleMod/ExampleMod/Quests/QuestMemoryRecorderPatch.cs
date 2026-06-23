using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Harmony Postfix on IssueManager.StartIssueQuest。
    /// 接取 Quest 时写入 QuestRecord 到 NPC 的 QuestHistory。
    /// StoryDialog 路径也会由 CommissionIntent 显式调用 RecordQuestIssued。
    /// </summary>
    [HarmonyPatch(typeof(IssueManager), nameof(IssueManager.StartIssueQuest))]
    public static class QuestMemoryRecorderPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Hero issueOwner)
        {
            RecordQuestIssued(issueOwner);
        }

        /// <summary>
        /// 写入一条 "Issued" 类型的 QuestRecord。
        /// 公开方法供 CommissionIntent 在 StoryDialog 路径调用。
        /// </summary>
        public static void RecordQuestIssued(Hero hero)
        {
            if (hero == null) return;

            var issue = hero.Issue;
            if (issue == null) return;

            string questId = VanillaQuestMapping.GetIdForIssueTypeName(issue.GetType().Name);
            if (string.IsNullOrEmpty(questId)) return;

            var mem = AllNpcMemoryManager.GetMemory(hero.StringId);
            if (mem == null) return;

            string questName = issue.Title?.ToString() ?? issue.GetType().Name;
            string giverName = hero.Name?.ToString() ?? "";
            string settlement = hero.CurrentSettlement?.Name?.ToString() ?? "";

            var record = new QuestRecord
            {
                QuestId = questId,
                QuestName = questName,
                RecordType = "Issued",
                GiverName = giverName,
                SettlementName = settlement,
                Summary = $"{giverName} 委托玩家完成「{questName}」",
            };

            mem.AddQuestRecord(record);
        }
    }
}
