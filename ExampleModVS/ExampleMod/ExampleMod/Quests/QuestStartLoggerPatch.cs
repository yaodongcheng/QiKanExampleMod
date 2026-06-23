using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Harmony Postfix on QuestBase.StartQuest()。
    /// 记录所有 Quest 的激活（原版对话 / StoryDialog / 其他路径）。
    /// 搜 [QuestStart] 即可看到全部任务接取日志。
    /// </summary>
    [HarmonyPatch(typeof(QuestBase), nameof(QuestBase.StartQuest))]
    public static class QuestStartLoggerPatch
    {
        [HarmonyPostfix]
        public static void Postfix(QuestBase __instance)
        {
            string giver = __instance.QuestGiver?.Name?.ToString() ?? "?";
            string questType = __instance.GetType().Name;
            string title = __instance.Title?.ToString() ?? questType;
            DebugLogger.Log($"[QuestStart] {questType} \"{title}\" | giver={giver} | IsOngoing={__instance.IsOngoing} | RewardGold={__instance.RewardGold}");
        }
    }
}
