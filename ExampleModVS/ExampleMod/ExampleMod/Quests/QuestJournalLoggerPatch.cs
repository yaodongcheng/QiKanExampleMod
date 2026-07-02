using System.Linq;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem.ViewModelCollection.Quests;

namespace LivingWorldNpcs
{
    /// <summary>
    /// Harmony Postfix on QuestsVM.SetSelectedItem()。
    /// 当玩家打开任务面板或切换任务条目时，将玩家眼睛看到的任务信息
    /// 完整打印到 DebugLogger。方便排查"任务 journal 显示不对"的问题。
    /// 搜 [QuestJournal] 即可找到所有任务面板渲染日志。
    /// </summary>
    [HarmonyPatch(typeof(QuestsVM), "SetSelectedItem")]
    public static class QuestJournalLoggerPatch
    {
        /// <summary>
        /// 去重：QuestsVM 构造时会多次调用 SetSelectedItem（初始选中 + RefreshValues
        /// 绑定回调 + 排序控制器），同一个 quest 不变就不重复打印。
        /// </summary>
        private static QuestItemVM _lastLoggedQuest;

        [HarmonyPostfix]
        public static void Postfix(QuestsVM __instance, QuestItemVM quest)
        {
            try
            {
                if (quest == null) return;

                // 去重：同一个 QuestItemVM 实例不再重复打印
                if (quest == _lastLoggedQuest) return;
                _lastLoggedQuest = quest;

                var sb = new StringBuilder();
                sb.AppendLine();

                // ── 任务概览 ──
                string questName = quest.Name ?? "?";
                string questType = quest.Quest?.GetType().Name
                    ?? (quest.Issue != null ? "Issue: " + quest.Issue.GetType().Name
                    : (quest.QuestLogEntry != null ? "QuestLog: " + quest.QuestLogEntry.GetType().Name
                    : "Unknown"));
                string questGiver = quest.Quest?.QuestGiver?.Name?.ToString()
                    ?? quest.QuestGiverHero?.NameText
                    ?? "?";
                string isMainQuest = quest.IsMainQuest ? " [MainQuest]" : "";
                string isTracked = quest.IsTracked ? " [Tracked]" : "";
                string completionFlag = quest.IsCompleted
                    ? (quest.IsCompletedSuccessfully ? " [成功完成]" : " [失败]")
                    : "";

                sb.AppendLine($"══════════════════════════════════════");
                sb.AppendLine($"📋 [QuestJournal] 任务: \"{questName}\"{isMainQuest}{isTracked}{completionFlag}");
                sb.AppendLine($"   类型: {questType}");
                sb.AppendLine($"   委托人: {questGiver}");

                // 时间/状态信息
                if (quest.Quest != null)
                {
                    sb.AppendLine($"   剩余天数: {quest.RemainingDaysText ?? "无"}");
                    sb.AppendLine($"   IsOngoing={quest.Quest.IsOngoing} | IsTrackEnabled={quest.Quest.IsTrackEnabled}");
                }

                // ── 任务阶段 (Journal Stages) ──
                var stages = quest.Stages;
                if (stages != null && stages.Count > 0)
                {
                    sb.AppendLine($"   ── 阶段日志 ({stages.Count} 条) ──");
                    for (int i = 0; i < stages.Count; i++)
                    {
                        var stage = stages[i];
                        string desc = stage.DescriptionText ?? "(空)";
                        string date = stage.DateText ?? "";
                        string flags = "";
                        if (stage.IsNew) flags += "🆕";
                        if (stage.IsLastStage) flags += "🔚";
                        if (stage.IsTaskCompleted) flags += "✅";

                        sb.AppendLine($"   [{i}] {desc}");

                        if (!string.IsNullOrEmpty(date) || !string.IsNullOrEmpty(flags))
                        {
                            string meta = string.Join(" | ", new[] { date, flags }.Where(s => !string.IsNullOrEmpty(s)));
                            sb.AppendLine($"       日期: {meta}");
                        }

                        // 任务进度 (如 "护送商人到 XX 镇 (3/1)")
                        if (stage.HasATask && stage.StageTask != null && stage.StageTask.IsValid)
                        {
                            var task = stage.StageTask;
                            string progress = task.TargetProgress > 0
                                ? $" ({task.CurrentProgress}/{task.TargetProgress})"
                                : "";
                            sb.AppendLine($"       📌 子任务: {task.TaskName}{progress}");
                        }

                        // 底层 JournalLog 的原始 LogText（如果与 DescriptionText 不同）
                        try
                        {
                            var rawLog = stage.Log?.LogText?.ToString();
                            if (!string.IsNullOrEmpty(rawLog) && rawLog != desc)
                            {
                                sb.AppendLine($"       📝 [原始LogText]: {rawLog}");
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    sb.AppendLine($"   ⚠ 阶段日志: 无 (Stages.Count=0)");
                }

                sb.AppendLine($"══════════════════════════════════════");

                DebugLogger.Log(sb.ToString().TrimEnd());
            }
            catch
            {
                // 日志系统绝不能影响游戏正常运行
            }
        }
    }
}
